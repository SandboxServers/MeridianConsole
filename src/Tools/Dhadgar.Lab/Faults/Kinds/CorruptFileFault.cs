using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Random;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Lab.Faults.Kinds;

/// <summary>
/// Arguments of the <c>corrupt_file</c> fault kind: the logical file (C10), the member (refused until L33), the mode
/// (<c>truncate</c>, <c>garbage</c> or <c>delete</c>), and the seed for <c>garbage</c> (the run seed; a new seed when absent).
/// </summary>
public sealed record CorruptFileArgs(string? File = null, string? Mode = null, string? Member = null, ulong? Seed = null);

/// <summary>
/// Heal data of a corrupt file: the logical name, the mode, the original length and SHA-256, and the seed used for garbage.
/// </summary>
public sealed record CorruptFileHealData(string File, string Mode, long Length, string Sha256, ulong Seed);

/// <summary>
/// The <c>corrupt_file</c> fault kind (<c>fault_corrupt_file</c>): corrupts one host logical file of the Nodes CA. Prepare copies
/// the file to <c>LabPaths.FaultBackups\&lt;fault_id&gt;\</c> and records its SHA-256; heal writes the backup back, verifies the
/// hash, zeroes the buffer and deletes the backup. Logical names resolve only through the table below (R-PATH).
/// </summary>
public sealed class CorruptFileFault(
    LabPaths paths,
    IPathValidator validator,
    IFaultJournal journal,
    ILogger<CorruptFileFault> logger) : IFaultKind
{
    private const string BackupSuffix = ".bak";
    private const string HashSuffix = ".sha256";
    private const long MaxFileBytes = 1024 * 1024;

    /// <summary>The fixed LabRandom stream for garbage bytes: the same seed writes the same bytes whatever the fault id (D-ML81).</summary>
    private const string GarbageStream = "corrupt_file:garbage";

    /// <summary>The logical files (C10) and their names under <c>LabPaths.State\nodes-ca</c>.</summary>
    internal static readonly IReadOnlyDictionary<string, string> LogicalFiles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["nodes-ca-key"] = "ca.key",
        ["nodes-ca-cert"] = "ca.crt",
    };

    private static readonly HashSet<string> Modes = new(StringComparer.Ordinal) { "truncate", "garbage", "delete" };

    private readonly LabPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IPathValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly IFaultJournal _journal = journal ?? throw new ArgumentNullException(nameof(journal));
    private readonly ILogger<CorruptFileFault> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string Kind => "corrupt_file";

    /// <inheritdoc />
    public async Task<Result<JsonElement>> PrepareAsync(ResolvedTarget target, JsonElement args, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        Result<CorruptFileArgs> parsed = KindSupport.FromHealData<CorruptFileArgs>(args);
        if (parsed.IsFailure)
        {
            return Result<JsonElement>.Failure(parsed.Error);
        }

        if (parsed.Value.Member is not null)
        {
            return Result<JsonElement>.Failure("[Lab.Unavailable] members arrive in L33");
        }

        if (parsed.Value.File is null || !LogicalFiles.ContainsKey(parsed.Value.File))
        {
            return Result<JsonElement>.Failure("[Lab.InvalidArgument] unknown logical file");
        }

        if (parsed.Value.Mode is null || !Modes.Contains(parsed.Value.Mode))
        {
            return Result<JsonElement>.Failure("[Lab.InvalidArgument] mode must be truncate, garbage or delete");
        }

        string logical = parsed.Value.File;
        Result<string> live = LivePath(logical);
        if (live.IsFailure)
        {
            return Result<JsonElement>.Failure(live.Error);
        }

        if (!File.Exists(live.Value))
        {
            return Result<JsonElement>.Failure("[Lab.NotFound] the logical file does not exist");
        }

        long length = new FileInfo(live.Value).Length;
        if (length > MaxFileBytes)
        {
            return Result<JsonElement>.Failure("[Lab.InvalidArgument] the logical file is too large to corrupt");
        }

        Result<string> folder = BackupFolder(faultId);
        if (folder.IsFailure)
        {
            return Result<JsonElement>.Failure(folder.Error);
        }

        string sha = HashOf(live.Value)!;
        try
        {
            Directory.CreateDirectory(folder.Value);
            File.Copy(live.Value, Path.Combine(folder.Value, logical + BackupSuffix), overwrite: true);
            await File.WriteAllTextAsync(Path.Combine(folder.Value, logical + HashSuffix), sha, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return Result<JsonElement>.Failure("[Lab.IO] the backup could not be written");
        }

        return KindSupport.ToHealData(new CorruptFileHealData(logical, parsed.Value.Mode, length, sha, parsed.Value.Seed ?? LabRandom.NewSeed()));
    }

    /// <inheritdoc />
    public async Task<Result> InjectAsync(ResolvedTarget target, JsonElement args, JsonElement healData, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        await Task.CompletedTask.ConfigureAwait(false);

        Result<CorruptFileHealData> heal = KindSupport.FromHealData<CorruptFileHealData>(healData);
        if (heal.IsFailure)
        {
            return Result.Failure(heal.Error);
        }

        Result<string> live = LivePath(heal.Value.File);
        if (live.IsFailure)
        {
            return Result.Failure(live.Error);
        }

        try
        {
            switch (heal.Value.Mode)
            {
                case "truncate":
                    File.WriteAllBytes(live.Value, []);
                    break;
                case "garbage":
                    WriteGarbage(live.Value, heal.Value);
                    break;
                default:
                    File.Delete(live.Value);
                    break;
            }
        }
        catch (IOException)
        {
            return Result.Failure("[Lab.IO] the logical file could not be corrupted");
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> HealAsync(FaultJournalEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await Task.CompletedTask.ConfigureAwait(false);

        Result<CorruptFileHealData> heal = KindSupport.FromHealData<CorruptFileHealData>(entry.HealData);
        if (heal.IsFailure)
        {
            return Result.Success();
        }

        Result<string> live = LivePath(heal.Value.File);
        Result<string> folder = BackupFolder(entry.FaultId);
        if (live.IsFailure || folder.IsFailure)
        {
            return Result.Failure(live.IsFailure ? live.Error : folder.Error);
        }

        // No backup means the fault was never injected or is already healed.
        return RestoreFromBackup(heal.Value.File, live.Value, folder.Value, heal.Value.Sha256);
    }

    /// <inheritdoc />
    public async Task<Result<int>> ReconcileAsync(CancellationToken ct)
    {
        Result<IReadOnlyList<FaultJournalEntry>> journalled = await _journal.ReadAsync(ct).ConfigureAwait(false);
        if (journalled.IsFailure)
        {
            return Result<int>.Failure(journalled.Error);
        }

        if (!Directory.Exists(_paths.FaultBackups))
        {
            return Result<int>.Success(0);
        }

        int restored = 0;
        foreach (string folder in Directory.EnumerateDirectories(_paths.FaultBackups))
        {
            string faultId = Path.GetFileName(folder);
            if (journalled.Value.Any(e => string.Equals(e.FaultId, faultId, StringComparison.Ordinal)))
            {
                continue;
            }

            Result<string> checkedFolder = BackupFolder(faultId);
            if (checkedFolder.IsFailure)
            {
                continue;
            }

            foreach (string hashFile in Directory.GetFiles(checkedFolder.Value, "*" + HashSuffix))
            {
                string logical = Path.GetFileName(hashFile)[..^HashSuffix.Length];
                Result<string> live = LivePath(logical);
                if (live.IsFailure)
                {
                    continue;
                }

                string recorded = (await File.ReadAllTextAsync(hashFile, ct).ConfigureAwait(false)).Trim();
                Result restore = RestoreFromBackup(logical, live.Value, checkedFolder.Value, recorded);
                if (restore.IsSuccess)
                {
                    restored++;
                    _logger.LogInformation("Reconcile restored an unowned corrupted file {File}", logical);
                }
            }

            // The backup folder belongs to no journal entry, so it goes once its files are handled.
            if (Directory.Exists(checkedFolder.Value))
            {
                Directory.Delete(checkedFolder.Value, recursive: true);
            }
        }

        return Result<int>.Success(restored);
    }

    /// <summary>
    /// Restores the backup over the live file when the live hash differs from the recorded one, verifies the hash, zeroes the
    /// buffer and deletes the backup. A live file that already matches is only cleaned up.
    /// </summary>
    private static Result RestoreFromBackup(string logical, string live, string folder, string sha)
    {
        string backup = Path.Combine(folder, logical + BackupSuffix);
        string hashFile = Path.Combine(folder, logical + HashSuffix);
        if (!File.Exists(backup))
        {
            return Result.Success();
        }

        try
        {
            if (!string.Equals(HashOf(live), sha, StringComparison.OrdinalIgnoreCase))
            {
                byte[] buffer = File.ReadAllBytes(backup);
                try
                {
                    if (!string.Equals(Convert.ToHexString(SHA256.HashData(buffer)), sha, StringComparison.OrdinalIgnoreCase))
                    {
                        return Result.Failure("[Lab.Integrity] the backup does not match the recorded hash");
                    }

                    File.WriteAllBytes(live, buffer);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(buffer);
                }

                if (!string.Equals(HashOf(live), sha, StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure("[Lab.Integrity] the restored file does not match the recorded hash");
                }
            }

            File.Delete(backup);
            File.Delete(hashFile);
            if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0)
            {
                Directory.Delete(folder);
            }

            return Result.Success();
        }
        catch (IOException)
        {
            return Result.Failure("[Lab.IO] the backup could not be restored");
        }
    }

    private static void WriteGarbage(string live, CorruptFileHealData heal)
    {
        byte[] buffer = new byte[heal.Length];
        try
        {
            new LabRandom(heal.Seed, GarbageStream).NextBytes(buffer);
            File.WriteAllBytes(live, buffer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private Result<string> LivePath(string logical)
    {
        if (!LogicalFiles.TryGetValue(logical, out string? name))
        {
            return Result<string>.Failure("[Lab.InvalidArgument] unknown logical file");
        }

        return _validator.ValidatePath(Path.Combine(_paths.State, "nodes-ca", name), [_paths.State]);
    }

    private Result<string> BackupFolder(string faultId) =>
        _validator.ValidatePath(Path.Combine(_paths.FaultBackups, faultId), [_paths.FaultBackups]);

    private static string? HashOf(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
