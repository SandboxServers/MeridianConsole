using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Results;
using Dhadgar.Shared.Results;
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
    public async Task Run_AgentElevatedCleanTree_ReachesFootprintGuard()
    {
        var actions = new RecordingActions();
        var probe = new FakeProbe("service:DhadgarAgent");
        var tree = new FakeWorkTree(Result<bool>.Success(true));

        var (exit, err) = await RunRunCapturedAsync(actions, probe, launchedByAgent: true, tree);

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Contains("agent footprint present", err, StringComparison.Ordinal);
        Assert.DoesNotContain("D-S239", err, StringComparison.Ordinal);
        Assert.DoesNotContain("D-S225", err, StringComparison.Ordinal);
        Assert.Equal(1, tree.Calls);
        Assert.False(File.Exists(new RunPaths(_repo, RunId).Manifest));
        Assert.False(LaunchRecord.Exists(new RunPaths(_repo, RunId)));
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Run_AgentNotElevated_Refused()
    {
        var actions = new RecordingActions { Elevated = false };
        var tree = new FakeWorkTree(Result<bool>.Success(true));

        var (exit, err) = await RunRunCapturedAsync(actions, new FakeProbe(), launchedByAgent: true, tree);

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Contains("not elevated", err, StringComparison.Ordinal);
        Assert.Equal(0, tree.Calls);
        Assert.False(File.Exists(new RunPaths(_repo, RunId).Manifest));
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Run_AgentDirtyTree_RefusedWithoutManifest()
    {
        var actions = new RecordingActions();

        var (exit, err) = await RunRunCapturedAsync(actions, new FakeProbe(), launchedByAgent: true, new FakeWorkTree(Result<bool>.Success(false)));

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Contains("clean tree (D-S239)", err, StringComparison.Ordinal);
        Assert.False(File.Exists(new RunPaths(_repo, RunId).Manifest));
        Assert.False(LaunchRecord.Exists(new RunPaths(_repo, RunId)));
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Run_AgentTreeCheckFails_Refused()
    {
        var actions = new RecordingActions();
        var failed = Result<bool>.Failure("[UAT.TreeCheck] git status failed: exit 128");

        var (exit, err) = await RunRunCapturedAsync(actions, new FakeProbe(), launchedByAgent: true, new FakeWorkTree(failed));

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Contains("tree check failed", err, StringComparison.Ordinal);
        Assert.False(File.Exists(new RunPaths(_repo, RunId).Manifest));
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Run_UserDirtyTree_IsNotAskedAndReachesFootprintGuard()
    {
        var actions = new RecordingActions();
        var probe = new FakeProbe("service:DhadgarAgent");

        var (exit, err) = await RunRunCapturedAsync(actions, probe, launchedByAgent: false, new ThrowingWorkTree());

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Contains("agent footprint present", err, StringComparison.Ordinal);
        Assert.False(File.Exists(new RunPaths(_repo, RunId).Manifest));
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Run_NotElevated_RefusesWithoutManifestEntry()
    {
        var actions = new RecordingActions { Elevated = false };

        var (exit, err) = await RunRunCapturedAsync(actions, new FakeProbe(), launchedByAgent: false, new ThrowingWorkTree());

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

        var (exit, err) = await RunRunCapturedAsync(actions, probe, launchedByAgent: false, new ThrowingWorkTree());

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Contains("agent footprint present", err, StringComparison.Ordinal);
        Assert.False(File.Exists(new RunPaths(_repo, RunId).Manifest));
        Assert.Empty(actions.Calls);
    }

    [Theory]
    [InlineData("launch")]
    [InlineData("manifest")]
    public async Task Run_RunFolderAlreadyUsed_Refused(string existing)
    {
        var paths = new RunPaths(_repo, RunId);
        Directory.CreateDirectory(paths.RunDir);
        var file = existing == "launch" ? Path.Combine(paths.RunDir, LaunchRecord.FileName) : paths.Manifest;
        File.WriteAllText(file, "{}");
        var actions = new RecordingActions();

        var (exit, err) = await RunRunCapturedAsync(actions, new FakeProbe(), launchedByAgent: false, new ThrowingWorkTree());

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Contains("already used", err, StringComparison.Ordinal);
        Assert.Equal("{}", File.ReadAllText(file));
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Cleanup_AgentOnAgentRunFolder_Proceeds()
    {
        var actions = new RecordingActions();
        WriteServiceManifest();
        WriteLaunch("agent", RunId);

        var (exit, err) = await RunCleanupCapturedAsync(actions, launchedByAgent: true);

        Assert.NotEqual(UatExitCodes.Refused, exit);
        Assert.DoesNotContain("cleanup refused", err, StringComparison.Ordinal);
        Assert.Contains("StopService:DhadgarAgent", actions.Calls);
        Assert.Contains("DeleteService:DhadgarAgent", actions.Calls);
    }

    [Fact]
    public async Task Cleanup_AgentOnUserRunFolder_Refused()
    {
        var actions = new RecordingActions();
        WriteServiceManifest();
        WriteLaunch("user", RunId);
        var manifestBefore = File.ReadAllText(new RunPaths(_repo, RunId).Manifest);

        var (exit, err) = await RunCleanupCapturedAsync(actions, launchedByAgent: true);

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Contains("not an agent run", err, StringComparison.Ordinal);
        Assert.Equal(manifestBefore, File.ReadAllText(new RunPaths(_repo, RunId).Manifest));
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Cleanup_AgentWithoutLaunchRecord_Refused()
    {
        var actions = new RecordingActions();
        WriteServiceManifest();

        var (exit, err) = await RunCleanupCapturedAsync(actions, launchedByAgent: true);

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Contains("[Launch.Missing]", err, StringComparison.Ordinal);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Cleanup_AgentLaunchRecordForOtherRun_Refused()
    {
        var actions = new RecordingActions();
        WriteServiceManifest();
        WriteLaunch("agent", "20261027-000000-0000");

        var (exit, _) = await RunCleanupCapturedAsync(actions, launchedByAgent: true);

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Cleanup_UserWithoutLaunchRecord_Proceeds()
    {
        var actions = new RecordingActions();
        WriteServiceManifest();

        var (exit, err) = await RunCleanupCapturedAsync(actions, launchedByAgent: false);

        Assert.NotEqual(UatExitCodes.Refused, exit);
        Assert.DoesNotContain("[Launch.Missing]", err, StringComparison.Ordinal);
        Assert.Contains("StopService:DhadgarAgent", actions.Calls);
        Assert.Contains("DeleteService:DhadgarAgent", actions.Calls);
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

    [Fact]
    public async Task StackOnly_RunFolderExists_RefusesAndTouchesNothing()
    {
        var actions = new RecordingActions();
        WriteServiceManifest();
        var args = new UatArgs("stack-only", _repo, RunId, null, AcceptMachineChanges: false, RunDir: null);

        var (exit, err) = await CaptureAsync(() => UatRunner.RunWithAsync(
            args,
            actions,
            new FakeProbe(),
            launchedByAgent: true,
            new ThrowingWorkTree(),
            new Dictionary<string, IUatScenario>(StringComparer.Ordinal),
            CancellationToken.None));

        Assert.Equal(UatExitCodes.Refused, exit);
        Assert.Contains("run folder already exists", err, StringComparison.Ordinal);
        Assert.Empty(actions.Calls);
    }

    private void WriteServiceManifest()
    {
        var runDir = new RunPaths(_repo, RunId).RunDir;
        Directory.CreateDirectory(runDir);
        File.WriteAllText(new RunPaths(_repo, RunId).Manifest, "{\"kind\":\"service\",\"name\":\"DhadgarAgent\"}\n");
    }

    private void WriteLaunch(string launchedBy, string runId)
    {
        var paths = new RunPaths(_repo, RunId);
        Directory.CreateDirectory(paths.RunDir);
        var record = new LaunchRecord(1, runId, "run", "process", launchedBy, true, "0123456789abcdef", DateTime.UtcNow);
        Assert.True(LaunchRecord.Write(paths, record).IsSuccess);
    }

    private Task<(int Exit, string Err)> RunRunCapturedAsync(RecordingActions actions, IMachineProbe probe, bool launchedByAgent, IWorkTree tree) =>
        CaptureAsync(() => RunRunAsync(actions, probe, launchedByAgent, tree));

    private Task<(int Exit, string Err)> RunCleanupCapturedAsync(RecordingActions actions, bool launchedByAgent)
    {
        var args = new UatArgs("cleanup", _repo, RunId, null, AcceptMachineChanges: false, RunDir: new RunPaths(_repo, RunId).RunDir);
        return CaptureAsync(() => UatRunner.RunWithAsync(
            args,
            actions,
            new FakeProbe(),
            launchedByAgent,
            new ThrowingWorkTree(),
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

    private Task<int> RunRunAsync(RecordingActions actions, IMachineProbe probe, bool launchedByAgent, IWorkTree tree)
    {
        var args = new UatArgs("run", _repo, RunId, "process", AcceptMachineChanges: true, RunDir: null);
        return UatRunner.RunWithAsync(
            args,
            actions,
            probe,
            launchedByAgent,
            tree,
            new Dictionary<string, IUatScenario>(StringComparer.Ordinal),
            CancellationToken.None);
    }

    private sealed class FakeProbe(params string[] items) : IMachineProbe
    {
        public Footprint Scan() => new(items);
    }

    /// <summary>A tree check that returns a fixed result and counts its calls.</summary>
    private sealed class FakeWorkTree(Result<bool> result) : IWorkTree
    {
        public int Calls { get; private set; }

        public Task<Result<bool>> IsCleanAsync(string repoRoot, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    /// <summary>A tree check that must never run: a user run does not depend on git (D-S239).</summary>
    private sealed class ThrowingWorkTree : IWorkTree
    {
        public Task<Result<bool>> IsCleanAsync(string repoRoot, CancellationToken ct) =>
            throw new InvalidOperationException("the tree check must not run for this call");
    }
}
