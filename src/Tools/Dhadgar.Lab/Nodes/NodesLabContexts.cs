using Dhadgar.Lab.Stack;
using Dhadgar.Nodes.Data;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Lab.Nodes;

/// <summary>
/// Builds the per-call <see cref="NodesDbContext"/> factory the lab adapter uses. The connection string is resolved from the
/// vault on each call, as the lab's owner role on the direct host port (the daemon's own writes never pass a fault proxy).
/// </summary>
public static class NodesLabContexts
{
    /// <summary>
    /// Returns a factory that opens a fresh <see cref="NodesDbContext"/> on <c>meridian_lab_nodes</c>. A failure to resolve
    /// the connection throws; <see cref="NodesLabAdapter"/> turns it into a <c>[Lab.Failed]</c> result.
    /// </summary>
    public static Func<NodesDbContext> ForOwner(LabConnectionStrings connections)
    {
        ArgumentNullException.ThrowIfNull(connections);

        return () =>
        {
            var connection = connections.OwnerDirect("nodes");
            if (connection.IsFailure)
            {
                throw new InvalidOperationException(connection.Error);
            }

            var options = new DbContextOptionsBuilder<NodesDbContext>()
                .UseNpgsql(connection.Value)
                .Options;
            return new NodesDbContext(options);
        };
    }
}
