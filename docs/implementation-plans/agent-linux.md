# Agent Linux Implementation Plan

> **Status**: Blocked on Agent.Core
> **Last Updated**: 2026-02-01
> **Current State**: Scaffolding only - depends on Agent.Core completion

## Executive Summary

The `Dhadgar.Agent.Linux` project provides the Linux-specific implementation of the customer-hosted agent. It extends `Agent.Core` with Linux-specific functionality:

- systemd service integration (notify protocol)
- File-based certificate storage
- cgroups v2 for process resource limits
- Linux namespaces for process isolation (optional)
- journald logging integration

**Prerequisites**: Agent.Core must be complete before starting this implementation.

---

## Documentation Validation

All Linux-specific approaches validated against official documentation:

| Component | Validated Source | Key Details |
|-----------|-----------------|-------------|
| systemd Hosting | [MS Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/linux-service) | `Microsoft.Extensions.Hosting.Systemd` v10.0.1, `UseSystemd()` |
| cgroups v2 | [Kernel Docs](https://docs.kernel.org/admin-guide/cgroup-v2.html) | File-based API, `cpu.max`, `memory.max`, `memory.high` |
| .NET cgroups awareness | [MS Learn](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/environment-processorcount-on-windows) | `Environment.ProcessorCount` respects cgroups since .NET 6 |
| X509 on Linux | [MS Learn](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography) | `LocalMachine\My` NOT supported - use file-based storage |

### Critical: Linux Certificate Storage

**Windows Certificate Store APIs do NOT work on Linux for LocalMachine:**

From Microsoft docs:
> On Linux, the `LocalMachine\My` store is NOT supported (❌).

**Solution**: File-based certificate storage:
- Location: `/etc/dhadgar/certs/`
- Private key: `/etc/dhadgar/certs/agent.key` (mode 600)
- Certificate: `/etc/dhadgar/certs/agent.crt` (mode 644)
- CA cert: `/etc/dhadgar/certs/ca.crt` (mode 644)

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Phase 1: systemd Service Hosting](#phase-1-systemd-service-hosting)
3. [Phase 2: File-Based Certificate Store](#phase-2-file-based-certificate-store)
4. [Phase 3: cgroups v2 Process Isolation](#phase-3-cgroups-v2-process-isolation)
5. [Phase 4: journald Logging](#phase-4-journald-logging)
6. [Phase 5: Linux Namespaces](#phase-5-linux-namespaces)
7. [Phase 6: Package Distribution](#phase-6-package-distribution)
8. [Dependencies](#dependencies)
9. [Success Criteria](#success-criteria)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                       Dhadgar.Agent.Linux                           │
├─────────────────────────────────────────────────────────────────────┤
│  Program.cs                                                          │
│  ├── UseSystemd()                                                   │
│  ├── AddLinuxCertificateStore()                                     │
│  └── AddLinuxProcessManager()                                       │
├─────────────────────────────────────────────────────────────────────┤
│  Linux/                                                              │
│  ├── LinuxCertificateStore.cs      # ICertificateStore impl         │
│  ├── LinuxProcessManager.cs        # IProcessManager impl           │
│  ├── CgroupManager.cs              # cgroups v2 file operations     │
│  ├── NamespaceManager.cs           # Linux namespace setup          │
│  ├── UserManager.cs                # Game server user creation      │
│  └── SignalHandler.cs              # SIGTERM/SIGKILL handling       │
├─────────────────────────────────────────────────────────────────────┤
│  Systemd/                                                            │
│  ├── SystemdNotify.cs              # sd_notify protocol             │
│  └── dhadgar-agent.service         # Unit file template             │
└─────────────────────────────────────────────────────────────────────┘
        │
        │ References
        ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        Dhadgar.Agent.Core                           │
│  (Configuration, Communication, Commands, etc.)                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Phase 1: systemd Service Hosting

**Goal**: Run the agent as a systemd service with proper lifecycle management.

### Tasks

#### 1.1 Update Program.cs

```csharp
using Dhadgar.Agent.Core.Hosting;
using Dhadgar.Agent.Linux.Linux;
using Microsoft.Extensions.Hosting.Systemd;

var builder = Host.CreateApplicationBuilder(args);

// Configure for systemd
builder.Services.AddSystemd();

// Add Agent.Core defaults
builder.ConfigureAgentDefaults();

// Add Linux-specific implementations
builder.Services.AddSingleton<ICertificateStore, LinuxCertificateStore>();
builder.Services.AddSingleton<IProcessManager, LinuxProcessManager>();

// Configure journald logging (via systemd hosting)
builder.Logging.AddSystemdConsole();

var host = builder.Build();
await host.RunAsync();
```

#### 1.2 Add Package Reference

```xml
<PackageReference Include="Microsoft.Extensions.Hosting.Systemd" Version="10.0.1" />
```

#### 1.3 Create systemd Unit File

**`Systemd/dhadgar-agent.service`**:
```ini
[Unit]
Description=Meridian Console Agent
Documentation=https://docs.meridianconsole.com/agent
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
ExecStart=/opt/dhadgar/agent/Dhadgar.Agent.Linux
WorkingDirectory=/opt/dhadgar/agent
User=dhadgar-agent
Group=dhadgar-agent

# Security hardening
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
PrivateTmp=yes
PrivateDevices=yes
ProtectKernelTunables=yes
ProtectKernelModules=yes
ProtectControlGroups=no
# Note: ProtectControlGroups=no required to manage game server cgroups

# Allow binding to game server ports
AmbientCapabilities=CAP_NET_BIND_SERVICE

# Restart policy
Restart=always
RestartSec=5
WatchdogSec=60

# Resource limits for agent itself
MemoryMax=256M
CPUQuota=10%

# Delegate cgroups to agent for game server management
Delegate=yes

# Logging
StandardOutput=journal
StandardError=journal
SyslogIdentifier=dhadgar-agent

[Install]
WantedBy=multi-user.target
```

#### 1.4 Implement systemd Notify

```csharp
namespace Dhadgar.Agent.Linux.Systemd;

public static class SystemdNotify
{
    private const string ReadyMessage = "READY=1";
    private const string StoppingMessage = "STOPPING=1";
    private const string WatchdogMessage = "WATCHDOG=1";

    private static readonly string? NotifySocket =
        Environment.GetEnvironmentVariable("NOTIFY_SOCKET");

    public static void Ready()
    {
        if (NotifySocket is null) return;
        SendMessage(ReadyMessage);
    }

    public static void Stopping()
    {
        if (NotifySocket is null) return;
        SendMessage(StoppingMessage);
    }

    public static void Watchdog()
    {
        if (NotifySocket is null) return;
        SendMessage(WatchdogMessage);
    }

    public static void Status(string status)
    {
        if (NotifySocket is null) return;
        SendMessage($"STATUS={status}");
    }

    private static void SendMessage(string message)
    {
        // Implementation uses Unix domain socket
        // The AddSystemd() extension handles this automatically
    }
}
```

### Deliverables
- [ ] Updated `Program.cs` with systemd support
- [ ] `Systemd/dhadgar-agent.service` unit file
- [ ] `Systemd/SystemdNotify.cs` (or rely on hosting package)
- [ ] Installation script for systemd

### Estimated Effort
~2-3 hours

---

## Phase 2: File-Based Certificate Store

**Goal**: Implement `ICertificateStore` using secure file storage.

### Tasks

#### 2.1 Implement LinuxCertificateStore

```csharp
namespace Dhadgar.Agent.Linux.Linux;

public sealed class LinuxCertificateStore : ICertificateStore
{
    private const string CertDirectory = "/etc/dhadgar/certs";
    private const string AgentCertFile = "agent.crt";
    private const string AgentKeyFile = "agent.key";
    private const string CaCertFile = "ca.crt";

    private readonly ILogger<LinuxCertificateStore> _logger;

    public LinuxCertificateStore(ILogger<LinuxCertificateStore> logger)
    {
        _logger = logger;
        EnsureDirectoryExists();
    }

    public async Task<X509Certificate2> GetClientCertificateAsync(CancellationToken cancellationToken = default)
    {
        var certPath = Path.Combine(CertDirectory, AgentCertFile);
        var keyPath = Path.Combine(CertDirectory, AgentKeyFile);

        if (!File.Exists(certPath) || !File.Exists(keyPath))
        {
            throw new InvalidOperationException(
                $"Agent certificate not found. Expected at {certPath} and {keyPath}");
        }

        // Load certificate and combine with private key
        var certPem = await File.ReadAllTextAsync(certPath, cancellationToken);
        var keyPem = await File.ReadAllTextAsync(keyPath, cancellationToken);

        var certificate = X509Certificate2.CreateFromPem(certPem, keyPem);

        _logger.LogDebug("Loaded certificate: {Subject}, expires: {NotAfter}",
            certificate.Subject, certificate.NotAfter);

        return certificate;
    }

    public async Task StoreCertificateAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default)
    {
        var certPath = Path.Combine(CertDirectory, AgentCertFile);
        var keyPath = Path.Combine(CertDirectory, AgentKeyFile);

        // Export certificate (PEM format)
        var certPem = certificate.ExportCertificatePem();
        await File.WriteAllTextAsync(certPath, certPem, cancellationToken);

        // Set certificate file permissions (644)
        File.SetUnixFileMode(certPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        // Export private key (PEM format)
        if (certificate.GetRSAPrivateKey() is RSA rsa)
        {
            var keyPem = rsa.ExportRSAPrivateKeyPem();
            await File.WriteAllTextAsync(keyPath, keyPem, cancellationToken);

            // Set private key permissions (600 - owner only)
            File.SetUnixFileMode(keyPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        else if (certificate.GetECDsaPrivateKey() is ECDsa ecdsa)
        {
            var keyPem = ecdsa.ExportECPrivateKeyPem();
            await File.WriteAllTextAsync(keyPath, keyPem, cancellationToken);
            File.SetUnixFileMode(keyPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        _logger.LogInformation("Certificate stored at {CertPath}", certPath);
    }

    public Task<bool> HasValidCertificateAsync(CancellationToken cancellationToken = default)
    {
        var certPath = Path.Combine(CertDirectory, AgentCertFile);
        var keyPath = Path.Combine(CertDirectory, AgentKeyFile);

        if (!File.Exists(certPath) || !File.Exists(keyPath))
        {
            return Task.FromResult(false);
        }

        try
        {
            var certPem = File.ReadAllText(certPath);
            var certificate = X509Certificate2.CreateFromPem(certPem);

            // Check if certificate is still valid
            var isValid = certificate.NotAfter > DateTime.UtcNow &&
                          certificate.NotBefore < DateTime.UtcNow;

            return Task.FromResult(isValid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate certificate");
            return Task.FromResult(false);
        }
    }

    public async Task<X509Certificate2> GetCaCertificateAsync(CancellationToken cancellationToken = default)
    {
        var caPath = Path.Combine(CertDirectory, CaCertFile);

        if (!File.Exists(caPath))
        {
            throw new InvalidOperationException($"CA certificate not found at {caPath}");
        }

        var caPem = await File.ReadAllTextAsync(caPath, cancellationToken);
        return X509Certificate2.CreateFromPem(caPem);
    }

    public Task ClearCertificatesAsync(CancellationToken cancellationToken = default)
    {
        var certPath = Path.Combine(CertDirectory, AgentCertFile);
        var keyPath = Path.Combine(CertDirectory, AgentKeyFile);

        if (File.Exists(certPath))
        {
            File.Delete(certPath);
            _logger.LogInformation("Deleted {Path}", certPath);
        }

        if (File.Exists(keyPath))
        {
            // Securely wipe key file before deletion
            SecureDelete(keyPath);
            _logger.LogInformation("Securely deleted {Path}", keyPath);
        }

        return Task.CompletedTask;
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(CertDirectory))
        {
            Directory.CreateDirectory(CertDirectory);
            // Set directory permissions (700)
            File.SetUnixFileMode(CertDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void SecureDelete(string path)
    {
        var fileInfo = new FileInfo(path);
        var length = fileInfo.Length;

        // Overwrite with random data
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            var random = new byte[4096];
            Random.Shared.NextBytes(random);

            for (long i = 0; i < length; i += random.Length)
            {
                var bytesToWrite = (int)Math.Min(random.Length, length - i);
                fs.Write(random, 0, bytesToWrite);
            }

            fs.Flush();
        }

        File.Delete(path);
    }
}
```

### Deliverables
- [ ] `Linux/LinuxCertificateStore.cs`
- [ ] Secure file permission handling
- [ ] Secure key deletion
- [ ] Unit tests with temp directories

### Estimated Effort
~3-4 hours

---

## Phase 3: cgroups v2 Process Isolation

**Goal**: Implement `IProcessManager` using cgroups v2 for resource limits.

### Tasks

#### 3.1 Implement cgroups Manager

```csharp
namespace Dhadgar.Agent.Linux.Linux;

/// <summary>
/// Manages cgroups v2 for game server process isolation.
/// Requires systemd service with Delegate=yes.
/// </summary>
public sealed class CgroupManager : IDisposable
{
    private const string CgroupBasePath = "/sys/fs/cgroup";
    private const string AgentSlice = "dhadgar-servers.slice";
    private readonly ILogger<CgroupManager> _logger;

    public CgroupManager(ILogger<CgroupManager> logger)
    {
        _logger = logger;
        EnsureSliceExists();
    }

    /// <summary>
    /// Create a cgroup for a game server with resource limits.
    /// </summary>
    public async Task<string> CreateCgroupAsync(
        Guid serverId,
        ResourceLimits? limits,
        CancellationToken cancellationToken = default)
    {
        var cgroupName = $"dhadgar-server-{serverId:N}.scope";
        var cgroupPath = Path.Combine(CgroupBasePath, AgentSlice, cgroupName);

        // Create the cgroup directory
        Directory.CreateDirectory(cgroupPath);

        // Apply resource limits
        if (limits is not null)
        {
            await ApplyLimitsAsync(cgroupPath, limits, cancellationToken);
        }

        _logger.LogInformation("Created cgroup: {CgroupPath}", cgroupPath);
        return cgroupPath;
    }

    /// <summary>
    /// Move a process into a cgroup.
    /// </summary>
    public async Task AddProcessAsync(
        string cgroupPath,
        int pid,
        CancellationToken cancellationToken = default)
    {
        var procsPath = Path.Combine(cgroupPath, "cgroup.procs");
        await File.WriteAllTextAsync(procsPath, pid.ToString(), cancellationToken);

        _logger.LogDebug("Added PID {Pid} to cgroup {CgroupPath}", pid, cgroupPath);
    }

    /// <summary>
    /// Get current resource usage for a cgroup.
    /// </summary>
    public async Task<CgroupResourceUsage> GetUsageAsync(
        string cgroupPath,
        CancellationToken cancellationToken = default)
    {
        var cpuStatPath = Path.Combine(cgroupPath, "cpu.stat");
        var memoryCurrentPath = Path.Combine(cgroupPath, "memory.current");

        long cpuUsageUsec = 0;
        long memoryBytes = 0;

        if (File.Exists(cpuStatPath))
        {
            var cpuStat = await File.ReadAllTextAsync(cpuStatPath, cancellationToken);
            foreach (var line in cpuStat.Split('\n'))
            {
                if (line.StartsWith("usage_usec"))
                {
                    cpuUsageUsec = long.Parse(line.Split(' ')[1]);
                    break;
                }
            }
        }

        if (File.Exists(memoryCurrentPath))
        {
            var memoryStr = await File.ReadAllTextAsync(memoryCurrentPath, cancellationToken);
            memoryBytes = long.Parse(memoryStr.Trim());
        }

        return new CgroupResourceUsage
        {
            CpuUsageMicroseconds = cpuUsageUsec,
            MemoryUsageBytes = memoryBytes
        };
    }

    /// <summary>
    /// Delete a cgroup (must be empty first).
    /// </summary>
    public void DeleteCgroup(string cgroupPath)
    {
        if (Directory.Exists(cgroupPath))
        {
            Directory.Delete(cgroupPath);
            _logger.LogInformation("Deleted cgroup: {CgroupPath}", cgroupPath);
        }
    }

    private async Task ApplyLimitsAsync(
        string cgroupPath,
        ResourceLimits limits,
        CancellationToken cancellationToken)
    {
        // CPU limit: cpu.max format is "QUOTA PERIOD"
        // e.g., "50000 100000" = 50% of one CPU
        if (limits.MaxCpuPercent.HasValue)
        {
            var period = 100000; // 100ms period (standard)
            var quota = limits.MaxCpuPercent.Value * 1000; // percentage * 1000
            var cpuMaxPath = Path.Combine(cgroupPath, "cpu.max");
            await File.WriteAllTextAsync(cpuMaxPath, $"{quota} {period}", cancellationToken);

            _logger.LogDebug("Set CPU limit: {Percent}% at {Path}",
                limits.MaxCpuPercent.Value, cpuMaxPath);
        }

        // Memory hard limit: memory.max
        if (limits.MaxMemoryBytes.HasValue)
        {
            var memoryMaxPath = Path.Combine(cgroupPath, "memory.max");
            await File.WriteAllTextAsync(
                memoryMaxPath,
                limits.MaxMemoryBytes.Value.ToString(),
                cancellationToken);

            // Also set memory.high to 90% for gradual throttling before OOM
            var memoryHighPath = Path.Combine(cgroupPath, "memory.high");
            var highValue = (long)(limits.MaxMemoryBytes.Value * 0.9);
            await File.WriteAllTextAsync(memoryHighPath, highValue.ToString(), cancellationToken);

            _logger.LogDebug("Set memory limit: {Bytes} bytes at {Path}",
                limits.MaxMemoryBytes.Value, memoryMaxPath);
        }

        // I/O bandwidth limit: io.max (if disk device specified)
        if (limits.MaxDiskIoBytesPerSecond.HasValue)
        {
            // Note: Requires knowing the device major:minor number
            // Typically set at the slice level or determined dynamically
            _logger.LogDebug("I/O limits require device-specific configuration");
        }
    }

    private void EnsureSliceExists()
    {
        var slicePath = Path.Combine(CgroupBasePath, AgentSlice);
        if (!Directory.Exists(slicePath))
        {
            Directory.CreateDirectory(slicePath);

            // Enable CPU and memory controllers for child cgroups
            var subtreeControlPath = Path.Combine(slicePath, "cgroup.subtree_control");
            File.WriteAllText(subtreeControlPath, "+cpu +memory +io");

            _logger.LogInformation("Created agent slice: {SlicePath}", slicePath);
        }
    }

    public void Dispose()
    {
        // Cleanup is handled by systemd when the agent stops
    }
}

public sealed record CgroupResourceUsage
{
    public required long CpuUsageMicroseconds { get; init; }
    public required long MemoryUsageBytes { get; init; }
}
```

#### 3.2 Implement LinuxProcessManager

```csharp
namespace Dhadgar.Agent.Linux.Linux;

public sealed class LinuxProcessManager : IProcessManager, IDisposable
{
    private readonly CgroupManager _cgroupManager;
    private readonly ILogger<LinuxProcessManager> _logger;
    private readonly ConcurrentDictionary<Guid, ManagedProcess> _processes = new();

    public LinuxProcessManager(
        CgroupManager cgroupManager,
        ILogger<LinuxProcessManager> logger)
    {
        _cgroupManager = cgroupManager;
        _logger = logger;
    }

    public async Task<ProcessHandle> StartAsync(
        ProcessStartConfig config,
        CancellationToken cancellationToken = default)
    {
        // 1. Create cgroup with resource limits
        var cgroupPath = await _cgroupManager.CreateCgroupAsync(
            config.ServerId,
            config.ResourceLimits,
            cancellationToken);

        // 2. Start process
        var psi = new ProcessStartInfo
        {
            FileName = config.ExecutablePath,
            WorkingDirectory = config.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };

        foreach (var arg in config.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        foreach (var (key, value) in config.EnvironmentVariables)
        {
            psi.Environment[key] = value;
        }

        var process = new Process { StartInfo = psi };
        process.EnableRaisingEvents = true;

        process.Start();

        // 3. Move process to cgroup (immediately after start)
        await _cgroupManager.AddProcessAsync(cgroupPath, process.Id, cancellationToken);

        // 4. Set up async output reading
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var processId = Guid.NewGuid();
        var managedProcess = new ManagedProcess
        {
            ProcessId = processId,
            ServerId = config.ServerId,
            Process = process,
            CgroupPath = cgroupPath,
            StartedAt = DateTimeOffset.UtcNow
        };

        _processes[processId] = managedProcess;

        _logger.LogInformation(
            "Started process {ProcessId} (PID: {Pid}) in cgroup {CgroupPath}",
            processId, process.Id, cgroupPath);

        return new ProcessHandle
        {
            ProcessId = processId,
            Pid = process.Id,
            StartedAt = managedProcess.StartedAt
        };
    }

    public async Task StopAsync(
        Guid processId,
        TimeSpan gracePeriod,
        CancellationToken cancellationToken = default)
    {
        if (!_processes.TryGetValue(processId, out var managed))
        {
            throw new InvalidOperationException($"Process {processId} not found");
        }

        var process = managed.Process;

        // 1. Send SIGTERM for graceful shutdown
        try
        {
            process.Kill(Posix.SIGTERM);
        }
        catch (InvalidOperationException)
        {
            // Process already exited
            CleanupProcess(processId, managed);
            return;
        }

        // 2. Wait for graceful exit
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(gracePeriod);

        try
        {
            await process.WaitForExitAsync(cts.Token);
            _logger.LogInformation("Process {ProcessId} stopped gracefully", processId);
        }
        catch (OperationCanceledException)
        {
            // 3. Force kill with SIGKILL
            _logger.LogWarning("Process {ProcessId} did not stop gracefully, sending SIGKILL", processId);
            process.Kill(true); // Kill process tree
        }

        CleanupProcess(processId, managed);
    }

    public async Task<ProcessResourceUsage> GetResourceUsageAsync(
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        if (!_processes.TryGetValue(processId, out var managed))
        {
            throw new InvalidOperationException($"Process {processId} not found");
        }

        var cgroupUsage = await _cgroupManager.GetUsageAsync(
            managed.CgroupPath,
            cancellationToken);

        return new ProcessResourceUsage
        {
            CpuUsageMicroseconds = cgroupUsage.CpuUsageMicroseconds,
            MemoryUsageBytes = cgroupUsage.MemoryUsageBytes
        };
    }

    private void CleanupProcess(Guid processId, ManagedProcess managed)
    {
        _processes.TryRemove(processId, out _);

        try
        {
            _cgroupManager.DeleteCgroup(managed.CgroupPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete cgroup {CgroupPath}", managed.CgroupPath);
        }

        managed.Process.Dispose();
    }

    private sealed class ManagedProcess
    {
        public required Guid ProcessId { get; init; }
        public required Guid ServerId { get; init; }
        public required Process Process { get; init; }
        public required string CgroupPath { get; init; }
        public required DateTimeOffset StartedAt { get; init; }
    }

    public void Dispose()
    {
        foreach (var (_, managed) in _processes)
        {
            managed.Process.Dispose();
        }
        _processes.Clear();
    }
}

// POSIX signal constants
internal static class Posix
{
    public const int SIGTERM = 15;
    public const int SIGKILL = 9;
}

// Extension method for sending specific signals
internal static class ProcessExtensions
{
    public static void Kill(this Process process, int signal)
    {
        // Use kill command to send specific signal
        using var kill = Process.Start(new ProcessStartInfo
        {
            FileName = "kill",
            ArgumentList = { $"-{signal}", process.Id.ToString() },
            UseShellExecute = false,
            CreateNoWindow = true
        });
        kill?.WaitForExit();
    }
}
```

### Deliverables
- [ ] `Linux/CgroupManager.cs`
- [ ] `Linux/LinuxProcessManager.cs`
- [ ] cgroups v2 detection and validation
- [ ] Integration tests on Linux VM
- [ ] Resource limit verification tests

### Estimated Effort
~6-8 hours

---

## Phase 4: journald Logging

**Goal**: Integrate with systemd journald for structured logging.

### Tasks

#### 4.1 Configure journald Logging

The `AddSystemd()` extension automatically routes logs to journald when running as a service. Additional configuration:

```csharp
// In Program.cs
builder.Logging.AddSystemdConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
});
```

#### 4.2 Journal Query Script

Create `scripts/view-logs.sh`:
```bash
#!/bin/bash
# View agent logs from journald

# Recent logs
journalctl -u dhadgar-agent -n 100 --no-pager

# Follow logs
# journalctl -u dhadgar-agent -f

# Logs since last boot
# journalctl -u dhadgar-agent -b

# Logs with JSON output
# journalctl -u dhadgar-agent -o json
```

### Deliverables
- [ ] journald integration via systemd hosting
- [ ] Log viewing scripts
- [ ] Log rotation configuration

### Estimated Effort
~1-2 hours

---

## Phase 5: Linux Namespaces (Optional)

**Goal**: Add optional namespace isolation for enhanced security.

### Tasks

#### 5.1 Implement Namespace Manager

```csharp
namespace Dhadgar.Agent.Linux.Linux;

/// <summary>
/// Optional namespace isolation for game servers.
/// Provides additional isolation beyond cgroups.
/// </summary>
public sealed class NamespaceManager
{
    private readonly ILogger<NamespaceManager> _logger;

    /// <summary>
    /// Start a process in isolated namespaces using unshare.
    /// </summary>
    public ProcessStartInfo WrapWithNamespaces(
        ProcessStartInfo original,
        NamespaceOptions options)
    {
        var args = new List<string>();

        // Mount namespace (isolate filesystem view)
        if (options.IsolateMount)
        {
            args.Add("--mount");
        }

        // PID namespace (isolate process IDs)
        if (options.IsolatePid)
        {
            args.Add("--pid");
            args.Add("--fork");
        }

        // Network namespace (isolate network stack)
        // Note: Typically NOT used for game servers as they need network access
        if (options.IsolateNetwork)
        {
            args.Add("--net");
        }

        // User namespace (run as different user)
        if (options.MapUser is not null)
        {
            args.Add($"--map-user={options.MapUser}");
        }

        args.Add("--");
        args.Add(original.FileName);
        foreach (var arg in original.ArgumentList)
        {
            args.Add(arg);
        }

        return new ProcessStartInfo
        {
            FileName = "unshare",
            WorkingDirectory = original.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = original.RedirectStandardOutput,
            RedirectStandardError = original.RedirectStandardError,
            RedirectStandardInput = original.RedirectStandardInput,
            ArgumentList = { string.Join(" ", args) }
        };
    }
}

public sealed record NamespaceOptions
{
    public bool IsolateMount { get; init; }
    public bool IsolatePid { get; init; }
    public bool IsolateNetwork { get; init; }
    public string? MapUser { get; init; }
}
```

### Deliverables
- [ ] `Linux/NamespaceManager.cs`
- [ ] Configuration options for namespace features
- [ ] Documentation on when to use namespaces

### Estimated Effort
~3-4 hours

---

## Phase 6: Package Distribution

**Goal**: Create distribution packages for common Linux distributions.

### Tasks

#### 6.1 Create Installation Script

**`scripts/install.sh`**:
```bash
#!/bin/bash
set -euo pipefail

# Meridian Console Agent - Linux Installation Script

INSTALL_DIR="/opt/dhadgar/agent"
CONFIG_DIR="/etc/dhadgar"
DATA_DIR="/var/lib/dhadgar"
LOG_DIR="/var/log/dhadgar"

# Parse arguments
ENROLLMENT_TOKEN=""
CONTROL_PLANE="https://api.meridianconsole.com"

while [[ $# -gt 0 ]]; do
    case $1 in
        --token)
            ENROLLMENT_TOKEN="$2"
            shift 2
            ;;
        --control-plane)
            CONTROL_PLANE="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

echo "Installing Meridian Console Agent..."

# 1. Create user
if ! id "dhadgar-agent" &>/dev/null; then
    useradd --system --shell /usr/sbin/nologin --home-dir "$DATA_DIR" dhadgar-agent
    echo "Created dhadgar-agent user"
fi

# 2. Create directories
mkdir -p "$INSTALL_DIR" "$CONFIG_DIR" "$DATA_DIR"/{servers,backups} "$LOG_DIR" "$CONFIG_DIR/certs"
chown -R dhadgar-agent:dhadgar-agent "$DATA_DIR" "$LOG_DIR"
chown root:dhadgar-agent "$CONFIG_DIR"
chmod 750 "$CONFIG_DIR" "$CONFIG_DIR/certs"

# 3. Download and extract agent
echo "Downloading agent..."
curl -fsSL "https://releases.meridianconsole.com/agent/linux/latest/dhadgar-agent-linux-x64.tar.gz" | \
    tar -xzf - -C "$INSTALL_DIR"
chmod +x "$INSTALL_DIR/Dhadgar.Agent.Linux"

# 4. Create configuration
cat > "$CONFIG_DIR/appsettings.json" << EOF
{
  "Agent": {
    "NodeId": null,
    "ControlPlane": {
      "Endpoint": "$CONTROL_PLANE",
      "EnrollmentToken": "$ENROLLMENT_TOKEN"
    },
    "Process": {
      "ServerBasePath": "$DATA_DIR/servers"
    },
    "Files": {
      "TempDirectory": "$DATA_DIR/temp"
    }
  }
}
EOF
chown root:dhadgar-agent "$CONFIG_DIR/appsettings.json"
chmod 640 "$CONFIG_DIR/appsettings.json"

# 5. Install systemd service
cp "$INSTALL_DIR/dhadgar-agent.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable dhadgar-agent

# 6. Start service
systemctl start dhadgar-agent

echo "Installation complete!"
echo "Check status: systemctl status dhadgar-agent"
echo "View logs: journalctl -u dhadgar-agent -f"
```

#### 6.2 Create .deb Package (Debian/Ubuntu)

Create `packaging/debian/control`:
```
Package: dhadgar-agent
Version: 1.0.0
Section: admin
Priority: optional
Architecture: amd64
Depends: libc6 (>= 2.31)
Maintainer: Sandbox Servers <support@sandboxservers.com>
Description: Meridian Console Agent
 Customer-hosted agent for game server management.
```

#### 6.3 Create .rpm Package (RHEL/Fedora)

Create `packaging/rpm/dhadgar-agent.spec`:
```rpm
Name: dhadgar-agent
Version: 1.0.0
Release: 1
Summary: Meridian Console Agent
License: Proprietary
URL: https://meridianconsole.com

%description
Customer-hosted agent for game server management.

%install
mkdir -p %{buildroot}/opt/dhadgar/agent
cp -r * %{buildroot}/opt/dhadgar/agent/

%files
/opt/dhadgar/agent/*

%post
systemctl daemon-reload
systemctl enable dhadgar-agent

%preun
systemctl stop dhadgar-agent
systemctl disable dhadgar-agent
```

### Deliverables
- [ ] `scripts/install.sh` installation script
- [ ] Debian package (.deb)
- [ ] RPM package (.rpm)
- [ ] Tar.gz archive for manual install
- [ ] Uninstallation script

### Estimated Effort
~4-5 hours

---

## Dependencies

### Required Agent.Core Interfaces

The following interfaces from Agent.Core must be complete:

- [ ] `ICertificateStore` - Certificate management abstraction
- [ ] `IProcessManager` - Process lifecycle abstraction
- [ ] `AgentOptions` - Configuration classes
- [ ] `IControlPlaneClient` - SignalR communication

### Package Dependencies

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Hosting.Systemd" Version="10.0.1" />
</ItemGroup>
```

### System Requirements

- **Kernel**: Linux 4.15+ (cgroups v2 support)
- **systemd**: 245+ (cgroup delegation support)
- **Distribution**: Ubuntu 20.04+, Debian 11+, RHEL 8+, Fedora 34+

---

## Success Criteria

### Phase 1 Complete When
- [ ] Agent runs as systemd service
- [ ] Service notifies systemd of ready state
- [ ] Service restarts on failure
- [ ] Watchdog works correctly

### Phase 2 Complete When
- [ ] Certificates stored with correct permissions
- [ ] Private key has 600 permissions
- [ ] Certificate retrieval works
- [ ] Secure key deletion implemented

### Phase 3 Complete When
- [ ] Processes run in cgroups
- [ ] Memory limits enforced
- [ ] CPU limits enforced
- [ ] Resource usage metrics accurate

### Phase 4 Complete When
- [ ] Logs appear in journald
- [ ] Structured fields preserved
- [ ] Log levels correct

### Phase 5 Complete When
- [ ] Namespace isolation works (optional feature)
- [ ] Configuration toggles work

### Phase 6 Complete When
- [ ] Installation script works on Ubuntu, Debian, RHEL
- [ ] Packages install cleanly
- [ ] Upgrade preserves config
- [ ] Uninstall removes everything

### Overall Complete When
- [ ] All unit tests pass
- [ ] Integration tests pass on Linux VM
- [ ] Security review passes (agent-service-guardian)
- [ ] Tested on Ubuntu 22.04, Debian 12, RHEL 9

---

## Estimated Total Effort

| Phase | Effort | Dependencies |
|-------|--------|--------------|
| Phase 1: systemd Service | ~2-3 hours | Agent.Core complete |
| Phase 2: Certificate Store | ~3-4 hours | Phase 1 |
| Phase 3: cgroups v2 | ~6-8 hours | Phase 1 |
| Phase 4: journald | ~1-2 hours | Phase 1 |
| Phase 5: Namespaces | ~3-4 hours | Phase 3 (optional) |
| Phase 6: Packaging | ~4-5 hours | All phases |
| **Total** | **~19-26 hours** | |
