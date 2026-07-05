using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Wega.Duel.Components;
using Content.Server._Wega.Duel.Systems;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Wega.Duel;

[TestFixture]
[TestOf(typeof(ArenaAirstrikeSystem))]
public sealed class ArenaAirstrikeSystemTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  name: DuelAirstrikeTestDummy
  id: DuelAirstrikeTestDummy
  components:
  - type: Damageable
  - type: Injurable
    damageContainer: Biological
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      200: Dead
  - type: HumanoidProfile

- type: entity
  id: DuelTestAirstrikeTracker
  parent: DuelArenaTracker
  components:
  - type: ArenaAirstrike
    firstStrikeDelay: 0
    strikeInterval: 999
    warningDuration: 0.5
    strikeCount: 1
    strikeRadius: 3
";

    private static void ExpandGrid(SharedMapSystem mapSystem, Entity<MapGridComponent> grid, Tile tile)
    {
        for (var x = -3; x <= 3; x++)
        {
            for (var y = -3; y <= 3; y++)
            {
                mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), tile);
            }
        }
    }

    [Test]
    public async Task AirstrikeSpawnsAndFiresMarkerTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid tracker = default;
        EntityUid fighter1 = default;
        EntityUid fighter2 = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var gridUid = tile.GridUid;
            var coordinates = new EntityCoordinates(gridUid, tile.GridIndices.X, tile.GridIndices.Y);

            tracker = entManager.SpawnEntity("DuelTestAirstrikeTracker", coordinates);
            fighter1 = entManager.SpawnEntity("DuelAirstrikeTestDummy", coordinates.Offset(new Vector2(1, 0)));
            fighter2 = entManager.SpawnEntity("DuelAirstrikeTestDummy", coordinates.Offset(new Vector2(2, 0)));

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);
        });

        await pair.RunTicksSync(1);

        EntityUid marker = default;
        await server.WaitAssertion(() =>
        {
            var airstrike = entManager.GetComponent<ArenaAirstrikeComponent>(tracker);
            Assert.That(airstrike.NextStrikeAt, Is.Not.Null, "Следующая волна должна быть запланирована");
            Assert.That(airstrike.PendingStrikes.Count, Is.EqualTo(1), "Должен появиться один маркер-прицел");

            marker = airstrike.PendingStrikes[0].Marker;
            Assert.That(entManager.Deleted(marker), Is.False, "Маркер должен существовать");
        });

        // Ждём warningDuration + запас, чтобы взрыв отработал и маркер удалился.
        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var airstrike = entManager.GetComponent<ArenaAirstrikeComponent>(tracker);
            Assert.That(airstrike.PendingStrikes.Count, Is.EqualTo(0), "После взрыва список ожидающих ударов должен очиститься");
            Assert.That(entManager.Deleted(marker), Is.True, "Маркер должен быть удалён перед взрывом");
        });
    }

    [Test]
    public async Task AirstrikeDoesNotTargetEmptyTilesTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid tracker = default;
        EntityUid fighter1 = default;
        EntityUid fighter2 = default;
        Vector2i emptyTile = default;

        await server.WaitAssertion(() =>
        {
            var grid = (testMap.Grid.Owner, testMap.Grid.Comp);
            var gridUid = testMap.Tile.GridUid;
            var tile = testMap.Tile.Tile;
            var coordinates = new EntityCoordinates(gridUid, testMap.Tile.GridIndices.X, testMap.Tile.GridIndices.Y);

            // Грид 5x5, но центральный тайл (0,0) оставляем пустым — туда не должен падать прицел.
            emptyTile = new Vector2i(0, 0);
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                {
                    var pos = new Vector2i(x, y);
                    if (pos != emptyTile)
                        mapSystem.SetTile(grid.Owner, grid.Comp, pos, tile);
                }
            }

            tracker = entManager.SpawnEntity("DuelTestAirstrikeTracker", coordinates);
            fighter1 = entManager.SpawnEntity("DuelAirstrikeTestDummy", coordinates.Offset(new Vector2(1, 0)));
            fighter2 = entManager.SpawnEntity("DuelAirstrikeTestDummy", coordinates.Offset(new Vector2(2, 0)));

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var airstrike = entManager.GetComponent<ArenaAirstrikeComponent>(tracker);
            Assert.That(airstrike.PendingStrikes.Count, Is.GreaterThan(0), "Должен появиться маркер-прицел");

            var marker = airstrike.PendingStrikes[0].Marker;
            var markerCoords = transformSystem.GetMapCoordinates(marker);
            var trackerXform = entManager.GetComponent<TransformComponent>(tracker);
            var grid = trackerXform.GridUid;
            Assert.That(grid, Is.Not.Null, "Трекер должен быть на гриде");
            var markerTile = mapSystem.TileIndicesFor(grid!.Value, testMap.Grid.Comp, new EntityCoordinates(grid.Value, markerCoords.Position));

            Assert.That(markerTile, Is.Not.EqualTo(emptyTile), "Прицел авиаудара не должен появляться на пустом тайле");
        });
    }
}
