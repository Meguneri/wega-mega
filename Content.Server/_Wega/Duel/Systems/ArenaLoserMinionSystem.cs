using System.Linq;
using System.Numerics;
using Content.Shared._Wega.Duel.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Система миньона-помощника для проигравшего 3 дуэли подряд.
/// </summary>
public sealed class ArenaLoserMinionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private PhysicsSystem _physics = default!;
    [Dependency] private DamageableSystem _damageable = default!;

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ArenaLoserMinionComponent, TransformComponent, PhysicsComponent>();

        while (query.MoveNext(out var uid, out var minion, out var xform, out var phys))
        {
            // Если владелец был задан и умер/исчез — миньон тоже умирает.
            // Если владельца нет (спавн через меню), миньон просто стоит.
            if (minion.MinionOwner.IsValid() &&
                (!Exists(minion.MinionOwner) || TerminatingOrDeleted(minion.MinionOwner) ||
                 TryComp<MobStateComponent>(minion.MinionOwner, out var ownerMob) && ownerMob.CurrentState == Shared.Mobs.MobState.Dead))
            {
                QueueDel(uid);
                continue;
            }

            if (now < minion.NextAction)
                continue;

            minion.NextAction = now + TimeSpan.FromSeconds(minion.ActionCooldown);

            // Без владельца останавливаемся и ничего не делаем.
            if (!minion.MinionOwner.IsValid())
            {
                _physics.SetLinearVelocity(uid, Vector2.Zero, body: phys);
                continue;
            }

            var ownerXform = Transform(minion.MinionOwner);
            var ownerPos = _transform.GetWorldPosition(ownerXform);
            var minionPos = _transform.GetWorldPosition(xform);

            // Следуем за владельцем, если отстали.
            var offset = ownerPos - minionPos;
            if (offset.Length() > minion.FollowRadius)
            {
                var dir = offset.Normalized();
                _physics.SetLinearVelocity(uid, dir * minion.MoveSpeed, body: phys);
            }
            else
            {
                _physics.SetLinearVelocity(uid, Vector2.Zero, body: phys);
            }

            // Если владелец ранен — лечим.
            if (TryComp<DamageableComponent>(minion.MinionOwner, out var damageable) &&
                _damageable.GetTotalDamage((minion.MinionOwner, damageable)) > 0)
            {
                HealOwner(uid, minion);
                continue;
            }

            // Иначе ищем цель и стреляем.
            if (TryFindTarget(minion, ownerPos, out var target))
                ShootAt(uid, minion, target);
        }
    }

    /// <summary>
    /// Спавнит миньона-помощника рядом с указанным владельцем.
    /// </summary>
    public EntityUid SpawnMinion(EntityUid owner, EntityCoordinates coords)
    {
        var minion = Spawn("ArenaLoserMinion", coords);
        Log.Info($"[duel-arena-loserminion] SpawnMinion: spawned entity {ToPrettyString(minion)} at {coords} for owner {ToPrettyString(owner)}");
        if (TryComp<ArenaLoserMinionComponent>(minion, out var comp))
            comp.MinionOwner = owner;
        else
            Log.Warning($"[duel-arena-loserminion] SpawnMinion: spawned entity {ToPrettyString(minion)} does not have ArenaLoserMinionComponent!");

        return minion;
    }

    private bool TryFindTarget(ArenaLoserMinionComponent minion, Vector2 ownerPos, out EntityUid target)
    {
        target = default;
        EntityUid? closest = null;
        var closestDist = float.MaxValue;

        var query = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var mob, out var xform))
        {
            if (uid == minion.MinionOwner)
                continue;

            // Цель должна быть на той же карте и жива/в криту (не мёртва/гибнута).
            if (xform.MapID != Transform(minion.MinionOwner).MapID)
                continue;
            if (mob.CurrentState == Shared.Mobs.MobState.Dead)
                continue;

            var pos = _transform.GetWorldPosition(xform);
            var dist = (pos - ownerPos).Length();
            if (dist > minion.AttackRadius || dist >= closestDist)
                continue;

            closest = uid;
            closestDist = dist;
        }

        if (closest == null)
            return false;

        target = closest.Value;
        return true;
    }

    private void ShootAt(EntityUid minionUid, ArenaLoserMinionComponent minion, EntityUid target)
    {
        var start = _transform.GetWorldPosition(minionUid);
        var end = _transform.GetWorldPosition(target);
        var dir = (end - start).Normalized();
        if (dir.Length() == 0)
            dir = Vector2.UnitX;

        var spawnPos = new MapCoordinates(start + dir * 0.5f, Transform(minionUid).MapID);
        var projectile = Spawn(minion.ProjectileProto, spawnPos);
        if (TryComp<PhysicsComponent>(projectile, out var phys))
        {
            _physics.SetLinearVelocity(projectile, dir * minion.ProjectileSpeed, body: phys);
        }
    }

    private void HealOwner(EntityUid minionUid, ArenaLoserMinionComponent minion)
    {
        if (TryComp<DamageableComponent>(minion.MinionOwner, out var damageable))
        {
            var target = new Entity<DamageableComponent?>(minion.MinionOwner, damageable);
            _damageable.HealEvenly(target, -FixedPoint2.New(minion.HealAmount), origin: minionUid);
        }
    }
}
