using System.ComponentModel.DataAnnotations;
using Dhadgar.Shared.Data;

namespace Dhadgar.Mods.Data.Entities;

/// <summary>
/// A specific version of a mod.
/// </summary>
public sealed class ModVersion : BaseEntity
{
    /// <summary>Reference to the parent mod.</summary>
    public Guid ModId { get; set; }
    public Mod Mod { get; set; } = null!;

    /// <summary>Semantic version string (e.g., "1.2.3").</summary>
    [Required]
    [MaxLength(50)]
    public string Version { get; set; } = string.Empty;

    /// <summary>Major version number (parsed from Version).</summary>
    public int Major { get; set; }

    /// <summary>Minor version number (parsed from Version).</summary>
    public int Minor { get; set; }

    /// <summary>Patch version number (parsed from Version).</summary>
    public int Patch { get; set; }

    /// <summary>Prerelease identifier (e.g., "alpha", "beta.1").</summary>
    [MaxLength(50)]
    public string? Prerelease { get; set; }

    /// <summary>Build metadata (e.g., "build.123").</summary>
    [MaxLength(100)]
    public string? BuildMetadata { get; set; }

    /// <summary>Release notes for this version.</summary>
    [MaxLength(10000)]
    public string? ReleaseNotes { get; set; }

    /// <summary>SHA-256 hash of the mod file.</summary>
    [MaxLength(64)]
    public string? FileHash { get; set; }

    /// <summary>Size of the mod file in bytes.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>File storage path or URL.</summary>
    [MaxLength(500)]
    public string? FilePath { get; set; }

    /// <summary>Minimum game version this mod supports.</summary>
    [MaxLength(50)]
    public string? MinGameVersion { get; set; }

    /// <summary>Maximum game version this mod supports.</summary>
    [MaxLength(50)]
    public string? MaxGameVersion { get; set; }

    /// <summary>Whether this is the latest stable version.</summary>
    public bool IsLatest { get; set; }

    /// <summary>Whether this is a prerelease version.</summary>
    public bool IsPrerelease { get; set; }

    /// <summary>Whether this version is deprecated.</summary>
    public bool IsDeprecated { get; set; }

    /// <summary>Reason for deprecation.</summary>
    [MaxLength(500)]
    public string? DeprecationReason { get; set; }

    /// <summary>Number of downloads for this version.</summary>
    public long DownloadCount { get; set; }

    /// <summary>When this version was published.</summary>
    public DateTime? PublishedAt { get; set; }

    // Navigation properties
    public ICollection<ModDependency> Dependencies { get; set; } = [];
    public ICollection<ModCompatibility> Incompatibilities { get; set; } = [];
}
