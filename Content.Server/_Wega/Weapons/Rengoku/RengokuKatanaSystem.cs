using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Wega.Weapons.Rengoku;
using Content.Shared.Camera;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Weapons.Rengoku;

/// <summary>
/// Обрабатывает приёмы Дыхания Пламени катаны Рэнгоку.
/// Действия выдаются носителю через ItemActionGrant, поэтому события
/// действий приходят на саму катану (ent.Owner), а носитель — в args.Performer.
/// </summary>
public sealed partial class RengokuKatanaSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ThrowingSystem _throwing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RengokuKatanaComponent, RengokuFirstFormActionEvent>(OnFirstForm);
        SubscribeLocalEvent<RengokuKatanaComponent, RengokuNinthFormActionEvent>(OnNinthForm);
    }

    // === Первая форма: Неведомый огонь ===
    private void OnFirstForm(Entity<RengokuKatanaComponent> ent, ref RengokuFirstFormActionEvent args)
    {
        var comp = ent.Comp;
        var user = args.Performer;

        var origin = _transform.GetWorldPosition(user);
        var facing = _transform.GetWorldRotation(user).ToWorldVec();

        // Видимый веер пламени перед носителем — рисует сам взмах, даже если приём прошёл мимо.
        SpawnArc(user, facing, comp.FirstFormRadius * 0.7f, comp.FirstFormHalfAngle, comp.FirstFormArcCount, comp.FirstFormArcEffect);

        var hit = false;
        foreach (var target in _lookup.GetEntitiesInRange<MobStateComponent>(Transform(user).Coordinates, comp.FirstFormRadius))
        {
            if (target.Owner == user || _mobState.IsDead(target.Owner))
                continue;

            // Конус: цель должна быть в пределах полуугла от направления взгляда.
            var toTarget = _transform.GetWorldPosition(target.Owner) - origin;
            if (toTarget.LengthSquared() > 0.001f && !InCone(facing, toTarget, comp.FirstFormHalfAngle))
                continue;

            _damageable.TryChangeDamage(target.Owner, comp.FirstFormDamage, origin: user);
            _flammable.AdjustFireStacks(target.Owner, comp.FirstFormFireStacks, ignite: true);

            // Вспышка пламени прямо на задетой цели.
            if (comp.FirstFormEffect is { } hitEffect)
                Spawn(hitEffect, Transform(target.Owner).Coordinates);

            hit = true;
        }

        _audio.PlayPvs(comp.FirstFormSound, user);

        if (!hit)
            _popup.PopupEntity(Loc.GetString("rengoku-katana-first-form-miss"), user, user);

        args.Handled = true;
    }

    // === Девятая форма: Рэнгоку ===
    private void OnNinthForm(Entity<RengokuKatanaComponent> ent, ref RengokuNinthFormActionEvent args)
    {
        var comp = ent.Comp;
        var user = args.Performer;

        var facing = _transform.GetWorldRotation(user).ToWorldVec();
        if (facing.LengthSquared() < 0.001f)
            facing = new Vector2(1, 0);

        var direction = facing.Normalized() * comp.NinthFormRange;
        _throwing.TryThrow(user, direction, comp.NinthFormSpeed, compensateFriction: true);
        _audio.PlayPvs(comp.NinthFormChargeSound, user);

        // Боевой клич Пламенного столпа — виден всем рядом.
        _popup.PopupEntity(Loc.GetString("rengoku-katana-ninth-form-cry"), user, PopupType.LargeCaution);

        var flyTime = comp.NinthFormRange / comp.NinthFormSpeed;
        var katana = ent.Owner;

        // Огненный след: вспышки пламени в текущей позиции носителя по ходу рывка.
        if (comp.NinthFormTrailEffect is { } trail && comp.NinthFormTrailCount > 0)
        {
            for (var i = 0; i < comp.NinthFormTrailCount; i++)
            {
                var t = flyTime * i / comp.NinthFormTrailCount;
                Timer.Spawn(TimeSpan.FromSeconds(t), () =>
                {
                    if (!Deleted(user))
                        Spawn(trail, Transform(user).Coordinates);
                });
            }
        }

        Timer.Spawn(TimeSpan.FromSeconds(flyTime), () =>
        {
            if (Deleted(user) || Deleted(katana))
                return;

            DetonateNinthForm(katana, user);
        });

        args.Handled = true;
    }

    private void DetonateNinthForm(EntityUid katana, EntityUid user)
    {
        if (!TryComp<RengokuKatanaComponent>(katana, out var comp))
            return;

        var coords = Transform(user).Coordinates;

        // Урон, поджог, вспышка НА цели и тряска экрана — для каждого задетого.
        foreach (var target in _lookup.GetEntitiesInRange<MobStateComponent>(coords, comp.NinthFormRadius))
        {
            if (target.Owner == user || _mobState.IsDead(target.Owner))
                continue;

            _damageable.TryChangeDamage(target.Owner, comp.NinthFormDamage, origin: user);
            _flammable.AdjustFireStacks(target.Owner, comp.NinthFormFireStacks, ignite: true);
            ShakeCamera(target.Owner, comp.NinthFormShakeStrength);

            if (comp.NinthFormHitEffect is { } hitEffect)
                Spawn(hitEffect, Transform(target.Owner).Coordinates);
        }

        ShakeCamera(user, comp.NinthFormShakeStrength);

        // Раскатистый звук взрыва и центральная вспышка в точке приземления.
        _audio.PlayPvs(comp.NinthFormSound, user);
        if (comp.NinthFormEffect is { } effect)
            Spawn(effect, coords);

        // Огненное кольцо по краю радиуса поражения — взрыв «раскрывается».
        SpawnRing(_transform.GetMapCoordinates(user), comp.NinthFormRadius * 0.7f, comp.NinthFormRingCount, comp.NinthFormRingEffect);
    }

    /// <summary>Выкладывает <paramref name="proto"/> веером перед носителем, рисуя дугу взмаха.</summary>
    private void SpawnArc(EntityUid user, Vector2 facing, float radius, float halfAngleDegrees, int count, EntProtoId? proto)
    {
        if (proto is not { } effect || count <= 0)
            return;

        if (facing.LengthSquared() < 0.001f)
            facing = new Vector2(1, 0);

        var mapPos = _transform.GetMapCoordinates(user);
        var baseAngle = new Angle(facing);
        // 80% полуугла — веер держится внутри конуса поражения и выглядит аккуратнее.
        var spread = halfAngleDegrees * 0.8f;

        for (var i = 0; i < count; i++)
        {
            var frac = count == 1 ? 0.5f : (float)i / (count - 1);
            var angle = baseAngle + Angle.FromDegrees((frac - 0.5f) * 2f * spread);
            var pos = mapPos.Position + angle.ToVec() * radius;
            Spawn(effect, new MapCoordinates(pos, mapPos.MapId));
        }
    }

    /// <summary>Выкладывает <paramref name="proto"/> равномерным кольцом вокруг <paramref name="center"/>.</summary>
    private void SpawnRing(MapCoordinates center, float radius, int count, EntProtoId? proto)
    {
        if (proto is not { } effect || count <= 0)
            return;

        for (var i = 0; i < count; i++)
        {
            var angle = Angle.FromDegrees(360f * i / count);
            var pos = center.Position + angle.ToVec() * radius;
            Spawn(effect, new MapCoordinates(pos, center.MapId));
        }
    }

    private void ShakeCamera(EntityUid uid, float strength)
    {
        if (!HasComp<CameraRecoilComponent>(uid))
            return;

        var kick = _random.NextAngle().ToVec() * strength;
        _recoil.KickCamera(uid, kick);
    }

    /// <summary>Лежит ли вектор <paramref name="toTarget"/> в конусе вокруг <paramref name="facing"/>.</summary>
    private static bool InCone(Vector2 facing, Vector2 toTarget, float halfAngleDegrees)
    {
        var f = facing.Normalized();
        var t = toTarget.Normalized();
        var dot = Math.Clamp(Vector2.Dot(f, t), -1f, 1f);
        var angle = MathF.Acos(dot);
        return angle <= Angle.FromDegrees(halfAngleDegrees).Theta;
    }
}
