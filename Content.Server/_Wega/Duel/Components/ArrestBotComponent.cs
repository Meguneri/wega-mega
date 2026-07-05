using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Состояние арест-бота.
/// </summary>
public enum ArrestBotState
{
    Idle,
    Pursuing,
    Stunned,
    Cuffed,
    Delivering,
    Done
}

/// <summary>
/// Компонент арест-бота. Преследует назначенную цель, оглушает её, надевает наручники и ведёт к тому, кто отдал приказ.
/// </summary>
[RegisterComponent]
public sealed partial class ArrestBotComponent : Component
{
    /// <summary>
    /// Цель задержания.
    /// </summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>
    /// Тот, кто отдал приказ на задержание. Бот ведёт цель к этому существу.
    /// </summary>
    [ViewVariables]
    public EntityUid? Issuer;

    /// <summary>
    /// Текущее состояние.
    /// </summary>
    [ViewVariables]
    public ArrestBotState State = ArrestBotState.Idle;

    /// <summary>
    /// Радиус, с которого бот применяет стан/наручники.
    /// </summary>
    [DataField]
    public float ArrestRange = 1.2f;

    /// <summary>
    /// Длительность оглушения (секунды).
    /// </summary>
    [DataField]
    public float StunDuration = 4f;

    /// <summary>
    /// Прототип наручников, которые надевает бот.
    /// </summary>
    [DataField]
    public EntProtoId HandcuffPrototype = "Handcuffs";

    /// <summary>
    /// Скорость бота.
    /// </summary>
    [DataField]
    public float MoveSpeed = 3.5f;

    /// <summary>
    /// Задержка между действиями (секунды).
    /// </summary>
    [DataField]
    public float ActionCooldown = 1f;

    /// <summary>
    /// Время следующего действия.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextAction;

    /// <summary>
    /// Время, через которое бот сбросится, если не может завершить задержание.
    /// </summary>
    [DataField]
    public float Timeout = 60f;

    /// <summary>
    /// Время окончания таймаута.
    /// </summary>
    [ViewVariables]
    public TimeSpan? TimeoutEndAt;
}
