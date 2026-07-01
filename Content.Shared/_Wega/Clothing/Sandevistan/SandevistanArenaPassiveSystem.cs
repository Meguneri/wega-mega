using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Random;

namespace Content.Shared._Wega.Clothing.Sandevistan;

/// <summary>
/// Applies the arena Sandevistan implant's passive perks while
/// <see cref="SandevistanArenaPassiveComponent"/> is on a mob: a standing incoming-damage reduction,
/// a flat dodge chance and a small permanent speed boost. Lets the implant grant on the mob what the
/// worn eyewear grants through clothing slots.
/// </summary>
public sealed partial class SandevistanArenaPassiveSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SandevistanArenaPassiveComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SandevistanArenaPassiveComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SandevistanArenaPassiveComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<SandevistanArenaPassiveComponent, DamageModifyEvent>(OnDamageModify);
    }

    // Recompute speed when the perk attaches/detaches (implant inserted/extracted) so the passive
    // boost is applied and later cleared.
    private void OnStartup(Entity<SandevistanArenaPassiveComponent> ent, ref ComponentStartup args)
    {
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnShutdown(Entity<SandevistanArenaPassiveComponent> ent, ref ComponentShutdown args)
    {
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnRefreshSpeed(Entity<SandevistanArenaPassiveComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.SpeedModifier, ent.Comp.SpeedModifier);
    }

    // Mirrors the active Sandevistan's damage handling, but always-on: roll a dodge (decided on the
    // server so the negated damage replicates without client flicker), otherwise apply the standing
    // reduction. Only reacts to actual incoming attacks, never healing or environmental damage.
    private void OnDamageModify(Entity<SandevistanArenaPassiveComponent> ent, ref DamageModifyEvent args)
    {
        if (args.Origin == null || args.Damage.GetTotal() <= 0)
            return;

        if (ent.Comp.DodgeChance > 0f && _net.IsServer && _random.Prob(ent.Comp.DodgeChance))
        {
            args.Damage = new DamageSpecifier();
            _popup.PopupEntity(Loc.GetString("evasion-dodged"), ent.Owner, ent.Owner, PopupType.SmallCaution);
            return;
        }

        if (ent.Comp.DamageCoefficient < 1f)
            args.Damage *= ent.Comp.DamageCoefficient;
    }
}
