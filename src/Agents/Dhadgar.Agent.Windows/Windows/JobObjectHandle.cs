using Microsoft.Win32.SafeHandles;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Safe handle wrapper for Windows Job Objects.
/// Ensures proper cleanup even on exceptions or process termination.
/// </summary>
/// <remarks>
/// SECURITY: Using SafeHandle ensures the Job Object handle is properly closed
/// even if an exception occurs or the finalizer runs. This prevents handle leaks
/// and ensures all processes in the job are terminated when the handle is closed
/// (due to JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE flag).
/// </remarks>
internal sealed class JobObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JobObjectHandle"/> class.
    /// </summary>
    public JobObjectHandle() : base(ownsHandle: true)
    {
    }

    /// <summary>
    /// Initializes a new instance with an existing handle value.
    /// </summary>
    /// <param name="existingHandle">The existing native handle.</param>
    /// <param name="ownsHandle">Whether this instance owns the handle.</param>
    public JobObjectHandle(IntPtr existingHandle, bool ownsHandle = true) : base(ownsHandle)
    {
        SetHandle(existingHandle);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        return NativeMethods.CloseHandle(handle);
    }
}
