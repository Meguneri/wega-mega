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
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Item;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Wega.Duel;

[TestFixture]
[TestOf(typeof(BossArenaSystem))]
public sealed class BossArenaSystemTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  name: BossTestDummy
  id: BossTestDummy
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
  name: BossTestReward
  id: BossTestReward
  components:
  - type: Sprite
    sprite: Objects/Misc/guardian_info.rsi
";

    private static readonly ProtoId<DamageTypePrototype> TestDamageType = "Blunt";

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
    public async Task BossArenaStartPhaseAndDeathTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid tracker = default;
        EntityUid participant1 = default;
        EntityUid participant2 = default;
        EntityUid bossSpawnMarker = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var gridUid = tile.GridUid;
            var coordinates = new EntityCoordinates(gridUid, tile.GridIndices.X, tile.GridIndices.Y);

            tracker = entManager.SpawnEntity("BossArenaTracker", coordinates);
            participant1 = entManager.SpawnEntity("BossTestDummy", coordinates.Offset(new Vector2(1, 0)));
            participant2 = entManager.SpawnEntity("BossTestDummy", coordinates.Offset(new Vector2(2, 0)));
            bossSpawnMarker = entManager.SpawnEntity("BossArenaBossSpawnMarker", coordinates.Offset(new Vector2(0, 1)));

            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            arena.BossPrototype = "MobBossArena";
            arena.RewardPrototype = "BossTestReward";
            // Фазовая математика теста посчитана под базового босса — скейлинг по участникам отключаем.
            arena.HealthScaleBase = 1f;
            arena.HealthScalePerParticipant = 0f;
            Assert.That(arena.IsActive, Is.False);

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.True, "Арена должна быть активной после сигнала старта");
            Assert.That(arena.Participants.Count, Is.EqualTo(2), "Должно быть 2 участника");
            Assert.That(arena.Participants, Does.Contain(participant1));
            Assert.That(arena.Participants, Does.Contain(participant2));
            Assert.That(arena.Boss, Is.Not.Null, "Босс должен быть заспавнен");

            var boss = arena.Boss!.Value;
            var bossComp = entManager.GetComponent<BossArenaBossComponent>(boss);
            Assert.That(bossComp.CurrentPhase, Is.EqualTo(0), "Начальная фаза должна быть 0");

            // Телепорт участников и босса на маркеры.
            var bossCoords = entManager.GetComponent<TransformComponent>(boss).Coordinates;
            var markerCoords = entManager.GetComponent<TransformComponent>(bossSpawnMarker).Coordinates;
            Assert.That(bossCoords, Is.EqualTo(markerCoords), "Босс должен быть телепортирован на маркер");

            // Наносим урон, чтобы спровоцировать переход во 2-ю фазу (порог 0.66, босс мёртв при 600).
            // Здоровье = 1 - damage/600. Фаза 1 при damage >= 600 * (1 - 0.66) = 204.
            DamageSpecifier phaseDamage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(250));
            damageableSystem.TryChangeDamage(boss, phaseDamage, true);

            arena = entManager.GetComponent<BossArenaComponent>(tracker);
            bossComp = entManager.GetComponent<BossArenaBossComponent>(boss);
            Assert.That(bossComp.CurrentPhase, Is.EqualTo(1), "Фаза должна перейти на 1 после урона > 204");
            Assert.That(arena.Phase, Is.EqualTo(1), "Арена должна отслеживать фазу босса");

            // Добиваем босса.
            DamageSpecifier lethalDamage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(100000));
            damageableSystem.TryChangeDamage(boss, lethalDamage, true);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.False, "Арена должна завершиться после смерти босса");
            Assert.That(arena.Boss, Is.Null, "Босс должен быть удалён");
            Assert.That(arena.Minions.Count, Is.EqualTo(0), "Миньоны должны быть удалены при завершении арены");
            Assert.That(arena.GateCloseAt, Is.Not.Null, "Должен быть запланирован сигнал закрытия");

            // Находим награду на карте.
            var reward = entManager.EntityQuery<MetaDataComponent>()
                .FirstOrDefault(m => m.EntityPrototype?.ID == "BossTestReward");
            Assert.That(reward, Is.Not.Null, "Награда должна быть заспавнена");
        });
    }

    [Test]
    public async Task BossArenaMinionWaveTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid tracker = default;
        EntityUid participant1 = default;
        EntityUid participant2 = default;
        EntityUid bossSpawnMarker = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var gridUid = tile.GridUid;
            var coordinates = new EntityCoordinates(gridUid, tile.GridIndices.X, tile.GridIndices.Y);

            tracker = entManager.SpawnEntity("BossArenaTracker", coordinates);
            participant1 = entManager.SpawnEntity("BossTestDummy", coordinates.Offset(new Vector2(1, 0)));
            participant2 = entManager.SpawnEntity("BossTestDummy", coordinates.Offset(new Vector2(2, 0)));
            bossSpawnMarker = entManager.SpawnEntity("BossArenaBossSpawnMarker", coordinates.Offset(new Vector2(0, 1)));

            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            arena.BossPrototype = "MobBossArena";
            arena.RewardPrototype = "BossTestReward";
            // Фазовая математика теста посчитана под базового босса — скейлинг по участникам отключаем.
            arena.HealthScaleBase = 1f;
            arena.HealthScalePerParticipant = 0f;
            arena.MinionPrototypes = new List<EntProtoId> { "BossTestDummy" };
            arena.MinionPhaseStart = 1;
            arena.MinionSpawnInterval = 2f;
            arena.MinionSpawnPerWave = 3;
            arena.MaxMinions = 6;
            arena.MinionSpawnRadius = 4f;

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.True, "Арена должна быть активной");
            Assert.That(arena.Boss, Is.Not.Null, "Босс должен быть заспавнен");

            // Переводим босса во фазу 1, чтобы разрешить волны миньонов.
            var boss = arena.Boss!.Value;
            DamageSpecifier phaseDamage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(250));
            damageableSystem.TryChangeDamage(boss, phaseDamage, true);
            arena = entManager.GetComponent<BossArenaComponent>(tracker);
            Assert.That(arena.Phase, Is.EqualTo(1), "Фаза должна быть 1 для волн миньонов");
            Assert.That(arena.Minions.Count, Is.EqualTo(0), "До первого интервала миньонов быть не должно");
        });

        // Ждём интервал спавна (2 секунды ≈ 60 тиков при 30 тик/с, даём запас).
        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            Assert.That(arena.Minions.Count, Is.GreaterThanOrEqualTo(1), "Должна быть заспавнена хотя бы одна волна миньонов");
            Assert.That(arena.Minions.Count, Is.LessThanOrEqualTo(arena.MaxMinions), "Количество миньонов не должно превышать максимум");
        });

        // Добиваем босса, чтобы проверить очистку миньонов.
        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            var boss = arena.Boss!.Value;
            DamageSpecifier lethalDamage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(100000));
            damageableSystem.TryChangeDamage(boss, lethalDamage, true);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.False, "Арена должна завершиться");
            Assert.That(arena.Minions.Count, Is.EqualTo(0), "Миньоны должны быть удалены после завершения арены");
        });
    }

    /// <summary>
    /// Скейлинг по числу участников: при двух участниках пороги ХП босса умножаются на 1.25
    /// (0.75 + 0.25×2), а базовый урон природного оружия — на 1.1 (1 + 0.1×(2−1)).
    /// </summary>
    [Test]
    public async Task BossArenaScalesWithParticipantsTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var thresholdSystem = server.System<MobThresholdSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid tracker = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var coordinates = new EntityCoordinates(tile.GridUid, tile.GridIndices.X, tile.GridIndices.Y);

            tracker = entManager.SpawnEntity("BossArenaTracker", coordinates);
            entManager.SpawnEntity("BossTestDummy", coordinates.Offset(new Vector2(1, 0)));
            entManager.SpawnEntity("BossTestDummy", coordinates.Offset(new Vector2(2, 0)));

            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            arena.BossPrototype = "MobBossArena";

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.True, "Арена должна быть активной");
            Assert.That(arena.Participants.Count, Is.EqualTo(2), "Должно быть 2 участника");
            Assert.That(arena.Boss, Is.Not.Null, "Босс должен быть заспавнен");

            var boss = arena.Boss!.Value;

            // ХП: 600 × (0.75 + 0.25×2) = 750 до смерти.
            var deadThreshold = thresholdSystem.GetThresholdForState(boss, MobState.Dead);
            Assert.That((float) deadThreshold, Is.EqualTo(750f).Within(0.01f),
                "Порог смерти босса должен отмасштабироваться под 2 участников");

            // Урон природного оружия: 25 × (1 + 0.1×(2−1)) = 27.5.
            var bossComp = entManager.GetComponent<BossArenaBossComponent>(boss);
            Assert.That((float) bossComp.BaseMeleeDamage!.GetTotal(), Is.EqualTo(27.5f).Within(0.01f),
                "Базовый урон босса должен отмасштабироваться под 2 участников");

            var melee = entManager.GetComponent<MeleeWeaponComponent>(boss);
            Assert.That((float) melee.Damage.GetTotal(), Is.EqualTo(27.5f).Within(0.01f),
                "Отмасштабированный урон должен примениться к оружию босса");
        });
    }

    /// <summary>
    /// Энрейдж: по истечении MaxFightDuration бой НЕ обрывается поражением — босс впадает в ярость
    /// (удвоение урона/скорости поверх фазовых), и арена продолжается до вайпа любой стороны.
    /// </summary>
    [Test]
    public async Task BossArenaEnrageTest()
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
            var coordinates = new EntityCoordinates(tile.GridUid, tile.GridIndices.X, tile.GridIndices.Y);

            tracker = entManager.SpawnEntity("BossArenaTracker", coordinates);
            entManager.SpawnEntity("BossTestDummy", coordinates.Offset(new Vector2(1, 0)));
            entManager.SpawnEntity("BossTestDummy", coordinates.Offset(new Vector2(2, 0)));

            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            arena.BossPrototype = "MobBossArena";
            // Чистые числа: скейлинг отключён (базовый урон 25 → в ярости 50).
            arena.HealthScaleBase = 1f;
            arena.HealthScalePerParticipant = 0f;
            arena.DamageScalePerParticipant = 0f;
            arena.MaxFightDuration = 1f;

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.True, "Арена должна быть активной");
            Assert.That(arena.Enraged, Is.False, "До истечения таймера ярости быть не должно");
        });

        // Таймер боя — 1 секунда, ждём с запасом.
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            Assert.That(arena.Enraged, Is.True, "По истечении таймера босс должен впасть в ярость");
            Assert.That(arena.IsActive, Is.True, "Энрейдж не должен завершать арену — бой идёт до вайпа");
            Assert.That(arena.FightEndAt, Is.Null, "Таймер боя снимается после энрейджа");
            Assert.That(arena.Boss, Is.Not.Null, "Босс должен остаться на арене");

            var melee = entManager.GetComponent<MeleeWeaponComponent>(arena.Boss!.Value);
            Assert.That((float) melee.Damage.GetTotal(), Is.EqualTo(50f).Within(0.01f),
                "Урон босса в ярости должен удвоиться (25 × 2)");
        });
    }

    /// <summary>
    /// Анти-кайт: все живые участники держатся дальше AntiKiteRange от босса дольше grace-периода —
    /// босс запускает регенерацию. Расстрелять его с безопасной дистанции не выйдет.
    /// </summary>
    [Test]
    public async Task BossArenaAntiKiteRegenTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid tracker = default;
        EntityUid participant1 = default;
        EntityUid participant2 = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var coordinates = new EntityCoordinates(tile.GridUid, tile.GridIndices.X, tile.GridIndices.Y);

            tracker = entManager.SpawnEntity("BossArenaTracker", coordinates);
            participant1 = entManager.SpawnEntity("BossTestDummy", coordinates.Offset(new Vector2(1, 0)));
            participant2 = entManager.SpawnEntity("BossTestDummy", coordinates.Offset(new Vector2(2, 0)));

            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            arena.BossPrototype = "MobBossArena";
            arena.HealthScaleBase = 1f;
            arena.HealthScalePerParticipant = 0f;
            arena.AntiKiteGraceSeconds = 1f;

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.True, "Арена должна быть активной");
            Assert.That(arena.Boss, Is.Not.Null, "Босс должен быть заспавнен");

            // Наносим урон и уводим обоих участников далеко за AntiKiteRange (12 тайлов), оставаясь на гриде.
            var boss = arena.Boss!.Value;
            DamageSpecifier damage = new(prototypeManager.Index(TestDamageType), FixedPoint2.New(100));
            damageableSystem.TryChangeDamage(boss, damage, true);

            transformSystem.SetCoordinates(participant1, new EntityCoordinates(testMap.Grid.Owner, new Vector2(25, 0)));
            transformSystem.SetCoordinates(participant2, new EntityCoordinates(testMap.Grid.Owner, new Vector2(25, 1)));
        });

        // grace 1 с + пара секунд регенерации (20 ХП/с).
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.True, "Арена должна продолжаться — участники живы и на гриде");
            Assert.That(arena.OutOfRangeSince, Is.Not.Null, "Кайт-таймер должен быть запущен");

            var boss = arena.Boss!.Value;
            Assert.That((float) damageableSystem.GetTotalDamage(boss), Is.LessThan(100f),
                "Босс должен отрегенерировать часть урона, пока все участники далеко");
        });
    }

    /// <summary>
    /// Уход с грида арены = поражение: когда все участники покидают грид трекера, арена сбрасывается.
    /// </summary>
    [Test]
    public async Task BossArenaGridLeaveFailsTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        var testMap1 = await pair.CreateTestMap();
        var testMap2 = await pair.CreateTestMap();

        EntityUid tracker = default;
        EntityUid participant1 = default;
        EntityUid participant2 = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap1.Grid, testMap1.Tile.Tile);

            var tile = testMap1.Tile;
            var coordinates = new EntityCoordinates(tile.GridUid, tile.GridIndices.X, tile.GridIndices.Y);

            tracker = entManager.SpawnEntity("BossArenaTracker", coordinates);
            participant1 = entManager.SpawnEntity("BossTestDummy", coordinates.Offset(new Vector2(1, 0)));
            participant2 = entManager.SpawnEntity("BossTestDummy", coordinates.Offset(new Vector2(2, 0)));

            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            arena.BossPrototype = "MobBossArena";

            var startEv = new SignalReceivedEvent("Open");
            entManager.EventBus.RaiseLocalEvent(tracker, ref startEv);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.True, "Арена должна быть активной");

            // Уводим всех участников на другую карту.
            var tile2 = testMap2.Tile;
            var coords2 = new EntityCoordinates(tile2.GridUid, tile2.GridIndices.X, tile2.GridIndices.Y);
            transformSystem.SetCoordinates(participant1, coords2);
            transformSystem.SetCoordinates(participant2, coords2.Offset(new Vector2(0, 1)));
        });

        // Интервал сканирования 0.5 с ≈ 15 тиков при 30 тик/с — ждём с запасом.
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var arena = entManager.GetComponent<BossArenaComponent>(tracker);
            Assert.That(arena.IsActive, Is.False, "Арена должна сброситься, когда все участники покинули грид");
            Assert.That(arena.Boss, Is.Null, "Босс должен быть удалён при сбросе");
        });
    }

    /// <summary>
    /// Привязка огнестрела босса (BossArenaBoundGun): поднять и выстрелить из него может только
    /// сущность с BossArenaBossComponent; для остальных обе попытки отменяются.
    /// </summary>
    [Test]
    public async Task BossArenaBoundGunTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var coordinates = new EntityCoordinates(tile.GridUid, tile.GridIndices.X, tile.GridIndices.Y);

            var dummy = entManager.SpawnEntity("BossTestDummy", coordinates);
            var boss = entManager.SpawnEntity(null, coordinates.Offset(new Vector2(1, 0)));
            entManager.AddComponent<BossArenaBossComponent>(boss);
            var gun = entManager.SpawnEntity("WeaponBossArenaKara", coordinates.Offset(new Vector2(2, 0)));
            Assert.That(entManager.HasComponent<BossArenaBoundGunComponent>(gun), Is.True,
                "Пулемёт босса должен нести маркер привязки");

            // Подбор посторонним — отменяется.
            var pickupEv = new GettingPickedUpAttemptEvent(dummy, gun, false);
            entManager.EventBus.RaiseLocalEvent(gun, pickupEv);
            Assert.That(pickupEv.Cancelled, Is.True, "Посторонний не должен поднять привязанный огнестрел");

            // Подбор боссом — разрешается.
            var bossPickupEv = new GettingPickedUpAttemptEvent(boss, gun, false);
            entManager.EventBus.RaiseLocalEvent(gun, bossPickupEv);
            Assert.That(bossPickupEv.Cancelled, Is.False, "Босс должен поднимать привязанный огнестрел");

            // Выстрел посторонним — отменяется.
            var shotEv = new ShotAttemptedEvent { User = dummy, Used = gun };
            entManager.EventBus.RaiseLocalEvent(gun, ref shotEv);
            Assert.That(shotEv.Cancelled, Is.True, "Посторонний не должен стрелять из привязанного огнестрела");

            // Выстрел боссом — разрешается.
            var bossShotEv = new ShotAttemptedEvent { User = boss, Used = gun };
            entManager.EventBus.RaiseLocalEvent(gun, ref bossShotEv);
            Assert.That(bossShotEv.Cancelled, Is.False, "Босс должен стрелять из привязанного огнестрела");
        });
    }

    /// <summary>
    /// Очереди огнестрела (BossArenaVolley): первый выстрел открывает очередь из 3–5 выстрелов;
    /// когда очередь исчерпана — включается кулдаун, и HTN уводит босса в ближний бой.
    /// </summary>
    [Test]
    public async Task BossArenaVolleyTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var tile = testMap.Tile;
            var coordinates = new EntityCoordinates(tile.GridUid, tile.GridIndices.X, tile.GridIndices.Y);

            var boss = entManager.SpawnEntity(null, coordinates);
            var volley = entManager.AddComponent<BossArenaVolleyComponent>(boss);
            // Детерминированная очередь ровно в 3 выстрела.
            volley.VolleyShotsMin = 3;
            volley.VolleyShotsMax = 3;

            var gun = entManager.SpawnEntity("WeaponBossArenaKara", coordinates.Offset(new Vector2(1, 0)));

            void FireShot()
            {
                var ev = new GunShotEvent(boss, new List<(EntityUid? Uid, IShootable Shootable)>());
                entManager.EventBus.RaiseLocalEvent(gun, ref ev);
            }

            FireShot();
            Assert.That(volley.ShotsRemaining, Is.EqualTo(2), "Первый выстрел должен открыть очередь из 3");

            FireShot();
            Assert.That(volley.ShotsRemaining, Is.EqualTo(1));
            Assert.That(volley.NextVolleyAt, Is.Null, "Кулдаун не должен включаться до конца очереди");

            FireShot();
            Assert.That(volley.ShotsRemaining, Is.EqualTo(0), "Очередь должна исчерпаться");
            Assert.That(volley.NextVolleyAt, Is.Not.Null, "После очереди должен включиться кулдаун огнестрела");
        });
    }
}
