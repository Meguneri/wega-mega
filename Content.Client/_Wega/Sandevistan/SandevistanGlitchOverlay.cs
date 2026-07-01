using Content.Shared._Wega.Clothing.Sandevistan;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Wega.Sandevistan;

/// <summary>
/// Cyberpunk "помехи" overlay shown to the Sandevistan WEARER while their own burst is active
/// (<see cref="SandevistanActiveComponent"/>): digital tear bands, channel corruption, chromatic
/// aberration and RGB sparkle (the <c>SandevistanGlitch</c> shader). The strength follows an envelope —
/// a hard spike the instant the burst kicks in (with a brief radial zoom-lurch via the shader's
/// <c>punch</c> uniform), settling to a low ambient hum for the rest of the burst, then easing out.
/// Distinct from the cool "charged" speed-vision overlay; purely cosmetic.
/// </summary>
public sealed partial class SandevistanGlitchOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> ShaderProto = "SandevistanGlitch";

    [Dependency] private IEntityManager _entity = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;

    /// <summary>Eased overall glitch strength (0..1) passed to the shader.</summary>
    private float _intensity;

    /// <summary>Activation zoom-lurch (0..1), decays fast after the burst starts.</summary>
    private float _punch;

    /// <summary>When the local player's current burst began (rising edge), for the activation spike.</summary>
    private TimeSpan? _activatedAt;
    private bool _wasActive;

    /// <summary>Low constant glitch hum kept up for the whole burst.</summary>
    private const float Ambient = 0.16f;
    private const float FadeSpeed = 8f;

    public SandevistanGlitchOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(ShaderProto).InstanceUnique();
        ZIndex = 12; // above the charged/slow overlays
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var active = IsLocalPlayerActive();
        var now = _timing.CurTime;

        // Rising edge: the local player's burst just started — kick the activation spike + zoom-lurch.
        if (active && !_wasActive)
            _activatedAt = now;
        _wasActive = active;

        float target;
        if (active)
        {
            var since = _activatedAt is { } a ? (float) (now - a).TotalSeconds : 999f;
            // Strong burst at activation, essentially gone by ~0.6s, leaving the ambient hum.
            var spike = MathF.Exp(-since * 6f);
            target = Math.Clamp(Ambient + spike, 0f, 1f);
            _punch = spike;
        }
        else
        {
            target = 0f;
            _punch = 0f;
        }

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

        return _intensity > 0.005f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("intensity", _intensity);
        _shader.SetParameter("punch", _punch);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
