// Adapted from ss14Starlight/space-station-14, fff9583f; MIT. See Resources/Prototypes/_Wega/StarlightLighting.
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Light;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Wires;

namespace Content.Shared._Wega.StarlightLighting;

public sealed partial class WegaStarlightLightFloorSystem : EntitySystem
{
    [Dependency] private SharedWiresSystem _wires = default!;
    [Dependency] private DamageOnInteractSystem _damage = default!;

    [SubscribeLocalEvent]
    private void OnLightLevel(Entity<WegaStarlightLightFloorComponent> ent,
        ref WegaStarlightLightFixtureLevelEvent args)
    {
        args.LightEnergy *= ent.Comp.EnergyMultiplier;
        args.LightRadius = Math.Min(args.LightRadius, ent.Comp.MaxRadius);
    }

    [SubscribeLocalEvent]
    private void OnPanelChanged(Entity<WegaStarlightLightFloorComponent> ent, ref PanelChangedEvent args)
    {
        if (args.Open && TryComp<PoweredLightComponent>(ent, out var light))
            SetBurn(ent, light.CurrentLit);
    }

    private void SetBurn(EntityUid uid, bool active)
    {
        if (TryComp<DamageOnInteractComponent>(uid, out var burn))
            _damage.SetIsDamageActiveTo((uid, burn), active);
    }

    [SubscribeLocalEvent(before: [typeof(SharedPoweredLightSystem), typeof(DamageOnInteractSystem)])]
    private void OnInteractHand(Entity<WegaStarlightLightFloorComponent> ent, ref InteractHandEvent args)
    {
        if (_wires.IsPanelOpen(ent.Owner))
            return;
        SetBurn(ent, false);
        args.Handled = true;
    }

    [SubscribeLocalEvent(before: [typeof(SharedPoweredLightSystem)])]
    private void OnEject(EntityUid uid, WegaStarlightLightFloorComponent comp, PoweredLightDoAfterEvent args)
    {
        if (!args.Cancelled && !_wires.IsPanelOpen(uid))
            args.Handled = true;
    }

    [SubscribeLocalEvent(before: [typeof(SharedPoweredLightSystem)])]
    private void OnInteractUsing(Entity<WegaStarlightLightFloorComponent> ent, ref InteractUsingEvent args)
    {
        if (!_wires.IsPanelOpen(ent.Owner) &&
            (HasComp<LightBulbComponent>(args.Used) || HasComp<LightReplacerComponent>(args.Used)))
            args.Handled = true;
    }
}
