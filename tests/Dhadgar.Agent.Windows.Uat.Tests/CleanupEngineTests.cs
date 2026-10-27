using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class CleanupEngineTests
{
    private const string RunId = "20261027-142233-9f3a";
    private static readonly string Repo = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "uat-engine"));
    private readonly RunPaths _paths = new(Repo, RunId);

    private static readonly CleanupEntry[] Sample =
    [
        new RegistryKeyEntry("HKLM", @"SOFTWARE\Meridian Console\Agent", false),
        new CertificateEntry("LocalMachine", "Root", "AAA111"),
        new RegistryValueEntry(@"SOFTWARE\Meridian Console\Agent", "EnrollmentToken", false),
    ];

    [Fact]
    public async Task UndoAsync_ReverseOfAppendOrder_UndoesNewestFirst()
    {
        var actions = new RecordingActions();

        await new CleanupEngine(actions, _paths).UndoAsync(Sample, CancellationToken.None);

        Assert.Equal(
            [
                @"DeleteRegistryValue:SOFTWARE\Meridian Console\Agent\EnrollmentToken",
                "RemoveCertificate:LocalMachine\\Root\\AAA111",
                @"DeleteRegistryKey:HKLM\SOFTWARE\Meridian Console\Agent",
            ],
            actions.Calls);
    }

    [Fact]
    public async Task UndoAsync_OneFailure_StillUndoesOthersAndReportsIt()
    {
        var actions = new RecordingActions();
        actions.FailOn.Add("RemoveCertificate:LocalMachine\\Root\\AAA111");

        var report = await new CleanupEngine(actions, _paths).UndoAsync(Sample, CancellationToken.None);

        Assert.Equal(2, report.Undone);
        var failure = Assert.Single(report.Failed);
        Assert.Equal("certificate", failure.Kind);
        Assert.Equal("AAA111", failure.Identity);
    }

    [Fact]
    public async Task UndoAsync_RunTwice_GivesSameCallsAndSuccess()
    {
        var actions = new RecordingActions();
        var engine = new CleanupEngine(actions, _paths);

        var first = await engine.UndoAsync(Sample, CancellationToken.None);
        var firstCalls = actions.Calls.ToList();
        actions.Calls.Clear();
        var second = await engine.UndoAsync(Sample, CancellationToken.None);

        Assert.Empty(first.Failed);
        Assert.Empty(second.Failed);
        Assert.Equal(firstCalls, actions.Calls);
    }

    [Fact]
    public async Task UndoAsync_ExistedBeforeTrue_NeverDeletes()
    {
        var actions = new RecordingActions();
        CleanupEntry[] entries =
        [
            new RegistryKeyEntry("HKLM", @"SOFTWARE\Preexisting", true),
            new CertStoreEntry("MeridianConsoleAgent", true),
        ];

        var report = await new CleanupEngine(actions, _paths).UndoAsync(entries, CancellationToken.None);

        Assert.Empty(actions.Calls);
        Assert.Equal(0, report.Undone);
    }

    [Fact]
    public async Task UndoAsync_ForeignServiceName_IsRefusedAndNotCalled()
    {
        var actions = new RecordingActions();

        var report = await new CleanupEngine(actions, _paths).UndoAsync([new ServiceEntry("Spooler")], CancellationToken.None);

        Assert.Empty(actions.Calls);
        Assert.Single(report.Failed);
    }

    [Fact]
    public async Task UndoAsync_RegistryKeyOutsideAgentKey_IsRefused()
    {
        var actions = new RecordingActions();

        var report = await new CleanupEngine(actions, _paths)
            .UndoAsync([new RegistryKeyEntry("HKLM", @"SOFTWARE\Microsoft\Windows NT", false)], CancellationToken.None);

        Assert.Empty(actions.Calls);
        Assert.Single(report.Failed);
    }

    [Fact]
    public async Task UndoAsync_ContainerWithoutUatPrefix_IsRefused()
    {
        var actions = new RecordingActions();

        var report = await new CleanupEngine(actions, _paths).UndoAsync([new ContainerEntry("-v")], CancellationToken.None);

        Assert.Empty(actions.Calls);
        Assert.Single(report.Failed);
    }

    [Fact]
    public async Task UndoAsync_ActionThrows_StillUndoesOthersAndReportsIt()
    {
        var actions = new RecordingActions();
        actions.ThrowOn.Add(@"DeleteRegistryValue:SOFTWARE\Meridian Console\Agent\EnrollmentToken");

        var report = await new CleanupEngine(actions, _paths).UndoAsync(Sample, CancellationToken.None);

        Assert.Equal(2, report.Undone);
        var failure = Assert.Single(report.Failed);
        Assert.Equal("registryValue", failure.Kind);
        Assert.Contains("IOException", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UndoAsync_DirectoryOutsideRunFolder_IsRefused()
    {
        var actions = new RecordingActions();
        CleanupEntry[] entries = [new DirectoryEntry(@"C:\Windows\System32", false), new FileEntry(@"C:\Windows\win.ini")];

        var report = await new CleanupEngine(actions, _paths).UndoAsync(entries, CancellationToken.None);

        Assert.DoesNotContain(actions.Calls, call => call.StartsWith("DeleteDirectory:", StringComparison.Ordinal));
        Assert.DoesNotContain(actions.Calls, call => call.StartsWith("DeleteFile:", StringComparison.Ordinal));
        Assert.Equal(2, report.Failed.Count);
    }

    [Fact]
    public async Task UndoAsync_DirectoryInsideRunFolder_IsDeleted()
    {
        var actions = new RecordingActions();
        var path = Path.Combine(_paths.RunDir, "agent");

        await new CleanupEngine(actions, _paths).UndoAsync([new DirectoryEntry(path, false)], CancellationToken.None);

        Assert.Equal([$"DeleteDirectory:{path}"], actions.Calls);
    }

    [Fact]
    public async Task UndoAsync_ServiceAndProcess_AreQuiescedBeforeAnyDelete()
    {
        var actions = new RecordingActions();
        CleanupEntry[] entries =
        [
            new ServiceEntry("DhadgarAgent"),
            new RegistryKeyEntry("HKLM", @"SOFTWARE\Meridian Console\Agent", false),
            new ProcessEntry(1, DateTime.UtcNow, "agent"),
        ];

        await new CleanupEngine(actions, _paths).UndoAsync(entries, CancellationToken.None);

        Assert.Equal("KillProcess:1", actions.Calls[0]);
        Assert.Equal("StopService:DhadgarAgent", actions.Calls[1]);
        var firstDelete = actions.Calls.FindIndex(call => call.StartsWith("Delete", StringComparison.Ordinal));
        Assert.True(firstDelete > 1);
    }
}
