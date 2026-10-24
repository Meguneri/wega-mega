// Adapted from ss14Starlight/space-station-14, fff9583f; MIT. See Resources/Prototypes/_Wega/StarlightLighting.
using Robust.Shared.Serialization;

namespace Content.Shared._Wega.StarlightLighting;

/// <summary>A light floor with a removable cover and reduced bulb output.</summary>
[RegisterComponent]
public sealed partial class WegaStarlightLightFloorComponent : Component
{
    [DataField] public float EnergyMultiplier = 0.6f;
    [DataField] public float MaxRadius = 4f;
}

/// <summary>Allows fixtures to adjust the output of an inserted bulb.</summary>
[ByRefEvent]
public record struct WegaStarlightLightFixtureLevelEvent(float LightEnergy, float LightRadius);

[Serializable, NetSerializable]
public enum WegaStarlightLightFloorLayers : byte
{
    Cover,
    CoverGlow
}
