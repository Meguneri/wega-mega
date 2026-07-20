using Robust.Shared.Serialization;

namespace Content.Shared._Wega.Chess;

/// <summary>
/// UI шахматной доски: позиция, очередь хода, подсветка легальных ходов, часы и статус партии.
/// Правила целиком на сервере (<c>ChessPosition</c>) — клиент только рисует и просит сделать ход.
/// </summary>
[Serializable, NetSerializable]
public enum ChessUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ChessBuiState : BoundUserInterfaceState
{
    /// <summary>Позиция в FEN — из неё клиент рисует доску.</summary>
    public string Fen = string.Empty;

    /// <summary>Легальные ходы стороны, которая ходит (UCI: «e2e4»), для подсветки.</summary>
    public List<ChessMove> LegalMoves = new();

    public ChessStatus Status;

    /// <summary>Чей ход.</summary>
    public ChessColor SideToMove;

    /// <summary>Имя игрока за белых/чёрных; пусто = место свободно.</summary>
    public string WhiteName = string.Empty;
    public string BlackName = string.Empty;

    /// <summary>
    /// Кто сидит за доской. Состояние UI общее для всех зрителей, поэтому свой цвет клиент
    /// определяет сам, сравнивая эти сущности со своей — так надёжнее, чем сверять имена.
    /// </summary>
    public NetEntity? WhitePlayer;
    public NetEntity? BlackPlayer;

    /// <summary>Остаток времени, сек. Отрицательное не бывает; 0 при выключенных часах.</summary>
    public float WhiteTime;
    public float BlackTime;

    /// <summary>Часы включены (блиц) — иначе таймеры не показываем.</summary>
    public bool ClockEnabled;

    /// <summary>Партия окончена: победитель (null = ничья) и причина.</summary>
    public bool Finished;
    public ChessColor? Winner;

    /// <summary>Последний сделанный ход — подсвечиваем клетки «откуда/куда».</summary>
    public int LastFrom = -1;
    public int LastTo = -1;
}

/// <summary>Клиент просит сделать ход. Легальность проверяет сервер.</summary>
[Serializable, NetSerializable]
public sealed class ChessMoveMessage : BoundUserInterfaceMessage
{
    public ChessMove Move;

    public ChessMoveMessage(ChessMove move)
    {
        Move = move;
    }
}

/// <summary>Сесть за доску выбранным цветом (или встать, если уже сидишь).</summary>
[Serializable, NetSerializable]
public sealed class ChessSitMessage : BoundUserInterfaceMessage
{
    public ChessColor Color;

    public ChessSitMessage(ChessColor color)
    {
        Color = color;
    }
}

/// <summary>Начать новую партию. <see cref="ClockSeconds"/> = 0 — без часов.</summary>
[Serializable, NetSerializable]
public sealed class ChessNewGameMessage : BoundUserInterfaceMessage
{
    public int ClockSeconds;

    public ChessNewGameMessage(int clockSeconds)
    {
        ClockSeconds = clockSeconds;
    }
}
