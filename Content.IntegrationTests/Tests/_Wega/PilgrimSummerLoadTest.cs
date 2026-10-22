using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Wega;

[TestFixture]
public sealed class PilgrimSummerLoadTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [Test]
    public async Task SummerMapLoadsAndInitializes()
    {
        var entities = Server.ResolveDependency<IEntityManager>();
        var loader = entities.System<MapLoaderSystem>();
        await Server.WaitAssertion(() =>
        {
            Assert.That(loader.TryLoadMap(new ResPath("/Maps/_Wega/PilgrimSummer/pilgrim_summer.yml"),
                out _, out var grids, new DeserializationOptions { InitializeMaps = true }), Is.True);
            var definitions = Server.ResolveDependency<ITileDefinitionManager>();
            var maps = entities.System<SharedMapSystem>();
            foreach (var grid in grids!)
            {
                foreach (var tile in maps.GetAllTiles(grid.Owner, grid.Comp))
                {
                    Assert.That(tile.Tile.Variant, Is.LessThan(definitions[tile.Tile.TypeId].Variants),
                        $"Недопустимый вариант пола {tile.GridIndices}");
                }
            }
        });
        await Server.WaitIdleAsync();
    }

    [Test]
    public async Task SummerRoundStarts()
    {
        var cfg = Server.ResolveDependency<IConfigurationManager>();
        var ticker = Server.EntMan.System<ServerGameTicker>();
        await Server.WaitPost(() =>
        {
            cfg.SetCVar(CCVars.GameMap, "WegaPilgrimSummer");
            cfg.SetCVar(CCVars.GameDummyTicker, false);
            cfg.SetCVar(CCVars.GameLobbyEnabled, true);
            ticker.RestartRound();
        });
        await Pair.WaitCommand("forcemap WegaPilgrimSummer");
        await Pair.WaitCommand("forcepreset Extended");
        await Pair.WaitCommand("startround");
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() => Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound)));
    }
}
