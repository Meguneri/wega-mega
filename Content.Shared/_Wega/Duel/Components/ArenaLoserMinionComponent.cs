using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Shared._Wega.Duel.Components;

/// <summary>
/// Миньон-помощник для игрока, проигравшего 3 дуэли подряд.
/// Следует за владельцем, стреляет в его противников или лечит владельца, если тот ранен.
/// Удаляется при смерти владельца или по окончании боя.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ArenaLoserMinionComponent : Component
{
    /// <summary>Тело-владелец миньона.</summary>
    [DataField]
    public EntityUid MinionOwner;

    /// <summary>Время следующего действия (выстрел/лечение).</summary>
    [DataField(customTypeSerializer: typeof(TimespanSerializer))]
    public TimeSpan NextAction;

    /// <summary>Кулдаун между действиями в секундах.</summary>
    [DataField]
    public float ActionCooldown = 2f;

    /// <summary>Миньон старается держаться в этом радиусе от владельца.</summary>
    [DataField]
    public float FollowRadius = 2f;

    /// <summary>Радиус поиска цели.</summary>
    [DataField]
    public float AttackRadius = 12f;

    /// <summary>Если здоровье владельца ниже этого процента, миньон переключается на лечение.</summary>
    [DataField]
    public float HealThreshold = 0.5f;

    /// <summary>Сколько лечить за одно действие.</summary>
    [DataField]
    public float HealAmount = 10f;

    /// <summary>
    /// Явный список противников (дуэлянты той же арены). Если пуст, миньон стреляет
    /// в ближайшего живого моба в радиусе атаки.
    /// </summary>
    [DataField]
    public List<EntityUid> Enemies = new();

    /// <summary>Прототип снаряда, который миньон запускает во врагов.</summary>
    [DataField]
    public EntProtoId ProjectileProto = "BulletArenaMinion";

    /// <summary>Скорость снаряда.</summary>
    [DataField]
    public float ProjectileSpeed = 15f;

    /// <summary>Скорость полёта миньона к владельцу.</summary>
    [DataField]
    public float MoveSpeed = 5f;
}
