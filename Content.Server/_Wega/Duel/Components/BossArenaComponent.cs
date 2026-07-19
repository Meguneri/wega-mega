using Robust.Shared.Audio;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Трекер босс-арены PvE. По сигналу старта собирает игроков в зоне, телепортирует их в арену,
/// спавнит босса и отслеживает его фазы. По смерти босса выдаёт награду, по поражении участников — сбрасывается.
/// </summary>
[RegisterComponent]
public sealed partial class BossArenaComponent : Component, IDuelScoreStore
{
    Dictionary<NetUserId, int> IDuelScoreStore.Scores => Scores;
    Dictionary<NetUserId, string> IDuelScoreStore.ScoreNames => ScoreNames;
    Dictionary<NetUserId, int> IDuelScoreStore.LosingStreaks => LosingStreaks;
    NetUserId? IDuelScoreStore.StreakUser { get => StreakUser; set => StreakUser = value; }
    int IDuelScoreStore.Streak { get => Streak; set => Streak = value; }

    /// <summary>Полностью обнуляет накопленный счёт (см. <see cref="IDuelScoreStore.Reset"/>).</summary>
    public void Reset()
    {
        Scores.Clear();
        ScoreNames.Clear();
        LosingStreaks.Clear();
        StreakUser = null;
        Streak = 0;
    }

    /// <summary>
    /// Победы по игрокам (NetUserId → число побед в босс-арене).
    /// </summary>
    public readonly Dictionary<NetUserId, int> Scores = new();

    /// <summary>
    /// Последние известные имена игроков (NetUserId → имя) для табло.
    /// </summary>
    public readonly Dictionary<NetUserId, string> ScoreNames = new();

    /// <summary>
    /// Текущая серия поражений по игрокам (NetUserId → число поражений подряд).
    /// </summary>
    public readonly Dictionary<NetUserId, int> LosingStreaks = new();

    /// <summary>
    /// Игрок текущей серии побед подряд.
    /// </summary>
    public NetUserId? StreakUser;

    /// <summary>
    /// Длина текущей серии побед подряд.
    /// </summary>
    public int Streak;
    /// <summary>
    /// Радиус, в котором ищутся участники (запасной, если трекер не на гриде).
    /// </summary>
    [DataField]
    public float ScanRange = 200f;

    /// <summary>
    /// Как часто сканируется зона на наличие живых участников.
    /// </summary>
    [DataField]
    public float ScanInterval = 0.5f;

    /// <summary>
    /// Выходной порт, на который шлётся сигнал при завершении арены.
    /// </summary>
    [DataField]
    public string ResetPort = "BossArenaEnded";

    /// <summary>
    /// Через сколько секунд после завершения арены трекер пошлёт сигнал на закрытие шлюзов.
    /// </summary>
    [DataField]
    public float ReturnGrace = 20f;

    /// <summary>
    /// Максимальная длительность боя (в секундах). 0 — не ограничена.
    /// По истечении босс впадает в ярость (энрейдж), а бой продолжается до вайпа.
    /// </summary>
    [DataField]
    public float MaxFightDuration = 0f;

    // ── Скейлинг по числу участников ─────────────────────────────────────────────

    /// <summary>
    /// Базовый множитель ХП босса: итоговый = <see cref="HealthScaleBase"/> +
    /// <see cref="HealthScalePerParticipant"/> × число участников. При связке 0.75 + 0.25N
    /// одиночный боец получает базового босса (×1.0), двое — ×1.25, четверо — ×1.75.
    /// </summary>
    [DataField]
    public float HealthScaleBase = 0.75f;

    /// <summary>Множитель ХП босса за каждого участника (см. <see cref="HealthScaleBase"/>).</summary>
    [DataField]
    public float HealthScalePerParticipant = 0.25f;

    /// <summary>
    /// Бонус к урону босса за каждого участника СВЕРХ первого: итоговый множитель =
    /// 1 + <see cref="DamageScalePerParticipant"/> × (N − 1). Двое — ×1.1, четверо — ×1.3.
    /// </summary>
    [DataField]
    public float DamageScalePerParticipant = 0.1f;

    // ── Анти-кайт ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Дистанция (в тайлах), дальше которой ВСЕ живые участники считаются «кайтящими»:
    /// если это держится дольше <see cref="AntiKiteGraceSeconds"/>, босс начинает регенерировать.
    /// </summary>
    [DataField]
    public float AntiKiteRange = 12f;

    /// <summary>Сколько секунд непрерывного кайта до включения регенерации босса.</summary>
    [DataField]
    public float AntiKiteGraceSeconds = 10f;

    /// <summary>Скорость регенерации босса (ХП/сек) при активном анти-кайте. 0 — отключено.</summary>
    [DataField]
    public float AntiKiteRegenPerSecond = 20f;

    // ── Энрейдж ──────────────────────────────────────────────────────────────────

    /// <summary>Множитель скорости босса в ярости (поверх фазовых).</summary>
    [DataField]
    public float EnrageSpeedMultiplier = 2f;

    /// <summary>Множитель урона босса в ярости (поверх фазовых).</summary>
    [DataField]
    public float EnrageDamageMultiplier = 2f;

    /// <summary>
    /// Прототип босса, который спавнится в центре арены.
    /// </summary>
    [DataField]
    public EntProtoId? BossPrototype;

    /// <summary>
    /// Прототип награды, выпадающей при смерти босса.
    /// </summary>
    [DataField]
    public EntProtoId? RewardPrototype;

    /// <summary>
    /// Прототипы прислужников/миньонов, которых босс призывает во время боя.
    /// </summary>
    [DataField]
    public List<EntProtoId> MinionPrototypes = new();

    /// <summary>
    /// Минимальная фаза босса (0 — начальная), с которой начинают спавниться миньоны.
    /// </summary>
    [DataField]
    public int MinionPhaseStart = 1;

    /// <summary>
    /// Интервал между волнами миньонов (в секундах). 0 или пустой список прототипов — отключено.
    /// </summary>
    [DataField]
    public float MinionSpawnInterval = 20f;

    /// <summary>
    /// Сколько миньонов спавнится за одну волну.
    /// </summary>
    [DataField]
    public int MinionSpawnPerWave = 3;

    /// <summary>
    /// Максимальное количество живых миньонов одновременно. При превышении новые не спавнятся.
    /// </summary>
    [DataField]
    public int MaxMinions = 10;

    /// <summary>
    /// Радиус вокруг босса, в котором появляются миньоны.
    /// </summary>
    [DataField]
    public float MinionSpawnRadius = 8f;

    /// <summary>
    /// Звук, проигрываемый при старте арены.
    /// </summary>
    [DataField]
    public SoundSpecifier? StartSound = new SoundPathSpecifier("/Audio/_Wega/Duel/duel_start.ogg");

    /// <summary>
    /// Звук, проигрываемый при смене фазы босса.
    /// </summary>
    [DataField]
    public SoundSpecifier? PhaseSound = new SoundPathSpecifier("/Audio/_Wega/Duel/duel_end.ogg");

    // ── Runtime state ──────────────────────────────────────────────────────────

    /// <summary>
    /// Арена активна (бой идёт).
    /// </summary>
    [ViewVariables]
    public bool IsActive;

    /// <summary>
    /// Зарегистрированные участники текущего боя.
    /// </summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> Participants = new();

    /// <summary>
    /// Сущность босса текущего боя.
    /// </summary>
    [ViewVariables]
    public EntityUid? Boss;

    /// <summary>
    /// Босс в ярости (энрейдж по истечении <see cref="MaxFightDuration"/>): множители
    /// <see cref="EnrageSpeedMultiplier"/>/<see cref="EnrageDamageMultiplier"/> поверх фазовых.
    /// </summary>
    [ViewVariables]
    public bool Enraged;

    /// <summary>
    /// С какого момента все живые участники непрерывно дальше <see cref="AntiKiteRange"/> от босса.
    /// null — кто-то из живых в зоне досягаемости.
    /// </summary>
    [ViewVariables]
    public TimeSpan? OutOfRangeSince;

    /// <summary>Анонс анти-кайт регенерации уже показывался в текущем эпизоде кайта.</summary>
    [ViewVariables]
    public bool AntiKiteAnnounced;

    /// <summary>
    /// Текущая фаза босса (0 — начальная).
    /// </summary>
    [ViewVariables]
    public int Phase;

    /// <summary>
    /// Время окончания основного таймера боя.
    /// </summary>
    [ViewVariables]
    public TimeSpan? FightEndAt;

    /// <summary>
    /// Время отправки сигнала закрытия шлюзов после завершения боя.
    /// </summary>
    [ViewVariables]
    public TimeSpan? GateCloseAt;

    /// <summary>
    /// Время следующего сканирования.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextScan;

    /// <summary>
    /// Время следующей волны миньонов.
    /// </summary>
    [ViewVariables]
    public TimeSpan? NextMinionSpawnAt;

    /// <summary>
    /// Живые миньоны, призванные боссом. Чистятся при завершении арены.
    /// </summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> Minions = new();
}
