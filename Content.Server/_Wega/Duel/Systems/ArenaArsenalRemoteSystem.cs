using System.Linq;
using Content.Server._Wega.Duel.Components;
using Content.Shared._Wega.Duel;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Пульт арсенала: задаёт тир крейта, который <see cref="DuelArenaSystem"/> спавнит у спавн-маркеров
/// каждый раунд. Выбор применяется ко ВСЕМ аренам и полностью заменяет прежний тир (без наложения).
/// </summary>
public sealed class ArenaArsenalRemoteSystem : EntitySystem
{
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArenaArsenalRemoteComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ArenaArsenalRemoteComponent, ArenaArsenalSelectMessage>(OnSelect);
    }

    private void OnUseInHand(EntityUid uid, ArenaArsenalRemoteComponent comp, UseInHandEvent args)
    {
        if (args.Handled || !_ui.HasUi(uid, ArenaArsenalRemoteUiKey.Key))
            return;

        UpdateUi(uid, comp);
        _ui.OpenUi(uid, ArenaArsenalRemoteUiKey.Key, args.User);
        args.Handled = true;
    }

    private void OnSelect(EntityUid uid, ArenaArsenalRemoteComponent comp, ArenaArsenalSelectMessage args)
    {
        EntProtoId? crate = null;
        if (args.CrateProto is { } sel)
        {
            // Валидация на сервере: выбор обязан быть из списка пульта (список в UI — только визуальный),
            // и прототип должен существовать. Иначе модифицированный клиент подсунул бы что угодно.
            if (!comp.Crates.Any(c => c.Id == sel) || !_prototype.HasIndex<EntityPrototype>(sel))
                return;

            crate = sel;
        }

        var count = SetArsenalCrate(crate);
        UpdateUi(uid, comp);

        var tier = crate is { } c ? GetName(c) : Loc.GetString("arena-arsenal-remote-off");
        _popup.PopupEntity(
            Loc.GetString("arena-arsenal-remote-applied", ("tier", tier), ("count", count)),
            args.Actor, args.Actor);
    }

    private int SetArsenalCrate(EntProtoId? crate)
    {
        var count = 0;
        var query = EntityQueryEnumerator<DuelArenaComponent>();
        while (query.MoveNext(out _, out var arena))
        {
            arena.ArsenalCrate = crate;
            count++;
        }

        return count;
    }

    private void UpdateUi(EntityUid uid, ArenaArsenalRemoteComponent comp)
    {
        if (!_ui.HasUi(uid, ArenaArsenalRemoteUiKey.Key))
            return;

        // Текущий тир берём с первой попавшейся арены — команда/пульт держат их одинаковыми.
        EntProtoId? current = null;
        var arenas = EntityQueryEnumerator<DuelArenaComponent>();
        if (arenas.MoveNext(out _, out var firstArena))
            current = firstArena.ArsenalCrate;

        var options = new List<ArenaArsenalOption>();
        foreach (var crate in comp.Crates)
        {
            options.Add(new ArenaArsenalOption
            {
                CrateProto = crate.Id,
                Name = GetName(crate),
                Current = current?.Id == crate.Id,
            });
        }

        options.Add(new ArenaArsenalOption
        {
            CrateProto = null,
            Name = Loc.GetString("arena-arsenal-remote-off"),
            Current = current == null,
        });

        _ui.SetUiState(uid, ArenaArsenalRemoteUiKey.Key, new ArenaArsenalRemoteBuiState(options));
    }

    private string GetName(EntProtoId proto)
        => _prototype.TryIndex<EntityPrototype>(proto, out var p) ? p.Name : proto.Id;
}
