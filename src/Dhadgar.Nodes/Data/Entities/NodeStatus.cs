namespace Dhadgar.Nodes.Data.Entities;

/// <summary>
/// Node lifecycle states following a state machine pattern.
/// </summary>
public enum NodeStatus
{
    /// <summary>Initial state during agent enrollment process.</summary>
    Enrolling = 0,

    /// <summary>Node is healthy and receiving heartbeats.</summary>
    Online = 1,

    /// <summary>Node is online but reporting health issues.</summary>
    Degraded = 2,

    /// <summary>No heartbeat received within threshold.</summary>
    Offline = 3,

    /// <summary>Intentionally taken offline for maintenance.</summary>
    Maintenance = 4,

    /// <summary>Permanently retired (soft-deleted).</summary>
    Decommissioned = 5
}
