using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Lab.Results;

/// <summary>
/// The only owner of the results database (D-ML91): a singleton that opens <see cref="ResultsDb"/> lazily, builds the
/// <see cref="BundleIngestor"/> for each ingest, and serializes every database use behind one gate, because a
/// <c>SqliteConnection</c> is not thread-safe. Observers (A3) run after the gate is released, so an observer may call back
/// into <see cref="ReadAsync{T}"/> without deadlocking. An open failure is returned and retried on the next call.
/// </summary>
public sealed class ResultsService : IDisposable
{
    private const string KnownIssuesFile = "known-issues.toml";

    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly Redactor _redactor;
    private readonly IServiceProvider _services;
    private readonly ILoggerFactory _loggers;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ResultsDb? _db;
    private bool _disposed;

    /// <summary>
    /// Creates the service. Nothing is opened here; the database opens on first use.
    /// </summary>
    /// <param name="paths">The lab layout (the results root and the repo root).</param>
    /// <param name="validator">Validates every path the service reads.</param>
    /// <param name="redactor">Redacts every error text before it reaches a payload (R-LOG).</param>
    /// <param name="services">Resolves the <see cref="IIngestObserver"/> services at call time.</param>
    /// <param name="loggers">Creates the loggers of the ingest and the known-issue loader.</param>
    /// <param name="time">The clock for flake detection times.</param>
    public ResultsService(LabPaths paths, IPathValidator validator, Redactor redactor, IServiceProvider services,
        ILoggerFactory loggers, TimeProvider time)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Loads the shipped known-issue file under <c>Lab:RepoRoot</c>. Reloads on every call. An unchecked outcome carries one
    /// of a fixed set of reasons, never file content (D-ML92).
    /// </summary>
    public KnownIssueCheck LoadKnownIssues()
    {
        if (string.IsNullOrWhiteSpace(_paths.RepoRoot))
        {
            return KnownIssueCheck.Unchecked("Lab:RepoRoot is not set");
        }

        string path = Path.Combine(_paths.RepoRoot, "docs", "guides", "uat-specs", KnownIssuesFile);
        Result<string> validated = _validator.ValidatePath(path, [_paths.RepoRoot]);
        if (validated.IsFailure)
        {
            return KnownIssueCheck.Unchecked("known-issues.toml could not be read");
        }

        if (!File.Exists(validated.Value))
        {
            return KnownIssueCheck.Unchecked("known-issues.toml is absent under Lab:RepoRoot");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(validated.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return KnownIssueCheck.Unchecked("known-issues.toml could not be read");
        }

        try
        {
            string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Result<KnownIssues> loaded = KnownIssues.LoadText(KnownIssuesFile, Encoding.UTF8.GetString(bytes),
                _loggers.CreateLogger<ResultsService>());
            return loaded.IsFailure
                ? KnownIssueCheck.Unchecked("known-issues.toml is invalid; run the loader tests for the problems")
                : KnownIssueCheck.Of(loaded.Value, sha256);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    /// <summary>
    /// Ingests one bundle folder under the lab roots. The write runs under the gate; observers run after it is released.
    /// Returns a failure, never throws, for a path, IO or database error.
    /// </summary>
    /// <param name="bundleFolder">The bundle folder (under <c>LabPaths.UatRuns</c> or <c>LabPaths.Jobs</c>).</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task<Result<IngestResult>> IngestAsync(string bundleFolder, CancellationToken ct)
    {
        BundleIngestor ingestor;
        Result<IngestResult> written;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Result<ResultsDb> db = EnsureOpen();
            if (db.IsFailure)
            {
                return Result<IngestResult>.Failure(db.Error);
            }

            ingestor = new BundleIngestor(db.Value, _validator, LoadKnownIssues(), _redactor.Redact,
                _services.GetServices<IIngestObserver>(), _loggers.CreateLogger<BundleIngestor>(), _time);
            written = await ingestor.WriteAsync(bundleFolder, [_paths.UatRuns, _paths.Jobs], ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (written.IsSuccess)
        {
            await ingestor.NotifyAsync(written.Value.RunId, ct).ConfigureAwait(false);
        }

        return written;
    }

    /// <summary>
    /// Runs one read under the gate. Used by the query tools (step 7) and by observers that read the index (D-ML91).
    /// </summary>
    internal async Task<Result<T>> ReadAsync<T>(Func<ResultsDb, Result<T>> read, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Result<ResultsDb> db = EnsureOpen();
            return db.IsFailure ? Result<T>.Failure(db.Error) : read(db.Value);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The JSON an MCP tool returns for one ingest: <c>{"ok":true,"run_id","overall","rows","signatures","known_issues"}</c>
    /// on success, or <c>{"ok":false,"error"}</c> with the error redacted (R-LOG).
    /// </summary>
    public JsonObject ToPayload(Result<IngestResult> ingested)
    {
        if (ingested.IsFailure)
        {
            return new JsonObject
            {
                ["ok"] = false,
                ["error"] = _redactor.Redact(ingested.Error),
            };
        }

        IngestResult result = ingested.Value;
        return new JsonObject
        {
            ["ok"] = true,
            ["run_id"] = result.RunId,
            ["overall"] = result.Overall,
            ["rows"] = result.Rows,
            ["signatures"] = result.Signatures,
            ["known_issues"] = result.KnownIssues.ToJson(),
        };
    }

    /// <summary>Disposes the database when it is open, then the gate.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Wait();
        try
        {
            _db?.Dispose();
            _db = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    /// <summary>Opens the database on first use. Must be called with the gate held.</summary>
    private Result<ResultsDb> EnsureOpen()
    {
        if (_db is not null)
        {
            return Result<ResultsDb>.Success(_db);
        }

        Result<ResultsDb> opened = ResultsDb.Open(_paths.Results, _validator);
        if (opened.IsFailure)
        {
            return Result<ResultsDb>.Failure("[Lab.Unavailable] the results database is not available: " +
                _redactor.Redact(opened.Error));
        }

        _db = opened.Value;
        return Result<ResultsDb>.Success(_db);
    }
}
