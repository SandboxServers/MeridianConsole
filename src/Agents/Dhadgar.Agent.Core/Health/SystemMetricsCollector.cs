using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Dhadgar.Agent.Core.Telemetry;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Core.Health;

/// <summary>
/// Collects system metrics for health reporting.
/// </summary>
public sealed class SystemMetricsCollector : ISystemMetricsCollector
{
    private readonly AgentMeter _meter;
    private readonly ILogger<SystemMetricsCollector> _logger;
    private readonly TimeProvider _timeProvider;

    // For CPU calculation (thread-safe access via lock)
    private readonly object _cpuLock = new();
    private DateTime _lastCpuCheck;
    private TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;
    private bool _isFirstCollection = true;

    public SystemMetricsCollector(
        AgentMeter meter,
        ILogger<SystemMetricsCollector> logger,
        TimeProvider? timeProvider = null)
    {
        _meter = meter ?? throw new ArgumentNullException(nameof(meter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastCpuCheck = _timeProvider.GetUtcNow().UtcDateTime;
    }

    public Task<Result<SystemMetrics>> CollectAsync(CancellationToken cancellationToken = default)
    {
        // Respect cancellation token
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Result<SystemMetrics>.Failure("[Metrics.Cancelled] Collection was cancelled"));
        }

        try
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

            return Task.FromResult(Result<SystemMetrics>.Success(metrics));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect system metrics");
            return Task.FromResult(Result<SystemMetrics>.Failure("[Metrics.CollectionFailed] Failed to collect system metrics"));
        }
    }

    private double GetCpuUsage()
    {
        try
        {
            // Capture CPU time outside the lock to minimize lock duration
            TimeSpan currentCpuTime;
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                currentCpuTime = process.TotalProcessorTime;
            }

            var currentTime = _timeProvider.GetUtcNow().UtcDateTime;

            // Thread-safe access to shared state
            lock (_cpuLock)
            {
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
            // Clamp to non-negative to handle edge cases where MemoryLoadBytes > TotalAvailableMemoryBytes
            return Math.Max(0, gcInfo.TotalAvailableMemoryBytes - gcInfo.MemoryLoadBytes);
        }
        catch
        {
            return 0;
        }
    }

    private List<DiskMetrics> GetDiskMetrics()
    {
        var disks = new List<DiskMetrics>();

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate drives");
            return disks;
        }

        foreach (var drive in drives)
        {
            try
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
            catch (Exception ex)
            {
                // Per-drive error handling: log and continue to other drives
                _logger.LogDebug(ex, "Failed to get metrics for drive {DriveName}", drive.Name);
            }
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

                // Some interfaces don't support IPv4 statistics (e.g., certain virtual adapters)
                if (!iface.Supports(System.Net.NetworkInformation.NetworkInterfaceComponent.IPv4))
                {
                    continue;
                }

                try
                {
                    var stats = iface.GetIPv4Statistics();

                    networks.Add(new NetworkMetrics
                    {
                        InterfaceName = iface.Name,
                        BytesReceived = stats.BytesReceived,
                        BytesSent = stats.BytesSent,
                        IsOperational = iface.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                    });
                }
                catch (NetworkInformationException)
                {
                    // Some interfaces may throw even if they claim to support IPv4
                    // (e.g., disconnected adapters on certain platforms)
                    continue;
                }
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
