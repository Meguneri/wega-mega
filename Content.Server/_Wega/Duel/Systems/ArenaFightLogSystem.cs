using System.Linq;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Боевая летопись игроков — сырьё для LLM-тренера арены. Копит по каждому игроку: нанесённый
/// и полученный урон по типам, точность мили (замахи/попадания по оружию) и стрельбы, противников
/// и исход. Бои режутся на «эпизоды»: пауза 25с без боевых событий или смерть закрывают эпизод.
/// Хранится в памяти сервера (последние эпизоды на игрока), чистится на рестарте раунда.
/// </summary>
public sealed partial class ArenaFightLogSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan EpisodeGap = TimeSpan.FromSeconds(25);
    private const int MaxHistory = 5;

    private readonly Dictionary<EntityUid, PlayerLog> _logs = new();

    /// <summary>Один бой глазами одного игрока.</summary>
    public sealed class FightEpisode
    {
        public TimeSpan Start;
        public TimeSpan LastEvent;
        public readonly Dictionary<string, double> DamageDealt = new();  // тип урона → сумма
        public readonly Dictionary<string, double> DamageTaken = new();
        public readonly Dictionary<string, int> Swings = new();         // оружие → замахи (мили)
        public readonly Dictionary<string, int> Hits = new();           // оружие → попадания
        public int ShotsFired;
        public int ShotsHit;
        public readonly HashSet<string> Opponents = new();
        public string? LastAttacker;
        public string? Outcome;

        /// <summary>Бой шёл на гриде арены (настоящая дуэль), а не случайная стычка на станции.</summary>
        public bool IsDuel;

        /// <summary>Суммарный обмен уроном — мера «настоящести» боя (мусор отсекается по нему).</summary>
        public double TotalExchange => DamageDealt.Values.Sum() + DamageTaken.Values.Sum();

        /// <summary>
        /// Настоящий бой для разбора: это дуэль на арене ИЛИ был живой противник и заметный обмен
        /// (не «зашёл в разгерму, получил 2 урона»). Тонкие данные пусть тренер трактует осторожно.
        /// </summary>
        public bool IsMeaningful =>
            IsDuel
            || (Opponents.Count > 0
                && (TotalExchange >= 20
                    || (Outcome != null && (Outcome.Contains("ПОБЕДА") || Outcome.Contains("ПОРАЖЕНИЕ")))));
    }

    private sealed class PlayerLog
    {
        public string Name = string.Empty;
        public FightEpisode? Current;
        public readonly List<FightEpisode> History = new();
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnGunShot);
        // Пара (ProjectileComponent, ProjectileHitEvent) занята SharedProjectileSystem, а событие
        // не бродкастится — подписываемся через TransformComponent (есть у любого снаряда).
        SubscribeLocalEvent<TransformComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<MobStateComponent, Content.Shared.Damage.Systems.DamageChangedEvent>(OnDamaged);
        // Пара (MobStateComponent, MobStateChangedEvent) уже занята SharedStunSystem, а движок
        // допускает одну directed-подписку на пару глобально — слушаем broadcast-вариант.
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _logs.Clear();
    }

    private bool IsPlayer(EntityUid uid)
        => HasComp<ActorComponent>(uid) && HasComp<MobStateComponent>(uid);

    /// <summary>
    /// Стоит ли моб сейчас на гриде арены (= в настоящей дуэли). Арена — отдельный грид сущности
    /// с DuelArenaComponent; сравниваем грид моба с гридами всех активных арен.
    /// </summary>
    private bool IsInDuel(EntityUid mob)
    {
        var grid = Transform(mob).GridUid;
        if (grid == null)
            return false;

        var arenas = EntityQueryEnumerator<Content.Server._Wega.Duel.Components.DuelArenaComponent>();
        while (arenas.MoveNext(out var arena, out _))
        {
            if (Transform(arena).GridUid == grid)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Текущий эпизод игрока без создания: закрывает протухший по паузе, возвращает активный или null.
    /// Для «неоднозначных» событий (промах в воздух, выстрел в стену) — чтобы они не РОЖДАЛИ бой.
    /// </summary>
    private FightEpisode? CurrentEp(EntityUid player)
    {
        if (!_logs.TryGetValue(player, out var log))
            return null;
        log.Name = MetaData(player).EntityName;

        if (log.Current is { } ep && _timing.RealTime - ep.LastEvent > EpisodeGap)
        {
            CloseEpisode(log, "бой затих — разошлись");
            return null;
        }
        return log.Current;
    }

    /// <summary>
    /// Начинает (или продлевает) эпизод: только для настоящих боевых событий — удар/попадание по
    /// мобу, PvP-урон. Метит эпизод дуэлью, если игрок на арене. Продлевает LastEvent.
    /// </summary>
    private FightEpisode StartEp(EntityUid player)
    {
        if (!_logs.TryGetValue(player, out var log))
        {
            log = new PlayerLog();
            _logs[player] = log;
        }
        log.Name = MetaData(player).EntityName;

        var now = _timing.RealTime;
        if (log.Current is not { } ep || now - ep.LastEvent > EpisodeGap)
        {
            CloseEpisode(log, "бой затих — разошлись");
            ep = new FightEpisode { Start = now };
            log.Current = ep;
        }

        if (IsInDuel(player))
            ep.IsDuel = true;
        ep.LastEvent = now;
        return ep;
    }

    private static void CloseEpisode(PlayerLog log, string defaultOutcome)
    {
        if (log.Current is not { } ep)
            return;
        log.Current = null;

        // Пустые эпизоды (один случайный тычок без урона) историю не засоряют.
        if (ep.DamageDealt.Count == 0 && ep.DamageTaken.Count == 0 && ep.Swings.Count == 0
            && ep.ShotsFired == 0)
            return;

        ep.Outcome ??= defaultOutcome;
        log.History.Add(ep);
        if (log.History.Count > MaxHistory)
            log.History.RemoveAt(0);
    }

    private void OnMeleeHit(Entity<MeleeWeaponComponent> weapon, ref MeleeHitEvent args)
    {
        if (!IsPlayer(args.User))
            return;

        var hitMob = false;
        foreach (var hit in args.HitEntities)
        {
            if (!HasComp<MobStateComponent>(hit) || hit == args.User)
                continue;
            hitMob = true;
        }

        // Попадание по мобу — настоящий бой (начинаем эпизод). Промах в воздух/стену считаем
        // (как мазок в бою), только если бой уже идёт или ты в дуэли — иначе тычки по лутеру
        // и ящикам не порождают «бой» и не портят статистику.
        var ep = hitMob || IsInDuel(args.User) ? StartEp(args.User) : CurrentEp(args.User);
        if (ep == null)
            return;

        var name = weapon.Owner == args.User ? "кулаки" : MetaData(weapon).EntityName;
        ep.Swings[name] = ep.Swings.GetValueOrDefault(name) + 1;
        if (hitMob)
        {
            ep.Hits[name] = ep.Hits.GetValueOrDefault(name) + 1;
            foreach (var hit in args.HitEntities)
            {
                if (HasComp<MobStateComponent>(hit) && hit != args.User)
                    ep.Opponents.Add(MetaData(hit).EntityName);
            }
        }
    }

    private void OnGunShot(Entity<GunComponent> gun, ref GunShotEvent args)
    {
        if (!IsPlayer(args.User))
            return;
        // Выстрел сам по себе бой не заводит (можно палить в тир/стену): считаем, только если
        // бой уже идёт или стрелок в дуэли. Первое попадание по мобу заведёт эпизод отдельно.
        var ep = IsInDuel(args.User) ? StartEp(args.User) : CurrentEp(args.User);
        if (ep != null)
            ep.ShotsFired += Math.Max(1, args.Ammo.Count);
    }

    private void OnProjectileHit(Entity<TransformComponent> proj, ref ProjectileHitEvent args)
    {
        if (args.Shooter is not { } shooter || !IsPlayer(shooter) || !HasComp<MobStateComponent>(args.Target))
            return;
        if (args.Target == shooter)
            return;
        var ep = StartEp(shooter);
        ep.ShotsHit += 1;
        ep.Opponents.Add(MetaData(args.Target).EntityName);
    }

    private void OnDamaged(Entity<MobStateComponent> victim, ref Content.Shared.Damage.Systems.DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta is not { } delta)
            return;

        var attacker = args.Origin;
        var attackerIsMob = attacker is { } a && Exists(a) && HasComp<MobStateComponent>(a) && a != victim.Owner;

        // КЛЮЧЕВОЕ: урон от окружения (разгерма, огонь, падение — нет моба-источника) боем НЕ
        // считается и эпизод не заводит. Учитываем только урон от другого моба (PvP) — либо в
        // уже идущий бой (тогда это часть боя), либо начинаем новый.
        if (!attackerIsMob)
        {
            // Разве что стихия во время активного боя — как фон (например, добили штормом): в
            // текущий эпизод, но новый из-за неё не рождаем.
            if (IsPlayer(victim) && CurrentEp(victim) is { } ongoing)
            {
                foreach (var (type, amount) in delta.DamageDict)
                {
                    if (amount > 0)
                        ongoing.DamageTaken[type.ToString()] =
                            ongoing.DamageTaken.GetValueOrDefault(type.ToString()) + amount.Double();
                }
            }
            return;
        }

        // Полученный урон — жертве-игроку.
        if (IsPlayer(victim))
        {
            var ep = StartEp(victim);
            foreach (var (type, amount) in delta.DamageDict)
            {
                if (amount <= 0)
                    continue;
                ep.DamageTaken[type.ToString()] = ep.DamageTaken.GetValueOrDefault(type.ToString()) + amount.Double();
            }
            ep.LastAttacker = MetaData(attacker!.Value).EntityName;
            ep.Opponents.Add(ep.LastAttacker);
        }

        // Нанесённый урон — атакующему-игроку.
        if (IsPlayer(attacker!.Value))
        {
            var ep = StartEp(attacker.Value);
            foreach (var (type, amount) in delta.DamageDict)
            {
                if (amount <= 0)
                    continue;
                ep.DamageDealt[type.ToString()] = ep.DamageDealt.GetValueOrDefault(type.ToString()) + amount.Double();
            }
            ep.Opponents.Add(MetaData(victim).EntityName);
        }
    }

    private void OnMobState(MobStateChangedEvent args)
    {
        if (args.NewMobState is not (MobState.Critical or MobState.Dead))
            return;

        var mob = args.Target;

        // Пал сам — закрываем эпизод поражением.
        if (_logs.TryGetValue(mob, out var log) && log.Current is { } ep)
        {
            ep.Outcome = args.NewMobState == MobState.Dead
                ? $"ПОРАЖЕНИЕ — погиб{(ep.LastAttacker != null ? $", добил {ep.LastAttacker}" : "")}"
                : $"ПОРАЖЕНИЕ — упал без сознания{(ep.LastAttacker != null ? $", от рук {ep.LastAttacker}" : "")}";
            CloseEpisode(log, ep.Outcome);
        }

        // Сразил противника — закрываем эпизод победителя.
        if (args.Origin is { } winner && winner != mob && IsPlayer(winner)
            && _logs.TryGetValue(winner, out var winnerLog) && winnerLog.Current is { } winnerEp)
        {
            winnerEp.Outcome = $"ПОБЕДА — сразил {MetaData(mob).EntityName}";
            CloseEpisode(winnerLog, winnerEp.Outcome);
        }
    }

    // ------------------------------------------------------------------ Отчёты

    /// <summary>
    /// Текстовый отчёт о боях игрока (для промпта LLM). lastOnly — только последний бой
    /// (режим по умолчанию у тренера); false — все бои сессии. null = боёв не видели.
    /// </summary>
    public string? GetReport(EntityUid player, bool lastOnly = true)
    {
        if (!_logs.TryGetValue(player, out var log))
            return null;

        // Залежавшийся текущий эпизод закрываем, чтобы он попал в отчёт.
        if (log.Current is { } current && _timing.RealTime - current.LastEvent > EpisodeGap)
            CloseEpisode(log, "бой затих — разошлись");

        // Только настоящие бои: дуэли на арене и заметные PvP-стычки. Разгерма, тычки по ящикам
        // и прочий мусор в разбор не идут — тренер не должен делать выводы из шума.
        var episodes = new List<FightEpisode>(log.History);
        if (log.Current is { } active)
            episodes.Add(active);
        episodes = episodes.Where(e => e.IsMeaningful).ToList();
        if (episodes.Count == 0)
            return null;

        if (lastOnly)
            episodes.RemoveRange(0, episodes.Count - 1);

        var now = _timing.RealTime;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(lastOnly
            ? $"Последняя дуэль {log.Name} (разбирай ТОЛЬКО эти цифры, не домысливай):"
            : $"Дуэли {log.Name} за сессию (от старых к новым, разбирай ТОЛЬКО эти цифры):");
        var index = 0;
        foreach (var ep in episodes)
        {
            index++;
            var ago = (int)(now - ep.LastEvent).TotalMinutes;
            var duration = (int)(ep.LastEvent - ep.Start).TotalSeconds;
            var kind = ep.IsDuel ? "дуэль на арене" : "стычка";
            sb.Append($"Бой {index} ({kind}, {(ago < 1 ? "только что" : $"{ago} мин назад")}, длился ~{Math.Max(duration, 1)} c)");
            if (ep.Opponents.Count > 0)
                sb.Append($", противник: {string.Join(", ", ep.Opponents.Take(3))}");
            sb.AppendLine(".");

            sb.AppendLine($"  Нанёс урона: {FormatDamage(ep.DamageDealt)}; получил: {FormatDamage(ep.DamageTaken)}.");

            foreach (var (weapon, swings) in ep.Swings)
            {
                var hits = ep.Hits.GetValueOrDefault(weapon);
                sb.AppendLine($"  Ближний бой ({weapon}): {swings} замахов, {hits} попаданий ({Percent(hits, swings)}).");
            }
            if (ep.ShotsFired > 0)
                sb.AppendLine($"  Стрельба: {ep.ShotsFired} выстрелов, {ep.ShotsHit} попаданий ({Percent(ep.ShotsHit, ep.ShotsFired)}).");

            sb.AppendLine($"  Исход: {ep.Outcome ?? "бой ещё идёт"}.");

            // Явно помечаем скудные данные — чтобы модель не строила теорий на паре ударов.
            var swingTotal = ep.Swings.Values.Sum();
            if (ep.TotalExchange < 30 && swingTotal < 5 && ep.ShotsFired < 5)
                sb.AppendLine("  [данных мало: короткий бой — не выдумывай развёрнутый разбор, " +
                    "скажи прямо, что материала на анализ почти нет].");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Отчёт по имени (игрок может быть уже не рядом): точное имя, затем вхождение.</summary>
    public string? GetReportByName(string name, bool lastOnly = true)
    {
        var needle = name.Trim().ToLowerInvariant();
        if (needle.Length == 0)
            return null;

        EntityUid? partial = null;
        foreach (var (uid, log) in _logs)
        {
            var logName = log.Name.ToLowerInvariant();
            if (logName == needle)
                return GetReport(uid, lastOnly);
            if (partial == null && (logName.Contains(needle) || needle.Contains(logName)))
                partial = uid;
        }
        return partial is { } p ? GetReport(p, lastOnly) : null;
    }

    private static string FormatDamage(Dictionary<string, double> damage)
    {
        if (damage.Count == 0)
            return "0";
        var total = damage.Values.Sum();
        var top = damage.OrderByDescending(kv => kv.Value).Take(3)
            .Select(kv => $"{kv.Key} {kv.Value:0}");
        return $"{total:0} ({string.Join(", ", top)})";
    }

    private static string Percent(int part, int whole)
        => whole <= 0 ? "0%" : $"{100 * part / whole}%";
}
