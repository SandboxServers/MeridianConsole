using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Soak;

/// <summary>
/// A soak run as prepared before its job starts: the spec, the optional rows, the run length and the seed.
/// </summary>
/// <param name="Spec">The spec path, relative to <c>docs/guides/uat-specs/</c>, already validated.</param>
/// <param name="Rows">The rows to loop, or null for every row of the spec.</param>
/// <param name="DurationS">The run length in seconds.</param>
/// <param name="Seed">The run seed, recorded with the job and passed to <c>uat_run</c>.</param>
public sealed record SoakRun(string Spec, IReadOnlyList<string>? Rows, int DurationS, ulong Seed);

/// <summary>
/// The soak job (<c>soak_run</c>, L29). It loops a spec's rows through <c>uat_run</c> for the run length, paced by the Gateway
/// pacer (D-ML88). Every <see cref="SampleInterval"/> it takes a resource sample (A4) into the shared
/// <see cref="ResourceSampleStore"/>, where I7 reads the trend. A soak injects no faults, so its <c>finally</c> only ends the
/// sample store, on the job's own path, whether the run finishes or is cancelled.
/// </summary>
public static class SoakJob
{
    /// <summary>The job kind, also the bundle kind.</summary>
    public const string Kind = "soak";

    /// <summary>The time between two resource samples.</summary>
    public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(30);

    /// <summary>The pause after a failed workload run, before the next one.</summary>
    public static readonly TimeSpan WorkloadRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>The fewest successful workload loops a soak can pass with (D-ML112).</summary>
    internal const int MinLoops = 3;

    /// <summary>
    /// The job work. Runs the workload and the samples until the run length has passed or the job is cancelled. A sample
    /// failure is recorded in the store (an I7 check error) and does not stop the run. Each tool call carries the job's own
    /// lease id as <c>lease_id</c>; the id is never logged or written.
    /// </summary>
    /// <param name="run">The prepared run.</param>
    /// <param name="context">The job context.</param>
    /// <param name="invoke">The tool call: the dispatcher's <c>InvokeAsync</c> in the daemon, a fake in tests.</param>
    /// <param name="pacer">The Gateway pacer, awaited before each workload run.</param>
    /// <param name="sampler">The labelled-container sampler.</param>
    /// <param name="store">The sample store that I7 reads.</param>
    /// <param name="time">The clock.</param>
    /// <param name="ct">The job's cancellation token.</param>
    public static async Task<Result<JobOutcome>> RunAsync(SoakRun run, JobContext context,
        Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> invoke,
        GatewayPacer pacer, ResourceSampler sampler, ResourceSampleStore store, TimeProvider time, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(invoke);
        ArgumentNullException.ThrowIfNull(pacer);
        ArgumentNullException.ThrowIfNull(sampler);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);

        int loops = 0;
        int workloadErrors = 0;
        int samples = 0;
        int sampleErrors = 0;
        DateTimeOffset started = time.GetUtcNow();
        DateTimeOffset end = started + TimeSpan.FromSeconds(run.DurationS);
        DateTimeOffset nextSample = started;
        string i7 = "checked";
        bool i7Failed = false;

        store.Begin();
        try
        {
            while (time.GetUtcNow() < end)
            {
                ct.ThrowIfCancellationRequested();

                if (time.GetUtcNow() >= nextSample)
                {
                    Result<ResourceSampleSet> sample = await sampler.SampleAsync(ct).ConfigureAwait(false);
                    if (sample.IsFailure)
                    {
                        sampleErrors++;
                        store.Fail(sample.Error);
                    }
                    else
                    {
                        samples++;
                        store.Add(sample.Value!);
                    }

                    nextSample = time.GetUtcNow() + SampleInterval;
                }

                Result paced = await pacer.WaitAsync(ct).ConfigureAwait(false);
                if (paced.IsFailure)
                {
                    workloadErrors++;
                    await Task.Delay(WorkloadRetryDelay, time, ct).ConfigureAwait(false);
                    continue;
                }

                var args = JsonSerializer.SerializeToElement(WorkloadArguments(run, context));
                LabToolResult call = await invoke("uat_run", args, CallContext(context), ct).ConfigureAwait(false);
                if (call.IsError)
                {
                    workloadErrors++;
                    await Task.Delay(WorkloadRetryDelay, time, ct).ConfigureAwait(false);
                    continue;
                }

                loops++;
            }

            // R2-N3 and D-ML100: the soak ends with an I7 check over its own window, while the store is still open. A cancelled
            // soak leaves the loop by throwing, so it never reaches this line.
            LabToolResult check = await invoke("invariants_check",
                JsonSerializer.SerializeToElement(new { since = started.ToString("O", CultureInfo.InvariantCulture) }),
                CallContext(context), ct).ConfigureAwait(false);
            i7Failed = check.IsError || HasI7(check.Payload, "violations") || HasI7(check.Payload, "errors");
            i7 = check.IsError ? "error" : HasI7(check.Payload, "skipped") ? "skipped" : "checked";
        }
        finally
        {
            // A soak injects no faults; ending the store is its only cleanup. It runs on cancel too.
            store.End();
        }

        (string overall, string? reason) = Grade(loops, samples, sampleErrors, i7, i7Failed);
        JsonNode? result = JsonSerializer.SerializeToNode(new
        {
            loops,
            workload_errors = workloadErrors,
            samples,
            sample_errors = sampleErrors,
            i7,
            duration_s = run.DurationS,
            seed = run.Seed,
            reason,
        });
        return Result<JobOutcome>.Success(new JobOutcome(overall, result));
    }

    /// <summary>D-ML112: fail on a sampler error, a failed I7 or no loop; weak on too few loops or samples; else pass.</summary>
    internal static (string Overall, string? Reason) Grade(int loops, int samples, int sampleErrors, string i7, bool i7Failed)
    {
        if (sampleErrors > 0 || i7Failed || loops == 0)
        {
            return ("fail", loops == 0 ? "no workload loop succeeded" : null);
        }

        if (loops < MinLoops)
        {
            return ("weak", "fewer than 3 workload loops");
        }

        if (samples < I7ResourceTrendInvariant.MinSamples || i7 == "skipped")
        {
            return ("weak", "too few resource samples for I7");
        }

        return ("pass", null);
    }

    /// <summary>The <c>uat_run</c> arguments for one loop: the spec, the rows when given, the seed and the job's lease.</summary>
    internal static Dictionary<string, object?> WorkloadArguments(SoakRun run, JobContext context)
    {
        var args = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["spec"] = run.Spec,
            ["seed"] = run.Seed,
            ["lease_id"] = context.LeaseId,
        };
        if (run.Rows is not null)
        {
            args["rows"] = run.Rows;
        }

        return args;
    }

    private static LabCallContext CallContext(JobContext context) =>
        new(Guid.NewGuid().ToString("N"), context.LeaseId, "job:" + context.JobId);

    /// <summary>True when the named array of an <c>invariants_check</c> payload has an entry for I7.</summary>
    private static bool HasI7(JsonElement payload, string arrayName)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(arrayName, out JsonElement entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement entry in entries.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("invariant_id", out JsonElement id)
                && id.ValueKind == JsonValueKind.String
                && string.Equals(id.GetString(), "I7", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
