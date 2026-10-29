using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Mutants;

/// <summary>
/// The end-to-end mutation run (L54): <c>mutation_run</c> with the M1 mutant builds the patched product, starts its services on
/// the mutant build, runs the history detector and must report <c>detected</c>. Needs Docker and a git checkout of the repo; it
/// runs only in the Docker lane. Cleanup is by the lab label, through the job's own restore.
/// </summary>
[Collection("LabHome")]
[Trait("RequiresDocker", "true")]
public sealed class MutationDockerTests
{
    private const string MutantId = "M1-token-no-concurrency";

    [Fact(Timeout = 1_800_000)]
    public async Task M1_TokenNoConcurrency_IsDetected()
    {
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
                Time: new FakeTimeProvider(DateTimeOffset.UnixEpoch)), CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);

            await using LabDaemon daemon = started.Value;
            IServiceProvider services = daemon.Services;
            Result<LeaseGrant> grant = services.GetRequiredService<LeaseBook>().Acquire("l54-docker", "mutation", 1800, force: false, reason: null);
            Assert.True(grant.IsSuccess, grant.Error);

            var args = new JsonObject
            {
                ["mutants"] = new JsonArray(MutantId),
                [LabToolDispatcher.LeaseIdArgument] = grant.Value!.LeaseId,
            };
            LabToolResult call = await services.GetRequiredService<LabToolDispatcher>().InvokeAsync(
                "mutation_run",
                JsonSerializer.SerializeToElement(args),
                new LabCallContext(Guid.NewGuid().ToString("N"), grant.Value.LeaseId, "test:l54"),
                CancellationToken.None);
            Assert.False(call.IsError);
            string jobId = call.Payload.GetProperty("job_id").GetString()!;

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
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
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
