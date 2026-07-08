using Content.IntegrationTests.Fixtures;
using System.Collections.Generic;
using Content.Shared._Wega.Duel;
using Content.Shared.Damage;
using Content.Shared.Power.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Wega;

[TestFixture]
public sealed class ArenaPunisherTest : GameTest
{
    /// <summary>
    /// First hit brands the target; a second hit while branded detonates it — removing the brand
    /// and dealing bonus damage.
    /// </summary>
    [Test]
    public async Task BrandThenDetonate()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();

        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var target = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var shooter = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var bolt = entMan.SpawnEntity("ArenaBrandBolt", testMap.GridCoords);

            Assert.That(entMan.HasComponent<BrandedComponent>(target), Is.False);

            // First hit — brand.
            var ev1 = new ProjectileHitEvent(new DamageSpecifier(), target, shooter);
            entMan.EventBus.RaiseLocalEvent(bolt, ref ev1);
            Assert.That(entMan.TryGetComponent<BrandedComponent>(target, out var branded), Is.True,
                "first hit should apply a brand");
            var effect = branded!.Effect;
            Assert.That(effect, Is.Not.Null, "branded target should get a visual mark");
            Assert.That(entMan.EntityExists(effect!.Value), Is.True, "brand mark should exist");

            // Second hit — detonate (only the detonation branch clears the brand on a hit).
            var ev2 = new ProjectileHitEvent(new DamageSpecifier(), target, shooter);
            entMan.EventBus.RaiseLocalEvent(bolt, ref ev2);

            Assert.That(entMan.HasComponent<BrandedComponent>(target), Is.False,
                "detonation should clear the brand");
            Assert.That(entMan.EntityExists(effect.Value), Is.False,
                "detonation should remove the brand mark");
        });
    }

    /// <summary>
    /// The gun prototype carries the requested features: two-handed wield with an accuracy bonus,
    /// self-recharge, and casing ejection.
    /// </summary>
    [Test]
    public async Task PunisherHasExpectedFeatures()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();

        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var gun = entMan.SpawnEntity("WeaponArenaPunisher", testMap.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<WieldableComponent>(gun), Is.True, "should be two-handed");
                Assert.That(entMan.HasComponent<GunWieldBonusComponent>(gun), Is.True, "should be more accurate when wielded");
                Assert.That(entMan.HasComponent<BatterySelfRechargerComponent>(gun), Is.True, "should self-recharge");
                Assert.That(entMan.HasComponent<CasingEjectOnShotComponent>(gun), Is.True, "should eject casings");
            });
        });
    }

    /// <summary>
    /// A fired casing must land in the world, not stay parented to (magnetized onto) the shooter.
    /// </summary>
    [Test]
    public async Task CasingEjectsToWorldNotShooter()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();

        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var shooter = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var gun = entMan.SpawnEntity("WeaponArenaPunisher", testMap.GridCoords);

            var ev = new GunShotEvent(shooter, new List<(EntityUid? Uid, IShootable Shootable)>());
            entMan.EventBus.RaiseLocalEvent(gun, ref ev);

            EntityUid? casing = null;
            var query = entMan.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var uid, out var meta))
            {
                if (meta.EntityPrototype?.ID == "ArenaSpentCell")
                {
                    casing = uid;
                    break;
                }
            }

            Assert.That(casing, Is.Not.Null, "firing should eject a casing");
            var parent = entMan.GetComponent<TransformComponent>(casing!.Value).ParentUid;
            Assert.That(parent, Is.Not.EqualTo(shooter),
                "casing must drop into the world, not magnetize onto the shooter");
        });
    }
}
