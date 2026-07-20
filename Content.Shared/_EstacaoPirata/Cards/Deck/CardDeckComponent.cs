// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Audio;

namespace Content.Shared._EstacaoPirata.Cards.Deck;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class CardDeckComponent : Component
{
    [DataField("shuffleSound")]
    public SoundSpecifier ShuffleSound = new SoundCollectionSpecifier("cardFan");

    [DataField("pickUpSound")]
    public SoundSpecifier PickUpSound = new SoundCollectionSpecifier("cardSlide");

    [DataField("placeDownSound")]
    public SoundSpecifier PlaceDownSound = new SoundCollectionSpecifier("cardShove");

    [DataField("yOffset")]
    public float YOffset = 0.02f;

    [DataField("scale")]
    public float Scale = 1;

    [DataField("limit")]
    public int CardLimit = 5;

    /// <summary>
    /// Wega: сколько карт раздаёт верб «Раздать» каждому игроку рядом. Без раздачи старт партии
    /// стоил бы по одному альт-клику на карту (7 карт × 2 игрока = 14 действий).
    /// 6 — «дурак» на обычной колоде; у колоды Kotahi переопределено на 7.
    /// </summary>
    [DataField("dealCount")]
    public int DealCount = 6;

    /// <summary>Wega: радиус поиска игроков для раздачи, тайлы.</summary>
    [DataField("dealRange")]
    public float DealRange = 2.5f;
}