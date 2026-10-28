using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Contracts.Agents;
using Microsoft.Win32;

namespace Dhadgar.Lab.Probe;

/// <summary>
/// Reads the agent's Windows footprint (service, registry key, certificate stores, state file, event source) and
/// returns metadata only. Read-only: no certificate material, no registry value contents, no service changes.
/// </summary>
[SupportedOSPlatform("windows")]
public static class FootprintReader
{
    private const int ServiceQueryTimeoutMs = 15_000;

    /// <summary>
    /// Builds the footprint. Returns false with a one-line <paramref name="error"/> when the registry key is
    /// unreadable for this account; nothing is thrown for it.
    /// </summary>
    public static bool TryRead(out ProbeFootprint? footprint, out string? error)
    {
        footprint = null;
        error = null;

        if (!TryReadRegistry(out RegistryInfo? registry, out error))
        {
            return false;
        }

        footprint = new ProbeFootprint(
            Service: ReadService(),
            Registry: registry!,
            MachineMy: ReadStore(StoreName.My, StoreLocation.LocalMachine),
            MachineRoot: ReadStore(StoreName.Root, StoreLocation.LocalMachine),
            AgentCa: ReadStore(AgentWindowsFootprint.CaStoreName, StoreLocation.LocalMachine),
            StateFile: ReadStateFile(),
            EventSourceExists: EventSourceExists());
        return true;
    }

    /// <summary>
    /// Presence and kind/length of each value under the agent key. The enrollment token entry is always present in
    /// the list, with <c>Present = false</c> when it is absent. A value's contents are read only to measure them.
    /// </summary>
    private static bool TryReadRegistry(out RegistryInfo? info, out string? error)
    {
        info = null;
        error = null;

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(AgentWindowsFootprint.RegistryKeyPath, writable: false);
            if (key is null)
            {
                info = new RegistryInfo(
                    Exists: false,
                    Values: [new RegistryValueInfo(AgentWindowsFootprint.EnrollmentTokenValueName, string.Empty, false, 0)]);
                return true;
            }

            var values = new List<RegistryValueInfo>();
            bool sawToken = false;
            foreach (string name in key.GetValueNames())
            {
                sawToken |= string.Equals(name, AgentWindowsFootprint.EnrollmentTokenValueName, StringComparison.Ordinal);
                values.Add(MeasureValue(key, name));
            }

            if (!sawToken)
            {
                values.Add(new RegistryValueInfo(AgentWindowsFootprint.EnrollmentTokenValueName, string.Empty, false, 0));
            }

            info = new RegistryInfo(Exists: true, Values: values);
            return true;
        }
        catch (SecurityException)
        {
            error = "registry read denied";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            error = "registry read denied";
            return false;
        }
    }

    private static RegistryValueInfo MeasureValue(RegistryKey key, string name)
    {
        RegistryValueKind kind = key.GetValueKind(name);
        object? value = key.GetValue(name);
        int length = value switch
        {
            string text => text.Length,
            byte[] bytes => bytes.Length,
            string[] lines => lines.Sum(line => line.Length),
            _ => 0,
        };

        // The value itself is not kept; only its length leaves this method.
        return new RegistryValueInfo(name, kind.ToString(), Present: true, Length: length);
    }

    private static ServiceInfo? ReadService()
    {
        string? qc = RunScQuery("qc", AgentWindowsFootprint.ServiceName);
        if (qc is null)
        {
            return null;
        }

        string? query = RunScQuery("query", AgentWindowsFootprint.ServiceName);
        return new ServiceInfo(
            State: LastToken(Field(query, "STATE")),
            StartType: LastToken(Field(qc, "START_TYPE")),
            Account: Field(qc, "SERVICE_START_NAME") ?? string.Empty,
            BinaryPath: Field(qc, "BINARY_PATH_NAME") ?? string.Empty);
    }

    /// <summary>
    /// Runs <c>sc.exe</c> with fixed arguments and returns its stdout, or null when the service does not exist or
    /// the call fails. The service name is a constant; arguments go through <see cref="ProcessStartInfo.ArgumentList"/>.
    /// </summary>
    private static string? RunScQuery(string verb, string serviceName)
    {
        var startInfo = new ProcessStartInfo("sc.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(verb);
        startInfo.ArgumentList.Add(serviceName);

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(ServiceQueryTimeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited between the check and the kill.
            }

            return null;
        }

        return process.ExitCode == 0 ? stdout.GetAwaiter().GetResult() : null;
    }

    private static string? Field(string? output, string key)
    {
        if (output is null)
        {
            return null;
        }

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith(key, StringComparison.Ordinal))
            {
                continue;
            }

            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            return line[(colon + 1)..].Trim();
        }

        return null;
    }

    private static string LastToken(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts[^1];
    }

    private static List<CertInfo> ReadStore(string storeName, StoreLocation location)
    {
        using var store = new X509Store(storeName, location);
        return ReadOpenStore(store);
    }

    private static List<CertInfo> ReadStore(StoreName storeName, StoreLocation location)
    {
        using var store = new X509Store(storeName, location);
        return ReadOpenStore(store);
    }

    private static List<CertInfo> ReadOpenStore(X509Store store)
    {
        try
        {
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        }
        catch (CryptographicException)
        {
            // A store that does not exist yet is an empty store.
            return [];
        }

        var certs = new List<CertInfo>();
        foreach (X509Certificate2 cert in store.Certificates)
        {
            using (cert)
            {
                certs.Add(new CertInfo(
                    Thumbprint: cert.Thumbprint,
                    Subject: cert.Subject,
                    Issuer: cert.Issuer,
                    Serial: cert.SerialNumber,
                    NotBefore: cert.NotBefore,
                    NotAfter: cert.NotAfter,
                    HasPrivateKey: cert.HasPrivateKey));
            }
        }

        return certs;
    }

    private static StateFileInfo ReadStateFile()
    {
        string root = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        string path = Path.GetFullPath(Path.Combine(root, AgentWindowsFootprint.StateDirectoryRelative, AgentWindowsFootprint.StateFileName));

        // The path is built from constants; the containment check keeps it under %ProgramData% regardless.
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return new StateFileInfo(Exists: false, Path: string.Empty, LastWrite: null);
        }

        if (!File.Exists(path))
        {
            return new StateFileInfo(Exists: false, Path: path, LastWrite: null);
        }

        return new StateFileInfo(Exists: true, Path: path, LastWrite: File.GetLastWriteTimeUtc(path));
    }

    private static bool EventSourceExists()
    {
        string keyPath = @"SYSTEM\CurrentControlSet\Services\EventLog\Application\" + AgentWindowsFootprint.EventLogSourceName;
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
        return key is not null;
    }
}
