using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Preferences;

[TestFixture]
[TestOf(typeof(HumanoidCharacterProfile))]
public sealed class ProfileExportTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
    };

    /// <summary>
    /// Exports a fully-populated profile to yaml (as the "export character" button does)
    /// and imports it back on the client (as the "import character" button does),
    /// verifying the roundtrip.
    /// </summary>
    [Test]
    public async Task ExportImportRoundtrip()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        await server.WaitIdleAsync();
        await client.WaitIdleAsync();

        string yaml = default!;

        await server.WaitAssertion(() =>
        {
            var protoMan = server.ResolveDependency<IPrototypeManager>();
            var loadout = new RoleLoadout("JobPassenger");

            var profile = HumanoidCharacterProfile.RandomWithSpecies("Vox")
                .WithName("Тест Тестович")
                .WithFlavorText("Обычный флейвор")
                .WithOOCFlavorText("OOC текст")
                .WithCharacterText("Характер")
                .WithGreenPreferencesText("Зелёное")
                .WithYellowPreferencesText("Жёлтое")
                .WithRedPreferencesText("Красное")
                .WithTagsText("#тег1, #тег2")
                .WithLinksText("https://example.com")
                .WithNSFWPreferencesText("нет")
                .WithStatus(Status.No)
                .WithHeight(178f)
                .WithJobPriority("Passenger", JobPriority.High)
                .WithAntagPreference("Traitor", true)
                .WithSpawnPriorityPreference(SpawnPriorityPreference.Arrivals)
                .WithLoadout(loadout);

            var dataNode = profile.ToDataNode();
            var sw = new StringWriter();
            dataNode.Write(sw);
            yaml = sw.ToString();

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
            var session = server.PlayerMan.Sessions.First();
            var imported = HumanoidCharacterProfile.FromStream(stream, session);

            Assert.That(DiffProfiles(profile, imported), Is.Empty,
                "Server-side imported profile does not match exported profile");
        });

        // Now the real path: importing on the client, like HumanoidProfileEditor.ImportProfile does.
        await client.WaitAssertion(() =>
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
            var session = client.Session!;
            var imported = HumanoidCharacterProfile.FromStream(stream, session);

            Assert.That(imported.Name, Is.EqualTo("Тест Тестович"));
        });
    }

    /// <summary>
    /// Imports a character file exported by an older build (pre-VisualBody refactor),
    /// where appearance still had hair/facialHair fields and a flat markings list.
    /// </summary>
    [Test]
    public async Task ImportLegacyV2()
    {
        const string yaml = @"forkId: wega
version: 2
profile:
  preferenceUnavailable: SpawnAsOverflow
  _jobPriorities:
    Passenger: High
  _antagPreferences: []
  _traitPreferences: []
  _loadouts: {}
  name: Старый Персонаж
  flavorText: ''
  species: Human
  voice: Eleanora
  barkVoice: BarksHuman1
  age: 30
  sex: Male
  gender: Male
  status: No
  height: 175
  appearance:
    hair: HumanHairAfricanPigtails
    hairColor: '#A0680AFF'
    facialHair: FacialHairShaved
    facialHairColor: '#A0680AFF'
    eyeColor: '#0000FFFF'
    skinColor: '#C0A080FF'
    markings:
    - markingId: TattooHiveChest
      markingColor:
      - '#999999FF'
  spawnPriority: None
";

        var server = Pair.Server;
        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
            var session = server.PlayerMan.Sessions.First();
            var imported = HumanoidCharacterProfile.FromStream(stream, session);
            Assert.That(imported.Name, Is.EqualTo("Старый Персонаж"));
            var markings = imported.Appearance.Markings.Values
                .SelectMany(v => v.Values)
                .SelectMany(m => m)
                .Select(m => m.MarkingId)
                .ToList();
            Assert.That(markings, Does.Contain("HumanHairAfricanPigtails"),
                $"Converted markings: [{string.Join(",", markings)}]");

            // Regression: the hair color must survive the legacy conversion, not reset to black.
            var hairMarking = imported.Appearance.Markings.Values
                .SelectMany(v => v.Values)
                .SelectMany(m => m)
                .First(m => m.MarkingId == "HumanHairAfricanPigtails");
            Assert.That(hairMarking.MarkingColors[0].ToHex(), Is.EqualTo("#A0680AFF"),
                "Hair color was lost during legacy import");
        });
    }

    /// <summary>
    /// Imports a character exported from a foreign server/fork: old appearance format,
    /// unknown fork-specific fields, an English name and prototypes that don't exist here.
    /// </summary>
    [Test]
    public async Task ImportForeignServerProfile()
    {
        const string yaml = @"forkId: sunrise
version: 2
profile:
  preferenceUnavailable: SpawnAsOverflow
  _jobPriorities:
    Passenger: High
    SomeForeignJob: High
  _antagPreferences:
  - SomeForeignAntag
  _traitPreferences:
  - SomeForeignTrait
  _loadouts:
    SomeForeignLoadout:
      role: SomeForeignLoadout
      selectedLoadouts: {}
  name: John Doe
  flavorText: 'Hello world'
  customForkField: 'some data'
  anotherCustomField: 42
  species: SomeForeignSpecies
  voice: SomeForeignVoice
  age: 30
  sex: Male
  gender: Male
  appearance:
    hair: HumanHairAfricanPigtails
    hairColor: '#A0680AFF'
    facialHair: FacialHairShaved
    facialHairColor: '#A0680AFF'
    eyeColor: '#0000FFFF'
    skinColor: '#C0A080FF'
    markings:
    - markingId: SomeForeignMarking
      markingColor:
      - '#999999FF'
  spawnPriority: None
";

        var server = Pair.Server;
        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
            var session = server.PlayerMan.Sessions.First();
            var imported = HumanoidCharacterProfile.FromStream(stream, session);

            TestContext.Out.WriteLine($"Imported name: '{imported.Name}', species: {imported.Species}, " +
                                      $"jobs: [{string.Join(",", imported.JobPriorities)}], " +
                                      $"antags: [{string.Join(",", imported.AntagPreferences)}], " +
                                      $"traits: [{string.Join(",", imported.TraitPreferences)}], " +
                                      $"loadouts: [{string.Join(",", imported.Loadouts.Keys)}], " +
                                      $"voice: {imported.Voice}");

            Assert.That(imported.Species.Id, Is.EqualTo("Human"), "Unknown species should reset to default");
            Assert.That(imported.Name.Trim(), Is.Not.Empty,
                "A name emptied by the restricted-name filter should be regenerated");
            Assert.That(imported.JobPriorities.Keys.Select(k => k.Id), Does.Not.Contain("SomeForeignJob"));
            Assert.That(imported.AntagPreferences, Is.Empty);
            Assert.That(imported.TraitPreferences, Is.Empty);
            Assert.That(imported.Loadouts.Keys, Does.Not.Contain("SomeForeignLoadout"));
        });
    }

    /// <summary>
    /// Imports a real character file exported from lust-station (Sunrise fork) — generated by that
    /// repo's own serializer — to verify a cross-server transfer actually works end to end.
    /// </summary>
    [Test]
    public async Task ImportLustStationProfile()
    {
        const string path = "/private/tmp/claude-501/-Users-meguneri-Programming-wega-mega/d0a569de-32da-4411-a5d9-41f12d768d9d/scratchpad/lust_export.yml";
        if (!File.Exists(path))
            Assert.Ignore($"lust-station export file not present at {path}");

        var yaml = File.ReadAllText(path);

        var server = Pair.Server;
        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
            var session = server.PlayerMan.Sessions.First();
            var imported = HumanoidCharacterProfile.FromStream(stream, session);

            var markings = imported.Appearance.Markings.Values
                .SelectMany(v => v.Values)
                .SelectMany(m => m)
                .Select(m => $"{m.MarkingId}({string.Join("|", m.MarkingColors.Select(c => c.ToHex()))})")
                .ToList();

            TestContext.Out.WriteLine($"Imported name: '{imported.Name}'");
            TestContext.Out.WriteLine($"Species: {imported.Species}, age: {imported.Age}, sex: {imported.Sex}");
            TestContext.Out.WriteLine($"Skin: {imported.Appearance.SkinColor}, eye: {imported.Appearance.EyeColor}");
            TestContext.Out.WriteLine($"Jobs: [{string.Join(",", imported.JobPriorities)}]");
            TestContext.Out.WriteLine($"Antags: [{string.Join(",", imported.AntagPreferences)}]");
            TestContext.Out.WriteLine($"Loadouts: [{string.Join(",", imported.Loadouts.Keys)}]");
            TestContext.Out.WriteLine($"SpawnPriority: {imported.SpawnPriority}");
            TestContext.Out.WriteLine($"Markings: [{string.Join(", ", markings)}]");

            // "John Doe" is Latin, so wega's RestrictedNames filter regenerates it to a Russian name.
            Assert.That(imported.Name.Trim(), Is.Not.Empty);
            Assert.That(imported.Species.Id, Is.EqualTo("Human"));
            Assert.That(imported.Age, Is.EqualTo(30));
            Assert.That(imported.JobPriorities.Keys.Select(k => k.Id), Does.Contain("Passenger"));
            Assert.That(imported.AntagPreferences.Select(a => a.Id), Does.Contain("Thief"));
            Assert.That(imported.Loadouts.Keys, Does.Contain("JobPassenger"));
            Assert.That(imported.Appearance.SkinColor.ToHex(), Is.EqualTo("#C0A080FF"));
            Assert.That(imported.Appearance.EyeColor.ToHex(), Is.EqualTo("#0000FFFF"));

            // The whole point of this test: hair color must transfer, not reset to black.
            Assert.That(markings, Has.Some.Contains("HumanHairAfricanPigtails(#A0680AFF)"),
                $"Hair color lost on cross-server import. Markings: [{string.Join(", ", markings)}]");
        });
    }

    private static List<string> DiffProfiles(HumanoidCharacterProfile a, HumanoidCharacterProfile b)
    {
        var diffs = new List<string>();

        void Check<T>(string name, T x, T y)
        {
            if (!Equals(x, y))
                diffs.Add($"{name}: '{x}' -> '{y}'");
        }

        Check(nameof(a.Name), a.Name, b.Name);
        Check(nameof(a.Age), a.Age, b.Age);
        Check(nameof(a.Sex), a.Sex, b.Sex);
        Check(nameof(a.Gender), a.Gender, b.Gender);
        Check(nameof(a.Status), a.Status, b.Status);
        Check(nameof(a.Height), a.Height, b.Height);
        Check(nameof(a.Species), a.Species, b.Species);
        Check(nameof(a.Voice), a.Voice, b.Voice);
        Check(nameof(a.BarkVoice), a.BarkVoice, b.BarkVoice);
        Check(nameof(a.PreferenceUnavailable), a.PreferenceUnavailable, b.PreferenceUnavailable);
        Check(nameof(a.SpawnPriority), a.SpawnPriority, b.SpawnPriority);
        Check(nameof(a.FlavorText), a.FlavorText, b.FlavorText);
        Check(nameof(a.OOCFlavorText), a.OOCFlavorText, b.OOCFlavorText);
        Check(nameof(a.CharacterFlavorText), a.CharacterFlavorText, b.CharacterFlavorText);
        Check(nameof(a.GreenFlavorText), a.GreenFlavorText, b.GreenFlavorText);
        Check(nameof(a.YellowFlavorText), a.YellowFlavorText, b.YellowFlavorText);
        Check(nameof(a.RedFlavorText), a.RedFlavorText, b.RedFlavorText);
        Check(nameof(a.TagsFlavorText), a.TagsFlavorText, b.TagsFlavorText);
        Check(nameof(a.LinksFlavorText), a.LinksFlavorText, b.LinksFlavorText);
        Check(nameof(a.NSFWFlavorText), a.NSFWFlavorText, b.NSFWFlavorText);

        if (!a.JobPriorities.OrderBy(kv => kv.Key.Id).SequenceEqual(b.JobPriorities.OrderBy(kv => kv.Key.Id)))
            diffs.Add($"JobPriorities: [{string.Join(",", a.JobPriorities)}] -> [{string.Join(",", b.JobPriorities)}]");
        if (!a.AntagPreferences.SetEquals(b.AntagPreferences))
            diffs.Add($"AntagPreferences: [{string.Join(",", a.AntagPreferences)}] -> [{string.Join(",", b.AntagPreferences)}]");
        if (!a.TraitPreferences.SetEquals(b.TraitPreferences))
            diffs.Add($"TraitPreferences: [{string.Join(",", a.TraitPreferences)}] -> [{string.Join(",", b.TraitPreferences)}]");
        if (!a.Loadouts.Keys.OrderBy(k => k).SequenceEqual(b.Loadouts.Keys.OrderBy(k => k)))
            diffs.Add($"Loadouts: [{string.Join(",", a.Loadouts.Keys)}] -> [{string.Join(",", b.Loadouts.Keys)}]");
        Check("EyeColor", a.Appearance.EyeColor, b.Appearance.EyeColor);
        Check("SkinColor", a.Appearance.SkinColor, b.Appearance.SkinColor);

        // EnsureValid is allowed to normalize empty marking categories, so compare the actual
        // markings (ids + colors) instead of the dictionary structure.
        static List<string> FlattenMarkings(HumanoidCharacterProfile p)
        {
            return p.Appearance.Markings.Values
                .SelectMany(v => v.Values)
                .SelectMany(m => m)
                .Select(m => $"{m.MarkingId}({string.Join("|", m.MarkingColors)})")
                .OrderBy(s => s)
                .ToList();
        }

        var aMarkings = FlattenMarkings(a);
        var bMarkings = FlattenMarkings(b);
        if (!aMarkings.SequenceEqual(bMarkings))
            diffs.Add($"Markings: [{string.Join(",", aMarkings)}] -> [{string.Join(",", bMarkings)}]");

        return diffs;
    }
}
