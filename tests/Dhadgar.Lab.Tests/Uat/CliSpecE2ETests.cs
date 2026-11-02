using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Processes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Dhadgar.Lab.Tests.Uat;

/// <summary>
/// L37 (F144): the daemon runs in process on an ephemeral loopback port with a temp data root and the real repo root. The test takes a
/// lease, builds the repo through <c>lab_build</c>, runs the shipped <c>docs/guides/uat-specs/cli.toml</c> through <c>uat_run</c>, and
/// checks that all five rows pass in order, the bundle's <c>secret_scan</c> is <c>passed</c>, and the invariants found nothing. Needs the
/// Linux Docker engine. The spec's own rows bring the <c>meridian-lab-*</c> stack and the CLI member up and down; the test removes only
/// its temp data root and the lab-labelled containers and network.
/// </summary>
/// <remarks>
/// The CLI's loopback gate only accepts the fixed lab ports, so this test calls <c>/mcp</c> on the daemon's own base address.
/// </remarks>
[Collection(LabDockerGate.Name)]
public sealed class CliSpecE2ETests
{
    private const string CliSpec = "cli.toml";
    private const string WipeEnvVar = "MERIDIAN_LAB_S6_WIPE";
    private static readonly string[] ExpectedRowIds = ["version", "auth-login", "nodes-list", "notifications-admin", "gateway-down"];

    [SkippableFact(Timeout = 2_400_000)]
    [Trait("Seam", "D-ML163")]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task CliSpec_EveryRowPasses_SecretScanPasses_NoInvariantErrors()
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
            Assert.Fail("labelled lab containers exist; set MERIDIAN_LAB_S6_WIPE=1 to let the CLI spec's stack_up wipe remove them");
        }

        string repoRoot = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        string home = Path.Combine(Path.GetTempPath(), "meridian-lab-cli-" + Guid.NewGuid().ToString("N"));
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
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["owner"] = "l37-cli-live", ["purpose"] = "L37 live cli.toml" });
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
                        ["spec"] = CliSpec,
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
            RemoveLabNetwork(context);
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
    /// Removes the lab network, and only when it carries the <c>meridian-lab=1</c> label and the exact lab network name.
    /// </summary>
    private static void RemoveLabNetwork(string context)
    {
        (int _, string ids) = RunDocker(context, ["network", "ls", "-q", "--filter", "label=" + LabDefaults.ContainerLabel + "=1", "--filter", "name=^" + LabDefaults.NetworkName + "$"]);
        foreach (string id in ids.Split((char)10, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            _ = RunDocker(context, ["network", "rm", id]);
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
    /// Checks the <c>uat_run</c> result: overall pass, exactly the five cli.toml rows in order, every row graded pass, the bundle's secret
    /// scan passed, the invariants found nothing, and ledger.md written.
    /// </summary>
    private static void AssertRunPasses(JsonElement root, string home, string repoRoot)
    {
        // Every row must pass. The rows are checked in the spec's order; the row id is the row_id property (snake_case, as LabCli reads it).
        string rowsText = root.GetProperty("rows").GetRawText();
        string diag = "overall " + root.GetProperty("overall").GetString() + "; rows: " + rowsText[..Math.Min(3000, rowsText.Length)];
        var rowIds = new List<string>();
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            rowIds.Add(row.GetProperty("row_id").GetString() ?? string.Empty);
            Assert.True(row.GetProperty("grade").GetString() == "pass", diag);
        }

        Assert.Equal(ExpectedRowIds, rowIds);

        // The result names its bundle folder (C4); the folder sits directly under UatRuns.
        string bundleName = root.TryGetProperty("bundle", out JsonElement bundleElement) ? bundleElement.GetString() ?? string.Empty : string.Empty;
        Assert.True(bundleName.Length > 0, "the uat_run result has no bundle: " + diag);
        string bundle = Path.Combine(LabPaths.FromRoot(home, repoRoot).UatRuns, bundleName);
        Assert.True(Directory.Exists(bundle), "bundle folder missing: " + bundle);

        using var runJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "run.json")));
        Assert.Equal("passed", runJson.RootElement.GetProperty("secret_scan").GetString());
        Assert.True(File.Exists(Path.Combine(bundle, "ledger.md")));

        // L25: the run was ingested and graded against the shipped known issues.
        JsonElement ingest = root.GetProperty("ingest");
        Assert.True(ingest.GetProperty("ok").GetBoolean(), "ingest failed: " + ingest.GetRawText());
        Assert.Equal("pass", ingest.GetProperty("overall").GetString());
        Assert.True(ingest.GetProperty("known_issues").GetProperty("checked").GetBoolean(), ingest.GetRawText());
        string resultsDb = Path.Combine(LabPaths.FromRoot(home, repoRoot).Results, "lab-results.db");
        Assert.True(File.Exists(resultsDb), "results database missing: " + resultsDb);

        // L21: the always-on invariants found nothing and no check errored. Skips are not asserted: the version row and the rows without
        // Nodes legitimately skip invariant sources.
        Assert.Equal(0, runJson.RootElement.GetProperty("violations").GetInt32());
        using var invariantsJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "invariants.json")));
        Assert.Equal(0, invariantsJson.RootElement.GetProperty("errors").GetArrayLength());
        Assert.Equal(0, invariantsJson.RootElement.GetProperty("violations").GetArrayLength());
    }

    /// <summary>
    /// Calls one MCP tool on the in-process daemon and returns its JSON text. Throws on a tool error so the test fails with the text.
    /// </summary>
    private static async Task<JsonDocument> CallToolAsync(Uri baseAddress, string token, string toolName, Dictionary<string, object?> arguments)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(40) };
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
