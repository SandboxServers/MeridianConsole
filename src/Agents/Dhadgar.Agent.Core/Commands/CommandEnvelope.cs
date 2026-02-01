using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Agent.Core.Commands;

/// <summary>
/// Envelope for commands received from the control plane.
/// Contains metadata for validation and tracking.
/// </summary>
public sealed class CommandEnvelope
{
    /// <summary>
    /// Maximum length for command type identifier.
    /// </summary>
    public const int MaxCommandTypeLength = 128;

    /// <summary>
    /// Maximum payload size (256 KB).
    /// </summary>
    public const int MaxPayloadLength = 256 * 1024;

    /// <summary>
    /// Maximum signature length.
    /// </summary>
    public const int MaxSignatureLength = 2048;

    /// <summary>
    /// Maximum correlation ID length.
    /// </summary>
    public const int MaxCorrelationIdLength = 128;
    /// <summary>
    /// Unique identifier for this command instance.
    /// </summary>
    public required Guid CommandId { get; init; }

    /// <summary>
    /// Type of command to execute.
    /// </summary>
    [MaxLength(MaxCommandTypeLength)]
    public required string CommandType { get; init; }

    /// <summary>
    /// Target node identifier.
    /// </summary>
    public required Guid NodeId { get; init; }

    /// <summary>
    /// Organization that owns this node.
    /// </summary>
    public required Guid OrganizationId { get; init; }

    /// <summary>
    /// User who initiated the command (null for system-initiated).
    /// </summary>
    public Guid? InitiatedByUserId { get; init; }

    /// <summary>
    /// When the command was issued.
    /// </summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>
    /// Command expiration time. Commands received after this time should be rejected.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Command payload as JSON.
    /// </summary>
    [MaxLength(MaxPayloadLength)]
    public required string PayloadJson { get; init; }

    /// <summary>
    /// Cryptographic signature of the command for verification.
    /// </summary>
    [MaxLength(MaxSignatureLength)]
    public string? Signature { get; init; }

    /// <summary>
    /// Priority level for command execution.
    /// </summary>
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// </summary>
    [MaxLength(MaxCorrelationIdLength)]
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Command priority levels.
/// </summary>
public enum CommandPriority
{
    /// <summary>
    /// Low priority, can be delayed.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Normal priority.
    /// </summary>
    Normal = 1,

    /// <summary>
    /// High priority, process before normal commands.
    /// </summary>
    High = 2,

    /// <summary>
    /// Critical priority, process immediately.
    /// </summary>
    Critical = 3
}
