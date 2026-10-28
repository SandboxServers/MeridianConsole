using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Processes;
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
    private const string WipeEnvVar = "MERIDIAN_LAB_S6_WIPE";

    [SkippableFact(Timeout = 900_000)]
    [Trait("Seam", "S-L6")]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task LabSelfTest_EveryRowPasses_SecretScanPasses_LedgerWritten()
    {
        string context = Environment.GetEnvironmentVariable("Lab__Docker__LinuxContext") is { Length: > 0 } configured
            ? configured
            : ContainerLogFollower.DefaultLinuxContext;
        (int versionExit, _) = RunDocker(context, ["version", "--format", "{{.Server.Version}}"]);
        Skip.IfNot(versionExit == 0, "docker is not reachable on context " + context);

        // The guard runs before any daemon starts. Labelled containers are never removed here: stack_up wipe does that, and only with the opt-in.
        // Label AND name prefix, as the product's orphan wipe does (D-ML6). Docker name filters are regexes.
        (int psExit, string labelled) = RunDocker(context, ["ps", "-aq", "--filter", "label=" + LabDefaults.ContainerLabel + "=1", "--filter", "name=^meridian-lab-", "--format", "{{.ID}}"]);
        Assert.True(psExit == 0, "docker ps failed");
        if (labelled.Trim().Length > 0 && Environment.GetEnvironmentVariable(WipeEnvVar) != "1")
        {
            Assert.Fail("labelled lab containers exist; set MERIDIAN_LAB_S6_WIPE=1 to let the self-test's stack_up wipe remove them");
        }

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
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["checkout"] = repoRoot, ["lease_id"] = leaseId });

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
            RemoveLabContainers(context);
            if (Directory.Exists(home))
            {
                Directory.Delete(home, recursive: true);
            }
        }
    }

    /// <summary>
    /// Removes containers labelled <c>meridian-lab=1</c> (and their anonymous volumes). The daemon under test starts with an empty
    /// target registry, so its <c>stack_up wipe</c> cannot see containers left by an earlier run; those would keep the old
    /// Postgres roles and reject the new run's passwords. Only lab-labelled containers are touched.
    /// </summary>
    private static void RemoveLabContainers(string context)
    {
        (int _, string ids) = RunDocker(context, ["ps", "-aq", "--filter", "label=" + LabDefaults.ContainerLabel + "=1", "--filter", "name=^meridian-lab-", "--format", "{{.ID}}"]);
        foreach (string id in ids.Split((char)10, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            _ = RunDocker(context, ["rm", "-f", "-v", id]);
        }
    }

    /// <summary>
    /// Runs the docker CLI on the given context with an argument list. Returns the exit code (-1 when docker cannot start) and stdout.
    /// </summary>
    private static (int ExitCode, string Output) RunDocker(string context, string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("--context");
        psi.ArgumentList.Add(context);
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(psi)!;
            string output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, string.Empty);
        }
    }

    /// <summary>
    /// Checks the <c>uat_run</c> result: overall pass, every row graded pass, the bundle's secret scan passed, and ledger.md written.
    /// </summary>
    private static void AssertRunPasses(JsonElement root, string home, string repoRoot)
    {
        // Every row must pass, nodes-log included: its log clause reads the supervisor's ring (S-L6 since = run_start).
        string rowsText = root.GetProperty("rows").GetRawText();
        string diag = "overall " + root.GetProperty("overall").GetString() + "; rows: " + rowsText[..Math.Min(3000, rowsText.Length)];
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            Assert.True(row.GetProperty("grade").GetString() == "pass", diag);
        }

        // The result names its bundle folder (C4); the folder sits directly under UatRuns.
        string bundleName = root.TryGetProperty("bundle", out JsonElement bundleElement) ? bundleElement.GetString() ?? string.Empty : string.Empty;
        Assert.True(bundleName.Length > 0, "the uat_run result has no bundle: " + diag);
        string bundle = Path.Combine(LabPaths.FromRoot(home, repoRoot).UatRuns, bundleName);
        Assert.True(Directory.Exists(bundle), "bundle folder missing: " + bundle);

        using var runJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "run.json")));
        Assert.Equal("passed", runJson.RootElement.GetProperty("secret_scan").GetString());
        Assert.True(File.Exists(Path.Combine(bundle, "ledger.md")));

        // L21: the always-on invariants found nothing and no check errored.
        Assert.Equal(0, runJson.RootElement.GetProperty("violations").GetInt32());
        using var invariantsJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "invariants.json")));
        Assert.Equal(0, invariantsJson.RootElement.GetProperty("errors").GetArrayLength());
        Assert.Equal(0, invariantsJson.RootElement.GetProperty("violations").GetArrayLength());

        // D-ML80: sources that do not exist yet are skipped, only before Nodes has migrated. A skip at run end means a gate that always skips.
        JsonElement skipped = invariantsJson.RootElement.GetProperty("skipped");
        foreach (JsonElement skip in skipped.EnumerateArray())
        {
            string row = skip.GetProperty("row").GetString() ?? string.Empty;
            Assert.True(row is "(run-start)" or "stack-up", "unexpected invariant skip: " + skip.GetRawText());
        }

        Assert.Contains(skipped.EnumerateArray(), s => s.GetProperty("row").GetString() == "(run-start)");
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
