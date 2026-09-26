using System.Numerics;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Wega.Magic.IcicleStorm;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Wega.Magic.IcicleStorm;

public sealed partial class IcicleStormSystem : EntitySystem
{
    [Dependency] private TransformSystem _xform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MapSystem _mapManager = default!;
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private AudioSystem _audio = default!;

    // Звук каста. Проигрывается тут на сервере (PlayPvs), а не через Action.sound:
    // у этих заклинаний обработчик серверный, поэтому предсказанный звук Action
    // не доходит до самого кастера (его слышали бы только окружающие).
    private static readonly SoundSpecifier CastSound =
        new SoundPathSpecifier("/Audio/_Wega/Magic/ice_explosion.ogg");

    private const string ProjectilePrototype = "ProjectileIcicle";
    private const float ProjectileSpeed = 12f;
    private const float Range = 8f;
    private const int ProjectileCount = 12;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IcicleStormSpellEvent>(OnSpellCast);
        SubscribeLocalEvent<IcicleTripleShotSpellEvent>(OnTripleShot);
    }

    private void OnSpellCast(IcicleStormSpellEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var caster = args.Performer;
        _audio.PlayPvs(CastSound, caster);
        var casterXform = Transform(caster);
        var casterMapPos = _xform.GetMapCoordinates(caster);

        var inRange = new HashSet<EntityUid>();
        _lookup.GetEntitiesInRange(caster, Range, inRange, LookupFlags.Dynamic);
        inRange.Remove(caster);

        var targets = new List<EntityUid>();
        foreach (var entity in inRange)
        {
            if (HasComp<MobStateComponent>(entity))
                targets.Add(entity);
        }

        var spawnCoords = _mapManager.TryFindGridAt(casterMapPos, out var gridUid, out _)
            ? _xform.WithEntityId(casterXform.Coordinates, gridUid)
            : new EntityCoordinates(_map.GetMapOrInvalid(casterMapPos.MapId), casterMapPos.Position);

        var remaining = ProjectileCount;
        while (remaining > 0)
        {
            Vector2 direction;
            if (targets.Count > 0)
            {
                var target = _random.Pick(targets);
                var targetPos = _xform.GetMapCoordinates(target);
                var offset = _random.NextVector2(0.4f);
                direction = (targetPos.Position + offset) - casterMapPos.Position;
            }
            else
            {
                // no targets — shoot in random directions
                var angle = _random.NextFloat(0f, MathF.Tau);
                direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }

            var projectile = Spawn(ProjectilePrototype, spawnCoords);
            if (TryComp<ProjectileComponent>(projectile, out var proj))
                proj.Damage *= 1f; // базовый урон, можно масштабировать

            _gunSystem.ShootProjectile(projectile, direction, Vector2.Zero, caster, caster, ProjectileSpeed);
            remaining--;
        }
    }

    private void OnTripleShot(IcicleTripleShotSpellEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var caster = args.Performer;
        _audio.PlayPvs(CastSound, caster);
        var casterMapPos = _xform.GetMapCoordinates(caster);

        var spawnCoords = _mapManager.TryFindGridAt(casterMapPos, out var gridUid, out _)
            ? _xform.WithEntityId(Transform(caster).Coordinates, gridUid)
            : new EntityCoordinates(_map.GetMapOrInvalid(casterMapPos.MapId), casterMapPos.Position);

        var targetPos = _xform.ToMapCoordinates(args.Target).Position;
        var baseDirection = (targetPos - casterMapPos.Position).Normalized();

        // три сосульки веером: центр, -15°, +15°
        var spreads = new[] { 0f, -15f, 15f };
        foreach (var deg in spreads)
        {
            var angle = new Angle(baseDirection) + Angle.FromDegrees(deg);
            var dir = angle.ToVec();
            var projectile = Spawn(ProjectilePrototype, spawnCoords);
            _gunSystem.ShootProjectile(projectile, dir, Vector2.Zero, caster, caster, 25f);
        }
    }
}
