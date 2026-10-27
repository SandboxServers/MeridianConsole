using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Probe;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>Tests that change the global console error writer are not run in parallel.</summary>
[CollectionDefinition("Console", DisableParallelization = true)]
public class ConsoleCollection;

[Collection("Console")]
public class UatRunnerGuardTests : IDisposable
{
    private const string RunId = "20261027-142233-9f3a";
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-guard-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    [Fact]
    public async Task Run_ClaudeCodeSet_RefusesWithoutManifestEntry()
    {
        var actions = new RecordingActions();

        var (exit, err) = await RunRunCapturedAsync(actions, new FakeProbe(), launchedByAgent: true);

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Contains("CLAUDECODE is set", err, StringComparison.Ordinal);
        Assert.False(File.Exists(new RunPaths(_repo, RunId).Manifest));
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Run_NotElevated_RefusesWithoutManifestEntry()
    {
        var actions = new RecordingActions { Elevated = false };

        var (exit, err) = await RunRunCapturedAsync(actions, new FakeProbe(), launchedByAgent: false);

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Contains("not elevated", err, StringComparison.Ordinal);
        Assert.False(File.Exists(new RunPaths(_repo, RunId).Manifest));
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Run_FootprintPresent_RefusesWithoutManifestEntry()
    {
        var actions = new RecordingActions();
        var probe = new FakeProbe("service:DhadgarAgent");

        var (exit, err) = await RunRunCapturedAsync(actions, probe, launchedByAgent: false);

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Contains("agent footprint present", err, StringComparison.Ordinal);
        Assert.False(File.Exists(new RunPaths(_repo, RunId).Manifest));
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Cleanup_ClaudeCodeSet_RefusesAndTouchesNothing()
    {
        var actions = new RecordingActions();
        WriteServiceManifest();

        var (exit, _) = await RunCleanupCapturedAsync(actions, launchedByAgent: true);

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Cleanup_NotElevatedWithMachineItems_Refuses()
    {
        var actions = new RecordingActions { Elevated = false };
        WriteServiceManifest();

        var (exit, _) = await RunCleanupCapturedAsync(actions, launchedByAgent: false);

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Empty(actions.Calls);
    }

    private void WriteServiceManifest()
    {
        var runDir = new RunPaths(_repo, RunId).RunDir;
        Directory.CreateDirectory(runDir);
        File.WriteAllText(new RunPaths(_repo, RunId).Manifest, "{\"kind\":\"service\",\"name\":\"DhadgarAgent\"}\n");
    }

    private Task<(int Exit, string Err)> RunRunCapturedAsync(RecordingActions actions, IMachineProbe probe, bool launchedByAgent) =>
        CaptureAsync(() => RunRunAsync(actions, probe, launchedByAgent));

    private Task<(int Exit, string Err)> RunCleanupCapturedAsync(RecordingActions actions, bool launchedByAgent)
    {
        var args = new UatArgs("cleanup", _repo, RunId, null, AcceptMachineChanges: false, RunDir: new RunPaths(_repo, RunId).RunDir);
        return CaptureAsync(() => UatRunner.RunWithAsync(
            args,
            actions,
            new FakeProbe(),
            launchedByAgent,
            new Dictionary<string, IUatScenario>(StringComparer.Ordinal),
            CancellationToken.None));
    }

    private static async Task<(int Exit, string Err)> CaptureAsync(Func<Task<int>> run)
    {
        var err = new StringWriter();
        var old = Console.Error;
        Console.SetError(err);
        try
        {
            var exit = await run();
            return (exit, err.ToString());
        }
        finally
        {
            Console.SetError(old);
        }
    }

    private Task<int> RunRunAsync(RecordingActions actions, IMachineProbe probe, bool launchedByAgent)
    {
        var args = new UatArgs("run", _repo, RunId, "process", AcceptMachineChanges: true, RunDir: null);
        return UatRunner.RunWithAsync(
            args,
            actions,
            probe,
            launchedByAgent,
            new Dictionary<string, IUatScenario>(StringComparer.Ordinal),
            CancellationToken.None);
    }

    private sealed class FakeProbe(params string[] items) : IMachineProbe
    {
        public Footprint Scan() => new(items);
    }
}
