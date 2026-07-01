using Content.Shared._Wega.Clothing.Sandevistan;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Wega.Sandevistan;

/// <summary>
/// Fullscreen "you're charged" effect shown to the Sandevistan WEARER while their own burst is
/// active (<see cref="SandevistanActiveComponent"/>): a pulsing cyan edge glow and slight contrast
/// punch. Deliberately distinct from the victims' cold "slowed" overlay. Intensity eases in/out.
/// </summary>
public sealed partial class SandevistanActiveOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> ShaderProto = "SandevistanActive";

    [Dependency] private IEntityManager _entity = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;

    private float _intensity;

    private const float FadeSpeed = 6f;

    public SandevistanActiveOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(ShaderProto).InstanceUnique();
        ZIndex = 11;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var target = IsLocalPlayerActive() ? 1f : 0f;
        _intensity += (target - _intensity) * Math.Clamp(FadeSpeed * args.DeltaSeconds, 0f, 1f);
    }

    private bool IsLocalPlayerActive()
    {
        var player = _player.LocalEntity;
        if (player == null)
            return false;

        return _entity.TryGetComponent<SandevistanActiveComponent>(player, out var active)
            && active.EndTime > _timing.CurTime;
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
