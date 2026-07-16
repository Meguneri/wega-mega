using Content.Shared.Interaction;
using Content.Shared.Light.Components;
using Content.Shared.Popups;
using Robust.Shared.Map;

namespace Content.Server._Wega.SunlightSwitch;

public sealed partial class SunlightSwitchSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SunlightSwitchComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SunlightSwitchComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnMapInit(EntityUid uid, SunlightSwitchComponent comp, MapInitEvent args)
    {
        Apply(uid, comp);
    }

    private void OnActivate(EntityUid uid, SunlightSwitchComponent comp, ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        comp.Enabled = !comp.Enabled;
        Apply(uid, comp);
        _popup.PopupEntity(
            Loc.GetString(comp.Enabled ? "sunlight-switch-on" : "sunlight-switch-off"),
            uid, args.User);
        args.Handled = true;
    }

    private void Apply(EntityUid uid, SunlightSwitchComponent comp)
    {
        var xform = Transform(uid);
        if (xform.MapID == MapId.Nullspace)
            return;

        // Гриды по умолчанию получают ImplicitRoofComponent («полностью под крышей»),
        // из-за чего свет карты не рисуется над гридом. Снимаем неявную крышу
        // с грида, на котором стоит выключатель, чтобы «солнце» освещало арену.
        if (xform.GridUid is { } gridUid)
        {
            RemComp<ImplicitRoofComponent>(gridUid);
            EnsureComp<RoofComponent>(gridUid);
        }

        _map.SetAmbientLight(xform.MapID, comp.Enabled ? comp.DayColor : comp.NightColor);
    }
}
