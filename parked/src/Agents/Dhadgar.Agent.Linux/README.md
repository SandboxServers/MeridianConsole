# Dhadgar.Agent.Linux

## Table of Contents

1. [Overview](#overview)
2. [Security Model](#security-model)
3. [Architecture](#architecture)
4. [Installation](#installation)
5. [Configuration](#configuration)
6. [Systemd Integration](#systemd-integration)
7. [Linux-Specific Features](#linux-specific-features)
8. [Process Management](#process-management)
9. [File System and Permissions](#file-system-and-permissions)
10. [Networking](#networking)
11. [Logging](#logging)
12. [Monitoring and Health Checks](#monitoring-and-health-checks)
13. [Troubleshooting](#troubleshooting)
14. [Building](#building)
15. [Testing](#testing)
16. [Development Guidelines](#development-guidelines)
17. [Related Documentation](#related-documentation)

---

## Overview

### What is Dhadgar.Agent.Linux?

Dhadgar.Agent.Linux is the Linux-specific agent component of the Meridian Console (Dhadgar) platform. It is a lightweight daemon that runs on customer-owned Linux servers and acts as the local orchestrator for game server management. The agent is the bridge between the Meridian Console control plane (running in the cloud) and the physical or virtual Linux machines that host game servers.

### Key Responsibilities

The Linux agent is responsible for:

1. **Game Server Lifecycle Management**: Starting, stopping, restarting, and monitoring game server processes on the Linux host
2. **Health Reporting**: Periodically reporting node health, resource utilization (CPU, memory, disk, network), and server status to the control plane
3. **Command Execution**: Receiving and executing orchestration commands from the control plane (provisioning, configuration updates, mod installations)
4. **File Management**: Managing game server files, including downloads, updates, backups, and mod installations
5. **Console Streaming**: Proxying real-time console output from game servers to the control plane via SignalR
6. **Resource Monitoring**: Tracking resource consumption per game server instance for capacity planning and billing
7. **Log Collection**: Aggregating and forwarding game server logs to the observability stack

### Architecture Context

```text
+------------------+     HTTPS/mTLS      +-------------------+
|  Control Plane   | <------------------ |  Dhadgar Agent    |
|  (Cloud)         |     (Outbound Only) |  (Customer Linux) |
+------------------+                     +-------------------+
        |                                        |
        v                                        v
+------------------+                     +-------------------+
| - Gateway        |                     | - Game Server 1   |
| - Nodes Service  |                     | - Game Server 2   |
| - Tasks Service  |                     | - Game Server N   |
| - Files Service  |                     +-------------------+
+------------------+
```

**Critical Design Principle**: The agent makes **outbound-only connections** to the control plane. This means:

- No inbound firewall holes are required on customer networks
- The agent initiates all communication
- Long-lived connections (WebSocket/SignalR) are used for real-time bidirectional communication
- Periodic polling is used for command retrieval when WebSocket is unavailable

### Current Status

> **Important**: This agent is currently in **early scaffolding stage**. The codebase provides the structural foundation for incremental development. Core functionality such as process management, file operations, and control plane communication are planned but not yet implemented.

**Implemented:**

- Basic project structure and build configuration
- Hello world smoke test endpoint
- Security analyzer integration
- Project references to shared contracts

**Planned:**

- mTLS authentication with certificate rotation
- WebSocket/SignalR connection to control plane
- Game server process management
- Resource monitoring and health reporting
- File download and management
- Console output streaming
- Systemd service integration

---

## Security Model

### SECURITY CRITICAL WARNING

This agent runs on **customer-owned hardware** with **elevated privileges**. It has the ability to:

- Execute processes on the host system
- Read and write files
- Manage network configurations (via local firewall tooling such as iptables or nftables)
- Access system resources

**Every code change to this agent MUST be reviewed for security vulnerabilities.**

### Threat Model

#### Assets to Protect

1. **Customer Hardware**: The physical/virtual machine running the agent
2. **Customer Data**: Game server configurations, player data, server logs
3. **Customer Network**: The agent should not become a vector for lateral movement
4. **Control Plane Credentials**: mTLS certificates and authentication tokens

#### Threat Actors

1. **Malicious Control Plane Impersonation**: Attackers attempting to send commands by impersonating the control plane
2. **Man-in-the-Middle**: Network attackers intercepting agent-to-control-plane communication
3. **Local Privilege Escalation**: Attackers with limited access attempting to use the agent for privilege escalation
4. **Malicious Commands**: Injection attacks through command parameters
5. **Path Traversal**: Attempts to access files outside designated directories

### Security Principles

#### 1. Outbound-Only Connections

```
Customer Network                    Internet                      Control Plane
+---------------+              +----------------+              +---------------+
|  Agent        | ----TLS---> | Firewall       | ----TLS---> | Gateway       |
|  (initiates)  |             | (outbound 443) |             | (receives)    |
+---------------+              +----------------+              +---------------+
```

- Agent NEVER listens on any port
- All connections are initiated by the agent
- Eliminates the need for inbound firewall rules
- Reduces attack surface dramatically

#### 2. Mutual TLS (mTLS) Authentication

**Planned Implementation:**

```csharp
// Certificate-based authentication (planned)
public class AgentCertificateAuthentication
{
    // Each agent receives a unique client certificate upon enrollment
    // Certificate is signed by Meridian Console CA
    // Control plane validates agent identity via certificate
    // Certificates have short lifespans and auto-rotate
}
```

**Certificate Lifecycle:**

1. Agent generates a CSR (Certificate Signing Request)
2. CSR is submitted to control plane during enrollment
3. Control plane signs and returns the certificate
4. Agent uses certificate for all subsequent communication
5. Certificates rotate automatically before expiration (typically 24-72 hours)

#### 3. Command Validation

All commands received from the control plane MUST be validated:

```csharp
// Example validation (planned implementation)
public class CommandValidator
{
    public ValidationResult ValidateCommand(AgentCommand command)
    {
        // 1. Verify command signature (HMAC or digital signature)
        // 2. Check command timestamp (prevent replay attacks)
        // 3. Validate command parameters (injection prevention)
        // 4. Verify command is within agent's authorized scope
        // 5. Check resource limits (prevent resource exhaustion)
    }
}
```

#### 4. Sandboxed Execution

Game server processes are isolated from the agent and each other:

**Linux-Specific Isolation Mechanisms:**

- **Separate User Accounts**: Each game server runs under its own unprivileged user
- **cgroups**: Resource limits (CPU, memory) enforced via cgroups v2
- **Namespaces**: Process, network, and mount namespace isolation (optional, configurable)
- **Seccomp**: System call filtering for game server processes
- **AppArmor/SELinux**: Mandatory access control profiles (distribution-dependent)

```bash
# Example: Game server user isolation
# Agent runs as: dhadgar-agent
# Game servers run as: dhadgar-gs-{server-id}

uid=1001(dhadgar-agent) gid=1001(dhadgar-agent) groups=1001(dhadgar-agent)
uid=2001(dhadgar-gs-abc123) gid=2000(dhadgar-gameservers)
uid=2002(dhadgar-gs-def456) gid=2000(dhadgar-gameservers)
```

#### 5. Path Sanitization

All file operations MUST validate paths to prevent traversal attacks:

```csharp
// Example path validation (planned)
public class PathValidator
{
    private readonly string _allowedBasePath = "/var/lib/dhadgar/servers";

    public bool IsPathAllowed(string requestedPath)
    {
        var fullPath = Path.GetFullPath(requestedPath);
        return fullPath.StartsWith(_allowedBasePath, StringComparison.Ordinal);
    }
}
```

**Allowed Directories:**

- `/var/lib/dhadgar/servers/` - Game server data
- `/var/lib/dhadgar/mods/` - Mod files
- `/var/lib/dhadgar/backups/` - Server backups
- `/var/log/dhadgar/` - Agent and server logs
- `/etc/dhadgar/` - Configuration files (read-only for servers)

#### 6. Input Sanitization

All external inputs must be sanitized:

- **Command-line arguments**: Escape shell metacharacters, use parameterized execution
- **File names**: Reject names with path separators, null bytes, or control characters
- **Configuration values**: Validate types, ranges, and formats
- **Network data**: Validate JSON schema, reject oversized payloads

### Security Analyzers

The project includes SecurityCodeScan.VS2019 for static analysis:

```xml
<ItemGroup>
  <!-- Security analyzers - critical for customer-hosted code -->
  <PackageReference Include="SecurityCodeScan.VS2019" />
</ItemGroup>
```

This analyzer detects:

- SQL injection vulnerabilities
- Command injection
- Path traversal
- Cross-site scripting (XSS)
- Insecure cryptographic practices
- XML External Entity (XXE) injection

### Required Security Review Checklist

Before any code change is merged, verify:

- [ ] **Command Injection**: No unsanitized input is passed to shell commands
- [ ] **Path Traversal**: All file paths are validated against allowed directories
- [ ] **Privilege Escalation**: No elevation of privileges beyond necessary scope
- [ ] **Resource Exhaustion**: Resource limits are enforced on all operations
- [ ] **Sensitive Data**: No secrets or credentials are logged or exposed
- [ ] **Authentication**: All control plane communication uses mTLS
- [ ] **Input Validation**: All external inputs are validated and sanitized
- [ ] **Error Handling**: Errors don't leak sensitive information
- [ ] **Cryptography**: Only approved cryptographic algorithms are used

---

## Architecture

### Project Structure

```
src/Agents/Dhadgar.Agent.Linux/
├── Dhadgar.Agent.Linux.csproj    # Project file with security settings
├── Program.cs                     # Application entry point
├── Hello.cs                       # Smoke test class
├── CLAUDE.md                      # AI assistant guidance
└── README.md                      # This file
```

**Planned Structure (to be implemented):**

```
src/Agents/Dhadgar.Agent.Linux/
├── Dhadgar.Agent.Linux.csproj
├── Program.cs
├── appsettings.json
├── appsettings.Production.json
├── Configuration/
│   ├── AgentOptions.cs           # Strongly-typed configuration
│   ├── ConnectionOptions.cs      # Control plane connection settings
│   └── SecurityOptions.cs        # Security-related settings
├── Services/
│   ├── IAgentService.cs          # Main agent service interface
│   ├── AgentService.cs           # Agent service implementation
│   ├── HealthReporter.cs         # Health and metrics reporting
│   └── CommandProcessor.cs       # Command handling
├── Communication/
│   ├── ControlPlaneClient.cs     # HTTP/WebSocket client
│   ├── SignalRConnection.cs      # SignalR hub connection
│   └── CertificateManager.cs     # mTLS certificate handling
├── Process/
│   ├── GameServerManager.cs      # Process lifecycle management
│   ├── ProcessMonitor.cs         # Process health monitoring
│   └── ResourceTracker.cs        # Resource usage tracking
├── FileSystem/
│   ├── FileManager.cs            # File operations
│   ├── PathValidator.cs          # Security path validation
│   └── BackupService.cs          # Backup management
├── Linux/
│   ├── SystemdIntegration.cs     # Systemd notify protocol
│   ├── CgroupManager.cs          # cgroups v2 management
│   ├── UserManager.cs            # User/group management
│   └── SignalHandler.cs          # Unix signal handling
└── Logging/
    ├── StructuredLogger.cs       # Structured logging
    └── LogForwarder.cs           # Log forwarding to control plane
```

### Dependencies

#### Project References

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Shared\Dhadgar.Contracts\Dhadgar.Contracts.csproj" />
  <ProjectReference Include="..\..\Shared\Dhadgar.Shared\Dhadgar.Shared.csproj" />
</ItemGroup>
```

- **Dhadgar.Contracts**: Shared DTOs and message contracts for control plane communication
- **Dhadgar.Shared**: Common utilities and primitives

#### NuGet Packages (Current and Planned)

| Package                             | Purpose                              | Status   |
| ----------------------------------- | ------------------------------------ | -------- |
| SecurityCodeScan.VS2019             | Static security analysis             | Included |
| Microsoft.Extensions.Hosting        | Generic host for background services | Planned  |
| Microsoft.AspNetCore.SignalR.Client | Real-time communication              | Planned  |
| System.Diagnostics.Process          | Process management                   | Built-in |
| OpenTelemetry                       | Observability                        | Planned  |

### Build Configuration

The `.csproj` file includes specific settings for agent security:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>

  <!-- Agent code runs on customer hardware - enforce strict security -->
  <AnalysisMode>All</AnalysisMode>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
</PropertyGroup>
```

- **AnalysisMode=All**: Enables all code analysis rules
- **EnableTrimAnalyzer**: Ensures code is compatible with trimming for smaller deployments
- **EnableSingleFileAnalyzer**: Ensures compatibility with single-file publishing

---

## Installation

### System Requirements

#### Minimum Requirements

- **OS**: Linux kernel 4.15+ (Ubuntu 18.04+, Debian 10+, RHEL/CentOS 8+, or equivalent)
- **Architecture**: x86_64 (amd64)
- **Memory**: 256 MB RAM (agent only, plus game server requirements)
- **Disk**: 100 MB for agent, plus game server storage
- **Network**: Outbound HTTPS (443) connectivity to control plane

#### Recommended Requirements

- **OS**: Ubuntu 22.04 LTS or Debian 12 (for best systemd and cgroups v2 support)
- **Memory**: 512 MB RAM minimum (agent + headroom for game servers)
- **Disk**: SSD storage for game server files
- **cgroups**: cgroups v2 enabled (default on modern distributions)

### Prerequisites

1. **.NET Runtime 10.0** (or self-contained deployment)
2. **systemd** (version 245+ recommended for cgroups v2 delegation)
3. **curl** or **wget** (for installation script)
4. **jq** (optional, for JSON parsing in scripts)

### Installation Methods

#### Method 1: Installation Script (Recommended)

```bash
# Download and run the installation script
curl -fsSL https://install.meridianconsole.com/agent/linux | sudo bash

# Or with specific options
curl -fsSL https://install.meridianconsole.com/agent/linux | sudo bash -s -- \
  --control-plane https://api.meridianconsole.com \
  --enrollment-token YOUR_ENROLLMENT_TOKEN
```

#### Method 2: Manual Installation

```bash
# 1. Create the dhadgar-agent user
sudo useradd --system --shell /usr/sbin/nologin --home-dir /var/lib/dhadgar dhadgar-agent
sudo usermod -aG systemd-journal dhadgar-agent

# 2. Create directory structure
sudo mkdir -p /opt/dhadgar/agent
sudo mkdir -p /var/lib/dhadgar/{servers,mods,backups}
sudo mkdir -p /var/log/dhadgar
sudo mkdir -p /etc/dhadgar

# 3. Download the agent binary
# Option A: Framework-dependent (requires .NET 10 runtime)
sudo curl -fsSL -o /opt/dhadgar/agent/dhadgar-agent.zip \
  https://releases.meridianconsole.com/agent/linux/latest/framework-dependent.zip
sudo unzip /opt/dhadgar/agent/dhadgar-agent.zip -d /opt/dhadgar/agent/

# Option B: Self-contained (no runtime required, larger download)
sudo curl -fsSL -o /opt/dhadgar/agent/dhadgar-agent.zip \
  https://releases.meridianconsole.com/agent/linux/latest/self-contained-linux-x64.zip
sudo unzip /opt/dhadgar/agent/dhadgar-agent.zip -d /opt/dhadgar/agent/

# 4. Set permissions
sudo chown -R dhadgar-agent:dhadgar-agent /opt/dhadgar/agent
sudo chown -R dhadgar-agent:dhadgar-agent /var/lib/dhadgar
sudo chown -R dhadgar-agent:dhadgar-agent /var/log/dhadgar
sudo chmod 755 /opt/dhadgar/agent/Dhadgar.Agent.Linux

# 5. Create configuration
sudo tee /etc/dhadgar/agent.json > /dev/null << 'EOF'
{
  "Agent": {
    "NodeId": null,
    "NodeName": null
  },
  "ControlPlane": {
    "Endpoint": "https://api.meridianconsole.com",
    "EnrollmentToken": "YOUR_ENROLLMENT_TOKEN"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
EOF
sudo chmod 640 /etc/dhadgar/agent.json
sudo chown root:dhadgar-agent /etc/dhadgar/agent.json

# 6. Install systemd service (see Systemd Integration section)

# 7. Start the agent
sudo systemctl enable dhadgar-agent
sudo systemctl start dhadgar-agent
```

#### Method 3: Container Deployment (Planned)

For environments where containerization is preferred, the agent can run in a privileged container:

```bash
# Pull the agent container
docker pull ghcr.io/sandboxservers/dhadgar-agent-linux:latest

# Run with required privileges
docker run -d \
  --name dhadgar-agent \
  --privileged \
  --pid=host \
  --network=host \
  -v /var/lib/dhadgar:/var/lib/dhadgar \
  -v /etc/dhadgar:/etc/dhadgar:ro \
  -v /var/log/dhadgar:/var/log/dhadgar \
  -e AGENT__CONTROLPLANE__ENDPOINT=https://api.meridianconsole.com \
  -e AGENT__CONTROLPLANE__ENROLLMENTTOKEN=YOUR_TOKEN \
  ghcr.io/sandboxservers/dhadgar-agent-linux:latest
```

> **Note**: Container deployment requires `--privileged` for process management capabilities. This is typically used for testing or specific deployment scenarios. Native installation is recommended for production.

### Enrollment Process

After installation, the agent must be enrolled with the control plane:

1. **Generate Enrollment Token** (via Meridian Console UI or API)
   - Navigate to: Organization Settings > Nodes > Add Node
   - Generate a one-time enrollment token
   - Token is valid for 24 hours

2. **Configure the Agent** with the enrollment token (see Configuration section)

3. **Start the Agent**
   - Agent contacts control plane with enrollment token
   - Control plane validates token and organization membership
   - Agent receives:
     - Unique Node ID
     - mTLS client certificate
     - Configuration parameters
   - Agent stores credentials securely

4. **Verify Enrollment**
   - Check agent status: `sudo systemctl status dhadgar-agent`
   - Check logs: `sudo journalctl -u dhadgar-agent -f`
   - Verify in UI: Node appears in Organization > Nodes list

### Uninstallation

```bash
# Stop and disable the service
sudo systemctl stop dhadgar-agent
sudo systemctl disable dhadgar-agent

# Remove systemd service file
sudo rm /etc/systemd/system/dhadgar-agent.service
sudo systemctl daemon-reload

# Remove agent files
sudo rm -rf /opt/dhadgar/agent

# Optional: Remove data and logs (WARNING: destroys game server data)
# sudo rm -rf /var/lib/dhadgar
# sudo rm -rf /var/log/dhadgar

# Optional: Remove configuration (contains enrollment data)
# sudo rm -rf /etc/dhadgar

# Remove the dhadgar-agent user
sudo userdel dhadgar-agent
```

---

## Configuration

### Configuration File Locations

| File                           | Purpose                                       | Permissions                     |
| ------------------------------ | --------------------------------------------- | ------------------------------- |
| `/etc/dhadgar/agent.json`      | Main configuration file                       | 640 root:dhadgar-agent          |
| `/etc/dhadgar/agent.d/*.json`  | Override files (merged in alphabetical order) | 640 root:dhadgar-agent          |
| `/var/lib/dhadgar/agent.state` | Runtime state (node ID, certificates)         | 600 dhadgar-agent:dhadgar-agent |

### Configuration Hierarchy

Configuration is loaded in this order (later sources override earlier):

1. Built-in defaults (compiled into agent)
2. `/etc/dhadgar/agent.json`
3. `/etc/dhadgar/agent.d/*.json` (alphabetically)
4. Environment variables (prefixed with `DHADGAR__`)
5. Command-line arguments

### Configuration Options

#### Complete Configuration Reference

```json
{
  "Agent": {
    "NodeId": null,
    "NodeName": "my-game-server",
    "DataDirectory": "/var/lib/dhadgar",
    "LogDirectory": "/var/log/dhadgar",
    "MaxConcurrentServers": 10,
    "HeartbeatIntervalSeconds": 30,
    "CommandPollIntervalSeconds": 5,
    "ShutdownTimeoutSeconds": 60
  },

  "ControlPlane": {
    "Endpoint": "https://api.meridianconsole.com",
    "EnrollmentToken": null,
    "ConnectionTimeoutSeconds": 30,
    "RetryAttempts": 5,
    "RetryDelaySeconds": 10,
    "UseWebSocket": true,
    "CertificateRefreshHours": 12
  },

  "Security": {
    "AllowedBasePaths": [
      "/var/lib/dhadgar/servers",
      "/var/lib/dhadgar/mods",
      "/var/lib/dhadgar/backups"
    ],
    "MaxFileSize": "10GB",
    "AllowedExecutables": ["/opt/dhadgar/runtimes/*"],
    "EnableCgroups": true,
    "EnableNamespaces": false,
    "EnableSeccomp": true
  },

  "Resources": {
    "DefaultCpuLimit": "2.0",
    "DefaultMemoryLimit": "4GB",
    "DefaultDiskQuota": "50GB",
    "ReservedSystemMemory": "512MB"
  },

  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System": "Warning",
      "Dhadgar.Agent": "Information"
    },
    "EnableConsoleLogging": true,
    "EnableFileLogging": true,
    "MaxLogFileSizeMB": 100,
    "MaxLogFiles": 10,
    "ForwardToControlPlane": true
  },

  "Observability": {
    "EnableMetrics": true,
    "MetricsIntervalSeconds": 60,
    "EnableTracing": false,
    "OtlpEndpoint": null
  }
}
```

#### Configuration Option Details

##### Agent Section

| Option                       | Type   | Default          | Description                                                   |
| ---------------------------- | ------ | ---------------- | ------------------------------------------------------------- |
| `NodeId`                     | string | null             | Unique identifier assigned by control plane during enrollment |
| `NodeName`                   | string | hostname         | Human-readable name for this node                             |
| `DataDirectory`              | string | /var/lib/dhadgar | Root directory for game server data                           |
| `LogDirectory`               | string | /var/log/dhadgar | Directory for agent logs                                      |
| `MaxConcurrentServers`       | int    | 10               | Maximum game servers on this node                             |
| `HeartbeatIntervalSeconds`   | int    | 30               | Interval between health reports                               |
| `CommandPollIntervalSeconds` | int    | 5                | Fallback polling interval (when WebSocket unavailable)        |
| `ShutdownTimeoutSeconds`     | int    | 60               | Grace period for server shutdown                              |

##### ControlPlane Section

| Option                     | Type   | Default  | Description                                          |
| -------------------------- | ------ | -------- | ---------------------------------------------------- |
| `Endpoint`                 | string | required | Control plane API endpoint URL                       |
| `EnrollmentToken`          | string | null     | One-time token for initial enrollment                |
| `ConnectionTimeoutSeconds` | int    | 30       | Connection timeout                                   |
| `RetryAttempts`            | int    | 5        | Number of retry attempts on failure                  |
| `RetryDelaySeconds`        | int    | 10       | Delay between retries (with exponential backoff)     |
| `UseWebSocket`             | bool   | true     | Enable WebSocket/SignalR for real-time communication |
| `CertificateRefreshHours`  | int    | 12       | Hours before certificate expiry to refresh           |

##### Security Section

| Option               | Type     | Default              | Description                                     |
| -------------------- | -------- | -------------------- | ----------------------------------------------- |
| `AllowedBasePaths`   | string[] | [/var/lib/dhadgar/*] | Directories where file operations are permitted |
| `MaxFileSize`        | string   | 10GB                 | Maximum file size for downloads                 |
| `AllowedExecutables` | string[] | []                   | Glob patterns for allowed executables           |
| `EnableCgroups`      | bool     | true                 | Use cgroups v2 for resource limits              |
| `EnableNamespaces`   | bool     | false                | Use Linux namespaces for isolation              |
| `EnableSeccomp`      | bool     | true                 | Enable seccomp filtering for servers            |

##### Resources Section

| Option                 | Type   | Default | Description                      |
| ---------------------- | ------ | ------- | -------------------------------- |
| `DefaultCpuLimit`      | string | 2.0     | Default CPU cores per server     |
| `DefaultMemoryLimit`   | string | 4GB     | Default memory per server        |
| `DefaultDiskQuota`     | string | 50GB    | Default disk quota per server    |
| `ReservedSystemMemory` | string | 512MB   | Memory reserved for system/agent |

### Environment Variable Overrides

Any configuration option can be overridden via environment variables:

```bash
# Format: DHADGAR__SECTION__OPTION
export DHADGAR__Agent__NodeName="production-node-1"
export DHADGAR__ControlPlane__Endpoint="https://api.meridianconsole.com"
export DHADGAR__Logging__LogLevel__Default="Debug"
```

For systemd services, add to the service file or use an environment file:

```bash
# /etc/dhadgar/agent.env
DHADGAR__ControlPlane__Endpoint=https://api.meridianconsole.com
DHADGAR__Logging__LogLevel__Default=Information
```

### Sensitive Configuration

Sensitive values should NOT be stored in plain-text configuration files:

- **Enrollment Token**: Use environment variable or pass via command line
- **Certificates**: Stored in `/var/lib/dhadgar/agent.state` with 600 permissions

```bash
# Pass enrollment token securely
sudo DHADGAR__ControlPlane__EnrollmentToken=xxxxx systemctl start dhadgar-agent

# Or use systemd credentials (systemd 250+)
sudo systemd-creds encrypt - /etc/credstore/dhadgar-enrollment-token <<< "your-token"
```

---

## Systemd Integration

### Service File

Create `/etc/systemd/system/dhadgar-agent.service`:

```ini
[Unit]
Description=Dhadgar Agent for Meridian Console
Documentation=https://docs.meridianconsole.com/agent
After=network-online.target
Wants=network-online.target
StartLimitIntervalSec=300
StartLimitBurst=5

[Service]
Type=notify
User=dhadgar-agent
Group=dhadgar-agent

# Working directory
WorkingDirectory=/opt/dhadgar/agent

# Environment
Environment=DOTNET_ENVIRONMENT=Production
EnvironmentFile=-/etc/dhadgar/agent.env

# Execution
ExecStart=/opt/dhadgar/agent/Dhadgar.Agent.Linux
ExecReload=/bin/kill -HUP $MAINPID

# Restart policy
Restart=always
RestartSec=10
WatchdogSec=300

# Security hardening
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
PrivateTmp=yes
ProtectKernelTunables=yes
ProtectKernelModules=yes
ProtectControlGroups=no
RestrictRealtime=yes
RestrictSUIDSGID=yes

# Allow necessary capabilities for process management
AmbientCapabilities=CAP_SETUID CAP_SETGID CAP_KILL CAP_SYS_RESOURCE CAP_DAC_OVERRIDE
CapabilityBoundingSet=CAP_SETUID CAP_SETGID CAP_KILL CAP_SYS_RESOURCE CAP_DAC_OVERRIDE

# cgroups delegation for resource management
Delegate=yes

# File system access
ReadWritePaths=/var/lib/dhadgar /var/log/dhadgar
ReadOnlyPaths=/etc/dhadgar

# Logging
StandardOutput=journal
StandardError=journal
SyslogIdentifier=dhadgar-agent

# Resource limits for the agent itself
MemoryMax=512M
CPUQuota=50%

[Install]
WantedBy=multi-user.target
```

### Service Management

```bash
# Reload systemd after creating/modifying service file
sudo systemctl daemon-reload

# Enable and start the agent
sudo systemctl enable dhadgar-agent
sudo systemctl start dhadgar-agent

# Check status
sudo systemctl status dhadgar-agent

# View logs
sudo journalctl -u dhadgar-agent -f

# Restart the agent
sudo systemctl restart dhadgar-agent

# Reload configuration (sends SIGHUP)
sudo systemctl reload dhadgar-agent

# Stop the agent (graceful shutdown)
sudo systemctl stop dhadgar-agent
```

### Watchdog Integration

The agent implements the systemd watchdog protocol for health monitoring:

```csharp
// Planned implementation
public class SystemdNotifier : IHostedService
{
    private readonly ILogger<SystemdNotifier> _logger;
    private Timer? _watchdogTimer;

    public Task StartAsync(CancellationToken ct)
    {
        // Notify systemd that we're ready
        SystemdNotify.Ready();

        // Start watchdog timer
        var interval = SystemdNotify.WatchdogInterval;
        if (interval.HasValue)
        {
            _watchdogTimer = new Timer(_ =>
            {
                if (IsHealthy())
                    SystemdNotify.Watchdog();
            }, null, TimeSpan.Zero, interval.Value / 2);
        }

        return Task.CompletedTask;
    }
}
```

If the agent fails to notify the watchdog within `WatchdogSec`, systemd will restart it.

### Journal Integration

The agent writes structured logs to the systemd journal:

```bash
# View all agent logs
journalctl -u dhadgar-agent

# View logs since last boot
journalctl -u dhadgar-agent -b

# Follow logs in real-time
journalctl -u dhadgar-agent -f

# View logs with specific priority
journalctl -u dhadgar-agent -p err

# Export logs as JSON
journalctl -u dhadgar-agent -o json-pretty

# View logs for a specific game server
journalctl -u dhadgar-agent GAMESERVER_ID=abc123
```

### cgroups v2 Delegation

For proper resource management of game servers, cgroups v2 delegation must be enabled:

```bash
# Check if cgroups v2 is enabled
mount | grep cgroup2

# The agent needs Delegate=yes in the service file (already included above)

# Verify delegation
cat /sys/fs/cgroup/system.slice/dhadgar-agent.service/cgroup.controllers
# Should show: cpu memory io pids
```

The agent creates sub-cgroups for each game server:

```
/sys/fs/cgroup/system.slice/dhadgar-agent.service/
├── cgroup.controllers
├── server-abc123/
│   ├── cpu.max
│   ├── memory.max
│   └── pids.max
└── server-def456/
    ├── cpu.max
    ├── memory.max
    └── pids.max
```

---

## Linux-Specific Features

### Comparison with Windows Agent

| Feature            | Linux Agent            | Windows Agent             |
| ------------------ | ---------------------- | ------------------------- |
| Service Management | systemd                | Windows Service           |
| Process Isolation  | cgroups v2, namespaces | Job Objects               |
| User Isolation     | Unix users/groups      | Windows users             |
| Signal Handling    | SIGTERM, SIGHUP, etc.  | Service control events    |
| File Permissions   | POSIX permissions      | Windows ACLs              |
| Log Output         | systemd journal        | Windows Event Log         |
| Resource Limits    | cgroups v2             | Windows Job Object limits |

### Unix Signal Handling

The Linux agent responds to standard Unix signals:

| Signal    | Action                                        |
| --------- | --------------------------------------------- |
| `SIGTERM` | Graceful shutdown (stop all servers, cleanup) |
| `SIGINT`  | Same as SIGTERM                               |
| `SIGHUP`  | Reload configuration                          |
| `SIGUSR1` | Dump current state to logs                    |
| `SIGUSR2` | Trigger immediate health report               |

```csharp
// Planned implementation
public class SignalHandler : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnShutdown);
        PosixSignalRegistration.Create(PosixSignal.SIGHUP, OnReload);
        PosixSignalRegistration.Create(PosixSignal.SIGUSR1, OnDumpState);
        return Task.CompletedTask;
    }
}
```

### Process Management via procfs

The agent uses `/proc` filesystem for efficient process monitoring:

```csharp
// Planned implementation
public class ProcessMonitor
{
    public ProcessStats GetProcessStats(int pid)
    {
        // /proc/{pid}/stat - CPU time, state
        // /proc/{pid}/statm - Memory usage
        // /proc/{pid}/fd - Open file descriptors
        // /proc/{pid}/io - I/O statistics
    }
}
```

### cgroups v2 Resource Management

```csharp
// Planned implementation
public class CgroupManager
{
    private const string CgroupBasePath = "/sys/fs/cgroup/system.slice/dhadgar-agent.service";

    public void CreateServerCgroup(string serverId, ResourceLimits limits)
    {
        var cgroupPath = Path.Combine(CgroupBasePath, $"server-{serverId}");
        Directory.CreateDirectory(cgroupPath);

        // Set CPU limit (e.g., 200% = 2 cores)
        File.WriteAllText(
            Path.Combine(cgroupPath, "cpu.max"),
            $"{limits.CpuMicroseconds} {CpuPeriodMicroseconds}");

        // Set memory limit
        File.WriteAllText(
            Path.Combine(cgroupPath, "memory.max"),
            limits.MemoryBytes.ToString());

        // Set process limit
        File.WriteAllText(
            Path.Combine(cgroupPath, "pids.max"),
            limits.MaxProcesses.ToString());
    }

    public void AddProcessToCgroup(string serverId, int pid)
    {
        var procsFile = Path.Combine(CgroupBasePath, $"server-{serverId}", "cgroup.procs");
        File.AppendAllText(procsFile, pid.ToString());
    }
}
```

### User Namespace Isolation (Optional)

For enhanced security, game servers can run in user namespaces:

```csharp
// Planned implementation
public class NamespaceIsolation
{
    public Process StartIsolatedProcess(string executable, string[] args, ProcessOptions options)
    {
        // Uses unshare(2) or clone(2) with CLONE_NEWUSER, CLONE_NEWNS, CLONE_NEWPID
        // Maps UID/GID ranges for isolation
        // Sets up pivot_root for filesystem isolation
    }
}
```

### AppArmor/SELinux Profiles

The agent includes optional mandatory access control profiles:

**AppArmor Profile** (`/etc/apparmor.d/dhadgar-agent`):

```
#include <tunables/global>

profile dhadgar-agent /opt/dhadgar/agent/Dhadgar.Agent.Linux {
  #include <abstractions/base>
  #include <abstractions/nameservice>

  # Agent binary
  /opt/dhadgar/agent/** r,
  /opt/dhadgar/agent/Dhadgar.Agent.Linux ix,

  # Configuration
  /etc/dhadgar/** r,

  # Data directories
  /var/lib/dhadgar/** rw,
  /var/log/dhadgar/** rw,

  # Process management
  capability setuid,
  capability setgid,
  capability kill,
  capability sys_resource,

  # Network (outbound only)
  network inet stream,
  network inet6 stream,

  # Deny dangerous operations
  deny /etc/passwd w,
  deny /etc/shadow rw,
  deny /root/** rw,
}
```

---

## Process Management

### Game Server Lifecycle

```
┌──────────────────────────────────────────────────────────────────┐
│                     Game Server Lifecycle                        │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌─────────┐    ┌──────────┐    ┌─────────┐    ┌──────────┐    │
│  │Provision│ -> │Installing│ -> │Starting │ -> │ Running  │    │
│  └─────────┘    └──────────┘    └─────────┘    └──────────┘    │
│       │              │              │              │   │        │
│       │              │              │              │   │        │
│       v              v              v              v   v        │
│  ┌─────────┐    ┌──────────┐    ┌─────────┐    ┌──────────┐    │
│  │ Failed  │    │  Failed  │    │ Failed  │    │ Stopping │    │
│  └─────────┘    └──────────┘    └─────────┘    └──────────┘    │
│                                                      │          │
│                                                      v          │
│                                                 ┌──────────┐    │
│                                                 │ Stopped  │    │
│                                                 └──────────┘    │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

### Server States

| State          | Description                             |
| -------------- | --------------------------------------- |
| `Provisioning` | Server resources being allocated        |
| `Installing`   | Game files being downloaded/installed   |
| `Starting`     | Process starting, awaiting health check |
| `Running`      | Process running and healthy             |
| `Stopping`     | Graceful shutdown in progress           |
| `Stopped`      | Process stopped cleanly                 |
| `Failed`       | Process crashed or failed health check  |
| `Updating`     | Applying updates (files, mods, config)  |

### Process Spawning

```csharp
// Planned implementation
public class GameServerManager
{
    public async Task<GameServerProcess> StartServerAsync(
        ServerConfig config,
        CancellationToken ct)
    {
        // 1. Create server user if not exists
        var serverUser = await _userManager.EnsureUserAsync($"dhadgar-gs-{config.Id}");

        // 2. Create cgroup for resource limits
        _cgroupManager.CreateServerCgroup(config.Id, config.ResourceLimits);

        // 3. Prepare working directory
        var workDir = Path.Combine(_options.DataDirectory, "servers", config.Id);
        await _fileManager.PrepareServerDirectoryAsync(workDir, serverUser);

        // 4. Build process start info
        var startInfo = new ProcessStartInfo
        {
            FileName = config.Executable,
            Arguments = config.Arguments,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UserName = serverUser.UserName,
            // Environment variables from config
            EnvironmentVariables = { ... }
        };

        // 5. Start the process
        var process = Process.Start(startInfo);

        // 6. Add to cgroup
        _cgroupManager.AddProcessToCgroup(config.Id, process.Id);

        // 7. Start console streaming
        _consoleStreamer.StartStreaming(config.Id, process);

        // 8. Start health monitoring
        _healthMonitor.StartMonitoring(config.Id, process, config.HealthCheck);

        return new GameServerProcess(process, config);
    }
}
```

### Graceful Shutdown

When stopping a game server, the agent follows this sequence:

1. **Send SIGTERM** to the main process
2. **Wait for configurable timeout** (default: 30 seconds)
3. **Send SIGKILL** if process hasn't exited
4. **Clean up cgroup** and resources
5. **Report status** to control plane

```csharp
// Planned implementation
public async Task StopServerAsync(string serverId, CancellationToken ct)
{
    var server = _servers[serverId];

    // Notify control plane
    await _controlPlane.ReportStateAsync(serverId, ServerState.Stopping);

    // Send SIGTERM
    Mono.Unix.Native.Syscall.kill(server.ProcessId, Mono.Unix.Native.Signum.SIGTERM);

    // Wait for exit with timeout
    var exitTask = server.Process.WaitForExitAsync(ct);
    var completedTask = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(_options.ShutdownTimeoutSeconds), ct));

    if (completedTask != exitTask)
    {
        // Timeout - force kill
        _logger.LogWarning("Server {ServerId} did not stop gracefully, sending SIGKILL", serverId);
        Mono.Unix.Native.Syscall.kill(server.ProcessId, Mono.Unix.Native.Signum.SIGKILL);
        await exitTask;
    }

    // Cleanup
    _cgroupManager.RemoveServerCgroup(serverId);
    await _controlPlane.ReportStateAsync(serverId, ServerState.Stopped);
}
```

### Crash Handling

When a game server crashes unexpectedly:

1. **Detect crash** via process exit monitoring
2. **Capture exit code** and any crash dumps
3. **Report to control plane** with diagnostics
4. **Auto-restart** if configured (with backoff)
5. **Alert** if crash threshold exceeded

```csharp
// Planned implementation
private async Task OnProcessExitedAsync(string serverId, int exitCode)
{
    _logger.LogWarning("Server {ServerId} exited with code {ExitCode}", serverId, exitCode);

    // Report crash to control plane
    await _controlPlane.ReportCrashAsync(serverId, new CrashReport
    {
        ExitCode = exitCode,
        Timestamp = DateTime.UtcNow,
        LastLogs = _logBuffer.GetLast(serverId, 100),
        ResourceUsage = _resourceTracker.GetLastSnapshot(serverId)
    });

    // Check restart policy
    var policy = _servers[serverId].Config.RestartPolicy;
    if (policy.AutoRestart && _crashCounter.ShouldRestart(serverId))
    {
        _logger.LogInformation("Auto-restarting server {ServerId}", serverId);
        await Task.Delay(policy.RestartDelaySeconds * 1000);
        await StartServerAsync(_servers[serverId].Config, CancellationToken.None);
    }
}
```

---

## File System and Permissions

### Directory Structure

```
/
├── opt/
│   └── dhadgar/
│       └── agent/                    # Agent installation
│           ├── Dhadgar.Agent.Linux   # Main executable
│           └── *.dll                 # Dependencies
│
├── var/
│   ├── lib/
│   │   └── dhadgar/
│   │       ├── agent.state           # Agent state (certificates, node ID)
│   │       ├── servers/              # Game server data
│   │       │   ├── {server-id}/
│   │       │   │   ├── game/         # Game files
│   │       │   │   ├── config/       # Server configuration
│   │       │   │   ├── data/         # Persistent data
│   │       │   │   └── logs/         # Server logs
│   │       ├── mods/                 # Shared mod repository
│   │       │   └── {game-type}/
│   │       │       └── {mod-id}/
│   │       └── backups/              # Server backups
│   │           └── {server-id}/
│   │               └── {timestamp}/
│   └── log/
│       └── dhadgar/
│           ├── agent.log             # Agent logs
│           └── servers/
│               └── {server-id}/
│                   ├── stdout.log
│                   └── stderr.log
│
└── etc/
    └── dhadgar/
        ├── agent.json                # Main configuration
        ├── agent.d/                  # Configuration overrides
        └── agent.env                 # Environment variables
```

### User and Group Setup

```bash
# System user for the agent
sudo useradd --system \
    --shell /usr/sbin/nologin \
    --home-dir /var/lib/dhadgar \
    --comment "Dhadgar Agent Service" \
    dhadgar-agent

# Group for game server processes
sudo groupadd --system dhadgar-gameservers

# Add agent to gameservers group (for monitoring)
sudo usermod -aG dhadgar-gameservers dhadgar-agent
```

### Permission Model

| Path                                     | Owner           | Group               | Mode | Purpose                    |
| ---------------------------------------- | --------------- | ------------------- | ---- | -------------------------- |
| `/opt/dhadgar/agent/`                    | root            | root                | 755  | Agent binaries (read-only) |
| `/opt/dhadgar/agent/Dhadgar.Agent.Linux` | root            | root                | 755  | Main executable            |
| `/var/lib/dhadgar/`                      | dhadgar-agent   | dhadgar-agent       | 755  | Agent data root            |
| `/var/lib/dhadgar/agent.state`           | dhadgar-agent   | dhadgar-agent       | 600  | Sensitive state            |
| `/var/lib/dhadgar/servers/`              | dhadgar-agent   | dhadgar-gameservers | 750  | Server data                |
| `/var/lib/dhadgar/servers/{id}/`         | dhadgar-gs-{id} | dhadgar-gameservers | 750  | Individual server          |
| `/var/log/dhadgar/`                      | dhadgar-agent   | dhadgar-agent       | 755  | Log directory              |
| `/etc/dhadgar/`                          | root            | dhadgar-agent       | 750  | Configuration              |
| `/etc/dhadgar/agent.json`                | root            | dhadgar-agent       | 640  | Main config (no secrets)   |

### File Operations Security

All file operations are subject to:

1. **Path Validation**: Must be within allowed base paths
2. **Symlink Resolution**: Symlinks are resolved and validated
3. **Race Condition Prevention**: Atomic operations where possible
4. **Quota Enforcement**: Disk usage limits per server

```csharp
// Planned implementation
public class SecureFileOperations
{
    public async Task WriteFileAsync(string relativePath, byte[] content)
    {
        // Validate path
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, relativePath));
        if (!fullPath.StartsWith(_basePath, StringComparison.Ordinal))
            throw new SecurityException($"Path traversal attempt: {relativePath}");

        // Check symlinks
        var realPath = GetRealPath(fullPath);
        if (!realPath.StartsWith(_basePath, StringComparison.Ordinal))
            throw new SecurityException($"Symlink escape attempt: {relativePath}");

        // Check quota
        if (!await _quotaManager.HasSpaceAsync(_serverId, content.Length))
            throw new QuotaExceededException($"Disk quota exceeded for server {_serverId}");

        // Atomic write
        var tempPath = $"{fullPath}.{Guid.NewGuid()}.tmp";
        await File.WriteAllBytesAsync(tempPath, content);
        File.Move(tempPath, fullPath, overwrite: true);
    }
}
```

---

## Networking

### Outbound Connectivity Requirements

The agent requires outbound connectivity on port 443 (HTTPS/WSS) to:

| Destination                 | Purpose                 | Protocol        |
| --------------------------- | ----------------------- | --------------- |
| `api.meridianconsole.com`   | Control plane API       | HTTPS           |
| `ws.meridianconsole.com`    | Real-time communication | WSS (WebSocket) |
| `files.meridianconsole.com` | File downloads          | HTTPS           |

### Firewall Configuration

#### iptables

```bash
# Allow outbound HTTPS (if using restrictive firewall)
sudo iptables -A OUTPUT -p tcp --dport 443 -j ACCEPT

# Allow established connections
sudo iptables -A INPUT -m state --state ESTABLISHED,RELATED -j ACCEPT
```

#### firewalld

```bash
# Add rule to allow outbound HTTPS
sudo firewall-cmd --permanent --add-service=https
sudo firewall-cmd --reload
```

#### ufw

```bash
# Allow outbound HTTPS
sudo ufw allow out 443/tcp
```

### Game Server Port Management

Game servers typically require inbound ports for player connections. The agent manages local firewall rules directly:

1. **Port Allocation**: Control plane allocates ports from configured ranges
2. **Local Firewall**: Agent manages iptables/nftables rules locally
3. **Port Binding**: Game server binds to allocated port
4. **Cleanup**: Firewall rules removed when server stops

```csharp
// Planned implementation
public class PortManager
{
    public async Task<int> AllocatePortAsync(string serverId, string protocol)
    {
        // Request port allocation from control plane
        var allocation = await _controlPlane.AllocatePortAsync(new PortRequest
        {
            ServerId = serverId,
            Protocol = protocol,
            PreferredRange = _options.PreferredPortRange
        });

        // Manage local firewall rule (iptables/nftables)
        await _localFirewall.OpenPortAsync(new LocalFirewallRule
        {
            Port = allocation.Port,
            Protocol = protocol,
            Direction = "inbound",
            Description = $"Game server {serverId}"
        });

        return allocation.Port;
    }
}
```

### Proxy Support

For environments behind corporate proxies:

```json
{
  "ControlPlane": {
    "Proxy": {
      "Enabled": true,
      "Address": "http://proxy.company.com:8080",
      "Username": "proxyuser",
      "Password": "env:PROXY_PASSWORD",
      "BypassLocal": true
    }
  }
}
```

Or via environment variables:

```bash
export HTTP_PROXY=http://proxy.company.com:8080
export HTTPS_PROXY=http://proxy.company.com:8080
export NO_PROXY=localhost,127.0.0.1
```

---

## Logging

### Log Locations

| Log Type      | Location                                   | Format                     |
| ------------- | ------------------------------------------ | -------------------------- |
| Agent logs    | systemd journal                            | Structured JSON            |
| Agent logs    | `/var/log/dhadgar/agent.log`               | Structured JSON (optional) |
| Server stdout | `/var/log/dhadgar/servers/{id}/stdout.log` | Plain text                 |
| Server stderr | `/var/log/dhadgar/servers/{id}/stderr.log` | Plain text                 |

### Log Levels

| Level         | Description                           | Use Case                  |
| ------------- | ------------------------------------- | ------------------------- |
| `Trace`       | Most verbose, includes internal state | Development debugging     |
| `Debug`       | Detailed diagnostic information       | Troubleshooting           |
| `Information` | General operational events            | Normal operation          |
| `Warning`     | Potential issues                      | Investigation triggers    |
| `Error`       | Failures that need attention          | Alert triggers            |
| `Critical`    | System-wide failures                  | Immediate action required |

### Structured Logging

The agent uses structured logging with consistent fields:

```json
{
  "timestamp": "2026-01-22T10:30:45.123Z",
  "level": "Information",
  "message": "Game server started",
  "properties": {
    "serverId": "abc123",
    "gameType": "minecraft",
    "processId": 12345,
    "port": 25565
  },
  "correlation": {
    "traceId": "0af7651916cd43dd8448eb211c80319c",
    "spanId": "b7ad6b7169203331",
    "requestId": "req-xyz789"
  }
}
```

### Log Rotation

File logs are rotated automatically:

```json
{
  "Logging": {
    "MaxLogFileSizeMB": 100,
    "MaxLogFiles": 10
  }
}
```

This creates files like:

- `agent.log` (current)
- `agent.1.log` (previous)
- `agent.2.log` (older)
- ...up to `agent.9.log`

### systemd Journal Integration

```bash
# View agent logs with structured data
journalctl -u dhadgar-agent --output=json-pretty

# Filter by game server ID
journalctl -u dhadgar-agent GAMESERVER_ID=abc123

# Filter by severity
journalctl -u dhadgar-agent -p warning

# Follow logs in real-time
journalctl -u dhadgar-agent -f

# Export logs for analysis
journalctl -u dhadgar-agent --since "1 hour ago" --output=json > logs.json
```

### Log Forwarding to Control Plane

Optionally, logs can be forwarded to the control plane for centralized viewing:

```json
{
  "Logging": {
    "ForwardToControlPlane": true,
    "ForwardMinLevel": "Warning"
  }
}
```

### Log Sanitization

Sensitive data is automatically redacted from logs:

- Enrollment tokens
- Certificates and keys
- Passwords and secrets
- Authentication headers

```csharp
// Example: Token is automatically masked
_logger.LogInformation("Enrolling with token: {Token}", enrollmentToken);
// Output: "Enrolling with token: [REDACTED]"
```

---

## Monitoring and Health Checks

### Health Check Endpoint

The agent exposes a local health check (for systemd watchdog and local monitoring):

```bash
# Check via file
cat /var/lib/dhadgar/health.json

# Example output
{
  "status": "healthy",
  "timestamp": "2026-01-22T10:30:45Z",
  "uptime": "3d 12h 45m",
  "version": "1.0.0",
  "controlPlane": {
    "connected": true,
    "lastHeartbeat": "2026-01-22T10:30:15Z"
  },
  "servers": {
    "total": 3,
    "running": 2,
    "stopped": 1,
    "failed": 0
  },
  "resources": {
    "cpuUsagePercent": 45.2,
    "memoryUsedMB": 8192,
    "memoryTotalMB": 16384,
    "diskUsedGB": 120,
    "diskTotalGB": 500
  }
}
```

### Metrics Collection

The agent collects and reports metrics to the control plane:

| Metric                         | Type    | Description               |
| ------------------------------ | ------- | ------------------------- |
| `dhadgar_agent_uptime_seconds` | Gauge   | Agent uptime              |
| `dhadgar_servers_total`        | Gauge   | Total servers managed     |
| `dhadgar_servers_running`      | Gauge   | Currently running servers |
| `dhadgar_cpu_usage_percent`    | Gauge   | Host CPU usage            |
| `dhadgar_memory_used_bytes`    | Gauge   | Host memory usage         |
| `dhadgar_disk_used_bytes`      | Gauge   | Disk usage                |
| `dhadgar_network_rx_bytes`     | Counter | Network bytes received    |
| `dhadgar_network_tx_bytes`     | Counter | Network bytes transmitted |
| `dhadgar_server_cpu_seconds`   | Counter | Per-server CPU time       |
| `dhadgar_server_memory_bytes`  | Gauge   | Per-server memory usage   |

### OpenTelemetry Integration (Planned)

For environments with observability infrastructure:

```json
{
  "Observability": {
    "EnableMetrics": true,
    "EnableTracing": true,
    "OtlpEndpoint": "http://otel-collector:4317"
  }
}
```

---

## Troubleshooting

### Common Issues

#### Agent Won't Start

**Symptoms**: `systemctl start dhadgar-agent` fails or times out

**Diagnostic Steps**:

```bash
# Check service status
sudo systemctl status dhadgar-agent

# Check logs for errors
sudo journalctl -u dhadgar-agent --since "5 minutes ago"

# Verify permissions
ls -la /opt/dhadgar/agent/
ls -la /var/lib/dhadgar/
ls -la /etc/dhadgar/

# Run manually for more output
sudo -u dhadgar-agent /opt/dhadgar/agent/Dhadgar.Agent.Linux
```

**Common Causes**:

1. Missing .NET runtime (for framework-dependent deployment)
2. Incorrect file permissions
3. Configuration file syntax error
4. Missing required configuration values

#### Can't Connect to Control Plane

**Symptoms**: Logs show connection timeouts or certificate errors

**Diagnostic Steps**:

```bash
# Test DNS resolution
dig api.meridianconsole.com

# Test connectivity
curl -v https://api.meridianconsole.com/health

# Check if behind proxy
env | grep -i proxy

# Verify certificates
openssl s_client -connect api.meridianconsole.com:443 </dev/null
```

**Common Causes**:

1. Firewall blocking outbound 443
2. DNS resolution failure
3. Corporate proxy not configured
4. Clock skew (certificate validation failure)

#### Game Server Won't Start

**Symptoms**: Server status shows "Failed" after starting

**Diagnostic Steps**:

```bash
# Check server logs
cat /var/log/dhadgar/servers/{server-id}/stdout.log
cat /var/log/dhadgar/servers/{server-id}/stderr.log

# Check process with strace
sudo strace -f -p $(pgrep -f "server-{server-id}")

# Check resource limits
cat /sys/fs/cgroup/system.slice/dhadgar-agent.service/server-{server-id}/memory.max
cat /sys/fs/cgroup/system.slice/dhadgar-agent.service/server-{server-id}/cpu.max

# Check file permissions
ls -la /var/lib/dhadgar/servers/{server-id}/
```

**Common Causes**:

1. Missing game files
2. Insufficient permissions
3. Resource limits too restrictive
4. Port already in use

#### High Resource Usage

**Symptoms**: Agent consuming excessive CPU or memory

**Diagnostic Steps**:

```bash
# Check agent resource usage
top -p $(pgrep Dhadgar.Agent.Linux)

# Check for runaway log collection
du -sh /var/log/dhadgar/

# Check open file descriptors
ls -la /proc/$(pgrep Dhadgar.Agent.Linux)/fd | wc -l

# Check thread count
cat /proc/$(pgrep Dhadgar.Agent.Linux)/status | grep Threads
```

**Common Causes**:

1. Log file not rotating
2. Memory leak (report as bug)
3. Too many game servers
4. Network connection issues causing retry storms

### Debug Mode

Enable debug logging temporarily:

```bash
# Via environment variable
sudo DHADGAR__Logging__LogLevel__Default=Debug systemctl restart dhadgar-agent

# Or edit configuration
sudo jq '.Logging.LogLevel.Default = "Debug"' /etc/dhadgar/agent.json > /tmp/agent.json
sudo mv /tmp/agent.json /etc/dhadgar/agent.json
sudo systemctl restart dhadgar-agent
```

### Support Information Collection

When contacting support, collect:

```bash
# Create support bundle
mkdir -p /tmp/dhadgar-support
journalctl -u dhadgar-agent --since "24 hours ago" > /tmp/dhadgar-support/agent.log
cp /etc/dhadgar/agent.json /tmp/dhadgar-support/ # Remove secrets first!
uname -a > /tmp/dhadgar-support/system-info.txt
cat /etc/os-release >> /tmp/dhadgar-support/system-info.txt
dotnet --info >> /tmp/dhadgar-support/system-info.txt 2>/dev/null || echo "No .NET runtime" >> /tmp/dhadgar-support/system-info.txt
cat /var/lib/dhadgar/health.json >> /tmp/dhadgar-support/health.json
tar -czvf /tmp/dhadgar-support.tar.gz /tmp/dhadgar-support/
```

---

## Building

### Prerequisites

- .NET SDK 10.0.100 (as specified in `global.json`)
- Linux build environment (for native dependencies)

### Build Commands

```bash
# Navigate to repository root
cd /path/to/MeridianConsole

# Restore dependencies
dotnet restore

# Build the Linux agent
dotnet build src/Agents/Dhadgar.Agent.Linux -c Release

# Build self-contained for Linux x64
dotnet publish src/Agents/Dhadgar.Agent.Linux \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -o ./artifacts/agent-linux-x64

# Build framework-dependent (smaller, requires .NET runtime)
dotnet publish src/Agents/Dhadgar.Agent.Linux \
  -c Release \
  -r linux-x64 \
  --self-contained false \
  -o ./artifacts/agent-linux-x64-fdd
```

### Single-File Publishing

For easier distribution:

```bash
dotnet publish src/Agents/Dhadgar.Agent.Linux \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./artifacts/agent-linux-x64-single
```

### Cross-Compilation

Build for Linux from Windows or macOS:

```bash
# From Windows
dotnet publish src/Agents/Dhadgar.Agent.Linux -c Release -r linux-x64 --self-contained true

# From macOS
dotnet publish src/Agents/Dhadgar.Agent.Linux -c Release -r linux-x64 --self-contained true
```

### Build Outputs

| File                      | Description                 | Size (approx) |
| ------------------------- | --------------------------- | ------------- |
| `Dhadgar.Agent.Linux`     | Executable (self-contained) | ~80 MB        |
| `Dhadgar.Agent.Linux.dll` | .NET assembly (FDD)         | ~50 KB        |
| `Dhadgar.Contracts.dll`   | Shared contracts            | ~20 KB        |
| `Dhadgar.Shared.dll`      | Shared utilities            | ~15 KB        |

### CI/CD Pipeline

The agent is built via Azure Pipelines as defined in `azure-pipelines.yml`:

```yaml
- id: Dhadgar.Agent.Linux
  projectPath: src/Agents/Dhadgar.Agent.Linux/Dhadgar.Agent.Linux.csproj
  testProjectPath: tests/Dhadgar.Agent.Linux.Tests/Dhadgar.Agent.Linux.Tests.csproj
  deploy: # Disabled: agent deployment not needed yet
  # agent:
  #   runtimes: 'linux-x64'
  #   storageAccount: meridianconsoleblob
  #   containerName: agent-releases
```

---

## Testing

### Unit Tests

```bash
# Run Linux agent tests
dotnet test tests/Dhadgar.Agent.Linux.Tests

# Run with coverage
dotnet test tests/Dhadgar.Agent.Linux.Tests --collect:"XPlat Code Coverage"

# Run specific test
dotnet test tests/Dhadgar.Agent.Linux.Tests --filter "FullyQualifiedName~HelloWorldTests"
```

### Current Test Coverage

The test project (`tests/Dhadgar.Agent.Linux.Tests/`) currently contains basic smoke tests:

```csharp
public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Agent.Linux", Hello.Message);
    }
}
```

### Planned Test Categories

As the agent is developed, tests will be added in these categories:

1. **Unit Tests**
   - Configuration parsing and validation
   - Path sanitization and security
   - Command validation
   - Resource limit calculations

2. **Integration Tests**
   - Process management (requires Linux)
   - cgroups operations (requires Linux)
   - File system operations
   - Control plane communication (mocked)

3. **Security Tests**
   - Path traversal prevention
   - Command injection prevention
   - Input validation
   - Certificate handling

### Running Tests in Container

For Linux-specific tests from non-Linux environments:

```bash
# Build test image
docker build -f tests/Dhadgar.Agent.Linux.Tests/Dockerfile -t dhadgar-agent-tests .

# Run tests
docker run --rm dhadgar-agent-tests
```

---

## Development Guidelines

### Code Style

Follow the solution-wide code style defined in `.editorconfig`:

- Use C# 12+ features
- Nullable reference types enabled
- Implicit usings enabled
- File-scoped namespaces preferred

### Security Requirements

All code changes MUST be reviewed by the **agent-service-guardian** agent (see `.claude/agents/agent-service-guardian/`):

1. **Mandatory Review**: Any changes to agent code require security review
2. **Threat Modeling**: New features require threat assessment
3. **Input Validation**: All external input must be validated
4. **Logging**: Sensitive data must never be logged

### Adding New Features

1. **Design Review**: Discuss architecture with team
2. **Threat Model**: Identify security implications
3. **Implementation**: Follow existing patterns
4. **Unit Tests**: Achieve >80% coverage for new code
5. **Integration Tests**: Test on actual Linux system
6. **Security Review**: Pass agent-service-guardian review
7. **Documentation**: Update this README

### Debugging

For local development:

```bash
# Run with debug output
DHADGAR__Logging__LogLevel__Default=Debug dotnet run --project src/Agents/Dhadgar.Agent.Linux

# Attach debugger (requires VS Code or JetBrains Rider)
# Set breakpoints and start with F5

# Run with mock control plane
DHADGAR__ControlPlane__Endpoint=http://localhost:5000 dotnet run --project src/Agents/Dhadgar.Agent.Linux
```

---

## Related Documentation

### Repository Documentation

- **[CLAUDE.md](/CLAUDE.md)** - Main repository development guide
- **[Agent Core CLAUDE.md](/src/Agents/Dhadgar.Agent.Core/CLAUDE.md)** - Shared agent library documentation
- **[Windows Agent CLAUDE.md](/src/Agents/Dhadgar.Agent.Windows/CLAUDE.md)** - Windows agent documentation
- **[Development Setup](/docs/DEVELOPMENT_SETUP.md)** - Local development environment setup
- **[Configuration Management](/docs/CONFIGURATION-MANAGEMENT.md)** - Configuration patterns and best practices

### Architecture Documentation

- **[docs/architecture/](/docs/architecture/)** - Architecture decision records
- **[docs/runbooks/](/docs/runbooks/)** - Operational runbooks

### API Documentation

- **[Nodes Service](/src/Dhadgar.Nodes/CLAUDE.md)** - Node inventory and agent management service
- **[Tasks Service](/src/Dhadgar.Tasks/CLAUDE.md)** - Task orchestration service
- **[Files Service](/src/Dhadgar.Files/CLAUDE.md)** - File transfer service

### External Resources

- [.NET on Linux Documentation](https://docs.microsoft.com/en-us/dotnet/core/install/linux)
- [systemd Service Unit](https://www.freedesktop.org/software/systemd/man/systemd.service.html)
- [cgroups v2 Documentation](https://www.kernel.org/doc/html/latest/admin-guide/cgroup-v2.html)
- [Linux Namespaces](https://man7.org/linux/man-pages/man7/namespaces.7.html)

---

## Changelog

### Version 1.0.0 (Planned)

- Initial release
- Basic agent functionality
- Game server lifecycle management
- Control plane integration

### Current: Scaffolding

- Project structure established
- Build configuration with security analyzers
- Basic smoke tests
- Documentation framework

---

## Support

For issues with the Dhadgar Linux Agent:

1. **Check this documentation** for common issues and solutions
2. **Review logs** using the troubleshooting section
3. **Collect support information** using the provided script
4. **Contact support** via the Meridian Console support portal

For security vulnerabilities, please follow responsible disclosure procedures outlined in the repository's SECURITY.md file.
