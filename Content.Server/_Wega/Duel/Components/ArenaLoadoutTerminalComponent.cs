using Content.Shared._Wega.Duel;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Терминал выбора набора снаряжения для дуэльной арены. Список доступных наборов задаётся
/// в прототипе; игрок может открыть UI и выбрать loadout, который получит при старте боя.
/// </summary>
[RegisterComponent]
public sealed partial class ArenaLoadoutTerminalComponent : Component
{
    [DataField("loadouts", required: true)]
    public List<ProtoId<ArenaLoadoutPrototype>> Loadouts = new();
}
