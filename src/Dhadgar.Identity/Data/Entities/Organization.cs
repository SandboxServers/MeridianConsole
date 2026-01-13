using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Identity.Data.Entities;

public sealed class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// URL-safe identifier (e.g., "acme-corp")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = null!;

    /// <summary>
    /// User who owns this organization (typically creator)
    /// </summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Organization settings stored as JSON
    /// </summary>
    public OrganizationSettings Settings { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public uint Version { get; set; }

    public User Owner { get; set; } = null!;
    public ICollection<UserOrganization> Members { get; set; } = new List<UserOrganization>();
    public ICollection<OrganizationRole> Roles { get; set; } = new List<OrganizationRole>();
}

public sealed class OrganizationSettings
{
    public bool AllowMemberInvites { get; set; } = true;
    public bool RequireEmailVerification { get; set; } = true;
    public int MaxMembers { get; set; } = 10;
    public Dictionary<string, string> CustomSettings { get; set; } = new();
}
