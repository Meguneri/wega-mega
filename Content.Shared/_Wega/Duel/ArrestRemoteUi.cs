using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared._Wega.Duel;

[Serializable, NetSerializable]
public enum ArrestRemoteUiKey : byte
{
    Key
}

/// <summary>
/// Действие пульта: открыть окно выбора цели ареста.
/// </summary>
public sealed partial class OpenArrestRemoteEvent : InstantActionEvent
{
}

/// <summary>
/// Одна возможная цель ареста в списке пульта.
/// </summary>
[Serializable, NetSerializable]
public struct ArrestRemoteTarget
{
    public NetEntity Entity;

    /// <summary>Отображаемое имя цели.</summary>
    public string Name;

    /// <summary>Краткое описание состояния (вид, «в наручниках», «оглушён» и т.п.).</summary>
    public string Status;

    /// <summary>Уже задерживается — выбирать повторно нельзя.</summary>
    public bool AlreadyTargeted;
}

/// <summary>
/// Состояние окна пульта арестатора: список гуманоидов на гриде и наличие свободного арестатора.
/// </summary>
[Serializable, NetSerializable]
public sealed class ArrestRemoteBuiState : BoundUserInterfaceState
{
    public readonly List<ArrestRemoteTarget> Targets;
    public readonly bool HasBot;

    public ArrestRemoteBuiState(List<ArrestRemoteTarget> targets, bool hasBot)
    {
        Targets = targets;
        HasBot = hasBot;
    }
}

/// <summary>
/// Игрок выбрал цель ареста в окне пульта.
/// </summary>
[Serializable, NetSerializable]
public sealed class ArrestRemoteSelectMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Target;

    public ArrestRemoteSelectMessage(NetEntity target)
    {
        Target = target;
    }
}
