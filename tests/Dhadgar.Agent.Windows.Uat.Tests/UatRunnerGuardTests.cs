using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Probe;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

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

        var exit = await RunRunAsync(actions, new FakeProbe(), launchedByAgent: true);

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.False(File.Exists(new RunPaths(_repo, RunId).Manifest));
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Run_NotElevated_RefusesWithoutManifestEntry()
    {
        var actions = new RecordingActions { Elevated = false };

        var exit = await RunRunAsync(actions, new FakeProbe(), launchedByAgent: false);

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.False(File.Exists(new RunPaths(_repo, RunId).Manifest));
    }

    [Fact]
    public async Task Run_FootprintPresent_RefusesWithoutManifestEntry()
    {
        var actions = new RecordingActions();
        var probe = new FakeProbe("service:DhadgarAgent");

        var exit = await RunRunAsync(actions, probe, launchedByAgent: false);

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.False(File.Exists(new RunPaths(_repo, RunId).Manifest));
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
