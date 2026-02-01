using System.ComponentModel.DataAnnotations;
using Dhadgar.Shared.Data;

namespace Dhadgar.Mods.Data.Entities;

/// <summary>
/// Primary entity representing a mod in the registry.
/// </summary>
public sealed class Mod : BaseEntity
{
    /// <summary>Organization that owns this mod (null for public/community mods).</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>Mod name.</summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>URL-friendly slug (unique per organization).</summary>
    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    /// <summary>Mod description.</summary>
    [MaxLength(4000)]
    public string? Description { get; set; }

    /// <summary>Mod author name.</summary>
    [MaxLength(200)]
    public string? Author { get; set; }

    /// <summary>Category identifier.</summary>
    public Guid? CategoryId { get; set; }
    public ModCategory? Category { get; set; }

    /// <summary>Game type this mod is for.</summary>
    [Required]
    [MaxLength(50)]
    public string GameType { get; set; } = string.Empty;

    /// <summary>Total number of downloads across all versions.</summary>
    public long TotalDownloads { get; set; }

    /// <summary>Whether this mod is publicly discoverable.</summary>
    public bool IsPublic { get; set; }

    /// <summary>Whether this mod is archived and hidden from normal lists.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Project URL (GitHub, CurseForge, etc.).</summary>
    [MaxLength(500)]
    public string? ProjectUrl { get; set; }

    /// <summary>Icon URL for display.</summary>
    [MaxLength(500)]
    public string? IconUrl { get; set; }

    /// <summary>
    /// User-defined tags for categorization.
    /// </summary>
#pragma warning disable CA1002 // Do not expose generic lists - required for EF Core JSONB mapping
    public List<string> Tags { get; set; } = [];
#pragma warning restore CA1002

    // Navigation properties
    public ICollection<ModVersion> Versions { get; set; } = [];
}
