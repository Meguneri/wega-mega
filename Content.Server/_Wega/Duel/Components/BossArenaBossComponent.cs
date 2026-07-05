using Content.Shared.Damage;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Компонент босса босс-арены. Хранит пороги фаз и множители скорости/урона для каждой фазы.
/// </summary>
[RegisterComponent]
public sealed partial class BossArenaBossComponent : Component
{
    /// <summary>
    /// Пороги здоровья (доля от максимума) для перехода на следующую фазу. Должны убывать.
    /// </summary>
    [DataField]
    public List<float> PhaseThresholds = new()
    {
        0.66f,
        0.33f,
    };

    /// <summary>
    /// Множители скорости для каждой фазы (индекс = номер фазы).
    /// </summary>
    [DataField]
    public List<float> PhaseSpeedMultipliers = new()
    {
        1.0f,
        1.2f,
        1.5f,
    };

    /// <summary>
    /// Множители урона для каждой фазы (индекс = номер фазы).
    /// </summary>
    [DataField]
    public List<float> PhaseDamageMultipliers = new()
    {
        1.0f,
        1.2f,
        1.5f,
    };

    /// <summary>
    /// Арена, к которой привязан этот босс.
    /// </summary>
    [ViewVariables]
    public EntityUid? Arena;

    /// <summary>
    /// Текущая фаза босса (0 — начальная).
    /// </summary>
    [ViewVariables]
    public int CurrentPhase;

    /// <summary>
    /// Базовая скорость ходьбы, зафиксированная при старте.
    /// </summary>
    [ViewVariables]
    public float BaseWalkSpeed;

    /// <summary>
    /// Базовая скорость бега, зафиксированная при старте.
    /// </summary>
    [ViewVariables]
    public float BaseSprintSpeed;

    /// <summary>
    /// Базовый урон ближнего боя, зафиксированный при старте.
    /// </summary>
    [ViewVariables]
    public DamageSpecifier? BaseMeleeDamage;
}
