using Content.Server._Wega.Duel.Components;
using Content.Server._Wega.Duel.Systems;
using Content.Server.Clothing.Systems;
using Content.Shared._Wega.Duel;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.UserInterface;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Система выбора и выдачи наборов снаряжения для дуэльной арены.
/// </summary>
public sealed class ArenaLoadoutSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private OutfitSystem _outfit = default!;
    [Dependency] private DuelArenaCleanupSystem _cleanup = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorage = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArenaLoadoutTerminalComponent, ActivateInWorldEvent>(OnTerminalActivate);
        SubscribeLocalEvent<ArenaLoadoutTerminalComponent, ArenaLoadoutSelectedMessage>(OnLoadoutSelected);
    }

    private void OnTerminalActivate(EntityUid uid, ArenaLoadoutTerminalComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(args.User, out _))
            return;

        OpenLoadoutSelectionUI(uid, component, args.User);
        args.Handled = true;
    }

    private void OpenLoadoutSelectionUI(EntityUid terminal, ArenaLoadoutTerminalComponent component, EntityUid user)
    {
        var available = new List<ProtoId<ArenaLoadoutPrototype>>();

        foreach (var loadoutId in component.Loadouts)
        {
            if (_prototype.TryIndex(loadoutId, out _))
                available.Add(loadoutId);
        }

        if (available.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("arena-loadout-terminal-empty"), terminal, user);
            return;
        }

        var state = new ArenaLoadoutSelectionState(available);
        _ui.OpenUi(terminal, ArenaLoadoutUiKey.Key, user);
        _ui.SetUiState(terminal, ArenaLoadoutUiKey.Key, state);
    }

    private void OnLoadoutSelected(EntityUid uid, ArenaLoadoutTerminalComponent component, ArenaLoadoutSelectedMessage args)
    {
        if (!_prototype.TryIndex(args.LoadoutId, out var loadout))
            return;

        var user = GetEntity(args.User);
        if (!Exists(user))
            return;

        var preference = EnsureComp<ArenaLoadoutPreferenceComponent>(user);
        preference.SelectedLoadout = args.LoadoutId;

        _popup.PopupEntity(
            Loc.GetString("arena-loadout-selected", ("loadoutName", Loc.GetString(loadout.Name))),
            user,
            user);

        _audio.PlayPvs("/Audio/Machines/terminal_select.ogg", uid);
    }

    /// <summary>
    /// Выдаёт каждому дуэлянту выбранный набор в ящик на его позиции. Ящик и всё содержимое
    /// помечаются ArenaIssuedItem и будут удалены после боя. StartingGear (если задан) выдаётся
    /// напрямую на тело и помечается рекурсивно.
    /// </summary>
    public void EquipDuelists(IEnumerable<EntityUid> duelists)
    {
        foreach (var duelist in duelists)
        {
            if (!Exists(duelist) || !HasComp<InventoryComponent>(duelist))
                continue;

            var preference = CompOrNull<ArenaLoadoutPreferenceComponent>(duelist);
            if (preference?.SelectedLoadout is not { } loadoutId ||
                !_prototype.TryIndex(loadoutId, out var loadout))
            {
                // Без выбора — ничего не выдаём, боец идёт с тем, что принёс.
                continue;
            }

            var coords = Transform(duelist).Coordinates;

            // 1. Выдаём startingGear напрямую на тело, если указан.
            if (loadout.StartingGear is { } gearId)
            {
                _outfit.SetOutfit(duelist, gearId);
                MarkMobInventoryIssued(duelist);
            }

            // 2. Спавним ящик с contents рядом с бойцом.
            if (loadout.Contents.Count > 0)
            {
                var crate = Spawn("ArenaLoadoutCrate", coords);
                if (TryComp<EntityStorageComponent>(crate, out var entityStorage))
                {
                    foreach (var item in loadout.Contents)
                    {
                        for (var i = 0; i < item.Amount; i++)
                        {
                            var spawned = Spawn(item.EntityId, Transform(crate).Coordinates);
                            _entityStorage.Insert(spawned, crate, entityStorage);
                        }
                    }
                }

                _cleanup.MarkIssuedRecursive(crate);
            }

            _popup.PopupEntity(
                Loc.GetString("arena-loadout-delivered", ("loadoutName", Loc.GetString(loadout.Name))),
                duelist,
                duelist);
        }
    }

    /// <summary>
    /// Помечает всё снаряжение на теле бойца как выданное ареной: инвентарные слоты, руки,
    /// а также вложенные контейнеры. Нужно, чтобы startingGear удалялся очисткой после боя.
    /// </summary>
    private void MarkMobInventoryIssued(EntityUid mob)
    {
        if (!TryComp<InventoryComponent>(mob, out var inventory))
            return;

        var slots = _inventory.GetSlotEnumerator((mob, inventory));
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is { } item)
                _cleanup.MarkIssuedRecursive(item);
        }

        if (TryComp<HandsComponent>(mob, out var hands))
        {
            foreach (var handName in hands.Hands.Keys)
            {
                if (_hands.TryGetHeldItem((mob, hands), handName, out var held))
                    _cleanup.MarkIssuedRecursive(held.Value);
            }
        }
    }
}
