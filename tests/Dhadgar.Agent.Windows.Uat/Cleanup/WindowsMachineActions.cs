using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Msi;
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
    private const int HResultFileNotFound = unchecked((int)0x80070002); // ERROR_FILE_NOT_FOUND, as a CryptographicException HResult
    private const int HResultBadKeyset = unchecked((int)0x80090016); // NTE_BAD_KEYSET: CNG key not found
    private const int HResultNotFound = unchecked((int)0x80090011); // NTE_NOT_FOUND
    private const int ScServiceDoesNotExist = 1060;
    private static readonly TimeSpan ServiceStopWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MsiexecTimeout = TimeSpan.FromMinutes(5);
    private const int MsiSuccessRebootRequested = 3010;
    private const int MsiUnknownProduct = 1605;
    private readonly IMsiProducts _msiProducts;
    private readonly Func<IReadOnlyList<string>, Result<CommandOutput>> _msiexec;
    private readonly string? _msiCleanupLogPath;

    /// <summary>
    /// Creates the machine actions over the real Windows Installer database and <c>msiexec.exe</c>.
    /// </summary>
    /// <param name="msiCleanupLogPath">
    /// Where the cleanup <c>msiexec /x</c> writes its verbose log (<c>/l*v</c>); null writes none.
    /// </param>
    public WindowsMachineActions(string? msiCleanupLogPath = null)
        : this(
            new MsiProducts(),
            args => CommandRunner.RunAsync("msiexec.exe", args, MsiexecTimeout).GetAwaiter().GetResult(),
            msiCleanupLogPath)
    {
    }

    /// <summary>Creates the machine actions over the given installer seams (tests pass fakes).</summary>
    internal WindowsMachineActions(
        IMsiProducts msiProducts,
        Func<IReadOnlyList<string>, Result<CommandOutput>> msiexec,
        string? msiCleanupLogPath = null)
    {
        _msiProducts = msiProducts;
        _msiexec = msiexec;
        _msiCleanupLogPath = msiCleanupLogPath;
    }

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
    public Result<ProcessImage> GetProcessImage(int pid, DateTime startTimeUtc)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (Math.Abs((process.StartTime.ToUniversalTime() - startTimeUtc).TotalSeconds) > 1)
            {
                return Result<ProcessImage>.Success(ProcessImage.NotRunning); // PID reused by another process.
            }

            var image = process.MainModule?.FileName;
            return string.IsNullOrEmpty(image)
                ? Result<ProcessImage>.Failure($"could not read the image of process {pid}")
                : Result<ProcessImage>.Success(ProcessImage.Running(image));
        }
        catch (ArgumentException)
        {
            return Result<ProcessImage>.Success(ProcessImage.NotRunning); // No such process.
        }
        catch (InvalidOperationException)
        {
            return Result<ProcessImage>.Success(ProcessImage.NotRunning); // Exited between lookup and read.
        }
        catch (Win32Exception ex)
        {
            return Result<ProcessImage>.Failure($"could not read the image of process {pid}: {ex.Message}");
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
    public Result DeleteEmptyRegistryKey(string hive, string path)
    {
        if (!string.Equals(hive, "HKLM", StringComparison.Ordinal))
        {
            return Result.Failure($"unsupported hive {hive}");
        }

        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return DeleteEmptySubKey(hklm, path);
    }

    /// <summary>Deletes <paramref name="path"/> under <paramref name="baseKey"/> only when it is empty. Tests pass HKCU.</summary>
    internal static Result DeleteEmptySubKey(RegistryKey baseKey, string path)
    {
        try
        {
            using (var key = baseKey.OpenSubKey(path, writable: false))
            {
                if (key is null)
                {
                    return Result.Success(); // Already gone.
                }

                if (key.SubKeyCount > 0 || key.ValueCount > 0)
                {
                    return Result.Failure($"registry key {path} is not empty; left in place");
                }
            }

            baseKey.DeleteSubKey(path, throwOnMissingSubKey: false);
            return Result.Success();
        }
        catch (InvalidOperationException)
        {
            return Result.Failure($"registry key {path} is not empty; left in place"); // A subkey appeared after the check.
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure($"registry delete denied: {ex.Message}");
        }
        catch (System.Security.SecurityException ex)
        {
            return Result.Failure($"registry delete denied: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result.Failure($"registry delete failed: {ex.Message}");
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
            try
            {
                foreach (var cert in matches)
                {
                    var removed = RemoveMatchedCertificate(cert, storeName, location == StoreLocation.LocalMachine, store.Remove);
                    if (removed.IsFailure)
                    {
                        return removed;
                    }
                }

                return Result.Success();
            }
            finally
            {
                foreach (var cert in matches)
                {
                    cert.Dispose();
                }

                matches.Clear();
            }
        }
        catch (CryptographicException ex) when (ex.HResult == HResultFileNotFound)
        {
            return Result.Success(); // Store does not exist: already gone.
        }
        catch (CryptographicException ex)
        {
            return Result.Failure($"certificate removal failed: 0x{ex.HResult:X8} {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Result AddCertificate(string storeLocation, string storeName, X509Certificate2 certificate)
    {
        // P22 (D-S240): the TLS leaf and the decoy CA are the only machine certificate additions the UAT makes,
        // so every other store is refused before anything is parsed or opened.
        if (!string.Equals(storeLocation, "LocalMachine", StringComparison.Ordinal)
            || !string.Equals(storeName, "Root", StringComparison.Ordinal))
        {
            return Result.Failure($"refusing to add a certificate to {storeLocation}\\{storeName}; only LocalMachine\\Root is allowed");
        }

        if (!Enum.TryParse<StoreLocation>(storeLocation, out var location))
        {
            return Result.Failure($"unknown store location {storeLocation}");
        }

        if (certificate.HasPrivateKey)
        {
            return Result.Failure("refusing to add a certificate with a private key to a store");
        }

        try
        {
            using var store = new X509Store(storeName, location);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);
            return Result.Success();
        }
        catch (CryptographicException ex)
        {
            return Result.Failure($"certificate add failed: 0x{ex.HResult:X8} {ex.Message}");
        }
    }

    /// <summary>True for a certificate the driver or the agent CA created: the agent CA, an agent client certificate, or a UAT subject.</summary>
    private static bool IsDriverCertificate(X509Certificate2 cert) =>
        AgentCertificateMatcher.IsAgentCa(cert)
        || AgentCertificateMatcher.IsAgentClientCertificate(cert, new DateTimeOffset(cert.NotBefore))
        || cert.Subject.Contains("O=Meridian Console UAT", StringComparison.OrdinalIgnoreCase);

    private const string MyStoreName = "My";
    private const string KeyStorageProviderName = "Microsoft Software Key Storage Provider";
    private const string PlatformCryptoProviderName = "Microsoft Platform Crypto Provider";

    /// <summary>
    /// Removes one matched certificate. An agent client certificate in <c>My</c> has its own private key deleted first
    /// (the agent's key has a Windows-assigned name, so it is reached through its certificate, never by name; D-S238).
    /// If the key delete fails, the certificate stays so a rerun can find the key again.
    /// </summary>
    internal static Result RemoveMatchedCertificate(
        X509Certificate2 cert, string storeName, bool requireMachineKey, Action<X509Certificate2> remove)
    {
        // A forged thumbprint in the manifest must not remove an unrelated certificate.
        if (!IsDriverCertificate(cert))
        {
            return Result.Failure($"certificate {cert.Thumbprint} is not an agent or UAT certificate; not removed");
        }

        if (string.Equals(storeName, MyStoreName, StringComparison.Ordinal)
            && AgentCertificateMatcher.IsAgentClientIdentity(cert))
        {
            var keyDeleted = DeleteCertificateKey(cert, requireMachineKey);
            if (keyDeleted.IsFailure)
            {
                return Result.Failure($"certificate {cert.Thumbprint} kept: {keyDeleted.Error}");
            }
        }

        remove(cert);
        return Result.Success();
    }

    /// <summary>
    /// Deletes the persisted CNG key of <paramref name="cert"/>. Only an ECDSA CNG key under one of the two Microsoft
    /// providers is deleted, and only a machine key when <paramref name="requireMachineKey"/> is true. A key that is
    /// already gone counts as success.
    /// </summary>
    internal static Result DeleteCertificateKey(X509Certificate2 cert, bool requireMachineKey)
    {
        if (!cert.HasPrivateKey)
        {
            return Result.Success();
        }

        try
        {
            using var ecdsa = cert.GetECDsaPrivateKey();
            if (ecdsa is not ECDsaCng cng)
            {
                return Result.Failure("private key is not an ECDSA CNG key; not deleted");
            }

            using var key = cng.Key;
            var provider = key.Provider?.Provider;
            if (provider is not (KeyStorageProviderName or PlatformCryptoProviderName))
            {
                return Result.Failure($"private key provider {provider ?? "(none)"} is not allowed; not deleted");
            }

            if (requireMachineKey && !key.IsMachineKey)
            {
                return Result.Failure("private key is not a machine key; not deleted");
            }

            key.Delete();
            return Result.Success();
        }
        catch (CryptographicException ex) when (ex.HResult is HResultBadKeyset or HResultNotFound)
        {
            return Result.Success(); // Key does not exist: already gone.
        }
        catch (CryptographicException ex)
        {
            return Result.Failure($"certificate key delete failed: 0x{ex.HResult:X8} {ex.Message}");
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
        catch (CryptographicException ex) when (ex.HResult is HResultBadKeyset or HResultNotFound)
        {
            return Result.Success(); // Key does not exist: already gone.
        }
        catch (CryptographicException ex)
        {
            return Result.Failure($"cng key delete failed: 0x{ex.HResult:X8} {ex.Message}");
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
            File.Delete(path);   // silent when the file is missing but its folder exists
            return Result.Success();
        }
        catch (DirectoryNotFoundException)
        {
            // D-S243: the folder is gone, so the file is too (an earlier pass removed the folder). Already undone.
            // CleanupEngine.DeleteGuarded validated the path before this call; that allowlist is unchanged.
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

    /// <inheritdoc />
    public Result UninstallMsiProduct(string upgradeCode) =>
        UninstallRelatedProducts(_msiProducts, upgradeCode, _msiexec, _msiCleanupLogPath);

    /// <summary>
    /// Runs <c>msiexec /x</c> for each product the upgrade code lists. Exit 0, 3010 (reboot requested) and 1605
    /// (unknown product, already gone) are success. Every product is attempted; the failures are joined. When the
    /// installer cannot list the products, absence is unproven and the result is a failure, never a success.
    /// </summary>
    internal static Result UninstallRelatedProducts(
        IMsiProducts products,
        string upgradeCode,
        Func<IReadOnlyList<string>, Result<CommandOutput>> msiexec,
        string? logPath = null)
    {
        var related = products.RelatedProducts(upgradeCode);
        if (related.IsFailure)
        {
            return Result.Failure($"related products unreadable, cannot prove the MSI is gone: {related.Error}");
        }

        var failures = new List<string>();
        foreach (var product in related.Value)
        {
            var args = new List<string> { "/x", product, "/qn", "/norestart" };
            if (logPath is not null)
            {
                args.Add("/l*v");
                args.Add(logPath);
            }

            var output = msiexec(args);
            if (output.IsFailure)
            {
                failures.Add(output.Error!);
                continue;
            }

            var exitCode = output.Value.ExitCode;
            if (exitCode is 0 or MsiSuccessRebootRequested or MsiUnknownProduct)
            {
                continue;
            }

            failures.Add($"msiexec /x {product} exited {exitCode}");
        }

        return failures.Count == 0 ? Result.Success() : Result.Failure(string.Join("; ", failures));
    }

    private static Result<CommandOutput> RunSc(IReadOnlyList<string> args) =>
        CommandRunner.RunAsync("sc.exe", args, ToolTimeout).GetAwaiter().GetResult();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

    [LibraryImport("crypt32.dll", EntryPoint = "CertUnregisterSystemStore", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CertUnregisterSystemStore(string storeName, uint flags);
}
