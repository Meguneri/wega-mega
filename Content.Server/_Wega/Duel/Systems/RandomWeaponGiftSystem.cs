using Content.Server._Wega.Duel.Components;
using Content.Server.Administration.Logs;
using Content.Server.Hands.Systems;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Interaction.Components;
using Content.Shared.Item;
using Content.Shared.Blood.Cult.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Whitelist;
using Robust.Server.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
///     «Подарок-рулетка»: одно случайное оружие из всех прототипов игры.
///     Пул собирается динамически — каждый предмет с огнестрелом или достаточно
///     злым MeleeWeapon, без вайтлистов и ручных списков. Распаковка — как у
///     новогоднего подарка (RandomGiftSystem): использовать в руке.
/// </summary>
public sealed partial class RandomWeaponGiftSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private DuelArenaCleanupSystem _cleanup = default!;

    // Кэш пула на (порог урона) — прототипы не меняются в рантайме, кроме reload.
    private readonly Dictionary<double, List<EntProtoId>> _poolCache = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RandomWeaponGiftComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<RandomWeaponGiftComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<RandomWeaponGiftComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _poolCache.Clear());
    }

    private void OnMapInit(EntityUid uid, RandomWeaponGiftComponent comp, MapInitEvent args)
    {
        var pool = GetPool(comp.MinMeleeDamage);
        if (pool.Count == 0)
            return;

        comp.SelectedEntity = _random.Pick(pool);
    }

    private void OnExamined(EntityUid uid, RandomWeaponGiftComponent comp, ExaminedEvent args)
    {
        if (_whitelistSystem.IsWhitelistFail(comp.ContentsViewers, args.Examiner) || comp.SelectedEntity is null)
            return;

        var name = _prototype.Index(comp.SelectedEntity.Value).Name;
        args.PushText(Loc.GetString("gift-packin-contains", ("name", name)));
    }

    private void OnUseInHand(EntityUid uid, RandomWeaponGiftComponent comp, UseInHandEvent args)
    {
        if (args.Handled || comp.SelectedEntity is null)
            return;

        var coords = Transform(args.User).Coordinates;
        var weapon = Spawn(comp.SelectedEntity, coords);
        _adminLogger.Add(LogType.EntitySpawn, LogImpact.Low, $"{ToPrettyString(args.User)} unwrapped {ToPrettyString(uid)} which spawned {ToPrettyString(weapon)}");

        // Культовое оружие иначе нельзя держать некультисту — рулетка выдаёт его без привязки к роли.
        RemComp<CultWeaponComponent>(weapon);

        if (comp.MarkIssuedItems)
            _cleanup.MarkIssuedRecursive(weapon);

        if (comp.Wrapper is not null)
            Spawn(comp.Wrapper, coords);

        _audio.PlayPvs(comp.Sound, args.User);

        // Нельзя удалять сущность прямо в шине событий — ставим в очередь.
        // Рука нужна под новый предмет, поэтому подарок уводим в nullspace.
        _transform.DetachEntity(uid, Transform(uid));
        QueueDel(uid);

        _hands.PickupOrDrop(args.User, weapon);

        args.Handled = true;
    }

    private List<EntProtoId> GetPool(double minMeleeDamage)
    {
        if (_poolCache.TryGetValue(minMeleeDamage, out var cached))
            return cached;

        var pool = new List<EntProtoId>();
        var factory = EntityManager.ComponentFactory;

        foreach (var proto in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract || proto.HideSpawnMenu)
                continue;

            // Только подбираемые предметы — никаких турелей, вендоматов и мехов.
            if (!proto.TryGetComponent<ItemComponent>(out _, factory))
                continue;

            // Unremoveable намертво прилипает к руке — такое не выдаём.
            // Культовое оружие в пуле остаётся: ролевой ограничитель снимается при распаковке.
            if (proto.TryGetComponent<UnremoveableComponent>(out _, factory))
                continue;

            if (proto.TryGetComponent<GunComponent>(out _, factory))
            {
                pool.Add(proto.ID);
                continue;
            }

            // Без огнестрела предмет проходит только как полноценное холодное оружие:
            // порог урона отсекает ручки, игрушки и прочие предметы «с тычком».
            if (proto.TryGetComponent<MeleeWeaponComponent>(out var melee, factory)
                && melee.Damage.GetTotal().Double() >= minMeleeDamage)
            {
                pool.Add(proto.ID);
            }
        }

        _poolCache[minMeleeDamage] = pool;
        return pool;
    }
}
