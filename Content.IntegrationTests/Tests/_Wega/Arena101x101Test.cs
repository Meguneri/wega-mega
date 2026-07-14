using System.Linq;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Wega;

[TestFixture]
public sealed class Arena101x101Test : GameTest
{
    private const string MapPath = "/Maps/_Wega/arena_101x101.yml";

    [Test]
    public async Task LoadAndVerifyCenter()
    {
        var server = Pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapLoader = entMan.System<MapLoaderSystem>();
        var mapSys = entMan.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var opts = new DeserializationOptions { InitializeMaps = true };
            Assert.That(mapLoader.TryLoadMap(new ResPath(MapPath), out var mapUid, out var gridUids, opts), Is.True,
                $"Не удалось загрузить карту {MapPath}");

            Assert.That(gridUids, Has.Count.EqualTo(1), "Ожидался ровно один grid");
            var grid = gridUids.Single();

            // Центральный тайл должен быть FloorSteel
            Assert.That(mapSys.GetTileRef(grid, new Vector2i(0, 0)).Tile.TypeId, Is.Not.EqualTo(Tile.Empty.TypeId),
                "Центральный тайл (0,0) не должен быть пустым");

            // Крайние тайлы внутри 101x101 области должны быть непустыми
            Assert.That(mapSys.GetTileRef(grid, new Vector2i(-50, -50)).Tile.TypeId, Is.Not.EqualTo(Tile.Empty.TypeId));
            Assert.That(mapSys.GetTileRef(grid, new Vector2i(50, 50)).Tile.TypeId, Is.Not.EqualTo(Tile.Empty.TypeId));

            // Тайлы за пределами 101x101 должны быть пустыми
            Assert.That(mapSys.GetTileRef(grid, new Vector2i(-51, -51)).Tile.TypeId, Is.EqualTo(Tile.Empty.TypeId));
            Assert.That(mapSys.GetTileRef(grid, new Vector2i(51, 51)).Tile.TypeId, Is.EqualTo(Tile.Empty.TypeId));
        });

        await server.WaitIdleAsync();
    }
}
