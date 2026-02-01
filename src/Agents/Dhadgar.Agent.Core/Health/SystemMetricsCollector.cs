using System.Diagnostics;
using System.Runtime.InteropServices;
using Dhadgar.Agent.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Core.Health;

/// <summary>
/// Collects system metrics for health reporting.
/// </summary>
public sealed class SystemMetricsCollector : ISystemMetricsCollector
{
    private readonly AgentMeter _meter;
    private readonly ILogger<SystemMetricsCollector> _logger;

    // For CPU calculation
    private DateTime _lastCpuCheck = DateTime.UtcNow;
    private TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;
    private bool _isFirstCollection = true;

    public SystemMetricsCollector(
        AgentMeter meter,
        ILogger<SystemMetricsCollector> logger)
    {
        _meter = meter ?? throw new ArgumentNullException(nameof(meter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SystemMetrics> CollectAsync(CancellationToken cancellationToken = default)
    {
        var metrics = new SystemMetrics
        {
            CpuUsagePercent = GetCpuUsage(),
            TotalMemoryBytes = GetTotalMemory(),
            AvailableMemoryBytes = GetAvailableMemory(),
            Disks = GetDiskMetrics(),
            Networks = GetNetworkMetrics(),
            SystemUptime = GetSystemUptime(),
            ProcessorCount = Environment.ProcessorCount,
            OsDescription = RuntimeInformation.OSDescription
        };

        // Update telemetry meter
        _meter.UpdateSystemMetrics(metrics.CpuUsagePercent, metrics.UsedMemoryBytes);

        return Task.FromResult(metrics);
    }

    private double GetCpuUsage()
    {
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var currentTime = DateTime.UtcNow;
            var currentCpuTime = process.TotalProcessorTime;

            if (_isFirstCollection)
            {
                _lastCpuCheck = currentTime;
                _lastTotalProcessorTime = currentCpuTime;
                _isFirstCollection = false;
                return 0;
            }

            var timeDiff = currentTime - _lastCpuCheck;
            var cpuDiff = currentCpuTime - _lastTotalProcessorTime;

            _lastCpuCheck = currentTime;
            _lastTotalProcessorTime = currentCpuTime;

            if (timeDiff.TotalMilliseconds > 0)
            {
                // Calculate CPU percentage across all processors
                var cpuPercent = (cpuDiff.TotalMilliseconds / timeDiff.TotalMilliseconds)
                    / Environment.ProcessorCount * 100;
                return Math.Min(100, Math.Max(0, cpuPercent));
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get CPU usage");
            return 0;
        }
    }

    private static long GetTotalMemory()
    {
        try
        {
            // GC.GetGCMemoryInfo() provides memory info that respects container limits
            var gcInfo = GC.GetGCMemoryInfo();
            return gcInfo.TotalAvailableMemoryBytes;
        }
        catch
        {
            return 0;
        }
    }

    private static long GetAvailableMemory()
    {
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            // Available = Total - (Heap + unmanaged used)
            return gcInfo.TotalAvailableMemoryBytes - gcInfo.MemoryLoadBytes;
        }
        catch
        {
            return 0;
        }
    }

    private List<DiskMetrics> GetDiskMetrics()
    {
        var disks = new List<DiskMetrics>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                // Only include fixed drives (not network, CD-ROM, etc.)
                if (drive.DriveType != DriveType.Fixed)
                {
                    continue;
                }

                disks.Add(new DiskMetrics
                {
                    Name = drive.Name,
                    TotalBytes = drive.TotalSize,
                    AvailableBytes = drive.AvailableFreeSpace
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get disk metrics");
        }

        return disks;
    }

    private List<NetworkMetrics> GetNetworkMetrics()
    {
        var networks = new List<NetworkMetrics>();

        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();

            foreach (var iface in interfaces)
            {
                // Skip loopback and non-operational interfaces
                if (iface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var stats = iface.GetIPv4Statistics();

                networks.Add(new NetworkMetrics
                {
                    InterfaceName = iface.Name,
                    BytesReceived = stats.BytesReceived,
                    BytesSent = stats.BytesSent,
                    IsOperational = iface.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get network metrics");
        }

        return networks;
    }

    private static TimeSpan GetSystemUptime()
    {
        try
        {
            return TimeSpan.FromMilliseconds(Environment.TickCount64);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }
}
