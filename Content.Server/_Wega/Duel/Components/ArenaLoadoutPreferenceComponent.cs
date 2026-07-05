using Content.Shared._Wega.Duel;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Хранит выбранный игроком набор для дуэльной арены. Вешается на бойца при первом использовании
/// терминала выбора набора и используется ArenaLoadoutSystem при старте боя.
/// </summary>
[RegisterComponent]
public sealed partial class ArenaLoadoutPreferenceComponent : Component
{
    [DataField]
    public ProtoId<ArenaLoadoutPrototype>? SelectedLoadout;
}
