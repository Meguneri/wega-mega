using Content.IntegrationTests.Fixtures;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Wega
{
    // Повторяет DuelRotationSystem.PreloadArenas: грузит каждую карту-арену ротации с
    // InitializeMaps = true и проверяет, что загрузка/инициализация не падает (под RTB 278
    // старт сущности с NoRot и ненулевым поворотом бросает assert и роняет загрузку карты).
    [TestFixture]
    public sealed class ArenaMapsLoadTest : GameTest
    {
        private static readonly string[] ArenaMaps =
        {
            "/Maps/_Wega/Arena/DMarenaWALLrotation.yml",
            "/Maps/_Wega/Arena/DMarena2urban.yml",
            "/Maps/_Wega/Arena/arena_duel_31.yml",
        };

        [Test]
        public async Task ArenaMapsLoad()
        {
            var server = Pair.Server;
            var sEntities = server.ResolveDependency<IEntityManager>();
            var mapLoader = sEntities.System<MapLoaderSystem>();

            foreach (var path in ArenaMaps)
            {
                await server.WaitAssertion(() =>
                {
                    var opts = new DeserializationOptions { InitializeMaps = true };
                    Assert.That(
                        mapLoader.TryLoadMap(new ResPath(path), out _, out _, opts),
                        Is.True,
                        $"Не удалось загрузить арену {path}");
                });

                await server.WaitIdleAsync();
            }
        }
    }
}
