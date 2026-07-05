using Content.Shared._Wega.Duel;
using Robust.Client.UserInterface;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._Wega.Duel;

public sealed class ArenaLoadoutBoundUserInterface : BoundUserInterface
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;

    [ViewVariables]
    private ArenaLoadoutWindow? _window;

    public ArenaLoadoutBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ArenaLoadoutWindow>();
        _window.OnLoadoutSelected += loadoutId => OnLoadoutSelected(loadoutId);
        _window.OnClose += Close;

        _window.OpenCentered();
    }

    private void OnLoadoutSelected(ProtoId<ArenaLoadoutPrototype> loadoutId)
    {
        var user = _playerManager.LocalSession?.AttachedEntity ?? EntityUid.Invalid;
        SendMessage(new ArenaLoadoutSelectedMessage(_entMan.GetNetEntity(user), loadoutId));
        Close();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ArenaLoadoutSelectionState cast)
            _window?.Populate(cast);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _window?.Dispose();
    }
}
