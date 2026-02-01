using Dhadgar.Console.Data;
using Dhadgar.Console.Data.Entities;
using Dhadgar.Contracts.Console;
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
    private readonly List<Regex> _dangerousPatterns;

    public CommandDispatcher(
        ConsoleDbContext db,
        IPublishEndpoint publishEndpoint,
        IOptions<ConsoleOptions> options,
        ILogger<CommandDispatcher> logger)
    {
        _db = db;
        _publishEndpoint = publishEndpoint;
        _options = options.Value;
        _logger = logger;

        // Compile dangerous command patterns
        _dangerousPatterns = _options.DangerousCommandPatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToList();
    }

    public async Task<CommandResultDto> DispatchCommandAsync(
        Guid serverId,
        Guid organizationId,
        string command,
        Guid? userId,
        string? username,
        string? connectionId,
        string? clientIpHash,
        CancellationToken ct = default)
    {
        // Validate command
        var validation = ValidateCommand(command);
        if (!validation.IsValid)
        {
            // Log the blocked command
            await LogCommandAsync(serverId, organizationId, userId, username, command,
                wasAllowed: false, validation.BlockReason, CommandResultStatus.Blocked, connectionId, clientIpHash, ct);

            _logger.LogWarning("Blocked dangerous command on server {ServerId}: {Command} - {Reason}",
                serverId, command, validation.BlockReason);

            return new CommandResultDto(serverId, command, false, validation.BlockReason, DateTime.UtcNow);
        }

        // Log the command
        await LogCommandAsync(serverId, organizationId, userId, username, command,
            wasAllowed: true, null, CommandResultStatus.Success, connectionId, clientIpHash, ct);

        // Dispatch command to agent via MassTransit
        var commandId = Guid.NewGuid();
        await _publishEndpoint.Publish(new ExecuteServerCommand(
            commandId,
            serverId,
            organizationId,
            command,
            userId,
            username,
            DateTime.UtcNow), ct);

        _logger.LogInformation("Dispatched command {CommandId} to server {ServerId}: {Command}",
            commandId, serverId, command);

        return new CommandResultDto(serverId, command, true, null, DateTime.UtcNow);
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

        // Check for dangerous patterns
        foreach (var pattern in _dangerousPatterns)
        {
            if (pattern.IsMatch(command))
            {
                return (false, "Command contains potentially dangerous pattern");
            }
        }

        return (true, null);
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
        var log = new CommandAuditLog
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
            ClientIpHash = clientIpHash
        };

        _db.CommandAuditLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }
}
