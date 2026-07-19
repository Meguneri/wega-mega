namespace Content.Server.Traitor.Uplink.SurplusBundle;

/// <summary>
///     Fill crate with a random uplink items.
/// </summary>
[RegisterComponent]
public sealed partial class SurplusBundleComponent : Component
{
    /// <summary>
    ///     Total price of all content inside bundle.
    /// </summary>
    [DataField]
    public int TotalPrice = 20;

    /// <summary>
    ///     Hard cap on the number of items generated, regardless of remaining budget.
    ///     Keeps the bundle from overflowing the crate's storage capacity (default 30):
    ///     extra items would otherwise fail to insert and spill onto the floor.
    /// </summary>
    [DataField]
    public int MaxItems = 30;

    /// <summary>
    ///     Maximum number of items allowed per store category (keyed by category ID string).
    ///     Items with any category that hits its limit are excluded from further picks.
    /// </summary>
    [DataField]
    public Dictionary<string, int> CategoryLimits = new();

    /// <summary>
    ///     Listing IDs that are never rolled into this bundle. Lets a single shared pool exclude
    ///     specific items from one crate (e.g. drop grenade launchers from the big chaos box only).
    /// </summary>
    [DataField]
    public HashSet<string> ExcludedListings = new();

    /// <summary>
    ///     Categories from which at least one item is guaranteed to be picked before the
    ///     regular random fill, as long as the budget allows. Used, for example, to ensure
    ///     a melee arena crate always contains at least one melee weapon.
    /// </summary>
    [DataField]
    public List<string> GuaranteedCategories = new();

    /// <summary>
    ///     Companion guarantees, keyed by trigger category -> companion category. If the rolled
    ///     bundle contains any listing from the trigger category but none from the companion
    ///     category, one random companion listing is added on top (even past the price budget).
    ///     Used so an injector (hypospray/hypopen) never drops without at least one chem bottle.
    /// </summary>
    [DataField]
    public Dictionary<string, string> CompanionCategories = new();

    /// <summary>
    ///     Optional spawn chance (0..1) for a companion guarantee, keyed by the same trigger
    ///     category as <see cref="CompanionCategories"/>. A trigger without an entry here always
    ///     adds its companion (chance 1.0); with an entry, the companion is only added on a random
    ///     roll. Used so a water gun only sometimes drops with a napalm/phlogiston refill bottle.
    /// </summary>
    [DataField]
    public Dictionary<string, float> CompanionChances = new();

    /// <summary>
    ///     Store category holding ammo listings (magazines, speedloaders, boxes). When a gun has
    ///     already been rolled into the bundle, ammo listings whose product carries a tag matching
    ///     one of the gun's magazine/chamber whitelist tags get their pick weight multiplied by
    ///     <see cref="AmmoAffinityMultiplier"/> — compatible ammo becomes much more likely.
    /// </summary>
    [DataField]
    public string? AmmoAffinityCategory;

    /// <summary>
    ///     Weight multiplier applied to ammo compatible with an already-rolled gun.
    /// </summary>
    [DataField]
    public double AmmoAffinityMultiplier = 4.0;

    /// <summary>
    ///     If true, item picks are weighted by cost (probability ∝ price^<see cref="CostWeightExponent"/>)
    ///     instead of uniform. This biases the bundle toward more expensive gear, so each opening yields a
    ///     capable loadout rather than a pile of cheap filler — used by the duel arena crates.
    ///     Vanilla surplus crates leave this false (uniform selection).
    /// </summary>
    [DataField]
    public bool WeightByCost;

    /// <summary>
    ///     Exponent applied to item price when <see cref="WeightByCost"/> is set: weight = price^exponent.
    ///     1.0 = linear (price-proportional, strongly favours the priciest items); 0.5 = square root
    ///     (gentle bias, keeps top-tier items a rare jackpot); 0 = uniform. Lower it for rarer big-ticket drops.
    /// </summary>
    [DataField]
    public double CostWeightExponent = 0.5;

    /// <summary>
    ///     If true, every item generated into this bundle is tagged with
    ///     <c>ArenaIssuedItemComponent</c>. Used by the duel arena crates so the arena cleanup
    ///     deletes only crate-issued gear, leaving players' own items and map props untouched.
    ///     Leave false for vanilla surplus crates.
    /// </summary>
    [DataField]
    public bool MarkIssuedItems;

    /// <summary>
    ///     If true, at least one usable weapon (a product with a <c>GunComponent</c> or
    ///     <c>MeleeWeaponComponent</c>) is guaranteed in the bundle before the regular fill, budget
    ///     permitting. Unlike <see cref="GuaranteedCategories"/> this works on the product's components,
    ///     so the huge Full Arsenal pool doesn't need every gun hand-tagged into a weapon category — it
    ///     just stops the "no weapon at all" openings.
    /// </summary>
    [DataField]
    public bool GuaranteedWeapon;

    /// <summary>
    ///     If true, at least one firearm (a product with a <c>GunComponent</c>) is guaranteed in the
    ///     bundle before the regular fill, budget permitting. <see cref="GuaranteedWeapon"/> alone lets
    ///     the box roll melee-only (~1 opening in 5 for the Full Arsenal pool); this stops that.
    /// </summary>
    [DataField]
    public bool GuaranteedGun;

    /// <summary>
    ///     Guarantee at least this many items costing at least <see cref="GuaranteedValueThreshold"/> TC,
    ///     picked before the regular fill (budget permitting). Stops a box from rolling entirely into cheap
    ///     filler (food, bags, 1-TC trinkets) and leaving the buyer with nothing of substance.
    /// </summary>
    [DataField]
    public int GuaranteedValueCount;

    /// <summary>Minimum cost (TC) for an item to count toward <see cref="GuaranteedValueCount"/>.</summary>
    [DataField]
    public int GuaranteedValueThreshold = 5;

    /// <summary>
    ///     Per-crate cost overrides, keyed by listing ID -> TC cost. Replaces the listing's pool price for the
    ///     purposes of this bundle only (selection weighting and budget spend) — the shared pool price is left
    ///     untouched for every other crate. Lets a single item be cheap in small crates but a pricey jackpot in
    ///     a big-budget one (e.g. the deathsquad hardsuit costs 35 TC normally, 80 TC in the 120-TC mega crate).
    /// </summary>
    [DataField]
    public Dictionary<string, int> CostOverrides = new();

    /// <summary>
    ///     Optional explicit pick-weight per listing ID, overriding the cost-based weight from
    ///     <see cref="WeightByCost"/>. Decouples "how much budget an item eats" from "how often it is rolled":
    ///     a <see cref="CostOverrides"/> entry that raises an item's price to make it a budget sink would
    ///     otherwise also raise its cost-based weight (price^exponent), making the supposed jackpot MORE likely.
    ///     Set a low weight here (e.g. 0.5) so the item stays a genuinely rare drop while still costing its
    ///     full overridden price when it does appear. Ammo-affinity multipliers still apply on top.
    /// </summary>
    [DataField]
    public Dictionary<string, double> WeightOverrides = new();

    /// <summary>
    ///     If true, listings in <see cref="AmmoAffinityCategory"/> are only rolled once a compatible gun is
    ///     already in the bundle (matched by the gun's magazine/chamber tags). Stops "orphan" ammo — boxes
    ///     of magazines for a gun that never dropped.
    /// </summary>
    [DataField]
    public bool RequireGunForAmmo;
}
