using System.Numerics;
using Content.Server.Body.Systems;
using Content.Shared.Body.Systems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._Wega.Magic.BloodRitual;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Wega.Magic.BloodRitual;

public sealed partial class BloodRitualSystem : EntitySystem
{
    [Dependency] private BloodstreamSystem _blood = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private Content.Shared.Mobs.Systems.MobStateSystem _mobState = default!;

    private const string BloodReagent = "Blood";
    private const string PentagramEffect = "BloodRuneRitualDimensionalRendingEffect";

    /// <summary>Radius of the ritual area of effect, in tiles.</summary>
    private const float Radius = 3f;

    private static readonly DamageSpecifier RitualDamage = new()
    {
        DamageDict = { { "Slash", 15 } },
    };

    /// <summary>Fraction of dealt damage returned to the caster as healing (life-steal).</summary>
    private const float LifeStealFraction = 0.4f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BloodRitualSpellEvent>(OnSpellCast);
    }

    private void OnSpellCast(BloodRitualSpellEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var caster = args.Performer;
        var coords = _transform.GetMapCoordinates(caster);

        // Draw the big pentagram under the caster.
        Spawn(PentagramEffect, coords);

        var totalDamage = FixedPoint2.Zero;

        foreach (var target in _lookup.GetEntitiesInRange<MobStateComponent>(coords, Radius))
        {
            if (target.Owner == caster || _mobState.IsDead(target.Owner))
                continue;

            if (_damageable.TryChangeDamage(target.Owner, RitualDamage, out var dealt, origin: caster))
                totalDamage += dealt.GetTotal();

            // Fully exsanguinate the victim — drains every drop of blood onto the floor.
            _blood.SpillAllSolutions(target.Owner);

            SpillBlood(target.Owner);
        }

        // Life-steal: the caster heals for a fraction of all damage drained.
        if (totalDamage > FixedPoint2.Zero)
        {
            var heal = new DamageSpecifier { DamageDict = { { "Slash", -(totalDamage.Float() * LifeStealFraction) } } };
            _damageable.TryChangeDamage(caster, heal);
        }

        _popup.PopupEntity(Loc.GetString("blood-ritual-cast"), caster, caster, PopupType.MediumCaution);
    }

    private void SpillBlood(EntityUid target)
    {
        var coords = Transform(target).Coordinates;

        for (var i = 0; i < 4; i++)
        {
            var offset = new Vector2(
                _random.NextFloat(-0.8f, 0.8f),
                _random.NextFloat(-0.8f, 0.8f));

            var solution = new Solution(BloodReagent, FixedPoint2.New(_random.Next(15, 30)));
            _puddle.TrySpillAt(coords.Offset(offset), solution, out _);
        }
    }
}
