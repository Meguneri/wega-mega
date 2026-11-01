// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from ArcaneSS14/arcane 81cd66873de20e597282565f27b686331e65f677.
using System.Numerics;
using System.Linq;
using Content.Shared._Wega.ArcaneSocial;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Wega.ArcaneSocial;

/// <summary>Maintains removable physical leads without leaving stale distance joints.</summary>
public sealed partial class ArcaneLeashSystem : EntitySystem
{
    [Dependency] private SharedJointSystem _joints = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ArcaneLeashComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<ArcaneLeashComponent, UseInHandEvent>(OnUse);
        SubscribeLocalEvent<ArcaneLeashComponent, DroppedEvent>(OnDrop);
        SubscribeLocalEvent<ArcaneLeashComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<ArcaneLeashComponent, ComponentShutdown>(OnLeashShutdown);
        SubscribeLocalEvent<ArcaneLeashComponent, GetVerbsEvent<InteractionVerb>>(OnLeashVerbs);
        SubscribeLocalEvent<ArcaneCollarComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<ArcaneLeashedComponent, ComponentShutdown>(OnTargetShutdown);
        SubscribeLocalEvent<ArcaneLeashedComponent, MoveEvent>(OnTargetMove);
        SubscribeLocalEvent<ArcaneLeashedComponent, GetVerbsEvent<InteractionVerb>>(OnTargetVerbs);
        SubscribeLocalEvent<ArcaneLeashAnchorComponent, MoveEvent>(OnAnchorMove);
        SubscribeLocalEvent<ArcaneLeashAnchorComponent, ComponentShutdown>(OnAnchorShutdown);
    }

    private void OnInteract(EntityUid uid, ArcaneLeashComponent comp, ref AfterInteractEvent args)
    {
        if (!args.Handled && args.CanReach && args.Target is { } target && TryAttach(uid, args.User, target))
            args.Handled = true;
    }

    private void OnUse(EntityUid uid, ArcaneLeashComponent comp, ref UseInHandEvent args)
    {
        if (!args.Handled && comp.Target != null)
        {
            Detach(uid, comp);
            args.Handled = true;
        }
    }

    /// <summary>Connects a held lead to a different entity wearing a compatible collar.</summary>
    public bool TryAttach(EntityUid leashUid, EntityUid user, EntityUid target)
    {
        if (!TryComp<ArcaneLeashComponent>(leashUid, out var leash) || leash.Target != null || user == target ||
            HasComp<ArcaneLeashedComponent>(target) || !_hands.IsHolding(user, leashUid, out _) ||
            !HasComp<PhysicsComponent>(user) || !HasComp<PhysicsComponent>(target) ||
            !_inventory.TryGetSlotEntity(target, "neck", out var collar) || !HasComp<ArcaneCollarComponent>(collar) ||
            !_interaction.InRangeUnobstructed(user, target) ||
            !float.IsFinite(leash.MaxDistance) || leash.MaxDistance < 0.5f || leash.MaxDistance > 10f)
            return false;

        var userXform = Transform(user);
        var targetXform = Transform(target);
        if (userXform.MapUid == null || userXform.MapUid != targetXform.MapUid)
            return false;

        leash.Target = target;
        EnsureComp<ArcaneLeashedComponent>(target).Leash = leashUid;
        SetAnchor(leashUid, leash, user);
        return true;
    }

    private void SetAnchor(EntityUid uid, ArcaneLeashComponent leash, EntityUid anchor)
    {
        RemoveJoint(uid, leash);
        if (leash.Target is not { } target || anchor == target || !HasComp<PhysicsComponent>(anchor))
        {
            Detach(uid, leash);
            return;
        }

        leash.Anchor = anchor;
        leash.JointId = "wega-arcane-lead-" + uid.ToString();
        EnsureComp<ArcaneLeashAnchorComponent>(anchor).Leashes.Add(uid);
        var joint = _joints.CreateDistanceJoint(anchor, target, id: leash.JointId);
        joint.MinLength = 0f;
        joint.MaxLength = leash.MaxDistance;
        Dirty(uid, leash);
    }

    private void RemoveJoint(EntityUid uid, ArcaneLeashComponent leash)
    {
        if (leash.Anchor is { } anchor)
        {
            if (leash.JointId is { } id)
                _joints.RemoveJoint(anchor, id);
            if (TryComp<ArcaneLeashAnchorComponent>(anchor, out var tracker))
                tracker.Leashes.Remove(uid);
        }
        leash.Anchor = null;
        leash.JointId = null;
    }

    /// <summary>Releases both ends and removes the physics constraint.</summary>
    public void Detach(EntityUid uid, ArcaneLeashComponent? leash = null)
    {
        if (!Resolve(uid, ref leash, false))
            return;
        var previousTarget = leash.Target;
        // Shutdown компонента вызывается сразу даже при отложенном удалении.
        // Сначала очищаем ссылку, чтобы обратный вызов не начал отсоединение повторно.
        leash.Target = null;
        RemoveJoint(uid, leash);
        if (previousTarget is { } target && TryComp<ArcaneLeashedComponent>(target, out var link) &&
            link.Leash == uid && link.LifeStage < ComponentLifeStage.Stopping && LifeStage(target) < EntityLifeStage.Terminating)
            RemCompDeferred<ArcaneLeashedComponent>(target);
        if (LifeStage(uid) < EntityLifeStage.Terminating)
            Dirty(uid, leash);
    }

    private void OnLeashShutdown(EntityUid uid, ArcaneLeashComponent comp, ComponentShutdown args) => Detach(uid, comp);

    private void OnTargetShutdown(EntityUid uid, ArcaneLeashedComponent comp, ComponentShutdown args)
    {
        if (TryComp<ArcaneLeashComponent>(comp.Leash, out var leash) && leash.Target == uid)
            Detach(comp.Leash, leash);
    }

    private void OnUnequipped(EntityUid uid, ArcaneCollarComponent comp, GotUnequippedEvent args)
    {
        if (args.Slot == "neck" && TryComp<ArcaneLeashedComponent>(args.EquipTarget, out var link) &&
            TryComp<ArcaneLeashComponent>(link.Leash, out var leash) && leash.Target == args.EquipTarget)
            Detach(link.Leash, leash);
    }

    private void OnDrop(EntityUid uid, ArcaneLeashComponent comp, ref DroppedEvent args)
    {
        if (comp.Target != null)
            SetAnchor(uid, comp, uid);
    }

    private void OnInserted(EntityUid uid, ArcaneLeashComponent comp, ref EntGotInsertedIntoContainerMessage args)
    {
        if (comp.Target == null)
            return;
        if (_hands.IsHolding(args.Container.Owner, uid, out _))
            SetAnchor(uid, comp, args.Container.Owner);
        else
            Detach(uid, comp);
    }

    private void CheckConnection(EntityUid uid, ArcaneLeashComponent comp, ref MoveEvent args)
    {
        if (comp.Target is not { } target || comp.Anchor is not { } anchor)
            return;
        var targetXform = Transform(target);
        var anchorXform = Transform(anchor);
        var limit = comp.MaxDistance + 1.5f;
        if (!float.IsFinite(limit) || targetXform.MapUid == null || targetXform.MapUid != anchorXform.MapUid ||
            Vector2.DistanceSquared(_transform.GetWorldPosition(targetXform), _transform.GetWorldPosition(anchorXform)) > limit * limit)
            Detach(uid, comp);
    }

    private void OnTargetMove(EntityUid uid, ArcaneLeashedComponent comp, ref MoveEvent args)
    {
        if (TryComp<ArcaneLeashComponent>(comp.Leash, out var leash))
            CheckConnection(comp.Leash, leash, ref args);
    }

    private void OnAnchorMove(EntityUid uid, ArcaneLeashAnchorComponent comp, ref MoveEvent args)
    {
        foreach (var leashUid in comp.Leashes.ToArray())
        {
            if (TryComp<ArcaneLeashComponent>(leashUid, out var leash))
                CheckConnection(leashUid, leash, ref args);
        }
    }

    private void OnAnchorShutdown(EntityUid uid, ArcaneLeashAnchorComponent comp, ComponentShutdown args)
    {
        foreach (var leashUid in comp.Leashes.ToArray())
        {
            if (TryComp<ArcaneLeashComponent>(leashUid, out var leash) && leash.Anchor == uid)
                Detach(leashUid, leash);
        }
    }

    private void OnLeashVerbs(EntityUid uid, ArcaneLeashComponent comp, GetVerbsEvent<InteractionVerb> args)
    {
        if (comp.Target != null && args.CanAccess && args.CanInteract)
            args.Verbs.Add(new InteractionVerb { Text = Loc.GetString("wega-arcane-leash-detach"), Act = () => Detach(uid) });
    }

    private void OnTargetVerbs(EntityUid uid, ArcaneLeashedComponent comp, GetVerbsEvent<InteractionVerb> args)
    {
        if (args.CanAccess && args.CanInteract)
            args.Verbs.Add(new InteractionVerb { Text = Loc.GetString("wega-arcane-leash-detach"), Act = () => Detach(comp.Leash) });
    }
}

/// <summary>Tracks the leads anchored to a holder or a dropped item.</summary>
[RegisterComponent]
public sealed partial class ArcaneLeashAnchorComponent : Component
{
    public HashSet<EntityUid> Leashes = new();
}
