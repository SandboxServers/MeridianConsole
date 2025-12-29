using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Identity.Data.Entities;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// External authentication ID from Better Auth (sub claim from exchange token)
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string ExternalAuthId { get; set; } = null!;

    [Required]
    [MaxLength(320)]
    [EmailAddress]
    public string Email { get; set; } = null!;

    public bool EmailVerified { get; set; }

    /// <summary>
    /// User's preferred organization (sticky choice across sessions)
    /// </summary>
    public Guid? PreferredOrganizationId { get; set; }

    /// <summary>
    /// Flag indicating user has passkeys registered in Better Auth
    /// Synced via webhook - actual passkeys stored in Better Auth
    /// </summary>
    public bool HasPasskeysRegistered { get; set; }

    public DateTime? LastPasskeyAuthAt { get; set; }
    public DateTime? LastAuthenticatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// PostgreSQL xmin-based optimistic concurrency
    /// </summary>
    public uint Version { get; set; }

    public Organization? PreferredOrganization { get; set; }
    public ICollection<UserOrganization> Organizations { get; set; } = new List<UserOrganization>();
    public ICollection<LinkedAccount> LinkedAccounts { get; set; } = new List<LinkedAccount>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
