using System.Linq;
using System.Numerics;
using System.Text.Json;
using Content.IntegrationTests.Fixtures;
using Content.Server._Wega.ArcaneSocial;
using Content.Server.Storage.EntitySystems;
using Content.Shared._Wega.ArcaneSocial;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.ContentPack;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Wega.Clothing;

[TestFixture]
public sealed class ArcaneAccessoriesTest : GameTest
{
    [Test]
    public async Task CrateContainsNewItemsAndExistingEquivalentsAndClothingFits()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            using var stream = Server.ResolveDependency<IResourceManager>().ContentFileRead(
                new ResPath("/Prototypes/_Wega/ArcaneAccessories/manifest.json"));
            using var document = JsonDocument.Parse(stream);
            var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
            var reused = document.RootElement.GetProperty("reused").EnumerateObject()
                .Select(property => property.Value.GetString()!).Distinct().ToArray();
            var expected = items.Select(item => item.GetProperty("id").GetString()!).Concat(reused).ToArray();
            var crate = SSpawnAtPosition("WegaArcaneAccessoriesCrate", map.GridCoords);
            Assert.That(SEntMan.HasComponent<StorageComponent>(crate), Is.False);
            var container = SComp<EntityStorageComponent>(crate).Contents;
            var contents = container.ContainedEntities.Select(uid => SComp<MetaDataComponent>(uid).EntityPrototype!.ID).ToArray();
            Assert.That(contents, Is.EquivalentTo(expected));
            Server.System<EntityStorageSystem>().OpenStorage(crate);
            Assert.That(container.ContainedEntities, Is.Empty, "Opening the lid must release the supplied items.");
            var human = SSpawnAtPosition("MobHuman", map.GridCoords);
            var inventory = Server.System<InventorySystem>();
            foreach (var item in items)
            {
                var id = item.GetProperty("id").GetString()!;
                var uid = SSpawnAtPosition(id, map.GridCoords);
                var slot = item.GetProperty("category").GetString()!;
                if (slot == "prop")
                    Assert.That(SComp<MeleeWeaponComponent>(uid).PossibilityWideAtack, Is.False, id);
                if (slot is not ("neck" or "gloves" or "shoes" or "jumpsuit"))
                    continue;
                Assert.That(inventory.TryEquip(human, uid, slot, silent: true), Is.True, id);
                Assert.That(inventory.TryUnequip(human, slot, silent: true), Is.True, id);
            }
        });
    }

    private (EntityUid Holder, EntityUid Target, EntityUid Leash, EntityUid Collar) SpawnPair(
        EntityCoordinates coords, string collarId = "WegaArcaneClothingNeckChokerRing")
    {
        var holder = SSpawnAtPosition("MobHuman", coords);
        var target = SSpawnAtPosition("MobHuman", coords.Offset(new Vector2(0.75f, 0)));
        var collar = SSpawnAtPosition(collarId, coords);
        var leash = SSpawnAtPosition("WegaArcaneLeash", coords);
        Assert.That(Server.System<InventorySystem>().TryEquip(target, collar, "neck", silent: true), Is.True);
        Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(holder, leash), Is.True);
        return (holder, target, leash, collar);
    }

    private string Attach(EntityUid holder, EntityUid target, EntityUid leash)
    {
        Assert.That(Server.System<ArcaneLeashSystem>().TryAttach(leash, holder, target), Is.True);
        var id = SComp<ArcaneLeashComponent>(leash).JointId!;
        Assert.That(SComp<JointComponent>(holder).GetJoints.ContainsKey(id), Is.True);
        Assert.That(SComp<JointComponent>(target).GetJoints.ContainsKey(id), Is.True);
        return id;
    }

    private void AssertReleased(EntityUid leash, EntityUid holder, string jointId)
    {
        Assert.That(SComp<ArcaneLeashComponent>(leash).Target, Is.Null);
        Assert.That(SComp<ArcaneLeashComponent>(leash).Anchor, Is.Null);
        Assert.That(SComp<ArcaneLeashComponent>(leash).JointId, Is.Null);
        if (SEntMan.TryGetComponent<JointComponent>(holder, out var joints))
            Assert.That(joints.GetJoints.ContainsKey(jointId), Is.False);
    }

    [TestCase("WegaArcaneClothingNeckChokerRing")]
    [TestCase("ClothingNeckChokerBlack")]
    public async Task RemovingCollarReleasesBothEnds(string collarId)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var (holder, target, leash, _) = SpawnPair(map.GridCoords, collarId);
            var id = Attach(holder, target, leash);
            Assert.That(Server.System<InventorySystem>().TryUnequip(target, "neck", silent: true), Is.True);
            AssertReleased(leash, holder, id);
        });
    }

    [TestCase("leash")]
    [TestCase("target")]
    [TestCase("collar")]
    public async Task DeletionDoesNotLeavePhysicsConnections(string deleted)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var (holder, target, leash, collar) = SpawnPair(map.GridCoords);
            var id = Attach(holder, target, leash);
            SEntMan.DeleteEntity(deleted switch { "leash" => leash, "target" => target, _ => collar });
            if (deleted != "leash")
                AssertReleased(leash, holder, id);
            else if (SEntMan.TryGetComponent<JointComponent>(holder, out var joints))
                Assert.That(joints.GetJoints.ContainsKey(id), Is.False);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task TeleportingEitherEndToAnotherMapReleasesConnection(bool moveHolder)
    {
        var first = await Pair.CreateTestMap();
        var second = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var (holder, target, leash, _) = SpawnPair(first.GridCoords);
            var id = Attach(holder, target, leash);
            Server.System<SharedTransformSystem>().SetCoordinates(moveHolder ? holder : target, second.GridCoords);
            AssertReleased(leash, holder, id);
        });
    }

    [Test]
    public async Task DroppingAndPickingUpOwnLeadDoesNotCreateSelfJoint()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var (holder, target, leash, _) = SpawnPair(map.GridCoords);
            var id = Attach(holder, target, leash);
            var hands = Server.System<SharedHandsSystem>();
            Assert.That(hands.TryDrop(holder, leash), Is.True);
            Assert.That(SComp<ArcaneLeashComponent>(leash).Anchor, Is.EqualTo(leash));
            Assert.That(hands.TryPickupAnyHand(target, leash), Is.True);
            AssertReleased(leash, holder, id);
        });
    }

    [Test]
    public async Task PuttingLeadIntoStorageReleasesConnection()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var (holder, target, leash, _) = SpawnPair(map.GridCoords);
            var id = Attach(holder, target, leash);
            var crate = SSpawnAtPosition("CrateGenericSteel", map.GridCoords);
            Assert.That(Server.System<SharedHandsSystem>().TryDrop(holder, leash), Is.True);
            Assert.That(Server.System<SharedContainerSystem>().Insert(leash, SComp<EntityStorageComponent>(crate).Contents), Is.True);
            AssertReleased(leash, holder, id);
        });
    }

    [Test]
    public async Task OldTargetCleanupDoesNotDetachNewConnection()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var (holder, target, leash, _) = SpawnPair(map.GridCoords);
            Attach(holder, target, leash);
            var system = Server.System<ArcaneLeashSystem>();
            system.Detach(leash);
            var next = SSpawnAtPosition("MobHuman", map.GridCoords.Offset(new Vector2(0, 0.75f)));
            var collar = SSpawnAtPosition("ClothingNeckChokerBlack", map.GridCoords);
            Assert.That(Server.System<InventorySystem>().TryEquip(next, collar, "neck", silent: true), Is.True);
            Attach(holder, next, leash);
            SEntMan.RemoveComponent<ArcaneLeashedComponent>(target);
            Assert.That(SComp<ArcaneLeashComponent>(leash).Target, Is.EqualTo(next));
        });
    }

    [Test]
    public async Task SelfAttachmentAndSecondLeadAreRejected()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var (holder, target, leash, _) = SpawnPair(map.GridCoords);
            var system = Server.System<ArcaneLeashSystem>();
            Assert.That(system.TryAttach(leash, holder, holder), Is.False);
            Attach(holder, target, leash);
            var second = SSpawnAtPosition("WegaArcaneLeash", map.GridCoords);
            Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(holder, second), Is.True);
            Assert.That(system.TryAttach(second, holder, target), Is.False);
            Assert.That(SComp<ArcaneLeashComponent>(second).Target, Is.Null);
        });
    }
}
