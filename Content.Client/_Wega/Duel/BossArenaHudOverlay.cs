using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Wega.Duel;

/// <summary>
/// Полоска ХП босса босс-арены сверху экрана: имя босса, красная полоса по доле здоровья,
/// подпись фазы; в энрейдже полоса пульсирует. Состояние читает из <see cref="BossArenaHudSystem"/>.
/// </summary>
public sealed partial class BossArenaHudOverlay : Overlay
{
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly BossArenaHudSystem _hud;
    private readonly Font _font;
    private readonly Font _fontSmall;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private static readonly Color FrameColor = Color.Black.WithAlpha(0.75f);
    private static readonly Color BackgroundColor = new(30, 30, 30, 220);
    private static readonly Color FillColor = new(200, 40, 40);
    private static readonly Color FillEnragedColor = new(235, 30, 30);

    public BossArenaHudOverlay(BossArenaHudSystem hud)
    {
        IoCManager.InjectDependencies(this);
        _hud = hud;
        _font = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 16);
        _fontSmall = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 12);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _hud.HudActive && base.BeforeDraw(args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var vpWidth = args.ViewportBounds.Width;

        var ratio = Math.Clamp(_hud.HealthRatio, 0f, 1f);
        var barWidth = Math.Min(500f, vpWidth * 0.45f);
        const float barHeight = 16f;
        var left = (vpWidth - barWidth) / 2f;
        const float top = 24f;

        // Рамка и фон.
        handle.DrawRect(new UIBox2(left - 2, top - 2, left + barWidth + 2, top + barHeight + 2), FrameColor);
        handle.DrawRect(new UIBox2(left, top, left + barWidth, top + barHeight), BackgroundColor);

        // Заполнение по доле здоровья; в ярости пульсирует.
        var fillColor = _hud.Enraged ? FillEnragedColor : FillColor;
        if (_hud.Enraged)
        {
            var pulse = 0.65f + 0.35f * MathF.Sin((float) _timing.CurTime.TotalSeconds * 6f);
            fillColor = fillColor.WithAlpha(pulse);
        }

        if (ratio > 0f)
            handle.DrawRect(new UIBox2(left, top, left + barWidth * ratio, top + barHeight), fillColor);

        // Имя босса над полоской, фаза/ярость — под ней.
        handle.DrawString(_font, new Vector2(left, top - 20), _hud.BossName, Color.White);

        var sub = _hud.Enraged
            ? Loc.GetString("boss-arena-hud-enraged")
            : Loc.GetString("boss-arena-hud-phase", ("phase", _hud.Phase + 1));
        handle.DrawString(_fontSmall, new Vector2(left, top + barHeight + 4), sub,
            _hud.Enraged ? Color.OrangeRed : Color.LightGray);
    }
}
