using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Lab.Jobs;

/// <summary>
/// The work a job runs. It returns a failure for an expected error and observes <see cref="JobContext.Token"/>.
/// </summary>
public delegate Task<Result<JobOutcome>> JobWork(JobContext context, CancellationToken ct);

/// <summary>
/// What a finished job reports: its overall grade (<c>pass</c>, <c>weak</c>, <c>fail</c> or <c>incomplete</c>) and an optional result.
/// </summary>
public sealed record JobOutcome(string Overall, JsonNode? Result);

/// <summary>
/// The context a job's work receives. <see cref="LeaseId"/> is the job's own lease: it is passed to lab tools through
/// <c>LabToolDispatcher</c> and is never logged or written to a file.
/// </summary>
public sealed class JobContext
{
    private readonly JobRunner _runner;

    internal JobContext(JobRunner runner, string jobId, string kind, ulong seed, string leaseId, string folder, CancellationToken token)
    {
        _runner = runner;
        JobId = jobId;
        Kind = kind;
        Seed = seed;
        LeaseId = leaseId;
        Folder = folder;
        Token = token;
    }

    /// <summary>The job id (12 lowercase hex characters).</summary>
    public string JobId { get; }

    /// <summary>The job kind, for example <c>flake_check</c> or <c>hunt</c>. Also the bundle's <c>runs.kind</c>.</summary>
    public string Kind { get; }

    /// <summary>The run seed.</summary>
    public ulong Seed { get; }

    /// <summary>The job's own lease id. Never log it or persist it.</summary>
    public string LeaseId { get; }

    /// <summary>The job folder, <c>LabPaths.Jobs\&lt;job_id&gt;</c>.</summary>
    public string Folder { get; }

    /// <summary>Cancelled by <c>job_cancel</c>, by the lease ending, or by the lease's renewal failing.</summary>
    public CancellationToken Token { get; }

    /// <summary>Sets the progress label shown by <c>job_status</c>.</summary>
    public void Report(string progress) => _runner.SetProgress(JobId, progress);
}

/// <summary>
/// A job as <c>job_status</c> reports it. Carries no lease id.
/// </summary>
public sealed record JobRecord(string JobId, string Kind, ulong Seed, string State, DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt, string? Progress, JsonNode? Result);

/// <summary>
/// Returned by <see cref="JobRunner.Start"/>: the job id and a task that completes when the job has ended.
/// </summary>
public sealed record JobHandle(string JobId, Task Completed);

/// <summary>
/// Runs one lab job at a time (C4, OwnLease semantics like <c>uat_run</c>). The job holds its own lease <c>job/&lt;job_id&gt;</c>,
/// renews it while it runs, and writes <c>job.json</c> and a <c>run.json</c> bundle under <c>LabPaths.Jobs\&lt;job_id&gt;</c>. The
/// bundle is ingested when the job ends. Ending the lease (released, expired or taken over) cancels the job (L27 hook).
/// A second job is refused with <c>[Lab.JobRunning] &lt;job_id&gt;</c>.
/// </summary>
public sealed class JobRunner : ILeaseEndHook, ILabStatusSource, IDisposable
{
    /// <summary>The job lease's TTL, seconds.</summary>
    internal const int LeaseTtlSeconds = 120;

    /// <summary>How often the running job renews its lease, seconds.</summary>
    internal const int RenewSeconds = 30;

    private const string OwnerPrefix = "job/";
    private const int MaxKindLength = 40;

    private readonly LabPaths _paths;
    private readonly LeaseBook _leases;
    private readonly IPathValidator _validator;
    private readonly TimeProvider _time;
    private readonly ResultsService? _results;
    private readonly ILogger<JobRunner> _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ActiveJob> _jobs = new(StringComparer.Ordinal);
    private ActiveJob? _current;
    private bool _disposed;

    /// <summary>
    /// Creates the runner. With <paramref name="results"/> every ended job's bundle is ingested; null skips the ingest.
    /// </summary>
    public JobRunner(LabPaths paths, LeaseBook leases, IPathValidator validator, TimeProvider time, ILogger<JobRunner> logger,
        ResultsService? results = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _results = results;
    }

    /// <summary>
    /// Starts a job. Refuses a second job while one runs. Returns a failure (never throws) for a held lease, an invalid kind,
    /// or a folder or file error.
    /// </summary>
    /// <param name="kind">The job kind.</param>
    /// <param name="seed">The run seed, written to <c>job.json</c>.</param>
    /// <param name="work">The job's work.</param>
    public Result<JobHandle> Start(string kind, ulong seed, JobWork work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > MaxKindLength)
        {
            return Result<JobHandle>.Failure("[Lab.InvalidArgument] kind must be 1 to 40 characters");
        }

        string jobId = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        Result<string> folder = _validator.ValidatePath(Path.Combine(_paths.Jobs, jobId), [_paths.Jobs]);
        if (folder.IsFailure)
        {
            return Result<JobHandle>.Failure("[Lab.InvalidArgument] the job folder is outside the lab jobs root");
        }

        lock (_gate)
        {
            if (_current is not null)
            {
                return Result<JobHandle>.Failure("[Lab.JobRunning] " + _current.JobId);
            }

            Result<LeaseGrant> grant = _leases.Acquire(OwnerPrefix + jobId, kind, LeaseTtlSeconds, false, null);
            if (grant.IsFailure)
            {
                return Result<JobHandle>.Failure(grant.Error);
            }

            var job = new ActiveJob(jobId, kind, seed, grant.Value!.LeaseId, folder.Value!, _time.GetUtcNow());
            Result written = WriteJobFile(job);
            if (written.IsFailure)
            {
                _ = _leases.Release(job.LeaseId);
                return Result<JobHandle>.Failure(written.Error);
            }

            _jobs[jobId] = job;
            _current = job;
            var context = new JobContext(this, jobId, kind, seed, job.LeaseId, job.Folder, job.Cancellation.Token);
            job.Completed = Task.Run(() => RunAsync(job, work, context));
            _logger.LogInformation("lab job {Kind} started", kind);
            return Result<JobHandle>.Success(new JobHandle(jobId, job.Completed));
        }
    }

    /// <summary>
    /// The job with <paramref name="jobId"/>, or the running job when <paramref name="jobId"/> is null.
    /// </summary>
    public Result<JobRecord> Get(string? jobId)
    {
        lock (_gate)
        {
            ActiveJob? job = jobId is null ? _current : Find(jobId);
            if (job is null)
            {
                return Result<JobRecord>.Failure(jobId is null ? "[Lab.NotFound] no job is running" : "[Lab.NotFound] no such job");
            }

            return Result<JobRecord>.Success(job.Snapshot());
        }
    }

    /// <summary>
    /// Every job this daemon has run, oldest first.
    /// </summary>
    public IReadOnlyList<JobRecord> List()
    {
        lock (_gate)
        {
            return _jobs.Values.OrderBy(j => j.StartedAt).Select(j => j.Snapshot()).ToList();
        }
    }

    /// <summary>
    /// Requests cancellation of the job. The job ends as <c>cancelled</c> when its work returns.
    /// </summary>
    public Result<JobRecord> Cancel(string jobId)
    {
        lock (_gate)
        {
            ActiveJob? job = Find(jobId);
            if (job is null)
            {
                return Result<JobRecord>.Failure("[Lab.NotFound] no such job");
            }

            if (job.State == JobState.Running)
            {
                _ = job.Cancellation.CancelAsync();
            }

            return Result<JobRecord>.Success(job.Snapshot());
        }
    }

    /// <summary>
    /// Cancels the running job when its own lease ends. Ignores every other lease.
    /// </summary>
    public Task OnLeaseEndedAsync(LeaseHistoryEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            if (_current is { State: JobState.Running } job && entry.Owner == OwnerPrefix + job.JobId)
            {
                _ = job.Cancellation.CancelAsync();
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Contribute(StatusBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        lock (_gate)
        {
            builder.RunningJob = _current?.JobId;
        }
    }

    /// <summary>
    /// Sets the progress label of a job. Called by <see cref="JobContext.Report"/>.
    /// </summary>
    internal void SetProgress(string jobId, string progress)
    {
        lock (_gate)
        {
            if (Find(jobId) is { State: JobState.Running } job)
            {
                job.Progress = progress;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _ = _current?.Cancellation.CancelAsync();
        }
    }

    private async Task RunAsync(ActiveJob job, JobWork work, JobContext context)
    {
        Task renewal = RenewAsync(job);
        JobState state;
        JsonNode? result = null;
        string overall = "incomplete";
        try
        {
            Result<JobOutcome> outcome = await work(context, job.Cancellation.Token).ConfigureAwait(false);
            if (job.Cancellation.IsCancellationRequested)
            {
                state = JobState.Cancelled;
            }
            else if (outcome.IsFailure)
            {
                state = JobState.Failed;
                result = new JsonObject { ["error"] = ErrorCode(outcome.Error) };
            }
            else
            {
                state = JobState.Succeeded;
                overall = outcome.Value!.Overall;
                result = outcome.Value.Result;
            }
        }
        catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
        {
            state = JobState.Cancelled;
        }
        catch (Exception ex)
        {
            // Only the type is logged: a message can carry lease or path detail.
            _logger.LogWarning("lab job {Kind} failed with {ExceptionType}", job.Kind, ex.GetType().Name);
            state = JobState.Failed;
            result = new JsonObject { ["error"] = "[Lab.JobFailed]" };
        }

        _ = job.Stop.CancelAsync();
        try
        {
            await renewal.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The renewal loop stops when the job stops.
        }

        lock (_gate)
        {
            job.Finish(state, result, _time.GetUtcNow());
            _current = null;
        }

        if (state == JobState.Failed)
        {
            overall = "fail";
        }
        else if (state == JobState.Cancelled)
        {
            overall = "incomplete";
        }

        _ = WriteJobFile(job);
        _ = WriteRunFile(job, overall);
        _ = _leases.Release(job.LeaseId);
        if (_results is not null)
        {
            Result<IngestResult> ingested = await _results.IngestAsync(job.Folder, CancellationToken.None).ConfigureAwait(false);
            if (ingested.IsFailure)
            {
                _logger.LogWarning("lab job {Kind} bundle was not ingested", job.Kind);
            }
        }

        _logger.LogInformation("lab job {Kind} ended {State}", job.Kind, state.ToString().ToLowerInvariant());
        job.Cancellation.Dispose();
        job.Stop.Dispose();
    }

    private async Task RenewAsync(ActiveJob job)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(RenewSeconds), _time, job.Stop.Token).ConfigureAwait(false);
                if (_leases.Renew(job.LeaseId, LeaseTtlSeconds).IsFailure)
                {
                    // The lease is gone: the job cannot run without it.
                    _ = job.Cancellation.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (job.Stop.IsCancellationRequested)
        {
            // The job ended.
        }
    }

    private ActiveJob? Find(string jobId) => _jobs.TryGetValue(jobId, out ActiveJob? job) ? job : null;

    private Result WriteJobFile(ActiveJob job)
    {
        return WriteFile(job, "job.json", job.ToJson());
    }

    private Result WriteRunFile(ActiveJob job, string overall)
    {
        var run = new JsonObject
        {
            ["run_id"] = job.JobId,
            ["kind"] = job.Kind,
            ["seed"] = job.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["state"] = job.State.ToString().ToLowerInvariant(),
            ["overall"] = overall,
            ["started_at"] = job.StartedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["ended_at"] = job.EndedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };
        return WriteFile(job, "run.json", run);
    }

    private Result WriteFile(ActiveJob job, string name, JsonNode node)
    {
        Result<string> file = _validator.ValidatePath(Path.Combine(job.Folder, name), [_paths.Jobs]);
        if (file.IsFailure)
        {
            return Result.Failure("[Lab.InvalidArgument] the job file is outside the lab jobs root");
        }

        try
        {
            Directory.CreateDirectory(job.Folder);
            File.WriteAllText(file.Value!, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure("[Lab.IoError] the job file could not be written");
        }
    }

    private static string ErrorCode(string error)
    {
        int close = error.IndexOf(']', StringComparison.Ordinal);
        return error.StartsWith('[') && close > 0 ? error[..(close + 1)] : "[Lab.JobFailed]";
    }

    /// <summary>The job states.</summary>
    internal enum JobState { Running, Succeeded, Failed, Cancelled }

    private sealed class ActiveJob
    {
        public ActiveJob(string jobId, string kind, ulong seed, string leaseId, string folder, DateTimeOffset startedAt)
        {
            JobId = jobId;
            Kind = kind;
            Seed = seed;
            LeaseId = leaseId;
            Folder = folder;
            StartedAt = startedAt;
        }

        public string JobId { get; }
        public string Kind { get; }
        public ulong Seed { get; }
        public string LeaseId { get; }
        public string Folder { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset? EndedAt { get; private set; }
        public JobState State { get; private set; }
        public string? Progress { get; set; }
        public JsonNode? Result { get; private set; }
        public CancellationTokenSource Cancellation { get; } = new();
        public CancellationTokenSource Stop { get; } = new();
        public Task Completed { get; set; } = Task.CompletedTask;
        public string Owner => OwnerPrefix + JobId;

        public void Finish(JobState state, JsonNode? result, DateTimeOffset at)
        {
            State = state;
            Result = result;
            EndedAt = at;
        }

        public JobRecord Snapshot() => new(JobId, Kind, Seed, State.ToString().ToLowerInvariant(), StartedAt, EndedAt, Progress, Result);

        public JsonObject ToJson() => new()
        {
            ["job_id"] = JobId,
            ["kind"] = Kind,
            ["seed"] = Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["state"] = State.ToString().ToLowerInvariant(),
            ["started_at"] = StartedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["ended_at"] = EndedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["progress"] = Progress,
            ["result"] = Result?.DeepClone(),
        };
    }
}
