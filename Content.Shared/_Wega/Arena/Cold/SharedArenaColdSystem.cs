using Content.Shared.Movement.Systems;

namespace Content.Shared._Wega.Arena.Cold;

/// <summary>
/// Shared half of the arena cold: applies the movement slow from <see cref="ColdExposureComponent.Level"/>.
/// Runs on both sides so the slowdown is predicted; the actual cold accumulation and damage live in the
/// server-side <c>ArenaColdSystem</c>.
/// </summary>
public sealed class SharedArenaColdSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _speed = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ColdExposureComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnRefreshSpeed(Entity<ColdExposureComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var comp = ent.Comp;
        if (comp.Level <= comp.EffectThreshold)
            return;

        // Ramp the slow linearly from the effect threshold up to full cold; a winter coat softens it.
        var t = Math.Clamp((comp.Level - comp.EffectThreshold) / (1f - comp.EffectThreshold), 0f, 1f);
        var minMult = comp.Protected ? comp.ProtectedMinSpeedMultiplier : comp.MinSpeedMultiplier;
        var mult = 1f + (minMult - 1f) * t;
        args.ModifySpeed(mult);
    }
}
