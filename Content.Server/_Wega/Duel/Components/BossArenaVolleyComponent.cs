using Robust.Shared.GameObjects;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Дисциплина огнестрела босса: стреляет ОЧЕРЕДЯМИ по 3–5 выстрелов, затем уходит на кулдаун
/// в ближний бой. Готовность новой очереди проверяет HTN-ветка дальнего боя через
/// <c>BossVolleyReadyPrecondition</c>; отсчёт выстрелов ведёт BossArenaSystem по GunShotEvent.
/// </summary>
[RegisterComponent]
public sealed partial class BossArenaVolleyComponent : Component
{
    /// <summary>Минимум выстрелов в одной очереди.</summary>
    [DataField]
    public int VolleyShotsMin = 3;

    /// <summary>Максимум выстрелов в одной очереди.</summary>
    [DataField]
    public int VolleyShotsMax = 5;

    /// <summary>Кулдаун между очередями (секунды) — всё это время босс дерётся в ближнем бою.</summary>
    [DataField]
    public float VolleyCooldown = 10f;

    /// <summary>Осталось выстрелов в текущей очереди. 0 — очереди нет (кулдаун или ожидание).</summary>
    [ViewVariables]
    public int ShotsRemaining;

    /// <summary>Когда разрешена следующая очередь. null — очередь доступна сразу.</summary>
    [ViewVariables]
    public TimeSpan? NextVolleyAt;
}
