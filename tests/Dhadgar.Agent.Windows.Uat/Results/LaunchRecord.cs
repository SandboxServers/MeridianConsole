using System.Text.Json;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Results;

/// <summary>
/// <c>launch.json</c> in the run folder (D-S239): who started the folder, written before the first manifest entry and
/// never overwritten. Agent cleanup trusts only this file. <see cref="LaunchedBy"/> is <c>user</c> or <c>agent</c>.
/// </summary>
internal sealed record LaunchRecord(
    int SchemaVersion,
    string RunId,
    string Verb,
    string? Mode,
    string LaunchedBy,
    bool Elevated,
    string GitSha,
    DateTime StartedUtc)
{
    /// <summary>The file name in the run folder.</summary>
    public const string FileName = "launch.json";

    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Writes the record once. The content goes to a temp file in the run folder, then moves to <c>launch.json</c> with
    /// <c>overwrite: false</c>, so a second write fails and keeps the first file (R-RUNDIR).
    /// </summary>
    public static Result Write(RunPaths paths, LaunchRecord record)
    {
        var target = paths.Validate(Path.Combine(paths.RunDir, FileName));
        if (target.IsFailure)
        {
            return Result.Failure($"[Launch.Write] {target.Error}");
        }

        var temp = paths.Validate(Path.Combine(paths.RunDir, $"{FileName}.{Guid.NewGuid():N}.tmp"));
        if (temp.IsFailure)
        {
            return Result.Failure($"[Launch.Write] {temp.Error}");
        }

        try
        {
            using (var stream = new FileStream(temp.Value, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, record, JsonOptions);
                stream.Flush(true);
            }

            File.Move(temp.Value, target.Value, overwrite: false);
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            TryDelete(temp.Value);
            return Result.Failure($"[Launch.Write] could not write {FileName}: {ex.Message}");
        }
    }

    /// <summary>Loads and validates the record. A missing file is a Failure, never a null success (R-NONULL).</summary>
    public static Result<LaunchRecord> Load(RunPaths paths)
    {
        var path = paths.Validate(Path.Combine(paths.RunDir, FileName));
        if (path.IsFailure)
        {
            return Result<LaunchRecord>.Failure($"[Launch.Missing] {path.Error}");
        }

        if (!File.Exists(path.Value))
        {
            return Result<LaunchRecord>.Failure($"[Launch.Missing] {FileName} is not in the run folder");
        }

        try
        {
            using var stream = new FileStream(path.Value, FileMode.Open, FileAccess.Read, FileShare.Read);
            var record = JsonSerializer.Deserialize<LaunchRecord>(stream, JsonOptions);
            if (record is null)
            {
                return Result<LaunchRecord>.Failure($"[Launch.Invalid] {FileName} is empty");
            }

            if (record.SchemaVersion != CurrentSchemaVersion)
            {
                return Result<LaunchRecord>.Failure($"[Launch.Invalid] {FileName} schemaVersion {record.SchemaVersion} is not {CurrentSchemaVersion}");
            }

            if (record.LaunchedBy is not ("user" or "agent"))
            {
                return Result<LaunchRecord>.Failure($"[Launch.Invalid] {FileName} launchedBy is not user or agent");
            }

            return Result<LaunchRecord>.Success(record);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return Result<LaunchRecord>.Failure($"[Launch.Invalid] {FileName} could not be read: {ex.Message}");
        }
    }

    /// <summary>True when <c>launch.json</c> already exists in the run folder.</summary>
    public static bool Exists(RunPaths paths) => File.Exists(Path.Combine(paths.RunDir, FileName));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The temp file is inert; a failed delete leaves it in the run folder for the user to read.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }
}
