using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Service;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Agent;

/// <summary>
/// Service mode (P22): the agent runs as the DhadgarAgent LocalSystem service, created by U10. The first-start
/// entries are the same ones <see cref="ProcessAgentHost"/> writes, so cleanup covers both modes alike.
/// </summary>
internal sealed class ServiceAgentHost : IAgentHost
{
    private readonly IServiceControl _service;
    private readonly CleanupManifest _manifest;
    private bool _entriesWritten;

    /// <summary>Wraps the service control for one run.</summary>
    public ServiceAgentHost(IServiceControl service, CleanupManifest manifest)
    {
        _service = service;
        _manifest = manifest;
    }

    /// <inheritdoc />
    public string Mode => "service";

    /// <summary>The service has no console log; the agent's log reaches the run through the event log export (U9).</summary>
    public string? ConsoleLogPath => null;

    /// <inheritdoc />
    public Task<Result> PrepareAsync(CancellationToken ct)
    {
        if (_entriesWritten)
        {
            return Task.FromResult(Result.Success());
        }

        var entries = AgentProcess.AppendFirstStartEntries(_manifest);
        if (entries.IsSuccess)
        {
            _entriesWritten = true;
        }

        return Task.FromResult(entries);
    }

    /// <inheritdoc />
    public Task<Result> StartAsync(CancellationToken ct) => _service.StartAsync(ct);

    /// <inheritdoc />
    public Task<Result> StopAsync(CancellationToken ct) => _service.StopAsync(ct);

    /// <inheritdoc />
    public Result<bool> SawRecoveryWarning() => Result<bool>.Success(false);
}
