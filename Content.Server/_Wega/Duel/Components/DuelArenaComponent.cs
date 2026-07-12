using System.Numerics;
using Content.Shared.Decals;
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

    /// <summary>
    /// Крейт арсенала (FullArsenal SurplusBundle), который спавнится у каждого спавн-маркера при
    /// старте раунда. Задаётся командой/пультом и ПЕРЕЗАПИСЫВАЕТСЯ целиком — никакого наложения тиров:
    /// сменил 120→40 ТК, и следующий раунд спавнит только 40. null — крейты не спавнить.
    /// </summary>
    [DataField]
    public EntProtoId? ArsenalCrate = "CrateSyndicateFullArsenal";

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
    /// Пристайн-планировка ВСЕХ заякоренных конструкций арены: тайл грида → список конструкций на
    /// нём (стены, окна, решётки, светильники, столы, перила, двери, постеры, растения и т.п.). На
    /// одном тайле их может быть несколько (окно поверх решётки, светильник на стене), поэтому
    /// значение — список. После каждой дуэли по снимку восстанавливается всё разрушенное.
    /// Инфраструктура с сигнальными связями (трекер, кнопки, шлюзы, спавнеры — всё с device-link)
    /// в снимок НЕ попадает: её нельзя пересоздавать, иначе рвутся линковки сигналов.
    /// </summary>
    public readonly Dictionary<Vector2i, List<ArenaStructure>> StructureSnapshot = new();

    /// <summary>
    /// Пол по ВСЕМУ гриду арены: тайл → плитка. Если за бой пол уничтожили (дыра в космос),
    /// восстанавливаем его по этому снимку — иначе на тайле не заякорить конструкцию, а сам провал
    /// остался бы. Снимаются только непустые тайлы, поэтому «лишний» пол мы никогда не срезаем.
    /// </summary>
    public readonly Dictionary<Vector2i, Tile> TileSnapshot = new();

    /// <summary>
    /// Свободные (не заякоренные) предметы-декор арены: прототип + позиция + поворот. Плюшевые
    /// игрушки, шахматы, безделушки, вывески и прочий разбрасываемый инвентарь карты. После боя все
    /// свободные предметы на гриде удаляются и раскладываются заново по этому снимку — так их
    /// позиции и количество точно совпадают с исходными.
    /// </summary>
    public readonly List<ArenaProp> PropSnapshot = new();

    /// <summary>
    /// Декали арены (нанесённые на пол метки: кровь-декор, надписи, разметка). После боя все декали
    /// грида стираются и накатываются заново по этому снимку — так с пола пропадает боевая кровь и
    /// подпалины, а исходная разметка возвращается.
    /// </summary>
    public readonly List<Decal> DecalSnapshot = new();

    /// <summary>
    /// Снимок арены (конструкции + пол + предметы + декали) уже снят. Снимаем РОВНО ОДИН РАЗ — при
    /// первом старте дуэли, пока арена пристайн (стены целы, боевой крови/мусора ещё нет). Повторный
    /// мерж на каждом старте забетонировал бы в «эталон» уцелевший после боя мусор и сдвинутые
    /// предметы, поэтому снимок фиксируется один раз и дальше служит эталоном для восстановления.
    /// </summary>
    public bool SnapshotCaptured;

    /// <summary>
    /// Отложенное восстановление арены: выставляется при завершении/сбросе дуэли, выполняется
    /// в Update по достижении <see cref="PendingRestoreAt"/> — вне стека события смерти
    /// (MobStateChanged), где удаление и спавн сущностей могут конфликтовать с обработкой урона.
    /// </summary>
    public bool PendingRestore;

    /// <summary>
    /// Момент, когда выполнить отложенное восстановление. Ставится на <see cref="RestoreDelay"/>
    /// секунд позже конца боя: смертельный удар может быть нанесён отложенной взрывчаткой (граната,
    /// заряд), чей взрыв обрабатывается уже ПОСЛЕ конца дуэли — если восстановить сразу на следующем
    /// тике, разрушение от такого взрыва останется навсегда. Задержка даёт взрывам осесть.
    /// </summary>
    public TimeSpan? PendingRestoreAt;

    /// <summary>Задержка восстановления арены после конца боя, сек (см. <see cref="PendingRestoreAt"/>).</summary>
    [DataField]
    public float RestoreDelay = 2.5f;

    /// <summary>
    /// Арсенал-ящики (<see cref="ArsenalCrate"/>) уже заспавнены в текущем раунде. Гард от повторного
    /// спавна: раунд готовится через <c>ArenaRoundPreparingEvent</c> (перенос бойцов на арену), а
    /// <see cref="DuelArenaSystem"/> подстраховывается ещё и в ArmDuel — флаг не даёт задвоить ящики.
    /// Сбрасывается по концу/сбросу боя (после очистки, которая эти ящики убирает).
    /// </summary>
    public bool ArsenalSpawned;

    /// <summary>
    /// Бойцы для ОТЛОЖЕННОГО повторного исцеления в момент <see cref="PendingRestoreAt"/>. Немедленный
    /// Rejuvenate в ConcludeDuel поднимает их из крита сразу, но конечности, оторванные отложенным
    /// взрывом (граната/заряд, сработавший смертельным ударом) уже ПОСЛЕ конца боя, к тому моменту ещё
    /// на месте. Повторный Rejuvenate после оседания взрывов отращивает их (RestoreMissingOrgans берёт
    /// недостающие части из InitialBody). Идемпотентен: целому бойцу ничего не меняет.
    /// </summary>
    public List<EntityUid> PendingHealDuelists = new();

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

/// <summary>Одна заякоренная конструкция снимка арены: прототип и поворот (для настенных объектов).</summary>
public struct ArenaStructure
{
    public EntProtoId Proto;
    public Angle Rotation;

    public ArenaStructure(EntProtoId proto, Angle rotation)
    {
        Proto = proto;
        Rotation = rotation;
    }
}

/// <summary>Один свободный предмет-декор снимка арены: прототип, локальная позиция на гриде и поворот.</summary>
public struct ArenaProp
{
    public EntProtoId Proto;
    public Vector2 Position;
    public Angle Rotation;

    public ArenaProp(EntProtoId proto, Vector2 position, Angle rotation)
    {
        Proto = proto;
        Position = position;
        Rotation = rotation;
    }
}
