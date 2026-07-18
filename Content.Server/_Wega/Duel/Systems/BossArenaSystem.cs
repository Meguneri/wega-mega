using Content.Server._Wega.Duel.Components;
using Content.Server.Chat.Managers;
using Content.Server.DeviceLinking.Systems;
using Content.Shared._Wega.Duel;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Humanoid;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Mind;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Linq;
using System.Numerics;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Система босс-арены PvE. Управляет стартом, телепортом участников, спавном босса,
/// фазами босса, наградой и сбросом арены.
/// </summary>
public sealed partial class BossArenaSystem : EntitySystem
{
    [Dependency] private DeviceLinkSystem _signalSystem = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private DuelArenaScoreSystem _score = default!;
    [Dependency] private MobThresholdSystem _mobThresholds = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BossArenaComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<BossArenaComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<BossArenaBossComponent, DamageChangedEvent>(OnBossDamageChanged);

        // Привязка «боссовского» огнестрела: поднять/выстрелить может только сам босс.
        SubscribeLocalEvent<BossArenaBoundGunComponent, ShotAttemptedEvent>(OnBoundGunShot);
        SubscribeLocalEvent<BossArenaBoundGunComponent, GettingPickedUpAttemptEvent>(OnBoundGunPickup);

        // Учёт очередей босса: 3–5 выстрелов из огнестрела, затем кулдаун (см. BossArenaVolleyComponent).
        SubscribeLocalEvent<BossArenaBoundGunComponent, GunShotEvent>(OnBoundGunShotFired);
    }

    private void OnInit(EntityUid uid, BossArenaComponent comp, ComponentInit args)
    {
        _signalSystem.EnsureSinkPorts(uid, "Open", "Toggle");
        _signalSystem.EnsureSourcePorts(uid, comp.ResetPort);
    }

    private string SafeName(EntityUid uid)
        => Exists(uid) ? MetaData(uid).EntityName : "?";

    private NetUserId? GetUser(EntityUid body)
    {
        return _mind.TryGetMind(body, out _, out var mind) ? mind.UserId : null;
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<BossArenaComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // Закрытие шлюзов после grace-периода.
            if (comp.GateCloseAt != null && now >= comp.GateCloseAt)
            {
                comp.GateCloseAt = null;
                _signalSystem.SendSignal(uid, comp.ResetPort, true);
            }

            // Таймер боя истёк — босс впадает в ярость (энрейдж), а бой идёт до вайпа любой стороны.
            if (comp.IsActive && !comp.Enraged && comp.FightEndAt is { } fightEnd && now >= fightEnd)
                EnrageBoss(uid, comp);

            // Анти-кайт работает каждый тик (регенерация идёт по frameTime).
            if (comp.IsActive)
                UpdateAntiKite(uid, comp, now, frameTime);

            // Сканируем активную арену, чтобы засечь поражение.
            if (!comp.IsActive || now < comp.NextScan)
                continue;

            comp.NextScan = now + TimeSpan.FromSeconds(comp.ScanInterval);
            Scan(uid, comp);
            UpdateMinions(uid, comp, now);

            // Периодическое обновление HUD-полоски ХП у участников (мгновенные — на старте,
            // смене фазы, энрейдже и завершении).
            SendHudState(comp, true);
        }
    }

    private void Scan(EntityUid uid, BossArenaComponent comp)
    {
        // Все участники мертвы/исчезли/покинули грид арены — поражение.
        var trackerGrid = Transform(uid).GridUid;
        if (comp.Participants.All(p => !Exists(p) || _mobState.IsDead(p) || Transform(p).GridUid != trackerGrid))
        {
            ConcludeArena(uid, comp, success: false);
            return;
        }
    }

    /// <summary>
    /// Энрейдж: по истечении таймера боя босс впадает в ярость — фазовые множители скорости и урона
    /// умножаются на <see cref="BossArenaComponent.EnrageSpeedMultiplier"/> /
    /// <see cref="BossArenaComponent.EnrageDamageMultiplier"/> (см. ApplyPhaseBuffs). Поражение
    /// участников возможно теперь только их гибелью — затянувшийся бой становится смертельно опасным,
    /// а не обрывается антиклимаксом.
    /// </summary>
    private void EnrageBoss(EntityUid uid, BossArenaComponent comp)
    {
        comp.Enraged = true;
        comp.FightEndAt = null;

        if (comp.Boss is { } boss && Exists(boss) && TryComp<BossArenaBossComponent>(boss, out var bossComp))
            ApplyPhaseBuffs(boss, bossComp);

        _chatManager.DispatchServerAnnouncement(Loc.GetString("boss-arena-enraged"), Color.OrangeRed);

        if (comp.PhaseSound != null)
            _audio.PlayPvs(comp.PhaseSound, uid);

        SendHudState(comp, true);
    }

    /// <summary>
    /// Анти-кайт: если ВСЕ живые участники держатся дальше <see cref="BossArenaComponent.AntiKiteRange"/>
    /// тайлов от босса дольше <see cref="BossArenaComponent.AntiKiteGraceSeconds"/> секунд подряд,
    /// босс начинает регенерировать — расстрелять его с безопасной дистанции не выйдет, бой требует
    /// сближения. Возврат хотя бы одного живого участника в зону сбрасывает таймер и анонс.
    /// </summary>
    private void UpdateAntiKite(EntityUid uid, BossArenaComponent comp, TimeSpan now, float frameTime)
    {
        if (comp.AntiKiteRegenPerSecond <= 0f)
            return;

        if (comp.Boss is not { } boss || !Exists(boss) || _mobState.IsDead(boss))
            return;

        var bossPos = _transform.GetWorldPosition(boss);
        var nearest = float.MaxValue;
        foreach (var p in comp.Participants)
        {
            if (!Exists(p) || !_mobState.IsAlive(p))
                continue;

            var dist = (_transform.GetWorldPosition(p) - bossPos).Length();
            if (dist < nearest)
                nearest = dist;
        }

        if (nearest <= comp.AntiKiteRange)
        {
            comp.OutOfRangeSince = null;
            comp.AntiKiteAnnounced = false;
            return;
        }

        // Живых в зоне нет (или все далеко): гибель всех разрулит Scan, а здесь копим кайт-таймер.
        comp.OutOfRangeSince ??= now;
        if (now - comp.OutOfRangeSince.Value < TimeSpan.FromSeconds(comp.AntiKiteGraceSeconds))
            return;

        if (!comp.AntiKiteAnnounced)
        {
            comp.AntiKiteAnnounced = true;
            _chatManager.DispatchServerAnnouncement(Loc.GetString("boss-arena-antikite-regen"), Color.OrangeRed);
        }

        RegenerateBoss(boss, comp.AntiKiteRegenPerSecond * frameTime);
    }

    /// <summary>
    /// Лечит босса на <paramref name="amount"/>, распределяя лечение пропорционально текущим типам
    /// урона, чтобы не «стирать» один конкретный тип и не лечить отсутствующий.
    /// </summary>
    private void RegenerateBoss(EntityUid boss, float amount)
    {
        if (amount <= 0f || !TryComp<DamageableComponent>(boss, out var damageable))
            return;

        // Чтение расклада урона напрямую: анализатор доступа (RA0002) закрывает Damage для чужих
        // систем, но публичного среза «сколько какого типа» DamageableSystem не даёт, а лечить
        // пропорционально иначе нечем. Только чтение, запись — честным TryChangeDamage ниже.
#pragma warning disable RA0002
        var total = (float) damageable.Damage.GetTotal();
        if (total <= 0f)
            return;

        var heal = new DamageSpecifier();
        foreach (var (type, value) in damageable.Damage.DamageDict)
        {
            if (value <= 0)
                continue;
            heal.DamageDict[type] = FixedPoint2.New(-amount * ((float) value / total));
        }
#pragma warning restore RA0002

        _damageable.TryChangeDamage(boss, heal, true);
    }

    /// <summary>
    /// Привязка «боссовского» огнестрела (<see cref="BossArenaBoundGunComponent"/>): выстрелить из
    /// него может только сам босс (сущность с <see cref="BossArenaBossComponent"/>).
    /// </summary>
    private void OnBoundGunShot(EntityUid uid, BossArenaBoundGunComponent comp, ref ShotAttemptedEvent args)
    {
        if (HasComp<BossArenaBossComponent>(args.User))
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("boss-arena-bound-gun-denied"), args.User, args.User);
    }

    /// <summary>Поднять привязанный огнестрел может тоже только босс — для остальных отмена с попапом.</summary>
    private void OnBoundGunPickup(EntityUid uid, BossArenaBoundGunComponent comp, ref GettingPickedUpAttemptEvent args)
    {
        if (HasComp<BossArenaBossComponent>(args.User))
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("boss-arena-bound-gun-denied"), args.User, args.User);
    }

    /// <summary>
    /// Отсчёт очереди «Карателя»: первый выстрел открывает очередь из 3–5 выстрелов
    /// (<see cref="BossArenaVolleyComponent"/>), последний — включает кулдаун, и HTN уводит босса
    /// в ближний бой до следующей очереди. Так огнестрел остаётся разовой атакой, а не постоянным.
    /// </summary>
    private void OnBoundGunShotFired(EntityUid uid, BossArenaBoundGunComponent comp, ref GunShotEvent args)
    {
        if (!TryComp<BossArenaVolleyComponent>(args.User, out var volley))
            return;

        if (volley.ShotsRemaining <= 0)
            volley.ShotsRemaining = _random.Next(volley.VolleyShotsMin, volley.VolleyShotsMax + 1);

        volley.ShotsRemaining--;
        if (volley.ShotsRemaining <= 0)
            volley.NextVolleyAt = _timing.CurTime + TimeSpan.FromSeconds(volley.VolleyCooldown);
    }

    /// <summary>
    /// Шлёт участникам состояние HUD-полоски ХП босса (только тем, у кого есть игровая сессия —
    /// NPC-участники пропускаются). При active=false полоска скрывается, поля не важны.
    /// </summary>
    private void SendHudState(BossArenaComponent comp, bool active)
    {
        var ev = new BossArenaHudEvent { Active = active };
        if (active && comp.Boss is { } boss && Exists(boss))
        {
            ev.BossName = MetaData(boss).EntityName;
            ev.HealthRatio = GetHealthRatio(boss);
            ev.Phase = comp.Phase;
            ev.Enraged = comp.Enraged;
        }

        foreach (var p in comp.Participants)
        {
            if (TryComp<ActorComponent>(p, out var actor))
                RaiseNetworkEvent(ev, actor.PlayerSession);
        }
    }

    /// <summary>
    /// Управляет волнами миньонов: планирует первую волну и спавнит последующие по таймеру.
    /// </summary>
    private void UpdateMinions(EntityUid uid, BossArenaComponent comp, TimeSpan now)
    {
        if (comp.MinionPrototypes.Count == 0)
            return;

        // Фаза ещё не та, в которой появляются миньоны — ждём.
        if (comp.Phase < comp.MinionPhaseStart)
            return;

        // Планируем первую волну при входе в фазу, если ещё не запланировано.
        if (comp.NextMinionSpawnAt == null)
        {
            comp.NextMinionSpawnAt = now + TimeSpan.FromSeconds(comp.MinionSpawnInterval);
            return;
        }

        if (now < comp.NextMinionSpawnAt)
            return;

        comp.NextMinionSpawnAt = now + TimeSpan.FromSeconds(comp.MinionSpawnInterval);

        // Чистим мертвых/исчезнувших миньонов из отслеживания.
        comp.Minions.RemoveWhere(m => !Exists(m) || _mobState.IsDead(m));

        if (comp.Minions.Count >= comp.MaxMinions)
            return;

        var center = GetMinionSpawnCenter(uid, comp);
        if (center == null)
            return;

        var spawned = 0;
        var toSpawn = Math.Min(comp.MinionSpawnPerWave, comp.MaxMinions - comp.Minions.Count);
        for (var i = 0; i < toSpawn; i++)
        {
            var angle = _random.NextFloat() * MathF.Tau;
            var dist = _random.NextFloat(2f, comp.MinionSpawnRadius);
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
            var coords = center.Value.Offset(offset);

            var proto = _random.Pick(comp.MinionPrototypes);
            var minion = Spawn(proto, coords);
            comp.Minions.Add(minion);
            spawned++;
        }

        if (spawned > 0)
        {
            _chatManager.DispatchServerAnnouncement(
                Loc.GetString("boss-arena-minion-wave", ("count", spawned)), Color.OrangeRed);
        }
    }

    private EntityCoordinates? GetMinionSpawnCenter(EntityUid uid, BossArenaComponent comp)
    {
        if (comp.Boss != null && Exists(comp.Boss.Value) && !_mobState.IsDead(comp.Boss.Value))
            return Transform(comp.Boss.Value).Coordinates;

        var aliveParticipant = comp.Participants.FirstOrDefault(p => Exists(p) && _mobState.IsAlive(p));
        if (aliveParticipant != default)
            return Transform(aliveParticipant).Coordinates;

        return Transform(uid).Coordinates;
    }

    private void OnSignalReceived(EntityUid uid, BossArenaComponent comp, ref SignalReceivedEvent args)
    {
        switch (args.Port)
        {
            case "Open":
                StartArena(uid, comp);
                break;
            case "Toggle":
                ResetArena(uid, comp);
                break;
        }
    }

    private void StartArena(EntityUid uid, BossArenaComponent comp)
    {
        if (comp.IsActive)
            return;

        var participants = GetAliveParticipants(uid, comp);
        if (participants.Count == 0)
        {
            _chatManager.DispatchServerAnnouncement(
                Loc.GetString("boss-arena-not-started-no-participants"), Color.Gray);
            return;
        }

        comp.Participants.Clear();
        foreach (var p in participants)
            comp.Participants.Add(p);

        comp.IsActive = true;
        comp.Phase = 0;
        comp.FightEndAt = comp.MaxFightDuration > 0f
            ? _timing.CurTime + TimeSpan.FromSeconds(comp.MaxFightDuration)
            : null;
        comp.GateCloseAt = null;
        comp.Minions.Clear();
        comp.NextMinionSpawnAt = null;
        comp.Enraged = false;
        comp.OutOfRangeSince = null;
        comp.AntiKiteAnnounced = false;

        var trackerXform = Transform(uid);
        var trackerCoords = trackerXform.Coordinates;
        var gridUid = trackerXform.GridUid;

        // Собираем маркеры на том же гриде.
        var participantMarkers = new Dictionary<int, EntityCoordinates>();
        EntityCoordinates? bossMarker = null;
        var markerQuery = EntityQueryEnumerator<BossArenaSpawnMarkerComponent, TransformComponent>();
        while (markerQuery.MoveNext(out var markerUid, out var marker, out var xform))
        {
            if (xform.GridUid != gridUid)
                continue;

            if (marker.SpawnType == BossArenaSpawnType.Participant)
                participantMarkers[marker.Index] = xform.Coordinates;
            else if (marker.SpawnType == BossArenaSpawnType.Boss)
                bossMarker = xform.Coordinates;
        }

        // Телепортируем участников.
        var index = 0;
        foreach (var p in participants)
        {
            var coords = participantMarkers.TryGetValue(index, out var markerCoords)
                ? markerCoords
                : trackerCoords.Offset(GetDefaultParticipantOffset(index));
            _transform.SetCoordinates(p, coords);
            index++;
        }

        // Спавним босса.
        if (comp.BossPrototype != null)
        {
            var bossCoords = bossMarker != null ? bossMarker.Value : trackerCoords;
            comp.Boss = Spawn(comp.BossPrototype.Value, bossCoords);
            if (TryComp<BossArenaBossComponent>(comp.Boss, out var bossComp))
            {
                bossComp.Arena = uid;
                CaptureBaseStats(comp.Boss.Value, bossComp);
                ApplyParticipantScaling(comp, comp.Boss.Value, bossComp);
            }
        }

        _signalSystem.SendSignal(uid, comp.ResetPort, false);

        _chatManager.DispatchServerAnnouncement(
            Loc.GetString("boss-arena-started", ("participants", participants.Count)), Color.Gold);

        if (comp.StartSound != null && comp.Boss != null)
            _audio.PlayPvs(comp.StartSound, comp.Boss.Value);

        SendHudState(comp, true);
    }

    private void ResetArena(EntityUid uid, BossArenaComponent comp)
    {
        if (!comp.IsActive)
            return;

        ConcludeArena(uid, comp, success: false);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        var uid = args.Target;
        var query = EntityQueryEnumerator<BossArenaComponent>();
        while (query.MoveNext(out var arenaUid, out var arena))
        {
            if (!arena.IsActive)
                continue;

            if (arena.Boss == uid)
            {
                ConcludeArena(arenaUid, arena, success: true);
                break;
            }
        }
    }

    private void OnBossDamageChanged(EntityUid uid, BossArenaBossComponent comp, DamageChangedEvent args)
    {
        if (comp.Arena == null || !TryComp<BossArenaComponent>(comp.Arena.Value, out var arena) || !arena.IsActive)
            return;

        var ratio = GetHealthRatio(uid);
        var newPhase = 0;
        foreach (var threshold in comp.PhaseThresholds)
        {
            if (ratio <= threshold)
                newPhase++;
            else
                break;
        }

        if (newPhase == comp.CurrentPhase)
            return;

        comp.CurrentPhase = newPhase;
        arena.Phase = newPhase;
        ApplyPhaseBuffs(uid, comp);

        _chatManager.DispatchServerAnnouncement(
            Loc.GetString("boss-arena-phase-changed", ("phase", newPhase + 1)), Color.OrangeRed);

        if (comp.Arena != null && arena.PhaseSound != null)
            _audio.PlayPvs(arena.PhaseSound, comp.Arena.Value);

        SendHudState(arena, true);
    }

    /// <summary>
    /// Завершает арену: при успехе выдаёт награду, при поражении убирает босса и участников.
    /// </summary>
    private void ConcludeArena(EntityUid uid, BossArenaComponent comp, bool success)
    {
        if (!comp.IsActive)
            return;

        comp.IsActive = false;
        comp.FightEndAt = null;
        comp.GateCloseAt = _timing.CurTime + TimeSpan.FromSeconds(comp.ReturnGrace);

        _signalSystem.SendSignal(uid, comp.ResetPort, false);

        if (success)
        {
            var names = string.Join(", ", comp.Participants.Where(Exists).Select(p => MetaData(p).EntityName));
            _chatManager.DispatchServerAnnouncement(
                Loc.GetString("boss-arena-concluded-success", ("names", names)), Color.Gold);

            // Запоминаем имена всех победителей и ведём общий счёт босс-арены.
            foreach (var participant in comp.Participants.Where(Exists))
            {
                var user = GetUser(participant);
                if (user != null)
                    comp.ScoreNames[user.Value] = SafeName(participant);
            }

            var livingWinners = comp.Participants.Where(p => Exists(p) && _mobState.IsAlive(p)).ToList();
            NetUserId? winnerUser = null;
            if (livingWinners.Count == 1)
                winnerUser = GetUser(livingWinners[0]);
            else if (livingWinners.Count > 1)
            {
                // При групповой победе засчитываем серию всей группе, но общий счёт — каждому.
                foreach (var winner in livingWinners)
                {
                    var user = GetUser(winner);
                    if (user == null)
                        continue;
                    comp.Scores[user.Value] = comp.Scores.GetValueOrDefault(user.Value) + 1;
                }

                winnerUser = GetUser(livingWinners[0]);
            }

            if (livingWinners.Count == 1 && winnerUser != null)
                comp.Scores[winnerUser.Value] = comp.Scores.GetValueOrDefault(winnerUser.Value) + 1;

            // Серия побед подряд: в групповом режиме серия не ломается, пока побеждает любой из участников.
            if (winnerUser != null && comp.StreakUser == winnerUser)
                comp.Streak++;
            else
            {
                comp.StreakUser = winnerUser;
                comp.Streak = 1;
            }

            var scoreboard = _score.BuildScoreboard(comp);
            if (scoreboard != null)
                _chatManager.DispatchServerAnnouncement(
                    Loc.GetString("boss-arena-scoreboard", ("scores", scoreboard)), Color.Gold);

            if (comp.Boss != null && Exists(comp.Boss.Value))
            {
                var coords = Transform(comp.Boss.Value).Coordinates;
                if (comp.RewardPrototype != null)
                    Spawn(comp.RewardPrototype.Value, coords);
            }
        }
        else
        {
            _chatManager.DispatchServerAnnouncement(
                Loc.GetString("boss-arena-concluded-failure"), Color.DarkGray);
        }

        // Убираем босса, если он ещё жив.
        if (comp.Boss != null && Exists(comp.Boss.Value))
        {
            QueueDel(comp.Boss.Value);
        }

        comp.Boss = null;

        // Скрываем HUD-полоску ХП у участников — бой окончен.
        SendHudState(comp, false);

        comp.Participants.Clear();
        comp.Phase = 0;
        comp.Enraged = false;
        comp.OutOfRangeSince = null;
        comp.AntiKiteAnnounced = false;

        foreach (var minion in comp.Minions)
        {
            if (Exists(minion))
                QueueDel(minion);
        }
        comp.Minions.Clear();
        comp.NextMinionSpawnAt = null;
    }

    private HashSet<EntityUid> GetAliveParticipants(EntityUid uid, BossArenaComponent comp)
    {
        var trackerXform = Transform(uid);
        var trackerPos = _transform.GetMapCoordinates(trackerXform);
        var trackerGrid = trackerXform.GridUid;

        var alive = new HashSet<EntityUid>();
        var mobQuery = EntityQueryEnumerator<MobStateComponent, HumanoidProfileComponent>();
        while (mobQuery.MoveNext(out var mobUid, out _, out _))
        {
            var mobXform = Transform(mobUid);

            if (trackerGrid != null)
            {
                if (mobXform.GridUid != trackerGrid)
                    continue;
            }
            else
            {
                var mobPos = _transform.GetMapCoordinates(mobXform);
                if (mobPos.MapId != trackerPos.MapId)
                    continue;
                if ((mobPos.Position - trackerPos.Position).Length() > comp.ScanRange)
                    continue;
            }

            if (_mobState.IsAlive(mobUid))
                alive.Add(mobUid);
        }

        return alive;
    }

    private Vector2 GetDefaultParticipantOffset(int index)
    {
        return index switch
        {
            0 => new Vector2(-2, 0),
            1 => new Vector2(2, 0),
            2 => new Vector2(0, -2),
            3 => new Vector2(0, 2),
            _ => Vector2.Zero,
        };
    }

    private void CaptureBaseStats(EntityUid uid, BossArenaBossComponent comp)
    {
        if (TryComp<MovementSpeedModifierComponent>(uid, out var speed))
        {
            comp.BaseWalkSpeed = speed.BaseWalkSpeed;
            comp.BaseSprintSpeed = speed.BaseSprintSpeed;
        }

        if (TryComp<MeleeWeaponComponent>(uid, out var melee))
        {
            comp.BaseMeleeDamage = new DamageSpecifier(melee.Damage);
        }
    }

    /// <summary>
    /// Масштабирует босса под число участников текущего боя: пороги ХП умножаются на
    /// <see cref="BossArenaComponent.HealthScaleBase"/> + <see cref="BossArenaComponent.HealthScalePerParticipant"/> × N,
    /// базовый урон природного оружия — на 1 + <see cref="BossArenaComponent.DamageScalePerParticipant"/> × (N − 1).
    /// Одиночный боец получает базового босса; каждый дополнительный делает его толще и злее.
    /// Вызывается из StartArena сразу после <see cref="CaptureBaseStats"/>, чтобы дальше
    /// фазовые множители и энрейдж работали уже поверх отмасштабированной базы.
    /// </summary>
    private void ApplyParticipantScaling(BossArenaComponent comp, EntityUid boss, BossArenaBossComponent bossComp)
    {
        var count = comp.Participants.Count;

        var healthFactor = comp.HealthScaleBase + comp.HealthScalePerParticipant * count;
        if (healthFactor > 0f && Math.Abs(healthFactor - 1f) > 0.001f)
        {
            foreach (var state in new[] { MobState.PreCritical, MobState.Critical, MobState.Dead })
            {
                if (_mobThresholds.TryGetThresholdForState(boss, state, out var threshold))
                    _mobThresholds.SetMobStateThreshold(boss, threshold.Value * healthFactor, state);
            }
        }

        var damageFactor = 1f + comp.DamageScalePerParticipant * (count - 1);
        if (bossComp.BaseMeleeDamage == null || damageFactor <= 0f || Math.Abs(damageFactor - 1f) <= 0.001f)
            return;

        var scaled = new DamageSpecifier();
        foreach (var (type, amount) in bossComp.BaseMeleeDamage.DamageDict)
        {
            scaled.DamageDict[type] = amount * damageFactor;
        }
        bossComp.BaseMeleeDamage = scaled;

        // Применяем сразу, чтобы MeleeWeapon босса отражал отмасштабированную базу (фаза 0, без энрейджа).
        ApplyPhaseBuffs(boss, bossComp);
    }

    private void ApplyPhaseBuffs(EntityUid uid, BossArenaBossComponent comp)
    {
        var phase = comp.CurrentPhase;
        var speedMultiplier = phase < comp.PhaseSpeedMultipliers.Count
            ? comp.PhaseSpeedMultipliers[phase]
            : 1f;
        var damageMultiplier = phase < comp.PhaseDamageMultipliers.Count
            ? comp.PhaseDamageMultipliers[phase]
            : 1f;

        // Энрейдж: множители ярости поверх фазовых — сохраняются и при последующих сменах фаз.
        if (comp.Arena is { } arenaUid
            && TryComp<BossArenaComponent>(arenaUid, out var arena)
            && arena.Enraged)
        {
            speedMultiplier *= arena.EnrageSpeedMultiplier;
            damageMultiplier *= arena.EnrageDamageMultiplier;
        }

        if (TryComp<MovementSpeedModifierComponent>(uid, out _))
        {
            _movementSpeed.ChangeBaseSpeed(uid, comp.BaseWalkSpeed * speedMultiplier, comp.BaseSprintSpeed * speedMultiplier, 20f);
        }

        if (comp.BaseMeleeDamage != null && TryComp<MeleeWeaponComponent>(uid, out var melee))
        {
            var newDamage = new DamageSpecifier();
            foreach (var (type, amount) in comp.BaseMeleeDamage.DamageDict)
            {
                newDamage.DamageDict[type] = amount * damageMultiplier;
            }
            melee.Damage = newDamage;
        }
    }

    private float GetHealthRatio(EntityUid uid)
    {
        if (!TryComp<MobThresholdsComponent>(uid, out var thresholds) || thresholds.Thresholds.Count == 0)
            return 1f;

        var maxDamage = thresholds.Thresholds.Keys.Max();
        var totalDamage = _damageable.GetTotalDamage(uid);
        var ratio = 1f - (float)(totalDamage / maxDamage);
        return Math.Clamp(ratio, 0f, 1f);
    }
}
