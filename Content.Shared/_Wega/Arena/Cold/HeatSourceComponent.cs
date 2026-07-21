namespace Content.Shared._Wega.Arena.Cold;

/// <summary>
/// Marks an entity (the arena Generator, steam hubs) as a heat source. Mobs within <see cref="Radius"/>
/// tiles on the same map are kept warm and their <see cref="ColdExposureComponent"/> cold recedes.
/// Read only on the server by <c>ArenaColdSystem</c>; lives in Shared so the client can load prototypes
/// that reference it.
/// </summary>
[RegisterComponent]
public sealed partial class HeatSourceComponent : Component
{
    /// <summary>Warmth radius in tiles.</summary>
    [DataField]
    public float Radius = 20f;
}
