using System.Text.RegularExpressions;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Bundle;

/// <summary>
/// Finds the bundle folder of one run under <c>LabPaths.UatRuns</c> (folder name <c>yyyyMMdd-buildId-runId</c>) and reads files
/// inside it. Every path passes through <see cref="IPathValidator"/> against the bundle folder before use.
/// </summary>
public static class BundleFolders
{
    /// <summary>The largest bundle file a read returns, in bytes.</summary>
    public const long MaxReadBytes = 1024 * 1024;

    private static readonly Regex RunIdPattern = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns the bundle folder whose name ends with <c>-&lt;runId&gt;</c>.
    /// </summary>
    /// <param name="uatRuns">The <c>LabPaths.UatRuns</c> root.</param>
    /// <param name="runId">The run id as returned by <c>uat_run</c>.</param>
    /// <param name="validator">Validates the folder against the runs root.</param>
    public static Result<string> Find(string uatRuns, string? runId, IPathValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        if (string.IsNullOrEmpty(runId) || !RunIdPattern.IsMatch(runId))
        {
            return Result<string>.Failure("[Lab.InvalidArgument] run_id is not valid");
        }

        if (!Directory.Exists(uatRuns))
        {
            return Result<string>.Failure("[Lab.NotFound] no bundle for that run_id");
        }

        foreach (string dir in Directory.EnumerateDirectories(uatRuns))
        {
            if (Path.GetFileName(dir).EndsWith("-" + runId, StringComparison.Ordinal))
            {
                return validator.ValidatePath(dir, [uatRuns]);
            }
        }

        return Result<string>.Failure("[Lab.NotFound] no bundle for that run_id");
    }

    /// <summary>
    /// Resolves a file path relative to a bundle folder. A path that leaves the folder (for example <c>..\run.json</c> of
    /// another run) is refused.
    /// </summary>
    /// <param name="folder">The validated bundle folder.</param>
    /// <param name="relative">The path inside the bundle.</param>
    /// <param name="validator">Validates the path.</param>
    public static Result<string> ResolveFile(string folder, string? relative, IPathValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        if (string.IsNullOrWhiteSpace(relative))
        {
            return Result<string>.Failure("[Lab.InvalidArgument] path is required");
        }

        Result<string> checkedPath = validator.ValidatePath(Path.Combine(folder, relative), [folder]);
        if (checkedPath.IsFailure)
        {
            return Result<string>.Failure("[Lab.InvalidArgument] the path is outside the bundle folder");
        }

        if (!File.Exists(checkedPath.Value))
        {
            return Result<string>.Failure("[Lab.NotFound] no such file in the bundle");
        }

        return checkedPath;
    }

    /// <summary>
    /// Reads a bundle file as text, refusing files larger than <see cref="MaxReadBytes"/>.
    /// </summary>
    /// <param name="path">A path already validated by <see cref="ResolveFile"/>.</param>
    /// <param name="ct">The cancellation token.</param>
    public static async Task<Result<string>> ReadTextAsync(string path, CancellationToken ct)
    {
        try
        {
            if (new FileInfo(path).Length > MaxReadBytes)
            {
                return Result<string>.Failure("[Lab.InvalidArgument] the file is larger than 1 MiB");
            }

            return Result<string>.Success(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure("[Lab.Unavailable] the file could not be read");
        }
    }
}
