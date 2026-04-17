using Dhadgar.Contracts.Console;
using Dhadgar.Shared.Results;

namespace Dhadgar.Console.Services;

public interface ICommandDispatcher
{
    /// <summary>
    /// Validates and dispatches a command to a server.
    /// </summary>
    Task<Result<CommandResultDto>> DispatchCommandAsync(
        Guid serverId,
        Guid organizationId,
        string command,
        Guid? userId,
        string? username,
        string? connectionId,
        string? clientIpHash,
        CancellationToken ct = default);
}
