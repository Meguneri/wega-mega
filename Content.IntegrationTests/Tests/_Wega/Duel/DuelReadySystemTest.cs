using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Wega.Duel.Components;
using Content.Server._Wega.Duel.Systems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Wega.Duel;

[TestFixture]
[TestOf(typeof(DuelReadySystem))]
public sealed class DuelReadySystemTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  name: DuelReadyTestDummy
  id: DuelReadyTestDummy
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
";

    private static void ExpandGrid(SharedMapSystem mapSystem, Entity<MapGridComponent> grid, Tile tile)
    {
        for (var x = -2; x <= 2; x++)
        {
            for (var y = -2; y <= 2; y++)
            {
                mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), tile);
            }
        }
    }

    [Test]
    public async Task ReadyButtonsStartDuelTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid tracker = default;
        EntityUid button1 = default;
        EntityUid button2 = default;
        EntityUid user1 = default;
        EntityUid user2 = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var gridUid = tile.GridUid;
            var coordinates = new EntityCoordinates(gridUid, tile.GridIndices.X, tile.GridIndices.Y);

            tracker = entManager.SpawnEntity("DuelArenaTracker", coordinates);
            button1 = entManager.SpawnEntity("DuelStartButton", coordinates.Offset(new Vector2(1, 0)));
            button2 = entManager.SpawnEntity("DuelStartButton", coordinates.Offset(new Vector2(2, 0)));
            user1 = entManager.SpawnEntity("DuelReadyTestDummy", coordinates.Offset(new Vector2(0, 1)));
            user2 = entManager.SpawnEntity("DuelReadyTestDummy", coordinates.Offset(new Vector2(0, 2)));

            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.ReadyButtons.Count, Is.EqualTo(0), "Изначально готовности нет");
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            // Первый боец нажимает свою кнопку.
            var ev = new ActivateInWorldEvent(user1, button1, true);
            entManager.EventBus.RaiseLocalEvent(button1, ev);

            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.ReadyButtons.Contains(button1), Is.True, "Первая кнопка должна стать готовой");
            Assert.That(arena.ReadyHolograms.ContainsKey(button2), Is.True, "Над второй кнопкой должна появиться голограмма готовности");
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            // Второй боец нажимает свою кнопку — все готовы, система вызывает старт.
            var ev = new ActivateInWorldEvent(user2, button2, true);
            entManager.EventBus.RaiseLocalEvent(button2, ev);

            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.ReadyButtons.Count, Is.EqualTo(0), "При старте готовность должна сброситься");
            Assert.That(arena.ReadyHolograms.Count, Is.EqualTo(0), "Голограммы должны исчезнуть");
        });
    }
}
