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
using Content.Shared.SSDIndicator;
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
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private Content.Shared.Humanoid.HumanoidProfileSystem _humanoidProfile = default!;
    [Dependency] private Content.Shared.Body.SharedVisualBodySystem _visualBody = default!;
    [Dependency] private Robust.Shared.Map.ITileDefinitionManager _tileDefs = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GoliathBossComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<GoliathBossComponent, Content.Shared.Interaction.Events.AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<GoliathBossComponent, MapInitEvent>(OnMapInit);
        // Босса могут удалить, не убив (админский деспавн, уборка арены, конец раунда) — тогда
        // Update до восстановления пола уже не дойдёт. Возвращаем плитки и на удалении.
        SubscribeLocalEvent<GoliathBossComponent, ComponentShutdown>((ent, comp, _) => RestoreTiles(ent, comp));
    }

    private void OnMapInit(Entity<GoliathBossComponent> ent, ref MapInitEvent args)
    {
        // Это NPC: SSD-индикатор игрока не должен показывать «Zzz» во время выключения ИИ на кастах.
        RemComp<SSDIndicatorComponent>(ent);
    }

    /// <summary>
    /// Пока Голиаф скован своим замахом (или стаггером), он не машет молотом: телеграф обязан быть
    /// честным. Раньше для этого вешался настоящий стан — но любой стан рассылает
    /// DropHandItemsEvent, и босс ронял молот перед каждой атакой. Блокируем ровно удар.
    /// </summary>
    private void OnAttackAttempt(Entity<GoliathBossComponent> ent,
        ref Content.Shared.Interaction.Events.AttackAttemptEvent args)
    {
        if (ent.Comp.State != GoliathState.Idle || ent.Comp.StaggeredUntil != null)
            args.Cancel();
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
            {
                // Босс лёг — возвращаем сорванный слэмами пол на место.
                RestoreTiles(uid, goliath);
                continue;
            }

            InitOnce(uid, goliath);
            SyncPhase(uid, goliath);

            // На замахах, в чардже и стаггере штатное движение выключено — гасим остаточную
            // скорость каждый тик, иначе босс «уезжал» с телеграфа за время замаха.
            if (goliath.State != GoliathState.Idle || goliath.StaggeredUntil != null)
                _physics.SetLinearVelocity(uid, Vector2.Zero);

            // Стаггер: стоит оглушённый — окно для урона.
            if (goliath.StaggeredUntil is { } staggered)
            {
                if (now < staggered)
                {
                    // Окно ФИКСИРОВАННОЕ и не прерывается: удары гасит OnAttackAttempt, движение —
                    // снятый ActiveNPC плюс обнуление скорости выше. Снятия ActiveNPCComponent
                    // одного было мало — NPCOptimizationSystem будит NPC, рядом с которым игрок
                    // или который получил урон, то есть ровно в стаггер-окне.
                    RemComp<ActiveNPCComponent>(uid);
                    continue;
                }

                goliath.StaggeredUntil = null;
                EnsureComp<ActiveNPCComponent>(uid);
                _chat.TrySendInGameICMessage(uid, "с лязгом выпрямляется, восстанавливая равновесие",
                    InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
            }

            // В «занятом» состоянии босс скован замахом: удары режет OnAttackAttempt, а ходьбу —
            // снятый ActiveNPC (его постоянно возвращает NPCOptimizationSystem, поэтому снимаем
            // каждый тик) и обнуление скорости выше.
            if (goliath.State != GoliathState.Idle)
                RemComp<ActiveNPCComponent>(uid);

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
                case GoliathState.ShockWindup:
                    if (now >= goliath.StateEndsAt)
                        StartShockwave(uid, goliath);
                    break;
                case GoliathState.Shockwave:
                    UpdateShockwave(uid, goliath, now, frameTime);
                    break;
            }
        }
    }

    /// <summary>
    /// Разовая инициализация: Голиаф всегда мужчина максимального для вида роста — босс обязан
    /// выглядеть одинаково и нависать, а не выпадать случайной субтильной фигурой.
    /// Делается в первом Update — после того, как отработала случайная внешность.
    /// </summary>
    private void InitOnce(EntityUid uid, GoliathBossComponent goliath)
    {
        if (goliath.SetupDone)
            return;
        goliath.SetupDone = true;

        if (!TryComp<Content.Shared.Humanoid.HumanoidProfileComponent>(uid, out var humanoid))
            return;

        var profile = Content.Shared.Preferences.HumanoidCharacterProfile
            .RandomWithSpecies(humanoid.Species)
            .WithSex(Content.Shared.Humanoid.Sex.Male)
            .WithGender(Robust.Shared.Enums.Gender.Male);

        _visualBody.ApplyProfileTo(uid, profile);
        _humanoidProfile.ApplyProfileTo(uid, profile);
        // Имя не трогаем: оно из прототипа (ent-ключ), а не из случайного профиля.

        if (_proto.TryIndex(humanoid.Species, out var species))
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

        // Ударная волна: средняя/дальняя дистанция, босс не двигается — гасит «беготню по кругу».
        if (dist >= goliath.ShockMinTargetRange && now >= goliath.NextShock)
        {
            var dir = Vector2.Normalize(targetPos - myPos);

            FreezeMovement(uid);
            goliath.State = GoliathState.ShockWindup;
            goliath.StateEndsAt = now + TimeSpan.FromSeconds(goliath.ShockWindup);
            _rotateToFace.TryFaceCoordinates(uid, targetPos);
            _audio.PlayPvs(goliath.WindupSound, uid);
            _chat.TrySendInGameICMessage(uid,
                "заносит молот вбок — под ногами с хрустом идут трещины",
                InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);

            // Веер лучей: на фазе 2 их три — уклоняться вбок больше не бесплатно.
            goliath.ShockDirs.Clear();
            goliath.ShockDirs.Add(dir);
            if (goliath.LastPhase >= 1)
            {
                var rad = MathF.PI / 180f * goliath.ShockFanAngle;
                goliath.ShockDirs.Add(Rotate(dir, rad));
                goliath.ShockDirs.Add(Rotate(dir, -rad));
            }

            // Телеграф: каждый луч на всю длину.
            foreach (var d in goliath.ShockDirs)
            {
                for (var step = 1f; step <= goliath.ShockRange; step += 1f)
                    Spawn(goliath.WarningProto, Transform(uid).Coordinates.Offset(d * step));
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
        _chat.TrySendInGameICMessage(uid, "обрушивает молот — пол вздыбливается, плиты летят в стороны",
            InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);

        var myPos = _transform.GetWorldPosition(uid);
        var map = Transform(uid).MapID;

        // Пыль по всей площади удара + сорванные плиты в эпицентре.
        var r = (int)MathF.Ceiling(goliath.SlamRadius);
        for (var dx = -r; dx <= r; dx++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                var offset = new Vector2(dx, dy);
                var dist = offset.Length();
                if (dist > goliath.SlamRadius + 0.2f)
                    continue;

                Spawn(goliath.DustProto, Transform(uid).Coordinates.Offset(offset));
                if (dist <= goliath.SlamRipRadius)
                    RipTile(uid, goliath, Transform(uid).Coordinates.Offset(offset));
            }
        }

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

    /// <summary>
    /// Срывает плитку пола: тайл заменяется своей подложкой (обычно плитинг — дыры в космос не
    /// будет) и запоминается, чтобы вернуться на место после боя. Предметы-плитки не спавним:
    /// пол должен выглядеть разбитым, а не завалить арену мусором.
    /// </summary>
    private void RipTile(EntityUid uid, GoliathBossComponent goliath, Robust.Shared.Map.EntityCoordinates coords)
    {
        var grid = _transform.GetGrid(coords);
        if (grid is not { } gridUid || !TryComp<Robust.Shared.Map.Components.MapGridComponent>(gridUid, out var gridComp))
            return;

        var indices = _map.TileIndicesFor(gridUid, gridComp, coords);
        var tileRef = _map.GetTileRef(gridUid, gridComp, indices);
        if (tileRef.Tile.IsEmpty)
            return;

        if (_tileDefs[tileRef.Tile.TypeId] is not Content.Shared.Maps.ContentTileDefinition def
            || def.BaseTurf is not { } baseTurf
            || !def.CanCrowbar) // непробиваемое (плитинг арены, спец-покрытия) не трогаем
            return;

        if (_tileDefs[baseTurf] is not Content.Shared.Maps.ContentTileDefinition baseDef)
            return;

        goliath.RippedTiles.Add((gridUid, indices, tileRef.Tile));
        _map.SetTile(gridUid, gridComp, indices, new Robust.Shared.Map.Tile(baseDef.TileId));
        Spawn(goliath.RubbleProto, _map.GridTileToLocal(gridUid, gridComp, indices));
    }

    /// <summary>Возвращает сорванные плитки: арена не должна оставаться дырявой после боя.</summary>
    private void RestoreTiles(EntityUid uid, GoliathBossComponent goliath)
    {
        if (goliath.TilesRestored || goliath.RippedTiles.Count == 0)
            return;

        goliath.TilesRestored = true;
        foreach (var (gridUid, indices, old) in goliath.RippedTiles)
        {
            if (TryComp<Robust.Shared.Map.Components.MapGridComponent>(gridUid, out var gridComp))
                _map.SetTile(gridUid, gridComp, indices, old);
        }
        goliath.RippedTiles.Clear();
    }

    /// <summary>Поворот вектора на угол (радианы) — для веера ударных волн.</summary>
    private static Vector2 Rotate(Vector2 v, float radians)
    {
        var (sin, cos) = MathF.SinCos(radians);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    /// <summary>Замах кончился — волна пошла по полу.</summary>
    private void StartShockwave(EntityUid uid, GoliathBossComponent goliath)
    {
        goliath.State = GoliathState.Shockwave;
        goliath.ShockTravelled = 0f;
        goliath.ShockHit.Clear();

        _audio.PlayPvs(goliath.SlamSound, uid);
        _chat.TrySendInGameICMessage(uid, "бьёт молотом оземь — по полу расходится ударная волна",
            InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
    }

    /// <summary>
    /// Фронт волны ползёт от босса по каждому лучу: на своём пути ломает пол, поднимает пыль и
    /// сбивает с ног. От неё уходят вбок (или за спину боссу) — но на фазе 2 лучей три.
    /// </summary>
    private void UpdateShockwave(EntityUid uid, GoliathBossComponent goliath, TimeSpan now, float frameTime)
    {
        var prev = goliath.ShockTravelled;
        goliath.ShockTravelled += goliath.ShockSpeed * frameTime;

        var xform = Transform(uid);
        var origin = _transform.GetWorldPosition(xform);
        var map = xform.MapID;

        // Визуал: фронт на каждом пройденном за тик тайле каждого луча.
        for (var t = MathF.Ceiling(prev); t <= MathF.Min(goliath.ShockTravelled, goliath.ShockRange); t += 1f)
        {
            foreach (var dir in goliath.ShockDirs)
            {
                var coords = xform.Coordinates.Offset(dir * t);
                Spawn(goliath.ShockProto, coords);
                if (t <= goliath.ShockRange * 0.6f)
                    RipTile(uid, goliath, coords);
            }
        }

        // Урон: кто оказался под фронтом (проекция на луч в пределах пройденного, отклонение — в
        // пределах полуширины). Каждого задеваем один раз за волну.
        var mobs = EntityQueryEnumerator<MobStateComponent>();
        while (mobs.MoveNext(out var mob, out _))
        {
            if (mob == uid || HasComp<GoliathBossComponent>(mob) || goliath.ShockHit.Contains(mob)
                || Transform(mob).MapID != map)
                continue;

            var rel = _transform.GetWorldPosition(mob) - origin;
            foreach (var dir in goliath.ShockDirs)
            {
                var along = Vector2.Dot(rel, dir);
                if (along < prev || along > goliath.ShockTravelled || along > goliath.ShockRange)
                    continue;
                if (MathF.Abs(rel.X * -dir.Y + rel.Y * dir.X) > goliath.ShockHalfWidth)
                    continue;

                goliath.ShockHit.Add(mob);
                var damage = new DamageSpecifier();
                damage.DamageDict.Add("Blunt", goliath.ShockDamage);
                _damageable.TryChangeDamage(mob, damage, origin: uid);
                _stun.TryAddParalyzeDuration(mob, TimeSpan.FromSeconds(goliath.ShockParalyze));
                break;
            }
        }

        if (goliath.ShockTravelled >= goliath.ShockRange)
        {
            goliath.State = GoliathState.Idle;
            goliath.NextShock = now + TimeSpan.FromSeconds(goliath.ShockCooldown * CooldownScale(goliath));
            EnsureComp<ActiveNPCComponent>(uid);
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

            // Пыль и осыпавшаяся крошка от удара: место стаггера должно быть видно издалека.
            var crashCoords = Transform(uid).Coordinates;
            Spawn(goliath.DustProto, crashCoords);
            foreach (var side in new[] { -1f, 1f })
            {
                Spawn(goliath.DustProto, crashCoords.Offset(perp * side));
                Spawn(goliath.DustProto, crashCoords.Offset(goliath.ChargeDir + perp * side * 0.5f));
            }
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

        // След за чарджем: пыль из-под ног всегда, наледь — на фазе 2 (кайтить по своему следу
        // не выйдет). Оба сыплются по одному накопителю пути.
        goliath.FrostAccumulator += step;
        while (goliath.FrostAccumulator >= 0.7f)
        {
            goliath.FrostAccumulator -= 0.7f;
            Spawn(goliath.DustProto, xform.Coordinates);
            if (goliath.LastPhase >= 1)
                Spawn(goliath.FrostProto, xform.Coordinates);
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
