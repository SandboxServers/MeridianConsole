namespace Dhadgar.Contracts.Console;

// Console service contracts for SignalR and MassTransit

/// <summary>
/// Type of console output.
/// </summary>
public enum ConsoleOutputType
{
    StdOut = 0,
    StdErr = 1,
    Command = 2,
    System = 3,
    Warning = 4
}

// SignalR Hub Requests

public record JoinServerRequest(
    Guid ServerId,
    Guid OrganizationId,
    int HistoryLines = 100);

public record LeaveServerRequest(
    Guid ServerId);

public record ExecuteCommandRequest(
    Guid ServerId,
    string Command);

public record RequestHistoryRequest(
    Guid ServerId,
    int LineCount = 100,
    DateTime? Before = null);

// SignalR Hub Responses

public record ConsoleOutputDto(
    Guid ServerId,
    ConsoleOutputType OutputType,
    string Content,
    DateTime Timestamp,
    long SequenceNumber);

public record ConsoleLine(
    string Content,
    ConsoleOutputType OutputType,
    DateTime Timestamp,
    long SequenceNumber);

public record CommandResultDto(
    Guid ServerId,
    string Command,
    bool Success,
    string? Error,
    DateTime ExecutedAt);

public record ConsoleHistoryDto(
    Guid ServerId,
    IReadOnlyList<ConsoleLine> Lines,
    bool HasMore,
    DateTime? OldestTimestamp);

// MassTransit Commands (to agents)

/// <summary>
/// Command sent to an agent to execute a console command on a server.
/// </summary>
public record ExecuteServerCommand(
    Guid CommandId,
    Guid ServerId,
    Guid OrganizationId,
    string Command,
    Guid? UserId,
    string? Username,
    DateTime RequestedAt);

// MassTransit Events (from agents)

/// <summary>
/// Event received from an agent with console output.
/// </summary>
public record ConsoleOutputReceived(
    Guid ServerId,
    Guid OrganizationId,
    ConsoleOutputType OutputType,
    string Content,
    DateTime Timestamp,
    long SequenceNumber,
    Guid? SessionId);

/// <summary>
/// Event received from an agent with command execution result.
/// </summary>
public record CommandExecutionResult(
    Guid CommandId,
    Guid ServerId,
    bool Success,
    string? Error,
    int? ExitCode,
    DateTime CompletedAt);

/// <summary>
/// Event when a server's console session starts.
/// </summary>
public record ConsoleSessionStarted(
    Guid ServerId,
    Guid OrganizationId,
    Guid SessionId,
    DateTime StartedAt);

/// <summary>
/// Event when a server's console session ends.
/// </summary>
public record ConsoleSessionEnded(
    Guid ServerId,
    Guid SessionId,
    DateTime EndedAt,
    string? Reason);

// REST API Requests

public record SearchConsoleHistoryRequest(
    Guid ServerId,
    string? Query = null,
    DateTime? StartTime = null,
    DateTime? EndTime = null,
    ConsoleOutputType? OutputType = null,
    int Page = 1,
    int PageSize = 100);

// REST API Responses

public record ConsoleHistorySearchResult(
    IReadOnlyList<ConsoleLine> Lines,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasMore);
