using Content.Shared.Blocking;
using Content.Shared.Blocking.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._Wega.Weapons.Parry;

/// <summary>
/// Handles priming a riposte when the holder parries a melee attack with an
/// actively raised blocking item, and paying it out on the next melee hit.
/// </summary>
public sealed partial class ParryRiposteSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BlockingUserComponent, AttackedEvent>(OnBlockerAttacked);
        SubscribeLocalEvent<ParryRiposteComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnBlockerAttacked(EntityUid uid, BlockingUserComponent component, AttackedEvent args)
    {
        if (component.BlockingItem is not { } item
            || !TryComp<ParryRiposteComponent>(item, out var parry))
        {
            return;
        }

        // Can't parry yourself, and shoving with bare hands shouldn't prime it.
        if (args.User == uid || args.Used == item)
            return;

        parry.PrimedUntil = _timing.CurTime + parry.RiposteWindow;
        Dirty(item, parry);

        _audio.PlayPredicted(parry.ParrySound, uid, args.User);
        _popup.PopupPredicted(Loc.GetString("parry-riposte-primed", ("item", item)), uid, uid);
    }

    private void OnMeleeHit(EntityUid uid, ParryRiposteComponent component, MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        if (component.PrimedUntil is not { } until || until < _timing.CurTime)
            return;

        component.PrimedUntil = null;
        Dirty(uid, component);

        args.BonusDamage += component.RiposteBonusDamage;

        foreach (var target in args.HitEntities)
        {
            _stamina.TakeStaminaDamage(target, component.RiposteStaminaDamage, source: args.User, with: uid);
        }

        _popup.PopupPredicted(Loc.GetString("parry-riposte-strike", ("item", uid)), args.User, args.User);
    }
}
