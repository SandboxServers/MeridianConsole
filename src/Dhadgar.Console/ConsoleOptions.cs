using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Console;

/// <summary>
/// Configuration options for the Console service.
/// </summary>
public sealed class ConsoleOptions
{
    public const string SectionName = "Console";

    /// <summary>
    /// TTL in minutes for hot storage (Redis) entries.
    /// </summary>
    [Range(5, 1440)]
    public int HotStorageTtlMinutes { get; set; } = 60;

    /// <summary>
    /// Number of days to retain console history in cold storage.
    /// </summary>
    [Range(1, 365)]
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Maximum length of a console command.
    /// </summary>
    [Range(1, 10000)]
    public int MaxCommandLength { get; set; } = 2000;

    /// <summary>
    /// Interval in seconds for history cleanup background service.
    /// </summary>
    [Range(60, 3600)]
    public int HistoryCleanupIntervalSeconds { get; set; } = 3600;

    /// <summary>
    /// Interval in seconds for session cleanup background service.
    /// </summary>
    [Range(30, 600)]
    public int SessionCleanupIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Regex patterns for dangerous commands that should be blocked.
    /// </summary>
    public IList<string> DangerousCommandPatterns { get; set; } =
    [
        @"^(rm|del|delete|format)\s+-rf?\s+[/\\]",
        @"^(shutdown|poweroff|reboot|halt)\s*",
        @":(){ :|:& };:",
        @">\s*/dev/(sda|null|zero)",
        @"mkfs\.",
        @"dd\s+if="
    ];
}
