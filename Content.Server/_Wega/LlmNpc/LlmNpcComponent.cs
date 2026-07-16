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
    /// Задать женскую внешность на спавне (случайный профиль вида, форсированный в женский пол).
    /// false — оставить внешность как в прототипе.
    /// </summary>
    [DataField]
    public bool ForceFemale = true;

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

    /// <summary>Троттлинг реакции на боль (не вздрагивать на каждый тик урона).</summary>
    [ViewVariables]
    public TimeSpan NextPain;

    /// <summary>Когда её последний раз ранили (для «отлежаться» — реген только вне боя).</summary>
    [ViewVariables]
    public TimeSpan LastHurtAt;

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
