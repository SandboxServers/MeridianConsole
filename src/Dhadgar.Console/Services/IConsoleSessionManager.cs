namespace Dhadgar.Console.Services;

public interface IConsoleSessionManager
{
    /// <summary>
    /// Adds a connection to a server's console session.
    /// </summary>
    Task AddConnectionAsync(string connectionId, Guid serverId, Guid organizationId, Guid? userId, CancellationToken ct = default);

    /// <summary>
    /// Removes a connection from a server's console session.
    /// </summary>
    Task RemoveConnectionAsync(string connectionId, Guid serverId, CancellationToken ct = default);

    /// <summary>
    /// Removes all connections for a connection ID (on disconnect).
    /// </summary>
    Task RemoveAllConnectionsAsync(string connectionId, CancellationToken ct = default);

    /// <summary>
    /// Gets all connection IDs for a server.
    /// </summary>
    Task<IReadOnlyList<string>> GetServerConnectionsAsync(Guid serverId, CancellationToken ct = default);

    /// <summary>
    /// Checks if a connection is connected to a server.
    /// </summary>
    Task<bool> IsConnectedToServerAsync(string connectionId, Guid serverId, CancellationToken ct = default);

    /// <summary>
    /// Gets all servers a connection is connected to.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetConnectionServersAsync(string connectionId, CancellationToken ct = default);
}
