using System.Linq;
using System.Numerics;
using Content.Server._Wega.Duel.Components;
using Content.Server.Decals;
using Content.Server.Light.EntitySystems;
using Content.Server.Traitor.Uplink.SurplusBundle;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Decals;
using Content.Shared.DeviceLinking;
using Content.Shared.FixedPoint;
using Content.Shared.Item;
using Content.Shared.Light.Components;
using Content.Shared.Mobs.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Полное восстановление дуэльной арены к исходному (пристайн) состоянию после каждого раунда.
///
/// При ПЕРВОМ старте дуэли на арене (пока всё цело, боевого мусора ещё нет) снимается единый снимок:
/// — все плитки пола грида;
/// — все заякоренные конструкции карты (стены, окна, решётки, светильники, столы, перила, двери,
///   постеры, растения…) — КРОМЕ инфраструктуры с сигнальными связями (трекер, кнопки, шлюзы,
///   спавнеры): её пересоздание порвало бы линковки, поэтому её мы не трогаем;
/// — все свободные предметы-декор, лежащие на полу;
/// — все декали.
///
/// После каждого раунда арена приводится к снимку: провалы пола застилаются, разрушенные конструкции
/// чинятся или ставятся заново (а чужие постройки/обломки на их тайлах убираются), свободные предметы
/// удаляются и раскладываются заново, декали стираются и накатываются исходные. Восстановление
/// выполняется отложенно — на тике после завершения боя, вне стека события смерти.
/// </summary>
public sealed partial class DuelArenaRestoreSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private PoweredLightSystem _poweredLight = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private IMapManager _mapManager = default!;

    /// <summary>
    /// Заякоренная сущность — восстанавливаемая конструкция карты (её можно пересоздать по снимку).
    /// Исключаем живых существ, выданное ареной снаряжение, арсенал-ящики и ЛЮБУЮ инфраструктуру с
    /// сигнальными связями (трекер, кнопки, шлюзы, спавнеры) — её пересоздание порвало бы device-link.
    /// </summary>
    private bool IsRestorableStructure(EntityUid uid)
    {
        if (!Exists(uid) || MetaData(uid).EntityPrototype == null)
            return false;

        if (HasComp<MobStateComponent>(uid)
            || HasComp<ArenaIssuedItemComponent>(uid)
            || HasComp<ArenaCleanupExemptComponent>(uid) // достали из спавн-меню — не трогаем
            || HasComp<SurplusBundleComponent>(uid)
            || HasComp<DuelArenaComponent>(uid)
            || HasComp<DuelArenaCleanupComponent>(uid))
            return false;

        // Инфраструктуру с сигнальными связями (кнопки, шлюзы, спавнеры, трекер) не трогаем: её
        // пересоздание порвало бы линковки сигналов. ИСКЛЮЧЕНИЕ — светильники: штатные лампы тоже
        // держат device-link (SmartLight-сеть выключателей), но их восстанавливать НУЖНО, а потеря
        // этой связи безвредна (лампа горит от питания). Светильник опознаём по PoweredLight —
        // кнопки/шлюзы/спавнеры его не несут, поэтому они остаются исключёнными.
        if ((HasComp<DeviceLinkSourceComponent>(uid) || HasComp<DeviceLinkSinkComponent>(uid))
            && !HasComp<PoweredLightComponent>(uid))
            return false;

        return true;
    }

    /// <summary>Обломок снесённой стены — балка (Girder/*). В снимок не берём, при восстановлении убираем.</summary>
    private bool IsDebris(EntityUid uid)
        => MetaData(uid).EntityPrototype?.ID is { } proto && proto.Contains("Girder");

    // ── Снимок (один раз, при первом старте на пристайн-арене) ──────────────────────────────────

    /// <summary>
    /// Снимает эталон арены целиком (пол + конструкции + свободные предметы + декали). Вызывается при
    /// КАЖДОМ старте дуэли, но реально отрабатывает только один раз — пока арена не тронута.
    /// </summary>
    public void SnapshotArena(EntityUid arenaUid, DuelArenaComponent comp)
    {
        if (comp.SnapshotCaptured)
            return;

        var grid = Transform(arenaUid).GridUid;
        if (grid == null || !TryComp<MapGridComponent>(grid, out var gridComp))
        {
            Log.Warning($"[duel-arena] снимок невозможен: трекер {ToPrettyString(arenaUid)} не на гриде");
            return;
        }

        SnapshotTiles(grid.Value, gridComp, comp);
        SnapshotStructures(grid.Value, gridComp, comp);
        SnapshotProps(grid.Value, gridComp, comp);
        SnapshotDecals(grid.Value, gridComp, comp);

        comp.SnapshotCaptured = true;
        Log.Info($"[duel-arena] снимок арены снят ({ToPrettyString(arenaUid)}): "
            + $"{comp.TileSnapshot.Count} плиток пола, {comp.StructureSnapshot.Count} тайлов с конструкциями, "
            + $"{comp.PropSnapshot.Count} предметов, {comp.DecalSnapshot.Count} декалей");
    }

    private void SnapshotTiles(EntityUid grid, MapGridComponent gridComp, DuelArenaComponent comp)
    {
        var en = _map.GetAllTilesEnumerator(grid, gridComp);
        while (en.MoveNext(out var tileRef))
            comp.TileSnapshot[tileRef.Value.GridIndices] = tileRef.Value.Tile;
    }

    private void SnapshotStructures(EntityUid grid, MapGridComponent gridComp, DuelArenaComponent comp)
    {
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored)
                continue;
            if (!IsRestorableStructure(uid) || IsDebris(uid))
                continue;
            if (MetaData(uid).EntityPrototype?.ID is not { } proto)
                continue;

            var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
            if (!comp.StructureSnapshot.TryGetValue(tile, out var list))
            {
                list = new List<ArenaStructure>();
                comp.StructureSnapshot[tile] = list;
            }

            // Пишем КАЖДУЮ конструкцию тайла отдельно — на одном тайле их бывает несколько, в т.ч.
            // одинаковых с разным поворотом (две направленные оконные секции/перила по разным граням).
            // Снимок берётся один раз, поэтому дублей от повторных проходов тут не возникает.
            list.Add(new ArenaStructure(proto, xform.LocalRotation));
        }
    }

    private void SnapshotProps(EntityUid grid, MapGridComponent gridComp, DuelArenaComponent comp)
    {
        var query = EntityQueryEnumerator<ItemComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            // Только предметы, лежащие прямо на полу грида: не заякоренные, не в контейнере/руках/инвентаре.
            if (xform.GridUid != grid || xform.Anchored || xform.ParentUid != grid)
                continue;
            if (!IsRestorableProp(uid))
                continue;
            if (MetaData(uid).EntityPrototype?.ID is not { } proto)
                continue;

            comp.PropSnapshot.Add(new ArenaProp(proto, xform.LocalPosition, xform.LocalRotation));

            // Метим оригинал: если боец за бой унесёт его в инвентарь, метка позволит удалить именно
            // этот экземпляр при восстановлении и не оставить дубль после переспавна набора.
            EnsureComp<ArenaMapPropComponent>(uid);
        }
    }

    /// <summary>
    /// Свободный предмет — восстанавливаемый декор арены (его можно удалить и разложить заново). Живых
    /// существ, выданное ареной снаряжение и предметы с сигнальными связями (линкованный сигнальщик и
    /// т.п.) не трогаем: пересоздание порвало бы линковки, а снаряжение убирает штатная очистка.
    /// </summary>
    private bool IsRestorableProp(EntityUid uid)
        => !HasComp<MobStateComponent>(uid)
            && !HasComp<ArenaIssuedItemComponent>(uid)
            && !HasComp<ArenaCleanupExemptComponent>(uid) // достали из спавн-меню — не трогаем
            && !HasComp<DeviceLinkSourceComponent>(uid)
            && !HasComp<DeviceLinkSinkComponent>(uid);

    private void SnapshotDecals(EntityUid grid, MapGridComponent gridComp, DuelArenaComponent comp)
    {
        foreach (var (_, decal) in _decals.GetDecalsIntersecting(grid, gridComp.LocalAABB.Enlarged(1f)))
            comp.DecalSnapshot.Add(decal);
    }

    // ── Восстановление (на тике после конца боя) ────────────────────────────────────────────────

    /// <summary>Приводит арену к снимку: пол → конструкции → свободные предметы → декали.</summary>
    public void RestoreArena(EntityUid arenaUid, DuelArenaComponent comp)
    {
        if (!comp.SnapshotCaptured)
        {
            Log.Warning($"[duel-arena] восстановление пропущено: снимок ещё не снят ({ToPrettyString(arenaUid)})");
            return;
        }

        var grid = Transform(arenaUid).GridUid;
        if (grid == null || !TryComp<MapGridComponent>(grid, out var gridComp))
        {
            Log.Warning($"[duel-arena] восстановление невозможно: трекер {ToPrettyString(arenaUid)} не на гриде");
            return;
        }

        // Порядок важен: сперва пол (конструкции нужно на что-то якорить, предметы — куда класть),
        // затем конструкции, потом свободные предметы, и в конце декали (не кладутся на пустой тайл).
        RestoreTiles(grid.Value, gridComp, comp);
        RestoreStructures(arenaUid, grid.Value, gridComp, comp);
        RestoreProps(grid.Value, comp);
        RestoreDecals(grid.Value, gridComp, comp);
    }

    private void RestoreTiles(EntityUid grid, MapGridComponent gridComp, DuelArenaComponent comp)
    {
        var restored = 0;
        foreach (var (tile, saved) in comp.TileSnapshot)
        {
            if (_map.GetTileRef(grid, gridComp, tile).Tile != saved)
            {
                _map.SetTile(grid, gridComp, tile, saved);
                restored++;
            }
        }

        if (restored > 0)
            Log.Info($"[duel-arena] восстановлено плиток пола: {restored}");
    }

    private void RestoreStructures(EntityUid arenaUid, EntityUid grid, MapGridComponent gridComp, DuelArenaComponent comp)
    {
        var healed = 0;
        var respawned = 0;
        var removed = 0;
        var blockedCount = 0;
        var failed = 0;

        var anchored = new List<EntityUid>();
        foreach (var (tile, expected) in comp.StructureSnapshot)
        {
            try
            {
                anchored.Clear();
                _map.GetAnchoredEntities((grid, gridComp), tile, anchored);

                // Текущие восстанавливаемые конструкции на тайле (инфраструктуру не трогаем вовсе).
                var current = anchored.Where(IsRestorableStructure).ToList();

                // Сопоставляем ожидаемое с имеющимся: совпал прототип — чиним; нет — в очередь на спавн.
                var matched = new HashSet<EntityUid>();
                var toSpawn = new List<ArenaStructure>();
                foreach (var exp in expected)
                {
                    EntityUid? hit = null;
                    foreach (var e in current)
                    {
                        if (!matched.Contains(e) && MetaData(e).EntityPrototype?.ID == exp.Proto.Id)
                        {
                            hit = e;
                            break;
                        }
                    }

                    if (hit is { } found)
                    {
                        matched.Add(found);
                        HealStructure(found);
                        // Выравниваем поворот к эталону: если на тайле несколько одинаковых направленных
                        // секций, сопоставление идёт по прототипу и уцелевшую могли привязать «не к той»
                        // записи — принудительный поворот возвращает верную грань.
                        _transform.SetLocalRotation(found, exp.Rotation);
                        healed++;
                    }
                    else
                    {
                        toSpawn.Add(exp);
                    }
                }

                // Всё непарное на тайле снимка — обломки-балки, чужие постройки, лишние копии — убираем.
                foreach (var e in current)
                {
                    if (!matched.Contains(e))
                    {
                        Del(e);
                        removed++;
                    }
                }

                if (toSpawn.Count == 0)
                    continue;

                // Пол под конструкцию (подстраховка на случай, если RestoreTiles тайл не покрыл).
                if (_map.GetTileRef(grid, gridComp, tile).Tile.IsEmpty
                    && comp.TileSnapshot.TryGetValue(tile, out var savedTile))
                    _map.SetTile(grid, gridComp, tile, savedTile);

                var coords = _map.GridTileToLocal(grid, gridComp, tile);

                // Отодвигаем бойцов с тайла: новая сплошная конструкция иначе их зажмёт / не заякорится.
                var blocked = false;
                foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(coords, 0.45f))
                {
                    if (TryFindFreeTile(grid, gridComp, comp, tile, out var freeCoords))
                        _transform.SetCoordinates(mob.Owner, freeCoords);
                    else
                        blocked = true;
                }

                if (blocked)
                {
                    blockedCount++;
                    continue;
                }

                foreach (var exp in toSpawn)
                {
                    var ent = Spawn(exp.Proto, coords);
                    _transform.SetLocalRotation(ent, exp.Rotation);

                    // Конструкции (стены/окна/решётки/мебель) спавнятся УЖЕ заякоренными (anchored: true в
                    // прототипе). Повторный AnchorEntity добавил бы ту же сущность в ячейку снапгрида второй
                    // раз → debug-ассерт AddToSnapGridCell (в release — дубликат в сетке). Поэтому якорим
                    // только то, что заспавнилось не заякоренным.
                    if (Transform(ent).Anchored || _transform.AnchorEntity(ent))
                    {
                        RestoreBulb(ent); // no-op, если это не светильник
                        respawned++;
                    }
                    else
                    {
                        failed++;
                        Log.Warning($"[duel-arena] не удалось заякорить восстановленную конструкцию {exp.Proto} на тайле {tile}");
                    }
                }
            }
            catch (Exception e)
            {
                failed++;
                Log.Error($"[duel-arena] ошибка восстановления конструкций на тайле {tile}: {e}");
            }
        }

        // Чужие постройки, возведённые за бой на изначально ПУСТЫХ тайлах (в снимке их нет), проход по
        // тайлам снимка не достанет — сносим их отдельным проходом по всему гриду. Снимок пристайн-арены
        // полон, поэтому «нет в снимке» = построено во время боя. Инфраструктуру IsRestorableStructure
        // отсекает, так что трекер/кнопки/шлюзы не пострадают. Список собираем до удаления: Del во время
        // обхода query нельзя.
        var foreignList = new List<EntityUid>();
        var gridQuery = EntityQueryEnumerator<TransformComponent>();
        while (gridQuery.MoveNext(out var uid, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored || !IsRestorableStructure(uid))
                continue;

            var t = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
            if (!comp.StructureSnapshot.ContainsKey(t))
                foreignList.Add(uid);
        }

        foreach (var uid in foreignList)
            Del(uid);
        var foreign = foreignList.Count;

        Log.Info($"[duel-arena] восстановление конструкций ({ToPrettyString(arenaUid)}): "
            + $"вылечено {healed}, переставлено {respawned}, убрано лишних {removed}, снесено чужих {foreign}, занято мобами {blockedCount}, ошибок {failed}");
    }

    private void RestoreProps(EntityUid grid, DuelArenaComponent comp)
    {
        // Удаляем ВСЕ помеченные предметы-декор в зоне арены — включая подобранные бойцами в инвентарь
        // (их GridUid == null, поэтому грид резолвим по мировой позиции держателя) — и раскладываем набор
        // заново по снимку. Так количество и позиции точно совпадают с эталоном, а унесённый за бой
        // предмет не порождает дубль. Собираем список до удаления: Del во время обхода query нельзя.
        var toDelete = new List<EntityUid>();
        var query = EntityQueryEnumerator<ArenaMapPropComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _))
        {
            if (OnArenaGrid(uid, grid))
                toDelete.Add(uid);
        }

        foreach (var uid in toDelete)
        {
            // Внутри предмета мог спрятаться боец (коробка-невидимка и т.п.) — извлекаем перед удалением,
            // иначе каскадное удаление контейнера заберёт тело вместе с ним.
            EjectMobsBeforeDelete(uid);
            Del(uid);
        }

        foreach (var prop in comp.PropSnapshot)
        {
            var ent = Spawn(prop.Proto, new EntityCoordinates(grid, prop.Position));
            _transform.SetLocalRotation(ent, prop.Rotation);
            EnsureComp<ArenaMapPropComponent>(ent);
        }

        if (toDelete.Count > 0 || comp.PropSnapshot.Count > 0)
            Log.Info($"[duel-arena] свободные предметы: убрано {toDelete.Count}, разложено {comp.PropSnapshot.Count}");
    }

    /// <summary>Стоит ли сущность над гридом арены (по мировой позиции — работает и для вещей в инвентаре).</summary>
    private bool OnArenaGrid(EntityUid uid, EntityUid grid)
    {
        var xform = Transform(uid);
        if (xform.GridUid == grid)
            return true;

        var pos = _transform.GetMapCoordinates(uid, xform);
        return _mapManager.TryFindGridAt(pos, out var gridUnder, out _) && gridUnder == grid;
    }

    /// <summary>
    /// Извлекает всех существ из удаляемого предмета (в т.ч. из вложенных контейнеров), реперентя их на
    /// место предмета, чтобы каскадное удаление не забрало живое тело вместе с предметом.
    /// </summary>
    private void EjectMobsBeforeDelete(EntityUid uid)
    {
        var dropParent = Transform(uid).ParentUid;
        EjectMobsRecursive(uid, dropParent, Transform(uid).Coordinates);
    }

    private void EjectMobsRecursive(EntityUid uid, EntityUid dropParent, EntityCoordinates dropAt)
    {
        var children = new List<EntityUid>();
        var en = Transform(uid).ChildEnumerator;
        while (en.MoveNext(out var child))
            children.Add(child);

        foreach (var child in children)
        {
            if (HasComp<MobStateComponent>(child))
            {
                if (_container.TryGetContainingContainer((child, null), out var cont))
                    _container.Remove(child, cont, force: true, reparent: true);

                if (Transform(child).ParentUid == uid && dropParent.IsValid())
                    _transform.SetCoordinates(child, dropAt);
            }
            else
            {
                EjectMobsRecursive(child, dropParent, dropAt);
            }
        }
    }

    private void RestoreDecals(EntityUid grid, MapGridComponent gridComp, DuelArenaComponent comp)
    {
        // Стираем все текущие декали грида (боевую кровь, подпалины) и накатываем исходные заново.
        foreach (var (id, _) in _decals.GetDecalsIntersecting(grid, gridComp.LocalAABB.Enlarged(1f)))
            _decals.RemoveDecal(grid, id);

        var added = 0;
        foreach (var decal in comp.DecalSnapshot)
        {
            if (_decals.TryAddDecal(decal, new EntityCoordinates(grid, decal.Coordinates), out _))
                added++;
        }

        if (comp.DecalSnapshot.Count > 0)
            Log.Info($"[duel-arena] восстановление декалей: накатано {added} из {comp.DecalSnapshot.Count}");
    }

    // ── Вспомогательное ─────────────────────────────────────────────────────────────────────────

    /// <summary>Чинит конструкцию: обнуляет урон и (если это светильник) возвращает рабочую лампу.</summary>
    private void HealStructure(EntityUid uid)
    {
        if (TryComp<DamageableComponent>(uid, out var damage))
            _damageable.SetAllDamage((uid, damage), FixedPoint2.Zero);

        RestoreBulb(uid);
    }

    /// <summary>Ищет ближайший свободный тайл вокруг origin, куда можно отодвинуть бойца.</summary>
    private bool TryFindFreeTile(EntityUid gridUid, MapGridComponent gridComp, DuelArenaComponent comp, Vector2i origin, out EntityCoordinates coords)
    {
        coords = default;
        var check = new List<EntityUid>();

        for (var r = 1; r <= 4; r++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                for (var dy = -r; dy <= r; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                        continue;

                    var candidate = origin + new Vector2i(dx, dy);

                    // Не отправляем бойца на тайл, где сами будем восстанавливать конструкцию.
                    if (comp.StructureSnapshot.ContainsKey(candidate))
                        continue;

                    check.Clear();
                    _map.GetAnchoredEntities((gridUid, gridComp), candidate, check);
                    if (check.Any(IsRestorableStructure))
                        continue;

                    coords = _map.GridTileToLocal(gridUid, gridComp, candidate);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Возвращает лампу светильника в рабочее состояние (для не-светильников — ничего не делает).</summary>
    private void RestoreBulb(EntityUid light)
    {
        if (!TryComp<PoweredLightComponent>(light, out var comp))
            return;

        var bulb = _poweredLight.GetBulb(light, comp);

        if (bulb is { } present && TryComp<LightBulbComponent>(present, out var bulbComp) && bulbComp.State == LightBulbState.Normal)
            return;

        if (comp.HasLampOnSpawn is not { } lampProto)
            return;

        if (bulb is { } old)
            Del(old);

        var fresh = Spawn(lampProto, Transform(light).Coordinates);
        _poweredLight.InsertBulb(light, fresh, comp);
    }
}
