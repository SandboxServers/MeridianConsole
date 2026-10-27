using System.IO;
using Dhadgar.Agent.Windows.Uat.Cleanup;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Tests;

/// <summary>Fake machine actions: records every call in order and fails the calls named in <see cref="FailOn"/>.</summary>
internal sealed class RecordingActions : IMachineActions
{
    public List<string> Calls { get; } = [];

    public HashSet<string> FailOn { get; } = [];

    public bool Elevated { get; set; } = true;

    /// <summary>Calls that throw an <see cref="IOException"/> instead of returning a result.</summary>
    public HashSet<string> ThrowOn { get; } = [];

    public bool IsElevated() => Elevated;

    public Result RemoveContainer(string name) => Record($"RemoveContainer:{name}");

    public Result KillProcess(int pid, DateTime startTimeUtc) => Record($"KillProcess:{pid}");

    /// <summary>Image paths by PID. A PID not listed reports the test host's own image, which the engine allows.</summary>
    public Dictionary<int, string?> ProcessImages { get; } = [];

    /// <summary>PIDs whose image cannot be read: the probe fails.</summary>
    public HashSet<int> UnreadableProcesses { get; } = [];

    public Result<string?> GetProcessImagePath(int pid, DateTime startTimeUtc)
    {
        if (UnreadableProcesses.Contains(pid))
        {
            return Result<string?>.Failure($"fake: image of {pid} unreadable");
        }

        return Result<string?>.Success(ProcessImages.TryGetValue(pid, out var image) ? image : Environment.ProcessPath);
    }

    public Result StopService(string name) => Record($"StopService:{name}");

    public Result DeleteService(string name) => Record($"DeleteService:{name}");

    public Result DeleteRegistryKey(string hive, string path) => Record($"DeleteRegistryKey:{hive}\\{path}");

    public Result DeleteRegistryValue(string path, string name) => Record($"DeleteRegistryValue:{path}\\{name}");

    public Result RemoveCertificate(string storeLocation, string storeName, string thumbprintSha1) =>
        Record($"RemoveCertificate:{storeLocation}\\{storeName}\\{thumbprintSha1}");

    public Result DeleteCngKey(string keyName, string provider, bool machineKey) => Record($"DeleteCngKey:{keyName}");

    public Result UnregisterCertStore(string storeName) => Record($"UnregisterCertStore:{storeName}");

    public Result DeleteDirectory(string path) => Record($"DeleteDirectory:{path}");

    public Result DeleteFile(string path) => Record($"DeleteFile:{path}");

    public Result DeleteEventSource(string source) => Record($"DeleteEventSource:{source}");

    private Result Record(string call)
    {
        Calls.Add(call);
        if (ThrowOn.Contains(call))
        {
            throw new IOException($"fake exception: {call}");
        }

        return FailOn.Contains(call) ? Result.Failure($"fake failure: {call}") : Result.Success();
    }
}
