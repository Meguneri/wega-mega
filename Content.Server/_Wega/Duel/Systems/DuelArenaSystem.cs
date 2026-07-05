using Content.Server._Wega.Duel.Components;
using Content.Server.Chat.Managers;
using Content.Server.DeviceLinking.Systems;
using Content.Shared._Wega.Clothing.Sandevistan;
using Content.Shared._Wega.Duel;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Administration.Systems;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Magic.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Трекер дуэльной арены. По сигналу старта запоминает бойцов в зоне; когда один теряет
/// сознание (крит/смерть) — объявляет победителя на весь сервер. Сигнал закрытия шлюзов
/// (порт <see cref="DuelArenaComponent.ResetPort"/>) отправляется не сразу, а спустя
/// <see cref="DuelArenaComponent.ReturnGrace"/> секунд — чтобы дуэлянты успели вернуться в базы.
/// </summary>
public sealed class DuelArenaSystem : EntitySystem
{
    [Dependency] private DeviceLinkSystem _signalSystem = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private DuelArenaCleanupSystem _cleanup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private RejuvenateSystem _rejuvenate = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private DuelRotationSystem _rotation = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private DuelReadySystem _readySystem = default!;
    [Dependency] private DuelArenaRestoreSystem _restoreSystem = default!;
    [Dependency] private ArenaLoadoutSystem _loadoutSystem = default!;
    [Dependency] private ArenaStormSystem _stormSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DuelArenaComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<DuelArenaComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<DuelArenaComponent, RotationRoundStartEvent>(OnRotationRoundStart);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnInit(EntityUid uid, DuelArenaComponent comp, ComponentInit args)
    {
        _signalSystem.EnsureSinkPorts(uid, "Open", "Toggle");
        _signalSystem.EnsureSourcePorts(uid, comp.ResetPort);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<DuelArenaComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // Отложенное восстановление стен: запланировано в ConcludeDuel/ResetDuel, выполняем
            // здесь — на тике ПОСЛЕ завершения боя, вне стека события смерти (MobStateChanged).
            // Удаление/спавн стен прямо из обработчика смертельного удара могло конфликтовать
            // с обработкой урона и срывать восстановление.
            if (comp.PendingWallRestore)
            {
                comp.PendingWallRestore = false;
                _restoreSystem.RestoreWalls(uid, comp);
                _restoreSystem.RestoreGrilles(uid, comp);
                _restoreSystem.RestoreLights(uid, comp);
            }

            // Отложенное вооружение раунда ротации: бойцы перенесены на эту арену на прошлом тике,
            // теперь грид/состояние осели — ArmDuel корректно увидит их и объявит «дуэль начата».
            if (comp.PendingRotationArm)
            {
                comp.PendingRotationArm = false;
                ArmDuel(uid, comp);
            }

            // Истёк grace-период после боя — шлём на шлюзы баз сигнал закрытия.
            // Дуэлянты уже успели вернуться по открытым шлюзам.
            // (Восстановление стен НЕ привязано к этому таймеру — оно выполняется сразу при
            // завершении боя в ConcludeDuel/ResetDuel, иначе при быстром старте нового боя
            // grace отменяется и стены чинятся хаотично уже во время следующего раунда.)
            if (comp.GateCloseAt != null && now >= comp.GateCloseAt)
            {
                comp.GateCloseAt = null;
                _signalSystem.SendSignal(uid, comp.ResetPort, true);
            }

            // Авто-дроп снабжения во время активного боя: сбрасываем маяк в центр арены
            // (он сам даёт колокол/свет и спавнит ящик), затем перепланируем по интервалу.
            if (comp.IsActive && comp.SupplyDropProto != null
                && comp.SupplyDropAt != null && now >= comp.SupplyDropAt)
            {
                Spawn(comp.SupplyDropProto.Value, Transform(uid).Coordinates);
                comp.SupplyDropAt = comp.SupplyDropInterval > 0f
                    ? now + TimeSpan.FromSeconds(comp.SupplyDropInterval)
                    : null;
            }

            // Таймер боя: истёк основной лимит — запускаем внезапную смерть или фиксируем ничью.
            if (comp.IsActive && comp.FightEndAt is { } fightEnd && now >= fightEnd)
            {
                comp.FightEndAt = null;
                if (comp.SuddenDeathDuration > 0)
                {
                    StartSuddenDeath(uid, comp);
                }
                else
                {
                    ConcludeDuel(uid, comp, forceDraw: true);
                    continue;
                }
            }

            // Таймер внезапной смерти: истёк — дуэль заканчивается вничью.
            if (comp.SuddenDeathActive && comp.SuddenDeathEndAt is { } sdEnd && now >= sdEnd)
            {
                ConcludeDuel(uid, comp, forceDraw: true);
                continue;
            }

            // Сканируем только вооружённые арены — чтобы вовремя снять взвод,
            // если дуэлянты разошлись живыми и победа в OnMobStateChanged не наступит.
            if (!comp.IsActive || now < comp.NextScan)
                continue;

            comp.NextScan = now + TimeSpan.FromSeconds(comp.ScanInterval);
            Scan(uid, comp);
        }
    }

    /// <summary>
    /// Периодическая проверка исхода боя. Подстраховывает событие крит/смерти: если его не
    /// поймали вовремя и в живых остался ≤1 дуэлянт — объявляем итог здесь. Если оба ещё живы,
    /// но никого из них нет в зоне — дуэль заброшена, тихо снимаем взвод (без победителя).
    /// </summary>
    private void Scan(EntityUid uid, DuelArenaComponent comp)
    {
        // Присутствующие на арене бойцы (живые ИЛИ в криту/мертвы, но ещё не исчезли).
        var present = comp.Duelists.Where(d => OnArena(uid, d)).ToList();

        // Никого не осталось (все ушли с арены либо удалены/гибнуты без итога) — дуэль заброшена,
        // тихо снимаем взвод без объявления победителя.
        if (present.Count == 0)
        {
            ResetDuel(uid, comp);
            return;
        }

        // На ногах остался ≤1 из присутствующих — подводим итог (победа выжившего или ничья,
        // если оба слегли). ConcludeDuel сам определит победителя/ничью.
        var standing = present.Count(d => !_mobState.IsIncapacitated(d));
        if (standing <= 1)
            ConcludeDuel(uid, comp);
    }

    /// <summary>
    /// Присутствует ли дуэлянт на арене: существует (не гибнут/не удалён), имеет состояние мобa
    /// и находится на гриде трекера. Уход с арены = исчезновение участника.
    /// </summary>
    private bool OnArena(EntityUid arenaUid, EntityUid d)
    {
        if (!Exists(d) || !HasComp<MobStateComponent>(d))
            return false;
        var trackerGrid = Transform(arenaUid).GridUid;
        return trackerGrid == null || Transform(d).GridUid == trackerGrid;
    }

    /// <summary>
    /// «Ещё в бою» ли дуэлянт. Боец выбывает при лежачем крите, смерти, гибе/исчезновении или
    /// уходе с арены. Предкрит (PreCritical) НЕ выводит из боя — дуэль продолжается.
    /// </summary>
    private bool IsActiveFighter(EntityUid arenaUid, EntityUid d)
        => OnArena(arenaUid, d) && !_mobState.IsIncapacitated(d);

    private string SafeName(EntityUid uid)
        => Exists(uid) ? MetaData(uid).EntityName : "?";

    /// <summary>
    /// Собирает живых дуэлянтов-гуманоидов на арене. Арена — отдельный грид, поэтому охватываем
    /// весь грид трекера целиком (без ограничения радиусом): это покрывает всю арену и не цепляет
    /// станцию. Если трекер не на гриде (в космосе) — откатываемся на радиус <see cref="DuelArenaComponent.ScanRange"/>.
    /// </summary>
    private HashSet<EntityUid> GetAliveInRange(EntityUid uid, DuelArenaComponent comp)
    {
        var trackerXform = Transform(uid);
        var trackerPos = trackerXform.MapPosition;
        var trackerGrid = trackerXform.GridUid;

        var alive = new HashSet<EntityUid>();
        var mobQuery = EntityQueryEnumerator<MobStateComponent, HumanoidProfileComponent>();
        while (mobQuery.MoveNext(out var mobUid, out _, out _))
        {
            var mobXform = Transform(mobUid);

            if (trackerGrid != null)
            {
                // Весь грид арены — без радиуса.
                if (mobXform.GridUid != trackerGrid)
                    continue;
            }
            else
            {
                // Космос/без грида: запасной охват по дистанции.
                var mobPos = mobXform.MapPosition;
                if (mobPos.MapId != trackerPos.MapId)
                    continue;
                if ((mobPos.Position - trackerPos.Position).Length() > comp.ScanRange)
                    continue;
            }
            // Бойцами считаются и игроки, и гуманоидные NPC (например, синдикатские пехотинцы):
            // дуэль может идти против мобов. Выбытие любого из них (крит/смерть/гиб/уход) учтётся
            // в IsActiveFighter — поэтому лишних «вечно живых» бойцов это уже не создаёт.
            if (_mobState.IsAlive(mobUid))
                alive.Add(mobUid);
        }

        return alive;
    }

    /// <summary>
    /// Вооружает дуэль: запоминает живых бойцов в арене на момент старта.
    /// Вызывается по сигналу кнопки старта (порт Open), а не по подсчёту присутствующих.
    /// </summary>
    private void ArmDuel(EntityUid uid, DuelArenaComponent comp)
    {
        // Сигнал старта может прийти повторно (двойное нажатие/повтор сигнала). Если дуэль уже
        // идёт — молча игнорируем, чтобы не дублировать объявление «Дуэль началась».
        if (comp.IsActive)
            return;

        comp.PendingWallRestore = false;

        var duelists = GetAliveInRange(uid, comp);
        if (duelists.Count < 2)
        {
            _chatManager.DispatchServerAnnouncement(
                Loc.GetString(duelists.Count == 0
                    ? "duel-arena-not-started-no-fighters"
                    : "duel-arena-not-started-need-two"),
                Color.Gray);
            return;
        }

        comp.Duelists.Clear();
        foreach (var d in duelists)
            comp.Duelists.Add(d);
        comp.IsActive = true;

        // Пока стены ещё целы (бой только начинается) — мержим их планировку в снимок,
        // чтобы после дуэли восстановить разрушенное. Мерж на каждом старте: новые тайлы
        // добавляются, старые не перезаписываются — снимок самовосстанавливается, даже
        // если какой-то из проходов вышел неполным.
        _restoreSystem.SnapshotWalls(uid, comp);
        _restoreSystem.SnapshotGrilles(uid, comp);
        _restoreSystem.SnapshotLights(uid, comp);

        // Отменяем grace-период предыдущей дуэли — иначе Update отправит сигнал закрытия
        // шлюзов уже во время нового боя.
        comp.GateCloseAt = null;
        _signalSystem.SendSignal(uid, comp.ResetPort, false);

        // Планируем первый авто-дроп снабжения (если включён для этой арены).
        comp.SupplyDropAt = comp.SupplyDropProto != null
            ? _timing.CurTime + TimeSpan.FromSeconds(comp.SupplyDropDelay)
            : null;

        // Планируем лимит основного времени боя. Внезапная смерть (если задана) запустится позже.
        comp.FightEndAt = comp.MaxFightDuration > 0f
            ? _timing.CurTime + TimeSpan.FromSeconds(comp.MaxFightDuration)
            : null;
        comp.SuddenDeathEndAt = null;
        comp.SuddenDeathActive = false;

        var vsSep = $" {Loc.GetString("duel-arena-connector-vs")} ";
        var names = string.Join(vsSep, comp.Duelists.Select(d => MetaData(d).EntityName));
        _chatManager.DispatchServerAnnouncement(
            Loc.GetString("duel-arena-started", ("fighters", names)), Color.Gold);

        // Выдаём выбранные наборы снаряжения каждому дуэлянту.
        _loadoutSystem.EquipDuelists(comp.Duelists);

        // Звук старта дуэли играет штатный DuelStartSoundEmitter на карте
        // (EmitGlobalSoundOnSignal по сигналу DuelFight) — здесь дублировать не нужно.
    }

    /// <summary>
    /// Режим ротации: контроллер перенёс бойцов на эту арену и просит вооружить раунд.
    /// Вооружение НЕ выполняется сразу: перенос и полное исцеление проигравшего происходят
    /// синхронно в ConcludeDuel, когда грид/состояние ещё не «осели». Ставим флаг и вооружаем
    /// на следующем тике в Update, когда GetAliveInRange уже корректно увидит прибывших бойцов
    /// и объявит старт.
    /// </summary>
    private void OnRotationRoundStart(EntityUid uid, DuelArenaComponent comp, RotationRoundStartEvent args)
    {
        comp.PendingRotationArm = true;
    }

    private void OnSignalReceived(EntityUid uid, DuelArenaComponent comp, ref SignalReceivedEvent args)
    {
        switch (args.Port)
        {
            // Сигнал старта (после отсчёта таймера) — вооружаем дуэль.
            case "Open":
                // Дебаунс: один импульс старта может прийти дважды за короткое время (двойная
                // линковка/фронты сигнала/несколько передатчиков на канале). Без этого объявление
                // «нужно минимум 2 бойца» дублировалось бы в чате. Успешный старт и так защищён
                // проверкой IsActive в ArmDuel, а здесь гасим и повтор неудачной попытки.
                var now = _timing.CurTime;
                if (comp.LastStartSignal is { } last && now - last < TimeSpan.FromSeconds(0.5))
                    break;
                comp.LastStartSignal = now;
                ArmDuel(uid, comp);
                break;
            // Ручной сброс текущего боя (кнопка сброса). Накопленный счёт не трогает —
            // его обнуляет только админ-команда duelscorereset.
            case "Toggle":
                ResetDuel(uid, comp);
                break;
        }
    }

    private void ResetDuel(EntityUid uid, DuelArenaComponent comp)
    {
        comp.Duelists.Clear();
        comp.IsActive = false;

        // Сброс ready-check: убираем готовность и голограммы «ГОТОВ».
        _readySystem.ClearReady(comp);

        // Останавливаем авто-дроп снабжения до следующего боя.
        comp.SupplyDropAt = null;

        // Сбрасываем таймеры боя и внезапной смерти.
        comp.FightEndAt = null;
        comp.SuddenDeathEndAt = null;
        comp.SuddenDeathActive = false;

        // Шлюзы закроем через ReturnGrace секунд — чтобы бойцы успели вернуться в свои базы.
        comp.GateCloseAt = _timing.CurTime + TimeSpan.FromSeconds(comp.ReturnGrace);

        // Стены чиним на следующем тике (см. Update) — вне стека текущего события,
        // чтобы арена была целой к следующему раунду.
        comp.PendingWallRestore = true;
    }

    /// <summary>
    /// Запускает фазу «внезапной смерти»: форсирует сужение шторма (если он есть на трекере) и
    /// планирует окончательную ничью через <see cref="DuelArenaComponent.SuddenDeathDuration"/>.
    /// </summary>
    private void StartSuddenDeath(EntityUid uid, DuelArenaComponent comp)
    {
        comp.SuddenDeathActive = true;
        comp.SuddenDeathEndAt = _timing.CurTime + TimeSpan.FromSeconds(comp.SuddenDeathDuration);

        if (TryComp<ArenaStormComponent>(uid, out var storm) && storm.Enabled && !storm.Active)
            _stormSystem.ForceStartStorm(uid, storm);

        _chatManager.DispatchServerAnnouncement(
            Loc.GetString("duel-arena-sudden-death"), Color.OrangeRed);
    }

    /// <summary>
    /// Обнуляет накопленный счёт на всех дуэльных аренах. Вызывается админ-командой duelscorereset.
    /// Возвращает число арен, на которых счёт был непустым.
    /// </summary>
    public int ResetAllScores()
    {
        var cleared = 0;
        var query = EntityQueryEnumerator<DuelArenaComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.Scores.Count == 0)
                continue;

            comp.Scores.Clear();
            comp.ScoreNames.Clear();
            comp.StreakUser = null;
            comp.Streak = 0;
            cleared++;
        }

        // Счёт босс-арен тоже обнуляем.
        var bossQuery = EntityQueryEnumerator<BossArenaComponent>();
        while (bossQuery.MoveNext(out _, out var boss))
        {
            if (boss.Scores.Count == 0)
                continue;

            boss.Scores.Clear();
            boss.ScoreNames.Clear();
            boss.StreakUser = null;
            boss.Streak = 0;
            cleared++;
        }

        // Общий счёт контроллеров ротации — тоже обнуляем.
        var rotQuery = EntityQueryEnumerator<DuelRotationComponent>();
        while (rotQuery.MoveNext(out _, out var rot))
        {
            if (rot.Scores.Count == 0)
                continue;

            rot.Scores.Clear();
            rot.ScoreNames.Clear();
            rot.StreakUser = null;
            rot.Streak = 0;
            cleared++;
        }

        if (cleared > 0)
            _chatManager.DispatchServerAnnouncement(Loc.GetString("duel-arena-scores-reset"), Color.Gold);

        return cleared;
    }

    /// <summary>
    /// Возвращает идентификатор игрока, управляющего телом, или null для тел без разума (NPC).
    /// </summary>
    public NetUserId? GetUser(EntityUid body)
    {
        return _mind.TryGetMind(body, out _, out var mind) ? mind.UserId : null;
    }

    /// <summary>
    /// Собирает строку общего счёта: «Имя — N», сортировка по убыванию побед, затем по имени.
    /// Источник — одиночная арена или контроллер ротации (см. <see cref="IDuelScoreStore"/>).
    /// Возвращает null, если счёта ещё нет.
    /// </summary>
    public string? BuildScoreboard(IDuelScoreStore store)
    {
        if (store.Scores.Count == 0)
            return null;

        var entries = store.Scores
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => store.ScoreNames.GetValueOrDefault(kv.Key, "?"))
            .Select(kv => $"{store.ScoreNames.GetValueOrDefault(kv.Key, "?")} — {kv.Value}");

        return string.Join(", ", entries);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Critical && args.NewMobState != MobState.Dead)
            return;

        var uid = args.Target;
        var query = EntityQueryEnumerator<DuelArenaComponent>();
        while (query.MoveNext(out var arenaUid, out var arena))
        {
            if (!arena.IsActive || !arena.Duelists.Contains(uid))
                continue;

            ConcludeDuel(arenaUid, arena);
            break;
        }
    }

    /// <summary>
    /// Подводит итог дуэли, если в живых остался ≤1 дуэлянт: объявляет победителя (или ничью),
    /// начисляет счёт, убирает выданное снаряжение и запускает grace-период закрытия шлюзов.
    /// Если живых ещё ≥2 — ничего не делает (бой продолжается). Идемпотентна за счёт IsActive.
    /// </summary>
    private bool ConcludeDuel(EntityUid arenaUid, DuelArenaComponent arena)
        => ConcludeDuel(arenaUid, arena, forceDraw: false);

    /// <summary>
    /// Подводит итог дуэли. Если <paramref name="forceDraw"/> — считает ничьёй независимо от
    /// числа живых бойцов (используется по таймауту). Иначе — стандартная логика победителя/ничьей.
    /// </summary>
    private bool ConcludeDuel(EntityUid arenaUid, DuelArenaComponent arena, bool forceDraw)
    {
        if (!arena.IsActive)
            return false;

        var aliveDuelists = arena.Duelists.Where(d => IsActiveFighter(arenaUid, d)).ToList();
        if (!forceDraw && aliveDuelists.Count > 1)
            return false; // бой ещё идёт

        arena.IsActive = false;

        // Готовность к этому бою больше не нужна — убираем остатки ready-check (на всякий случай).
        _readySystem.ClearReady(arena);

        // Останавливаем авто-дроп снабжения — бой окончен.
        arena.SupplyDropAt = null;

        // Таймеры боя больше не нужны.
        arena.FightEndAt = null;
        arena.SuddenDeathEndAt = null;
        arena.SuddenDeathActive = false;

        // Куда писать счёт: одиночная арена ведёт его сама; в режиме ротации — общий счёт на
        // контроллере. Развилка по флагу RotationController (пусто = старое поведение).
        DuelRotationComponent? ctrl = null;
        var inRotation = arena.RotationController is { } ctrlUid
            && TryComp(ctrlUid, out ctrl);
        IDuelScoreStore store = inRotation ? ctrl! : arena;

        // Состав боя фиксируем до очистки списка — нужен для перехода на следующую арену.
        var roundDuelists = arena.Duelists.ToList();

        EntityUid? winner = aliveDuelists.Count == 1 ? aliveDuelists[0] : null;

        // Запоминаем актуальные имена всех бойцов этого боя по их NetUserId — чтобы общий счёт
        // ниже отображался с именами, даже если кто-то из них не участвует в следующих раундах.
        foreach (var duelist in arena.Duelists)
        {
            var user = GetUser(duelist);
            if (user != null)
                store.ScoreNames[user.Value] = SafeName(duelist);
        }

        string msg;
        if (winner != null)
        {
            var winnerName = SafeName(winner.Value);

            // Проигравшие — все остальные зарегистрированные бойцы (для дуэлей 3+ их несколько).
            var losers = arena.Duelists.Where(d => d != winner.Value).ToList();
            var loserNames = losers.Count > 0
                ? string.Join(", ", losers.Select(SafeName))
                : Loc.GetString("duel-arena-losers-fallback");

            // Счёт ведём по игроку (NetUserId), а не по телу: иначе после клона/респавна
            // боец получает новый EntityUid и счёт каждый раунд начинается заново.
            var winnerUser = GetUser(winner.Value);

            if (winnerUser != null)
                store.Scores[winnerUser.Value] = store.Scores.GetValueOrDefault(winnerUser.Value) + 1;

            // Серия побед подряд: растёт, если победил тот же игрок, иначе начинается заново.
            if (winnerUser != null && store.StreakUser == winnerUser)
                store.Streak++;
            else
            {
                store.StreakUser = winnerUser;
                store.Streak = 1;
            }

            msg = Loc.GetString("duel-arena-concluded-winner",
                ("winner", winnerName),
                ("streak", store.Streak),
                ("losers", loserNames),
                ("loserCount", losers.Count));
        }
        else
        {
            // Никого живого — ничья: серия прерывается.
            store.StreakUser = null;
            store.Streak = 0;

            var andSep = $" {Loc.GetString("duel-arena-connector-and")} ";
            var names = string.Join(andSep, arena.Duelists.Select(SafeName));
            msg = Loc.GetString("duel-arena-concluded-draw", ("fighters", names));
        }

        // Полное исцеление обоих участников по завершении дуэли (поднимает из крита, чинит весь урон).
        foreach (var duelist in arena.Duelists)
        {
            if (!Exists(duelist))
                continue;

            _rejuvenate.PerformRejuvenate(duelist);
            // Принудительно пересчитываем модификаторы скорости: иначе замедление от
            // SlowOnDamage, навешенное в крите, может остаться закешированным после
            // полного исцеления (переход из крита гонится с обновлением модификатора).
            _movementSpeed.RefreshMovementSpeedModifiers(duelist);
            PurgeDuelistTraces(duelist);
        }

        arena.Duelists.Clear();

        // Дописываем общий накопленный счёт (одиночная арена или контроллер ротации).
        var scoreboard = BuildScoreboard(store);
        if (scoreboard != null)
            msg += "\n" + Loc.GetString("duel-arena-scoreboard", ("scores", scoreboard));

        // Убираем снаряжение и объявляем результат одним сообщением.
        _cleanup.CleanupArea(arenaUid, arena.CleanupRange);
        _chatManager.DispatchServerAnnouncement(msg, Color.Gold);

        // Сигнал завершения дуэли — играем для всех бойцов раунда (список снят до очистки).
        if (arena.EndSound != null)
            foreach (var d in roundDuelists)
                if (Exists(d))
                    _audio.PlayPvs(arena.EndSound, d);

        // Восстанавливаем разрушенные за бой стены на следующем тике (см. Update): сразу по
        // завершении, но вне стека события смерти — удаление/спавн стен из обработчика
        // смертельного удара могло срывать восстановление. RestoreWalls сам отодвигает
        // бойцов с тайлов под стенами, так что никого не зажмёт.
        arena.PendingWallRestore = true;

        // Сигнал закрытия шлюзов шлём не сразу, а через ReturnGrace секунд: дуэлянты
        // возвращаются в базы по открытым шлюзам, и только потом те закрываются (см. Update).
        arena.GateCloseAt = _timing.CurTime + TimeSpan.FromSeconds(arena.ReturnGrace);

        // Режим ротации: переносим бойцов на следующую арену и запускаем там раунд. Бойцы уже
        // исцелены выше, прошлая арена восстановится на следующем тике (на ней уже никого не будет).
        if (inRotation)
            _rotation.AdvanceToNextArena((arena.RotationController!.Value, ctrl!), roundDuelists);

        return true;
    }

    /// <summary>
    /// Снимает с дуэлянта «следы» боя, которые не убирает обычное исцеление:
    /// — «критовые» действия (последнее слово / сдаться / притвориться мёртвым): переход из
    ///   крита их обычно снимает, но ревайв через Rejuvenate может оставить их висеть в тулбаре;
    /// — магические спеллы из гримуара (любое action со <see cref="MagicComponent"/>).
    /// Руны (со свитка рун и культа) чистятся отдельно в <c>CleanupArea</c> по зоне арены.
    /// </summary>
    private void PurgeDuelistTraces(EntityUid duelist)
    {
        // Активный сандэвистан: если раунд кончился, пока он действует, баф (скорость + глобальный
        // bullet-time) висит на бойце и уехал бы с ним на следующую арену, замедляя нового соперника.
        // Снимаем его и пересчитываем скорость. Заодно снимаем замок оружия арена-версии — иначе
        // после удаления очков боец остался бы с запретом на оружие.
        if (HasComp<SandevistanActiveComponent>(duelist))
        {
            RemComp<SandevistanActiveComponent>(duelist);
            _movementSpeed.RefreshMovementSpeedModifiers(duelist);
        }

        // Замок оружия снимаем ТОЛЬКО если арена-сандэвистан уже не надет: иначе боец, оставшийся
        // в очках на следующий раунд, потерял бы запрет и смог бы взять оружие/броню прямо в них.
        // Когда очки реально снимают/удаляют, замок убирает обработчик ClothingGotUnequipped; здесь
        // лишь подчищаем «осиротевший» замок, переживший удаление очков.
        if (!IsWearingArenaSandevistan(duelist))
            RemComp<ArenaWeaponLockComponent>(duelist);

        if (TryComp<MobStateActionsComponent>(duelist, out var mobActions))
        {
            foreach (var act in mobActions.GrantedActions)
                Del(act);
            mobActions.GrantedActions.Clear();
        }

        // Спеллы из гримуара покупаются в магазине и кладутся в контейнер действий РАЗУМА
        // (ActionsContainer разума), а к телу лишь «прикрепляются». Поэтому простой RemoveAction
        // (отвязка) их не убирает: сущность-действие остаётся в разуме и возвращается на тело.
        // Собираем все магические действия, привязанные к телу или его разуму, и УДАЛЯЕМ сами
        // сущности — тогда они исчезают и из разума.
        EntityUid? mindUid = _mind.TryGetMind(duelist, out var mind, out _) ? mind : null;

        var spells = new List<EntityUid>();
        var query = EntityQueryEnumerator<MagicComponent, ActionComponent>();
        while (query.MoveNext(out var actionUid, out _, out var action))
        {
            if (action.AttachedEntity == duelist || (mindUid != null && action.AttachedEntity == mindUid))
                spells.Add(actionUid);
        }

        foreach (var spell in spells)
        {
            _actions.RemoveAction(spell);
            if (!Deleted(spell))
                QueueDel(spell);
        }
    }

    /// <summary>
    /// Носит ли боец сейчас арена-версию сандэвистана (очки с <see cref="SandevistanArenaLockComponent"/>).
    /// Пока носит — замок оружия должен оставаться, чтобы в очках нельзя было взять оружие или броню.
    /// </summary>
    private bool IsWearingArenaSandevistan(EntityUid duelist)
    {
        if (!_inventory.TryGetContainerSlotEnumerator(duelist, out var slots))
            return false;

        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is { } worn && HasComp<SandevistanArenaLockComponent>(worn))
                return true;
        }

        return false;
    }
}
