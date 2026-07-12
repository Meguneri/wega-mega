using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

//
// Contains model definitions related to persistent raid stash progress.
//

internal static class ModelRaidStash
{
    public static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RaidStash>()
            .HasIndex(r => r.PlayerUserId)
            .IsUnique();

        modelBuilder.Entity<RaidStash>()
            .HasOne(r => r.Player)
            .WithMany()
            .HasForeignKey(r => r.PlayerUserId)
            .HasPrincipalKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Persistent raid stash data for a single player.
/// Stores currency balance, serialized stash items and raid statistics.
/// </summary>
[Table("raid_stash")]
public sealed class RaidStash
{
    /// <summary>
    /// Player user id. Also serves as the primary key — one stash per player.
    /// </summary>
    [Required, Key, ForeignKey("Player")]
    public Guid PlayerUserId { get; set; }

    /// <summary>
    /// Navigation property to the player record.
    /// </summary>
    [ForeignKey(nameof(PlayerUserId))]
    public Player Player { get; set; } = null!;

    /// <summary>
    /// JSON payload with the full stash snapshot.
    /// On Postgres this column is typed as jsonb via provider-specific configuration.
    /// </summary>
    [Required]
    public string StashData { get; set; } = "{}";

    /// <summary>
    /// SHA-256 checksum of <see cref="StashData"/> for integrity validation.
    /// </summary>
    [Required]
    public string Checksum { get; set; } = string.Empty;

    /// <summary>
    /// Snapshot format version, for future migrations.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Last time the stash was updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
