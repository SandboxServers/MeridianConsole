using Microsoft.Win32.SafeHandles;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Safe handle wrapper for Windows IO Completion Ports.
/// Ensures proper cleanup even on exceptions or process termination.
/// </summary>
/// <remarks>
/// SECURITY: Using SafeHandle ensures the IO completion port handle is properly closed
/// even if an exception occurs or the finalizer runs. This prevents handle leaks
/// and ensures proper resource cleanup.
/// </remarks>
internal sealed class IoCompletionPortHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IoCompletionPortHandle"/> class.
    /// </summary>
    /// <remarks>
    /// SECURITY: This constructor is private to prevent misuse. All IoCompletionPortHandle instances
    /// must be created with a valid handle via the constructor that takes an existingHandle parameter.
    /// CA1419 is suppressed because this handle is only used internally and is never marshaled
    /// from native code where a parameterless constructor would be required.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Interoperability",
        "CA1419:Provide a parameterless constructor that is as visible as the containing type for concrete types derived from 'System.Runtime.InteropServices.SafeHandle'",
        Justification = "This SafeHandle is only used internally and is never marshaled from native code. Making the constructor private prevents misuse.")]
    private IoCompletionPortHandle() : base(ownsHandle: true)
    {
    }

    /// <summary>
    /// Initializes a new instance with an existing handle value.
    /// </summary>
    /// <param name="existingHandle">The existing native handle.</param>
    /// <param name="ownsHandle">Whether this instance owns the handle.</param>
    public IoCompletionPortHandle(IntPtr existingHandle, bool ownsHandle = true) : base(ownsHandle)
    {
        SetHandle(existingHandle);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        return NativeMethods.CloseHandle(handle);
    }
}
