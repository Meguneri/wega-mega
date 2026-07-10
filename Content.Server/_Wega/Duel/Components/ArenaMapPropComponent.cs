using Robust.Shared.GameObjects;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Маркер свободного предмета-декора арены (плюшевые игрушки, шахматы, безделушки, вывески…),
/// снятого в снимок при первом старте дуэли. По этой метке после раунда все экземпляры — включая
/// подобранные бойцами в инвентарь — удаляются, а затем исходный набор раскладывается заново.
/// Так предмет всегда возвращается на своё место и не плодит дубликаты, если его унесли за бой.
/// </summary>
[RegisterComponent]
public sealed partial class ArenaMapPropComponent : Component
{
}
