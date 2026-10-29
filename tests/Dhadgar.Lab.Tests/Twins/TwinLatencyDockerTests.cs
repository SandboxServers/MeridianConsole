using System.Buffers.Text;
using System.Diagnostics;
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
                Assert.True(held.ValueKind == JsonValueKind.True, "twin relation did not hold: " + twin.RootElement.GetRawText());
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
        (int _, string ids) = RunDocker(context, ["ps", "-aq", "--filter", "label=" + LabDefaults.ContainerLabel + "=1", "--format", "{{.ID}}"]);
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
