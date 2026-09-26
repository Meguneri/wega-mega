using System.Linq;
using Content.Shared.Armor;
using Content.Shared.Blocking;
using Content.Shared.Blocking.Components;
using Content.Shared.Clothing;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Medical.Healing;
using Content.Shared.Mobs.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared._Wega.Clothing.Sandevistan;

/// <summary>
/// The arena Sandevistan's weapon lock: while the eyewear (<see cref="SandevistanArenaLockComponent"/>)
/// is worn, the wearer may only attack with weapons flagged <see cref="ArenaAllowedWeaponComponent"/>
/// (the gloves of the north star). Any other melee weapon and all guns are blocked; unarmed attacks
/// remain allowed.
/// </summary>
public sealed partial class SandevistanArenaLockSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SandevistanArenaLockComponent, ClothingGotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<SandevistanArenaLockComponent, ClothingGotUnequippedEvent>(OnUnequipped);

        // Same marker, but on the arena implant: route its implant/extract lifecycle into the same
        // reference-counted lock, so the implant and the eyewear can't clobber each other.
        SubscribeLocalEvent<SandevistanArenaLockComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<SandevistanArenaLockComponent, ImplantRemovedEvent>(OnImplantRemoved);

        // Drops the now-forbidden gear the moment the lock attaches, whatever the source — worn
        // eyewear or the arena implant.
        SubscribeLocalEvent<ArenaWeaponLockComponent, ComponentStartup>(OnLockStartup);

        SubscribeLocalEvent<ArenaWeaponLockComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<ArenaWeaponLockComponent, ShotAttemptedEvent>(OnShotAttempt);
        SubscribeLocalEvent<ArenaWeaponLockComponent, IsEquippingTargetAttemptEvent>(OnEquipTargetAttempt);
        SubscribeLocalEvent<ArenaWeaponLockComponent, PickupAttemptEvent>(OnPickupAttempt);
        SubscribeLocalEvent<ArenaWeaponLockComponent, DidEquipHandEvent>(OnDidEquipHand);

        SubscribeLocalEvent<ArenaAllowedWeaponComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
        SubscribeLocalEvent<ArenaAllowedWeaponComponent, MeleeHitEvent>(OnMeleeHit);
    }

    /// <summary>
    /// Оружие (melee/огнестрел) и щиты нельзя взять в руки, пока надет арена-сандэвистан — попытка
    /// подбора отменяется. Перчатки полярной звезды (флаг <see cref="ArenaAllowedWeaponComponent"/>)
    /// надеваются в слот перчаток, а не в руки, так что их это не касается.
    /// </summary>
    private void OnPickupAttempt(Entity<ArenaWeaponLockComponent> ent, ref PickupAttemptEvent args)
    {
        if (IsHandLocked(args.Item))
            args.Cancel();
    }

    /// <summary>
    /// Подстраховка к запрету подбора: некоторые пути выдачи кладут предмет прямо в руку через
    /// <c>PickupOrDrop</c> в обход <see cref="PickupAttemptEvent"/> (рулетка оружия, наборы
    /// <c>SpawnItemsOnUse</c>). Если так в руку попало заблокированное оружие/щит — роняем его сразу.
    /// </summary>
    private void OnDidEquipHand(Entity<ArenaWeaponLockComponent> ent, ref DidEquipHandEvent args)
    {
        if (IsHandLocked(args.Equipped))
            _hands.TryDrop(ent.Owner, args.Equipped, checkActionBlocker: false);
    }

    /// <summary>Оружие или щит, не помеченные как разрешённые — такое в руках держать нельзя.</summary>
    private bool IsHandLocked(EntityUid item)
    {
        if (HasComp<ArenaAllowedWeaponComponent>(item))
            return false;

        // Лекарства, расходники и еда — не оружие, держать можно (бутылки/пилюли/напитки имеют
        // MeleeWeaponComponent, т.к. ими «можно ударить», но это не повод запрещать лечиться).
        if (HasComp<EdibleComponent>(item) || HasComp<HealingComponent>(item))
            return false;

        return HasComp<MeleeWeaponComponent>(item)
            || HasComp<GunComponent>(item)
            || HasComp<BlockingComponent>(item);
    }

    /// <summary>
    /// Пока надет арена-сандэвистан, носитель не может надеть элементы брони (с <c>Armor</c>) и
    /// ускоряющую обувь (<c>ClothingSpeedModifier</c> с бустом &gt; 1).
    /// </summary>
    private void OnEquipTargetAttempt(Entity<ArenaWeaponLockComponent> ent, ref IsEquippingTargetAttemptEvent args)
    {
        if (!IsSlotLocked(args.Equipment))
            return;

        args.Reason = "sandevistan-arena-gear-locked";
        args.Cancel();
    }

    /// <summary>
    /// Носимая экипировка, запрещённая под арена-сандэвистаном: броня (<c>Armor</c>) и ускоряющая
    /// одежда (<c>ClothingSpeedModifier</c> с бустом &gt; 1). Сам арена-сандэвистан и перчатки полярной
    /// звезды исключены — иначе при выдаче замка слетел бы сам сандэвистан (у него есть буст скорости).
    /// </summary>
    private bool IsSlotLocked(EntityUid item)
    {
        if (HasComp<SandevistanArenaLockComponent>(item) || HasComp<ArenaAllowedWeaponComponent>(item))
            return false;

        if (HasComp<ArmorComponent>(item))
            return true;

        return TryComp<ClothingSpeedModifierComponent>(item, out var speed)
            && (speed.WalkModifier > 1f || speed.SprintModifier > 1f);
    }

    /// <summary>
    /// Перчатки полярной звезды бьют сильнее (15 блант), если на носителе есть любой сандэвистан
    /// (<see cref="SandevistanWearerComponent"/>) — арена, базовый или «стоп-кадр», очки или имплант.
    /// Каждый N-й удар (по умолчанию третий) вместо этого наносит 10 блант, которые игнорируют броню —
    /// чтобы у кулаков был ответ на тяжёлую защиту.
    /// </summary>
    private void OnGetMeleeDamage(Entity<ArenaAllowedWeaponComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (!HasComp<SandevistanWearerComponent>(args.User))
            return;

        // GetMeleeDamageEvent поднимается отдельно для урона и для проверки пробития до того, как
        // удар «приземлится» (счётчик растёт в OnMeleeHit), поэтому следующий удар — это HitCount + 1.
        if (IsPiercingHit(ent.Comp))
        {
            args.Damage.DamageDict["Blunt"] = ent.Comp.PierceDamage;
            args.ResistanceBypass = true;
        }
        else
        {
            args.Damage.DamageDict["Blunt"] = 15;
        }
    }

    /// <summary>Будет ли следующий приземлившийся удар бронебойным.</summary>
    private static bool IsPiercingHit(ArenaAllowedWeaponComponent comp)
    {
        return (comp.HitCount + 1) % comp.ArmorPierceEveryNthHit == 0;
    }

    /// <summary>
    /// Считаем только удары носителя сандэвистана, попавшие по живому существу — на них завязан цикл
    /// пробития. Удары по стенам, окнам, шкафам и прочим объектам цикл не двигают, иначе бронебойный
    /// удар можно было бы «зарядить», просто молотя по стене.
    /// </summary>
    private void OnMeleeHit(Entity<ArenaAllowedWeaponComponent> ent, ref MeleeHitEvent args)
    {
        if (!HasComp<SandevistanWearerComponent>(args.User))
            return;

        // Swap the sharp punch for a deeper, muffled "gorilla-arms" thud while a Sandevistan is worn.
        // Mobs carry no MeleeSoundComponent, so this override actually plays on them (see MeleeSoundSystem).
        args.HitSoundOverride = ent.Comp.SandevistanHitSound;

        var hitMob = false;
        foreach (var hit in args.HitEntities)
        {
            if (HasComp<MobStateComponent>(hit))
            {
                hitMob = true;
                break;
            }
        }

        if (!hitMob)
            return;

        // This landed hit is the armour-piercing one (the counter hasn't been bumped for it yet) —
        // give it visible/audible feedback on every mob it connected with.
        if (IsPiercingHit(ent.Comp))
        {
            foreach (var hit in args.HitEntities)
            {
                if (!HasComp<MobStateComponent>(hit))
                    continue;

                _audio.PlayPredicted(ent.Comp.PierceSound, hit, args.User);
                PredictedSpawnAttachedTo(ent.Comp.PierceEffect, Transform(hit).Coordinates);
            }
        }

        ent.Comp.HitCount++;
        Dirty(ent);
    }

    private void OnEquipped(Entity<SandevistanArenaLockComponent> ent, ref ClothingGotEquippedEvent args)
    {
        // Серверная мутация: компонент сетевой и приедет клиенту состоянием. Добавлять его на клиенте
        // (особенно во время сброса предсказанных сущностей) нельзя — это ломает обход коллекции.
        if (_net.IsClient)
            return;

        AddArenaLock(args.Wearer);
    }

    /// <summary>
    /// The weapon lock just attached to a mob — worn arena eyewear was equipped, or the arena implant
    /// was inserted (which adds <see cref="ArenaWeaponLockComponent"/> via its
    /// <c>implantComponents</c>). Drop everything the lock forbids. Server-only: this mutates
    /// hands/inventory authoritatively and the component replicates to the client on its own.
    /// </summary>
    private void OnLockStartup(Entity<ArenaWeaponLockComponent> ent, ref ComponentStartup args)
    {
        if (_net.IsClient)
            return;

        DropLockedGear(ent.Owner);
    }

    /// <summary>
    /// Сбрасывает с бойца всё, что запрещено под арена-сандэвистаном: зажатое оружие/щиты выпадают
    /// из рук, надетая броня и ускоряющая одежда — из слотов. Вызывается при надевании очков.
    /// </summary>
    private void DropLockedGear(EntityUid wearer)
    {
        // Оружие/щиты из рук.
        foreach (var held in _hands.EnumerateHeld(wearer).ToList())
        {
            if (IsHandLocked(held))
                _hands.TryDrop(wearer, held, checkActionBlocker: false);
        }

        // Броня и ускоряющая одежда из слотов. Снимаем после перечисления, чтобы не менять
        // контейнеры прямо во время обхода.
        if (!_inventory.TryGetContainerSlotEnumerator(wearer, out var slots, SlotFlags.WITHOUT_POCKET))
            return;

        var toUnequip = new List<string>();
        while (slots.MoveNext(out var container, out var slotDef))
        {
            if (container.ContainedEntity is { } worn && IsSlotLocked(worn))
                toUnequip.Add(slotDef.Name);
        }

        foreach (var slot in toUnequip)
            _inventory.TryUnequip(wearer, slot, force: true);
    }

    private void OnUnequipped(Entity<SandevistanArenaLockComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        // Снятие компонента — тоже только на сервере (см. OnEquipped).
        if (_net.IsClient)
            return;

        RemoveArenaLock(args.Wearer);
    }

    // The arena Sandevistan implant was inserted/extracted (events raised on the implant entity).
    // Funnel into the same reference-counted lock the eyewear uses.
    private void OnImplanted(Entity<SandevistanArenaLockComponent> ent, ref ImplantImplantedEvent args)
    {
        if (_net.IsClient)
            return;

        AddArenaLock(args.Implanted);
    }

    private void OnImplantRemoved(Entity<SandevistanArenaLockComponent> ent, ref ImplantRemovedEvent args)
    {
        if (_net.IsClient)
            return;

        RemoveArenaLock(args.Implanted);
    }

    /// <summary>
    /// Adds one lock source to <paramref name="mob"/>. The first source attaches
    /// <see cref="ArenaWeaponLockComponent"/> (whose ComponentStartup drops the forbidden gear);
    /// further sources only bump the counter.
    /// </summary>
    private void AddArenaLock(EntityUid mob)
    {
        var lockComp = EnsureComp<ArenaWeaponLockComponent>(mob);
        lockComp.Sources++;
    }

    /// <summary>
    /// Removes one lock source from <paramref name="mob"/>. The lock itself lifts only once the last
    /// source is gone, so removing one of two (glasses + implant) keeps it active.
    /// </summary>
    private void RemoveArenaLock(EntityUid mob)
    {
        if (!TryComp<ArenaWeaponLockComponent>(mob, out var lockComp))
            return;

        lockComp.Sources--;
        if (lockComp.Sources <= 0)
            RemComp<ArenaWeaponLockComponent>(mob);
    }

    private void OnAttackAttempt(Entity<ArenaWeaponLockComponent> ent, ref AttackAttemptEvent args)
    {
        // Unarmed attacks (no weapon, or the weapon is the user's own body) stay allowed.
        if (args.Weapon is not { } weapon || weapon.Owner == ent.Owner)
            return;

        // Only flagged weapons (the gloves of the north star) are permitted.
        if (HasComp<ArenaAllowedWeaponComponent>(weapon.Owner))
            return;

        _popup.PopupClient(Loc.GetString("sandevistan-arena-weapon-locked"), ent, ent);
        args.Cancel();
    }

    private void OnShotAttempt(Entity<ArenaWeaponLockComponent> ent, ref ShotAttemptedEvent args)
    {
        // Guns are never allowed under the lock — only the flagged melee gloves.
        if (HasComp<ArenaAllowedWeaponComponent>(args.Used.Owner))
            return;

        _popup.PopupClient(Loc.GetString("sandevistan-arena-weapon-locked"), ent, ent);
        args.Cancel();
    }
}
