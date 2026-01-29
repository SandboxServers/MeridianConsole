using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Nodes.Data.Entities;

/// <summary>
/// One-time use token for agent enrollment.
/// Tokens are hashed for storage - never store plaintext.
/// </summary>
public sealed class EnrollmentToken
{
    public Guid Id { get; set; }

    /// <summary>Organization this token belongs to.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>SHA-256 hash of the token. Never store plaintext.</summary>
    [Required]
    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Optional label for administrative identification.</summary>
    [MaxLength(200)]
    public string? Label { get; set; }

    /// <summary>When this token expires and can no longer be used.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>When this token was used (null if unused).</summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>Node ID that used this token (null if unused).</summary>
    public Guid? UsedByNodeId { get; set; }

    /// <summary>User ID who created this token.</summary>
    [Required]
    [MaxLength(100)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Whether this token has been revoked.</summary>
    public bool IsRevoked { get; set; }
}
