using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Wega.Duel;

/// <summary>
/// Пресет снаряжения для дуэльной арены. Используется терминалом выбора набора и выдаётся
/// бойцам в ящике при старте боя. Все предметы помечаются ArenaIssuedItem и чистятся после раунда.
/// </summary>
[Prototype]
public sealed partial class ArenaLoadoutPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name = string.Empty;

    [DataField("description")]
    public string Description = string.Empty;

    [DataField("icon")]
    public SpriteSpecifier? Icon;

    /// <summary>
    /// Предметы, которые появятся в ящике набора. Если пусто — набор полагается только на startingGear.
    /// </summary>
    [DataField("contents")]
    public List<ArenaLoadoutItem> Contents = new();

    /// <summary>
    /// Опциональный startingGear — выдаётся напрямую на тело бойца (броня, одежда, слоты).
    /// </summary>
    [DataField("startingGear")]
    public ProtoId<StartingGearPrototype>? StartingGear;

    /// <summary>
    /// Категория для группировки в UI (Melee, Ranged, Heavy, Special).
    /// </summary>
    [DataField("category")]
    public string Category = string.Empty;
}

[DataRecord]
public sealed partial record ArenaLoadoutItem
{
    [DataField("id")]
    public EntProtoId EntityId { get; init; }

    [DataField("amount")]
    public int Amount { get; init; } = 1;
}

[Serializable, NetSerializable]
public enum ArenaLoadoutUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed partial class ArenaLoadoutSelectionState : BoundUserInterfaceState
{
    public readonly List<ProtoId<ArenaLoadoutPrototype>> AvailableLoadouts;

    public ArenaLoadoutSelectionState(List<ProtoId<ArenaLoadoutPrototype>> availableLoadouts)
    {
        AvailableLoadouts = availableLoadouts;
    }
}

[Serializable, NetSerializable]
public sealed partial class ArenaLoadoutSelectedMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity User;
    public readonly ProtoId<ArenaLoadoutPrototype> LoadoutId;

    public ArenaLoadoutSelectedMessage(NetEntity user, ProtoId<ArenaLoadoutPrototype> loadoutId)
    {
        User = user;
        LoadoutId = loadoutId;
    }
}
