using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Client._Wega.Species;

/// <summary>
/// Uses Aavikko's Orc clothing variants without changing upstream clothing resources.
/// </summary>
public sealed partial class OrcClothingVisualsSystem : EntitySystem
{
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private ISerializationManager _serialization = default!;

    public void AdaptVisuals(Entity<ClothingComponent> ent, GetEquipmentVisualsEvent args)
    {
        if (!TryComp<InventoryComponent>(args.Equipee, out var inventory) || inventory.SpeciesId != "orc")
            return;

        for (var i = 0; i < args.Layers.Count; i++)
        {
            var (key, layer) = args.Layers[i];
            if (layer.State == null || layer.State.EndsWith("-orc"))
                continue;

            var sourcePath = layer.RsiPath ?? ent.Comp.RsiPath;
            if (sourcePath == null && TryComp<SpriteComponent>(ent, out var sprite))
                sourcePath = sprite.BaseRSI?.Path.ToString();

            if (sourcePath == null)
                continue;

            if (sourcePath.StartsWith("/Textures/"))
                sourcePath = sourcePath.Substring("/Textures/".Length);

            if (!sourcePath.StartsWith("Clothing/") && !sourcePath.StartsWith("Corvax/Clothing/"))
                continue;

            var variantPath = SpriteSpecifierSerializer.TextureRoot / ("_Wega/Clothing/Orc/" + sourcePath);
            var state = layer.State + "-orc";
            // Не меняем данные прототипа: тот же предмет может носить персонаж другой расы.
            PrototypeLayerData copy = new();
            _serialization.CopyTo(layer, ref copy, notNullableOverride: true);
            if (_cache.TryGetResource<RSIResource>(variantPath, out var resource) &&
                resource.RSI.TryGetState(state, out _))
            {
                copy.RsiPath = variantPath.ToString();
                copy.State = state;
            }
            // Для обычных состояний Inventory применит карту смещения при добавлении слоя.
            // Готовые состояния -orc автоматически пропускают этот этап.
            args.Layers[i] = (key, copy);
        }
    }
}
