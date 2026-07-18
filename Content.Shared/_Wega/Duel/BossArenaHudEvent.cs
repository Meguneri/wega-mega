using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Wega.Duel;

/// <summary>
/// Состояние HUD-полоски ХП босса для участников босс-арены. Шлётся сервером при старте боя,
/// смене фазы, энрейдже и завершении, а также периодически во время боя (обновление полоски).
/// Обработчик на клиенте — BossArenaHudSystem.
/// </summary>
[Serializable, NetSerializable]
public sealed class BossArenaHudEvent : EntityEventArgs
{
    /// <summary>Бой идёт — полоску показывать; false — скрыть (остальные поля не важны).</summary>
    public bool Active;

    /// <summary>Имя босса для заголовка полоски.</summary>
    public string BossName = string.Empty;

    /// <summary>Доля здоровья босса (0..1).</summary>
    public float HealthRatio = 1f;

    /// <summary>Текущая фаза босса (0 — начальная).</summary>
    public int Phase;

    /// <summary>Босс в ярости (энрейдж).</summary>
    public bool Enraged;
}
