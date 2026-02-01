using Dhadgar.Contracts;
using Dhadgar.Contracts.Servers;

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
    Task<ServiceResult<ServerDetail>> GetServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new server.
    /// </summary>
    Task<ServiceResult<ServerDetail>> CreateServerAsync(
        Guid organizationId,
        CreateServerRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a server's properties.
    /// </summary>
    Task<ServiceResult<ServerDetail>> UpdateServerAsync(
        Guid organizationId,
        Guid serverId,
        UpdateServerRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a server (soft delete).
    /// </summary>
    Task<ServiceResult<bool>> DeleteServerAsync(
        Guid organizationId,
        Guid serverId,
        CancellationToken ct = default);
}
