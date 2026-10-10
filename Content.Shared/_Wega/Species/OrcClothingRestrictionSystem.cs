using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._Wega.Species;

/// <summary>
/// Orcs need fitted EVA equipment; their fitted hardsuits cannot be worn by other species.
/// Adapted from Aavikko-2.0 (MIT), see Resources/_Wega/Orc/LICENSE.txt.
/// </summary>
public sealed partial class OrcClothingRestrictionSystem : EntitySystem
{
    [Dependency] private TagSystem _tags = default!;

    private static readonly ProtoId<TagPrototype> OrcHardsuitTag = "OrcHardsuit";
    private static readonly ProtoId<TagPrototype> HardsuitTag = "Hardsuit";
    private static readonly ProtoId<TagPrototype> EvaTag = "SuitEVA";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClothingComponent, BeingEquippedAttemptEvent>(OnEquipAttempt);
    }

    private void OnEquipAttempt(Entity<ClothingComponent> ent, ref BeingEquippedAttemptEvent args)
    {
        if (args.Cancelled || (args.Slot != "head" && args.Slot != "outerClothing") ||
            !TryComp<InventoryComponent>(args.EquipTarget, out var inventory))
            return;

        var orc = inventory.SpeciesId == "orc";
        var fitted = _tags.HasTag(ent.Owner, OrcHardsuitTag);
        if (orc && !fitted && (_tags.HasTag(ent.Owner, HardsuitTag) || _tags.HasTag(ent.Owner, EvaTag)))
        {
            args.Reason = "wega-orc-clothing-too-small";
            args.Cancel();
        }
        else if (!orc && fitted)
        {
            args.Reason = "wega-orc-clothing-too-large";
            args.Cancel();
        }
    }
}
