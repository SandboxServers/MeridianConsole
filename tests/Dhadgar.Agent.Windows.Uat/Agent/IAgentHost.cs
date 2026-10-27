using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Agent;

/// <summary>
/// The seam that lets the agent legs run in every mode (P22): the agent as a console child (process), as the
/// DhadgarAgent LocalSystem service (service) or as the installed product (msi, P23). The legs never start or stop
/// the agent any other way.
/// </summary>
internal interface IAgentHost
{
    /// <summary>"process", "service" or "msi".</summary>
    string Mode { get; }

    /// <summary>Writes the manifest entries the agent may cause, before the first start. Idempotent.</summary>
    Task<Result> PrepareAsync(CancellationToken ct);

    /// <summary>Starts the agent (process) or the service (service mode).</summary>
    Task<Result> StartAsync(CancellationToken ct);

    /// <summary>Stops the agent. A stop of an agent that is not running is success.</summary>
    Task<Result> StopAsync(CancellationToken ct);

    /// <summary>
    /// <c>agent.log</c> in process mode (the legs read the service-recovery warning only when this is not null);
    /// null in service mode, where the agent's log is the event log export (U9).
    /// </summary>
    string? ConsoleLogPath { get; }

    /// <summary>
    /// True when <see cref="ConsoleLogPath"/> holds the service-recovery warning. Called only when
    /// <see cref="ConsoleLogPath"/> is not null; a host without a console log returns success with false.
    /// </summary>
    Result<bool> SawRecoveryWarning();
}
