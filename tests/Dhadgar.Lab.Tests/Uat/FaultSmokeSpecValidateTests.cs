using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat.Spec;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat;

/// <summary>
/// The fault-smoke spec loads and validates against the daemon's real tool registry, and keeps the D-ML83 shape.
/// </summary>
[Collection("LabHome")]
public sealed class FaultSmokeSpecValidateTests
{
    [Fact]
    public async Task FaultSmokeSpec_LoadsValidates_AndChecksTheDbPathNotReadiness()
    {
        string root = Path.Combine(Path.GetTempPath(), "fault-smoke-spec-" + Guid.NewGuid().ToString("N"));
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

            string path = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "tests", "Dhadgar.Lab.Tests", "Uat", "Specs", "fault-smoke.toml");
            string text = await File.ReadAllTextAsync(path, CancellationToken.None);

            SpecLoadOutcome loaded = SpecLoader.LoadText("fault-smoke.toml", text);
            Assert.True(loaded.Problems.Count == 0, string.Join("; ", loaded.Problems.Select(p => $"line {p.Line} {p.Message}")));
            Assert.NotNull(loaded.Spec);

            IReadOnlyList<UatProblem> problems = SpecValidator.Validate(loaded.Spec!, text, registry);
            Assert.True(problems.Count == 0, string.Join("; ", problems.Select(p => $"line {p.Line} {p.Message}")));

            UatRow row = Assert.Single(loaded.Spec!.Rows);
            Assert.Equal("pg-down", row.Id);
            Assert.Empty(loaded.Spec.Section.AllowInvariants);
            Assert.Equal(new[] { "I1" }, row.AllowInvariants);
            Assert.DoesNotContain(row.Expect, c => c.Op == "ne" && c.SourceFields.TryGetValue("path", out JsonElement p) && p.GetString() == "/readyz");
            Assert.Contains(row.Expect, c => c.Required && c.Op == "gte" && c.Value is { } v && v.GetInt32() == 500
                && c.SourceFields.TryGetValue("step", out JsonElement s) && s.GetString() == "enroll-down");
            Assert.Contains(row.Expect, c => c.Required && c.Op == "eq" && c.Value is { } v && v.GetInt32() == 201
                && c.SourceFields.TryGetValue("step", out JsonElement s) && s.GetString() == "enroll-after");
            UatFault fault = Assert.Single(row.Faults);
            Assert.Equal("after_step:mint", fault.Start);
            Assert.Equal("after_step:enroll-down", fault.Heal);
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
