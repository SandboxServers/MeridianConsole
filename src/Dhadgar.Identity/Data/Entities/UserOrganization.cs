using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Identity.Data.Entities;

public sealed class UserOrganization
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Role within this organization (owner, admin, operator, viewer)
    /// Role implies a set of claims (see RoleDefinitions)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = "viewer";

    public bool IsActive { get; set; } = true;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }

    public Guid? InvitedByUserId { get; set; }
    public DateTime? InvitationAcceptedAt { get; set; }

    public User User { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
    public User? InvitedBy { get; set; }
    public ICollection<UserOrganizationClaim> CustomClaims { get; set; } = new List<UserOrganizationClaim>();
}
