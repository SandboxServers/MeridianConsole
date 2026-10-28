using System.Text;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Observe;

/// <summary>
/// The observation recorder (C18). One singleton; one open <c>events.jsonl</c> per run. Every append takes the run's lock,
/// assigns <see cref="LabObservation.Seq"/> from the run's counter (never from the clock), redacts <c>Args</c> and
/// <c>Result</c>, writes one line and flushes it.
/// </summary>
public interface ILabObservationSink
{
    /// <summary>
    /// Opens <c>&lt;runFolder&gt;\events.jsonl</c> for a new run. A second begin for the same run id fails.
    /// </summary>
    Result BeginRun(string runId, string runFolder);

    /// <summary>
    /// Closes every open op as <c>Unknown</c>, flushes and closes the file. Returns the number of lines in the run.
    /// </summary>
    Task<Result<long>> EndRunAsync(string runId, CancellationToken ct);

    /// <summary>
    /// The next operation id for an actor in a run.
    /// </summary>
    OpId NextOp(string runId, string actor);

    /// <summary>
    /// Appends one observation. <c>draft.Seq</c> is ignored. Fails when the run is not open or the run is truncated.
    /// </summary>
    Result<long> Append(LabObservation draft);

    /// <summary>
    /// Returns the lines of a run (open or closed) whose <c>Seq</c> is greater than <paramref name="afterSeq"/>, at most <paramref name="max"/>.
    /// </summary>
    IReadOnlyList<LabObservation> Read(string runId, long afterSeq, int max);
}

/// <summary>
/// The observation sink. Open runs are kept in memory until <see cref="EndRunAsync"/>; a closed run stays readable by id.
/// </summary>
public sealed class LabObservationSink : ILabObservationSink, IDisposable
{
    /// <summary>
    /// Lines a run may hold. The line after the last one is a single <c>truncated</c> note.
    /// </summary>
    public const int MaxObservationsPerRun = 100_000;

    /// <summary>
    /// The observation file name inside a run folder.
    /// </summary>
    public const string FileName = "events.jsonl";

    private const string TruncatedNote = "truncated";

    private readonly Redactor _redactor;
    private readonly TimeProvider _time;
    private readonly IPathValidator _validator;
    private readonly IEnumerable<string> _allowedRoots;
    private readonly Dictionary<string, RunState> _runs = new(StringComparer.Ordinal);
    private readonly object _registryGate = new();
    private bool _disposed;

    /// <summary>
    /// Creates the sink. Run folders must sit under one of <paramref name="allowedRoots"/>.
    /// </summary>
    public LabObservationSink(Redactor redactor, TimeProvider time, IPathValidator validator, IEnumerable<string> allowedRoots)
    {
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _allowedRoots = (allowedRoots ?? throw new ArgumentNullException(nameof(allowedRoots))).ToArray();
    }

    /// <inheritdoc />
    public Result BeginRun(string runId, string runFolder)
    {
        if (string.IsNullOrWhiteSpace(runId) || runId.IndexOfAny(['/', '\\', '.']) >= 0)
        {
            return Result.Failure("[Lab.InvalidArgument] run id is not valid");
        }

        Result<string> folder = _validator.ValidatePath(runFolder ?? string.Empty, _allowedRoots);
        if (folder.IsFailure)
        {
            return Result.Failure(folder.Error);
        }

        lock (_registryGate)
        {
            if (_runs.ContainsKey(runId))
            {
                return Result.Failure("[Lab.Conflict] the run already has an observation file");
            }

            try
            {
                Directory.CreateDirectory(folder.Value);
                string path = Path.Combine(folder.Value, FileName);
                var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = false };
                _runs[runId] = new RunState(runId, path, stream, writer);
                return Result.Success();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Result.Failure("[Lab.Unavailable] the observation file could not be created");
            }
        }
    }

    /// <inheritdoc />
    public Task<Result<long>> EndRunAsync(string runId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        RunState? state = Find(runId);
        if (state is null)
        {
            return Task.FromResult(Result<long>.Failure("[Lab.NotFound] no open observation run with that id"));
        }

        lock (state.Gate)
        {
            if (state.Closed)
            {
                return Task.FromResult(Result<long>.Failure("[Lab.NotFound] no open observation run with that id"));
            }

            try
            {
                foreach (KeyValuePair<string, OpenOp> pending in state.Open.ToArray())
                {
                    // Closing an op is bookkeeping, not an append by a caller, so the cap does not drop it.
                    WriteLocked(state, new LabObservation(
                        0, runId, _time.GetUtcNow(), pending.Value.Actor, pending.Key, ObservationKind.Complete,
                        pending.Value.Op, EmptyObject(), OpOutcome.Unknown, ParseObject("{\"reason\":\"run ended\"}"),
                        pending.Value.CausedBy));
                }

                state.Open.Clear();
                state.Writer.Flush();
                state.Writer.Dispose();
                state.Stream.Dispose();
                state.Closed = true;
                return Task.FromResult(Result<long>.Success(state.Counter));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Task.FromResult(Result<long>.Failure("[Lab.Unavailable] the observation file could not be written"));
            }
        }
    }

    /// <inheritdoc />
    public OpId NextOp(string runId, string actor)
    {
        RunState? state = Find(runId);
        if (state is null)
        {
            // An unknown run has no counter; the zero sequence marks an op that was never recorded.
            return new OpId(runId, actor, 0);
        }

        lock (state.Gate)
        {
            state.OpCounter++;
            return new OpId(runId, actor, state.OpCounter);
        }
    }

    /// <inheritdoc />
    public Result<long> Append(LabObservation draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RunState? state = Find(draft.RunId);
        if (state is null)
        {
            return Result<long>.Failure("[Lab.NotFound] no open observation run with that id");
        }

        lock (state.Gate)
        {
            if (state.Closed)
            {
                return Result<long>.Failure("[Lab.NotFound] no open observation run with that id");
            }

            if (state.Truncated)
            {
                return Result<long>.Failure("[Lab.Truncated] the run holds its maximum number of observations");
            }

            if (state.Counter >= MaxObservationsPerRun)
            {
                state.Truncated = true;
                WriteLocked(state, new LabObservation(
                    0, state.RunId, _time.GetUtcNow(), ActorIds.Lab, null, ObservationKind.Note, TruncatedNote,
                    EmptyObject(), null, EmptyObject(), null));
                return Result<long>.Failure("[Lab.Truncated] the run holds its maximum number of observations");
            }

            try
            {
                long seq = WriteLocked(state, draft);
                return Result<long>.Success(seq);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Result<long>.Failure("[Lab.Unavailable] the observation file could not be written");
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<LabObservation> Read(string runId, long afterSeq, int max)
    {
        RunState? state = Find(runId);
        if (state is null || max <= 0)
        {
            return [];
        }

        var lines = new List<LabObservation>();
        lock (state.Gate)
        {
            if (!state.Closed)
            {
                state.Writer.Flush();
            }

            // The writer holds the file for writing, so a reader must share write access.
            using var stream = new FileStream(state.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                LabObservation? observation = JsonSerializer.Deserialize<LabObservation>(line, ObserveJson.Options);
                if (observation is null || observation.Seq <= afterSeq)
                {
                    continue;
                }

                lines.Add(observation);
                if (lines.Count >= max)
                {
                    break;
                }
            }
        }

        return lines;
    }

    /// <summary>
    /// Closes every run that is still open, without writing completions. Used at daemon shutdown.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_registryGate)
        {
            foreach (RunState state in _runs.Values)
            {
                lock (state.Gate)
                {
                    if (!state.Closed)
                    {
                        state.Closed = true;
                        state.Writer.Dispose();
                        state.Stream.Dispose();
                    }
                }
            }
        }
    }

    private RunState? Find(string runId)
    {
        if (string.IsNullOrEmpty(runId))
        {
            return null;
        }

        lock (_registryGate)
        {
            return _runs.TryGetValue(runId, out RunState? state) ? state : null;
        }
    }

    /// <summary>
    /// Assigns the next sequence, redacts, writes one line and flushes. The caller holds the run's lock.
    /// </summary>
    private long WriteLocked(RunState state, LabObservation draft)
    {
        state.Counter++;
        var line = new LabObservation(
            state.Counter,
            state.RunId,
            _time.GetUtcNow(),
            draft.Actor,
            draft.OpId,
            draft.Kind,
            draft.Op,
            _redactor.Redact(Normalise(draft.Args)),
            draft.Outcome,
            _redactor.Redact(Normalise(draft.Result)),
            draft.CausedBy);

        state.Writer.WriteLine(JsonSerializer.Serialize(line, ObserveJson.Options));
        state.Writer.Flush();
        TrackOpen(state, draft);
        return state.Counter;
    }

    private static void TrackOpen(RunState state, LabObservation line)
    {
        if (line.OpId is null)
        {
            return;
        }

        if (line.Kind == ObservationKind.Invoke)
        {
            state.Open[line.OpId] = new OpenOp(line.Actor, line.Op, line.CausedBy);
        }
        else if (line.Kind == ObservationKind.Complete)
        {
            state.Open.Remove(line.OpId);
        }
    }

    private static JsonElement Normalise(JsonElement element) =>
        element.ValueKind == JsonValueKind.Undefined ? EmptyObject() : element;

    private static JsonElement EmptyObject() => ParseObject("{}");

    private static JsonElement ParseObject(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed record OpenOp(string Actor, string Op, string? CausedBy);

    private sealed class RunState(string runId, string path, FileStream stream, StreamWriter writer)
    {
        public string RunId { get; } = runId;

        public string Path { get; } = path;

        public FileStream Stream { get; } = stream;

        public StreamWriter Writer { get; } = writer;

        public object Gate { get; } = new();

        public long Counter { get; set; }

        public long OpCounter { get; set; }

        public bool Truncated { get; set; }

        public bool Closed { get; set; }

        public Dictionary<string, OpenOp> Open { get; } = new(StringComparer.Ordinal);
    }
}
