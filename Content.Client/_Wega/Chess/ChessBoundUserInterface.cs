using Content.Shared._Wega.Chess;
using Robust.Client.UserInterface;

namespace Content.Client._Wega.Chess;

public sealed class ChessBoundUserInterface : BoundUserInterface
{
    private ChessWindow? _window;

    public ChessBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ChessWindow>();
        _window.OnMove += move => SendMessage(new ChessMoveMessage(move));
        _window.OnSit += color => SendMessage(new ChessSitMessage(color));
        _window.OnNewGame += seconds => SendMessage(new ChessNewGameMessage(seconds));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ChessBuiState cast)
            _window?.Populate(cast);
    }
}
