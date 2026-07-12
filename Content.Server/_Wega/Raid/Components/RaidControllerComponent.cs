using Content.Shared.Damage;
using Content.Shared.Store;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Wega.Raid.Components;

/// <summary>
/// Невидимый контроллер экстракшн-режима («рейд»). Ставится ОДИН раз на хаб-карту (как
/// <c>DuelRotationController</c> у арены). При инициализации предзагружает карту-локацию рейда
/// (<see cref="RaidMap"/>), чтобы вход не вызывал загрузочный фриз.
///
/// Кор-луп: игрок жмёт кнопку входа (<c>RaidEntry</c>) на хабе → телепортируется на карту рейда со
/// своим снаряжением → лутает/воюет → встаёт на точку экстракта (<c>RaidExtractionPoint</c>) и через
/// несколько секунд эвакуируется обратно на хаб (выжил, шмот при нём). Смерть в рейде = тело и
/// снаряжение остаются на карте лутом (штатное поведение SS14, отдельного кода не нужно).
///
/// Раунд-скоуп: между раундами сервера карта рейда переинициализируется движком, поэтому стэш/лут
/// живут только в пределах раунда — персистентности между раундами здесь намеренно нет.
/// </summary>
[RegisterComponent]
public sealed partial class RaidControllerComponent : Component
{
    /// <summary>Путь к карте-локации рейда (.yml в Resources). Предзагружается один раз.</summary>
    [DataField(required: true)]
    public ResPath RaidMap;

    /// <summary>MapId загруженной карты рейда. null — ещё не загружена.</summary>
    [ViewVariables]
    public MapId? LoadedMap;

    /// <summary>Карта рейда предзагружена один раз при инициализации. true после успеха.</summary>
    [ViewVariables]
    public bool Loaded;

    /// <summary>
    /// Авто-настройка для быстрой проверки: если на загруженной карте рейда нет маркеров входа —
    /// контроллер сам расставит спавны, точку экстракта и поля лута/скавов на свободных тайлах.
    /// Нужно для теста режима на любой готовой карте без ручной расстановки (см. RaidControllerTest).
    /// На полноценной карте с расставленными маркерами не делает ничего.
    /// </summary>
    [DataField]
    public bool AutoSetup;

    /// <summary>
    /// Длительность рейда в секундах. По истечении ещё не вышедших рейдеров принудительно
    /// эвакуируют на хаб, а рейд закрывается (следующий вход стартует новый таймер).
    /// </summary>
    [DataField]
    public float RaidDuration = 900f;

    /// <summary>Идёт ли сейчас рейд (есть таймер). Сбрасывается в false по истечении таймера.</summary>
    [ViewVariables]
    public bool Active;

    /// <summary>Время завершения текущего рейда. null — рейд не идёт.</summary>
    [ViewVariables]
    public TimeSpan? EndTime;

    /// <summary>Мобы, находящиеся сейчас в рейде (телепортированы на карту рейда, ещё не вышли).</summary>
    [ViewVariables]
    public HashSet<EntityUid> Raiders = new();

    /// <summary>Звук объявления старта рейда (на весь сервер).</summary>
    [DataField]
    public SoundSpecifier? StartSound = new SoundPathSpecifier("/Audio/_Wega/Duel/duel_start.ogg");

    /// <summary>Звук объявления завершения рейда.</summary>
    [DataField]
    public SoundSpecifier? EndSound = new SoundPathSpecifier("/Audio/_Wega/Duel/duel_end.ogg");

    // ── Экономика: награда за вынос (Фаза 2) ──────────────────────────────────────────────────

    /// <summary>
    /// Сколько кредитов карго-стоимости вынесенного лута идёт за 1 ТК награды. Чем выше — тем дороже
    /// «обменный курс» (меньше ТК за тот же лут).
    /// </summary>
    [DataField]
    public float CreditsPerTc = 100f;

    /// <summary>Базовая награда (ТК) за сам факт успешного экстракта — бонус за выживание.</summary>
    [DataField]
    public int BaseReward = 2;

    /// <summary>Потолок награды (ТК) за один экстракт. 0 — без потолка.</summary>
    [DataField]
    public int MaxReward;

    /// <summary>Прототип валюты-награды (из прототипов валют магазина). Зачисляется на персистентный счёт рейдера.</summary>
    [DataField]
    public ProtoId<CurrencyPrototype> RewardCurrency = "Telecrystal";

    // ── Напряжение таймера (Фаза 1.1) ─────────────────────────────────────────────────────────

    /// <summary>За сколько секунд до конца рейда объявлять предупреждения «на выход».</summary>
    [DataField]
    public List<float> WarningTimes = new() { 300f, 60f };

    /// <summary>Рантайм: ещё не объявленные пороги предупреждений (секунды до конца), по убыванию.</summary>
    [ViewVariables]
    public List<float> PendingWarnings = new();

    /// <summary>
    /// Урон, добивающий тех, кто не успел эвакуироваться к истечению таймера (MIA = смерть и потеря
    /// всего). Задаётся в прототипе. null — не успевших просто «вытащит» без последствий (мягкий режим).
    /// </summary>
    [DataField]
    public DamageSpecifier? MiaDamage;
}
