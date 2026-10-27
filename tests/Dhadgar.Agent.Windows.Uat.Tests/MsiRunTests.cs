using System.Text;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Msi;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Results;
using Dhadgar.Agent.Windows.Uat.Scenarios;
using Dhadgar.Agent.Windows.Uat.Service;
using Dhadgar.Agent.Windows.Uat.Stack;
using Dhadgar.Shared.Results;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// The MSI run's wiring (P23): U10 without a service, the manifest-before-install order, the U9 scan of UTF-16 MSI logs,
/// and the <c>msi</c> object of the results. No test runs <c>msiexec</c>.
/// </summary>
public sealed class MsiRunTests : IDisposable
{
    private const string RunId = "20261027-142233-9f3a";
    private const string Token = "uat-enroll-token-0123456789abcdef";
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-msi-run-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;
    private readonly RecordingActions _actions = new();

    public MsiRunTests()
    {
        _paths = new RunPaths(_repo, RunId);
        Directory.CreateDirectory(_paths.Logs);
        Directory.CreateDirectory(_paths.AgentPublish);
        File.WriteAllText(Path.Combine(_paths.AgentPublish, "appsettings.json"), "{}");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    private string MsiPath => Path.Combine(_paths.Msi, "MeridianConsoleAgent.msi");

    [SkippableFact]
    public async Task RunTlsOnly_PlantsLeafAndDecoyOnly_AndLeavesAppSettingsAlone()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var manifest = new CleanupManifest(_paths.Manifest);
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await ServiceSetup.RunTlsOnlyAsync(
            _paths, RunId, manifest, new Redactor(), _actions, RootOf, evidence);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("pass", evidence["U10"]);
        Assert.Equal(result.Value.Tls.ThumbprintSha1, evidence["tlsThumbprint"]);
        Assert.False(evidence.ContainsKey("serviceBinaryPath"));
        Assert.Equal("{}", await File.ReadAllTextAsync(Path.Combine(_paths.AgentPublish, "appsettings.json")));
    }

    [SkippableFact]
    public async Task RunTlsOnly_EveryEntryItAppendsPassesTheRealCleanupEngine()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var manifest = new CleanupManifest(_paths.Manifest);
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);
        var setUp = await ServiceSetup.RunTlsOnlyAsync(_paths, RunId, manifest, new Redactor(), _actions, RootOf, evidence);
        Assert.True(setUp.IsSuccess, setUp.Error);
        var entries = CleanupManifest.Load(_paths.Manifest);
        Assert.True(entries.IsSuccess, entries.Error);

        var report = await new CleanupEngine(_actions, _paths).UndoAsync(entries.Value, CancellationToken.None);

        Assert.Empty(report.Failed);
        Assert.NotEmpty(entries.Value);
    }

    [SkippableFact]
    public async Task RunTlsOnly_Fails_WhenTheDecoyIsNotInRoot()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        var manifest = new CleanupManifest(_paths.Manifest);
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await ServiceSetup.RunTlsOnlyAsync(
            _paths, RunId, manifest, new Redactor(), _actions,
            () => Result<RootSnapshot>.Success(new RootSnapshot(_actions.Added
                .Where(a => a.Certificate.Subject.StartsWith("CN=localhost", StringComparison.Ordinal))
                .Select(a => a.Certificate.Thumbprint)
                .ToList())),
            evidence);

        Assert.True(result.IsFailure);
        Assert.Contains("decoy", result.Error!, StringComparison.Ordinal);
        Assert.Equal("fail", evidence["U10"]);
    }

    [Fact]
    public async Task PrepareAndInstall_RecordsTheManifestEntriesBeforeMsiexecIsCalled()
    {
        var manifest = new CleanupManifest(_paths.Manifest);
        var installer = new OrderCheckingInstaller(_paths.Manifest);
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await MsiLegs.PrepareAndInstallAsync(
            () => AppendPure(manifest),
            installer, new NoServiceControl(), new NoProducts(), MsiPath, new MintedToken(Guid.NewGuid(), Token),
            new Uri("https://localhost:8443/"), TimeSpan.Zero, TimeSpan.Zero, evidence, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, installer.InstallCalls);
        Assert.Equal(8, installer.EntriesAtInstall);
        Assert.IsType<MsiProductEntry>(installer.FirstEntryAtInstall);
        Assert.Equal("fail", evidence["U11a"]);
    }

    [Fact]
    public async Task PrepareAndInstall_FailedAppend_NeverCallsMsiexec()
    {
        var installer = new OrderCheckingInstaller(_paths.Manifest);
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = await MsiLegs.PrepareAndInstallAsync(
            () => Result.Failure("disk full"),
            installer, new NoServiceControl(), new NoProducts(), MsiPath, new MintedToken(Guid.NewGuid(), Token),
            new Uri("https://localhost:8443/"), TimeSpan.Zero, TimeSpan.Zero, evidence, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, installer.InstallCalls);
        Assert.Equal("fail", evidence["U11a"]);
    }

    [Theory]
    [InlineData("utf16")]
    [InlineData("utf8")]
    public void U9_TokenInAnMsiLog_FailsAndTheFileIsRewritten(string encoding)
    {
        var logPath = Path.Combine(_paths.Logs, "msi-install.log");
        var text = $"MSI (s) Property(S): ENROLLMENT_TOKEN = {Token}\r\n";
        File.WriteAllText(logPath, text, encoding == "utf16" ? new UnicodeEncoding(false, true) : new UTF8Encoding(false));
        var redactor = new Redactor();
        redactor.AddSecret(Token, SecretKind.EnrollmentToken);

        var step = UatRunner.CleanupAndScan(_paths, new CleanupSummary("clean", 1, [], [], [], []), redactor, []);

        Assert.Equal("fail", step.Status);
        Assert.Contains("msi-install.log line 1", step.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, File.ReadAllText(logPath), StringComparison.Ordinal);
    }

    [Fact]
    public void U9_CleanMsiLogs_Pass()
    {
        foreach (var name in new[] { "msi-install.log", "msi-uninstall.log", "msi-cleanup.log" })
        {
            File.WriteAllText(Path.Combine(_paths.Logs, name), "Property(S): ENROLLMENT_TOKEN = **********\r\n", new UnicodeEncoding(false, true));
        }

        var redactor = new Redactor();
        redactor.AddSecret(Token, SecretKind.EnrollmentToken);

        var step = UatRunner.CleanupAndScan(_paths, new CleanupSummary("clean", 1, [], [], [], []), redactor, []);

        Assert.Equal("pass", step.Status);
        Assert.Equal(3, step.Evidence["filesScanned"]);
    }

    [Fact]
    public void MsiInfoFor_ReadsTheEvidenceTheMsiLegsWrote()
    {
        var msi = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["productCode"] = "{11111111-2222-3333-4444-555555555555}",
            ["productVersion"] = "1.2.3",
            ["installExitCode"] = 0,
            ["uninstallExitCode"] = 3010,
            ["residue"] = new List<string> { "certStore:MeridianConsoleAgent" },
        };
        var u1 = new StepResult("U1", "msi", "pass", new Dictionary<string, object?> { ["msi"] = msi }, null, 1);

        var info = UatRunner.MsiInfoFor([u1]);

        Assert.Equal("{11111111-2222-3333-4444-555555555555}", info.ProductCode);
        Assert.Equal("1.2.3", info.ProductVersion);
        Assert.Equal(0, info.InstallExitCode);
        Assert.Equal(3010, info.UninstallExitCode);
        Assert.Equal(["certStore:MeridianConsoleAgent"], info.Residue);
    }

    [Fact]
    public void MsiInfoFor_NoEvidence_IsEmpty()
    {
        var info = UatRunner.MsiInfoFor([new StepResult("U1", "msi", "fail", new Dictionary<string, object?>(), "x", 1)]);

        Assert.Null(info.ProductCode);
        Assert.Equal(-1, info.InstallExitCode);
        Assert.Empty(info.Residue);
    }

    [Fact]
    public void Write_MsiObject_IsWrittenOnlyWhenPresent_AndMarkdownListsIt()
    {
        var step = new StepResult("U1", "msi", "pass", new Dictionary<string, object?>(), null, 5);
        var plain = Results(step);

        Assert.True(ResultsWriter.Write(plain, _repo, new Redactor()).IsSuccess);
        Assert.DoesNotContain("\"msi\": {", File.ReadAllText(Path.Combine(_repo, "results.json")), StringComparison.Ordinal);

        var withMsi = plain with
        {
            Msi = new MsiRunInfo("{AAAA}", "1.0.0", 0, 0, [@"directory:%ProgramData%\Meridian Console\Agent"]),
        };
        Assert.True(ResultsWriter.Write(withMsi, _repo, new Redactor()).IsSuccess);
        var json = File.ReadAllText(Path.Combine(_repo, "results.json"));
        Assert.Contains("\"productCode\": \"{AAAA}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"uninstallExitCode\": 0", json, StringComparison.Ordinal);
        Assert.Contains("\"residue\"", json, StringComparison.Ordinal);
        var markdown = File.ReadAllText(Path.Combine(_repo, "results.md"));
        Assert.Contains("- Residue: directory:", markdown, StringComparison.Ordinal);
        Assert.Contains("- Product code: {AAAA}; version: 1.0.0", markdown, StringComparison.Ordinal);
    }

    private Result<RootSnapshot> RootOf() =>
        Result<RootSnapshot>.Success(new RootSnapshot(_actions.Added.Select(a => a.Certificate.Thumbprint).ToList()));

    private Result AppendPure(CleanupManifest manifest)
    {
        foreach (var entry in MsiLegs.PreInstallEntries(MsiPath, false, false, false, false, false))
        {
            var appended = manifest.Append(entry);
            if (appended.IsFailure)
            {
                return appended;
            }
        }

        return Result.Success();
    }

    private UatResults Results(StepResult step)
    {
        Directory.CreateDirectory(_repo);
        return new UatResults(
            1, RunId, "run", "msi", DateTime.UtcNow, DateTime.UtcNow, "abc123", false, 22631, "10.0.0", "user", true, "pass", 0,
            [step], CleanupSummary.NotRun(), []);
    }

    /// <summary>Records how many manifest entries existed when <c>msiexec /i</c> was called, then fails the install.</summary>
    private sealed class OrderCheckingInstaller(string manifestPath) : IMsiInstaller
    {
        public int InstallCalls { get; private set; }

        public int EntriesAtInstall { get; private set; }

        public CleanupEntry? FirstEntryAtInstall { get; private set; }

        public Result<MsiOutcome> Install(string msiPath, MintedToken token, Uri endpoint)
        {
            InstallCalls++;
            var loaded = CleanupManifest.Load(manifestPath);
            EntriesAtInstall = loaded.IsSuccess ? loaded.Value.Count : -1;
            FirstEntryAtInstall = loaded.IsSuccess && loaded.Value.Count > 0 ? loaded.Value[0] : null;
            return Result<MsiOutcome>.Failure("stop after the order check");
        }

        public Result<MsiOutcome> Uninstall(string msiPath) => Result<MsiOutcome>.Failure("not used");
    }

    private sealed class NoServiceControl : IServiceControl
    {
        public Task<Result> Create(string exePath) => Task.FromResult(Result.Failure("not used"));

        public Task<Result<string>> Query() => Task.FromResult(Result<string>.Failure("not used"));

        public Task<Result<ServiceConfig>> QueryConfig() => Task.FromResult(Result<ServiceConfig>.Failure("not used"));

        public Task<Result> StartAsync(CancellationToken ct) => Task.FromResult(Result.Failure("not used"));

        public Task<Result> StopAsync(CancellationToken ct) => Task.FromResult(Result.Failure("not used"));
    }

    private sealed class NoProducts : IMsiProducts
    {
        public Result<IReadOnlyList<string>> RelatedProducts(string upgradeCode) =>
            Result<IReadOnlyList<string>>.Success([]);

        public Result<string> ProductVersion(string productCode) => Result<string>.Failure("not installed");
    }
}
