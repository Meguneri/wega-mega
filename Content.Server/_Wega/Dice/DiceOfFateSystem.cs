using System.Linq;
using Content.Server._Wega.Duel.Components;
using Content.Server._Wega.Duel.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Antag;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Polymorph.Systems;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Systems;
using Content.Shared.Damage;
using Content.Shared.Antag;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Dice;
using Content.Shared.Disease;
using Content.Shared.Effects;
using Content.Shared.Explosion;
using Content.Shared.Gibbing;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.PDA;
using Content.Shared.Polymorph;
using Content.Shared.Popups;
using Content.Shared.Roles.Components;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Dice;

public sealed partial class DiceOfFateSystem : EntitySystem
{
    [Dependency] private SharedAccessSystem _access = default!;
    [Dependency] private IAdminLogManager _admin = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DuelArenaCleanupSystem _cleanup = default!;
    [Dependency] private SharedColorFlashEffectSystem _colorFlash = default!;
    [Dependency] private SharedPointLightSystem _pointLight = default!;
    [Dependency] private AntagSelectionSystem _antag = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedDiceSystem _dice = default!;
    [Dependency] private SharedDiseaseSystem _disease = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private GibbingSystem _gibbing = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PolymorphSystem _polymorph = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private RejuvenateSystem _rejuvenate = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Единственная подписка на каждое событие — перехватываем ДО SharedDiceSystem.
        // Внутри сами катим кубик и блокируем SharedDiceSystem через args.Handled.
        // Это обходит ограничение движка: нельзя иметь две подписки на один (comp, event).
        SubscribeLocalEvent<DiceOfFateComponent, UseInHandEvent>(OnUseInHand, before: [typeof(SharedDiceSystem)]);
        SubscribeLocalEvent<DiceOfFateComponent, LandEvent>(OnLand, before: [typeof(SharedDiceSystem)]);
    }

    private void OnUseInHand(Entity<DiceOfFateComponent> entity, ref UseInHandEvent args)
    {
        if (entity.Comp.Used || args.Handled)
            return;
        if (!TryComp<DiceComponent>(entity, out var dice))
            return;

        entity.Comp.Used = true;
        args.Handled = true;

        var roll = _random.Next(1, dice.Sides + 1);
        _dice.SetCurrentValue((entity.Owner, dice), roll);

        ApplyRollResult(entity.Owner, entity.Comp, dice, args.User);
        Timer.Spawn(TimeSpan.FromSeconds(1), () => { QueueDel(entity); });
    }

    private void OnLand(Entity<DiceOfFateComponent> entity, ref LandEvent args)
    {
        if (entity.Comp.Used || args.User == null)
            return;
        if (!TryComp<DiceComponent>(entity, out var dice))
            return;

        entity.Comp.Used = true;

        var roll = _random.Next(1, dice.Sides + 1);
        _dice.SetCurrentValue((entity.Owner, dice), roll);

        ApplyRollResult(entity.Owner, entity.Comp, dice, args.User.Value);
        Timer.Spawn(TimeSpan.FromSeconds(1), () => { QueueDel(entity); });
    }

    private void ApplyRollResult(EntityUid diceUid, DiceOfFateComponent comp, DiceComponent dice, EntityUid user)
    {
        if (comp.Arena)
        {
            // Арена: единый попап в стиле кубика лучника — число + что именно выпало.
            // Только бросивший видит результат, в админ-лог не пишем.
            var effect = Loc.GetString($"arena-war-die-effect-{dice.CurrentValue}");
            var arenaText = Loc.GetString("arena-war-die-rolled",
                ("roll", dice.CurrentValue), ("effect", effect));
            _popup.PopupEntity(arenaText, user, user, PopupType.Large);
            RollArena(user, dice.CurrentValue);
        }
        else
        {
            // Обычный кубик: публичный попап + звук для всех в зоне видимости + админ-лог.
            var text = Loc.GetString("dice-component-on-roll-land", ("die", diceUid), ("currentSide", dice.CurrentValue));
            _popup.PopupPredicted(text, diceUid, user);
            _audio.PlayPredicted(dice.Sound, diceUid, user);
            var success = RollClassic(user, dice.CurrentValue);
            _admin.Add(LogType.Action, LogImpact.Extreme,
                $"{ToPrettyString(user):user} rolls dice of fate: outcome '{success}', number {dice.CurrentValue}.");
        }
    }

    private static readonly ProtoId<DamageModifierSetPrototype> DamageMod = "DiceOfFateMod";
    private static readonly ProtoId<PolymorphPrototype> Monkey = "Monkey";
    private static readonly ProtoId<DiseasePrototype> Cold = "SpaceCold";
    private static readonly ProtoId<DamageTypePrototype> Damage = "Asphyxiation";
    private static readonly EntProtoId RandomAgressive = "RandomAgressiveAnimal";
    private static readonly EntProtoId RandomSpellbook = "RandomSpellbook";
    private static readonly EntProtoId Revolver = "WeaponRevolverInspector";
    private static readonly EntProtoId DefaultWizardRule = "Wizard";
    private static readonly EntProtoId Cookie = "FoodBakedCookie";
    private static readonly EntProtoId Servant = "PlushieLizard";
    private static readonly EntProtoId Toolbox = "ToolboxThief";
    private static readonly EntProtoId Cash = "SpaceCash10000";

    // ── Arena table ──────────────────────────────────────────────────────────
    private static readonly ProtoId<DamageModifierSetPrototype> ArenaArmorMod = "DiceOfFateMod"; // 0.5x damage taken
    private static readonly EntProtoId CombatMedkit = "MedkitCombatFilled";
    private static readonly EntProtoId Stim = "Stimpack";
    private static readonly EntProtoId MiniStim = "StimpackMini";
    private static readonly EntProtoId Shield = "EnergyShield";
    private static readonly EntProtoId StrongWeapon = "EnergySword";
    private static readonly EntProtoId StrongWeaponKatana = "EnergyKatana";
    private static readonly EntProtoId StrongWeaponBat = "HomerunBat";

    // Случайные оружия — нижний ярус (грани 7, 11, 19).
    private static readonly EntProtoId[] ArenaWeapons =
    {
        "WeaponPistolViper",       // пистолет
        "WeaponPistolMk58",        // стандартный пистолет
        "WeaponPistolCobra",       // пистолет с остановкой
        "WeaponPistolN1984",       // табельный пистолет
        "WeaponRevolverInspector", // револьвер инспектора
        "Cutlass",                 // абордажная сабля
        "Machete",                 // мачете
        "CombatKnife",             // боевой нож
        "FireAxe",                 // пожарный топор
        "EnergyDagger",            // энергокинжал
        "BaseBallBat",             // бейсбольная бита
        "WeaponShotgunDoubleBarreled", // двустволка
    };

    // Случайная броня (грань 5).
    private static readonly EntProtoId[] ArenaArmors =
    {
        "ClothingOuterArmorBasic",       // лёгкий бронежилет
        "ClothingOuterArmorRiot",        // антирадиационный
        "ClothingOuterArmorBulletproof", // пуленепробиваемый
        "ClothingOuterArmorReflective",  // отражающий (vs энергооружие)
        "ClothingOuterVestWeb",          // тактический жилет
        "ClothingOuterArmorHeavy",       // тяжёлая броня (замедляет)
        "ClothingOuterArmorScrap",       // металлолом
    };

    private const float ArenaBuffSeconds = 30f;
    private const float ArenaRushSeconds = 10f;

    public void RollFate(EntityUid user, int value)
    {
        var success = RollClassic(user, value);
        _admin.Add(LogType.Action, LogImpact.Extreme, $"{ToPrettyString(user):user} rolls dice of fate: outcome '{success}', number {value}.");
    }

    private bool RollClassic(EntityUid user, int value)
    {
        return value switch
        {
            1 => CompleteAnnihilation(user),
            2 => InstantDeath(user),
            3 => SummonAggressiveCreatures(user),
            4 => DestroyAllEquippedItems(user),
            5 => TransformIntoMonkey(user),
            6 => PermanentMovementSpeedReduction(user),
            7 => StunAndDamage(user),
            8 => ExplosionUser(user),
            9 => CommonCold(user),
            10 => NothingHappens(user),
            11 => SpawnCookie(user),
            12 => FullHealthRestoration(user),
            13 => SpawnMoney(user),
            14 => SpawnRevolver(user),
            15 => SpawnSpellbook(user),
            16 => SummonServant(user),
            17 => SuspiciousBeacon(user),
            18 => FullAccess(user),
            19 => PermanentDamageReduction(user),
            20 => BecomeWizard(user),
            _ => NothingHappens(user)
        };
    }

    private bool CompleteAnnihilation(EntityUid user)
    {
        _gibbing.Gib(user, true);
        return true;
    }

    private bool InstantDeath(EntityUid user)
    {
        var damage = new DamageSpecifier { DamageDict = { { Damage, 400 } } };
        _damage.ChangeDamage(user, damage, true);
        return true;
    }

    private bool SummonAggressiveCreatures(EntityUid user)
    {
        var count = _random.Next(3, 6);
        for (var i = 0; i < count; i++)
        {
            Spawn(RandomAgressive, Transform(user).Coordinates);
        }

        return true;
    }

    private bool DestroyAllEquippedItems(EntityUid user)
    {
        if (_inventory.TryGetSlots(user, out var slots))
        {
            foreach (var slot in slots)
            {
                if (_inventory.TryGetSlotEntity(user, slot.Name, out var ent))
                    QueueDel(ent);
            }
        }

        return true;
    }

    private bool TransformIntoMonkey(EntityUid user)
    {
        _polymorph.PolymorphEntity(user, Monkey);
        return true;
    }

    private bool PermanentMovementSpeedReduction(EntityUid user)
    {
        if (TryComp(user, out MovementSpeedModifierComponent? speedmod))
        {
            var originalWalkSpeed = speedmod.BaseWalkSpeed;
            var originalSprintSpeed = speedmod.BaseSprintSpeed;

            var multiplier = _random.NextFloat(0.3f, 0.95f);
            _speed.ChangeBaseSpeed(user, originalWalkSpeed * multiplier, originalSprintSpeed * multiplier, speedmod.Acceleration, speedmod);
        }

        return true;
    }

    private bool StunAndDamage(EntityUid user)
    {
        _stun.TryKnockdown(user, TimeSpan.FromSeconds(30));
        var damage = new DamageSpecifier { DamageDict = { { Damage, 50 } } };
        _damage.ChangeDamage(user, damage, true);

        return true;
    }

    private bool ExplosionUser(EntityUid user)
    {
        if (!ProtoMan.TryIndex(ExplosionSystem.DefaultExplosionPrototypeId, out ExplosionPrototype? type))
            return false;

        _explosion.QueueExplosion(user, type.ID, 5000f, 3f, 10f);
        return true;
    }

    private bool CommonCold(EntityUid user)
    {
        _disease.TryAddDisease(user, Cold);
        return true;
    }

    private bool NothingHappens(EntityUid user)
    {
        // I'm Lazy
        _popup.PopupEntity(Loc.GetString("reagent-desc-nothing"), user, user);
        return true;
    }

    private bool SpawnCookie(EntityUid user)
    {
        var cookie = Spawn(Cookie, Transform(user).Coordinates);
        _hands.TryForcePickupAnyHand(user, cookie);

        return true;
    }

    private bool FullHealthRestoration(EntityUid user)
    {
        _rejuvenate.PerformRejuvenate(user);
        return true;
    }

    private bool SpawnMoney(EntityUid user)
    {
        var cash = Spawn(Cash, Transform(user).Coordinates);
        _hands.TryForcePickupAnyHand(user, cash);

        return true;
    }

    private bool SpawnRevolver(EntityUid user)
    {
        var revolver = Spawn(Revolver, Transform(user).Coordinates);
        _hands.TryForcePickupAnyHand(user, revolver);

        return true;
    }

    private bool SpawnSpellbook(EntityUid user)
    {
        var spellbook = Spawn(RandomSpellbook, Transform(user).Coordinates);
        _hands.TryForcePickupAnyHand(user, spellbook);

        return true;
    }

    // Very goodluck
    private bool SummonServant(EntityUid user)
    {
        var servant = Spawn(Servant, Transform(user).Coordinates);
        _hands.TryForcePickupAnyHand(user, servant);

        return true;
    }

    private bool SuspiciousBeacon(EntityUid user)
    {
        var toolbox = Spawn(Toolbox, Transform(user).Coordinates);
        _hands.TryForcePickupAnyHand(user, toolbox);

        return true;
    }

    private bool FullAccess(EntityUid user)
    {
        var ent = FindActiveId(user);
        if (ent == null)
            return false;

        GiveAllAccess(ent.Value);
        return true;
    }

    private EntityUid? FindActiveId(EntityUid target)
    {
        if (_inventory.TryGetSlotEntity(target, "id", out var slotEntity))
        {
            if (HasComp<AccessComponent>(slotEntity))
            {
                return slotEntity.Value;
            }
            else if (TryComp<PdaComponent>(slotEntity, out var pda)
                && HasComp<IdCardComponent>(pda.ContainedId))
            {
                return pda.ContainedId;
            }
        }
        else if (TryComp<HandsComponent>(target, out var hands))
        {
            foreach (var held in _hands.EnumerateHeld((target, hands)))
            {
                if (HasComp<AccessComponent>(held))
                {
                    return held;
                }
            }
        }

        return null;
    }

    private void GiveAllAccess(EntityUid entity)
    {
        var allAccess = ProtoMan
            .EnumeratePrototypes<AccessLevelPrototype>()
            .Select(p => new ProtoId<AccessLevelPrototype>(p.ID)).ToArray();

        _access.TrySetTags(entity, allAccess);
    }

    private bool PermanentDamageReduction(EntityUid user)
    {
        _damage.SetDamageModifierSetId(user, DamageMod);
        return true;
    }

    private bool BecomeWizard(EntityUid user)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
            return false;

        _antag.ForceMakeAntag<WizardRoleComponent>(actor.PlayerSession, DefaultWizardRule);
        return true;
    }

    // ── Arena table ──────────────────────────────────────────────────────────
    // Боевой набор под дуэль: только полезные и нейтральные исходы. 1 — скромно, 20 — джекпот.
    // Каждая грань уникальна, ценность нарастает.
    private bool RollArena(EntityUid user, int value)
    {
        return value switch
        {
            1  => NothingHappens(user),             // ничего
            2  => ArenaMiniStim(user),              // микростим в руки
            3  => ArenaCombatMedkit(user),          // боевая аптечка в руки
            4  => ArenaCombatStim(user),            // боевой стим в руки
            5  => ArenaSpawnArmor(user),            // броня в руки
            6  => ArenaSpawnShield(user),           // энергощит в руки
            7  => ArenaRandomWeapon(user),          // случайное оружие в руки
            8  => ArenaRush(user),                  // рывок: ×1.8 скорость на 10 сек
            9  => ArenaAdrenaline(user),            // ×1.3 скорость на 30 сек
            10 => ArenaTemporaryArmor(user),        // ×0.5 урона на 30 сек
            11 => ArenaWeaponAndMiniStim(user),     // случайное оружие + микростим
            12 => ArenaMedkitAndStim(user),         // аптечка + стим
            13 => ArenaBerserk(user),               // скорость + броня на 30 сек
            14 => ArenaStrongWeapon(user),          // энергокатана с дэшем
            15 => ArenaRegen(user),                 // регенерация: +30 хп за 30 сек
            16 => ArenaStrongWeaponAndShield(user), // мощное оружие + энергощит
            17 => ArenaWarJackpot(user),            // бита с нокбеком + броня на 30 сек
            18 => ArenaBerserkAndMedkit(user),      // скорость + броня + аптечка
            19 => ArenaRegenAndWeapon(user),        // регенерация + случайное оружие
            20 => ArenaMegaJackpot(user),           // скорость + броня + мощное оружие
            _  => NothingHappens(user)
        };
    }

    private bool ArenaSpawnArmor(EntityUid user)
    {
        _hands.TryForcePickupAnyHand(user, SpawnArena(_random.Pick(ArenaArmors), user));
        return true;
    }

    private bool ArenaSpawnShield(EntityUid user)
    {
        var shield = Spawn(Shield, Transform(user).Coordinates);
        _hands.TryForcePickupAnyHand(user, shield);
        return true;
    }

    private bool ArenaMegaJackpot(EntityUid user)
    {
        ApplyTempGlow(user, GlowBerserk, ArenaBuffSeconds, EffectWave);
        TempSpeedMultiplier(user, 1.3f, ArenaBuffSeconds);
        TempDamageMod(user, ArenaArmorMod, ArenaBuffSeconds);
        _hands.TryForcePickupAnyHand(user, SpawnArena(StrongWeapon, user));
        return true;
    }

    private bool ArenaMiniStim(EntityUid user)
    {
        _hands.TryForcePickupAnyHand(user, SpawnArena(MiniStim, user));
        return true;
    }

    private bool ArenaCombatMedkit(EntityUid user)
    {
        _hands.TryForcePickupAnyHand(user, SpawnArena(CombatMedkit, user));
        return true;
    }

    private bool ArenaAdrenaline(EntityUid user)
    {
        ApplyTempGlow(user, GlowSpeed, ArenaBuffSeconds, EffectWave);
        TempSpeedMultiplier(user, 1.3f, ArenaBuffSeconds);
        return true;
    }

    private bool ArenaRandomWeapon(EntityUid user)
    {
        _hands.TryForcePickupAnyHand(user, SpawnArena(_random.Pick(ArenaWeapons), user));
        return true;
    }

    private bool ArenaTemporaryArmor(EntityUid user)
    {
        ApplyTempGlow(user, GlowArmor, ArenaBuffSeconds, EffectSparks);
        TempDamageMod(user, ArenaArmorMod, ArenaBuffSeconds);
        return true;
    }

    private bool ArenaCombatStim(EntityUid user)
    {
        _hands.TryForcePickupAnyHand(user, SpawnArena(Stim, user));
        return true;
    }

    // Грань 14: энергокатана с дэшем.
    private bool ArenaStrongWeapon(EntityUid user)
    {
        _hands.TryForcePickupAnyHand(user, SpawnArena(StrongWeaponKatana, user));
        return true;
    }

    private bool ArenaBerserk(EntityUid user)
    {
        ApplyTempGlow(user, GlowBerserk, ArenaBuffSeconds, EffectWave);
        TempSpeedMultiplier(user, 1.3f, ArenaBuffSeconds);
        TempDamageMod(user, ArenaArmorMod, ArenaBuffSeconds);
        return true;
    }

    // Грань 17: бита с нокбеком + временная броня — неожиданный вариант.
    private bool ArenaWarJackpot(EntityUid user)
    {
        ApplyTempGlow(user, GlowArmor, ArenaBuffSeconds, EffectSparks);
        TempDamageMod(user, ArenaArmorMod, ArenaBuffSeconds);
        _hands.TryForcePickupAnyHand(user, SpawnArena(StrongWeaponBat, user));
        return true;
    }

    // ── Новые уникальные исходы ──────────────────────────────────────────────

    // Грань 8: короткий но мощный рывок — ×1.8 скорость на 10 сек.
    private bool ArenaRush(EntityUid user)
    {
        ApplyTempGlow(user, GlowRush, ArenaRushSeconds, EffectWave);
        TempSpeedMultiplier(user, 1.8f, ArenaRushSeconds);
        return true;
    }

    // Грань 11: случайное оружие + микростим.
    private bool ArenaWeaponAndMiniStim(EntityUid user)
    {
        _hands.TryForcePickupAnyHand(user, SpawnArena(_random.Pick(ArenaWeapons), user));
        _hands.TryForcePickupAnyHand(user, SpawnArena(MiniStim, user));
        return true;
    }

    // Грань 12: боевая аптечка + стим.
    private bool ArenaMedkitAndStim(EntityUid user)
    {
        _hands.TryForcePickupAnyHand(user, SpawnArena(CombatMedkit, user));
        _hands.TryForcePickupAnyHand(user, SpawnArena(Stim, user));
        return true;
    }

    // Грань 15: периодическая регенерация — лечит постепенно за 30 сек.
    private bool ArenaRegen(EntityUid user)
    {
        ApplyTempGlow(user, GlowRegen, ArenaBuffSeconds, EffectRegen);
        const int ticks = 10;
        const float interval = ArenaBuffSeconds / ticks; // 3 сек на тик
        var heal = new DamageSpecifier { DamageDict =
        {
            { "Blunt",  -8 },
            { "Slash",  -8 },
            { "Pierce", -4 },
            { "Burn",   -8 },
            { "Shock",  -2 },
        }};
        for (var i = 1; i <= ticks; i++)
        {
            var delay = i;
            Timer.Spawn(TimeSpan.FromSeconds(interval * delay), () =>
            {
                if (!Deleted(user))
                    _damage.TryChangeDamage(user, heal, ignoreResistances: true);
            });
        }
        return true;
    }

    // Грань 16: мощное оружие + энергощит.
    private bool ArenaStrongWeaponAndShield(EntityUid user)
    {
        _hands.TryForcePickupAnyHand(user, SpawnArena(StrongWeapon, user));
        _hands.TryForcePickupAnyHand(user, SpawnArena(Shield, user));
        return true;
    }

    // Грань 18: берсерк (скорость + броня) + боевая аптечка.
    private bool ArenaBerserkAndMedkit(EntityUid user)
    {
        ApplyTempGlow(user, GlowBerserk, ArenaBuffSeconds, EffectWave);
        TempSpeedMultiplier(user, 1.3f, ArenaBuffSeconds);
        TempDamageMod(user, ArenaArmorMod, ArenaBuffSeconds);
        _hands.TryForcePickupAnyHand(user, SpawnArena(CombatMedkit, user));
        return true;
    }

    // Грань 19: регенерация + случайное оружие.
    private bool ArenaRegenAndWeapon(EntityUid user)
    {
        ArenaRegen(user);
        _hands.TryForcePickupAnyHand(user, SpawnArena(_random.Pick(ArenaWeapons), user));
        return true;
    }

    // ── Спавн арена-предметов ────────────────────────────────────────────────

    /// <summary>
    /// Спавнит предмет и сразу тегирует его <see cref="ArenaIssuedItemComponent"/>,
    /// чтобы очистка арены удалила его по окончании дуэли.
    /// </summary>
    // Спавнит предмет и рекурсивно тегирует его и всё вложенное ArenaIssuedItemComponent.
    private EntityUid SpawnArena(EntProtoId proto, EntityUid user)
    {
        var uid = Spawn(proto, Transform(user).Coordinates);
        _cleanup.MarkIssuedRecursive(uid);
        return uid;
    }

    // ── Визуальные эффекты баффов ────────────────────────────────────────────

    // Счётчик активных свечений: если > 0, PointLight держится на персонаже.
    private readonly Dictionary<EntityUid, int> _activeGlows = new();

    private static readonly Color GlowRush    = Color.FromHex("#FFD700"); // золотой   — рывок
    private static readonly Color GlowSpeed   = Color.FromHex("#00CFFF"); // голубой   — адреналин
    private static readonly Color GlowArmor   = Color.FromHex("#3A6FFF"); // синий     — броня
    private static readonly Color GlowBerserk = Color.FromHex("#AA44FF"); // фиолетовый — берсерк
    private static readonly Color GlowRegen   = Color.FromHex("#44FF88"); // зелёный   — регенерация

    /// <summary>
    /// Тройная пульсирующая вспышка + крупный попап над игроком (виден всем)
    /// + постоянное свечение на время баффа.
    /// </summary>
    // Готовые анимации эффектов под разные типы баффа.
    private static readonly EntProtoId EffectWave = "EffectGravityPulse"; // волна — скорость/берсерк
    private static readonly EntProtoId EffectSparks = "EffectSparks";     // искры — броня
    private static readonly EntProtoId EffectRegen = "EffectDiceRegen";   // голограмма — регенерация

    private void ApplyTempGlow(EntityUid user, Color color, float seconds, EntProtoId effect)
    {
        // Текст результата показывает единый попап броска (arena-war-die-rolled), поэтому здесь
        // подпись не дублируем — оставляем только визуал: эффект, вспышку и свечение на время баффа.

        // Анимация эффекта поверх игрока в момент срабатывания.
        Spawn(effect, Transform(user).Coordinates);

        // Тройная пульсирующая вспышка: 0 мс — 120 мс — 240 мс.
        for (var i = 0; i < 3; i++)
        {
            var delay = i * 120;
            Timer.Spawn(delay, () =>
            {
                if (!Deleted(user))
                    _colorFlash.RaiseEffect(color, [user], Filter.Pvs(user, entityManager: EntityManager));
            });
        }

        // Постоянное свечение на время баффа.
        _activeGlows.TryGetValue(user, out var count);
        _activeGlows[user] = count + 1;

        EnsureComp<PointLightComponent>(user, out var light);
        _pointLight.SetColor(user, color, light);
        _pointLight.SetEnergy(user, 2.5f, light);
        _pointLight.SetRadius(user, 3f, light);
        _pointLight.SetEnabled(user, true, light);

        Timer.Spawn(TimeSpan.FromSeconds(seconds), () =>
        {
            if (Deleted(user))
            {
                _activeGlows.Remove(user);
                return;
            }

            if (!_activeGlows.TryGetValue(user, out var c))
                return;

            if (c <= 1)
            {
                _activeGlows.Remove(user);
                RemCompDeferred<PointLightComponent>(user);
            }
            else
            {
                _activeGlows[user] = c - 1;
            }
        });
    }

    // ── Учёт перекрывающихся временных баффов ────────────────────────────────
    // Раньше каждый бафф запоминал ТЕКУЩУЮ базу/мод и восстанавливал её по своему таймеру.
    // При перекрытии двух баффов поздний таймер восстанавливал уже разогнанное значение —
    // бафф залипал навсегда. Теперь храним «чистое» состояние (до любых баффов) и список
    // активных множителей: база = чистая × произведение активных множителей; восстановление
    // в чистое — только когда спал последний бафф.
    private readonly Dictionary<EntityUid, (float Walk, float Sprint, float Accel)> _pristineSpeed = new();
    private readonly Dictionary<EntityUid, List<float>> _speedMults = new();
    private readonly Dictionary<EntityUid, (ProtoId<DamageModifierSetPrototype>? Original, int Count)> _damageBuffs = new();

    /// <summary>
    /// Временно умножает базовую скорость и возвращает исходную через <paramref name="seconds"/> секунд.
    /// Корректно стакается с другими временными баффами (множители перемножаются).
    /// </summary>
    private void TempSpeedMultiplier(EntityUid user, float multiplier, float seconds)
    {
        if (!TryComp<MovementSpeedModifierComponent>(user, out var move))
            return;

        // Чистую базу фиксируем один раз — пока на игроке нет ни одного активного множителя.
        if (!_pristineSpeed.ContainsKey(user))
            _pristineSpeed[user] = (move.BaseWalkSpeed, move.BaseSprintSpeed, move.Acceleration);

        var mults = _speedMults.GetOrNew(user);
        mults.Add(multiplier);
        ApplySpeedMults(user, move);

        Timer.Spawn(TimeSpan.FromSeconds(seconds), () =>
        {
            if (!_speedMults.TryGetValue(user, out var active))
                return;

            active.Remove(multiplier);

            if (Deleted(user) || !TryComp<MovementSpeedModifierComponent>(user, out var current))
            {
                _speedMults.Remove(user);
                _pristineSpeed.Remove(user);
                return;
            }

            if (active.Count == 0)
            {
                if (_pristineSpeed.TryGetValue(user, out var pristine))
                    _speed.ChangeBaseSpeed(user, pristine.Walk, pristine.Sprint, pristine.Accel, current);
                _speedMults.Remove(user);
                _pristineSpeed.Remove(user);
            }
            else
            {
                ApplySpeedMults(user, current);
            }
        });
    }

    private void ApplySpeedMults(EntityUid user, MovementSpeedModifierComponent move)
    {
        if (!_pristineSpeed.TryGetValue(user, out var pristine) || !_speedMults.TryGetValue(user, out var mults))
            return;

        var product = 1f;
        foreach (var m in mults)
            product *= m;

        _speed.ChangeBaseSpeed(user, pristine.Walk * product, pristine.Sprint * product, pristine.Accel, move);
    }

    /// <summary>
    /// Временно ставит набор модификаторов урона и возвращает исходный через <paramref name="seconds"/> секунд.
    /// Перекрывающиеся баффы считаются по рефкаунту: оригинал восстанавливается, когда спал последний.
    /// </summary>
    private void TempDamageMod(EntityUid user, ProtoId<DamageModifierSetPrototype> mod, float seconds)
    {
        if (!TryComp<DamageableComponent>(user, out var damageable))
            return;

        // Оригинал фиксируем только при первом баффе, иначе «оригиналом» станет сам бафф.
        if (_damageBuffs.TryGetValue(user, out var state))
            _damageBuffs[user] = (state.Original, state.Count + 1);
        else
            _damageBuffs[user] = (damageable.DamageModifierSetId, 1);

        _damage.SetDamageModifierSetId(user, mod);

        Timer.Spawn(TimeSpan.FromSeconds(seconds), () =>
        {
            if (!_damageBuffs.TryGetValue(user, out var cur))
                return;

            if (cur.Count <= 1)
            {
                if (!Deleted(user))
                    _damage.SetDamageModifierSetId(user, cur.Original);
                _damageBuffs.Remove(user);
            }
            else
            {
                _damageBuffs[user] = (cur.Original, cur.Count - 1);
            }
        });
    }
}
