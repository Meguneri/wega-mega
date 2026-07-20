using System.Numerics;
using Content.Server._Wega.Duel.Components;
using Content.Server.Chat.Systems;
using Content.Shared.Administration.Systems;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Robust.Server.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Боевая логика арена-босса «Пепельная танцовщица» (DS3-стиль): двойные телеграфированные
/// вращения (внутреннее кольцо → внешнее — обман таймингов), телепорт за спину кайтящей цели,
/// усталость после трёх серий (стаггер-окно ×2 урона) и второе дыхание — упав в крит, встаёт
/// с 40% ХП, быстрее и злее, вращения оставляют тлеющие сектора. Второе дыхание триггерится
/// на КРИТЕ (до смерти) — BossArenaSystem завершает бой только по IsDead, так что арена
/// продолжается штатно. Замахи/колени — с выключенным ИИ (паттерн Голиафа).
/// </summary>
public sealed partial class DancerBossSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private RejuvenateSystem _rejuvenate = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private Robust.Server.GameObjects.TransformSystem _transform = default!;
    [Dependency] private Robust.Shared.Physics.Systems.SharedPhysicsSystem _physics = default!;
    [Dependency] private Content.Shared.Interaction.RotateToFaceSystem _rotateToFace = default!;
    [Dependency] private Content.Shared.Hands.EntitySystems.SharedHandsSystem _hands = default!;
    [Dependency] private Content.Server.NPC.Systems.NPCSteeringSystem _steering = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DancerBossComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<DancerBossComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    /// <summary>На коленях — неуязвима (пауза для всех); в усталости — двойной урон.</summary>
    private void OnDamageModify(Entity<DancerBossComponent> ent, ref DamageModifyEvent args)
    {
        if (ent.Comp.State == DancerState.Kneeling)
            args.Damage *= 0f;
        else if (ent.Comp.ExhaustedUntil is { } until && _timing.CurTime < until)
            args.Damage *= ent.Comp.ExhaustDamageMultiplier;
    }

    /// <summary>Второе дыхание: упала в крит — встаёт (один раз за бой).</summary>
    private void OnMobStateChanged(Entity<DancerBossComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Critical || ent.Comp.SecondLifeUsed)
            return;

        var (uid, dancer) = ent;
        dancer.SecondLifeUsed = true;

        // Полный откат из крита, затем урон до (1 - доля) от порога смерти → остаётся 40% ХП.
        _rejuvenate.PerformRejuvenate(uid);
        if (_thresholds.TryGetThresholdForState(uid, MobState.Dead, out var dead))
        {
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Slash", dead.Value.Double() * (1f - dancer.SecondLifeHealthFraction));
            _damageable.TryChangeDamage(uid, damage, ignoreResistances: true);
        }

        FreezeMovement(uid);
        dancer.State = DancerState.Kneeling;
        dancer.StateEndsAt = _timing.CurTime + TimeSpan.FromSeconds(dancer.KneelDuration);
        dancer.ExhaustedUntil = null;
        dancer.ComboCount = 0;
        _chat.TrySendInGameICMessage(uid,
            "падает на колени, опираясь на клинок, — пепел медленно кружит вокруг неё",
            InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<DancerBossComponent>();
        while (query.MoveNext(out var uid, out var dancer))
        {
            // На коленях IsIncapacitated уже false (Rejuvenate) — но проверка нужна для смерти.
            if (_mobState.IsIncapacitated(uid))
                continue;

            // На замахах/коленях/в усталости штатное движение выключено — гасим скорость.
            if (dancer.State != DancerState.Idle || dancer.ExhaustedUntil != null)
                _physics.SetLinearVelocity(uid, Vector2.Zero);

            // Усталость: стоит, тяжело дыша — окно для урона.
            if (dancer.ExhaustedUntil is { } exhausted)
            {
                if (now < exhausted)
                    continue;
                dancer.ExhaustedUntil = null;
                EnsureComp<ActiveNPCComponent>(uid);
                _chat.TrySendInGameICMessage(uid, "выпрямляется одним текучим движением",
                    InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
            }

            switch (dancer.State)
            {
                case DancerState.Idle:
                    UpdateIdle(uid, dancer, now);
                    break;
                case DancerState.SpinInner:
                    if (now >= dancer.StateEndsAt)
                        ResolveInnerSpin(uid, dancer, now);
                    break;
                case DancerState.SpinOuter:
                    if (now >= dancer.StateEndsAt)
                        ResolveOuterSpin(uid, dancer, now);
                    break;
                case DancerState.Kneeling:
                    if (now >= dancer.StateEndsAt)
                        Rise(uid, dancer);
                    break;
            }
        }
    }

    private void FreezeMovement(EntityUid uid)
    {
        RemComp<ActiveNPCComponent>(uid);
        _steering.Unregister(uid);
        _physics.SetLinearVelocity(uid, Vector2.Zero);
    }

    private float CooldownScale(DancerBossComponent dancer)
        => dancer.SecondLife ? dancer.SecondLifeCooldownMultiplier : 1f;

    private void UpdateIdle(EntityUid uid, DancerBossComponent dancer, TimeSpan now)
    {
        if (FindTarget(uid) is not { } target)
            return;

        var myPos = _transform.GetWorldPosition(uid);
        var targetPos = _transform.GetWorldPosition(target);
        var dist = (targetPos - myPos).Length();

        // Телепорт за спину кайтящей цели: вспышки пепла в обеих точках, потом сразу танец.
        if (dist >= dancer.TeleportRange && now >= dancer.NextTeleport)
        {
            dancer.NextTeleport = now + TimeSpan.FromSeconds(dancer.TeleportCooldown * CooldownScale(dancer));

            Spawn(dancer.AshProto, Transform(uid).Coordinates);
            var behind = targetPos + Vector2.Normalize(targetPos - myPos) * 1.2f;
            _transform.SetWorldPosition(uid, behind);
            Spawn(dancer.AshProto, Transform(uid).Coordinates);
            _audio.PlayPvs(dancer.TeleportSound, uid);
            _rotateToFace.TryFaceCoordinates(uid, targetPos);
            _chat.TrySendInGameICMessage(uid, "рассыпается пеплом и возникает за спиной",
                InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);

            // Появление из пепла сразу переходит в танец — если серия не на кулдауне.
            if (now >= dancer.NextSpin)
                StartSpinSequence(uid, dancer, now);
            return;
        }

        if (dist <= dancer.SpinTriggerRange && now >= dancer.NextSpin)
            StartSpinSequence(uid, dancer, now);
    }

    private void StartSpinSequence(EntityUid uid, DancerBossComponent dancer, TimeSpan now)
    {
        FreezeMovement(uid);
        dancer.State = DancerState.SpinInner;
        dancer.StateEndsAt = now + TimeSpan.FromSeconds(dancer.SpinWindup);
        _chat.TrySendInGameICMessage(uid, "заносит клинки, начиная смертельный танец",
            InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
        TelegraphRing(uid, dancer.InnerWarningProto, 0f, dancer.InnerRadius);
    }

    private void ResolveInnerSpin(EntityUid uid, DancerBossComponent dancer, TimeSpan now)
    {
        _audio.PlayPvs(dancer.SpinSound, uid);
        Spawn(dancer.InnerSpinProto, Transform(uid).Coordinates);
        DamageRing(uid, dancer, 0f, dancer.InnerRadius + 0.3f, dancer.InnerDamage, paralyze: 0f);

        // Сразу второй замах — внешнее кольцо: «увернулся от первого — не расслабляйся».
        dancer.State = DancerState.SpinOuter;
        dancer.StateEndsAt = now + TimeSpan.FromSeconds(dancer.SpinWindup);
        TelegraphRing(uid, dancer.OuterWarningProto, dancer.InnerRadius - 0.4f, dancer.OuterRadius);
    }

    private void ResolveOuterSpin(EntityUid uid, DancerBossComponent dancer, TimeSpan now)
    {
        _audio.PlayPvs(dancer.SpinSound, uid);
        Spawn(dancer.OuterSpinProto, Transform(uid).Coordinates);
        DamageRing(uid, dancer, dancer.InnerRadius - 0.6f, dancer.OuterRadius + 0.3f,
            dancer.OuterDamage, dancer.OuterParalyze);

        // Вторая жизнь: после вращения пол остаётся тлеть.
        if (dancer.SecondLife)
        {
            var r = (int)MathF.Ceiling(dancer.OuterRadius);
            for (var dx = -r; dx <= r; dx++)
            {
                for (var dy = -r; dy <= r; dy++)
                {
                    var len = new Vector2(dx, dy).Length();
                    if (len > dancer.OuterRadius || (dx + dy) % 2 != 0)
                        continue;
                    Spawn(dancer.EmberProto, Transform(uid).Coordinates.Offset(new Vector2(dx, dy)));
                }
            }
        }

        dancer.State = DancerState.Idle;
        dancer.ComboCount++;
        dancer.NextSpin = now + TimeSpan.FromSeconds(dancer.SpinCooldown * CooldownScale(dancer));

        // После трёх серий — выдохлась: стаггер-окно.
        if (dancer.ComboCount >= dancer.CombosUntilExhausted)
        {
            dancer.ComboCount = 0;
            dancer.ExhaustedUntil = now + TimeSpan.FromSeconds(dancer.ExhaustDuration);
            _chat.TrySendInGameICMessage(uid, "тяжело опирается на клинки, переводя дыхание",
                InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
            return;
        }

        EnsureComp<ActiveNPCComponent>(uid);
    }

    /// <summary>Подъём после второго дыхания: 40% ХП, злее и быстрее.</summary>
    private void Rise(EntityUid uid, DancerBossComponent dancer)
    {
        dancer.State = DancerState.Idle;
        dancer.SecondLife = true;
        EnsureComp<ActiveNPCComponent>(uid);
        Spawn(dancer.RiseProto, Transform(uid).Coordinates);
        _audio.PlayPvs(dancer.RiseSound, uid);
        _chat.TrySendInGameICMessage(uid,
            "поднимается — из-под капюшона вспыхивают угли, клинки раскаляются добела",
            InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);

        // Клинки злее.
        foreach (var held in _hands.EnumerateHeld((uid, null)))
        {
            if (TryComp<MeleeWeaponComponent>(held, out var melee))
                melee.Damage *= dancer.SecondLifeDamageMultiplier;
        }
    }

    /// <summary>Телеграф-плитки кольца [inner..outer] вокруг босса.</summary>
    private void TelegraphRing(EntityUid uid, EntProtoId warningProto, float inner, float outer)
    {
        var r = (int)MathF.Ceiling(outer);
        for (var dx = -r; dx <= r; dx++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                var len = new Vector2(dx, dy).Length();
                if (len > outer + 0.2f || len < inner - 0.2f)
                    continue;
                Spawn(warningProto, Transform(uid).Coordinates.Offset(new Vector2(dx, dy)));
            }
        }
    }

    /// <summary>Урон по кольцу [inner..outer] вокруг босса.</summary>
    private void DamageRing(EntityUid uid, DancerBossComponent dancer, float inner, float outer,
        float amount, float paralyze)
    {
        var myPos = _transform.GetWorldPosition(uid);
        var map = Transform(uid).MapID;
        var mobs = EntityQueryEnumerator<MobStateComponent>();
        while (mobs.MoveNext(out var mob, out _))
        {
            if (mob == uid || HasComp<DancerBossComponent>(mob) || Transform(mob).MapID != map)
                continue;
            var dist = (_transform.GetWorldPosition(mob) - myPos).Length();
            if (dist > outer || dist < inner)
                continue;

            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Slash", amount);
            _damageable.TryChangeDamage(mob, damage, origin: uid);
            if (paralyze > 0f)
                _stun.TryAddParalyzeDuration(mob, TimeSpan.FromSeconds(paralyze));
        }
    }

    /// <summary>Ближайший живой игрок на карте (в разумном радиусе).</summary>
    private EntityUid? FindTarget(EntityUid uid)
    {
        var map = Transform(uid).MapID;
        var myPos = _transform.GetWorldPosition(uid);

        EntityUid? nearest = null;
        var nearestDist = 20f;
        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent>();
        while (query.MoveNext(out var mob, out _, out _))
        {
            if (Transform(mob).MapID != map || _mobState.IsIncapacitated(mob))
                continue;
            var dist = (_transform.GetWorldPosition(mob) - myPos).Length();
            if (dist < nearestDist)
            {
                nearest = mob;
                nearestDist = dist;
            }
        }
        return nearest;
    }
}
