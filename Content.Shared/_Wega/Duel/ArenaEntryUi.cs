using Robust.Shared.Serialization;

namespace Content.Shared._Wega.Duel;

/// <summary>
/// UI кнопки входа на арену (хаб ротации): выбор тира арсенал-ящиков перед входом. Состояние окна
/// переиспользует <see cref="ArenaArsenalRemoteBuiState"/> (тот же список тиров, что у пульта).
/// </summary>
[Serializable, NetSerializable]
public enum ArenaEntryUiKey : byte
{
    Key
}

/// <summary>
/// Игрок нажал «Войти» в окне кнопки входа: выбранный тир (<c>null</c> — без ящиков) применяется ко
/// всем аренам, после чего дуэлянтов сразу телепортирует на арену, а ящики спавнятся у спавнов.
/// </summary>
[Serializable, NetSerializable]
public sealed class ArenaEntryConfirmMessage : BoundUserInterfaceMessage
{
    public readonly string? CrateProto;

    public ArenaEntryConfirmMessage(string? crateProto)
    {
        CrateProto = crateProto;
    }
}
