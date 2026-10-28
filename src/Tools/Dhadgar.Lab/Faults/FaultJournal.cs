using System.Text.Json;
using System.Text.Json.Serialization;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Faults;

/// <summary>
/// Where a journalled fault is. <c>Prepared</c> is written before anything changes a target; <c>Active</c> once inject succeeded;
/// <c>Healing</c> while a heal runs; <c>HealFailed</c> when the heal failed twice and the entry is kept for recovery.
/// </summary>
public enum FaultState
{
    /// <summary>Journalled before any change to the target.</summary>
    Prepared,

    /// <summary>The fault is in place on the target.</summary>
    Active,

    /// <summary>A heal is running.</summary>
    Healing,

    /// <summary>The heal failed; the entry stays journalled with <c>HealError</c>.</summary>
    HealFailed,
}

/// <summary>
/// One journalled fault (the write-ahead record, D-ML46). <see cref="HealData"/> is what heal needs, computed by
/// <see cref="IFaultKind.PrepareAsync"/> without changing the target. The entry never carries a lease id.
/// </summary>
public sealed record FaultJournalEntry(
    string FaultId,
    string Kind,
    string TargetId,
    JsonElement Args,
    JsonElement HealData,
    FaultState State,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline,
    string? HealError);

/// <summary>
/// The durable store of journalled faults. Reads return an empty list when no journal exists.
/// </summary>
public interface IFaultJournal
{
    /// <summary>
    /// Reads every journalled entry.
    /// </summary>
    Task<Result<IReadOnlyList<FaultJournalEntry>>> ReadAsync(CancellationToken ct);

    /// <summary>
    /// Replaces the journal with <paramref name="entries"/>, durably.
    /// </summary>
    Task<Result> WriteAsync(IReadOnlyList<FaultJournalEntry> entries, CancellationToken ct);
}

/// <summary>
/// The journal at <c>LabPaths.State\faults.json</c>. A write goes to a temp file in the same folder, is flushed to disk, and
/// then moved over the journal, so an interrupted write never leaves a partial <c>faults.json</c>.
/// </summary>
public sealed class FaultJournal : IFaultJournal
{
    /// <summary>
    /// The journal file name inside the state folder.
    /// </summary>
    public const string FileName = "faults.json";

    private static readonly JsonSerializerOptions Options = Create();

    private readonly string _path;

    /// <summary>
    /// Creates the journal over the lab's state folder.
    /// </summary>
    public FaultJournal(LabPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _path = System.IO.Path.Combine(paths.State, FileName);
    }

    /// <summary>
    /// The full path of the journal file.
    /// </summary>
    public string FilePath => _path;

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<FaultJournalEntry>>> ReadAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_path))
            {
                return Result<IReadOnlyList<FaultJournalEntry>>.Success([]);
            }

            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
            var entries = await JsonSerializer.DeserializeAsync<List<FaultJournalEntry>>(stream, Options, ct).ConfigureAwait(false);
            return Result<IReadOnlyList<FaultJournalEntry>>.Success(entries ?? []);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // The message can carry file content, so only the exception type is returned.
            return Result<IReadOnlyList<FaultJournalEntry>>.Failure($"[Lab.Journal] fault journal is not readable ({ex.GetType().Name})");
        }
    }

    /// <summary>
    /// Forces the bytes to the disk. Kept out of the async write: <see cref="FileStream.Flush(bool)"/> blocks, and this is the only call that reaches the disk.
    /// </summary>
    private static void FlushToDisk(FileStream stream) => stream.Flush(flushToDisk: true);

    /// <inheritdoc />
    public async Task<Result> WriteAsync(IReadOnlyList<FaultJournalEntry> entries, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entries);
        string? temp = null;
        try
        {
            var folder = System.IO.Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(folder);
            temp = System.IO.Path.Combine(folder, $"{FileName}.{Guid.NewGuid():N}.tmp");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(entries, Options);

            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                FlushToDisk(stream);
            }

            File.Move(temp, _path, overwrite: true);
            temp = null;
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Result.Failure($"[Lab.Journal] fault journal could not be written ({ex.GetType().Name})");
        }
        finally
        {
            if (temp is not null)
            {
                TryDelete(temp);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The temp file is harmless; the next write uses a new name.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
