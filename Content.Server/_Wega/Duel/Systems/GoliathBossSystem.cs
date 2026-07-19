using System.Numerics;
using Content.Server._Wega.Duel.Components;
using Content.Server.Chat.Systems;
using Content.Server.NPC.Components;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.Physics;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Robust.Server.Audio;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Боевая логика арена-босса «Голиаф» (DS3-стиль): телеграфированный чардж через арену
/// (в стену = стаггер с двойным уроном — окно наказания), телеграфированный АоЕ-слэм с
/// нокдауном, морозный след на фазе 2. Обычное преследование между атаками ведёт штатный
/// HTN; на время замахов/чарджа/стаггера ИИ выключается снятием ActiveNPCComponent.
/// </summary>
public sealed partial class GoliathBossSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private Robust.Server.GameObjects.TransformSystem _transform = default!;
    [Dependency] private Robust.Shared.Physics.Systems.SharedPhysicsSystem _physics = default!;
    [Dependency] private Content.Shared.Interaction.RotateToFaceSystem _rotateToFace = default!;
    [Dependency] private Content.Shared.Hands.EntitySystems.SharedHandsSystem _hands = default!;
    [Dependency] private Content.Server.NPC.Systems.NPCSteeringSystem _steering = default!;
    [Dependency] private Content.Shared.Height.HeightSystem _height = default!;
    [Dependency] private Robust.Shared.Prototypes.IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GoliathBossComponent, DamageModifyEvent>(OnDamageModify);
    }

    /// <summary>Стаггер-окно: оглушённый об стену Голиаф получает двойной урон.</summary>
    private void OnDamageModify(Entity<GoliathBossComponent> ent, ref DamageModifyEvent args)
    {
        if (ent.Comp.StaggeredUntil is { } until && _timing.CurTime < until)
            args.Damage *= ent.Comp.StaggerDamageMultiplier;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<GoliathBossComponent>();
        while (query.MoveNext(out var uid, out var goliath))
        {
            if (_mobState.IsIncapacitated(uid))
                continue;

            InitOnce(uid, goliath);
            SyncPhase(uid, goliath);

            // На замахах, в чардже и стаггере штатное движение выключено — гасим остаточную
            // скорость каждый тик, иначе босс «уезжал» с телеграфа за время замаха.
            if (goliath.State != GoliathState.Idle || goliath.StaggeredUntil != null)
                _physics.SetLinearVelocity(uid, Vector2.Zero);

            // Стаггер: стоит оглушённый, ИИ выключен — окно для урона.
            if (goliath.StaggeredUntil is { } staggered)
            {
                if (now < staggered)
                    continue;
                goliath.StaggeredUntil = null;
                EnsureComp<ActiveNPCComponent>(uid);
                _chat.TrySendInGameICMessage(uid, "с лязгом выпрямляется, восстанавливая равновесие",
                    InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
            }

            switch (goliath.State)
            {
                case GoliathState.Idle:
                    UpdateIdle(uid, goliath, now);
                    break;
                case GoliathState.SlamWindup:
                    if (now >= goliath.StateEndsAt)
                        ResolveSlam(uid, goliath, now);
                    break;
                case GoliathState.ChargeWindup:
                    if (now >= goliath.StateEndsAt)
                    {
                        goliath.State = GoliathState.Charging;
                        goliath.ChargeHit.Clear();
                        goliath.FrostAccumulator = 0f;
                    }
                    break;
                case GoliathState.Charging:
                    UpdateCharging(uid, goliath, now, frameTime);
                    break;
            }
        }
    }

    /// <summary>
    /// Разовая инициализация: рост всегда максимальный для вида — Голиаф обязан нависать.
    /// Делается в первом Update (после MapInit порядок применения случайной внешности уже не важен).
    /// </summary>
    private void InitOnce(EntityUid uid, GoliathBossComponent goliath)
    {
        if (goliath.SetupDone)
            return;
        goliath.SetupDone = true;

        if (TryComp<Content.Shared.Humanoid.HumanoidProfileComponent>(uid, out var humanoid)
            && _proto.TryIndex(humanoid.Species, out var species))
            _height.SetHeight(uid, species.MaxHeight);
    }

    /// <summary>Останавливает штатное движение: сбрасывает стиринг (иначе зажатые «клавиши» ввода
    /// продолжают везти босса) и гасит скорость. Вызывается при входе в замах/стаггер.</summary>
    private void FreezeMovement(EntityUid uid)
    {
        RemComp<ActiveNPCComponent>(uid);
        _steering.Unregister(uid);
        _physics.SetLinearVelocity(uid, Vector2.Zero);
    }

    /// <summary>Фаза 2: разовый эмоут + скейл молота (BossArenaSystem скейлит только природное меле).</summary>
    private void SyncPhase(EntityUid uid, GoliathBossComponent goliath)
    {
        if (!TryComp<BossArenaBossComponent>(uid, out var boss) || boss.CurrentPhase == goliath.LastPhase)
            return;

        goliath.LastPhase = boss.CurrentPhase;

        // Молот в руке: применяем фазовый множитель урона вручную.
        foreach (var held in _hands.EnumerateHeld((uid, null)))
        {
            if (!TryComp<MeleeWeaponComponent>(held, out var melee))
                continue;
            goliath.HammerBaseDamage ??= melee.Damage;
            var index = Math.Min(boss.CurrentPhase, boss.PhaseDamageMultipliers.Count - 1);
            if (index >= 0 && goliath.HammerBaseDamage != null)
                melee.Damage = goliath.HammerBaseDamage * boss.PhaseDamageMultipliers[index];
        }

        if (boss.CurrentPhase >= 1)
            _chat.TrySendInGameICMessage(uid, "рычит сервоприводами — из щелей брони валит морозный пар",
                InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
    }

    private float CooldownScale(GoliathBossComponent goliath)
        => goliath.LastPhase >= 1 ? goliath.Phase2CooldownMultiplier : 1f;

    private void UpdateIdle(EntityUid uid, GoliathBossComponent goliath, TimeSpan now)
    {
        if (FindTarget(uid) is not { } target)
            return;

        var myPos = _transform.GetWorldPosition(uid);
        var targetPos = _transform.GetWorldPosition(target);
        var dist = (targetPos - myPos).Length();

        // Слэм: цель в упор — телеграф-кольцо и удар по площади.
        if (dist <= goliath.SlamTriggerRange && now >= goliath.NextSlam)
        {
            FreezeMovement(uid);
            goliath.State = GoliathState.SlamWindup;
            goliath.StateEndsAt = now + TimeSpan.FromSeconds(goliath.SlamWindup);
            _audio.PlayPvs(goliath.TelegraphSound, uid);
            _chat.TrySendInGameICMessage(uid, "вздымает молот над головой", InGameICChatType.Emote,
                ChatTransmitRange.Normal, ignoreActionBlocker: true);

            // Телеграф: плитки в радиусе слэма.
            var r = (int)MathF.Ceiling(goliath.SlamRadius);
            for (var dx = -r; dx <= r; dx++)
            {
                for (var dy = -r; dy <= r; dy++)
                {
                    if (new Vector2(dx, dy).Length() > goliath.SlamRadius + 0.2f)
                        continue;
                    Spawn(goliath.WarningProto, Transform(uid).Coordinates.Offset(new Vector2(dx, dy)));
                }
            }
            return;
        }

        // Чардж: цель на дистанции — телеграф-линия до стены и рывок.
        if (dist >= goliath.ChargeMinTargetRange && now >= goliath.NextCharge)
        {
            var dir = Vector2.Normalize(targetPos - myPos);

            // Дальность: до стены (Impassable) либо максимум.
            var mapId = Transform(uid).MapID;
            var ray = new Robust.Shared.Physics.CollisionRay(myPos, dir, (int)CollisionGroup.Impassable);
            var planned = goliath.ChargeMaxDistance;
            foreach (var hit in _physics.IntersectRay(mapId, ray, goliath.ChargeMaxDistance, uid,
                         returnOnFirstHit: false))
            {
                if (!Transform(hit.HitEntity).Anchored || HasComp<MobStateComponent>(hit.HitEntity))
                    continue;
                planned = MathF.Min(planned, hit.Distance);
                break;
            }

            FreezeMovement(uid);
            goliath.State = GoliathState.ChargeWindup;
            goliath.StateEndsAt = now + TimeSpan.FromSeconds(goliath.ChargeWindup);
            goliath.ChargeDir = dir;
            goliath.ChargeRemaining = planned;
            _rotateToFace.TryFaceCoordinates(uid, targetPos);
            _audio.PlayPvs(goliath.WindupSound, uid);
            _chat.TrySendInGameICMessage(uid, "приседает — гидравлика брони взводится с воем",
                InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);

            // Телеграф: линия по пути чарджа (каждый тайл).
            for (var step = 1f; step <= planned; step += 1f)
                Spawn(goliath.WarningProto, Transform(uid).Coordinates.Offset(dir * step));
        }
    }

    private void ResolveSlam(EntityUid uid, GoliathBossComponent goliath, TimeSpan now)
    {
        goliath.State = GoliathState.Idle;
        goliath.NextSlam = now + TimeSpan.FromSeconds(goliath.SlamCooldown * CooldownScale(goliath));
        EnsureComp<ActiveNPCComponent>(uid);

        _audio.PlayPvs(goliath.SlamSound, uid);
        _chat.TrySendInGameICMessage(uid, "обрушивает молот — пол содрогается", InGameICChatType.Emote,
            ChatTransmitRange.Normal, ignoreActionBlocker: true);

        var myPos = _transform.GetWorldPosition(uid);
        var map = Transform(uid).MapID;
        var mobs = EntityQueryEnumerator<MobStateComponent>();
        while (mobs.MoveNext(out var mob, out _))
        {
            if (mob == uid || HasComp<GoliathBossComponent>(mob) || Transform(mob).MapID != map)
                continue;
            if ((_transform.GetWorldPosition(mob) - myPos).Length() > goliath.SlamRadius + 0.3f)
                continue;

            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", goliath.SlamDamage);
            _damageable.TryChangeDamage(mob, damage, origin: uid);
            _stun.TryAddParalyzeDuration(mob, TimeSpan.FromSeconds(goliath.SlamParalyze));
        }
    }

    private void UpdateCharging(EntityUid uid, GoliathBossComponent goliath, TimeSpan now, float frameTime)
    {
        var step = goliath.ChargeSpeed * frameTime;
        var xform = Transform(uid);
        var myPos = _transform.GetWorldPosition(xform);

        // Стена по курсу: три луча (центр и ±перпендикуляр — ловим углы), шаг не заходит в стену.
        var perp = new Vector2(-goliath.ChargeDir.Y, goliath.ChargeDir.X);
        var minDist = float.MaxValue;
        foreach (var side in new[] { 0f, -0.35f, 0.35f })
        {
            var origin = myPos + perp * side;
            var ray = new Robust.Shared.Physics.CollisionRay(origin, goliath.ChargeDir, (int)CollisionGroup.Impassable);
            foreach (var hit in _physics.IntersectRay(xform.MapID, ray, step + 0.8f, uid, returnOnFirstHit: false))
            {
                if (!Transform(hit.HitEntity).Anchored || HasComp<MobStateComponent>(hit.HitEntity))
                    continue;
                minDist = MathF.Min(minDist, hit.Distance);
                break;
            }
        }

        if (minDist <= step + 0.55f)
        {
            // Врезаемся: подъезжаем вплотную (не в стену!) и стаггеримся — окно наказания.
            var allowed = MathF.Max(0f, minDist - 0.55f);
            if (allowed > 0f)
                _transform.SetCoordinates(uid, xform.Coordinates.Offset(goliath.ChargeDir * allowed));

            goliath.State = GoliathState.Idle;
            goliath.NextCharge = now + TimeSpan.FromSeconds(goliath.ChargeCooldown * CooldownScale(goliath));
            goliath.StaggeredUntil = now + TimeSpan.FromSeconds(goliath.StaggerDuration);
            _physics.SetLinearVelocity(uid, Vector2.Zero);
            _audio.PlayPvs(goliath.WallSound, uid);
            _chat.TrySendInGameICMessage(uid,
                "с оглушительным лязгом врезается в стену и застывает, потеряв равновесие",
                InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
            return;
        }

        // Движение.
        _transform.SetCoordinates(uid, xform.Coordinates.Offset(goliath.ChargeDir * step));
        goliath.ChargeRemaining -= step;

        // Таран: урон + нокдаун всем на пути (один раз за рывок).
        var map = xform.MapID;
        var pos = _transform.GetWorldPosition(uid);
        var mobs = EntityQueryEnumerator<MobStateComponent>();
        while (mobs.MoveNext(out var mob, out _))
        {
            if (mob == uid || HasComp<GoliathBossComponent>(mob) || goliath.ChargeHit.Contains(mob)
                || Transform(mob).MapID != map)
                continue;
            if ((_transform.GetWorldPosition(mob) - pos).Length() > 1.2f)
                continue;

            goliath.ChargeHit.Add(mob);
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", goliath.ChargeDamage);
            _damageable.TryChangeDamage(mob, damage, origin: uid);
            _stun.TryAddParalyzeDuration(mob, TimeSpan.FromSeconds(goliath.ChargeParalyze));
        }

        // Морозный след (фаза 2): наледь позади, кайтить по своему следу не выйдет.
        if (goliath.LastPhase >= 1)
        {
            goliath.FrostAccumulator += step;
            while (goliath.FrostAccumulator >= 0.7f)
            {
                goliath.FrostAccumulator -= 0.7f;
                Spawn(goliath.FrostProto, xform.Coordinates);
            }
        }

        // Дистанция вышла — плавная остановка без стаггера.
        if (goliath.ChargeRemaining <= 0f)
        {
            goliath.State = GoliathState.Idle;
            goliath.NextCharge = now + TimeSpan.FromSeconds(goliath.ChargeCooldown * CooldownScale(goliath));
            EnsureComp<ActiveNPCComponent>(uid);
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
