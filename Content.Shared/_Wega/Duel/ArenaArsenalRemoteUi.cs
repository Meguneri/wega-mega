using Robust.Shared.Serialization;

namespace Content.Shared._Wega.Duel;

[Serializable, NetSerializable]
public enum ArenaArsenalRemoteUiKey : byte
{
    Key
}

/// <summary>Один выбираемый тир арсенала в окне пульта.</summary>
[Serializable, NetSerializable]
public struct ArenaArsenalOption
{
    /// <summary>Прототип крейта; <c>null</c> — вариант «отключить спавн».</summary>
    public string? CrateProto;

    /// <summary>Отображаемое имя тира.</summary>
    public string Name;

    /// <summary>Это текущий выбранный тир.</summary>
    public bool Current;
}

/// <summary>Состояние окна пульта арсенала: список выбираемых тиров.</summary>
[Serializable, NetSerializable]
public sealed class ArenaArsenalRemoteBuiState : BoundUserInterfaceState
{
    public readonly List<ArenaArsenalOption> Options;

    public ArenaArsenalRemoteBuiState(List<ArenaArsenalOption> options)
    {
        Options = options;
    }
}

/// <summary>Игрок выбрал тир арсенала в окне пульта. <c>null</c> — отключить спавн крейтов.</summary>
[Serializable, NetSerializable]
public sealed class ArenaArsenalSelectMessage : BoundUserInterfaceMessage
{
    public readonly string? CrateProto;

    public ArenaArsenalSelectMessage(string? crateProto)
    {
        CrateProto = crateProto;
    }
}
