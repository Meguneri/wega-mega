using Content.Server._Wega.Duel;
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
using Content.Shared.Physics;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
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
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private DuelArenaScoreSystem _score = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _map = default!;

    /// <summary>
    /// Спавнит текущий арсенал-крейт (<see cref="DuelArenaComponent.ArsenalCrate"/>, задаётся пультом
    /// <see cref="ArenaArsenalRemoteSystem"/> / кнопкой входа) рядом с каждым спавн-маркером арены.
    /// Крейт помечен markIssuedItems, поэтому его содержимое метится ArenaIssued и очистка снесёт всё
    /// после боя. Тайл под ящик подбираем аккуратно (<see cref="FindCrateCoords"/>): ближайший пустой
    /// в 1-2 клетках от спавна, иначе наименее нагруженный — чтобы ящик не влез в стол/стену.
    ///
    /// Идемпотентно в пределах раунда: гард <see cref="DuelArenaComponent.ArsenalSpawned"/> не даёт
    /// задвоить ящики. Основной вызов — при подготовке раунда (ArenaRoundPreparingEvent, перенос бойцов
    /// на арену), чтобы ящики стояли у спавнов ДО начала боя; в ArmDuel — подстраховка для одиночных арен
    /// (без ротации), где переноса-на-арену нет. Флаг сбрасывается по концу/сбросу боя.
    /// </summary>
    private void EnsureArsenalCrates(EntityUid arenaUid, DuelArenaComponent comp)
    {
        if (comp.ArsenalSpawned || comp.ArsenalCrate is not { } crateProto)
            return;

        var arenaGrid = Transform(arenaUid).GridUid;
        if (arenaGrid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        comp.ArsenalSpawned = true;

        var query = EntityQueryEnumerator<DuelArenaSpawnComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            var spawnTile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
            var coords = FindCrateCoords(grid, gridComp, spawnTile);
            Spawn(crateProto, coords);
        }
    }

    // Смещения тайлов вокруг спавна, отсортированные по РЕАЛЬНОЙ близости: сперва прямые соседи
    // (вверх/вниз/влево/вправо), затем диагонали радиуса 1, затем радиус 2 в том же порядке. Ящик
    // встаёт ВПЛОТНУЮ к спавну, а не по диагонали или дальше, когда рядом есть свободный тайл.
    private static readonly Vector2i[] CrateTileOffsets =
    {
        // радиус 1 — прямые соседи (расстояние 1)
        new(0, 1), new(0, -1), new(1, 0), new(-1, 0),
        // радиус 1 — диагонали (≈1.41)
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
        // радиус 2 — прямые (2)
        new(0, 2), new(0, -2), new(2, 0), new(-2, 0),
        // радиус 2 — ближние «косые» (≈2.24)
        new(1, 2), new(-1, 2), new(1, -2), new(-1, -2),
        new(2, 1), new(2, -1), new(-2, 1), new(-2, -1),
        // радиус 2 — дальние диагонали (≈2.83)
        new(2, 2), new(2, -2), new(-2, 2), new(-2, -2),
    };

    /// <summary>
    /// Подбирает тайл под арсенал-ящик рядом со спавном дуэлянта, идя от БЛИЖАЙШИХ клеток к дальним
    /// (<see cref="CrateTileOffsets"/>). Берёт первый свободный тайл (пол есть, нет БЛОКИРУЮЩЕЙ
    /// заякоренной сущности — стола, стены, барьера); если свободного нет — наименее нагруженный (при
    /// равной нагрузке побеждает ближайший, т.к. список упорядочен). Так ящик не оказывается в столе/стене
    /// и стоит вплотную. Считаем только реальные препятствия: кабели/трубы/провода под полом заякорены,
    /// но ящику не мешают — иначе на «жилых» аренах (закабелённый пол) ящик уезжал бы за 2 тайла от спавна.
    /// </summary>
    private EntityCoordinates FindCrateCoords(EntityUid grid, MapGridComponent gridComp, Vector2i spawnTile)
    {
        var anchored = new List<EntityUid>();
        Vector2i? bestLoaded = null;
        var bestLoad = int.MaxValue;

        foreach (var offset in CrateTileOffsets)
        {
            var tile = spawnTile + offset;

            // Нет пола (космос/пустота) — ящик туда не ставим.
            if (_map.GetTileRef(grid, gridComp, tile).Tile.IsEmpty)
                continue;

            anchored.Clear();
            _map.GetAnchoredEntities((grid, gridComp), tile, anchored);
            var load = CountBlockers(anchored);

            // Первый свободный в порядке близости — он же ближайший. Готово.
            if (load == 0)
                return _map.GridTileToLocal(grid, gridComp, tile);

            // Иначе запоминаем наименее нагруженный (строгое <, поэтому при равенстве остаётся ближайший).
            if (load < bestLoad)
            {
                bestLoad = load;
                bestLoaded = tile;
            }
        }

        // Свободного тайла не нашлось — наименее нагруженный; в самом крайнем случае сам спавн-тайл.
        return _map.GridTileToLocal(grid, gridComp, bestLoaded ?? spawnTile);
    }

    /// <summary>
    /// Сколько из заякоренных на тайле сущностей реально блокируют размещение ящика: статичное твёрдое
    /// тело на слое <see cref="CollisionGroup.Impassable"/>. Кабели, трубы, провода и прочая
    /// незакрывающая тайл электрика в счёт не идут.
    /// </summary>
    private int CountBlockers(List<EntityUid> anchored)
    {
        var count = 0;
        foreach (var ent in anchored)
        {
            if (!TryComp<PhysicsComponent>(ent, out var body))
                continue;

            if (body.BodyType != BodyType.Static ||
                !body.Hard ||
                (body.CollisionLayer & (int) CollisionGroup.Impassable) == 0)
                continue;

            count++;
        }

        return count;
    }

    private void OnRoundPreparing(EntityUid uid, DuelArenaComponent comp, ref ArenaRoundPreparingEvent args)
    {
        if (!comp.IsActive)
            comp.Phase = DuelArenaPhase.Preparing;

        EnsureArsenalCrates(uid, comp);
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DuelArenaComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<DuelArenaComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<DuelArenaComponent, ArenaRoundPreparingEvent>(OnRoundPreparing);
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

                if (comp.Phase == DuelArenaPhase.Restoring)
                    comp.Phase = DuelArenaPhase.Idle;

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
        comp.Phase = DuelArenaPhase.Fighting;

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

        var vsSep = $" {Loc.GetString("duel-arena-connector-vs")} ";
        var names = string.Join(vsSep, comp.Duelists.Select(d => MetaData(d).EntityName));
        _chatManager.DispatchServerAnnouncement(
            Loc.GetString("duel-arena-started", ("fighters", names)), Color.Gold);

        // Подстраховка выдачи арсенал-ящиков для одиночных арен (без ротации): там нет переноса бойцов
        // на арену, а значит и ArenaRoundPreparingEvent, поэтому спавним здесь. Гард ArsenalSpawned
        // делает вызов no-op, если ящики уже выданы при подготовке раунда (ротация).
        EnsureArsenalCrates(uid, comp);

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
            _minionSystem.SpawnMinion(duelist, coords);
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
        comp.Phase = DuelArenaPhase.Restoring;

        // Сброс ready-check: убираем готовность и голограммы «ГОТОВ».
        _readySystem.ClearReady(comp);

        // Останавливаем авто-дроп снабжения до следующего боя.
        comp.SupplyDropAt = null;

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

        // Ящики этого раунда очистка уже убрала — разрешаем выдать их заново на следующем.
        comp.ArsenalSpawned = false;
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
    {
        if (!arena.IsActive)
            return false;

        var aliveDuelists = arena.Duelists.Where(d => IsActiveFighter(arenaUid, d)).ToList();
        if (aliveDuelists.Count > 1)
            return false; // бой ещё идёт

        arena.Phase = DuelArenaPhase.Restoring;

        // Готовность к этому бою больше не нужна — убираем остатки ready-check (на всякий случай).
        _readySystem.ClearReady(arena);

        // Останавливаем авто-дроп снабжения — бой окончен.
        arena.SupplyDropAt = null;

        // Куда писать счёт: одиночная арена ведёт его сама; в режиме ротации — общий счёт на
        // контроллере. Развилка по флагу RotationController (пусто = старое поведение).
        DuelRotationComponent? ctrl = null;
        var inRotation = arena.RotationController is { } ctrlUid
            && TryComp(ctrlUid, out ctrl);
        IDuelScoreStore store = inRotation ? ctrl! : arena;

        // Состав боя фиксируем до очистки списка — нужен для перехода на следующую арену.
        var roundDuelists = arena.Duelists.ToList();

        EntityUid? winner = aliveDuelists.Count == 1 ? aliveDuelists[0] : null;

        // Начисляем победы/поражения и серии в отдельной системе; получаем строку табло.
        // ВАЖНО: делаем это ДО сборки сообщения — иначе store.Streak в анонсе показывает серию
        // прошлого победителя (ещё не обновлённую этим боем), из-за чего новому чемпиону
        // приписывалась чужая длинная серия («выиграл 5 подряд» вместо реальной 1).
        var scoreboard = _score.RecordMatchResult(store, arena.Duelists, winner);

        var msg = BuildConclusionMessage(arena, store, winner, scoreboard);

        HealAndClearDuelists(arena, roundDuelists);

        // Убираем снаряжение и объявляем результат одним сообщением.
        _cleanup.CleanupArea(arenaUid, arena.CleanupRange);
        _chatManager.DispatchServerAnnouncement(msg, Color.Gold);

        // Сигнал завершения дуэли — играем для всех бойцов раунда (список снят до очистки).
        if (arena.EndSound != null)
            foreach (var d in roundDuelists)
                if (Exists(d))
                    _audio.PlayPvs(arena.EndSound, d);

        ScheduleArenaRestore(arena, roundDuelists);

        // Режим ротации: переносим бойцов на следующую арену и запускаем там раунд. Бойцы уже
        // исцелены выше, прошлая арена восстановится на следующем тике (на ней уже никого не будет).
        if (inRotation)
            _rotation.AdvanceToNextArena((arena.RotationController!.Value, ctrl!), roundDuelists);

        return true;
    }

    private void HealAndClearDuelists(DuelArenaComponent arena, List<EntityUid> roundDuelists)
    {
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
    }

    private void ScheduleArenaRestore(DuelArenaComponent arena, List<EntityUid> roundDuelists)
    {
        // Восстанавливаем разрушенную за бой арену с задержкой (см. Update / PendingRestoreAt): вне
        // стека события смерти (удаление/спавн из обработчика смертельного удара мог срывать
        // восстановление) И после оседания отложенных взрывов — смертельный удар мог быть нанесён
        // гранатой/зарядом, чей взрыв обрабатывается уже после конца дуэли, иначе разрушение от него
        // осталось бы навсегда. RestoreArena сам отодвигает бойцов с тайлов под конструкциями.
        arena.PendingRestore = true;
        arena.PendingRestoreAt = _timing.CurTime + TimeSpan.FromSeconds(arena.RestoreDelay);

        // Ящики этого раунда очистка уже убрала — разрешаем выдать их заново на следующем.
        arena.ArsenalSpawned = false;

        // Повторно исцелить бойцов в тот же отложенный момент: если смертельный удар нанесла отложенная
        // взрывчатка, её взрыв отрывает конечности уже ПОСЛЕ немедленного Rejuvenate выше — без этого
        // прохода боец улетел бы на следующую арену с полным ХП, но без оторванных частей.
        arena.PendingHealDuelists = new List<EntityUid>(roundDuelists);

        // Сигнал закрытия шлюзов шлём не сразу, а через ReturnGrace секунд: дуэлянты
        // возвращаются в базы по открытым шлюзам, и только потом те закрываются (см. Update).
        arena.GateCloseAt = _timing.CurTime + TimeSpan.FromSeconds(arena.ReturnGrace);
    }

    private string BuildConclusionMessage(
        DuelArenaComponent arena,
        IDuelScoreStore store,
        EntityUid? winner,
        string? scoreboard)
    {
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

        if (scoreboard != null)
            msg += "\n" + Loc.GetString("duel-arena-scoreboard", ("scores", scoreboard));

        return msg;
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
