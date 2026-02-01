using System.ComponentModel.DataAnnotations;
using Dhadgar.Shared.Data;

namespace Dhadgar.Mods.Data.Entities;

/// <summary>
/// Represents a dependency from one mod version to another mod.
/// </summary>
public sealed class ModDependency : BaseEntity
{
    /// <summary>The mod version that has this dependency.</summary>
    public Guid ModVersionId { get; set; }
    public ModVersion ModVersion { get; set; } = null!;

    /// <summary>The mod that is required.</summary>
    public Guid DependsOnModId { get; set; }
    public Mod DependsOnMod { get; set; } = null!;

    /// <summary>Minimum version required (semver range expression).</summary>
    [MaxLength(100)]
    public string? MinVersion { get; set; }

    /// <summary>Maximum version allowed (semver range expression).</summary>
    [MaxLength(100)]
    public string? MaxVersion { get; set; }

    /// <summary>Whether this dependency is optional.</summary>
    public bool IsOptional { get; set; }
}
