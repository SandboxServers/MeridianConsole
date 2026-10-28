using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Dhadgar.Nodes.Hubs;

/// <summary>
/// In-memory <see cref="IAgentConnectionRegistry"/>. Single-instance for the MVP: connections held by another
/// Nodes instance are not visible here. Scale-out needs a Redis backplane (ADR-0008).
/// </summary>
public sealed class AgentConnectionRegistry : IAgentConnectionRegistry
{
    private readonly ConcurrentDictionary<Guid, ImmutableHashSet<string>> _connections = new();

    // Writers take this lock so that removing an emptied key cannot race a concurrent Add for the same node.
    // Reads stay lock-free through the ConcurrentDictionary.
    private readonly object _writeGate = new();

    /// <inheritdoc />
    public void Add(Guid nodeId, string connectionId)
    {
        lock (_writeGate)
        {
            _connections.AddOrUpdate(
                nodeId,
                _ => ImmutableHashSet.Create(connectionId),
                (_, existing) => existing.Add(connectionId));
        }
    }

    /// <inheritdoc />
    public void Remove(Guid nodeId, string connectionId)
    {
        lock (_writeGate)
        {
            if (!_connections.TryGetValue(nodeId, out var existing))
            {
                return;
            }

            var remaining = existing.Remove(connectionId);
            if (remaining.IsEmpty)
            {
                _connections.TryRemove(nodeId, out _);
            }
            else
            {
                _connections.AddOrUpdate(nodeId, remaining, (_, _) => remaining);
            }
        }
    }

    /// <inheritdoc />
    public bool IsConnected(Guid nodeId) =>
        _connections.TryGetValue(nodeId, out var connections) && !connections.IsEmpty;

    /// <inheritdoc />
    public int ConnectedNodeCount => _connections.Count;
}
