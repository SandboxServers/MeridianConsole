using System.Buffers.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Processes;
using Dhadgar.Lab.Tools;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Lab.Tests.Mutants;

/// <summary>
/// The end-to-end mutation run (L54): <c>mutation_run</c> with the M1 mutant builds the patched product, starts its services on
/// the mutant build, runs the history detector and must report <c>detected</c>. Needs Docker and a git checkout of the repo; it
/// runs only in the Docker lane. The stack is brought up with <c>stack_up wipe</c> and Nodes then the Gateway are started with
/// <c>svc_start</c>, all under one lease of 3600 s (R3-N4). Cleanup is by the lab label; the lease is released without throwing.
/// </summary>
[Collection(LabDockerGate.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MutationDockerTests
{
    private const string MutantId = "M1-token-no-concurrency";
    private const string WipeEnvVar = "MERIDIAN_LAB_S6_WIPE";

    [Fact(Timeout = 1_800_000)]
    public async Task M1_TokenNoConcurrency_IsDetected()
    {
        string context = Environment.GetEnvironmentVariable("Lab__Docker__LinuxContext") is { Length: > 0 } configured
            ? configured
            : ContainerLogFollower.DefaultLinuxContext;
        (int psExit, string labelled) = RunDocker(context, ["ps", "-aq", "--filter", "label=" + LabDefaults.ContainerLabel + "=1", "--filter", "name=^meridian-lab-", "--format", "{{.ID}}"]);
        Assert.True(psExit == 0, "docker ps failed");
        if (labelled.Trim().Length > 0 && Environment.GetEnvironmentVariable(WipeEnvVar) != "1")
        {
            Assert.Fail("labelled lab containers exist; set MERIDIAN_LAB_S6_WIPE=1 to let the mutation stack_up wipe remove them");
        }

        string repoRoot = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        string root = Path.Combine(Path.GetTempPath(), "lab-l54-docker-" + Guid.NewGuid().ToString("N"));
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
                RepoRoot: repoRoot,
                Time: TimeProvider.System), CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);

            await using LabDaemon daemon = started.Value;
            IServiceProvider services = daemon.Services;
            LabToolDispatcher dispatcher = services.GetRequiredService<LabToolDispatcher>();
            string? leaseId = null;
            try
            {
                JsonElement lease = await InvokeAsync(
                    dispatcher,
                    "lab_lease_acquire",
                    new JsonObject { ["owner"] = "l54-docker", ["purpose"] = "mutation", ["ttl_s"] = 3600 },
                    leaseId: null);
                leaseId = lease.GetProperty("lease_id").GetString()!;

                _ = await InvokeAsync(dispatcher, "lab_build", new JsonObject { ["checkout"] = repoRoot }, leaseId);
                _ = await InvokeAsync(dispatcher, "stack_up", new JsonObject { ["wipe"] = true }, leaseId);

                // Nodes creates its schema on start (F80); the Gateway's readiness needs Nodes.
                foreach (string service in new[] { "nodes", "gateway" })
                {
                    _ = await InvokeAsync(dispatcher, "svc_start", new JsonObject { ["service"] = service }, leaseId);
                }

                // mutation_run is OwnLease (wave 5 M1): it takes its own lease, so this test releases its lease first and
                // passes no lease_id. The finally block then has nothing to release.
                _ = services.GetRequiredService<LeaseBook>().Release(leaseId);
                leaseId = null;

                JsonElement call = await InvokeAsync(
                    dispatcher,
                    "mutation_run",
                    new JsonObject { ["mutants"] = new JsonArray(MutantId) },
                    leaseId: null);
                string jobId = call.GetProperty("job_id").GetString()!;

                JobRecord record = await WaitForJobAsync(services.GetRequiredService<JobRunner>(), jobId, CancellationToken.None);

                Assert.Equal("complete", record.State);
                JsonObject mutant = record.Result!["mutants"]!.AsArray()
                    .Select(node => node!.AsObject())
                    .Single(entry => entry["id"]!.GetValue<string>() == MutantId);
                Assert.Equal("detected", mutant["verdict"]!.GetValue<string>());
                Assert.True(record.Result["restore"]!["ok"]!.GetValue<bool>());
            }
            finally
            {
                if (leaseId is not null)
                {
                    _ = services.GetRequiredService<LeaseBook>().Release(leaseId);
                }

                RemoveLabContainers(context);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<JsonElement> InvokeAsync(LabToolDispatcher dispatcher, string tool, JsonObject args, string? leaseId)
    {
        LabToolResult call = await dispatcher.InvokeAsync(
            tool,
            JsonSerializer.SerializeToElement(args),
            new LabCallContext(Guid.NewGuid().ToString("N"), leaseId, "test:l54"),
            CancellationToken.None);
        Assert.False(call.IsError, call.Payload.ToString());
        return call.Payload;
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

    private static async Task<JobRecord> WaitForJobAsync(JobRunner jobs, string jobId, CancellationToken ct)
    {
        while (true)
        {
            JobRecord record = jobs.Get(jobId).Value!;
            if (record.State is not ("queued" or "running"))
            {
                return record;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}
