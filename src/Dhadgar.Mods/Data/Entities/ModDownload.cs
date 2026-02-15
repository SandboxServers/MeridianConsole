using System.ComponentModel.DataAnnotations;
using Dhadgar.Shared.Data;

namespace Dhadgar.Mods.Data.Entities;

/// <summary>
/// Records a mod download for analytics purposes.
/// </summary>
public sealed class ModDownload : BaseEntity, ITenantScoped
{
    /// <summary>The version that was downloaded.</summary>
    public Guid ModVersionId { get; set; }
    public ModVersion ModVersion { get; set; } = null!;

    /// <summary>Organization that downloaded (for org-level analytics).</summary>
    public Guid? OrganizationId { get; set; }

    // ITenantScoped requires non-nullable OrganizationId, but for downloads
    // we allow anonymous downloads without org context, so we implement explicitly
    Guid ITenantScoped.OrganizationId => OrganizationId ?? Guid.Empty;

    /// <summary>Node that downloaded (optional, for node-level analytics).</summary>
    public Guid? NodeId { get; set; }

    /// <summary>Server that downloaded (optional, for server-level analytics).</summary>
    public Guid? ServerId { get; set; }

    /// <summary>When the download occurred (set via TimeProvider at creation).</summary>
    public DateTime DownloadedAt { get; set; }

    /// <summary>IP address hash (for abuse detection, not tracking).</summary>
    [MaxLength(IpAddressHashMaxLength)]
    public string? IpAddressHash { get; set; }

    public const int IpAddressHashMaxLength = 64;
}
