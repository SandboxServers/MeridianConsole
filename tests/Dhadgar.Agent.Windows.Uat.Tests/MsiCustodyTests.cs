using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Scenarios;
using Dhadgar.Agent.Windows.Uat.Service;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// Certificate custody after a failed MSI install (P23 security review, N2). The agent may have enrolled before the
/// install failed, and <c>AgentLegs</c> never runs on that path, so the certificate must reach the manifest and then
/// the real <see cref="CleanupEngine"/>. Fakes stand in for the service and the store; no test installs anything.
/// </summary>
public sealed class MsiCustodyTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-custody-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;

    public MsiCustodyTests()
    {
        _paths = new RunPaths(_repo, "run-1");
        Directory.CreateDirectory(_paths.RunDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    [Fact]
    public async Task AfterAFailedInstall_TheAgentCertificateIsInTheManifestAndStopsTheService()
    {
        var manifest = new CleanupManifest(_paths.Manifest);
        var service = new FakeService();
        var store = new FakeStore(new StoreCert(
            "CN=agent", "AB12", "CD34", [], DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30), false, null));
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        await MsiLegs.RecordCustodyAfterFailedInstallAsync(service, store, manifest, evidence);

        Assert.Equal(CancellationToken.None, service.StopToken);
        var loaded = CleanupManifest.Load(_paths.Manifest);
        Assert.True(loaded.IsSuccess, loaded.Error);
        var certificate = Assert.Single(loaded.Value.OfType<CertificateEntry>());
        Assert.Equal("AB12", certificate.ThumbprintSha1);
        Assert.Empty(MsiLegs.EvidenceWarnings(evidence));
    }

    [Fact]
    public async Task AfterAFailedInstall_TheCleanupEngineRemovesTheCertificate()
    {
        var manifest = new CleanupManifest(_paths.Manifest);
        var store = new FakeStore(new StoreCert(
            "CN=agent", "AB12", "CD34", [], DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30), false, null));

        await MsiLegs.RecordCustodyAfterFailedInstallAsync(new FakeService(), store, manifest, new Dictionary<string, object?>(StringComparer.Ordinal));
        var entries = CleanupManifest.Load(_paths.Manifest);
        Assert.True(entries.IsSuccess, entries.Error);

        var actions = new RecordingActions();
        var report = await new CleanupEngine(actions, _paths).UndoAsync(entries.Value, CancellationToken.None);

        Assert.Empty(report.Failed);
        Assert.Contains(actions.Calls, call => call.StartsWith("RemoveCertificate:", StringComparison.Ordinal) && call.EndsWith("\\AB12", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedStop_IsAWarningNotAnException()
    {
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);
        var service = new FakeService { StopResult = Result.Failure("sc stop timed out") };

        await MsiLegs.RecordCustodyAfterFailedInstallAsync(
            service, new FakeStore(), new CleanupManifest(_paths.Manifest), evidence);

        var warnings = MsiLegs.EvidenceWarnings(evidence);
        Assert.Contains(warnings, w => w.Contains("sc stop timed out", StringComparison.Ordinal));
    }

    private sealed class FakeService : IServiceControl
    {
        public CancellationToken? StopToken { get; private set; }

        public Result StopResult { get; init; } = Result.Success();

        public Task<Result> Create(string exePath) => Task.FromResult(Result.Success());

        public Task<Result<string>> Query() => Task.FromResult(Result<string>.Failure("not used"));

        public Task<Result<ServiceConfig>> QueryConfig() => Task.FromResult(Result<ServiceConfig>.Failure("not used"));

        public Task<Result> StartAsync(CancellationToken ct) => Task.FromResult(Result.Success());

        public Task<Result> StopAsync(CancellationToken ct)
        {
            StopToken = ct;
            return Task.FromResult(StopResult);
        }
    }

    private sealed class FakeStore(params StoreCert[] mine) : IStoreInspector
    {
        public IReadOnlyList<StoreCert> AgentCertificatesInMy() => mine;

        public IReadOnlyList<StoreCert> CaInAgentStore() => [];

        public IReadOnlyList<StoreCert> AgentCasInRoot() => [];

        public bool AgentCertificateChainsToAgentCa() => false;

        public Result<StateFileAclInfo> StateFileAcl(string path) => Result<StateFileAclInfo>.Failure("not used");
    }
}
