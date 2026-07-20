using Content.Shared._Wega.Duel.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>Скрытое одноразовое «Право на реванш» вместо постоянного миньона.</summary>
public sealed partial class ArenaLoserMinionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private MovementModStatusSystem _movementMod = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;

    private static readonly TimeSpan BuffDuration = TimeSpan.FromSeconds(5);
    private static readonly EntProtoId SpeedEffect = "BasicSpeedUpStatusEffect";
    private static readonly SoundSpecifier ActivationSound =
        new SoundPathSpecifier("/Audio/_Wega/Duel/duel_ready.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArenaLoserMinionComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ArenaLoserMinionComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var revenge, out var damageable))
        {
            if (revenge.Used || !TryGetHealthFraction(uid, damageable, out var healthFraction))
                continue;

            if (healthFraction > 0.4f)
                continue;

            revenge.Used = true;
            revenge.ActiveUntil = now + BuffDuration;
            revenge.AddedIgnoreSlowOnDamage = !HasComp<IgnoreSlowOnDamageComponent>(uid);
            if (revenge.AddedIgnoreSlowOnDamage)
                EnsureComp<IgnoreSlowOnDamageComponent>(uid);
            _movementMod.TryUpdateMovementSpeedModDuration(
                uid, SpeedEffect, BuffDuration, 1.15f);
            _popup.PopupEntity(Loc.GetString("duel-arena-revenge-activated"), uid, PopupType.LargeCaution);
            _audio.PlayPvs(ActivationSound, uid);
        }

        var activeQuery = EntityQueryEnumerator<ArenaLoserMinionComponent>();
        while (activeQuery.MoveNext(out var uid, out var active))
        {
            if (active.ActiveUntil != TimeSpan.Zero && now >= active.ActiveUntil)
            {
                active.ActiveUntil = TimeSpan.Zero;
                RemoveOwnedSlowdownImmunity(uid, active);
            }
        }
    }

    /// <summary>Выдаёт скрытый заряд бойцу; отдельный дрон больше не спавнится.</summary>
    public void SpawnMinion(EntityUid owner, EntityCoordinates coords)
    {
        var revenge = EnsureComp<ArenaLoserMinionComponent>(owner);
        revenge.Used = false;
        revenge.ActiveUntil = TimeSpan.Zero;
        revenge.AddedIgnoreSlowOnDamage = false;
    }

    /// <summary>Снимает заряд после завершения дуэли.</summary>
    public void RemoveMinionsForOwners(ICollection<EntityUid> owners)
    {
        foreach (var owner in owners)
        {
            if (TryComp<ArenaLoserMinionComponent>(owner, out var revenge))
                RemoveOwnedSlowdownImmunity(owner, revenge);

            RemComp<ArenaLoserMinionComponent>(owner);
        }
    }

    private bool TryGetHealthFraction(EntityUid uid, DamageableComponent damageable, out float fraction)
    {
        fraction = 1f;
        if (!_thresholds.TryGetThresholdForState(uid, MobState.Critical, out var critical) ||
            critical.Value <= FixedPoint2.Zero)
            return false;

        var damage = _damageable.GetTotalDamage((uid, damageable));
        fraction = Math.Clamp(1f - damage.Float() / critical.Value.Float(), 0f, 1f);
        return true;
    }

    private void OnShutdown(EntityUid uid, ArenaLoserMinionComponent component, ref ComponentShutdown args)
    {
        RemoveOwnedSlowdownImmunity(uid, component);
    }

    private void RemoveOwnedSlowdownImmunity(EntityUid uid, ArenaLoserMinionComponent component)
    {
        _statusEffects.TryRemoveStatusEffect(uid, SpeedEffect);

        if (!component.AddedIgnoreSlowOnDamage)
            return;

        component.AddedIgnoreSlowOnDamage = false;
        RemComp<IgnoreSlowOnDamageComponent>(uid);
    }
}
