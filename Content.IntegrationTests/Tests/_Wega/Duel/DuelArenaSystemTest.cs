using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Wega.Duel.Components;
using Content.Server._Wega.Duel.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared._Wega.Duel;
using Content.Shared._Wega.Duel.Components;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Wega.Duel;

[TestFixture]
[TestOf(typeof(DuelArenaSystem))]
public sealed class DuelArenaSystemTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  name: DuelTestDummy
  id: DuelTestDummy
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
  name: DuelTestWall
  id: DuelTestWall
  components:
  - type: Sprite
    sprite: Structures/Walls/solid.rsi
  - type: Tag
    tags:
    - Wall
  - type: Damageable
    damageModifierSet: StructuralMetallic
  - type: Injurable
    damageContainer: StructuralInorganic
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""

- type: entity
  name: DuelTestIssuedItem
  id: DuelTestIssuedItem
  components:
  - type: ArenaIssuedItem
  - type: Sprite
    sprite: Objects/Misc/guardian_info.rsi

- type: entity
  id: DuelTestStormTracker
  parent: DuelArenaTracker
  components:
  - type: ArenaStorm
    initialRadius: 5
    minRadius: 1
    shrinkStep: 1
    shrinkInterval: 1
    startDelay: 0
    damageInterval: 0.01
    damage:
      types:
        Heat: 5
";

    private static readonly ProtoId<DamageTypePrototype> TestDamageType = "Blunt";

    /// <summary>
    /// CreateTestMap создаёт грид из одного тайла. Для арены нужно пространство для трекера и бойцов,
    /// поэтому расширяем грид до 5x5 вокруг начала координат.
    /// </summary>
    private static void ExpandGrid(SharedMapSystem mapSystem, Entity<MapGridComponent> grid, Tile tile)
    {
        for (var x = -1; x <= 3; x++)
        {
            for (var y = -1; y <= 3; y++)
            {
                mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), tile);
            }
        }
    }

    [Test]
    public async Task DuelStartAndConcludeTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var mobStateSystem = server.System<MobStateSystem>();
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

            tracker = entManager.SpawnEntity("DuelArenaTracker", coordinates);
            fighter1 = entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(1, 0)));
            fighter2 = entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(2, 0)));

            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.False);
            Assert.That(arena.Phase, Is.EqualTo(DuelArenaPhase.Idle));

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.True, "Дуэль должна быть активной после сигнала старта");
            Assert.That(arena.Phase, Is.EqualTo(DuelArenaPhase.Fighting));
            Assert.That(arena.Duelists.Count, Is.EqualTo(2), "Должно быть зарегистрировано 2 дуэлянта");
            Assert.That(arena.Duelists, Does.Contain(fighter1));
            Assert.That(arena.Duelists, Does.Contain(fighter2));

            DamageSpecifier damage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(100000));
            damageableSystem.TryChangeDamage(fighter2, damage, true);

            // ConcludeDuel вызывается синхронно по событию смерти и тут же воскрешает бойцов через
            // Rejuvenate, поэтому сразу после урона fighter2 уже жив. Проверяем сам факт завершения.
            arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.False, "Дуэль должна завершиться после смерти одного из бойцов");
            Assert.That(arena.Phase, Is.EqualTo(DuelArenaPhase.Restoring));
            Assert.That(arena.Duelists.Count, Is.EqualTo(0), "Список дуэлянтов должен очиститься");
            Assert.That(arena.GateCloseAt, Is.Not.Null, "Должен быть запланирован сигнал закрытия шлюзов");
            Assert.That(mobStateSystem.IsAlive(fighter2), Is.True, "Проигравший должен быть воскрешён");
            Assert.That(mobStateSystem.IsAlive(fighter1), Is.True, "Победитель должен быть воскрешён");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.False, "Дуэль должна оставаться завершённой");
            Assert.That(arena.GateCloseAt, Is.Not.Null, "Grace-период закрытия шлюзов должен сохраняться");
        });
    }

    [Test]
    public async Task DuelHasNoAutomaticTimeoutTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mobStateSystem = server.System<MobStateSystem>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid tracker = default;
        EntityUid fighter1 = default;
        EntityUid fighter2 = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var coordinates = new EntityCoordinates(tile.GridUid, tile.GridIndices.X, tile.GridIndices.Y);

            tracker = entManager.SpawnEntity("DuelArenaTracker", coordinates);
            fighter1 = entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(1, 0)));
            fighter2 = entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(2, 0)));

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.Phase, Is.EqualTo(DuelArenaPhase.Fighting));
            Assert.That(arena.Duelists, Is.EquivalentTo(new[] { fighter1, fighter2 }));
        });

        // Четыре игровых минуты превышают прежние 3 минуты боя и 30 секунд внезапной смерти.
        await pair.RunSeconds(240f);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.True, "Обычная дуэль не должна завершаться по времени");
            Assert.That(arena.Phase, Is.EqualTo(DuelArenaPhase.Fighting));
            Assert.That(arena.Duelists, Is.EquivalentTo(new[] { fighter1, fighter2 }));
            Assert.That(arena.GateCloseAt, Is.Null, "Закрытие шлюзов не должно планироваться без результата боя");
            Assert.That(arena.Scores, Is.Empty, "Без победителя счёт не должен изменяться");
            Assert.That(mobStateSystem.IsAlive(fighter1), Is.True);
            Assert.That(mobStateSystem.IsAlive(fighter2), Is.True);
        });
    }

    [Test]
    public async Task DuelArenaCanStartAgainAfterRestoreTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var mindSystem = entManager.System<SharedMindSystem>();
        var playerManager = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
        var duelArenaSystem = server.System<DuelArenaSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid tracker = default;
        EntityUid fighter1 = default;
        EntityUid fighter2 = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var coordinates = new EntityCoordinates(tile.GridUid, tile.GridIndices.X, tile.GridIndices.Y);

            tracker = entManager.SpawnEntity("DuelArenaTracker", coordinates);
            fighter1 = entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(1, 0)));
            fighter2 = entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(2, 0)));

            var player = playerManager.Sessions.Single();
            var mind = mindSystem.CreateMind(player.UserId);
            mindSystem.TransferTo(mind, fighter1);

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.Phase, Is.EqualTo(DuelArenaPhase.Fighting));

            DamageSpecifier damage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(100000));
            damageableSystem.TryChangeDamage(fighter2, damage, true);

            arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.Phase, Is.EqualTo(DuelArenaPhase.Restoring));
            Assert.That(arena.IsActive, Is.False);
            Assert.That(arena.Scores.Count, Is.EqualTo(1), "Победа первого раунда должна попасть в счёт арены");
            Assert.That(arena.Scores.Values.Single(), Is.EqualTo(1));
            Assert.That(arena.Streak, Is.EqualTo(1));
        });

        // RestoreDelay по умолчанию равен 2.5 секунды.
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.Phase, Is.EqualTo(DuelArenaPhase.Idle), "После восстановления арена должна стать Idle");
            Assert.That(arena.PendingRestore, Is.False);
            Assert.That(arena.Duelists, Is.Empty);
        });

        await server.WaitAssertion(() =>
        {
            var secondStart = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref secondStart);
        });
        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.Phase, Is.EqualTo(DuelArenaPhase.Fighting), "После восстановления должен запускаться новый раунд");
            Assert.That(arena.Duelists, Is.EquivalentTo(new[] { fighter1, fighter2 }));

            DamageSpecifier damage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(100000));
            damageableSystem.TryChangeDamage(fighter2, damage, true);

            arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.Scores.Values.Single(), Is.EqualTo(2), "Победы двух раундов должны суммироваться");
            Assert.That(arena.Streak, Is.EqualTo(2), "Серия одного победителя должна продолжиться во втором раунде");

            Assert.That(duelArenaSystem.ResetAllScores(), Is.EqualTo(1), "Должна быть очищена одна арена");
            Assert.That(arena.Scores, Is.Empty);
            Assert.That(arena.ScoreNames, Is.Empty);
            Assert.That(arena.LosingStreaks, Is.Empty);
            Assert.That(arena.StreakUser, Is.Null);
            Assert.That(arena.Streak, Is.Zero);
            Assert.That(arena.Phase, Is.EqualTo(DuelArenaPhase.Restoring), "Обнуление счёта не должно менять фазу арены");
        });
    }

    [Test]
    public async Task DuelResetBySignalTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid tracker = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var gridUid = tile.GridUid;
            var coordinates = new EntityCoordinates(gridUid, tile.GridIndices.X, tile.GridIndices.Y);

            tracker = entManager.SpawnEntity("DuelArenaTracker", coordinates);
            var fighter1 = entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(1, 0)));
            var fighter2 = entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(2, 0)));

            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.True);
            Assert.That(arena.Duelists.Count, Is.EqualTo(2));

            var resetEv = new SignalReceivedEvent("Toggle");
            entManager.EventBus.RaiseLocalEvent(tracker, ref resetEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.False, "Дуэль должна сброситься по сигналу Toggle");
            Assert.That(arena.Phase, Is.EqualTo(DuelArenaPhase.Restoring));
            Assert.That(arena.Duelists.Count, Is.EqualTo(0), "Список дуэлянтов должен очиститься");
            Assert.That(arena.GateCloseAt, Is.Not.Null, "Должен быть запланирован сигнал закрытия шлюзов");
        });
    }

    [Test]
    public async Task DuelScoreResetClearsAllStoresTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var duelArenaSystem = server.System<DuelArenaSystem>();

        var testMap = await pair.CreateTestMap();
        EntityUid duelArena = default;
        EntityUid bossArena = default;
        EntityUid rotationController = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);
            var tile = testMap.Tile;
            var coordinates = new EntityCoordinates(tile.GridUid, tile.GridIndices.X, tile.GridIndices.Y);
            var user = new NetUserId(new Guid("640bd619-fc8d-4fe2-bf3c-4a5fb17d6ddd"));

            duelArena = entManager.SpawnEntity(null, coordinates);
            var duelScore = entManager.AddComponent<DuelArenaComponent>(duelArena);
            duelScore.Scores[user] = 2;
            duelScore.ScoreNames[user] = "Duelist";
            duelScore.LosingStreaks[user] = 1;
            duelScore.StreakUser = user;
            duelScore.Streak = 2;

            bossArena = entManager.SpawnEntity(null, coordinates.Offset(new Vector2(1, 0)));
            var bossScore = entManager.AddComponent<BossArenaComponent>(bossArena);
            bossScore.Scores[user] = 3;
            bossScore.ScoreNames[user] = "Duelist";
            bossScore.LosingStreaks[user] = 2;
            bossScore.StreakUser = user;
            bossScore.Streak = 3;

            rotationController = entManager.SpawnEntity(null, coordinates.Offset(new Vector2(2, 0)));
            var rotationScore = entManager.AddComponent<DuelRotationComponent>(rotationController);
            rotationScore.Scores[user] = 4;
            rotationScore.ScoreNames[user] = "Duelist";
            rotationScore.LosingStreaks[user] = 3;
            rotationScore.StreakUser = user;
            rotationScore.Streak = 4;
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(duelArenaSystem.ResetAllScores(), Is.EqualTo(3));

            var duelScore = entManager.GetComponent<DuelArenaComponent>(duelArena);
            Assert.That(duelScore.Scores, Is.Empty);
            Assert.That(duelScore.ScoreNames, Is.Empty);
            Assert.That(duelScore.LosingStreaks, Is.Empty);
            Assert.That(duelScore.StreakUser, Is.Null);
            Assert.That(duelScore.Streak, Is.Zero);

            var bossScore = entManager.GetComponent<BossArenaComponent>(bossArena);
            Assert.That(bossScore.Scores, Is.Empty);
            Assert.That(bossScore.ScoreNames, Is.Empty);
            Assert.That(bossScore.LosingStreaks, Is.Empty);
            Assert.That(bossScore.StreakUser, Is.Null);
            Assert.That(bossScore.Streak, Is.Zero);

            var rotationScore = entManager.GetComponent<DuelRotationComponent>(rotationController);
            Assert.That(rotationScore.Scores, Is.Empty);
            Assert.That(rotationScore.ScoreNames, Is.Empty);
            Assert.That(rotationScore.LosingStreaks, Is.Empty);
            Assert.That(rotationScore.StreakUser, Is.Null);
            Assert.That(rotationScore.Streak, Is.Zero);
        });
    }

    [Test]
    public async Task DuelWallRestoreTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var restoreSystem = server.System<DuelArenaRestoreSystem>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid tracker = default;
        EntityUid wall = default;
        Vector2i wallTile = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var grid = (testMap.Grid.Owner, testMap.Grid.Comp);
            var gridUid = tile.GridUid;
            var coordinates = new EntityCoordinates(gridUid, tile.GridIndices.X, tile.GridIndices.Y);

            // Ставим трекер в центр, стену на соседнем тайле.
            tracker = entManager.SpawnEntity("DuelArenaTracker", coordinates);
            var wallCoords = coordinates.Offset(new Vector2(1, 0));
            wall = entManager.SpawnEntity("DuelTestWall", wallCoords);
            wallTile = mapSystem.TileIndicesFor(gridUid, grid.Comp, wallCoords);

            // Заякориваем стену вручную — тестовый прототип не делает этого при спавне.
            entManager.System<SharedTransformSystem>().AnchorEntity(wall);
            Assert.That(entManager.GetComponent<TransformComponent>(wall).Anchored, Is.True, "Тестовая стена должна быть заякорена");

            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            restoreSystem.SnapshotArena(tracker, arena);
            Assert.That(arena.StructureSnapshot.ContainsKey(wallTile), Is.True, "Стена должна попасть в снимок");

            // Убираем стену, имитируя разрушение.
            entManager.DeleteEntity(wall);
        });

        // Даём движку физически удалить сущность и очистить snap-grid ячейку.
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            arena.PendingRestore = true;
            restoreSystem.RestoreArena(tracker, arena);

            var grid = (testMap.Grid.Owner, testMap.Grid.Comp);
            var anchored = new List<EntityUid>();
            mapSystem.GetAnchoredEntities(grid, wallTile, anchored);

            Assert.That(anchored.Count, Is.GreaterThan(0), "После восстановления на тайле должна быть стена");

            var restored = anchored.FirstOrDefault(e => entManager.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "DuelTestWall");
            Assert.That(restored, Is.Not.EqualTo(default(EntityUid)), "На тайле должна появиться DuelTestWall");
        });
    }

    [Test]
    public async Task DuelArenaCleanupSystemTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid controller = default;
        EntityUid item = default;
        EntityUid mob = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var gridUid = tile.GridUid;
            var coordinates = new EntityCoordinates(gridUid, tile.GridIndices.X, tile.GridIndices.Y);

            controller = entManager.SpawnEntity("DuelCleanupController", coordinates);
            item = entManager.SpawnEntity("DuelTestIssuedItem", coordinates.Offset(new Vector2(1, 0)));
            mob = entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(2, 0)));

            // Помечаем моба как выданное снаряжение — CleanupArea должна защитить живых существ.
            entManager.EnsureComponent<ArenaIssuedItemComponent>(mob);
            Assert.That(entManager.HasComponent<ArenaIssuedItemComponent>(item), Is.True);
            Assert.That(entManager.HasComponent<ArenaIssuedItemComponent>(mob), Is.True);

            var cleanEv = new SignalReceivedEvent("Trigger");
            entManager.EventBus.RaiseLocalEvent(controller, ref cleanEv);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.Deleted(item), Is.True, "Выданный предмет должен быть удалён очисткой");
            Assert.That(entManager.Deleted(mob), Is.False, "Живой моб не должен быть удалён очисткой");
            Assert.That(entManager.HasComponent<ArenaIssuedItemComponent>(mob), Is.False, "Метка арены должна быть снята с моба");
        });
    }

    [Test]
    public async Task ArenaStormSystemTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var stormSystem = entManager.System<ArenaStormSystem>();
        var damageableSystem = server.System<DamageableSystem>();

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

            tracker = entManager.SpawnEntity("DuelTestStormTracker", coordinates);
            fighter1 = entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(1, 0)));
            fighter2 = entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(2, 0)));

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.True, "Дуэль должна быть активной");
            Assert.That(arena.Duelists.Count, Is.EqualTo(2), "Должно быть 2 дуэлянта");

            // Принудительно запускаем шторм без задержки.
            stormSystem.StartAllStorms();
        });

        // StartAllStorms лишь планирует старт; активность выставляется на следующем тике Update.
        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var storm = entManager.GetComponent<ArenaStormComponent>(tracker);
            Assert.That(storm.Active, Is.True, "Шторм должен стать активным");

            // Переносим второго бойца далеко за пределы безопасной зоны.
            transformSystem.SetCoordinates(fighter2, new EntityCoordinates(testMap.Grid.Owner, new Vector2(50, 0)));
            Assert.That(transformSystem.GetMapCoordinates(fighter2).MapId, Is.EqualTo(transformSystem.GetMapCoordinates(tracker).MapId));
        });

        // Даём шторму нанести хотя бы один тик урона.
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(damageableSystem.GetTotalDamage(fighter1), Is.EqualTo(FixedPoint2.Zero), "Боец в центре не должен получать урон");
            Assert.That(damageableSystem.GetTotalDamage(fighter2), Is.GreaterThan(FixedPoint2.Zero), "Боец за пределами зоны должен получить урон");
        });
    }

    /// <summary>
    /// Проигравший 3 дуэли подряд должен получить миньона-помощника на старте следующей дуэли.
    /// Бойцам прикрепляются разумы с NetUserId — как у реальных игроков (счёт ведётся по игроку).
    /// </summary>
    [Test]
    public async Task LoserMinionSpawnsAfterThreeLossesTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var mindSystem = server.System<SharedMindSystem>();
        var mapSystem = server.System<SharedMapSystem>();

        var winnerSession = await server.AddDummySession("MinionWinner");
        var loserSession = await server.AddDummySession("MinionLoser");

        var testMap = await pair.CreateTestMap();

        EntityUid tracker = default;
        EntityUid fighter1 = default;
        EntityUid fighter2 = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var coordinates = new EntityCoordinates(tile.GridUid, tile.GridIndices.X, tile.GridIndices.Y);

            tracker = entManager.SpawnEntity("DuelArenaTracker", coordinates);
            fighter1 = entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(1, 0)));
            fighter2 = entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(2, 0)));

            var mind1 = mindSystem.CreateMind(winnerSession.UserId, "MinionWinner");
            mindSystem.TransferTo(mind1, fighter1);
            var mind2 = mindSystem.CreateMind(loserSession.UserId, "MinionLoser");
            mindSystem.TransferTo(mind2, fighter2);
        });

        // Три поражения fighter2 подряд.
        for (var round = 1; round <= 3; round++)
        {
            var r = round;
            await server.WaitAssertion(() =>
            {
                var startEv = new SignalReceivedEvent("Open");
                entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);

                var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
                Assert.That(arena.IsActive, Is.True, $"Дуэль {r} должна начаться");

                DamageSpecifier damage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(100000));
                damageableSystem.TryChangeDamage(fighter2, damage, true);

                Assert.That(arena.IsActive, Is.False, $"Дуэль {r} должна завершиться");
                Assert.That(arena.LosingStreaks.GetValueOrDefault(loserSession.UserId), Is.EqualTo(r),
                    $"После дуэли {r} серия поражений должна быть {r}");
            });

            // Пережидаем дебаунс сигнала старта (0.5 c) перед следующим раундом.
            await pair.RunTicksSync(20);
        }

        // Четвёртая дуэль: у проигравшего серия 3 — на старте должен появиться миньон.
        await server.WaitAssertion(() =>
        {
            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);

            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.True, "Четвёртая дуэль должна начаться");

            EntityUid? minionOwner = null;
            var query = entManager.EntityQueryEnumerator<ArenaLoserMinionComponent>();
            while (query.MoveNext(out _, out var minion))
                minionOwner = minion.MinionOwner;

            Assert.That(minionOwner, Is.Not.Null, "Миньон должен заспавниться у проигравшего 3 раза подряд");
            Assert.That(minionOwner, Is.EqualTo(fighter2), "Миньон должен принадлежать проигравшему");
        });
    }

    [Test]
    public async Task ArmDuelResetsPendingRestoreTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid tracker = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var gridUid = tile.GridUid;
            var coordinates = new EntityCoordinates(gridUid, tile.GridIndices.X, tile.GridIndices.Y);

            tracker = entManager.SpawnEntity("DuelArenaTracker", coordinates);
            entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(1, 0)));
            entManager.SpawnEntity("DuelTestDummy", coordinates.Offset(new Vector2(2, 0)));
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            arena.PendingRestore = true;

            // ArmDuel должен сбросить флаг отложенного восстановления сразу при старте.
            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.True, "Дуэль должна быть активной");
            Assert.That(arena.PendingRestore, Is.False, "ArmDuel должен сбросить PendingRestore");
        });
    }
}
