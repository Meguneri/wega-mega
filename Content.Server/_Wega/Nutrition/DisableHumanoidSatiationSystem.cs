using Content.Server.Nutrition.EntitySystems;
using Content.Shared.CCVar;
using Content.Shared.Humanoid;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Nutrition;

/// <summary>
/// Removes hunger and thirst from every humanoid, including already spawned characters.
/// </summary>
public sealed partial class DisableHumanoidSatiationSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ServerSatiationSystem _satiation = default!;

    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck || !_cfg.GetCVar(WegaCVars.DisableHumanoidSatiation))
            return;

        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(1);
        var query = EntityQueryEnumerator<HumanoidProfileComponent, SatiationComponent>();
        while (query.MoveNext(out var uid, out _, out var satiation))
        {
            if (satiation.Has(SatiationSystem.Hunger))
                _satiation.RemoveSatiationType((uid, satiation), SatiationSystem.Hunger);

            if (satiation.Has(SatiationSystem.Thirst))
                _satiation.RemoveSatiationType((uid, satiation), SatiationSystem.Thirst);
        }
    }
}
