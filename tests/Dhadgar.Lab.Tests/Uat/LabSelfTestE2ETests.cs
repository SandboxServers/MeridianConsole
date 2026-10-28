using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Dhadgar.Lab.Tests.Uat;

/// <summary>
/// S-L6: the daemon runs in process on an ephemeral loopback port with a temp data root and the real repo root. The test takes a
/// lease, builds the repo through <c>lab_build</c>, runs <c>docs/guides/uat-specs/lab-selftest.toml</c> through <c>uat_run</c>, and
/// checks that every row passes, the bundle's <c>secret_scan</c> is <c>passed</c>, and <c>ledger.md</c> exists. Needs the Linux
/// Docker engine. The spec's own rows bring the <c>meridian-lab-*</c> stack up and down; the test only removes its temp data root.
/// </summary>
/// <remarks>
/// The CLI's loopback gate only accepts the fixed lab ports, so this test calls <c>/mcp</c> on the daemon's own base address.
/// </remarks>
[Collection("LabHome")]
public sealed class LabSelfTestE2ETests
{
    private const string SelfTestSpec = "lab-selftest.toml";

    [Fact(Timeout = 900_000)]
    [Trait("Seam", "S-L6")]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task LabSelfTest_EveryRowPasses_SecretScanPasses_LedgerWritten()
    {
        string repoRoot = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        string home = Path.Combine(Path.GetTempPath(), "meridian-lab-s6-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(home);
        try
        {
            string token = NewToken();
            var options = new LabDaemonOptions(
                Port: 0,
                Token: token,
                DataRoot: home,
                EnforceSingleInstance: false,
                RefuseElevated: false,
                RepoRoot: repoRoot);

            var started = await LabDaemon.StartAsync(options, CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);
            await using var daemon = started.Value;

            using var lease = await CallToolAsync(
                daemon.BaseAddress,
                token,
                "lab_lease_acquire",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["owner"] = "s6-selftest", ["purpose"] = "S-L6 self-test" });
            string leaseId = lease.RootElement.GetProperty("lease_id").GetString() ?? string.Empty;
            Assert.NotEmpty(leaseId);

            try
            {
                using var build = await CallToolAsync(
                    daemon.BaseAddress,
                    token,
                    "lab_build",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["lease_id"] = leaseId });

                using var run = await CallToolAsync(
                    daemon.BaseAddress,
                    token,
                    "uat_run",
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["spec"] = SelfTestSpec,
                        ["specs_dir"] = Path.Combine(repoRoot, "docs", "guides", "uat-specs"),
                        ["lease_id"] = leaseId,
                    });
                AssertRunPasses(run.RootElement, home, repoRoot);
            }
            finally
            {
                using var released = await CallToolAsync(
                    daemon.BaseAddress,
                    token,
                    "lab_lease_release",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["lease_id"] = leaseId });
            }
        }
        finally
        {
            if (Directory.Exists(home))
            {
                Directory.Delete(home, recursive: true);
            }
        }
    }

    /// <summary>
    /// Checks the <c>uat_run</c> result: overall pass, every row graded pass, the bundle's secret scan passed, and ledger.md written.
    /// </summary>
    private static void AssertRunPasses(JsonElement root, string home, string repoRoot)
    {
        Assert.Equal("pass", root.GetProperty("overall").GetString());
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            Assert.Equal("pass", row.GetProperty("grade").GetString());
        }

        string runId = root.GetProperty("run_id").GetString() ?? string.Empty;
        string bundle = Path.Combine(LabPaths.FromRoot(home, repoRoot).UatRuns, Path.GetFileName(runId));
        Assert.True(Directory.Exists(bundle), bundle);

        using var runJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "run.json")));
        Assert.Equal("passed", runJson.RootElement.GetProperty("secret_scan").GetString());
        Assert.True(File.Exists(Path.Combine(bundle, "ledger.md")));
    }

    /// <summary>
    /// Calls one MCP tool on the in-process daemon and returns its JSON text. Throws on a tool error so the test fails with the text.
    /// </summary>
    private static async Task<JsonDocument> CallToolAsync(Uri baseAddress, string token, string toolName, Dictionary<string, object?> arguments)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(baseAddress, LabDefaults.McpPath.TrimStart('/')),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
        };
        await using var transport = new HttpClientTransport(transportOptions, http);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);
        var result = await client.CallToolAsync(toolName, arguments, cancellationToken: CancellationToken.None);
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        if (result.IsError == true)
        {
            throw new InvalidOperationException(toolName + " failed: " + text);
        }

        return JsonDocument.Parse(text);
    }

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
        try
        {
            return Base64Url.EncodeToString(bytes);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }
}
