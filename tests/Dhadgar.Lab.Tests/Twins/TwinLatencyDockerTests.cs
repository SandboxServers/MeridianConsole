using System.Buffers.Text;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Processes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Dhadgar.Lab.Tests.Twins;

/// <summary>
/// L53 Docker check: <c>twin_run enroll-latency-invariance</c> holds. The base world enrolls once on a clean stack; the variant adds
/// 1500 ms of latency on the Nodes database proxy, and the enrollment must keep its outcome and its step status. Needs the Linux Docker
/// engine and the lab stack. Containers are removed by label only.
/// </summary>
[Collection(LabDockerGate.Name)]
public sealed class TwinLatencyDockerTests
{
    private const string WipeEnvVar = "MERIDIAN_LAB_S6_WIPE";
    private const string EnrollRowPath = "rows/catalog-twin-enroll/enroll-once.json";
    private const int RowTextLimit = 4000;

    [SkippableFact(Timeout = 1_800_000)]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task EnrollLatencyInvariance_Holds()
    {
        string context = Environment.GetEnvironmentVariable("Lab__Docker__LinuxContext") is { Length: > 0 } configured
            ? configured
            : ContainerLogFollower.DefaultLinuxContext;
        (int versionExit, _) = RunDocker(context, ["version", "--format", "{{.Server.Version}}"]);
        Skip.IfNot(versionExit == 0, "docker is not reachable on context " + context);

        (int psExit, string labelled) = RunDocker(context, ["ps", "-aq", "--filter", "label=" + LabDefaults.ContainerLabel + "=1", "--filter", "name=^meridian-lab-", "--format", "{{.ID}}"]);
        Assert.True(psExit == 0, "docker ps failed");
        if (labelled.Trim().Length > 0 && Environment.GetEnvironmentVariable(WipeEnvVar) != "1")
        {
            Assert.Fail("labelled lab containers exist; set MERIDIAN_LAB_S6_WIPE=1 to let the latency stack_up wipe remove them");
        }

        string repoRoot = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        string home = Path.Combine(Path.GetTempPath(), "meridian-lab-twin-latency-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(home);
        try
        {
            string token = NewToken();
            var options = new LabDaemonOptions(Port: 0, Token: token, DataRoot: home, EnforceSingleInstance: false, RefuseElevated: false, RepoRoot: repoRoot, Time: TimeProvider.System);
            var started = await LabDaemon.StartAsync(options, CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);
            await using var daemon = started.Value;

            using JsonDocument lease = await CallToolAsync(
                daemon.BaseAddress,
                token,
                "lab_lease_acquire",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["owner"] = "twin-latency", ["purpose"] = "L53 enrollment latency invariance", ["ttl_s"] = 3600 });
            string leaseId = lease.RootElement.GetProperty("lease_id").GetString() ?? string.Empty;
            Assert.NotEmpty(leaseId);

            try
            {
                using JsonDocument build = await CallToolAsync(
                    daemon.BaseAddress,
                    token,
                    "lab_build",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["checkout"] = repoRoot, ["lease_id"] = leaseId });

                using JsonDocument up = await CallToolAsync(
                    daemon.BaseAddress,
                    token,
                    "stack_up",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["wipe"] = true, ["lease_id"] = leaseId });

                // Nodes creates its schema on start (F80); the Gateway's readiness needs Nodes.
                foreach (string service in new[] { "nodes", "gateway" })
                {
                    using JsonDocument svc = await CallToolAsync(
                        daemon.BaseAddress,
                        token,
                        "svc_start",
                        new Dictionary<string, object?>(StringComparer.Ordinal) { ["service"] = service, ["lease_id"] = leaseId });
                }

                using JsonDocument twin = await CallToolAsync(
                    daemon.BaseAddress,
                    token,
                    "twin_run",
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["experiment"] = "enroll-latency-invariance",
                        ["lease_id"] = leaseId,
                    });
                JsonElement held = twin.RootElement.GetProperty("held");
                string rows = held.ValueKind == JsonValueKind.True ? string.Empty : await DescribeRowsAsync(daemon.BaseAddress, token, twin.RootElement);
                Assert.True(held.ValueKind == JsonValueKind.True, "twin relation did not hold: " + twin.RootElement.GetRawText() + rows);

                // Anti-hollow (L53 H4): the toxic must be in place for the enroll, not reset by the row's own stack_up.
                double enrollMs = await VariantEnrollDurationMsAsync(daemon.BaseAddress, token, twin.RootElement);
                Assert.True(enrollMs >= 1500, "the latency did not reach the enroll: " + enrollMs.ToString(CultureInfo.InvariantCulture));
            }
            finally
            {
                await TryReleaseAsync(daemon.BaseAddress, token, leaseId);
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
    /// The two worlds' enroll rows, as text for a failure message (L53 diagnosis). A failed read is written as its text and never hides the twin failure.
    /// </summary>
    private static async Task<string> DescribeRowsAsync(Uri baseAddress, string token, JsonElement twin)
    {
        string text = string.Empty;
        foreach (string world in new[] { "base", "variant" })
        {
            string row;
            try
            {
                string runId = twin.GetProperty(world).GetProperty("run_id").GetString() ?? string.Empty;
                string full = await ReadRowAsync(baseAddress, token, runId);
                row = full.Length > RowTextLimit ? full[..RowTextLimit] : full;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                row = "read failed: " + ex.Message;
            }

            text += "\n" + world + " row: " + row;
        }

        return text;
    }

    /// <summary>Reads the enroll row of one run through <c>bundle_read</c> (Open, no lease). The whole text is returned.</summary>
    private static async Task<string> ReadRowAsync(Uri baseAddress, string token, string runId)
    {
        using JsonDocument row = await CallToolAsync(
            baseAddress,
            token,
            "bundle_read",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["run_id"] = runId, ["path"] = EnrollRowPath });
        return row.RootElement.GetProperty("text").GetString() ?? string.Empty;
    }

    /// <summary>The duration of the variant's <c>enroll</c> step in milliseconds, or -1 when the row has no such step.</summary>
    private static async Task<double> VariantEnrollDurationMsAsync(Uri baseAddress, string token, JsonElement twin)
    {
        string runId = twin.GetProperty("variant").GetProperty("run_id").GetString() ?? string.Empty;
        using JsonDocument row = JsonDocument.Parse(await ReadRowAsync(baseAddress, token, runId));
        foreach (JsonElement step in row.RootElement.GetProperty("steps").EnumerateArray())
        {
            if (step.GetProperty("id").GetString() == "enroll")
            {
                return step.TryGetProperty("duration_ms", out JsonElement ms) && ms.ValueKind == JsonValueKind.Number ? ms.GetDouble() : -1;
            }
        }

        return -1;
    }

    /// <summary>Releases the lease for cleanup. A failed release is written to the console and never thrown (R3-N5).</summary>
    private static async Task TryReleaseAsync(Uri baseAddress, string token, string leaseId)
    {
        try
        {
            using JsonDocument released = await CallToolAsync(
                baseAddress,
                token,
                "lab_lease_release",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["lease_id"] = leaseId });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await Console.Error.WriteLineAsync("lab_lease_release failed during cleanup: " + ex.Message);
        }
    }

    private static void RemoveLabContainers(string context)
    {
        (int _, string ids) = RunDocker(context, ["ps", "-aq", "--filter", "label=" + LabDefaults.ContainerLabel + "=1", "--filter", "name=^meridian-lab-", "--format", "{{.ID}}"]);
        foreach (string id in ids.Split((char)10, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            _ = RunDocker(context, ["rm", "-f", "-v", id]);
        }
    }

    private static (int ExitCode, string Output) RunDocker(string context, string[] args)
    {
        var psi = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("--context");
        psi.ArgumentList.Add(context);
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using Process process = Process.Start(psi)!;
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

    private static async Task<JsonDocument> CallToolAsync(Uri baseAddress, string token, string toolName, Dictionary<string, object?> arguments)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(baseAddress, LabDefaults.McpPath.TrimStart('/')),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
        };
        await using var transport = new HttpClientTransport(transportOptions, http);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);
        CallToolResult result = await client.CallToolAsync(toolName, arguments, cancellationToken: CancellationToken.None);
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        if (result.IsError == true)
        {
            throw new InvalidOperationException(toolName + " failed: " + text);
        }

        return JsonDocument.Parse(text);
    }

    private static string NewToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
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
