using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Identity;

/// <summary>
/// Default state-file protection. Unix: file 0600, and a load refuses a group- or world-writable file.
/// Windows: CreateNew and write-through only; the Windows host replaces this with WindowsStateFileSecurity (P27f).
/// The file owner is not checked on Unix (Linux follow-up, D-S218).
/// </summary>
public sealed class PortableStateFileSecurity : IStateFileSecurity
{
    /// <inheritdoc />
    public Result<FileStream> CreateNew(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = System.IO.FileOptions.Asynchronous | System.IO.FileOptions.WriteThrough,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        FileStream? stream = null;
        try
        {
            stream = new FileStream(path, options);
            var created = Result<FileStream>.Success(stream);
            stream = null; // ownership passes to the caller
            return created;
        }
        catch (IOException ex)
        {
            return Result<FileStream>.Failure($"[AgentState.Io] Failed to create state file ({ex.GetType().Name})");
        }
        catch (UnauthorizedAccessException)
        {
            return Result<FileStream>.Failure("[AgentState.Io] Access denied creating state file");
        }
        finally
        {
            stream?.Dispose();
        }
    }

    /// <inheritdoc />
    public Result VerifyTrusted(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (OperatingSystem.IsWindows())
        {
            return Result.Success(); // the Windows host registers WindowsStateFileSecurity (P27f)
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) == 0
                ? Result.Success()
                : Result.Failure("[AgentState.Untrusted] State file is writable by group or others");
        }
        catch (IOException ex)
        {
            return Result.Failure($"[AgentState.Io] Failed to inspect state file ({ex.GetType().Name})");
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure("[AgentState.Io] Access denied inspecting state file");
        }
    }
}
