using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Shared.FixedPoint;
using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Raid;

/// <summary>
/// Persistent snapshot of a player's raid stash.
/// Stored in the database as JSON and kept in memory while the player is connected.
/// </summary>
public sealed class RaidStashSnapshot
{
    /// <summary>
    /// Snapshot format version. Incremented when the schema changes.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Persistent currency balances (e.g., Telecrystals).
    /// </summary>
    [JsonConverter(typeof(RaidStashCurrencyDictionaryConverter))]
    public Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2> Currency { get; set; } = new();

    /// <summary>
    /// Serialized items stored in the player's stash.
    /// </summary>
    public List<RaidStashItem> Items { get; set; } = new();

    /// <summary>
    /// Raid statistics tracked across sessions.
    /// </summary>
    public RaidStats Stats { get; set; } = new();

    public RaidStashSnapshot()
    {
    }
}

/// <summary>
/// A single persisted item (or item container) in the raid stash.
/// The YAML blob is produced by <see cref="Robust.Shared.EntitySerialization.Systems.MapLoaderSystem.SerializeEntitiesRecursive"/>.
/// </summary>
public sealed class RaidStashItem
{
    /// <summary>
    /// Serialized entity YAML, including recursive children.
    /// </summary>
    public string YamlBlob { get; set; } = string.Empty;

    /// <summary>
    /// When this item was originally extracted from a raid.
    /// </summary>
    public DateTime ExtractedAt { get; set; }
}

/// <summary>
/// Cross-session raid statistics for a player.
/// </summary>
public sealed class RaidStats
{
    public int RaidsCompleted { get; set; }

    public int RaidsFailed { get; set; }

    public int RaidsKia { get; set; }

    public int RaidsMia { get; set; }

    public long TotalLootValue { get; set; }
}
