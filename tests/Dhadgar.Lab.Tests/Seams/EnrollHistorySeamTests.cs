using System.Buffers.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Processes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Dhadgar.Lab.Tests.Seams;

/// <summary>
/// S-L11: <c>history_run</c> drives the enrollment race through the daemon, the barrier proxy and the checker. Needs the Linux Docker
/// engine and the lab stack. The stack is brought up with <c>stack_up wipe</c>, and Nodes then the Gateway are started with
/// <c>svc_start</c>, all under one lease, so the schema exists and the barrier's upstream answers (F80). Containers are removed by
/// label and name prefix only.
/// </summary>
[Collection("LabHome")]
public sealed class EnrollHistorySeamTests
{
    private const string WipeEnvVar = "MERIDIAN_LAB_S6_WIPE";

    [SkippableFact(Timeout = 900_000)]
    [Trait("Seam", "S-L11")]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task EnrollRace_IsLinearizable_LostResponse_IsNotUnknown()
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
            Assert.Fail("labelled lab containers exist; set MERIDIAN_LAB_S6_WIPE=1 to let the seam's stack_up wipe remove them");
        }

        string repoRoot = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        string home = Path.Combine(Path.GetTempPath(), "meridian-lab-s11-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(home);
        try
        {
            string token = NewToken();
            var options = new LabDaemonOptions(Port: 0, Token: token, DataRoot: home, EnforceSingleInstance: false, RefuseElevated: false, RepoRoot: repoRoot);
            var started = await LabDaemon.StartAsync(options, CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);
            await using var daemon = started.Value;

            using var lease = await CallToolAsync(
                daemon.BaseAddress,
                token,
                "lab_lease_acquire",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["owner"] = "s11-enroll-history", ["purpose"] = "S-L11 enrollment history" });
            string leaseId = lease.RootElement.GetProperty("lease_id").GetString() ?? string.Empty;
            Assert.NotEmpty(leaseId);

            try
            {
                using var build = await CallToolAsync(
                    daemon.BaseAddress,
                    token,
                    "lab_build",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["checkout"] = repoRoot, ["lease_id"] = leaseId });

                using var up = await CallToolAsync(
                    daemon.BaseAddress,
                    token,
                    "stack_up",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["wipe"] = true, ["lease_id"] = leaseId });

                // Nodes creates its schema on start (F80); the barrier's upstream is agent-ingress -> Gateway, whose readiness needs Nodes.
                foreach (string service in new[] { "nodes", "gateway" })
                {
                    using JsonDocument svc = await CallToolAsync(
                        daemon.BaseAddress,
                        token,
                        "svc_start",
                        new Dictionary<string, object?>(StringComparer.Ordinal) { ["service"] = service, ["lease_id"] = leaseId });
                }

                using JsonDocument race =await CallToolAsync(daemon.BaseAddress, token, "history_run", new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["scenario"] = "enroll-race",
                    ["clients"] = 4,
                    ["seed"] = 11,
                    ["lease_id"] = leaseId,
                });
                Assert.Equal("linearizable", race.RootElement.GetProperty("verdict").GetString());
                AssertNoRequiredViolation(race.RootElement);
                Assert.Equal(4, CountEventLines(home, "invoke", "client:"));
                Assert.Equal(4, CountEventLines(home, "complete", "client:"));

                int invokesBefore = CountEventLines(home, "invoke", "client:");
                using JsonDocument lost = await CallToolAsync(daemon.BaseAddress, token, "history_run", new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["scenario"] = "enroll-lost-response",
                    ["seed"] = 12,
                    ["lease_id"] = leaseId,
                });
                Assert.NotEqual("unknown", lost.RootElement.GetProperty("verdict").GetString());
                Assert.Equal("linearizable", lost.RootElement.GetProperty("verdict").GetString());
                AssertNoRequiredViolation(lost.RootElement);
                // One client, same-key retry after a dropped response: the first attempt and the retry both reach the proxy.
                Assert.Equal(2, CountEventLines(home, "invoke", "client:") - invokesBefore);
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

    /// <summary>Fails when any violation in the <c>history_run</c> payload is marked required (E1-E4, E6).</summary>
    private static void AssertNoRequiredViolation(JsonElement root)
    {
        foreach (JsonElement violation in root.GetProperty("violations").EnumerateArray())
        {
            Assert.False(
                violation.GetProperty("required").GetBoolean(),
                "required violation: " + violation.GetRawText());
        }
    }

    private static int CountEventLines(string home, string kind, string actorPrefix)
    {
        int count = 0;
        foreach (string file in Directory.EnumerateFiles(home, "events.jsonl", SearchOption.AllDirectories))
        {
            foreach (string line in File.ReadLines(file))
            {
                if (line.Contains("\"kind\":\"" + kind + "\"", StringComparison.Ordinal) && line.Contains("\"actor\":\"" + actorPrefix, StringComparison.Ordinal))
                {
                    count++;
                }
            }
        }

        return count;
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
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
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
