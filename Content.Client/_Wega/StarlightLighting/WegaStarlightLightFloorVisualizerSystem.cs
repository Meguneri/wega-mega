// Adapted from ss14Starlight/space-station-14, fff9583f; MIT. See Resources/Prototypes/_Wega/StarlightLighting.
using Content.Client.Interactable.Components;
using Content.Shared._Wega.StarlightLighting;
using Content.Shared.Light;
using Content.Shared.Wires;
using Robust.Client.GameObjects;

namespace Content.Client._Wega.StarlightLighting;

public sealed partial class WegaStarlightLightFloorVisualizerSystem : VisualizerSystem<WegaStarlightLightFloorComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, WegaStarlightLightFloorComponent component,
        ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;
        AppearanceSystem.TryGetData<bool>(uid, WiresVisuals.MaintenancePanelState, out var open, args.Component);
        var lit = !AppearanceSystem.TryGetData<PoweredLightState>(uid, PoweredLightVisuals.BulbState,
            out var state, args.Component) || state == PoweredLightState.On;
        SpriteSystem.LayerSetVisible((uid, args.Sprite), WegaStarlightLightFloorLayers.Cover, !open);
        SpriteSystem.LayerSetVisible((uid, args.Sprite), WegaStarlightLightFloorLayers.CoverGlow, !open && lit);
        if (TryComp<PointLightComponent>(uid, out var light))
            SpriteSystem.LayerSetColor((uid, args.Sprite), WegaStarlightLightFloorLayers.CoverGlow, light.Color);
        if (open)
            EnsureComp<InteractionOutlineComponent>(uid);
        else
            RemComp<InteractionOutlineComponent>(uid);
    }
}
