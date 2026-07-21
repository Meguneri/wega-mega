using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Server._Wega.LlmNpc;

/// <summary>
/// Помечает сущность как NPC-компаньона на языковой модели: он слышит IC-речь и эмоуты вокруг,
/// раз в паузу собирает контекст, спрашивает модель через API и отвечает речью/эмоутом, копя
/// факты о собеседниках в файле памяти. Только сервер; тело (движение/боёвка) — отдельно, на HTN.
/// </summary>
[RegisterComponent]
public sealed partial class LlmNpcComponent : Component
{
    /// <summary>
    /// Неизменяемый блок личности: кто он, характер, манера речи. Задаётся в прототипе и в промпт
    /// идёт как system-роль отдельно от накопленной памяти — чтобы персонаж не «дрейфовал».
    /// </summary>
    [DataField(required: true)]
    public string Personality = string.Empty;

    /// <summary>
    /// Имя файла памяти в data-папке сервера (llm_npc/&lt;MemoryFile&gt;.md). Накопленные факты о людях
    /// и событиях; растёт от reply к reply. Несколько NPC с одним файлом делят память.
    /// </summary>
    [DataField]
    public string MemoryFile = "companion";

    /// <summary>Радиус (в тайлах), в котором NPC слышит речь и эмоуты.</summary>
    [DataField]
    public float HearingRange = 6f;

    /// <summary>
    /// Задать внешность на спавне (случайный профиль вида, форсированный в пол <see cref="Sex"/>).
    /// false — оставить внешность как в прототипе.
    /// </summary>
    [DataField]
    public bool ForceFemale = true;

    /// <summary>Пол генерируемой внешности (когда <see cref="ForceFemale"/> включён).</summary>
    [DataField]
    public Content.Shared.Humanoid.Sex Sex = Content.Shared.Humanoid.Sex.Female;

    /// <summary>
    /// Причёска, надеваемая на спавне (id marking-прототипа волос). Случайный профиль вида волос не
    /// генерирует (движок этого не делает), поэтому без этого NPC лысый. Пусто = не трогать волосы.
    /// </summary>
    [DataField]
    public string? Hair = "HumanHairLongsidepart";

    /// <summary>Цвет волос (hex). Применяется вместе с <see cref="Hair"/>.</summary>
    [DataField]
    public Color HairColor = Color.FromHex("#F0D890");

    /// <summary>Сколько последних услышанных строк держать в контексте.</summary>
    [DataField]
    public int ContextLines = 20;

    /// <summary>
    /// Сколько СВОИХ последних реплик помнить отдельно от общего контекста. Нужен отдельный
    /// список: в общем окне на 20 строк собственные слова быстро вытесняются чужими репликами и
    /// служебными заметками о действиях — и NPC, не видя, что уже говорил, повторяется.
    /// </summary>
    [DataField]
    public int SelfMemoryLines = 8;

    /// <summary>Последние собственные реплики (для блока «не повторяйся» и отсева дублей).</summary>
    [ViewVariables]
    public readonly List<string> RecentSaid = new();

    // --- пер-NPC переопределения API (иначе — глобальные cvar'ы wega.llm_npc_*) ---

    /// <summary>Модель только для этого NPC (напр. "openai/gpt-5-mini"). null = глобальная.</summary>
    [DataField]
    public string? ModelOverride;

    /// <summary>Эндпоинт только для этого NPC. null = глобальный.</summary>
    [DataField]
    public string? EndpointOverride;

    /// <summary>
    /// Имя cvar'а с API-ключом для этого NPC (напр. "wega.llm_npc_api_key2" — второй провайдер).
    /// Сам ключ в прототип класть НЕЛЬЗЯ (репозиторий публичный) — только имя cvar'а.
    /// null/пусто или cvar пуст = глобальный ключ.
    /// </summary>
    [DataField]
    public string? ApiKeyCvar;

    /// <summary>Уже извинилась за молчание при исчерпании бюджета раунда (одноразовая реплика).</summary>
    [ViewVariables]
    public bool BudgetExcused;

    // --- рантайм-состояние (не сохраняется) ---

    /// <summary>Кольцо последних услышанных реплик/эмоутов, "Имя: текст".</summary>
    [ViewVariables]
    public readonly List<string> Heard = new();

    /// <summary>Время, когда стоит ответить (ставится при новой услышанной реплике). null = молчит.</summary>
    [ViewVariables]
    public TimeSpan? ReplyAt;

    /// <summary>Идёт запрос к API — второй не шлём, пока не вернётся первый.</summary>
    [ViewVariables]
    public bool Thinking;

    // --- текущее поручение (простое действие тела) ---

    /// <summary>Что сейчас делает тело. None = стоит.</summary>
    [ViewVariables]
    public LlmErrand Errand = LlmErrand.None;

    /// <summary>Цель поручения: человек (GoTo/GiveItem/Follow) или предмет на полу (PickUp).</summary>
    [ViewVariables]
    public EntityUid? ErrandTarget;

    /// <summary>Что вручить по прибытии (только GiveItem).</summary>
    [ViewVariables]
    public EntityUid? ErrandItem;

    /// <summary>Когда бросить попытку дойти (застряла/нет пути). null у Follow — он бессрочный.</summary>
    [ViewVariables]
    public TimeSpan? ErrandTimeout;

    /// <summary>«Своё место» — точка спавна; сюда возвращается после доставки.</summary>
    [ViewVariables]
    public Robust.Shared.Map.EntityCoordinates? Home;

    // --- конвейер приготовления напитка (GoMix → Mixing → [GiveItem]) ---

    /// <summary>Что готовим (рецепт из каталога). null = не готовим.</summary>
    [ViewVariables]
    public LlmDrinks.DrinkRecipe? PendingDrink;

    /// <summary>Кому подать готовый напиток (null = оставить у себя в руке).</summary>
    [ViewVariables]
    public EntityUid? PendingServe;

    /// <summary>Когда смешивание закончится и напиток будет готов.</summary>
    [ViewVariables]
    public TimeSpan? MixUntil;

    /// <summary>Следующая проверка «слежения взглядом» за ближайшим игроком (троттлинг).</summary>
    [ViewVariables]
    public TimeSpan NextGaze;

    /// <summary>Когда в следующий раз переминуться с ноги на ногу (лёгкое блуждание у своего места).</summary>
    [ViewVariables]
    public TimeSpan NextWander;

    /// <summary>Куда сейчас переминаемся (тайл рядом со своим местом).</summary>
    [ViewVariables]
    public Robust.Shared.Map.EntityCoordinates? WanderTo;

    // --- проактивность ---

    /// <summary>Кого и когда приветствовали: не здороваемся с одним человеком чаще раза в N минут.</summary>
    [ViewVariables]
    public readonly Dictionary<EntityUid, TimeSpan> Greeted = new();

    /// <summary>Когда рядом последний раз звучала речь (для «разбить тишину»).</summary>
    [ViewVariables]
    public TimeSpan LastHeardAt;

    /// <summary>Раньше этого времени тишину не разбиваем (кулдаун, чтобы не бубнила без конца).</summary>
    [ViewVariables]
    public TimeSpan NextNudge;

    /// <summary>
    /// До этого времени NPC молчит (инструмент be_quiet после «помолчи»). Слышать продолжает;
    /// прямое обращение по имени снимает мьют досрочно.
    /// </summary>
    [ViewVariables]
    public TimeSpan? MuteUntil;

    // --- гаджет (портативный компьютер тренера в сумке) ---

    /// <summary>
    /// Прототип гаджета, который NPC реально достаёт из сумки при поиске данных (fight_stats),
    /// стучит по клавишам и после ответа убирает обратно. Пусто = без гаджета (эмоут без предмета).
    /// </summary>
    [DataField]
    public string? GadgetProto;

    /// <summary>Звук принтера в момент выдачи распечатки боя с гаджета.</summary>
    [DataField]
    public Robust.Shared.Audio.SoundSpecifier PrintSound =
        new Robust.Shared.Audio.SoundPathSpecifier("/Audio/Machines/printer.ogg");

    /// <summary>Гаджет сейчас в руке (достала для поиска). null = убран.</summary>
    [ViewVariables]
    public EntityUid? HeldGadget;

    /// <summary>Когда убрать гаджет обратно в сумку (ставится после выдачи ответа).</summary>
    [ViewVariables]
    public TimeSpan? StowGadgetAt;

    // --- присутствие гостей и чаевые ---

    /// <summary>Когда человека последний раз видели в зоне бара (для «вернулся после отлучки»).</summary>
    [ViewVariables]
    public readonly Dictionary<EntityUid, TimeSpan> PresenceLastNear = new();

    /// <summary>Кто сейчас «у бара» и с какого момента. Пропал из зоны — заметка «ушёл».</summary>
    [ViewVariables]
    public readonly Dictionary<EntityUid, TimeSpan> PresenceArrivedAt = new();

    /// <summary>Следующий скан присутствия/чаевых (троттлинг, раз в ~5 сек).</summary>
    [ViewVariables]
    public TimeSpan NextPresenceScan;

    /// <summary>Пачки кредитов, уже замеченные как чаевые — второй раз не благодарим.</summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> NotedTips = new();

    /// <summary>Осколки (разбитая посуда/стекло), уже отмеченные — на каждый звенит один раз.</summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> NotedShards = new();

    /// <summary>Троттлинг заметок о швырянии предметов (при потасовке летит много всего).</summary>
    [ViewVariables]
    public TimeSpan NextThrowNote;

    /// <summary>
    /// Бесхозные предметы у бара → вероятный владелец (кто стоял вплотную/бросил). Если предмет
    /// потом оказывается в инвентаре другого — она понимает, что взяли ЧУЖОЕ.
    /// </summary>
    [ViewVariables]
    public readonly Dictionary<EntityUid, EntityUid> LooseItemOwner = new();

    // --- настроение и отношения (живое состояние, не вечное) ---

    /// <summary>
    /// Текущее настроение свободным текстом («обижена на Иванова — он её ударил»). Уходит в промпт
    /// и окрашивает тон. null = ровное. Ставится кодом (боль) и самой моделью (set_mood): извинились
    /// и загладили вину — модель смягчает или очищает, а по <see cref="MoodUntil"/> обида забывается сама.
    /// </summary>
    [ViewVariables]
    public string? Mood;

    /// <summary>Когда настроение само вернётся к ровному (обиды не вечны).</summary>
    [ViewVariables]
    public TimeSpan MoodUntil;

    /// <summary>
    /// Отношение к людям на эту смену: имя → короткий текст («тепло», «настороженно», «холодно»).
    /// Меняет сама модель через set_attitude; долговременное уходит в память через remember.
    /// </summary>
    [ViewVariables]
    public readonly Dictionary<string, string> Attitude = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Троттлинг реакции на боль (не вздрагивать на каждый тик урона).</summary>
    [ViewVariables]
    public TimeSpan NextPain;

    /// <summary>Когда её последний раз ранили (для «отлежаться» — реген только вне боя).</summary>
    [ViewVariables]
    public TimeSpan LastHurtAt;

    /// <summary>
    /// Сколько болевых событий подряд без большой паузы. Растёт при избиении, обнуляется, когда
    /// удары прекращаются. Чем выше — тем острее эмоциональный регистр реакции (возмущение → паника).
    /// </summary>
    [ViewVariables]
    public byte HurtStreak;

    /// <summary>
    /// Боевой темперамент 0..1: как рано она ломается и как охотно даёт сдачи под уроном.
    /// 0 — трусиха (рано отступает, бьёт в ответ неохотно), 1 — берсерк (дерётся до последнего,
    /// добивает обидчика). 0.5 — обычный человек. Задаёт разброс реакций между разными NPC.
    /// </summary>
    [DataField]
    public float Aggression = 0.5f;

    /// <summary>Даёт инструмент undress — снимать одежду с себя или (по согласию) с других.</summary>
    [DataField]
    public bool CanUndress;

    /// <summary>Троттлинг реакции на «с тебя сняли одежду», чтобы не отмечать каждый слот в потоке.</summary>
    [ViewVariables]
    public TimeSpan NextUndressNote;

    /// <summary>Следующий тик самолечения.</summary>
    [ViewVariables]
    public TimeSpan NextRegen;
}

/// <summary>Виды поручений тела LLM-NPC.</summary>
public enum LlmErrand : byte
{
    None,
    /// <summary>Подойти к человеку и остаться там.</summary>
    GoTo,
    /// <summary>Отнести предмет человеку, вручить и вернуться на своё место.</summary>
    GiveItem,
    /// <summary>Подойти к предмету и поднять его.</summary>
    PickUp,
    /// <summary>Ходить за человеком, пока не скажут прекратить.</summary>
    Follow,
    /// <summary>Вернуться на своё место.</summary>
    GoHome,
    /// <summary>Идти к своему месту (бару), чтобы там смешать напиток.</summary>
    GoMix,
    /// <summary>Стоит у бара и смешивает напиток (таймер MixUntil).</summary>
    Mixing,
    /// <summary>Переминается: шаг на соседний тайл у своего места и обратно (жизнь в простое).</summary>
    Wander,
}
