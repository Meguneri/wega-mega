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
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Linq;
using System.Numerics;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Трекер дуэльной арены. По сигналу старта запоминает бойцов в зоне; когда один теряет
/// сознание (крит/смерть) — объявляет победителя на весь сервер. Сигнал закрытия шлюзов
/// (порт <see cref="DuelArenaComponent.ResetPort"/>) отправляется не сразу, а спустя
/// <see cref="DuelArenaComponent.ReturnGrace"/> секунд — чтобы дуэлянты успели вернуться в базы.
/// </summary>
public sealed partial class DuelArenaSystem : EntitySystem
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
    [Dependency] private ArenaLoserMinionSystem _minionSystem = default!;
    [Dependency] private DuelArenaRestoreSystem _restoreSystem = default!;
    [Dependency] private ArenaStormSystem _stormSystem = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private DuelArenaScoreSystem _score = default!;
    [Dependency] private IRobustRandom _random = default!;

    // Восемь соседних тайлов вокруг спавн-маркера (радиус 1). Крейт кладём на случайный из них,
    // но не на сам маркер, чтобы боец не заспавнился внутри ящика.
    private static readonly Vector2[] ArsenalCrateOffsets =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    /// <summary>
    /// Спавнит текущий арсенал-крейт (<see cref="DuelArenaComponent.ArsenalCrate"/>) у каждого
    /// спавн-маркера арены — на случайном соседнем тайле. Крейт помечен markIssuedItems, поэтому
    /// очистка арены снесёт его после боя.
    ///
    /// TODO(arena-arsenal-crates): ВРЕМЕННО НЕ ВЫЗЫВАЕТСЯ (единственный вызов в ArmDuel закомментирован) —
    /// система работает некорректно: ящики спавнятся по нажатию кнопок (из ArmDuel), а не по концу дуэли /
    /// при подготовке раунда. Метод оставлен как есть для доработки тайминга; выдаваемая им снаряга
    /// удаляется корректно, чинить надо именно МОМЕНТ спавна. После переработки — вернуть вызов в ArmDuel.
    /// </summary>
    private void SpawnArsenalCrates(EntityUid arenaUid, DuelArenaComponent comp)
    {
        if (comp.ArsenalCrate is not { } crateProto)
            return;

        var arenaGrid = Transform(arenaUid).GridUid;
        var query = EntityQueryEnumerator<DuelArenaSpawnComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid != arenaGrid)
                continue;

            var coords = xform.Coordinates.Offset(_random.Pick(ArsenalCrateOffsets));
            Spawn(crateProto, coords);
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DuelArenaComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<DuelArenaComponent, SignalReceivedEvent>(OnSignalReceived);
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
            // Отложенное восстановление арены: запланировано в ConcludeDuel/ResetDuel, выполняем
            // здесь — вне стека события смерти (MobStateChanged), где удаление/спавн сущностей могли
            // конфликтовать с обработкой урона. Ждём PendingRestoreAt: смертельный удар мог быть от
            // отложенной взрывчатки (граната/заряд), чей взрыв обрабатывается уже ПОСЛЕ конца дуэли —
            // без задержки восстановление прошло бы по ещё целой арене, а разрушение осталось бы навсегда.
            if (comp.PendingRestore && now >= comp.PendingRestoreAt)
            {
                comp.PendingRestore = false;
                comp.PendingRestoreAt = null;
                _restoreSystem.RestoreArena(uid, comp);

                // Повторное исцеление бойцов после оседания взрывов — возвращает конечности,
                // оторванные отложенной взрывчаткой уже после конца боя (см. PendingHealDuelists).
                foreach (var duelist in comp.PendingHealDuelists)
                {
                    if (!Exists(duelist))
                        continue;
                    _rejuvenate.PerformRejuvenate(duelist);
                    _movementSpeed.RefreshMovementSpeedModifiers(duelist);
                }
                comp.PendingHealDuelists.Clear();
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
        var trackerPos = _transform.GetMapCoordinates(trackerXform);
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
                var mobPos = _transform.GetMapCoordinates(mobXform);
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

        comp.PendingRestore = false;
        comp.PendingRestoreAt = null;
        comp.PendingHealDuelists.Clear();

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

        // Пока арена ещё цела (бой только начинается) — снимаем её эталон (пол + конструкции +
        // свободные предметы + декали), чтобы после дуэли восстановить всё разрушенное. Реально
        // снимок берётся ровно один раз — при первом старте на нетронутой арене (см. SnapshotArena).
        _restoreSystem.SnapshotArena(uid, comp);

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

        // TODO(arena-arsenal-crates): система спавна арсенал-крейтов ОТКЛЮЧЕНА — работает некорректно.
        // Сейчас SpawnArsenalCrates дёргается отсюда, из ArmDuel (т.е. по нажатию кнопки старта/готовности),
        // из-за чего ящики появляются «после нажатия кнопок», а не в нужный момент раунда (по концу дуэли /
        // при подготовке следующего раунда). Возможный побочный эффект — из-за этого ломается общая логика
        // раунда. Снаряжение, которое ящики выдают, при этом удаляется корректно. Переработать тайминг
        // (перенести спавн в нужную фазу) и снова включить вызов.
        //
        // TODO(arena-arsenal-crates): целевое поведение — ящики спавнятся ЧЕРЕЗ ПУЛЬТ (арсенал-ремоут,
        // см. ArenaArsenalRemoteSystem), а НЕ вручную/по нажатию кнопки старта. При этом спавн через пульт
        // НЕ должен ломать текущую систему дуэлей (тайминг раунда, очистку и восстановление арены).
        // SpawnArsenalCrates(uid, comp);

        // Усиление для проигравшего 3 раза подряд: миньон-помощник.
        DuelRotationComponent? ctrl = null;
        var inRotation = comp.RotationController is { } ctrlUid
            && TryComp(ctrlUid, out ctrl);
        IDuelScoreStore store = inRotation ? ctrl! : comp;

        foreach (var duelist in comp.Duelists)
        {
            var user = _score.GetUser(duelist);
            if (user == null)
                continue;

            var streak = store.LosingStreaks.GetValueOrDefault(user.Value);
            if (streak < 3)
                continue;

            var coords = Transform(duelist).Coordinates;
            _minionSystem.SpawnMinion(duelist, coords, comp.Duelists);
            _chatManager.DispatchServerAnnouncement(
                Loc.GetString("duel-arena-loser-minion-spawned", ("name", SafeName(duelist))),
                Color.Pink);
        }

        // Звук старта дуэли играет штатный DuelStartSoundEmitter на карте
        // (EmitGlobalSoundOnSignal по сигналу DuelFight) — здесь дублировать не нужно.
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

        // Убираем выданное снаряжение — как и в ConcludeDuel. Без этого раунд, завершившийся сбросом
        // (уход бойцов с арены → Scan, ручная кнопка Toggle, таймаут), НЕ чистил гир: восстановление шло
        // по PendingRestore, а очистка вызывалась только в ConcludeDuel — надетое/выданное переживало.
        // На этом тике бойцы ещё на гриде арены (ротация не переносила их), поэтому надетое попадёт в зону.
        _cleanup.CleanupArea(uid, comp.CleanupRange);

        // Арену восстанавливаем с задержкой (см. Update / PendingRestoreAt) — вне стека текущего события
        // и после оседания отложенных взрывов, чтобы она была целой к следующему раунду.
        comp.PendingRestore = true;
        comp.PendingRestoreAt = _timing.CurTime + TimeSpan.FromSeconds(comp.RestoreDelay);
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
        var cleared = _score.ResetAllScores();
        if (cleared > 0)
            _chatManager.DispatchServerAnnouncement(Loc.GetString("duel-arena-scores-reset"), Color.Gold);
        return cleared;
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

        // При forceDraw (таймаут / внезапная смерть) исход всегда ничья, даже если формально остался
        // один «активный» боец: второй мог отойти за пределы грида арены. Иначе таймаут присуждал бы
        // победу вопреки параметру forceDraw и комментарию StartSuddenDeath («ничья»).
        EntityUid? winner = !forceDraw && aliveDuelists.Count == 1 ? aliveDuelists[0] : null;

        string msg;
        if (winner != null)
        {
            var winnerName = SafeName(winner.Value);

            // Проигравшие — все остальные зарегистрированные бойцы (для дуэлей 3+ их несколько).
            var losers = arena.Duelists.Where(d => d != winner.Value).ToList();
            var loserNames = losers.Count > 0
                ? string.Join(", ", losers.Select(SafeName))
                : Loc.GetString("duel-arena-losers-fallback");

            msg = Loc.GetString("duel-arena-concluded-winner",
                ("winner", winnerName),
                ("streak", store.Streak),
                ("losers", loserNames),
                ("loserCount", losers.Count));
        }
        else
        {
            var andSep = $" {Loc.GetString("duel-arena-connector-and")} ";
            var names = string.Join(andSep, arena.Duelists.Select(SafeName));
            msg = Loc.GetString("duel-arena-concluded-draw", ("fighters", names));
        }

        // Начисляем победы/поражения и серии в отдельной системе; получаем строку табло.
        var scoreboard = _score.RecordMatchResult(store, arena.Duelists, winner);
        if (scoreboard != null)
            msg += "\n" + Loc.GetString("duel-arena-scoreboard", ("scores", scoreboard));

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

        // Удаляем миньонов проигравших этого боя независимо от позиции: победитель мог увести дрона
        // с грида арены, и радиусная CleanupArea его бы не достала.
        _minionSystem.RemoveMinionsForOwners(roundDuelists);

        // Убираем снаряжение и объявляем результат одним сообщением.
        _cleanup.CleanupArea(arenaUid, arena.CleanupRange);
        _chatManager.DispatchServerAnnouncement(msg, Color.Gold);

        // Сигнал завершения дуэли — играем для всех бойцов раунда (список снят до очистки).
        if (arena.EndSound != null)
            foreach (var d in roundDuelists)
                if (Exists(d))
                    _audio.PlayPvs(arena.EndSound, d);

        // Восстанавливаем разрушенную за бой арену с задержкой (см. Update / PendingRestoreAt): вне
        // стека события смерти (удаление/спавн из обработчика смертельного удара мог срывать
        // восстановление) И после оседания отложенных взрывов — смертельный удар мог быть нанесён
        // гранатой/зарядом, чей взрыв обрабатывается уже после конца дуэли, иначе разрушение от него
        // осталось бы навсегда. RestoreArena сам отодвигает бойцов с тайлов под конструкциями.
        arena.PendingRestore = true;
        arena.PendingRestoreAt = _timing.CurTime + TimeSpan.FromSeconds(arena.RestoreDelay);

        // Повторно исцелить бойцов в тот же отложенный момент: если смертельный удар нанесла отложенная
        // взрывчатка, её взрыв отрывает конечности уже ПОСЛЕ немедленного Rejuvenate выше — без этого
        // прохода боец улетел бы на следующую арену с полным ХП, но без оторванных частей.
        arena.PendingHealDuelists = new List<EntityUid>(roundDuelists);

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
