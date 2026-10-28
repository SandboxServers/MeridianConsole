using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

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

/// <summary>Arguments of <c>results_flake_check</c>. <c>rows</c> defaults to the run's failed rows; <c>reruns</c> defaults to 2.</summary>
public sealed record FlakeCheckArgs(string? RunId = null, IReadOnlyList<string>? Rows = null, int? Reruns = null, string? LeaseId = null);

/// <summary>The <c>results_flake_check</c> payload.</summary>
public sealed record FlakeCheckStarted(string JobId);

/// <summary>
/// <c>results_flake_check</c> (OwnLease): starts a flake check job over one run and returns its <c>job_id</c>. The job takes
/// its own lease, <c>job/&lt;job_id&gt;</c>; a caller's <c>lease_id</c> is checked by the dispatcher and not used further.
/// <c>reruns</c> is 1 to 5 (default 2); <c>rows</c> defaults to the run's failed rows.
/// </summary>
public sealed class FlakeCheckTool : LabTool<FlakeCheckArgs>
{
    private readonly JobRunner _jobs;
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Creates the tool. The dispatcher is resolved at call time because the tool registry contains this tool and the
    /// dispatcher depends on the registry.
    /// </summary>
    public FlakeCheckTool(JobRunner jobs, LabPaths paths, IPathValidator validator, IServiceProvider services)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public override string Name => "results_flake_check";

    /// <inheritdoc />
    public override string Description => "Starts a flake check: reruns the named rows (default: the run's failed rows) of run_id with the same seed and the current build, reruns times (1 to 5, default 2), through uat_run. Returns job_id; follow it with job_status.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(FlakeCheckArgs args, LabCallContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.RunId))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] run_id is required");
        }

        Result<FlakeCheckPlan> plan = await FlakeCheckJob.PlanAsync(_paths.UatRuns, _validator, args.RunId, args.Rows,
            args.Reruns ?? FlakeCheckJob.DefaultReruns, ct).ConfigureAwait(false);
        if (plan.IsFailure)
        {
            return Result<object>.Failure(plan.Error);
        }

        FlakeCheckPlan chosen = plan.Value!;
        Result<JobHandle> started = _jobs.Start(FlakeCheckJob.Kind, chosen.Seed, (jobContext, token) =>
            FlakeCheckJob.RunAsync(chosen, jobContext,
                (name, arguments, callContext, callToken) =>
                    _services.GetRequiredService<LabToolDispatcher>().InvokeAsync(name, arguments, callContext, callToken),
                _paths.UatRuns, _validator, token));
        return started.IsFailure
            ? Result<object>.Failure(started.Error)
            : Result<object>.Success(new FlakeCheckStarted(started.Value!.JobId));
    }
}
