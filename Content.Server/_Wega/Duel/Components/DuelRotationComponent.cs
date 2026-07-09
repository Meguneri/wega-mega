using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Невидимый контроллер арена-ротации. Опциональная надстройка над обычной дуэлью: если его на
/// карте НЕТ — режим работает по-старому (одна арена, свой счёт в <see cref="DuelArenaComponent"/>).
/// Если ЕСТЬ — он держит общий счёт по всем аренам и между раундами переносит бойцов с одной
/// карты-арены на другую (случайно, без повтора подряд).
///
/// Каждая арена — отдельная карта (<see cref="Arenas"/>); все они предзагружаются при инициализации,
/// чтобы переход между раундами шёл без загрузочных лагов. Точки появления на каждой арене задаются
/// маркерами <see cref="DuelArenaSpawnComponent"/> прямо в маппинге.
/// </summary>
[RegisterComponent]
public sealed partial class DuelRotationComponent : Component, IDuelScoreStore
{
    Dictionary<NetUserId, int> IDuelScoreStore.Scores => Scores;
    Dictionary<NetUserId, string> IDuelScoreStore.ScoreNames => ScoreNames;
    Dictionary<NetUserId, int> IDuelScoreStore.LosingStreaks => LosingStreaks;
    NetUserId? IDuelScoreStore.StreakUser { get => StreakUser; set => StreakUser = value; }
    int IDuelScoreStore.Streak { get => Streak; set => Streak = value; }

    /// <summary>
    /// Список карт-арен для ротации (пути к .yml в Resources). Добавить арену = дописать строку.
    /// </summary>
    [DataField(required: true)]
    public List<ResPath> Arenas = new();

    /// <summary>
    /// Карты-арены загружаются один раз при инициализации (а не каждый раунд) — чтобы переход
    /// не вызывал фриз. true после успешной предзагрузки.
    /// </summary>
    public bool Loaded;

    /// <summary>
    /// Загруженные арены: индекс в <see cref="Arenas"/> → MapId загруженной карты. Заполняется
    /// при предзагрузке. По нему контроллер находит грид арены и её спавн-маркеры.
    /// </summary>
    public readonly Dictionary<int, MapId> LoadedArenas = new();

    /// <summary>
    /// Индекс арены, на которой идёт (или только что шёл) текущий бой. -1 — бой ещё не начинался.
    /// Используется, чтобы не выбрать ту же арену два раунда подряд.
    /// </summary>
    public int CurrentArena = -1;

    /// <summary>
    /// Запомненный спавн каждого игрока (NetUserId → <see cref="DuelArenaSpawnComponent.SpawnIndex"/>),
    /// выбранный персональной кнопкой входа. При переходе между раундами боец возвращается на свой угол,
    /// а не раскидывается по маркерам в порядке списка (иначе после дуэли спавнило бы на чужой спавн).
    /// </summary>
    public readonly Dictionary<NetUserId, int> PreferredSpawns = new();

    // --- Общий счёт по всем аренам (та же схема, что в DuelArenaComponent, но единый на ротацию) ---

    /// <summary>
    /// Накопительный счёт побед по игрокам (NetUserId → число побед). Ключ — игрок, а не тело:
    /// счёт переживает клон/респавн и смену арены.
    /// </summary>
    public readonly Dictionary<NetUserId, int> Scores = new();

    /// <summary>
    /// Последнее известное имя игрока (NetUserId → имя) для отображения общего счёта.
    /// </summary>
    public readonly Dictionary<NetUserId, string> ScoreNames = new();

    /// <summary>
    /// Текущая серия поражений по игрокам (NetUserId → число поражений подряд).
    /// </summary>
    public readonly Dictionary<NetUserId, int> LosingStreaks = new();

    /// <summary>
    /// Игрок, выигравший прошлый раунд, и его текущая серия побед подряд (сквозь арены).
    /// </summary>
    public NetUserId? StreakUser;

    public int Streak;
}
