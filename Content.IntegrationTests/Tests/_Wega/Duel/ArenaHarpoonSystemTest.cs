using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Wega.Duel.Systems;
using Content.Shared._Wega.Duel;
using Content.Shared.Interaction.Events;
using Content.Shared.Maps;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Wega.Duel;

[TestFixture]
[TestOf(typeof(ArenaHarpoonSystem))]
public sealed class ArenaHarpoonSystemTest : GameTest
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

    /// <summary>
    /// Потрошащий гарпун (reaper) переключает режим добивания по использованию в руке:
    /// срыв конечности ↔ обезглавливание.
    /// </summary>
    [Test]
    public async Task HarpoonReaperModeCyclesOnUseInHandTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid harpoon = default;
        EntityUid user = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var gridUid = testMap.Tile.GridUid;
            var coords = new EntityCoordinates(gridUid, testMap.Tile.GridIndices.X, testMap.Tile.GridIndices.Y);

            harpoon = entManager.SpawnEntity("WeaponArenaHarpoonReaper", coords);
            user = entManager.SpawnEntity("DuelTestDummy", coords.Offset(new System.Numerics.Vector2(1, 0)));

            var mode = entManager.GetComponent<ArenaHarpoonModeComponent>(harpoon);
            Assert.That(mode.Current, Is.EqualTo(ArenaHarpoonFinisher.Dismember), "Начальный режим потрошителя — срыв конечности");
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var mode = entManager.GetComponent<ArenaHarpoonModeComponent>(harpoon);

            var useEv = new UseInHandEvent(user);
            entManager.EventBus.RaiseLocalEvent(harpoon, useEv);

            Assert.That(useEv.Handled, Is.True, "Использование в руке должно быть обработано");
            Assert.That(mode.Current, Is.EqualTo(ArenaHarpoonFinisher.Behead), "После первого переключения режим — обезглавливание");

            useEv = new UseInHandEvent(user);
            entManager.EventBus.RaiseLocalEvent(harpoon, useEv);

            Assert.That(useEv.Handled, Is.True, "Повторное использование должно быть обработано");
            Assert.That(mode.Current, Is.EqualTo(ArenaHarpoonFinisher.Dismember), "После второго переключения режим возвращается к срыву конечности");
        });
    }

    /// <summary>
    /// Потрошащий гарпун добавляет пункт переключения режима в альтернативное контекстное меню.
    /// </summary>
    [Test]
    public async Task HarpoonReaperProvidesModeVerbTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = server.System<SharedMapSystem>();

        var testMap = await pair.CreateTestMap();

        EntityUid harpoon = default;
        EntityUid user = default;

        await server.WaitAssertion(() =>
        {
            ExpandGrid(mapSystem, testMap.Grid, testMap.Tile.Tile);

            var gridUid = testMap.Tile.GridUid;
            var coords = new EntityCoordinates(gridUid, testMap.Tile.GridIndices.X, testMap.Tile.GridIndices.Y);

            harpoon = entManager.SpawnEntity("WeaponArenaHarpoonReaper", coords);
            user = entManager.SpawnEntity("DuelTestDummy", coords.Offset(new System.Numerics.Vector2(1, 0)));
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var ev = new GetVerbsEvent<AlternativeVerb>(user, harpoon, harpoon, null, canInteract: true, canComplexInteract: true, canAccess: true, new List<VerbCategory>());
            entManager.EventBus.RaiseLocalEvent(harpoon, ev);

            Assert.That(ev.Verbs.Count, Is.GreaterThan(0), "Должен быть хотя бы один альтернативный глагол");
            Assert.That(ev.Verbs.Any(v => v.Text != null && v.Text.Contains(Loc.GetString("arena-harpoon-mode-behead"))),
                Is.True, "Среди глаголов должен быть переключатель режима");
        });
    }
}
