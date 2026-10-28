using Dhadgar.Agent.Core.Files;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Processes;

/// <summary>
/// Resolves the log file for a process under <see cref="LabPaths.Logs"/>. <see cref="IPathValidator"/> checks the lexical
/// path; this guard also refuses any symbolic link or junction on the log folder or the file, because the validator does
/// not resolve links.
/// </summary>
public sealed class LogPathGuard
{
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;

    /// <summary>
    /// Creates the guard over the lab paths and the shared path validator.
    /// </summary>
    public LogPathGuard(LabPaths paths, IPathValidator validator)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    /// <summary>
    /// The validated path of <c>logs/&lt;name&gt;.jsonl</c>. Creates the logs folder when missing.
    /// </summary>
    /// <param name="processName">A process name that already passed the supervisor's name rule.</param>
    public Result<string> ResolveLogFile(string processName)
    {
        ArgumentNullException.ThrowIfNull(processName);
        string candidate = Path.Combine(_paths.Logs, processName + ".jsonl");
        Result<string> validated = _validator.ValidatePath(candidate, [_paths.Logs]);
        if (validated.IsFailure)
        {
            return Result<string>.Failure(validated.Error);
        }

        try
        {
            _ = Directory.CreateDirectory(_paths.Logs);
            if (IsLink(_paths.Logs) || IsLink(validated.Value!))
            {
                return Result<string>.Failure("[Lab.Path] the log path crosses a symbolic link or junction");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure("[Lab.Unavailable] the log folder could not be checked");
        }

        return Result<string>.Success(validated.Value!);
    }

    /// <summary>
    /// True when <paramref name="path"/> exists and is a symbolic link or junction (a reparse point). A missing path is not a link.
    /// </summary>
    /// <param name="path">The path to check.</param>
    internal static bool IsLink(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }
}
