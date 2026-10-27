using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Service;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// The seam between the P22 producers and the cleanup allowlist (R-CLEANUP, D-S238). Every entry the service-mode
/// producers append, including the two <c>LocalMachine\Root</c> certificates, is read back from the manifest and
/// pushed through the real <see cref="CleanupEngine"/>; none may be refused.
/// </summary>
public sealed class ServiceCleanupEntriesTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-svc-entries-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;

    public ServiceCleanupEntriesTests()
    {
        _paths = new RunPaths(_repo, "run-1");
        Directory.CreateDirectory(_paths.AgentPublish);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    [SkippableFact]
    public async Task UndoAsync_EveryServiceModeEntry_IsUndoneWithoutFailure()
    {
        Skip.IfNot(OperatingSystem.IsWindows());

        var manifest = new CleanupManifest(_paths.Manifest);
        var actions = new RecordingActions();
        var redactor = new Redactor();
        var exe = Path.Combine(_paths.AgentPublish, "Dhadgar.Agent.Windows.exe");

        var tls = UatTlsCertificate.Create(_paths, "run-1", manifest, redactor, actions);
        var decoy = DecoyCa.Plant("run-1", manifest, actions);
        var created = await new ServiceControl(_paths, manifest, (_, _, _) => Task.FromResult(
            Dhadgar.Shared.Results.Result<CommandOutput>.Success(new CommandOutput(0, string.Empty, string.Empty))),
            TimeSpan.Zero).Create(exe);

        Assert.True(tls.IsSuccess);
        Assert.True(decoy.IsSuccess);
        Assert.True(created.IsSuccess);

        var entries = CleanupManifest.Load(_paths.Manifest).Value;
        var report = await new CleanupEngine(actions, _paths).UndoAsync(entries, CancellationToken.None);

        Assert.Empty(report.Failed);
        Assert.Equal(entries.Count, report.Undone);
        Assert.Contains(entries, e => e is CertificateEntry { StoreLocation: "LocalMachine", StoreName: "Root" });
        Assert.Contains(entries, e => e is ServiceEntry { Name: AgentWindowsFacts.ServiceName });
    }

    [SkippableFact]
    public void RemoveMatchedCertificate_TlsLeafAndDecoy_AreRemoved()
    {
        // The second gate: the real removal check (IsDriverCertificate) must accept what P22 adds to Root, not only the allowlist.
        Skip.IfNot(OperatingSystem.IsWindows());

        var manifest = new CleanupManifest(_paths.Manifest);
        var actions = new RecordingActions();
        var tls = UatTlsCertificate.Create(_paths, "run-1", manifest, new Redactor(), actions);
        var decoy = DecoyCa.Plant("run-1", manifest, actions);

        Assert.True(tls.IsSuccess);
        Assert.True(decoy.IsSuccess);
        Assert.Equal(2, actions.Added.Count);

        var removed = new List<string>();
        foreach (var entry in actions.Added)
        {
            var result = WindowsMachineActions.RemoveMatchedCertificate(entry.Certificate, "Root", false, c => removed.Add(c.Thumbprint));
            Assert.True(result.IsSuccess, result.Error);
        }

        Assert.Equal(2, removed.Count);
        Assert.Contains(removed, t => string.Equals(t, tls.Value.ThumbprintSha1, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(removed, t => string.Equals(t, decoy.Value, StringComparison.OrdinalIgnoreCase));
    }
}
