using Content.Server._Wega.Raid.Systems;
using Robust.Shared.GameObjects;

namespace Content.Server._Wega.Raid.Components;

/// <summary>
/// Game-rule marker for the raid extraction mode. When active, players spawn on their personal hideout
/// and can enter the raid through the entry button.
/// </summary>
[RegisterComponent, Access(typeof(RaidRuleSystem))]
public sealed partial class RaidRuleComponent : Component
{
}
