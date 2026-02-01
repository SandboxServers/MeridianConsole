# Agent Windows Implementation Plan

> **Status**: Blocked on Agent.Core
> **Last Updated**: 2026-02-01
> **Current State**: Scaffolding only - depends on Agent.Core completion

## Executive Summary

The `Dhadgar.Agent.Windows` project provides the Windows-specific implementation of the customer-hosted agent. It extends `Agent.Core` with Windows-specific functionality:

- Windows Service integration (SCM)
- Windows Certificate Store for mTLS certificates
- Job Objects for process isolation
- Event Log integration
- NTFS ACLs for file security

**Prerequisites**: Agent.Core must be complete before starting this implementation.

---

## Documentation Validation

All Windows-specific approaches validated against official Microsoft documentation:

| Component | Validated Source | Key Details |
|-----------|-----------------|-------------|
| Windows Service | [MS Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service) | `Microsoft.Extensions.Hosting.WindowsServices` v10.0.1 |
| Job Objects | [MS Learn](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects) | `CreateJobObject`, `SetInformationJobObject`, CPU/memory limits |
| X509Store | [MS Learn](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509store) | `StoreLocation.LocalMachine`, `StoreName.My` |
| Environment.ProcessorCount | [MS Learn](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/environment-processorcount-on-windows) | Respects Job Object CPU limits since .NET 6 |

### Windows-Specific Certificate Storage

Windows uses the built-in Certificate Store:
- **Location**: `LocalMachine\My` (requires admin for write)
- **API**: `X509Store` class
- **Private Key**: Stored securely by Windows CryptoAPI

```csharp
using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
store.Open(OpenFlags.ReadWrite);
store.Add(certificate);
```

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Phase 1: Windows Service Hosting](#phase-1-windows-service-hosting)
3. [Phase 2: Certificate Store Implementation](#phase-2-certificate-store-implementation)
4. [Phase 3: Job Object Process Isolation](#phase-3-job-object-process-isolation)
5. [Phase 4: Event Log Integration](#phase-4-event-log-integration)
6. [Phase 5: Windows Firewall](#phase-5-windows-firewall)
7. [Phase 6: Installer](#phase-6-installer)
8. [Dependencies](#dependencies)
9. [Success Criteria](#success-criteria)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                      Dhadgar.Agent.Windows                          │
├─────────────────────────────────────────────────────────────────────┤
│  Program.cs                                                          │
│  ├── UseWindowsService()                                            │
│  ├── AddWindowsCertificateStore()                                   │
│  └── AddWindowsProcessManager()                                     │
├─────────────────────────────────────────────────────────────────────┤
│  Windows/                                                            │
│  ├── WindowsCertificateStore.cs    # ICertificateStore impl         │
│  ├── WindowsProcessManager.cs      # IProcessManager impl           │
│  ├── JobObjectManager.cs           # Job Object P/Invoke            │
│  ├── EventLogSink.cs               # Serilog sink for Event Log     │
│  └── FirewallManager.cs            # Windows Firewall API           │
├─────────────────────────────────────────────────────────────────────┤
│  Installation/                                                       │
│  ├── ServiceInstaller.cs           # sc.exe wrapper                 │
│  └── RegistryConfig.cs             # Registry-based config          │
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

## Phase 1: Windows Service Hosting

**Goal**: Run the agent as a Windows Service with proper lifecycle management.

### Tasks

#### 1.1 Update Program.cs

```csharp
using Dhadgar.Agent.Core.Hosting;
using Dhadgar.Agent.Windows.Windows;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = Host.CreateApplicationBuilder(args);

// Configure as Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "DhadgarAgent";
});

// Add Agent.Core defaults
builder.ConfigureAgentDefaults();

// Add Windows-specific implementations
builder.Services.AddSingleton<ICertificateStore, WindowsCertificateStore>();
builder.Services.AddSingleton<IProcessManager, WindowsProcessManager>();

// Add Windows Event Log logging
builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "Meridian Console Agent";
    settings.LogName = "Application";
});

var host = builder.Build();
await host.RunAsync();
```

#### 1.2 Add Package Reference

```xml
<PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="10.0.1" />
```

#### 1.3 Configure Service Recovery

Create `Installation/ServiceInstaller.cs`:
```csharp
public static class ServiceInstaller
{
    public static void ConfigureRecovery()
    {
        // Configure service to restart on failure
        // First failure: restart after 5 seconds
        // Second failure: restart after 10 seconds
        // Subsequent failures: restart after 30 seconds
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = "failure DhadgarAgent reset= 86400 actions= restart/5000/restart/10000/restart/30000",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        process?.WaitForExit();
    }
}
```

### Deliverables
- [ ] Updated `Program.cs` with Windows Service support
- [ ] Service recovery configuration
- [ ] Service installation scripts
- [ ] Service start/stop tests

### Estimated Effort
~2-3 hours

---

## Phase 2: Certificate Store Implementation

**Goal**: Implement `ICertificateStore` using Windows Certificate Store.

### Tasks

#### 2.1 Implement WindowsCertificateStore

```csharp
namespace Dhadgar.Agent.Windows.Windows;

public sealed class WindowsCertificateStore : ICertificateStore
{
    private const string CertificateFriendlyName = "Meridian Console Agent";
    private readonly ILogger<WindowsCertificateStore> _logger;

    public WindowsCertificateStore(ILogger<WindowsCertificateStore> logger)
    {
        _logger = logger;
    }

    public Task<X509Certificate2> GetClientCertificateAsync(CancellationToken cancellationToken = default)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);

        var certificates = store.Certificates
            .Find(X509FindType.FindBySubjectName, "dhadgar-agent", validOnly: true)
            .OfType<X509Certificate2>()
            .Where(c => c.HasPrivateKey)
            .OrderByDescending(c => c.NotAfter)
            .ToList();

        if (certificates.Count == 0)
        {
            throw new InvalidOperationException("No valid agent certificate found in LocalMachine\\My store");
        }

        _logger.LogDebug("Found certificate: {Subject}, expires: {NotAfter}",
            certificates[0].Subject, certificates[0].NotAfter);

        return Task.FromResult(certificates[0]);
    }

    public Task StoreCertificateAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);

        // Set friendly name for easier identification
        certificate.FriendlyName = CertificateFriendlyName;

        store.Add(certificate);

        _logger.LogInformation("Certificate stored in LocalMachine\\My: {Thumbprint}",
            certificate.Thumbprint);

        return Task.CompletedTask;
    }

    public Task<bool> HasValidCertificateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);

            var certificates = store.Certificates
                .Find(X509FindType.FindBySubjectName, "dhadgar-agent", validOnly: true);

            return Task.FromResult(certificates.Count > 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for valid certificate");
            return Task.FromResult(false);
        }
    }

    public Task<X509Certificate2> GetCaCertificateAsync(CancellationToken cancellationToken = default)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);

        var certificates = store.Certificates
            .Find(X509FindType.FindBySubjectName, "Meridian Console CA", validOnly: true);

        if (certificates.Count == 0)
        {
            throw new InvalidOperationException("CA certificate not found in LocalMachine\\Root store");
        }

        return Task.FromResult(certificates[0]);
    }

    public Task ClearCertificatesAsync(CancellationToken cancellationToken = default)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);

        var toRemove = store.Certificates
            .Find(X509FindType.FindBySubjectName, "dhadgar-agent", validOnly: false);

        foreach (var cert in toRemove)
        {
            store.Remove(cert);
            _logger.LogInformation("Removed certificate: {Thumbprint}", cert.Thumbprint);
        }

        return Task.CompletedTask;
    }
}
```

### Deliverables
- [ ] `Windows/WindowsCertificateStore.cs`
- [ ] Unit tests with mock certificate store
- [ ] Integration tests requiring admin privileges

### Estimated Effort
~3-4 hours

---

## Phase 3: Job Object Process Isolation

**Goal**: Implement `IProcessManager` using Windows Job Objects for process isolation.

### Tasks

#### 3.1 Create Job Object P/Invoke Wrapper

```csharp
namespace Dhadgar.Agent.Windows.Windows;

internal static partial class NativeMethods
{
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetInformationJobObject(
        IntPtr hJob,
        JobObjectInfoType infoType,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateJobObject(IntPtr hJob, uint uExitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr hObject);
}

internal enum JobObjectInfoType
{
    BasicLimitInformation = 2,
    ExtendedLimitInformation = 9,
    CpuRateControlInformation = 15
}

[StructLayout(LayoutKind.Sequential)]
internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
{
    public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
    public IO_COUNTERS IoInfo;
    public UIntPtr ProcessMemoryLimit;
    public UIntPtr JobMemoryLimit;
    public UIntPtr PeakProcessMemoryUsed;
    public UIntPtr PeakJobMemoryUsed;
}

[StructLayout(LayoutKind.Sequential)]
internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
{
    public long PerProcessUserTimeLimit;
    public long PerJobUserTimeLimit;
    public uint LimitFlags;
    public UIntPtr MinimumWorkingSetSize;
    public UIntPtr MaximumWorkingSetSize;
    public uint ActiveProcessLimit;
    public IntPtr Affinity;
    public uint PriorityClass;
    public uint SchedulingClass;
}

[StructLayout(LayoutKind.Sequential)]
internal struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
{
    public uint ControlFlags;
    public uint CpuRate; // Percentage * 100 (e.g., 5000 = 50%)
}
```

#### 3.2 Implement WindowsProcessManager

```csharp
namespace Dhadgar.Agent.Windows.Windows;

public sealed class WindowsProcessManager : IProcessManager, IDisposable
{
    private readonly ILogger<WindowsProcessManager> _logger;
    private readonly ConcurrentDictionary<Guid, ManagedProcess> _processes = new();

    public async Task<ProcessHandle> StartAsync(
        ProcessStartConfig config,
        CancellationToken cancellationToken = default)
    {
        // 1. Create Job Object with resource limits
        var jobHandle = CreateJobObjectWithLimits(config.ResourceLimits);

        // 2. Start process
        var psi = new ProcessStartInfo
        {
            FileName = config.ExecutablePath,
            WorkingDirectory = config.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
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

        // 3. Assign to Job Object (MUST be done before process does any work)
        if (!NativeMethods.AssignProcessToJobObject(jobHandle, process.Handle))
        {
            var error = Marshal.GetLastWin32Error();
            process.Kill();
            throw new Win32Exception(error, "Failed to assign process to job object");
        }

        // 4. Set up async output reading
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var processId = Guid.NewGuid();
        var managedProcess = new ManagedProcess
        {
            ProcessId = processId,
            ServerId = config.ServerId,
            Process = process,
            JobHandle = jobHandle,
            StartedAt = DateTimeOffset.UtcNow
        };

        _processes[processId] = managedProcess;

        _logger.LogInformation("Started process {ProcessId} (PID: {Pid}) in job object",
            processId, process.Id);

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

        // 1. Try graceful shutdown (close main window)
        if (process.CloseMainWindow())
        {
            if (await WaitForExitAsync(process, gracePeriod, cancellationToken))
            {
                _logger.LogInformation("Process {ProcessId} stopped gracefully", processId);
                CleanupProcess(processId, managed);
                return;
            }
        }

        // 2. Force kill via Job Object (kills all child processes too)
        _logger.LogWarning("Process {ProcessId} did not stop gracefully, terminating job object", processId);
        NativeMethods.TerminateJobObject(managed.JobHandle, 1);

        CleanupProcess(processId, managed);
    }

    private IntPtr CreateJobObjectWithLimits(ResourceLimits? limits)
    {
        var jobHandle = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
        if (jobHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create job object");
        }

        if (limits is null) return jobHandle;

        // Set memory limit
        if (limits.MaxMemoryBytes.HasValue)
        {
            var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = 0x0100 // JOB_OBJECT_LIMIT_PROCESS_MEMORY
                },
                ProcessMemoryLimit = (UIntPtr)limits.MaxMemoryBytes.Value
            };

            SetJobObjectInfo(jobHandle, JobObjectInfoType.ExtendedLimitInformation, extendedInfo);
        }

        // Set CPU rate limit
        if (limits.MaxCpuPercent.HasValue)
        {
            var cpuInfo = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
            {
                ControlFlags = 0x1 | 0x4, // Enable + HardCap
                CpuRate = (uint)(limits.MaxCpuPercent.Value * 100) // Percentage * 100
            };

            SetJobObjectInfo(jobHandle, JobObjectInfoType.CpuRateControlInformation, cpuInfo);
        }

        // Configure to kill all processes when job closes
        var limitInfo = new JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            LimitFlags = 0x2000 // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        };

        var extInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = limitInfo
        };

        SetJobObjectInfo(jobHandle, JobObjectInfoType.ExtendedLimitInformation, extInfo);

        return jobHandle;
    }

    private static void SetJobObjectInfo<T>(IntPtr jobHandle, JobObjectInfoType infoType, T info) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!NativeMethods.SetInformationJobObject(jobHandle, infoType, ptr, (uint)size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private sealed class ManagedProcess
    {
        public required Guid ProcessId { get; init; }
        public required Guid ServerId { get; init; }
        public required Process Process { get; init; }
        public required IntPtr JobHandle { get; init; }
        public required DateTimeOffset StartedAt { get; init; }
    }
}
```

### Deliverables
- [ ] `Windows/NativeMethods.cs` (P/Invoke declarations)
- [ ] `Windows/JobObjectManager.cs`
- [ ] `Windows/WindowsProcessManager.cs`
- [ ] Integration tests with resource limit verification
- [ ] Security tests for privilege isolation

### Estimated Effort
~6-8 hours

---

## Phase 4: Event Log Integration

**Goal**: Integrate with Windows Event Log for operational logging.

### Tasks

#### 4.1 Create Event Log Source

```csharp
namespace Dhadgar.Agent.Windows.Windows;

public static class EventLogSetup
{
    public const string SourceName = "Meridian Console Agent";
    public const string LogName = "Application";

    public static void EnsureEventSource()
    {
        if (!EventLog.SourceExists(SourceName))
        {
            EventLog.CreateEventSource(SourceName, LogName);
        }
    }
}
```

#### 4.2 Custom Event IDs

```csharp
public static class AgentEventIds
{
    public const int ServiceStarted = 1000;
    public const int ServiceStopped = 1001;
    public const int Connected = 2000;
    public const int Disconnected = 2001;
    public const int ReconnectFailed = 2002;
    public const int EnrollmentSucceeded = 3000;
    public const int EnrollmentFailed = 3001;
    public const int ProcessStarted = 4000;
    public const int ProcessStopped = 4001;
    public const int ProcessCrashed = 4002;
    public const int CommandReceived = 5000;
    public const int CommandSucceeded = 5001;
    public const int CommandFailed = 5002;
    public const int SecurityViolation = 9000;
}
```

### Deliverables
- [ ] `Windows/EventLogSetup.cs`
- [ ] `Windows/AgentEventIds.cs`
- [ ] Event Log source registration in installer
- [ ] Structured logging to Event Log

### Estimated Effort
~2-3 hours

---

## Phase 5: Windows Firewall

**Goal**: Manage Windows Firewall rules for game servers.

### Tasks

#### 5.1 Implement Firewall Manager

```csharp
namespace Dhadgar.Agent.Windows.Windows;

public sealed class FirewallManager
{
    private readonly ILogger<FirewallManager> _logger;

    public void AllowPort(int port, string ruleName, string protocol = "TCP")
    {
        var args = $"advfirewall firewall add rule name=\"{ruleName}\" " +
                   $"dir=in action=allow protocol={protocol} localport={port}";

        ExecuteNetsh(args);
        _logger.LogInformation("Added firewall rule: {RuleName} for port {Port}/{Protocol}",
            ruleName, port, protocol);
    }

    public void RemoveRule(string ruleName)
    {
        var args = $"advfirewall firewall delete rule name=\"{ruleName}\"";
        ExecuteNetsh(args);
        _logger.LogInformation("Removed firewall rule: {RuleName}", ruleName);
    }

    private static void ExecuteNetsh(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        process?.WaitForExit();

        if (process?.ExitCode != 0)
        {
            throw new InvalidOperationException($"netsh failed with exit code {process?.ExitCode}");
        }
    }
}
```

### Deliverables
- [ ] `Windows/FirewallManager.cs`
- [ ] Automatic rule creation for game server ports
- [ ] Rule cleanup on process stop

### Estimated Effort
~2-3 hours

---

## Phase 6: Installer

**Goal**: Create MSI installer for production deployment.

### Tasks

#### 6.1 WiX Installer Configuration

Create `installer/Agent.Windows.wxs`:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="Meridian Console Agent"
           Manufacturer="Sandbox Servers"
           Version="1.0.0"
           UpgradeCode="YOUR-GUID-HERE">

    <MajorUpgrade DowngradeErrorMessage="A newer version is already installed." />

    <Feature Id="ProductFeature" Title="Meridian Console Agent">
      <ComponentGroupRef Id="ProductComponents" />
      <ComponentGroupRef Id="ServiceComponents" />
    </Feature>

    <CustomAction Id="InstallService"
                  Directory="INSTALLFOLDER"
                  ExeCommand="[INSTALLFOLDER]Dhadgar.Agent.Windows.exe install"
                  Execute="deferred"
                  Impersonate="no" />

    <CustomAction Id="StartService"
                  Directory="INSTALLFOLDER"
                  ExeCommand="net start DhadgarAgent"
                  Execute="deferred"
                  Impersonate="no" />

    <InstallExecuteSequence>
      <Custom Action="InstallService" After="InstallFiles">NOT Installed</Custom>
      <Custom Action="StartService" After="InstallService">NOT Installed</Custom>
    </InstallExecuteSequence>
  </Package>
</Wix>
```

### Deliverables
- [ ] WiX installer project
- [ ] Silent install support
- [ ] Enrollment token parameter
- [ ] Upgrade support
- [ ] Uninstall cleanup

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
  <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="10.0.1" />
  <PackageReference Include="Microsoft.Extensions.Logging.EventLog" Version="10.0.0" />
</ItemGroup>
```

---

## Success Criteria

### Phase 1 Complete When
- [ ] Agent runs as Windows Service
- [ ] Service restarts on failure
- [ ] Service responds to SCM commands

### Phase 2 Complete When
- [ ] Certificates stored in LocalMachine\My
- [ ] Certificate retrieval works
- [ ] CA certificate validation works

### Phase 3 Complete When
- [ ] Processes run in Job Objects
- [ ] Memory limits enforced
- [ ] CPU limits enforced
- [ ] Child processes killed with parent

### Phase 4 Complete When
- [ ] Events appear in Event Viewer
- [ ] Event IDs match documentation
- [ ] Error events have useful details

### Phase 5 Complete When
- [ ] Firewall rules created for servers
- [ ] Rules cleaned up on server stop

### Phase 6 Complete When
- [ ] MSI installs cleanly
- [ ] Silent install works
- [ ] Upgrade preserves config
- [ ] Uninstall removes everything

### Overall Complete When
- [ ] All unit tests pass
- [ ] Integration tests pass on Windows Server
- [ ] Security review passes (agent-service-guardian)
- [ ] Tested on Windows 10, Windows 11, Windows Server 2022

---

## Estimated Total Effort

| Phase | Effort | Dependencies |
|-------|--------|--------------|
| Phase 1: Windows Service | ~2-3 hours | Agent.Core complete |
| Phase 2: Certificate Store | ~3-4 hours | Phase 1 |
| Phase 3: Job Objects | ~6-8 hours | Phase 1 |
| Phase 4: Event Log | ~2-3 hours | Phase 1 |
| Phase 5: Firewall | ~2-3 hours | Phase 3 |
| Phase 6: Installer | ~4-5 hours | All phases |
| **Total** | **~19-26 hours** | |
