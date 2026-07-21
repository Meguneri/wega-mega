using System.Linq;
using System.Text;

namespace Content.Shared._Wega.Chess;

/// <summary>
/// Полные правила шахмат: позиция, генерация ЛЕГАЛЬНЫХ ходов, шах/мат/пат, рокировка, взятие
/// на проходе, превращение, правило 50 ходов, недостаточный материал и троекратное повторение.
/// Скорость не важна (это доска в UI, а не поисковый движок) — важна корректность, поэтому
/// генерация прямолинейная: псевдолегальные ходы + отсев тех, после которых свой король под боем.
/// Корректность проверяется perft-тестами (Content.Tests/_Wega/Chess).
/// </summary>
public sealed class ChessPosition
{
    // Кодировка клетки: 0 — пусто; 1..6 — белые (пешка..король); 9..14 — чёрные.
    private const byte ColorShift = 8;
    private readonly byte[] _squares = new byte[64];

    /// <summary>Хеши позиций для правила троекратного повторения (без ходов — только расстановка).</summary>
    private readonly List<string> _history = new();

    public ChessColor SideToMove { get; private set; } = ChessColor.White;
    public bool WhiteKingSide { get; private set; } = true;
    public bool WhiteQueenSide { get; private set; } = true;
    public bool BlackKingSide { get; private set; } = true;
    public bool BlackQueenSide { get; private set; } = true;

    /// <summary>Клетка, куда возможно взятие на проходе (-1 = нет).</summary>
    public int EnPassant { get; private set; } = -1;

    /// <summary>Полуходы без взятий и ходов пешкой (правило 50 ходов = 100 полуходов).</summary>
    public int HalfmoveClock { get; private set; }

    public int FullmoveNumber { get; private set; } = 1;

    /// <summary>Партия в нотации SAN, по полуходам.</summary>
    public List<string> MoveLog { get; } = new();

    /// <summary>Съеденные фигуры в порядке взятия — для «кладбища» рядом с игроками.</summary>
    public List<(ChessColor Color, ChessPieceType Type)> Captured { get; } = new();

    public ChessPosition()
    {
        SetupInitial();
    }

    private ChessPosition(bool empty)
    {
        // Пустая позиция — для клонирования и FEN.
    }

    // ── Доступ к доске ────────────────────────────────────────────────────────

    public ChessPieceType TypeAt(int square)
        => (ChessPieceType)(_squares[square] & 7);

    public ChessColor ColorAt(int square)
        => (_squares[square] & ColorShift) != 0 ? ChessColor.Black : ChessColor.White;

    public bool IsEmpty(int square) => _squares[square] == 0;

    private void Put(int square, ChessColor color, ChessPieceType type)
        => _squares[square] = (byte)((byte)type | (color == ChessColor.Black ? ColorShift : 0));

    private void SetupInitial()
    {
        var back = new[]
        {
            ChessPieceType.Rook, ChessPieceType.Knight, ChessPieceType.Bishop, ChessPieceType.Queen,
            ChessPieceType.King, ChessPieceType.Bishop, ChessPieceType.Knight, ChessPieceType.Rook,
        };

        for (var file = 0; file < 8; file++)
        {
            Put(ChessSquare.Of(file, 0), ChessColor.White, back[file]);
            Put(ChessSquare.Of(file, 1), ChessColor.White, ChessPieceType.Pawn);
            Put(ChessSquare.Of(file, 6), ChessColor.Black, ChessPieceType.Pawn);
            Put(ChessSquare.Of(file, 7), ChessColor.Black, back[file]);
        }

        _history.Add(PositionKey());
    }

    public ChessPosition Clone()
    {
        var copy = new ChessPosition(true);
        Array.Copy(_squares, copy._squares, 64);
        copy.SideToMove = SideToMove;
        copy.WhiteKingSide = WhiteKingSide;
        copy.WhiteQueenSide = WhiteQueenSide;
        copy.BlackKingSide = BlackKingSide;
        copy.BlackQueenSide = BlackQueenSide;
        copy.EnPassant = EnPassant;
        copy.HalfmoveClock = HalfmoveClock;
        copy.FullmoveNumber = FullmoveNumber;
        copy._history.AddRange(_history);
        copy.MoveLog.AddRange(MoveLog);
        copy.Captured.AddRange(Captured);
        return copy;
    }

    // ── Атаки и шах ───────────────────────────────────────────────────────────

    /// <summary>Бьёт ли сторона <paramref name="by"/> клетку <paramref name="square"/>.</summary>
    public bool IsAttacked(int square, ChessColor by)
    {
        var file = ChessSquare.File(square);
        var rank = ChessSquare.Rank(square);

        // Пешки: смотрим «назад» от атакуемой клетки.
        var pawnRank = by == ChessColor.White ? rank - 1 : rank + 1;
        foreach (var df in new[] { -1, 1 })
        {
            if (!ChessSquare.Valid(file + df, pawnRank))
                continue;
            var sq = ChessSquare.Of(file + df, pawnRank);
            if (TypeAt(sq) == ChessPieceType.Pawn && ColorAt(sq) == by && !IsEmpty(sq))
                return true;
        }

        // Кони.
        foreach (var (df, dr) in KnightOffsets)
        {
            if (!ChessSquare.Valid(file + df, rank + dr))
                continue;
            var sq = ChessSquare.Of(file + df, rank + dr);
            if (!IsEmpty(sq) && TypeAt(sq) == ChessPieceType.Knight && ColorAt(sq) == by)
                return true;
        }

        // Король (для проверки «короли не рядом»).
        for (var df = -1; df <= 1; df++)
        {
            for (var dr = -1; dr <= 1; dr++)
            {
                if ((df | dr) == 0 || !ChessSquare.Valid(file + df, rank + dr))
                    continue;
                var sq = ChessSquare.Of(file + df, rank + dr);
                if (!IsEmpty(sq) && TypeAt(sq) == ChessPieceType.King && ColorAt(sq) == by)
                    return true;
            }
        }

        // Скользящие: ладья/ферзь по прямым, слон/ферзь по диагоналям.
        if (SlidingAttack(file, rank, RookDirections, by, ChessPieceType.Rook))
            return true;
        if (SlidingAttack(file, rank, BishopDirections, by, ChessPieceType.Bishop))
            return true;

        return false;
    }

    private bool SlidingAttack(int file, int rank, (int, int)[] dirs, ChessColor by, ChessPieceType straight)
    {
        foreach (var (df, dr) in dirs)
        {
            for (var step = 1; ; step++)
            {
                var f = file + df * step;
                var r = rank + dr * step;
                if (!ChessSquare.Valid(f, r))
                    break;

                var sq = ChessSquare.Of(f, r);
                if (IsEmpty(sq))
                    continue;

                if (ColorAt(sq) == by)
                {
                    var type = TypeAt(sq);
                    if (type == straight || type == ChessPieceType.Queen)
                        return true;
                }
                break; // любая фигура перекрывает луч
            }
        }
        return false;
    }

    public int FindKing(ChessColor color)
    {
        for (var sq = 0; sq < 64; sq++)
        {
            if (!IsEmpty(sq) && TypeAt(sq) == ChessPieceType.King && ColorAt(sq) == color)
                return sq;
        }
        return -1;
    }

    /// <summary>Сторона <paramref name="color"/> под шахом.</summary>
    public bool InCheck(ChessColor color)
    {
        var king = FindKing(color);
        return king >= 0 && IsAttacked(king, Opposite(color));
    }

    public static ChessColor Opposite(ChessColor color)
        => color == ChessColor.White ? ChessColor.Black : ChessColor.White;

    // ── Генерация ходов ───────────────────────────────────────────────────────

    private static readonly (int, int)[] KnightOffsets =
    {
        (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2),
    };

    private static readonly (int, int)[] RookDirections = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    private static readonly (int, int)[] BishopDirections = { (1, 1), (1, -1), (-1, 1), (-1, -1) };

    /// <summary>Все легальные ходы стороны, которая ходит.</summary>
    public List<ChessMove> GetLegalMoves()
    {
        var result = new List<ChessMove>(48);
        foreach (var move in GeneratePseudoLegal())
        {
            var next = Clone();
            next.ApplyRaw(move);
            if (!next.InCheck(SideToMove))
                result.Add(move);
        }
        return result;
    }

    /// <summary>Легальные ходы конкретной фигуры — для подсветки на доске.</summary>
    public List<ChessMove> GetLegalMovesFrom(int square)
        => GetLegalMoves().Where(m => m.From == square).ToList();

    private List<ChessMove> GeneratePseudoLegal()
    {
        var moves = new List<ChessMove>(64);
        for (var from = 0; from < 64; from++)
        {
            if (IsEmpty(from) || ColorAt(from) != SideToMove)
                continue;

            switch (TypeAt(from))
            {
                case ChessPieceType.Pawn:
                    GeneratePawn(from, moves);
                    break;
                case ChessPieceType.Knight:
                    GenerateJumps(from, KnightOffsets, moves);
                    break;
                case ChessPieceType.Bishop:
                    GenerateSlides(from, BishopDirections, moves);
                    break;
                case ChessPieceType.Rook:
                    GenerateSlides(from, RookDirections, moves);
                    break;
                case ChessPieceType.Queen:
                    GenerateSlides(from, RookDirections, moves);
                    GenerateSlides(from, BishopDirections, moves);
                    break;
                case ChessPieceType.King:
                    GenerateKing(from, moves);
                    break;
            }
        }
        return moves;
    }

    private void GeneratePawn(int from, List<ChessMove> moves)
    {
        var file = ChessSquare.File(from);
        var rank = ChessSquare.Rank(from);
        var dir = SideToMove == ChessColor.White ? 1 : -1;
        var startRank = SideToMove == ChessColor.White ? 1 : 6;
        var lastRank = SideToMove == ChessColor.White ? 7 : 0;

        // Ход вперёд.
        if (ChessSquare.Valid(file, rank + dir))
        {
            var one = ChessSquare.Of(file, rank + dir);
            if (IsEmpty(one))
            {
                AddPawnMove(from, one, lastRank, moves);

                // Двойной — только со стартовой горизонтали и только через пустую клетку.
                if (rank == startRank)
                {
                    var two = ChessSquare.Of(file, rank + dir * 2);
                    if (IsEmpty(two))
                        moves.Add(new ChessMove(from, two));
                }
            }
        }

        // Взятия, включая взятие на проходе.
        foreach (var df in new[] { -1, 1 })
        {
            if (!ChessSquare.Valid(file + df, rank + dir))
                continue;

            var target = ChessSquare.Of(file + df, rank + dir);
            if (!IsEmpty(target) && ColorAt(target) != SideToMove)
                AddPawnMove(from, target, lastRank, moves);
            else if (target == EnPassant)
                moves.Add(new ChessMove(from, target));
        }
    }

    private void AddPawnMove(int from, int to, int lastRank, List<ChessMove> moves)
    {
        if (ChessSquare.Rank(to) == lastRank)
        {
            moves.Add(new ChessMove(from, to, ChessPieceType.Queen));
            moves.Add(new ChessMove(from, to, ChessPieceType.Rook));
            moves.Add(new ChessMove(from, to, ChessPieceType.Bishop));
            moves.Add(new ChessMove(from, to, ChessPieceType.Knight));
        }
        else
        {
            moves.Add(new ChessMove(from, to));
        }
    }

    private void GenerateJumps(int from, (int, int)[] offsets, List<ChessMove> moves)
    {
        var file = ChessSquare.File(from);
        var rank = ChessSquare.Rank(from);
        foreach (var (df, dr) in offsets)
        {
            if (!ChessSquare.Valid(file + df, rank + dr))
                continue;
            var to = ChessSquare.Of(file + df, rank + dr);
            if (IsEmpty(to) || ColorAt(to) != SideToMove)
                moves.Add(new ChessMove(from, to));
        }
    }

    private void GenerateSlides(int from, (int, int)[] dirs, List<ChessMove> moves)
    {
        var file = ChessSquare.File(from);
        var rank = ChessSquare.Rank(from);
        foreach (var (df, dr) in dirs)
        {
            for (var step = 1; ; step++)
            {
                var f = file + df * step;
                var r = rank + dr * step;
                if (!ChessSquare.Valid(f, r))
                    break;

                var to = ChessSquare.Of(f, r);
                if (IsEmpty(to))
                {
                    moves.Add(new ChessMove(from, to));
                    continue;
                }

                if (ColorAt(to) != SideToMove)
                    moves.Add(new ChessMove(from, to));
                break;
            }
        }
    }

    private void GenerateKing(int from, List<ChessMove> moves)
    {
        var file = ChessSquare.File(from);
        var rank = ChessSquare.Rank(from);
        for (var df = -1; df <= 1; df++)
        {
            for (var dr = -1; dr <= 1; dr++)
            {
                if ((df | dr) == 0 || !ChessSquare.Valid(file + df, rank + dr))
                    continue;
                var to = ChessSquare.Of(file + df, rank + dr);
                if (IsEmpty(to) || ColorAt(to) != SideToMove)
                    moves.Add(new ChessMove(from, to));
            }
        }

        // Рокировка: право не потеряно, между королём и ладьёй пусто, король не под шахом и не
        // проходит через битое поле. Само поле назначения проверит общий отсев легальности.
        var homeRank = SideToMove == ChessColor.White ? 0 : 7;
        if (from != ChessSquare.Of(4, homeRank) || InCheck(SideToMove))
            return;

        var enemy = Opposite(SideToMove);
        var kingSide = SideToMove == ChessColor.White ? WhiteKingSide : BlackKingSide;
        var queenSide = SideToMove == ChessColor.White ? WhiteQueenSide : BlackQueenSide;

        if (kingSide
            && IsEmpty(ChessSquare.Of(5, homeRank)) && IsEmpty(ChessSquare.Of(6, homeRank))
            && !IsAttacked(ChessSquare.Of(5, homeRank), enemy)
            && TypeAt(ChessSquare.Of(7, homeRank)) == ChessPieceType.Rook
            && !IsEmpty(ChessSquare.Of(7, homeRank)) && ColorAt(ChessSquare.Of(7, homeRank)) == SideToMove)
        {
            moves.Add(new ChessMove(from, ChessSquare.Of(6, homeRank)));
        }

        if (queenSide
            && IsEmpty(ChessSquare.Of(1, homeRank)) && IsEmpty(ChessSquare.Of(2, homeRank))
            && IsEmpty(ChessSquare.Of(3, homeRank))
            && !IsAttacked(ChessSquare.Of(3, homeRank), enemy)
            && TypeAt(ChessSquare.Of(0, homeRank)) == ChessPieceType.Rook
            && !IsEmpty(ChessSquare.Of(0, homeRank)) && ColorAt(ChessSquare.Of(0, homeRank)) == SideToMove)
        {
            moves.Add(new ChessMove(from, ChessSquare.Of(2, homeRank)));
        }
    }

    // ── Выполнение хода ───────────────────────────────────────────────────────

    /// <summary>
    /// Делает ход, если он легален. Возвращает false, если ход не входит в список легальных —
    /// то есть проверка правил и очерёдности целиком на этой стороне, клиенту верить не нужно.
    /// </summary>
    public bool TryMakeMove(ChessMove move)
    {
        var legal = GetLegalMoves();
        if (!legal.Any(m => m.From == move.From && m.To == move.To && m.Promotion == move.Promotion))
            return false;

        // SAN и съеденную фигуру определяем ДО применения — нужна исходная позиция
        // (для уточнений «Nbd2» и чтобы знать, кого именно сняли с доски).
        var san = ToSan(move, legal);
        if (GetVictim(move) is { } victim)
            Captured.Add(victim);
        ApplyRaw(move);
        _history.Add(PositionKey());

        // Знак шаха/мата дописываем уже по новой позиции.
        var status = GetStatus();
        if (status == ChessStatus.Checkmate)
            san += "#";
        else if (status == ChessStatus.Check)
            san += "+";
        MoveLog.Add(san);
        return true;
    }

    /// <summary>
    /// Кого снимет с доски этот ход (null — тихий ход). Отдельный метод, потому что при взятии
    /// на проходе съеденная пешка стоит НЕ на клетке назначения, а сбоку.
    /// </summary>
    private (ChessColor Color, ChessPieceType Type)? GetVictim(ChessMove move)
    {
        if (!IsEmpty(move.To))
            return (ColorAt(move.To), TypeAt(move.To));

        if (TypeAt(move.From) == ChessPieceType.Pawn && move.To == EnPassant)
            return (Opposite(SideToMove), ChessPieceType.Pawn);

        return null;
    }

    /// <summary>Применяет ход без проверок (внутреннее: для отсева легальности и TryMakeMove).</summary>
    private void ApplyRaw(ChessMove move)
    {
        var piece = TypeAt(move.From);
        var color = ColorAt(move.From);
        var captured = !IsEmpty(move.To);

        // Взятие на проходе: снимаем пешку, стоящую сбоку, а не на клетке назначения.
        if (piece == ChessPieceType.Pawn && move.To == EnPassant && IsEmpty(move.To))
        {
            var capturedPawn = ChessSquare.Of(ChessSquare.File(move.To), ChessSquare.Rank(move.From));
            _squares[capturedPawn] = 0;
            captured = true;
        }

        // Рокировка: король шагает на две клетки — переставляем и ладью.
        if (piece == ChessPieceType.King && Math.Abs(ChessSquare.File(move.To) - ChessSquare.File(move.From)) == 2)
        {
            var rank = ChessSquare.Rank(move.From);
            var (rookFrom, rookTo) = ChessSquare.File(move.To) == 6
                ? (ChessSquare.Of(7, rank), ChessSquare.Of(5, rank))
                : (ChessSquare.Of(0, rank), ChessSquare.Of(3, rank));
            _squares[rookTo] = _squares[rookFrom];
            _squares[rookFrom] = 0;
        }

        _squares[move.To] = _squares[move.From];
        _squares[move.From] = 0;

        if (move.Promotion != ChessPieceType.None)
            Put(move.To, color, move.Promotion);

        // Право на рокировку теряется навсегда при ходе короля/ладьи и при взятии ладьи в углу.
        UpdateCastlingRights(move.From);
        UpdateCastlingRights(move.To);

        // Поле взятия на проходе живёт ровно один полуход.
        EnPassant = piece == ChessPieceType.Pawn
                    && Math.Abs(ChessSquare.Rank(move.To) - ChessSquare.Rank(move.From)) == 2
            ? ChessSquare.Of(ChessSquare.File(move.From), (ChessSquare.Rank(move.From) + ChessSquare.Rank(move.To)) / 2)
            : -1;

        HalfmoveClock = piece == ChessPieceType.Pawn || captured ? 0 : HalfmoveClock + 1;
        if (SideToMove == ChessColor.Black)
            FullmoveNumber++;
        SideToMove = Opposite(SideToMove);
    }

    private void UpdateCastlingRights(int square)
    {
        switch (square)
        {
            case 4: WhiteKingSide = WhiteQueenSide = false; break;   // e1
            case 0: WhiteQueenSide = false; break;                    // a1
            case 7: WhiteKingSide = false; break;                     // h1
            case 60: BlackKingSide = BlackQueenSide = false; break;   // e8
            case 56: BlackQueenSide = false; break;                   // a8
            case 63: BlackKingSide = false; break;                    // h8
        }
    }

    // ── Состояние партии ──────────────────────────────────────────────────────

    public ChessStatus GetStatus()
    {
        var moves = GetLegalMoves();
        var check = InCheck(SideToMove);

        if (moves.Count == 0)
            return check ? ChessStatus.Checkmate : ChessStatus.Stalemate;

        if (HalfmoveClock >= 100)
            return ChessStatus.FiftyMove;
        if (IsInsufficientMaterial())
            return ChessStatus.InsufficientMaterial;
        if (_history.Count(k => k == _history[^1]) >= 3)
            return ChessStatus.Repetition;

        return check ? ChessStatus.Check : ChessStatus.Playing;
    }

    /// <summary>Матовать нечем: К против К, К+слон, К+конь, К+слон против К+слон одного цвета полей.</summary>
    private bool IsInsufficientMaterial()
    {
        var minors = new List<(ChessColor Color, ChessPieceType Type, int Square)>();
        for (var sq = 0; sq < 64; sq++)
        {
            if (IsEmpty(sq))
                continue;
            var type = TypeAt(sq);
            if (type == ChessPieceType.King)
                continue;
            if (type is ChessPieceType.Pawn or ChessPieceType.Rook or ChessPieceType.Queen)
                return false;
            minors.Add((ColorAt(sq), type, sq));
        }

        if (minors.Count <= 1)
            return true;

        if (minors.Count == 2 && minors.All(m => m.Type == ChessPieceType.Bishop)
            && minors[0].Color != minors[1].Color)
        {
            // Разноцветные слоны матуют, только если стоят на полях разного цвета.
            var a = (ChessSquare.File(minors[0].Square) + ChessSquare.Rank(minors[0].Square)) % 2;
            var b = (ChessSquare.File(minors[1].Square) + ChessSquare.Rank(minors[1].Square)) % 2;
            return a == b;
        }

        return false;
    }

    // ── Нотация ───────────────────────────────────────────────────────────────

    /// <summary>Ход в человеческой нотации SAN («Nf3», «exd5», «O-O», «e8=Q»).</summary>
    private string ToSan(ChessMove move, List<ChessMove> legal)
    {
        var piece = TypeAt(move.From);

        if (piece == ChessPieceType.King
            && Math.Abs(ChessSquare.File(move.To) - ChessSquare.File(move.From)) == 2)
        {
            return ChessSquare.File(move.To) == 6 ? "O-O" : "O-O-O";
        }

        var capture = !IsEmpty(move.To)
                      || (piece == ChessPieceType.Pawn && move.To == EnPassant);

        var sb = new StringBuilder();
        if (piece == ChessPieceType.Pawn)
        {
            if (capture)
                sb.Append((char)('a' + ChessSquare.File(move.From))).Append('x');
        }
        else
        {
            sb.Append(ChessSquare.SanLetter(piece));

            // Уточнение, если такой же ход может сделать другая такая же фигура.
            var rivals = legal.Where(m => m.To == move.To && m.From != move.From
                                          && TypeAt(m.From) == piece).ToList();
            if (rivals.Count > 0)
            {
                var sameFile = rivals.Any(m => ChessSquare.File(m.From) == ChessSquare.File(move.From));
                var sameRank = rivals.Any(m => ChessSquare.Rank(m.From) == ChessSquare.Rank(move.From));
                if (!sameFile)
                    sb.Append((char)('a' + ChessSquare.File(move.From)));
                else if (!sameRank)
                    sb.Append(ChessSquare.Rank(move.From) + 1);
                else
                    sb.Append(ChessSquare.Name(move.From));
            }

            if (capture)
                sb.Append('x');
        }

        sb.Append(ChessSquare.Name(move.To));
        if (move.Promotion != ChessPieceType.None)
            sb.Append('=').Append(char.ToUpperInvariant(ChessSquare.PromotionChar(move.Promotion)));

        return sb.ToString();
    }

    /// <summary>Ключ позиции для повторений: расстановка + очередь + права + поле взятия.</summary>
    private string PositionKey()
    {
        var sb = new StringBuilder(80);
        foreach (var sq in _squares)
            sb.Append((char)('0' + sq));
        sb.Append(SideToMove == ChessColor.White ? 'w' : 'b');
        sb.Append(WhiteKingSide ? 'K' : '-').Append(WhiteQueenSide ? 'Q' : '-');
        sb.Append(BlackKingSide ? 'k' : '-').Append(BlackQueenSide ? 'q' : '-');
        sb.Append(EnPassant);
        return sb.ToString();
    }

    // ── FEN (для сети, тестов и отладки) ──────────────────────────────────────

    /// <summary>Позиция в FEN — этим состояние уезжает клиенту.</summary>
    public string ToFen()
    {
        var sb = new StringBuilder();
        for (var rank = 7; rank >= 0; rank--)
        {
            var empty = 0;
            for (var file = 0; file < 8; file++)
            {
                var sq = ChessSquare.Of(file, rank);
                if (IsEmpty(sq))
                {
                    empty++;
                    continue;
                }

                if (empty > 0)
                {
                    sb.Append(empty);
                    empty = 0;
                }

                var c = TypeAt(sq) switch
                {
                    ChessPieceType.Pawn => 'p',
                    ChessPieceType.Knight => 'n',
                    ChessPieceType.Bishop => 'b',
                    ChessPieceType.Rook => 'r',
                    ChessPieceType.Queen => 'q',
                    _ => 'k',
                };
                sb.Append(ColorAt(sq) == ChessColor.White ? char.ToUpperInvariant(c) : c);
            }

            if (empty > 0)
                sb.Append(empty);
            if (rank > 0)
                sb.Append('/');
        }

        sb.Append(SideToMove == ChessColor.White ? " w " : " b ");
        var castling = new StringBuilder();
        if (WhiteKingSide) castling.Append('K');
        if (WhiteQueenSide) castling.Append('Q');
        if (BlackKingSide) castling.Append('k');
        if (BlackQueenSide) castling.Append('q');
        sb.Append(castling.Length > 0 ? castling.ToString() : "-");
        sb.Append(' ').Append(EnPassant >= 0 ? ChessSquare.Name(EnPassant) : "-");
        sb.Append(' ').Append(HalfmoveClock).Append(' ').Append(FullmoveNumber);
        return sb.ToString();
    }

    /// <summary>Разбор FEN — нужен тестам (perft по эталонным позициям) и восстановлению партии.</summary>
    public static ChessPosition FromFen(string fen)
    {
        var pos = new ChessPosition(true);
        var parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rank = 7;
        var file = 0;

        foreach (var c in parts[0])
        {
            if (c == '/')
            {
                rank--;
                file = 0;
                continue;
            }

            if (char.IsDigit(c))
            {
                file += c - '0';
                continue;
            }

            var type = char.ToLowerInvariant(c) switch
            {
                'p' => ChessPieceType.Pawn,
                'n' => ChessPieceType.Knight,
                'b' => ChessPieceType.Bishop,
                'r' => ChessPieceType.Rook,
                'q' => ChessPieceType.Queen,
                _ => ChessPieceType.King,
            };
            pos.Put(ChessSquare.Of(file, rank), char.IsUpper(c) ? ChessColor.White : ChessColor.Black, type);
            file++;
        }

        pos.SideToMove = parts.Length > 1 && parts[1] == "b" ? ChessColor.Black : ChessColor.White;

        var rights = parts.Length > 2 ? parts[2] : "-";
        pos.WhiteKingSide = rights.Contains('K');
        pos.WhiteQueenSide = rights.Contains('Q');
        pos.BlackKingSide = rights.Contains('k');
        pos.BlackQueenSide = rights.Contains('q');

        pos.EnPassant = parts.Length > 3 && parts[3] != "-"
            ? ChessSquare.Of(parts[3][0] - 'a', parts[3][1] - '1')
            : -1;
        pos.HalfmoveClock = parts.Length > 4 && int.TryParse(parts[4], out var hm) ? hm : 0;
        pos.FullmoveNumber = parts.Length > 5 && int.TryParse(parts[5], out var fm) ? fm : 1;

        pos._history.Add(pos.PositionKey());
        return pos;
    }

    /// <summary>
    /// Perft — эталонная проверка генератора ходов: сколько листьев в дереве на глубину N.
    /// Числа для известных позиций опубликованы, расхождение = баг в правилах.
    /// </summary>
    public long Perft(int depth)
    {
        if (depth <= 0)
            return 1;

        var moves = GetLegalMoves();
        if (depth == 1)
            return moves.Count;

        long nodes = 0;
        foreach (var move in moves)
        {
            var next = Clone();
            next.ApplyRaw(move);
            nodes += next.Perft(depth - 1);
        }
        return nodes;
    }
}
