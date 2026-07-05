using Robust.Shared.GameObjects;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Кнопка готовности к дуэли. Нажатие (ActivateInWorld) помечает эту кнопку готовой на её арене
/// (готовность хранится на трекере <see cref="DuelArenaComponent"/>, привязка по гриду; счёт — по
/// кнопкам, по одной на базу). Когда нажаты все кнопки арены (минимум 2) — кнопка программно дёргает
/// порт <see cref="StartPort"/> (DuelStart через AutoLink), запуская штатную цепочку старта: таймер →
/// DuelFight → барьеры/колокол/ArmDuel. Над кнопкой соперника висит голограмма «ГОТОВ» (см.
/// <see cref="DuelArenaComponent.ReadyHologram"/>). Логика — в <see cref="Systems.DuelReadySystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class DuelReadyButtonComponent : Component
{
    /// <summary>
    /// Порт device-link, который дёргается, когда готовы все. Должен совпадать с источником,
    /// связанным AutoLink с таймером дуэли (по умолчанию Pressed → канал DuelStart).
    /// </summary>
    [DataField]
    public string StartPort = "Pressed";
}
