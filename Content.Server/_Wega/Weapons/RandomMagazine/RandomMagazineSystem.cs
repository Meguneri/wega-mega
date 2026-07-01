using Content.Shared._Wega.Weapons.RandomMagazine;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Random;

namespace Content.Server._Wega.Weapons.RandomMagazine;

public sealed partial class RandomMagazineSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RandomMagazineComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<RandomMagazineComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Magazines.Count == 0)
            return;

        var proto = _random.Pick(ent.Comp.Magazines);

        if (!_slots.TryGetSlot(ent.Owner, SharedGunSystem.MagazineSlot, out var slot))
            return;

        // Remove whatever startingItem put in there. QueueDel alone leaves the item in the slot this
        // tick, so the insert below would hit CanInsert's HasItem guard and drop the new magazine on
        // the floor — eject first to free the slot synchronously, then delete the old magazine.
        if (slot.Item is { } existing)
        {
            _slots.TryEject(ent.Owner, slot, null, out _, excludeUserAudio: true);
            QueueDel(existing);
        }

        var mag = Spawn(proto, Transform(ent.Owner).Coordinates);
        _slots.TryInsert(ent.Owner, slot, mag, null);
    }
}
