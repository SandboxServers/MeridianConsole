using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Audit;

/// <summary>
/// Append-only JSON-lines audit log. Each event is one line <c>{ts, name, ...fields}</c> in
/// <c>audit-yyyyMMdd.jsonl</c> under <see cref="LabPaths.Audit"/>, using the UTC day of the event. The log keeps one
/// file open for the current day and disposes it on roll-over. Every value passes through <see cref="Redactor"/>.
/// Entries never carry tool arguments; <see cref="RecordCall"/> has no such parameter.
/// </summary>
/// <remarks>
/// <see cref="ILabEvents.Emit"/> cannot return a failure, so an IO failure is kept in <see cref="LastFailure"/>
/// and the next successful write clears it. <see cref="RecordCall"/> returns the failure directly.
/// </remarks>
public sealed class AuditLog : ILabEvents, IDisposable
{
    private const string TimestampField = "ts";
    private const string NameField = "name";

    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;
    private readonly Redactor _redactor;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private FileStream? _stream;
    private string? _streamDay;
    private bool _disposed;

    /// <summary>
    /// Creates an audit log writing under <see cref="LabPaths.Audit"/>.
    /// </summary>
    public AuditLog(LabPaths paths, IPathValidator validator, Redactor redactor, TimeProvider time)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// The last write failure, or null when the last write succeeded.
    /// </summary>
    public string? LastFailure { get; private set; }

    /// <summary>
    /// Writes one event. A write failure is kept in <see cref="LastFailure"/>; nothing is thrown.
    /// </summary>
    public void Emit(LabEvent labEvent)
    {
        ArgumentNullException.ThrowIfNull(labEvent);
        _ = Write(labEvent);
    }

    /// <summary>
    /// Writes one event and returns this write's own result. A caller that must refuse work when its audit line cannot be
    /// written decides on this result, never on <see cref="LastFailure"/>, which another thread may have changed.
    /// </summary>
    public Result TryEmit(LabEvent labEvent)
    {
        ArgumentNullException.ThrowIfNull(labEvent);
        return Write(labEvent);
    }

    /// <summary>
    /// Writes a <c>lab.call</c> event for one finished tool call. The entry records the tool name, gate, caller,
    /// lease owner, outcome and duration. It never records the arguments.
    /// </summary>
    public Result RecordCall(string callId, string tool, LabGate? gate, string caller, string? leaseOwner, string outcome, long durationMs)
    {
        ArgumentNullException.ThrowIfNull(callId);
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(outcome);

        var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["call_id"] = callId,
            ["tool"] = tool,
            ["gate"] = gate?.ToString(),
            ["caller"] = caller,
            ["lease_owner"] = leaseOwner,
            ["outcome"] = outcome,
            ["duration_ms"] = durationMs.ToString(CultureInfo.InvariantCulture),
        };

        return Write(new LabEvent("lab.call", _time.GetUtcNow(), fields));
    }

    /// <summary>
    /// Closes the current day's file. Later writes fail with <c>[Lab.Audit]</c>.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            CloseStream();
            _disposed = true;
        }
    }

    private Result Write(LabEvent labEvent)
    {
        lock (_gate)
        {
            var result = WriteLocked(labEvent);
            LastFailure = result.IsFailure ? result.Error : null;
            return result;
        }
    }

    private Result WriteLocked(LabEvent labEvent)
    {
        if (_disposed)
        {
            return Result.Failure("[Lab.Audit] audit log is closed");
        }

        var day = labEvent.At.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        try
        {
            if (_stream is null || !string.Equals(_streamDay, day, StringComparison.Ordinal))
            {
                var opened = OpenDay(day);
                if (opened.IsFailure)
                {
                    return opened;
                }
            }

            var line = SerializeLine(labEvent);
            _stream!.Write(line.WrittenSpan);
            _stream.WriteByte((byte)'\n');
            _stream.Flush();
            return Result.Success();
        }
        catch (IOException)
        {
            CloseStream();
            return Result.Failure("[Lab.Audit] write failed");
        }
        catch (UnauthorizedAccessException)
        {
            CloseStream();
            return Result.Failure("[Lab.Audit] write denied");
        }
    }

    private Result OpenDay(string day)
    {
        // Roll-over: the previous day's file is closed before the new one opens.
        CloseStream();

        // Owner-only on Unix; the root directory's own mode is set by the daemon start packet.
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(_paths.Audit);
        }
        else
        {
            Directory.CreateDirectory(_paths.Audit, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var candidate = Path.Combine(_paths.Audit, $"audit-{day}.jsonl");
        var validated = _validator.ValidatePath(candidate, [_paths.Audit]);
        if (validated.IsFailure)
        {
            return Result.Failure("[Lab.Audit] path refused: " + validated.Error);
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.Append,
            Access = FileAccess.Write,
            Share = FileShare.Read,
        };

        // The setter throws on Windows, so the Unix mode is only set where it applies.
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        _stream = new FileStream(validated.Value, options);
        _streamDay = day;
        return Result.Success();
    }

    private void CloseStream()
    {
        _stream?.Dispose();
        _stream = null;
        _streamDay = null;
    }

    private ArrayBufferWriter<byte> SerializeLine(LabEvent labEvent)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(TimestampField, labEvent.At.ToUniversalTime());
            writer.WriteString(NameField, _redactor.Redact(labEvent.Name));

            foreach (var (key, value) in labEvent.Fields)
            {
                // A field never overwrites the envelope.
                if (key is TimestampField or NameField)
                {
                    continue;
                }

                // A key is redacted like a value; a key that redacts to the envelope name is skipped too.
                var fieldName = _redactor.Redact(key);
                if (fieldName is TimestampField or NameField)
                {
                    continue;
                }

                if (value is null)
                {
                    writer.WriteNull(fieldName);
                }
                else
                {
                    writer.WriteString(fieldName, _redactor.Redact(value));
                }
            }

            writer.WriteEndObject();
        }

        return buffer;
    }
}
