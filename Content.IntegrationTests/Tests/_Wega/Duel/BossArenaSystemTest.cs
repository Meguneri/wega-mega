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
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
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
}
