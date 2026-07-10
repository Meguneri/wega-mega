using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Пульт, задающий тир арсенал-крейта на всех аренах (см. <see cref="DuelArenaComponent.ArsenalCrate"/>).
/// Окно предлагает тиры из <see cref="Crates"/> плюс вариант «отключить». Выбор проверяется на сервере
/// по этому списку — клиентский список только визуальный.
/// </summary>
[RegisterComponent]
public sealed partial class ArenaArsenalRemoteComponent : Component
{
    /// <summary>Выбираемые тиры — прототипы арсенал-крейтов (SurplusBundle).</summary>
    [DataField]
    public List<EntProtoId> Crates = new();
}
