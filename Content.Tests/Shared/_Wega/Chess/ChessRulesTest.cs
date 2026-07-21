using System.Linq;
using Content.Shared._Wega.Chess;
using NUnit.Framework;

namespace Content.Tests.Shared._Wega.Chess;

/// <summary>
/// Проверка шахматных правил по методу perft: считаем число листьев в дереве ходов на глубину N
/// и сверяем с опубликованными эталонными числами. Расхождение хотя бы на единицу означает баг
/// в генерации — забытая рокировка, лишнее взятие на проходе, пропущенный отсев шаха и т.п.
/// Позиции — стандартный набор (Chess Programming Wiki), он покрывает все особые правила.
/// </summary>
[TestFixture]
public sealed class ChessRulesTest
{
    private const string Kiwipete =
        "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";
    private const string EnPassantHeavy = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1";
    private const string PromotionHeavy = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 b - - 0 1";
    private const string Position4 =
        "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1";
    private const string Position5 = "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8";

    [Test]
    public void InitialPositionPerft()
    {
        var pos = new ChessPosition();
        Assert.Multiple(() =>
        {
            Assert.That(pos.Perft(1), Is.EqualTo(20), "стартовая, глубина 1");
            Assert.That(pos.Perft(2), Is.EqualTo(400), "стартовая, глубина 2");
            Assert.That(pos.Perft(3), Is.EqualTo(8902), "стартовая, глубина 3");
            Assert.That(pos.Perft(4), Is.EqualTo(197281), "стартовая, глубина 4");
        });
    }

    /// <summary>Kiwipete: рокировки в обе стороны, связки, много взятий — главный тест на правила.</summary>
    [Test]
    public void KiwipetePerft()
    {
        var pos = ChessPosition.FromFen(Kiwipete);
        Assert.Multiple(() =>
        {
            Assert.That(pos.Perft(1), Is.EqualTo(48));
            Assert.That(pos.Perft(2), Is.EqualTo(2039));
            Assert.That(pos.Perft(3), Is.EqualTo(97862));
        });
    }

    /// <summary>
    /// Позиция 3 CPW: плотное взятие на проходе и вскрытые шахи. Числа только опубликованные —
    /// свои выдумывать нельзя, иначе тест проверяет не движок, а мою фантазию.
    /// </summary>
    [Test]
    public void EnPassantPerft()
    {
        var pos = ChessPosition.FromFen(EnPassantHeavy);
        Assert.Multiple(() =>
        {
            Assert.That(pos.Perft(1), Is.EqualTo(14));
            Assert.That(pos.Perft(2), Is.EqualTo(191));
            Assert.That(pos.Perft(3), Is.EqualTo(2812));
            Assert.That(pos.Perft(4), Is.EqualTo(43238));
            Assert.That(pos.Perft(5), Is.EqualTo(674624));
        });
    }

    /// <summary>Позиция 4: превращения под шахом.</summary>
    [Test]
    public void PromotionPerft()
    {
        var pos = ChessPosition.FromFen(Position4);
        Assert.Multiple(() =>
        {
            Assert.That(pos.Perft(1), Is.EqualTo(6));
            Assert.That(pos.Perft(2), Is.EqualTo(264));
            Assert.That(pos.Perft(3), Is.EqualTo(9467));
        });
    }

    /// <summary>Позиция 5: частичные права на рокировку.</summary>
    [Test]
    public void CastlingRightsPerft()
    {
        var pos = ChessPosition.FromFen(Position5);
        Assert.Multiple(() =>
        {
            Assert.That(pos.Perft(1), Is.EqualTo(44));
            Assert.That(pos.Perft(2), Is.EqualTo(1486));
            Assert.That(pos.Perft(3), Is.EqualTo(62379));
        });
    }

    /// <summary>Детский мат: партия обязана закончиться матом, а не «шахом».</summary>
    [Test]
    public void ScholarsMateEndsGame()
    {
        var pos = new ChessPosition();
        foreach (var uci in new[] { "e2e4", "e7e5", "f1c4", "b8c6", "d1h5", "g8f6", "h5f7" })
        {
            var from = ChessSquare.Of(uci[0] - 'a', uci[1] - '1');
            var to = ChessSquare.Of(uci[2] - 'a', uci[3] - '1');
            Assert.That(pos.TryMakeMove(new ChessMove(from, to)), Is.True, $"ход {uci} должен быть легален");
        }

        Assert.That(pos.GetStatus(), Is.EqualTo(ChessStatus.Checkmate), "должен быть мат");
        Assert.That(pos.MoveLog[^1], Is.EqualTo("Qxf7#"), "последний ход в SAN");
    }

    /// <summary>Нелегальные ходы должны отбиваться: чужой очередью, сквозь фигуры, под шах.</summary>
    [Test]
    public void IllegalMovesRejected()
    {
        var pos = new ChessPosition();
        Assert.Multiple(() =>
        {
            // Ход чёрными, когда очередь белых.
            Assert.That(pos.TryMakeMove(new ChessMove(ChessSquare.Of(4, 6), ChessSquare.Of(4, 4))), Is.False);
            // Ладья сквозь свою пешку.
            Assert.That(pos.TryMakeMove(new ChessMove(ChessSquare.Of(0, 0), ChessSquare.Of(0, 4))), Is.False);
            // Пешка на три клетки.
            Assert.That(pos.TryMakeMove(new ChessMove(ChessSquare.Of(4, 1), ChessSquare.Of(4, 4))), Is.False);
        });

        // Король под шахом ладьи по линии e: уходить можно только С линии, а не по ней.
        var check = ChessPosition.FromFen("4r3/8/8/8/8/8/8/4K3 w - - 0 1");
        var kingMoves = check.GetLegalMovesFrom(ChessSquare.Of(4, 0));
        Assert.Multiple(() =>
        {
            Assert.That(check.InCheck(ChessColor.White), Is.True, "белый король под шахом");
            Assert.That(kingMoves.Any(m => m.To == ChessSquare.Of(4, 1)), Is.False,
                "e2 остаётся под боем ладьи — ход нелегален");
            Assert.That(kingMoves.Select(m => m.To),
                Is.EquivalentTo(new[]
                {
                    ChessSquare.Of(3, 0), ChessSquare.Of(5, 0),   // d1, f1
                    ChessSquare.Of(3, 1), ChessSquare.Of(5, 1),   // d2, f2
                }), "уйти можно ровно на четыре клетки вне линии e");
        });

        // Связка: конь e2 стоит между своим королём e1 и чёрной ладьёй e8 — любой его ход
        // открывает короля под шах, значит легальных ходов у коня нет вовсе.
        var pinned = ChessPosition.FromFen("4r2k/8/8/8/8/8/4N3/4K3 w - - 0 1");
        Assert.Multiple(() =>
        {
            Assert.That(pinned.InCheck(ChessColor.White), Is.False, "шаха нет: конь перекрывает линию");
            Assert.That(pinned.GetLegalMovesFrom(ChessSquare.Of(4, 1)), Is.Empty, "связанный конь не ходит");
        });
    }

    /// <summary>Пат: ходов нет, но шаха нет — ничья, а не мат.</summary>
    [Test]
    public void StalemateDetected()
    {
        var pos = ChessPosition.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");
        Assert.That(pos.GetStatus(), Is.EqualTo(ChessStatus.Stalemate));
    }

    /// <summary>Ничья по недостатку материала: голые короли и король со слоном.</summary>
    [Test]
    public void InsufficientMaterialDetected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ChessPosition.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1").GetStatus(),
                Is.EqualTo(ChessStatus.InsufficientMaterial), "голые короли");
            Assert.That(ChessPosition.FromFen("4k3/8/8/8/8/8/8/3BK3 w - - 0 1").GetStatus(),
                Is.EqualTo(ChessStatus.InsufficientMaterial), "король со слоном");
            Assert.That(ChessPosition.FromFen("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1").GetStatus(),
                Is.Not.EqualTo(ChessStatus.InsufficientMaterial), "с пешкой мат возможен");
        });
    }

    /// <summary>
    /// Учёт съеденных фигур: обычное взятие и взятие на проходе, где съеденная пешка стоит
    /// НЕ на клетке назначения — там легко записать не ту фигуру или не записать вовсе.
    /// </summary>
    [Test]
    public void CapturedPiecesTracked()
    {
        var pos = new ChessPosition();
        Assert.That(pos.Captured, Is.Empty, "в начале партии никого не съели");

        // 1.e4 d5 2.exd5 — белые забирают пешку.
        Play(pos, "e2e4", "d7d5", "e4d5");
        Assert.That(pos.Captured, Has.Count.EqualTo(1));
        Assert.That(pos.Captured[0], Is.EqualTo((ChessColor.Black, ChessPieceType.Pawn)));

        // Взятие на проходе: чёрная пешка бьёт белую, стоящую сбоку от клетки назначения.
        var ep = ChessPosition.FromFen("4k3/8/8/8/4pP2/8/8/4K3 b - f3 0 1");
        Play(ep, "e4f3");
        Assert.That(ep.Captured, Has.Count.EqualTo(1), "взятие на проходе должно учитываться");
        Assert.That(ep.Captured[0], Is.EqualTo((ChessColor.White, ChessPieceType.Pawn)));

        // Тихий ход ничего не добавляет.
        var quiet = new ChessPosition();
        Play(quiet, "g1f3");
        Assert.That(quiet.Captured, Is.Empty, "ход без взятия не должен попадать в список");
    }

    private static void Play(ChessPosition pos, params string[] moves)
    {
        foreach (var uci in moves)
        {
            var from = ChessSquare.Of(uci[0] - 'a', uci[1] - '1');
            var to = ChessSquare.Of(uci[2] - 'a', uci[3] - '1');
            Assert.That(pos.TryMakeMove(new ChessMove(from, to)), Is.True, $"ход {uci} должен быть легален");
        }
    }

    /// <summary>FEN должен переживать круговой обход без потерь.</summary>
    [Test]
    public void FenRoundTrip()
    {
        foreach (var fen in new[] { Kiwipete, Position4, Position5 })
            Assert.That(ChessPosition.FromFen(fen).ToFen(), Is.EqualTo(fen), $"круговой обход {fen}");
    }
}
