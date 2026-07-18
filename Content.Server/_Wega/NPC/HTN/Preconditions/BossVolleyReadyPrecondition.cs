using Content.Server._Wega.Duel.Components;
using Robust.Shared.Timing;

namespace Content.Server.NPC.HTN.Preconditions;

/// <summary>
/// Истинно, когда боссу босс-арены разрешена новая очередь огнестрела: идёт незаконченная очередь
/// (<see cref="BossArenaVolleyComponent.ShotsRemaining"/> &gt; 0) или кулдаун между очередями истёк.
/// Ставится на ветку дальнего боя в <c>BossArenaCompound</c>, чтобы босс чередовал очереди из
/// пулемёта с ближним боем, а не поливал огнём постоянно.
/// </summary>
public sealed partial class BossVolleyReadyPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager))
            return false;

        // Компонента нет — очереди не ограничиваем (ветка работает как обычно).
        if (!_entManager.TryGetComponent<BossArenaVolleyComponent>(owner, out var volley))
            return true;

        if (volley.ShotsRemaining > 0)
            return true;

        return volley.NextVolleyAt == null || _timing.CurTime >= volley.NextVolleyAt.Value;
    }
}
