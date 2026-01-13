using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Dhadgar.Identity.Data.Entities;

/// <summary>
/// Primary user record backed by ASP.NET Core Identity.
/// </summary>
public sealed class User : IdentityUser<Guid>
{
    /// <summary>
    /// External authentication ID from Better Auth (sub claim from exchange token)
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string ExternalAuthId { get; set; } = null!;

    [MaxLength(200)]
    public string? DisplayName { get; set; }

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
    /// PostgreSQL xmin-based optimistic concurrency token.
    /// With Npgsql 7.0+, IsRowVersion() on uint automatically maps to PostgreSQL xmin system column.
    /// </summary>
    public uint Version { get; set; }

    public Organization? PreferredOrganization { get; set; }
    public ICollection<UserOrganization> Organizations { get; set; } = new List<UserOrganization>();
    public ICollection<LinkedAccount> LinkedAccounts { get; set; } = new List<LinkedAccount>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
