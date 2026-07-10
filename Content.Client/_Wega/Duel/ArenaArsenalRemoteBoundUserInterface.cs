using Content.Shared._Wega.Duel;
using Robust.Client.UserInterface;

namespace Content.Client._Wega.Duel;

public sealed class ArenaArsenalRemoteBoundUserInterface : BoundUserInterface
{
    private ArenaArsenalRemoteWindow? _window;

    public ArenaArsenalRemoteBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ArenaArsenalRemoteWindow>();
        _window.OnSelect += crate => SendMessage(new ArenaArsenalSelectMessage(crate));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ArenaArsenalRemoteBuiState cast)
            _window?.Populate(cast);
    }
}
