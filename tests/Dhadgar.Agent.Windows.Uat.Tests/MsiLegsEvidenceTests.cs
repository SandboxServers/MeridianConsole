using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Msi;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Scenarios;
using Dhadgar.Agent.Windows.Uat.Service;
using Dhadgar.Agent.Windows.Uat.Stack;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// The MSI legs' warnings (P23 N4): the install's 3010 text and the uninstall's residue reach the evidence
/// <c>warnings</c> list, so the results and the cleanup pass see them. The installer, service, database and store are
/// fakes; the registry and directory reads are read-only.
/// </summary>
public sealed class MsiLegsEvidenceTests
{
    private static readonly Uri Endpoint = new("https://localhost:5443/");

    // The legs read the real HKLM registry and the event log (EventLog.SourceExists), which only Windows has.
    private const string WindowsOnlyReason =
        "Windows-only test: the MSI legs read the real registry and event log, which do not exist on non-Windows runners";

    [SkippableFact]
    public async Task InstallAsync_A3010Warning_IsAddedToTheEvidenceWarnings()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsOnlyReason);
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);
        var installer = new FakeInstaller(new MsiOutcome(3010, "msiexec /i <msi>", ["reboot requested (3010)"]));

        // The check fails (nothing is installed here); the warning must still be recorded before it does.
        var result = await MsiLegs.InstallAsync(
            installer, new FakeService(), new FakeProducts(), "msi", Token(), Endpoint,
            TimeSpan.Zero, TimeSpan.Zero, evidence, CancellationToken.None);

        Assert.True(result.IsFailure);
        var warnings = Assert.IsType<List<string>>(evidence["warnings"]);
        Assert.Contains(warnings, w => w.Contains("reboot requested", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task UninstallAsync_ResidueAndUninstallWarnings_AreAddedToTheEvidenceWarnings()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsOnlyReason);
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);
        var installer = new FakeInstaller(new MsiOutcome(3010, "msiexec /x <product>", ["uninstall reboot requested"]));
        var store = new FakeStore(
            new StoreCert("CN=agent", "AB12", "CD34", [], DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30), false, null));

        var result = await MsiLegs.UninstallAsync(installer, new FakeService(), new FakeProducts(), store, "msi", evidence);

        Assert.True(result.IsSuccess, result.Error);
        var warnings = Assert.IsType<List<string>>(evidence["warnings"]);
        Assert.Contains(warnings, w => w.Contains("uninstall reboot requested", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("certificate:AB12", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task InstallAsync_AnExistingWarningsListIsReusedNotReplaced()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsOnlyReason);
        // RunStepsAsync reuses the list the MSI install wrote, so the warnings from both steps end up in one list.
        var existing = new List<string> { "earlier" };
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal) { ["warnings"] = existing };
        var installer = new FakeInstaller(new MsiOutcome(3010, "cmd", ["later"]));

        await MsiLegs.InstallAsync(
            installer, new FakeService(), new FakeProducts(), "msi", Token(), Endpoint,
            TimeSpan.Zero, TimeSpan.Zero, evidence, CancellationToken.None);

        Assert.Same(existing, evidence["warnings"]);
        Assert.Equal(["earlier", "later"], existing);
    }

    private static MintedToken Token() => new(Guid.NewGuid(), "tok-" + Guid.NewGuid().ToString("N"));

    private sealed class FakeInstaller(MsiOutcome outcome) : IMsiInstaller
    {
        public Result<MsiOutcome> Install(string msiPath, MintedToken token, Uri endpoint) => Result<MsiOutcome>.Success(outcome);

        public Result<MsiOutcome> Uninstall(string msiPath) => Result<MsiOutcome>.Success(outcome);
    }

    private sealed class FakeService : IServiceControl
    {
        public Task<Result> Create(string exePath) => Task.FromResult(Result.Success());

        public Task<Result<string>> Query() =>
            Task.FromResult(Result<string>.Failure("The specified service does not exist as an installed service."));

        public Task<Result<ServiceConfig>> QueryConfig() =>
            Task.FromResult(Result<ServiceConfig>.Failure("The specified service does not exist as an installed service."));

        public Task<Result> StartAsync(CancellationToken ct) => Task.FromResult(Result.Success());

        public Task<Result> StopAsync(CancellationToken ct) => Task.FromResult(Result.Success());
    }

    private sealed class FakeProducts : IMsiProducts
    {
        public Result<IReadOnlyList<string>> RelatedProducts(string upgradeCode) => Result<IReadOnlyList<string>>.Success([]);

        public Result<string> ProductVersion(string productCode) => Result<string>.Failure("not installed");
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
