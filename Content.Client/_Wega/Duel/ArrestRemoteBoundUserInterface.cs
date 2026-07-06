using Content.Shared._Wega.Duel;
using Robust.Client.UserInterface;

namespace Content.Client._Wega.Duel;

public sealed class ArrestRemoteBoundUserInterface : BoundUserInterface
{
    private ArrestRemoteWindow? _window;

    public ArrestRemoteBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ArrestRemoteWindow>();
        _window.OnSelect += target => SendMessage(new ArrestRemoteSelectMessage(target));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ArrestRemoteBuiState cast)
            _window?.Populate(cast, EntMan);
    }
}
