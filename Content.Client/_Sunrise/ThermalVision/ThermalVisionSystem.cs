using Content.Shared._Sunrise.ThermalVision;
using Content.Shared.Eye.Blinding.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._Sunrise.ThermalVision;

public sealed partial class ThermalVisionSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private TransformSystem _xformSys = default!;
    private ThroughWallsVisionOverlay _throughWallsOverlay = default!;
    private ThermalVisionOverlay _overlay = default!;

    private EntityUid? _effect = null;
    private readonly EntProtoId _effectPrototype = "EffectThermalVision";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SunriseThermalVisionComponent, ComponentInit>(OnVisionInit);
        SubscribeLocalEvent<SunriseThermalVisionComponent, ComponentShutdown>(OnVisionShutdown);

        SubscribeLocalEvent<SunriseThermalVisionComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<SunriseThermalVisionComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);

        _throughWallsOverlay = new();
        _overlay = new();
    }

    private void OnPlayerAttached(Entity<SunriseThermalVisionComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        if (_effect == null)
            AddThermalVision(ent.Owner);
        else if (HasComp<EyeProtectionComponent>(ent.Owner))
            RemoveThermalVision();
    }

    private void OnPlayerDetached(Entity<SunriseThermalVisionComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        RemoveThermalVision();
    }

    private void OnVisionInit(Entity<SunriseThermalVisionComponent> ent, ref ComponentInit args)
    {
        if (_player.LocalEntity != ent.Owner)
            return;

        if (_effect == null)
            AddThermalVision(ent.Owner);
        else if (HasComp<EyeProtectionComponent>(ent.Owner))
            RemoveThermalVision();
    }

    private void OnVisionShutdown(Entity<SunriseThermalVisionComponent> ent, ref ComponentShutdown args)
    {
        if (_player.LocalEntity != ent.Owner)
            return;

        RemoveThermalVision();
    }

    private void AddThermalVision(EntityUid uid)
    {
        if (HasComp<EyeProtectionComponent>(uid))
            return;

        _overlayMan.AddOverlay(_throughWallsOverlay);
        _overlayMan.AddOverlay(_overlay);
        _effect = SpawnAttachedTo(_effectPrototype, Transform(uid).Coordinates);
        _xformSys.SetParent(_effect.Value, uid);
    }

    private void RemoveThermalVision()
    {
        _overlayMan.RemoveOverlay(_throughWallsOverlay);
        _overlayMan.RemoveOverlay(_overlay);
        Del(_effect);
        _effect = null;
    }
}
