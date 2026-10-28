using System.Text;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Bundle;

/// <summary>
/// Writes a bundle change as one staged, roll-forward commit (OD-1, D-ML89). Every text is first written and flushed under
/// <c>attest-staging\</c> in the bundle folder. An empty <c>commit</c> marker is then written and flushed. Only after the marker
/// exists are the staged files moved over their targets. A failure before the marker leaves the bundle as it was. A failure
/// after it is completed by <see cref="Recover"/>. Every path passes through <see cref="IPathValidator"/> before use.
/// </summary>
internal static class BundleStaging
{
    /// <summary>The staging folder, under the bundle folder and outside <c>rows\</c>, so the row reader never sees it.</summary>
    internal const string FolderName = "attest-staging";

    /// <summary>The empty marker file written after every staged file is flushed.</summary>
    internal const string CommitMarker = "commit";

    private const string OutsideFolder = "[Lab.InvalidArgument] the bundle path is outside the bundle folder";
    private const string Unavailable = "[Lab.Unavailable] the bundle could not be written";

    /// <summary>
    /// Recovers any earlier commit, then stages each text, writes the marker and rolls the staged files forward.
    /// Each text is redacted again with <paramref name="redactor"/> before it is staged.
    /// </summary>
    /// <param name="folder">The validated bundle folder.</param>
    /// <param name="texts">The bundle-relative path and text of each file to write.</param>
    /// <param name="redactor">Redacts every staged text.</param>
    /// <param name="validator">Validates every path.</param>
    internal static Result Commit(string folder, IReadOnlyList<(string Relative, string Text)> texts, Redactor redactor, IPathValidator validator)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(redactor);
        ArgumentNullException.ThrowIfNull(validator);

        Result recovered = Recover(folder, validator);
        if (recovered.IsFailure)
        {
            return recovered;
        }

        Result<string> stagingRoot = validator.ValidatePath(Path.Combine(folder, FolderName), [folder]);
        if (stagingRoot.IsFailure)
        {
            return Result.Failure(OutsideFolder);
        }

        string staging = stagingRoot.Value!;
        try
        {
            foreach ((string relative, string text) in texts)
            {
                Result staged = WriteStaged(folder, staging, relative, redactor.Redact(text), validator);
                if (staged.IsFailure)
                {
                    _ = DeleteStaging(staging);
                    return staged;
                }
            }

            using (var marker = new FileStream(Path.Combine(staging, CommitMarker), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                marker.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = DeleteStaging(staging);
            return Result.Failure(Unavailable);
        }

        return RollForward(folder, staging, validator);
    }

    /// <summary>
    /// With the marker present, moves each staged file over its target and deletes the staging folder. With no marker, deletes
    /// the staging folder. No staging folder is success. Idempotent.
    /// </summary>
    /// <param name="folder">The validated bundle folder.</param>
    /// <param name="validator">Validates every path.</param>
    internal static Result Recover(string folder, IPathValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        Result<string> stagingRoot = validator.ValidatePath(Path.Combine(folder, FolderName), [folder]);
        if (stagingRoot.IsFailure)
        {
            return Result.Failure(OutsideFolder);
        }

        string staging = stagingRoot.Value!;
        if (!Directory.Exists(staging))
        {
            return Result.Success();
        }

        return File.Exists(Path.Combine(staging, CommitMarker))
            ? RollForward(folder, staging, validator)
            : DeleteStaging(staging);
    }

    private static Result WriteStaged(string folder, string staging, string relative, string text, IPathValidator validator)
    {
        Result<string> target = validator.ValidatePath(Path.Combine(folder, relative), [folder]);
        Result<string> path = validator.ValidatePath(Path.Combine(staging, relative), [staging]);
        if (target.IsFailure || path.IsFailure)
        {
            return Result.Failure(OutsideFolder);
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(path.Value!)!);
        using var stream = new FileStream(path.Value!, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] bytes = new UTF8Encoding(false).GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
        return Result.Success();
    }

    private static Result RollForward(string folder, string staging, IPathValidator validator)
    {
        try
        {
            string markerPath = Path.GetFullPath(Path.Combine(staging, CommitMarker));
            foreach (string source in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
                         .Where(file => Path.GetFullPath(file) != markerPath)
                         .Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(staging, source);
                Result<string> target = validator.ValidatePath(Path.Combine(folder, relative), [folder]);
                if (target.IsFailure)
                {
                    return Result.Failure(OutsideFolder);
                }

                _ = Directory.CreateDirectory(Path.GetDirectoryName(target.Value!)!);
                File.Move(source, target.Value!, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The marker is still present, so the next Recover completes the roll forward.
            return Result.Failure(Unavailable);
        }

        return DeleteStaging(staging);
    }

    private static Result DeleteStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(Unavailable);
        }

        return Result.Success();
    }
}
