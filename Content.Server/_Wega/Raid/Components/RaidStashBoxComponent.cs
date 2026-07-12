using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Server._Wega.Raid.Components;

/// <summary>
/// Marks an entity as a persistent raid stash container for a specific player.
/// </summary>
[RegisterComponent]
public sealed partial class RaidStashBoxComponent : Component
{
    /// <summary>
    /// Owner of this stash box. Not a DataField because NetUserId does not support prototype serialization.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public NetUserId OwnerId;
}
