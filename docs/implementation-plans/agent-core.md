# Agent Core Implementation Plan

> **Status**: Ready for implementation
> **Last Updated**: 2026-02-01
> **Current State**: Scaffolding only - project structure exists, functionality planned

## Executive Summary

The `Dhadgar.Agent.Core` library provides the shared foundation for customer-hosted agents on both Linux and Windows. This is **security-critical code** that runs on customer hardware with elevated privileges.

This plan covers implementing the core library that both platform-specific agents depend on. The implementation is organized into phases that can be delivered incrementally.

**Key deliverables:**
1. Configuration system with validation
2. Control plane client (SignalR with automatic reconnect)
3. Certificate management abstraction (platform implementations in Windows/Linux agents)
4. Heartbeat and health reporting
5. Command dispatch framework
6. Process management abstractions
7. File transfer with P2P support (ICE/STUN/TURN)
8. Telemetry and observability

---

## Documentation Validation

All technical approaches in this plan have been validated against official documentation:

| Component | Validated Source | Key Details |
|-----------|-----------------|-------------|
| Worker Services | [MS Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers) | `IHostedService`, `BackgroundService` |
| SignalR Client | [MS Learn](https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client) | `HubConnectionBuilder.WithAutomaticReconnect()` |
| Polly Resilience | [MS Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience) | `Microsoft.Extensions.Http.Resilience` for retry/circuit breaker |
| Process Management | [MS Learn](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process) | `Process`, `ProcessStartInfo`, async output reading |
| X509 Certificates | [MS Learn](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography) | Platform differences - Windows uses store, Linux uses files |
| OpenTelemetry | [MS Learn](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel) | `OpenTelemetry.Extensions.Hosting` |
| SIPSorcery WebRTC | [NuGet](https://www.nuget.org/packages/SIPSorcery) / [GitHub](https://github.com/sipsorcery-org/sipsorcery) | v10.0.3, .NET 10 support, pure C# ICE/STUN implementation |

### Critical Platform Difference: Certificate Storage

**This affects architecture decisions:**

| Platform | Certificate Storage | API |
|----------|-------------------|-----|
| Windows | Windows Certificate Store | `X509Store(StoreName.My, StoreLocation.LocalMachine)` |
| Linux | File-based | Direct file I/O to `/etc/dhadgar/certs/` |

The Agent.Core library must define an `ICertificateStore` abstraction that platform agents implement.

---

## Table of Contents

1. [Current Implementation Status](#current-implementation-status)
2. [Architecture Overview](#architecture-overview)
3. [Phase 1: Configuration and Hosting](#phase-1-configuration-and-hosting)
4. [Phase 2: Control Plane Client](#phase-2-control-plane-client)
5. [Phase 3: Certificate Management](#phase-3-certificate-management)
6. [Phase 4: Heartbeat and Health](#phase-4-heartbeat-and-health)
7. [Phase 5: Command Framework](#phase-5-command-framework)
8. [Phase 6: Process Management](#phase-6-process-management)
9. [Phase 7: File Transfer](#phase-7-file-transfer)
10. [Phase 8: Telemetry](#phase-8-telemetry)
11. [Dependencies and Prerequisites](#dependencies-and-prerequisites)
12. [Success Criteria](#success-criteria)

---

## Current Implementation Status

### What Exists

| File | Purpose |
|------|---------|
| `Dhadgar.Agent.Core.csproj` | Project with security analyzers enabled |
| `Hello.cs` | Smoke test class |
| `Program.cs` | Entry point placeholder |
| `README.md` | Comprehensive architecture documentation |

### What's Missing (This Plan)

| Component | Priority | Phase |
|-----------|----------|-------|
| Configuration system | Critical | 1 |
| SignalR client | Critical | 2 |
| Certificate abstraction | Critical | 3 |
| Heartbeat service | Critical | 4 |
| Command dispatcher | Critical | 5 |
| Process manager interface | High | 6 |
| File transfer | High | 7 |
| OpenTelemetry | Medium | 8 |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Agent.Core Library                           │
├─────────────────────────────────────────────────────────────────────┤
│  Configuration/                                                      │
│  ├── AgentOptions.cs           # Strongly-typed config              │
│  ├── ControlPlaneOptions.cs    # Connection settings                │
│  └── SecurityOptions.cs        # Security settings                  │
├─────────────────────────────────────────────────────────────────────┤
│  Communication/                                                      │
│  ├── IControlPlaneClient.cs    # SignalR abstraction                │
│  ├── ControlPlaneClient.cs     # Implementation                     │
│  └── ConnectionState.cs        # State machine                      │
├─────────────────────────────────────────────────────────────────────┤
│  Authentication/                                                     │
│  ├── ICertificateStore.cs      # Platform abstraction               │
│  ├── CertificateValidator.cs   # Validation logic                   │
│  └── EnrollmentService.cs      # Initial enrollment                 │
├─────────────────────────────────────────────────────────────────────┤
│  Health/                                                             │
│  ├── IHealthReporter.cs        # Health reporting interface         │
│  ├── HeartbeatService.cs       # Background heartbeat               │
│  └── SystemMetrics.cs          # System resource collection         │
├─────────────────────────────────────────────────────────────────────┤
│  Commands/                                                           │
│  ├── ICommandHandler.cs        # Command handler interface          │
│  ├── CommandDispatcher.cs      # Routes commands to handlers        │
│  ├── CommandValidator.cs       # Validates incoming commands        │
│  └── Handlers/                 # Built-in handlers                  │
├─────────────────────────────────────────────────────────────────────┤
│  Process/                                                            │
│  ├── IProcessManager.cs        # Platform abstraction               │
│  ├── ProcessInfo.cs            # Process state                      │
│  ├── ResourceLimits.cs         # CPU/memory limits                  │
│  └── ProcessMonitor.cs         # Health monitoring                  │
├─────────────────────────────────────────────────────────────────────┤
│  Files/                                                              │
│  ├── IFileTransferService.cs   # Transfer abstraction               │
│  ├── PathValidator.cs          # Security validation                │
│  ├── FileIntegrityChecker.cs   # Hash verification                  │
│  └── P2P/                      # Peer-to-peer transfer              │
│      ├── IceConnection.cs      # ICE/STUN/TURN                      │
│      └── DataChannelTransfer.cs                                     │
├─────────────────────────────────────────────────────────────────────┤
│  Telemetry/                                                          │
│  ├── AgentMeter.cs             # Custom metrics                     │
│  └── ActivitySources.cs        # Distributed tracing                │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Phase 1: Configuration and Hosting

**Goal**: Establish the configuration system and host infrastructure.

### Tasks

#### 1.1 Create Configuration Classes

**`Configuration/AgentOptions.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>
    /// Unique identifier assigned during enrollment. Null until enrolled.
    /// </summary>
    public Guid? NodeId { get; set; }

    /// <summary>
    /// Human-readable name for this node.
    /// </summary>
    public string? NodeName { get; set; }

    /// <summary>
    /// Control plane connection settings.
    /// </summary>
    public ControlPlaneOptions ControlPlane { get; set; } = new();

    /// <summary>
    /// Security settings.
    /// </summary>
    public SecurityOptions Security { get; set; } = new();

    /// <summary>
    /// Process management settings.
    /// </summary>
    public ProcessOptions Process { get; set; } = new();

    /// <summary>
    /// File handling settings.
    /// </summary>
    public FileOptions Files { get; set; } = new();
}

public sealed class ControlPlaneOptions
{
    /// <summary>
    /// Control plane endpoint URL.
    /// </summary>
    [Required, Url]
    public string Endpoint { get; set; } = "https://api.meridianconsole.com";

    /// <summary>
    /// Heartbeat interval in seconds.
    /// </summary>
    [Range(10, 300)]
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Initial reconnect delay in seconds.
    /// </summary>
    [Range(1, 60)]
    public int ReconnectDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Maximum reconnect delay in seconds (exponential backoff cap).
    /// </summary>
    [Range(60, 3600)]
    public int MaxReconnectDelaySeconds { get; set; } = 300;
}

public sealed class SecurityOptions
{
    /// <summary>
    /// Require all commands to be cryptographically signed.
    /// </summary>
    public bool RequireSignedCommands { get; set; } = true;

    /// <summary>
    /// Maximum age of a command timestamp before rejection (replay prevention).
    /// </summary>
    [Range(30, 300)]
    public int CommandMaxAgeSeconds { get; set; } = 60;

    /// <summary>
    /// Enable audit logging of all operations.
    /// </summary>
    public bool EnableAuditLogging { get; set; } = true;
}

public sealed class ProcessOptions
{
    /// <summary>
    /// Base directory for game server files.
    /// </summary>
    [Required]
    public string ServerBasePath { get; set; } = string.Empty;

    /// <summary>
    /// Maximum concurrent game server processes.
    /// </summary>
    [Range(1, 100)]
    public int MaxConcurrentServers { get; set; } = 10;

    /// <summary>
    /// Graceful shutdown timeout in seconds.
    /// </summary>
    [Range(5, 300)]
    public int GracefulShutdownTimeoutSeconds { get; set; } = 30;
}

public sealed class FileOptions
{
    /// <summary>
    /// Temporary directory for downloads and staging.
    /// </summary>
    [Required]
    public string TempDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Maximum file size for transfers (bytes).
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 10L * 1024 * 1024 * 1024; // 10GB

    /// <summary>
    /// Enable P2P file transfer (ICE/STUN/TURN).
    /// </summary>
    public bool EnableP2PTransfer { get; set; } = true;
}
```

#### 1.2 Create Host Builder Extension

**`Hosting/AgentHostBuilderExtensions.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Hosting;

public static class AgentHostBuilderExtensions
{
    public static IHostBuilder ConfigureAgentDefaults(this IHostBuilder builder)
    {
        return builder
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false)
                      .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
                      .AddEnvironmentVariables("DHADGAR_")
                      .AddCommandLine(Environment.GetCommandLineArgs());
            })
            .ConfigureServices((context, services) =>
            {
                // Bind configuration
                services.Configure<AgentOptions>(context.Configuration.GetSection(AgentOptions.SectionName));

                // Add options validation
                services.AddOptions<AgentOptions>()
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                // Add core services (registered in later phases)
            });
    }
}
```

#### 1.3 Add Required Packages

Update `Dhadgar.Agent.Core.csproj`:
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Hosting" />
  <PackageReference Include="Microsoft.Extensions.Options.DataAnnotations" />
  <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
</ItemGroup>
```

### Deliverables
- [ ] `Configuration/AgentOptions.cs`
- [ ] `Configuration/ControlPlaneOptions.cs` (can be nested or separate)
- [ ] `Hosting/AgentHostBuilderExtensions.cs`
- [ ] Updated `.csproj` with packages
- [ ] Unit tests for configuration validation

### Estimated Effort
~3-4 hours

---

## Phase 2: Control Plane Client

**Goal**: Implement SignalR client with automatic reconnection and resilience.

### Tasks

#### 2.1 Define Control Plane Interface

**`Communication/IControlPlaneClient.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Communication;

public interface IControlPlaneClient
{
    /// <summary>
    /// Current connection state.
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// Event raised when connection state changes.
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Event raised when a command is received.
    /// </summary>
    event EventHandler<CommandReceivedEventArgs>? CommandReceived;

    /// <summary>
    /// Connect to the control plane.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the control plane.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a heartbeat with current status.
    /// </summary>
    Task SendHeartbeatAsync(HeartbeatPayload payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send command result back to control plane.
    /// </summary>
    Task SendCommandResultAsync(CommandResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send telemetry data.
    /// </summary>
    Task SendTelemetryAsync(TelemetryPayload payload, CancellationToken cancellationToken = default);
}

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}
```

#### 2.2 Implement SignalR Client

**`Communication/ControlPlaneClient.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Communication;

public sealed class ControlPlaneClient : IControlPlaneClient, IAsyncDisposable
{
    private readonly IOptions<AgentOptions> _options;
    private readonly ICertificateStore _certificateStore;
    private readonly ILogger<ControlPlaneClient> _logger;
    private HubConnection? _connection;
    private ConnectionState _state = ConnectionState.Disconnected;

    public ConnectionState State => _state;
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
    public event EventHandler<CommandReceivedEventArgs>? CommandReceived;

    public ControlPlaneClient(
        IOptions<AgentOptions> options,
        ICertificateStore certificateStore,
        ILogger<ControlPlaneClient> logger)
    {
        _options = options;
        _certificateStore = certificateStore;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var certificate = await _certificateStore.GetClientCertificateAsync(cancellationToken);

        _connection = new HubConnectionBuilder()
            .WithUrl($"{_options.Value.ControlPlane.Endpoint}/agent/v1/hub", options =>
            {
                options.ClientCertificates.Add(certificate);
            })
            .WithAutomaticReconnect(new ExponentialBackoffRetryPolicy(
                _options.Value.ControlPlane.ReconnectDelaySeconds,
                _options.Value.ControlPlane.MaxReconnectDelaySeconds))
            .AddJsonProtocol()
            .Build();

        // Wire up event handlers
        _connection.Closed += OnConnectionClosed;
        _connection.Reconnecting += OnReconnecting;
        _connection.Reconnected += OnReconnected;

        // Register command handler
        _connection.On<AgentCommand>("ReceiveCommand", OnCommandReceived);

        SetState(ConnectionState.Connecting);
        await _connection.StartAsync(cancellationToken);
        SetState(ConnectionState.Connected);

        _logger.LogInformation("Connected to control plane at {Endpoint}",
            _options.Value.ControlPlane.Endpoint);
    }

    public async Task SendHeartbeatAsync(HeartbeatPayload payload, CancellationToken cancellationToken = default)
    {
        if (_connection?.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("Cannot send heartbeat - not connected");
            return;
        }

        await _connection.InvokeAsync("Heartbeat", payload, cancellationToken);
    }

    // ... additional methods

    private void OnCommandReceived(AgentCommand command)
    {
        CommandReceived?.Invoke(this, new CommandReceivedEventArgs(command));
    }

    private void SetState(ConnectionState newState)
    {
        var oldState = _state;
        _state = newState;
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(oldState, newState));
    }
}

internal sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly int _initialDelaySeconds;
    private readonly int _maxDelaySeconds;

    public ExponentialBackoffRetryPolicy(int initialDelaySeconds, int maxDelaySeconds)
    {
        _initialDelaySeconds = initialDelaySeconds;
        _maxDelaySeconds = maxDelaySeconds;
    }

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        // Exponential backoff with jitter
        var delay = Math.Min(
            _initialDelaySeconds * Math.Pow(2, retryContext.PreviousRetryCount),
            _maxDelaySeconds);

        // Add jitter (±20%)
        var jitter = delay * 0.2 * (Random.Shared.NextDouble() * 2 - 1);

        return TimeSpan.FromSeconds(delay + jitter);
    }
}
```

#### 2.3 Add Required Packages

```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />
```

### Deliverables
- [ ] `Communication/IControlPlaneClient.cs`
- [ ] `Communication/ControlPlaneClient.cs`
- [ ] `Communication/ConnectionState.cs`
- [ ] `Communication/ExponentialBackoffRetryPolicy.cs`
- [ ] Integration tests with mock SignalR server

### Estimated Effort
~4-5 hours

---

## Phase 3: Certificate Management

**Goal**: Create platform-agnostic certificate abstraction with secure storage.

### Tasks

#### 3.1 Define Certificate Store Interface

**`Authentication/ICertificateStore.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Authentication;

/// <summary>
/// Platform-specific certificate storage.
/// Windows: Windows Certificate Store (LocalMachine\My)
/// Linux: File-based (/etc/dhadgar/certs/)
/// </summary>
public interface ICertificateStore
{
    /// <summary>
    /// Get the agent's client certificate for mTLS.
    /// </summary>
    Task<X509Certificate2> GetClientCertificateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Store a new client certificate (after enrollment or rotation).
    /// </summary>
    Task StoreCertificateAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a valid certificate exists.
    /// </summary>
    Task<bool> HasValidCertificateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the CA certificate for validating control plane.
    /// </summary>
    Task<X509Certificate2> GetCaCertificateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all stored certificates (for re-enrollment).
    /// </summary>
    Task ClearCertificatesAsync(CancellationToken cancellationToken = default);
}
```

#### 3.2 Implement Enrollment Service

**`Authentication/EnrollmentService.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Authentication;

public sealed class EnrollmentService
{
    private readonly IOptions<AgentOptions> _options;
    private readonly ICertificateStore _certificateStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EnrollmentService> _logger;

    public async Task<EnrollmentResult> EnrollAsync(
        string enrollmentToken,
        CancellationToken cancellationToken = default)
    {
        // 1. Generate key pair (ECDSA P-384 for modern security and smaller key size)
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        // 2. Create CSR
        var csr = CreateCertificateSigningRequest(ecdsa);

        // 3. Submit to control plane
        var client = _httpClientFactory.CreateClient("Enrollment");
        var response = await client.PostAsJsonAsync(
            $"{_options.Value.ControlPlane.Endpoint}/api/v1/agents/enroll",
            new EnrollmentRequest
            {
                Token = enrollmentToken,
                Csr = Convert.ToBase64String(csr),
                Platform = GetPlatformIdentifier(),
                Version = GetAgentVersion()
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Enrollment failed: {StatusCode} - {Error}",
                response.StatusCode, error);
            return EnrollmentResult.Failed(error);
        }

        var result = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(cancellationToken);

        // 4. Create certificate with private key
        var certificate = CreateCertificateWithPrivateKey(result!.Certificate, rsa);

        // 5. Store certificate
        await _certificateStore.StoreCertificateAsync(certificate, cancellationToken);

        _logger.LogInformation("Enrollment successful. Node ID: {NodeId}", result.NodeId);

        return EnrollmentResult.Success(result.NodeId);
    }

    private byte[] CreateCertificateSigningRequest(RSA rsa)
    {
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN=dhadgar-agent"),
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSigningRequest();
    }
}
```

### Deliverables
- [ ] `Authentication/ICertificateStore.cs`
- [ ] `Authentication/EnrollmentService.cs`
- [ ] `Authentication/CertificateValidator.cs`
- [ ] Unit tests for enrollment flow
- [ ] Note: Platform implementations in Agent.Windows/Agent.Linux

### Estimated Effort
~4-5 hours

---

## Phase 4: Heartbeat and Health

**Goal**: Implement background heartbeat service with system metrics collection.

### Tasks

#### 4.1 Create Health Reporter Interface

**`Health/IHealthReporter.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Health;

public interface IHealthReporter
{
    /// <summary>
    /// Collect current system metrics.
    /// </summary>
    Task<SystemMetrics> CollectMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get overall health status.
    /// </summary>
    HealthStatus GetHealthStatus();
}

public sealed record SystemMetrics
{
    public required double CpuUsagePercent { get; init; }
    public required long MemoryUsedBytes { get; init; }
    public required long MemoryTotalBytes { get; init; }
    public required IReadOnlyList<DiskMetrics> Disks { get; init; }
    public required IReadOnlyList<NetworkMetrics> Networks { get; init; }
    public required DateTime CollectedAt { get; init; }
}

public sealed record DiskMetrics
{
    public required string MountPoint { get; init; }
    public required long UsedBytes { get; init; }
    public required long TotalBytes { get; init; }
}

public sealed record NetworkMetrics
{
    public required string InterfaceName { get; init; }
    public required long BytesSent { get; init; }
    public required long BytesReceived { get; init; }
}
```

#### 4.2 Implement Heartbeat Service

**`Health/HeartbeatService.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Health;

public sealed class HeartbeatService : BackgroundService
{
    private readonly IControlPlaneClient _controlPlaneClient;
    private readonly IHealthReporter _healthReporter;
    private readonly IOptions<AgentOptions> _options;
    private readonly ILogger<HeartbeatService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.Value.ControlPlane.HeartbeatIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_controlPlaneClient.State == ConnectionState.Connected)
                {
                    var metrics = await _healthReporter.CollectMetricsAsync(stoppingToken);
                    var payload = new HeartbeatPayload
                    {
                        NodeId = _options.Value.NodeId!.Value,
                        Timestamp = DateTimeOffset.UtcNow,
                        Status = _healthReporter.GetHealthStatus(),
                        Metrics = metrics
                    };

                    await _controlPlaneClient.SendHeartbeatAsync(payload, stoppingToken);

                    _logger.LogDebug("Heartbeat sent. CPU: {Cpu:F1}%, Memory: {Memory:F1}%",
                        metrics.CpuUsagePercent,
                        (double)metrics.MemoryUsedBytes / metrics.MemoryTotalBytes * 100);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to send heartbeat");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
```

### Deliverables
- [ ] `Health/IHealthReporter.cs`
- [ ] `Health/SystemMetrics.cs`
- [ ] `Health/HeartbeatService.cs`
- [ ] `Health/HeartbeatPayload.cs`
- [ ] Unit tests for metrics collection

### Estimated Effort
~3-4 hours

---

## Phase 5: Command Framework

**Goal**: Implement command dispatch with validation and handler registration.

### Tasks

#### 5.1 Define Command Interfaces

**`Commands/ICommandHandler.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Commands;

public interface ICommandHandler<TCommand> where TCommand : AgentCommand
{
    /// <summary>
    /// Handle the command and return a result.
    /// </summary>
    Task<CommandResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

public abstract record AgentCommand
{
    public required Guid CommandId { get; init; }
    public required string Type { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Signature { get; init; }
}

public sealed record CommandResult
{
    public required Guid CommandId { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public object? Data { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
}
```

#### 5.2 Implement Command Dispatcher

**`Commands/CommandDispatcher.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Commands;

public sealed class CommandDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly CommandValidator _validator;
    private readonly ILogger<CommandDispatcher> _logger;
    private readonly Dictionary<string, Type> _handlerTypes = new();

    public void RegisterHandler<TCommand, THandler>()
        where TCommand : AgentCommand
        where THandler : ICommandHandler<TCommand>
    {
        var commandType = typeof(TCommand).Name;
        _handlerTypes[commandType] = typeof(THandler);
    }

    public async Task<CommandResult> DispatchAsync(
        AgentCommand command,
        CancellationToken cancellationToken = default)
    {
        using var activity = AgentActivitySource.Source.StartActivity("DispatchCommand");
        activity?.SetTag("command.type", command.Type);
        activity?.SetTag("command.id", command.CommandId.ToString());

        try
        {
            // 1. Validate command
            var validationResult = _validator.Validate(command);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Command validation failed: {Errors}",
                    string.Join(", ", validationResult.Errors));
                return CommandResult.Failure(command.CommandId, "Validation failed");
            }

            // 2. Find handler
            if (!_handlerTypes.TryGetValue(command.Type, out var handlerType))
            {
                _logger.LogWarning("No handler registered for command type: {Type}", command.Type);
                return CommandResult.Failure(command.CommandId, $"Unknown command type: {command.Type}");
            }

            // 3. Resolve and execute handler
            var handler = _serviceProvider.GetRequiredService(handlerType);
            var handleMethod = handlerType.GetMethod("HandleAsync");
            var task = (Task<CommandResult>)handleMethod!.Invoke(handler, [command, cancellationToken])!;

            return await task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command execution failed: {CommandId}", command.CommandId);
            return CommandResult.Failure(command.CommandId, ex.Message);
        }
    }
}
```

#### 5.3 Implement Command Validator

**`Commands/CommandValidator.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Commands;

public sealed class CommandValidator
{
    private readonly IOptions<AgentOptions> _options;
    private readonly ICertificateStore _certificateStore;

    public ValidationResult Validate(AgentCommand command)
    {
        var errors = new List<string>();

        // 1. Check timestamp freshness (replay prevention)
        var age = DateTimeOffset.UtcNow - command.Timestamp;
        if (age.TotalSeconds > _options.Value.Security.CommandMaxAgeSeconds)
        {
            errors.Add($"Command too old: {age.TotalSeconds:F0}s (max: {_options.Value.Security.CommandMaxAgeSeconds}s)");
        }

        // 2. Verify signature if required
        if (_options.Value.Security.RequireSignedCommands)
        {
            if (!VerifySignature(command))
            {
                errors.Add("Invalid command signature");
            }
        }

        return new ValidationResult(errors.Count == 0, errors);
    }

    private bool VerifySignature(AgentCommand command)
    {
        // Verify HMAC or digital signature using control plane's public key
        // Implementation depends on signing scheme
        return true; // TODO: Implement
    }
}
```

### Deliverables
- [ ] `Commands/ICommandHandler.cs`
- [ ] `Commands/AgentCommand.cs`
- [ ] `Commands/CommandResult.cs`
- [ ] `Commands/CommandDispatcher.cs`
- [ ] `Commands/CommandValidator.cs`
- [ ] Built-in handlers: `PingHandler`, `StatusHandler`
- [ ] Unit tests for dispatch and validation

### Estimated Effort
~4-5 hours

---

## Phase 6: Process Management

**Goal**: Create process management abstraction with resource tracking.

### Tasks

#### 6.1 Define Process Manager Interface

**`Process/IProcessManager.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Process;

/// <summary>
/// Platform-specific process management.
/// Windows: Job Objects
/// Linux: cgroups v2 + namespaces
/// </summary>
public interface IProcessManager
{
    /// <summary>
    /// Start a new game server process.
    /// </summary>
    Task<ProcessHandle> StartAsync(ProcessStartConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop a process gracefully, then forcefully if needed.
    /// </summary>
    Task StopAsync(Guid processId, TimeSpan gracePeriod, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current status of a process.
    /// </summary>
    Task<ProcessStatus> GetStatusAsync(Guid processId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get resource usage for a process.
    /// </summary>
    Task<ProcessResourceUsage> GetResourceUsageAsync(Guid processId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List all managed processes.
    /// </summary>
    Task<IReadOnlyList<ProcessInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attach to an existing process (for recovery after agent restart).
    /// </summary>
    Task<ProcessHandle?> AttachAsync(int pid, CancellationToken cancellationToken = default);
}

public sealed record ProcessStartConfig
{
    public required Guid ServerId { get; init; }
    public required string ExecutablePath { get; init; }
    public required string WorkingDirectory { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } =
        new Dictionary<string, string>();
    public ResourceLimits? ResourceLimits { get; init; }
}

public sealed record ResourceLimits
{
    /// <summary>
    /// Maximum CPU usage as percentage (0-100 per core).
    /// </summary>
    public int? MaxCpuPercent { get; init; }

    /// <summary>
    /// Maximum memory in bytes.
    /// </summary>
    public long? MaxMemoryBytes { get; init; }

    /// <summary>
    /// Maximum disk I/O bandwidth in bytes/second.
    /// </summary>
    public long? MaxDiskIoBytesPerSecond { get; init; }
}

public sealed record ProcessHandle
{
    public required Guid ProcessId { get; init; }
    public required int Pid { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}

public enum ProcessStatus
{
    Starting,
    Running,
    Stopping,
    Stopped,
    Crashed,
    Unknown
}
```

#### 6.2 Implement Process Monitor

**`Process/ProcessMonitor.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Process;

public sealed class ProcessMonitor : BackgroundService
{
    private readonly IProcessManager _processManager;
    private readonly IControlPlaneClient _controlPlaneClient;
    private readonly ILogger<ProcessMonitor> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processes = await _processManager.ListAsync(stoppingToken);

                foreach (var process in processes)
                {
                    var status = await _processManager.GetStatusAsync(process.ProcessId, stoppingToken);

                    if (status == ProcessStatus.Crashed)
                    {
                        _logger.LogWarning("Process {ProcessId} crashed. PID was {Pid}",
                            process.ProcessId, process.Pid);

                        // Notify control plane
                        await _controlPlaneClient.SendTelemetryAsync(new TelemetryPayload
                        {
                            Type = "ProcessCrashed",
                            Data = new { process.ProcessId, process.ServerId }
                        }, stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Process monitoring failed");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }
}
```

### Deliverables
- [ ] `Process/IProcessManager.cs`
- [ ] `Process/ProcessStartConfig.cs`
- [ ] `Process/ResourceLimits.cs`
- [ ] `Process/ProcessMonitor.cs`
- [ ] `Process/ProcessInfo.cs`
- [ ] Unit tests for process lifecycle
- [ ] Note: Platform implementations in Agent.Windows/Agent.Linux

### Estimated Effort
~5-6 hours

---

## Phase 7: File Transfer

**Goal**: Implement secure file transfer with P2P support using WebRTC/ICE.

### Tasks

#### 7.1 Define File Transfer Interface

**`Files/IFileTransferService.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Files;

public interface IFileTransferService
{
    /// <summary>
    /// Download a file from the control plane or via P2P.
    /// </summary>
    Task<FileTransferResult> DownloadAsync(
        FileDownloadRequest request,
        IProgress<FileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upload a file to the control plane or via P2P.
    /// </summary>
    Task<FileTransferResult> UploadAsync(
        FileUploadRequest request,
        IProgress<FileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify file integrity.
    /// </summary>
    Task<bool> VerifyIntegrityAsync(string filePath, string expectedHash, CancellationToken cancellationToken = default);
}

public sealed record FileDownloadRequest
{
    public required Guid TransferId { get; init; }
    public required string SourceUrl { get; init; }
    public required string DestinationPath { get; init; }
    public required string ExpectedHash { get; init; }
    public long? ExpectedSize { get; init; }
    public bool AllowP2P { get; init; } = true;
}

public sealed record FileTransferProgress
{
    public required long BytesTransferred { get; init; }
    public required long TotalBytes { get; init; }
    public required double BytesPerSecond { get; init; }
    public required TransferMethod Method { get; init; }
}

public enum TransferMethod
{
    Direct,      // Direct HTTPS from control plane
    P2P,         // WebRTC data channel
    TurnRelay    // TURN relay fallback
}
```

#### 7.2 Implement Path Validator

**`Files/PathValidator.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Files;

public sealed class PathValidator
{
    private readonly IOptions<AgentOptions> _options;
    private readonly ILogger<PathValidator> _logger;

    /// <summary>
    /// Validate that a path is within allowed directories.
    /// CRITICAL: This prevents path traversal attacks.
    /// </summary>
    public bool IsPathAllowed(string requestedPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(requestedPath);
            var serverBasePath = Path.GetFullPath(_options.Value.Process.ServerBasePath);
            var tempPath = Path.GetFullPath(_options.Value.Files.TempDirectory);

            // Use OS-appropriate comparison
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            // Must be within server base path or temp directory
            var serverBaseWithSep = serverBasePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var tempWithSep = tempPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            return fullPath.StartsWith(serverBaseWithSep, comparison) ||
                   fullPath.StartsWith(tempWithSep, comparison);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Path validation failed for: {Path}", requestedPath);
            return false;
        }
    }

    /// <summary>
    /// Validate file name doesn't contain dangerous characters.
    /// </summary>
    public bool IsFileNameSafe(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        // Reject path separators
        if (fileName.Contains('/') || fileName.Contains('\\'))
            return false;

        // Reject null bytes
        if (fileName.Contains('\0'))
            return false;

        // Reject parent directory references
        if (fileName == ".." || fileName.StartsWith("../") || fileName.StartsWith("..\\"))
            return false;

        // Reject invalid Windows characters
        var invalidChars = Path.GetInvalidFileNameChars();
        if (fileName.Any(c => invalidChars.Contains(c)))
            return false;

        return true;
    }
}
```

#### 7.3 Implement P2P Transfer (SIPSorcery)

**`Files/P2P/IceConnection.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Files.P2P;

/// <summary>
/// WebRTC ICE connection for P2P file transfer.
/// Uses SIPSorcery library for pure C# implementation.
/// </summary>
public sealed class IceConnection : IAsyncDisposable
{
    private readonly RTCPeerConnection _peerConnection;
    private readonly ILogger<IceConnection> _logger;
    private RTCDataChannel? _dataChannel;

    public IceConnection(IceConfiguration config, ILogger<IceConnection> logger)
    {
        _logger = logger;

        var rtcConfig = new RTCConfiguration
        {
            iceServers = new List<RTCIceServer>
            {
                new RTCIceServer { urls = config.StunServers.ToArray() },
                new RTCIceServer
                {
                    urls = config.TurnServers.ToArray(),
                    username = config.TurnUsername,
                    credential = config.TurnPassword
                }
            }
        };

        _peerConnection = new RTCPeerConnection(rtcConfig);

        _peerConnection.oniceconnectionstatechange += (state) =>
        {
            _logger.LogDebug("ICE connection state: {State}", state);
        };

        _peerConnection.ondatachannel += (channel) =>
        {
            _logger.LogInformation("Data channel received: {Label}", channel.label);
            _dataChannel = channel;
        };
    }

    public async Task<string> CreateOfferAsync()
    {
        _dataChannel = await _peerConnection.createDataChannel("file-transfer");
        var offer = _peerConnection.createOffer();
        await _peerConnection.setLocalDescription(offer);
        return offer.sdp;
    }

    public async Task SetRemoteDescriptionAsync(string sdp, RTCSdpType type)
    {
        var description = new RTCSessionDescriptionInit { sdp = sdp, type = type };
        await _peerConnection.setRemoteDescription(description);
    }

    public void AddIceCandidate(string candidate, string? sdpMid, ushort sdpMLineIndex)
    {
        var iceCandidate = new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid,
            sdpMLineIndex = sdpMLineIndex
        };
        _peerConnection.addIceCandidate(iceCandidate);
    }

    public async ValueTask DisposeAsync()
    {
        _dataChannel?.close();
        _peerConnection.close();
    }
}

public sealed record IceConfiguration
{
    public IReadOnlyList<string> StunServers { get; init; } = ["stun:stun.l.google.com:19302"];
    public IReadOnlyList<string> TurnServers { get; init; } = [];
    public string? TurnUsername { get; init; }
    public string? TurnPassword { get; init; }
}
```

#### 7.4 Add Required Packages

```xml
<PackageReference Include="SIPSorcery" Version="10.0.3" />
```

### Deliverables
- [ ] `Files/IFileTransferService.cs`
- [ ] `Files/PathValidator.cs`
- [ ] `Files/FileIntegrityChecker.cs`
- [ ] `Files/P2P/IceConnection.cs`
- [ ] `Files/P2P/DataChannelTransfer.cs`
- [ ] Security tests for path traversal
- [ ] Integration tests for file transfer

### Estimated Effort
~6-8 hours

---

## Phase 8: Telemetry

**Goal**: Implement OpenTelemetry integration for metrics, traces, and logs.

### Tasks

#### 8.1 Create Activity Sources

**`Telemetry/AgentActivitySource.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Telemetry;

public static class AgentActivitySource
{
    public const string Name = "Dhadgar.Agent";
    public static readonly ActivitySource Source = new(Name, "1.0.0");
}
```

#### 8.2 Create Custom Metrics

**`Telemetry/AgentMeter.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Telemetry;

public static class AgentMeter
{
    public const string Name = "Dhadgar.Agent";
    private static readonly Meter Meter = new(Name, "1.0.0");

    // Connection metrics
    public static readonly Counter<long> ConnectionAttempts =
        Meter.CreateCounter<long>("agent.connection.attempts", "count", "Number of connection attempts");
    public static readonly Counter<long> ConnectionFailures =
        Meter.CreateCounter<long>("agent.connection.failures", "count", "Number of connection failures");
    public static readonly ObservableGauge<int> ConnectionState =
        Meter.CreateObservableGauge("agent.connection.state", () => 0, "state", "Current connection state");

    // Command metrics
    public static readonly Counter<long> CommandsReceived =
        Meter.CreateCounter<long>("agent.commands.received", "count", "Commands received from control plane");
    public static readonly Counter<long> CommandsSucceeded =
        Meter.CreateCounter<long>("agent.commands.succeeded", "count", "Commands executed successfully");
    public static readonly Counter<long> CommandsFailed =
        Meter.CreateCounter<long>("agent.commands.failed", "count", "Commands that failed");
    public static readonly Histogram<double> CommandDuration =
        Meter.CreateHistogram<double>("agent.commands.duration", "ms", "Command execution duration");

    // Process metrics
    public static readonly ObservableGauge<int> ManagedProcessCount =
        Meter.CreateObservableGauge("agent.processes.count", () => 0, "count", "Number of managed processes");
    public static readonly Counter<long> ProcessStarted =
        Meter.CreateCounter<long>("agent.processes.started", "count", "Processes started");
    public static readonly Counter<long> ProcessCrashed =
        Meter.CreateCounter<long>("agent.processes.crashed", "count", "Processes that crashed");

    // File transfer metrics
    public static readonly Counter<long> BytesTransferred =
        Meter.CreateCounter<long>("agent.files.bytes_transferred", "bytes", "Total bytes transferred");
    public static readonly Histogram<double> TransferSpeed =
        Meter.CreateHistogram<double>("agent.files.transfer_speed", "bytes/s", "File transfer speed");
}
```

#### 8.3 Add OpenTelemetry Configuration

**`Hosting/OpenTelemetryExtensions.cs`**:
```csharp
namespace Dhadgar.Agent.Core.Hosting;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddAgentTelemetry(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .AddSource(AgentActivitySource.Name)
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter();
            })
            .WithMetrics(builder =>
            {
                builder
                    .AddMeter(AgentMeter.Name)
                    .AddProcessInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddOtlpExporter();
            });

        return services;
    }
}
```

#### 8.4 Add Required Packages

```xml
<PackageReference Include="OpenTelemetry.Extensions.Hosting" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" />
<PackageReference Include="OpenTelemetry.Instrumentation.Process" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
```

### Deliverables
- [ ] `Telemetry/AgentActivitySource.cs`
- [ ] `Telemetry/AgentMeter.cs`
- [ ] `Hosting/OpenTelemetryExtensions.cs`
- [ ] Updated `.csproj` with packages
- [ ] Integration with Aspire dashboard

### Estimated Effort
~3-4 hours

---

## Dependencies and Prerequisites

### Must Be Complete Before Starting

1. **Nodes Service** (already implemented)
   - Agent enrollment endpoint
   - Certificate signing (CA)
   - Heartbeat endpoint

2. **Contracts** (already exists)
   - Add agent-specific contracts as needed

### Package Dependencies

```xml
<ItemGroup>
  <!-- Core -->
  <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
  <PackageReference Include="Microsoft.Extensions.Options.DataAnnotations" Version="10.0.0" />

  <!-- Communication -->
  <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />
  <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="10.0.0" />

  <!-- P2P -->
  <PackageReference Include="SIPSorcery" Version="10.0.3" />

  <!-- Telemetry -->
  <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.11.0" />
  <PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.11.0" />
  <PackageReference Include="OpenTelemetry.Instrumentation.Process" Version="0.5.0-beta.7" />
  <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.11.0" />
  <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.11.0" />

  <!-- Security -->
  <PackageReference Include="SecurityCodeScan.VS2019" Version="5.6.7" />
</ItemGroup>
```

---

## Success Criteria

### Phase 1 Complete When
- [ ] Configuration classes compile and validate
- [ ] Host builder extension works
- [ ] Unit tests pass for configuration validation

### Phase 2 Complete When
- [ ] SignalR client connects to mock server
- [ ] Automatic reconnection works
- [ ] Connection state events fire correctly

### Phase 3 Complete When
- [ ] Certificate interface defined
- [ ] Enrollment service connects to Nodes API
- [ ] Certificate validation passes

### Phase 4 Complete When
- [ ] Heartbeat sends on schedule
- [ ] System metrics collected accurately
- [ ] Health status reflects actual state

### Phase 5 Complete When
- [ ] Commands dispatch to correct handlers
- [ ] Validation rejects old/invalid commands
- [ ] Built-in handlers work (Ping, Status)

### Phase 6 Complete When
- [ ] Process interface defined
- [ ] Process monitor detects crashes
- [ ] Resource limits structure defined

### Phase 7 Complete When
- [ ] File downloads work via HTTPS
- [ ] P2P connection establishes (with mock signaling)
- [ ] Path validation rejects traversal attacks

### Phase 8 Complete When
- [ ] Traces appear in Aspire dashboard
- [ ] Metrics export to OTLP endpoint
- [ ] Custom agent metrics recorded

### Overall Complete When
- [ ] All unit tests pass
- [ ] Integration tests pass with mock control plane
- [ ] Security review passes (agent-service-guardian)
- [ ] Documentation updated

---

## Estimated Total Effort

| Phase | Effort | Dependencies |
|-------|--------|--------------|
| Phase 1: Configuration | ~3-4 hours | None |
| Phase 2: Control Plane Client | ~4-5 hours | Phase 1 |
| Phase 3: Certificate Management | ~4-5 hours | Phase 1 |
| Phase 4: Heartbeat | ~3-4 hours | Phase 2, 3 |
| Phase 5: Commands | ~4-5 hours | Phase 2 |
| Phase 6: Process Management | ~5-6 hours | Phase 1 |
| Phase 7: File Transfer | ~6-8 hours | Phase 1, 5 |
| Phase 8: Telemetry | ~3-4 hours | Phase 1 |
| **Total** | **~32-41 hours** | |

---

## Security Review Checklist

Before merging any phase, verify:

- [ ] **No Command Injection**: All process arguments are properly escaped
- [ ] **No Path Traversal**: All file paths validated against allowed directories
- [ ] **No Secret Leakage**: No credentials in logs or telemetry
- [ ] **mTLS Enforced**: All control plane communication uses mTLS
- [ ] **Input Validated**: All control plane inputs validated and sanitized
- [ ] **Replay Prevention**: Command timestamps checked
- [ ] **Signature Verification**: Command signatures verified (when enabled)

**IMPORTANT**: Run `agent-service-guardian` review before any PR.
