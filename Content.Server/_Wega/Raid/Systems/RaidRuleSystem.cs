using Content.Server._Wega.Raid.Components;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Robust.Shared.GameObjects;

namespace Content.Server._Wega.Raid.Systems;

/// <summary>
/// Game rule for the raid extraction mode. Activating the rule spawns a <see cref="RaidControllerComponent"/>
/// if one does not already exist, so the raid only runs when the game mode is explicitly selected.
/// </summary>
public sealed partial class RaidRuleSystem : GameRuleSystem<RaidRuleComponent>
{
    protected override void Started(EntityUid uid, RaidRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        // Only spawn a controller if the map does not already contain one (mappers may place it manually).
        var query = EntityQueryEnumerator<RaidControllerComponent>();
        if (query.MoveNext(out _, out _))
            return;

        Spawn("RaidController");
    }
}
