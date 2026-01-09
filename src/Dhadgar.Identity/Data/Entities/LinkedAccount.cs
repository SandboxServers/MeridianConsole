using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Identity.Data.Entities;

public sealed class LinkedAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>
    /// OAuth provider (e.g., "discord", "steam", "epic")
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Provider { get; set; } = null!;

    /// <summary>
    /// Provider's user ID
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string ProviderAccountId { get; set; } = null!;

    /// <summary>
    /// Provider-specific metadata (avatar, username, etc.)
    /// Stored as JSON in PostgreSQL
    /// </summary>
    public LinkedAccountMetadata? ProviderMetadata { get; set; }

    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }

    public User User { get; set; } = null!;
}

public sealed class LinkedAccountMetadata
{
    public string? AvatarUrl { get; set; }
    public string? DisplayName { get; set; }
    public string? Username { get; set; }
    public Dictionary<string, string> ExtraData { get; set; } = new();
}
