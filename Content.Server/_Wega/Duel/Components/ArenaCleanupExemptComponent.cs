using Robust.Shared.GameObjects;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Маркер сущности, которую игрок вручную достал из спавн-меню (система placement). Такие предметы и
/// конструкции НЕ участвуют ни в очистке арены (<see cref="ArenaIssuedItemComponent"/> и блэнкет-проходы),
/// ни в её восстановлении к снимку: их не удаляют и не считают ни выданным снаряжением, ни декором карты.
/// Ставится на всю ветку (предмет + вложенное содержимое) в момент спавна из меню и остаётся навсегда.
/// </summary>
[RegisterComponent]
public sealed partial class ArenaCleanupExemptComponent : Component
{
}
