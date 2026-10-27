using System.Diagnostics;
using System.Runtime.InteropServices;
using Dhadgar.Shared.Results;
using Microsoft.Win32.SafeHandles;

namespace Dhadgar.Agent.Windows.Uat.Infra;

/// <summary>
/// A Windows job object with <c>KILL_ON_JOB_CLOSE</c>, so every child process dies with the driver
/// even if the driver crashes. The handle is a <see cref="SafeHandle"/>: it is closed by <see cref="Dispose"/>,
/// and by the finalizer if a caller forgets.
/// </summary>
internal sealed partial class JobObject : IDisposable
{
    private const int ExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private readonly JobHandle _handle;

    private JobObject(JobHandle handle)
    {
        _handle = handle;
    }

    /// <summary>Creates a job object configured to kill its processes when the handle closes.</summary>
    public static Result<JobObject> Create()
    {
        var raw = CreateJobObjectW(IntPtr.Zero, null);
        if (raw == IntPtr.Zero)
        {
            return Result<JobObject>.Failure($"CreateJobObjectW failed with Win32 error {Marshal.GetLastPInvokeError()}");
        }

        // Wrap the raw handle before anything else can fail, so an early return still closes it.
        var handle = new JobHandle(raw);
        var configured = ConfigureKillOnJobClose(handle);
        if (configured.IsFailure)
        {
            handle.Dispose();
            return Result<JobObject>.Failure(configured.Error);
        }

        JobObject? job = new JobObject(handle);
        try
        {
            var result = Result<JobObject>.Success(job);
            job = null; // The caller owns the job through the result; finally must not dispose it.
            return result;
        }
        finally
        {
            job?.Dispose();
        }
    }

    /// <summary>Sets the KILL_ON_JOB_CLOSE limit on the job handle.</summary>
    private static Result ConfigureKillOnJobClose(JobHandle handle)
    {
        var info = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose },
        };

        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, fDeleteOld: false);
            if (!SetInformationJobObject(handle.DangerousGetHandle(), ExtendedLimitInformationClass, ptr, (uint)size))
            {
                return Result.Failure($"SetInformationJobObject failed with Win32 error {Marshal.GetLastPInvokeError()}");
            }

            return Result.Success();
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>Assigns a started process to this job.</summary>
    public Result AssignProcess(Process process)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        return AssignProcessToJobObject(_handle.DangerousGetHandle(), process.Handle)
            ? Result.Success()
            : Result.Failure($"AssignProcessToJobObject failed with Win32 error {Marshal.GetLastPInvokeError()}");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _handle.Dispose();
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateJobObjectW(IntPtr attributes, string? name);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    /// <summary>Owns the job handle. Closed once, whether by <see cref="Dispose"/> or the finalizer.</summary>
    private sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public JobHandle(IntPtr existing)
            : base(ownsHandle: true)
        {
            SetHandle(existing);
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
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
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
