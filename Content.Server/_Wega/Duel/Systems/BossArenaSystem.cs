using Content.Server._Wega.Duel.Components;
using Content.Server.Chat.Managers;
using Content.Server.DeviceLinking.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Mind;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Network;
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

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BossArenaComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<BossArenaComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<BossArenaBossComponent, DamageChangedEvent>(OnBossDamageChanged);
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

            // Таймер боя: если истёк и босс ещё жив — поражение.
            if (comp.IsActive && comp.FightEndAt is { } fightEnd && now >= fightEnd)
            {
                ConcludeArena(uid, comp, success: false);
                continue;
            }

            // Сканируем активную арену, чтобы засечь поражение.
            if (!comp.IsActive || now < comp.NextScan)
                continue;

            comp.NextScan = now + TimeSpan.FromSeconds(comp.ScanInterval);
            Scan(uid, comp);
            UpdateMinions(uid, comp, now);
        }
    }

    private void Scan(EntityUid uid, BossArenaComponent comp)
    {
        // Все участники мертвы/исчезли — поражение.
        if (comp.Participants.All(p => !Exists(p) || _mobState.IsDead(p)))
        {
            ConcludeArena(uid, comp, success: false);
            return;
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
            }
        }

        _signalSystem.SendSignal(uid, comp.ResetPort, false);

        _chatManager.DispatchServerAnnouncement(
            Loc.GetString("boss-arena-started", ("participants", participants.Count)), Color.Gold);

        if (comp.StartSound != null && comp.Boss != null)
            _audio.PlayPvs(comp.StartSound, comp.Boss.Value);
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
        comp.Participants.Clear();
        comp.Phase = 0;

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

    private void ApplyPhaseBuffs(EntityUid uid, BossArenaBossComponent comp)
    {
        var phase = comp.CurrentPhase;
        var speedMultiplier = phase < comp.PhaseSpeedMultipliers.Count
            ? comp.PhaseSpeedMultipliers[phase]
            : 1f;
        var damageMultiplier = phase < comp.PhaseDamageMultipliers.Count
            ? comp.PhaseDamageMultipliers[phase]
            : 1f;

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
