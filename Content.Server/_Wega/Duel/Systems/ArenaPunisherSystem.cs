using System.Numerics;
using Content.Shared._Wega.Duel;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// «Каратель»: снаряд ставит клеймо на бойца, второй выстрел по заклеймённому детонирует его —
/// доп. урон и отбрасывание. Клеймо гаснет само по таймеру. Пока висит — над целью красный прицел.
/// </summary>
public sealed partial class ArenaPunisherSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    private static readonly EntProtoId BrandEffect = "ArenaBrandEffect";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BrandBoltComponent, ProjectileHitEvent>(OnBoltHit);
    }

    private void OnBoltHit(Entity<BrandBoltComponent> ent, ref ProjectileHitEvent args)
    {
        var target = args.Target;

        // Клеймим только живых бойцов — трупы не клеймим и не детонируем.
        if (!HasComp<MobStateComponent>(target) || _mobState.IsDead(target))
            return;

        var shooter = args.Shooter;

        if (TryComp<BrandedComponent>(target, out var branded) && branded.EndTime > _timing.CurTime)
        {
            // Детонация клейма.
            _damageable.TryChangeDamage(target, ent.Comp.DetonateDamage, origin: shooter);

            if (shooter != null)
            {
                var dir = _transform.GetMapCoordinates(target).Position
                          - _transform.GetMapCoordinates(shooter.Value).Position;
                if (dir.LengthSquared() > 0.001f)
                    _throwing.TryThrow(target, dir.Normalized() * ent.Comp.Knockback, ent.Comp.Knockback, doSpin: false);
            }

            RemoveBrand(target, branded);
            _popup.PopupEntity(Loc.GetString("arena-punisher-detonate"), target, PopupType.LargeCaution);
        }
        else
        {
            // Первое попадание — ставим (или обновляем) клеймо.
            branded ??= AddComp<BrandedComponent>(target);
            branded.EndTime = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.BrandDuration);
            branded.Source = shooter;

            // Красный прицел над целью, привязанный к ней.
            if (!Exists(branded.Effect))
                branded.Effect = SpawnAttachedTo(BrandEffect, new EntityCoordinates(target, Vector2.Zero));

            _popup.PopupEntity(Loc.GetString("arena-punisher-branded"), target, PopupType.Medium);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<BrandedComponent>();
        while (query.MoveNext(out var uid, out var branded))
        {
            if (branded.EndTime <= now)
                RemoveBrand(uid, branded);
        }
    }

    private void RemoveBrand(EntityUid uid, BrandedComponent branded)
    {
        if (Exists(branded.Effect))
            Del(branded.Effect.Value);

        RemComp<BrandedComponent>(uid);
    }
}
