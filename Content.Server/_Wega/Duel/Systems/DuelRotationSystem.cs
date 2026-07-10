using System.Linq;
using Content.Server._Wega.Duel.Components;
using Content.Shared._Wega.Duel;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Random;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Арена-ротация: опциональная надстройка над обычной дуэлью (<see cref="DuelArenaSystem"/>).
/// При инициализации контроллера (<see cref="DuelRotationComponent"/>) предзагружает все его
/// карты-арены и связывает найденные на них трекеры (<see cref="DuelArenaComponent"/>) с собой.
/// После каждого раунда DuelArenaSystem дёргает <see cref="AdvanceToNextArena"/> — бойцы
/// переносятся на спавн-маркеры случайной следующей арены, и там стартует новый раунд.
///
/// Если контроллера на карте нет — ничего из этого не происходит, дуэль работает в одиночном
/// режиме без изменений.
/// </summary>
public sealed class DuelRotationSystem : EntitySystem
{
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private PullingSystem _pulling = default!;

    /// <summary>
    /// Останавливает перетаскивание в обе стороны перед телепортом бойца на другую арену:
    /// иначе джойнт перетаскивания свяжет тела на разных картах и движок упадёт
    /// (DebugAssert «cross-map joint» в SharedJointSystem).
    /// </summary>
    private void StopPulls(EntityUid fighter)
    {
        // Если бойца кто-то тащит — отпускаем.
        if (TryComp<PullableComponent>(fighter, out var pullable) && pullable.BeingPulled)
            _pulling.TryStopPull(fighter, pullable);

        // Если боец сам кого-то тащит — отпускаем ту цель.
        if (TryComp<PullerComponent>(fighter, out var puller)
            && puller.Pulling is { } pulled
            && TryComp<PullableComponent>(pulled, out var pulledComp))
            _pulling.TryStopPull(pulled, pulledComp);
    }

    /// <summary>Возвращает идентификатор игрока, управляющего телом, или null для тел без разума (NPC).</summary>
    private NetUserId? GetUser(EntityUid body)
    {
        return _mind.TryGetMind(body, out _, out var mind) ? mind.UserId : null;
    }

    /// <summary>
    /// Защита от рекурсии: пока идёт предзагрузка арен, загрузка карты-арены может сама содержать
    /// контроллер ротации (или в <see cref="DuelRotationComponent.Arenas"/> по ошибке попал путь
    /// этой же карты). Без этого флага его MapInit запустил бы предзагрузку снова — и так до зависания.
    /// </summary>
    private bool _preloading;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DuelRotationComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<DuelArenaComponent, MapInitEvent>(OnArenaTrackerMapInit);
        SubscribeLocalEvent<DuelArenaEntryComponent, ActivateInWorldEvent>(OnEntryActivate);
    }

    /// <summary>
    /// Любой трекер дуэли, появившийся на карте уже загруженной арены, сам привязывается к её
    /// контроллеру ротации. Нужно для подмены трекера прямо во время игры (например, замена обычного
    /// трекера на трекер со штормом): без этого новый трекер остался бы с пустым
    /// <see cref="DuelArenaComponent.RotationController"/>, и его раунды выпали бы из ротации —
    /// бойцов не переносило бы на следующую арену и карты не менялись.
    /// </summary>
    private void OnArenaTrackerMapInit(EntityUid uid, DuelArenaComponent arena, MapInitEvent args)
    {
        // Уже привязан вручную/ранее — не трогаем.
        if (arena.RotationController != null)
            return;

        // Свежезагруженные при предзагрузке арены привязывает сам PreloadArenas (их карты ещё не
        // занесены в LoadedArenas на этот момент) — здесь дублировать не нужно.
        if (_preloading)
            return;

        var map = Transform(uid).MapID;
        var query = EntityQueryEnumerator<DuelRotationComponent>();
        while (query.MoveNext(out var ctrlUid, out var ctrl))
        {
            if (!ctrl.Loaded || !ctrl.LoadedArenas.Any(kv => kv.Value == map))
                continue;

            arena.RotationController = ctrlUid;
            Log.Info($"[duel-rotation] трекер на карте {map} привязан к контроллеру (подмена во время игры)");
            return;
        }
    }

    private void OnMapInit(EntityUid uid, DuelRotationComponent comp, MapInitEvent args)
    {
        // Этот контроллер появился из карты, загружаемой как арена — нейтрализуем его, чтобы он
        // не запустил предзагрузку тех же арен повторно (иначе бесконечная рекурсия и зависание).
        if (_preloading)
        {
            comp.Loaded = true;
            return;
        }

        PreloadArenas(uid, comp);
    }

    /// <summary>
    /// Загружает все карты-арены контроллера (один раз) и связывает их трекеры дуэли с ним.
    /// Карты инициализируются сразу (сущности живые и доступны для запросов), но не ставятся
    /// на паузу — арена просто ждёт бойцов. Несработавшую карту пропускаем с ошибкой в лог,
    /// остальные грузятся независимо.
    /// </summary>
    private void PreloadArenas(EntityUid uid, DuelRotationComponent comp)
    {
        if (comp.Loaded)
            return;

        // Помечаем загруженным сразу и поднимаем флаг реентерабельности до начала загрузки карт:
        // TryLoadMap инициализирует карту синхронно, и если на ней есть контроллер ротации, его
        // MapInit не должен снова войти сюда.
        comp.Loaded = true;
        _preloading = true;
        try
        {
            var opts = new DeserializationOptions { InitializeMaps = true };

            for (var i = 0; i < comp.Arenas.Count; i++)
            {
                var path = comp.Arenas[i];
                if (!_mapLoader.TryLoadMap(path, out var map, out _, opts))
                {
                    Log.Error($"[duel-rotation] не удалось загрузить арену {path} (индекс {i})");
                    continue;
                }

                var mapId = map.Value.Comp.MapId;
                comp.LoadedArenas[i] = mapId;
                LinkArenaTrackers(mapId, uid);
            }
        }
        finally
        {
            _preloading = false;
        }

        Log.Info($"[duel-rotation] предзагружено арен: {comp.LoadedArenas.Count} из {comp.Arenas.Count}");
    }

    /// <summary>
    /// Привязывает все трекеры дуэли на карте <paramref name="mapId"/> к контроллеру — после этого
    /// их раунды считаются частью ротации (общий счёт + переход на следующую арену).
    /// </summary>
    private void LinkArenaTrackers(MapId mapId, EntityUid controller)
    {
        var query = EntityQueryEnumerator<DuelArenaComponent, TransformComponent>();
        while (query.MoveNext(out var arenaUid, out var arena, out var xform))
        {
            if (xform.MapID != mapId)
                continue;
            arena.RotationController = controller;
        }
    }

    /// <summary>
    /// Переносит бойцов на следующую арену и запускает там новый раунд. Следующая арена — случайная
    /// из загруженных, кроме той, на которой только что был бой (без повтора подряд). Если других
    /// загруженных арен нет или на выбранной нет спавн-маркеров — тихо ничего не делаем (раунд
    /// просто завершится как обычно). Вызывается из DuelArenaSystem.ConcludeDuel.
    /// </summary>
    public void AdvanceToNextArena(Entity<DuelRotationComponent> controller, IReadOnlyCollection<EntityUid> duelists)
    {
        var comp = controller.Comp;

        if (comp.LoadedArenas.Count == 0)
        {
            Log.Warning("[duel-rotation] нет загруженных арен — переход после боя невозможен");
            return;
        }

        // Индекс арены, на которой только что закончился бой (по карте любого из бойцов).
        // Важно искать честно: FirstOrDefault на словаре вернул бы Key=0 при ненайденной карте,
        // из-за чего реальная текущая арена могла бы остаться в кандидатах (телепорт на ту же
        // карту → «карта не меняется») либо единственная арена выпасть из кандидатов (нет телепорта).
        int currentIndex = -1;
        var currentMap = duelists.Select(d => Transform(d).MapID).FirstOrDefault();
        foreach (var kv in comp.LoadedArenas)
        {
            if (kv.Value == currentMap)
            {
                currentIndex = kv.Key;
                break;
            }
        }

        // Предпочитаем ДРУГУЮ арену (тогда поле боя сменится). Если другой нет — переигрываем на
        // текущей: бойцов всё равно возвращаем на спавн-маркеры, чтобы новый раунд начался с углов.
        var candidates = comp.LoadedArenas.Keys.Where(i => i != currentIndex).ToList();
        var nextIndex = candidates.Count > 0
            ? _random.Pick(candidates)
            : (currentIndex >= 0 ? currentIndex : _random.Pick(comp.LoadedArenas.Keys.ToList()));

        if (candidates.Count == 0)
            Log.Info("[duel-rotation] других арен нет — переигрываем на текущей, бойцы возвращены на спавны");

        // Только переносим бойцов на арену — раунд НЕ вооружаем автоматически.
        // Старт объявляется лишь после нажатия кнопки старта на самой арене (как и первый раунд),
        // иначе «дуэль началась» печаталось бы до нажатия кнопки.
        MoveAndStart(comp, nextIndex, duelists);
    }

    /// <summary>
    /// Переносит бойцов на арену с индексом <paramref name="arenaIndex"/> (из загруженных) на их
    /// спавн-маркеры. Общий код для перехода между раундами (<see cref="AdvanceToNextArena"/>) и для
    /// входа с хаба по кнопке (<see cref="OnEntryActivate"/>). Раунд НЕ вооружается автоматически —
    /// старт объявляется кнопкой старта на самой арене. Если арена не загружена или на ней нет
    /// спавн-маркеров — тихо ничего не делаем (с предупреждением в лог).
    /// </summary>
    private void MoveAndStart(DuelRotationComponent comp, int arenaIndex, IReadOnlyCollection<EntityUid> fighters)
    {
        if (!comp.LoadedArenas.TryGetValue(arenaIndex, out var map))
        {
            Log.Warning($"[duel-rotation] арена (индекс {arenaIndex}) не загружена — переход отменён");
            return;
        }

        var spawns = GetSpawns(map);
        if (spawns.Count == 0)
        {
            Log.Warning($"[duel-rotation] на арене (индекс {arenaIndex}) нет спавн-маркеров DuelArenaSpawnMarker — переход отменён");
            return;
        }

        // Раскидываем бойцов по спавн-маркерам по кругу (если бойцов больше, чем маркеров).
        var ordered = fighters.Where(Exists).ToList();

        if (ordered.Count > spawns.Count)
            Log.Warning($"[duel-rotation] на арене (индекс {arenaIndex}) бойцов ({ordered.Count}) больше, " +
                $"чем спавн-маркеров ({spawns.Count}) — лишние ставятся со сдвигом, чтобы не оказаться на одном тайле");

        // Назначаем каждому бойцу маркер БЕЗ коллизий, в два прохода. Раньше слот считался как
        // i % spawns.Count и лишь затем, возможно, переопределялся закреплённым спавном — но слоты не
        // резервировались, поэтому round-robin одного бойца и закреплённый спавн другого могли совпасть:
        // второго сдвигало на соседний тайл вместо свободного маркера (баг «не на тот спавн» между
        // раундами при входе персональными кнопками). Теперь сначала резервируем закреплённые спавны,
        // потом раздаём оставшиеся свободные; сдвиг — только если бойцов реально больше, чем маркеров.
        var slotOf = new int[ordered.Count];
        var reserved = new bool[spawns.Count];

        // Проход 1: закреплённые за игроком спавны (по DuelArenaSpawnComponent.SpawnIndex), если маркер
        // ещё свободен. Так боец возвращается на свой угол, выбранный кнопкой входа.
        for (var i = 0; i < ordered.Count; i++)
        {
            slotOf[i] = -1;
            if (GetUser(ordered[i]) is not { } user
                || !comp.PreferredSpawns.TryGetValue(user, out var pref))
                continue;

            var idx = spawns.FindIndex(s => Comp<DuelArenaSpawnComponent>(s).SpawnIndex == pref);
            if (idx >= 0 && !reserved[idx])
            {
                slotOf[i] = idx;
                reserved[idx] = true;
            }
        }

        // Проход 2: остальные занимают ближайший ещё свободный маркер по порядку.
        var nextFree = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (slotOf[i] >= 0)
                continue;

            while (nextFree < spawns.Count && reserved[nextFree])
                nextFree++;

            if (nextFree < spawns.Count)
            {
                slotOf[i] = nextFree;
                reserved[nextFree] = true;
            }
            else
            {
                // Бойцов больше, чем маркеров — заворачиваем по кругу; лишних разведёт сдвиг ниже.
                slotOf[i] = i % spawns.Count;
            }
        }

        // Расставляем. Сдвиг на соседнюю клетку (n,0) применяется только когда на один маркер реально
        // попало больше одного бойца (бойцов больше, чем маркеров) — иначе двое оказались бы на тайле.
        var usage = new int[spawns.Count];
        for (var i = 0; i < ordered.Count; i++)
        {
            var slot = slotOf[i];
            var coords = Transform(spawns[slot]).Coordinates;
            if (usage[slot] > 0)
                coords = coords.Offset(new System.Numerics.Vector2(usage[slot], 0f));
            usage[slot]++;

            StopPulls(ordered[i]);
            _transform.SetCoordinates(ordered[i], coords);
        }

        comp.CurrentArena = arenaIndex;
    }

    /// <summary>
    /// Кнопка входа с хаба: при нажатии собирает ВСЕХ мобов на гриде кнопки (игроков И NPC) и
    /// переносит их на арену из <see cref="DuelArenaEntryComponent.ArenaIndex"/>. Бой при этом НЕ
    /// стартует — раунд запускается отдельно (кнопкой старта на самой арене). Контроллер ротации
    /// ищем первый на сервере (он один).
    /// </summary>
    private void OnEntryActivate(EntityUid uid, DuelArenaEntryComponent comp, ActivateInWorldEvent args)
    {
        var ctrlQuery = EntityQueryEnumerator<DuelRotationComponent>();
        if (!ctrlQuery.MoveNext(out _, out var ctrl) || !ctrl.Loaded)
        {
            Log.Warning("[duel-rotation] кнопка входа: контроллер ротации не найден или не загружен");
            return;
        }

        var grid = Transform(uid).GridUid;
        if (grid == null)
            return;

        // Персональная кнопка: нажавшего ставим на выбранный спавн, а остальных бойцов с хаба
        // подтягиваем на ту же арену — каждого на свой свободный спавн (чтобы оба дуэлянта попали
        // на арену, даже если кнопку нажал только один).
        if (comp.SpawnIndex is { } spawnIndex)
        {
            // Сначала нажавший — он занимает выбранный (или ближайший свободный) спавн.
            MoveOneToSpawn(ctrl, comp.ArenaIndex, args.User, spawnIndex);

            // Затем остальные мобы с хаба — на оставшиеся свободные спавны той же арены.
            var others = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
            while (others.MoveNext(out var mob, out _, out var xform))
            {
                if (mob == args.User || xform.GridUid != grid)
                    continue;
                MoveOneToSpawn(ctrl, comp.ArenaIndex, mob, spawnIndex);
            }
            return;
        }

        // Все мобы (игроки и NPC) на гриде хаба.
        var fighters = new List<EntityUid>();
        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var mob, out _, out var xform))
        {
            if (xform.GridUid == grid)
                fighters.Add(mob);
        }

        MoveAndStart(ctrl, comp.ArenaIndex, fighters);
    }

    /// <summary>
    /// Переносит одного бойца на спавн-маркер арены, начиная с номера <paramref name="spawnIndex"/>
    /// (<see cref="DuelArenaSpawnComponent.SpawnIndex"/>). Если этот спавн уже занят другим бойцом —
    /// ищем следующий свободный по возрастанию номера (и заворачиваем с начала). Так двое дуэлянтов
    /// могут жать одну и ту же кнопку: первый встаёт на спавн №0, второй — на следующий свободный.
    /// Используется персональными кнопками входа на хабе. Бой не вооружается (как и общий вход).
    /// </summary>
    private void MoveOneToSpawn(DuelRotationComponent comp, int arenaIndex, EntityUid fighter, int spawnIndex)
    {
        if (!comp.LoadedArenas.TryGetValue(arenaIndex, out var map))
        {
            Log.Warning($"[duel-rotation] арена (индекс {arenaIndex}) не загружена — вход отменён");
            return;
        }

        if (!TryGetFreeSpawn(map, spawnIndex, fighter, out var target))
        {
            Log.Warning($"[duel-rotation] на арене (индекс {arenaIndex}) нет свободного спавн-маркера (с номера {spawnIndex}) — вход отменён");
            return;
        }

        StopPulls(fighter);
        _transform.SetCoordinates(fighter, Transform(target).Coordinates);

        // Запоминаем, на каком спавне игрок реально оказался, чтобы между раундами возвращать его сюда же.
        if (GetUser(fighter) is { } user)
            comp.PreferredSpawns[user] = Comp<DuelArenaSpawnComponent>(target).SpawnIndex;
    }

    /// <summary>
    /// Свободный спавн-маркер на карте. Маркеры берём по возрастанию номера, начиная с
    /// <paramref name="preferredIndex"/> (и заворачивая в начало списка), и выбираем первый, на
    /// чьей клетке не стоит чужой боец. Если все заняты — берём предпочитаемый (или первый),
    /// чтобы вход не блокировался. <paramref name="self"/> исключается из проверки занятости.
    /// </summary>
    private bool TryGetFreeSpawn(MapId mapId, int preferredIndex, EntityUid self, out EntityUid spawn)
    {
        var spawns = new List<(int Index, EntityUid Uid)>();
        var query = EntityQueryEnumerator<DuelArenaSpawnComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var marker, out var xform))
        {
            if (xform.MapID == mapId)
                spawns.Add((marker.SpawnIndex, uid));
        }

        if (spawns.Count == 0)
        {
            spawn = default;
            return false;
        }

        spawns.Sort((a, b) => a.Index.CompareTo(b.Index));

        // Стартуем перебор с предпочитаемого спавна (или с ближайшего следующего), затем по кругу.
        var start = spawns.FindIndex(s => s.Index >= preferredIndex);
        if (start < 0)
            start = 0;

        for (var i = 0; i < spawns.Count; i++)
        {
            var candidate = spawns[(start + i) % spawns.Count].Uid;
            if (!IsSpawnOccupied(candidate, self))
            {
                spawn = candidate;
                return true;
            }
        }

        // Все спавны заняты — не блокируем вход, ставим на предпочитаемый.
        spawn = spawns[start].Uid;
        return true;
    }

    /// <summary>На клетке спавна <paramref name="spawn"/> стоит чужой моб (не <paramref name="self"/>)?</summary>
    private bool IsSpawnOccupied(EntityUid spawn, EntityUid self)
    {
        var spawnXform = Transform(spawn);
        var grid = spawnXform.GridUid;
        var spawnPos = _transform.GetWorldPosition(spawn);

        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var mob, out _, out var xform))
        {
            if (mob == self || xform.GridUid != grid)
                continue;

            if ((_transform.GetWorldPosition(mob) - spawnPos).LengthSquared() <= 0.25f)
                return true;
        }
        return false;
    }

    /// <summary>Все спавн-маркеры на карте, отсортированные по номеру (<see cref="DuelArenaSpawnComponent.SpawnIndex"/>)
    /// для предсказуемого распределения при общем входе.</summary>
    private List<EntityUid> GetSpawns(MapId mapId)
    {
        var result = new List<(int Index, EntityUid Uid)>();
        var query = EntityQueryEnumerator<DuelArenaSpawnComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var marker, out var xform))
        {
            if (xform.MapID == mapId)
                result.Add((marker.SpawnIndex, uid));
        }
        return result.OrderBy(s => s.Index).Select(s => s.Uid).ToList();
    }

}
