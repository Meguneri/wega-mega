using Robust.Shared.Maths;

namespace Content.Server._Wega.SunlightSwitch;

/// <summary>
/// Настенный выключатель «солнца»: управляет ambient-светом всей карты,
/// на которой находится. При маппинге включает дневной свет автоматически.
/// </summary>
[RegisterComponent]
public sealed partial class SunlightSwitchComponent : Component
{
    /// <summary>Включено ли «солнце» сейчас (и при старте карты).</summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>Цвет дневного света.</summary>
    [DataField]
    public Color DayColor = Color.FromHex("#FFF2D8");

    /// <summary>Цвет при выключенном солнце (тьма — светят только лампы).</summary>
    [DataField]
    public Color NightColor = Color.FromSrgb(Color.Black);
}
