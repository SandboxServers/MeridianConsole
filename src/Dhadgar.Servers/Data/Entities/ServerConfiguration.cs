using System.ComponentModel.DataAnnotations;
using Dhadgar.Shared.Data;

namespace Dhadgar.Servers.Data.Entities;

/// <summary>
/// Server-specific configuration settings.
/// </summary>
public sealed class ServerConfiguration : BaseEntity
{
    /// <summary>Reference to the parent server.</summary>
    public Guid ServerId { get; set; }
    public Server Server { get; set; } = null!;

    /// <summary>Command line used to start the server.</summary>
    [MaxLength(2000)]
    public string? StartupCommand { get; set; }

    /// <summary>
    /// Game-specific settings stored as JSON.
    /// Examples: server.properties for Minecraft, GameUserSettings.ini for ARK.
    /// </summary>
    public string? GameSettings { get; set; }

    /// <summary>Environment variables for the server process.</summary>
    public string? EnvironmentVariables { get; set; }

    /// <summary>Whether to automatically start the server when the node boots.</summary>
    public bool AutoStart { get; set; }

    /// <summary>Whether to automatically restart the server if it crashes.</summary>
    public bool AutoRestartOnCrash { get; set; }

    /// <summary>Maximum number of auto-restart attempts within the cooldown period.</summary>
    public int MaxAutoRestartAttempts { get; set; } = 3;

    /// <summary>Cooldown period in seconds for auto-restart attempts.</summary>
    public int AutoRestartCooldownSeconds { get; set; } = 300;

    /// <summary>Delay in seconds before auto-restarting after a crash.</summary>
    public int AutoRestartDelaySeconds { get; set; } = 30;

    /// <summary>Graceful shutdown timeout in seconds before force-killing the process.</summary>
    public int ShutdownTimeoutSeconds { get; set; } = 60;

    /// <summary>Java memory allocation flags (for Java-based games like Minecraft).</summary>
    [MaxLength(500)]
    public string? JavaFlags { get; set; }
}
