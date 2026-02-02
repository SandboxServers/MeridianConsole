using System.Runtime.InteropServices;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// P/Invoke declarations for Windows Job Objects.
/// Uses LibraryImport (source-generated) for AOT compatibility.
/// </summary>
/// <remarks>
/// SECURITY: Job Objects are used to isolate and control game server processes.
/// These APIs allow resource limiting (CPU, memory) and process group termination.
/// All handles must be properly closed to avoid resource leaks.
/// </remarks>
internal static partial class NativeMethods
{
    private const string Kernel32 = "kernel32.dll";

    private const string Shell32 = "shell32.dll";

    #region Command Line Parsing APIs

    /// <summary>
    /// Parses a Unicode command line string and returns an array of pointers to the command line arguments.
    /// </summary>
    /// <param name="lpCmdLine">Pointer to a null-terminated Unicode string that contains the full command line.</param>
    /// <param name="pNumArgs">Pointer to an int that receives the number of array elements returned.</param>
    /// <returns>A pointer to an array of LPWSTR values, or NULL if the function fails.</returns>
    /// <remarks>
    /// SECURITY: This is the proper Windows API for parsing command lines. It handles all edge cases
    /// including quoted strings, escaped characters, and special characters correctly.
    /// The returned pointer must be freed using LocalFree.
    /// </remarks>
    [LibraryImport(Shell32, EntryPoint = "CommandLineToArgvW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint CommandLineToArgvW(
        string lpCmdLine,
        out int pNumArgs);

    /// <summary>
    /// Frees the specified local memory object and invalidates its handle.
    /// </summary>
    /// <param name="hMem">A handle to the local memory object.</param>
    /// <returns>If the function succeeds, the return value is NULL. If the function fails, the return value equals hMem.</returns>
    [LibraryImport(Kernel32, EntryPoint = "LocalFree", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint LocalFree(nint hMem);

    #endregion

    #region Job Object APIs

    /// <summary>
    /// Creates or opens a job object.
    /// </summary>
    /// <param name="lpJobAttributes">Security attributes (null for default).</param>
    /// <param name="lpName">Job name (null for anonymous job).</param>
    /// <returns>Handle to the job object, or IntPtr.Zero on failure.</returns>
    [LibraryImport(Kernel32, EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint CreateJobObjectW(
        nint lpJobAttributes,
        string? lpName);

    /// <summary>
    /// Assigns a process to a job object.
    /// </summary>
    /// <param name="hJob">Handle to the job object.</param>
    /// <param name="hProcess">Handle to the process to assign.</param>
    /// <returns>True if successful, false otherwise.</returns>
    [LibraryImport(Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AssignProcessToJobObject(
        nint hJob,
        nint hProcess);

    /// <summary>
    /// Sets limits and other properties for a job object.
    /// </summary>
    /// <param name="hJob">Handle to the job object.</param>
    /// <param name="jobObjectInfoClass">The type of information to set.</param>
    /// <param name="lpJobObjectInfo">Pointer to the information structure.</param>
    /// <param name="cbJobObjectInfoLength">Size of the information structure.</param>
    /// <returns>True if successful, false otherwise.</returns>
    [LibraryImport(Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetInformationJobObject(
        nint hJob,
        JobObjectInfoType jobObjectInfoClass,
        nint lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    /// <summary>
    /// Retrieves limit and job state information for a job object.
    /// </summary>
    /// <param name="hJob">Handle to the job object.</param>
    /// <param name="jobObjectInfoClass">The type of information to retrieve.</param>
    /// <param name="lpJobObjectInfo">Pointer to receive the information.</param>
    /// <param name="cbJobObjectInfoLength">Size of the buffer.</param>
    /// <param name="lpReturnLength">Receives the actual size of data returned.</param>
    /// <returns>True if successful, false otherwise.</returns>
    [LibraryImport(Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryInformationJobObject(
        nint hJob,
        JobObjectInfoType jobObjectInfoClass,
        nint lpJobObjectInfo,
        uint cbJobObjectInfoLength,
        out uint lpReturnLength);

    /// <summary>
    /// Terminates all processes in a job object.
    /// </summary>
    /// <param name="hJob">Handle to the job object.</param>
    /// <param name="uExitCode">Exit code for all terminated processes.</param>
    /// <returns>True if successful, false otherwise.</returns>
    [LibraryImport(Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateJobObject(
        nint hJob,
        uint uExitCode);

    /// <summary>
    /// Closes an open object handle.
    /// </summary>
    /// <param name="hObject">Handle to close.</param>
    /// <returns>True if successful, false otherwise.</returns>
    [LibraryImport(Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    #endregion

    #region Enums

    /// <summary>
    /// Information class for job object queries and settings.
    /// </summary>
    public enum JobObjectInfoType
    {
        /// <summary>Basic accounting information.</summary>
        BasicAccountingInformation = 1,

        /// <summary>Basic limit information.</summary>
        BasicLimitInformation = 2,

        /// <summary>Basic process ID list.</summary>
        BasicProcessIdList = 3,

        /// <summary>Basic UI restrictions.</summary>
        BasicUIRestrictions = 4,

        /// <summary>Security limit information (deprecated in Vista+).</summary>
        SecurityLimitInformation = 5,

        /// <summary>End of job time information.</summary>
        EndOfJobTimeInformation = 6,

        /// <summary>Associate completion port.</summary>
        AssociateCompletionPortInformation = 7,

        /// <summary>Basic and IO accounting information.</summary>
        BasicAndIoAccountingInformation = 8,

        /// <summary>Extended limit information (includes memory limits).</summary>
        ExtendedLimitInformation = 9,

        /// <summary>Job set information.</summary>
        JobSetInformation = 10,

        /// <summary>Group information.</summary>
        GroupInformation = 11,

        /// <summary>Notification limit information.</summary>
        NotificationLimitInformation = 12,

        /// <summary>Limit violation information.</summary>
        LimitViolationInformation = 13,

        /// <summary>Group information extended.</summary>
        GroupInformationEx = 14,

        /// <summary>CPU rate control information.</summary>
        CpuRateControlInformation = 15,

        /// <summary>Network rate control information (Windows 10+).</summary>
        NetRateControlInformation = 32,

        /// <summary>Notification limit information (version 2).</summary>
        NotificationLimitInformation2 = 33,

        /// <summary>Limit violation information (version 2).</summary>
        LimitViolationInformation2 = 34,
    }

    /// <summary>
    /// Flags that specify the job object limits.
    /// </summary>
    [Flags]
    public enum JobObjectLimit : uint
    {
        /// <summary>No limits.</summary>
        None = 0,

        /// <summary>
        /// Causes all processes associated with the job to use the same minimum
        /// and maximum working set sizes.
        /// </summary>
        WorkingSet = 0x00000001,

        /// <summary>
        /// Establishes a user-mode execution time limit for each currently active
        /// process and for all future processes associated with the job.
        /// </summary>
        ProcessTime = 0x00000002,

        /// <summary>
        /// Establishes a user-mode execution time limit for the job.
        /// </summary>
        JobTime = 0x00000004,

        /// <summary>
        /// Establishes a maximum number of simultaneously active processes
        /// associated with the job.
        /// </summary>
        ActiveProcess = 0x00000008,

        /// <summary>
        /// Causes all processes associated with the job to use the same processor affinity.
        /// </summary>
        Affinity = 0x00000010,

        /// <summary>
        /// Causes all processes associated with the job to use the same priority class.
        /// </summary>
        PriorityClass = 0x00000020,

        /// <summary>
        /// Preserves any job time limits you previously set.
        /// </summary>
        PreserveJobTime = 0x00000040,

        /// <summary>
        /// Causes all processes associated with the job to use the same scheduling class.
        /// </summary>
        SchedulingClass = 0x00000080,

        /// <summary>
        /// Causes all processes associated with the job to limit their committed memory.
        /// </summary>
        ProcessMemory = 0x00000100,

        /// <summary>
        /// Causes all processes associated with the job to limit the job-wide sum
        /// of their committed memory.
        /// </summary>
        JobMemory = 0x00000200,

        /// <summary>
        /// Forces a call to the SetErrorMode function with the SEM_NOGPFAULTERRORBOX flag.
        /// </summary>
        DieOnUnhandledException = 0x00000400,

        /// <summary>
        /// If any process associated with the job creates a child process using
        /// the CREATE_BREAKAWAY_FROM_JOB flag, the child process is not associated
        /// with the job.
        /// </summary>
        BreakawayOk = 0x00000800,

        /// <summary>
        /// Allows any process associated with the job to create child processes
        /// that are not associated with the job.
        /// </summary>
        SilentBreakawayOk = 0x00001000,

        /// <summary>
        /// Causes all processes associated with the job to terminate when the
        /// last handle to the job is closed.
        /// </summary>
        KillOnJobClose = 0x00002000,

        /// <summary>
        /// Allows processes to use a subset of the processor affinity for all
        /// processes associated with the job.
        /// </summary>
        SubsetAffinity = 0x00004000,

        /// <summary>
        /// Causes all processes in the job to be notified when the job memory
        /// limit is exceeded. Used with notification limit information.
        /// </summary>
        JobMemoryLow = 0x00008000,
    }

    /// <summary>
    /// Flags for CPU rate control.
    /// </summary>
    [Flags]
    public enum CpuRateControlFlags : uint
    {
        /// <summary>No CPU rate control.</summary>
        None = 0,

        /// <summary>
        /// This flag enables CPU rate control for the job.
        /// The system enforces the CPU rate limit.
        /// </summary>
        Enable = 0x00000001,

        /// <summary>
        /// The CPU rate is a hard cap (processes are stopped when limit is reached).
        /// Mutually exclusive with WeightBased.
        /// </summary>
        HardCap = 0x00000004,

        /// <summary>
        /// The CPU rate is an advisory limit enforced through soft CPU rate control.
        /// Mutually exclusive with HardCap.
        /// </summary>
        WeightBased = 0x00000008,

        /// <summary>
        /// Send notification when the CPU rate limit is exceeded.
        /// </summary>
        Notify = 0x00000010,

        /// <summary>
        /// The CPU rate control limit can be set to a minimum rate.
        /// Used with MinMaxRate instead of CpuRate.
        /// </summary>
        MinMaxRate = 0x00000020,
    }

    #endregion

    #region Structs

    /// <summary>
    /// Contains basic limit information for a job object.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        /// <summary>
        /// Per-process user-mode execution time limit, in 100-nanosecond ticks.
        /// </summary>
        public long PerProcessUserTimeLimit;

        /// <summary>
        /// Per-job user-mode execution time limit, in 100-nanosecond ticks.
        /// </summary>
        public long PerJobUserTimeLimit;

        /// <summary>
        /// The limit flags that are in effect.
        /// </summary>
        public JobObjectLimit LimitFlags;

        /// <summary>
        /// The minimum working set size for each process in the job.
        /// </summary>
        public nuint MinimumWorkingSetSize;

        /// <summary>
        /// The maximum working set size for each process in the job.
        /// </summary>
        public nuint MaximumWorkingSetSize;

        /// <summary>
        /// The maximum number of active processes in the job.
        /// </summary>
        public uint ActiveProcessLimit;

        /// <summary>
        /// The processor affinity for all processes in the job.
        /// </summary>
        public nuint Affinity;

        /// <summary>
        /// The priority class for all processes in the job.
        /// </summary>
        public uint PriorityClass;

        /// <summary>
        /// The scheduling class for all processes in the job.
        /// </summary>
        public uint SchedulingClass;
    }

    /// <summary>
    /// Contains I/O accounting information for a process or job object.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS
    {
        /// <summary>The number of read operations performed.</summary>
        public ulong ReadOperationCount;

        /// <summary>The number of write operations performed.</summary>
        public ulong WriteOperationCount;

        /// <summary>The number of I/O operations performed other than read and write.</summary>
        public ulong OtherOperationCount;

        /// <summary>The number of bytes read.</summary>
        public ulong ReadTransferCount;

        /// <summary>The number of bytes written.</summary>
        public ulong WriteTransferCount;

        /// <summary>The number of bytes transferred during other operations.</summary>
        public ulong OtherTransferCount;
    }

    /// <summary>
    /// Contains extended limit information for a job object, including memory limits.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        /// <summary>Basic limit information.</summary>
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;

        /// <summary>I/O counters for the job.</summary>
        public IO_COUNTERS IoInfo;

        /// <summary>
        /// The per-process memory limit, in bytes.
        /// Used when LimitFlags includes ProcessMemory.
        /// </summary>
        public nuint ProcessMemoryLimit;

        /// <summary>
        /// The per-job memory limit, in bytes.
        /// Used when LimitFlags includes JobMemory.
        /// </summary>
        public nuint JobMemoryLimit;

        /// <summary>
        /// The peak memory used by any process in the job, in bytes.
        /// </summary>
        public nuint PeakProcessMemoryUsed;

        /// <summary>
        /// The peak memory usage of all processes in the job, in bytes.
        /// </summary>
        public nuint PeakJobMemoryUsed;
    }

    /// <summary>
    /// Contains CPU rate control information for a job object.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
    {
        /// <summary>
        /// The scheduling policy and flags.
        /// </summary>
        public CpuRateControlFlags ControlFlags;

        /// <summary>
        /// CPU rate or weight, depending on ControlFlags.
        /// When HardCap or no weight flag: percentage of CPU cycles (1-10000, where 10000 = 100%).
        /// When WeightBased: scheduling weight (1-9).
        /// When MinMaxRate: use MinRate and MaxRate fields instead.
        /// </summary>
        public uint CpuRate;

        /// <summary>
        /// Minimum CPU rate percentage (when MinMaxRate flag is set).
        /// Value from 0-10000, where 10000 = 100%.
        /// </summary>
        public uint MinRate
        {
            readonly get => CpuRate;
            set => CpuRate = value;
        }

        /// <summary>
        /// Maximum CPU rate percentage (when MinMaxRate flag is set).
        /// Value from 0-10000, where 10000 = 100%.
        /// </summary>
        public uint MaxRate;

        /// <summary>
        /// Scheduling weight when using WeightBased flag.
        /// Value from 1-9.
        /// </summary>
        public uint Weight
        {
            readonly get => CpuRate;
            set => CpuRate = value;
        }
    }

    #endregion
}
