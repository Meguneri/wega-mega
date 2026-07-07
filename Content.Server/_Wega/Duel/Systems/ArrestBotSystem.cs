using System.Linq;
using Content.Server.NPC.Pathfinding;
using Content.Server._Wega.Duel.Components;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._Wega.Duel;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Implants.Components;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.NPC;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Trigger.Components.Triggers;
using Content.Shared.Trigger.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.Audio;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Система арестатора. Управляет преследованием, оглушением станнером,
/// снятием брони и одежды, наручниками, доставкой цели к инициатору и уходом в блюспейс.
/// </summary>
public sealed partial class ArrestBotSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedCuffableSystem _cuff = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private TriggerSystem _trigger = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private IRobustRandom _random = default!;

    private static readonly string[] DetainLines =
    {
        "arrest-bot-say-detain-1",
        "arrest-bot-say-detain-2",
        "arrest-bot-say-detain-3",
        "arrest-bot-say-detain-4",
    };

    private static readonly string[] StripLines =
    {
        "arrest-bot-say-strip-1",
        "arrest-bot-say-strip-2",
        "arrest-bot-say-strip-3",
        "arrest-bot-say-strip-4",
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArrestBotComponent, MapInitEvent>(OnBotMapInit);

        SubscribeLocalEvent<ArrestBotRemoteComponent, AfterInteractEvent>(OnRemoteAfterInteract);
        SubscribeLocalEvent<ArrestBotRemoteComponent, GotEquippedHandEvent>(OnRemoteEquipped);
        SubscribeLocalEvent<ArrestBotRemoteComponent, GotUnequippedHandEvent>(OnRemoteUnequipped);
        SubscribeLocalEvent<ArrestBotRemoteComponent, ArrestRemoteSelectMessage>(OnRemoteSelect);
        SubscribeLocalEvent<OpenArrestRemoteEvent>(OnRemoteAction);
    }

    private void OnBotMapInit(EntityUid uid, ArrestBotComponent component, MapInitEvent args)
    {
        // Без ActiveNPC система стиринга не двигает сущность, а HTN-мозга у арестатора нет.
        EnsureComp<ActiveNPCComponent>(uid);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ArrestBotComponent>();
        while (query.MoveNext(out var uid, out var bot))
        {
            if (bot.State == ArrestBotState.Idle)
                continue;

            // Арестатор сам выведен из строя — задержание отменяется.
            if (_mobState.IsIncapacitated(uid))
            {
                ResetBot(uid, bot, "arrest-bot-bot-down");
                continue;
            }

            // Арестатор сам оглушён — пережидает и продолжает.
            if (HasComp<StunnedComponent>(uid))
                continue;

            // Таймаут: если долго не может завершить — сброс.
            if (bot.TimeoutEndAt is { } timeout && now >= timeout)
            {
                ResetBot(uid, bot, "arrest-bot-timeout");
                continue;
            }

            // Цель или инициатор исчезли — сброс.
            if (bot.Target is { } target && (!Exists(target) || _mobState.IsDead(target)))
            {
                ResetBot(uid, bot, "arrest-bot-target-lost");
                continue;
            }

            if (bot.Issuer is { } issuer && !Exists(issuer))
            {
                ResetBot(uid, bot, "arrest-bot-issuer-lost");
                continue;
            }

            switch (bot.State)
            {
                case ArrestBotState.Pursuing:
                    UpdatePursuing(uid, bot, now);
                    break;
                case ArrestBotState.Stripping:
                    UpdateStripping(uid, bot, now);
                    break;
                case ArrestBotState.Cuffing:
                    UpdateCuffing(uid, bot, now);
                    break;
                case ArrestBotState.Delivering:
                    UpdateDelivering(uid, bot, now);
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ Commands

    /// <summary>
    /// Отдаёт арестатору приказ задержать цель и доставить её инициатору.
    /// </summary>
    public void CommandArrest(EntityUid bot, EntityUid target, EntityUid issuer)
    {
        if (!TryComp<ArrestBotComponent>(bot, out var comp) || bot == target)
            return;

        // Перед новым приказом отпускаем старую добычу.
        StopPulling(bot);
        comp.FailedStripSlots.Clear();
        comp.Spotted = false;

        // Включаем всё оружие с переключателем (например, стан-дубинку).
        foreach (var held in _hands.EnumerateHeld(bot))
        {
            if (HasComp<ItemToggleComponent>(held) && !_toggle.IsActivated(held))
                _toggle.TryActivate(held, bot);
        }

        // Боевой режим нужен для ударов оружием ближнего боя.
        if (TryComp<CombatModeComponent>(bot, out var combatMode))
            _combat.SetInCombatMode(bot, true, combatMode);

        comp.Target = target;
        comp.Issuer = issuer;
        comp.State = ArrestBotState.Pursuing;
        comp.TimeoutEndAt = _timing.CurTime + TimeSpan.FromSeconds(comp.Timeout);
        comp.NextAction = _timing.CurTime;

        _chat.DispatchServerAnnouncement(
            Loc.GetString("arrest-bot-commanded", ("target", MetaData(target).EntityName)), Color.Gold);

        MoveToTarget(bot, comp, target);
    }

    /// <summary>
    /// Находит ближайшего свободного арестатора к цели на её карте и отдаёт ему приказ.
    /// </summary>
    private bool CommandNearestBot(EntityUid target, EntityUid issuer)
    {
        if (FindNearestBot(target) is not { } bot)
            return false;

        CommandArrest(bot, target, issuer);
        return true;
    }

    // ------------------------------------------------------------------ State machine

    private void UpdatePursuing(EntityUid uid, ArrestBotComponent bot, TimeSpan now)
    {
        if (bot.Target is not { } target)
            return;

        // Как только цель попала в поле зрения — объявляем о преследовании.
        if (!bot.Spotted && HasLineOfSight(uid, target, bot.FiringRange * 2f))
        {
            bot.Spotted = true;
            Speak(uid, Loc.GetString("arrest-bot-say-spotted", ("target", MetaData(target).EntityName)));
        }

        if (EnsureSubdued(uid, bot, target, now))
        {
            Speak(uid, Loc.GetString(_random.Pick(DetainLines), ("target", MetaData(target).EntityName)));
            bot.State = ArrestBotState.Stripping;
        }
    }

    private void UpdateStripping(EntityUid uid, ArrestBotComponent bot, TimeSpan now)
    {
        if (bot.Target is not { } target)
            return;

        if (!EnsureSubdued(uid, bot, target, now))
            return;

        if (now < bot.NextAction)
            return;

        // Всё, что могло нести броню, снято — можно надевать наручники.
        if (FindNextStripSlot(target, bot) is not { } slot)
        {
            bot.State = ArrestBotState.Cuffing;
            return;
        }

        bot.NextAction = now + TimeSpan.FromSeconds(bot.ActionCooldown);
        StripSlot(uid, bot, target, slot);
    }

    private void UpdateCuffing(EntityUid uid, ArrestBotComponent bot, TimeSpan now)
    {
        if (bot.Target is not { } target)
            return;

        if (IsCuffed(target))
        {
            _chat.DispatchServerAnnouncement(
                Loc.GetString("arrest-bot-cuffed", ("target", MetaData(target).EntityName)), Color.Gold);

            // Освобождаем руки, чтобы тащить цель, и берём её на буксир.
            StowWeapons(uid);
            TryStartPull(uid, target);
            bot.State = ArrestBotState.Delivering;
            return;
        }

        if (!EnsureSubdued(uid, bot, target, now))
            return;

        if (now < bot.NextAction)
            return;

        bot.NextAction = now + TimeSpan.FromSeconds(bot.ActionCooldown);
        ApplyCuffs(uid, bot, target);
    }

    private void UpdateDelivering(EntityUid uid, ArrestBotComponent bot, TimeSpan now)
    {
        if (bot.Target is not { } target)
            return;

        if (bot.Issuer is not { } issuer)
        {
            ResetBot(uid, bot, "arrest-bot-issuer-lost");
            return;
        }

        // Убедимся, что конвоир тащит цель.
        if (!IsPulling(uid, target))
            TryStartPull(uid, target);

        MoveToTarget(uid, bot, issuer);

        if (InRange(uid, issuer, bot.ArrestRange * 1.5f))
        {
            _chat.DispatchServerAnnouncement(
                Loc.GetString("arrest-bot-delivered", ("target", MetaData(target).EntityName)), Color.Gold);

            // Сначала отпускаем цель у инициатора, потом уходим в блюспейс.
            ResetBot(uid, bot, null);
            ActivateBluespaceImplant(uid, bot);
        }
    }

    /// <summary>
    /// Держит цель обездвиженной и рядом: стреляет станнером с дистанции, если та ещё на ногах,
    /// подходит вплотную, если уже оглушена. Возвращает true, когда цель обездвижена и в упор.
    /// </summary>
    private bool EnsureSubdued(EntityUid uid, ArrestBotComponent bot, EntityUid target, TimeSpan now)
    {
        var down = HasComp<StunnedComponent>(target) || HasComp<KnockedDownComponent>(target) || IsCuffed(target);

        if (!down)
        {
            // Не на ногах ещё — стреляем с дистанции при прямой видимости.
            if (InRange(uid, target, bot.FiringRange) && HasLineOfSight(uid, target, bot.FiringRange) && now >= bot.NextAction)
            {
                bot.NextAction = now + TimeSpan.FromSeconds(bot.ActionCooldown);
                ApplyStun(uid, bot, target);
            }

            // В любом случае продолжаем сближение, чтобы потом снять вещи и надеть наручники.
            MoveToTarget(uid, bot, target);
            return false;
        }

        // Оглушена — подходим вплотную для снятия вещей и наручников.
        if (!InRange(uid, target, bot.ArrestRange))
        {
            MoveToTarget(uid, bot, target);
            return false;
        }

        // Уже вплотную и цель обездвижена — стоим спокойно, а не кружим вокруг.
        StopMoving(uid, bot);
        return true;
    }

    // ------------------------------------------------------------------ Actions

    private void ApplyStun(EntityUid bot, ArrestBotComponent comp, EntityUid target)
    {
        var meleeRange = InRange(bot, target, comp.ArrestRange);

        // Вплотную — реальный удар дубинкой; с дистанции — выстрел из станнера.
        if (!meleeRange && TryFireStunner(bot, target))
            return;

        if (TrySwingMelee(bot, target))
            return;

        if (TryFireStunner(bot, target))
            return;

        // Оружие выбили из рук — арестатора это не остановит.
        _audio.PlayPvs(comp.StunSound, bot);
    }

    private bool TryFireStunner(EntityUid bot, EntityUid target)
    {
        foreach (var held in _hands.EnumerateHeld(bot))
        {
            if (!TryComp<GunComponent>(held, out var gun))
                continue;

            var coords = Transform(target).Coordinates;
            return _gun.AttemptShoot(bot, (held, gun), coords, target);
        }

        return false;
    }

    private bool TrySwingMelee(EntityUid bot, EntityUid target)
    {
        foreach (var held in _hands.EnumerateHeld(bot))
        {
            if (!TryComp<MeleeWeaponComponent>(held, out var melee))
                continue;

            // Дубинка бьёт током только во включённом состоянии.
            if (HasComp<ItemToggleComponent>(held) && !_toggle.IsActivated(held))
                _toggle.TryActivate(held, bot);

            return _melee.AttemptLightAttack(bot, held, melee, target);
        }

        return false;
    }

    private string? FindNextStripSlot(EntityUid target, ArrestBotComponent bot)
    {
        foreach (var slot in bot.StripSlots)
        {
            if (bot.FailedStripSlots.Contains(slot))
                continue;

            if (_inventory.TryGetSlotEntity(target, slot, out _))
                return slot;
        }

        return null;
    }

    private void StripSlot(EntityUid bot, ArrestBotComponent comp, EntityUid target, string slot)
    {
        if (!_inventory.TryGetSlotEntity(target, slot, out var item)
            || !_inventory.TryUnequip(bot, target, slot, force: true))
        {
            // Не снимается — пропускаем, чтобы не застрять до таймаута.
            comp.FailedStripSlots.Add(slot);
            return;
        }

        _popup.PopupEntity(
            Loc.GetString("arrest-bot-strips",
                ("bot", MetaData(bot).EntityName),
                ("item", MetaData(item.Value).EntityName),
                ("target", MetaData(target).EntityName)),
            target, PopupType.MediumCaution);

        // Иногда сопровождаем раздевание репликой отыгрыша.
        if (_random.Prob(0.5f))
            Speak(bot, Loc.GetString(_random.Pick(StripLines), ("target", MetaData(target).EntityName)));
    }

    /// <summary>
    /// Прячет оружие из рук в снаряжение либо роняет на пол, освобождая руки для буксировки.
    /// </summary>
    private void StowWeapons(EntityUid bot)
    {
        var slots = new[] { "suitstorage", "belt", "back" };

        foreach (var held in _hands.EnumerateHeld(bot).ToArray())
        {
            var stowed = false;
            foreach (var slot in slots)
            {
                if (_inventory.TryEquip(bot, bot, held, slot, silent: true))
                {
                    stowed = true;
                    break;
                }
            }

            if (!stowed)
                _hands.TryDrop(bot, held, checkActionBlocker: false);
        }
    }

    private void Speak(EntityUid bot, string message)
    {
        _chatSystem.TrySendInGameICMessage(bot, message, InGameICChatType.Speak, ChatTransmitRange.Normal);
    }

    private void StopMoving(EntityUid uid, ArrestBotComponent bot)
    {
        if (HasComp<NPCSteeringComponent>(uid))
            _steering.Unregister(uid);
        bot.LastGoal = null;
    }

    private void ApplyCuffs(EntityUid bot, ArrestBotComponent comp, EntityUid target)
    {
        if (!TryComp<CuffableComponent>(target, out var cuffable))
            return;

        // Добивающий стан, чтобы цель не очнулась ровно в момент надевания наручников.
        _stun.TryUpdateParalyzeDuration(target, TimeSpan.FromSeconds(comp.StunDuration));

        var cuffs = Spawn(comp.HandcuffPrototype, Transform(target).Coordinates);
        if (!_cuff.TryAddNewCuffs(target, bot, cuffs, cuffable))
        {
            QueueDel(cuffs);
            return;
        }

        TryStartPull(bot, target);
    }

    /// <summary>
    /// Принудительно активирует имплант «Блюспейс коридор» — арестатор телепортируется прочь.
    /// </summary>
    private void ActivateBluespaceImplant(EntityUid bot, ArrestBotComponent comp)
    {
        if (!TryComp<ImplantedComponent>(bot, out var implanted))
            return;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities.ToArray())
        {
            if (!TryComp<TriggerOnActivateImplantComponent>(implant, out var trig))
                continue;

            if (MetaData(implant).EntityPrototype?.ID != comp.BluespaceImplant.Id)
                continue;

            // Как штатная активация импланта через действие — телепортирует носителя прочь.
            _trigger.Trigger(implant, bot, trig.KeyOut);
            return;
        }
    }

    // ------------------------------------------------------------------ Remote: direct tap

    private void OnRemoteAfterInteract(EntityUid uid, ArrestBotRemoteComponent remote, ref AfterInteractEvent args)
    {
        if (args.Target is not { } target)
            return;

        if (!IsValidTarget(target) || target == args.User)
            return;

        args.Handled = true;

        if (!CommandNearestBot(target, args.User))
            _popup.PopupEntity(Loc.GetString("arrest-bot-no-bot-nearby"), args.User, args.User, PopupType.SmallCaution);
    }

    // ------------------------------------------------------------------ Remote: action + UI

    private void OnRemoteEquipped(EntityUid uid, ArrestBotRemoteComponent remote, GotEquippedHandEvent args)
    {
        _actions.AddAction(args.User, ref remote.ActionEntity, remote.Action, uid);
    }

    private void OnRemoteUnequipped(EntityUid uid, ArrestBotRemoteComponent remote, GotUnequippedHandEvent args)
    {
        _actions.RemoveAction(remote.ActionEntity);
        remote.ActionEntity = null;
    }

    private void OnRemoteAction(OpenArrestRemoteEvent args)
    {
        if (args.Handled)
            return;

        // Событие действия — широковещательное; сам пульт берём из провайдера действия.
        if (args.Action.Comp.Container is not { } remote || !HasComp<ArrestBotRemoteComponent>(remote))
            return;

        args.Handled = true;

        if (!_ui.HasUi(remote, ArrestRemoteUiKey.Key))
            return;

        _ui.OpenUi(remote, ArrestRemoteUiKey.Key, args.Performer);
        UpdateRemoteUi(remote, args.Performer);
    }

    private void OnRemoteSelect(EntityUid uid, ArrestBotRemoteComponent remote, ArrestRemoteSelectMessage args)
    {
        var user = args.Actor;
        var target = GetEntity(args.Target);

        if (!Exists(target) || !IsValidTarget(target) || target == user)
            return;

        if (CommandNearestBot(target, user))
            _popup.PopupEntity(Loc.GetString("arrest-bot-dispatched", ("target", MetaData(target).EntityName)), user, user);
        else
            _popup.PopupEntity(Loc.GetString("arrest-bot-no-bot-nearby"), user, user, PopupType.SmallCaution);

        _ui.CloseUi(uid, ArrestRemoteUiKey.Key, user);
    }

    private void UpdateRemoteUi(EntityUid remote, EntityUid user)
    {
        if (!_ui.HasUi(remote, ArrestRemoteUiKey.Key))
            return;

        var grid = Transform(user).GridUid;
        var targets = new List<ArrestRemoteTarget>();

        var query = EntityQueryEnumerator<HumanoidProfileComponent, TransformComponent>();
        while (query.MoveNext(out var humanoid, out _, out var xform))
        {
            if (humanoid == user)
                continue;

            // Самих арестаторов задерживать нельзя.
            if (HasComp<ArrestBotComponent>(humanoid))
                continue;

            if (grid != null && xform.GridUid != grid)
                continue;

            if (_mobState.IsDead(humanoid))
                continue;

            targets.Add(new ArrestRemoteTarget
            {
                Entity = GetNetEntity(humanoid),
                Name = MetaData(humanoid).EntityName,
                Status = DescribeTarget(humanoid),
                AlreadyTargeted = IsBeingArrested(humanoid),
            });
        }

        targets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        _ui.SetUiState(remote, ArrestRemoteUiKey.Key, new ArrestRemoteBuiState(targets, AnyFreeBot()));
    }

    private string DescribeTarget(EntityUid target)
    {
        if (IsCuffed(target))
            return Loc.GetString("arrest-remote-status-cuffed");
        if (HasComp<StunnedComponent>(target) || HasComp<KnockedDownComponent>(target))
            return Loc.GetString("arrest-remote-status-down");
        if (IsBeingArrested(target))
            return Loc.GetString("arrest-remote-status-hunted");
        return Loc.GetString("arrest-remote-status-free");
    }

    // ------------------------------------------------------------------ Helpers

    private bool IsValidTarget(EntityUid target)
    {
        return HasComp<HumanoidProfileComponent>(target) && !HasComp<ArrestBotComponent>(target);
    }

    private bool IsBeingArrested(EntityUid target)
    {
        var query = EntityQueryEnumerator<ArrestBotComponent>();
        while (query.MoveNext(out _, out var bot))
        {
            if (bot.State != ArrestBotState.Idle && bot.Target == target)
                return true;
        }

        return false;
    }

    private bool AnyFreeBot()
    {
        var query = EntityQueryEnumerator<ArrestBotComponent>();
        while (query.MoveNext(out var uid, out var bot))
        {
            if (bot.State == ArrestBotState.Idle && !_mobState.IsIncapacitated(uid))
                return true;
        }

        return false;
    }

    private void TryStartPull(EntityUid bot, EntityUid target)
    {
        if (TryComp<PullableComponent>(target, out _) && TryComp<PullerComponent>(bot, out _))
            _pulling.TryStartPull(bot, target);
    }

    private void StopPulling(EntityUid bot)
    {
        if (TryComp<PullerComponent>(bot, out var puller) && puller.Pulling is { } pulling
            && TryComp<PullableComponent>(pulling, out var pullable))
            _pulling.TryStopPull(pulling, pullable, bot);
    }

    private bool IsCuffed(EntityUid target)
    {
        return TryComp<CuffableComponent>(target, out var cuffable) && cuffable.CuffedHandCount > 0;
    }

    private bool IsPulling(EntityUid puller, EntityUid pulled)
    {
        return TryComp<PullerComponent>(puller, out var pullerComp) && pullerComp.Pulling == pulled;
    }

    private bool HasLineOfSight(EntityUid bot, EntityUid target, float range)
    {
        return _interaction.InRangeUnobstructed(bot, target, range);
    }

    private void MoveToTarget(EntityUid uid, ArrestBotComponent bot, EntityUid target)
    {
        var coords = Transform(target).Coordinates;

        // Ключевой момент: НЕ перерегистрируем цель каждый тик. Register сбрасывает уже
        // найденный путь при любом изменении координат, из-за чего с движущейся целью бот
        // терял маршрут и упирался в стены. Обновляем цель только когда она заметно
        // сместилась либо когда бот застрял/дошёл до устаревшей точки.
        if (TryComp<NPCSteeringComponent>(uid, out var existing))
        {
            var goalMoved = bot.LastGoal is not { } last
                || !last.TryDistance(EntityManager, coords, out var dist)
                || dist > existing.RepathRange;

            if (!goalMoved && existing.Status == SteeringStatus.Moving)
                return;

            // Если цель сдвинулась или бот застрял в NoPath, сбрасываем счётчик неудач
            // поиска пути — старая ошибка могла быть вызвана закрытой дверью/препятствием,
            // которое уже исчезло.
            if (goalMoved || existing.Status == SteeringStatus.NoPath)
                existing.FailedPathCount = 0;
        }

        var steering = _steering.Register(uid, coords);

        // Конвоир может открывать двери (Access) и бампать их. Без этих флагов
        // pathfinding считает шлюзы непроходимыми и не строит маршрут через станцию.
        steering.Flags = PathFlags.Interact | PathFlags.Prying;

        // Register не сбрасывает залипший NoPath — форсируем повторный поиск пути.
        steering.Status = SteeringStatus.Moving;
        steering.FailedPathCount = 0;

        bot.LastGoal = coords;
    }

    private bool InRange(EntityUid a, EntityUid b, float range)
    {
        var posA = _transform.GetMapCoordinates(a);
        var posB = _transform.GetMapCoordinates(b);
        return posA.MapId == posB.MapId && (posA.Position - posB.Position).Length() <= range;
    }

    /// <summary>
    /// Ближайший свободный (в приоритете) арестатор к цели на её карте.
    /// </summary>
    private EntityUid? FindNearestBot(EntityUid target)
    {
        var targetPos = _transform.GetMapCoordinates(target);
        EntityUid? nearest = null;
        var nearestDist = float.MaxValue;
        var nearestIdle = false;

        var query = EntityQueryEnumerator<ArrestBotComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var bot, out _))
        {
            if (uid == target || _mobState.IsIncapacitated(uid))
                continue;

            var pos = _transform.GetMapCoordinates(uid);
            if (pos.MapId != targetPos.MapId)
                continue;

            var idle = bot.State == ArrestBotState.Idle;
            var dist = (pos.Position - targetPos.Position).LengthSquared();

            // Свободные арестаторы всегда предпочтительнее занятых.
            if (nearest == null || (idle && !nearestIdle) || (idle == nearestIdle && dist < nearestDist))
            {
                nearest = uid;
                nearestDist = dist;
                nearestIdle = idle;
            }
        }

        return nearest;
    }

    private void ResetBot(EntityUid uid, ArrestBotComponent bot, string? announceKey)
    {
        StopPulling(uid);

        // Останавливаемся, иначе стиринг продолжит вести к последней точке.
        _steering.Unregister(uid);

        if (TryComp<CombatModeComponent>(uid, out var combatMode))
            _combat.SetInCombatMode(uid, false, combatMode);

        bot.State = ArrestBotState.Idle;
        bot.Target = null;
        bot.Issuer = null;
        bot.TimeoutEndAt = null;
        bot.NextAction = TimeSpan.Zero;
        bot.FailedStripSlots.Clear();
        bot.Spotted = false;
        bot.LastGoal = null;

        if (announceKey != null)
            _chat.DispatchServerAnnouncement(Loc.GetString(announceKey), Color.Gray);
    }
}
