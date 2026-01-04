using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Identity.Data.Entities;

public sealed class UserOrganizationClaim
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserOrganizationId { get; set; }

    /// <summary>
    /// Grant: Adds permission beyond role-implied
    /// Deny: Revokes permission even if role-implied
    /// </summary>
    public ClaimType ClaimType { get; set; }

    /// <summary>
    /// The claim value (e.g., "servers:delete", "billing:read")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ClaimValue { get; set; } = null!;

    /// <summary>
    /// Optional resource scoping (e.g., "server", "node")
    /// </summary>
    [MaxLength(50)]
    public string? ResourceType { get; set; }

    /// <summary>
    /// Optional specific resource ID
    /// </summary>
    public Guid? ResourceId { get; set; }

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public Guid GrantedByUserId { get; set; }

    /// <summary>
    /// Optional expiration for temporary grants
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    public UserOrganization UserOrganization { get; set; } = null!;
    public User GrantedBy { get; set; } = null!;
}

public enum ClaimType
{
    None = 0,
    Grant = 1,
    Deny = 2
}
