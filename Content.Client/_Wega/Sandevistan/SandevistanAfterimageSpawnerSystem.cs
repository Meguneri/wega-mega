using System.Collections.Generic;
using Content.Shared._Wega.Clothing.Sandevistan;
using Robust.Shared.Physics.Components;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Client._Wega.Sandevistan;

/// <summary>
/// Spawns the glowing blue "David Martinez" afterimage trail client-side for every active
/// Sandevistan user. Client-side so each ghost is left at the locally-predicted position and the
/// trail tracks the mover in real time, instead of trailing a network round-trip behind (which made
/// it sit far behind the model at high speed).
///
/// The ghosts are cosmetic clientside entities. Crucially, <see cref="SandevistanAfterimageComponent.SourceEntity"/>
/// is set *before* visuals are applied — adding the component fires its startup with SourceEntity
/// still unset, so we call <see cref="SandevistanAfterimageSystem.ApplyVisuals"/> explicitly afterwards.
/// They fade out via <see cref="TimedDespawnComponent"/>.
/// </summary>
public sealed partial class SandevistanAfterimageSpawnerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SandevistanAfterimageSystem _afterimage = default!;

    /// <summary>Per-user next-spawn time, kept client-local so networking never resets it.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextSpawn = new();

    // Below this speed the user is treated as standing still — no trail (ghosts would just pile up).
    private const float MinSpeedSq = 0.01f;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Spawn once per real tick, not during prediction re-runs (would duplicate the trail).
        if (!_timing.IsFirstTimePredicted)
            return;

        var curTime = _timing.CurTime;

        var query = EntityQueryEnumerator<SandevistanActiveComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var active, out var xform))
        {
            if (active.EndTime <= curTime)
            {
                _nextSpawn.Remove(uid);
                continue;
            }

            // Only trail while actually moving.
            if (TryComp<PhysicsComponent>(uid, out var body) && body.LinearVelocity.LengthSquared() < MinSpeedSq)
                continue;

            if (_nextSpawn.TryGetValue(uid, out var next) && curTime < next)
                continue;

            _nextSpawn[uid] = curTime + active.AfterimageInterval;

            var afterimage = Spawn(null, xform.Coordinates);

            var comp = EnsureComp<SandevistanAfterimageComponent>(afterimage);
            comp.SourceEntity = uid;
            comp.DirectionOverride = xform.LocalRotation.GetCardinalDir();
            comp.FadeDuration = (float) active.AfterimageLifetime.TotalSeconds;
            comp.BaseColor = active.AfterimageColor;
            _afterimage.ApplyVisuals((afterimage, comp));

            var despawn = EnsureComp<TimedDespawnComponent>(afterimage);
            despawn.Lifetime = comp.FadeDuration;
        }
    }
}
