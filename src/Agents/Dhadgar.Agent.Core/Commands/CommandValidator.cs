using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Core.Commands;

/// <summary>
/// Validates incoming commands before execution.
/// </summary>
public sealed class CommandValidator : ICommandValidator
{
    private readonly AgentOptions _options;
    private readonly ILogger<CommandValidator> _logger;

    public CommandValidator(
        IOptions<AgentOptions> options,
        ILogger<CommandValidator> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Result<CommandEnvelope> Validate(CommandEnvelope? envelope)
    {
        if (envelope is null)
        {
            return Fail("Envelope cannot be null", "NullEnvelope");
        }

        // Validate command ID
        if (envelope.CommandId == Guid.Empty)
        {
            return Fail("CommandId is required", "MissingCommandId");
        }

        // Validate command type
        if (string.IsNullOrWhiteSpace(envelope.CommandType))
        {
            return Fail("CommandType is required", "MissingCommandType");
        }

        if (envelope.CommandType.Length > CommandEnvelope.MaxCommandTypeLength)
        {
            return Fail($"CommandType exceeds maximum length of {CommandEnvelope.MaxCommandTypeLength}", "CommandTypeTooLong");
        }

        // Validate node ID matches
        if (_options.NodeId.HasValue && envelope.NodeId != _options.NodeId.Value)
        {
            _logger.LogWarning(
                "Command {CommandId} has mismatched NodeId. Expected: {Expected}, Got: {Got}",
                envelope.CommandId,
                _options.NodeId.Value,
                envelope.NodeId);
            return Fail("Command is not intended for this node", "NodeIdMismatch");
        }

        // Validate organization ID matches (multi-tenant isolation)
        if (_options.OrganizationId.HasValue && envelope.OrganizationId != _options.OrganizationId.Value)
        {
            _logger.LogWarning(
                "Command {CommandId} has mismatched OrganizationId. Expected: {Expected}, Got: {Got}",
                envelope.CommandId,
                _options.OrganizationId.Value,
                envelope.OrganizationId);
            return Fail("Command is not for this organization", "OrganizationIdMismatch");
        }

        // Validate expiration
        if (envelope.ExpiresAt.HasValue && envelope.ExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Command {CommandId} has expired", envelope.CommandId);
            return Fail("Command has expired", "CommandExpired");
        }

        // Validate timestamp age (replay prevention)
        var maxAge = TimeSpan.FromSeconds(_options.Security.CommandMaxAgeSeconds);
        var commandAge = DateTimeOffset.UtcNow - envelope.IssuedAt;

        if (commandAge > maxAge)
        {
            _logger.LogWarning(
                "Command {CommandId} is too old. Age: {Age}, Max: {MaxAge}",
                envelope.CommandId,
                commandAge,
                maxAge);
            return Fail($"Command timestamp is too old ({commandAge.TotalSeconds:F0}s)", "CommandTooOld");
        }

        if (commandAge < TimeSpan.Zero)
        {
            _logger.LogWarning(
                "Command {CommandId} has future timestamp: {IssuedAt}",
                envelope.CommandId,
                envelope.IssuedAt);
            return Fail("Command has future timestamp", "FutureTimestamp");
        }

        // Validate signature if required
        if (_options.Security.RequireSignedCommands)
        {
            if (string.IsNullOrWhiteSpace(envelope.Signature))
            {
                return Fail("Command signature is required", "MissingSignature");
            }

            // SECURITY: Signature verification not yet implemented.
            // Reject all commands when signing is required to prevent
            // false sense of security from presence-only checks.
            _logger.LogError(
                "Command {CommandId} rejected: signature verification not implemented. " +
                "Disable RequireSignedCommands until control plane implements signing.",
                envelope.CommandId);
            return Fail("Signature verification not yet implemented", "SignatureVerificationUnavailable");
        }

        // Validate payload exists and size (use byte count for accurate size check)
        if (string.IsNullOrWhiteSpace(envelope.PayloadJson))
        {
            return Fail("Command payload is required", "MissingPayload");
        }

        var payloadByteCount = System.Text.Encoding.UTF8.GetByteCount(envelope.PayloadJson);
        if (payloadByteCount > CommandEnvelope.MaxPayloadLength)
        {
            return Fail($"Payload exceeds maximum size of {CommandEnvelope.MaxPayloadLength} bytes", "PayloadTooLarge");
        }

        // Validate signature size if present
        if (envelope.Signature is not null && envelope.Signature.Length > CommandEnvelope.MaxSignatureLength)
        {
            return Fail($"Signature exceeds maximum length of {CommandEnvelope.MaxSignatureLength}", "SignatureTooLong");
        }

        // Validate correlation ID size if present
        if (envelope.CorrelationId is not null && envelope.CorrelationId.Length > CommandEnvelope.MaxCorrelationIdLength)
        {
            return Fail($"CorrelationId exceeds maximum length of {CommandEnvelope.MaxCorrelationIdLength}", "CorrelationIdTooLong");
        }

        return Result<CommandEnvelope>.Success(envelope);
    }

    private static Result<CommandEnvelope> Fail(string message, string code)
    {
        return Result<CommandEnvelope>.Failure($"[{code}] {message}");
    }
}
