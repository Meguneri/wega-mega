using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Состояние арестатора.
/// </summary>
public enum ArrestBotState
{
    Idle,
    Pursuing,
    Stripping,
    Cuffing,
    Delivering
}

/// <summary>
/// Компонент арестатора. Преследует назначенную цель, оглушает её станнером,
/// снимает броню и одежду, надевает наручники и ведёт к тому, кто отдал приказ,
/// после чего уходит в блюспейс.
/// </summary>
[RegisterComponent]
public sealed partial class ArrestBotComponent : Component
{
    /// <summary>
    /// Цель задержания.
    /// </summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>
    /// Тот, кто отдал приказ на задержание. Арестатор ведёт цель к этому существу.
    /// </summary>
    [ViewVariables]
    public EntityUid? Issuer;

    /// <summary>
    /// Текущее состояние.
    /// </summary>
    [ViewVariables]
    public ArrestBotState State = ArrestBotState.Idle;

    /// <summary>
    /// Радиус ближнего боя — с него арестатор снимает одежду и надевает наручники.
    /// </summary>
    [DataField]
    public float ArrestRange = 1.4f;

    /// <summary>
    /// Дальность, с которой арестатор стреляет из станнера по цели.
    /// </summary>
    [DataField]
    public float FiringRange = 7f;

    /// <summary>
    /// Длительность гарантированного добивающего оглушения при надевании наручников (секунды).
    /// Основной стан наносит само оружие; это лишь страховка, чтобы цель не очнулась в упор.
    /// </summary>
    [DataField]
    public float StunDuration = 4f;

    /// <summary>
    /// Прототип наручников, которые надевает арестатор.
    /// </summary>
    [DataField]
    public EntProtoId HandcuffPrototype = "Handcuffs";

    /// <summary>
    /// Имплант «Блюспейс коридор», который арестатор активирует после доставки цели.
    /// </summary>
    [DataField]
    public EntProtoId BluespaceImplant = "BluespaceLifelineImplant";

    /// <summary>
    /// Задержка между действиями — выстрелами, снятием вещей, наручниками (секунды).
    /// </summary>
    [DataField]
    public float ActionCooldown = 1f;

    /// <summary>
    /// Слоты, которые конвоир снимает с цели перед наручниками: броня или скафандр,
    /// шлем, РПС, рюкзак, перчатки, комбинезон и обувь — всё это может нести защиту.
    /// </summary>
    [DataField]
    public List<string> StripSlots = new() { "outerClothing", "head", "belt", "back", "gloves", "jumpsuit", "shoes" };

    /// <summary>
    /// Уже объявил ли конвоир о том, что заметил цель (чтобы не повторяться).
    /// </summary>
    [ViewVariables]
    public bool Spotted;

    /// <summary>
    /// Последняя точка, к которой прокладывался путь. Нужна, чтобы не сбрасывать
    /// найденный маршрут каждый тик — перепрокладываем, только когда цель заметно сместилась.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates? LastGoal;

    /// <summary>
    /// Звук на случай, если оружие выбили из рук — задержание всё равно продолжается.
    /// </summary>
    [DataField]
    public SoundSpecifier StunSound = new SoundPathSpecifier("/Audio/Weapons/egloves.ogg");

    /// <summary>
    /// Время следующего действия.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextAction;

    /// <summary>
    /// Время, через которое арестатор сбросится, если не может завершить задержание.
    /// </summary>
    [DataField]
    public float Timeout = 90f;

    /// <summary>
    /// Время окончания таймаута.
    /// </summary>
    [ViewVariables]
    public TimeSpan? TimeoutEndAt;

    /// <summary>
    /// Слоты, которые не удалось снять в текущем задержании — больше не пробуем.
    /// </summary>
    [ViewVariables]
    public HashSet<string> FailedStripSlots = new();

    // --- пролом (маршрута нет — крушим преграду на линии к цели) ---

    /// <summary>Преграда (стена/дверь), которую арестатор сейчас выламывает. null = не ломает.</summary>
    [ViewVariables]
    public EntityUid? BreachTarget;

    /// <summary>
    /// Точка взлома: ближайшая к цели ДОСТИЖИМАЯ точка (ищется пасфайндером по кольцам вокруг
    /// цели). Бот сначала штатно доходит сюда и только отсюда пробивает минимальную преграду —
    /// а не долбит стену по прямой через полстанции.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates? BreachStaging;

    /// <summary>Идёт асинхронный подбор точки взлома — второй не запускаем.</summary>
    [ViewVariables]
    public bool BreachPlanning;

    /// <summary>Время следующего удара по преграде.</summary>
    [ViewVariables]
    public TimeSpan NextBreach;

    /// <summary>Структурный урон одного удара по преграде.</summary>
    [DataField]
    public float BreachDamage = 60f;

    /// <summary>Пауза между ударами по преграде, сек.</summary>
    [DataField]
    public float BreachCooldown = 1.5f;

    /// <summary>Звук удара по преграде.</summary>
    [DataField]
    public SoundSpecifier BreachSound = new SoundCollectionSpecifier("ArrestBotBreachHit");

    /// <summary>Звук обрушения преграды.</summary>
    [DataField]
    public SoundSpecifier BreachCollapseSound =
        new SoundPathSpecifier("/Audio/_Wega/Duel/breach/stone_push_long.ogg");

    /// <summary>Эффект, спавнящийся на преграде при каждом ударе.</summary>
    [DataField]
    public EntProtoId BreachHitEffect = "EffectArrestBotBreachHit";

    /// <summary>Эффект на месте рухнувшей преграды.</summary>
    [DataField]
    public EntProtoId BreachCollapseEffect = "EffectArrestBotWallCollapse";

    /// <summary>Последние координаты выламываемой преграды — для эффекта обрушения.</summary>
    [ViewVariables]
    public EntityCoordinates? BreachTargetCoords;

    /// <summary>
    /// Лучшая (минимальная) дистанция до цели за погоню: пока бот сближается, таймаут
    /// продлевается — дорога через полстанции не должна отменять приказ.
    /// </summary>
    [ViewVariables]
    public float BestGoalDistance = float.MaxValue;
}
