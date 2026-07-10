using System.Linq;
using System.Numerics;
using Content.Shared._Wega.Duel.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Система миньона-помощника для проигравшего 3 дуэли подряд.
/// </summary>
public sealed partial class ArenaLoserMinionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private PhysicsSystem _physics = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedGunSystem _gun = default!;

    /// <summary>
    /// Удаляет всех миньонов, чьи владельцы входят в <paramref name="owners"/> (бойцы завершившегося
    /// боя), независимо от их позиции — иначе дрон «уезжает» со станции за победителем мимо радиусной
    /// очистки арены (<c>DuelArenaCleanupSystem.CleanupArea</c>).
    /// </summary>
    public void RemoveMinionsForOwners(ICollection<EntityUid> owners)
    {
        if (owners.Count == 0)
            return;

        var query = EntityQueryEnumerator<ArenaLoserMinionComponent>();
        while (query.MoveNext(out var uid, out var minion))
        {
            if (owners.Contains(minion.MinionOwner))
                QueueDel(uid);
        }
    }

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
                 TryComp<MobStateComponent>(minion.MinionOwner, out var ownerMob) && ownerMob.CurrentState == MobState.Dead))
            {
                QueueDel(uid);
                continue;
            }

            // Без владельца останавливаемся и ничего не делаем.
            if (!minion.MinionOwner.IsValid())
            {
                _physics.SetLinearVelocity(uid, Vector2.Zero, body: phys);
                continue;
            }

            var ownerXform = Transform(minion.MinionOwner);
            var ownerPos = _transform.GetWorldPosition(ownerXform);
            var minionPos = _transform.GetWorldPosition(xform);

            // Движение обновляем каждый тик, чтобы дрон не летел вслепую между действиями.
            var offset = ownerPos - minionPos;
            if (offset.Length() > minion.FollowRadius)
                _physics.SetLinearVelocity(uid, offset.Normalized() * minion.MoveSpeed, body: phys);
            else
                _physics.SetLinearVelocity(uid, Vector2.Zero, body: phys);

            if (now < minion.NextAction)
                continue;

            minion.NextAction = now + TimeSpan.FromSeconds(minion.ActionCooldown);

            // Лечим, только когда владелец просел ниже порога — иначе стреляем.
            if (ShouldHeal(minion))
            {
                HealOwner(uid, minion);
                continue;
            }

            if (TryFindTarget(uid, minion, ownerPos, out var target))
                ShootAt(uid, minion, target);
        }
    }

    /// <summary>
    /// Спавнит миньона-помощника рядом с указанным владельцем.
    /// Через <paramref name="enemies"/> передаётся список противников (обычно дуэлянты той же арены);
    /// владелец из него отфильтровывается автоматически.
    /// </summary>
    public EntityUid SpawnMinion(EntityUid owner, EntityCoordinates coords, IEnumerable<EntityUid>? enemies = null)
    {
        var minion = Spawn("ArenaLoserMinion", coords);
        Log.Debug($"[duel-arena-loserminion] SpawnMinion: spawned entity {ToPrettyString(minion)} at {coords} for owner {ToPrettyString(owner)}");
        if (TryComp<ArenaLoserMinionComponent>(minion, out var comp))
        {
            comp.MinionOwner = owner;
            if (enemies != null)
                comp.Enemies = enemies.Where(e => e != owner).ToList();
        }
        else
            Log.Warning($"[duel-arena-loserminion] SpawnMinion: spawned entity {ToPrettyString(minion)} does not have ArenaLoserMinionComponent!");

        return minion;
    }

    /// <summary>
    /// Лечить стоит, только если владелец ранен и его здоровье ниже <see cref="ArenaLoserMinionComponent.HealThreshold"/>
    /// (доля от порога крита). Если порог крита неизвестен — лечим при любом уроне, как раньше.
    /// </summary>
    private bool ShouldHeal(ArenaLoserMinionComponent minion)
    {
        if (!TryComp<DamageableComponent>(minion.MinionOwner, out var damageable))
            return false;

        var damage = _damageable.GetTotalDamage((minion.MinionOwner, damageable));
        if (damage <= FixedPoint2.Zero)
            return false;

        if (!_mobThreshold.TryGetThresholdForState(minion.MinionOwner, MobState.Critical, out var crit) ||
            crit.Value <= FixedPoint2.Zero)
            return true;

        var healthFraction = 1f - damage.Float() / crit.Value.Float();
        return healthFraction < minion.HealThreshold;
    }

    private bool TryFindTarget(EntityUid minionUid, ArenaLoserMinionComponent minion, Vector2 ownerPos, out EntityUid target)
    {
        target = default;
        EntityUid? closest = null;
        var closestDist = float.MaxValue;
        var ownerMapId = Transform(minion.MinionOwner).MapID;

        // Если известны противники по дуэли — стреляем только по ним.
        if (minion.Enemies.Count > 0)
        {
            foreach (var enemy in minion.Enemies)
            {
                if (!IsValidTarget(minionUid, minion, enemy, ownerPos, ownerMapId, out var dist) || dist >= closestDist)
                    continue;

                closest = enemy;
                closestDist = dist;
            }
        }
        else
        {
            var query = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out _))
            {
                if (!IsValidTarget(minionUid, minion, uid, ownerPos, ownerMapId, out var dist) || dist >= closestDist)
                    continue;

                closest = uid;
                closestDist = dist;
            }
        }

        if (closest == null)
            return false;

        target = closest.Value;
        return true;
    }

    private bool IsValidTarget(EntityUid minionUid, ArenaLoserMinionComponent minion, EntityUid candidate,
        Vector2 ownerPos, MapId ownerMapId, out float dist)
    {
        dist = float.MaxValue;

        if (candidate == minion.MinionOwner || !Exists(candidate) || TerminatingOrDeleted(candidate))
            return false;

        // По чужим дронам-помощникам не стреляем.
        if (HasComp<ArenaLoserMinionComponent>(candidate))
            return false;

        // Цель должна быть на той же карте и жива/в криту (не мертва).
        if (!TryComp<MobStateComponent>(candidate, out var mob) || mob.CurrentState == MobState.Dead)
            return false;

        var xform = Transform(candidate);
        if (xform.MapID != ownerMapId)
            return false;

        var pos = _transform.GetWorldPosition(xform);
        dist = (pos - ownerPos).Length();
        if (dist > minion.AttackRadius)
            return false;

        // Сквозь стены не стреляем. Дистанцию до цели меряем от владельца, а видимость — от
        // миньона, поэтому даём запас в FollowRadius.
        return _interaction.InRangeUnobstructed(minionUid, candidate,
            minion.AttackRadius + minion.FollowRadius, CollisionGroup.Opaque);
    }

    private void ShootAt(EntityUid minionUid, ArenaLoserMinionComponent minion, EntityUid target)
    {
        var start = _transform.GetWorldPosition(minionUid);
        var end = _transform.GetWorldPosition(target);
        var dir = end - start;
        dir = dir.LengthSquared() > 0.001f ? dir.Normalized() : Vector2.UnitX;

        var spawnPos = new MapCoordinates(start + dir * 0.5f, Transform(minionUid).MapID);
        var projectile = Spawn(minion.ProjectileProto, spawnPos);

        // Оружие — миньон, стрелок — владелец: снаряд игнорирует обоих,
        // а урон в админ-логах атрибутируется игроку.
        _gun.ShootProjectile(projectile, dir, Vector2.Zero, minionUid, minion.MinionOwner, minion.ProjectileSpeed);
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
