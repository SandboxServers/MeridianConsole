namespace Dhadgar.Servers.Data.Entities;

/// <summary>
/// Represents the lifecycle status of a game server.
/// </summary>
public enum ServerStatus
{
    /// <summary>Server record created but not yet provisioned.</summary>
    Created = 0,

    /// <summary>Server is being provisioned on a node.</summary>
    Provisioning = 1,

    /// <summary>Game files are being installed/updated.</summary>
    Installing = 2,

    /// <summary>Server is ready to start (files installed, not running).</summary>
    Ready = 3,

    /// <summary>Server is in the process of starting.</summary>
    Starting = 4,

    /// <summary>Server is running and accepting connections.</summary>
    Running = 5,

    /// <summary>Server is in the process of stopping.</summary>
    Stopping = 6,

    /// <summary>Server is stopped (graceful shutdown).</summary>
    Stopped = 7,

    /// <summary>Server crashed unexpectedly.</summary>
    Crashed = 8,

    /// <summary>Server is in an error state requiring intervention.</summary>
    Error = 9,

    /// <summary>Server is suspended (billing or administrative).</summary>
    Suspended = 10,

    /// <summary>Server is under maintenance.</summary>
    Maintenance = 11,

    /// <summary>Server is soft-deleted.</summary>
    Deleted = 12
}
