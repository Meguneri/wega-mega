using System.Numerics;
using Content.Shared._Wega.Magic.SoulSwap;
using Content.Server.Body.Systems;
using Content.Shared.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Wega.Magic.SoulSwap;

public sealed partial class SoulSwapSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private BloodstreamSystem _blood = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private TransformSystem _xform = default!;
    [Dependency] private IRobustRandom _random = default!;

    private static readonly SoundSpecifier TransformSound =
        new SoundPathSpecifier("/Audio/Effects/Changeling/changeling_transform.ogg",
            AudioParams.Default.WithVolume(4f));

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SoulSwapSpellEvent>(OnCast);
    }

    private void OnCast(SoulSwapSpellEvent args)
    {
        if (args.Handled)
            return;

        var caster = args.Performer;
        var target = args.Target;

        if (!HasComp<MobStateComponent>(target))
            return;

        args.Handled = true;

#pragma warning disable CS0618
        var casterDamage = _damageable.GetAllDamage(caster);
        var targetDamage = _damageable.GetAllDamage(target);
#pragma warning restore CS0618

        // обнуляем обоих, затем применяем урон противника
        _damageable.SetAllDamage(caster, 0);
        _damageable.SetAllDamage(target, 0);

        // применяем урон со знаком + (наносим)
        if (targetDamage.GetTotal() > 0)
            _damageable.TryChangeDamage(caster, targetDamage, ignoreResistances: true, interruptsDoAfters: false);
        if (casterDamage.GetTotal() > 0)
            _damageable.TryChangeDamage(target, casterDamage, ignoreResistances: true, interruptsDoAfters: false);

        // переносим кровотечение
        SwapBleeding(caster, target);

        // эффекты на обеих позициях
        SpawnEffect(caster);
        SpawnEffect(target);

        _audio.PlayPvs(TransformSound, caster);
        _audio.PlayPvs(TransformSound, target);

        _popup.PopupEntity(Loc.GetString("soul-swap-target"), target, target, PopupType.LargeCaution);
        _popup.PopupEntity(Loc.GetString("soul-swap-caster", ("target", Name(target))), caster, caster, PopupType.Medium);
    }

    private void SwapBleeding(EntityUid caster, EntityUid target)
    {
        var casterBleed = TryComp<BloodstreamComponent>(caster, out var cb) ? cb.BleedAmount : 0f;
        var targetBleed = TryComp<BloodstreamComponent>(target, out var tb) ? tb.BleedAmount : 0f;

        // обнуляем и устанавливаем нужное значение через TryModifyBleedAmount
        if (cb != null)
            _blood.TryModifyBleedAmount(caster, -casterBleed + targetBleed);
        if (tb != null)
            _blood.TryModifyBleedAmount(target, -targetBleed + casterBleed);
    }

    private void SpawnEffect(EntityUid uid)
    {
        var mapPos = _xform.GetMapCoordinates(uid);
        for (var i = 0; i < 3; i++)
        {
            var offset = new Vector2(_random.NextFloat(-0.4f, 0.4f), _random.NextFloat(-0.4f, 0.4f));
            Spawn("EffectSoulSwap", new MapCoordinates(mapPos.Position + offset, mapPos.MapId));
        }
    }
}
