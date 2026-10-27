using System.Net;
using System.Net.Sockets;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Probe;

namespace Dhadgar.Agent.Windows.Uat.Preflight;

/// <summary>The modes a check can be required for. <see cref="Preflight"/> is the report-only verb (evaluated as Run).</summary>
internal enum PreflightMode
{
    /// <summary>The <c>preflight</c> verb: report, judged against the Run rules.</summary>
    Preflight,

    /// <summary>The <c>stack-only</c> verb.</summary>
    StackOnly,

    /// <summary>The <c>run</c> verb.</summary>
    Run,
}

/// <summary>One check. Required flags say which modes need it to pass.</summary>
internal sealed record PreflightCheck(
    string Name,
    bool Passed,
    string Detail,
    bool RequiredForStackOnly,
    bool RequiredForRun)
{
    /// <summary>True when this check must pass for <paramref name="mode"/>.</summary>
    public bool RequiredFor(PreflightMode mode) => mode switch
    {
        PreflightMode.StackOnly => RequiredForStackOnly,
        PreflightMode.Run => RequiredForRun,
        _ => RequiredForRun,
    };
}

/// <summary>The outcome of every check.</summary>
internal sealed record PreflightReport(IReadOnlyList<PreflightCheck> Checks)
{
    /// <summary>True when every check required for <paramref name="mode"/> passes.</summary>
    public bool PassesFor(PreflightMode mode) =>
        Checks.Where(c => c.RequiredFor(mode)).All(c => c.Passed);
}

/// <summary>
/// The preflight checks (D-S225). Changes nothing; named <c>PreflightChecks</c> so it does not share a name with its namespace. Checks required from stack-only on use Docker,
/// dev certificates, port 5672 and the Root snapshot; checks required for run add elevation,
/// an agent run needs a clean tree (D-S239), and an empty footprint.
/// </summary>
internal sealed class PreflightChecks
{
    private const int MinimumWindowsBuild = 10240;
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(60);
    private readonly IMachineProbe _probe;
    private readonly IMachineActions _actions;
    private readonly string _runDir;
    private readonly string? _runMode;
    private readonly bool _launchedByAgent;
    private readonly bool? _agentTreeClean;

    /// <summary>Creates the checks for one run folder. <paramref name="agentTreeClean"/> is null when the tree check failed or was not made.</summary>
    public PreflightChecks(
        IMachineProbe probe, IMachineActions actions, string runDir, string? runMode, bool launchedByAgent, bool? agentTreeClean)
    {
        _probe = probe;
        _actions = actions;
        _runDir = runDir;
        _runMode = runMode;
        _launchedByAgent = launchedByAgent;
        _agentTreeClean = agentTreeClean;
    }

    /// <summary>
    /// The agent-run tree rule (D-S239). Passes for user runs and for agent runs on a clean tree; fails for an agent run
    /// on a dirty tree or when the tree check failed (null).
    /// </summary>
    internal static PreflightCheck AgentRunTree(bool launchedByAgent, bool? treeClean)
    {
        if (!launchedByAgent)
        {
            return new PreflightCheck("agentRunTree", true, "not an agent run", false, true);
        }

        if (treeClean is null)
        {
            return new PreflightCheck("agentRunTree", false, "tree check failed: agent run refused", false, true);
        }

        return treeClean == true
            ? new PreflightCheck("agentRunTree", true, "agent run on a clean tree (D-S239)", false, true)
            : new PreflightCheck("agentRunTree", false, "agent run needs a clean tree (D-S239)", false, true);
    }

    /// <summary>Runs every check. <paramref name="mode"/> is informational here; the report carries each check's requirement.</summary>
    public async Task<PreflightReport> RunAsync(PreflightMode mode, CancellationToken ct)
    {
        var checks = new List<PreflightCheck>
        {
            new("windows", OperatingSystem.IsWindows(), "Windows host", true, true),
            new("buildNumber", Environment.OSVersion.Version.Build >= MinimumWindowsBuild,
                $"build {Environment.OSVersion.Version.Build} (minimum {MinimumWindowsBuild})", true, true),
            new("elevated", _actions.IsElevated(), "elevated PowerShell (required for run)", false, true),
            AgentRunTree(_launchedByAgent, _agentTreeClean),
            await DockerAsync(ct),
            await DevCertsAsync(ct),
            Port5672(),
            FootprintCheck(),
            new("runPathNoSpaces", !_runDir.Contains(' '), "run folder path has no spaces (required for service mode)", false,
                _runMode == "service"),
            RootReadable(),
        };

        return new PreflightReport(checks);
    }

    private async Task<PreflightCheck> DockerAsync(CancellationToken ct)
    {
        var result = await CommandRunner.RunAsync("docker", ["info", "--format", "{{.ServerVersion}}"], ToolTimeout, null, null, ct);
        var ok = result.IsSuccess && result.Value.ExitCode == 0;
        return new PreflightCheck("docker", ok, ok ? "Docker server reachable" : "Docker is not running", true, true);
    }

    private async Task<PreflightCheck> DevCertsAsync(CancellationToken ct)
    {
        var result = await CommandRunner.RunAsync("dotnet", ["dev-certs", "https", "--check", "--trust"], ToolTimeout, null, null, ct);
        var ok = result.IsSuccess && result.Value.ExitCode == 0;
        return new PreflightCheck("devCerts", ok, ok ? "dev HTTPS certificate trusted" : "run 'dotnet dev-certs https --trust'", true, true);
    }

    private static PreflightCheck Port5672()
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, 5672);
            listener.Start();
            listener.Stop();
            return new PreflightCheck("port5672", true, "port 5672 free", true, true);
        }
        catch (SocketException)
        {
            return new PreflightCheck("port5672", false, "port 5672 is busy: stop a dev-compose RabbitMQ", true, true);
        }
    }

    private PreflightCheck FootprintCheck()
    {
        var footprint = _probe.Scan();
        var detail = footprint.IsEmpty ? "no agent footprint" : "footprint: " + string.Join(", ", footprint.Items);
        return new PreflightCheck("footprintEmpty", footprint.IsEmpty, detail, false, true);
    }

    private static PreflightCheck RootReadable()
    {
        var snapshot = RootSnapshot.Take();
        return new PreflightCheck("rootReadable", snapshot.IsSuccess,
            snapshot.IsSuccess ? "LocalMachine\\Root readable" : snapshot.Error, true, true);
    }
}
