using Robust.Shared.Prototypes;

namespace Content.Shared._Wega.Arena.Cold;

/// <summary>
/// Put on a grid or map to make it "cold": mobs standing on it freeze over time unless they are near a
/// <see cref="HeatSourceComponent"/>. This is what turns the winter-arena grid into a Frostpunk-style
/// warm-center / cold-everywhere-else zone without touching atmospherics. On map-init it also starts
/// snowfall over the whole map, so one component declares both the cold and the weather.
/// </summary>
[RegisterComponent]
public sealed partial class ArenaColdZoneComponent : Component
{
    /// <summary>Cold gained per second while outside every heat radius (0..1 over the whole scale).</summary>
    [DataField]
    public float ColdPerSecond = 0.055f;

    /// <summary>Cold lost per second while within a heat radius.</summary>
    [DataField]
    public float WarmPerSecond = 0.3f;

    /// <summary>Weather started over the map on init. Null = don't touch the weather.</summary>
    [DataField]
    public EntProtoId? Weather = "WeatherSnowfallMedium";
}
