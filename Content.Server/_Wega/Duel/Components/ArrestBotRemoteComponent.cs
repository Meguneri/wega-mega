using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Компонент пульта управления арестатором. Позволяет как ткнуть цель напрямую,
/// так и открыть окно выбора цели среди гуманоидов на гриде по действию (клавише).
/// </summary>
[RegisterComponent]
public sealed partial class ArrestBotRemoteComponent : Component
{
    /// <summary>
    /// Радиус поиска арестатора при прямом тычке в цель.
    /// </summary>
    [DataField]
    public float BotSearchRange = 30f;

    /// <summary>
    /// Действие, открывающее окно выбора цели. Выдаётся, пока пульт в руке.
    /// </summary>
    [DataField]
    public EntProtoId Action = "ActionOpenArrestRemote";

    /// <summary>
    /// Выданное действие (для снятия при выкладывании из рук).
    /// </summary>
    [ViewVariables]
    public EntityUid? ActionEntity;
}
