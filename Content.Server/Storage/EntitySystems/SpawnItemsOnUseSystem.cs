using Content.Server.Administration.Logs;
using Content.Server.Cargo.Systems;
using Content.Server.Storage.Components;
using Content.Shared.Cargo;
using Content.Shared.Clothing.Components;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;
using static Content.Shared.Storage.EntitySpawnCollection;

namespace Content.Server.Storage.EntitySystems
{
    public sealed partial class SpawnItemsOnUseSystem : EntitySystem
    {
        [Dependency] private IRobustRandom _random = default!;
        [Dependency] private IAdminLogManager _adminLogger = default!;
        [Dependency] private SharedHandsSystem _hands = default!;
        [Dependency] private PricingSystem _pricing = default!;
        [Dependency] private SharedAudioSystem _audio = default!;
        [Dependency] private SharedTransformSystem _transform = default!;
        [Dependency] private InventorySystem _inventory = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<SpawnItemsOnUseComponent, UseInHandEvent>(OnUseInHand);
            SubscribeLocalEvent<SpawnItemsOnUseComponent, PriceCalculationEvent>(CalculatePrice, before: new[] { typeof(PricingSystem) });
        }

        private void CalculatePrice(EntityUid uid, SpawnItemsOnUseComponent component, ref PriceCalculationEvent args)
        {
            var ungrouped = CollectOrGroups(component.Items, out var orGroups);

            foreach (var entry in ungrouped)
            {
                var protUid = Spawn(entry.PrototypeId, MapCoordinates.Nullspace);

                // Calculate the average price of the possible spawned items
                args.Price += _pricing.GetPrice(protUid) * entry.SpawnProbability * entry.GetAmount(getAverage: true);

                Del(protUid);
            }

            foreach (var group in orGroups)
            {
                foreach (var entry in group.Entries)
                {
                    var protUid = Spawn(entry.PrototypeId, MapCoordinates.Nullspace);

                    // Calculate the average price of the possible spawned items
                    args.Price += _pricing.GetPrice(protUid) *
                                  (entry.SpawnProbability / group.CumulativeProbability) *
                                  entry.GetAmount(getAverage: true);

                    Del(protUid);
                }
            }

            args.Handled = true;
        }

        private void OnUseInHand(EntityUid uid, SpawnItemsOnUseComponent component, UseInHandEvent args)
        {
            if (args.Handled)
                return;

            // If starting with zero or less uses, this component is a no-op
            if (component.Uses <= 0)
                return;

            var xform = Transform(args.User);
            var spawnEntities = GetSpawns(component.Items, _random);
            EntityUid? entityToPlaceInHands = null;
            EntityUid? weaponForHands = null;
            var spawned = new List<EntityUid>();

            foreach (var proto in spawnEntities)
            {
                var item = Spawn(proto, xform.Coordinates);
                spawned.Add(item);
                _adminLogger.Add(LogType.EntitySpawn, LogImpact.Low, $"{ToPrettyString(args.User)} used {ToPrettyString(uid)} which spawned {ToPrettyString(item)}");

                // Wega: auto-equip kits dress the user — clothing goes onto a matching empty slot and is
                // taken out of the hands/floor handling; everything else falls through as before.
                if (component.EquipToUser && TryAutoEquip(args.User, item))
                    continue;

                entityToPlaceInHands = item;

                // Wega: for an auto-equip kit, prefer to end up holding the first weapon rather than the last
                // leftover (a book/food) — the kit's clothing is already worn, so the gun/blade is what matters.
                if (component.EquipToUser && weaponForHands == null && IsWeapon(item))
                    weaponForHands = item;
            }

            // Wega: позволяем другим системам отреагировать на свежезаспавненное содержимое
            // (например, дуэльная арена пробрасывает свою метку «выдано ареной» на распакованные
            // предметы, чтобы они подчищались после боя — иначе пачка/зажигалка/гипопен из набора
            // остаются на карте).
            RaiseLocalEvent(uid, new SpawnItemsOnUsedEvent(spawned));

            // The entity is often deleted, so play the sound at its position rather than parenting
            if (component.Sound != null)
                _audio.PlayPvs(component.Sound, xform.Coordinates);

            component.Uses--;

            // Delete entity only if component was successfully used
            if (component.Uses <= 0)
            {
                // Don't delete the entity in the event bus, so we queue it for deletion.
                // We need the free hand for the new item, so we send it to nullspace.
                _transform.DetachEntity(uid, Transform(uid));
                QueueDel(uid);
            }

            var handItem = weaponForHands ?? entityToPlaceInHands;
            if (handItem != null)
                _hands.PickupOrDrop(args.User, handItem.Value);

            args.Handled = true;
        }

        /// <summary>
        ///     Wega: tries to put a freshly spawned clothing item onto the user in the first matching empty
        ///     inventory slot. Returns true if it was equipped. Non-clothing, no matching slot, or an already
        ///     occupied slot returns false so the caller falls back to hands/floor — the user is never stripped.
        /// </summary>
        private bool TryAutoEquip(EntityUid user, EntityUid item)
        {
            if (!TryComp<ClothingComponent>(item, out var clothing) || clothing.Slots == SlotFlags.NONE)
                return false;

            if (!_inventory.TryGetSlots(user, out var slots))
                return false;

            foreach (var slot in slots)
            {
                // Curated kit clothing belongs in its real slot, not stuffed into a pocket.
                if ((slot.SlotFlags & SlotFlags.POCKET) != 0)
                    continue;

                if ((clothing.Slots & slot.SlotFlags) == 0)
                    continue;

                // Only fill empty slots — never strip gear the duelist already wears.
                if (_inventory.TryGetSlotEntity(user, slot.Name, out _))
                    continue;

                if (_inventory.TryEquip(user, item, slot.Name, silent: true, force: true))
                    return true;
            }

            return false;
        }

        /// <summary>Wega: a usable weapon is one with a gun or a melee-weapon component.</summary>
        private bool IsWeapon(EntityUid uid)
            => HasComp<GunComponent>(uid) || HasComp<MeleeWeaponComponent>(uid);
    }

    /// <summary>
    /// Wega: направленное событие, поднимается на сущности-упаковке (<see cref="SpawnItemsOnUseComponent"/>)
    /// сразу после спавна её содержимого. Содержит список заспавненных сущностей.
    /// </summary>
    public sealed class SpawnItemsOnUsedEvent : EntityEventArgs
    {
        public readonly List<EntityUid> Spawned;

        public SpawnItemsOnUsedEvent(List<EntityUid> spawned)
        {
            Spawned = spawned;
        }
    }
}
