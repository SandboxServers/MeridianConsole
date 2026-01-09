using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Identity.Data.Entities;

public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>
    /// Hashed refresh token (SHA256)
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string TokenHash { get; set; } = null!;

    /// <summary>
    /// Organization context for this token
    /// </summary>
    public Guid OrganizationId { get; set; }

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Optional device information for audit trail
    /// </summary>
    [MaxLength(500)]
    public string? DeviceInfo { get; set; }

    public User User { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
}
