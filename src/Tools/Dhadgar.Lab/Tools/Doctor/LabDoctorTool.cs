using System.Diagnostics;
using System.Security.Principal;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Windows;
using Dhadgar.Shared.Results;
using Microsoft.Win32;

namespace Dhadgar.Lab.Tools;

/// <summary>
/// Arguments of <c>lab_doctor</c>. <c>member</c> is refused until members arrive in L32.
/// </summary>
public sealed record LabDoctorArgs(string? Member = null);

/// <summary>
/// <c>lab_doctor</c> (Open): read-only environment checks. Each external command runs with <c>ArgumentList</c>, a 10 second
/// limit, and its output through <see cref="Redactor"/> before it is kept. Docker runs through <see cref="IDockerCli"/> on the
/// Linux context. The result is <c>{ok, checks:[{name, status, detail}]}</c>; <c>ok</c> is false when any check fails.
/// </summary>
public sealed class LabDoctorTool : LabTool<LabDoctorArgs>
{
    private static readonly TimeSpan CommandLimit = TimeSpan.FromSeconds(10);

    private readonly Redactor _redactor;
    private readonly IDockerCli _docker;
    private readonly LabPaths _paths;
    private readonly DaemonTokenInfo _token;
    private readonly LabListenerPorts _listeners;

    /// <summary>
    /// Creates the tool over the redactor used for command output, the docker client, the lab paths, the token length
    /// and the daemon's own listener ports.
    /// </summary>
    public LabDoctorTool(Redactor redactor, IDockerCli docker, LabPaths paths, DaemonTokenInfo token, LabListenerPorts listeners)
    {
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _token = token ?? throw new ArgumentNullException(nameof(token));
        _listeners = listeners ?? throw new ArgumentNullException(nameof(listeners));
    }

    /// <inheritdoc />
    public override string Name => "lab_doctor";

    /// <inheritdoc />
    public override string Description => "Environment checks: OS, build, dotnet, Docker on the lab context, docker-users, lab ports, dev ports, token length, repo root. Read-only.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(LabDoctorArgs args, LabCallContext context, CancellationToken ct)
    {
        if (args.Member is not null)
        {
            return Result<object>.Failure("[Lab.Unavailable] members arrive in L32");
        }

        var docker = await DockerCheckAsync(ct).ConfigureAwait(false);
        var checks = new List<DoctorCheck>
        {
            new("os", DoctorStatus.Info, Environment.OSVersion.Platform.ToString()),
            BuildCheck(),
            await VersionCheckAsync("dotnet", ["--version"], "dotnet", ct).ConfigureAwait(false),
            docker,
            DockerUsersCheck(docker),
            await PortsCheckAsync(ct).ConfigureAwait(false),
            DevPortsCheck(),
            TokenCheck(_token),
            RepoRootCheck(_paths.RepoRoot),
        };

        return Result<object>.Success(LabDoctorResult.From(checks));
    }

    /// <summary>
    /// The token check: the length only, never the value or a prefix of it.
    /// </summary>
    internal static DoctorCheck TokenCheck(DaemonTokenInfo token)
    {
        ArgumentNullException.ThrowIfNull(token);
        string detail = $"{token.ByteLength} bytes (min {LabDefaults.MinTokenBytes})";
        return token.ByteLength >= LabDefaults.MinTokenBytes
            ? new DoctorCheck("token", DoctorStatus.Ok, detail)
            : new DoctorCheck("token", DoctorStatus.Fail, detail);
    }

    /// <summary>
    /// The repo-root check. It never carries the path, because the path carries the user name into bundles.
    /// </summary>
    internal static DoctorCheck RepoRootCheck(string repoRoot)
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return new DoctorCheck("repo-root", DoctorStatus.Fail, "Lab:RepoRoot is not set");
        }

        if (!Directory.Exists(repoRoot))
        {
            return new DoctorCheck("repo-root", DoctorStatus.Fail, "Lab:RepoRoot does not exist");
        }

        return File.Exists(Path.Combine(repoRoot, "Dhadgar.sln"))
            ? new DoctorCheck("repo-root", DoctorStatus.Ok, "exists")
            : new DoctorCheck("repo-root", DoctorStatus.Warn, "Lab:RepoRoot is not a MeridianConsole checkout");
    }

    /// <summary>
    /// The docker-users check on Windows. A member is ok. A non-member is a warning when the engine answered anyway, else a failure.
    /// </summary>
    internal static DoctorCheck DockerUsersCheck(DoctorCheck docker)
    {
        ArgumentNullException.ThrowIfNull(docker);
        if (!OperatingSystem.IsWindows())
        {
            return new DoctorCheck("docker-users", DoctorStatus.Info, "not Windows");
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole("docker-users"))
            {
                return new DoctorCheck("docker-users", DoctorStatus.Ok, "member");
            }

            return docker.Status == DoctorStatus.Ok
                ? new DoctorCheck("docker-users", DoctorStatus.Warn, "not a member; the engine answered anyway")
                : new DoctorCheck("docker-users", DoctorStatus.Fail, "not a member of docker-users");
        }
        catch (SystemException)
        {
            return new DoctorCheck("docker-users", DoctorStatus.Warn, "docker-users group not found");
        }
    }

    private static DoctorCheck DevPortsCheck()
    {
        // src/Dhadgar.AppHost/Program.cs binds no port as fixed (postgres, cache, messaging and the projects get dynamic
        // ports), so none of the dev ports in the N4 list is fixed and there is nothing to probe.
        return new DoctorCheck("dev-ports", DoctorStatus.Info, "no fixed dev ports in the AppHost");
    }

    private async Task<DoctorCheck> PortsCheckAsync(CancellationToken ct)
    {
        IReadOnlyList<int> ports = DoctorPorts.Checked;
        Dictionary<int, PortState> states = ports.ToDictionary(port => port, DoctorPorts.Probe);

        // The daemon's own listeners count as held by the lab (D-ML69), so docker is asked only when some in-use port is not one of them.
        IReadOnlySet<int> daemonHeld = _listeners.Held;
        IReadOnlySet<int>? labOwned = states.Any(state => state.Value == PortState.InUse && !daemonHeld.Contains(state.Key))
            ? await LabOwnedPortsAsync(ct).ConfigureAwait(false)
            : new HashSet<int>();

        return DoctorPorts.EvaluatePorts(ports, port => states[port], labOwned, daemonHeld);
    }

    private async Task<IReadOnlySet<int>?> LabOwnedPortsAsync(CancellationToken ct)
    {
        Result<DockerOutput> run = await _docker.RunAsync(
            DockerEngine.Linux,
            ["ps", "--filter", $"label={LabDefaults.ContainerLabel}=1", "--format", "{{.Ports}}"],
            ct).ConfigureAwait(false);

        return run.IsSuccess && run.Value!.ExitCode == 0
            ? DoctorPorts.ParseLabPublished(run.Value.StdOut)
            : null;
    }

    private async Task<DoctorCheck> DockerCheckAsync(CancellationToken ct)
    {
        Result<DockerOutput> run = await _docker.RunAsync(DockerEngine.Linux, ["version", "--format", "{{.Server.Os}}"], ct).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return new DoctorCheck("docker", DoctorStatus.Fail, _redactor.Redact(run.Error));
        }

        if (run.Value!.ExitCode != 0)
        {
            return new DoctorCheck("docker", DoctorStatus.Fail, "docker server not reachable on the lab context");
        }

        string os = _redactor.Redact(run.Value.StdOut.Trim());
        return os == "linux"
            ? new DoctorCheck("docker", DoctorStatus.Ok, "linux")
            : new DoctorCheck("docker", DoctorStatus.Fail, $"lab Linux context answers with {Truncate(os)}");
    }

    private static DoctorCheck BuildCheck()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new DoctorCheck("build", DoctorStatus.Info, "not Windows");
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
            var build = key?.GetValue("CurrentBuildNumber") as string;
            return build is null
                ? new DoctorCheck("build", DoctorStatus.Warn, "CurrentBuildNumber not found")
                : new DoctorCheck("build", DoctorStatus.Ok, build);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new DoctorCheck("build", DoctorStatus.Warn, "build number read denied");
        }
    }

    private async Task<DoctorCheck> VersionCheckAsync(string name, string[] arguments, string command, CancellationToken ct)
    {
        using var process = new Process();
        var start = process.StartInfo;
        start.FileName = command;
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.CreateNoWindow = true;
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        LabProcessStart.StripToken(start);

        try
        {
            if (!process.Start())
            {
                return new DoctorCheck(name, DoctorStatus.Fail, "did not start");
            }

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(CommandLimit);
            var output = await process.StandardOutput.ReadToEndAsync(limit.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);

            var line = _redactor.Redact(output.Trim());
            return process.ExitCode == 0
                ? new DoctorCheck(name, DoctorStatus.Ok, Truncate(line))
                : new DoctorCheck(name, DoctorStatus.Fail, $"exit {process.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return new DoctorCheck(name, DoctorStatus.Fail, "timed out");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new DoctorCheck(name, DoctorStatus.Fail, "not found on PATH");
        }
    }

    private static string Truncate(string text) => text.Length > 200 ? text[..200] : text;
}
