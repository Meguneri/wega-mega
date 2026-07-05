using System.Linq;
using Content.Server._Wega.Duel.Components;
using Content.Server.Explosion.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Случайные авиаудары на дуэльной арене. Ждёт фронт старта боя (IsActive), затем периодически
/// бьёт по случайным тайлам в <see cref="ArenaAirstrikeComponent.StrikeRadius"/> тайлах от
/// живых дуэлянтов: сначала появляется маркер-прицел, спустя <see cref="ArenaAirstrikeComponent.WarningDuration"/>
/// секунд — взрыв с эффектами China Lake / PMC.
/// </summary>
public sealed class ArenaAirstrikeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    // Те же визуальные эффекты, что у снаряда China Lake.
    private static readonly string[] StrikeEffects =
    [
        "CMExplosionEffectGrenade",
        "RMCExplosionEffectGrenadeShockWave",
        "ExplosionEffectSmoke",
    ];

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ArenaAirstrikeComponent, DuelArenaComponent>();
        while (query.MoveNext(out var uid, out var airstrike, out var arena))
        {
            if (!airstrike.Enabled)
                continue;

            if (arena.IsActive && !airstrike.WasDuelActive)
                OnDuelStarted(airstrike, now);
            else if (!arena.IsActive && airstrike.WasDuelActive)
                OnDuelEnded(airstrike);

            airstrike.WasDuelActive = arena.IsActive;

            if (!arena.IsActive)
                continue;

            // Активируем запланированные удары.
            for (var i = airstrike.PendingStrikes.Count - 1; i >= 0; i--)
            {
                var (marker, fireAt) = airstrike.PendingStrikes[i];
                if (now < fireAt)
                    continue;

                airstrike.PendingStrikes.RemoveAt(i);

                if (!Exists(marker))
                    continue;

                var coords = _transform.GetMapCoordinates(marker);
                QueueDel(marker);

                foreach (var effect in StrikeEffects)
                    Spawn(effect, coords);

                _explosion.QueueExplosion(
                    coords,
                    airstrike.ExplosionType,
                    airstrike.TotalIntensity,
                    airstrike.Slope,
                    airstrike.MaxTileIntensity,
                    cause: uid,
                    canCreateVacuum: false);
            }

            // Запускаем следующую волну.
            if (airstrike.NextStrikeAt is { } nextAt && now >= nextAt)
            {
                airstrike.NextStrikeAt = now + TimeSpan.FromSeconds(airstrike.StrikeInterval);
                ScheduleWave(uid, airstrike, arena, now);
            }
        }
    }

    private void OnDuelStarted(ArenaAirstrikeComponent airstrike, TimeSpan now)
    {
        airstrike.NextStrikeAt = now + TimeSpan.FromSeconds(airstrike.FirstStrikeDelay);
        airstrike.PendingStrikes.Clear();
    }

    private void OnDuelEnded(ArenaAirstrikeComponent airstrike)
    {
        airstrike.NextStrikeAt = null;

        foreach (var (marker, _) in airstrike.PendingStrikes)
            if (Exists(marker))
                QueueDel(marker);

        airstrike.PendingStrikes.Clear();
    }

    /// <summary>
    /// Включает или выключает авиаудары на конкретной арене. При выключении отменяет запланированные удары.
    /// </summary>
    public void ToggleAirstrike(EntityUid uid, bool enabled)
    {
        if (!TryComp<ArenaAirstrikeComponent>(uid, out var airstrike))
            return;

        airstrike.Enabled = enabled;

        if (!enabled)
        {
            airstrike.NextStrikeAt = null;
            foreach (var (marker, _) in airstrike.PendingStrikes)
                if (Exists(marker))
                    QueueDel(marker);
            airstrike.PendingStrikes.Clear();
        }
        else if (TryComp<DuelArenaComponent>(uid, out var arena) && arena.IsActive)
        {
            airstrike.NextStrikeAt = _timing.CurTime + TimeSpan.FromSeconds(airstrike.FirstStrikeDelay);
            airstrike.PendingStrikes.Clear();
        }
    }

    private void ScheduleWave(EntityUid uid, ArenaAirstrikeComponent airstrike, DuelArenaComponent arena, TimeSpan now)
    {
        var gridUid = Transform(uid).GridUid;
        if (gridUid == null || !TryComp<MapGridComponent>(gridUid, out var gridComp))
            return;

        var allTiles = _map.GetAllTiles(gridUid.Value, gridComp).Where(t => !t.Tile.IsEmpty).ToList();
        if (allTiles.Count == 0)
            return;

        // Живые дуэлянты на гриде арены — цели для прицеливания.
        var targets = arena.Duelists
            .Where(d => Exists(d) && Transform(d).GridUid == gridUid)
            .ToList();

        var fireAt = now + TimeSpan.FromSeconds(airstrike.WarningDuration);
        var radiusSq = airstrike.StrikeRadius * airstrike.StrikeRadius;

        for (var i = 0; i < airstrike.StrikeCount; i++)
        {
            // Строим пул тайлов в радиусе от случайного бойца; если никого нет — весь грид.
            List<TileRef> pool;
            if (targets.Count > 0)
            {
                var target = _random.Pick(targets);
                var playerTile = _map.TileIndicesFor(gridUid.Value, gridComp, Transform(target).Coordinates);
                pool = allTiles
                    .Where(t =>
                    {
                        var dx = t.GridIndices.X - playerTile.X;
                        var dy = t.GridIndices.Y - playerTile.Y;
                        return dx * dx + dy * dy <= radiusSq;
                    })
                    .ToList();

                if (pool.Count == 0)
                    pool = allTiles;
            }
            else
            {
                pool = allTiles;
            }

            var tile = _random.Pick(pool);
            var coords = _map.ToCenterCoordinates(tile, gridComp);

            var marker = Spawn(airstrike.WarningProto, coords);
            airstrike.PendingStrikes.Add((marker, fireAt));
        }
    }
}
