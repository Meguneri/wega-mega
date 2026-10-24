using Content.IntegrationTests.Fixtures;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Wega;

[TestFixture]
public sealed class MappingMapsLoadTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [Test]
    public async Task SavedMappingMapsLoadAndInitialize()
    {
        await Server.WaitAssertion(() =>
        {
            var loader = Server.EntMan.System<MapLoaderSystem>();
            foreach (var path in new[] { "/Maps/_Wega/Arena/Adelin_Penthouse.yml", "/Maps/_Wega/Mapping/NEWMAP.yml" })
            {
                Assert.That(loader.TryLoadMap(new ResPath(path), out _, out _,
                    new DeserializationOptions { InitializeMaps = true }), Is.True, path);
            }
        });
        await Server.WaitIdleAsync();
    }
}
