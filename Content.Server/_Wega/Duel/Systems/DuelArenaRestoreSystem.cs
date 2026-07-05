using System.Linq;
using System.Numerics;
using Content.Server._Wega.Duel.Components;
using Content.Server.Light.EntitySystems;
using Content.Shared.Construction;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Light.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Восстановление стен, решёток и светильников дуэльной арены. При каждом старте дуэли (конструкции целы)
/// планировка мержится в снимок (тайл → прототип + тайл пола под ним). После каждого раунда
/// конструкции приводятся к снимку: целая правильная стена/окно чинится; отсутствующая, разрушенная
/// до балки или чужая конструкция убирается и заново ставится свежей; уничтоженный пол под ней
/// восстанавливается. Восстановление выполняется отложенно — на тике после завершения боя, вне стека
/// события смерти.
/// </summary>
public sealed class DuelArenaRestoreSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private PoweredLightSystem _poweredLight = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";

    /// <summary>
    /// Восстанавливаемая конструкция арены — стена или окно.
    /// </summary>
    private bool IsRestorableStructure(EntityUid uid)
        => _tag.HasTag(uid, WallTag) || _tag.HasTag(uid, WindowTag);

    private bool IsRestorableStructure(TagComponent tagComp)
        => _tag.HasTag(tagComp, WallTag) || _tag.HasTag(tagComp, WindowTag);

    /// <summary>
    /// Мержит текущую планировку стен арены в снимок. Вызывается при КАЖДОМ старте дуэли.
    /// </summary>
    public void SnapshotWalls(EntityUid arenaUid, DuelArenaComponent comp)
    {
        var grid = Transform(arenaUid).GridUid;
        if (grid == null || !TryComp<MapGridComponent>(grid, out var gridComp))
        {
            Log.Warning($"[duel-arena] снимок стен невозможен: трекер {ToPrettyString(arenaUid)} не на гриде");
            return;
        }

        var added = 0;

        var query = EntityQueryEnumerator<TagComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var tagComp, out var xform))
        {
            if (xform.GridUid != grid || !IsRestorableStructure(tagComp))
                continue;

            if (MetaData(uid).EntityPrototype?.ID is not { } proto)
                continue;

            var tile = _map.TileIndicesFor(grid.Value, gridComp, xform.Coordinates);
            if (comp.WallSnapshot.TryAdd(tile, proto))
                added++;

            comp.WallTileSnapshot.TryAdd(tile, _map.GetTileRef(grid.Value, gridComp, tile).Tile);
        }

        if (added > 0)
            Log.Info($"[duel-arena] снимок стен пополнен: +{added}, всего {comp.WallSnapshot.Count} тайлов");
        else if (comp.WallSnapshot.Count == 0)
            Log.Warning($"[duel-arena] снимок стен ПУСТ: на гриде {grid} не найдено ни одной стены с тегом Wall");
    }

    /// <summary>
    /// Приводит стены арены к снимку. Вызывается из DuelArenaSystem.Update на тике после завершения боя.
    /// </summary>
    public void RestoreWalls(EntityUid arenaUid, DuelArenaComponent comp)
    {
        if (comp.WallSnapshot.Count == 0)
        {
            Log.Warning($"[duel-arena] восстановление стен пропущено: снимок пуст ({ToPrettyString(arenaUid)})");
            return;
        }

        var grid = Transform(arenaUid).GridUid;
        if (grid == null || !TryComp<MapGridComponent>(grid, out var gridComp))
        {
            Log.Warning($"[duel-arena] восстановление стен невозможно: трекер {ToPrettyString(arenaUid)} не на гриде");
            return;
        }

        var healed = 0;
        var respawned = 0;
        var blockedCount = 0;
        var failed = 0;

        var anchored = new List<EntityUid>();
        foreach (var (tile, proto) in comp.WallSnapshot)
        {
            try
            {
                anchored.Clear();
                _map.GetAnchoredEntities((grid.Value, gridComp), tile, anchored);

                EntityUid? wall = null;
                foreach (var e in anchored)
                {
                    if (Exists(e) && IsRestorableStructure(e))
                    {
                        wall = e;
                        break;
                    }
                }

                if (wall is { } existing && MetaData(existing).EntityPrototype?.ID == proto.Id)
                {
                    if (TryComp<DamageableComponent>(existing, out var damage))
                    {
                        _damageable.SetAllDamage((existing, damage), FixedPoint2.Zero);
                        healed++;
                    }
                    continue;
                }

                foreach (var debris in anchored)
                {
                    if (Exists(debris) && (IsWallDebris(debris) || IsRestorableStructure(debris)))
                        Del(debris);
                }

                if (_map.GetTileRef(grid.Value, gridComp, tile).Tile.IsEmpty
                    && comp.WallTileSnapshot.TryGetValue(tile, out var savedTile))
                {
                    _map.SetTile(grid.Value, gridComp, tile, savedTile);
                }

                var coords = _map.GridTileToLocal(grid.Value, gridComp, tile);

                var blocked = false;
                foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(coords, 0.45f))
                {
                    if (TryFindFreeTile(grid.Value, gridComp, comp, tile, out var freeCoords))
                        _transform.SetCoordinates(mob.Owner, freeCoords);
                    else
                        blocked = true;
                }

                if (blocked)
                {
                    blockedCount++;
                    continue;
                }

                var newWall = Spawn(proto, coords);
                if (_transform.AnchorEntity(newWall))
                {
                    respawned++;
                }
                else
                {
                    failed++;
                    Log.Warning($"[duel-arena] не удалось заякорить восстановленную стену {proto} на тайле {tile}");
                }
            }
            catch (Exception e)
            {
                failed++;
                Log.Error($"[duel-arena] ошибка восстановления стены на тайле {tile}: {e}");
            }
        }

        Log.Info($"[duel-arena] восстановление стен ({ToPrettyString(arenaUid)}): снимок {comp.WallSnapshot.Count}, "
            + $"вылечено {healed}, переставлено {respawned}, занято мобами {blockedCount}, ошибок {failed}");
    }

    /// <summary>Решётка любого вида — опознаётся по прототипу, т.к. общего тега нет.</summary>
    private bool IsGrille(EntityUid uid)
        => Exists(uid) && MetaData(uid).EntityPrototype?.ID is { } proto && proto.Contains("Grille");

    /// <summary>Мержит текущую расстановку решёток арены в снимок.</summary>
    public void SnapshotGrilles(EntityUid arenaUid, DuelArenaComponent comp)
    {
        var grid = Transform(arenaUid).GridUid;
        if (grid == null || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var added = 0;

        var query = EntityQueryEnumerator<SharedCanBuildWindowOnTopComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            if (MetaData(uid).EntityPrototype?.ID is not { } proto)
                continue;

            var tile = _map.TileIndicesFor(grid.Value, gridComp, xform.Coordinates);
            if (comp.GrilleSnapshot.TryAdd(tile, proto))
                added++;

            comp.GrilleTileSnapshot.TryAdd(tile, _map.GetTileRef(grid.Value, gridComp, tile).Tile);
        }

        if (added > 0)
            Log.Info($"[duel-arena] снимок решёток пополнен: +{added}, всего {comp.GrilleSnapshot.Count} тайлов");
    }

    /// <summary>Приводит решётки арены к снимку.</summary>
    public void RestoreGrilles(EntityUid arenaUid, DuelArenaComponent comp)
    {
        if (comp.GrilleSnapshot.Count == 0)
            return;

        var grid = Transform(arenaUid).GridUid;
        if (grid == null || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var healed = 0;
        var respawned = 0;
        var failed = 0;

        var anchored = new List<EntityUid>();
        foreach (var (tile, proto) in comp.GrilleSnapshot)
        {
            try
            {
                anchored.Clear();
                _map.GetAnchoredEntities((grid.Value, gridComp), tile, anchored);

                EntityUid? intact = null;
                foreach (var e in anchored)
                {
                    if (Exists(e) && HasComp<SharedCanBuildWindowOnTopComponent>(e)
                        && MetaData(e).EntityPrototype?.ID == proto.Id)
                    {
                        intact = e;
                        break;
                    }
                }

                if (intact is { } grille)
                {
                    if (TryComp<DamageableComponent>(grille, out var dmg))
                        _damageable.SetAllDamage((grille, dmg), FixedPoint2.Zero);
                    healed++;
                    continue;
                }

                foreach (var debris in anchored)
                {
                    if (Exists(debris) && IsGrille(debris))
                        Del(debris);
                }

                if (_map.GetTileRef(grid.Value, gridComp, tile).Tile.IsEmpty
                    && comp.GrilleTileSnapshot.TryGetValue(tile, out var savedTile))
                {
                    _map.SetTile(grid.Value, gridComp, tile, savedTile);
                }

                var coords = _map.GridTileToLocal(grid.Value, gridComp, tile);
                var newGrille = Spawn(proto, coords);
                if (_transform.AnchorEntity(newGrille))
                {
                    respawned++;
                }
                else
                {
                    failed++;
                    Log.Warning($"[duel-arena] не удалось заякорить восстановленную решётку {proto} на тайле {tile}");
                }
            }
            catch (Exception e)
            {
                failed++;
                Log.Error($"[duel-arena] ошибка восстановления решётки на тайле {tile}: {e}");
            }
        }

        Log.Info($"[duel-arena] восстановление решёток ({ToPrettyString(arenaUid)}): снимок {comp.GrilleSnapshot.Count}, "
            + $"вылечено {healed}, переставлено {respawned}, ошибок {failed}");
    }

    /// <summary>Остаток снесённой стены — балка (Girder/*).</summary>
    private bool IsWallDebris(EntityUid uid)
    {
        return Exists(uid) && MetaData(uid).EntityPrototype?.ID is { } proto && proto.Contains("Girder");
    }

    /// <summary>Ищет ближайший свободный тайл вокруг origin.</summary>
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

                    if (comp.WallSnapshot.ContainsKey(candidate))
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

    /// <summary>Мержит текущую расстановку светильников арены в снимок.</summary>
    public void SnapshotLights(EntityUid arenaUid, DuelArenaComponent comp)
    {
        var grid = Transform(arenaUid).GridUid;
        if (grid == null || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var added = 0;

        var query = EntityQueryEnumerator<PointLightComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored)
                continue;

            if (MetaData(uid).EntityPrototype?.ID is not { } proto)
                continue;

            var tile = _map.TileIndicesFor(grid.Value, gridComp, xform.Coordinates);
            if (comp.LightSnapshot.TryAdd(tile, proto))
            {
                comp.LightRotationSnapshot[tile] = xform.LocalRotation;
                added++;
            }
        }

        if (added > 0)
            Log.Info($"[duel-arena] снимок светильников пополнен: +{added}, всего {comp.LightSnapshot.Count} тайлов");
    }

    /// <summary>Приводит светильники арены к снимку.</summary>
    public void RestoreLights(EntityUid arenaUid, DuelArenaComponent comp)
    {
        if (comp.LightSnapshot.Count == 0)
            return;

        var grid = Transform(arenaUid).GridUid;
        if (grid == null || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var healed = 0;
        var respawned = 0;
        var failed = 0;

        var anchored = new List<EntityUid>();
        foreach (var (tile, proto) in comp.LightSnapshot)
        {
            try
            {
                anchored.Clear();
                _map.GetAnchoredEntities((grid.Value, gridComp), tile, anchored);

                EntityUid? fixture = null;
                foreach (var e in anchored)
                {
                    if (Exists(e) && HasComp<PointLightComponent>(e) && MetaData(e).EntityPrototype?.ID == proto.Id)
                    {
                        fixture = e;
                        break;
                    }
                }

                if (fixture is { } light)
                {
                    if (TryComp<DamageableComponent>(light, out var dmg))
                        _damageable.SetAllDamage((light, dmg), FixedPoint2.Zero);

                    RestoreBulb(light);
                    healed++;
                    continue;
                }

                foreach (var debris in anchored)
                {
                    if (Exists(debris) && HasComp<PointLightComponent>(debris))
                        Del(debris);
                }

                var coords = _map.GridTileToLocal(grid.Value, gridComp, tile);
                var newLight = Spawn(proto, coords);

                if (comp.LightRotationSnapshot.TryGetValue(tile, out var rot))
                    _transform.SetLocalRotation(newLight, rot);

                if (_transform.AnchorEntity(newLight))
                {
                    respawned++;
                }
                else
                {
                    failed++;
                    Log.Warning($"[duel-arena] не удалось заякорить восстановленный светильник {proto} на тайле {tile}");
                }
            }
            catch (Exception e)
            {
                failed++;
                Log.Error($"[duel-arena] ошибка восстановления светильника на тайле {tile}: {e}");
            }
        }

        Log.Info($"[duel-arena] восстановление светильников ({ToPrettyString(arenaUid)}): снимок {comp.LightSnapshot.Count}, "
            + $"вылечено {healed}, переставлено {respawned}, ошибок {failed}");
    }

    /// <summary>Возвращает лампу светильника в рабочее состояние.</summary>
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
