using System.Text.RegularExpressions;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Service;

/// <summary>The parts of <c>sc.exe qc</c> the UAT checks: start type, account and binary path.</summary>
internal sealed record ServiceConfig(string StartType, string Account, string BinaryPath);

/// <summary>
/// Creates, queries, starts and stops the <c>DhadgarAgent</c> service for service mode (D-S222 (A)). Every call is a
/// <c>sc.exe</c> invocation with one <c>ArgumentList</c> item per token (R-ARGS).
/// </summary>
internal interface IServiceControl
{
    /// <summary>Creates the service as LocalSystem, demand start, for an executable under the run's publish folder.</summary>
    Task<Result> Create(string exePath);

    /// <summary>Returns the <c>STATE</c> word (for example <c>RUNNING</c> or <c>STOPPED</c>).</summary>
    Task<Result<string>> Query();

    /// <summary>Returns the start type, account and binary path from <c>sc.exe qc</c>.</summary>
    Task<Result<ServiceConfig>> QueryConfig();

    /// <summary>Starts the service and waits up to 30 seconds for RUNNING.</summary>
    Task<Result> StartAsync(CancellationToken ct);

    /// <summary>Stops the service and waits up to 30 seconds for STOPPED. A stopped or missing service is success.</summary>
    Task<Result> StopAsync(CancellationToken ct);
}

/// <summary>Runs one tool with arguments; the seam that lets tests replace <see cref="CommandRunner"/>.</summary>
internal delegate Task<Result<CommandOutput>> ToolRunner(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout);

/// <summary>The <c>sc.exe</c> implementation of <see cref="IServiceControl"/>.</summary>
internal sealed class ServiceControl : IServiceControl
{
    private const string StateRunning = "RUNNING";
    private const string StateStopped = "STOPPED";
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceNotActive = 1062;
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);
    private static readonly Regex StatePattern = new(@"STATE\s*:\s*\d+\s+(?<state>[A-Z_]+)", RegexOptions.CultureInvariant);
    private static readonly Regex StartTypePattern = new(@"START_TYPE\s*:\s*\d+\s+(?<value>\S+)", RegexOptions.CultureInvariant);
    private static readonly Regex AccountPattern = new(@"SERVICE_START_NAME\s*:\s*(?<value>.+?)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex BinaryPathPattern = new(@"BINARY_PATH_NAME\s*:\s*(?<value>.+?)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private readonly RunPaths _paths;
    private readonly CleanupManifest _manifest;
    private readonly ToolRunner _run;
    private readonly TimeSpan _pollInterval;
    private readonly string _scPath;

    /// <summary>Creates the control over the run folder and manifest, with <c>sc.exe</c> run through <see cref="CommandRunner"/>.</summary>
    public ServiceControl(RunPaths paths, CleanupManifest manifest, Redactor redactor)
        : this(paths, manifest, (file, args, timeout) => CommandRunner.RunAsync(file, args, timeout, redactor), TimeSpan.FromSeconds(1))
    {
    }

    /// <summary>Creates the control with a tool runner and poll interval (tests use a fake runner and no delay).</summary>
    internal ServiceControl(RunPaths paths, CleanupManifest manifest, ToolRunner run, TimeSpan pollInterval)
    {
        _paths = paths;
        _manifest = manifest;
        _run = run;
        _pollInterval = pollInterval;
        _scPath = Path.Combine(Environment.SystemDirectory, "sc.exe");
    }

    /// <inheritdoc />
    public async Task<Result> Create(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || exePath.Contains(' ', StringComparison.Ordinal))
        {
            return Result.Failure("[ServiceControl.Create] the executable path must not be empty or contain a space");
        }

        var validated = _paths.Validate(exePath);
        if (validated.IsFailure)
        {
            return Result.Failure($"[ServiceControl.Create] {validated.Error}");
        }

        var publish = Path.GetFullPath(_paths.AgentPublish).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!validated.Value.StartsWith(publish, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("[ServiceControl.Create] the executable is not under the agent publish folder");
        }

        var recorded = _manifest.Append(new ServiceEntry(AgentWindowsFacts.ServiceName));
        if (recorded.IsFailure)
        {
            return Result.Failure($"[ServiceControl.Create] {recorded.Error}");
        }

        var result = await RunSc(
            ["create", AgentWindowsFacts.ServiceName, "binPath=", exePath, "start=", "demand", "obj=", "LocalSystem"]).ConfigureAwait(false);
        return result.IsFailure ? Result.Failure(result.Error) : Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<string>> Query()
    {
        var output = await RunSc(["query", AgentWindowsFacts.ServiceName]).ConfigureAwait(false);
        if (output.IsFailure)
        {
            return Result<string>.Failure(output.Error);
        }

        var match = StatePattern.Match(output.Value.StandardOutput);
        return match.Success
            ? Result<string>.Success(match.Groups["state"].Value)
            : Result<string>.Failure("[ServiceControl.Query] no STATE in the sc.exe output");
    }

    /// <inheritdoc />
    public async Task<Result<ServiceConfig>> QueryConfig()
    {
        var output = await RunSc(["qc", AgentWindowsFacts.ServiceName]).ConfigureAwait(false);
        if (output.IsFailure)
        {
            return Result<ServiceConfig>.Failure(output.Error);
        }

        var text = output.Value.StandardOutput;
        var startType = StartTypePattern.Match(text);
        var account = AccountPattern.Match(text);
        var binaryPath = BinaryPathPattern.Match(text);
        if (!startType.Success || !account.Success || !binaryPath.Success)
        {
            return Result<ServiceConfig>.Failure("[ServiceControl.QueryConfig] START_TYPE, SERVICE_START_NAME or BINARY_PATH_NAME missing");
        }

        return Result<ServiceConfig>.Success(new ServiceConfig(
            startType.Groups["value"].Value,
            account.Groups["value"].Value,
            binaryPath.Groups["value"].Value));
    }

    /// <inheritdoc />
    public async Task<Result> StartAsync(CancellationToken ct)
    {
        var started = await RunSc(["start", AgentWindowsFacts.ServiceName], ErrorServiceAlreadyRunning).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return Result.Failure(started.Error);
        }

        return await WaitForState(StateRunning, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> StopAsync(CancellationToken ct)
    {
        var stopped = await RunSc(["stop", AgentWindowsFacts.ServiceName], ErrorServiceNotActive, ErrorServiceDoesNotExist).ConfigureAwait(false);
        if (stopped.IsFailure)
        {
            return Result.Failure(stopped.Error);
        }

        return await WaitForState(StateStopped, ct).ConfigureAwait(false);
    }

    /// <summary>Polls <see cref="Query"/> once a second until the wanted state or the 30-second limit.</summary>
    private async Task<Result> WaitForState(string wanted, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (true)
        {
            var state = await Query().ConfigureAwait(false);
            if (state.IsFailure)
            {
                // A service that does not exist is already stopped.
                return wanted == StateStopped && state.Error.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                    ? Result.Success()
                    : Result.Failure(state.Error);
            }

            if (string.Equals(state.Value, wanted, StringComparison.Ordinal))
            {
                return Result.Success();
            }

            if (DateTime.UtcNow >= deadline)
            {
                return Result.Failure($"[ServiceControl.Wait] service state is {state.Value}, not {wanted} after 30 seconds");
            }

            await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Runs <c>sc.exe</c>; exit codes in <paramref name="tolerated"/> count as success.</summary>
    private async Task<Result<CommandOutput>> RunSc(IReadOnlyList<string> arguments, params int[] tolerated)
    {
        var run = await _run(_scPath, arguments, ToolTimeout).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result<CommandOutput>.Failure($"[ServiceControl.Run] {run.Error}");
        }

        var output = run.Value;
        if (output.ExitCode == 0 || tolerated.Contains(output.ExitCode))
        {
            return Result<CommandOutput>.Success(output);
        }

        if (output.ExitCode == ErrorServiceDoesNotExist)
        {
            return Result<CommandOutput>.Failure("[ServiceControl.Run] service does not exist");
        }

        return Result<CommandOutput>.Failure($"[ServiceControl.Run] sc.exe {arguments[0]} exited {output.ExitCode}");
    }
}
