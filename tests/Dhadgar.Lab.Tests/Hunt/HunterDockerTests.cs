using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Lab.Tests.Hunt;

/// <summary>
/// The D-ML84 shape for the hunter: three iterations of <c>hunt_model_run</c> against the real lab finish with a result and a
/// job folder. Runs only where the Linux Docker engine is available (the wave boundary, not the per-packet checks).
/// </summary>
[Collection(LabDockerGate.Name)]
public sealed class HunterDockerTests
{
    [Fact]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task ThreeIterations_AgainstRealLab_FinishWithResultAndBundle()
    {
        string root = Path.Combine(Path.GetTempPath(), "lab-l27-hunter-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
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
                RefuseElevated: false), CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);

            await using LabDaemon daemon = started.Value;
            IServiceProvider services = daemon.Services;
            var dispatcher = services.GetRequiredService<LabToolDispatcher>();
            var jobs = services.GetRequiredService<JobRunner>();
            LabPaths paths = services.GetRequiredService<LabPaths>();

            using JsonDocument args = JsonDocument.Parse("{\"iterations\":3,\"budget_s\":120}");
            LabToolResult call = await dispatcher.InvokeAsync("hunt_model_run", args.RootElement.Clone(),
                new LabCallContext(Guid.NewGuid().ToString("N"), null, "hunter-docker-test"), CancellationToken.None);
            Assert.False(call.IsError, call.Payload.GetRawText());
            string jobId = call.Payload.GetProperty("job_id").GetString()!;

            // Wait for the job to end; the 120 s budget bounds the loop.
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(5);
            JobRecord? record = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                record = jobs.Get(jobId).Value;
                if (record is not null && record.State != "running")
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }

            Assert.NotNull(record);
            Assert.NotEqual("running", record!.State);
            Assert.True(File.Exists(Path.Combine(paths.Jobs, jobId, "job.json")));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: a container may still hold a file open; the lab's own reset clears it later.
            }
        }
    }
}
