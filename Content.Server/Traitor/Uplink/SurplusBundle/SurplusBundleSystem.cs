using System;
using System.Linq;
using Content.Server._Wega.Duel.Components;
using Content.Server._Wega.Duel.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Server.Store.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Content.Shared.Tag;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Traitor.Uplink.SurplusBundle;

public sealed partial class SurplusBundleSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private EntityStorageSystem _entityStorage = default!;
    [Dependency] private StoreSystem _store = default!;
    [Dependency] private DuelArenaCleanupSystem _cleanup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SurplusBundleComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, SurplusBundleComponent component, MapInitEvent args)
    {
        if (!TryComp<StoreComponent>(uid, out var store))
            return;

        FillStorage((uid, component, store));
    }

    private void FillStorage(Entity<SurplusBundleComponent, StoreComponent> ent)
    {
        var cords = Transform(ent).Coordinates;
        var content = GetRandomContent(ent);

        // Tag arena-issued gear so the duel cleanup only removes what the crate gave out.
        // MarkIssuedItems is set exclusively on the dedicated duel arena crates, which are
        // bought from the uplink BEFORE the duelists arm the fight — so we tag on fill rather
        // than gating on an active duel (the crate is filled while the duel isn't running yet).
        var shouldTag = ent.Comp1.MarkIssuedItems;

        foreach (var item in content)
        {
            var dode = Spawn(item.ProductEntity, cords);

            if (shouldTag)
                _cleanup.MarkIssuedRecursive(dode);

            _entityStorage.Insert(dode, ent);
        }
    }

    private List<ListingData> GetRandomContent(Entity<SurplusBundleComponent, StoreComponent> ent)
    {
        var ret = new List<ListingData>();
        var categoryCounts = new Dictionary<string, int>();

        var listings = _store.GetAvailableListings(ent, null, ent.Comp2.Categories)
            .Where(p => !ent.Comp1.ExcludedListings.Contains(p.ID))
            .OrderBy(p => EffectiveCost(p, ent.Comp1))
            .ToList();

        if (listings.Count == 0)
            return ret;

        var totalCost = FixedPoint2.Zero;

        // Guaranteed picks: ensure at least one item from each guaranteed category (budget permitting).
        foreach (var category in ent.Comp1.GuaranteedCategories)
        {
            if (ret.Count >= ent.Comp1.MaxItems)
                break;

            var remainingBudget = ent.Comp1.TotalPrice - totalCost;

            var eligible = listings
                .Where(l => l.Categories.Any(c => c.Id == category) &&
                            EffectiveCost(l, ent.Comp1) <= remainingBudget &&
                            !ExceedsCategoryLimit(l, ent.Comp1.CategoryLimits, categoryCounts))
                .ToList();

            if (eligible.Count == 0)
                continue;

            var guaranteed = PickItem(eligible, ent.Comp1);
            ret.Add(guaranteed);
            totalCost += EffectiveCost(guaranteed, ent.Comp1);

            foreach (var cat in guaranteed.Categories)
                categoryCounts[cat.Id] = categoryCounts.GetValueOrDefault(cat.Id) + 1;

            listings.Remove(guaranteed);
        }

        // Guaranteed gun: a chaos crate bought for a shooting duel shouldn't roll melee-only. Picked
        // before the weapon guarantee (a gun satisfies it too) so the ammo affinity sees the gun early.
        if (ent.Comp1.GuaranteedGun && !ret.Any(l => IsGunProduct(l)))
        {
            var remainingBudget = ent.Comp1.TotalPrice - totalCost;

            var eligible = listings
                .Where(l => IsGunProduct(l) &&
                            EffectiveCost(l, ent.Comp1) <= remainingBudget &&
                            !ExceedsCategoryLimit(l, ent.Comp1.CategoryLimits, categoryCounts))
                .ToList();

            if (eligible.Count > 0)
            {
                var gun = PickItem(eligible, ent.Comp1);
                ret.Add(gun);
                totalCost += EffectiveCost(gun, ent.Comp1);
                foreach (var cat in gun.Categories)
                    categoryCounts[cat.Id] = categoryCounts.GetValueOrDefault(cat.Id) + 1;
                listings.Remove(gun);
            }
        }

        // Guaranteed weapon: every box should contain at least one thing to fight with. Picked early so a
        // following compatible-ammo affinity (and the orphan-ammo gate below) can see the rolled gun.
        if (ent.Comp1.GuaranteedWeapon && !ret.Any(l => IsWeaponProduct(l)))
        {
            var remainingBudget = ent.Comp1.TotalPrice - totalCost;

            var eligible = listings
                .Where(l => IsWeaponProduct(l) &&
                            EffectiveCost(l, ent.Comp1) <= remainingBudget &&
                            !ExceedsCategoryLimit(l, ent.Comp1.CategoryLimits, categoryCounts))
                .ToList();

            if (eligible.Count > 0)
            {
                var weapon = PickItem(eligible, ent.Comp1);
                ret.Add(weapon);
                totalCost += EffectiveCost(weapon, ent.Comp1);
                foreach (var cat in weapon.Categories)
                    categoryCounts[cat.Id] = categoryCounts.GetValueOrDefault(cat.Id) + 1;
                listings.Remove(weapon);
            }
        }

        // Guaranteed value: force a few substantial items so the box can't roll entirely into cheap filler.
        for (var i = 0; i < ent.Comp1.GuaranteedValueCount && ret.Count < ent.Comp1.MaxItems; i++)
        {
            var remainingBudget = ent.Comp1.TotalPrice - totalCost;

            var eligible = listings
                .Where(l => EffectiveCost(l, ent.Comp1) >= ent.Comp1.GuaranteedValueThreshold &&
                            EffectiveCost(l, ent.Comp1) <= remainingBudget &&
                            IsEligibleAmmo(l, ent.Comp1, ret) &&
                            !ExceedsCategoryLimit(l, ent.Comp1.CategoryLimits, categoryCounts))
                .ToList();

            if (eligible.Count == 0)
                break;

            var pricey = PickItem(eligible, ent.Comp1, GetWantedAmmoTags(ret));
            ret.Add(pricey);
            totalCost += EffectiveCost(pricey, ent.Comp1);
            foreach (var cat in pricey.Categories)
                categoryCounts[cat.Id] = categoryCounts.GetValueOrDefault(cat.Id) + 1;
            listings.Remove(pricey);
        }

        while (totalCost < ent.Comp1.TotalPrice && ret.Count < ent.Comp1.MaxItems)
        {
            var remainingBudget = ent.Comp1.TotalPrice - totalCost;

            // Eligible: within budget, not over any category limit, and — if RequireGunForAmmo is set —
            // not orphan ammo (ammo for a gun the bundle doesn't contain).
            var eligible = listings
                .Where(l => EffectiveCost(l, ent.Comp1) <= remainingBudget &&
                            IsEligibleAmmo(l, ent.Comp1, ret) &&
                            !ExceedsCategoryLimit(l, ent.Comp1.CategoryLimits, categoryCounts))
                .ToList();

            if (eligible.Count == 0)
                break;

            var pick = PickItem(eligible, ent.Comp1, GetWantedAmmoTags(ret));
            ret.Add(pick);
            totalCost += EffectiveCost(pick, ent.Comp1);

            foreach (var cat in pick.Categories)
                categoryCounts[cat.Id] = categoryCounts.GetValueOrDefault(cat.Id) + 1;

            listings.Remove(pick);
        }

        // Companion guarantees: e.g. an injector must always come with at least one chem bottle.
        // Added on top even if the budget is already spent — the guarantee beats the price cap.
        foreach (var (trigger, companion) in ent.Comp1.CompanionCategories)
        {
            if (!ret.Any(l => l.Categories.Any(c => c.Id == trigger)))
                continue;

            if (ret.Any(l => l.Categories.Any(c => c.Id == companion)))
                continue;

            // Optional chance: a trigger listed in CompanionChances only spawns its companion on a
            // successful roll; otherwise the guarantee is unconditional (chance 1.0).
            if (ent.Comp1.CompanionChances.TryGetValue(trigger, out var chance) && !_random.Prob(chance))
                continue;

            var companions = listings
                .Where(l => l.Categories.Any(c => c.Id == companion))
                .ToList();

            if (companions.Count == 0)
                continue;

            ret.Add(PickItem(companions, ent.Comp1));
        }

        return ret;
    }

    /// <summary>
    ///     Picks one listing from the eligible set. Uniform by default; if the bundle has
    ///     <see cref="SurplusBundleComponent.WeightByCost"/> set, probability is weighted by
    ///     price^<see cref="SurplusBundleComponent.CostWeightExponent"/> so pricier gear is more likely
    ///     (gently, by default), while top-tier items stay a rare jackpot.
    /// </summary>
    private ListingDataWithCostModifiers PickItem(List<ListingDataWithCostModifiers> eligible, SurplusBundleComponent comp, HashSet<string>? wantedAmmoTags = null)
    {
        var total = 0.0;
        foreach (var listing in eligible)
            total += Weight(listing, comp, wantedAmmoTags);

        var roll = _random.NextDouble() * total;
        foreach (var listing in eligible)
        {
            roll -= Weight(listing, comp, wantedAmmoTags);
            if (roll <= 0.0)
                return listing;
        }

        return eligible[^1];
    }

    private double Weight(ListingDataWithCostModifiers listing, SurplusBundleComponent comp, HashSet<string>? wantedAmmoTags)
    {
        // An explicit per-listing weight override wins over the cost-based weight: it lets a budget-sink
        // jackpot (priced up via CostOverrides so it eats the budget) stay a rare drop instead of a likely
        // one, since cost-based weight would otherwise make the priciest item the most probable pick.
        var weight = comp.WeightOverrides.TryGetValue(listing.ID, out var weightOverride)
            ? weightOverride
            : comp.WeightByCost
                ? Math.Pow(Math.Max(1.0, EffectiveCost(listing, comp).Double()), comp.CostWeightExponent)
                : 1.0;

        if (wantedAmmoTags is { Count: > 0 }
            && comp.AmmoAffinityCategory is { } ammoCategory
            && listing.Categories.Any(c => c.Id == ammoCategory)
            && ListingHasAnyTag(listing, wantedAmmoTags))
        {
            weight *= comp.AmmoAffinityMultiplier;
        }

        return weight;
    }

    /// <summary>
    ///     Collects magazine/chamber whitelist tags from every gun already rolled into the bundle.
    ///     Ammo whose product carries one of these tags is "compatible" for affinity weighting.
    /// </summary>
    private HashSet<string> GetWantedAmmoTags(List<ListingData> picked)
    {
        var wanted = new HashSet<string>();

        foreach (var listing in picked)
        {
            if (listing.ProductEntity is not { } productId
                || !_prototype.TryIndex<EntityPrototype>(productId, out var proto)
                || !proto.TryGetComponent<GunComponent>(out _, EntityManager.ComponentFactory))
            {
                continue;
            }

            if (proto.TryGetComponent<ItemSlotsComponent>(out var slots, EntityManager.ComponentFactory))
            {
                foreach (var slot in slots.Slots.Values)
                {
                    if (slot.Whitelist?.Tags is not { } tags)
                        continue;

                    foreach (var tag in tags)
                        wanted.Add(tag.Id);
                }
            }

            if (proto.TryGetComponent<BallisticAmmoProviderComponent>(out var ballistic, EntityManager.ComponentFactory)
                && ballistic.Whitelist?.Tags is { } ballisticTags)
            {
                foreach (var tag in ballisticTags)
                    wanted.Add(tag.Id);
            }
        }

        return wanted;
    }

    /// <summary>
    ///     The cost this bundle treats a listing as having: a per-crate <see cref="SurplusBundleComponent.CostOverrides"/>
    ///     entry if present, otherwise the listing's shared pool price. Only affects this crate's selection and
    ///     budget — the listing's real price elsewhere is unchanged.
    /// </summary>
    private FixedPoint2 EffectiveCost(ListingDataWithCostModifiers listing, SurplusBundleComponent comp)
    {
        return comp.CostOverrides.TryGetValue(listing.ID, out var tc)
            ? tc
            : listing.Cost.Values.Sum();
    }

    /// <summary>True if the listing's product is a usable weapon — has a gun or a melee-weapon component.</summary>
    private bool IsWeaponProduct(ListingData listing)
    {
        if (listing.ProductEntity is not { } productId
            || !_prototype.TryIndex<EntityPrototype>(productId, out var proto))
        {
            return false;
        }

        return proto.TryGetComponent<GunComponent>(out _, EntityManager.ComponentFactory)
            || proto.TryGetComponent<MeleeWeaponComponent>(out _, EntityManager.ComponentFactory);
    }

    /// <summary>True if the listing's product is a firearm — has a gun component.</summary>
    private bool IsGunProduct(ListingData listing)
    {
        return listing.ProductEntity is { } productId
            && _prototype.TryIndex<EntityPrototype>(productId, out var proto)
            && proto.TryGetComponent<GunComponent>(out _, EntityManager.ComponentFactory);
    }

    /// <summary>
    ///     Gate for orphan ammo. When <see cref="SurplusBundleComponent.RequireGunForAmmo"/> is set, a
    ///     listing in the ammo affinity category is only eligible once a compatible gun (matched by its
    ///     magazine/chamber tags) is already in the bundle. Non-ammo listings always pass.
    /// </summary>
    private bool IsEligibleAmmo(ListingData listing, SurplusBundleComponent comp, List<ListingData> picked)
    {
        if (!comp.RequireGunForAmmo || comp.AmmoAffinityCategory is not { } ammoCategory)
            return true;

        if (!listing.Categories.Any(c => c.Id == ammoCategory))
            return true;

        var wanted = GetWantedAmmoTags(picked);
        return wanted.Count > 0 && ListingHasAnyTag(listing, wanted);
    }

    private bool ListingHasAnyTag(ListingData listing, HashSet<string> wanted)
    {
        if (listing.ProductEntity is not { } productId
            || !_prototype.TryIndex<EntityPrototype>(productId, out var proto))
        {
            return false;
        }

        if (PrototypeHasAnyTag(proto, wanted))
            return true;

        // Loose-cartridge boxes carry no magazine tag themselves — match by the tags of
        // the cartridge prototype they are filled with (e.g. CartridgeLightRifle).
        if (proto.TryGetComponent<BallisticAmmoProviderComponent>(out var ballistic, EntityManager.ComponentFactory)
            && ballistic.Proto is { } cartridgeId
            && _prototype.TryIndex<EntityPrototype>(cartridgeId, out var cartridgeProto))
        {
            return PrototypeHasAnyTag(cartridgeProto, wanted);
        }

        return false;
    }

    private bool PrototypeHasAnyTag(EntityPrototype proto, HashSet<string> wanted)
    {
        if (!proto.TryGetComponent<TagComponent>(out var tagComp, EntityManager.ComponentFactory))
            return false;

        foreach (var tag in tagComp.Tags)
        {
            if (wanted.Contains(tag.Id))
                return true;
        }

        return false;
    }

    private static bool ExceedsCategoryLimit(
        ListingData listing,
        Dictionary<string, int> limits,
        Dictionary<string, int> counts)
    {
        foreach (var cat in listing.Categories)
        {
            if (limits.TryGetValue(cat.Id, out var limit) && counts.GetValueOrDefault(cat.Id) >= limit)
                return true;
        }
        return false;
    }
}
