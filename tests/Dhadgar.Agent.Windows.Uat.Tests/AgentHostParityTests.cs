using Dhadgar.Agent.Windows.Uat.Agent;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Scenarios;
using Dhadgar.Agent.Windows.Uat.Service;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// P22 parity: the same agent legs, run once with the process host and once with the service host over the same
/// fake machine state, make the same calls and reach the same steps. The one difference is the agent.log read,
/// which only the process host has (<see cref="IAgentHost.ConsoleLogPath"/>).
/// </summary>
public class AgentHostParityTests : IDisposable
{
    private const string RunId = "20261027-142233-9f3a";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uat-parity-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private RunPaths Paths(string name)
    {
        var paths = new RunPaths(Path.Combine(_root, name), RunId);
        Directory.CreateDirectory(paths.AgentPublish);
        Directory.CreateDirectory(paths.Logs);
        File.WriteAllText(Path.Combine(paths.AgentPublish, "appsettings.json"), "{}");
        return paths;
    }

    [Fact]
    public async Task ProcessAndServiceHosts_MakeTheSameCallsAndReachTheSameSteps()
    {
        var processPaths = Paths("process");
        var processManifest = new CleanupManifest(processPaths.Manifest);
        var processWorld = new LegWorld(processPaths, processManifest);
        var processEvidence = new Dictionary<string, object?>(StringComparer.Ordinal);
        var processLegs = new AgentLegs(
            processPaths,
            processManifest,
            new AgentLegsDeps(processWorld, processWorld, processWorld, processWorld, processWorld.MintAsync),
            AgentLegsTests.Options(processPaths, processWorld.OrganizationId));
        var processResult = await processLegs.RunAsync(processEvidence, CancellationToken.None);

        var servicePaths = Paths("service");
        var serviceManifest = new CleanupManifest(servicePaths.Manifest);
        var serviceWorld = new LegWorld(servicePaths, serviceManifest);
        var serviceHost = new ServiceAgentHost(new WorldServiceControl(serviceWorld), serviceManifest);
        var serviceEvidence = new Dictionary<string, object?>(StringComparer.Ordinal);
        var serviceLegs = new AgentLegs(
            servicePaths,
            serviceManifest,
            new AgentLegsDeps(serviceWorld, serviceWorld, serviceHost, serviceWorld, serviceWorld.MintAsync),
            AgentLegsTests.Options(servicePaths, serviceWorld.OrganizationId));
        var serviceResult = await serviceLegs.RunAsync(serviceEvidence, CancellationToken.None);

        Assert.True(processResult.IsSuccess, processResult.Error);
        Assert.True(serviceResult.IsSuccess, serviceResult.Error);
        Assert.Equal(
            processWorld.Calls.Where(call => call != "agent.log.read"),
            serviceWorld.Calls);
        foreach (var step in new[] { "U2", "U3", "U4" })
        {
            Assert.Equal(processEvidence[step], serviceEvidence[step]);
        }

        Assert.Equal(processEvidence["warnings"], serviceEvidence["warnings"]);
        Assert.Equal("service", serviceHost.Mode);
        Assert.Null(serviceHost.ConsoleLogPath);
    }

    /// <summary>Stands in for <c>sc.exe</c>: start and stop drive the same fake agent the process host drives.</summary>
    private sealed class WorldServiceControl : IServiceControl
    {
        private readonly LegWorld _world;

        public WorldServiceControl(LegWorld world) => _world = world;

        public Task<Result> Create(string exePath) => Task.FromResult(Result.Success());

        public Task<Result<string>> Query() => Task.FromResult(Result<string>.Failure("not used by the legs"));

        public Task<Result<ServiceConfig>> QueryConfig() =>
            Task.FromResult(Result<ServiceConfig>.Failure("not used by the legs"));

        public Task<Result> StartAsync(CancellationToken ct) => _world.StartAsync(ct);

        public Task<Result> StopAsync(CancellationToken ct) => _world.StopAsync(ct);
    }
}
