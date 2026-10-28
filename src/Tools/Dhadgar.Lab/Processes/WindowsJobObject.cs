using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dhadgar.Shared.Results;
using Microsoft.Win32.SafeHandles;

namespace Dhadgar.Lab.Processes;

/// <summary>
/// A kill-on-close Windows Job Object. The daemon holds one; every child is assigned to it, so the children die with the
/// daemon (D-ML19). The Windows calls sit behind <c>[SupportedOSPlatform("windows")]</c> and an <c>OperatingSystem.IsWindows()</c> check.
/// </summary>
internal sealed partial class WindowsJobObject : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private readonly SafeJobObjectHandle _handle;
    private bool _disposed;

    private WindowsJobObject(SafeJobObjectHandle handle)
    {
        _handle = handle;
    }

    /// <summary>
    /// Creates the job. Returns <c>[Lab.Unavailable]</c> off Windows or when the OS refuses the job.
    /// </summary>
    public static Result<WindowsJobObject> Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result<WindowsJobObject>.Failure("[Lab.Unavailable] job objects need Windows");
        }

        return CreateOnWindows();
    }

    /// <summary>
    /// Assigns a started process to the job. Returns <c>[Lab.Unavailable]</c> when the OS refuses the assignment.
    /// </summary>
    /// <param name="processHandle">The OS handle of the started process.</param>
    public Result Assign(IntPtr processHandle)
    {
        if (_disposed)
        {
            return Result.Failure("[Lab.Unavailable] the job object is disposed");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Result.Failure("[Lab.Unavailable] job objects need Windows");
        }

        return AssignOnWindows(processHandle);
    }

    /// <summary>
    /// Closes the job. Kill-on-close ends every process still assigned to it.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
    }

    [SupportedOSPlatform("windows")]
    private static Result<WindowsJobObject> CreateOnWindows()
    {
        SafeJobObjectHandle handle = NativeCreateJobObject(IntPtr.Zero, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return Result<WindowsJobObject>.Failure("[Lab.Unavailable] could not create the job object");
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
        if (!NativeSetInformationJobObject(handle, JobObjectExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            handle.Dispose();
            return Result<WindowsJobObject>.Failure("[Lab.Unavailable] could not set the job object limits");
        }

        return Result<WindowsJobObject>.Success(new WindowsJobObject(handle));
    }

    [SupportedOSPlatform("windows")]
    private Result AssignOnWindows(IntPtr processHandle)
    {
        return NativeAssignProcessToJobObject(_handle, processHandle)
            ? Result.Success()
            : Result.Failure("[Lab.Unavailable] could not assign the child to the job object");
    }

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    private static partial SafeJobObjectHandle NativeCreateJobObject(IntPtr attributes, IntPtr name);

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll", EntryPoint = "SetInformationJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeSetInformationJobObject(SafeJobObjectHandle job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length);

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll", EntryPoint = "AssignProcessToJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeAssignProcessToJobObject(SafeJobObjectHandle job, IntPtr process);

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeCloseHandle(IntPtr handle);

    /// <summary>
    /// Owns the job handle and closes it with <c>CloseHandle</c>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class SafeJobObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobObjectHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => NativeCloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
