using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Components;

/// <summary>Состояние боевой машины Голиафа.</summary>
public enum GoliathState : byte
{
    Idle,
    /// <summary>Замах слэма: телеграф-кольцо на полу, босс стоит.</summary>
    SlamWindup,
    /// <summary>Замах чарджа: телеграф-линия, босс «заводит гидравлику».</summary>
    ChargeWindup,
    /// <summary>Несётся по прямой; стена = стаггер, игроки на пути — таран.</summary>
    Charging,
}

/// <summary>
/// Арена-босс «Голиаф» — тяжёлый рыцарь в духе DS3 (Вордт/Гундир): медленный танк с двумя
/// телеграфированными атаками (чардж через арену и АоЕ-слэм) и наказуемой ошибкой — врезавшись
/// в стену после чарджа, оглушённо стоит и получает двойной урон (стаггер-окно). Фаза 2
/// (BossArenaBossComponent.CurrentPhase >= 1): короче кулдауны + морозный след за чарджем.
/// Вся дарксоулс-логика — в <see cref="Systems.GoliathBossSystem"/>; фазы/ярость/HP-бар — штатные
/// (BossArenaSystem/BossArenaHud).
/// </summary>
[RegisterComponent]
public sealed partial class GoliathBossComponent : Component
{
    // ── Чардж ────────────────────────────────────────────────────────────────

    /// <summary>Кулдаун чарджа, сек (фаза 2 — умножается на <see cref="Phase2CooldownMultiplier"/>).</summary>
    [DataField]
    public float ChargeCooldown = 9f;

    /// <summary>Замах чарджа (телеграф-линия видна), сек.</summary>
    [DataField]
    public float ChargeWindup = 1.1f;

    /// <summary>Скорость чарджа, тайлов/сек.</summary>
    [DataField]
    public float ChargeSpeed = 13f;

    /// <summary>Максимальная дальность чарджа, тайлов.</summary>
    [DataField]
    public float ChargeMaxDistance = 12f;

    /// <summary>Урон тарана (Blunt) по каждому задетому.</summary>
    [DataField]
    public float ChargeDamage = 30f;

    /// <summary>Нокдаун задетых чарджем, сек.</summary>
    [DataField]
    public float ChargeParalyze = 1.5f;

    /// <summary>Дистанция до цели, с которой босс ВООБЩЕ рассматривает чардж.</summary>
    [DataField]
    public float ChargeMinTargetRange = 4f;

    // ── Слэм ─────────────────────────────────────────────────────────────────

    /// <summary>Кулдаун слэма, сек.</summary>
    [DataField]
    public float SlamCooldown = 8f;

    /// <summary>Замах слэма (кольцо на полу видно), сек.</summary>
    [DataField]
    public float SlamWindup = 1.0f;

    /// <summary>Радиус слэма, тайлов.</summary>
    [DataField]
    public float SlamRadius = 2.4f;

    /// <summary>Урон слэма (Blunt).</summary>
    [DataField]
    public float SlamDamage = 35f;

    /// <summary>Нокдаун от слэма, сек.</summary>
    [DataField]
    public float SlamParalyze = 2f;

    /// <summary>Дистанция до цели, при которой босс начинает слэм.</summary>
    [DataField]
    public float SlamTriggerRange = 2.6f;

    // ── Стаггер (наказание за чардж в стену) ─────────────────────────────────

    /// <summary>Сколько секунд босс оглушён после удара в стену.</summary>
    [DataField]
    public float StaggerDuration = 3.5f;

    /// <summary>Множитель входящего урона во время стаггера.</summary>
    [DataField]
    public float StaggerDamageMultiplier = 2f;

    // ── Фаза 2 ───────────────────────────────────────────────────────────────

    /// <summary>Множитель кулдаунов на фазе 2 (короче = злее).</summary>
    [DataField]
    public float Phase2CooldownMultiplier = 0.65f;

    /// <summary>Время жизни морозного следа, сек.</summary>
    [DataField]
    public float FrostLifetime = 6f;

    // ── Прототипы и звуки ────────────────────────────────────────────────────

    [DataField]
    public EntProtoId WarningProto = "EffectGoliathWarning";

    [DataField]
    public EntProtoId FrostProto = "EffectGoliathFrost";

    [DataField]
    public SoundSpecifier WindupSound = new SoundPathSpecifier("/Audio/_Wega/Duel/goliath/windup.ogg");

    [DataField]
    public SoundSpecifier WallSound = new SoundPathSpecifier("/Audio/_Wega/Duel/goliath/wall.ogg");

    [DataField]
    public SoundSpecifier SlamSound = new SoundPathSpecifier("/Audio/_Wega/Duel/goliath/slam.ogg");

    [DataField]
    public SoundSpecifier TelegraphSound = new SoundPathSpecifier("/Audio/_Wega/Duel/goliath/telegraph.ogg");

    // ── Runtime ──────────────────────────────────────────────────────────────

    /// <summary>Разовая инициализация выполнена (максимальный рост и т.п.).</summary>
    [ViewVariables]
    public bool SetupDone;

    [ViewVariables]
    public GoliathState State = GoliathState.Idle;

    /// <summary>Когда завершается текущий замах (Slam/ChargeWindup).</summary>
    [ViewVariables]
    public TimeSpan StateEndsAt;

    /// <summary>Направление текущего чарджа (мировые координаты, нормализовано).</summary>
    [ViewVariables]
    public Vector2 ChargeDir;

    /// <summary>Сколько тайлов чарджа осталось пролететь.</summary>
    [ViewVariables]
    public float ChargeRemaining;

    /// <summary>Кого уже протаранили в текущем чардже (урон один раз за рывок).</summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> ChargeHit = new();

    /// <summary>Накопитель пути для морозного следа (капля наледи раз в ~0.7 тайла).</summary>
    [ViewVariables]
    public float FrostAccumulator;

    /// <summary>До какого момента босс оглушён (стаггер). null = в строю.</summary>
    [ViewVariables]
    public TimeSpan? StaggeredUntil;

    [ViewVariables]
    public TimeSpan NextCharge;

    [ViewVariables]
    public TimeSpan NextSlam;

    /// <summary>Последняя увиденная фаза BossArenaBoss — для разового скейла молота и эмоута.</summary>
    [ViewVariables]
    public int LastPhase;

    /// <summary>Базовый урон молота в руке (до фазовых множителей).</summary>
    [ViewVariables]
    public Content.Shared.Damage.DamageSpecifier? HammerBaseDamage;
}
