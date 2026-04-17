using System.ComponentModel.DataAnnotations;
using Dhadgar.Shared.Data;

namespace Dhadgar.Mods.Data.Entities;

/// <summary>
/// Represents a known incompatibility between mod versions.
/// </summary>
public sealed class ModCompatibility : BaseEntity
{
    /// <summary>The mod version that has the incompatibility.</summary>
    public Guid ModVersionId { get; set; }
    public ModVersion ModVersion { get; set; } = null!;

    /// <summary>The mod that is incompatible.</summary>
    public Guid IncompatibleWithModId { get; set; }
    public Mod IncompatibleWithMod { get; set; } = null!;

    /// <summary>Minimum version that's incompatible (null means all versions).</summary>
    [MaxLength(100)]
    public string? MinVersion { get; set; }

    /// <summary>Maximum version that's incompatible (null means all versions).</summary>
    [MaxLength(100)]
    public string? MaxVersion { get; set; }

    /// <summary>Reason for the incompatibility.</summary>
    [MaxLength(1000)]
    public string? Reason { get; set; }

    /// <summary>When this incompatibility was verified.</summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>Whether this was reported by a user (vs verified by maintainers).</summary>
    public bool IsUserReported { get; set; }
}
