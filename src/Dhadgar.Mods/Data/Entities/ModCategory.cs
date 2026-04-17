using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Mods.Data.Entities;

/// <summary>
/// Reference data for mod categories.
/// </summary>
public sealed class ModCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Category name.</summary>
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>URL-friendly slug.</summary>
    [Required]
    [MaxLength(50)]
    public string Slug { get; set; } = string.Empty;

    /// <summary>Category description.</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Icon name or URL.</summary>
    [MaxLength(200)]
    public string? Icon { get; set; }

    /// <summary>Sort order for display.</summary>
    public int SortOrder { get; set; }

    /// <summary>Parent category (for hierarchical categories).</summary>
    public Guid? ParentId { get; set; }
    public ModCategory? Parent { get; set; }

    // Navigation properties
    public ICollection<Mod> Mods { get; set; } = [];
    public ICollection<ModCategory> Children { get; set; } = [];
}
