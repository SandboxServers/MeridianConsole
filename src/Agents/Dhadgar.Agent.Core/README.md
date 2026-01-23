# Dhadgar.Agent.Core

## The Shared Core Library for Customer-Hosted Agents

---

## Table of Contents

1. [Overview](#overview)
2. [Security Model](#security-model)
3. [Architecture](#architecture)
4. [Key Components](#key-components)
5. [Communication](#communication)
6. [Authentication](#authentication)
7. [Command Handling](#command-handling)
8. [Telemetry](#telemetry)
9. [Process Management](#process-management)
10. [File Operations](#file-operations)
11. [Configuration](#configuration)
12. [Extension Points](#extension-points)
13. [Testing](#testing)
14. [Security Considerations](#security-considerations)
15. [Related Documentation](#related-documentation)

---

## Overview

### What is Dhadgar.Agent.Core?

`Dhadgar.Agent.Core` is the shared core library that provides common functionality for the Meridian Console customer-hosted agents. This library contains the platform-agnostic code that both the Linux agent (`Dhadgar.Agent.Linux`) and Windows agent (`Dhadgar.Agent.Windows`) depend upon.

### Purpose

The Agent Core library serves as the foundation for agents that:

- **Execute orchestrated commands** from the Meridian Console control plane
- **Manage game server processes** on customer-owned hardware
- **Report telemetry and health metrics** back to the control plane
- **Handle file operations** for game server deployments, mods, and configurations
- **Maintain secure communication** with the central platform

### Current Status

**Status: Early-Stage Scaffolding**

The Agent Core library is currently in its foundational scaffolding phase. The existing code provides the basic project structure and "hello world" functionality used for build verification and smoke tests. The full implementation of agent functionality (command execution, process management, telemetry, etc.) is planned but not yet implemented.

The current implementation includes:

- Basic project structure with security analyzers enabled
- `Hello.cs` - A simple static class for smoke testing
- `Program.cs` - Entry point placeholder

### What This Library Is NOT

- **NOT a standalone executable for production use** - While it can be run directly, the production agents are `Dhadgar.Agent.Linux` and `Dhadgar.Agent.Windows`, which extend this library with platform-specific functionality.
- **NOT a general-purpose library** - This is specifically designed for the Meridian Console agent architecture and should not be used outside of this context.

---

## Security Model

### Why This Code is Security-Critical

**The Dhadgar.Agent.Core library and its platform-specific implementations represent the MOST SECURITY-CRITICAL components of the entire Meridian Console platform.**

Here's why:

#### 1. Runs on Customer Hardware with Elevated Privileges

Agents run directly on customer-owned servers, game hosting machines, and infrastructure. To perform their duties, they require elevated privileges:

- **Process management**: Starting, stopping, and monitoring game server processes
- **Resource allocation**: Managing CPU, memory, and network port assignments
- **File system access**: Reading/writing game files, configurations, and mods
- **Network operations**: Binding ports, managing firewall rules (where applicable)

#### 2. High-Trust Component

Customers are entrusting this software to:

1. Execute commands on their machines
2. Have visibility into their system resources
3. Manage processes with system-level access
4. Handle potentially sensitive game server data

**Any security vulnerability in agent code has a direct path to customer infrastructure.**

#### 3. Attack Surface Considerations

The agent represents a potential attack vector for:

- **Control Plane Compromise**: If the control plane is compromised, agents could receive malicious commands
- **Man-in-the-Middle Attacks**: Communication interception could lead to unauthorized command execution
- **Process Escape**: Malicious game server binaries might attempt to escape isolation
- **Data Exfiltration**: Improperly secured telemetry could leak sensitive information

### Security Design Principles

The Agent Core library is designed with these security principles:

| Principle                     | Description                                                                |
| ----------------------------- | -------------------------------------------------------------------------- |
| **Least Privilege**           | Agents request only the minimum permissions necessary                      |
| **Defense in Depth**          | Multiple layers of security controls protect against single-point failures |
| **Fail Secure**               | When in doubt, operations are denied rather than allowed                   |
| **Minimal Attack Surface**    | Every new capability increases risk and must be justified                  |
| **Outbound-Only Connections** | Agents never accept inbound connections from the internet                  |

### Security Analyzers and Tooling

The Agent Core project has enhanced security analysis enabled:

```xml
<!-- From Dhadgar.Agent.Core.csproj -->
<PropertyGroup>
  <!-- Agent code runs on customer hardware - enforce strict security -->
  <AnalysisMode>All</AnalysisMode>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
</PropertyGroup>

<ItemGroup>
  <!-- Security analyzers - critical for customer-hosted code -->
  <PackageReference Include="SecurityCodeScan.VS2019" />
</ItemGroup>
```

The **SecurityCodeScan** analyzer detects:

- SQL injection vulnerabilities
- Command injection vulnerabilities
- Path traversal attacks
- Cross-site scripting (XSS)
- Cryptographic weaknesses
- Hardcoded secrets

---

## Architecture

### System Context

```text
                                    ┌───────────────────────────────────────┐
                                    │        Meridian Console               │
                                    │         Control Plane                 │
                                    │                                       │
                                    │  ┌─────────┐  ┌─────────┐             │
                                    │  │ Gateway │  │ Nodes   │  ...        │
                                    │  └────┬────┘  └────┬────┘             │
                                    │       │            │                  │
                                    │       └─────┬──────┘                  │
                                    │             │                         │
                                    └─────────────┼─────────────────────────┘
                                                  │
                                                  │  HTTPS/WSS
                                                  │  (outbound only)
                                                  │
              ┌───────────────────────────────────┼──────────────────────────────────┐
              │                 Customer Infrastructure                              │
              │                                                                      │
              │  ┌─────────────────────────┐    ┌─────────────────────────┐          │
              │  │     Linux Server        │    │    Windows Server       │          │
              │  │                         │    │                         │          │
              │  │  ┌───────────────────┐  │    │  ┌───────────────────┐  │          │
              │  │  │ Dhadgar.Agent     │  │    │  │ Dhadgar.Agent     │  │          │
              │  │  │    .Linux         │  │    │  │    .Windows       │  │          │
              │  │  │                   │  │    │  │                   │  │          │
              │  │  │ ┌───────────────┐ │  │    │  │ ┌───────────────┐ │  │          │
              │  │  │ │ Agent.Core    │ │  │    │  │ │ Agent.Core    │ │  │          │
              │  │  │ └───────────────┘ │  │    │  │ └───────────────┘ │  │          │
              │  │  └───────────────────┘  │    │  └───────────────────┘  │          │
              │  │           │             │    │           │             │          │
              │  │  ┌────────┴────────┐    │    │  ┌────────┴────────┐    │          │
              │  │  │  Game Servers   │    │    │  │  Game Servers   │    │          │
              │  │  │  (Minecraft,    │    │    │  │  (ARK, Rust,    │    │          │
              │  │  │  Valheim, etc.) │    │    │  │  etc.)          │    │          │
              │  │  └─────────────────┘    │    │  └─────────────────┘    │          │
              │  └─────────────────────────┘    └─────────────────────────┘          │
              └──────────────────────────────────────────────────────────────────────┘
```

### Library Structure

```
Dhadgar.Agent.Core/
├── Dhadgar.Agent.Core.csproj    # Project file with security settings
├── Program.cs                    # Entry point (scaffolding)
├── Hello.cs                      # Smoke test class
└── README.md                     # This documentation

Planned structure (not yet implemented):
├── Communication/                # Control plane communication
│   ├── IControlPlaneClient.cs
│   ├── ControlPlaneClient.cs
│   └── Messages/
├── Commands/                     # Command handling
│   ├── ICommandHandler.cs
│   ├── CommandDispatcher.cs
│   └── Handlers/
├── Process/                      # Process management
│   ├── IProcessManager.cs
│   ├── ProcessSandbox.cs
│   └── ResourceLimits.cs
├── Telemetry/                    # Metrics and health reporting
│   ├── ITelemetryReporter.cs
│   └── Metrics/
├── Files/                        # File operations
│   ├── IFileTransferService.cs
│   └── PathValidator.cs
├── Authentication/               # mTLS and certificate management
│   ├── ICertificateManager.cs
│   └── TokenValidator.cs
└── Configuration/                # Agent configuration
    └── AgentOptions.cs
```

### Project Dependencies

```
Dhadgar.Agent.Core
├── Dhadgar.Contracts         # DTOs, message contracts
└── Dhadgar.Shared            # Utilities, primitives

Dhadgar.Agent.Linux (extends Agent.Core)
├── Dhadgar.Agent.Core
├── Dhadgar.Contracts
└── Dhadgar.Shared

Dhadgar.Agent.Windows (extends Agent.Core)
├── Dhadgar.Agent.Core
├── Dhadgar.Contracts
└── Dhadgar.Shared
```

**Important**: Agent projects reference only the shared libraries (`Contracts`, `Shared`). They do NOT reference any microservices directly. This maintains the microservices architecture principle of runtime-only dependencies.

---

## Key Components

### Current Implementation

#### Hello.cs

```csharp
namespace Dhadgar.Agent.Core;

/// <summary>
/// "Hello world" surface area used by tests and quick smoke-checks.
/// </summary>
public static class Hello
{
    public const string Message = "Hello from Dhadgar.Agent.Core";
}
```

This class serves as:

- A build verification target
- A smoke test entry point
- A reference for the test project

#### Program.cs

```csharp
using Dhadgar.Agent.Core;

Console.WriteLine(Hello.Message);

// TODO: flesh this out with real behavior (options/commands/heartbeat/etc.).
```

The entry point is a placeholder indicating planned functionality:

- Command-line options parsing
- Command execution
- Heartbeat/health reporting

### Planned Components (Design Intent)

The following components represent the intended architecture. They are not yet implemented but define the target structure:

#### IControlPlaneClient

Responsible for all communication with the Meridian Console control plane.

**Planned responsibilities:**

- Establish and maintain secure connections
- Handle connection retry and backoff logic
- Manage authentication tokens and certificate rotation
- Send telemetry and receive commands

#### ICommandHandler

Interface for handling specific command types from the control plane.

**Planned command types:**

- `StartServerCommand`
- `StopServerCommand`
- `RestartServerCommand`
- `UpdateServerCommand`
- `SyncFilesCommand`
- `ExecuteMaintenanceCommand`

#### IProcessManager

Manages game server processes on the host system.

**Planned responsibilities:**

- Start processes with proper isolation
- Monitor process health and resource usage
- Enforce resource limits (CPU, memory, etc.)
- Graceful shutdown with timeout escalation

#### ITelemetryReporter

Reports metrics and health information to the control plane.

**Planned metrics:**

- System resource utilization (CPU, memory, disk, network)
- Per-server metrics (process status, port bindings, player counts)
- Agent health (uptime, version, last communication time)
- Error and warning events

#### ICertificateManager

Handles mTLS certificates and secure key storage.

**Planned responsibilities:**

- Secure storage of private keys
- Certificate rotation handling
- Certificate validation for control plane connections
- Key generation for enrollment

---

## Communication

### Connection Model: Outbound-Only

**Critical Design Principle**: Agents make OUTBOUND-ONLY connections to the control plane.

```
                   Customer Firewall
                        ┌───┐
                        │   │
  Control Plane  ◀──────┼───┼────── Agent
  (Accepts inbound)     │   │       (Initiates connection)
                        │   │
                        └───┘

  No inbound holes required on customer side!
```

This design provides several security benefits:

1. **Firewall-Friendly**: No ports need to be opened on customer infrastructure
2. **NAT Traversal**: Works through NAT without configuration
3. **Reduced Attack Surface**: No listening services on customer machines
4. **Customer Control**: Customers can block outbound if needed

### Communication Protocols (Planned)

| Protocol            | Use Case                 | Notes                                      |
| ------------------- | ------------------------ | ------------------------------------------ |
| **HTTPS**           | REST API calls           | Commands, configuration, enrollment        |
| **WebSocket (WSS)** | Bidirectional streaming  | Real-time console, continuous telemetry    |
| **gRPC over TLS**   | High-frequency telemetry | Alternative for performance-critical paths |

### Message Flow (Design Intent)

```
┌─────────┐                              ┌──────────────┐
│  Agent  │                              │ Control Plane │
└────┬────┘                              └──────┬───────┘
     │                                          │
     │  1. Connect (mTLS handshake)             │
     │─────────────────────────────────────────▶│
     │                                          │
     │  2. Heartbeat (I'm alive, here's my ID)  │
     │─────────────────────────────────────────▶│
     │                                          │
     │  3. Command: StartServer(config)         │
     │◀─────────────────────────────────────────│
     │                                          │
     │  4. Ack: Starting server                 │
     │─────────────────────────────────────────▶│
     │                                          │
     │  5. Telemetry: Server started, port 25565│
     │─────────────────────────────────────────▶│
     │                                          │
     │  ... (continuous telemetry stream) ...   │
     │                                          │
     │  6. Command: StopServer(graceful)        │
     │◀─────────────────────────────────────────│
     │                                          │
     │  7. Telemetry: Server stopped            │
     │─────────────────────────────────────────▶│
     │                                          │
```

### Reconnection and Resilience (Planned)

The agent must handle disconnection gracefully:

1. **Exponential Backoff**: Retry connections with increasing delays
2. **Offline Operation**: Continue managing servers even without control plane
3. **Command Queuing**: Buffer commands received just before disconnect
4. **State Reconciliation**: Sync state with control plane upon reconnection

---

## Authentication

### Current State

Authentication is planned but not yet implemented. The target architecture uses mutual TLS (mTLS) for all agent-to-control-plane communication.

### Planned: mTLS Architecture

```
┌─────────────────┐                    ┌─────────────────┐
│      Agent      │                    │  Control Plane   │
├─────────────────┤                    ├─────────────────┤
│                 │                    │                 │
│ Agent Private   │                    │ Server Private  │
│     Key         │                    │     Key         │
│ Agent Cert      │◀──── Verify ──────▶│ Server Cert     │
│ (signed by CA)  │                    │ (signed by CA)  │
│                 │                    │                 │
│ CA Public Cert  │                    │ CA Public Cert  │
│                 │                    │                 │
└─────────────────┘                    └─────────────────┘
           │                                    │
           │          TLS Handshake             │
           │◀──────────────────────────────────▶│
           │                                    │
           │  Both parties verified by CA       │
           └────────────────────────────────────┘
```

### Agent Enrollment Flow (Planned)

1. **Initial Enrollment**
   - Agent generates a key pair
   - Submits certificate signing request (CSR) to control plane
   - Control plane admin approves enrollment
   - Agent receives signed certificate

2. **Certificate Rotation**
   - Certificates have limited validity (e.g., 90 days)
   - Agent requests renewal before expiration
   - Control plane issues new certificate
   - Agent rotates to new certificate seamlessly

3. **Revocation**
   - Control plane can revoke agent certificates
   - Agents check certificate revocation list (CRL) or OCSP
   - Revoked agents cannot connect

### JWT Tokens (Planned)

In addition to mTLS, agents may use JWT tokens for:

- **Session Management**: Short-lived tokens for specific operations
- **Authorization**: Claim-based permissions for command execution
- **Audit Trail**: Token-based request attribution

---

## Command Handling

### Command Types (Planned)

Commands are instructions from the control plane to the agent. Each command type has a dedicated handler:

| Command         | Description                          | Security Considerations                         |
| --------------- | ------------------------------------ | ----------------------------------------------- |
| `StartServer`   | Launch a game server process         | Validate configuration, enforce resource limits |
| `StopServer`    | Gracefully stop a server             | Verify ownership, prevent unauthorized stops    |
| `RestartServer` | Stop then start a server             | Combine Start/Stop validations                  |
| `UpdateServer`  | Update server files                  | Validate file sources, check signatures         |
| `SyncFiles`     | Synchronize files from control plane | Path traversal prevention, integrity checks     |
| `ExecuteScript` | Run a maintenance script             | **DANGEROUS** - requires strict whitelisting    |

### Command Execution Flow (Planned)

```
┌─────────────────────────────────────────────────────────────────┐
│                        Command Execution                         │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
                    ┌───────────────────────┐
                    │  Receive Command      │
                    │  (from control plane) │
                    └───────────┬───────────┘
                                │
                                ▼
                    ┌───────────────────────┐
                    │  Validate Command     │
                    │  - Signature valid?   │
                    │  - Schema valid?      │
                    │  - Timestamp fresh?   │
                    └───────────┬───────────┘
                                │
                    ┌───────────┴───────────┐
                    │                       │
                    ▼                       ▼
             ┌──────────┐            ┌──────────┐
             │  Valid   │            │ Invalid  │──▶ Reject & Log
             └────┬─────┘            └──────────┘
                  │
                  ▼
        ┌───────────────────────┐
        │  Authorize Command    │
        │  - Agent has perms?   │
        │  - Resource allowed?  │
        └───────────┬───────────┘
                    │
        ┌───────────┴───────────┐
        │                       │
        ▼                       ▼
   ┌──────────┐          ┌──────────┐
   │ Authorized│          │Unauthorized│──▶ Reject & Audit
   └────┬─────┘          └──────────┘
        │
        ▼
   ┌───────────────────────┐
   │  Dispatch to Handler  │
   │  (type-specific)      │
   └───────────┬───────────┘
               │
               ▼
   ┌───────────────────────┐
   │  Execute Command      │
   │  (with resource limits)│
   └───────────┬───────────┘
               │
               ▼
   ┌───────────────────────┐
   │  Report Result        │
   │  (to control plane)   │
   └───────────────────────┘
```

### Command Validation Requirements (Planned)

Every command must be validated:

1. **Cryptographic Signature**: Commands must be signed by the control plane
2. **Timestamp Validation**: Reject stale commands (replay attack prevention)
3. **Schema Validation**: Command payload must match expected schema
4. **Authorization Check**: Agent must be authorized for the specific operation
5. **Resource Validation**: Target resources must exist and be valid

---

## Telemetry

### What Agents Report (Planned)

Agents collect and report telemetry to the control plane:

#### System Metrics

| Metric                   | Description                  | Frequency |
| ------------------------ | ---------------------------- | --------- |
| `cpu_usage_percent`      | Overall CPU utilization      | Every 10s |
| `memory_used_bytes`      | Total memory used            | Every 10s |
| `memory_available_bytes` | Available memory             | Every 10s |
| `disk_used_bytes`        | Disk space used (per volume) | Every 60s |
| `disk_available_bytes`   | Available disk space         | Every 60s |
| `network_bytes_sent`     | Network bytes transmitted    | Every 10s |
| `network_bytes_received` | Network bytes received       | Every 10s |

#### Server Metrics

| Metric                  | Description                      | Frequency |
| ----------------------- | -------------------------------- | --------- |
| `server_status`         | Running, stopped, crashed, etc.  | On change |
| `server_cpu_percent`    | Per-server CPU usage             | Every 10s |
| `server_memory_bytes`   | Per-server memory usage          | Every 10s |
| `server_port`           | Bound port number                | On change |
| `server_player_count`   | Connected players (if available) | Every 30s |
| `server_uptime_seconds` | Time since server started        | Every 60s |

#### Agent Metrics

| Metric                    | Description               | Frequency  |
| ------------------------- | ------------------------- | ---------- |
| `agent_version`           | Agent software version    | On connect |
| `agent_uptime_seconds`    | Time since agent started  | Every 60s  |
| `agent_last_command_time` | Timestamp of last command | On command |
| `agent_error_count`       | Errors since last report  | Every 60s  |

### Privacy and Data Collection Principles

**Minimum Data Collection**: Agents collect ONLY what is necessary for operation.

Agents do NOT collect:

- Game server content (save files, player data, etc.)
- Actual game traffic or chat logs
- Personal information beyond what's needed for operation
- Filesystem contents beyond managed directories

Agents MAY collect (with explicit user consent):

- Server logs (if user enables log forwarding)
- Crash dumps (for debugging, when explicitly requested)
- Performance profiles (for troubleshooting)

---

## Process Management

### Game Server Lifecycle (Planned)

Agents manage game server processes through their complete lifecycle:

```
┌────────────────────────────────────────────────────────────────────┐
│                    Game Server Lifecycle                            │
└────────────────────────────────────────────────────────────────────┘

    ┌──────────────────┐
    │     Created      │◀─── CreateServer command
    └────────┬─────────┘
             │
             ▼
    ┌──────────────────┐
    │   Configuring    │◀─── Files synced, config applied
    └────────┬─────────┘
             │
             ▼
    ┌──────────────────┐
    │    Starting      │◀─── StartServer command
    └────────┬─────────┘
             │
             ▼
    ┌──────────────────┐          ┌──────────────┐
    │    Running       │─────────▶│   Stopping   │◀─── StopServer command
    └────────┬─────────┘          └──────┬───────┘
             │                           │
             │                           ▼
             │                    ┌──────────────┐
             │                    │   Stopped    │
             │                    └──────┬───────┘
             │                           │
             ▼                           ▼
    ┌──────────────────┐          ┌──────────────┐
    │    Crashed       │          │   Deleting   │◀─── DeleteServer command
    └──────────────────┘          └──────┬───────┘
             │                           │
             │                           ▼
             │                    ┌──────────────┐
             │                    │   Deleted    │
             └───────────────────▶└──────────────┘
```

### Process Isolation (Planned)

Game servers must be isolated from each other and from the host system:

#### Linux Isolation Mechanisms

| Mechanism           | Purpose                                           |
| ------------------- | ------------------------------------------------- |
| **namespaces**      | Isolate process views (PID, network, mount, etc.) |
| **cgroups**         | Limit CPU, memory, I/O resources                  |
| **seccomp**         | Restrict system calls                             |
| **capabilities**    | Drop unnecessary privileges                       |
| **user namespaces** | Run as unprivileged user inside container         |

#### Windows Isolation Mechanisms

| Mechanism              | Purpose                                 |
| ---------------------- | --------------------------------------- |
| **Job objects**        | Group and limit processes               |
| **Integrity levels**   | Restrict process privileges             |
| **Token manipulation** | Create restricted tokens                |
| **AppContainer**       | Sandbox process capabilities (optional) |

### Resource Limits (Planned)

Every game server operates within defined resource limits:

```csharp
// Example resource limit configuration
public class ResourceLimits
{
    public int MaxCpuPercent { get; set; }        // e.g., 50
    public long MaxMemoryBytes { get; set; }      // e.g., 4GB
    public long MaxDiskBytes { get; set; }        // e.g., 50GB
    public int MaxNetworkMbps { get; set; }       // e.g., 100
    public int[] AllowedPorts { get; set; }       // e.g., [25565, 25566]
}
```

---

## File Operations

### File Transfer Use Cases (Planned)

Agents handle file transfers for:

1. **Game Server Installation**: Download and extract server binaries
2. **Mod Installation**: Download and apply mod files
3. **Configuration Updates**: Apply new server configurations
4. **Backup and Restore**: Upload/download server data
5. **Log Collection**: Upload logs for analysis

### Path Traversal Prevention

**CRITICAL SECURITY REQUIREMENT**: All file paths must be validated to prevent path traversal attacks.

```csharp
// Example path validation (conceptual)
public static class PathValidator
{
    public static bool IsPathSafe(string basePath, string requestedPath)
    {
        // Resolve to absolute paths
        var fullBase = Path.GetFullPath(basePath);
        var fullRequested = Path.GetFullPath(
            Path.Combine(basePath, requestedPath)
        );

        // Ensure the requested path is within the base path
        return fullRequested.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase);
    }
}

// NEVER do this:
var path = Path.Combine(basePath, userInput);  // Vulnerable!
File.ReadAllText(path);

// ALWAYS validate first:
if (!PathValidator.IsPathSafe(basePath, userInput))
{
    throw new SecurityException("Path traversal detected");
}
```

### File Integrity Verification (Planned)

All downloaded files should be verified:

1. **Hash Verification**: Compare SHA-256 hash against expected value
2. **Signature Verification**: Verify cryptographic signature from control plane
3. **Size Verification**: Reject files larger than expected

---

## Configuration

### Configuration Hierarchy (Planned)

Agent configuration follows a precedence order:

1. **Compiled Defaults** - Built into the agent binary
2. **Configuration File** - `agent.json` or `agent.yml`
3. **Environment Variables** - `DHADGAR_AGENT_*`
4. **Command-Line Arguments** - `--option value`

Higher numbers override lower numbers.

### Planned Configuration Options

```json
{
  "agent": {
    "id": "auto-generated-uuid",
    "name": "my-game-server-host",

    "controlPlane": {
      "url": "https://api.meridian.example.com",
      "heartbeatIntervalSeconds": 30,
      "reconnectDelaySeconds": 5,
      "maxReconnectDelaySeconds": 300
    },

    "authentication": {
      "certificatePath": "/etc/dhadgar/agent.crt",
      "keyPath": "/etc/dhadgar/agent.key",
      "caPath": "/etc/dhadgar/ca.crt"
    },

    "servers": {
      "basePath": "/var/dhadgar/servers",
      "maxConcurrent": 10
    },

    "telemetry": {
      "reportIntervalSeconds": 10,
      "batchSize": 100
    },

    "logging": {
      "level": "Information",
      "path": "/var/log/dhadgar/agent.log"
    }
  }
}
```

### Security-Sensitive Configuration

Some configuration values require special handling:

| Setting                  | Security Notes                                              |
| ------------------------ | ----------------------------------------------------------- |
| `authentication.keyPath` | Private key must have restricted permissions (600 on Linux) |
| `controlPlane.url`       | Must be HTTPS; reject HTTP                                  |
| `servers.basePath`       | Agent only operates within this directory tree              |

---

## Extension Points

### Platform-Specific Agents

The `Dhadgar.Agent.Core` library is extended by platform-specific agents:

#### Dhadgar.Agent.Linux

```
Dhadgar.Agent.Linux/
├── Dhadgar.Agent.Linux.csproj
├── Program.cs
├── Hello.cs
└── (planned: Linux-specific implementations)
    ├── LinuxProcessManager.cs      # Uses namespaces, cgroups
    ├── SystemdIntegration.cs       # Runs as systemd service
    └── LinuxCertificateStore.cs    # Uses system keyring
```

**Linux-specific features:**

- systemd service integration
- Linux namespaces and cgroups for isolation
- Native signal handling (SIGTERM, SIGKILL)
- POSIX file permissions

#### Dhadgar.Agent.Windows

```
Dhadgar.Agent.Windows/
├── Dhadgar.Agent.Windows.csproj
├── Program.cs
├── Hello.cs
└── (planned: Windows-specific implementations)
    ├── WindowsProcessManager.cs    # Uses job objects
    ├── WindowsServiceHost.cs       # Runs as Windows Service
    └── WindowsCertificateStore.cs  # Uses Windows cert store
```

**Windows-specific features:**

- Windows Service integration
- Job objects for process isolation
- Windows Event Log integration
- Windows Certificate Store access

### Extensibility Interfaces (Planned)

Platform-specific agents implement these interfaces:

```csharp
// Core defines the interface
public interface IProcessManager
{
    Task<ProcessInfo> StartProcessAsync(ProcessStartConfig config);
    Task StopProcessAsync(Guid processId, TimeSpan gracePeriod);
    Task<ProcessStatus> GetStatusAsync(Guid processId);
}

// Linux agent implements it
public class LinuxProcessManager : IProcessManager
{
    // Uses namespaces, cgroups, etc.
}

// Windows agent implements it
public class WindowsProcessManager : IProcessManager
{
    // Uses job objects, etc.
}
```

---

## Testing

### Test Project Structure

```
tests/Dhadgar.Agent.Core.Tests/
├── Dhadgar.Agent.Core.Tests.csproj
├── HelloWorldTests.cs
└── (planned: comprehensive test suites)
    ├── Communication/
    ├── Commands/
    ├── Security/
    └── Integration/
```

### Current Tests

```csharp
// HelloWorldTests.cs
using Xunit;
using Dhadgar.Agent.Core;

namespace Dhadgar.Agent.Core.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Agent.Core", Hello.Message);
    }
}
```

### Running Tests

```bash
# Run all Agent.Core tests
dotnet test tests/Dhadgar.Agent.Core.Tests

# Run with verbose output
dotnet test tests/Dhadgar.Agent.Core.Tests --verbosity detailed

# Run specific test
dotnet test tests/Dhadgar.Agent.Core.Tests --filter "FullyQualifiedName~HelloWorldTests"
```

### Planned Test Categories

| Category              | Description                                                  |
| --------------------- | ------------------------------------------------------------ |
| **Unit Tests**        | Test individual components in isolation                      |
| **Integration Tests** | Test component interactions                                  |
| **Security Tests**    | Validate security controls (path traversal, injection, etc.) |
| **Performance Tests** | Ensure telemetry doesn't impact host performance             |
| **Fuzz Tests**        | Test robustness against malformed inputs                     |

### Security Testing Guidelines

When implementing agent functionality, security tests are MANDATORY:

```csharp
// Example: Path traversal test (conceptual)
[Fact]
public void PathValidator_RejectsTraversal()
{
    var basePath = "/var/dhadgar/servers";

    // These should all be rejected
    Assert.False(PathValidator.IsPathSafe(basePath, "../etc/passwd"));
    Assert.False(PathValidator.IsPathSafe(basePath, "..\\..\\windows\\system32"));
    Assert.False(PathValidator.IsPathSafe(basePath, "server1/../../etc/passwd"));
    Assert.False(PathValidator.IsPathSafe(basePath, "/etc/passwd"));
}

// Example: Command injection test (conceptual)
[Fact]
public void CommandBuilder_SanitizesInput()
{
    var serverName = "test; rm -rf /";  // Malicious input

    var command = CommandBuilder.BuildStartCommand(serverName);

    // Should not contain shell metacharacters
    Assert.DoesNotContain(";", command);
    Assert.DoesNotContain("|", command);
    Assert.DoesNotContain("$", command);
}
```

---

## Security Considerations

### Code Review Requirements

**All changes to agent code MUST undergo security review.**

The repository includes a specialized agent: `agent-service-guardian` (`.claude/agents/agent-service-guardian.md`) that performs security reviews for agent code changes.

Security review checklist:

1. **Authentication & Authorization**
   - mTLS requirements maintained?
   - Authorization checks for all operations?
   - Certificate validation strict?

2. **Process Isolation**
   - Spawned processes properly isolated?
   - Resource limits enforced?
   - Privileges minimized?

3. **Input Validation**
   - All control plane inputs validated?
   - Command injection prevented?
   - Path traversal prevented?

4. **Data Handling**
   - Minimum data collection?
   - Logs sanitized of secrets?
   - Telemetry proportional?

5. **Failure Modes**
   - Graceful degradation without security compromise?
   - Error messages safe (no internal details)?

### What NOT To Do

**NEVER do these things in agent code:**

```csharp
// NEVER: Execute shell commands with user input
Process.Start("bash", $"-c '{userInput}'");  // Command injection!

// NEVER: Concatenate paths without validation
var path = basePath + "/" + userInput;  // Path traversal!
File.Delete(path);

// NEVER: Trust control plane data without validation
var command = JsonSerializer.Deserialize<Command>(data);
Execute(command);  // Could be tampered!

// NEVER: Log sensitive information
_logger.LogInformation($"Using API key: {apiKey}");  // Secret exposure!

// NEVER: Disable TLS validation
var handler = new HttpClientHandler {
    ServerCertificateCustomValidationCallback = (_, _, _, _) => true  // MITM vulnerability!
};

// NEVER: Store secrets in plain text
File.WriteAllText("/etc/dhadgar/secrets.txt", privateKey);  // Insecure!

// NEVER: Run processes with unnecessary privileges
Process.Start(new ProcessStartInfo {
    FileName = serverBinary,
    UseShellExecute = false,
    // Missing: privilege dropping!
});
```

### Security Best Practices

**ALWAYS follow these practices:**

```csharp
// ALWAYS: Use parameterized process execution
var psi = new ProcessStartInfo {
    FileName = validatedBinaryPath,
    Arguments = string.Join(" ", validatedArgs.Select(EscapeArgument)),
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true
};

// ALWAYS: Validate and sanitize paths
if (!PathValidator.IsPathSafe(basePath, requestedPath))
{
    throw new SecurityException("Invalid path");
}

// ALWAYS: Verify command signatures
if (!CryptoHelper.VerifySignature(command, signature, controlPlanePublicKey))
{
    throw new SecurityException("Invalid command signature");
}

// ALWAYS: Use structured logging without sensitive data
_logger.LogInformation(
    "Processing command {CommandType} for server {ServerId}",
    command.Type,
    command.ServerId
);

// ALWAYS: Enforce TLS with proper validation
var handler = new HttpClientHandler {
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
    {
        return ValidateCertificateAgainstPinnedCA(cert, chain);
    }
};

// ALWAYS: Use secure credential storage
var key = ProtectedData.Protect(keyBytes, null, DataProtectionScope.LocalMachine);
```

---

## Related Documentation

### Project Documentation

| Document                                                      | Description                                         |
| ------------------------------------------------------------- | --------------------------------------------------- |
| [CLAUDE.md](/CLAUDE.md)                                       | Main project instructions and architecture overview |
| [README.md](/README.md)                                       | Project overview and getting started guide          |
| [docs/architecture/](/docs/architecture/)                     | Architecture decisions and design docs              |
| [docs/LINTER_SAST_STRATEGY.md](/docs/LINTER_SAST_STRATEGY.md) | Security scanning implementation plan               |

### Agent-Specific Documentation

| Document                                                                              | Description                        |
| ------------------------------------------------------------------------------------- | ---------------------------------- |
| [Agent.Linux/CLAUDE.md](/src/Agents/Dhadgar.Agent.Linux/CLAUDE.md)                    | Linux agent specifics              |
| [Agent.Windows/CLAUDE.md](/src/Agents/Dhadgar.Agent.Windows/CLAUDE.md)                | Windows agent specifics            |
| [.claude/agents/agent-service-guardian.md](/.claude/agents/agent-service-guardian.md) | Security review agent instructions |

### Related Services

| Service                         | Description                        | Relevance to Agents                  |
| ------------------------------- | ---------------------------------- | ------------------------------------ |
| **Nodes** (`Dhadgar.Nodes`)     | Node inventory and health tracking | Registers agents, receives telemetry |
| **Servers** (`Dhadgar.Servers`) | Game server lifecycle management   | Issues server commands to agents     |
| **Tasks** (`Dhadgar.Tasks`)     | Orchestration and background jobs  | Schedules operations on agents       |
| **Files** (`Dhadgar.Files`)     | File metadata and transfers        | Coordinates file sync with agents    |

### External References

| Resource                                                                                     | Description                             |
| -------------------------------------------------------------------------------------------- | --------------------------------------- |
| [SecurityCodeScan](https://security-code-scan.github.io/)                                    | .NET security analyzer documentation    |
| [OWASP Command Injection](https://owasp.org/www-community/attacks/Command_Injection)         | Command injection prevention guide      |
| [OWASP Path Traversal](https://owasp.org/www-community/attacks/Path_Traversal)               | Path traversal prevention guide         |
| [Linux Namespaces](https://man7.org/linux/man-pages/man7/namespaces.7.html)                  | Linux namespace documentation           |
| [Windows Job Objects](https://docs.microsoft.com/en-us/windows/win32/procthread/job-objects) | Windows process isolation documentation |

---

## Changelog

| Date       | Version | Description                         |
| ---------- | ------- | ----------------------------------- |
| 2026-01-22 | 1.0.0   | Initial comprehensive documentation |

---

## Contributing

When contributing to the Agent Core library:

1. **Read this document thoroughly** - Understand the security model
2. **Follow security best practices** - Never compromise on security
3. **Write tests** - Especially security tests
4. **Request security review** - Use the `agent-service-guardian` agent
5. **Document changes** - Update this README if architecture changes

Remember: **This code runs on customer hardware. Security is not optional.**

---

_This documentation was created to make contributors near subject-matter experts on the Dhadgar.Agent.Core library. If you have questions not covered here, please open an issue or discussion._
