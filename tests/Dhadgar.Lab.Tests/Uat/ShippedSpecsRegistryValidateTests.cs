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
/// A6: every shipped UAT spec (<c>lab-selftest.toml</c> and <c>catalog/*.toml</c>) loads and passes the plan-time validator
/// against the daemon's real tool registry. <c>known-issues.toml</c>, <c>invariants/</c>, <c>chaos/</c>, <c>twins/</c> and the
/// catalog scenario files <c>enroll-history.toml</c> and <c>soak.toml</c> are not specs and are skipped. A failure is a finding to report, never an expected value to edit.
/// </summary>
[Collection("LabHome")]
public sealed class ShippedSpecsRegistryValidateTests
{
    [Fact]
    public async Task ShippedSpecs_LoadAndValidateAgainstTheRealRegistry()
    {
        string root = Path.Combine(Path.GetTempPath(), "lab-shipped-specs-" + Guid.NewGuid().ToString("N"));
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

            string specs = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "docs", "guides", "uat-specs");
            var files = new List<string> { Path.Combine(specs, "lab-selftest.toml") };
            string catalog = Path.Combine(specs, "catalog");
            if (Directory.Exists(catalog))
            {
                // enroll-history.toml and soak.toml are scenario files read by their own tools, not uat_run specs (F65).
                files.AddRange(Directory.EnumerateFiles(catalog, "*.toml", SearchOption.TopDirectoryOnly)
                    .Where(f => Path.GetFileName(f) is not ("enroll-history.toml" or "soak.toml"))
                    .Order(StringComparer.Ordinal));
            }

            var failures = new List<string>();
            foreach (string file in files)
            {
                string name = Path.GetRelativePath(specs, file).Replace(Path.DirectorySeparatorChar, '/');
                string text = await File.ReadAllTextAsync(file, CancellationToken.None);
                SpecLoadOutcome loaded = SpecLoader.LoadText(name, text);
                failures.AddRange(loaded.Problems.Select(p => $"{name} line {p.Line} {p.Message}"));
                if (loaded.Spec is null)
                {
                    continue;
                }

                failures.AddRange(SpecValidator.Validate(loaded.Spec, text, registry).Select(p => $"{name} line {p.Line} {p.Message}"));
            }

            Assert.True(files.Count > 1, "no catalog specs found under " + catalog);
            Assert.True(failures.Count == 0, string.Join("; ", failures));
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
