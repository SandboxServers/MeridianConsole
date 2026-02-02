# Windows Agent Implementation Plan (Comprehensive)

> **Status**: Ready to Implement
> **Last Updated**: 2026-02-01
> **Prerequisite**: Agent.Core PR #93 merged ✅
> **Reference**: Issue #81, docs/implementation-plans/agent-windows.md

## Executive Summary

This plan implements `Dhadgar.Agent.Windows` with all security patterns established in Agent.Core PR #93. The Windows Agent provides:

- Windows Service hosting (SCM integration)
- Windows Certificate Store for mTLS (`LocalMachine\My`)
- Job Objects for process isolation and resource limits
- Windows Event Log integration
- Windows Firewall management

**Security-First Approach**: All patterns from Agent.Core are pre-incorporated:
- `_disposed = true` FIRST in disposal
- ObjectDisposedException race condition prevention
- LinkedCancellationTokenSource for disposal propagation
- Result<T> railway-oriented error handling
- IValidatableObject for configuration validation
- HTTPS enforcement at configuration time
- CommandType sanitization for metrics
- Path validation before all file operations

---

## Pre-Implementation Security Checklist (from PR #93)

Before implementing any component, ensure these patterns are followed:

### Disposal Pattern
```csharp
public void Dispose()
{
    if (_disposed) return;

    // CRITICAL: Set disposed flag FIRST to close race window
    _disposed = true;

    // Cancel in-flight operations before disposing resources
    try { _disposeCts.Cancel(); }
    catch (ObjectDisposedException) { /* Already disposed, ignore */ }

    _disposeCts.Dispose();
    // Dispose other resources...
}
```

### ObjectDisposedException Handling
```csharp
// When acquiring semaphores or similar resources:
try
{
    await _semaphore.WaitAsync(cancellationToken);
}
catch (ObjectDisposedException)
{
    return Result<T>.Failure("[Component.Disposed] Service is shut down");
}

// When releasing semaphores:
try { _semaphore.Release(); }
catch (ObjectDisposedException) { /* Disposed during shutdown, safe to ignore */ }
```

### Disposed State Check Pattern
```csharp
public async Task<Result<T>> SomeOperationAsync(...)
{
    // Check disposed state BEFORE acquiring resources
    if (_disposed)
    {
        return Result<T>.Failure("[Component.Disposed] Service is shut down");
    }

    // Then acquire semaphore, etc.
}
```

### LinkedCancellationTokenSource for Disposal
```csharp
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
    cancellationToken, _disposeCts.Token);

// Operations will be cancelled on either user cancellation OR disposal
await SomeAsyncOperation(linkedCts.Token);
```

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                      Dhadgar.Agent.Windows                          │
├─────────────────────────────────────────────────────────────────────┤
│  Program.cs                                                          │
│  ├── UseWindowsService()                                            │
│  ├── AddSingleton<ICertificateStore, WindowsCertificateStore>()     │
│  └── AddSingleton<IProcessManager, WindowsProcessManager>()         │
├─────────────────────────────────────────────────────────────────────┤
│  Windows/                                                            │
│  ├── WindowsCertificateStore.cs    # ICertificateStore impl         │
│  ├── WindowsProcessManager.cs      # IProcessManager impl           │
│  ├── NativeMethods.cs              # Job Object P/Invoke            │
│  ├── JobObjectHandle.cs            # SafeHandle for Job Objects     │
│  ├── WindowsEventLogSink.cs        # Event Log logging              │
│  └── FirewallManager.cs            # Windows Firewall API           │
├─────────────────────────────────────────────────────────────────────┤
│  Installation/                                                       │
│  ├── ServiceInstaller.cs           # sc.exe wrapper (no injection)  │
│  └── installer/                    # WiX MSI project                │
└─────────────────────────────────────────────────────────────────────┘
        │
        │ References
        ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        Dhadgar.Agent.Core                           │
│  ICertificateStore, IProcessManager, IPathValidator, Result<T>     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Phase 1: Windows Service Hosting

**Goal**: Run the agent as a Windows Service with proper lifecycle management.
**Estimated Effort**: 3-4 hours

### 1.1 Update Dhadgar.Agent.Windows.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
    <!-- Publish as single file for easier deployment -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" />
    <PackageReference Include="Microsoft.Extensions.Logging.EventLog" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Dhadgar.Agent.Core\Dhadgar.Agent.Core.csproj" />
  </ItemGroup>
</Project>
```

### 1.2 Program.cs with Full Configuration

```csharp
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Hosting;
using Dhadgar.Agent.Core.Process;
using Dhadgar.Agent.Windows.Windows;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Windows;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Configure as Windows Service - MUST be called early
            builder.Services.AddWindowsService(options =>
            {
                options.ServiceName = "DhadgarAgent";
            });

            // Configure Agent options with validation
            builder.Services
                .AddOptions<AgentOptions>()
                .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // Add Agent.Core services
            builder.Services.AddAgentCore();

            // Add Windows-specific implementations (required by Agent.Core)
            builder.Services.AddSingleton<ICertificateStore, WindowsCertificateStore>();
            builder.Services.AddSingleton<IProcessManager, WindowsProcessManager>();

            // Add Windows Event Log logging
            builder.Logging.AddEventLog(settings =>
            {
                settings.SourceName = "Meridian Console Agent";
                settings.LogName = "Application";
            });

            // Validate configuration at startup
            var host = builder.Build();

            // Perform startup validation
            using (var scope = host.Services.CreateScope())
            {
                var options = scope.ServiceProvider.GetRequiredService<IOptions<AgentOptions>>().Value;

                // SECURITY: Validate HTTPS enforcement at startup
                if (!Uri.TryCreate(options.ControlPlane.Endpoint, UriKind.Absolute, out var uri) ||
                    !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("FATAL: Control plane endpoint must use HTTPS");
                    return 1;
                }
            }

            await host.RunAsync();
            return 0;
        }
        catch (OptionsValidationException ex)
        {
            Console.Error.WriteLine($"Configuration validation failed: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }
}
```

### 1.3 Service Installation Helper

```csharp
namespace Dhadgar.Agent.Windows.Installation;

/// <summary>
/// Configures Windows Service recovery options.
/// </summary>
/// <remarks>
/// SECURITY: Service name is hardcoded to prevent command injection.
/// If parameterization is needed in the future, add strict validation:
/// - Allow only alphanumeric, hyphens, underscores
/// - Reject shell metacharacters (quotes, semicolons, pipes, backticks)
/// - Maximum length of 256 characters
/// </remarks>
public static class ServiceInstaller
{
    private const string ServiceName = "DhadgarAgent";

    public static void ConfigureRecovery()
    {
        // First failure: restart after 5 seconds
        // Second failure: restart after 10 seconds
        // Subsequent: restart after 30 seconds
        // Reset failure count after 24 hours
        var process = System.Diagnostics.Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"failure {ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        });

        process?.WaitForExit();

        if (process?.ExitCode != 0)
        {
            var error = process?.StandardError.ReadToEnd();
            throw new InvalidOperationException($"Failed to configure service recovery: {error}");
        }
    }
}
```

### Deliverables
- [ ] Updated `Dhadgar.Agent.Windows.csproj`
- [ ] `Program.cs` with Windows Service hosting
- [ ] `Installation/ServiceInstaller.cs`
- [ ] Unit tests for startup validation
- [ ] Integration test with Windows Service on/off

---

## Phase 2: Certificate Store Implementation

**Goal**: Implement `ICertificateStore` using Windows Certificate Store.
**Estimated Effort**: 4-5 hours

### 2.1 WindowsCertificateStore with Disposal Pattern

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Windows Certificate Store implementation of ICertificateStore.
/// Uses LocalMachine\My for client certificates and LocalMachine\Root for CA.
/// </summary>
/// <remarks>
/// SECURITY: This class handles cryptographic material. Key security measures:
/// - Certificates are stored in Windows protected storage
/// - Private keys are marked non-exportable where possible
/// - Store access requires appropriate permissions (admin for write)
/// - All operations are logged for audit purposes
/// </remarks>
public sealed class WindowsCertificateStore : ICertificateStore, IDisposable
{
    private bool _disposed;
    private readonly ILogger<WindowsCertificateStore> _logger;
    private readonly object _storeLock = new();

    private const string AgentCertificateSubjectName = "CN=dhadgar-agent";
    private const string CaCertificateSubjectName = "CN=Meridian Console CA";
    private const string CertificateFriendlyName = "Meridian Console Agent";

    public WindowsCertificateStore(ILogger<WindowsCertificateStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public X509Certificate2? GetClientCertificate()
    {
        // SECURITY: Check disposed state first
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);

            var certificates = store.Certificates
                .Find(X509FindType.FindBySubjectDistinguishedName, AgentCertificateSubjectName, validOnly: true)
                .OfType<X509Certificate2>()
                .Where(c => c.HasPrivateKey)
                .Where(c => c.NotAfter > DateTime.UtcNow)
                .OrderByDescending(c => c.NotAfter)
                .ToList();

            if (certificates.Count == 0)
            {
                _logger.LogDebug("No valid agent certificate found in LocalMachine\\My store");
                return null;
            }

            var cert = certificates[0];
            _logger.LogDebug(
                "Found client certificate: {Thumbprint}, expires: {NotAfter}",
                cert.Thumbprint[..8] + "...", // SECURITY: Only log partial thumbprint
                cert.NotAfter);

            return cert;
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to access certificate store");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task StoreCertificateAsync(
        X509Certificate2 certificate,
        byte[] privateKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(privateKey);

        // SECURITY: Validate private key size to prevent memory exhaustion
        // RSA 4096-bit key = ~3KB, ECDSA P-384 = ~200 bytes
        // Allow up to 16KB to accommodate various formats with headers
        const int maxPrivateKeyBytes = 16 * 1024;
        if (privateKey.Length > maxPrivateKeyBytes)
        {
            throw new ArgumentException(
                $"Private key size ({privateKey.Length} bytes) exceeds maximum allowed ({maxPrivateKeyBytes} bytes)",
                nameof(privateKey));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        // Store operation is CPU-bound, use Task.Run for async
        await Task.Run(() =>
        {
            lock (_storeLock)
            {
                try
                {
                    // Import private key into certificate
                    X509Certificate2 certWithKey;
                    try
                    {
                        // Try PEM format first
                        var pemKey = System.Text.Encoding.UTF8.GetString(privateKey);
                        using var rsa = RSA.Create();
                        rsa.ImportFromPem(pemKey);
                        certWithKey = certificate.CopyWithPrivateKey(rsa);
                    }
                    catch
                    {
                        // Fall back to DER/PKCS#8 format
                        using var rsa = RSA.Create();
                        rsa.ImportPkcs8PrivateKey(privateKey, out _);
                        certWithKey = certificate.CopyWithPrivateKey(rsa);
                    }

                    // Set friendly name for easier identification
                    certWithKey.FriendlyName = CertificateFriendlyName;

                    using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                    store.Open(OpenFlags.ReadWrite);
                    store.Add(certWithKey);

                    _logger.LogInformation(
                        "Certificate stored in LocalMachine\\My: {Thumbprint}",
                        certWithKey.Thumbprint[..8] + "...");
                }
                catch (CryptographicException ex)
                {
                    _logger.LogError(ex, "Failed to store certificate - check permissions");
                    throw;
                }
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveCertificateAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await Task.Run(() =>
        {
            lock (_storeLock)
            {
                using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);

                var toRemove = store.Certificates
                    .Find(X509FindType.FindBySubjectDistinguishedName, AgentCertificateSubjectName, validOnly: false);

                foreach (var cert in toRemove)
                {
                    store.Remove(cert);
                    _logger.LogInformation(
                        "Removed certificate: {Thumbprint}",
                        cert.Thumbprint[..8] + "...");
                    cert.Dispose();
                }
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public X509Certificate2? GetCaCertificate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);

            var certificates = store.Certificates
                .Find(X509FindType.FindBySubjectDistinguishedName, CaCertificateSubjectName, validOnly: true)
                .OfType<X509Certificate2>()
                .Where(c => c.NotAfter > DateTime.UtcNow)
                .OrderByDescending(c => c.NotAfter)
                .ToList();

            if (certificates.Count == 0)
            {
                _logger.LogDebug("CA certificate not found in LocalMachine\\Root store");
                return null;
            }

            return certificates[0];
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to access CA certificate store");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task StoreCaCertificateAsync(
        X509Certificate2 certificate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // SECURITY: Validate certificate size to prevent memory exhaustion
        const int maxCertificateBytes = 16 * 1024;
        var certData = certificate.RawData;
        if (certData.Length > maxCertificateBytes)
        {
            throw new ArgumentException(
                $"Certificate size ({certData.Length} bytes) exceeds maximum allowed ({maxCertificateBytes} bytes)",
                nameof(certificate));
        }

        await Task.Run(() =>
        {
            lock (_storeLock)
            {
                try
                {
                    using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                    store.Open(OpenFlags.ReadWrite);
                    store.Add(certificate);

                    _logger.LogInformation(
                        "CA certificate stored in LocalMachine\\Root: {Thumbprint}",
                        certificate.Thumbprint[..8] + "...");
                }
                catch (CryptographicException ex)
                {
                    _logger.LogError(ex, "Failed to store CA certificate - check permissions");
                    throw;
                }
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public bool NeedsRenewal(int thresholdDays)
    {
        if (thresholdDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdDays), "Threshold must be non-negative");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        var cert = GetClientCertificate();
        if (cert is null)
        {
            return true; // No certificate = needs enrollment (which includes getting a cert)
        }

        var daysUntilExpiry = (cert.NotAfter - DateTime.UtcNow).TotalDays;
        var needsRenewal = daysUntilExpiry <= thresholdDays;

        if (needsRenewal)
        {
            _logger.LogInformation(
                "Certificate renewal needed: {DaysRemaining} days until expiry, threshold is {Threshold} days",
                (int)daysUntilExpiry, thresholdDays);
        }

        return needsRenewal;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        // SECURITY: Set disposed flag FIRST
        _disposed = true;

        // No unmanaged resources to dispose - X509Store is opened/closed per operation
        _logger.LogDebug("WindowsCertificateStore disposed");
    }
}
```

### Deliverables
- [ ] `Windows/WindowsCertificateStore.cs`
- [ ] Unit tests with mock certificate operations
- [ ] Integration tests (require admin privileges)
- [ ] Certificate expiry handling tests

---

## Phase 3: Job Object Process Manager

**Goal**: Implement `IProcessManager` using Windows Job Objects for resource isolation.
**Estimated Effort**: 8-10 hours

### 3.1 SafeHandle for Job Objects

```csharp
using Microsoft.Win32.SafeHandles;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Safe handle wrapper for Windows Job Objects.
/// Ensures proper cleanup even on exceptions.
/// </summary>
internal sealed class JobObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public JobObjectHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle()
    {
        return NativeMethods.CloseHandle(handle);
    }
}
```

### 3.2 P/Invoke Declarations with LibraryImport

```csharp
using System.Runtime.InteropServices;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Native Windows API declarations for Job Objects.
/// </summary>
/// <remarks>
/// Using LibraryImport (source-generated) for better AOT support and performance.
/// All structures use explicit layout for correct memory alignment.
/// </remarks>
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
    public static partial bool QueryInformationJobObject(
        IntPtr hJob,
        JobObjectInfoType infoType,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength,
        out uint lpReturnLength);

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
    BasicUIRestrictions = 4,
    ExtendedLimitInformation = 9,
    CpuRateControlInformation = 15
}

[Flags]
internal enum JobObjectLimit : uint
{
    ProcessMemory = 0x0100,
    JobMemory = 0x0200,
    KillOnJobClose = 0x2000
}

[Flags]
internal enum CpuRateControlFlags : uint
{
    Enable = 0x1,
    HardCap = 0x4,
    Notify = 0x8
}

[StructLayout(LayoutKind.Sequential)]
internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
{
    public long PerProcessUserTimeLimit;
    public long PerJobUserTimeLimit;
    public JobObjectLimit LimitFlags;
    public UIntPtr MinimumWorkingSetSize;
    public UIntPtr MaximumWorkingSetSize;
    public uint ActiveProcessLimit;
    public IntPtr Affinity;
    public uint PriorityClass;
    public uint SchedulingClass;
}

[StructLayout(LayoutKind.Sequential)]
internal struct IO_COUNTERS
{
    public ulong ReadOperationCount;
    public ulong WriteOperationCount;
    public ulong OtherOperationCount;
    public ulong ReadTransferCount;
    public ulong WriteTransferCount;
    public ulong OtherTransferCount;
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
internal struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
{
    public CpuRateControlFlags ControlFlags;
    public uint CpuRate; // Percentage * 100 (e.g., 5000 = 50%)
}
```

### 3.3 WindowsProcessManager with Full Security Patterns

```csharp
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Dhadgar.Agent.Core.Process;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Windows implementation of IProcessManager using Job Objects.
/// </summary>
/// <remarks>
/// SECURITY: Job Objects provide mandatory resource limits that cannot be bypassed:
/// - Memory limits are enforced by the kernel
/// - CPU limits are hard caps (not throttling)
/// - All child processes inherit job membership
/// - KillOnJobClose ensures no orphans
///
/// Disposal pattern follows Agent.Core security requirements:
/// - _disposed = true set FIRST
/// - ObjectDisposedException handled gracefully
/// - LinkedCancellationTokenSource for propagation
/// </remarks>
public sealed class WindowsProcessManager : IProcessManager, IDisposable
{
    private bool _disposed;
    private readonly ILogger<WindowsProcessManager> _logger;
    private readonly ConcurrentDictionary<Guid, ManagedProcessState> _processes = new();
    private readonly CancellationTokenSource _disposeCts = new();

    /// <summary>
    /// Maximum output line length before truncation.
    /// Prevents memory exhaustion from processes that output extremely long lines.
    /// </summary>
    private const int MaxOutputLineLength = 64 * 1024; // 64KB

    public WindowsProcessManager(ILogger<WindowsProcessManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

    /// <inheritdoc />
    public event EventHandler<ProcessOutputEventArgs>? OutputReceived;

    /// <inheritdoc />
    public async Task<Result<ManagedProcess>> StartProcessAsync(
        ProcessConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        // SECURITY: Check disposed state BEFORE any resource acquisition
        if (_disposed)
        {
            return Result<ManagedProcess>.Failure("[Process.Disposed] Process manager is shut down");
        }

        // Validate configuration
        var validationResults = config.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(config)).ToList();
        if (validationResults.Count > 0)
        {
            return Result<ManagedProcess>.Failure(
                $"[Process.InvalidConfig] {string.Join("; ", validationResults.Select(v => v.ErrorMessage))}");
        }

        // Create linked token for disposal propagation
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);

        try
        {
            // Create Job Object with resource limits
            var jobHandle = CreateJobObjectWithLimits(config.Limits);

            var psi = new ProcessStartInfo
            {
                FileName = config.ExecutablePath,
                WorkingDirectory = config.WorkingDirectory ?? Path.GetDirectoryName(config.ExecutablePath),
                UseShellExecute = false,
                RedirectStandardOutput = config.CaptureStdout,
                RedirectStandardError = config.CaptureStderr,
                CreateNoWindow = true
            };

            // SECURITY: Use ArgumentList instead of Arguments to prevent command injection
            if (!string.IsNullOrEmpty(config.Arguments))
            {
                // Split arguments carefully - this is a simplified approach
                // Production code should use a proper argument parser
                foreach (var arg in config.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    psi.ArgumentList.Add(arg);
                }
            }

            // Set environment variables
            foreach (var (key, value) in config.EnvironmentVariables)
            {
                psi.Environment[key] = value;
            }

            var process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            var processId = Guid.NewGuid();

            // Wire up exit handler before starting
            process.Exited += (sender, args) => OnProcessExited(processId, process);

            // Start the process
            if (!process.Start())
            {
                jobHandle.Dispose();
                return Result<ManagedProcess>.Failure("[Process.StartFailed] Failed to start process");
            }

            // CRITICAL: Assign to Job Object IMMEDIATELY after start
            // Before the process can do any work or spawn children
            if (!NativeMethods.AssignProcessToJobObject(jobHandle.DangerousGetHandle(), process.Handle))
            {
                var error = Marshal.GetLastWin32Error();
                try { process.Kill(); } catch { }
                jobHandle.Dispose();
                return Result<ManagedProcess>.Failure(
                    $"[Process.JobAssignFailed] Failed to assign process to job object: Win32 error {error}");
            }

            // Set up output capture with size limits
            if (config.CaptureStdout)
            {
                process.OutputDataReceived += (sender, e) =>
                    OnOutputReceived(processId, e.Data, isError: false);
                process.BeginOutputReadLine();
            }

            if (config.CaptureStderr)
            {
                process.ErrorDataReceived += (sender, e) =>
                    OnOutputReceived(processId, e.Data, isError: true);
                process.BeginErrorReadLine();
            }

            var state = new ManagedProcessState
            {
                ProcessId = processId,
                ServerId = config.ServerId,
                Process = process,
                JobHandle = jobHandle,
                Config = config,
                StartedAt = DateTimeOffset.UtcNow
            };

            _processes[processId] = state;

            _logger.LogInformation(
                "Started process {ProcessId} (PID: {Pid}) for server {ServerId} in job object",
                processId, process.Id, config.ServerId);

            return Result<ManagedProcess>.Success(CreateManagedProcess(state));
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Failed to start process: Win32 error {ErrorCode}", ex.NativeErrorCode);
            return Result<ManagedProcess>.Failure($"[Process.Win32Error] {ex.Message}");
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            return Result<ManagedProcess>.Failure("[Process.Disposed] Process manager is shut down");
        }
        catch (OperationCanceledException)
        {
            return Result<ManagedProcess>.Failure("[Process.Cancelled] Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error starting process");
            return Result<ManagedProcess>.Failure("[Process.UnexpectedError] Failed to start process");
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> StopProcessAsync(
        Guid processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Result<bool>.Failure("[Process.Disposed] Process manager is shut down");
        }

        if (!_processes.TryGetValue(processId, out var state))
        {
            return Result<bool>.Failure("[Process.NotFound] Process not found");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);

        try
        {
            var process = state.Process;

            // Try graceful shutdown first
            if (process.CloseMainWindow())
            {
                var exited = await WaitForExitAsync(process, timeout, linkedCts.Token);
                if (exited)
                {
                    _logger.LogInformation("Process {ProcessId} stopped gracefully", processId);
                    CleanupProcess(processId);
                    return Result<bool>.Success(true);
                }
            }

            // Graceful failed - force kill via Job Object
            _logger.LogWarning(
                "Process {ProcessId} did not stop gracefully within {Timeout}, terminating job object",
                processId, timeout);

            if (!NativeMethods.TerminateJobObject(state.JobHandle.DangerousGetHandle(), 1))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("Failed to terminate job object: Win32 error {Error}", error);
            }

            CleanupProcess(processId);
            return Result<bool>.Success(true);
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            return Result<bool>.Failure("[Process.Disposed] Process manager is shut down");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping process {ProcessId}", processId);
            return Result<bool>.Failure("[Process.StopError] Failed to stop process");
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> KillProcessAsync(Guid processId, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Result<bool>.Failure("[Process.Disposed] Process manager is shut down");
        }

        if (!_processes.TryGetValue(processId, out var state))
        {
            return Result<bool>.Failure("[Process.NotFound] Process not found");
        }

        try
        {
            // Terminate entire job (kills process and all children)
            if (!NativeMethods.TerminateJobObject(state.JobHandle.DangerousGetHandle(), 1))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogWarning("TerminateJobObject returned error {Error}, attempting direct kill", error);

                // Fallback to direct process kill
                try
                {
                    state.Process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited
                }
            }

            CleanupProcess(processId);
            _logger.LogInformation("Process {ProcessId} killed", processId);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error killing process {ProcessId}", processId);
            return Result<bool>.Failure("[Process.KillError] Failed to kill process");
        }
    }

    /// <inheritdoc />
    public ManagedProcess? GetProcess(Guid processId)
    {
        if (_processes.TryGetValue(processId, out var state))
        {
            return CreateManagedProcess(state);
        }
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<ManagedProcess> GetAllProcesses()
    {
        return _processes.Values
            .Select(CreateManagedProcess)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateResourceLimitsAsync(
        Guid processId,
        ResourceLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);

        if (_disposed)
        {
            return Result<bool>.Failure("[Process.Disposed] Process manager is shut down");
        }

        if (!_processes.TryGetValue(processId, out var state))
        {
            return Result<bool>.Failure("[Process.NotFound] Process not found");
        }

        try
        {
            // Update memory limit
            if (limits.MaxMemoryBytes.HasValue)
            {
                var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                    {
                        LimitFlags = JobObjectLimit.ProcessMemory | JobObjectLimit.KillOnJobClose
                    },
                    ProcessMemoryLimit = (UIntPtr)limits.MaxMemoryBytes.Value
                };

                SetJobObjectInfo(state.JobHandle.DangerousGetHandle(),
                    JobObjectInfoType.ExtendedLimitInformation, extendedInfo);
            }

            // Update CPU limit
            if (limits.MaxCpuPercent.HasValue)
            {
                var cpuInfo = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
                {
                    ControlFlags = CpuRateControlFlags.Enable | CpuRateControlFlags.HardCap,
                    CpuRate = (uint)(limits.MaxCpuPercent.Value * 100)
                };

                SetJobObjectInfo(state.JobHandle.DangerousGetHandle(),
                    JobObjectInfoType.CpuRateControlInformation, cpuInfo);
            }

            _logger.LogInformation(
                "Updated resource limits for process {ProcessId}: Memory={Memory}MB, CPU={Cpu}%",
                processId,
                limits.MaxMemoryBytes.HasValue ? limits.MaxMemoryBytes.Value / (1024 * 1024) : null,
                limits.MaxCpuPercent);

            return Result<bool>.Success(true);
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Failed to update resource limits for process {ProcessId}", processId);
            return Result<bool>.Failure($"[Process.LimitUpdateFailed] {ex.Message}");
        }
    }

    private JobObjectHandle CreateJobObjectWithLimits(ResourceLimits? limits)
    {
        var handle = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create job object");
        }

        var jobHandle = new JobObjectHandle();
        jobHandle.SetHandle(handle);

        // Always set KillOnJobClose to ensure no orphans
        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JobObjectLimit.KillOnJobClose
            }
        };

        // Add memory limit if specified
        if (limits?.MaxMemoryBytes.HasValue == true)
        {
            extendedInfo.BasicLimitInformation.LimitFlags |= JobObjectLimit.ProcessMemory;
            extendedInfo.ProcessMemoryLimit = (UIntPtr)limits.MaxMemoryBytes.Value;
        }

        SetJobObjectInfo(handle, JobObjectInfoType.ExtendedLimitInformation, extendedInfo);

        // Add CPU limit if specified
        if (limits?.MaxCpuPercent.HasValue == true)
        {
            var cpuInfo = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
            {
                ControlFlags = CpuRateControlFlags.Enable | CpuRateControlFlags.HardCap,
                CpuRate = (uint)(limits.MaxCpuPercent.Value * 100)
            };

            SetJobObjectInfo(handle, JobObjectInfoType.CpuRateControlInformation, cpuInfo);
        }

        return jobHandle;
    }

    private static void SetJobObjectInfo<T>(IntPtr jobHandle, JobObjectInfoType infoType, T info)
        where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!NativeMethods.SetInformationJobObject(jobHandle, infoType, ptr, (uint)size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to set job object information");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private void OnProcessExited(Guid processId, Process process)
    {
        try
        {
            var exitCode = process.ExitCode;
            var exitTime = process.ExitTime;

            _logger.LogInformation(
                "Process {ProcessId} exited with code {ExitCode} at {ExitTime}",
                processId, exitCode, exitTime);

            ProcessExited?.Invoke(this, new ProcessExitedEventArgs
            {
                ProcessId = processId,
                ExitCode = exitCode,
                ExitTime = exitTime,
                WasKilled = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in process exit handler for {ProcessId}", processId);
        }
    }

    private void OnOutputReceived(Guid processId, string? data, bool isError)
    {
        if (string.IsNullOrEmpty(data)) return;

        // SECURITY: Truncate extremely long lines to prevent memory exhaustion
        var truncatedData = data.Length > MaxOutputLineLength
            ? data[..MaxOutputLineLength] + "... [truncated]"
            : data;

        OutputReceived?.Invoke(this, new ProcessOutputEventArgs
        {
            ProcessId = processId,
            Data = truncatedData,
            IsError = isError,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    private void CleanupProcess(Guid processId)
    {
        if (_processes.TryRemove(processId, out var state))
        {
            state.JobHandle.Dispose();
            state.Process.Dispose();
        }
    }

    private static ManagedProcess CreateManagedProcess(ManagedProcessState state)
    {
        bool hasExited;
        int? exitCode = null;

        try
        {
            hasExited = state.Process.HasExited;
            if (hasExited)
            {
                exitCode = state.Process.ExitCode;
            }
        }
        catch (InvalidOperationException)
        {
            hasExited = true;
        }

        return new ManagedProcess
        {
            ProcessId = state.ProcessId,
            ServerId = state.ServerId,
            Pid = state.Process.Id,
            HasExited = hasExited,
            ExitCode = exitCode,
            StartedAt = state.StartedAt
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        // SECURITY: Set disposed flag FIRST to prevent new operations
        _disposed = true;

        // Cancel any pending operations
        try { _disposeCts.Cancel(); }
        catch (ObjectDisposedException) { /* Already disposed */ }

        // Terminate all job objects (kills all managed processes)
        foreach (var (processId, state) in _processes)
        {
            try
            {
                NativeMethods.TerminateJobObject(state.JobHandle.DangerousGetHandle(), 1);
                state.JobHandle.Dispose();
                state.Process.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing process {ProcessId}", processId);
            }
        }

        _processes.Clear();
        _disposeCts.Dispose();

        _logger.LogDebug("WindowsProcessManager disposed, all processes terminated");
    }

    private sealed class ManagedProcessState
    {
        public required Guid ProcessId { get; init; }
        public required Guid ServerId { get; init; }
        public required Process Process { get; init; }
        public required JobObjectHandle JobHandle { get; init; }
        public required ProcessConfig Config { get; init; }
        public required DateTimeOffset StartedAt { get; init; }
    }
}
```

### Deliverables
- [ ] `Windows/NativeMethods.cs`
- [ ] `Windows/JobObjectHandle.cs`
- [ ] `Windows/WindowsProcessManager.cs`
- [ ] Unit tests with mocked P/Invoke
- [ ] Integration tests with actual process creation
- [ ] Resource limit verification tests
- [ ] Cleanup verification (no orphan processes)

---

## Phase 4: Event Log Integration

**Goal**: Integrate with Windows Event Log for operational monitoring.
**Estimated Effort**: 2-3 hours

### 4.1 Event IDs and Logging

```csharp
namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Windows Event Log event IDs for the agent.
/// Organized by category (thousands digit):
/// - 1xxx: Service lifecycle
/// - 2xxx: Connection events
/// - 3xxx: Enrollment events
/// - 4xxx: Process events
/// - 5xxx: Command events
/// - 9xxx: Security events
/// </summary>
public static class AgentEventIds
{
    // Service lifecycle
    public const int ServiceStarted = 1000;
    public const int ServiceStopped = 1001;
    public const int ServiceFailed = 1002;

    // Connection events
    public const int Connected = 2000;
    public const int Disconnected = 2001;
    public const int ReconnectAttempt = 2002;
    public const int ReconnectFailed = 2003;

    // Enrollment events
    public const int EnrollmentStarted = 3000;
    public const int EnrollmentSucceeded = 3001;
    public const int EnrollmentFailed = 3002;
    public const int CertificateRenewed = 3003;

    // Process events
    public const int ProcessStarted = 4000;
    public const int ProcessStopped = 4001;
    public const int ProcessCrashed = 4002;
    public const int ProcessResourceExceeded = 4003;

    // Command events
    public const int CommandReceived = 5000;
    public const int CommandSucceeded = 5001;
    public const int CommandFailed = 5002;
    public const int CommandRejected = 5003;

    // Security events
    public const int SecurityViolation = 9000;
    public const int UnauthorizedAccess = 9001;
    public const int PathTraversalAttempt = 9002;
    public const int InvalidCertificate = 9003;
}

/// <summary>
/// Event Log source configuration.
/// </summary>
public static class EventLogSetup
{
    public const string SourceName = "Meridian Console Agent";
    public const string LogName = "Application";

    /// <summary>
    /// Ensures the event source exists. Must be called during installation (requires admin).
    /// </summary>
    public static void EnsureEventSource()
    {
        if (!System.Diagnostics.EventLog.SourceExists(SourceName))
        {
            System.Diagnostics.EventLog.CreateEventSource(SourceName, LogName);
        }
    }

    /// <summary>
    /// Removes the event source. Called during uninstallation.
    /// </summary>
    public static void RemoveEventSource()
    {
        if (System.Diagnostics.EventLog.SourceExists(SourceName))
        {
            System.Diagnostics.EventLog.DeleteEventSource(SourceName);
        }
    }
}
```

### Deliverables
- [ ] `Windows/AgentEventIds.cs`
- [ ] `Windows/EventLogSetup.cs`
- [ ] Event source registration in installer
- [ ] Verify events appear in Event Viewer

---

## Phase 5: Windows Firewall Management

**Goal**: Manage firewall rules for game server ports.
**Estimated Effort**: 3-4 hours

### 5.1 FirewallManager with Input Validation

```csharp
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Windows.Windows;

/// <summary>
/// Manages Windows Firewall rules for game server ports.
/// </summary>
/// <remarks>
/// SECURITY: All input is validated to prevent command injection:
/// - Port numbers are validated as integers in valid range
/// - Rule names are validated against a strict whitelist pattern
/// - Protocol is validated against allowed values only
/// </remarks>
public sealed partial class FirewallManager
{
    private readonly ILogger<FirewallManager> _logger;

    /// <summary>
    /// Regex for validating firewall rule names.
    /// Only allows alphanumeric, spaces, hyphens, underscores.
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z0-9\s\-_]+$", RegexOptions.Compiled)]
    private static partial Regex ValidRuleNamePattern();

    private const int MaxRuleNameLength = 256;

    public FirewallManager(ILogger<FirewallManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Adds a firewall rule to allow inbound traffic on the specified port.
    /// </summary>
    public void AllowInboundPort(int port, string ruleName, string protocol = "TCP")
    {
        ValidatePort(port);
        ValidateRuleName(ruleName);
        ValidateProtocol(protocol);

        // SECURITY: All parameters are validated before being used in command
        var args = $"advfirewall firewall add rule name=\"{ruleName}\" " +
                   $"dir=in action=allow protocol={protocol} localport={port}";

        ExecuteNetsh(args);

        _logger.LogInformation(
            "Added firewall rule: {RuleName} for inbound {Protocol}/{Port}",
            ruleName, protocol, port);
    }

    /// <summary>
    /// Removes a firewall rule by name.
    /// </summary>
    public void RemoveRule(string ruleName)
    {
        ValidateRuleName(ruleName);

        var args = $"advfirewall firewall delete rule name=\"{ruleName}\"";
        ExecuteNetsh(args);

        _logger.LogInformation("Removed firewall rule: {RuleName}", ruleName);
    }

    /// <summary>
    /// Checks if a firewall rule exists.
    /// </summary>
    public bool RuleExists(string ruleName)
    {
        ValidateRuleName(ruleName);

        var args = $"advfirewall firewall show rule name=\"{ruleName}\"";

        try
        {
            ExecuteNetsh(args);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void ValidatePort(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port,
                "Port must be between 1 and 65535.");
        }
    }

    private static void ValidateRuleName(string ruleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);

        if (ruleName.Length > MaxRuleNameLength)
        {
            throw new ArgumentException(
                $"Rule name must not exceed {MaxRuleNameLength} characters.",
                nameof(ruleName));
        }

        if (!ValidRuleNamePattern().IsMatch(ruleName))
        {
            throw new ArgumentException(
                "Rule name contains invalid characters. " +
                "Only alphanumeric characters, spaces, hyphens, and underscores are allowed.",
                nameof(ruleName));
        }
    }

    private static void ValidateProtocol(string protocol)
    {
        if (!string.Equals(protocol, "TCP", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(protocol, "UDP", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Protocol must be either 'TCP' or 'UDP'.",
                nameof(protocol));
        }
    }

    private void ExecuteNetsh(string arguments)
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

        if (process is null)
        {
            throw new InvalidOperationException("Failed to start netsh.exe");
        }

        process.WaitForExit(TimeSpan.FromSeconds(30));

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            _logger.LogWarning("netsh failed with exit code {ExitCode}: {Error}",
                process.ExitCode, error);
            throw new InvalidOperationException(
                $"netsh.exe failed with exit code {process.ExitCode}");
        }
    }
}
```

### Deliverables
- [ ] `Windows/FirewallManager.cs`
- [ ] Integration tests (require admin)
- [ ] Rule cleanup on process stop

---

## Phase 6: MSI Installer

**Goal**: Create professional MSI installer for deployment.
**Estimated Effort**: 4-5 hours

### 6.1 WiX Configuration (v5)

The installer should:
- Install as Windows Service
- Configure service recovery
- Create Event Log source
- Accept enrollment token as parameter
- Support silent install
- Support upgrade in place
- Clean uninstall (remove service, certs, rules)

### Deliverables
- [ ] `installer/` WiX project
- [ ] Silent install support (`/quiet ENROLLMENT_TOKEN=xxx`)
- [ ] Upgrade support
- [ ] Uninstall cleanup
- [ ] Tested on Windows 10, 11, Server 2022

---

## Testing Strategy

### Unit Tests (No Admin Required)
- Configuration validation
- Certificate parsing logic (with test certs)
- Input validation (FirewallManager, paths)
- Result<T> handling

### Integration Tests (Admin Required)
- Windows Service lifecycle
- Certificate Store operations
- Job Object creation and limits
- Firewall rule management
- Event Log writing

### End-to-End Tests
- Full enrollment flow against test control plane
- Process start/stop with resource limits
- Certificate renewal

---

## Security Review Checklist

Before completing each phase, verify:

- [ ] All inputs validated before use
- [ ] No command injection vectors
- [ ] Disposal pattern correct (_disposed = true FIRST)
- [ ] ObjectDisposedException handled
- [ ] LinkedCancellationTokenSource used for disposal propagation
- [ ] Result<T> pattern for error handling
- [ ] Partial information in logs (thumbprints, paths)
- [ ] Admin operations require admin permissions
- [ ] No credentials in configuration files
- [ ] HTTPS enforced for all network communication

Run `agent-service-guardian` agent before PR:
```
Use the agent-service-guardian to review all changes in src/Agents/Dhadgar.Agent.Windows
```

---

## Estimated Total Effort

| Phase | Effort | Dependencies |
|-------|--------|--------------|
| Phase 1: Windows Service | 3-4 hours | Agent.Core ✅ |
| Phase 2: Certificate Store | 4-5 hours | Phase 1 |
| Phase 3: Process Manager | 8-10 hours | Phase 1 |
| Phase 4: Event Log | 2-3 hours | Phase 1 |
| Phase 5: Firewall | 3-4 hours | Phase 3 |
| Phase 6: Installer | 4-5 hours | All phases |
| **Total** | **24-31 hours** | |

---

## Definition of Done

- [ ] All unit tests pass
- [ ] All integration tests pass on Windows 10/11/Server 2022
- [ ] Security review by agent-service-guardian passes
- [ ] No CodeRabbit critical/major issues unaddressed
- [ ] MSI installer tested (install, upgrade, uninstall)
- [ ] Documentation updated
- [ ] Event Log events documented
