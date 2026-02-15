using Dhadgar.Contracts;
using Dhadgar.Contracts.Servers;
using Dhadgar.Shared.Results;

namespace Dhadgar.Servers.Services;

public interface IServerService
{
    /// <summary>
    /// Gets a paginated list of servers with optional filtering and sorting.
    /// </summary>
    Task<FilteredPagedResponse<ServerListItem>> GetServersAsync(
        Guid organizationId,
        ServerListQuery query,
        CancellationToken ct = default);

    /// <summary>
    /// Gets detailed information about a specific server.
    /// </summary>
    Task<Result<ServerDetail>> GetServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new server.
    /// </summary>
    Task<Result<ServerDetail>> CreateServerAsync(
        Guid organizationId,
        CreateServerRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a server's properties.
    /// </summary>
    Task<Result<ServerDetail>> UpdateServerAsync(
        Guid organizationId,
        Guid serverId,
        UpdateServerRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a server (soft delete).
    /// </summary>
    Task<Result<bool>> DeleteServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default);
}
