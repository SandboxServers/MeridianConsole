using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Windows.Uat.Cleanup;

/// <summary>
/// What a process probe found. <see cref="NotRunning"/> means no process with that PID and start time runs
/// (gone, or the PID was reused): killing it is a no-op. A probe that cannot read a running process's image
/// returns a failure instead, never a value.
/// </summary>
internal sealed record ProcessImage(bool IsRunning, string Path)
{
    /// <summary>No process with that PID and start time runs.</summary>
    public static ProcessImage NotRunning { get; } = new(false, string.Empty);

    /// <summary>A running process and its full image path.</summary>
    public static ProcessImage Running(string path) => new(true, path);
}

/// <summary>
/// Every machine change the driver can undo, one method per kind. Each method returns a <see cref="Result"/>
/// and treats "already gone" as success, so undo is idempotent. Fakes stand in for it in unit tests.
/// </summary>
internal interface IMachineActions
{
    /// <summary>True when the driver runs elevated.</summary>
    bool IsElevated();

    /// <summary>Force-removes a Docker container (<c>docker rm -f</c>).</summary>
    Result RemoveContainer(string name);

    /// <summary>Kills a process tree if a process with this PID and start time still runs.</summary>
    Result KillProcess(int pid, DateTime startTimeUtc);

    /// <summary>
    /// Probes the process with this PID and start time. Success is <see cref="ProcessImage.NotRunning"/> or a running
    /// process with a non-empty image path. A failure means the image of a running process could not be read.
    /// </summary>
    Result<ProcessImage> GetProcessImage(int pid, DateTime startTimeUtc);

    /// <summary>Stops a service and waits up to 30 seconds for STOPPED.</summary>
    Result StopService(string name);

    /// <summary>Deletes a service.</summary>
    Result DeleteService(string name);

    /// <summary>Deletes a registry key tree.</summary>
    Result DeleteRegistryKey(string hive, string path);

    /// <summary>Deletes a registry value.</summary>
    Result DeleteRegistryValue(string path, string name);

    /// <summary>Removes a certificate, by SHA-1 thumbprint, from a store.</summary>
    Result RemoveCertificate(string storeLocation, string storeName, string thumbprintSha1);

    /// <summary>Deletes a CNG key.</summary>
    Result DeleteCngKey(string keyName, string provider, bool machineKey);

    /// <summary>Unregisters a system certificate store (local machine).</summary>
    Result UnregisterCertStore(string storeName);

    /// <summary>Deletes a directory recursively.</summary>
    Result DeleteDirectory(string path);

    /// <summary>Deletes a file.</summary>
    Result DeleteFile(string path);

    /// <summary>Deletes an event log source.</summary>
    Result DeleteEventSource(string source);
}
