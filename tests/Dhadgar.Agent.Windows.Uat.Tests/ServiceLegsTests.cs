using Dhadgar.Agent.Windows.Uat.Agent;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Scenarios;
using Dhadgar.Agent.Windows.Uat.Service;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>U10 "Service setup" (D-S222 (A)): the Root pass condition and the service configuration check.</summary>
public class ServiceLegsTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-service-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;
    private readonly CleanupManifest _manifest;
    private readonly Redactor _redactor = new();
    private readonly RecordingActions _actions = new();
    private readonly FakeServiceControl _service = new();

    public ServiceLegsTests()
    {
        _paths = new RunPaths(_repo, "run-1");
        Directory.CreateDirectory(_paths.AgentPublish);
        Directory.CreateDirectory(_paths.Logs);
        File.WriteAllText(Path.Combine(_paths.AgentPublish, "appsettings.json"), "{}");
        _manifest = new CleanupManifest(_paths.Manifest);
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    [SkippableFact]
    public async Task U10_Passes_WhenBothCertificatesAreInRootAndTheServiceIsDemandLocalSystemStopped()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await RunAsync(evidence, rootIncludesDecoy: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("pass", evidence["U10"]);
        Assert.Equal(result.Value.Tls.ThumbprintSha1, evidence["tlsThumbprint"]);
        Assert.Equal(result.Value.DecoyThumbprint, evidence["decoyThumbprint"]);
        Assert.Equal(Path.Combine(_paths.AgentPublish, "Dhadgar.Agent.Windows.exe"), evidence["serviceBinaryPath"]);
        Assert.Equal(Path.Combine(_paths.AgentPublish, "Dhadgar.Agent.Windows.exe"), _service.CreatedPath);
    }

    [SkippableFact]
    public async Task U10_Fails_WhenTheStartTypeIsAuto()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        _service.Config = new ServiceConfig("AUTO_START", "LocalSystem", "x");
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await RunAsync(evidence, rootIncludesDecoy: true);

        Assert.True(result.IsFailure);
        Assert.Contains("AUTO_START", result.Error!, StringComparison.Ordinal);
        Assert.Equal("fail", evidence["U10"]);
    }

    [SkippableFact]
    public async Task U10_Fails_WhenTheAccountIsNotLocalSystem()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        _service.Config = new ServiceConfig("DEMAND_START", @".\agent", "x");
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await RunAsync(evidence, rootIncludesDecoy: true);

        Assert.True(result.IsFailure);
        Assert.Contains("LocalSystem", result.Error!, StringComparison.Ordinal);
        Assert.Equal("fail", evidence["U10"]);
    }

    [SkippableFact]
    public async Task U10_Fails_WhenTheServiceIsNotStopped()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        _service.State = "RUNNING";
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await RunAsync(evidence, rootIncludesDecoy: true);

        Assert.True(result.IsFailure);
        Assert.Equal("fail", evidence["U10"]);
    }

    [SkippableFact]
    public async Task U10_Fails_WhenTheDecoyIsNotInRoot()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await RunAsync(evidence, rootIncludesDecoy: false);

        Assert.True(result.IsFailure);
        Assert.Contains("decoy", result.Error!, StringComparison.Ordinal);
        Assert.Equal("fail", evidence["U10"]);
    }

    [Fact]
    public async Task U3_InServiceMode_FailsWhenTheDecoyIsStillInRoot()
    {
        var manifest = new CleanupManifest(_paths.Manifest);
        var world = new LegWorld(_paths, manifest)
        {
            OnStart = w =>
            {
                if (w.StartCount == 1)
                {
                    w.Root.Add(new StoreCert(
                        "CN=Meridian Console Agent CA, O=UAT decoy run-1", w.CaSha1, w.CaSha256, [], DateTime.UtcNow,
                        DateTime.UtcNow.AddDays(1), false, null));
                }
            },
        };
        var options = AgentLegsTests.Options(_paths, world.OrganizationId) with { DecoyThumbprint = world.CaSha1 };
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await new AgentLegs(_paths, manifest, new AgentLegsDeps(world, world, world, world, world.MintAsync), options)
            .RunAsync(evidence, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("U3:", result.Error!, StringComparison.Ordinal);
        Assert.Contains("decoy CA from U10", result.Error!, StringComparison.Ordinal);
        Assert.Equal("fail", evidence["U3"]);
    }

    [Fact]
    public async Task U3_InServiceMode_PassesWhenTheDecoyIsGone()
    {
        var manifest = new CleanupManifest(_paths.Manifest);
        var world = new LegWorld(_paths, manifest);
        var options = AgentLegsTests.Options(_paths, world.OrganizationId) with { DecoyThumbprint = world.CaSha1 };
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        await new AgentLegs(_paths, manifest, new AgentLegsDeps(world, world, world, world, world.MintAsync), options)
            .RunAsync(evidence, CancellationToken.None);

        Assert.Equal("pass", evidence["U3"]);
    }

    /// <summary>Runs U10 with the recording machine; the fake Root holds the TLS leaf and, optionally, the decoy.</summary>
    private Task<Result<ServiceSetupOutcome>> RunAsync(Dictionary<string, object?> evidence, bool rootIncludesDecoy)
    {
        var config = new AgentConfig(
            new Uri("https://localhost:5443"),
            10,
            10,
            _paths.AgentState,
            Path.Combine(_paths.AgentData, "Servers"),
            Path.Combine(_paths.AgentData, "Temp"));
        var deps = new ServiceSetupDeps(_actions, _service, () =>
        {
            var thumbprints = _actions.Added
                .Where(a => rootIncludesDecoy || a.Certificate.Subject.StartsWith("CN=localhost", StringComparison.Ordinal))
                .Select(a => a.Certificate.Thumbprint)
                .ToList();
            return Result<RootSnapshot>.Success(new RootSnapshot(thumbprints));
        });

        return ServiceSetup.RunAsync(_paths, "run-1", _manifest, _redactor, config, deps, evidence);
    }

    /// <summary>A service control that records the create and answers with a configurable state and config.</summary>
    private sealed class FakeServiceControl : IServiceControl
    {
        public string? CreatedPath { get; private set; }

        public ServiceConfig Config { get; set; } = new("DEMAND_START", "LocalSystem", "x");

        public string State { get; set; } = "STOPPED";

        public Task<Result> Create(string exePath)
        {
            CreatedPath = exePath;
            return Task.FromResult(Result.Success());
        }

        public Task<Result<string>> Query() => Task.FromResult(Result<string>.Success(State));

        public Task<Result<ServiceConfig>> QueryConfig() => Task.FromResult(Result<ServiceConfig>.Success(Config));

        public Task<Result> StartAsync(CancellationToken ct) => Task.FromResult(Result.Success());

        public Task<Result> StopAsync(CancellationToken ct) => Task.FromResult(Result.Success());
    }
}
