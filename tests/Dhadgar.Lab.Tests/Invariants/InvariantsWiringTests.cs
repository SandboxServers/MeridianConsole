using System.Buffers.Text;
using System.Security.Cryptography;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Invariants;

/// <summary>
/// L21 step 5: I1 to I6 are registered through the Invariants area, I4 is the raw-line observer and the tool is classified and live.
/// </summary>
[Collection("LabHome")]
public sealed class InvariantsWiringTests
{
    [Fact]
    public async Task Daemon_RegistersI1ToI6_AndI4IsTheRawLineObserver()
    {
        string root = Path.Combine(Path.GetTempPath(), "lab-inv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
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
                Time: new FakeTimeProvider(DateTimeOffset.UnixEpoch)), CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);

            await using LabDaemon daemon = started.Value;
            string[] ids = daemon.Services.GetServices<IInvariant>().Select(i => i.Id).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(["I1", "I2", "I3", "I4", "I5", "I6", "I7"], ids);
            Assert.IsType<I4SecretLeakInvariant>(daemon.Services.GetRequiredService<IRawLineObserver>());
            Assert.Contains(daemon.Services.GetServices<ILabTool>(), t => t.Name == "invariants_check");
            Assert.Equal(LabGate.Open, ToolPolicy.GateFor("invariants_check"));
            Assert.DoesNotContain("invariants_check", ToolPolicy.PlannedTools);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
