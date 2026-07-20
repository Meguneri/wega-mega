using System.Linq;
using System.Numerics;
using Content.Shared._Wega.Chess;
using Robust.Client.Graphics;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._Wega.Chess;

/// <summary>
/// Доска 8×8 в духе Lichess: клик по своей фигуре подсвечивает её легальные ходы точками, клик по
/// подсвеченной клетке делает ход. Правила и очередь проверяет сервер — здесь только отрисовка,
/// поэтому подсветка строится строго по присланному списку легальных ходов.
/// </summary>
public sealed class ChessWindow : DefaultWindow
{
    private const int Ranks = 8;
    private const int SquarePx = 56;

    private static readonly Color LightSquare = Color.FromHex("#EFD9B4");
    private static readonly Color DarkSquare = Color.FromHex("#B58863");
    private static readonly Color LastMoveTint = Color.FromHex("#CDD26A");
    private static readonly Color SelectedTint = Color.FromHex("#7FB069");
    private static readonly Color HintColor = Color.FromHex("#20B2AA");

    private readonly IPlayerManager _player;
    private readonly SpriteSystem _sprites;

    private readonly GridContainer _board;
    private readonly PanelContainer[] _cells = new PanelContainer[64];
    private readonly TextureRect[] _pieces = new TextureRect[64];
    private readonly Control[] _hints = new Control[64];

    private readonly Label _status;
    private readonly Label _whiteLabel;
    private readonly Label _blackLabel;
    private readonly Button _sitWhite;
    private readonly Button _sitBlack;
    private readonly OptionButton _clockChoice;
    private readonly Button _newGame;

    private ChessBuiState? _state;
    private int _selected = -1;

    /// <summary>Доска развёрнута к чёрным (когда играешь за чёрных — как на Lichess).</summary>
    private bool _flipped;

    public event Action<ChessMove>? OnMove;
    public event Action<ChessColor>? OnSit;
    public event Action<int>? OnNewGame;

    public ChessWindow()
    {
        Title = Loc.GetString("chess-window-title");
        MinSize = new Vector2(Ranks * SquarePx + 260, Ranks * SquarePx + 96);

        _player = IoCManager.Resolve<IPlayerManager>();
        _sprites = IoCManager.Resolve<IEntityManager>().System<SpriteSystem>();

        // Именно Contents, а не AddChild: у DefaultWindow свои дети (фон, шапка, контейнер
        // содержимого), и добавление напрямую в окно кладёт доску поверх оформления.
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        Contents.AddChild(root);

        _board = new GridContainer { Columns = Ranks, HSeparationOverride = 0, VSeparationOverride = 0 };
        root.AddChild(_board);

        // 64 клетки: панель-фон + фигура + точка-подсказка поверх.
        for (var i = 0; i < 64; i++)
        {
            var piece = new TextureRect
            {
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
                TextureScale = new Vector2(1.6f, 1.6f),
                MouseFilter = MouseFilterMode.Ignore,
            };
            var hint = new PanelContainer
            {
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
                MinSize = new Vector2(16, 16),
                Visible = false,
                MouseFilter = MouseFilterMode.Ignore,
                PanelOverride = new StyleBoxFlat { BackgroundColor = HintColor.WithAlpha(0.65f) },
            };

            var cell = new PanelContainer
            {
                MinSize = new Vector2(SquarePx, SquarePx),
                PanelOverride = new StyleBoxFlat { BackgroundColor = LightSquare },
                // Обязательно: у Control по умолчанию MouseFilter = Ignore, и клетки не получали
                // бы кликов вовсе — доска была бы полностью нерабочей.
                MouseFilter = MouseFilterMode.Stop,
            };
            cell.AddChild(piece);
            cell.AddChild(hint);

            var index = i;
            cell.OnKeyBindDown += args =>
            {
                if (args.Function == EngineKeyFunctions.UIClick)
                    OnCellClicked(index);
            };

            _cells[i] = cell;
            _pieces[i] = piece;
            _hints[i] = hint;
            _board.AddChild(cell);
        }

        // Боковая панель: статус, часы, места, новая партия.
        var side = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MinWidth = 240,
            Margin = new Thickness(8, 0, 0, 0),
        };
        root.AddChild(side);

        _status = new Label { Text = string.Empty, Margin = new Thickness(0, 0, 0, 8) };
        _blackLabel = new Label { Text = string.Empty };
        _whiteLabel = new Label { Text = string.Empty };
        _sitBlack = new Button { Text = Loc.GetString("chess-sit-black") };
        _sitWhite = new Button { Text = Loc.GetString("chess-sit-white") };
        _sitBlack.OnPressed += _ => OnSit?.Invoke(ChessColor.Black);
        _sitWhite.OnPressed += _ => OnSit?.Invoke(ChessColor.White);

        _clockChoice = new OptionButton();
        _clockChoice.AddItem(Loc.GetString("chess-clock-none"), 0);
        _clockChoice.AddItem(Loc.GetString("chess-clock-blitz-3"), 180);
        _clockChoice.AddItem(Loc.GetString("chess-clock-blitz-5"), 300);
        _clockChoice.AddItem(Loc.GetString("chess-clock-rapid-10"), 600);
        _clockChoice.SelectId(300);
        _clockChoice.OnItemSelected += args => _clockChoice.SelectId(args.Id);

        _newGame = new Button { Text = Loc.GetString("chess-new-game"), Margin = new Thickness(0, 8, 0, 0) };
        _newGame.OnPressed += _ => OnNewGame?.Invoke(_clockChoice.SelectedId);

        side.AddChild(_status);
        side.AddChild(_blackLabel);
        side.AddChild(_sitBlack);
        side.AddChild(new Control { MinHeight = 12 });
        side.AddChild(_whiteLabel);
        side.AddChild(_sitWhite);
        side.AddChild(new Control { MinHeight = 16 });
        side.AddChild(new Label { Text = Loc.GetString("chess-clock-label") });
        side.AddChild(_clockChoice);
        side.AddChild(_newGame);
    }

    public void Populate(ChessBuiState state)
    {
        // Состояние прилетает раз в секунду ради часов. Сбрасывать выделение на каждом таком
        // обновлении нельзя — фигура «срывалась» бы из-под курсора; чистим только когда позиция
        // реально изменилась (кто-то сходил или началась новая партия).
        var positionChanged = _state?.Fen != state.Fen;
        _state = state;

        // Своя сторона: сравниваем сущность игрока, а не имя.
        var me = _player.LocalEntity is { } local
            ? IoCManager.Resolve<IEntityManager>().GetNetEntity(local)
            : (NetEntity?)null;
        var myColor = me != null && state.WhitePlayer == me ? ChessColor.White
            : me != null && state.BlackPlayer == me ? ChessColor.Black
            : (ChessColor?)null;
        _flipped = myColor == ChessColor.Black;

        if (positionChanged)
            _selected = -1;
        DrawBoard();

        _status.Text = BuildStatusText(state, myColor);
        _whiteLabel.Text = Loc.GetString("chess-side-white",
            ("name", string.IsNullOrEmpty(state.WhiteName) ? Loc.GetString("chess-seat-free") : state.WhiteName),
            ("clock", state.ClockEnabled ? FormatTime(state.WhiteTime) : "—"));
        _blackLabel.Text = Loc.GetString("chess-side-black",
            ("name", string.IsNullOrEmpty(state.BlackName) ? Loc.GetString("chess-seat-free") : state.BlackName),
            ("clock", state.ClockEnabled ? FormatTime(state.BlackTime) : "—"));

        // Своё место можно освободить (кнопка становится «Встать»), чужое — занять нельзя.
        _sitWhite.Disabled = !string.IsNullOrEmpty(state.WhiteName) && myColor != ChessColor.White;
        _sitBlack.Disabled = !string.IsNullOrEmpty(state.BlackName) && myColor != ChessColor.Black;
        _sitWhite.Text = Loc.GetString(myColor == ChessColor.White ? "chess-stand-up" : "chess-sit-white");
        _sitBlack.Text = Loc.GetString(myColor == ChessColor.Black ? "chess-stand-up" : "chess-sit-black");
    }

    private static string FormatTime(float seconds)
    {
        var s = Math.Max(0, (int)MathF.Ceiling(seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    private string BuildStatusText(ChessBuiState state, ChessColor? myColor)
    {
        if (state.Finished)
        {
            return state.Winner switch
            {
                ChessColor.White => Loc.GetString("chess-status-white-won"),
                ChessColor.Black => Loc.GetString("chess-status-black-won"),
                _ => Loc.GetString("chess-status-draw"),
            };
        }

        var turn = state.SideToMove == ChessColor.White
            ? Loc.GetString("chess-turn-white")
            : Loc.GetString("chess-turn-black");

        if (state.Status == ChessStatus.Check)
            turn += " " + Loc.GetString("chess-status-check");
        if (myColor == state.SideToMove)
            turn += " " + Loc.GetString("chess-your-turn");

        return turn;
    }

    /// <summary>Экранная клетка → индекс на доске (с учётом разворота за чёрных).</summary>
    private int CellToSquare(int cell)
    {
        var row = cell / Ranks;
        var col = cell % Ranks;
        return _flipped
            ? ChessSquare.Of(Ranks - 1 - col, row)
            : ChessSquare.Of(col, Ranks - 1 - row);
    }

    private int SquareToCell(int square)
    {
        var file = ChessSquare.File(square);
        var rank = ChessSquare.Rank(square);
        return _flipped
            ? rank * Ranks + (Ranks - 1 - file)
            : (Ranks - 1 - rank) * Ranks + file;
    }

    private void DrawBoard()
    {
        if (_state == null)
            return;

        var board = ParseFen(_state.Fen);
        var hints = _selected >= 0
            ? _state.LegalMoves.Where(m => m.From == _selected).Select(m => m.To).ToHashSet()
            : new HashSet<int>();

        for (var cell = 0; cell < 64; cell++)
        {
            var square = CellToSquare(cell);
            var row = cell / Ranks;
            var col = cell % Ranks;

            var color = (row + col) % 2 == 0 ? LightSquare : DarkSquare;
            if (square == _state.LastFrom || square == _state.LastTo)
                color = Color.InterpolateBetween(color, LastMoveTint, 0.55f);
            if (square == _selected)
                color = Color.InterpolateBetween(color, SelectedTint, 0.65f);

            ((StyleBoxFlat)_cells[cell].PanelOverride!).BackgroundColor = color;

            var piece = board[square];
            _pieces[cell].Texture = piece == null ? null : GetPieceTexture(piece.Value.Color, piece.Value.Type);
            _hints[cell].Visible = hints.Contains(square);
        }
    }

    private Texture? GetPieceTexture(ChessColor color, ChessPieceType type)
    {
        var name = type switch
        {
            ChessPieceType.Pawn => "pawn",
            ChessPieceType.Knight => "knight",
            ChessPieceType.Bishop => "bishop",
            ChessPieceType.Rook => "rook",
            ChessPieceType.Queen => "queen",
            ChessPieceType.King => "king",
            _ => null,
        };
        if (name == null)
            return null;

        var state = $"{(color == ChessColor.White ? "w" : "b")}_{name}";
        return _sprites.Frame0(new SpriteSpecifier.Rsi(
            new ResPath("/Textures/Objects/Fun/Tabletop/chess_pieces.rsi"), state));
    }

    private void OnCellClicked(int cell)
    {
        if (_state == null || _state.Finished)
            return;

        var square = CellToSquare(cell);

        // Второй клик по подсвеченной клетке — ход. Превращение всегда в ферзя: выбор из четырёх
        // на практике нужен раз в сто партий, а лишний диалог мешает каждый раз.
        // Ищем ход явно: раньше здесь сравнивалось с default, и при неудачном поиске на сервер
        // мог уйти пустой ход a1-a1.
        if (_selected >= 0)
        {
            var candidates = _state.LegalMoves.Where(m => m.From == _selected && m.To == square).ToList();
            if (candidates.Count > 0)
            {
                var move = candidates.FirstOrDefault(m => m.Promotion == ChessPieceType.Queen);
                if (move.From != _selected || move.To != square)
                    move = candidates[0]; // обычный ход без превращения

                OnMove?.Invoke(move);
                _selected = -1;
                DrawBoard();
                return;
            }
        }

        // Первый клик: выделяем свою фигуру, если у неё есть ходы.
        _selected = _state.LegalMoves.Any(m => m.From == square) ? square : -1;
        DrawBoard();
    }

    /// <summary>Разбор FEN в 64 клетки — клиенту хватает только расстановки.</summary>
    private static (ChessColor Color, ChessPieceType Type)?[] ParseFen(string fen)
    {
        var board = new (ChessColor, ChessPieceType)?[64];
        if (string.IsNullOrWhiteSpace(fen))
            return board;

        var rank = 7;
        var file = 0;
        foreach (var c in fen.Split(' ')[0])
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

            if (file is >= 0 and < 8 && rank is >= 0 and < 8)
                board[ChessSquare.Of(file, rank)] = (char.IsUpper(c) ? ChessColor.White : ChessColor.Black, type);
            file++;
        }

        return board;
    }
}
