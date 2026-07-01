using System.Numerics;
using Content.Shared.Actions;
using Content.Shared.Throwing;
using Content.Shared._Wega.Magic.Telekinesis;
using Content.Shared.Gravity;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Magic.Telekinesis;

public sealed partial class TelekinesisSystem : EntitySystem
{
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private TransformSystem _xform = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedActionsSystem _actions = default!;

    private static readonly TimeSpan GrabDuration = TimeSpan.FromSeconds(10);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TelekinesisGrabSpellEvent>(OnGrab);
        SubscribeLocalEvent<TelekinesisThrowSpellEvent>(OnThrow);
    }

    private void OnGrab(TelekinesisGrabSpellEvent args)
    {
        if (args.Handled)
            return;

        var caster = args.Performer;
        var target = args.Target;

        if (!HasComp<MobStateComponent>(target))
            return;

        // если уже держит кого-то — освобождаем
        if (TryComp<TelekinesisHoldingComponent>(caster, out var holding))
            Release(caster, holding.Target, holding);

        args.Handled = true;

        _stun.TryAddParalyzeDuration(target, GrabDuration);
        EnsureComp<TelekinesisGrabbedComponent>(target);
        _hands.DropAll(target, checkActionBlocker: false, doDropInteraction: false);

        // левитация
        var gravComp = EnsureComp<GravityAffectedComponent>(target);
        gravComp.Weightless = true;

        var holdComp = EnsureComp<TelekinesisHoldingComponent>(caster);
        holdComp.Target = target;

        // выдаём кнопку броска
        _actions.AddAction(caster, ref holdComp.ThrowActionEntity, holdComp.ThrowActionProto);

        Timer.Spawn(GrabDuration, () =>
        {
            if (Exists(caster) && TryComp<TelekinesisHoldingComponent>(caster, out var h) && h.Target == target)
                Release(caster, target, h);
        });

        _popup.PopupEntity(Loc.GetString("telekinesis-grab-target"), target, target, PopupType.LargeCaution);
        _popup.PopupEntity(Loc.GetString("telekinesis-grab-caster", ("target", Name(target))), caster, caster, PopupType.Medium);
    }

    private void OnThrow(TelekinesisThrowSpellEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var caster = args.Performer;

        if (!TryComp<TelekinesisHoldingComponent>(caster, out var holding))
            return;

        var target = holding.Target;
        var targetPos = _xform.ToMapCoordinates(args.Target).Position;
        var casterPos = _xform.GetMapCoordinates(caster).Position;
        var delta = targetPos - casterPos;

        Release(caster, target, holding);

        // Прицел броска в собственную позицию даёт нулевой вектор: Normalized() вернул бы NaN
        // и подал бы NaN-импульс в физику. В этом вырожденном случае просто не бросаем.
        if (delta.LengthSquared() > 0f)
            _throwing.TryThrow(target, delta.Normalized() * 6f, 15f, caster);
    }

    private void Release(EntityUid caster, EntityUid target, TelekinesisHoldingComponent holding)
    {
        if (Exists(target) && HasComp<TelekinesisGrabbedComponent>(target))
        {
            _status.TryRemoveStatusEffect(target, SharedStunSystem.StunId);
            RemComp<TelekinesisGrabbedComponent>(target);
            // Снимаем левитацию полностью: компонент навешивает только этот спелл (в прототипах его нет),
            // поэтому удаляем его, а не просто гасим Weightless — иначе он вечно висел бы на цели.
            RemComp<GravityAffectedComponent>(target);
        }

        // убираем кнопку броска
        _actions.RemoveAction(caster, holding.ThrowActionEntity);
        RemComp<TelekinesisHoldingComponent>(caster);
    }
}
