using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Shared._Wega.Duel.Components;

/// <summary>
/// Скрытое «Право на реванш» для игрока, проигравшего 3 дуэли подряд.
/// Компонент хранится на самом бойце: отдельная сущность больше не спавнится и не выдаёт позицию.
/// Удаляется при смерти владельца или по окончании боя.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ArenaLoserMinionComponent : Component
{
    /// <summary>Право на реванш уже использовано в текущей дуэли.</summary>
    [ViewVariables]
    public bool Used;

    /// <summary>Момент окончания краткого ускорения.</summary>
    [ViewVariables]
    public TimeSpan ActiveUntil;

    /// <summary>Компонент игнорирования замедления был добавлен именно «Правом на реванш».</summary>
    [ViewVariables]
    public bool AddedIgnoreSlowOnDamage;
}
