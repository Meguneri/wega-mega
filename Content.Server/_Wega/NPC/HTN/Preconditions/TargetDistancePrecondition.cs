using Robust.Shared.Map;

namespace Content.Server.NPC.HTN.Preconditions;

/// <summary>
/// Истинно, когда цель из блэкборда находится не дальше <see cref="MaxDistance"/> тайлов от NPC.
/// Нужно для ветвления «ближний бой вблизи — огнестрел вдали» в HTN босса босс-арены
/// (<c>BossArenaCompound</c>). В отличие от <c>TargetInRangePrecondition</c>, дистанция задаётся
/// константой в прототипе, а не ключом блэкборда.
/// </summary>
public sealed partial class TargetDistancePrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    private SharedTransformSystem _transformSystem = default!;

    /// <summary>Ключ блэкборда с целью (обычно Target).</summary>
    [DataField(required: true)]
    public string TargetKey = default!;

    /// <summary>Максимальная дистанция до цели в тайлах.</summary>
    [DataField]
    public float MaxDistance = 4f;

    /// <summary>Инвертировать условие (истинно, когда цель ДАЛЬШЕ MaxDistance).</summary>
    [DataField]
    public bool Invert;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _transformSystem = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.OwnerCoordinates, out var coordinates, _entManager))
            return false;

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager) ||
            !_entManager.TryGetComponent<TransformComponent>(target, out var targetXform))
            return false;

        return _transformSystem.InRange(coordinates, targetXform.Coordinates, MaxDistance) ^ Invert;
    }
}
