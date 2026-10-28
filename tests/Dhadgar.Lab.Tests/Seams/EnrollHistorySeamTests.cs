using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Processes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Dhadgar.Lab.Tests.Seams;

/// <summary>
/// S-L11: <c>history_run</c> drives the enrollment race through the daemon, the barrier proxy and the checker. Red until L51
/// registers <c>history_run</c> (<c>[Lab.UnknownTool]</c>); green after it. Needs Docker and the lab stack (S-L6's fixture).
/// </summary>
public sealed class EnrollHistorySeamTests
{
    [SkippableFact(Timeout = 900_000)]
    [Trait("Seam", "S-L11")]
    [Trait("RequiresDocker", "true")]
    public async Task EnrollRace_IsLinearizable_LostResponse_IsNotUnknown()
    {
        Skip.IfNot(DockerReachable(), "docker is not reachable on the lab context");
        string home = Path.Combine(Path.GetTempPath(), "meridian-lab-s11-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(home);
        try
        {
            string token = NewToken();
            var started = await LabDaemon.StartAsync(new LabDaemonOptions(
                Port: 0,
                Token: token,
                DataRoot: home,
                EnforceSingleInstance: false,
                RefuseElevated: false), CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);
            await using LabDaemon daemon = started.Value;

            using JsonDocument race = await CallToolAsync(daemon.BaseAddress, token, "history_run", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["scenario"] = "enroll-race",
                ["clients"] = 4,
                ["seed"] = 11,
            });
            Assert.Equal("linearizable", race.RootElement.GetProperty("verdict").GetString());
            Assert.Equal(4, CountEventLines(home, "invoke", "client:"));
            Assert.Equal(4, CountEventLines(home, "complete", "client:"));

            using JsonDocument lost = await CallToolAsync(daemon.BaseAddress, token, "history_run", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["scenario"] = "enroll-lost-response",
                ["seed"] = 12,
            });
            Assert.NotEqual("unknown", lost.RootElement.GetProperty("verdict").GetString());
            Assert.DoesNotContain("\"required\":true", lost.RootElement.GetRawText(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
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

    private static bool DockerReachable()
    {
        string context = Environment.GetEnvironmentVariable("Lab__Docker__LinuxContext") is { Length: > 0 } configured
            ? configured
            : ContainerLogFollower.DefaultLinuxContext;
        var psi = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("--context");
        psi.ArgumentList.Add(context);
        psi.ArgumentList.Add("version");
        psi.ArgumentList.Add("--format");
        psi.ArgumentList.Add("{{.Server.Version}}");
        try
        {
            using Process process = Process.Start(psi)!;
            _ = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
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
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        finally
        {
            Array.Clear(bytes);
        }
    }
}
