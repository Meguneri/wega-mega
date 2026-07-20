using Robust.Shared.Serialization;

namespace Content.Shared._Wega.Chess;

/// <summary>Сторона.</summary>
public enum ChessColor : byte
{
    White,
    Black,
}

/// <summary>Тип фигуры. None = пустая клетка.</summary>
public enum ChessPieceType : byte
{
    None,
    Pawn,
    Knight,
    Bishop,
    Rook,
    Queen,
    King,
}

/// <summary>Исход партии.</summary>
public enum ChessStatus : byte
{
    /// <summary>Партия идёт.</summary>
    Playing,
    /// <summary>Ход стороны, стоящей под шахом.</summary>
    Check,
    /// <summary>Мат: ходов нет, король под боем.</summary>
    Checkmate,
    /// <summary>Пат: ходов нет, шаха нет.</summary>
    Stalemate,
    /// <summary>Ничья по правилу 50 ходов.</summary>
    FiftyMove,
    /// <summary>Ничья: недостаточно материала для мата.</summary>
    InsufficientMaterial,
    /// <summary>Ничья троекратным повторением позиции.</summary>
    Repetition,
}

/// <summary>
/// Ход: откуда, куда и (для пешки на последней горизонтали) во что превращаемся.
/// Клетки — индексы 0..63, где 0 = a1, 7 = h1, 56 = a8, 63 = h8.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct ChessMove(int From, int To, ChessPieceType Promotion = ChessPieceType.None)
{
    /// <summary>Запись хода в UCI («e2e4», «e7e8q») — компактный ключ для сети и логов.</summary>
    public string ToUci()
    {
        var s = $"{ChessSquare.Name(From)}{ChessSquare.Name(To)}";
        return Promotion == ChessPieceType.None ? s : s + ChessSquare.PromotionChar(Promotion);
    }
}

/// <summary>Помощники по клеткам доски.</summary>
public static class ChessSquare
{
    public static int File(int square) => square & 7;
    public static int Rank(int square) => square >> 3;
    public static int Of(int file, int rank) => rank * 8 + file;
    public static bool Valid(int file, int rank) => file is >= 0 and < 8 && rank is >= 0 and < 8;

    /// <summary>Имя клетки в алгебраической нотации: 0 → «a1».</summary>
    public static string Name(int square) => $"{(char)('a' + File(square))}{Rank(square) + 1}";

    public static char PromotionChar(ChessPieceType type) => type switch
    {
        ChessPieceType.Queen => 'q',
        ChessPieceType.Rook => 'r',
        ChessPieceType.Bishop => 'b',
        ChessPieceType.Knight => 'n',
        _ => '?',
    };

    /// <summary>Буква фигуры для нотации SAN (пешка — пустая строка).</summary>
    public static string SanLetter(ChessPieceType type) => type switch
    {
        ChessPieceType.Knight => "N",
        ChessPieceType.Bishop => "B",
        ChessPieceType.Rook => "R",
        ChessPieceType.Queen => "Q",
        ChessPieceType.King => "K",
        _ => string.Empty,
    };
}
