using System.Numerics;
using Content.Shared._Wega.Duel;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Wega.Duel;

/// <summary>
/// Рисует «шторм» на дуэльной арене: опасная зона (всё за пределами безопасного круга — центр в
/// позиции трекера с <see cref="ArenaStormComponent"/>) затягивается полупрозрачным красным полотном.
/// Радиус берётся из <see cref="ArenaStormComponent.RadiusAt"/> — непрерывная интерполяция по времени,
/// поэтому зона сжимается ПЛАВНО (без рывков от сетевых шагов) и совпадает с серверной границей урона.
/// У самой границы — мягкое краевое свечение (несколько тонких колец, гаснущих наружу) и чёткая
/// пульсирующая линия-маркер; внутри — едва заметный контур-подсказка «куда отступать». Безопасная
/// зона не заливается. Самогейтится по активным штормам на карте глаза игрока.
/// </summary>
public sealed partial class ArenaStormOverlay : Overlay
{
    [Dependency] private IEntityManager _entity = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public ArenaStormOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entity.System<SharedTransformSystem>();
        ZIndex = 10;
    }

    /// <summary>Сегментов по окружности для заливки опасного кольца.</summary>
    private const int Segments = 96;

    /// <summary>Ширина краевого свечения у границы (в метрах).</summary>
    private const float EdgeWidth = 1.6f;

    /// <summary>Сколько тонких колец составляют краевое свечение.</summary>
    private const int EdgeBands = 3;

    /// <summary>Переиспользуемый буфер вершин triangle-strip (stackalloc запрещён песочницей).</summary>
    private readonly Vector2[] _ringVerts = new Vector2[(Segments + 1) * 2];

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var now = _timing.CurTime;

        // Пульсация границы: 0..1, период ~1.4с (плавное «дыхание» края). RealTime — для покадровой
        // гладкости анимации, не привязанной к тикам.
        var rt = (float)_timing.RealTime.TotalSeconds;
        var pulse = 0.5f + 0.5f * MathF.Sin(rt * MathF.Tau / 1.4f);

        var query = _entity.EntityQueryEnumerator<ArenaStormComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var storm, out var xform))
        {
            if (!storm.Active || xform.MapID != args.MapId)
                continue;

            // Радиус считается из времени — клиент сам плавно сжимает зону между сетевыми обновлениями.
            var inner = storm.RadiusAt(now);
            if (inner <= 0f)
                continue;

            var center = _transform.GetWorldPosition(uid);

            // Внешний радиус полотна — гарантированно за пределами видимой области, чтобы залить всё
            // опасное пространство до краёв экрана (дистанция до дальнего угла + запас).
            var bounds = args.WorldBounds;
            var outer = MaxDistance(center, bounds) + 4f;
            if (outer <= inner)
                continue;

            // 1) Базовая заливка опасной зоны — слабая, чтобы не мешать обзору (полупрозрачная).
            DrawDangerRing(handle, center, inner + EdgeWidth, outer, new Color(0.80f, 0.09f, 0.07f, 0.16f));

            // 2) Краевое свечение: тонкие кольца у самой границы, ярче у края и гаснут наружу —
            //    делает границу читаемой и аккуратной, но локально (узкая полоса, не залив весь экран).
            for (var i = 0; i < EdgeBands; i++)
            {
                var r0 = inner + EdgeWidth * (i / (float)EdgeBands);
                var r1 = inner + EdgeWidth * ((i + 1) / (float)EdgeBands);
                var a = 0.32f * (1f - i / (float)EdgeBands);
                DrawDangerRing(handle, center, r0, r1, new Color(0.96f, 0.20f, 0.13f, a));
            }

            // 3) Чёткая пульсирующая линия точно по границе урона.
            var line = new Color(1f, 0.34f, 0.20f, 0.5f + 0.4f * pulse);
            handle.DrawCircle(center, inner, line, false);

            // 4) Едва заметный контур-подсказка чуть внутри зоны — «куда отступать», без заливки.
            if (inner > 1.5f)
            {
                var guide = new Color(1f, 0.5f, 0.32f, 0.08f + 0.10f * pulse);
                handle.DrawCircle(center, inner - 1.1f, guide, false);
            }
        }
    }

    /// <summary>
    /// Заливает кольцо [inner; outer] вокруг center сплошным цветом через triangle-strip — так
    /// безопасный круг остаётся «дырой» без заливки, а всё снаружи затягивается полотном.
    /// </summary>
    private void DrawDangerRing(DrawingHandleWorld handle, Vector2 center, float inner, float outer, Color color)
    {
        // 2 вершины на сегмент (внутренняя/внешняя) + замыкающая пара.
        for (var i = 0; i <= Segments; i++)
        {
            var ang = i / (float)Segments * MathF.Tau;
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            _ringVerts[i * 2] = center + dir * inner;
            _ringVerts[i * 2 + 1] = center + dir * outer;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _ringVerts, color);
    }

    /// <summary>Максимальная дистанция от точки до углов прямоугольника обзора.</summary>
    private static float MaxDistance(Vector2 center, Box2Rotated bounds)
    {
        var max = 0f;
        max = MathF.Max(max, (bounds.BottomLeft - center).Length());
        max = MathF.Max(max, (bounds.BottomRight - center).Length());
        max = MathF.Max(max, (bounds.TopLeft - center).Length());
        max = MathF.Max(max, (bounds.TopRight - center).Length());
        return max;
    }
}
