using System.Text;
using System.Text.Json;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Cleanup;

/// <summary>
/// The append-only cleanup manifest (JSON Lines). Each entry is appended and flushed to disk before the
/// change it describes. A corrupt last line (a crash mid-write) is skipped with a warning.
/// </summary>
internal sealed class CleanupManifest
{
    /// <summary>JSON options for manifest lines (R-CONTRACT: JsonSerializerDefaults.Web).</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Creates a manifest bound to a file path. The file is created on the first append.</summary>
    public CleanupManifest(string path)
    {
        Path = path;
    }

    /// <summary>The manifest file.</summary>
    public string Path { get; }

    /// <summary>Appends one entry and flushes it to disk.</summary>
    public Result Append(CleanupEntry entry)
    {
        try
        {
            var line = JsonSerializer.Serialize(entry, Options) + "\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failure($"manifest append failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure($"manifest append denied: {ex.Message}");
        }
    }

    /// <summary>Loads the entries in append order. A missing file is an empty manifest.</summary>
    public static Result<IReadOnlyList<CleanupEntry>> Load(string path)
    {
        if (!File.Exists(path))
        {
            return Result<IReadOnlyList<CleanupEntry>>.Success([]);
        }

        try
        {
            var lines = File.ReadAllLines(path);
            var lastNonBlank = Array.FindLastIndex(lines, l => !string.IsNullOrWhiteSpace(l));
            var entries = new List<CleanupEntry>();
            for (var i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                CleanupEntry? entry;
                try
                {
                    entry = JsonSerializer.Deserialize<CleanupEntry>(lines[i], Options);
                }
                catch (JsonException)
                {
                    entry = null;
                }

                if (entry is not null)
                {
                    entries.Add(entry);
                }
                else if (i == lastNonBlank)
                {
                    Console.Error.WriteLine($"warning: skipped a truncated last line (line {i + 1}) of the cleanup manifest");
                }
                else
                {
                    return Result<IReadOnlyList<CleanupEntry>>.Failure($"cleanup manifest line {i + 1} is corrupt");
                }
            }

            return Result<IReadOnlyList<CleanupEntry>>.Success(entries);
        }
        catch (IOException ex)
        {
            return Result<IReadOnlyList<CleanupEntry>>.Failure($"cleanup manifest could not be read: {ex.Message}");
        }
    }
}
