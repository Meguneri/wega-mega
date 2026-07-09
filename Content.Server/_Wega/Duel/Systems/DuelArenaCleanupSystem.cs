using System.Linq;
using Content.Server._Wega.Duel.Components;
using Content.Shared._Wega.Duel.Components;
using Content.Server.Botany.Components;
using Content.Server.Chat.Managers;
using Content.Server.Construction.Components;
using Content.Server.Spawners.Components;
using Content.Server.Spawners.EntitySystems;
using Content.Server.Traitor.Uplink.SurplusBundle;
using Content.Shared._Wega.Spawners.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Modular.Suit;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Storage.Components;
using Content.Shared._Wega.Magic;
using Content.Shared.Blood.Cult.Components;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Modular.Suit;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.DeviceLinking.Events;
using Robust.Shared.Localization;
using Content.Shared.Fluids.Components;
using Content.Shared.Implants.Components;
using Content.Shared.Inventory;
using Content.Shared.Light.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Slippery;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Damage.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Очистка дуэльной арены. Удаляет:
/// — предметы из ящика-арсенала (<see cref="ArenaIssuedItemComponent"/>),
/// — потраченные гильзы (<see cref="CartridgeAmmoComponent"/> с Spent=true),
/// — лужи крови/химии.
/// Замаппленные вещи карты не трогаются: они загружаются до старта дуэли
/// и поэтому не получают тег ArenaIssuedItem.
/// </summary>
public sealed class DuelArenaCleanupSystem : EntitySystem
{
    [Dependency] private DeviceLinkSystem _link = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private ModularSuitSystem _modSuit = default!;
    [Dependency] private SpawnerSystem _spawner = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private IMapManager _mapManager = default!;

    private static readonly ProtoId<TagPrototype> SheetTag = "Sheet";
    private static readonly ProtoId<TagPrototype> SoapTag = "Soap";

    /// <summary>
    /// Прототип, который кидает дуэльный спавнер-аирдроп. Спавнер кидает не сам ящик, а
    /// маяк-телеграф (DuelSupplyDropBeacon); по этому id находим и гасим спавнер в конце боя.
    /// </summary>
    private static readonly EntProtoId DuelSupplyDropProto = "DuelSupplyDropBeacon";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DuelArenaCleanupComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<DuelArenaCleanupComponent, SignalReceivedEvent>(OnSignalReceived);

        // Тегаем гильзы и прочие картриджи, заспавненные во время активной дуэли,
        // чтобы клинап их убрал независимо от того, из чьего оружия они вылетели.
        SubscribeLocalEvent<CartridgeAmmoComponent, ComponentStartup>(OnCartridgeStartup);

        // Оружие из RandomSpawner-обёрток (например WeaponRifleSKM28RandomSkin — «случайный
        // скин» SKM-28): обёртка из ящика помечается ArenaIssued, но она через RandomSpawner
        // спавнит РЕАЛЬНУЮ винтовку (Spawn без события) и самоудаляется — заспавненный ствол
        // метку не наследует и переживал очистку. RandomSpawner отрабатывает в MapInitEvent
        // сразу при выдаче из ящика, т.е. ствол появляется на гриде активной дуэли — метим его
        // тем же правилом «создано во время боя на арене → убирается клинапом», что и гильзы.
        SubscribeLocalEvent<GunComponent, ComponentStartup>(OnGunStartup);

        // Стаки (алоэ-крем, мази и прочее счётное снаряжение из ящика): сам стак из ящика
        // метится, но при РАЗДЕЛЕНИИ стака (взять часть в руку, бросить часть, применить)
        // создаётся НОВАЯ сущность-стак через Spawn без метки — она переживала очистку.
        // Метим любой стак, появившийся на гриде активной арены, тем же правилом, что и гильзы.
        SubscribeLocalEvent<StackComponent, MapInitEvent>(OnStackStartup, after: [typeof(StorageSystem)]);

        // То же правило «создано во время боя на арене → убирается клинапом» для:
        // — рун культа и магических рун со свитка (нарисованы/наспавнены во время дуэли);
        // — листов материалов, выпавших из сломанных за бой стен.
        // Всё, что игрок принёс с собой (заспавнено НЕ в активной дуэли), метку не получает.
        SubscribeLocalEvent<BloodRuneComponent, ComponentStartup>(OnRuneStartup);
        SubscribeLocalEvent<MagicRuneComponent, ComponentStartup>(OnRuneStartup);
        SubscribeLocalEvent<MaterialComponent, ComponentStartup>(OnMaterialStartup);

        // Кластерное мыло (SlipocalypseClusterSoap) при срабатывании спавнит НОВЫЕ обмылки
        // (SoapletSyndie) через Spawn(FillPrototype) — они не лежат в контейнере мыла, поэтому
        // MarkIssuedRecursive их не достаёт. Метим любой свежий Slippery-предмет с тегом Soap,
        // появившийся в зоне активной дуэли, как боевой мусор — иначе обмылки переживут очистку.
        SubscribeLocalEvent<SlipperyComponent, ComponentStartup>(OnSlipperyStartup);

        // МОД-скафандры и прочее: содержимое (модули, батарея, части брони) спавнится и
        // вкладывается в контейнеры уже ПОСЛЕ того, как MarkIssuedRecursive отработал на самом
        // предмете. Нельзя повторно подписаться на ModularSuitPreassembledComponent/MapInitEvent
        // (этим уже владеет ModularSuitSystem), поэтому ловим само вкладывание в контейнер: если
        // владелец контейнера помечен как выданный аренной — помечаем и вложенную сущность.
        // Этот же обработчик покрывает переключаемый шлем хардсьютов: ToggleableClothingSystem
        // спавнит ClothingUid в MapInitEvent и вкладывает его в контейнер скафандра — вставка
        // помеченного аренной предмета тегирует шлем. Отдельная подписка на
        // ToggleableClothingComponent/MapInitEvent не нужна (и запрещена — ею владеет
        // ToggleableClothingSystem).
        SubscribeLocalEvent<ArenaIssuedItemComponent, EntInsertedIntoContainerMessage>(OnIssuedInsert);

        // Аирдроп-ящик снаряжения: StorageFill кладёт лут в MapInitEvent. Подписываемся
        // после StorageSystem, чтобы содержимое уже было в ящике, и помечаем ящик + весь
        // лут рекурсивно — тогда очистка арены уберёт их после раунда.
        SubscribeLocalEvent<ArenaSupplyDropComponent, MapInitEvent>(
            OnSupplyDropMapInit, after: [typeof(StorageSystem)]);

        // Упаковки со SpawnItemsOnUse (набор курильщика Интердайн, коробка гипопена и т.п.)
        // при использовании спавнят НОВОЕ содержимое без метки арены, а сама упаковка
        // самоудаляется. Если упаковка была выдана ареной — пробрасываем метку на содержимое
        // рекурсивно, иначе пачка/зажигалка/гипопен (и сигарета во рту) остаются после боя.
        SubscribeLocalEvent<ArenaIssuedItemComponent, SpawnItemsOnUsedEvent>(OnIssuedSpawnedItems);
    }

    private void OnIssuedSpawnedItems(EntityUid uid, ArenaIssuedItemComponent comp, SpawnItemsOnUsedEvent args)
    {
        foreach (var spawned in args.Spawned)
            MarkIssuedRecursive(spawned);
    }

    /// <summary>
    /// Извлекает всех существ (мобов) из удаляемого предмета перед его <see cref="QueueDel"/>, чтобы
    /// каскадное удаление не забрало тело вместе с предметом (например, соперника, спрятавшегося в
    /// коробке-невидимке <c>StealthBox</c>).
    ///
    /// Обходим именно ДЕТЕЙ трансформа, а не конкретные типы контейнеров: каскадное удаление идёт по
    /// дереву трансформа, поэтому ловить мобов надо там же. Это покрывает любой случай — entity_storage
    /// закрытой коробки, вложенный контейнер, да и моба, припаркованного прямо на трансформе предмета.
    /// Каждого моба вынимаем из его контейнера (если он в нём) и реперентим на грид/карту, где лежит
    /// удаляемый предмет — так он «выпадает» на месте коробки и переживает её удаление.
    /// </summary>
    private void EjectMobsBeforeDelete(EntityUid uid)
    {
        var dropParent = Transform(uid).ParentUid;
        EjectMobsRecursive(uid, dropParent, Transform(uid).Coordinates);
    }

    private void EjectMobsRecursive(EntityUid uid, EntityUid dropParent, EntityCoordinates dropAt)
    {
        // Снимок детей: вынимание мобов меняет дерево трансформа во время обхода.
        var children = new List<EntityUid>();
        var en = Transform(uid).ChildEnumerator;
        while (en.MoveNext(out var child))
            children.Add(child);

        foreach (var child in children)
        {
            if (HasComp<MobStateComponent>(child))
            {
                // Вынимаем из контейнера (entity_storage коробки и т.п.) — это уже реперентит моба
                // наружу. На всякий случай дотягиваем его до грида/карты предмета, чтобы он
                // гарантированно не остался ребёнком удаляемой сущности.
                if (_container.TryGetContainingContainer((child, null), out var cont))
                    _container.Remove(child, cont, force: true, reparent: true);

                if (Transform(child).ParentUid == uid && dropParent.IsValid())
                    _transform.SetCoordinates(child, dropAt);
            }
            else
            {
                // Моб может сидеть глубже (контейнер в контейнере, ящик в ящике) — идём рекурсивно.
                EjectMobsRecursive(child, dropParent, dropAt);
            }
        }
    }

    private void OnSupplyDropMapInit(EntityUid uid, ArenaSupplyDropComponent comp, MapInitEvent args)
    {
        MarkIssuedRecursive(uid);
    }

    private void OnInit(EntityUid uid, DuelArenaCleanupComponent comp, ComponentInit args)
    {
        _link.EnsureSinkPorts(uid, comp.TriggerPort);
    }

    private void OnCartridgeStartup(EntityUid uid, CartridgeAmmoComponent comp, ComponentStartup args)
    {
        // Тегаем гильзу только если она появилась в радиусе активной арены — иначе тег
        // повесился бы на каждый картридж, заспавненный где угодно на сервере во время боя
        // (перезарядка/стрельба вне арены), и чужие гильзы могла бы удалить очистка.
        if (IsInActiveDuelRange(uid))
            EnsureComp<ArenaIssuedItemComponent>(uid);
    }

    private void OnGunStartup(EntityUid uid, GunComponent comp, ComponentStartup args)
    {
        // Только оружие, заспавненное в зоне активной арены (RandomSpawner-обёртки из ящика и
        // прочие наспавненные за бой стволы). Принесённое игроком извне ComponentStartup на
        // арене не вызывает, поэтому его метка не касается.
        if (IsInActiveDuelRange(uid))
            EnsureComp<ArenaIssuedItemComponent>(uid);
    }

    private void OnStackStartup(EntityUid uid, StackComponent comp, MapInitEvent args)
    {
        // Только стаки, появившиеся в зоне активной арены (отделённые от выданного стака части
        // и т.п.). Принесённое игроком извне ComponentStartup на арене не вызывает.
        if (IsInActiveDuelRange(uid))
            EnsureComp<ArenaIssuedItemComponent>(uid);
    }

    private void OnRuneStartup(EntityUid uid, BloodRuneComponent comp, ComponentStartup args)
        => TagIfBattleDebrisGrid(uid);

    private void OnRuneStartup(EntityUid uid, MagicRuneComponent comp, ComponentStartup args)
        => TagIfBattleDebrisGrid(uid);

    private void OnMaterialStartup(EntityUid uid, MaterialComponent comp, ComponentStartup args)
    {
        // Только листы материалов (обломки сломанных стен), не руда/слитки в чьём-то инвентаре.
        if (_tag.HasTag(uid, SheetTag))
            TagIfBattleDebris(uid);
    }

    private void OnSlipperyStartup(EntityUid uid, SlipperyComponent comp, ComponentStartup args)
    {
        // Только мыло/обмылки (тег Soap) — рассыпанные кластерным мылом куски, а не чужие
        // банановые корки или прочие скользкие вещи, принесённые игроком.
        if (!_tag.HasTag(uid, SoapTag))
            return;

        // ВАЖНО: обмылки спавнятся кластерным мылом через Spawn(prototype, MapCoordinates).
        // На этот момент обход гридов ещё не перепривязал сущность к гриду арены — её GridUid
        // указывает на карту, поэтому грид-проверка в IsInActiveDuelRange/TagIfBattleDebris даёт
        // false и тег не ставится (в отличие от гильз, что вылетают сразу на гриде стрелка).
        // Поэтому грид под обмылком ищем по его мировой позиции через карту.
        if (IsOnActiveArenaGrid(uid))
            EnsureComp<ArenaIssuedItemComponent>(uid);
    }

    /// <summary>
    /// Стоит ли сущность над гридом активной арены (весь грид, а не радиус). Грид определяется по
    /// мировой позиции цели через <see cref="IMapManager.TryFindGridAt"/>, поэтому не зависит от
    /// её собственного GridUid — нужно для предметов, заспавненных по MapCoordinates (обмылки
    /// кластерного мыла), у которых грид на момент старта ещё не разрешён. В космосе (у арены нет
    /// грида) откатываемся на радиус <see cref="DuelArenaComponent.ScanRange"/>.
    /// </summary>
    public bool IsOnActiveArenaGrid(EntityUid target)
    {
        var pos = Transform(target).MapPosition;
        var gridUnderTarget = _mapManager.TryFindGridAt(pos, out var gridUid, out _) ? gridUid : (EntityUid?)null;

        var query = EntityQueryEnumerator<DuelArenaComponent>();
        while (query.MoveNext(out var arenaUid, out var arena))
        {
            if (!arena.IsActive)
                continue;

            var arenaGrid = Transform(arenaUid).GridUid;

            // Весь грид арены: цель физически над тем же гридом, что и маяк арены.
            if (arenaGrid != null && gridUnderTarget == arenaGrid)
                return true;

            // Космос/без грида: запасной охват по дистанции до маяка.
            if (arenaGrid == null)
            {
                var arenaPos = Transform(arenaUid).MapPosition;
                if (arenaPos.MapId == pos.MapId && (arenaPos.Position - pos.Position).Length() <= arena.ScanRange)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Помечает сущность как «выданную аренной», только если она появилась в зоне активной дуэли
    /// (GridUid-проверка). Для предметов, у которых GridUid уже разрешён к моменту старта.
    /// </summary>
    private void TagIfBattleDebris(EntityUid uid)
    {
        if (IsInActiveDuelRange(uid))
            EnsureComp<ArenaIssuedItemComponent>(uid);
    }

    /// <summary>
    /// Как <see cref="TagIfBattleDebris"/>, но использует позиционную проверку через
    /// <see cref="IsOnActiveArenaGrid"/>. Нужно для предметов, заспавненных по MapCoordinates
    /// (руны, обмылки), у которых GridUid может быть ещё не разрешён в момент ComponentStartup.
    /// </summary>
    private void TagIfBattleDebrisGrid(EntityUid uid)
    {
        if (IsOnActiveArenaGrid(uid))
            EnsureComp<ArenaIssuedItemComponent>(uid);
    }

    /// <summary>
    /// Тегирует сущность и всё вложенное в её контейнеры тегом <see cref="ArenaIssuedItemComponent"/>.
    /// Используется при спавне предметов из арена-ящика и кубика войны.
    /// </summary>
    public void MarkIssuedRecursive(EntityUid uid)
    {
        // Никогда не метим живых существ (игроков/NPC) и не лезем в их инвентарь. Иначе боец,
        // залезший в выданную ареной коробку-невидимку, получал бы метку «выданного снаряжения»
        // (через OnIssuedInsert при вкладывании в контейнер коробки) и удалялся очисткой — даже
        // после выхода из коробки. Вещи в инвентаре моба — это его собственность, не снаряжение арены.
        if (HasComp<MobStateComponent>(uid))
            return;

        EnsureComp<ArenaIssuedItemComponent>(uid);

        // Пристёгнутая одежда (шлем хардсьюта и т.п.): её сущность (ClothingUid) при
        // развёрнутом состоянии лежит не в контейнере, а надета в слот — рекурсия по
        // контейнерам её не достанет. Тегируем явно, иначе шлем переживёт очистку.
        if (TryComp<ToggleableClothingComponent>(uid, out var toggleable) && toggleable.ClothingUid != null)
            MarkIssuedRecursive(toggleable.ClothingUid.Value);

        if (!TryComp<ContainerManagerComponent>(uid, out var manager))
            return;

        foreach (var c in _container.GetAllContainers(uid, manager))
            foreach (var contained in c.ContainedEntities)
                MarkIssuedRecursive(contained);
    }

    private void OnIssuedInsert(EntityUid uid, ArenaIssuedItemComponent comp, EntInsertedIntoContainerMessage args)
    {
        // Сущность вложили в контейнер предмета, который сам помечен как выданный аренной
        // (например, модуль в МОД-скафандр при пре-сборке). Помечаем вложенное рекурсивно,
        // чтобы очистка арены убрала и его.
        MarkIssuedRecursive(args.Entity);
    }

    private void OnSignalReceived(EntityUid uid, DuelArenaCleanupComponent comp, ref SignalReceivedEvent args)
    {
        if (args.Port != comp.TriggerPort)
            return;

        if (IsDuelActiveNearby(uid, comp.Range))
            return;

        CleanupArea(uid, comp.Range);
        _chat.DispatchServerAnnouncement(Loc.GetString("duel-arena-cleaned"), Color.Gold);
    }

    /// <summary>
    /// Находится ли сущность в радиусе (<see cref="DuelArenaComponent.ScanRange"/>) какой-либо
    /// активной дуэльной арены. Используется, чтобы тегать только то, что заспавнилось на арене
    /// во время боя, а не где угодно на сервере.
    /// </summary>
    private bool IsInActiveDuelRange(EntityUid target)
    {
        var targetXform = Transform(target);
        var pos = targetXform.MapPosition;
        var targetGrid = targetXform.GridUid;
        var query = EntityQueryEnumerator<DuelArenaComponent>();
        while (query.MoveNext(out var arenaUid, out var arena))
        {
            if (!arena.IsActive)
                continue;
            if (InRange(arenaUid, pos, targetGrid, arena.ScanRange))
                return true;
        }
        return false;
    }

    private bool IsDuelActiveNearby(EntityUid originEntity, float range)
    {
        var originXform = Transform(originEntity);
        var origin = originXform.MapPosition;
        var originGrid = originXform.GridUid;
        var query = EntityQueryEnumerator<DuelArenaComponent>();
        while (query.MoveNext(out var arenaUid, out var arena))
        {
            if (!arena.IsActive)
                continue;
            if (InRange(arenaUid, origin, originGrid, range))
                return true;
        }
        return false;
    }

    public void CleanupArea(EntityUid originEntity, float range)
    {
        var originXform = Transform(originEntity);
        var origin = originXform.MapPosition;
        var originGrid = originXform.GridUid;

        // 1. Снаряжение из ящика + гильзы (все помечены ArenaIssuedItemComponent).
        var issuedQuery = EntityQueryEnumerator<ArenaIssuedItemComponent>();
        while (issuedQuery.MoveNext(out var itemUid, out _))
        {
            if (!InRange(itemUid, origin, originGrid, range))
                continue;

            // Подстраховка: живое существо (боец) никогда не считается выданным снаряжением и не
            // удаляется. Если метка как-то на него попала (например, со старого бага коробки) —
            // снимаем её и пропускаем, чтобы очистка не убила игрока.
            if (HasComp<MobStateComponent>(itemUid))
            {
                RemCompDeferred<ArenaIssuedItemComponent>(itemUid);
                continue;
            }

            // Заякоренное не трогаем (стены, мебель карты) — КРОМЕ рун: руны размещаются на
            // снапгриде заякоренными, и без этого исключения нарисованные за бой руны
            // переживали бы очистку.
            if (Transform(itemUid).Anchored
                && !HasComp<MagicRuneComponent>(itemUid) && !HasComp<BloodRuneComponent>(itemUid))
                continue;

            // Обувь без статов (нет ускорения/магнитов/анти-скольжения) — косметика, как
            // собственные ботинки игрока. Не удаляем и снимаем тег, чтобы не трогать впредь.
            if (IsStatlessFootwear(itemUid))
            {
                RemCompDeferred<ArenaIssuedItemComponent>(itemUid);
                continue;
            }

            // Вколотый имплант: принудительно вынимаем, чтобы SharedSubdermalImplantSystem
            // корректно снял дарованные действия/компоненты.
            if (_container.TryGetContainingContainer((itemUid, null), out var container)
                && container.ID == ImplanterComponent.ImplantSlotId)
            {
                _container.Remove(itemUid, container, reparent: false, force: true);
            }

            // МОД-контроллер: его развёрнутые части надеты в слоты брони игрока, а не вложены
            // в контроллер — удаляем их явно, иначе шлем/нагрудник/перчатки/ботинки останутся
            // на игроке после удаления самого МОД.
            if (HasComp<ModularSuitComponent>(itemUid))
            {
                foreach (var part in _modSuit.GetEquippedParts(itemUid))
                    QueueDel(part);
            }

            // Внутри выданного предмета может сидеть существо (например, соперник в
            // коробке-невидимке StealthBox). Извлекаем мобов перед удалением, иначе QueueDel
            // контейнера каскадно удалит тело соперника вместе с коробкой.
            EjectMobsBeforeDelete(itemUid);

            QueueDel(itemUid);
        }

        // 2. Лужи на полу (кровь, химия и т.п.).
        var puddleQuery = EntityQueryEnumerator<PuddleComponent>();
        while (puddleQuery.MoveNext(out var puddleUid, out _))
        {
            if (!InRange(puddleUid, origin, originGrid, range))
                continue;

            QueueDel(puddleUid);
        }

        // 3. Брёвна, оставшиеся после уничтожения деревьев на арене.
        var logQuery = EntityQueryEnumerator<LogComponent>();
        while (logQuery.MoveNext(out var logUid, out _))
        {
            if (!InRange(logUid, origin, originGrid, range))
                continue;
            if (Transform(logUid).Anchored)
                continue;

            QueueDel(logUid);
        }

        // 4. Миньоны-помощники (усиление для проигравшего 3 раза подряд).
        var minionQuery = EntityQueryEnumerator<ArenaLoserMinionComponent, TransformComponent>();
        while (minionQuery.MoveNext(out var minionUid, out _, out _))
        {
            if (!InRange(minionUid, origin, originGrid, range))
                continue;

            QueueDel(minionUid);
        }

        // 3b. Все магические руны со свитка рун (MagicRune) на гриде арены — удаляем блэнкетом,
        // независимо от того, нарисованы они ДО начала дуэли или во время боя. Метку
        // ArenaIssued получают только руны, заспавненные в активной дуэли (OnRuneStartup), поэтому
        // заранее размещённые руны без этого прохода пережили бы очистку. InRange ограничивает
        // удаление гридом арены, так что чужие руны вне дуэли не трогаются.
        var runeQuery = EntityQueryEnumerator<MagicRuneComponent>();
        while (runeQuery.MoveNext(out var runeUid, out _))
        {
            if (!InRange(runeUid, origin, originGrid, range))
                continue;

            QueueDel(runeUid);
        }

        // 3c. Обломки стен — гирдеры, оставшиеся после пролома стены за бой. Уникального компонента
        // у них нет, поэтому фильтруем конструируемые сущности по id прототипа (любой «*Girder*»).
        // Грид-скоуп (InRange) гарантирует, что чистится только сама арена.
        var girderQuery = EntityQueryEnumerator<ConstructionComponent>();
        while (girderQuery.MoveNext(out var girderUid, out _))
        {
            if (ProtoId(girderUid)?.Contains("Girder") != true)
                continue;
            if (!InRange(girderUid, origin, originGrid, range))
                continue;

            QueueDel(girderUid);
        }

        // 3d. Разбитые/перегоревшие лампочки и лампы. Целые (State Normal) НЕ трогаем, чтобы не
        // гасить рабочее освещение арены — убираем только сломанные.
        var bulbQuery = EntityQueryEnumerator<LightBulbComponent>();
        while (bulbQuery.MoveNext(out var bulbUid, out var bulb))
        {
            if (bulb.State == LightBulbState.Normal)
                continue;
            if (!InRange(bulbUid, origin, originGrid, range))
                continue;

            QueueDel(bulbUid);
        }

        // 3e. Осколки стекла (ShardGlass*) от разбитых за бой окон/стёкол.
        var shardQuery = EntityQueryEnumerator<SpaceGarbageComponent>();
        while (shardQuery.MoveNext(out var shardUid, out _))
        {
            if (ProtoId(shardUid)?.StartsWith("ShardGlass") != true)
                continue;
            if (!InRange(shardUid, origin, originGrid, range))
                continue;

            QueueDel(shardUid);
        }

        // Руны культа (BloodRune) и листы материалов от сломанных стен НЕ чистятся здесь
        // блэнкетом: они помечаются ArenaIssuedItem при спавне во время активной дуэли
        // (см. OnRuneStartup/OnMaterialStartup) и убираются общим проходом по меткам в п.1.
        // Так принесённое игроком извне (его руны/материалы) не удаляется.

        // 4. Сами арена-ящики (Full/Melee Arsenal — помечены markIssuedItems) — убираем вместе
        // с выданным снаряжением, чтобы после боя на арене не оставалось пустых ящиков.
        var crateQuery = EntityQueryEnumerator<SurplusBundleComponent>();
        while (crateQuery.MoveNext(out var crateUid, out var bundle))
        {
            if (!bundle.MarkIssuedItems)
                continue;
            if (!InRange(crateUid, origin, originGrid, range))
                continue;

            QueueDel(crateUid);
        }

        // 5. Останавливаем спавнер дуэльного аирдропа: после конца боя ящики снаряжения
        // больше не падают автоматически (без повторного нажатия кнопки).
        var spawnerQuery = EntityQueryEnumerator<SpawnerSignalControlComponent, TimedSpawnerComponent>();
        while (spawnerQuery.MoveNext(out var spawnerUid, out _, out var timed))
        {
            if (!timed.Enabled)
                continue;
            if (!timed.Prototypes.Contains(DuelSupplyDropProto))
                continue;
            if (!InRange(spawnerUid, origin, originGrid, range))
                continue;

            _spawner.SetEnabled(spawnerUid, timed, false);
        }
    }

    /// <summary>
    /// Обувь без эффектов на характеристики (скорость, магботы, анти-скольжение) — считаем
    /// косметической и не удаляем при очистке арены.
    /// </summary>
    private bool IsStatlessFootwear(EntityUid uid)
    {
        if (!TryComp<ClothingComponent>(uid, out var clothing) || (clothing.Slots & SlotFlags.FEET) == 0)
            return false;

        return !HasComp<ClothingSpeedModifierComponent>(uid)
            && !HasComp<MagbootsComponent>(uid)
            && !HasComp<NoSlipComponent>(uid)
            && !HasComp<ClothingSlowOnDamageModifierComponent>(uid);
    }

    /// <summary>
    /// Цель в зоне арены. Арена — отдельный грид, поэтому при наличии грида у источника охватываем
    /// весь его грид целиком (без ограничения радиусом) — это покрывает всю арену и не задевает
    /// станцию. Если у источника нет грида (в космосе) — откатываемся на проверку по дистанции.
    /// </summary>
    /// <summary>Id прототипа сущности (или null). Нужен для отбора сущностей без уникального
    /// компонента — гирдеров и осколков стекла — по их прототипу.</summary>
    private string? ProtoId(EntityUid uid)
        => MetaData(uid).EntityPrototype?.ID;

    private bool InRange(EntityUid target, MapCoordinates origin, EntityUid? originGrid, float range)
    {
        var targetXform = Transform(target);

        // Весь грид арены — без радиуса.
        if (originGrid != null)
        {
            if (targetXform.GridUid == originGrid)
                return true;

            // Надетые/зажатые предметы лежат в контейнерах инвентаря — у них GridUid == null,
            // поэтому прямая проверка грида их пропускает (перчатки/очки/импланты переживали
            // очистку). Резолвим грид по мировой позиции — она проходит через держателя.
            var wornPos = targetXform.MapPosition;
            return _mapManager.TryFindGridAt(wornPos, out var gridUid, out _) && gridUid == originGrid;
        }

        // Космос/без грида: запасной охват по дистанции.
        var pos = targetXform.MapPosition;
        if (pos.MapId != origin.MapId)
            return false;
        return (pos.Position - origin.Position).Length() <= range;
    }
}
