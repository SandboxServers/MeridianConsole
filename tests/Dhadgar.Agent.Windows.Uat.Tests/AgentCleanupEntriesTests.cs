using System.Security.Cryptography;
using Dhadgar.Agent.Windows.Uat.Agent;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Probe;
using Dhadgar.Agent.Windows.Uat.Scenarios;
using Dhadgar.Contracts.Nodes;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>
/// The seam between the legs' manifest entries and the cleanup allowlist (D-S238). Every entry a process-mode run
/// appends comes from a producer list and goes through the real <see cref="CleanupEngine"/>.
/// </summary>
public class AgentCleanupEntriesTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "uat-entries-" + Guid.NewGuid().ToString("N"));
    private readonly RunPaths _paths;

    public AgentCleanupEntriesTests()
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

    [Fact]
    public async Task UndoAsync_EveryEntryP20Appends_IsUndoneWithoutFailure()
    {
        var start1 = DateTime.UtcNow.AddMinutes(-5);
        var start2 = DateTime.UtcNow.AddMinutes(-1);
        var sha1 = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
        var certificate = new StoreCert(
            "CN=agent", sha1, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), [AgentCertificateProfile.SpiffeId(Guid.NewGuid())],
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30), true,
            new CngKeyInfo($"{Guid.NewGuid():N}_{Guid.NewGuid():D}", "Microsoft Software Key Storage Provider", true, true));

        var entries = new List<CleanupEntry>();
        entries.AddRange(RegistryToken.ManifestEntries(parentExisted: false, agentExisted: false, valueExisted: false)); // U2
        entries.AddRange(AgentProcess.FirstStartEntries(eventSourceExisted: false, agentDirectoryExisted: false));       // U3 first start
        entries.Add(new ProcessEntry(4242, start1, "agent"));
        entries.AddRange(AgentLegs.CertificateEntries(certificate));                                                      // U3 poll
        entries.AddRange(RegistryToken.ManifestEntries(parentExisted: true, agentExisted: true, valueExisted: false));   // U4 token T2
        entries.Add(new ProcessEntry(4243, start2, "agent"));                                                              // U4 restart

        var actions = new RecordingActions();
        var agentImage = Path.Combine(_paths.AgentPublish, AgentProcess.ExecutableName);
        actions.ProcessImages[4242] = agentImage;
        actions.ProcessImages[4243] = agentImage;

        var report = await new CleanupEngine(actions, _paths).UndoAsync(entries, CancellationToken.None);

        Assert.Empty(report.Failed);
        Assert.Equal(10, report.Undone);
        Assert.DoesNotContain(entries, e => e is CngKeyEntry);

        var tokenValue = $@"DeleteRegistryValue:{AgentWindowsFacts.RegistryKeyPath}\{AgentWindowsFacts.EnrollmentTokenValueName}";
        Assert.Equal(
            [
                "KillProcess:4243",
                "KillProcess:4242",
                "KillProcess:4243",
                tokenValue,
                $@"RemoveCertificate:LocalMachine\My\{sha1}",
                "KillProcess:4242",
                $"DeleteDirectory:{AgentDirectories.MachineAgentDirectory}",
                $"UnregisterCertStore:{AgentWindowsFacts.CaStoreName}",
                $"DeleteEventSource:{AgentWindowsFacts.EventLogSourceName}",
                tokenValue,
                $@"DeleteRegistryKey:HKLM\{AgentWindowsFacts.RegistryKeyPath}",
                $@"DeleteEmptyRegistryKey:HKLM\{AgentWindowsFacts.RegistryParentKeyPath}",
            ],
            actions.Calls);
    }
}
