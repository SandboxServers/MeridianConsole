using System.ComponentModel.DataAnnotations;
using Dhadgar.Shared.Data;

namespace Dhadgar.Servers.Data.Entities;

/// <summary>
/// Reusable server configuration template.
/// </summary>
public sealed class ServerTemplate : BaseEntity
{
    /// <summary>Organization that owns this template (null for system templates).</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>Template name.</summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Template description.</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Game type this template applies to.</summary>
    [Required]
    [MaxLength(50)]
    public string GameType { get; set; } = string.Empty;

    /// <summary>Whether this template is visible to all organizations.</summary>
    public bool IsPublic { get; set; }

    /// <summary>Whether this template is archived and hidden from normal selection.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Default CPU limit in millicores.</summary>
    public int DefaultCpuLimitMillicores { get; set; }

    /// <summary>Default memory limit in megabytes.</summary>
    public int DefaultMemoryLimitMb { get; set; }

    /// <summary>Default disk limit in megabytes.</summary>
    public int DefaultDiskLimitMb { get; set; }

    /// <summary>Default startup command.</summary>
    [MaxLength(2000)]
    public string? DefaultStartupCommand { get; set; }

    /// <summary>Default game settings JSON.</summary>
    public string? DefaultGameSettings { get; set; }

    /// <summary>Default environment variables JSON.</summary>
    public string? DefaultEnvironmentVariables { get; set; }

    /// <summary>Default Java flags for Java-based games.</summary>
    [MaxLength(500)]
    public string? DefaultJavaFlags { get; set; }

    /// <summary>Default port configurations JSON.</summary>
    public string? DefaultPorts { get; set; }

    /// <summary>Number of times this template has been used.</summary>
    public int UsageCount { get; set; }

    // Navigation properties
    public ICollection<Server> Servers { get; set; } = [];
}
