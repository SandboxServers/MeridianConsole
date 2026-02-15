using Dhadgar.Console.Data;
using Dhadgar.Console.Data.Entities;
using Dhadgar.Contracts.Console;
using Dhadgar.Shared.Results;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Dhadgar.Console.Services;

public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly ConsoleDbContext _db;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ConsoleOptions _options;
    private readonly ILogger<CommandDispatcher> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly List<Regex> _allowedPatterns;

    public CommandDispatcher(
        ConsoleDbContext db,
        IPublishEndpoint publishEndpoint,
        IOptions<ConsoleOptions> options,
        ILogger<CommandDispatcher> logger,
        TimeProvider timeProvider)
    {
        _db = db;
        _publishEndpoint = publishEndpoint;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;

        // Compile allowed command patterns with NonBacktracking to prevent ReDoS
        var regexTimeout = TimeSpan.FromMilliseconds(_options.CommandRegexTimeoutMs);
        _allowedPatterns = _options.AllowedCommandPatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, regexTimeout))
            .ToList();
    }

    public async Task<Result<CommandResultDto>> DispatchCommandAsync(
        Guid serverId,
        Guid organizationId,
        string command,
        Guid? userId,
        string? username,
        string? connectionId,
        string? clientIpHash,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Validate command
        var validation = ValidateCommand(command);
        if (!validation.IsValid)
        {
            // Log the blocked command
            await LogCommandAsync(serverId, organizationId, userId, username, command,
                wasAllowed: false, validation.BlockReason, CommandResultStatus.Blocked, connectionId, clientIpHash, ct);

            _logger.LogWarning("Blocked dangerous command on server {ServerId}: {Command} - {Reason}",
                serverId, command, validation.BlockReason);

            return Result<CommandResultDto>.Failure(validation.BlockReason ?? "Command blocked");
        }

        // Create audit log entry (not yet saved)
        var log = CreateAuditLog(serverId, organizationId, userId, username, command,
            wasAllowed: true, null, CommandResultStatus.Success, connectionId, clientIpHash, now);
        _db.CommandAuditLogs.Add(log);

        // Dispatch command to agent via MassTransit — publish before save
        // so the outbox captures it in the same transaction
        var commandId = Guid.NewGuid();
        await _publishEndpoint.Publish(new ExecuteServerCommand(
            commandId,
            serverId,
            organizationId,
            command,
            userId,
            username,
            now), ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Dispatched command {CommandId} to server {ServerId}: {Command}",
            commandId, serverId, command);

        return Result<CommandResultDto>.Success(new CommandResultDto(serverId, command, true, null, now));
    }

    private (bool IsValid, string? BlockReason) ValidateCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return (false, "Command cannot be empty");
        }

        if (command.Length > _options.MaxCommandLength)
        {
            return (false, $"Command exceeds maximum length of {_options.MaxCommandLength}");
        }

        // Normalize command before pattern matching to prevent bypass via leading whitespace
        var normalizedCommand = command.TrimStart();

        // Allowlist: command must match at least one allowed pattern
        var isAllowed = false;
        foreach (var pattern in _allowedPatterns)
        {
            if (pattern.IsMatch(normalizedCommand))
            {
                isAllowed = true;
                break;
            }
        }

        if (!isAllowed)
        {
            return (false, "Command does not match any allowed pattern");
        }

        return (true, null);
    }

    private static CommandAuditLog CreateAuditLog(
        Guid serverId,
        Guid organizationId,
        Guid? userId,
        string? username,
        string command,
        bool wasAllowed,
        string? blockReason,
        CommandResultStatus resultStatus,
        string? connectionId,
        string? clientIpHash,
        DateTime executedAt)
    {
        return new CommandAuditLog
        {
            ServerId = serverId,
            OrganizationId = organizationId,
            UserId = userId,
            Username = username,
            Command = command.Length > 2000 ? command[..2000] : command,
            WasAllowed = wasAllowed,
            BlockReason = blockReason,
            ResultStatus = resultStatus,
            ConnectionId = connectionId,
            ClientIpHash = clientIpHash,
            ExecutedAt = executedAt
        };
    }

    private async Task LogCommandAsync(
        Guid serverId,
        Guid organizationId,
        Guid? userId,
        string? username,
        string command,
        bool wasAllowed,
        string? blockReason,
        CommandResultStatus resultStatus,
        string? connectionId,
        string? clientIpHash,
        CancellationToken ct)
    {
        try
        {
            var log = CreateAuditLog(serverId, organizationId, userId, username, command,
                wasAllowed, blockReason, resultStatus, connectionId, clientIpHash,
                _timeProvider.GetUtcNow().UtcDateTime);
            _db.CommandAuditLogs.Add(log);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist blocked-command audit log for server {ServerId}", serverId);
        }
    }
}
