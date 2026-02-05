namespace Dhadgar.Agent.Core.Commands;

/// <summary>
/// Result of command execution sent back to the control plane.
/// </summary>
public sealed class CommandResult
{
    /// <summary>
    /// Command identifier this result is for.
    /// </summary>
    public required Guid CommandId { get; init; }

    /// <summary>
    /// Node that executed the command.
    /// </summary>
    public required Guid NodeId { get; init; }

    /// <summary>
    /// Execution status.
    /// </summary>
    public required CommandResultStatus Status { get; init; }

    /// <summary>
    /// When execution started.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// When execution completed.
    /// </summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Result payload as JSON (command-type specific).
    /// </summary>
    public string? ResultJson { get; init; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Error code for categorization.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when commandId or nodeId is empty.</exception>
    public static CommandResult Success(
        Guid commandId,
        Guid nodeId,
        DateTimeOffset startedAt,
        string? resultJson = null,
        string? correlationId = null)
    {
        if (commandId == Guid.Empty)
        {
            throw new ArgumentException("CommandId cannot be empty", nameof(commandId));
        }

        if (nodeId == Guid.Empty)
        {
            throw new ArgumentException("NodeId cannot be empty", nameof(nodeId));
        }

        var completedAt = DateTimeOffset.UtcNow;
        if (completedAt < startedAt)
        {
            completedAt = startedAt;
        }

        return new CommandResult
        {
            CommandId = commandId,
            NodeId = nodeId,
            Status = CommandResultStatus.Succeeded,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ResultJson = resultJson,
            CorrelationId = correlationId
        };
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when commandId or nodeId is empty.</exception>
    public static CommandResult Failure(
        Guid commandId,
        Guid nodeId,
        DateTimeOffset startedAt,
        string errorMessage,
        string? errorCode = null,
        string? correlationId = null)
    {
        if (commandId == Guid.Empty)
        {
            throw new ArgumentException("CommandId cannot be empty", nameof(commandId));
        }

        if (nodeId == Guid.Empty)
        {
            throw new ArgumentException("NodeId cannot be empty", nameof(nodeId));
        }

        var completedAt = DateTimeOffset.UtcNow;
        if (completedAt < startedAt)
        {
            completedAt = startedAt;
        }

        return new CommandResult
        {
            CommandId = commandId,
            NodeId = nodeId,
            Status = CommandResultStatus.Failed,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
            CorrelationId = correlationId
        };
    }

    /// <summary>
    /// Creates a rejected result (validation failed).
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when commandId or nodeId is empty.</exception>
    public static CommandResult Rejected(
        Guid commandId,
        Guid nodeId,
        string errorMessage,
        string? errorCode = null,
        string? correlationId = null)
    {
        if (commandId == Guid.Empty)
        {
            throw new ArgumentException("CommandId cannot be empty", nameof(commandId));
        }

        if (nodeId == Guid.Empty)
        {
            throw new ArgumentException("NodeId cannot be empty", nameof(nodeId));
        }

        var now = DateTimeOffset.UtcNow;
        return new CommandResult
        {
            CommandId = commandId,
            NodeId = nodeId,
            Status = CommandResultStatus.Rejected,
            StartedAt = now,
            CompletedAt = now,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
            CorrelationId = correlationId
        };
    }
}

/// <summary>
/// Command execution result status.
/// </summary>
public enum CommandResultStatus
{
    /// <summary>
    /// Command executed successfully.
    /// </summary>
    Succeeded,

    /// <summary>
    /// Command execution failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Command was rejected (validation failed, expired, etc.).
    /// </summary>
    Rejected,

    /// <summary>
    /// Command execution timed out.
    /// </summary>
    TimedOut,

    /// <summary>
    /// Command was cancelled.
    /// </summary>
    Cancelled
}
