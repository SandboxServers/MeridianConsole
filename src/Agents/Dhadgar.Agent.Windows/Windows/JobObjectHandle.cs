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
    /// <remarks>
    /// SECURITY: This constructor is private to prevent misuse. All JobObjectHandle instances
    /// must be created with a valid handle via the constructor that takes an existingHandle parameter.
    /// CA1419 is suppressed because this handle is only used internally and is never marshaled
    /// from native code where a parameterless constructor would be required.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Interoperability",
        "CA1419:Provide a parameterless constructor that is as visible as the containing type for concrete types derived from 'System.Runtime.InteropServices.SafeHandle'",
        Justification = "This SafeHandle is only used internally and is never marshaled from native code. Making the constructor private prevents misuse.")]
    private JobObjectHandle() : base(ownsHandle: true)
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
