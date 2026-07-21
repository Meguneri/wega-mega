using Content.Shared._Wega.Arena.Cold;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._Wega.Arena.Cold;

/// <summary>
/// Shows/hides <see cref="ColdExposureOverlay"/> while the local player carries a
/// <see cref="ColdExposureComponent"/> (added/removed by the server as they enter/leave the arena cold).
/// </summary>
public sealed class ColdExposureSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IOverlayManager _overlay = default!;

    private ColdExposureOverlay _overlayInstance = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlayInstance = new ColdExposureOverlay();

        SubscribeLocalEvent<ColdExposureComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ColdExposureComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ColdExposureComponent, LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<ColdExposureComponent, LocalPlayerDetachedEvent>(OnDetached);
    }

    private void OnInit(EntityUid uid, ColdExposureComponent component, ComponentInit args)
    {
        if (_player.LocalEntity == uid)
            _overlay.AddOverlay(_overlayInstance);
    }

    private void OnShutdown(EntityUid uid, ColdExposureComponent component, ComponentShutdown args)
    {
        if (_player.LocalEntity == uid)
            _overlay.RemoveOverlay(_overlayInstance);
    }

    private void OnAttached(EntityUid uid, ColdExposureComponent component, LocalPlayerAttachedEvent args)
    {
        _overlay.AddOverlay(_overlayInstance);
    }

    private void OnDetached(EntityUid uid, ColdExposureComponent component, LocalPlayerDetachedEvent args)
    {
        _overlay.RemoveOverlay(_overlayInstance);
    }
}
