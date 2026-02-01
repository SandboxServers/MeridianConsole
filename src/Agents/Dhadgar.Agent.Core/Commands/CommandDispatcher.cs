using System.Collections.Concurrent;
using System.Text.Json;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Core.Commands;

/// <summary>
/// Routes commands to appropriate handlers.
/// </summary>
public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly ConcurrentDictionary<string, ICommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ICommandValidator _validator;
    private readonly AgentOptions _options;
    private readonly AgentMeter _meter;
    private readonly AgentActivitySource _activitySource;
    private readonly ILogger<CommandDispatcher> _logger;

    public CommandDispatcher(
        ICommandValidator validator,
        IOptions<AgentOptions> options,
        AgentMeter meter,
        AgentActivitySource activitySource,
        ILogger<CommandDispatcher> logger)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _meter = meter ?? throw new ArgumentNullException(nameof(meter));
        _activitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RegisterHandler(ICommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!_handlers.TryAdd(handler.CommandType, handler))
        {
            throw new InvalidOperationException(
                $"A handler for command type '{handler.CommandType}' is already registered");
        }

        _logger.LogDebug("Registered handler for command type: {CommandType}", handler.CommandType);
    }

    public bool HasHandler(string commandType)
    {
        return _handlers.ContainsKey(commandType);
    }

    public async Task<CommandResult> DispatchAsync(
        CommandEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var startedAt = DateTimeOffset.UtcNow;

        using var activity = _activitySource.StartCommandExecution(
            envelope.CommandId,
            envelope.CommandType,
            envelope.CorrelationId);

        _logger.LogInformation(
            "Dispatching command {CommandType} with ID {CommandId}",
            envelope.CommandType,
            envelope.CommandId);

        // Validate the command
        var validationResult = _validator.Validate(envelope);
        if (!validationResult.IsSuccess)
        {
            var error = validationResult.Error;
            _logger.LogWarning(
                "Command validation failed for {CommandId}: {Error}",
                envelope.CommandId,
                error);

            _meter.RecordCommandExecuted(envelope.CommandType, success: false);
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, error);

            return CommandResult.Rejected(
                envelope.CommandId,
                _options.NodeId ?? Guid.Empty,
                error,
                errorCode: null,
                envelope.CorrelationId);
        }

        // Find handler
        if (!_handlers.TryGetValue(envelope.CommandType, out var handler))
        {
            _logger.LogWarning(
                "No handler registered for command type: {CommandType}",
                envelope.CommandType);

            _meter.RecordCommandExecuted(envelope.CommandType, success: false);
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "Unknown command type");

            return CommandResult.Rejected(
                envelope.CommandId,
                _options.NodeId ?? Guid.Empty,
                $"Unknown command type: {envelope.CommandType}",
                "UnknownCommandType",
                envelope.CorrelationId);
        }

        // Execute command
        try
        {
            var result = await handler.ExecuteAsync(envelope, cancellationToken);

            var success = result.Status == CommandResultStatus.Succeeded;
            _meter.RecordCommandExecuted(envelope.CommandType, success);

            if (!success)
            {
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, result.ErrorMessage);
            }

            _logger.LogInformation(
                "Command {CommandId} completed with status {Status}",
                envelope.CommandId,
                result.Status);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Command {CommandId} was cancelled", envelope.CommandId);
            _meter.RecordCommandExecuted(envelope.CommandType, success: false);
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "Cancelled");

            return new CommandResult
            {
                CommandId = envelope.CommandId,
                NodeId = _options.NodeId ?? Guid.Empty,
                Status = CommandResultStatus.Cancelled,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                ErrorMessage = "Command was cancelled",
                CorrelationId = envelope.CorrelationId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command {CommandId} failed with exception", envelope.CommandId);
            _meter.RecordCommandExecuted(envelope.CommandType, success: false);
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);

            // SECURITY: Return generic error message to caller, keep details in logs
            return CommandResult.Failure(
                envelope.CommandId,
                _options.NodeId ?? Guid.Empty,
                startedAt,
                "Internal execution error",
                "ExecutionException",
                envelope.CorrelationId);
        }
    }
}

/// <summary>
/// Base class for strongly-typed command handlers.
/// </summary>
/// <typeparam name="TPayload">Payload type.</typeparam>
public abstract class CommandHandlerBase<TPayload> : ICommandHandler<TPayload>
    where TPayload : class
{
    private readonly ILogger _logger;

    /// <summary>
    /// JSON deserialization options with depth limit to prevent DoS via deeply nested payloads.
    /// </summary>
    private static readonly JsonSerializerOptions DeserializerOptions = new()
    {
        MaxDepth = 64
    };

    protected CommandHandlerBase(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public abstract string CommandType { get; }

    public async Task<CommandResult> ExecuteAsync(
        CommandEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        // Deserialize payload with depth limit to prevent DoS
        TPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TPayload>(envelope.PayloadJson, DeserializerOptions);
        }
        catch (JsonException ex)
        {
            var isDepthError = ex.Message.Contains("depth", StringComparison.OrdinalIgnoreCase);
            var errorCode = isDepthError ? "PayloadTooDeep" : "InvalidPayload";
            _logger.LogError(ex, "Failed to deserialize payload for command {CommandId} (depth limit exceeded: {IsDepthError})",
                envelope.CommandId, isDepthError);
            // SECURITY: Return generic error message to caller, keep details in logs
            return CommandResult.Rejected(
                envelope.CommandId,
                envelope.NodeId,
                isDepthError ? "Payload exceeds maximum nesting depth" : "Invalid payload format",
                errorCode,
                envelope.CorrelationId);
        }

        if (payload is null)
        {
            _logger.LogWarning("Payload deserialized to null for command {CommandId}", envelope.CommandId);
            return CommandResult.Rejected(
                envelope.CommandId,
                envelope.NodeId,
                "Payload deserialized to null",
                "NullPayload",
                envelope.CorrelationId);
        }

        return await ExecuteAsync(envelope, payload, cancellationToken);
    }

    public abstract Task<CommandResult> ExecuteAsync(
        CommandEnvelope envelope,
        TPayload payload,
        CancellationToken cancellationToken = default);
}
