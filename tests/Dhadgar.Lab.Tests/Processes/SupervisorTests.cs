using System.Diagnostics;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Processes;

[Collection("LabProcesses")]
public sealed class SupervisorTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [SkippableFact]
    public async Task Supervisor_StartCaptureAndStop_KeepsLinesAndStops()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        using var harness = new SupervisorHarness();
        LabProcessSpec spec = SupervisorHarness.ChildSpec("tc-capture", relaunch: true, "--lines", "3", "--json", "--sleep-ms", "60000", "--message", "captured");

        Result<ProcessStatusDto> started = await harness.Supervisor.StartAsync(spec, CancellationToken.None);

        Assert.True(started.IsSuccess, started.Error);
        Assert.Equal("running", started.Value!.State);
        bool captured = await SupervisorHarness.WaitUntilAsync(
            () => (harness.Supervisor.ReadLines("tc-capture").Value?.Count ?? 0) >= 3, Patience);
        Assert.True(captured);
        Assert.Contains(harness.Supervisor.ReadLines("tc-capture").Value!, line => line.Message == "captured 3" && line.Level == "Information");

        Result stopped = await harness.Supervisor.StopAsync("tc-capture");

        Assert.True(stopped.IsSuccess, stopped.Error);
        ProcessStatusDto status = Assert.Single(harness.Supervisor.Snapshot());
        Assert.Equal("stopped", status.State);
        Assert.Equal(0, status.Restarts);
    }

    [SkippableFact]
    public async Task Supervisor_CrashWithLease_IsRelaunched()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        using var harness = new SupervisorHarness();
        _ = harness.Leases.Acquire("s1", "uat", null, false, null);
        LabProcessSpec spec = SupervisorHarness.ChildSpec("tc-relaunch", relaunch: true, "--exit", "3");

        _ = await harness.Supervisor.StartAsync(spec, CancellationToken.None);

        bool relaunched = await SupervisorHarness.WaitUntilAsync(() => harness.Supervisor.Snapshot().Single().Restarts >= 2, Patience);
        Assert.True(relaunched);
    }

    [SkippableFact]
    public async Task Supervisor_CrashWithoutLease_IsNotRelaunched()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        using var harness = new SupervisorHarness();
        LabProcessSpec spec = SupervisorHarness.ChildSpec("tc-nolease", relaunch: true, "--exit", "3");

        _ = await harness.Supervisor.StartAsync(spec, CancellationToken.None);

        bool crashed = await SupervisorHarness.WaitUntilAsync(() => harness.Supervisor.Snapshot().Single().State == "crashed", Patience);
        Assert.True(crashed);
        ProcessStatusDto status = harness.Supervisor.Snapshot().Single();
        Assert.Equal(1, status.Restarts);
    }

    [SkippableFact]
    public async Task Supervisor_FourthCrashInFiveMinutes_StaysCrashed()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        using var harness = new SupervisorHarness();
        _ = harness.Leases.Acquire("s1", "uat", null, false, null);
        LabProcessSpec spec = SupervisorHarness.ChildSpec("tc-fourth", relaunch: true, "--exit", "3");

        _ = await harness.Supervisor.StartAsync(spec, CancellationToken.None);

        bool stayedCrashed = await SupervisorHarness.WaitUntilAsync(() => harness.Supervisor.Snapshot().Single().Restarts == 4, Patience);
        Assert.True(stayedCrashed);
        await Task.Delay(300);
        ProcessStatusDto status = harness.Supervisor.Snapshot().Single();
        Assert.Equal("crashed", status.State);
        Assert.Equal(4, status.Restarts);
    }

    [SkippableFact]
    public async Task Supervisor_FaultedTarget_IsNotRelaunchedNorCounted()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        using var harness = new SupervisorHarness(faults: new FixedFaults("proc:tc-fault"));
        _ = harness.Leases.Acquire("s1", "uat", null, false, null);
        var exit = new TaskCompletionSource<ProcessExit>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Supervisor.Exited += (_, notice) => exit.TrySetResult(notice);
        LabProcessSpec spec = SupervisorHarness.ChildSpec("tc-fault", relaunch: true, "--exit", "3");

        _ = await harness.Supervisor.StartAsync(spec, CancellationToken.None);

        ProcessExit notice = await exit.Task.WaitAsync(Patience);
        Assert.True(notice.Expected);
        await Task.Delay(300);
        ProcessStatusDto status = harness.Supervisor.Snapshot().Single();
        Assert.Equal(0, status.Restarts);
        Assert.Equal("stopped", status.State);
        Assert.Null(status.Pid);
    }

    [SkippableFact]
    public async Task Supervisor_Relaunch_KeepsWritingLogFile()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        using var harness = new SupervisorHarness();
        _ = harness.Leases.Acquire("s1", "uat", null, false, null);
        const string marker = "relaunch-marker";
        LabProcessSpec spec = SupervisorHarness.ChildSpec("tc-keeplog", relaunch: true, "--lines", "1", "--message", marker, "--sleep-ms", "60000");
        string file = Path.Combine(harness.Paths.Logs, "tc-keeplog.jsonl");

        _ = await harness.Supervisor.StartAsync(spec, CancellationToken.None);
        bool firstLogged = await SupervisorHarness.WaitUntilAsync(() => CountMarker(file, marker) >= 1, Patience);
        Assert.True(firstLogged);

        int pid = harness.Supervisor.Snapshot().Single().Pid!.Value;
        using (Process child = Process.GetProcessById(pid))
        {
            child.Kill(entireProcessTree: true);
        }

        bool relaunched = await SupervisorHarness.WaitUntilAsync(
            () => harness.Supervisor.Snapshot().Single() is { Restarts: >= 1, State: "running" }, Patience);
        Assert.True(relaunched);

        // The relaunched child prints the marker again on start; the first launch also printed it, so two lines are expected.
        bool relogged = await SupervisorHarness.WaitUntilAsync(() => CountMarker(file, marker) >= 2, Patience);
        Assert.True(relogged);
    }

    [SkippableFact]
    public async Task Supervisor_EscapedVaultValue_IsRedactedAfterDecode()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        using var harness = new SupervisorHarness();
        const string secret = "Tk+9/abcDEF123456";
        _ = harness.Vault.Add(SecretKind.Password, secret.AsSpan());
        // System.Text.Json writes '+' as +, so the raw line does not contain the plain value.
        LabProcessSpec spec = SupervisorHarness.ChildSpec("tc-escaped", relaunch: true, "--lines", "1", "--json", "--message", "token " + secret, "--sleep-ms", "60000");

        _ = await harness.Supervisor.StartAsync(spec, CancellationToken.None);
        bool captured = await SupervisorHarness.WaitUntilAsync(() => (harness.Supervisor.ReadLines("tc-escaped").Value?.Count ?? 0) >= 1, Patience);
        Assert.True(captured);
        _ = await harness.Supervisor.StopAsync("tc-escaped");

        string ring = string.Join("\n", harness.Supervisor.ReadLines("tc-escaped").Value!.Select(line => line.Message));
        Assert.DoesNotContain(secret, ring, StringComparison.Ordinal);

        string file = Path.Combine(harness.Paths.Logs, "tc-escaped.jsonl");
        List<string> records = ReadSharedLines(file);
        Assert.NotEmpty(records);
        foreach (string record in records)
        {
            using JsonDocument document = JsonDocument.Parse(record);
            string message = document.RootElement.GetProperty("Message").GetString() ?? string.Empty;
            Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
        }
    }

    private static int CountMarker(string path, string marker) =>
        ReadSharedLines(path).Count(line => line.Contains(marker, StringComparison.Ordinal));

    /// <summary>
    /// Reads a log file the supervisor still holds open for writing. Returns no lines when the file does not exist yet.
    /// </summary>
    private static List<string> ReadSharedLines(string path)
    {
        var lines = new List<string>();
        if (!File.Exists(path))
        {
            return lines;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }
}
