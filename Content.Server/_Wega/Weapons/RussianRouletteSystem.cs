using Content.Shared._Wega.Weapons;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._Wega.Weapons;

/// <summary>
/// Заменяет поведение «использовать в руке» (Z) у револьвера русской рулетки: базовый
/// <see cref="SharedGunSystem"/> на это событие сдвигает барабан ровно на одну камору, что позволяет
/// вычислить положение патрона счётом. Здесь вместо этого барабан прокручивается на случайную камору —
/// то есть Z работает как настоящая раскрутка барабана.
/// </summary>
public sealed partial class RussianRouletteSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Строго до обработчика револьвера — он бы пометил событие обработанным и сделал Cycle().
        SubscribeLocalEvent<RussianRouletteComponent, UseInHandEvent>(OnUseInHand,
            before: new[] { typeof(SharedGunSystem) });
    }

    private void OnUseInHand(Entity<RussianRouletteComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<RevolverAmmoProviderComponent>(ent, out var revolver))
            return;

        if (!_useDelay.TryResetDelay(ent.Owner))
            return;

        args.Handled = true;

        revolver.CurrentIndex = _random.Next(revolver.Capacity);
        Dirty(ent.Owner, revolver);

        _audio.PlayPvs(revolver.SoundSpin, ent);
        _popup.PopupEntity(Loc.GetString("gun-revolver-spun"), ent, args.User);
    }
}
