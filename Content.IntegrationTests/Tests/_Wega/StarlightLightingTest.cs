using Content.IntegrationTests.Fixtures;
using Content.Server.Power.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Interaction;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Wires;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Wega;

[TestFixture]
public sealed class StarlightLightingTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [Test]
    public async Task EveryImportedEntityInitializes()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var entities = Server.EntMan;
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            var count = 0;
            foreach (var prototype in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (prototype.Abstract || !prototype.ID.StartsWith("WegaStarlight"))
                    continue;
                var entity = entities.SpawnEntity(prototype.ID, map.GridCoords);
                Assert.That(entities.EntityExists(entity), Is.True, prototype.ID);
                entities.DeleteEntity(entity);
                count++;
            }
            Assert.That(count, Is.GreaterThanOrEqualTo(40));
        });
    }

    [Test]
    public async Task FloorLimitsBrightnessAndProtectsCoveredBulb()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var entities = Server.EntMan;
            var floor = entities.SpawnEntity("WegaStarlightLightFloor", map.GridCoords);
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(floor).Powered = true;
            var lights = entities.System<SharedPoweredLightSystem>();
            lights.ToggleLight(floor);
            lights.ToggleLight(floor);
            var point = entities.GetComponent<PointLightComponent>(floor);
            Assert.That(point.Radius, Is.LessThanOrEqualTo(4f));
            Assert.That(point.Energy, Is.EqualTo(0.6f).Within(0.001f));
            var closed = new InteractHandEvent(user, floor);
            entities.EventBus.RaiseLocalEvent(floor, closed);
            Assert.That(closed.Handled, Is.True);
            Assert.That(entities.GetComponent<DamageOnInteractComponent>(floor).IsDamageActive, Is.False);
            var panel = entities.GetComponent<WiresPanelComponent>(floor);
            Assert.That(entities.System<SharedWiresSystem>().TogglePanel(floor, panel, true), Is.True);
            Assert.That(panel.Open, Is.True);
            Assert.That(entities.GetComponent<DamageOnInteractComponent>(floor).IsDamageActive, Is.True);
        });
    }
}
