using System.Buffers.Text;
using System.Security.Cryptography;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Tools.Faults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// The L20 kinds and tools as the daemon registers them (R-REG): every kind is registered once, every tool is Leased and a
/// fault tool, and reconcile on a daemon with no registered lab targets touches nothing.
/// </summary>
[Collection("LabHome")]
public sealed class ReconcileTests
{
    private static readonly string[] NewKinds = ["kill", "pause", "disk_fill", "corrupt_file", "db"];
    private static readonly string[] NewTools = ["fault_kill", "fault_pause", "fault_disk_fill", "fault_corrupt_file", "fault_db"];

    [Fact]
    public async Task Daemon_RegistersEachNewKindOnce()
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);
            List<string> kinds = daemon.Services.GetServices<IFaultKind>().Select(k => k.Kind).ToList();

            foreach (string kind in NewKinds)
            {
                Assert.Single(kinds, kind);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Daemon_RegistersEachNewToolAsALeasedFaultTool()
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);
            ILabTool[] tools = daemon.Services.GetServices<ILabTool>().ToArray();

            foreach (string name in NewTools)
            {
                ILabTool tool = Assert.Single(tools, t => t.Name == name);
                Assert.IsAssignableFrom<IFaultTool>(tool);
                Assert.Equal(LabGate.Leased, ToolPolicy.GateFor(name));
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Reconcile_NoLabTargets_TouchesNothing()
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);

            foreach (IFaultKind kind in daemon.Services.GetServices<IFaultKind>().Where(k => NewKinds.Contains(k.Kind)))
            {
                var result = await kind.ReconcileAsync(CancellationToken.None);
                Assert.True(result.IsSuccess, $"{kind.Kind}: {result.Error}");
                Assert.Equal(0, result.Value);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string NewRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), "lab-l20-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static async Task<LabDaemon> StartDaemonAsync(string root)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
        string token = Base64Url.EncodeToString(bytes);
        Array.Clear(bytes);

        var started = await LabDaemon.StartAsync(new LabDaemonOptions(
            Port: 0,
            Token: token,
            DataRoot: root,
            EnforceSingleInstance: false,
            RefuseElevated: false,
            Time: new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero))), CancellationToken.None);

        Assert.True(started.IsSuccess, started.Error);
        return started.Value;
    }
}
