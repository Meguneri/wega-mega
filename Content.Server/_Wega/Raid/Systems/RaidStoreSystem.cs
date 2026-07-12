using Content.Server._Wega.Raid.Components;
using Content.Server.Store.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Wega.Raid.Systems;

/// <summary>
/// Makes <see cref="RaidShopTerminal"/> spend currency directly from the player's persistent raid stash
/// rather than from physical currency items inserted into the terminal.
/// </summary>
/// <remarks>
/// The terminal still uses the standard <see cref="StoreComponent"/> for catalog/listing logic, but its
/// <c>Balance</c> is synchronized with <see cref="RaidStashSystem"/> around UI open and purchase events.
/// This is a sequential-use terminal: simultaneous shoppers may see each other's balance. Per-player
/// UI isolation is left for a future iteration.
/// </remarks>
public sealed partial class RaidStoreSystem : EntitySystem
{
    [Dependency] private RaidStashSystem _stash = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RaidStoreComponent, BeforeActivatableUIOpenEvent>(OnBeforeUiOpen);
        SubscribeLocalEvent<RaidStoreComponent, StoreRequestUpdateInterfaceMessage>(OnRequestUpdate);

        // Load the player's stash balance before the default store system validates and processes the purchase.
        SubscribeLocalEvent<RaidStoreComponent, StoreBuyListingMessage>(OnBuyRequest,
            before: new[] { typeof(StoreSystem) });

        // After the purchase is done, write the remaining terminal balance back to the player's stash.
        SubscribeLocalEvent<StoreBuyFinishedEvent>(OnBuyFinished);

        // Withdrawal is not supported for digital stash currency.
        SubscribeLocalEvent<RaidStoreComponent, StoreRequestWithdrawMessage>(OnWithdrawRequest,
            before: new[] { typeof(StoreSystem) });

        // Physical currency insertion is not supported either.
        SubscribeLocalEvent<RaidStoreComponent, CurrencyInsertAttemptEvent>(OnCurrencyInsertAttempt);
    }

    #region UI / Balance sync

    private void OnBeforeUiOpen(EntityUid uid, RaidStoreComponent component, BeforeActivatableUIOpenEvent args)
    {
        if (!TryComp<StoreComponent>(uid, out var store))
            return;

        if (!TryGetUserId(args.User, out var userId))
            return;

        SyncStoreBalanceFromStash(store, userId);
    }

    private void OnRequestUpdate(EntityUid uid, RaidStoreComponent component, StoreRequestUpdateInterfaceMessage args)
    {
        if (!TryComp<StoreComponent>(uid, out var store))
            return;

        if (!TryGetUserId(args.Actor, out var userId))
            return;

        SyncStoreBalanceFromStash(store, userId);
    }

    #endregion

    #region Purchases

    private void OnBuyRequest(EntityUid uid, RaidStoreComponent component, StoreBuyListingMessage args)
    {
        if (!TryComp<StoreComponent>(uid, out var store))
            return;

        if (!TryGetUserId(args.Actor, out var userId))
            return;

        component.BuyerUserId = userId;
        SyncStoreBalanceFromStash(store, userId);
    }

    private void OnBuyFinished(ref StoreBuyFinishedEvent args)
    {
        if (!TryComp<RaidStoreComponent>(args.StoreUid, out var raidStore))
            return;

        if (!TryComp<StoreComponent>(args.StoreUid, out var store))
            return;

        if (raidStore.BuyerUserId is not { } userId)
            return;

        SyncStashBalanceFromStore(userId, store);
        _ = _stash.SaveStashAsync(userId, force: true);
        raidStore.BuyerUserId = null;
    }

    #endregion

    #region Withdrawal (disabled)

    private void OnWithdrawRequest(EntityUid uid, RaidStoreComponent component, StoreRequestWithdrawMessage args)
    {
        if (!TryComp<StoreComponent>(uid, out var store))
            return;

        // Clear the terminal balance so the default withdraw handler cannot spawn physical currency.
        store.Balance.Clear();
    }

    #endregion

    #region Currency insertion (disabled)

    private void OnCurrencyInsertAttempt(EntityUid uid, RaidStoreComponent component, ref CurrencyInsertAttemptEvent args)
    {
        args.Cancel();
    }

    #endregion

    #region Helpers

    private bool TryGetUserId(EntityUid user, out NetUserId userId)
    {
        userId = default;

        if (!_mind.TryGetMind(user, out _, out var mind))
            return false;

        if (mind.UserId is not { } id)
            return false;

        userId = id;
        return true;
    }

    private void SyncStoreBalanceFromStash(StoreComponent store, NetUserId userId)
    {
        store.Balance.Clear();
        if (_stash.TryGetStash(userId, out var stash))
        {
            foreach (var (currency, amount) in stash.Currency)
            {
                store.Balance[currency] = amount;
            }
        }
    }

    private void SyncStashBalanceFromStore(NetUserId userId, StoreComponent store)
    {
        var stash = _stash.GetOrCreateStash(userId);
        stash.Currency.Clear();
        foreach (var (currency, amount) in store.Balance)
        {
            stash.Currency[currency] = amount;
        }
    }

    #endregion
}
