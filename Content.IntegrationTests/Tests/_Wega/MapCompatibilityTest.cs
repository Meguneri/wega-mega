using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Wega;

[TestFixture]
public sealed class MapCompatibilityTest : GameTest
{
    private static readonly Regex SavedComponent = new(@"(?m)^    - type: (\w+)[ \t]*\r?$");
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task TileAliasesHaveExistingDirectTargets()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            var definitions = Server.ResolveDependency<ITileDefinitionManager>();
            Assert.That(definitions["FloorAstroGrass"].Variants, Is.EqualTo(6),
                "Варианты второго родителя должны сохраняться при миграции карт");
            foreach (var alias in prototypes.EnumeratePrototypes<TileAliasPrototype>())
                Assert.That(definitions.TryGetDefinition(alias.Target, out _), Is.True,
                    $"Миграция пола {alias.ID} указывает на отсутствующий пол {alias.Target}");
        });
    }

    [Test]
    public async Task SavedMapComponentsAreRegistered()
    {
        await Server.WaitAssertion(() =>
        {
            var resources = Server.ResolveDependency<IResourceManager>();
            var components = Server.ResolveDependency<IComponentFactory>();
            var errors = new List<string>();
            foreach (var path in resources.ContentFindFiles(new ResPath("/Maps")))
            {
                if (!path.ToString().EndsWith(".yml"))
                    continue;
                using var stream = resources.ContentFileRead(path);
                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();
                foreach (Match match in SavedComponent.Matches(text))
                {
                    var name = match.Groups[1].Value;
                    if (!components.TryGetRegistration(name, out _) && !components.IsIgnored(name))
                        errors.Add($"{path}: {name}");
                }
            }
            Assert.That(errors.Distinct(), Is.Empty,
                "Незарегистрированные компоненты в сохранённых картах");
        });
    }
}
