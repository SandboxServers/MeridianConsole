using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Identity.Data.Entities;

public sealed class ClaimDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Claim name (e.g., "servers:read", "nodes:manage")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Category for grouping (e.g., "servers", "billing", "organization")
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = null!;

    /// <summary>
    /// System claims cannot be deleted
    /// </summary>
    public bool IsSystemClaim { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
