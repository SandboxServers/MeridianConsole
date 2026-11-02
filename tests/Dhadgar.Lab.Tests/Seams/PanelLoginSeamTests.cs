using System.Buffers.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Processes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Dhadgar.Lab.Tests.Seams;

/// <summary>
/// S-L9: a seeded user signs in through the Panel (Panel, Gateway, BetterAuth, Identity), reaches the dashboard, rotates the
/// refresh token and has the replayed old token refused. It drives the daemon tools <c>ui_flow_run</c> with the flows
/// <c>login_email</c> and <c>refresh</c>. It is red until L35 registers <c>ui_flow_run</c>; L35 must not change these
/// assertions. Needs the Linux Docker engine and the npm registry. Containers are removed by label and name prefix only.
/// </summary>
[Collection(LabDockerGate.Name)]
public sealed partial class PanelLoginSeamTests
{
    private const string WipeEnvVar = "MERIDIAN_LAB_S6_WIPE";

    [SkippableFact(Timeout = 1_800_000)]
    [Trait("Seam", "S-L9")]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task PanelLogin_ReachesDashboard_RefreshRotates_ReplayRefused()
    {
        string repoRoot = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        string home = Path.Combine(Path.GetTempPath(), "meridian-lab-s9-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(home);
        string context = Environment.GetEnvironmentVariable("Lab__Docker__LinuxContext") is { Length: > 0 } configured
            ? configured
            : ContainerLogFollower.DefaultLinuxContext;
        bool ownsContainers = false;
        try
        {
            string token = NewToken();
            var options = new LabDaemonOptions(Port: 0, Token: token, DataRoot: home, EnforceSingleInstance: false, RefuseElevated: false, RepoRoot: repoRoot);
            var started = await LabDaemon.StartAsync(options, CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);
            await using var daemon = started.Value;

            // First, before any Docker work: the flow tool must be registered, so this seam is red with or without Docker.
            IReadOnlyList<string> names = await ToolNamesAsync(daemon.BaseAddress, token);
            Assert.True(names.Contains("ui_flow_run"), "ui_flow_run is not registered; S-L9 stays red until L35");

            (int versionExit, _) = RunDocker(context, ["version", "--format", "{{.Server.Version}}"]);
            Skip.IfNot(versionExit == 0, "docker is not reachable on context " + context);

            (int psExit, string labelled) = RunDocker(context, ["ps", "-aq", "--filter", "label=" + LabDefaults.ContainerLabel + "=1", "--filter", "name=^meridian-lab-", "--format", "{{.ID}}"]);
            Assert.True(psExit == 0, "docker ps failed");
            if (labelled.Trim().Length > 0 && Environment.GetEnvironmentVariable(WipeEnvVar) != "1")
            {
                Assert.Fail("labelled lab containers exist; set MERIDIAN_LAB_S6_WIPE=1 to let the seam's stack_up wipe remove them");
            }

            ownsContainers = true;
            using var lease = await CallToolAsync(
                daemon.BaseAddress,
                token,
                "lab_lease_acquire",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["owner"] = "s9-panel-login", ["purpose"] = "S-L9 panel login", ["ttl_s"] = 3600 });
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
                foreach (string service in new[] { "nodes", "identity", "betterauth", "panel", "gateway" })
                {
                    using JsonDocument svc = await CallToolAsync(
                        daemon.BaseAddress,
                        token,
                        "svc_start",
                        new Dictionary<string, object?>(StringComparer.Ordinal) { ["service"] = service, ["lease_id"] = leaseId });
                }

                using JsonDocument login = await CallToolAsync(
                    daemon.BaseAddress,
                    token,
                    "ui_flow_run",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["flow"] = "login_email", ["seed"] = 9, ["lease_id"] = leaseId });
                JsonElement loginRoot = login.RootElement;
                Assert.True(loginRoot.GetProperty("ok").GetBoolean());
                JsonElement.ArrayEnumerator steps = loginRoot.GetProperty("steps").EnumerateArray();
                string lastUrl = steps.Last().GetProperty("url").GetString() ?? string.Empty;
                Assert.Equal("/dashboard", new Uri(lastUrl).AbsolutePath);
                JsonElement storage = loginRoot.GetProperty("storage");
                Assert.True(storage.GetProperty("access_token_present").GetBoolean());
                Assert.Matches(HexPrefix(), storage.GetProperty("refresh_token_sha256_prefix").GetString() ?? string.Empty);

                using JsonDocument refresh = await CallToolAsync(
                    daemon.BaseAddress,
                    token,
                    "ui_flow_run",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["flow"] = "refresh", ["seed"] = 9, ["lease_id"] = leaseId });
                JsonElement refreshRoot = refresh.RootElement;
                Assert.True(refreshRoot.GetProperty("ok").GetBoolean());
                foreach (string stepName in new[] { "refresh", "replay_refused" })
                {
                    JsonElement step = refreshRoot.GetProperty("steps").EnumerateArray()
                        .First(s => s.GetProperty("name").GetString() == stepName);
                    Assert.True(step.GetProperty("ok").GetBoolean(), stepName + " step is not ok");
                }

                Assert.Matches(HexPrefix(), refreshRoot.GetProperty("storage").GetProperty("refresh_token_sha256_prefix").GetString() ?? string.Empty);
            }
            finally
            {
                await TryReleaseAsync(daemon.BaseAddress, token, leaseId);
            }
        }
        finally
        {
            if (ownsContainers)
            {
                RemoveLabContainers(context);
            }

            try
            {
                if (Directory.Exists(home))
                {
                    Directory.Delete(home, recursive: true);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Best effort: the temp home is disposable.
            }
        }
    }

    [GeneratedRegex("^[0-9a-f]{8}$")]
    private static partial Regex HexPrefix();

    /// <summary>Releases the lease for cleanup. A failed release is written to the console and never thrown.</summary>
    private static async Task TryReleaseAsync(Uri baseAddress, string token, string leaseId)
    {
        try
        {
            using var released = await CallToolAsync(
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

    private static async Task<IReadOnlyList<string>> ToolNamesAsync(Uri baseAddress, string token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(baseAddress, LabDefaults.McpPath.TrimStart('/')),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
        };
        await using var transport = new HttpClientTransport(transportOptions, http);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);
        var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);
        return tools.Select(tool => tool.Name).ToList();
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
