using System.Numerics;
using Content.Shared._Wega.Duel.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Система миньона-помощника для проигравшего 3 дуэли подряд.
/// Дрон только следует за владельцем и лечит его — стрелять он не умеет (убрано намеренно,
/// чтобы поддержка отстающего не превращалась во вторую пушку на арене).
/// </summary>
public sealed partial class ArenaLoserMinionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private PhysicsSystem _physics = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;

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
            var dist = offset.Length();
            if (dist > minion.FollowRadius)
            {
                var dir = dist > 0 ? offset.Normalized() : Vector2.UnitX;
                _physics.SetLinearVelocity(uid, dir * minion.MoveSpeed, body: phys);
            }
            else
            {
                _physics.SetLinearVelocity(uid, Vector2.Zero, body: phys);
            }

            if (now < minion.NextAction)
                continue;

            // Лечим, только когда владелец рядом и ранен. Кулдаун взводим лишь при реальном
            // лечении — иначе, подлетев к раненому владельцу, дрон ждал бы полный цикл впустую.
            if (dist <= minion.FollowRadius + 0.5f && ShouldHeal(minion))
            {
                minion.NextAction = now + TimeSpan.FromSeconds(minion.ActionCooldown);
                HealOwner(uid, minion);
            }
        }
    }

    /// <summary>
    /// Спавнит миньона-помощника рядом с указанным владельцем.
    /// </summary>
    public EntityUid SpawnMinion(EntityUid owner, EntityCoordinates coords)
    {
        var minion = Spawn("ArenaLoserMinion", coords);
        if (TryComp<ArenaLoserMinionComponent>(minion, out var comp))
            comp.MinionOwner = owner;
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

    private void HealOwner(EntityUid minionUid, ArenaLoserMinionComponent minion)
    {
        if (TryComp<DamageableComponent>(minion.MinionOwner, out var damageable))
        {
            var target = new Entity<DamageableComponent?>(minion.MinionOwner, damageable);
            _damageable.HealEvenly(target, -FixedPoint2.New(minion.HealAmount), origin: minionUid);
        }
    }
}
