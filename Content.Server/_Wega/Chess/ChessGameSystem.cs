using System.Linq;
using Content.Shared._Wega.Chess;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._Wega.Chess;

/// <summary>
/// Шахматная партия на доске: рассаживает игроков, проверяет ходы движком правил, ведёт часы и
/// закрывает партию по мату/пату/ничьей/флажку. Клиенту отправляется только позиция и список
/// легальных ходов — подделать ход с клиента нельзя, всё сверяется здесь.
/// </summary>
public sealed partial class ChessGameSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private Robust.Shared.Timing.IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChessGameComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ChessGameComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<ChessGameComponent, ChessMoveMessage>(OnMove);
        SubscribeLocalEvent<ChessGameComponent, ChessSitMessage>(OnSit);
        SubscribeLocalEvent<ChessGameComponent, ChessNewGameMessage>(OnNewGame);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ChessGameComponent>();
        while (query.MoveNext(out var uid, out var chess))
        {
            if (!chess.ClockEnabled || !chess.ClockRunning || chess.Finished)
                continue;

            // Время идёт только у того, чей ход.
            if (chess.Position.SideToMove == ChessColor.White)
                chess.WhiteTime -= frameTime;
            else
                chess.BlackTime -= frameTime;

            // Раз в секунду досылаем состояние — иначе часы в окне стоят между ходами.
            // Чаще не нужно: показываем целые секунды.
            if (chess.WhiteTime > 0f && chess.BlackTime > 0f)
            {
                if (_timing.CurTime >= chess.NextClockSync)
                {
                    chess.NextClockSync = _timing.CurTime + TimeSpan.FromSeconds(1);
                    UpdateUi(uid, chess);
                }
                continue;
            }

            // Флажок: проигрывает тот, у кого кончилось время.
            chess.Finished = true;
            chess.Winner = chess.WhiteTime <= 0f ? ChessColor.Black : ChessColor.White;
            chess.WhiteTime = MathF.Max(0f, chess.WhiteTime);
            chess.BlackTime = MathF.Max(0f, chess.BlackTime);
            chess.ClockRunning = false;
            _popup.PopupEntity(Loc.GetString("chess-flag-fell"), uid);
            UpdateUi(uid, chess);
        }
    }

    /// <summary>Контроль времени из прототипа применяется сразу — доска готова к партии «из коробки».</summary>
    private void OnMapInit(Entity<ChessGameComponent> ent, ref MapInitEvent args)
    {
        var seconds = Math.Clamp(ent.Comp.DefaultClockSeconds, 0, 3600);
        ent.Comp.ClockEnabled = seconds > 0;
        ent.Comp.WhiteTime = seconds;
        ent.Comp.BlackTime = seconds;
    }

    private void OnUiOpened(Entity<ChessGameComponent> ent, ref BoundUIOpenedEvent args)
    {
        // Первый подошедший садится за белых, второй — за чёрных: обычно этого и хотят.
        if (ent.Comp.White == null && ent.Comp.Black != args.Actor)
            ent.Comp.White = args.Actor;
        else if (ent.Comp.Black == null && ent.Comp.White != args.Actor)
            ent.Comp.Black = args.Actor;

        UpdateUi(ent, ent.Comp);
    }

    private void OnSit(Entity<ChessGameComponent> ent, ref ChessSitMessage args)
    {
        var chess = ent.Comp;
        var actor = args.Actor;

        // Клик по своему же месту = встать из-за доски: иначе освободить его было нечем,
        // и второй игрок не мог бы сесть на твой цвет.
        var vacating = args.Color == ChessColor.White ? chess.White == actor : chess.Black == actor;

        // Встаём с прежнего места, чтобы нельзя было занять оба цвета сразу.
        if (chess.White == actor)
            chess.White = null;
        if (chess.Black == actor)
            chess.Black = null;

        if (vacating)
        {
            UpdateUi(ent, chess);
            return;
        }

        if (args.Color == ChessColor.White && chess.White == null)
            chess.White = actor;
        else if (args.Color == ChessColor.Black && chess.Black == null)
            chess.Black = actor;

        UpdateUi(ent, chess);
    }

    private void OnNewGame(Entity<ChessGameComponent> ent, ref ChessNewGameMessage args)
    {
        var chess = ent.Comp;

        chess.Position = new ChessPosition();
        chess.Finished = false;
        chess.Winner = null;
        chess.LastFrom = -1;
        chess.LastTo = -1;

        var seconds = Math.Clamp(args.ClockSeconds, 0, 3600);
        chess.ClockEnabled = seconds > 0;
        chess.WhiteTime = seconds;
        chess.BlackTime = seconds;
        chess.ClockRunning = false; // пойдут с первого хода

        UpdateUi(ent, chess);
    }

    private void OnMove(Entity<ChessGameComponent> ent, ref ChessMoveMessage args)
    {
        var chess = ent.Comp;
        if (chess.Finished)
            return;

        // Ходить может только тот, кто сидит за доской нужным цветом и чья сейчас очередь.
        var actor = args.Actor;
        var color = chess.White == actor ? ChessColor.White
            : chess.Black == actor ? ChessColor.Black
            : (ChessColor?)null;

        if (color is not { } side || side != chess.Position.SideToMove)
            return;

        var capture = !chess.Position.IsEmpty(args.Move.To);
        if (!chess.Position.TryMakeMove(args.Move))
            return; // нелегальный ход — молча игнорируем, клиент и так их не предлагает

        chess.LastFrom = args.Move.From;
        chess.LastTo = args.Move.To;
        _audio.PlayPvs(capture ? chess.CaptureSound : chess.MoveSound, ent);

        // Часы стартуют после первого хода чёрных: до этого партия ещё «не началась».
        if (chess.ClockEnabled && chess.Position.FullmoveNumber >= 2)
            chess.ClockRunning = true;

        var status = chess.Position.GetStatus();
        switch (status)
        {
            case ChessStatus.Checkmate:
                chess.Finished = true;
                // Матует тот, кто только что сходил, — то есть противник стороны, которой ходить.
                chess.Winner = ChessPosition.Opposite(chess.Position.SideToMove);
                chess.ClockRunning = false;
                break;
            case ChessStatus.Stalemate:
            case ChessStatus.FiftyMove:
            case ChessStatus.InsufficientMaterial:
            case ChessStatus.Repetition:
                chess.Finished = true;
                chess.Winner = null;
                chess.ClockRunning = false;
                break;
        }

        UpdateUi(ent, chess);
    }

    private void UpdateUi(EntityUid uid, ChessGameComponent chess)
    {
        var state = new ChessBuiState
        {
            Fen = chess.Position.ToFen(),
            LegalMoves = chess.Finished ? new List<ChessMove>() : chess.Position.GetLegalMoves(),
            Status = chess.Position.GetStatus(),
            SideToMove = chess.Position.SideToMove,
            WhiteName = chess.White is { } w && Exists(w) ? Name(w) : string.Empty,
            BlackName = chess.Black is { } b && Exists(b) ? Name(b) : string.Empty,
            WhitePlayer = chess.White is { } wp && Exists(wp) ? GetNetEntity(wp) : null,
            BlackPlayer = chess.Black is { } bp && Exists(bp) ? GetNetEntity(bp) : null,
            WhiteTime = chess.WhiteTime,
            BlackTime = chess.BlackTime,
            ClockEnabled = chess.ClockEnabled,
            Finished = chess.Finished,
            Winner = chess.Winner,
            LastFrom = chess.LastFrom,
            LastTo = chess.LastTo,
            // Съеденные фигуры раскладываем по тому, КТО их съел: чёрные фигуры — добыча белых.
            CapturedByWhite = chess.Position.Captured
                .Where(c => c.Color == ChessColor.Black).Select(c => c.Type).ToList(),
            CapturedByBlack = chess.Position.Captured
                .Where(c => c.Color == ChessColor.White).Select(c => c.Type).ToList(),
            MaterialBalance = chess.Position.Captured.Sum(c =>
                c.Color == ChessColor.Black ? PieceValue(c.Type) : -PieceValue(c.Type)),
        };

        _ui.SetUiState(uid, ChessUiKey.Key, state);
    }

    /// <summary>Классические ценности фигур в пешках — для перевеса по материалу.</summary>
    private static int PieceValue(ChessPieceType type) => type switch
    {
        ChessPieceType.Pawn => 1,
        ChessPieceType.Knight or ChessPieceType.Bishop => 3,
        ChessPieceType.Rook => 5,
        ChessPieceType.Queen => 9,
        _ => 0,
    };
}
