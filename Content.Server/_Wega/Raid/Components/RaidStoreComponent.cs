using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Server._Wega.Raid.Components;

/// <summary>
/// Marks a store terminal as using persistent raid stash currency instead of physical currency items.
/// </summary>
[RegisterComponent]
public sealed partial class RaidStoreComponent : Component
{
    /// <summary>
    /// Last player who initiated a purchase. Used to sync the terminal balance back to their stash
    /// after the store system finishes processing the buy request.
    /// </summary>
    [ViewVariables]
    public NetUserId? BuyerUserId;
}
