using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Console.Data.Entities;

/// <summary>
/// Console output stored in cold storage (PostgreSQL) for historical queries.
/// </summary>
public sealed class ConsoleHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Server this output belongs to.</summary>
    public Guid ServerId { get; set; }

    /// <summary>Organization that owns the server.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>Type of console output.</summary>
    public ConsoleOutputType OutputType { get; set; }

    /// <summary>Console output content.</summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>When this output was received.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Sequence number for ordering within a session.</summary>
    public long SequenceNumber { get; set; }

    /// <summary>Session ID for grouping related output.</summary>
    public Guid? SessionId { get; set; }
}

/// <summary>
/// Type of console output.
/// </summary>
public enum ConsoleOutputType
{
    /// <summary>Standard output from the server process.</summary>
    StdOut = 0,

    /// <summary>Error output from the server process.</summary>
    StdErr = 1,

    /// <summary>Command executed by user.</summary>
    Command = 2,

    /// <summary>System message (start, stop, crash).</summary>
    System = 3,

    /// <summary>Warning message.</summary>
    Warning = 4
}
