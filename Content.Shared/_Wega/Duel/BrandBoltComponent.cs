using Content.Shared.Damage;

namespace Content.Shared._Wega.Duel;

/// <summary>
/// Снаряд «Карателя». При попадании в бойца ставит <see cref="BrandedComponent"/> (клеймо).
/// Если цель уже заклеймена — детонирует клеймо: наносит <see cref="DetonateDamage"/> и
/// отбрасывает. Логику обрабатывает <c>ArenaPunisherSystem</c> на сервере.
/// </summary>
[RegisterComponent]
public sealed partial class BrandBoltComponent : Component
{
    /// <summary>Сколько секунд держится клеймо, если не детонировать.</summary>
    [DataField]
    public float BrandDuration = 5f;

    /// <summary>Доп. урон при детонации клейма (в упор второй выстрел по цели).</summary>
    [DataField]
    public DamageSpecifier DetonateDamage = new();

    /// <summary>Сила отбрасывания цели при детонации.</summary>
    [DataField]
    public float Knockback = 6f;
}
