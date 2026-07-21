using System.Numerics;
using Content.Server.Temperature.Components;
using Content.Shared._Wega.Arena.Cold;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Weather;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Arena.Cold;

/// <summary>
/// Server logic of the arena cold. Once a second: every living mob standing on an
/// <see cref="ArenaColdZoneComponent"/> grid/map freezes over unless it is within a
/// <see cref="HeatSourceComponent"/> radius (the Generator, steam hubs). Cold drives a screen frost overlay
/// (client), a gradual movement slow (<see cref="SharedArenaColdSystem"/>) and, past a threshold, a small
/// damage tick. Leaving the cold — or reaching warmth — lets the cold recede and everything fades back.
/// </summary>
public sealed partial class ArenaColdSystem : EntitySystem
{
    private static readonly ProtoId<DamageTypePrototype> ColdDamageType = "Cold";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedWeatherSystem _weather = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>Cold decay per second for a mob that has left the cold zone entirely.</summary>
    private const float OffZoneWarmPerSecond = 0.35f;

    private TimeSpan _nextTick;
    private DamageSpecifier _coldDamage = default!;

    public override void Initialize()
    {
        base.Initialize();
        _coldDamage = new DamageSpecifier(_proto.Index(ColdDamageType), FixedPoint2.New(1));

        SubscribeLocalEvent<ArenaColdZoneComponent, MapInitEvent>(OnZoneInit);
    }

    /// <summary>Tagging a grid/map as a cold zone also kicks off the snowfall over its whole map.</summary>
    private void OnZoneInit(Entity<ArenaColdZoneComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Weather is not { } weather)
            return;

        var mapId = Transform(ent).MapID;
        if (mapId != MapId.Nullspace)
            _weather.TrySetWeather(mapId, weather, out _);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextTick)
            return;
        _nextTick = _timing.CurTime + Interval;

        const float dt = 1f; // seconds since last tick
        var heat = CollectHeatSources();

        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var uid, out var mob, out var xform))
        {
            if (mob.CurrentState == MobState.Dead)
                continue;

            var zone = GetColdZone(xform);
            if (zone == null)
            {
                // Not in a cold zone — thaw out, then drop the component when fully warm.
                if (TryComp<ColdExposureComponent>(uid, out var warming))
                {
                    warming.Level -= OffZoneWarmPerSecond * dt;
                    if (warming.Level <= 0f)
                        RemComp<ColdExposureComponent>(uid);
                    else
                        Dirty(uid, warming);

                    // Recompute speed either way so the slow lifts as the mob thaws.
                    _speed.RefreshMovementSpeedModifiers(uid);
                }
                continue;
            }

            var warm = IsWarm(xform, heat);
            var cold = EnsureComp<ColdExposureComponent>(uid);
            var protectedNow = HasColdProtection(uid);

            var beforeLevel = cold.Level;
            cold.Level = Math.Clamp(
                cold.Level + (warm ? -zone.WarmPerSecond : zone.ColdPerSecond) * dt,
                0f, 1f);

            var changed = !MathHelper.CloseTo(cold.Level, beforeLevel);
            if (cold.Protected != protectedNow)
            {
                cold.Protected = protectedNow;
                changed = true;
            }

            if (changed)
            {
                Dirty(uid, cold);
                _speed.RefreshMovementSpeedModifiers(uid);
            }

            // ignoreResistances: the arena cold is deliberate — no innate species resistance (vulpkanins included).
            // A winter coat is the one thing that helps, and only halfway.
            if (cold.Level >= cold.DamageThreshold)
                _damageable.TryChangeDamage(uid, protectedNow ? _coldDamage * 0.5f : _coldDamage, ignoreResistances: true);
        }
    }

    private Dictionary<MapId, List<(Vector2 Pos, float RadiusSq)>> CollectHeatSources()
    {
        var result = new Dictionary<MapId, List<(Vector2, float)>>();
        var query = EntityQueryEnumerator<HeatSourceComponent, TransformComponent>();
        while (query.MoveNext(out _, out var heat, out var xform))
        {
            var mapId = xform.MapID;
            if (mapId == MapId.Nullspace)
                continue;

            if (!result.TryGetValue(mapId, out var list))
                result[mapId] = list = new List<(Vector2, float)>();

            list.Add((_xform.GetWorldPosition(xform), heat.Radius * heat.Radius));
        }
        return result;
    }

    private ArenaColdZoneComponent? GetColdZone(TransformComponent xform)
    {
        if (xform.GridUid != null && TryComp<ArenaColdZoneComponent>(xform.GridUid, out var gridZone))
            return gridZone;
        if (xform.MapUid != null && TryComp<ArenaColdZoneComponent>(xform.MapUid, out var mapZone))
            return mapZone;
        return null;
    }

    private bool IsWarm(TransformComponent xform, Dictionary<MapId, List<(Vector2 Pos, float RadiusSq)>> heat)
    {
        if (!heat.TryGetValue(xform.MapID, out var sources))
            return false;

        var pos = _xform.GetWorldPosition(xform);
        foreach (var (sourcePos, radiusSq) in sources)
        {
            if ((pos - sourcePos).LengthSquared() <= radiusSq)
                return true;
        }
        return false;
    }

    /// <summary>True if the mob wears insulating outerwear (a winter coat) — partial cold protection.</summary>
    private bool HasColdProtection(EntityUid uid)
    {
        var slots = _inventory.GetSlotEnumerator(uid, SlotFlags.OUTERCLOTHING);
        while (slots.NextItem(out var item, out _))
        {
            if (HasComp<TemperatureProtectionComponent>(item))
                return true;
        }
        return false;
    }
}
