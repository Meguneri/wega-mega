using Content.Shared._Wega.Duel;
using Robust.Client.UserInterface;

namespace Content.Client._Wega.Duel;

public sealed class ArenaEntryBoundUserInterface : BoundUserInterface
{
    private ArenaEntryWindow? _window;

    public ArenaEntryBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ArenaEntryWindow>();
        _window.OnConfirm += crate => SendMessage(new ArenaEntryConfirmMessage(crate));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ArenaArsenalRemoteBuiState cast)
            _window?.Populate(cast);
    }
}
