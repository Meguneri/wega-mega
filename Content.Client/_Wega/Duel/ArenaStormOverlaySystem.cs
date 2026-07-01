using Robust.Client.Graphics;

namespace Content.Client._Wega.Duel;

/// <summary>
/// Держит <see cref="ArenaStormOverlay"/> зарегистрированным. Оверлей сам гейтится по активным
/// штормам на карте игрока, поэтому системе достаточно добавить/убрать его.
/// </summary>
public sealed partial class ArenaStormOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayMan = default!;

    private ArenaStormOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new();
        _overlayMan.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayMan.RemoveOverlay(_overlay);
    }
}
