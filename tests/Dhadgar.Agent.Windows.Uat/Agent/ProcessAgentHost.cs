using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Agent;

/// <summary>Process mode (P20): the agent runs as a console child of the driver. No behaviour change from P20.</summary>
internal sealed class ProcessAgentHost : IAgentHost
{
    private readonly RunPaths _paths;
    private readonly AgentProcess _agent;

    /// <summary>Wraps the process for one run.</summary>
    public ProcessAgentHost(RunPaths paths, AgentProcess agent)
    {
        _paths = paths;
        _agent = agent;
    }

    /// <inheritdoc />
    public string Mode => "process";

    /// <inheritdoc />
    public string? ConsoleLogPath => Path.Combine(_paths.Logs, "agent.log");

    /// <inheritdoc />
    public Task<Result> PrepareAsync(CancellationToken ct) => Task.FromResult(_agent.PrepareFirstStart());

    /// <inheritdoc />
    public Task<Result> StartAsync(CancellationToken ct) => Task.FromResult(_agent.Start());

    /// <inheritdoc />
    public Task<Result> StopAsync(CancellationToken ct) => _agent.StopAsync();

    /// <inheritdoc />
    public Result<bool> SawRecoveryWarning() => _agent.SawRecoveryWarning();
}
