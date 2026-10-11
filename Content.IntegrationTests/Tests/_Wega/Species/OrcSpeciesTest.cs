using System.Linq;
using System.Numerics;
using System.Text.Json;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Body;
using Content.Shared.Clothing;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Speech;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Wega.Species;

[TestFixture]
public sealed class OrcSpeciesTest : GameTest
{
    [Test]
    public async Task SpawnWithCompleteBodyAndApplyHeight()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var orc = SSpawnAtPosition("MobOrc", map.GridCoords);
            var organs = SComp<BodyComponent>(orc).Organs!.ContainedEntities
                .Select(uid => SComp<MetaDataComponent>(uid).EntityPrototype!.ID).ToArray();
            Assert.That(organs, Has.Length.EqualTo(20));
            Assert.That(organs, Does.Contain("OrganHumanTongue"));
            Assert.That(organs, Does.Contain("OrganHumanEars"));
            Assert.That(organs, Does.Contain("OrganOrcStomach"));

            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Orc").WithHeight(237.25f);
            Server.System<HumanoidProfileSystem>().ApplyProfileTo(orc, profile);
            Assert.That(SComp<HumanoidProfileComponent>(orc).Height, Is.EqualTo(237.25f));
            Assert.That(SComp<InventoryComponent>(orc).SpeciesId, Is.EqualTo("orc"));
        });
    }

    [Test]
    public async Task FittedEquipmentAndOrdinaryClothingCanBeWorn()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var orc = SSpawnAtPosition("MobOrc", map.GridCoords);
            var human = SSpawnAtPosition("MobHuman", map.GridCoords);
            var shirt = SSpawnAtPosition("ClothingUniformJumpsuitColorGrey", map.GridCoords);
            var standard = SSpawnAtPosition("ClothingOuterHardsuitEngineering", map.GridCoords);
            var fitted = SSpawnAtPosition("ClothingOuterHardsuitOrcCadet", map.GridCoords);
            var helmet = SSpawnAtPosition("ClothingHeadHardsuitOrcCadet", map.GridCoords);
            var inventory = Server.System<InventorySystem>();

            Assert.That(inventory.TryEquip(orc, shirt, "jumpsuit", silent: true), Is.True);
            Assert.That(inventory.CanEquip(orc, standard, "outerClothing", out var tooSmall), Is.False);
            Assert.That(tooSmall, Is.EqualTo("wega-orc-clothing-too-small"));
            Assert.That(inventory.CanEquip(human, fitted, "outerClothing", out var tooLarge), Is.False);
            Assert.That(tooLarge, Is.EqualTo("wega-orc-clothing-too-large"));
            Assert.That(inventory.CanEquip(human, helmet, "head", out _), Is.False);
            Assert.That(inventory.TryEquip(orc, fitted, "outerClothing", silent: true), Is.True);
            Assert.That(inventory.TryEquip(orc, helmet, "head", silent: true), Is.True);
        });
    }

    [Test]
    public async Task ClothingVariantsDoNotChangeHumanVisuals()
    {
        await Client.WaitAssertion(() =>
        {
            var orc = CSpawn("AppearanceOrc");
            var human = CSpawn("AppearanceHuman");
            var shirt = CSpawn("ClothingUniformJumpsuitColorGrey");

            var orcVisuals = new GetEquipmentVisualsEvent(orc, "jumpsuit");
            CEntMan.EventBus.RaiseLocalEvent(shirt, orcVisuals);
            Assert.That(orcVisuals.Layers, Is.Not.Empty);
            Assert.That(orcVisuals.Layers[0].Item2.State, Is.EqualTo("equipped-INNERCLOTHING-orc"));
            Assert.That(orcVisuals.Layers[0].Item2.RsiPath, Does.Contain("_Wega/Clothing/Orc/"));

            var humanVisuals = new GetEquipmentVisualsEvent(human, "jumpsuit");
            CEntMan.EventBus.RaiseLocalEvent(shirt, humanVisuals);
            Assert.That(humanVisuals.Layers[0].Item2.State, Is.EqualTo("equipped-INNERCLOTHING"));
            Assert.That(humanVisuals.Layers[0].Item2.RsiPath ?? "", Does.Not.Contain("_Wega/Clothing/Orc/"));
        });
    }

    [Test]
    public async Task GenericClothingUsesDisplacementWithoutAdditionalScale()
    {
        await Client.WaitAssertion(() =>
        {
            var orc = CSpawn("AppearanceOrc");
            var inventory = CComp<InventoryComponent>(orc);
            var displacements = inventory.Displacements;
            Assert.That(displacements.ContainsKey("jumpsuit"), Is.True);
            Assert.That(displacements["jumpsuit"].SizeMaps[32].RsiPath,
                Is.EqualTo("_Wega/Mobs/Species/Orc/displacement.rsi"));

            var shirt = CSpawn("ClothingUniformJumpsuitCaptain");
            var visuals = new GetEquipmentVisualsEvent(orc, "jumpsuit");
            CEntMan.EventBus.RaiseLocalEvent(shirt, visuals);
            Assert.That(visuals.Layers, Is.Not.Empty);
            Assert.That(visuals.Layers[0].Item2.State, Is.EqualTo("equipped-INNERCLOTHING"));
            Assert.That(visuals.Layers[0].Item2.Scale ?? Vector2.One, Is.EqualTo(Vector2.One));
        });
    }

    [Test]
    public async Task DisplacementMapsAreLinearAndUseExistingInventorySlots()
    {
        await Client.WaitAssertion(() =>
        {
            var resources = Client.ResolveDependency<IResourceManager>();
            using var stream = resources.ContentFileRead(
                new ResPath("/Textures/_Wega/Mobs/Species/Orc/displacement.rsi/meta.json"));
            using var metadata = JsonDocument.Parse(stream);
            // Значения карты — числовые смещения, sRGB-преобразование заставляет читать чужие кадры атласа.
            Assert.That(metadata.RootElement.GetProperty("load").GetProperty("srgb").GetBoolean(), Is.False);

            var orc = CSpawn("AppearanceOrc");
            var inventory = CComp<InventoryComponent>(orc);
            var displacements = inventory.Displacements;
            var inventorySlots = inventory.Slots;
            var slots = inventorySlots.Select(slot => slot.Name).ToArray();
            Assert.That(displacements.Keys.All(slots.Contains), Is.True);
            foreach (var slot in new[] { "shoes", "socks", "underwearbottom", "underweartop", "suitstorage" })
                Assert.That(displacements.ContainsKey(slot), Is.True, slot);

            foreach (var (prototype, slot) in new[]
                     {
                         ("ClothingShoesColorBlack", "shoes"),
                         ("ClothingUnderBottomGrey", "underwearbottom"),
                         ("ClothingUnderTopBraGrey", "underweartop"),
                     })
            {
                var clothing = CSpawn(prototype);
                var visuals = new GetEquipmentVisualsEvent(orc, slot);
                CEntMan.EventBus.RaiseLocalEvent(clothing, visuals);
                Assert.That(visuals.Layers, Is.Not.Empty, prototype);
                foreach (var (_, layer) in visuals.Layers)
                    Assert.That(layer.Scale ?? Vector2.One, Is.EqualTo(Vector2.One), prototype);
            }
        });
    }

    [Test]
    public async Task AccentHandlesLongSpeech()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var orc = SSpawnAtPosition("MobOrc", map.GridCoords);
            var shortSpeech = new AccentGetEvent(orc, "ударяю!");
            SEntMan.EventBus.RaiseLocalEvent(orc, ref shortSpeech);
            Assert.That(shortSpeech.Message, Is.EqualTo("ударять!"));

            var longSpeech = new AccentGetEvent(orc, new string('а', 16377) + " ударяю");
            SEntMan.EventBus.RaiseLocalEvent(orc, ref longSpeech);
            Assert.That(longSpeech.Message, Does.EndWith(" ударять"));

            var pronoun = Loc.GetString("wega-accent-orc-words-1-1");
            var repeatedWords = new AccentGetEvent(orc, string.Join(" ", Enumerable.Repeat(pronoun, 8000)));
            SEntMan.EventBus.RaiseLocalEvent(orc, ref repeatedWords);
            Assert.That(repeatedWords.Message.Split(' '), Has.Length.EqualTo(8000));
            Assert.That(repeatedWords.Message, Is.Not.EqualTo(string.Join(" ", Enumerable.Repeat(pronoun, 8000))));
        });
    }
}
