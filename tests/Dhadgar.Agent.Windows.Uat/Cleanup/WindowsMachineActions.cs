using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Shared.Results;
using Microsoft.Win32;

namespace Dhadgar.Agent.Windows.Uat.Cleanup;

/// <summary>
/// The real <see cref="IMachineActions"/>. Every method is idempotent: "already gone" is success.
/// Writes to LocalMachine happen only from undo, and only for entries the manifest recorded.
/// </summary>
internal sealed partial class WindowsMachineActions : IMachineActions
{
    private const uint CertSystemStoreLocalMachine = 0x00020000;
    private const uint CertStoreDeleteFlag = 0x10;
    private const int Win32FileNotFound = 2;
    private const int ScServiceDoesNotExist = 1060;
    private static readonly TimeSpan ServiceStopWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(60);

    /// <inheritdoc />
    public bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (SystemException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Result RemoveContainer(string name)
    {
        var output = CommandRunner.RunAsync("docker", ["rm", "-f", name], ToolTimeout).GetAwaiter().GetResult();
        if (output.IsFailure)
        {
            return Result.Failure(output.Error);
        }

        var value = output.Value;
        if (value.ExitCode == 0 || value.StandardError.Contains("No such container", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success();
        }

        return Result.Failure($"docker rm -f exited {value.ExitCode}");
    }

    /// <inheritdoc />
    public Result KillProcess(int pid, DateTime startTimeUtc)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (Math.Abs((process.StartTime.ToUniversalTime() - startTimeUtc).TotalSeconds) > 1)
            {
                return Result.Success(); // PID reused by another process.
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(TimeSpan.FromSeconds(15));
            return Result.Success();
        }
        catch (ArgumentException)
        {
            return Result.Success(); // No such process.
        }
        catch (InvalidOperationException)
        {
            return Result.Success(); // Exited between lookup and kill.
        }
        catch (Win32Exception ex)
        {
            return Result.Failure($"could not kill process {pid}: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Result StopService(string name)
    {
        var stop = RunSc(["stop", name]);
        if (stop.IsFailure)
        {
            return Result.Failure(stop.Error);
        }

        var deadline = DateTime.UtcNow + ServiceStopWait;
        while (DateTime.UtcNow < deadline)
        {
            var query = RunSc(["query", name]);
            if (query.IsFailure)
            {
                return Result.Failure(query.Error);
            }

            if (query.Value.ExitCode == ScServiceDoesNotExist || query.Value.StandardOutput.Contains("STOPPED", StringComparison.Ordinal))
            {
                return Result.Success();
            }

            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        return Result.Failure($"service {name} did not reach STOPPED within 30 s");
    }

    /// <inheritdoc />
    public Result DeleteService(string name)
    {
        var deleted = RunSc(["delete", name]);
        if (deleted.IsFailure)
        {
            return Result.Failure(deleted.Error);
        }

        return deleted.Value.ExitCode is 0 or ScServiceDoesNotExist
            ? Result.Success()
            : Result.Failure($"sc delete exited {deleted.Value.ExitCode}");
    }

    /// <inheritdoc />
    public Result DeleteRegistryKey(string hive, string path)
    {
        if (!string.Equals(hive, "HKLM", StringComparison.Ordinal))
        {
            return Result.Failure($"unsupported hive {hive}");
        }

        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure($"registry delete denied: {ex.Message}");
        }
        catch (System.Security.SecurityException ex)
        {
            return Result.Failure($"registry delete denied: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Result DeleteRegistryValue(string path, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure($"registry value delete denied: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Result RemoveCertificate(string storeLocation, string storeName, string thumbprintSha1)
    {
        if (!Enum.TryParse<StoreLocation>(storeLocation, out var location))
        {
            return Result.Failure($"unknown store location {storeLocation}");
        }

        try
        {
            using var store = new X509Store(storeName, location);
            store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
            var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprintSha1, validOnly: false);
            foreach (var cert in matches)
            {
                store.Remove(cert);
                cert.Dispose();
            }

            matches.Clear();
            return Result.Success();
        }
        catch (CryptographicException)
        {
            return Result.Success(); // Store does not exist: already gone.
        }
    }

    /// <inheritdoc />
    public Result DeleteCngKey(string keyName, string provider, bool machineKey)
    {
        try
        {
            var options = machineKey ? CngKeyOpenOptions.MachineKey : CngKeyOpenOptions.None;
            using var key = CngKey.Open(keyName, new CngProvider(provider), options);
            key.Delete();
            return Result.Success();
        }
        catch (CryptographicException)
        {
            return Result.Success(); // Key does not exist: already gone.
        }
    }

    /// <inheritdoc />
    public Result UnregisterCertStore(string storeName)
    {
        if (CertUnregisterSystemStore(storeName, CertSystemStoreLocalMachine | CertStoreDeleteFlag))
        {
            return Result.Success();
        }

        var error = Marshal.GetLastPInvokeError();
        return error == Win32FileNotFound
            ? Result.Success()
            : Result.Failure($"CertUnregisterSystemStore failed with Win32 error {error}");
    }

    /// <inheritdoc />
    public Result DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failure($"directory delete failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure($"directory delete denied: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Result DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failure($"file delete failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure($"file delete denied: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes an event source by removing its key under the Application log, which is where
    /// <c>EventLog.DeleteEventSource</c> removes it. A missing key is success.
    /// </summary>
    public Result DeleteEventSource(string source)
    {
        return DeleteRegistryKey("HKLM", $@"SYSTEM\CurrentControlSet\Services\EventLog\Application\{source}");
    }

    private static Result<CommandOutput> RunSc(IReadOnlyList<string> args) =>
        CommandRunner.RunAsync("sc.exe", args, ToolTimeout).GetAwaiter().GetResult();

    [LibraryImport("crypt32.dll", EntryPoint = "CertUnregisterSystemStore", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CertUnregisterSystemStore(string storeName, uint flags);
}
