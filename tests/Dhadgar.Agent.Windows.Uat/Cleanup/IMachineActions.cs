using System.Security.Cryptography.X509Certificates;
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

    /// <summary>
    /// Deletes the key only when it has no values and no subkeys. Missing counts as success; a non-empty key is a
    /// failure and is left in place (D-S238).
    /// </summary>
    Result DeleteEmptyRegistryKey(string hive, string path);

    /// <summary>Deletes a registry value.</summary>
    Result DeleteRegistryValue(string path, string name);

    /// <summary>Removes a certificate, by SHA-1 thumbprint, from a store.</summary>
    Result RemoveCertificate(string storeLocation, string storeName, string thumbprintSha1);

    /// <summary>
    /// Adds a public certificate to a store (P22: the TLS leaf and the decoy CA go to <c>LocalMachine\Root</c>).
    /// The caller passes a certificate without a private key; the implementation refuses one that has a key.
    /// </summary>
    Result AddCertificate(string storeLocation, string storeName, X509Certificate2 certificate);

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

    /// <summary>
    /// Uninstalls every product that the upgrade code lists (<c>msiexec /x</c>). Exit 1605 (unknown product) counts as
    /// success, so a second run finds nothing and does nothing (idempotent).
    /// </summary>
    Result UninstallMsiProduct(string upgradeCode);
}
