using Content.Server.Fluids.EntitySystems;
using Content.Server.Power.EntitySystems;
using Content.Shared.Audio;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.DirtVisuals;
using Content.Shared.FixedPoint;
using Content.Shared.StatusEffectNew;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;

namespace Content.Server.Shower
{
    public sealed partial class ShowerSystem : EntitySystem
    {
        [Dependency] private SharedAmbientSoundSystem _ambient = default!;
        [Dependency] private SharedAppearanceSystem _appearance = default!;
        [Dependency] private SharedAudioSystem _audio = default!;
        [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
        [Dependency] private PuddleSystem _puddle = default!;
        [Dependency] private EntityLookupSystem _lookup = default!;
        [Dependency] private ReactiveSystem _reactive = default!;
        [Dependency] private StatusEffectsSystem _statusEffects = default!;

        /// <summary>
        ///     «Холодный душ» — антидот афродизиака: смывает любовное опьянение
        ///     и вымывает остатки реагента из крови, чтобы эффект не вернулся.
        /// </summary>
        private const string LoveVisionEffect = "StatusEffectLoveVision";
        private const string AphrodisiacReagent = "Aphrodisiac";
        private const string ArenaAphrodisiacReagent = "ArenaAphrodisiac";

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<ShowerComponent, GetVerbsEvent<AlternativeVerb>>(AddShowerVerb);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var query = EntityQueryEnumerator<ShowerComponent>();
            while (query.MoveNext(out var uid, out var shower))
            {
                if (!shower.IsSpraying)
                    continue;

                shower.RemainingTime -= frameTime;
                if (shower.RemainingTime <= 0)
                {
                    SprayWater(uid, shower);
                    shower.RemainingTime = shower.SprayTime;
                }
            }
        }

        private void AddShowerVerb(EntityUid uid, ShowerComponent component, GetVerbsEvent<AlternativeVerb> args)
        {
            if (!args.CanAccess || !args.CanInteract || !this.IsPowered(uid, EntityManager))
                return;

            AlternativeVerb verb = new()
            {
                Act = () => ToggleSpraying(uid, component),
                Text = component.IsSpraying
                    ? Loc.GetString("shower-verb-stop")
                    : Loc.GetString("shower-verb-start"),
                Priority = 2
            };

            args.Verbs.Add(verb);
        }

        public void ToggleSpraying(EntityUid uid, ShowerComponent component)
        {
            if (component.IsSpraying)
                StopSpraying(uid, component);
            else
                StartSpraying(uid, component);
        }

        public void StartSpraying(EntityUid uid, ShowerComponent component)
        {
            if (component.IsSpraying)
                return;

            component.IsSpraying = true;
            component.RemainingTime = component.SprayTime;

            _audio.PlayPvs(component.SprayStartSound, uid);
            _ambient.SetAmbience(uid, true);

            _appearance.SetData(uid, ShowerVisuals.Spraying, true);
        }

        private void StopSpraying(EntityUid uid, ShowerComponent component)
        {
            if (!component.IsSpraying)
                return;

            component.IsSpraying = false;

            _audio.PlayPvs(component.SprayEndSound, uid);
            _ambient.SetAmbience(uid, false);

            _appearance.SetData(uid, ShowerVisuals.Spraying, false);
        }

        private void SprayWater(EntityUid uid, ShowerComponent component)
        {
            if (!_solutionContainer.TryGetSolution(uid, "shower", out var showerSol, out var solution) || solution.Volume == 0)
            {
                // Пустой/недоступный резервуар не должен сам выключать душ —
                // он остаётся включённым, пока его не переключат вручную.
                return;
            }

            var coordinates = Transform(uid).Coordinates;
            var mobsInRange = _lookup.GetEntitiesInRange<ReactiveComponent>(coordinates, 0.5f);
            var amountPerTarget = component.WaterAmount / (mobsInRange.Count + 1);

            foreach (var mob in mobsInRange)
            {
                var mobSolution = _solutionContainer.SplitSolution(showerSol.Value, amountPerTarget);
                _reactive.DoEntityReaction(mob, mobSolution, ReactionMethod.Touch);

                _statusEffects.TryRemoveStatusEffect(mob, LoveVisionEffect);
                if (_solutionContainer.TryGetSolution(mob.Owner, "chemicals", out var chemSol, out _))
                {
                    _solutionContainer.RemoveReagent(chemSol.Value, AphrodisiacReagent, FixedPoint2.New(100));
                    _solutionContainer.RemoveReagent(chemSol.Value, ArenaAphrodisiacReagent, FixedPoint2.New(100));
                }
            }

            var floorSolution = _solutionContainer.SplitSolution(showerSol.Value, amountPerTarget);
            _puddle.TrySpillAt(coordinates, floorSolution, out _);
        }
    }
}
