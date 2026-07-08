namespace Content.Shared._Wega.Duel;

/// <summary>
/// Клеймо «Карателя» на бойце. Ставится первым попаданием, снимается детонацией (второй выстрел)
/// или по истечении времени. Пока висит — цель подсвечена. Управляется <c>ArenaPunisherSystem</c>.
/// </summary>
[RegisterComponent]
public sealed partial class BrandedComponent : Component
{
    /// <summary>Момент, когда клеймо само погаснет.</summary>
    [DataField]
    public TimeSpan EndTime;

    /// <summary>Кто поставил клеймо — ему засчитывается детонация.</summary>
    [DataField]
    public EntityUid? Source;

    /// <summary>Визуальный маркер клейма (красный прицел), следует за целью. Удаляется вместе с клеймом.</summary>
    [DataField]
    public EntityUid? Effect;
}
