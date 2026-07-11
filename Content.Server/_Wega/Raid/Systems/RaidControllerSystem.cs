using System.Linq;
using System.Numerics;
using Content.Server._Wega.Raid.Components;
using Content.Shared._Wega.Raid.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Stack;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Robust.Shared.ContentPack;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Server.Audio;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Wega.Raid.Systems;

/// <summary>
/// Контроллер экстракшн-режима. Предзагружает карту-локацию рейда, обрабатывает вход с хаба
/// (кнопка <see cref="RaidEntryComponent"/>), ведёт таймер рейда и выполняет эвакуацию рейдеров —
/// как штатную (по точке экстракта, через <see cref="ExtractRaider"/>), так и принудительную по
/// истечении таймера. Точки экстракта обслуживает отдельная <c>RaidExtractionSystem</c>.
/// </summary>
public sealed partial class RaidControllerSystem : EntitySystem
{
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private IResourceManager _resource = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private RaidLootFieldSystem _lootField = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>Защита от повторного входа в предзагрузку, если карта рейда сама несёт контроллер.</summary>
    private bool _preloading;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RaidControllerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<RaidEntryComponent, ActivateInWorldEvent>(OnEntryActivate);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
    }

    /// <summary>Погибший в рейде выбывает из списка живых рейдеров (его снаряжение остаётся лутом).</summary>
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        var query = EntityQueryEnumerator<RaidControllerComponent>();
        while (query.MoveNext(out var ctrlUid, out var comp))
        {
            if (!comp.Raiders.Remove(args.Target))
                continue;

            var name = Comp<MetaDataComponent>(args.Target).EntityName;
            _chat.DispatchServerAnnouncement(Loc.GetString("raid-died", ("name", name)), Color.Crimson);
            CheckRaidEnd(ctrlUid, comp);
            break;
        }
    }

    /// <summary>
    /// Игрок отключился во время активного рейда — экстренно эвакуируем его персонажа на хаб без
    /// награды. Стартовое снаряжение остаётся при нём; весь найденный в рейде лут (метка
    /// <see cref="RaidLootComponent"/>) остаётся на локации. Мёртвых не трогаем — их уже обработала
    /// <see cref="OnMobStateChanged"/>.
    /// </summary>
    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        if (!TryGetController(out var ctrlUid, out var ctrl) || !ctrl.Active)
            return;

        var raider = args.Entity;
        if (!ctrl.Raiders.Contains(raider))
            return;

        // Мёртвый рейдер уже выбыл через MobStateChanged; просто чистим список на всякий случай.
        if (_mobState.IsDead(raider))
        {
            ctrl.Raiders.Remove(raider);
            CheckRaidEnd(ctrlUid, ctrl);
            return;
        }

        // Экстренная эвакуация: лут рейда остаётся на локации, снаряжение при персонаже.
        var loot = new List<EntityUid>();
        CollectLoot(raider, loot);
        foreach (var item in loot)
            QueueDel(item);

        ReturnToHub(ctrlUid, raider);
        ctrl.Raiders.Remove(raider);

        var name = Comp<MetaDataComponent>(raider).EntityName;
        _chat.DispatchServerAnnouncement(Loc.GetString("raid-disconnected", ("name", name)), Color.Orange);
        CheckRaidEnd(ctrlUid, ctrl);
    }

    private void OnMapInit(EntityUid uid, RaidControllerComponent comp, MapInitEvent args)
    {
        // Контроллер появился из карты, загружаемой как рейд — нейтрализуем, чтобы не запустить
        // предзагрузку повторно (как делает DuelRotationSystem при рекурсии).
        if (_preloading)
        {
            comp.Loaded = true;
            return;
        }

        PreloadRaidMap(uid, comp);
    }

    /// <summary>Загружает карту рейда один раз (синхронно инициализируя её сущности).</summary>
    private void PreloadRaidMap(EntityUid uid, RaidControllerComponent comp)
    {
        if (comp.Loaded)
            return;

        // Карта рейда ещё не создана в редакторе — не дёргаем загрузчик (иначе движок логирует
        // ERROR «File not found»). До маппинга это нормально: предупреждаем и тихо выходим.
        // Пустой путь встречается при ручном создании компонента в тестах (карта уже создана вручную).
        if (string.IsNullOrEmpty(comp.RaidMap.CanonPath) || !_resource.ContentFileExists(comp.RaidMap))
        {
            comp.Loaded = true;
            Log.Warning($"[raid] карта рейда {comp.RaidMap} не найдена — рейд не активирован (создай карту в редакторе)");
            return;
        }

        comp.Loaded = true;
        _preloading = true;
        try
        {
            var opts = new DeserializationOptions { InitializeMaps = true };
            if (_mapLoader.TryLoadMap(comp.RaidMap, out var map, out _, opts))
            {
                comp.LoadedMap = map.Value.Comp.MapId;
                Log.Info($"[raid] карта рейда загружена: {comp.RaidMap} → map {comp.LoadedMap}");

                if (comp.AutoSetup)
                    AutoSetupRaidMap(comp.LoadedMap.Value);
            }
            else
            {
                Log.Error($"[raid] не удалось загрузить карту рейда {comp.RaidMap}");
            }
        }
        finally
        {
            _preloading = false;
        }
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<RaidControllerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Active || comp.EndTime is not { } end)
                continue;

            // Предупреждения «на выход» при приближении конца рейда. PendingWarnings отсортирован по
            // убыванию, поэтому снимаем с начала, пока текущий остаток времени не превысит порог.
            var secsLeft = (float)(end - now).TotalSeconds;
            while (comp.PendingWarnings.Count > 0 && secsLeft <= comp.PendingWarnings[0])
            {
                var threshold = comp.PendingWarnings[0];
                comp.PendingWarnings.RemoveAt(0);
                AnnounceTimeWarning(threshold);
            }

            if (now >= end)
                EndRaid(uid, comp);
        }
    }

    private void AnnounceTimeWarning(float seconds)
    {
        var msg = seconds >= 60f
            ? Loc.GetString("raid-time-warning-min", ("min", (int)System.Math.Round(seconds / 60f)))
            : Loc.GetString("raid-time-warning-sec", ("sec", (int)seconds));
        _chat.DispatchServerAnnouncement(msg, Color.Orange);
    }

    /// <summary>
    /// Кнопка входа: собирает всех мобов на гриде кнопки и переносит их на спавн-маркеры карты рейда.
    /// Запускает таймер рейда, если он ещё не идёт.
    /// </summary>
    private void OnEntryActivate(EntityUid uid, RaidEntryComponent comp, ActivateInWorldEvent args)
    {
        if (!TryGetController(out var ctrlUid, out var ctrl) || ctrl.LoadedMap is not { } map)
        {
            Log.Warning("[raid] кнопка входа: контроллер рейда не найден или карта не загружена");
            return;
        }

        var grid = Transform(uid).GridUid;
        if (grid == null)
            return;

        var spawns = GetSpawns(map);
        if (spawns.Count == 0)
        {
            Log.Warning("[raid] на карте рейда нет спавн-маркеров RaidSpawnMarker — вход отменён");
            return;
        }

        // Все мобы (игроки и NPC) на гриде хаба.
        var entrants = new List<EntityUid>();
        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var mob, out _, out var xform))
        {
            if (xform.GridUid == grid)
                entrants.Add(mob);
        }

        if (entrants.Count == 0)
            return;

        for (var i = 0; i < entrants.Count; i++)
        {
            var target = spawns[i % spawns.Count];
            var coords = Transform(target).Coordinates;
            // Сдвиг, если на один маркер попадает больше одного входящего, чтобы не оказаться на тайле.
            if (i >= spawns.Count)
                coords = coords.Offset(new Vector2(i / spawns.Count, 0f));

            StopPulls(entrants[i]);
            _transform.SetCoordinates(entrants[i], coords);
            ctrl.Raiders.Add(entrants[i]);
        }

        if (!ctrl.Active)
            StartRaid(ctrlUid, ctrl);
    }

    private void StartRaid(EntityUid uid, RaidControllerComponent comp)
    {
        comp.Active = true;
        comp.EndTime = _timing.CurTime + TimeSpan.FromSeconds(comp.RaidDuration);

        // Свежий заход: убираем хлам прошлого рейда и заново заселяем локацию (лут + скавы).
        if (comp.LoadedMap is { } map)
            RepopulateRaid(map);
        // Готовим пороги предупреждений (по убыванию: сначала дальние, потом ближние).
        comp.PendingWarnings = comp.WarningTimes.Where(t => t < comp.RaidDuration)
            .OrderByDescending(t => t).ToList();
        _chat.DispatchServerAnnouncement(Loc.GetString("raid-started"), Color.OrangeRed);
        if (comp.StartSound != null)
            _audio.PlayGlobal(comp.StartSound, Filter.Broadcast(), true);
    }

    private void EndRaid(EntityUid uid, RaidControllerComponent comp)
    {
        // Сбрасываем состояние ДО добивания: смерть от MiaDamage поднимет MobStateChanged, а он не
        // должен повторно войти в завершение (Active уже false) или дёргать KIA-фид (список уже пуст).
        comp.Active = false;
        comp.EndTime = null;
        comp.PendingWarnings.Clear();
        var raiders = comp.Raiders.ToList();
        comp.Raiders.Clear();

        // Не успел эвакуироваться к истечению таймера — MIA: добиваем (теряет всё). Тело и снаряжение
        // остаются на локации лутом и исчезнут на рестарте раунда (режим раунд-скоуп). Если MiaDamage
        // не задан — мягкий режим без последствий.
        foreach (var raider in raiders)
        {
            if (Exists(raider) && comp.MiaDamage is { } mia && _mobState.IsAlive(raider))
                _damageable.TryChangeDamage(raider, mia, ignoreResistances: true, origin: uid);
        }

        _chat.DispatchServerAnnouncement(Loc.GetString("raid-ended"), Color.OrangeRed);
        if (comp.EndSound != null)
            _audio.PlayGlobal(comp.EndSound, Filter.Broadcast(), true);
    }

    /// <summary>Завершает рейд досрочно, если в нём не осталось рейдеров (все вышли или погибли).</summary>
    private void CheckRaidEnd(EntityUid uid, RaidControllerComponent comp)
    {
        if (comp.Active && comp.Raiders.Count == 0)
            EndRaid(uid, comp);
    }

    /// <summary>
    /// Сбрасывает локацию к свежему состоянию: удаляет оставшийся лут и скавов прошлого захода и
    /// заново разбрасывает наполнение через все лут-поля карты. Тела игроков не трогаем (исчезнут на
    /// рестарте раунда). За счёт этого каждый рейд начинается с полной, нетронутой локации.
    /// </summary>
    private void RepopulateRaid(MapId map)
    {
        // Старый лут прошлого захода.
        var lootQuery = EntityQueryEnumerator<RaidLootComponent, TransformComponent>();
        while (lootQuery.MoveNext(out var lootUid, out _, out var xform))
        {
            if (xform.MapID == map)
                QueueDel(lootUid);
        }

        // Старые скавы/боссы (живые и трупы — все носители добычи).
        var skavQuery = EntityQueryEnumerator<RaidLootCarrierComponent, TransformComponent>();
        while (skavQuery.MoveNext(out var skavUid, out _, out var xform))
        {
            if (xform.MapID == map)
                QueueDel(skavUid);
        }

        // Заново заселяем через все лут-поля на карте рейда (лут + скавы).
        var fieldQuery = EntityQueryEnumerator<RaidLootFieldComponent, TransformComponent>();
        while (fieldQuery.MoveNext(out var fieldUid, out var field, out var xform))
        {
            if (xform.MapID == map)
                _lootField.Populate(fieldUid, field);
        }
    }

    /// <summary>
    /// Успешный экстракт рейдера: возврат на хаб + снятие из списка рейдеров + объявление. Вызывается
    /// из <c>RaidExtractionSystem</c>, когда рейдер достоял экстракт.
    /// </summary>
    public void ExtractRaider(EntityUid controller, RaidControllerComponent comp, EntityUid raider)
    {
        if (!comp.Raiders.Remove(raider))
            return;

        // Считаем и «продаём» вынесенную добычу: все предметы с меткой RaidLoot в инвентаре/руках/сумках.
        // Стартовое снаряжение игрока метки не имеет — остаётся при нём.
        var loot = new List<EntityUid>();
        CollectLoot(raider, loot);

        var value = 0.0;
        foreach (var item in loot)
            value += _pricing.GetPrice(item, includeContents: false);

        var reward = comp.BaseReward + (comp.CreditsPerTc > 0f ? (int)(value / comp.CreditsPerTc) : 0);
        if (comp.MaxReward > 0)
            reward = Math.Min(reward, comp.MaxReward);

        foreach (var item in loot)
            QueueDel(item);

        ReturnToHub(controller, raider);

        // Награда — физическими телекристаллами рядом с игроком на хабе (можно потерять с трупа).
        if (reward > 0)
            _stack.SpawnMultipleNextToOrDrop(comp.RewardCurrency, reward, raider);

        var name = Comp<MetaDataComponent>(raider).EntityName;
        _chat.DispatchServerAnnouncement(
            Loc.GetString("raid-extracted", ("name", name), ("value", (int)value), ("reward", reward)),
            Color.LimeGreen);

        // Все вышли/погибли — нет смысла держать таймер.
        CheckRaidEnd(controller, comp);
    }

    /// <summary>
    /// Рекурсивно собирает в <paramref name="into"/> все предметы с меткой <see cref="RaidLootComponent"/>
    /// из дерева трансформа <paramref name="root"/> (надетое, в руках, в сумках). Обходим именно детей
    /// трансформа: содержимое контейнеров инвентаря парентится на держателя, поэтому один обход
    /// покрывает все слоты и вложенные сумки.
    /// </summary>
    private void CollectLoot(EntityUid root, List<EntityUid> into)
    {
        var en = Transform(root).ChildEnumerator;
        while (en.MoveNext(out var child))
        {
            if (HasComp<RaidLootComponent>(child))
                into.Add(child);

            CollectLoot(child, into);
        }
    }

    /// <summary>Телепортирует моба на точку возврата хаба (или на сам контроллер, если маркера нет).</summary>
    private void ReturnToHub(EntityUid controller, EntityUid raider)
    {
        StopPulls(raider);
        _transform.SetCoordinates(raider, GetHubReturn(controller));
    }

    private EntityCoordinates GetHubReturn(EntityUid controller)
    {
        var returnQuery = EntityQueryEnumerator<RaidReturnComponent, TransformComponent>();
        while (returnQuery.MoveNext(out _, out _, out var xform))
            return xform.Coordinates;

        return Transform(controller).Coordinates;
    }

    /// <summary>
    /// Авто-расстановка маркеров на тестовой карте рейда: если на карте нет маркеров входа, ставит
    /// несколько спавнов, точку экстракта и поля лута/скавов на случайных свободных тайлах. Нужно,
    /// чтобы прогнать режим на любой готовой карте без ручной расстановки. На карте с уже
    /// размещёнными маркерами каждая категория пропускается (ничего не дублируется).
    /// </summary>
    private void AutoSetupRaidMap(MapId map)
    {
        // Первый грид карты рейда.
        EntityUid gridUid = default;
        MapGridComponent? grid = null;
        var gridQuery = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var gUid, out var gComp, out var gXform))
        {
            if (gXform.MapID != map)
                continue;
            gridUid = gUid;
            grid = gComp;
            break;
        }
        if (grid == null)
        {
            Log.Warning("[raid] авто-настройка: на карте рейда нет грида — отменено");
            return;
        }

        // Свободные напольные тайлы (не пустые/космос, без анкоренных структур).
        var free = new List<Vector2i>();
        var anchored = new List<EntityUid>();
        foreach (var tile in _map.GetAllTiles(gridUid, grid))
        {
            if (tile.Tile.IsEmpty)
                continue;
            anchored.Clear();
            _map.GetAnchoredEntities((gridUid, grid), tile.GridIndices, anchored);
            if (anchored.Count == 0)
                free.Add(tile.GridIndices);
        }
        if (free.Count == 0)
        {
            Log.Warning("[raid] авто-настройка: нет свободных тайлов — отменено");
            return;
        }

        _random.Shuffle(free);
        EntityCoordinates At(int i) => _map.GridTileToLocal(gridUid, grid, free[((i % free.Count) + free.Count) % free.Count]);

        // Точки входа (4) — если их ещё нет.
        if (!HasOnMap<RaidSpawnComponent>(map))
            for (var i = 0; i < 4 && i < free.Count; i++)
                Spawn("RaidSpawnMarker", At(i));

        // Точка экстракта — на дальнем (по перемешанному списку) тайле.
        if (!HasOnMap<RaidExtractionPointComponent>(map))
            Spawn("RaidExtractionPoint", At(free.Count - 1));

        // Поля лута и скавов — заселяются контроллером на старте каждого рейда.
        if (!HasOnMap<RaidLootFieldComponent>(map))
        {
            Spawn("RaidLootField", At(free.Count / 2));
            Spawn("RaidSkavField", At(free.Count / 3));
        }

        Log.Info($"[raid] авто-настройка карты рейда (map {map}) завершена");
    }

    /// <summary>Есть ли на карте хоть одна сущность с компонентом <typeparamref name="T"/>.</summary>
    private bool HasOnMap<T>(MapId map) where T : IComponent
    {
        var query = EntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapID == map)
                return true;
        }
        return false;
    }

    /// <summary>Первый контроллер рейда на сервере (он один).</summary>
    public bool TryGetController(out EntityUid uid, out RaidControllerComponent comp)
    {
        var query = EntityQueryEnumerator<RaidControllerComponent>();
        if (query.MoveNext(out uid, out comp!))
            return true;

        uid = default;
        comp = default!;
        return false;
    }

    /// <summary>Все спавн-маркеры рейда на карте, по возрастанию номера.</summary>
    private List<EntityUid> GetSpawns(MapId mapId)
    {
        var result = new List<(int Index, EntityUid Uid)>();
        var query = EntityQueryEnumerator<RaidSpawnComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var marker, out var xform))
        {
            if (xform.MapID == mapId)
                result.Add((marker.SpawnIndex, uid));
        }
        return result.OrderBy(s => s.Index).Select(s => s.Uid).ToList();
    }

    /// <summary>
    /// Останавливает перетаскивание в обе стороны перед телепортом (иначе джойнт связал бы тела на
    /// разных картах и движок упал бы на DebugAssert «cross-map joint»). Копия логики из ротации арен.
    /// </summary>
    private void StopPulls(EntityUid mob)
    {
        if (TryComp<PullableComponent>(mob, out var pullable) && pullable.BeingPulled)
            _pulling.TryStopPull(mob, pullable);

        if (TryComp<PullerComponent>(mob, out var puller)
            && puller.Pulling is { } pulled
            && TryComp<PullableComponent>(pulled, out var pulledComp))
            _pulling.TryStopPull(pulled, pulledComp);
    }
}
