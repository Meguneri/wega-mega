using System.Numerics;
using Content.Shared._Wega.Duel;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Wega.Duel;

/// <summary>
/// Рисует трос арена-гарпуна: для каждой сущности с <see cref="ArenaHarpoonRopeComponent"/> тянет
/// энергетическую алую плеть от её модельки к якорю (стрелку или точке зацепа). Трос строится каждый
/// кадр по живым мировым позициям обоих концов, поэтому он жёстко «приклеен» к модельке и плавно,
/// без рывков и исчезновения кусками, укорачивается по мере сближения. Лёгкая бегущая «дрожь» по
/// длине + свечение делают его похожим на натянутый под напряжением кабель.
/// </summary>
public sealed partial class ArenaHarpoonRopeOverlay : Overlay
{
    [Dependency] private IEntityManager _entity = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    /// <summary>Точек ломаной на тайл длины (плюс минимум), чтобы «дрожь» была гладкой.</summary>
    private const float PointsPerTile = 3f;
    private const int MinPoints = 6;
    private const int MaxPoints = 64;

    /// <summary>Переиспользуемый буфер вершин ломаной (без stackalloc — песочница).</summary>
    private readonly Vector2[] _points = new Vector2[MaxPoints];

    public ArenaHarpoonRopeOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entity.System<SharedTransformSystem>();
        ZIndex = 11;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var t = (float)_timing.RealTime.TotalSeconds;

        var query = _entity.EntityQueryEnumerator<ArenaHarpoonRopeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var rope, out var xform))
        {
            if (xform.MapID != args.MapId
                || rope.Anchor is not { } netAnchor
                || !_entity.TryGetEntity(netAnchor, out var anchor)
                || !_entity.TryGetComponent<TransformComponent>(anchor, out var anchorXform)
                || anchorXform.MapID != args.MapId)
                continue;

            var start = _transform.GetWorldPosition(uid);
            var end = _transform.GetWorldPosition(anchor.Value);

            DrawRope(handle, start, end, t);
        }
    }

    /// <summary>Строит ломаную с бегущей синусоидальной дрожью (затухающей к обоим концам, чтобы трос
    /// был приколочен к ним) и рисует её свечением + ярким ядром.</summary>
    private void DrawRope(DrawingHandleWorld handle, Vector2 start, Vector2 end, float t)
    {
        var delta = end - start;
        var dist = delta.Length();
        if (dist <= 0.01f)
            return;

        var dir = delta / dist;
        var perp = new Vector2(-dir.Y, dir.X);

        var count = Math.Clamp((int)(dist * PointsPerTile) + 1, MinPoints, MaxPoints);

        // Амплитуда дрожи небольшая и спадает на коротком тросе — вблизи цели трос «натягивается».
        var amp = MathF.Min(0.18f, dist * 0.05f);

        for (var i = 0; i < count; i++)
        {
            var f = i / (float)(count - 1);
            // Затухание к концам: sin(pi*f) — 0 на концах, 1 в середине.
            var envelope = MathF.Sin(f * MathF.PI);
            // Две бегущие волны разной частоты для «живого» энергокабеля.
            var wave = MathF.Sin(f * 18f - t * 14f) + 0.5f * MathF.Sin(f * 7f + t * 9f);
            var offset = perp * (wave * envelope * amp);
            _points[i] = Vector2.Lerp(start, end, f) + offset;
        }

        // Пульсация яркости.
        var pulse = 0.7f + 0.3f * MathF.Sin(t * 12f);

        // Свечение: широкая полупрозрачная «аура» из нескольких смещённых проходов.
        var glow = new Color(1f, 0.15f, 0.1f, 0.18f);
        DrawPolyline(handle, count, perp * 0.06f, glow);
        DrawPolyline(handle, count, perp * -0.06f, glow);
        DrawPolyline(handle, count, perp * 0.12f, glow * new Color(1f, 1f, 1f, 0.5f));
        DrawPolyline(handle, count, perp * -0.12f, glow * new Color(1f, 1f, 1f, 0.5f));

        // Ядро троса.
        DrawPolyline(handle, count, Vector2.Zero, new Color(1f, 0.55f * pulse, 0.4f * pulse, 0.95f));
    }

    /// <summary>Рисует текущую ломаную из <see cref="_points"/>, сдвинутую на <paramref name="shift"/>.</summary>
    private void DrawPolyline(DrawingHandleWorld handle, int count, Vector2 shift, Color color)
    {
        for (var i = 0; i < count - 1; i++)
            handle.DrawLine(_points[i] + shift, _points[i + 1] + shift, color);
    }
}
