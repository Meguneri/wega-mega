using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.Spawners.Components;

/// <summary>
/// Spawns entities at a set interval.
/// Can configure the set of entities, spawn timing, spawn chance,
/// and min/max number of entities to spawn.
/// </summary>
[RegisterComponent, EntityCategory("Spawner")]
[AutoGenerateComponentPause]
public sealed partial class TimedSpawnerComponent : Component, ISerializationHooks
{
    /// <summary>
    /// List of entities that can be spawned by this component. One will be randomly
    /// chosen for each entity spawned. When multiple entities are spawned at once,
    /// each will be randomly chosen separately.
    /// </summary>
    [DataField]
    public List<EntProtoId> Prototypes = [];

    /// <summary>
    /// Chance of an entity being spawned at the end of each interval.
    /// </summary>
    [DataField]
    public float Chance = 1.0f;

    /// <summary>
    /// Length of the interval between spawn attempts.
    /// </summary>
    [DataField]
    public TimeSpan IntervalSeconds = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The minimum number of entities that can be spawned when an interval elapses.
    /// </summary>
    [DataField]
    public int MinimumEntitiesSpawned = 1;

    /// <summary>
    /// The maximum number of entities that can be spawned when an interval elapses.
    /// </summary>
    [DataField]
    public int MaximumEntitiesSpawned = 1;

    /// <summary>
    /// The time at which the current interval will have elapsed and entities may be spawned.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextFire = TimeSpan.Zero;

    /// <summary>
    /// Whether this spawner is currently active. When false, no entities will be spawned.
    /// Can be toggled via signals (SpawnerSignalControl component).
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// If greater than zero, spawned entities are placed at a random position within this
    /// radius (in tiles) of <see cref="SpawnNearEntity"/> (when set) or of the spawner itself.
    /// </summary>
    [DataField]
    public float RandomSpawnRadius = 0f;

    /// <summary>
    /// Runtime-only reference: the entity whose position is used as the center for
    /// random-radius spawning. Set by <c>SpawnerSignalControlSystem</c> when a signal
    /// arrives from a linked button/switch. Not serialized.
    /// </summary>
    public EntityUid? SpawnNearEntity;

    void ISerializationHooks.AfterDeserialization()
    {
        if (MinimumEntitiesSpawned > MaximumEntitiesSpawned)
            throw new ArgumentException("MaximumEntitiesSpawned can't be lower than MinimumEntitiesSpawned!");
    }
}
