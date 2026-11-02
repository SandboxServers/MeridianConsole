using System.Buffers.Text;
using System.Security.Cryptography;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Pool;
using Dhadgar.Lab.Tools.Cli;
using Dhadgar.Lab.Uat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Pool;

/// <summary>
/// D-ML173: the daemon wires <see cref="CliMemberPreparer"/> as the member preparer, and <c>uat_run</c> uses it.
/// </summary>
[Collection("LabHome")]
public sealed class MemberPreparerWiringTests
{
    [Fact]
    public async Task Daemon_RegistersCliMemberPreparer_AndUatRunnerPreparesMembers()
    {
        string root = Path.Combine(Path.GetTempPath(), "lab-prep-" + Guid.NewGuid().ToString("N"));
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
            Assert.IsType<CliMemberPreparer>(daemon.Services.GetRequiredService<IPoolMemberPreparer>());
            Assert.True(daemon.Services.GetRequiredService<UatRunner>().PreparesMembers);
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
