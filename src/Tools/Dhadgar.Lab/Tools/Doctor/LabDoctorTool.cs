using System.Diagnostics;
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
/// One doctor line: a name, a status (<c>ok</c>, <c>warn</c>, <c>fail</c> or <c>info</c>) and a short detail.
/// </summary>
public sealed record DoctorCheck(string Name, string Status, string Detail);

/// <summary>
/// The result of <c>lab_doctor</c>.
/// </summary>
public sealed record LabDoctorResult(IReadOnlyList<DoctorCheck> Checks);

/// <summary>
/// <c>lab_doctor</c> (Open): read-only environment checks. Each external command runs with <c>ArgumentList</c>, a 10 second
/// limit, and its output through <see cref="Redactor"/> before it is kept. Checks not yet implemented are listed as
/// <c>info</c> so the report says what it did not look at.
/// </summary>
public sealed class LabDoctorTool : LabTool<LabDoctorArgs>
{
    private static readonly TimeSpan CommandLimit = TimeSpan.FromSeconds(10);

    private readonly Redactor _redactor;

    /// <summary>
    /// Creates the tool over the redactor used for command output.
    /// </summary>
    public LabDoctorTool(Redactor redactor)
    {
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
    }

    /// <inheritdoc />
    public override string Name => "lab_doctor";

    /// <inheritdoc />
    public override string Description => "Environment checks: OS and build number, dotnet version, Docker server OS. Read-only; reports what it did not check as info.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(LabDoctorArgs args, LabCallContext context, CancellationToken ct)
    {
        if (args.Member is not null)
        {
            return Result<object>.Failure("[Lab.Unavailable] members arrive in L32");
        }

        var checks = new List<DoctorCheck>
        {
            new("os", "info", Environment.OSVersion.Platform.ToString()),
            BuildCheck(),
            await VersionCheckAsync("dotnet", ["--version"], "dotnet", ct).ConfigureAwait(false),
            await VersionCheckAsync("docker", ["version", "--format", "{{.Server.Os}}"], "docker", ct).ConfigureAwait(false),
            new("ports", "info", "not checked in this packet"),
            new("docker-users", "info", "not checked in this packet"),
            new("token", "info", "not checked in this packet"),
            new("repo-root", "info", "not checked in this packet"),
        };

        return Result<object>.Success(new LabDoctorResult(checks));
    }

    private static DoctorCheck BuildCheck()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new DoctorCheck("build", "info", "not Windows; no build number");
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
            var build = key?.GetValue("CurrentBuildNumber") as string;
            return build is null
                ? new DoctorCheck("build", "warn", "CurrentBuildNumber not found")
                : new DoctorCheck("build", "ok", build);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new DoctorCheck("build", "warn", "build number read denied");
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

        try
        {
            if (!process.Start())
            {
                return new DoctorCheck(name, "fail", "did not start");
            }

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(CommandLimit);
            var output = await process.StandardOutput.ReadToEndAsync(limit.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);

            var line = _redactor.Redact(output.Trim());
            return process.ExitCode == 0
                ? new DoctorCheck(name, "ok", line.Length > 200 ? line[..200] : line)
                : new DoctorCheck(name, "fail", $"exit {process.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return new DoctorCheck(name, "fail", "timed out");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new DoctorCheck(name, "fail", "not found on PATH");
        }
    }
}
