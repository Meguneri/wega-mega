using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Wega.Weapons.Rengoku;
using Content.Shared._Wega.Weapons.Rengoku;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Wega.Weapons.Rengoku;

[TestFixture]
[TestOf(typeof(RengokuKatanaSystem))]
public sealed class RengokuKatanaSystemTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: RengokuTestDummy
  components:
  - type: Damageable
  - type: Injurable
    damageContainer: Biological
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      200: Dead
  - type: Flammable
    damage:
      types: {}
  - type: Appearance
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeCircle
          radius: 0.35
        density: 185
        mask:
        - MobMask
        layer:
        - MobLayer

- type: entity
  id: RengokuTestKatana
  components:
  - type: RengokuKatana
    firstFormRadius: 3
    firstFormHalfAngle: 75
    firstFormEffect: EffectRengokuFirstForm
    firstFormArcEffect: EffectRengokuFlame
    ninthFormRange: 2
    ninthFormSpeed: 20
    ninthFormEffect: EffectRengokuNinthForm
    ninthFormTrailEffect: EffectRengokuTrail
    ninthFormHitEffect: EffectRengokuNinthHit
    ninthFormRingEffect: EffectRengokuFlame
    firstFormSound:
      path: /Audio/Effects/fire.ogg
    ninthFormSound:
      path: /Audio/Effects/explosion1.ogg
    ninthFormChargeSound:
      path: /Audio/Effects/fire.ogg
";

    private static bool InCone(Vector2 facing, Vector2 toTarget, float halfAngleDegrees)
    {
        var f = facing.Normalized();
        var t = toTarget.Normalized();
        var dot = Math.Clamp(Vector2.Dot(f, t), -1f, 1f);
        var angle = MathF.Acos(dot);
        return angle <= Angle.FromDegrees(halfAngleDegrees).Theta;
    }

    [Test]
    public async Task FirstFormHitsTargetInCone()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var lookup = server.System<EntityLookupSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var testMap = await pair.CreateTestMap();

        EntityUid user = default;
        EntityUid target = default;
        EntityUid katana = default;

        await server.WaitAssertion(() =>
        {
            var coordinates = testMap.GridCoords;
            user = entManager.SpawnEntity("RengokuTestDummy", coordinates);
            target = entManager.SpawnEntity("RengokuTestDummy", coordinates.Offset(new Vector2(1.5f, 0)));
            katana = entManager.SpawnEntity("RengokuTestKatana", coordinates);

            transformSystem.SetWorldRotation(user, Angle.FromDegrees(90));

            var comp = entManager.GetComponent<RengokuKatanaComponent>(katana);
            var origin = transformSystem.GetWorldPosition(user);
            var facing = transformSystem.GetWorldRotation(user).ToWorldVec();
            var inRange = lookup.GetEntitiesInRange<MobStateComponent>(entManager.GetComponent<TransformComponent>(user).Coordinates, comp.FirstFormRadius);
            Assert.That(inRange.Count(e => e.Owner == target), Is.GreaterThan(0), "Цель должна быть в радиусе системы");

            var toTarget = transformSystem.GetWorldPosition(target) - origin;
            Assert.That(InCone(facing, toTarget, comp.FirstFormHalfAngle), Is.True, "Цель должна быть в конусе");

            var ev = new RengokuFirstFormActionEvent { Performer = user };
            entManager.EventBus.RaiseLocalEvent(katana, ev);

            Assert.That(ev.Handled, Is.True, "Первая форма должна быть обработана");
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var damage = damageableSystem.GetTotalDamage(target);
            Assert.That(damage, Is.GreaterThan(FixedPoint2.Zero), "Цель в конусе должна получить урон");
        });
    }

    [Test]
    public async Task FirstFormMissesTargetBehindUser()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var testMap = await pair.CreateTestMap();

        EntityUid user = default;
        EntityUid target = default;
        EntityUid katana = default;

        await server.WaitAssertion(() =>
        {
            var coordinates = testMap.GridCoords;
            user = entManager.SpawnEntity("RengokuTestDummy", coordinates);
            target = entManager.SpawnEntity("RengokuTestDummy", coordinates.Offset(new Vector2(-1.5f, 0)));
            katana = entManager.SpawnEntity("RengokuTestKatana", coordinates);

            // Поворачиваем пользователя лицом к +X (по умолчанию, но явно).
            transformSystem.SetWorldRotation(user, Angle.FromDegrees(90));

            var ev = new RengokuFirstFormActionEvent { Performer = user };
            entManager.EventBus.RaiseLocalEvent(katana, ev);

            Assert.That(ev.Handled, Is.True, "Первая форма должна быть обработана");
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var damage = damageableSystem.GetTotalDamage(target);
            Assert.That(damage, Is.EqualTo(FixedPoint2.Zero), "Цель за спиной не должна получить урон");
        });
    }

    [Test]
    public async Task NinthFormDamagesTargetAtLanding()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var damageableSystem = server.System<DamageableSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var testMap = await pair.CreateTestMap();

        EntityUid user = default;
        EntityUid target = default;
        EntityUid katana = default;

        await server.WaitAssertion(() =>
        {
            var coordinates = testMap.GridCoords;
            user = entManager.SpawnEntity("RengokuTestDummy", coordinates);
            target = entManager.SpawnEntity("RengokuTestDummy", coordinates.Offset(new Vector2(1.5f, 0)));
            katana = entManager.SpawnEntity("RengokuTestKatana", coordinates);

            // Рывок вдоль +X, цель уже в радиусе приземления.
            transformSystem.SetWorldRotation(user, Angle.FromDegrees(90));

            var ev = new RengokuNinthFormActionEvent { Performer = user };
            entManager.EventBus.RaiseLocalEvent(katana, ev);

            Assert.That(ev.Handled, Is.True, "Девятая форма должна быть обработана");
        });

        // Дождаться рывка и взрыва (range 2 / speed 20 = 0.1s + запас).
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var damage = damageableSystem.GetTotalDamage(target);
            Assert.That(damage, Is.GreaterThan(FixedPoint2.Zero), "Цель в радиусе приземления должна получить урон");
        });
    }
}
