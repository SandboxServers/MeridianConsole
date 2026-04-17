using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Mods;

/// <summary>
/// Configuration options for the Mods service.
/// </summary>
public sealed class ModsOptions
{
    public const string SectionName = "Mods";

    /// <summary>
    /// Maximum file size in bytes for mod uploads.
    /// </summary>
    [Range(1, 10737418240)] // 10GB max
    public long MaxFileSizeBytes { get; set; } = 1073741824; // 1GB

    /// <summary>
    /// Signed URL expiration in minutes.
    /// </summary>
    [Range(1, 1440)]
    public int DownloadUrlExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Interval in seconds for flushing download counts to the database.
    /// </summary>
    [Range(10, 600)]
    public int DownloadCountFlushIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Cache TTL in minutes for mod metadata.
    /// </summary>
    [Range(1, 1440)]
    public int CacheTtlMinutes { get; set; } = 15;

    /// <summary>
    /// Maximum number of versions to return in mod detail.
    /// </summary>
    [Range(1, 100)]
    public int MaxVersionsInDetail { get; set; } = 20;

    /// <summary>
    /// Download milestones for notifications.
    /// </summary>
    public IList<long> DownloadMilestones { get; set; } = [100, 1000, 10000, 100000, 1000000];
}
