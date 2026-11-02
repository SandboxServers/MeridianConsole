using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Soak;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Tools.Chaos;
using Dhadgar.Lab.Tools.Soak;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Chaos;

/// <summary>
/// The chaos and soak tools refuse to run without a configured repo root, before they resolve any plan or spec folder
/// (R2-N12, copied from <c>mutation_run</c>).
/// </summary>
public sealed class ChaosToolsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-chaos-tools", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);
    private readonly LabPaths _paths;
    private readonly JobRunner _runner;

    public ChaosToolsTests()
    {
        // No repo root: FromRoot leaves RepoRoot empty, which is the case under test.
        _paths = LabPaths.FromRoot(_root);
        Directory.CreateDirectory(_paths.Jobs);
        _runner = new JobRunner(_paths, new LeaseBook(_time, new NoEvents(), new LeaseOptions()), _validator, _time,
            NullLogger<JobRunner>.Instance);
    }

    public void Dispose()
    {
        _runner.Dispose();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file still open on a failing test; the temp folder is cleaned by the OS.
        }
    }

    [Fact]
    public async Task ChaosRun_EmptyRepoRoot_IsRefused()
    {
        var tool = new ChaosRunTool(_runner, _paths, _validator, new GatewayPacer(_time), _time, new NoServices());
        JsonElement arguments = JsonSerializer.SerializeToElement(new { plan = "chaos-plan", duration_s = 60 });

        Result<JsonElement> result = await tool.InvokeAsync(arguments, new LabCallContext("call-1", null, "test"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.InvalidArgument] the repo root is not configured", result.Error);
    }

    [Fact]
    public async Task SoakRun_EmptyRepoRoot_IsRefused()
    {
        var sampler = new ResourceSampler(new NoDocker(), new NoRegistry(), _ => throw new InvalidOperationException("not used"), _time);
        var tool = new SoakRunTool(_runner, _paths, _validator, new GatewayPacer(_time), sampler, new ResourceSampleStore(), _time,
            new NoServices());
        JsonElement arguments = JsonSerializer.SerializeToElement(new { spec = "docs/guides/uat-specs/lab-selftest.toml", duration_s = 300 });

        Result<JsonElement> result = await tool.InvokeAsync(arguments, new LabCallContext("call-2", null, "test"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.InvalidArgument] the repo root is not configured", result.Error);
    }

    [Fact]
    public async Task SoakRun_RowNotInSpec_RefusedBeforeStart()
    {
        SoakRunTool tool = await SoakToolWithTwoRowSpecAsync();
        JsonElement arguments = JsonSerializer.SerializeToElement(new { spec = "two-rows.toml", rows = new[] { "row-missing" }, duration_s = 300 });

        Result<JsonElement> result = await tool.InvokeAsync(arguments, new LabCallContext("call-3", null, "test"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.NotFound]", result.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(_paths.Jobs));
    }

    [Fact]
    public async Task SoakRun_RowWithoutDependency_Refused()
    {
        SoakRunTool tool = await SoakToolWithTwoRowSpecAsync();
        JsonElement arguments = JsonSerializer.SerializeToElement(new { spec = "two-rows.toml", rows = new[] { "row2" }, duration_s = 300 });

        Result<JsonElement> result = await tool.InvokeAsync(arguments, new LabCallContext("call-4", null, "test"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
        Assert.Contains("without its dependency", result.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(_paths.Jobs));
    }

    [Fact]
    public async Task SoakRun_SpecMissing_RefusedBeforeStart()
    {
        SoakRunTool tool = await SoakToolWithTwoRowSpecAsync();
        JsonElement arguments = JsonSerializer.SerializeToElement(new { spec = "absent.toml", duration_s = 300 });

        Result<JsonElement> result = await tool.InvokeAsync(arguments, new LabCallContext("call-5", null, "test"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.NotFound]", result.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(_paths.Jobs));
    }

    [Fact]
    public async Task SoakRun_SpecTooLarge_RefusedBeforeStart()
    {
        SoakRunTool tool = await SoakToolWithTwoRowSpecAsync();
        string big = Path.Combine(_root, "repo", "docs", "guides", "uat-specs", "big.toml");
        await File.WriteAllTextAsync(big, new string('#', (int)UatSpecPaths.MaxSpecBytes + 1));
        JsonElement arguments = JsonSerializer.SerializeToElement(new { spec = "big.toml", duration_s = 300 });

        Result<JsonElement> result = await tool.InvokeAsync(arguments, new LabCallContext("call-6", null, "test"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.InvalidArgument] the spec is too large", result.Error);
        Assert.Empty(Directory.GetDirectories(_paths.Jobs));
    }

    /// <summary>A soak tool over a temp repo root that holds a two-row spec; row 2 depends on row 1.</summary>
    private async Task<SoakRunTool> SoakToolWithTwoRowSpecAsync()
    {
        string repo = Path.Combine(_root, "repo");
        string specs = Path.Combine(repo, "docs", "guides", "uat-specs");
        Directory.CreateDirectory(specs);
        const string Spec = """
            schema = 1

            [section]
            id = "two-rows"
            title = "Two rows"
            sequential = true
            requires = ["docker"]
            allow_invariants = []

            [[row]]
            id = "row1"
            title = "First row"
            expected = "the first row runs"

            [[row]]
            id = "row2"
            title = "Second row"
            expected = "the second row runs"
            depends_on = ["row1"]
            """;
        await File.WriteAllTextAsync(Path.Combine(specs, "two-rows.toml"), Spec);

        var paths = LabPaths.FromRoot(_root, repo);
        var sampler = new ResourceSampler(new NoDocker(), new NoRegistry(), _ => throw new InvalidOperationException("not used"), _time);
        return new SoakRunTool(_runner, paths, _validator, new GatewayPacer(_time), sampler, new ResourceSampleStore(), _time,
            new NoServices());
    }

    /// <summary>An event sink that drops every event.</summary>
    private sealed class NoEvents : ILabEvents
    {
        public void Emit(LabEvent labEvent)
        {
        }
    }

    /// <summary>A service provider with no services; the guard refuses before any is resolved.</summary>
    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>A Docker client that must never be called by these tests.</summary>
    private sealed class NoDocker : IDockerCli
    {
        public Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct) =>
            throw new InvalidOperationException("not used");
    }

    /// <summary>A target registry with no targets; the guard refuses before any target is read.</summary>
    private sealed class NoRegistry : ILabTargetRegistry
    {
        public Result<LabTarget> Register(string targetId, LabTargetKind kind, string name, string? containerId) =>
            throw new InvalidOperationException("not used");

        public LabTarget? Find(string targetId) => null;

        public IReadOnlyList<LabTarget> All => [];

        public void Remove(string targetId)
        {
        }

        public Task<int> ReconcileAsync(CancellationToken ct) => Task.FromResult(0);
    }
}
