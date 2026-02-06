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
    /// Regex patterns for allowed game server commands.
    /// Only commands matching at least one pattern will be dispatched.
    /// </summary>
    public IList<string> AllowedCommandPatterns { get; set; } =
    [
        @"^(say|tell|msg|w|whisper|me)\s",
        @"^(stop|save-all|save-on|save-off|list|seed|difficulty|gamemode|gamerule|weather|time|whitelist|ban|pardon|kick|op|deop|tp|teleport|give|clear|effect|enchant|xp|scoreboard|title|bossbar|data|execute|function|reload|summon|kill|setblock|fill|clone|particle|playsound|stopsound|spreadplayers|tag|team|trigger|worldborder|advancement|recipe|loot|item|place|ride|damage|return|tick)\s?",
        @"^/",
        @"^(help|version|tps|plugins|mods|status|info|debug)\s*$"
    ];

    /// <summary>
    /// Timeout in milliseconds for command regex matching to prevent ReDoS.
    /// </summary>
    [Range(10, 5000)]
    public int CommandRegexTimeoutMs { get; set; } = 1000;
}
