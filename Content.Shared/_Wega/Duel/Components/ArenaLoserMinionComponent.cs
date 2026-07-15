using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Shared._Wega.Duel.Components;

/// <summary>
/// Миньон-помощник для игрока, проигравшего 3 дуэли подряд.
/// Следует за владельцем и лечит его, если тот ранен. Не атакует.
/// Удаляется при смерти владельца или по окончании боя.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ArenaLoserMinionComponent : Component
{
    /// <summary>Тело-владелец миньона.</summary>
    [DataField]
    public EntityUid MinionOwner;

    /// <summary>Время следующего лечения.</summary>
    [DataField(customTypeSerializer: typeof(TimespanSerializer))]
    public TimeSpan NextAction;

    /// <summary>Кулдаун между лечениями в секундах.</summary>
    [DataField]
    public float ActionCooldown = 2f;

    /// <summary>Миньон старается держаться в этом радиусе от владельца.</summary>
    [DataField]
    public float FollowRadius = 2f;

    /// <summary>Если здоровье владельца ниже этого процента, миньон лечит его.</summary>
    [DataField]
    public float HealThreshold = 0.5f;

    /// <summary>Сколько лечить за одно действие.</summary>
    [DataField]
    public float HealAmount = 10f;

    /// <summary>Скорость полёта миньона к владельцу.</summary>
    [DataField]
    public float MoveSpeed = 5f;
}
