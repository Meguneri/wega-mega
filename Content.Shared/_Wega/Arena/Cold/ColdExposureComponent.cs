using Robust.Shared.GameStates;

namespace Content.Shared._Wega.Arena.Cold;

/// <summary>
/// Per-mob cold buildup while in an <see cref="ArenaColdZoneComponent"/>. <see cref="Level"/> is networked so
/// the client frost overlay and the (server-driven) movement slow both read the same value. Added/removed by
/// the server as mobs enter/leave the cold zone.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ColdExposureComponent : Component
{
    /// <summary>Current cold: 0 = warm, 1 = fully frozen.</summary>
    [DataField, AutoNetworkedField]
    public float Level;

    /// <summary>Below this level nothing happens (small warm margin so brief trips out don't sting).</summary>
    [DataField]
    public float EffectThreshold = 0.15f;

    /// <summary>Movement multiplier at full cold — gradual, deliberately not crippling.</summary>
    [DataField]
    public float MinSpeedMultiplier = 0.65f;

    /// <summary>Wearing cold-insulating gear (a winter coat) — softer slow and halved cold damage. Server-set.</summary>
    [DataField, AutoNetworkedField]
    public bool Protected;

    /// <summary>Movement multiplier at full cold while protected — the slow is weaker but not gone.</summary>
    [DataField]
    public float ProtectedMinSpeedMultiplier = 0.85f;

    /// <summary>At or above this level the cold deals a small periodic damage tick.</summary>
    [DataField]
    public float DamageThreshold = 0.75f;
}
