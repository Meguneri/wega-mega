using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Wega.Raid.Components;
using Content.Server.Database;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Store;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.Server._Wega.Raid.Systems;

/// <summary>
/// Manages persistent raid stash data for players: currency, stored items and raid statistics.
/// Each player also gets a personal hideout map with a stash box and shop terminal.
/// Loads data on connect, saves on disconnect and round restart, and cleans up hideout maps.
/// </summary>
public sealed partial class RaidStashSystem : EntitySystem
{
    [Dependency] private UserDbDataManager _userDb = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedMapSystem _map = default!;

    private ISawmill _sawmill = default!;
    private readonly Dictionary<NetUserId, RaidStashSnapshot> _stashes = new();
    private readonly Dictionary<NetUserId, EntityUid> _stashBoxes = new();
    private readonly Dictionary<NetUserId, DateTime> _lastSave = new();

    // Per-player hideout state.
    private readonly Dictionary<NetUserId, MapId> _playerHideouts = new();
    private readonly Dictionary<NetUserId, EntityUid> _playerHideoutGrids = new();
    private readonly Dictionary<NetUserId, EntityCoordinates> _playerHideoutSpawnCoords = new();

    private const int SaveCooldownSeconds = 5;
    private const string HideoutMapPath = "/Maps/_Wega/Raid/hideout.yml";

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("raid_stash");
        _userDb.AddOnLoadPlayer(OnLoadPlayer);
        _userDb.AddOnFinishLoad(OnFinishLoad);
        _userDb.AddOnPlayerDisconnect(OnPlayerDisconnect);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
    }

    #region Loading / Saving

    private async Task OnLoadPlayer(ICommonSession session, CancellationToken cancel)
    {
        RaidStashSnapshot snapshot;
        try
        {
            var record = await _db.GetRaidStashAsync(session.UserId, cancel);
            snapshot = record == null ? new RaidStashSnapshot() : DeserializeSnapshot(record);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to load raid stash for {session.UserId}: {e}");
            snapshot = new RaidStashSnapshot();
        }

        cancel.ThrowIfCancellationRequested();
        _stashes[session.UserId] = snapshot;
        _sawmill.Debug($"Loaded raid stash for {session.Name}");
    }

    private void OnFinishLoad(ICommonSession session)
    {
        // Load the personal hideout map and materialize the stash box there.
        if (!LoadHideout(session.UserId))
        {
            // Fallback: spawn stash box on the hub if the hideout could not be loaded.
            TrySpawnStashBoxForPlayer(session.UserId);
        }
    }

    private void OnPlayerDisconnect(ICommonSession session)
    {
        SaveStashBoxContents(session.UserId);
        _ = SaveStashAsync(session.UserId, force: true);
        DeleteHideout(session.UserId);
        _stashes.Remove(session.UserId);
        _stashBoxes.Remove(session.UserId);
        _lastSave.Remove(session.UserId);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // Serialize stash boxes synchronously while entities still exist, then kick off DB saves
        // and delete all personal hideout maps.
        foreach (var userId in _stashes.Keys.ToList())
        {
            SaveStashBoxContents(userId);
        }

        foreach (var userId in _stashes.Keys.ToList())
        {
            _ = SaveStashAsync(userId, force: true);
            DeleteHideout(userId);
        }

        _playerHideouts.Clear();
        _playerHideoutGrids.Clear();
        _stashBoxes.Clear();
        _lastSave.Clear();
    }

    /// <summary>
    /// Saves the current snapshot to the database, with a cooldown for non-critical saves.
    /// </summary>
    public async Task SaveStashAsync(NetUserId userId, bool force = false)
    {
        if (!_stashes.TryGetValue(userId, out var stash))
            return;

        if (!force &&
            _lastSave.TryGetValue(userId, out var last) &&
            (DateTime.UtcNow - last).TotalSeconds < SaveCooldownSeconds)
        {
            return;
        }

        var json = SerializeSnapshot(stash);
        var checksum = ComputeChecksum(json);
        var record = new RaidStashRecord(userId, json, checksum, stash.Version, DateTime.UtcNow);

        try
        {
            await _db.SaveRaidStashAsync(userId, record);
            _lastSave[userId] = DateTime.UtcNow;
            _sawmill.Debug($"Saved raid stash for {userId}");
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to save raid stash for {userId}: {e}");
        }
    }

    #endregion

    #region Public API

    public bool TryGetStash(NetUserId userId, out RaidStashSnapshot stash)
    {
        return _stashes.TryGetValue(userId, out stash!);
    }

    public RaidStashSnapshot GetOrCreateStash(NetUserId userId)
    {
        if (!_stashes.TryGetValue(userId, out var stash))
        {
            stash = new RaidStashSnapshot();
            _stashes[userId] = stash;
        }

        return stash;
    }

    public void AddCurrency(NetUserId userId, ProtoId<CurrencyPrototype> currency, FixedPoint2 amount)
    {
        if (amount <= FixedPoint2.Zero)
            return;

        var stash = GetOrCreateStash(userId);
        stash.Currency[currency] = stash.Currency.GetValueOrDefault(currency) + amount;
    }

    public bool TrySpendCurrency(NetUserId userId, ProtoId<CurrencyPrototype> currency, FixedPoint2 amount)
    {
        if (amount <= FixedPoint2.Zero)
            return true;

        if (!TryGetStash(userId, out var stash))
            return false;

        var current = stash.Currency.GetValueOrDefault(currency);
        if (current < amount)
            return false;

        stash.Currency[currency] = current - amount;
        return true;
    }

    public FixedPoint2 GetCurrency(NetUserId userId, ProtoId<CurrencyPrototype> currency)
    {
        return TryGetStash(userId, out var stash)
            ? stash.Currency.GetValueOrDefault(currency)
            : FixedPoint2.Zero;
    }

    public void UpdateStats(NetUserId userId, Action<RaidStats> update)
    {
        var stash = GetOrCreateStash(userId);
        update(stash.Stats);
    }

    #endregion

    #region Hideout

    /// <summary>
    /// Returns all grid UIDs currently used as player hideouts.
    /// </summary>
    public IEnumerable<EntityUid> GetHideoutGrids()
    {
        return _playerHideoutGrids.Values;
    }

    /// <summary>
    /// Gets the hideout map and grid for a player, if loaded.
    /// </summary>
    public bool TryGetHideout(NetUserId userId, out MapId mapId, out EntityUid gridUid)
    {
        if (_playerHideouts.TryGetValue(userId, out mapId) &&
            _playerHideoutGrids.TryGetValue(userId, out gridUid) &&
            Exists(gridUid) && !_entityManager.Deleted(gridUid))
        {
            return true;
        }

        gridUid = default;
        return false;
    }

    /// <summary>
    /// Tries to return coordinates of the player spawn marker on the hideout grid.
    /// </summary>
    public EntityCoordinates? GetHideoutSpawnCoordinates(NetUserId userId)
    {
        if (_playerHideoutSpawnCoords.TryGetValue(userId, out var cached) &&
            cached.IsValid(EntityManager) &&
            TryGetHideout(userId, out _, out var gridUid) &&
            Transform(cached.EntityId).GridUid == gridUid)
        {
            return cached;
        }

        if (!TryGetHideout(userId, out _, out gridUid))
            return null;

        var coords = FindSpawnOnGrid(gridUid, RaidHideoutSpawnType.Player);
        if (coords != null)
            _playerHideoutSpawnCoords[userId] = coords.Value;

        return coords;
    }

    /// <summary>
    /// Loads a personal hideout map for the player and spawns their stash box there.
    /// </summary>
    /// <returns>True if the hideout was loaded successfully.</returns>
    private bool LoadHideout(NetUserId userId)
    {
        if (_playerHideouts.ContainsKey(userId))
            return true;

        var mapUid = _map.CreateMap(out var mapId);
        var opts = new DeserializationOptions { InitializeMaps = true };

        try
        {
            if (!_mapLoader.TryLoadGrid(mapId, new ResPath(HideoutMapPath), out var grid, opts))
            {
                _sawmill.Error($"Failed to load hideout grid for {userId} from {HideoutMapPath}");
                _map.DeleteMap(mapId);
                return false;
            }

            var gridUid = grid.Value.Owner;
            _playerHideouts[userId] = mapId;
            _playerHideoutGrids[userId] = gridUid;
            _sawmill.Info($"Loaded hideout map {mapId} grid {gridUid} for {userId}");

            // Cache the player spawn coordinates for quick lookups during teleports.
            var playerCoords = FindSpawnOnGrid(gridUid, RaidHideoutSpawnType.Player);
            if (playerCoords != null)
                _playerHideoutSpawnCoords[userId] = playerCoords.Value;

            // Spawn the stash box at the stash marker and remove the stash marker.
            var stashCoords = FindSpawnOnGrid(gridUid, RaidHideoutSpawnType.Stash);
            if (stashCoords != null)
            {
                var box = SpawnStashBox(userId, stashCoords.Value);
                _transform.SetParent(box, gridUid);
            }
            else
            {
                _sawmill.Warning($"No stash spawn marker found on hideout for {userId}");
            }

            DeleteSpawnMarkers(gridUid, RaidHideoutSpawnType.Stash);
            return true;
        }
        catch (Exception e)
        {
            _sawmill.Error($"Exception loading hideout for {userId}: {e}");
            _map.DeleteMap(mapId);
            return false;
        }
    }

    /// <summary>
    /// Deletes the player's personal hideout map and clears related caches.
    /// </summary>
    private void DeleteHideout(NetUserId userId)
    {
        if (_playerHideouts.TryGetValue(userId, out var mapId))
        {
            try
            {
                _map.DeleteMap(mapId);
                _sawmill.Debug($"Deleted hideout map {mapId} for {userId}");
            }
            catch (Exception e)
            {
                _sawmill.Error($"Failed to delete hideout map {mapId} for {userId}: {e}");
            }
        }

        _playerHideouts.Remove(userId);
        _playerHideoutGrids.Remove(userId);
        _playerHideoutSpawnCoords.Remove(userId);
        _stashBoxes.Remove(userId);
    }

    /// <summary>
    /// Teleports a newly attached player to their personal hideout if they are not already there.
    /// Works only when a raid controller exists in the world — otherwise spawned mobs stay
    /// wherever they were placed (admin arenas, ghost roles, etc.).
    /// </summary>
    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        // PlayerAttachedEvent поднимается до того, как MindContainerComponent получит ссылку на разум,
        // поэтому используем UserId напрямую из сессии события.
        var userId = args.Player.UserId;

        // Не телепортируем призраков/наблюдателей.
        if (HasComp<GhostComponent>(args.Entity))
            return;

        // Рейд-режим активен только при наличии контроллера рейда. Без него не трогаем спавн.
        var controllerQuery = EntityQueryEnumerator<RaidControllerComponent>();
        if (!controllerQuery.MoveNext(out _, out _))
            return;

        if (!TryGetHideout(userId, out _, out var gridUid))
            return;

        var xform = Transform(args.Entity);
        if (xform.GridUid == gridUid)
            return;

        var coords = GetHideoutSpawnCoordinates(userId);
        if (coords == null)
            return;

        _transform.SetCoordinates(args.Entity, coords.Value);
        _sawmill.Debug($"Teleported {userId} to hideout");
    }

    private EntityCoordinates? FindSpawnOnGrid(EntityUid gridUid, RaidHideoutSpawnType type)
    {
        // Основной поиск через EntityQuery.
        var query = EntityQueryEnumerator<RaidHideoutSpawnComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var spawn, out var xform))
        {
            if (spawn.SpawnType != type)
                continue;

            // GridUid может быть ещё не проставлен сразу после загрузки карты — проверяем также parent.
            if (xform.GridUid != gridUid && xform.ParentUid != gridUid)
                continue;

            return xform.Coordinates;
        }

        // Fallback: если EntityQuery не видит только что загруженные маркеры, ищем среди детей грида.
        var childEnum = Transform(gridUid).ChildEnumerator;
        while (childEnum.MoveNext(out var child))
        {
            if (!TryComp<RaidHideoutSpawnComponent>(child, out var spawn))
                continue;

            if (spawn.SpawnType != type)
                continue;

            return Transform(child).Coordinates;
        }

        return null;
    }

    private void DeleteSpawnMarkers(EntityUid gridUid, RaidHideoutSpawnType type)
    {
        var toDelete = new List<EntityUid>();
        var query = EntityQueryEnumerator<RaidHideoutSpawnComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var spawn, out var xform))
        {
            if (spawn.SpawnType != type)
                continue;

            if (xform.GridUid == gridUid || xform.ParentUid == gridUid)
                toDelete.Add(uid);
        }

        foreach (var uid in toDelete)
        {
            QueueDel(uid);
        }
    }

    #endregion

    #region Stash Box

    /// <summary>
    /// Gets the currently materialized stash box for a player, if it still exists.
    /// </summary>
    public EntityUid? GetStashBox(NetUserId userId)
    {
        if (_stashBoxes.TryGetValue(userId, out var box) && Exists(box) && !_entityManager.Deleted(box))
            return box;

        return null;
    }

    /// <summary>
    /// Tries to spawn or respawn the stash box for a player near the hub return marker.
    /// </summary>
    public EntityUid? TrySpawnStashBoxForPlayer(NetUserId userId)
    {
        if (GetStashBox(userId) is { } existing)
            return existing;

        var coords = FindHubSpawnCoordinates();
        if (coords == null)
        {
            _sawmill.Warning($"No hub return coordinates found, cannot spawn stash box for {userId}");
            return null;
        }

        return SpawnStashBox(userId, coords.Value);
    }

    public EntityUid SpawnStashBox(NetUserId userId, EntityCoordinates coords)
    {
        if (GetStashBox(userId) is { } oldBox)
            QueueDel(oldBox);

        EntityUid box;
        if (TryGetStash(userId, out var stash) && stash.Items.Count > 0)
        {
            box = LoadBoxFromSnapshot(stash, coords);
        }
        else
        {
            box = Spawn("RaidStashBox", coords);
        }

        if (TryComp<RaidStashBoxComponent>(box, out var comp))
            comp.OwnerId = userId;

        _stashBoxes[userId] = box;
        return box;
    }

    /// <summary>
    /// Serializes the contents of a stash box into the player's snapshot.
    /// Call this before major state changes (e.g., player entering a raid or round restart).
    /// </summary>
    public void SaveStashBoxContents(NetUserId userId)
    {
        if (!TryGetStash(userId, out var stash))
            return;

        var box = GetStashBox(userId);
        if (box == null)
            return;

        stash.Items = SerializeBoxContents(box.Value);
    }

    private List<RaidStashItem> SerializeBoxContents(EntityUid box)
    {
        var items = new List<RaidStashItem>();
        try
        {
            var options = new SerializationOptions { ErrorOnOrphan = false };
            var (node, _) = _mapLoader.SerializeEntitiesRecursive(new HashSet<EntityUid> { box }, options);
            var document = new YamlDocument(node.ToYaml());
            var stream = new YamlStream { document };
            var writer = new StringWriter();
            stream.Save(writer, false);

            items.Add(new RaidStashItem
            {
                YamlBlob = writer.ToString(),
                ExtractedAt = DateTime.UtcNow,
            });
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to serialize stash box {box}: {e}");
        }

        return items;
    }

    private EntityUid LoadBoxFromSnapshot(RaidStashSnapshot stash, EntityCoordinates coords)
    {
        var item = stash.Items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.YamlBlob));
        if (item == null)
            return Spawn("RaidStashBox", coords);

        try
        {
            using var reader = new StringReader(item.YamlBlob);
            if (!_mapLoader.TryLoadEntity(reader, "raid_stash", out var entity))
            {
                _sawmill.Error($"Failed to load stash box from snapshot for player {stash}");
                return Spawn("RaidStashBox", coords);
            }

            _transform.SetCoordinates(entity.Value.Owner, coords);
            return entity.Value.Owner;
        }
        catch (Exception e)
        {
            _sawmill.Error($"Exception while loading stash box from snapshot: {e}");
            return Spawn("RaidStashBox", coords);
        }
    }

    private EntityCoordinates? FindHubSpawnCoordinates()
    {
        // Prefer a RaidReturnMarker if one exists on the hub.
        var returnQuery = EntityQueryEnumerator<RaidReturnComponent, TransformComponent>();
        while (returnQuery.MoveNext(out _, out _, out var xform))
        {
            return xform.Coordinates;
        }

        // Fall back to the raid controller's location.
        var ctrlQuery = EntityQueryEnumerator<RaidControllerComponent, TransformComponent>();
        while (ctrlQuery.MoveNext(out _, out _, out var xform))
        {
            return xform.Coordinates;
        }

        return null;
    }

    #endregion

    #region Serialization Helpers

    private static string SerializeSnapshot(RaidStashSnapshot stash)
    {
        return JsonSerializer.Serialize(stash, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }

    private RaidStashSnapshot DeserializeSnapshot(RaidStashRecord record)
    {
        var expected = ComputeChecksum(record.StashData);
        if (!string.Equals(expected, record.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            _sawmill.Error($"Raid stash checksum mismatch for {record.UserId}. Expected {expected}, got {record.Checksum}.");
            return new RaidStashSnapshot();
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<RaidStashSnapshot>(record.StashData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });

            return snapshot ?? new RaidStashSnapshot();
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to deserialize raid stash for {record.UserId}: {e}");
            return new RaidStashSnapshot();
        }
    }

    private static string ComputeChecksum(string data)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(bytes);
    }

    #endregion
}
