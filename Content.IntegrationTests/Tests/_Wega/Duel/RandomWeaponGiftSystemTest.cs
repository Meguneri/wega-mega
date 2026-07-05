using Content.IntegrationTests.Fixtures;
using Content.Server._Wega.Duel.Components;
using Content.Server._Wega.Duel.Systems;
using System.Numerics;
using Content.Shared.Interaction.Events;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Wega.Duel;

[TestFixture]
[TestOf(typeof(RandomWeaponGiftSystem))]
public sealed class RandomWeaponGiftSystemTest : GameTest
{
    private static void ExpandGrid(SharedMapSystem mapSystem, Entity<MapGridComponent> grid, Tile tile)
    {
        for (var x = -2; x <= 2; x++)
        {
            for (var y = -2; y <= 2; y++)
            {
                mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), tile);
            }
        }
    }

    private static int CountEntitiesWithPrototype(IEntityManager entManager, EntProtoId proto)
    {
        var count = 0;
        var query = entManager.EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == proto.Id)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Подарок-рулетка при инициализации выбирает случайное оружие, а при использовании в руке
    /// спавнит именно его и удаляет сам подарок.
    /// </summary>
    [Test]
    public async Task RandomWeaponGiftUnwrapsSelectedWeaponTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid gift = default;
        EntityUid user = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var gridUid = testMap.Tile.GridUid;
            var coords = new EntityCoordinates(gridUid, testMap.Tile.GridIndices.X, testMap.Tile.GridIndices.Y);

            gift = entManager.SpawnEntity("PresentWeaponRoulette", coords);
            user = entManager.SpawnEntity("DuelTestDummy", coords.Offset(new Vector2(1, 0)));

            var giftComp = entManager.GetComponent<RandomWeaponGiftComponent>(gift);
            Assert.That(giftComp.SelectedEntity, Is.Not.Null, "После MapInit должен быть выбран прототип оружия");
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var giftComp = entManager.GetComponent<RandomWeaponGiftComponent>(gift);
            var selected = giftComp.SelectedEntity!.Value;

            Assert.That(CountEntitiesWithPrototype(entManager, selected), Is.EqualTo(0),
                "Выбранное оружие не должно существовать до распаковки");

            var useEv = new UseInHandEvent(user);
            entManager.EventBus.RaiseLocalEvent(gift, useEv);

            Assert.That(useEv.Handled, Is.True, "Использование в руке должно быть обработано");
            Assert.That(entManager.IsQueuedForDeletion(gift), Is.True, "Подарок должен быть поставлен в очередь на удаление");
            Assert.That(CountEntitiesWithPrototype(entManager, selected), Is.EqualTo(1),
                "После распаковки должно появиться ровно одно выбранное оружие");
        });
    }
}
