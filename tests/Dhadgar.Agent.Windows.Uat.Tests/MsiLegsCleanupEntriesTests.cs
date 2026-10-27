using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Scenarios;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// The seam between the MSI leg's pre-install entries and the cleanup allowlist (P23). Every entry goes through the
/// real <see cref="CleanupEngine"/> with <see cref="RecordingActions"/> as the machine.
/// </summary>
public class MsiLegsCleanupEntriesTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-msi-entries-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;

    public MsiLegsCleanupEntriesTests()
    {
        _paths = new RunPaths(_repo, "run-1");
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    private string MsiPath => Path.Combine(_paths.RunDir, "msi", "Dhadgar.Agent.Windows.msi");

    [Fact]
    public void PreInstallEntries_AreInTheSpecOrder()
    {
        var entries = MsiLegs.PreInstallEntries(MsiPath, false, false, false, false, false);

        Assert.Collection(
            entries,
            e => Assert.IsType<MsiProductEntry>(e),
            e => Assert.IsType<ServiceEntry>(e),
            e => Assert.Equal(AgentWindowsFacts.RegistryParentKeyPath, Assert.IsType<RegistryKeyEntry>(e).Path),
            e => Assert.Equal(AgentWindowsFacts.RegistryKeyPath, Assert.IsType<RegistryKeyEntry>(e).Path),
            e => Assert.IsType<EventSourceEntry>(e),
            e => Assert.IsType<CertStoreEntry>(e),
            e => Assert.Equal(AgentDirectories.MachineAgentDirectory, Assert.IsType<DirectoryEntry>(e).Path),
            e => Assert.Equal(AgentDirectories.MachineProgramFilesDirectory, Assert.IsType<DirectoryEntry>(e).Path));
    }

    [Fact]
    public async Task UndoAsync_EveryPreInstallEntry_IsUndoneWithoutFailure()
    {
        var entries = MsiLegs.PreInstallEntries(MsiPath, false, false, false, false, false);
        var actions = new RecordingActions();

        var report = await new CleanupEngine(actions, _paths).UndoAsync(entries, CancellationToken.None);

        Assert.Empty(report.Failed);
        Assert.Equal(8, report.Undone);
        Assert.Contains($"UninstallMsiProduct:{AgentWindowsFacts.MsiUpgradeCode}", actions.Calls);
        Assert.Contains($"DeleteDirectory:{AgentDirectories.MachineProgramFilesDirectory}", actions.Calls);
        Assert.Contains($"DeleteDirectory:{AgentDirectories.MachineAgentDirectory}", actions.Calls);
    }

    [Fact]
    public async Task UndoAsync_ExistedBeforeTrue_LeavesTheItemAlone()
    {
        var entries = MsiLegs.PreInstallEntries(MsiPath, true, true, true, true, true);
        var actions = new RecordingActions();

        var report = await new CleanupEngine(actions, _paths).UndoAsync(entries, CancellationToken.None);

        Assert.Empty(report.Failed);
        Assert.DoesNotContain(actions.Calls, c => c.StartsWith("DeleteDirectory:", StringComparison.Ordinal));
        Assert.DoesNotContain(actions.Calls, c => c.StartsWith("DeleteEventSource:", StringComparison.Ordinal));
    }
}
