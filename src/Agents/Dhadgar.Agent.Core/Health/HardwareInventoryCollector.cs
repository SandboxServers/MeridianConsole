using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Dhadgar.Contracts.Nodes;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Core.Health;

/// <summary>
/// Collects the static hardware inventory sent to the control plane at enrollment.
/// Nodes uses it to size the node's initial capacity.
/// </summary>
public interface IHardwareInventoryCollector
{
    HardwareInventoryRequest Collect();
}

/// <summary>
/// Cross-platform hardware inventory using only BCL APIs.
/// Every probe is best-effort: a failure degrades that field instead of failing enrollment.
/// </summary>
public sealed class HardwareInventoryCollector : IHardwareInventoryCollector
{
    // Mirrors Nodes' validation limits on HardwareInventoryDto / NetworkInterfaceDto.
    private const int MaxHostnameLength = 255;
    private const int MaxOsVersionLength = 255;
    private const int MaxInterfaceNameLength = 100;
    private const int MaxInterfaces = 32;
    private const int MaxCpuCores = 1024;

    private readonly ILogger<HardwareInventoryCollector> _logger;

    public HardwareInventoryCollector(ILogger<HardwareInventoryCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public HardwareInventoryRequest Collect()
    {
        return new HardwareInventoryRequest(
            Hostname: Truncate(SafeHostname(), MaxHostnameLength),
            OsVersion: Truncate(RuntimeInformation.OSDescription, MaxOsVersionLength),
            CpuCores: Math.Clamp(Environment.ProcessorCount, 1, MaxCpuCores),
            MemoryBytes: Math.Max(1, TotalMemoryBytes()),
            DiskBytes: Math.Max(1, TotalFixedDiskBytes()),
            NetworkInterfaces: CollectNetworkInterfaces());
    }

    private static string SafeHostname()
    {
        var name = Environment.MachineName;
        return string.IsNullOrWhiteSpace(name) ? "unknown-host" : name;
    }

    private long TotalMemoryBytes()
    {
        try
        {
            // Physical memory visible to this process (honours container/cgroup limits).
            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not determine total memory");
            return 1;
        }
    }

    private long TotalFixedDiskBytes()
    {
        long total = 0;
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive is { IsReady: true, DriveType: DriveType.Fixed })
                    {
                        total += drive.TotalSize;
                    }
                }
                catch (IOException)
                {
                    // Drive went away or is inaccessible; skip it.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not enumerate drives");
        }

        return total;
    }

    private List<NetworkInterfaceInfo>? CollectNetworkInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Take(MaxInterfaces)
                .Select(n => new NetworkInterfaceInfo(
                    Name: Truncate(n.Name, MaxInterfaceNameLength),
                    MacAddress: FormatMac(n.GetPhysicalAddress()),
                    IpAddresses: n.GetIPProperties().UnicastAddresses
                        .Where(a => a.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                        .Select(a => a.Address.ToString())
                        .ToList()))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not enumerate network interfaces");
            return null;
        }
    }

    private static string? FormatMac(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 6)
        {
            return null; // Nodes accepts at most "AA:BB:CC:DD:EE:FF" (17 chars).
        }

        return string.Join(':', bytes.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
