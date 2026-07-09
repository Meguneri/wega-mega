using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Components;

[RegisterComponent]
public sealed partial class DuelArenaComponent : Component, IDuelScoreStore
{
    // Звук СТАРТА дуэли воспроизводит DuelStartSoundEmitter на карте
    // (EmitGlobalSoundOnSignal). Здесь поля для него нет намеренно.

    /// <summary>Звук в момент завершения дуэли (победа/ничья).</summary>
    [DataField]
    public SoundSpecifier? EndSound = new SoundPathSpecifier("/Audio/_Wega/Duel/duel_end.ogg");

    Dictionary<NetUserId, int> IDuelScoreStore.Scores => Scores;
    Dictionary<NetUserId, string> IDuelScoreStore.ScoreNames => ScoreNames;
    Dictionary<NetUserId, int> IDuelScoreStore.LosingStreaks => LosingStreaks;
    NetUserId? IDuelScoreStore.StreakUser { get => StreakUser; set => StreakUser = value; }
    int IDuelScoreStore.Streak { get => Streak; set => Streak = value; }

    /// <summary>
    /// Охват арены — весь грид трекера (дуэлянты считаются по всему гриду, без радиуса).
    /// Это значение — лишь запасной радиус (в тайлах) на случай, если трекер не на гриде (в космосе).
    /// </summary>
    [DataField]
    public float ScanRange = 200f;

    /// <summary>
    /// Очистка снаряжения охватывает весь грид трекера (без радиуса).
    /// Это значение — лишь запасной радиус (в тайлах) на случай, если трекер не на гриде (в космосе).
    /// </summary>
    [DataField]
    public float CleanupRange = 200f;

    /// <summary>
    /// Как часто (в секундах) трекер сканирует зону на наличие дуэлянтов.
    /// </summary>
    [DataField]
    public float ScanInterval = 0.5f;

    /// <summary>
    /// Выходной порт, на который шлётся сигнал при завершении дуэли (закрывает барьеры).
    /// </summary>
    [DataField]
    public string ResetPort = "DuelEnded";

    /// <summary>
    /// <summary>Через сколько секунд после конца боя трекер шлёт на шлюзы баз сигнал закрытия —
    /// чтобы дуэлянты успели вернуться в свои базы по открытым шлюзам.
    /// </summary>
    [DataField]
    public float ReturnGrace = 20f;

    /// <summary>
    /// Максимальная длительность боя (в секундах). 0 — таймер выключен (бой длится до победы/сброса).
    /// По истечении начинается внезапная смерть или, если она отключена, дуэль завершается вничью.
    /// </summary>
    [DataField]
    public float MaxFightDuration = 0f;

    /// <summary>
    /// Длительность фазы «внезапной смерти» (в секундах). 0 — сразу ничья по таймауту.
    /// В эту фазу принудительно запускается шторм (если на трекере есть <see cref="ArenaStormComponent"/>),
    /// чтобы форсировать развязку. Если и за это время победитель не определился — ничья.
    /// </summary>
    [DataField]
    public float SuddenDeathDuration = 0f;

    /// <summary>
    /// Прототип маяка снабжения, который арена сбрасывает в центр во время активного боя.
    /// null — авто-дроп выключен (дроп управляется только кнопкой-спавнером, прежнее поведение).
    /// Обычно <c>DuelSupplyDropBeacon</c>.
    /// </summary>
    [DataField]
    public EntProtoId? SupplyDropProto;

    /// <summary>
    /// Через сколько секунд после старта боя падает первый ящик снабжения.
    /// </summary>
    [DataField]
    public float SupplyDropDelay = 30f;

    /// <summary>
    /// Интервал повторных сбросов снабжения (в секундах). 0 — одноразовый сброс за бой.
    /// </summary>
    [DataField]
    public float SupplyDropInterval = 30f;

    /// <summary>
    /// Время следующего сброса снабжения во время боя. null — не запланирован.
    /// </summary>
    public TimeSpan? SupplyDropAt;

    /// <summary>
    /// Зарегистрированные дуэлянты текущего боя.
    /// </summary>
    public readonly HashSet<EntityUid> Duelists = new();

    /// <summary>
    /// Накопительный счёт побед по игрокам (NetUserId → число побед).
    /// Ключ — игрок, а не тело: счёт переживает клонирование/респавн (новый EntityUid).
    /// Сохраняется между боями на этой арене; сбрасывается сигналом на порт Reset.
    /// </summary>
    public readonly Dictionary<NetUserId, int> Scores = new();

    /// <summary>
    /// Последнее известное имя игрока (NetUserId → имя). Нужно, чтобы показывать общий счёт
    /// с именами даже тех бойцов, кого нет в текущем бою. Сбрасывается вместе со <see cref="Scores"/>.
    /// </summary>
    public readonly Dictionary<NetUserId, string> ScoreNames = new();

    /// <summary>
    /// Текущая серия поражений по игрокам (NetUserId → число поражений подряд).
    /// Сбрасывается при победе; растёт при проигрыше/ничьей.
    /// </summary>
    public readonly Dictionary<NetUserId, int> LosingStreaks = new();

    /// <summary>
    /// Игрок, выигравший прошлый бой, и его текущая серия побед подряд.
    /// Серия растёт, пока побеждает тот же игрок; обнуляется при смене победителя, ничьей и сбросе счёта.
    /// </summary>
    public NetUserId? StreakUser;

    public int Streak;

    /// <summary>
    /// Дуэль «вооружена»: в зоне есть минимум двое бойцов (поддерживается 3+) и ждём исхода.
    /// </summary>
    public bool IsActive;

    /// <summary>
    /// Время, когда закончится основной таймер боя. null — таймер не запущен.
    /// </summary>
    public TimeSpan? FightEndAt;

    /// <summary>
    /// Время окончания фазы внезапной смерти. null — фаза не активна.
    /// </summary>
    public TimeSpan? SuddenDeathEndAt;

    /// <summary>
    /// Фаза внезапной смерти активна прямо сейчас.
    /// </summary>
    public bool SuddenDeathActive;

    /// <summary>
    /// Время следующего сканирования.
    /// </summary>
    public TimeSpan NextScan;

    /// <summary>
    /// Время, когда трекер отправит на шлюзы баз сигнал закрытия после конца боя (см. <see cref="ReturnGrace"/>).
    /// null — закрытие не запланировано (бой идёт либо шлюзы уже закрыты).
    /// </summary>
    public TimeSpan? GateCloseAt;

    /// <summary>
    /// Снимок исходной (пристайн) планировки стен арены: тайл грида → прототип стены.
    /// Пополняется при КАЖДОМ старте дуэли (мерж: новые тайлы добавляются, старые записи не
    /// перезаписываются) — так снимок самовосстанавливается, даже если первый проход вышел
    /// неполным. После каждой дуэли по снимку восстанавливаются разрушенные стены.
    /// </summary>
    public readonly Dictionary<Vector2i, EntProtoId> WallSnapshot = new();

    /// <summary>
    /// Тайл пола под каждой стеной снимка. Если за бой пол под стеной уничтожили (дыра в
    /// космос), стену нельзя заякорить — сначала восстанавливаем пол по этому снимку.
    /// </summary>
    public readonly Dictionary<Vector2i, Tile> WallTileSnapshot = new();

    /// <summary>
    /// Снимок исходной расстановки светильников арены: тайл грида → прототип светильника.
    /// Пополняется при КАЖДОМ старте дуэли (мерж, как у стен). После каждой дуэли по снимку
    /// чинятся/переставляются разбитые лампы и уничтоженные светильники любого типа.
    /// </summary>
    public readonly Dictionary<Vector2i, EntProtoId> LightSnapshot = new();

    /// <summary>
    /// Поворот (к какой стене примонтирован) каждого светильника снимка — чтобы переставленный
    /// заново светильник смотрел в ту же сторону, что и оригинал.
    /// </summary>
    public readonly Dictionary<Vector2i, Angle> LightRotationSnapshot = new();

    /// <summary>
    /// Снимок исходной расстановки решёток арены (обычных и заводных): тайл грида → прототип решётки.
    /// Хранится отдельно от стен, потому что на одном тайле решётка может соседствовать с окном
    /// (окна строятся поверх решёток) — раздельные снимки восстанавливают и решётку, и окно.
    /// Пополняется при КАЖДОМ старте дуэли (мерж, как у стен). После боя сломанные/уничтоженные
    /// решётки чинятся или ставятся заново.
    /// </summary>
    public readonly Dictionary<Vector2i, EntProtoId> GrilleSnapshot = new();

    /// <summary>
    /// Тайл пола под каждой решёткой снимка — чтобы восстановить пол, если его уничтожили за бой.
    /// </summary>
    public readonly Dictionary<Vector2i, Tile> GrilleTileSnapshot = new();

    /// <summary>
    /// Отложенное восстановление стен: выставляется при завершении/сбросе дуэли, выполняется
    /// в Update на следующем тике — вне стека события смерти (MobStateChanged), где удаление
    /// и спавн сущностей могут конфликтовать с обработкой урона.
    /// </summary>
    public bool PendingWallRestore;

    /// <summary>
    /// Отложенное вооружение раунда ротации: выставляется, когда контроллер переносит бойцов на
    /// эту арену (см. <c>DuelRotationSystem.MoveAndStart</c>), и срабатывает в Update на СЛЕДУЮЩЕМ
    /// тике. Нужно потому, что перенос и полное исцеление проигравшего происходят синхронно в
    /// ConcludeDuel: если вооружать сразу, GetAliveInRange может не увидеть бойцов (грид ещё не
    /// обновился / воскрешённый ещё не «жив»), раунд не вооружится и «дуэль начата» не объявится.
    /// </summary>
    public bool PendingRotationArm;

    /// <summary>
    /// Время последней обработки сигнала старта (порт Open). Используется для дебаунса: один и тот
    /// же импульс может прийти дважды (двойная линковка, фронты high/low, несколько передатчиков на
    /// канале DuelFight), из-за чего объявление «нужно минимум 2 бойца» дублировалось в чате.
    /// </summary>
    public TimeSpan? LastStartSignal;

    /// <summary>
    /// Контроллер арена-ротации, которому эта арена подчинена. <c>null</c> (по умолчанию) — арена
    /// одиночная и ведёт свой счёт сама (прежнее поведение). Если задан — арена входит в ротацию:
    /// исход раунда уходит контроллеру (общий счёт), а он переключает поля боя между раундами.
    /// Связывается контроллером при предзагрузке арен, в маппинге вручную не выставляется.
    /// </summary>
    public EntityUid? RotationController;

    // ── Ready-check (кнопка готовности) ────────────────────────────────────────

    /// <summary>
    /// Нажатые кнопки готовности этой арены (по одной на базу). Готовность считаем по КНОПКАМ, а не
    /// по бойцам «в радиусе»: дуэлянты сидят в отдельных запечатанных базах и в момент нажатия второй
    /// мог быть ещё не виден трекеру — счёт по кнопкам от их позиций не зависит. Бой стартует, когда
    /// нажаты все кнопки арены (минимум две). Сбрасывается при старте/завершении/сбросе.
    /// </summary>
    public readonly HashSet<EntityUid> ReadyButtons = new();

    /// <summary>
    /// Голограммы «ГОТОВ» (кнопка → сущность-голограмма). Висят над кнопкой бойца, который ещё НЕ
    /// нажал свою, пока готов хотя бы один соперник — так второй дуэлянт у своей кнопки видит, что
    /// первый уже подтвердил готовность. Снимаются при нажатии этой кнопки и при старте/конце/сбросе.
    /// </summary>
    public readonly Dictionary<EntityUid, EntityUid> ReadyHolograms = new();

    /// <summary>Прототип голограммы готовности, висящей над бойцом.</summary>
    [DataField]
    public EntProtoId ReadyHologram = "DuelReadyHologram";

    /// <summary>Звук подтверждения готовности (играется рядом с кнопкой).</summary>
    [DataField]
    public SoundSpecifier? ReadySound = new SoundPathSpecifier("/Audio/_Wega/Duel/duel_ready.ogg");
}
