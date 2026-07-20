// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._EstacaoPirata.Cards.Card;
using Content.Shared._EstacaoPirata.Cards.Hand;
using Content.Shared._EstacaoPirata.Cards.Stack;
using Content.Shared.Hands.Components;
using Content.Shared.Audio;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Shared._EstacaoPirata.Cards.Deck;

/// <summary>
/// This handles card decks
///
/// </summary>
public sealed partial class CardDeckSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CardStackSystem _cardStackSystem = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!; // Wega: раздача карт
    public readonly EntProtoId CardDeckBaseName = "CardDeckBase";
    /// <summary>Wega: прототип «руки» (веера) — им раздаём карты игрокам.</summary>
    public readonly EntProtoId CardHandBaseName = "CardHandBase";

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<CardDeckComponent, GetVerbsEvent<AlternativeVerb>>(AddTurnOnVerb);
    }

    private void AddTurnOnVerb(EntityUid uid, CardDeckComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        if (!TryComp(uid, out CardStackComponent? comp))
            return;

        args.Verbs.Add(new AlternativeVerb()
        {
            Act = () => TryShuffle(uid, component, comp),
            Text = Loc.GetString("cards-verb-shuffle"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/die.svg.192dpi.png")),
            Priority = 4
        });
        // Wega: раздача — иначе старт партии стоит по альт-клику на каждую карту.
        args.Verbs.Add(new AlternativeVerb()
        {
            Act = () => TryDeal(uid, component, comp, args.User),
            Text = Loc.GetString("cards-verb-deal", ("count", component.DealCount)),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/eject.svg.192dpi.png")),
            Priority = 5
        });
        args.Verbs.Add(new AlternativeVerb()
        {
            Act = () => TrySplit(args.Target, component, comp, args.User),
            Text = Loc.GetString("cards-verb-split"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/dot.svg.192dpi.png")),
            Priority = 3
        });
        args.Verbs.Add(new AlternativeVerb()
        {
            Act = () => TryOrganize(uid, component, comp, true),
            Text = Loc.GetString("cards-verb-organize-down"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/flip.svg.192dpi.png")),
            Priority = 2
        });
        args.Verbs.Add(new AlternativeVerb()
        {
            Act = () => TryOrganize(uid, component, comp, false),
            Text = Loc.GetString("cards-verb-organize-up"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/flip.svg.192dpi.png")),
            Priority = 1
        });
    }

    /// <summary>
    /// Wega: раздаёт по <see cref="CardDeckComponent.DealCount"/> карт каждому игроку рядом
    /// (включая раздающего) — веером, рубашкой вверх, прямо в руки. Кому не хватило рук или
    /// карт в колоде — пропускаем; остаток колоды остаётся на месте.
    /// </summary>
    private void TryDeal(EntityUid uid, CardDeckComponent deck, CardStackComponent stack, EntityUid user)
    {
        if (_net.IsClient)
            return;

        if (deck.DealCount <= 0 || stack.Cards.Count < deck.DealCount)
        {
            _popup.PopupEntity(Loc.GetString("cards-deal-not-enough"), uid, user);
            return;
        }

        // Раздающий первым, остальные — по мере удалённости; так порядок раздачи предсказуем.
        var origin = _transform.GetMapCoordinates(uid);
        var receivers = new List<EntityUid>();
        if (HasComp<HandsComponent>(user))
            receivers.Add(user);

        var query = EntityQueryEnumerator<HandsComponent, TransformComponent>();
        while (query.MoveNext(out var mob, out _, out var xform))
        {
            if (mob == user || !HasComp<ActorComponent>(mob))
                continue;
            var pos = _transform.GetMapCoordinates(xform);
            if (pos.MapId != origin.MapId || (pos.Position - origin.Position).Length() > deck.DealRange)
                continue;
            receivers.Add(mob);
        }

        if (receivers.Count == 0)
            return;

        var dealt = 0;
        foreach (var receiver in receivers)
        {
            if (stack.Cards.Count < deck.DealCount)
                break;

            var hand = SpawnInSameParent(CardHandBaseName, uid);
            if (!TryComp<CardStackComponent>(hand, out var handStack))
            {
                QueueDel(hand);
                break;
            }

            _cardStackSystem.TransferNLastCardFromStacks(user, deck.DealCount, uid, stack, hand, handStack);

            // Веер приходит рубашкой вверх: свои номиналы владелец смотрит через «Взять карту».
            if (TryComp<CardHandComponent>(hand, out var handComp))
                handComp.Flipped = true;
            _cardStackSystem.FlipAllCards(hand, handStack, true);

            if (!_hands.TryPickupAnyHand(receiver, hand))
                continue; // руки заняты — веер остаётся лежать рядом, поднимут сами

            dealt++;
        }

        if (dealt > 0)
            _audio.PlayPvs(deck.PickUpSound, uid);
    }

    private void TrySplit(EntityUid uid, CardDeckComponent deck, CardStackComponent stack, EntityUid user)
    {
        if (stack.Cards.Count <= 1)
            return;

        _audio.PlayPredicted(deck.PickUpSound, Transform(uid).Coordinates, user);

        if (!_net.IsServer)
            return;

        var cardDeck = SpawnInSameParent(CardDeckBaseName, uid);

        EnsureComp<CardStackComponent>(cardDeck, out var deckStack);

        _cardStackSystem.TransferNLastCardFromStacks(user, stack.Cards.Count / 2, uid, stack, cardDeck, deckStack);
        _hands.PickupOrDrop(user, cardDeck);
    }

    private void TryShuffle(EntityUid deck, CardDeckComponent comp, CardStackComponent? stack)
    {
        _cardStackSystem.ShuffleCards(deck, stack);
        if (_net.IsClient)
            return;

        _audio.PlayPvs(comp.ShuffleSound, deck, AudioHelpers.WithVariation(0.05f, _random));
        _popup.PopupEntity(Loc.GetString("card-verb-shuffle-success", ("target", MetaData(deck).EntityName)), deck);
    }

    private void TryOrganize(EntityUid deck, CardDeckComponent comp, CardStackComponent? stack, bool isFlipped)
    {
        if (_net.IsClient)
            return;
        _cardStackSystem.FlipAllCards(deck, stack, isFlipped: isFlipped);

        _audio.PlayPvs(comp.ShuffleSound, deck, AudioHelpers.WithVariation(0.05f, _random));
        _popup.PopupEntity(Loc.GetString("card-verb-organize-success", ("target", MetaData(deck).EntityName), ("facedown", isFlipped)), deck);
    }

    private EntityUid SpawnInSameParent(string prototype, EntityUid uid)
    {
        if (_container.IsEntityOrParentInContainer(uid) &&
            _container.TryGetOuterContainer(uid, Transform(uid), out var container))
        {
            return SpawnInContainerOrDrop(prototype, container.Owner, container.ID);
        }
        return Spawn(prototype, Transform(uid).Coordinates);
    }
}