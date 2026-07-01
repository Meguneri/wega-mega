using Content.Shared._Wega.Magic.LoveCurse;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._Wega.Magic.LoveCurse;

public sealed partial class LoveCurseUIController : UIController
{
    [Dependency] private IUserInterfaceManager _uiManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    private LoveCursePanel? _panel;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<LoveCurseMenuOpenedEvent>(OnMenuOpened);
    }

    private void OnMenuOpened(LoveCurseMenuOpenedEvent args, EntitySessionEventArgs session)
    {
        var casterEntity = _entityManager.GetEntity(args.Caster);
        if (_playerManager.LocalSession?.AttachedEntity != casterEntity)
            return;

        if (_panel == null)
        {
            _panel = _uiManager.CreateWindow<LoveCursePanel>();
            _panel.OnClose += () => _panel = null;
        }

        _panel.Populate(args.Caster);
        _panel.OpenCentered();
    }
}
