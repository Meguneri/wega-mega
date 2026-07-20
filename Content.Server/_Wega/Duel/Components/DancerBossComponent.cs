using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Components;

/// <summary>Состояние Танцовщицы.</summary>
public enum DancerState : byte
{
    Idle,
    /// <summary>Замах внутреннего вращения: кольцо радиуса ~2 на полу.</summary>
    SpinInner,
    /// <summary>Замах внешнего вращения: кольцо 2..4 на полу (двойной ритм — обман таймингов).</summary>
    SpinOuter,
    /// <summary>Второе дыхание: стоит на коленях, неуязвима, поднимется злее.</summary>
    Kneeling,
}

/// <summary>
/// Арена-босс «Пепельная танцовщица» — быстрый DS3-босс: двойные телеграфированные вращения
/// (внутреннее кольцо → внешнее), телепорт за спину кайтящей цели, усталость после трёх серий
/// (стаггер-окно ×2 урона) и второе дыхание — упав в крит, поднимается с 40% ХП, быстрее и
/// с поджигающими пол вращениями. Логика — <see cref="Systems.DancerBossSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class DancerBossComponent : Component
{
    // ── Вращения ─────────────────────────────────────────────────────────────

    /// <summary>Кулдаун серии вращений, сек (вторая жизнь — × <see cref="SecondLifeCooldownMultiplier"/>).</summary>
    [DataField]
    public float SpinCooldown = 6f;

    /// <summary>Замах каждого кольца, сек.</summary>
    [DataField]
    public float SpinWindup = 0.85f;

    /// <summary>Радиус внутреннего кольца.</summary>
    [DataField]
    public float InnerRadius = 2.1f;

    /// <summary>Внешний радиус второго кольца (бьёт по кольцу от <see cref="InnerRadius"/>-0.4 до этого).</summary>
    [DataField]
    public float OuterRadius = 4.2f;

    /// <summary>Урон внутреннего вращения (Slash).</summary>
    [DataField]
    public float InnerDamage = 22f;

    /// <summary>Урон внешнего вращения (Slash).</summary>
    [DataField]
    public float OuterDamage = 28f;

    /// <summary>Нокдаун от внешнего вращения, сек.</summary>
    [DataField]
    public float OuterParalyze = 1.2f;

    /// <summary>Дистанция до цели, при которой начинается серия вращений.</summary>
    [DataField]
    public float SpinTriggerRange = 2.8f;

    /// <summary>Сколько серий подряд до усталости (стаггер-окна).</summary>
    [DataField]
    public int CombosUntilExhausted = 3;

    /// <summary>Длительность усталости, сек.</summary>
    [DataField]
    public float ExhaustDuration = 2.5f;

    /// <summary>Множитель входящего урона в усталости.</summary>
    [DataField]
    public float ExhaustDamageMultiplier = 2f;

    // ── Телепорт ─────────────────────────────────────────────────────────────

    /// <summary>Дистанция до цели, с которой Танцовщица телепортируется за спину.</summary>
    [DataField]
    public float TeleportRange = 6.5f;

    /// <summary>Кулдаун телепорта, сек.</summary>
    [DataField]
    public float TeleportCooldown = 8f;

    // ── Второе дыхание ───────────────────────────────────────────────────────

    /// <summary>Доля ХП после подъёма (0.4 = 40%).</summary>
    [DataField]
    public float SecondLifeHealthFraction = 0.4f;

    /// <summary>Сколько секунд стоит на коленях (неуязвима, пауза-передышка для всех).</summary>
    [DataField]
    public float KneelDuration = 3f;

    /// <summary>Множитель кулдаунов после второго дыхания.</summary>
    [DataField]
    public float SecondLifeCooldownMultiplier = 0.7f;

    /// <summary>Множитель урона клинков после второго дыхания.</summary>
    [DataField]
    public float SecondLifeDamageMultiplier = 1.25f;

    // ── Прототипы и звуки ────────────────────────────────────────────────────

    [DataField]
    public EntProtoId InnerWarningProto = "EffectDancerWarningInner";

    [DataField]
    public EntProtoId OuterWarningProto = "EffectDancerWarningOuter";

    [DataField]
    public EntProtoId InnerSpinProto = "EffectDancerSpinInner";

    [DataField]
    public EntProtoId OuterSpinProto = "EffectDancerSpinOuter";

    [DataField]
    public EntProtoId EmberProto = "EffectDancerEmber";

    [DataField]
    public EntProtoId AshProto = "EffectDancerAsh";

    [DataField]
    public EntProtoId RiseProto = "EffectDancerRise";

    [DataField]
    public SoundSpecifier SpinSound = new SoundPathSpecifier("/Audio/_Wega/Duel/dancer/spin.ogg");

    [DataField]
    public SoundSpecifier TeleportSound = new SoundPathSpecifier("/Audio/_Wega/Duel/dancer/teleport.ogg");

    [DataField]
    public SoundSpecifier RiseSound = new SoundPathSpecifier("/Audio/_Wega/Duel/dancer/rise.ogg");

    // ── Runtime ──────────────────────────────────────────────────────────────

    [ViewVariables]
    public DancerState State = DancerState.Idle;

    [ViewVariables]
    public TimeSpan StateEndsAt;

    [ViewVariables]
    public int ComboCount;

    /// <summary>До какого момента «выдохлась» (стаггер-окно). null = в строю.</summary>
    [ViewVariables]
    public TimeSpan? ExhaustedUntil;

    [ViewVariables]
    public TimeSpan NextSpin;

    [ViewVariables]
    public TimeSpan NextTeleport;

    /// <summary>Второе дыхание уже использовано (одно на бой).</summary>
    [ViewVariables]
    public bool SecondLifeUsed;

    /// <summary>Вторая жизнь активна: короче кулдауны, злее клинки, вращения поджигают пол.</summary>
    [ViewVariables]
    public bool SecondLife;
}
