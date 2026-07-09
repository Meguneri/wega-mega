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
    /// </summary>
    [DataField]
    public float MaxFightDuration = 0f;

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
