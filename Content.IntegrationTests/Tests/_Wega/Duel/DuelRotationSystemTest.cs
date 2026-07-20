using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Wega.Duel.Components;
using Content.Server._Wega.Duel.Systems;
using Content.Shared._Wega.Duel;
using Content.Shared._Wega.Duel.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Players;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Wega.Duel;

[TestFixture]
[TestOf(typeof(DuelRotationSystem))]
public sealed class DuelRotationSystemTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  name: DuelRotationTestDummy
  id: DuelRotationTestDummy
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

    private static readonly ProtoId<DamageTypePrototype> TestDamageType = "Blunt";

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
    /// После завершения раунда на арене, привязанной к контроллеру ротации, бойцы должны
    /// автоматически переноситься на другую загруженную арену (без повтора подряд).
    /// </summary>
    [Test]
    public async Task RotationMovesFightersToNextArenaTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        var testMap1 = await pair.CreateTestMap();
        var testMap2 = await pair.CreateTestMap();

        EntityUid controller = default;
        EntityUid tracker1 = default;
        EntityUid tracker2 = default;
        EntityUid spawn0Map2 = default;
        EntityUid spawn1Map2 = default;
        EntityUid fighter1 = default;
        EntityUid fighter2 = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap1.Grid, testMap1.Tile.Tile);
            ExpandGrid(mapSystem, testMap2.Grid, testMap2.Tile.Tile);

            var tile1 = testMap1.Tile;
            var coords1 = new EntityCoordinates(tile1.GridUid, tile1.GridIndices.X, tile1.GridIndices.Y);

            var tile2 = testMap2.Tile;
            var coords2 = new EntityCoordinates(tile2.GridUid, tile2.GridIndices.X, tile2.GridIndices.Y);

            // Контроллер ротации на первой карте (хаб/арена 0).
            controller = entManager.SpawnEntity(null, coords1);
            var rotation = entManager.AddComponent<DuelRotationComponent>(controller);
            rotation.Loaded = true;
            rotation.LoadedArenas[0] = testMap1.MapId;
            rotation.LoadedArenas[1] = testMap2.MapId;

            // Трекеры на обеих аренах, привязанные к контроллеру.
            tracker1 = entManager.SpawnEntity("DuelArenaTracker", coords1);
            entManager.GetComponent<DuelArenaComponent>(tracker1).RotationController = controller;

            tracker2 = entManager.SpawnEntity("DuelArenaTracker", coords2);
            entManager.GetComponent<DuelArenaComponent>(tracker2).RotationController = controller;

            // Спавн-маркеры на обеих аренах.
            entManager.SpawnEntity("DuelArenaSpawnMarker", coords1.Offset(new Vector2(1, 0)));
            entManager.SpawnEntity("DuelArenaSpawnMarker1", coords1.Offset(new Vector2(1, 1)));

            spawn0Map2 = entManager.SpawnEntity("DuelArenaSpawnMarker", coords2.Offset(new Vector2(2, 0)));
            spawn1Map2 = entManager.SpawnEntity("DuelArenaSpawnMarker1", coords2.Offset(new Vector2(2, 2)));

            // Бойцы на арене 0.
            fighter1 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 0)));
            fighter2 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 1)));

            // Старт дуэли на арене 0.
            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker1, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena1 = entManager.GetComponent<DuelArenaComponent>(tracker1);
            Assert.That(arena1.IsActive, Is.True, "Дуэль на первой арене должна быть активной");
            Assert.That(arena1.Duelists.Count, Is.EqualTo(2), "Должно быть зарегистрировано 2 дуэлянта");

            // Убиваем одного из бойцов — это вызывает ConcludeDuel и затем AdvanceToNextArena.
            DamageSpecifier damage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(100000));
            damageableSystem.TryChangeDamage(fighter2, damage, true);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var rotation = entManager.GetComponent<DuelRotationComponent>(controller);
            Assert.That(rotation.CurrentArena, Is.EqualTo(1), "Текущая арена должна смениться на вторую");

            // Оба бойца должны оказаться на второй карте.
            Assert.That(transformSystem.GetMapCoordinates(fighter1).MapId, Is.EqualTo(testMap2.MapId), "Первый боец должен оказаться на второй арене");
            Assert.That(transformSystem.GetMapCoordinates(fighter2).MapId, Is.EqualTo(testMap2.MapId), "Второй боец должен оказаться на второй арене");

            // Оба бойца должны стоять на спавн-маркерах второй арены.
            var spawn0Pos = transformSystem.GetWorldPosition(spawn0Map2);
            var spawn1Pos = transformSystem.GetWorldPosition(spawn1Map2);
            var fighter1Pos = transformSystem.GetWorldPosition(fighter1);
            var fighter2Pos = transformSystem.GetWorldPosition(fighter2);

            static bool Near(Vector2 pos, Vector2 target) => (pos - target).LengthSquared() <= 0.01f;

            Assert.That(Near(fighter1Pos, spawn0Pos) || Near(fighter1Pos, spawn1Pos), Is.True, "Первый боец должен быть на одном из спавнов второй арены");
            Assert.That(Near(fighter2Pos, spawn0Pos) || Near(fighter2Pos, spawn1Pos), Is.True, "Второй боец должен быть на одном из спавнов второй арены");
            Assert.That(fighter1Pos, Is.Not.EqualTo(fighter2Pos), "Бойцы должны занять разные спавны");
        });
    }

    /// <summary>
    /// В режиме ротации победный счёт должен записываться на контроллер ротации,
    /// а не на отдельный трекер арены.
    /// </summary>
    [Test]
    public async Task RotationScoreStoredOnControllerTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var mindSystem = entManager.System<SharedMindSystem>();
        var playerManager = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();

        var testMap1 = await pair.CreateTestMap();
        var testMap2 = await pair.CreateTestMap();

        EntityUid controller = default;
        EntityUid tracker1 = default;
        EntityUid fighter1 = default;
        EntityUid fighter2 = default;
        NetUserId user1 = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap1.Grid, testMap1.Tile.Tile);
            ExpandGrid(mapSystem, testMap2.Grid, testMap2.Tile.Tile);

            var tile1 = testMap1.Tile;
            var coords1 = new EntityCoordinates(tile1.GridUid, tile1.GridIndices.X, tile1.GridIndices.Y);

            var tile2 = testMap2.Tile;
            var coords2 = new EntityCoordinates(tile2.GridUid, tile2.GridIndices.X, tile2.GridIndices.Y);

            controller = entManager.SpawnEntity(null, coords1);
            var rotation = entManager.AddComponent<DuelRotationComponent>(controller);
            rotation.Loaded = true;
            rotation.LoadedArenas[0] = testMap1.MapId;
            rotation.LoadedArenas[1] = testMap2.MapId;

            tracker1 = entManager.SpawnEntity("DuelArenaTracker", coords1);
            entManager.GetComponent<DuelArenaComponent>(tracker1).RotationController = controller;

            // Спавн-маркеры на обеих аренах, чтобы ротационный переход не ругался в лог.
            entManager.SpawnEntity("DuelArenaSpawnMarker", coords1.Offset(new Vector2(1, 0)));
            entManager.SpawnEntity("DuelArenaSpawnMarker1", coords1.Offset(new Vector2(1, 1)));
            entManager.SpawnEntity("DuelArenaSpawnMarker", coords2.Offset(new Vector2(2, 0)));
            entManager.SpawnEntity("DuelArenaSpawnMarker1", coords2.Offset(new Vector2(2, 2)));

            fighter1 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 0)));
            fighter2 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 1)));

            // Переносим разум подключённого игрока на fighter1 — так у него будет валидный NetUserId.
            var player = playerManager.Sessions.Single();
            user1 = player.UserId;
            var mindId = mindSystem.CreateMind(user1);
            mindSystem.TransferTo(mindId, fighter1);

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker1, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena1 = entManager.GetComponent<DuelArenaComponent>(tracker1);
            Assert.That(arena1.IsActive, Is.True, "Дуэль должна быть активной");

            // fighter1 побеждает.
            DamageSpecifier damage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(100000));
            damageableSystem.TryChangeDamage(fighter2, damage, true);

            var rotation = entManager.GetComponent<DuelRotationComponent>(controller);

            Assert.That(rotation.Scores.Count, Is.EqualTo(1), "Счёт должен быть записан на контроллер ротации");
            Assert.That(rotation.Scores.ContainsKey(user1), Is.True, "Победитель должен получить очко на контроллере");
            Assert.That(rotation.Scores[user1], Is.EqualTo(1), "У победителя должно быть 1 очко");
            Assert.That(rotation.Streak, Is.EqualTo(1), "Серия побед должна начаться с 1");
            Assert.That(rotation.StreakUser, Is.EqualTo(user1), "Серия должна принадлежать победителю");

            Assert.That(arena1.Scores.Count, Is.EqualTo(0), "Счёт не должен дублироваться на трекере арены");
        });

        // Если перед победой нового чемпиона в хранилище осталась чужая длинная серия,
        // она должна начаться заново с 1, а не перейти к новому победителю.
        var scoreSystem = entManager.System<DuelArenaScoreSystem>();
        var rotationAfterRound = entManager.GetComponent<DuelRotationComponent>(controller);
        var previousChampion = new NetUserId(new Guid("640bd619-fc8d-4fe2-bf3c-4a5fb17d6ddd"));
        rotationAfterRound.Streak = 5;
        rotationAfterRound.StreakUser = previousChampion;
        scoreSystem.RecordMatchResult(rotationAfterRound, new[] { fighter1, fighter2 }, fighter1);

        Assert.That(rotationAfterRound.Scores[user1], Is.EqualTo(2), "Новый чемпион должен получить ещё одну победу");
        Assert.That(rotationAfterRound.Streak, Is.EqualTo(1), "При смене чемпиона серия должна сброситься до 1");
        Assert.That(rotationAfterRound.StreakUser, Is.EqualTo(user1), "Серия должна принадлежать новому чемпиону");
    }

    /// <summary>
    /// Хаб-сценарий: в ротации боец, проигравший 3 раунда подряд (с автоматическим переносом между
    /// аренами), должен получить миньона-помощника на старте следующего раунда. Серия поражений
    /// живёт на контроллере ротации и не должна теряться при смене арен.
    /// </summary>
    [Test]
    public async Task RotationLoserMinionSpawnsAfterThreeLossesTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var mindSystem = server.System<SharedMindSystem>();
        var mapSystem = server.System<SharedMapSystem>();

        var winnerSession = await server.AddDummySession("RotMinionWinner");
        var loserSession = await server.AddDummySession("RotMinionLoser");

        var testMap1 = await pair.CreateTestMap();
        var testMap2 = await pair.CreateTestMap();

        EntityUid controller = default;
        EntityUid tracker1 = default;
        EntityUid tracker2 = default;
        EntityUid fighter1 = default;
        EntityUid fighter2 = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap1.Grid, testMap1.Tile.Tile);
            ExpandGrid(mapSystem, testMap2.Grid, testMap2.Tile.Tile);

            var tile1 = testMap1.Tile;
            var coords1 = new EntityCoordinates(tile1.GridUid, tile1.GridIndices.X, tile1.GridIndices.Y);
            var tile2 = testMap2.Tile;
            var coords2 = new EntityCoordinates(tile2.GridUid, tile2.GridIndices.X, tile2.GridIndices.Y);

            controller = entManager.SpawnEntity(null, coords1);
            var rotation = entManager.AddComponent<DuelRotationComponent>(controller);
            rotation.Loaded = true;
            rotation.LoadedArenas[0] = testMap1.MapId;
            rotation.LoadedArenas[1] = testMap2.MapId;

            tracker1 = entManager.SpawnEntity("DuelArenaTracker", coords1);
            entManager.GetComponent<DuelArenaComponent>(tracker1).RotationController = controller;
            tracker2 = entManager.SpawnEntity("DuelArenaTracker", coords2);
            entManager.GetComponent<DuelArenaComponent>(tracker2).RotationController = controller;

            entManager.SpawnEntity("DuelArenaSpawnMarker", coords1.Offset(new Vector2(1, 0)));
            entManager.SpawnEntity("DuelArenaSpawnMarker1", coords1.Offset(new Vector2(1, 1)));
            entManager.SpawnEntity("DuelArenaSpawnMarker", coords2.Offset(new Vector2(2, 0)));
            entManager.SpawnEntity("DuelArenaSpawnMarker1", coords2.Offset(new Vector2(2, 2)));

            fighter1 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 0)));
            fighter2 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 1)));

            var mind1 = mindSystem.CreateMind(winnerSession.UserId, "RotMinionWinner");
            mindSystem.TransferTo(mind1, fighter1);
            var mind2 = mindSystem.CreateMind(loserSession.UserId, "RotMinionLoser");
            mindSystem.TransferTo(mind2, fighter2);
        });

        // Три раунда подряд: как в игре — кнопка старта на арене, где стоят бойцы; fighter2
        // проигрывает; ротация переносит бойцов на другую арену (без автозапуска раунда).
        for (var round = 1; round <= 3; round++)
        {
            var r = round;
            await server.WaitAssertion(() =>
            {
                var tracker = TrackerOnFightersMap(entManager, fighter2, tracker1, tracker2);
                var startEv = new SignalReceivedEvent("Open");
                entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);

                var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
                Assert.That(arena.IsActive, Is.True, $"Раунд {r} должен начаться по кнопке");

                DamageSpecifier damage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(100000));
                damageableSystem.TryChangeDamage(fighter2, damage, true);

                Assert.That(arena.IsActive, Is.False, $"Раунд {r} должен завершиться");

                var rotation = entManager.GetComponent<DuelRotationComponent>(controller);
                Assert.That(rotation.LosingStreaks.GetValueOrDefault(loserSession.UserId), Is.EqualTo(r),
                    $"После раунда {r} серия поражений на контроллере должна быть {r}");
            });

            // Пережидаем дебаунс кнопки старта (0.5 c) и даём переносу осесть.
            await pair.RunTicksSync(20);
        }

        // Четвёртый раунд (снова по кнопке): у проигравшего серия 3 — должен появиться миньон.
        await server.WaitAssertion(() =>
        {
            var tracker = TrackerOnFightersMap(entManager, fighter2, tracker1, tracker2);
            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);

            var arena = entManager.GetComponent<DuelArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.True, "Четвёртый раунд должен начаться по кнопке");

            // «Право на реванш» — компонент на самом бойце, отдельная сущность не спавнится.
            EntityUid? holder = null;
            var query = entManager.EntityQueryEnumerator<ArenaLoserMinionComponent>();
            while (query.MoveNext(out var uid, out _))
                holder = uid;

            Assert.That(holder, Is.Not.Null,
                "Право на реванш должно появиться у проигравшего 3 раунда подряд в ротации");
            Assert.That(holder, Is.EqualTo(fighter2), "Право на реванш должно висеть на проигравшем");
        });
    }

    /// <summary>
    /// Закреплённый спавн (PreferredSpawns): боец с предпочтением возвращается на свой маркер
    /// при переходе на следующую арену, а второй боец занимает ближайший свободный.
    /// </summary>
    [Test]
    public async Task RotationKeepsPreferredSpawnTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var mindSystem = entManager.System<SharedMindSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var playerManager = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();

        var testMap1 = await pair.CreateTestMap();
        var testMap2 = await pair.CreateTestMap();

        EntityUid controller = default;
        EntityUid tracker1 = default;
        EntityUid spawn0Map2 = default;
        EntityUid spawn1Map2 = default;
        EntityUid fighter1 = default;
        EntityUid fighter2 = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap1.Grid, testMap1.Tile.Tile);
            ExpandGrid(mapSystem, testMap2.Grid, testMap2.Tile.Tile);

            var tile1 = testMap1.Tile;
            var coords1 = new EntityCoordinates(tile1.GridUid, tile1.GridIndices.X, tile1.GridIndices.Y);
            var tile2 = testMap2.Tile;
            var coords2 = new EntityCoordinates(tile2.GridUid, tile2.GridIndices.X, tile2.GridIndices.Y);

            controller = entManager.SpawnEntity(null, coords1);
            var rotation = entManager.AddComponent<DuelRotationComponent>(controller);
            rotation.Loaded = true;
            rotation.LoadedArenas[0] = testMap1.MapId;
            rotation.LoadedArenas[1] = testMap2.MapId;

            tracker1 = entManager.SpawnEntity("DuelArenaTracker", coords1);
            entManager.GetComponent<DuelArenaComponent>(tracker1).RotationController = controller;

            spawn0Map2 = entManager.SpawnEntity("DuelArenaSpawnMarker", coords2.Offset(new Vector2(2, 0)));
            spawn1Map2 = entManager.SpawnEntity("DuelArenaSpawnMarker1", coords2.Offset(new Vector2(2, 2)));

            fighter1 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 0)));
            fighter2 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 1)));

            // Боец 1 — реальный игрок с закреплённым спавном №1 (как после входа персональной кнопкой).
            var player = playerManager.Sessions.Single();
            var mindId = mindSystem.CreateMind(player.UserId);
            mindSystem.TransferTo(mindId, fighter1);
            rotation.PreferredSpawns[player.UserId] = 1;

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker1, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena1 = entManager.GetComponent<DuelArenaComponent>(tracker1);
            Assert.That(arena1.IsActive, Is.True, "Дуэль на первой арене должна быть активной");

            DamageSpecifier damage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(100000));
            damageableSystem.TryChangeDamage(fighter2, damage, true);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            static bool Near(Vector2 pos, Vector2 target) => (pos - target).LengthSquared() <= 0.01f;

            Assert.That(Near(transformSystem.GetWorldPosition(fighter1), transformSystem.GetWorldPosition(spawn1Map2)), Is.True,
                "Боец с закреплённым спавном должен вернуться на маркер №1");
            Assert.That(Near(transformSystem.GetWorldPosition(fighter2), transformSystem.GetWorldPosition(spawn0Map2)), Is.True,
                "Второй боец должен занять свободный маркер №0");
        });
    }

    /// <summary>
    /// Бойцов больше, чем спавн-маркеров: лишний встаёт на занятый маркер со сдвигом (1,0),
    /// чтобы двое не оказались на одном тайле.
    /// </summary>
    [Test]
    public async Task RotationOffsetsOverflowFightersTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        var testMap1 = await pair.CreateTestMap();
        var testMap2 = await pair.CreateTestMap();

        EntityUid controller = default;
        EntityUid tracker1 = default;
        EntityUid spawn0Map2 = default;
        EntityUid spawn1Map2 = default;
        EntityUid fighter1 = default;
        EntityUid fighter2 = default;
        EntityUid fighter3 = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap1.Grid, testMap1.Tile.Tile);
            ExpandGrid(mapSystem, testMap2.Grid, testMap2.Tile.Tile);

            var tile1 = testMap1.Tile;
            var coords1 = new EntityCoordinates(tile1.GridUid, tile1.GridIndices.X, tile1.GridIndices.Y);
            var tile2 = testMap2.Tile;
            var coords2 = new EntityCoordinates(tile2.GridUid, tile2.GridIndices.X, tile2.GridIndices.Y);

            controller = entManager.SpawnEntity(null, coords1);
            var rotation = entManager.AddComponent<DuelRotationComponent>(controller);
            rotation.Loaded = true;
            rotation.LoadedArenas[0] = testMap1.MapId;
            rotation.LoadedArenas[1] = testMap2.MapId;

            tracker1 = entManager.SpawnEntity("DuelArenaTracker", coords1);
            entManager.GetComponent<DuelArenaComponent>(tracker1).RotationController = controller;

            // На второй арене только два маркера — третьему бойцу места не хватит.
            spawn0Map2 = entManager.SpawnEntity("DuelArenaSpawnMarker", coords2.Offset(new Vector2(2, 0)));
            spawn1Map2 = entManager.SpawnEntity("DuelArenaSpawnMarker1", coords2.Offset(new Vector2(2, 2)));

            fighter1 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 0)));
            fighter2 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 1)));
            fighter3 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 2)));

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker1, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena1 = entManager.GetComponent<DuelArenaComponent>(tracker1);
            Assert.That(arena1.IsActive, Is.True, "Дуэль должна быть активной");
            Assert.That(arena1.Duelists.Count, Is.EqualTo(3), "Должно быть зарегистрировано 3 дуэлянта");

            // Бой идёт, пока живы двое — убиваем двоих, чтобы раунд завершился.
            DamageSpecifier damage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(100000));
            damageableSystem.TryChangeDamage(fighter2, damage, true);
            Assert.That(arena1.IsActive, Is.True, "С двумя живыми бойцами раунд должен продолжаться");
            damageableSystem.TryChangeDamage(fighter3, damage, true);
            Assert.That(arena1.IsActive, Is.False, "Раунд должен завершиться");
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            static bool Near(Vector2 pos, Vector2 target) => (pos - target).LengthSquared() <= 0.01f;

            var spawn0Pos = transformSystem.GetWorldPosition(spawn0Map2);
            var spawn1Pos = transformSystem.GetWorldPosition(spawn1Map2);
            var shiftedPos = spawn0Pos + new Vector2(1f, 0f);

            var fighters = new[] { fighter1, fighter2, fighter3 };
            foreach (var f in fighters)
            {
                Assert.That(transformSystem.GetMapCoordinates(f).MapId, Is.EqualTo(testMap2.MapId),
                    "Все бойцы должны оказаться на второй арене");
            }

            var positions = fighters.Select(f => transformSystem.GetWorldPosition(f)).ToList();
            Assert.That(positions.Count(p => Near(p, spawn0Pos)), Is.EqualTo(1), "Один боец — на маркере №0");
            Assert.That(positions.Count(p => Near(p, spawn1Pos)), Is.EqualTo(1), "Один боец — на маркере №1");
            Assert.That(positions.Count(p => Near(p, shiftedPos)), Is.EqualTo(1),
                "Лишний боец — на маркере №0 со сдвигом (1,0)");
        });
    }

    /// <summary>
    /// Переход на следующую арену НЕ вооружает раунд автоматически: арена ждёт кнопки старта
    /// в фазе Preparing, иначе «дуэль началась» печаталось бы до нажатия кнопки.
    /// </summary>
    [Test]
    public async Task RotationDoesNotAutoArmNextArenaTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap1 = await pair.CreateTestMap();
        var testMap2 = await pair.CreateTestMap();

        EntityUid controller = default;
        EntityUid tracker1 = default;
        EntityUid tracker2 = default;
        EntityUid fighter1 = default;
        EntityUid fighter2 = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap1.Grid, testMap1.Tile.Tile);
            ExpandGrid(mapSystem, testMap2.Grid, testMap2.Tile.Tile);

            var tile1 = testMap1.Tile;
            var coords1 = new EntityCoordinates(tile1.GridUid, tile1.GridIndices.X, tile1.GridIndices.Y);
            var tile2 = testMap2.Tile;
            var coords2 = new EntityCoordinates(tile2.GridUid, tile2.GridIndices.X, tile2.GridIndices.Y);

            controller = entManager.SpawnEntity(null, coords1);
            var rotation = entManager.AddComponent<DuelRotationComponent>(controller);
            rotation.Loaded = true;
            rotation.LoadedArenas[0] = testMap1.MapId;
            rotation.LoadedArenas[1] = testMap2.MapId;

            tracker1 = entManager.SpawnEntity("DuelArenaTracker", coords1);
            entManager.GetComponent<DuelArenaComponent>(tracker1).RotationController = controller;
            tracker2 = entManager.SpawnEntity("DuelArenaTracker", coords2);
            entManager.GetComponent<DuelArenaComponent>(tracker2).RotationController = controller;

            entManager.SpawnEntity("DuelArenaSpawnMarker", coords2.Offset(new Vector2(2, 0)));
            entManager.SpawnEntity("DuelArenaSpawnMarker1", coords2.Offset(new Vector2(2, 2)));

            fighter1 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 0)));
            fighter2 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 1)));

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker1, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena1 = entManager.GetComponent<DuelArenaComponent>(tracker1);
            Assert.That(arena1.IsActive, Is.True, "Дуэль на первой арене должна быть активной");

            DamageSpecifier damage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(100000));
            damageableSystem.TryChangeDamage(fighter2, damage, true);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena2 = entManager.GetComponent<DuelArenaComponent>(tracker2);
            Assert.That(arena2.IsActive, Is.False, "Следующая арена не должна вооружаться автоматически");
            Assert.That(arena2.Phase, Is.EqualTo(DuelArenaPhase.Preparing),
                "Следующая арена должна ждать кнопки старта в фазе Preparing");

            var arena1 = entManager.GetComponent<DuelArenaComponent>(tracker1);
            Assert.That(arena1.IsActive, Is.False, "Прошлая арена должна быть завершена");
        });
    }

    /// <summary>
    /// Единственная загруженная арена: перехода на другую нет — бойцы возвращаются на спавн-маркеры
    /// той же арены (переигровка), CurrentArena не меняется.
    /// </summary>
    [Test]
    public async Task RotationReplaysSingleArenaTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        var testMap1 = await pair.CreateTestMap();

        EntityUid controller = default;
        EntityUid tracker1 = default;
        EntityUid spawn0 = default;
        EntityUid spawn1 = default;
        EntityUid fighter1 = default;
        EntityUid fighter2 = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap1.Grid, testMap1.Tile.Tile);

            var tile1 = testMap1.Tile;
            var coords1 = new EntityCoordinates(tile1.GridUid, tile1.GridIndices.X, tile1.GridIndices.Y);

            controller = entManager.SpawnEntity(null, coords1);
            var rotation = entManager.AddComponent<DuelRotationComponent>(controller);
            rotation.Loaded = true;
            rotation.LoadedArenas[0] = testMap1.MapId;

            tracker1 = entManager.SpawnEntity("DuelArenaTracker", coords1);
            entManager.GetComponent<DuelArenaComponent>(tracker1).RotationController = controller;

            spawn0 = entManager.SpawnEntity("DuelArenaSpawnMarker", coords1.Offset(new Vector2(1, 0)));
            spawn1 = entManager.SpawnEntity("DuelArenaSpawnMarker1", coords1.Offset(new Vector2(1, 1)));

            // Бойцы стоят НЕ на маркерах — переигровка должна вернуть их на спавны.
            fighter1 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 0)));
            fighter2 = entManager.SpawnEntity("DuelRotationTestDummy", coords1.Offset(new Vector2(3, 1)));

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker1, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena1 = entManager.GetComponent<DuelArenaComponent>(tracker1);
            Assert.That(arena1.IsActive, Is.True, "Дуэль должна быть активной");

            DamageSpecifier damage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(100000));
            damageableSystem.TryChangeDamage(fighter2, damage, true);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            static bool Near(Vector2 pos, Vector2 target) => (pos - target).LengthSquared() <= 0.01f;

            var rotation = entManager.GetComponent<DuelRotationComponent>(controller);
            Assert.That(rotation.CurrentArena, Is.EqualTo(0), "При единственной арене CurrentArena не должен меняться");

            var fighter1Pos = transformSystem.GetWorldPosition(fighter1);
            var fighter2Pos = transformSystem.GetWorldPosition(fighter2);
            var spawn0Pos = transformSystem.GetWorldPosition(spawn0);
            var spawn1Pos = transformSystem.GetWorldPosition(spawn1);

            Assert.That(Near(fighter1Pos, spawn0Pos) || Near(fighter1Pos, spawn1Pos), Is.True,
                "Первый боец должен вернуться на один из спавнов той же арены");
            Assert.That(Near(fighter2Pos, spawn0Pos) || Near(fighter2Pos, spawn1Pos), Is.True,
                "Второй боец должен вернуться на один из спавнов той же арены");
            Assert.That(fighter1Pos, Is.Not.EqualTo(fighter2Pos), "Бойцы должны занять разные спавны");
        });
    }

    /// <summary>
    /// Возвращает трекер той арены, на карте которой сейчас стоит боец — как игрок, жмущий кнопку
    /// старта на своей арене.
    /// </summary>
    private static EntityUid TrackerOnFightersMap(IEntityManager entManager, EntityUid fighter, params EntityUid[] trackers)
    {
        var map = entManager.GetComponent<TransformComponent>(fighter).MapID;
        foreach (var tracker in trackers)
        {
            if (entManager.GetComponent<TransformComponent>(tracker).MapID == map)
                return tracker;
        }

        throw new InvalidOperationException("Боец стоит на карте без трекера арены");
    }
}
