using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Wega.Duel;

/// <summary>
/// Косметически выбрасывает гильзу при каждом выстреле — даже у энергооружия, ради «фидбека».
/// Обрабатывается <c>CasingEjectSystem</c> на сервере.
/// </summary>
[RegisterComponent]
public sealed partial class CasingEjectOnShotComponent : Component
{
    /// <summary>Что вылетает при выстреле.</summary>
    [DataField]
    public EntProtoId Casing = "CartridgePistolSpent";

    /// <summary>Звук падения гильзы.</summary>
    [DataField]
    public SoundSpecifier? Sound = new SoundCollectionSpecifier("CasingEject");

    /// <summary>Сила выброса гильзы.</summary>
    [DataField]
    public float Speed = 1.5f;
}
