using Robust.Shared.GameObjects;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Маркер огнестрела, привязанного к боссу босс-арены: стрелять из него и поднимать его может
/// только сущность с <see cref="BossArenaBossComponent"/>. Попытки остальных отменяются в
/// BossArenaSystem с поясняющим попапом. Ни в какие пулы и лут такое оружие не входит.
/// </summary>
[RegisterComponent]
public sealed partial class BossArenaBoundGunComponent : Component
{
}
