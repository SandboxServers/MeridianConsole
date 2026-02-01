namespace Dhadgar.Mods.Data.Entities;

/// <summary>
/// Records a mod download for analytics purposes.
/// </summary>
public sealed class ModDownload
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The version that was downloaded.</summary>
    public Guid ModVersionId { get; set; }
    public ModVersion ModVersion { get; set; } = null!;

    /// <summary>Organization that downloaded (for org-level analytics).</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>Node that downloaded (optional, for node-level analytics).</summary>
    public Guid? NodeId { get; set; }

    /// <summary>Server that downloaded (optional, for server-level analytics).</summary>
    public Guid? ServerId { get; set; }

    /// <summary>When the download occurred.</summary>
    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>IP address hash (for abuse detection, not tracking).</summary>
    public string? IpAddressHash { get; set; }
}
