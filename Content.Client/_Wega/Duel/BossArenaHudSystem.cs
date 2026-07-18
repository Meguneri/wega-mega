using Content.Shared._Wega.Duel;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;

namespace Content.Client._Wega.Duel;

/// <summary>
/// Клиентское состояние HUD-полоски ХП босса: принимает <see cref="BossArenaHudEvent"/> от сервера
/// и держит оверлей <see cref="BossArenaHudOverlay"/>, рисующий полоску сверху экрана, пока идёт бой.
/// </summary>
public sealed partial class BossArenaHudSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;

    private BossArenaHudOverlay? _overlay;

    public bool HudActive;
    public string BossName = string.Empty;
    public float HealthRatio = 1f;
    public int Phase;
    public bool Enraged;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<BossArenaHudEvent>(OnHudState);

        _overlay = new BossArenaHudOverlay(this);
        _overlayManager.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        if (_overlay != null)
            _overlayManager.RemoveOverlay(_overlay);
    }

    private void OnHudState(BossArenaHudEvent ev)
    {
        HudActive = ev.Active;
        if (!ev.Active)
            return;

        BossName = ev.BossName;
        HealthRatio = ev.HealthRatio;
        Phase = ev.Phase;
        Enraged = ev.Enraged;
    }
}
