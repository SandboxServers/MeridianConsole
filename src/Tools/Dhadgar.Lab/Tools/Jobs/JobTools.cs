using System.Text.Json.Nodes;
using Dhadgar.Lab.Jobs;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Jobs;

/// <summary>Arguments of <c>job_status</c>. Omit <see cref="JobId"/> to list every job.</summary>
public sealed record JobStatusArgs(string? JobId = null);

/// <summary>Arguments of <c>job_cancel</c>.</summary>
public sealed record JobCancelArgs(string JobId);

/// <summary>One job as <c>job_status</c> reports it. Carries no seed and no lease id.</summary>
public sealed record JobView(string JobId, string Kind, string State, DateTimeOffset StartedAt, string? Progress, JsonNode? Result);

/// <summary>The <c>job_status</c> payload.</summary>
public sealed record JobStatusResult(IReadOnlyList<JobView> Jobs);

/// <summary>
/// <c>job_status</c> (Open): the jobs of this daemon, or one job by id. Never carries a lease id.
/// </summary>
public sealed class JobStatusTool : LabTool<JobStatusArgs>
{
    private readonly JobRunner _jobs;

    /// <summary>
    /// Creates the tool over the job runner.
    /// </summary>
    public JobStatusTool(JobRunner jobs)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    /// <inheritdoc />
    public override string Name => "job_status";

    /// <inheritdoc />
    public override string Description => "Lists the lab jobs (job_id, kind, state, started_at, progress, result), or one job when job_id is given.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(JobStatusArgs args, LabCallContext context, CancellationToken ct)
    {
        if (args.JobId is null)
        {
            return Task.FromResult(Result<object>.Success(new JobStatusResult(_jobs.List().Select(View).ToList())));
        }

        Result<JobRecord> record = _jobs.Get(args.JobId);
        return Task.FromResult(record.IsFailure
            ? Result<object>.Failure(record.Error)
            : Result<object>.Success(new JobStatusResult([View(record.Value!)])));
    }

    internal static JobView View(JobRecord record) =>
        new(record.JobId, record.Kind, record.State, record.StartedAt, record.Progress, record.Result);
}

/// <summary>
/// <c>job_cancel</c> (Leased): requests cancellation of a job. The job ends as <c>cancelled</c> when its work returns.
/// </summary>
public sealed class JobCancelTool : LabTool<JobCancelArgs>
{
    private readonly JobRunner _jobs;

    /// <summary>
    /// Creates the tool over the job runner.
    /// </summary>
    public JobCancelTool(JobRunner jobs)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    /// <inheritdoc />
    public override string Name => "job_cancel";

    /// <inheritdoc />
    public override string Description => "Requests cancellation of a lab job. Returns job_id and state.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(JobCancelArgs args, LabCallContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.JobId))
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] job_id is required"));
        }

        Result<JobRecord> record = _jobs.Cancel(args.JobId);
        return Task.FromResult(record.IsFailure
            ? Result<object>.Failure(record.Error)
            : Result<object>.Success(new JobCancelResult(record.Value!.JobId, record.Value.State)));
    }
}

/// <summary>The <c>job_cancel</c> payload.</summary>
public sealed record JobCancelResult(string JobId, string State);
