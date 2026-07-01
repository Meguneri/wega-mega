using Content.Shared._Wega.Clothing.Sandevistan;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Wega.Sandevistan;

/// <summary>
/// Fullscreen "world slowed down" effect shown to a mob caught in someone else's Sandevistan
/// bullet-time (i.e. while it has an active <see cref="SandevistanSlowedComponent"/>). Cold blue
/// desaturation + vignette. The effect intensity eases in/out so it doesn't pop when the slow's
/// 0.6s window refreshes or lapses.
/// </summary>
public sealed partial class SandevistanSlowedOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> ShaderProto = "SandevistanSlow";

    [Dependency] private IEntityManager _entity = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;

    /// <summary>Eased 0..1 strength; lerped toward the target each frame.</summary>
    private float _intensity;

    private const float FadeSpeed = 6f;

    public SandevistanSlowedOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(ShaderProto).InstanceUnique();
        ZIndex = 11; // over the greyscale/noir overlays
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var target = IsLocalPlayerSlowed() ? 1f : 0f;
        _intensity += (target - _intensity) * Math.Clamp(FadeSpeed * args.DeltaSeconds, 0f, 1f);
    }

    private bool IsLocalPlayerSlowed()
    {
        var player = _player.LocalEntity;
        if (player == null)
            return false;

        return _entity.TryGetComponent<SandevistanSlowedComponent>(player, out var slowed)
            && slowed.EndTime > _timing.CurTime;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entity.TryGetComponent(_player.LocalEntity, out EyeComponent? eye) || args.Viewport.Eye != eye.Eye)
            return false;

        return _intensity > 0.001f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("intensity", _intensity);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
