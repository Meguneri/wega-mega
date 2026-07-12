using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Server._Wega.Raid.Components;

/// <summary>
/// Маркер точки спавна на персональной карте-базе игрока (<see cref="RaidHideoutSpawnType"/>).
/// </summary>
[RegisterComponent]
public sealed partial class RaidHideoutSpawnComponent : Component
{
    [DataField("spawnType"), ViewVariables]
    public RaidHideoutSpawnType SpawnType = RaidHideoutSpawnType.Player;
}

public enum RaidHideoutSpawnType
{
    Player,
    Stash,
}
