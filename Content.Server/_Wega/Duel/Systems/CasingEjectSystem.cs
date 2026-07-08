using Content.Shared._Wega.Duel;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Выбрасывает гильзу при выстреле для оружия с <see cref="CasingEjectOnShotComponent"/>.
/// </summary>
public sealed class CasingEjectSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CasingEjectOnShotComponent, GunShotEvent>(OnShot);
    }

    private void OnShot(Entity<CasingEjectOnShotComponent> ent, ref GunShotEvent args)
    {
        // Спавним у ног стрелка, а не по координатам оружия: оружие лежит в контейнере руки,
        // и гильза, привязанная к нему, «магнитилась» к персонажу.
        if (!Exists(args.User))
            return;

        var coords = Transform(args.User).Coordinates;
        var casing = Spawn(ent.Comp.Casing, coords);

        // Лёгкий разброс, чтобы гильза заметно отскочила и легла рядом.
        var dir = _random.NextAngle().ToVec();
        _throwing.TryThrow(casing, dir * 0.25f, ent.Comp.Speed, doSpin: true);

        _audio.PlayPvs(ent.Comp.Sound, casing);
    }
}
