using System.Numerics;
using Content.Shared._Wega.Arena.Cold;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Wega.Arena.Cold;

/// <summary>
/// Full-screen frost that creeps in as the local player freezes in the arena cold and fades back as they
/// warm up. Intensity is eased toward the networked <see cref="ColdExposureComponent.Level"/> so it never
/// pops. Pure texture draw, no shader.
/// </summary>
public sealed partial class ColdExposureOverlay : Overlay
{
    [Dependency] private IEntityManager _entity = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _cache = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly Texture _frost;

    /// <summary>Eased 0..1 shown strength; lerped toward the target each frame.</summary>
    private float _intensity;

    private const float FadeSpeed = 3.5f;
    private const float MaxAlpha = 0.85f;

    public ColdExposureOverlay()
    {
        IoCManager.InjectDependencies(this);
        _frost = _cache.GetResource<TextureResource>("/Textures/_Wega/Effects/frost_overlay.png");
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var target = 0f;
        if (_entity.TryGetComponent<ColdExposureComponent>(_player.LocalEntity, out var cold)
            && cold.Level > cold.EffectThreshold)
        {
            var t = (cold.Level - cold.EffectThreshold) / (1f - cold.EffectThreshold);
            target = Math.Clamp(t, 0f, 1f) * MaxAlpha;
        }

        _intensity += (target - _intensity) * Math.Clamp(FadeSpeed * args.DeltaSeconds, 0f, 1f);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entity.TryGetComponent(_player.LocalEntity, out EyeComponent? eye) || args.Viewport.Eye != eye.Eye)
            return false;

        return _intensity > 0.001f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var bounds = args.ViewportBounds;
        var rect = new UIBox2(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        args.ScreenHandle.DrawTextureRect(_frost, rect, Color.White.WithAlpha(_intensity));
    }
}
