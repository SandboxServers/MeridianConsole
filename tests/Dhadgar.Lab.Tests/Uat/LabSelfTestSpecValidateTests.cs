using System.Buffers.Text;
using System.Security.Cryptography;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat.Spec;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat;

/// <summary>
/// The shipped lab self-test spec loads and passes the plan-time validator against the daemon's real tool registry, so S-L6
/// is not refused by the validator before any step runs.
/// </summary>
[Collection("LabHome")]
public sealed class LabSelfTestSpecValidateTests
{
    [Fact]
    public async Task ShippedLabSelfTestSpec_LoadsAndValidatesWithoutProblems()
    {
        string root = Path.Combine(Path.GetTempPath(), "lab-selftest-spec-" + Guid.NewGuid().ToString("N"));
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
            LabToolRegistry registry = daemon.Services.GetRequiredService<LabToolRegistry>();

            string path = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "docs", "guides", "uat-specs", "lab-selftest.toml");
            string text = await File.ReadAllTextAsync(path, CancellationToken.None);

            SpecLoadOutcome loaded = SpecLoader.LoadText("lab-selftest.toml", text);
            Assert.True(loaded.Problems.Count == 0, string.Join("; ", loaded.Problems.Select(p => $"line {p.Line} {p.Message}")));
            Assert.NotNull(loaded.Spec);

            IReadOnlyList<UatProblem> problems = SpecValidator.Validate(loaded.Spec!, text, registry);
            Assert.True(problems.Count == 0, string.Join("; ", problems.Select(p => $"line {p.Line} {p.Message}")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
