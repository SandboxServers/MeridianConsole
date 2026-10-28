using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Daemon;

/// <summary>
/// Holds the per-user daemon mutex and writes <c>daemon.pid</c>. Disposing deletes the pidfile and releases the mutex.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly FileStream _pidFile;
    private readonly string _pidPath;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, FileStream pidFile, string pidPath)
    {
        _mutex = mutex;
        _pidFile = pidFile;
        _pidPath = pidPath;
    }

    /// <summary>
    /// Takes the mutex <c>Local\MeridianLab-&lt;user&gt;</c> and writes the pidfile under <paramref name="paths"/>.
    /// A second acquire while the first is held returns <c>[Lab.AlreadyRunning]</c>.
    /// </summary>
    public static Result<SingleInstanceGuard> Acquire(LabPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var mutex = new Mutex(initiallyOwned: true, MutexName(), out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return Result<SingleInstanceGuard>.Failure("[Lab.AlreadyRunning] another meridian-lab daemon holds this user's lock");
        }

        FileStream? pidFile = null;
        try
        {
            CreateRoot(paths.Root);
            pidFile = new FileStream(paths.PidFile, PidFileOptions());
            var pid = Encoding.ASCII.GetBytes(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            pidFile.Write(pid);
            pidFile.Flush();
            return Result<SingleInstanceGuard>.Success(new SingleInstanceGuard(mutex, pidFile, paths.PidFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            pidFile?.Dispose();
            ReleaseOwnership(mutex);
            mutex.Dispose();
            return Result<SingleInstanceGuard>.Failure("[Lab.Unavailable] cannot write the daemon pidfile under the lab root");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pidFile.Dispose();
        try
        {
            File.Delete(_pidPath);
        }
        catch (IOException)
        {
            // The pidfile is advisory; a leftover file is replaced by the next holder.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above: the next holder rewrites the file.
        }

        ReleaseOwnership(_mutex);
        _mutex.Dispose();
    }

    /// <summary>
    /// Releases the mutex when the calling thread owns it. A mutex is owned by the thread that acquired it,
    /// so a Dispose on another thread skips the release and only closes the handle.
    /// </summary>
    private static void ReleaseOwnership(Mutex mutex)
    {
        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not the owning thread.
        }
    }

    /// <summary>
    /// Creates the lab root. On Unix it is user-private (0700): it is the first directory the lab creates and every later
    /// child lives under it. On Windows the root keeps the inherited ACL; <c>MERIDIAN_LAB_HOME</c> must be a user-private directory.
    /// </summary>
    private static void CreateRoot(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(root);
            return;
        }

        Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>
    /// The pidfile options. <see cref="FileStreamOptions.UnixCreateMode"/> is set only on Unix: on Windows it throws.
    /// </summary>
    private static FileStreamOptions PidFileOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.Read,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    private static string MutexName() => "Local\\MeridianLab-" + CurrentUserId();

    private static string CurrentUserId()
    {
        if (OperatingSystem.IsWindows())
        {
            return CurrentSid();
        }

        return ElevationCheck.EffectiveUid().ToString(CultureInfo.InvariantCulture);
    }

    [SupportedOSPlatform("windows")]
    private static string CurrentSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? "unknown";
    }
}
