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
/// S-L12: <c>twin_run tenant-isolation-enroll</c> must return <c>held = true</c>. Tenant A's observed sets are equal in the base and
/// variant worlds, and tenant B never observes an id tenant A created. The experiment file arrives in L56; until then the call returns
/// <c>[Lab.NotFound]</c>, which is the expected red failure recorded at the wave boundary. Needs the Linux Docker engine. Containers are
/// removed by label and name prefix.
/// </summary>
[Collection(LabDockerGate.Name)]
public sealed class TenantTwinSeamTests
{
    private const string WipeEnvVar = "MERIDIAN_LAB_S6_WIPE";

    [SkippableFact(Timeout = 1_800_000)]
    [Trait("Seam", "S-L12")]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task TenantIsolationEnroll_Holds()
    {
        string context = Environment.GetEnvironmentVariable("Lab__Docker__LinuxContext") is { Length: > 0 } configured
            ? configured
            : ContainerLogFollower.DefaultLinuxContext;
        (int versionExit, _) = RunDocker(context, ["version", "--format", "{{.Server.Version}}"]);
        Skip.IfNot(versionExit == 0, "docker is not reachable on context " + context);

        // The twin_run stack_up wipes lab containers, so a labelled lab container left by another run stops the test (N4 pass 2).
        (int psExit, string labelled) = RunDocker(context, ["ps", "-aq", "--filter", "label=" + LabDefaults.ContainerLabel + "=1", "--filter", "name=^meridian-lab-", "--format", "{{.ID}}"]);
        Assert.True(psExit == 0, "docker ps failed");
        if (labelled.Trim().Length > 0 && Environment.GetEnvironmentVariable(WipeEnvVar) != "1")
        {
            Assert.Fail("labelled lab containers exist; set MERIDIAN_LAB_S6_WIPE=1 to let the twin_run stack_up wipe remove them");
        }

        string repoRoot = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        string home = Path.Combine(Path.GetTempPath(), "meridian-lab-s12-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(home);
        try
        {
            string token = NewToken();
            var options = new LabDaemonOptions(Port: 0, Token: token, DataRoot: home, EnforceSingleInstance: false, RefuseElevated: false, RepoRoot: repoRoot);
            var started = await LabDaemon.StartAsync(options, CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);
            await using var daemon = started.Value;

            using JsonDocument lease = await CallToolAsync(
                daemon.BaseAddress,
                token,
                "lab_lease_acquire",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["owner"] = "s12-tenant-twin", ["purpose"] = "S-L12 tenant isolation twin", ["ttl_s"] = 3600 });
            string leaseId = lease.RootElement.GetProperty("lease_id").GetString() ?? string.Empty;
            Assert.NotEmpty(leaseId);

            try
            {
                using JsonDocument twin = await CallToolAsync(
                    daemon.BaseAddress,
                    token,
                    "twin_run",
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["experiment"] = "tenant-isolation-enroll",
                        ["seed"] = 21,
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
