using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Stack;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Msi;

/// <summary>
/// The result of a successful <c>msiexec</c> run. <see cref="LoggedCommandLine"/> is the redacted command line the
/// driver logs; the enrollment token never appears in it.
/// </summary>
internal sealed record MsiOutcome(int ExitCode, string LoggedCommandLine, IReadOnlyList<string> Warnings);

/// <summary>Installs and uninstalls the agent MSI through <c>msiexec.exe</c> (P23, D-S223). The driver never runs it by hand.</summary>
internal interface IMsiInstaller
{
    /// <summary>Installs <paramref name="msiPath"/> silently, enrolling with <paramref name="token"/> against <paramref name="endpoint"/>.</summary>
    Result<MsiOutcome> Install(string msiPath, MintedToken token, Uri endpoint);

    /// <summary>Uninstalls the product that <paramref name="msiPath"/> installed, silently.</summary>
    Result<MsiOutcome> Uninstall(string msiPath);
}

/// <summary>
/// Runs <c>msiexec</c> with <c>ArgumentList</c> through <see cref="CommandRunner"/>. Exit 0 succeeds; 3010 succeeds
/// with a "reboot requested" warning; anything else fails with the code and the last 40 redacted log lines that
/// contain <c>Return value 3</c> or <c>Error</c>. The token is one argument, <c>ENROLLMENT_TOKEN=&lt;token&gt;</c>,
/// and appears in nothing the driver logs or returns.
/// </summary>
internal sealed class MsiInstaller : IMsiInstaller
{
    /// <summary>Timeout for one <c>msiexec</c> run.</summary>
    public static readonly TimeSpan MsiexecTimeout = TimeSpan.FromMinutes(5);

    private const int MsiSuccess = 0;
    private const int MsiSuccessRebootRequested = 3010;
    private const int LogTailLines = 40;
    private const string InstallLogName = "msi-install.log";
    private const string UninstallLogName = "msi-uninstall.log";

    private readonly RunPaths _paths;
    private readonly Redactor _redactor;
    private readonly Func<IReadOnlyList<string>, Result<CommandOutput>> _msiexec;

    /// <summary>Creates the installer over the real <c>msiexec.exe</c>.</summary>
    public MsiInstaller(RunPaths paths, Redactor redactor)
        : this(paths, redactor, args => CommandRunner.RunAsync("msiexec.exe", args, MsiexecTimeout, redactor).GetAwaiter().GetResult())
    {
    }

    /// <summary>Creates the installer over a delegate that runs <c>msiexec</c> (tests pass a fake).</summary>
    internal MsiInstaller(RunPaths paths, Redactor redactor, Func<IReadOnlyList<string>, Result<CommandOutput>> msiexec)
    {
        _paths = paths;
        _redactor = redactor;
        _msiexec = msiexec;
    }

    /// <inheritdoc />
    public Result<MsiOutcome> Install(string msiPath, MintedToken token, Uri endpoint)
    {
        var msi = _paths.Validate(msiPath);
        if (msi.IsFailure)
        {
            return Result<MsiOutcome>.Failure(msi.Error);
        }

        var log = LogPath(InstallLogName);
        if (log.IsFailure)
        {
            return Result<MsiOutcome>.Failure(log.Error);
        }

        // The token is one item (D-S229). It is the only argument that carries a secret.
        var args = new List<string>
        {
            "/i", msi.Value, "/qn", "/norestart", "/l*v", log.Value,
            $"{AgentWindowsFacts.MsiTokenProperty}={token.PlainText}",
            $"{AgentWindowsFacts.MsiEndpointProperty}={endpoint.AbsoluteUri}",
        };
        return Run(args, log.Value, "/i");
    }

    /// <inheritdoc />
    public Result<MsiOutcome> Uninstall(string msiPath)
    {
        var msi = _paths.Validate(msiPath);
        if (msi.IsFailure)
        {
            return Result<MsiOutcome>.Failure(msi.Error);
        }

        var log = LogPath(UninstallLogName);
        if (log.IsFailure)
        {
            return Result<MsiOutcome>.Failure(log.Error);
        }

        return Run(["/x", msi.Value, "/qn", "/norestart", "/l*v", log.Value], log.Value, "/x");
    }

    private Result<string> LogPath(string name)
    {
        var path = Path.Combine(_paths.Logs, name);
        return _paths.Validate(path);
    }

    private Result<MsiOutcome> Run(IReadOnlyList<string> args, string logPath, string verb)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        }
        catch (IOException ex)
        {
            return Result<MsiOutcome>.Failure($"msiexec log folder could not be created: {ex.GetType().Name}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<MsiOutcome>.Failure($"msiexec log folder could not be created: {ex.GetType().Name}");
        }

        var commandLine = _redactor.Redact("msiexec.exe " + string.Join(' ', args));
        var output = _msiexec(args);
        if (output.IsFailure)
        {
            return Result<MsiOutcome>.Failure(_redactor.Redact(output.Error));
        }

        var exitCode = output.Value.ExitCode;
        if (exitCode == MsiSuccess)
        {
            return Result<MsiOutcome>.Success(new MsiOutcome(exitCode, commandLine, []));
        }

        if (exitCode == MsiSuccessRebootRequested)
        {
            return Result<MsiOutcome>.Success(new MsiOutcome(
                exitCode, commandLine, [$"msiexec {verb} exited 3010: reboot requested"]));
        }

        var tail = LogTail(logPath);
        var message = $"msiexec {verb} exited {exitCode}";
        if (tail.Count > 0)
        {
            message += Environment.NewLine + string.Join(Environment.NewLine, tail);
        }

        return Result<MsiOutcome>.Failure(message);
    }

    /// <summary>The last 40 redacted lines of an MSI log that mention a return value 3 or an error.</summary>
    private IReadOnlyList<string> LogTail(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return [];
        }

        // A log that is still locked or unreadable gives the lines read so far; the failure is still returned, never thrown.
        var lines = new List<string>();
        try
        {
            using var reader = new StreamReader(logPath, detectEncodingFromByteOrderMarks: true);
            while (reader.ReadLine() is { } line)
            {
                if (line.Contains("Return value 3", StringComparison.Ordinal) || line.Contains("Error", StringComparison.Ordinal))
                {
                    lines.Add(_redactor.Redact(line));
                }
            }
        }
        catch (IOException)
        {
            // The lines read so far are kept.
        }
        catch (UnauthorizedAccessException)
        {
            // The lines read so far are kept.
        }

        return lines.TakeLast(LogTailLines).ToList();
    }
}
