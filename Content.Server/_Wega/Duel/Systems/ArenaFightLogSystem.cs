using System.Linq;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Network;
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
    // Историй на игрока за раунд. Было 5 — сессия из ~30 дуэлей обрезалась, и анализатор,
    // Феликс и Макс «видели» лишь хвост. Записи — лёгкие структуры со счётчиками, 100 не тяжело.
    private const int MaxHistory = 100;

    // КЛЮЧ — NetUserId игрока, не EntityUid: после смерти в дуэли игрок получает НОВОЕ тело,
    // и летопись по старому uid терялась («Записей нет» сразу после честной дуэли).
    private readonly Dictionary<NetUserId, PlayerLog> _logs = new();

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

        /// <summary>Сквозной номер дуэли (DuelArenaComponent.DuelNumber). 0 = неизвестен/стычка.</summary>
        public int DuelNumber;

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

    /// <summary>NetUserId игрока за мобом (false = не игрок/нет сессии).</summary>
    private bool TryUser(EntityUid mob, out NetUserId user)
    {
        if (TryComp<ActorComponent>(mob, out var actor) && HasComp<MobStateComponent>(mob))
        {
            user = actor.PlayerSession.UserId;
            return true;
        }
        user = default;
        return false;
    }

    /// <summary>
    /// Стоит ли моб сейчас на гриде арены (= в настоящей дуэли) и номер этой дуэли.
    /// Арена — отдельный грид сущности с DuelArenaComponent; сравниваем гриды.
    /// </summary>
    private bool TryGetDuel(EntityUid mob, out int duelNumber)
    {
        duelNumber = 0;
        var grid = Transform(mob).GridUid;
        if (grid == null)
            return false;

        var arenas = EntityQueryEnumerator<Content.Server._Wega.Duel.Components.DuelArenaComponent>();
        while (arenas.MoveNext(out var arena, out var comp))
        {
            if (Transform(arena).GridUid == grid)
            {
                duelNumber = comp.DuelNumber;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Текущий эпизод игрока без создания: закрывает протухший по паузе, возвращает активный или null.
    /// Для «неоднозначных» событий (промах в воздух, выстрел в стену) — чтобы они не РОЖДАЛИ бой.
    /// </summary>
    private FightEpisode? CurrentEp(EntityUid player)
    {
        if (!TryUser(player, out var user) || !_logs.TryGetValue(user, out var log))
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
        // Вызывающие проверяют IsPlayer, так что TryUser здесь не падает; на всякий случай —
        // фолбэк на «нулевого» пользователя, чтобы не уронить обработчик события.
        TryUser(player, out var user);

        if (!_logs.TryGetValue(user, out var log))
        {
            log = new PlayerLog();
            _logs[user] = log;
        }
        log.Name = MetaData(player).EntityName;

        var now = _timing.RealTime;
        if (log.Current is not { } ep || now - ep.LastEvent > EpisodeGap)
        {
            CloseEpisode(log, "бой затих — разошлись");
            ep = new FightEpisode { Start = now };
            log.Current = ep;
            Log.Debug($"fight_log: новый эпизод {log.Name}");
        }

        if (TryGetDuel(player, out var duelNumber))
        {
            if (!ep.IsDuel)
                Log.Debug($"fight_log: эпизод {log.Name} помечен как дуэль №{duelNumber}");
            ep.IsDuel = true;
            if (duelNumber > 0)
                ep.DuelNumber = duelNumber;
        }
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
        var ep = hitMob || TryGetDuel(args.User, out _) ? StartEp(args.User) : CurrentEp(args.User);
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
        var ep = TryGetDuel(args.User, out _) ? StartEp(args.User) : CurrentEp(args.User);
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
        if (TryUser(mob, out var victimUser) && _logs.TryGetValue(victimUser, out var log)
            && log.Current is { } ep)
        {
            ep.Outcome = args.NewMobState == MobState.Dead
                ? $"ПОРАЖЕНИЕ — погиб{(ep.LastAttacker != null ? $", добил {ep.LastAttacker}" : "")}"
                : $"ПОРАЖЕНИЕ — упал без сознания{(ep.LastAttacker != null ? $", от рук {ep.LastAttacker}" : "")}";
            CloseEpisode(log, ep.Outcome);
            Log.Debug($"fight_log: {log.Name} — эпизод закрыт: {ep.Outcome}");
        }

        // Сразил противника — закрываем эпизод победителя.
        if (args.Origin is { } winner && winner != mob && TryUser(winner, out var winnerUser)
            && _logs.TryGetValue(winnerUser, out var winnerLog) && winnerLog.Current is { } winnerEp)
        {
            winnerEp.Outcome = $"ПОБЕДА — сразил {MetaData(mob).EntityName}";
            CloseEpisode(winnerLog, winnerEp.Outcome);
            Log.Debug($"fight_log: {winnerLog.Name} — эпизод закрыт: {winnerEp.Outcome}");
        }
    }

    // ------------------------------------------------------------------ Отчёты

    /// <summary>
    /// Текстовый отчёт о боях игрока (для промпта LLM). lastOnly — только последний бой
    /// (режим по умолчанию у тренера); false — все бои сессии. null = боёв не видели.
    /// </summary>
    /// <summary>
    /// Собирает значимые эпизоды игрока (общая логика GetReport/GetPaperReport): закрывает
    /// протухший текущий, отсекает мусор (IsMeaningful), при lastOnly оставляет последний.
    /// </summary>
    private List<FightEpisode> CollectEpisodes(PlayerLog log, bool lastOnly)
    {
        // Залежавшийся текущий эпизод закрываем, чтобы он попал в отчёт.
        if (log.Current is { } current && _timing.RealTime - current.LastEvent > EpisodeGap)
            CloseEpisode(log, "бой затих — разошлись");

        // Только настоящие бои: дуэли на арене и заметные PvP-стычки. Разгерма, тычки по ящикам
        // и прочий мусор в разбор не идут — тренер не должен делать выводы из шума.
        var episodes = new List<FightEpisode>(log.History);
        if (log.Current is { } active)
            episodes.Add(active);
        episodes = episodes.Where(e => e.IsMeaningful).ToList();

        if (lastOnly && episodes.Count > 1)
            episodes.RemoveRange(0, episodes.Count - 1);
        return episodes;
    }

    public string? GetReport(EntityUid player, bool lastOnly = true)
        => TryUser(player, out var user) && _logs.TryGetValue(user, out var log)
            ? BuildReport(log, lastOnly)
            : null;

    private string? BuildReport(PlayerLog log, bool lastOnly)
    {
        var episodes = CollectEpisodes(log, lastOnly);
        if (episodes.Count == 0)
            return null;

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
            var kind = ep.IsDuel
                ? ep.DuelNumber > 0 ? $"дуэль №{ep.DuelNumber} на арене" : "дуэль на арене"
                : "стычка";
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
        => FindByName(name) is { } log ? BuildReport(log, lastOnly) : null;

    /// <summary>Красивая бумажная версия отчёта по имени (для распечатки анализатора).</summary>
    public string? GetPaperReportByName(string name, bool lastOnly = true)
        => FindByName(name) is { } log ? BuildPaperReport(log, lastOnly) : null;

    private PlayerLog? FindByName(string name)
    {
        var needle = name.Trim().ToLowerInvariant();
        if (needle.Length == 0)
            return null;

        PlayerLog? partial = null;
        foreach (var log in _logs.Values)
        {
            var logName = log.Name.ToLowerInvariant();
            if (logName == needle)
                return log;
            if (partial == null && (logName.Contains(needle) || needle.Contains(logName)))
                partial = log;
        }
        return partial;
    }

    /// <summary>
    /// «Красивая» версия отчёта для бумажной распечатки: заголовки, цвета, псевдографические
    /// бар-чарты урона и точности (моноширинный блок). Игроку в руки — читабельно и наглядно.
    /// </summary>
    public string? GetPaperReport(EntityUid player, bool lastOnly = true)
        => TryUser(player, out var user) && _logs.TryGetValue(user, out var log)
            ? BuildPaperReport(log, lastOnly)
            : null;

    private string? BuildPaperReport(PlayerLog log, bool lastOnly)
    {
        var episodes = CollectEpisodes(log, lastOnly);
        if (episodes.Count == 0)
            return null;

        var now = _timing.RealTime;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[head=2]АНАЛИЗ БОЯ[/head]");
        sb.AppendLine($"[bold]Боец:[/bold] {log.Name}");
        sb.AppendLine("[color=gray]────────────────────────────[/color]");

        var index = 0;
        foreach (var ep in episodes)
        {
            index++;
            var ago = (int)(now - ep.LastEvent).TotalMinutes;
            var duration = Math.Max((int)(ep.LastEvent - ep.Start).TotalSeconds, 1);
            var kind = ep.IsDuel
                ? ep.DuelNumber > 0 ? $"дуэль №{ep.DuelNumber} на арене" : "дуэль на арене"
                : "стычка";

            if (episodes.Count > 1)
                sb.AppendLine($"[head=3]Бой {index}[/head]");
            sb.AppendLine($"[bullet][bold]Формат:[/bold] {kind}, {(ago < 1 ? "только что" : $"{ago} мин назад")}, ~{duration} c");
            if (ep.Opponents.Count > 0)
                sb.AppendLine($"[bullet][bold]Противник:[/bold] {string.Join(", ", ep.Opponents.Take(3))}");

            // Исход — цветом: победа зелёная, поражение красное.
            var outcome = ep.Outcome ?? "бой ещё идёт";
            var outcomeColor = outcome.Contains("ПОБЕДА") ? "#3fbf5a"
                : outcome.Contains("ПОРАЖЕНИЕ") ? "#d94040" : "#c9a227";
            sb.AppendLine($"[bullet][bold]Исход:[/bold] [color={outcomeColor}]{outcome}[/color]");
            sb.AppendLine();

            // График урона: два бара в общем масштабе.
            var dealt = ep.DamageDealt.Values.Sum();
            var taken = ep.DamageTaken.Values.Sum();
            var damageMax = Math.Max(dealt, taken);
            sb.AppendLine("[bold]УРОН[/bold]");
            sb.AppendLine("[mono]");
            sb.AppendLine($"нанёс   {Bar(dealt, damageMax)} {dealt,4:0}");
            sb.AppendLine($"получил {Bar(taken, damageMax)} {taken,4:0}");
            sb.AppendLine("[/mono]");
            if (ep.DamageDealt.Count > 0)
                sb.AppendLine($"[color=#3fbf5a]отдал:[/color] {TopDamage(ep.DamageDealt)}");
            if (ep.DamageTaken.Count > 0)
                sb.AppendLine($"[color=#d94040]принял:[/color] {TopDamage(ep.DamageTaken)}");
            sb.AppendLine();

            // Точность: бар на каждое оружие + стрельба.
            if (ep.Swings.Count > 0 || ep.ShotsFired > 0)
            {
                sb.AppendLine("[bold]ТОЧНОСТЬ[/bold]");
                sb.AppendLine("[mono]");
                foreach (var (weapon, swings) in ep.Swings)
                {
                    var hits = ep.Hits.GetValueOrDefault(weapon);
                    sb.AppendLine($"{Fit(weapon, 10)} {Bar(hits, swings)} {hits}/{swings} ({Percent(hits, swings)})");
                }
                if (ep.ShotsFired > 0)
                    sb.AppendLine($"{Fit("стрельба", 10)} {Bar(ep.ShotsHit, ep.ShotsFired)} {ep.ShotsHit}/{ep.ShotsFired} ({Percent(ep.ShotsHit, ep.ShotsFired)})");
                sb.AppendLine("[/mono]");
            }

            sb.AppendLine("[color=gray]────────────────────────────[/color]");
        }

        sb.AppendLine("[italic]Сформировано портативным боевым анализатором.[/italic]");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Сухая голосовая выжимка последней дуэли — одна фраза с ключевыми цифрами
    /// («Нанёс 85, получил 120, точность мили 35%. Поражение.»). Для статистика без LLM.
    /// </summary>
    public string? GetVoiceSummary(EntityUid player)
    {
        if (!TryUser(player, out var user) || !_logs.TryGetValue(user, out var log))
            return null;

        var episodes = CollectEpisodes(log, lastOnly: true);
        if (episodes.Count == 0)
            return null;
        var ep = episodes[^1];

        var parts = new List<string>
        {
            $"нанёс {ep.DamageDealt.Values.Sum():0}, получил {ep.DamageTaken.Values.Sum():0}",
        };

        var swings = ep.Swings.Values.Sum();
        var hits = ep.Hits.Values.Sum();
        if (swings >= 3)
            parts.Add($"точность мили {Percent(hits, swings)}");
        if (ep.ShotsFired >= 3)
            parts.Add($"стрельба {Percent(ep.ShotsHit, ep.ShotsFired)}");

        var outcome = ep.Outcome ?? "";
        var verdict = outcome.Contains("ПОБЕДА") ? "Победа."
            : outcome.Contains("ПОРАЖЕНИЕ") ? "Поражение."
            : "Без исхода.";

        var duration = Math.Max((int)(ep.LastEvent - ep.Start).TotalSeconds, 1);
        // Различаем на слух: настоящая дуэль на арене или уличная стычка.
        var kind = ep.IsDuel
            ? ep.DuelNumber > 0 ? $"Дуэль №{ep.DuelNumber}" : "Дуэль"
            : "Стычка";
        return $"{kind}, {duration} секунд: {string.Join(", ", parts)}. {verdict}";
    }

    // ------------------------------------------------------------------ дуэле-центричные отчёты

    /// <summary>
    /// Все дуэли сессии (номер > 0), собранные из эпизодов ВСЕХ игроков: (номер, заголовок,
    /// подробный отчёт с разметкой). От свежих к старым. Для UI анализатора.
    /// </summary>
    public List<(int Number, string Title, string Report)> ListDuelReports()
    {
        var result = new List<(int, string, string)>();
        foreach (var group in AllDuelEpisodes().GroupBy(pair => pair.Episode.DuelNumber)
                     .OrderByDescending(g => g.Key))
        {
            var participants = group.Select(p => p.Name).Distinct().ToList();
            var last = group.Max(p => p.Episode.LastEvent);
            var ago = (int)(_timing.RealTime - last).TotalMinutes;
            var title = $"Дуэль №{group.Key} — {string.Join(" vs ", participants.Take(4))}" +
                        $" ({(ago < 1 ? "только что" : $"{ago} мин назад")})";
            result.Add((group.Key, title, BuildDuelReport(group.Key, group.ToList())));
        }
        return result;
    }

    /// <summary>Подробный отчёт по одной дуэли (null = нет такой).</summary>
    public string? GetDuelReport(int number)
    {
        var episodes = AllDuelEpisodes().Where(p => p.Episode.DuelNumber == number).ToList();
        return episodes.Count == 0 ? null : BuildDuelReport(number, episodes);
    }

    /// <summary>Все дуэльные эпизоды всех игроков (включая незакрытые текущие).</summary>
    private IEnumerable<(string Name, FightEpisode Episode)> AllDuelEpisodes()
    {
        foreach (var log in _logs.Values)
        {
            foreach (var ep in log.History)
            {
                if (ep.IsDuel && ep.DuelNumber > 0)
                    yield return (log.Name, ep);
            }
            if (log.Current is { IsDuel: true, DuelNumber: > 0 } current)
                yield return (log.Name, current);
        }
    }

    /// <summary>Отчёт по дуэли: блок на каждого участника с графиками (разметка бумаги).</summary>
    private string BuildDuelReport(int number, List<(string Name, FightEpisode Episode)> episodes)
    {
        var now = _timing.RealTime;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[head=2]ДУЭЛЬ №{number}[/head]");

        var start = episodes.Min(p => p.Episode.Start);
        var end = episodes.Max(p => p.Episode.LastEvent);
        var ago = (int)(now - end).TotalMinutes;
        var duration = Math.Max((int)(end - start).TotalSeconds, 1);
        sb.AppendLine($"[bullet]{(ago < 1 ? "только что" : $"{ago} мин назад")}, длилась ~{duration} c");
        sb.AppendLine("[color=gray]────────────────────────────[/color]");

        // Один игрок мог иметь несколько эпизодов одной дуэли (пауза) — сливаем по имени.
        foreach (var player in episodes.GroupBy(p => p.Name))
        {
            var eps = player.Select(p => p.Episode).ToList();
            var dealt = eps.Sum(e => e.DamageDealt.Values.Sum());
            var taken = eps.Sum(e => e.DamageTaken.Values.Sum());
            var swings = eps.Sum(e => e.Swings.Values.Sum());
            var hits = eps.Sum(e => e.Hits.Values.Sum());
            var shots = eps.Sum(e => e.ShotsFired);
            var shotHits = eps.Sum(e => e.ShotsHit);
            var outcome = eps.Select(e => e.Outcome).FirstOrDefault(o => o != null) ?? "без исхода";
            var outcomeColor = outcome.Contains("ПОБЕДА") ? "#3fbf5a"
                : outcome.Contains("ПОРАЖЕНИЕ") ? "#d94040" : "#c9a227";

            sb.AppendLine($"[bold]{player.Key}[/bold] — [color={outcomeColor}]{outcome}[/color]");
            var damageMax = Math.Max(dealt, taken);
            sb.AppendLine("[mono]");
            sb.AppendLine($"нанёс   {Bar(dealt, damageMax)} {dealt,4:0}");
            sb.AppendLine($"получил {Bar(taken, damageMax)} {taken,4:0}");
            if (swings > 0)
                sb.AppendLine($"мили    {Bar(hits, swings)} {hits}/{swings} ({Percent(hits, swings)})");
            if (shots > 0)
                sb.AppendLine($"стрельба {Bar(shotHits, shots)} {shotHits}/{shots} ({Percent(shotHits, shots)})");
            sb.AppendLine("[/mono]");
        }

        sb.AppendLine("[color=gray]────────────────────────────[/color]");
        sb.AppendLine("[italic]Сформировано портативным боевым анализатором.[/italic]");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Сводка сессии: сколько дуэлей и таблица бойцов (бои/победы/поражения, урон, точность).
    /// Считаются только дуэли на арене — уличные стычки в зачёт не идут.
    /// </summary>
    public string GetSessionOverview()
    {
        var all = AllDuelEpisodes().ToList();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[head=2]СВОДКА СЕССИИ[/head]");

        var duelCount = all.Select(p => p.Episode.DuelNumber).Distinct().Count();
        sb.AppendLine($"[bullet]Дуэлей сыграно: [bold]{duelCount}[/bold]");
        if (duelCount == 0)
        {
            sb.AppendLine("[italic]Пока пусто: ни одной дуэли за сессию.[/italic]");
            return sb.ToString().TrimEnd();
        }
        sb.AppendLine("[color=gray]────────────────────────────[/color]");

        sb.AppendLine("[bold]БОЙЦЫ[/bold] (бои / победы-поражения / урон нанёс-получил / точность):");
        sb.AppendLine("[mono]");
        foreach (var player in all.GroupBy(p => p.Name)
                     .OrderByDescending(g => g.Count(p => p.Episode.Outcome?.Contains("ПОБЕДА") == true)))
        {
            var eps = player.Select(p => p.Episode).ToList();
            var duels = eps.Select(e => e.DuelNumber).Distinct().Count();
            var wins = eps.Count(e => e.Outcome?.Contains("ПОБЕДА") == true);
            var losses = eps.Count(e => e.Outcome?.Contains("ПОРАЖЕНИЕ") == true);
            var dealt = eps.Sum(e => e.DamageDealt.Values.Sum());
            var taken = eps.Sum(e => e.DamageTaken.Values.Sum());
            var swings = eps.Sum(e => e.Swings.Values.Sum());
            var hits = eps.Sum(e => e.Hits.Values.Sum());
            var shots = eps.Sum(e => e.ShotsFired);
            var shotHits = eps.Sum(e => e.ShotsHit);
            var accuracy = swings + shots > 0 ? Percent(hits + shotHits, swings + shots) : "—";

            sb.AppendLine($"{Fit(player.Key, 14)} {duels,2} боя  {wins}П/{losses}п  " +
                          $"{dealt,4:0}/{taken,4:0}  {accuracy}");
        }
        sb.AppendLine("[/mono]");
        sb.AppendLine("[italic]П — победы, п — поражения. Сформировано боевым анализатором.[/italic]");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Псевдографический бар: █ — заполнено, ░ — пусто.</summary>
    private static string Bar(double value, double max, int width = 12)
    {
        if (max <= 0)
            return new string('░', width);
        var filled = (int)Math.Round(width * Math.Clamp(value / max, 0, 1));
        return new string('█', filled) + new string('░', width - filled);
    }

    /// <summary>Топ-3 типа урона: «Slash 60, Blunt 25».</summary>
    private static string TopDamage(Dictionary<string, double> damage)
        => string.Join(", ", damage.OrderByDescending(kv => kv.Value).Take(3)
            .Select(kv => $"{kv.Key} {kv.Value:0}"));

    /// <summary>Подгоняет имя под ширину моноколонки (обрезка с многоточием / паддинг).</summary>
    private static string Fit(string name, int width)
        => name.Length > width ? name[..(width - 1)] + "…" : name.PadRight(width);

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
