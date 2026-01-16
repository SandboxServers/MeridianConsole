using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Identity.Data.Entities;

public sealed class OrganizationRole
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string NormalizedName { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    public Collection<string> Permissions { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}
