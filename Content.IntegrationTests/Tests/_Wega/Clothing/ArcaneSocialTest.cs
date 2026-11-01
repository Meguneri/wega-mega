using System.Linq;
using System.Text.Json;
using Content.IntegrationTests.Fixtures;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Wega.Clothing;

[TestFixture]
public sealed class ArcaneSocialTest : GameTest
{
    [Test]
    public async Task WardrobeContainsEveryGarmentAndAllItemsSpawn()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            using var stream = Server.ResolveDependency<IResourceManager>().ContentFileRead(
                new ResPath("/Prototypes/_Wega/ArcaneSocial/manifest.json"));
            using var manifest = JsonDocument.Parse(stream);
            var items = manifest.RootElement.GetProperty("items").EnumerateArray().ToArray();
            var expected = items.Where(item => item.TryGetProperty("equipped", out _))
                .Select(item => item.GetProperty("id").GetString()!).ToArray();
            var crate = SSpawnAtPosition("WegaArcaneSocialWardrobe", map.GridCoords);
            Assert.That(SEntMan.HasComponent<StorageComponent>(crate), Is.False);
            var container = SComp<EntityStorageComponent>(crate).Contents;
            var contents = container.ContainedEntities
                .Select(uid => SComp<MetaDataComponent>(uid).EntityPrototype!.ID).ToArray();
            Assert.That(contents, Is.EquivalentTo(expected), "All clothes must fit inside the supplied crate.");
            Server.System<EntityStorageSystem>().OpenStorage(crate);
            Assert.That(container.ContainedEntities, Is.Empty, "The supplied clothes must be accessible after opening.");
            foreach (var item in items)
                SSpawnAtPosition(item.GetProperty("id").GetString()!, map.GridCoords);
        });
    }

    [Test]
    public async Task EveryGarmentCanBeEquippedOnHuman()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            using var stream = Server.ResolveDependency<IResourceManager>().ContentFileRead(
                new ResPath("/Prototypes/_Wega/ArcaneSocial/manifest.json"));
            using var manifest = JsonDocument.Parse(stream);
            var human = SSpawnAtPosition("MobHuman", map.GridCoords);
            var inventory = Server.System<InventorySystem>();
            foreach (var item in manifest.RootElement.GetProperty("items").EnumerateArray())
            {
                if (!item.TryGetProperty("equipped", out var equipped))
                    continue;
                var slot = equipped.GetString() switch
                {
                    "equipped-INNERCLOTHING" => "jumpsuit",
                    "equipped-OUTERCLOTHING" => "outerClothing",
                    _ => "head",
                };
                var id = item.GetProperty("id").GetString()!;
                var clothing = SSpawnAtPosition(id, map.GridCoords);
                Assert.That(inventory.TryEquip(human, clothing, slot, silent: true), Is.True, id);
                Assert.That(inventory.TryUnequip(human, slot, silent: true), Is.True, id);
            }
        });
    }
}
