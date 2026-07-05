using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Компонент пульта управления арест-ботом. При использовании на цели ищет ближайшего бота и отдаёт ему приказ.
/// </summary>
[RegisterComponent]
public sealed partial class ArrestBotRemoteComponent : Component
{
    /// <summary>
    /// Радиус поиска бота.
    /// </summary>
    [DataField]
    public float BotSearchRange = 30f;
}
