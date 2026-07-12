using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Wega.Raid.Components;
using Content.Server._Wega.Raid.Systems;
using Content.Shared._Wega.Raid.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Wega.Raid;

[TestFixture]
[TestOf(typeof(RaidControllerSystem))]
public sealed class RaidControllerTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  name: RaidTestDummy
  id: RaidTestDummy
  components:
  - type: Damageable
  - type: Injurable
    damageContainer: Biological
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: HumanoidProfile
";

    private static void ExpandGrid(SharedMapSystem mapSystem, Entity<MapGridComponent> grid, Tile tile)
    {
        for (var x = -2; x <= 4; x++)
        {
            for (var y = -2; y <= 4; y++)
            {
                mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), tile);
            }
        }
    }

    /// <summary>
    /// Кнопка входа переносит рейдера на карту рейда, а заход в точку экстракта — обратно на хаб.
    /// </summary>
    [Test]
    public async Task EntryButtonAndExtractTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var mapSystem = server.System<SharedMapSystem>();
        var transformSystem = SEntMan.System<SharedTransformSystem>();

        var hubMap = await pair.CreateTestMap();
        var raidMap = await pair.CreateTestMap();

        EntityUid controller = default;
        EntityUid entryButton = default;
        EntityUid returnMarker = default;
        EntityUid spawnMarker = default;
        EntityUid extractPoint = default;
        EntityUid raider = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, hubMap.Grid, hubMap.Tile.Tile);
            ExpandGrid(mapSystem, raidMap.Grid, raidMap.Tile.Tile);

            var hubCoords = hubMap.GridCoords;
            var raidCoords = raidMap.GridCoords;

            // Контроллер без предзагрузки файла — используем уже созданную тестовую карту.
            controller = SSpawnAtPosition(null, hubCoords);
            var ctrl = SEntMan.AddComponent<RaidControllerComponent>(controller);
            ctrl.RaidMap = new ResPath("/Maps/_Wega/Arena/arena_duel_31.yml");
            ctrl.Loaded = true;
            ctrl.LoadedMap = raidMap.MapId;
            ctrl.RaidDuration = 600f;
            ctrl.WarningTimes.Clear();

            entryButton = SSpawnAtPosition("RaidEntryButton", hubCoords);
            returnMarker = SSpawnAtPosition("RaidReturnMarker", hubCoords);
            spawnMarker = SSpawnAtPosition("RaidSpawnMarker", raidCoords);
            extractPoint = SSpawnAtPosition("RaidExtractionPoint", raidCoords.Offset(new Vector2(2, 0)));

            raider = SSpawnAtPosition("RaidTestDummy", hubCoords.Offset(new Vector2(1, 0)));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var ev = new ActivateInWorldEvent(raider, entryButton, true);
            SEntMan.EventBus.RaiseLocalEvent(entryButton, ev);

            var ctrl = SEntMan.GetComponent<RaidControllerComponent>(controller);
            Assert.That(ctrl.Raiders.Contains(raider), Is.True, "Рейдер должен попасть в список рейдеров");
            Assert.That(ctrl.Active, Is.True, "Рейд должен стать активным");
            Assert.That(SEntMan.GetComponent<TransformComponent>(raider).MapID, Is.EqualTo(raidMap.MapId), "Рейдер должен оказаться на карте рейда");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            // Телепортируем рейдера прямо в точку экстракта (мгновенный экстракт).
            var extractXform = SEntMan.GetComponent<TransformComponent>(extractPoint);
            transformSystem.SetCoordinates(raider, extractXform.Coordinates);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var ctrl = SEntMan.GetComponent<RaidControllerComponent>(controller);
            Assert.That(ctrl.Raiders.Contains(raider), Is.False, "Рейдер должен быть удалён из списка после экстракта");
            Assert.That(SEntMan.GetComponent<TransformComponent>(raider).MapID, Is.EqualTo(hubMap.MapId), "Рейдер должен вернуться на хаб");
        });
    }

    /// <summary>
    /// Если рейдер отключается во время рейда, его персонаж экстренно эвакуируется на хаб.
    /// </summary>
    [Test]
    public async Task PlayerDisconnectExtractsRaiderTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var mapSystem = server.System<SharedMapSystem>();
        var mindSystem = SEntMan.System<SharedMindSystem>();
        var playerManager = server.ResolveDependency<IPlayerManager>();
        var transformSystem = SEntMan.System<SharedTransformSystem>();

        var hubMap = await pair.CreateTestMap();
        var raidMap = await pair.CreateTestMap();

        EntityUid controller = default;
        EntityUid entryButton = default;
        EntityUid returnMarker = default;
        EntityUid spawnMarker = default;
        EntityUid raider = default;
        EntityUid? mindId = null;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, hubMap.Grid, hubMap.Tile.Tile);
            ExpandGrid(mapSystem, raidMap.Grid, raidMap.Tile.Tile);

            var hubCoords = hubMap.GridCoords;
            var raidCoords = raidMap.GridCoords;

            controller = SSpawnAtPosition(null, hubCoords);
            var ctrl = SEntMan.AddComponent<RaidControllerComponent>(controller);
            ctrl.RaidMap = new ResPath("/Maps/_Wega/Arena/arena_duel_31.yml");
            ctrl.Loaded = true;
            ctrl.LoadedMap = raidMap.MapId;
            ctrl.RaidDuration = 600f;
            ctrl.WarningTimes.Clear();

            entryButton = SSpawnAtPosition("RaidEntryButton", hubCoords);
            returnMarker = SSpawnAtPosition("RaidReturnMarker", hubCoords);
            spawnMarker = SSpawnAtPosition("RaidSpawnMarker", raidCoords);

            raider = SSpawnAtPosition("RaidTestDummy", hubCoords.Offset(new Vector2(1, 0)));

            // Присоединяем к рейдеру существующую тестовую сессию.
            var session = playerManager.Sessions.Single();
            mindId = mindSystem.CreateMind(session.UserId);
            mindSystem.TransferTo(mindId.Value, raider);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var ev = new ActivateInWorldEvent(raider, entryButton, true);
            SEntMan.EventBus.RaiseLocalEvent(entryButton, ev);

            Assert.That(SEntMan.GetComponent<TransformComponent>(raider).MapID, Is.EqualTo(raidMap.MapId), "Рейдер должен быть на карте рейда");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            // Отсоединяем игрока от тела — имитируем дисконнект.
            Assert.That(mindId, Is.Not.Null);
            mindSystem.TransferTo(mindId!.Value, null);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var ctrl = SEntMan.GetComponent<RaidControllerComponent>(controller);
            Assert.That(ctrl.Raiders.Contains(raider), Is.False, "Отключившийся рейдер должен быть удалён из списка");
            Assert.That(SEntMan.GetComponent<TransformComponent>(raider).MapID, Is.EqualTo(hubMap.MapId), "Отключившийся рейдер должен вернуться на хаб");
        });
    }

    /// <summary>
    /// По истечении таймера рейдера добивает MIA-урон.
    /// </summary>
    [Test]
    public async Task RaidTimerMiaTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var mapSystem = server.System<SharedMapSystem>();
        var mobState = SEntMan.System<MobStateSystem>();

        var hubMap = await pair.CreateTestMap();
        var raidMap = await pair.CreateTestMap();

        EntityUid controller = default;
        EntityUid entryButton = default;
        EntityUid spawnMarker = default;
        EntityUid raider = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, hubMap.Grid, hubMap.Tile.Tile);
            ExpandGrid(mapSystem, raidMap.Grid, raidMap.Tile.Tile);

            var hubCoords = hubMap.GridCoords;
            var raidCoords = raidMap.GridCoords;

            controller = SSpawnAtPosition(null, hubCoords);
            var ctrl = SEntMan.AddComponent<RaidControllerComponent>(controller);
            ctrl.RaidMap = new ResPath("/Maps/_Wega/Arena/arena_duel_31.yml");
            ctrl.Loaded = true;
            ctrl.LoadedMap = raidMap.MapId;
            ctrl.RaidDuration = 0.1f; // очень короткий таймер для теста
            ctrl.WarningTimes.Clear();
            ctrl.MiaDamage = new DamageSpecifier();
            ctrl.MiaDamage.DamageDict.Add("Asphyxiation", FixedPoint2.New(500));

            entryButton = SSpawnAtPosition("RaidEntryButton", hubCoords);
            spawnMarker = SSpawnAtPosition("RaidSpawnMarker", raidCoords);

            raider = SSpawnAtPosition("RaidTestDummy", hubCoords.Offset(new Vector2(1, 0)));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var ev = new ActivateInWorldEvent(raider, entryButton, true);
            SEntMan.EventBus.RaiseLocalEvent(entryButton, ev);
        });

        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            var ctrl = SEntMan.GetComponent<RaidControllerComponent>(controller);
            Assert.That(ctrl.Active, Is.False, "Рейд должен завершиться по таймеру");
            Assert.That(ctrl.Raiders.Contains(raider), Is.False, "Рейдер должен быть удалён из списка");
            Assert.That(mobState.IsDead(raider), Is.True, "Рейдер должен погибнуть от MIA-урона");
        });
    }

    /// <summary>
    /// Спавнер диких выдаёт разные типы (рандомизация работает).
    /// </summary>
    [Test]
    public async Task SkavSpawnerRandomizesTypesTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);
            var coords = testMap.GridCoords;

            for (var i = 0; i < 20; i++)
            {
                SSpawnAtPosition("RaidSkavSpawner", coords.Offset(new Vector2(i * 0.5f, 0)));
            }
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var ids = new List<string>();
            var query = SEntMan.EntityQueryEnumerator<MobStateComponent, MetaDataComponent>();
            while (query.MoveNext(out _, out _, out var meta))
            {
                var id = meta.EntityPrototype?.ID;
                if (id is "MobSkav" or "MobSkavRusher" or "MobSkavSniper" or "MobSkavHeavy")
                    ids.Add(id);
            }

            Assert.That(ids.Count, Is.GreaterThan(10), "Спавнеры должны заспавнить заметное число диких");
            Assert.That(ids.Distinct().Count(), Is.GreaterThan(1), "Спавнеры должны выдавать разные типы диких");
        });
    }

    #region Hideout Tests

    /// <summary>
    /// Для подключённого игрока загружается персональная карта-база (hideout) со своим гридом.
    /// </summary>
    [Test]
    public async Task HideoutLoadsForConnectedPlayerTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var stashSystem = server.System<RaidStashSystem>();
        var session = pair.Player;

        Assert.That(session, Is.Not.Null, "Тестовая сессия должна существовать");

        await server.WaitAssertion(() =>
        {
            Assert.That(stashSystem.TryGetHideout(session.UserId, out var mapId, out var gridUid), Is.True,
                "Для подключённого игрока должна загрузиться персональная база");
            Assert.That(mapId, Is.Not.EqualTo(MapId.Nullspace), "Hideout map не должен быть nullspace");
            Assert.That(gridUid, Is.Not.EqualTo(EntityUid.Invalid), "Hideout grid должен существовать");
            Assert.That(SEntMan.Deleted(gridUid), Is.False, "Hideout grid не должен быть удалён");
        });
    }

    /// <summary>
    /// При привязке игрока к телу персонаж телепортируется на свою персональную базу.
    /// </summary>
    [Test]
    public async Task PlayerTeleportsToHideoutOnAttachTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var mindSystem = SEntMan.System<SharedMindSystem>();
        var stashSystem = server.System<RaidStashSystem>();
        var playerManager = server.ResolveDependency<IPlayerManager>();
        var session = pair.Player;

        Assert.That(session, Is.Not.Null, "Тестовая сессия должна существовать");

        var testMap = await pair.CreateTestMap();
        EntityUid dummy = default;
        EntityUid? mindId = null;

        await server.WaitAssertion(() =>
        {
            var coords = testMap.GridCoords;
            dummy = SSpawnAtPosition("RaidTestDummy", coords);

            // Привязываем существующую тестовую сессию к новому телу.
            // Это имитирует спавн/возрождение игрока и должен вызвать телепорт на базу.
            mindId = mindSystem.CreateMind(session.UserId);
            mindSystem.TransferTo(mindId.Value, dummy);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(stashSystem.TryGetHideout(session.UserId, out _, out var gridUid), Is.True,
                "База игрока должна быть загружена");
            Assert.That(SEntMan.GetComponent<TransformComponent>(dummy).GridUid, Is.EqualTo(gridUid),
                "Персонаж должен оказаться на гриде своей базы");
        });
    }

    /// <summary>
    /// Кнопка входа, поставленная на персональной базе, переносит рейдера на карту рейда.
    /// </summary>
    [Test]
    public async Task HideoutEntryButtonTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var mapSystem = server.System<SharedMapSystem>();
        var stashSystem = server.System<RaidStashSystem>();
        var mindSystem = SEntMan.System<SharedMindSystem>();
        var session = pair.Player;

        Assert.That(session, Is.Not.Null, "Тестовая сессия должна существовать");

        var raidMap = await pair.CreateTestMap();

        EntityUid controller = default;
        EntityUid spawnMarker = default;
        EntityUid dummy = default;
        EntityUid? mindId = null;
        EntityUid entryButton = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, raidMap.Grid, raidMap.Tile.Tile);
            var raidCoords = raidMap.GridCoords;

            // Контроллер без предзагрузки файла — используем уже созданную тестовую карту.
            controller = SSpawnAtPosition(null, raidCoords);
            var ctrl = SEntMan.AddComponent<RaidControllerComponent>(controller);
            ctrl.RaidMap = new ResPath("/Maps/_Wega/Arena/arena_duel_31.yml");
            ctrl.Loaded = true;
            ctrl.LoadedMap = raidMap.MapId;
            ctrl.RaidDuration = 600f;
            ctrl.WarningTimes.Clear();

            spawnMarker = SSpawnAtPosition("RaidSpawnMarker", raidCoords);

            // Привязываем сессию к телу — телепорт на базу.
            dummy = SSpawnAtPosition("RaidTestDummy", raidCoords);
            mindId = mindSystem.CreateMind(session.UserId);
            mindSystem.TransferTo(mindId.Value, dummy);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(stashSystem.TryGetHideout(session.UserId, out _, out var gridUid), Is.True);
            var gridXform = SEntMan.GetComponent<TransformComponent>(gridUid);
            var buttonCoords = new EntityCoordinates(gridUid, new Vector2(7.5f, 9.5f));
            entryButton = SSpawnAtPosition("RaidEntryButton", buttonCoords);

            Assert.That(SEntMan.GetComponent<TransformComponent>(dummy).GridUid, Is.EqualTo(gridUid),
                "Персонаж должен быть на базе перед входом в рейд");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var ev = new ActivateInWorldEvent(dummy, entryButton, true);
            SEntMan.EventBus.RaiseLocalEvent(entryButton, ev);

            var ctrl = SEntMan.GetComponent<RaidControllerComponent>(controller);
            Assert.That(ctrl.Raiders.Contains(dummy), Is.True, "Рейдер должен попасть в список рейдеров");
            Assert.That(ctrl.Active, Is.True, "Рейд должен стать активным");
            Assert.That(SEntMan.GetComponent<TransformComponent>(dummy).MapID, Is.EqualTo(raidMap.MapId),
                "Рейдер должен оказаться на карте рейда");
        });
    }

    /// <summary>
    /// После успешного экстракта рейдер возвращается на свою персональную базу, а не на общий хаб.
    /// </summary>
    [Test]
    public async Task ExtractReturnsToHideoutTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var mapSystem = server.System<SharedMapSystem>();
        var transformSystem = SEntMan.System<SharedTransformSystem>();
        var stashSystem = server.System<RaidStashSystem>();
        var mindSystem = SEntMan.System<SharedMindSystem>();
        var session = pair.Player;

        Assert.That(session, Is.Not.Null, "Тестовая сессия должна существовать");

        var hubMap = await pair.CreateTestMap();
        var raidMap = await pair.CreateTestMap();

        EntityUid controller = default;
        EntityUid entryButton = default;
        EntityUid spawnMarker = default;
        EntityUid extractPoint = default;
        EntityUid dummy = default;
        EntityUid? mindId = null;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, hubMap.Grid, hubMap.Tile.Tile);
            ExpandGrid(mapSystem, raidMap.Grid, raidMap.Tile.Tile);

            var hubCoords = hubMap.GridCoords;
            var raidCoords = raidMap.GridCoords;

            controller = SSpawnAtPosition(null, hubCoords);
            var ctrl = SEntMan.AddComponent<RaidControllerComponent>(controller);
            ctrl.RaidMap = new ResPath("/Maps/_Wega/Arena/arena_duel_31.yml");
            ctrl.Loaded = true;
            ctrl.LoadedMap = raidMap.MapId;
            ctrl.RaidDuration = 600f;
            ctrl.WarningTimes.Clear();

            entryButton = SSpawnAtPosition("RaidEntryButton", hubCoords);
            spawnMarker = SSpawnAtPosition("RaidSpawnMarker", raidCoords);
            extractPoint = SSpawnAtPosition("RaidExtractionPoint", raidCoords.Offset(new Vector2(2, 0)));

            dummy = SSpawnAtPosition("RaidTestDummy", hubCoords.Offset(new Vector2(1, 0)));
            mindId = mindSystem.CreateMind(session.UserId);
            mindSystem.TransferTo(mindId.Value, dummy);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var ev = new ActivateInWorldEvent(dummy, entryButton, true);
            SEntMan.EventBus.RaiseLocalEvent(entryButton, ev);

            Assert.That(SEntMan.GetComponent<TransformComponent>(dummy).MapID, Is.EqualTo(raidMap.MapId),
                "Рейдер должен быть на карте рейда");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var extractXform = SEntMan.GetComponent<TransformComponent>(extractPoint);
            transformSystem.SetCoordinates(dummy, extractXform.Coordinates);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var ctrl = SEntMan.GetComponent<RaidControllerComponent>(controller);
            Assert.That(ctrl.Raiders.Contains(dummy), Is.False, "Рейдер должен быть удалён из списка после экстракта");
            Assert.That(stashSystem.TryGetHideout(session.UserId, out _, out var gridUid), Is.True,
                "База игрока должна существовать");
            Assert.That(SEntMan.GetComponent<TransformComponent>(dummy).GridUid, Is.EqualTo(gridUid),
                "После экстракта рейдер должен вернуться на свою базу");
        });
    }

    #endregion
}
