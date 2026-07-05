using System.Linq;
using System.Numerics;
using Content.Server._Wega.Duel.Components;
using Content.Server.Chat.Managers;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Server.NPC.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Система арест-бота. Управляет преследованием, оглушением, наручниками и доставкой цели к инициатору.
/// </summary>
public sealed class ArrestBotSystem : EntitySystem
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

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArrestBotRemoteComponent, AfterInteractEvent>(OnRemoteAfterInteract);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ArrestBotComponent>();
        while (query.MoveNext(out var uid, out var bot))
        {
            if (bot.State == ArrestBotState.Idle)
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
                case ArrestBotState.Stunned:
                    UpdateStunned(uid, bot, now);
                    break;
                case ArrestBotState.Cuffed:
                    UpdateCuffed(uid, bot, now);
                    break;
                case ArrestBotState.Delivering:
                    UpdateDelivering(uid, bot, now);
                    break;
            }
        }
    }

    private void OnRemoteAfterInteract(EntityUid uid, ArrestBotRemoteComponent remote, ref AfterInteractEvent args)
    {
        if (args.Target is not { } target)
            return;

        if (!HasComp<HumanoidProfileComponent>(target) || target == args.User)
            return;

        var user = args.User;

        if (FindNearestBot(user, remote.BotSearchRange) is not { } bot)
        {
            _popup.PopupEntity(Loc.GetString("arrest-bot-no-bot-nearby"), user, user, PopupType.SmallCaution);
            return;
        }

        CommandArrest(bot, target, user);
    }

    /// <summary>
    /// Отдаёт боту приказ задержать цель и доставить её инициатору.
    /// </summary>
    public void CommandArrest(EntityUid bot, EntityUid target, EntityUid issuer)
    {
        if (!TryComp<ArrestBotComponent>(bot, out var comp))
            return;

        comp.Target = target;
        comp.Issuer = issuer;
        comp.State = ArrestBotState.Pursuing;
        comp.TimeoutEndAt = _timing.CurTime + TimeSpan.FromSeconds(comp.Timeout);
        comp.NextAction = _timing.CurTime;

        _chat.DispatchServerAnnouncement(
            Loc.GetString("arrest-bot-commanded", ("target", MetaData(target).EntityName)), Color.Gold);

        // Заставляем бота идти к цели.
        MoveToTarget(bot, target);
    }

    private void UpdatePursuing(EntityUid uid, ArrestBotComponent bot, TimeSpan now)
    {
        if (bot.Target is not { } target)
            return;

        // Если цель уже в наручниках/оглушена — сразу переходим дальше.
        if (HasComp<StunnedComponent>(target) || IsCuffed(target))
        {
            bot.State = ArrestBotState.Stunned;
            return;
        }

        var botCoords = Transform(uid).Coordinates;
        var targetCoords = Transform(target).Coordinates;

        MoveToTarget(uid, target);

        if (InRange(uid, target, bot.ArrestRange) && now >= bot.NextAction)
        {
            bot.NextAction = now + TimeSpan.FromSeconds(bot.ActionCooldown);
            ApplyStun(uid, bot, target);
            bot.State = ArrestBotState.Stunned;
        }
    }

    private void UpdateStunned(EntityUid uid, ArrestBotComponent bot, TimeSpan now)
    {
        if (bot.Target is not { } target)
            return;

        if (!HasComp<StunnedComponent>(target) && now >= bot.NextAction)
        {
            // Повторный стан, если цель очнулась.
            ApplyStun(uid, bot, target);
            bot.NextAction = now + TimeSpan.FromSeconds(bot.ActionCooldown);
            return;
        }

        if (HasComp<StunnedComponent>(target) && now >= bot.NextAction)
        {
            bot.State = ArrestBotState.Cuffed;
            bot.NextAction = now + TimeSpan.FromSeconds(bot.ActionCooldown);
        }
    }

    private void UpdateCuffed(EntityUid uid, ArrestBotComponent bot, TimeSpan now)
    {
        if (bot.Target is not { } target)
            return;

        if (now < bot.NextAction)
            return;

        bot.NextAction = now + TimeSpan.FromSeconds(bot.ActionCooldown);

        if (IsCuffed(target))
        {
            _chat.DispatchServerAnnouncement(
                Loc.GetString("arrest-bot-cuffed", ("target", MetaData(target).EntityName)), Color.Gold);
            bot.State = ArrestBotState.Delivering;
            return;
        }

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

        // Убедимся, что бот тащит цель.
        if (!IsPulling(uid, target))
        {
            TryStartPull(uid, target);
        }

        MoveToTarget(uid, issuer);

        if (InRange(uid, issuer, bot.ArrestRange * 1.5f))
        {
            _chat.DispatchServerAnnouncement(
                Loc.GetString("arrest-bot-delivered", ("target", MetaData(target).EntityName)), Color.Gold);
            ResetBot(uid, bot, null);
        }
    }

    private void ApplyStun(EntityUid bot, ArrestBotComponent comp, EntityUid target)
    {
        _stun.TryKnockdown(target, TimeSpan.FromSeconds(comp.StunDuration), force: true);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/Guns/EmptyAlarm/smg_empty_alarm.ogg"), bot);
    }

    private void ApplyCuffs(EntityUid bot, ArrestBotComponent comp, EntityUid target)
    {
        if (!TryComp<CuffableComponent>(target, out var cuffable))
            return;

        var cuffs = Spawn(comp.HandcuffPrototype, Transform(target).Coordinates);
        if (!_cuff.TryAddNewCuffs(target, bot, cuffs, cuffable))
        {
            QueueDel(cuffs);
            return;
        }

        // После наручников пытаемся начать тащить цель.
        TryStartPull(bot, target);
    }

    private void TryStartPull(EntityUid bot, EntityUid target)
    {
        if (TryComp<PullableComponent>(target, out _) && TryComp<PullerComponent>(bot, out _))
        {
            _pulling.TryStartPull(bot, target);
        }
    }

    private bool IsCuffed(EntityUid target)
    {
        return TryComp<CuffableComponent>(target, out var cuffable) && cuffable.CuffedHandCount > 0;
    }

    private bool IsPulling(EntityUid puller, EntityUid pulled)
    {
        return TryComp<PullerComponent>(puller, out var pullerComp) && pullerComp.Pulling == pulled;
    }

    private void MoveToTarget(EntityUid uid, EntityUid target)
    {
        var coords = Transform(target).Coordinates;
        _steering.Register(uid, coords);
    }

    private bool InRange(EntityUid a, EntityUid b, float range)
    {
        var posA = Transform(a).MapPosition.Position;
        var posB = Transform(b).MapPosition.Position;
        return (posA - posB).Length() <= range;
    }

    private EntityUid? FindNearestBot(EntityUid user, float range)
    {
        var userPos = Transform(user).MapPosition.Position;
        EntityUid? nearest = null;
        var nearestDist = float.MaxValue;

        var query = EntityQueryEnumerator<ArrestBotComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            var pos = xform.MapPosition.Position;
            var dist = (pos - userPos).LengthSquared();
            if (dist > range * range)
                continue;

            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = uid;
            }
        }

        return nearest;
    }

    private void ResetBot(EntityUid uid, ArrestBotComponent bot, string? announceKey)
    {
        if (bot.Target is { } target && Exists(target))
        {
            // Если бот тащит цель — отпускаем.
            if (TryComp<PullerComponent>(uid, out var puller) && puller.Pulling == target)
                _pulling.TryStopPull(target, Comp<PullableComponent>(target));
        }

        bot.State = ArrestBotState.Idle;
        bot.Target = null;
        bot.Issuer = null;
        bot.TimeoutEndAt = null;
        bot.NextAction = TimeSpan.Zero;

        if (announceKey != null)
        {
            _chat.DispatchServerAnnouncement(Loc.GetString(announceKey), Color.Gray);
        }
    }
}
