namespace Dhadgar.Servers.Data.Entities;

/// <summary>
/// Represents the power state of a game server process.
/// </summary>
public enum ServerPowerState
{
    /// <summary>Server process is not running.</summary>
    Off = 0,

    /// <summary>Server process is starting up.</summary>
    Starting = 1,

    /// <summary>Server process is running.</summary>
    On = 2,

    /// <summary>Server process is shutting down.</summary>
    Stopping = 3,

    /// <summary>Server process crashed.</summary>
    Crashed = 4
}
