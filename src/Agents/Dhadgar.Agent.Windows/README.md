# Dhadgar.Agent.Windows

The Windows-specific agent for Meridian Console (Dhadgar) that runs on customer-owned Windows hardware to manage game server processes.

---

## Table of Contents

1. [Overview](#overview)
2. [Security Model](#security-model)
3. [Installation](#installation)
4. [Configuration](#configuration)
5. [Windows Service Integration](#windows-service-integration)
6. [Windows-Specific Features](#windows-specific-features)
7. [Process Management](#process-management)
8. [Permissions](#permissions)
9. [Firewall](#firewall)
10. [Logging](#logging)
11. [Troubleshooting](#troubleshooting)
12. [Building](#building)
13. [Testing](#testing)
14. [Related Documentation](#related-documentation)

---

## Overview

### What This Agent Does

The Dhadgar.Agent.Windows is a customer-hosted component of the Meridian Console platform that:

1. **Manages Game Server Processes**: Spawns, monitors, restarts, and terminates game server processes on Windows systems
2. **Reports Health and Metrics**: Sends telemetry data (CPU, memory, network, process health) back to the control plane
3. **Executes Orchestrated Commands**: Receives and executes commands from the central Meridian Console platform
4. **Handles File Operations**: Manages game server files, mods, configurations, and updates
5. **Isolates Resources**: Ensures game servers run in isolated environments with bounded resource allocation

### Current Implementation Status

**IMPORTANT**: This project is currently in early scaffolding stage. The codebase provides the architectural foundation and project structure, but core functionality is planned for future implementation.

**What exists today:**

- Project structure and build configuration
- Hello world surface area for smoke tests
- Security analyzer integration (SecurityCodeScan)
- References to shared contracts and utilities

**What will be implemented:**

- Agent enrollment and authentication with mTLS
- Heartbeat and health reporting
- Process spawning with Windows Job Objects isolation
- Command execution framework
- File handling with path traversal protection
- Windows Service integration
- Event Log integration

### Architecture Position

```
                    Meridian Console Control Plane (Cloud)
                                   |
                                   | HTTPS/WSS (Outbound only)
                                   |
                    +--------------v--------------+
                    |   Dhadgar.Agent.Windows     |
                    |   (Customer Hardware)       |
                    +--------------+--------------+
                                   |
                    +--------------v--------------+
                    |   Game Server Processes     |
                    |   (Isolated via Job Objects)|
                    +-----------------------------+
```

The Windows agent sits between the control plane and the actual game server processes. It is a **high-trust** component because customers grant it elevated privileges on their Windows machines.

### Comparison with Linux Agent

| Feature           | Windows Agent                    | Linux Agent           |
| ----------------- | -------------------------------- | --------------------- |
| Service Manager   | Windows Service (SCM)            | systemd               |
| Process Isolation | Windows Job Objects              | cgroups + namespaces  |
| Privilege Model   | Windows tokens, integrity levels | capabilities, seccomp |
| Logging           | Windows Event Log                | journald/syslog       |
| Firewall          | Windows Firewall API             | iptables/nftables     |
| File Permissions  | NTFS ACLs                        | POSIX permissions     |

---

## Security Model

### SECURITY CRITICAL

The Windows agent runs on customer-owned hardware with elevated privileges. This makes it the highest-trust component in the Meridian Console architecture. Every code change must be reviewed with extreme scrutiny.

### Trust Boundaries

```
+------------------------------------------------------------------+
|                          INTERNET                                 |
+------------------------------------------------------------------+
                              |
                    (TLS/mTLS encrypted)
                              |
+------------------------------------------------------------------+
|            MERIDIAN CONSOLE CONTROL PLANE                        |
|            - Issues commands                                      |
|            - Receives telemetry                                   |
|            - Manages certificates                                 |
+------------------------------------------------------------------+
                              |
              (OUTBOUND ONLY - no inbound firewall holes)
                              |
+------------------------------------------------------------------+
|            DHADGAR.AGENT.WINDOWS (Customer Machine)              |
|            - High-trust component                                |
|            - Elevated privileges                                 |
|            - Process spawning rights                             |
+------------------------------------------------------------------+
                              |
              (Windows Job Object isolation)
                              |
+------------------------------------------------------------------+
|            GAME SERVER PROCESSES (Sandboxed)                     |
|            - Isolated from each other                            |
|            - Isolated from host system                           |
|            - Resource-bounded                                    |
+------------------------------------------------------------------+
```

### Outbound-Only Connection Model

**Critical Design Principle**: The agent makes OUTBOUND-ONLY connections to the control plane. This is non-negotiable.

**Why this matters:**

- Customers do not need to open inbound firewall ports
- Reduces attack surface dramatically
- The control plane cannot initiate connections to customer hardware
- Commands are polled or delivered via persistent WebSocket connections

**Implementation:**

```csharp
// Planned: Agent initiates connection
var connection = new HubConnectionBuilder()
    .WithUrl("https://api.meridianconsole.com/agent/v1/hub")
    .WithAutomaticReconnect()
    .Build();

await connection.StartAsync(); // Outbound connection
```

### Authentication and Authorization

**Planned implementation:**

1. **Agent Enrollment**
   - One-time enrollment with enrollment token
   - Control plane issues client certificate
   - Certificate stored in Windows Certificate Store

2. **Ongoing Authentication**
   - mTLS for all control plane communication
   - Certificate pinning to prevent MITM attacks
   - Certificate rotation handled automatically

3. **Command Authorization**
   - All commands validated against agent's scope
   - Tenant isolation enforced
   - Audit logging for all operations

### Input Validation Requirements

All input from the control plane MUST be validated before use:

1. **Command Validation**
   - Allowlisted command types only
   - Parameter bounds checking
   - No shell command injection possible

2. **File Path Validation**
   - Absolute path resolution
   - Path traversal prevention (no `..` escape)
   - Jailed to approved directories only

3. **Deserialization Safety**
   - Type-safe deserialization
   - No polymorphic deserialization without type filtering
   - Size limits on payloads

### Data Protection

**Minimum data collection principle:**

- Collect only what is needed for operation
- Health metrics: CPU, memory, disk, network (no content)
- Process metrics: PID, status, resource usage
- No game content unless explicitly requested

**Sensitive data handling:**

- No plaintext secrets in logs
- No stack traces exposed to external systems
- Credentials stored in Windows Credential Manager (planned)

### Security Analyzers

The project includes security analyzers to catch vulnerabilities at compile time:

```xml
<ItemGroup>
  <!-- Security analyzers - critical for customer-hosted code -->
  <PackageReference Include="SecurityCodeScan.VS2019" />
</ItemGroup>
```

Additional build settings enforce strict analysis:

```xml
<PropertyGroup>
  <!-- Agent code runs on customer hardware - enforce strict security -->
  <AnalysisMode>All</AnalysisMode>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
</PropertyGroup>
```

### Code Review Requirements

**MANDATORY**: Use the `agent-service-guardian` specialized agent for security review after ANY code changes to this project. See `.claude/agents/agent-service-guardian.md` for the review checklist.

---

## Installation

### System Requirements

**Operating System:**

- Windows 10 version 1809 or later (build 17763+)
- Windows 11 (all versions)
- Windows Server 2019 or later

**Runtime:**

- .NET 10 Runtime (included with self-contained deployment)
- OR .NET 10 Runtime installed separately (framework-dependent deployment)

**Hardware:**

- Minimum: 2 CPU cores, 2 GB RAM (for agent only)
- Recommended: 4+ CPU cores, 8+ GB RAM (for agent + game servers)
- Storage: Varies by game servers managed

**Network:**

- Outbound HTTPS (port 443) to control plane
- Outbound WebSocket (WSS) to control plane
- Inbound ports as required by game servers

### Installation Methods

#### Method 1: MSI Installer (Planned)

The recommended installation method for production deployments:

```powershell
# Download the installer
Invoke-WebRequest -Uri "https://releases.meridianconsole.com/agent/windows/latest/dhadgar-agent-windows.msi" -OutFile "dhadgar-agent-windows.msi"

# Silent install
msiexec /i dhadgar-agent-windows.msi /quiet /log install.log ENROLLMENT_TOKEN="your-token-here"

# Interactive install
msiexec /i dhadgar-agent-windows.msi
```

The MSI installer will:

1. Install the agent to `C:\Program Files\Meridian Console\Agent\`
2. Create the Windows Service
3. Configure the firewall (outbound rules)
4. Start the enrollment process if token provided

#### Method 2: Manual Installation (Development/Testing)

For development or advanced scenarios:

```powershell
# 1. Create installation directory
New-Item -ItemType Directory -Path "C:\Program Files\Meridian Console\Agent" -Force

# 2. Copy published files
Copy-Item -Path ".\publish\*" -Destination "C:\Program Files\Meridian Console\Agent\" -Recurse

# 3. Create Windows Service
New-Service -Name "DhadgarAgent" `
  -BinaryPathName '"C:\Program Files\Meridian Console\Agent\Dhadgar.Agent.Windows.exe"' `
  -DisplayName "Meridian Console Agent" `
  -Description "Customer-hosted agent for Meridian Console game server management" `
  -StartupType Automatic

# 4. Configure service recovery
sc.exe failure DhadgarAgent reset= 86400 actions= restart/5000/restart/10000/restart/30000

# 5. Start the service
Start-Service -Name "DhadgarAgent"
```

#### Method 3: Self-Contained Deployment

For environments where installing the .NET runtime is not possible:

```powershell
# Build self-contained
dotnet publish src/Agents/Dhadgar.Agent.Windows -c Release -r win-x64 --self-contained true

# The output includes all dependencies, no runtime installation needed
```

### Enrollment Process (Planned)

After installation, the agent must be enrolled with the control plane:

1. **Generate Enrollment Token** (in Meridian Console UI)
   - Navigate to Nodes > Add Node
   - Generate one-time enrollment token
   - Token expires in 24 hours

2. **Run Enrollment**

   ```powershell
   # Via service configuration
   Set-ItemProperty -Path "HKLM:\SOFTWARE\Meridian Console\Agent" `
     -Name "EnrollmentToken" -Value "your-token-here"
   Restart-Service DhadgarAgent

   # OR via command line
   & "C:\Program Files\Meridian Console\Agent\Dhadgar.Agent.Windows.exe" enroll --token "your-token-here"
   ```

3. **Enrollment Completes**
   - Agent presents enrollment token to control plane
   - Control plane issues client certificate
   - Certificate stored in Windows Certificate Store
   - Agent begins normal operation

### Upgrading

The agent supports automatic updates from the control plane (planned):

1. **Automatic Updates**
   - Control plane pushes update notification
   - Agent downloads new version to staging directory
   - Agent verifies signature
   - Agent restarts with new version
   - Rollback on failure

2. **Manual Updates**

   ```powershell
   # Stop the service
   Stop-Service -Name "DhadgarAgent"

   # Replace files
   Copy-Item -Path ".\new-version\*" -Destination "C:\Program Files\Meridian Console\Agent\" -Recurse -Force

   # Start the service
   Start-Service -Name "DhadgarAgent"
   ```

### Uninstallation

```powershell
# Stop and remove the service
Stop-Service -Name "DhadgarAgent" -Force
sc.exe delete DhadgarAgent

# Remove installation directory
Remove-Item -Path "C:\Program Files\Meridian Console\Agent" -Recurse -Force

# Remove configuration
Remove-Item -Path "HKLM:\SOFTWARE\Meridian Console" -Recurse -Force

# Optional: Remove certificates
Get-ChildItem Cert:\LocalMachine\My | Where-Object {$_.Subject -like "*Meridian Console*"} | Remove-Item
```

---

## Configuration

### Configuration File Location

The agent configuration is stored in:

- **Primary**: `C:\ProgramData\Meridian Console\Agent\appsettings.json`
- **Fallback**: `C:\Program Files\Meridian Console\Agent\appsettings.json`
- **User Overrides**: `C:\ProgramData\Meridian Console\Agent\appsettings.local.json`

### Configuration Hierarchy

Configuration is loaded in this order (later overrides earlier):

1. `appsettings.json` (default configuration)
2. `appsettings.{Environment}.json` (environment-specific)
3. `appsettings.local.json` (local overrides, not version-controlled)
4. Environment variables
5. Command-line arguments

### Configuration Options

```json
{
  "Agent": {
    // Unique identifier assigned during enrollment
    "NodeId": null,

    // Control plane connection
    "ControlPlane": {
      "Endpoint": "https://api.meridianconsole.com",
      "HeartbeatIntervalSeconds": 30,
      "ReconnectIntervalSeconds": 5,
      "MaxReconnectAttempts": 0
    },

    // Certificate configuration
    "Certificates": {
      "StoreName": "My",
      "StoreLocation": "LocalMachine",
      "Thumbprint": null,
      "AllowExpiredCertificates": false
    },

    // Process management
    "ProcessManagement": {
      "DefaultWorkingDirectory": "C:\\GameServers",
      "MaxConcurrentProcesses": 10,
      "ProcessStartTimeoutSeconds": 120,
      "GracefulShutdownTimeoutSeconds": 30,
      "ForceKillAfterSeconds": 60
    },

    // Resource limits
    "ResourceLimits": {
      "MaxCpuPercent": 80,
      "MaxMemoryMB": null,
      "MaxDiskUsageGB": null,
      "ReserveMemoryMB": 2048
    },

    // Logging
    "Logging": {
      "LogLevel": "Information",
      "EnableEventLog": true,
      "EventLogSource": "Meridian Console Agent",
      "FileLogPath": "C:\\ProgramData\\Meridian Console\\Agent\\Logs",
      "RetainLogDays": 30
    },

    // File handling
    "FileHandling": {
      "GameServerRoot": "C:\\GameServers",
      "TempDirectory": "C:\\ProgramData\\Meridian Console\\Agent\\Temp",
      "MaxUploadSizeMB": 1024,
      "MaxDownloadSizeMB": 10240,
      "AllowedExtensions": [
        ".zip",
        ".exe",
        ".dll",
        ".bat",
        ".cfg",
        ".json",
        ".xml",
        ".txt"
      ]
    },

    // Security
    "Security": {
      "RequireSignedCommands": true,
      "CommandTimeoutSeconds": 300,
      "MaxCommandsPerMinute": 60,
      "EnableAuditLogging": true
    }
  }
}
```

### Environment Variable Overrides

Configuration can be overridden via environment variables using double underscores (`__`) as section separators:

```powershell
# Example: Override control plane endpoint
$env:Agent__ControlPlane__Endpoint = "https://staging-api.meridianconsole.com"

# Example: Override log level
$env:Agent__Logging__LogLevel = "Debug"

# Example: Override process limits
$env:Agent__ProcessManagement__MaxConcurrentProcesses = "20"
```

### Registry Configuration

Critical configuration is stored in the Windows Registry:

```
HKEY_LOCAL_MACHINE\SOFTWARE\Meridian Console\Agent
  - NodeId (REG_SZ): Assigned node identifier
  - EnrollmentToken (REG_SZ): One-time enrollment token (cleared after use)
  - CertificateThumbprint (REG_SZ): Current client certificate thumbprint
  - LastHeartbeat (REG_QWORD): Last successful heartbeat timestamp
```

### Configuration Validation

On startup, the agent validates configuration:

1. **Required fields** must be present
2. **Paths** must be valid and accessible
3. **Limits** must be within acceptable ranges
4. **Certificates** must be valid and not expired

If validation fails, the agent logs the error and exits with code 1.

---

## Windows Service Integration

### Service Architecture

The Windows agent runs as a Windows Service managed by the Service Control Manager (SCM):

```
+------------------+
| Service Control  |
| Manager (SCM)    |
+--------+---------+
         |
         | Start/Stop/Status
         |
+--------v---------+
| Dhadgar.Agent    |
| .Windows.exe     |
+--------+---------+
         |
         | Manages
         |
+--------v---------+
| Game Server      |
| Processes        |
+------------------+
```

### Service Properties

| Property     | Value                                                             |
| ------------ | ----------------------------------------------------------------- |
| Service Name | `DhadgarAgent`                                                    |
| Display Name | `Meridian Console Agent`                                          |
| Description  | Customer-hosted agent for Meridian Console game server management |
| Start Type   | Automatic (Delayed Start recommended)                             |
| Account      | `Local System` (default) or dedicated service account             |
| Dependencies | None (or specific network services if required)                   |

### Service Implementation (Planned)

The agent uses the .NET Generic Host with Windows Service support:

```csharp
// Planned implementation in Program.cs
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Configure as Windows Service
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "DhadgarAgent";
        });

        // Add agent services
        builder.Services.AddHostedService<AgentWorker>();
        builder.Services.AddSingleton<IProcessManager, WindowsProcessManager>();
        builder.Services.AddSingleton<IControlPlaneClient, ControlPlaneClient>();

        var host = builder.Build();
        await host.RunAsync();
    }
}
```

### Service Lifecycle

1. **OnStart** (Service starting)
   - Load configuration
   - Initialize logging
   - Connect to control plane
   - Report "ready" status

2. **Running** (Normal operation)
   - Maintain heartbeat
   - Process commands
   - Manage game server processes
   - Report metrics

3. **OnStop** (Service stopping)
   - Stop accepting new commands
   - Gracefully stop game server processes
   - Disconnect from control plane
   - Flush logs

4. **Recovery** (After failure)
   - Automatic restart (configured via SCM)
   - Exponential backoff (5s, 10s, 30s)
   - Alert control plane after 3 failures

### Service Control Commands

```powershell
# Start the service
Start-Service -Name "DhadgarAgent"

# Stop the service (graceful)
Stop-Service -Name "DhadgarAgent"

# Restart the service
Restart-Service -Name "DhadgarAgent"

# Query status
Get-Service -Name "DhadgarAgent"

# View service configuration
Get-CimInstance -ClassName Win32_Service -Filter "Name='DhadgarAgent'"
```

### Service Account Options

**Option 1: Local System (Default)**

- Full access to local system
- Simplest to configure
- Highest privileges

**Option 2: Network Service**

- Network access as machine account
- Reduced local privileges
- Good for joined-domain scenarios

**Option 3: Dedicated Service Account (Recommended for Production)**

> **Security Note**: Generate a strong, unique password using a password manager or generator. Never use example passwords in production.

```powershell
# Create service account (replace <StrongPasswordHere> with a generated password)
New-LocalUser -Name "svc_dhadgar" -Password (ConvertTo-SecureString "<StrongPasswordHere>" -AsPlainText -Force) -PasswordNeverExpires -UserMayNotChangePassword

# Grant "Log on as a service" right
# (Use Local Security Policy or secedit)

# Set service to run as this account (replace <StrongPasswordHere> with the same password)
sc.exe config DhadgarAgent obj= ".\svc_dhadgar" password= "<StrongPasswordHere>"
```

---

## Windows-Specific Features

### Windows Job Objects

The agent uses Windows Job Objects to isolate game server processes:

```csharp
// Planned implementation
public class WindowsProcessManager : IProcessManager
{
    public async Task<ProcessInfo> SpawnProcessAsync(ProcessRequest request)
    {
        // Create Job Object for isolation
        using var job = CreateJobObject(null, $"Dhadgar_Server_{request.ServerId}");

        // Set resource limits
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_PROCESS_MEMORY |
                             JOB_OBJECT_LIMIT_JOB_TIME |
                             JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                ProcessMemoryLimit = (UIntPtr)(request.MaxMemoryMB * 1024 * 1024)
            }
        };
        SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref limits, ...);

        // Start process in job
        var process = Process.Start(request.StartInfo);
        AssignProcessToJobObject(job, process.Handle);

        return new ProcessInfo(process, job);
    }
}
```

**Benefits of Job Objects:**

- Memory limits enforced by OS
- CPU time limits (optional)
- Process tree management (kill children on exit)
- I/O rate limiting (Windows 8+)
- Network limits (Windows 10+)

### Windows Integrity Levels

Game server processes run at lower integrity levels than the agent:

| Component             | Integrity Level  |
| --------------------- | ---------------- |
| Agent Service         | High (or System) |
| Game Server Processes | Medium           |
| Untrusted Downloads   | Low              |

```csharp
// Planned: Lower process integrity
var tokenHandle = LowerProcessIntegrity(processHandle, IntegrityLevel.Medium);
```

### Windows Credential Manager (Planned)

Sensitive credentials are stored in Windows Credential Manager:

```csharp
// Store credential
CredWrite(new CREDENTIAL
{
    Type = CRED_TYPE_GENERIC,
    TargetName = "MeridianConsole/Agent/ControlPlane",
    CredentialBlob = Encoding.UTF8.GetBytes(apiKey),
    Persist = CRED_PERSIST_LOCAL_MACHINE
});

// Retrieve credential
CredRead("MeridianConsole/Agent/ControlPlane", CRED_TYPE_GENERIC, 0, out credential);
```

### Windows Certificate Store

Agent certificates are stored in the Windows Certificate Store:

- **Location**: `LocalMachine\My`
- **Subject**: `CN=Meridian Console Agent, O={CustomerOrg}`
- **Usage**: Client Authentication

```powershell
# View agent certificates
Get-ChildItem Cert:\LocalMachine\My | Where-Object {$_.Subject -like "*Meridian Console*"}

# Export certificate (backup)
Export-Certificate -Cert (Get-ChildItem Cert:\LocalMachine\My\THUMBPRINT) -FilePath "agent-backup.cer"
```

### Windows Performance Counters (Planned)

The agent exposes Windows Performance Counters for monitoring:

```
\Meridian Console Agent\Active Game Servers
\Meridian Console Agent\Commands Processed/sec
\Meridian Console Agent\Heartbeat Latency (ms)
\Meridian Console Agent\Memory Usage (MB)
```

View with Performance Monitor:

```powershell
# Add counters
Get-Counter -ListSet "Meridian Console Agent" | Get-Counter
```

### Windows Event Tracing (ETW)

The agent publishes ETW events for advanced diagnostics:

```powershell
# Enable ETW tracing
logman create trace DhadgarAgentTrace -p "Meridian-Console-Agent" -o dhadgar-trace.etl -ets

# Stop tracing
logman stop DhadgarAgentTrace -ets

# Analyze with Windows Performance Analyzer or tracerpt
tracerpt dhadgar-trace.etl -o dhadgar-trace.txt
```

---

## Process Management

### Process Lifecycle

```
                    +-------------------+
                    |    PROVISIONING   |
                    |  (Download files) |
                    +--------+----------+
                             |
                    +--------v----------+
                    |     STARTING      |
                    |  (Spawn process)  |
                    +--------+----------+
                             |
                    +--------v----------+
                    |      RUNNING      |
                    | (Monitor health)  |
                    +--------+----------+
                             |
            +----------------+----------------+
            |                |                |
    +-------v------+  +------v-------+  +----v-------+
    |   STOPPING   |  |   CRASHED    |  |  UPDATING  |
    | (Graceful)   |  | (Unexpected) |  | (Hot swap) |
    +-------+------+  +------+-------+  +----+-------+
            |                |                |
            +----------------+----------------+
                             |
                    +--------v----------+
                    |      STOPPED      |
                    +-------------------+
```

### Spawning Game Servers

```csharp
// Planned implementation
public async Task<GameServerProcess> SpawnGameServerAsync(SpawnRequest request)
{
    // Validate request
    ValidateSpawnRequest(request);

    // Prepare working directory
    var workDir = Path.Combine(
        _config.GameServerRoot,
        request.OrganizationId.ToString(),
        request.ServerId.ToString());

    Directory.CreateDirectory(workDir);

    // Prepare process start info
    var startInfo = new ProcessStartInfo
    {
        FileName = Path.Combine(workDir, request.Executable),
        Arguments = request.Arguments,
        WorkingDirectory = workDir,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        CreateNoWindow = true
    };

    // Set environment variables
    foreach (var env in request.Environment)
    {
        startInfo.Environment[env.Key] = env.Value;
    }

    // Spawn with isolation
    var process = await SpawnIsolatedProcessAsync(startInfo, request.ResourceLimits);

    // Track process
    _activeProcesses[request.ServerId] = process;

    return process;
}
```

### Resource Isolation

Each game server process is isolated via Windows Job Objects:

| Resource      | Enforcement                  |
| ------------- | ---------------------------- |
| Memory        | Hard limit via Job Object    |
| CPU Time      | Soft limit with throttling   |
| Process Count | Limit child processes        |
| I/O Rate      | Rate limiting (Win 8+)       |
| Network       | Bandwidth limiting (Win 10+) |

### Process Monitoring

The agent continuously monitors game server health:

1. **Process State**
   - Running/Stopped/Crashed
   - Exit code on termination
   - Crash dump on unexpected exit

2. **Resource Usage**
   - CPU utilization
   - Memory working set
   - Handle count
   - Thread count

3. **Application Health**
   - Query port (game-specific)
   - Log analysis (optional)
   - Response time

### Graceful Shutdown

When stopping a game server:

1. **Signal Shutdown**
   - Send CTRL+C (console apps)
   - Send WM_CLOSE (GUI apps)
   - Call game-specific shutdown command

2. **Wait for Graceful Exit**
   - Wait for configured timeout (default: 30s)
   - Allow save game/world data

3. **Force Termination**
   - TerminateProcess if graceful fails
   - Kill entire Job Object (all child processes)

4. **Cleanup**
   - Release Job Object
   - Archive logs
   - Report final status

### Console I/O Handling

The agent captures and manages game server console I/O:

```csharp
// Planned: Console capture
process.OutputDataReceived += (sender, args) =>
{
    if (!string.IsNullOrEmpty(args.Data))
    {
        _consoleBuffer.Enqueue(new ConsoleMessage
        {
            ServerId = serverId,
            Timestamp = DateTime.UtcNow,
            Stream = ConsoleStream.StdOut,
            Data = args.Data
        });

        // Forward to control plane if client connected
        if (_consoleClients.TryGetValue(serverId, out var clients))
        {
            foreach (var client in clients)
            {
                client.SendAsync(args.Data);
            }
        }
    }
};
```

---

## Permissions

### Required Windows Permissions

The agent service account requires these permissions:

| Permission                   | Reason                      |
| ---------------------------- | --------------------------- |
| Log on as a service          | Run as Windows Service      |
| Create a token object        | Process token manipulation  |
| Increase scheduling priority | Process priority management |
| Adjust memory quotas         | Memory limit enforcement    |
| Replace process-level token  | Process isolation           |

### File System Permissions

| Path                                       | Permission   | Reason              |
| ------------------------------------------ | ------------ | ------------------- |
| `C:\Program Files\Meridian Console\Agent\` | Read         | Agent binaries      |
| `C:\ProgramData\Meridian Console\Agent\`   | Full Control | Configuration, logs |
| `C:\GameServers\`                          | Full Control | Game server files   |
| Temp directory                             | Full Control | File operations     |

### Registry Permissions

| Key                                                   | Permission   | Reason                |
| ----------------------------------------------------- | ------------ | --------------------- |
| `HKLM\SOFTWARE\Meridian Console`                      | Full Control | Agent configuration   |
| `HKLM\SYSTEM\CurrentControlSet\Services\DhadgarAgent` | Read         | Service configuration |

### Certificate Store Permissions

The service account needs access to the LocalMachine certificate store:

```powershell
# Grant certificate access (run as Administrator)
$cert = Get-ChildItem Cert:\LocalMachine\My\THUMBPRINT
$keyPath = $cert.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName
$keyFullPath = "$env:ProgramData\Microsoft\Crypto\RSA\MachineKeys\$keyPath"

$acl = Get-Acl $keyFullPath
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "NT SERVICE\DhadgarAgent",
    "Read",
    "Allow")
$acl.AddAccessRule($rule)
Set-Acl $keyFullPath $acl
```

### UAC Considerations

**Installation**: Requires elevation (Administrator)

- MSI installer requests elevation via manifest
- Manual installation must be run elevated

**Runtime**: Does not require elevation

- Service runs with configured account
- Game server processes run at lower integrity

**Management**: Some operations require elevation

- Service start/stop: Administrator
- Configuration changes: Administrator
- Log viewing: No elevation needed

### Least Privilege Configuration

For maximum security, configure a dedicated service account:

```powershell
# Create dedicated account
New-LocalUser -Name "svc_dhadgar" -Description "Meridian Console Agent Service" -PasswordNeverExpires

# Add to required groups (none by default)
# Service account should NOT be in Administrators

# Grant required privileges via Local Security Policy:
# - Log on as a service
# - Adjust memory quotas for a process
# - Replace a process level token

# Configure service
sc.exe config DhadgarAgent obj= ".\svc_dhadgar" password= "..."

# Set file permissions
icacls "C:\ProgramData\Meridian Console" /grant "svc_dhadgar:(OI)(CI)F"
icacls "C:\GameServers" /grant "svc_dhadgar:(OI)(CI)F"
```

---

## Firewall

### Windows Firewall Rules

The agent requires the following firewall rules:

**Outbound Rules (Required):**

| Rule Name                          | Direction | Port | Protocol | Purpose                    |
| ---------------------------------- | --------- | ---- | -------- | -------------------------- |
| Meridian Agent - Control Plane     | Outbound  | 443  | TCP      | HTTPS to control plane     |
| Meridian Agent - Control Plane WSS | Outbound  | 443  | TCP      | WebSocket to control plane |

**Inbound Rules (Optional, for game servers):**

Game-specific inbound rules are dynamically managed based on game server configuration:

| Example          | Direction | Port        | Protocol |
| ---------------- | --------- | ----------- | -------- |
| Minecraft Server | Inbound   | 25565       | TCP      |
| Valheim Server   | Inbound   | 2456-2458   | UDP      |
| ARK Server       | Inbound   | 7777, 27015 | UDP      |

### Automatic Firewall Management (Planned)

The agent automatically manages Windows Firewall rules:

```csharp
// Planned implementation
public async Task OpenPortAsync(int port, Protocol protocol, string description)
{
    var policy = (INetFwPolicy2)Activator.CreateInstance(
        Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!);

    var rule = (INetFwRule)Activator.CreateInstance(
        Type.GetTypeFromProgID("HNetCfg.FWRule")!);

    rule.Name = $"Meridian Console - {description}";
    rule.Description = $"Auto-managed by Meridian Console Agent for {description}";
    rule.Action = NET_FW_ACTION_.NET_FW_ACTION_ALLOW;
    rule.Direction = NET_FW_RULE_DIRECTION_.NET_FW_RULE_DIR_IN;
    rule.Enabled = true;
    rule.InterfaceTypes = "All";
    rule.LocalPorts = port.ToString();
    rule.Protocol = protocol == Protocol.TCP ? 6 : 17;

    policy.Rules.Add(rule);

    _managedRules[port] = rule.Name;
}

public async Task ClosePortAsync(int port)
{
    if (_managedRules.TryGetValue(port, out var ruleName))
    {
        var policy = (INetFwPolicy2)Activator.CreateInstance(
            Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!);

        policy.Rules.Remove(ruleName);
        _managedRules.Remove(port);
    }
}
```

### Firewall Rule Verification

```powershell
# List all Meridian Console firewall rules
Get-NetFirewallRule | Where-Object {$_.DisplayName -like "Meridian Console*"} |
  Format-Table DisplayName, Direction, Action, Enabled

# Verify outbound connectivity
Test-NetConnection -ComputerName api.meridianconsole.com -Port 443 -InformationLevel Detailed
```

### Firewall Best Practices

1. **Use Specific Rules**: Create rules per-game-server, not blanket allow rules
2. **Scope to Interface**: Limit rules to specific network interfaces if possible
3. **Audit Regularly**: Review and clean up unused rules
4. **Log Blocked Traffic**: Enable logging for blocked connections during troubleshooting

---

## Logging

### Log Destinations

The agent writes logs to multiple destinations:

1. **Windows Event Log**
   - Source: `Meridian Console Agent`
   - Log: `Application`
   - Used for: Critical events, service lifecycle

2. **File Logs**
   - Location: `C:\ProgramData\Meridian Console\Agent\Logs\`
   - Format: Structured JSON
   - Rotation: Daily, 30-day retention

3. **Console Output** (when not running as service)
   - Useful for debugging
   - Includes full detail

4. **OpenTelemetry** (Planned)
   - Traces and logs sent to control plane
   - Correlated with distributed traces

### Event Log Integration

The agent registers as an Event Log source:

```powershell
# Register event source (done during installation)
New-EventLog -LogName Application -Source "Meridian Console Agent"

# View agent events
Get-EventLog -LogName Application -Source "Meridian Console Agent" -Newest 50

# Filter by level
Get-EventLog -LogName Application -Source "Meridian Console Agent" -EntryType Error,Warning
```

**Event IDs:**

| ID Range  | Category                    |
| --------- | --------------------------- |
| 1000-1099 | Service lifecycle           |
| 2000-2099 | Control plane communication |
| 3000-3099 | Process management          |
| 4000-4099 | File operations             |
| 5000-5099 | Security events             |
| 9000-9099 | Errors                      |

### Log File Format

File logs use structured JSON format:

```json
{
  "timestamp": "2026-01-22T14:30:00.123Z",
  "level": "Information",
  "category": "ProcessManager",
  "message": "Game server started successfully",
  "correlationId": "abc123-def456",
  "properties": {
    "serverId": "srv-123",
    "processId": 12345,
    "executable": "minecraft_server.exe",
    "memoryLimitMB": 4096
  }
}
```

### Log Rotation

File logs are automatically rotated:

- **Trigger**: Daily at midnight or when file exceeds 100MB
- **Naming**: `agent-YYYY-MM-DD.log`, `agent-YYYY-MM-DD.1.log`, etc.
- **Retention**: 30 days (configurable)
- **Compression**: Old logs compressed with gzip

### Viewing Logs

```powershell
# View recent file logs
Get-Content "C:\ProgramData\Meridian Console\Agent\Logs\agent-$(Get-Date -Format 'yyyy-MM-dd').log" -Tail 100

# Search logs for errors
Select-String -Path "C:\ProgramData\Meridian Console\Agent\Logs\*.log" -Pattern '"level":"Error"'

# Parse JSON logs
Get-Content "C:\ProgramData\Meridian Console\Agent\Logs\agent-*.log" |
  ConvertFrom-Json |
  Where-Object {$_.level -eq "Error"} |
  Select-Object timestamp, message, properties
```

### Log Levels

| Level       | When Used                               |
| ----------- | --------------------------------------- |
| Trace       | Detailed debugging (performance impact) |
| Debug       | Debugging information                   |
| Information | Normal operations                       |
| Warning     | Recoverable issues                      |
| Error       | Failures requiring attention            |
| Critical    | Service-affecting failures              |

Configure log level in `appsettings.json`:

```json
{
  "Agent": {
    "Logging": {
      "LogLevel": "Information"
    }
  }
}
```

---

## Troubleshooting

### Common Issues

#### Agent Service Won't Start

**Symptoms:**

- Service fails to start
- Event log shows startup errors

**Diagnostics:**

```powershell
# Check service status
Get-Service -Name "DhadgarAgent" | Select-Object *

# Check Event Log
Get-EventLog -LogName Application -Source "Meridian Console Agent" -Newest 20

# Try running interactively
& "C:\Program Files\Meridian Console\Agent\Dhadgar.Agent.Windows.exe"
```

**Common Causes:**

1. Missing configuration file
2. Invalid configuration values
3. Certificate not found or expired
4. Insufficient permissions
5. Port conflicts

#### Cannot Connect to Control Plane

**Symptoms:**

- Agent starts but shows "disconnected" status
- Heartbeat failures in logs

**Diagnostics:**

```powershell
# Test network connectivity
Test-NetConnection -ComputerName api.meridianconsole.com -Port 443

# Check TLS
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri "https://api.meridianconsole.com/healthz" -UseBasicParsing

# Check proxy settings
netsh winhttp show proxy
```

**Common Causes:**

1. Firewall blocking outbound 443
2. Proxy not configured
3. Certificate validation failure
4. DNS resolution failure
5. Control plane endpoint incorrect

#### Game Server Fails to Start

**Symptoms:**

- Process spawns but immediately exits
- Error in agent logs about process failure

**Diagnostics:**

```powershell
# Check game server logs
Get-Content "C:\GameServers\{org}\{server}\logs\*.log" -Tail 100

# Check process exit code
# (Agent logs will contain exit code)

# Try running game server manually
Set-Location "C:\GameServers\{org}\{server}"
.\server.exe [arguments]
```

**Common Causes:**

1. Missing game files
2. Incorrect command arguments
3. Port already in use
4. Insufficient memory
5. Missing dependencies (VC++ Redistributable, etc.)

#### High Memory Usage

**Symptoms:**

- Agent process using excessive memory
- System performance degradation

**Diagnostics:**

```powershell
# Check agent memory
Get-Process -Name "Dhadgar.Agent.Windows" | Select-Object WorkingSet64, PrivateMemorySize64

# Check job object limits
# (Requires Windows SDK tools)
```

**Common Causes:**

1. Console buffer not being flushed
2. Large number of active game servers
3. Memory leak (report as bug)
4. Job object limits not enforced

#### Certificate Issues

**Symptoms:**

- Authentication failures
- "Certificate not found" errors
- "Certificate expired" errors

**Diagnostics:**

```powershell
# List agent certificates
Get-ChildItem Cert:\LocalMachine\My |
  Where-Object {$_.Subject -like "*Meridian Console*"} |
  Select-Object Thumbprint, Subject, NotBefore, NotAfter

# Check certificate validity
$cert = Get-ChildItem Cert:\LocalMachine\My\THUMBPRINT
$cert.Verify()

# Check private key access
$cert.HasPrivateKey
```

**Common Causes:**

1. Certificate expired
2. Private key permissions incorrect
3. Certificate revoked
4. Clock skew (system time wrong)

### Diagnostic Commands

```powershell
# Full system diagnostic
function Get-DhadgarAgentDiagnostics {
    Write-Host "=== Service Status ===" -ForegroundColor Cyan
    Get-Service -Name "DhadgarAgent" | Format-List *

    Write-Host "`n=== Recent Event Log Entries ===" -ForegroundColor Cyan
    Get-EventLog -LogName Application -Source "Meridian Console Agent" -Newest 10 |
      Format-Table TimeGenerated, EntryType, Message -Wrap

    Write-Host "`n=== Network Connectivity ===" -ForegroundColor Cyan
    Test-NetConnection -ComputerName api.meridianconsole.com -Port 443

    Write-Host "`n=== Certificates ===" -ForegroundColor Cyan
    Get-ChildItem Cert:\LocalMachine\My |
      Where-Object {$_.Subject -like "*Meridian Console*"} |
      Format-Table Thumbprint, Subject, NotAfter

    Write-Host "`n=== Firewall Rules ===" -ForegroundColor Cyan
    Get-NetFirewallRule |
      Where-Object {$_.DisplayName -like "Meridian*"} |
      Format-Table DisplayName, Direction, Action, Enabled

    Write-Host "`n=== Active Processes ===" -ForegroundColor Cyan
    Get-Process | Where-Object {$_.Path -like "*GameServers*"} |
      Format-Table Id, ProcessName, WorkingSet64, CPU
}

Get-DhadgarAgentDiagnostics
```

### Getting Help

1. **Check Logs**: File logs contain detailed error information
2. **Event Log**: Windows Event Log has critical errors
3. **Documentation**: Check related docs listed below
4. **Support**: Contact Meridian Console support with:
   - Agent version
   - Windows version
   - Error messages
   - Diagnostic output

---

## Building

### Prerequisites

- .NET SDK 10.0.100 (pinned in `global.json`)
- Visual Studio 2022 or VS Code (optional)
- Windows 10 SDK (for Windows-specific APIs)

### Building from Source

```powershell
# Navigate to repository root
cd C:\Source\MeridianConsole

# Restore dependencies
dotnet restore

# Build the Windows agent
dotnet build src/Agents/Dhadgar.Agent.Windows

# Build in Release mode
dotnet build src/Agents/Dhadgar.Agent.Windows -c Release

# Build self-contained executable
dotnet publish src/Agents/Dhadgar.Agent.Windows -c Release -r win-x64 --self-contained true -o .\publish\windows-agent

# Build single-file executable
dotnet publish src/Agents/Dhadgar.Agent.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish\windows-agent-single
```

### Project Structure

```
src/Agents/Dhadgar.Agent.Windows/
├── Dhadgar.Agent.Windows.csproj   # Project file
├── Program.cs                      # Entry point
├── Hello.cs                        # Smoke test surface
├── CLAUDE.md                       # AI development guidance
├── README.md                       # This file
├── appsettings.json                # Default configuration (planned)
├── Services/                       # Service implementations (planned)
│   ├── AgentWorker.cs              # Main hosted service
│   └── WindowsProcessManager.cs    # Process management
├── Commands/                       # Command handlers (planned)
├── Handlers/                       # Message handlers (planned)
└── Platform/                       # Windows-specific code (planned)
    ├── JobObjects.cs               # Job Object wrappers
    ├── ServiceControl.cs           # SCM integration
    └── EventLog.cs                 # Event Log integration
```

### Build Configuration

The project file includes security-focused build settings:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>Dhadgar.Agent.Windows</AssemblyName>
    <RootNamespace>Dhadgar.Agent.Windows</RootNamespace>
    <OutputType>Exe</OutputType>

    <!-- Agent code runs on customer hardware - enforce strict security -->
    <AnalysisMode>All</AnalysisMode>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>

    <!-- CA1303: Agents don't need localization for console output -->
    <NoWarn>$(NoWarn);CA1303</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Shared\Dhadgar.Contracts\Dhadgar.Contracts.csproj" />
    <ProjectReference Include="..\..\Shared\Dhadgar.Shared\Dhadgar.Shared.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- Security analyzers - critical for customer-hosted code -->
    <PackageReference Include="SecurityCodeScan.VS2019" />
  </ItemGroup>
</Project>
```

### Continuous Integration

The agent is built as part of the main Azure Pipelines CI:

```yaml
# In azure-pipelines.yml
- task: DotNetCoreCLI@2
  displayName: "Build Windows Agent"
  inputs:
    command: "build"
    projects: "src/Agents/Dhadgar.Agent.Windows/Dhadgar.Agent.Windows.csproj"
    arguments: "-c Release"

- task: DotNetCoreCLI@2
  displayName: "Publish Windows Agent"
  inputs:
    command: "publish"
    projects: "src/Agents/Dhadgar.Agent.Windows/Dhadgar.Agent.Windows.csproj"
    arguments: "-c Release -r win-x64 --self-contained true -o $(Build.ArtifactStagingDirectory)/windows-agent"
```

---

## Testing

### Test Project

The Windows agent has a corresponding test project:

```
tests/Dhadgar.Agent.Windows.Tests/
├── Dhadgar.Agent.Windows.Tests.csproj
└── HelloWorldTests.cs
```

### Running Tests

```powershell
# Run all agent tests
dotnet test tests/Dhadgar.Agent.Windows.Tests

# Run with detailed output
dotnet test tests/Dhadgar.Agent.Windows.Tests -v detailed

# Run specific test
dotnet test tests/Dhadgar.Agent.Windows.Tests --filter "FullyQualifiedName~HelloWorldTests"

# Run with coverage (requires coverlet)
dotnet test tests/Dhadgar.Agent.Windows.Tests --collect:"XPlat Code Coverage"
```

### Current Tests

```csharp
// HelloWorldTests.cs
public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Agent.Windows", Hello.Message);
    }
}
```

### Planned Test Categories

1. **Unit Tests**
   - Configuration loading
   - Input validation
   - Path sanitization
   - Command parsing

2. **Integration Tests**
   - Process spawning
   - Job Object isolation
   - File operations
   - Event Log writing

3. **Security Tests**
   - Path traversal prevention
   - Command injection prevention
   - Certificate validation
   - Permission checks

4. **Platform Tests** (Windows-specific)
   - Windows Service lifecycle
   - Job Object resource limits
   - Windows Firewall management
   - Certificate Store operations

### Test Infrastructure Requirements

Some tests require elevated permissions or specific Windows features:

```powershell
# Run tests that require admin
# Must run PowerShell/VS as Administrator
dotnet test tests/Dhadgar.Agent.Windows.Tests --filter "Category=RequiresAdmin"

# Skip integration tests in CI (no Windows Service available)
dotnet test tests/Dhadgar.Agent.Windows.Tests --filter "Category!=Integration"
```

---

## Related Documentation

### Repository Documentation

- **[Root CLAUDE.md](/CLAUDE.md)**: Main project guidance and conventions
- **[Root README.md](/README.md)**: Project overview and quick start
- **[Agent Core CLAUDE.md](/src/Agents/Dhadgar.Agent.Core/CLAUDE.md)**: Core agent library guidance
- **[Linux Agent README](/src/Agents/Dhadgar.Agent.Linux/README.md)**: Linux agent documentation (peer project)

### Architecture Documentation

- **[docs/architecture/README.md](/docs/architecture/README.md)**: Architecture decisions
- **[CONFIGURATION-MANAGEMENT.md](/docs/CONFIGURATION-MANAGEMENT.md)**: Configuration patterns
- **[DEVELOPMENT_SETUP.md](/docs/DEVELOPMENT_SETUP.md)**: Development environment setup

### Security Documentation

- **[.claude/agents/agent-service-guardian.md](/.claude/agents/agent-service-guardian.md)**: Security review agent
- **[.claude/agents/security-architect.md](/.claude/agents/security-architect.md)**: Security architecture guidance

### External References

- **[Windows Service Hosting in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service)**: Official Microsoft documentation
- **[Windows Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)**: Process isolation on Windows
- **[.NET 10 Documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10)**: Latest .NET features
- **[Windows Firewall API](https://learn.microsoft.com/en-us/windows/win32/fwp/windows-firewall-start-page)**: Firewall management

### Shared Libraries

- **[Dhadgar.Contracts](/src/Shared/Dhadgar.Contracts/)**: DTOs and message contracts
- **[Dhadgar.Shared](/src/Shared/Dhadgar.Shared/)**: Utilities and primitives

### Test Documentation

- **[tests/Dhadgar.Agent.Windows.Tests/](/tests/Dhadgar.Agent.Windows.Tests/)**: Test project

---

## Version History

| Version | Date        | Changes                                       |
| ------- | ----------- | --------------------------------------------- |
| 0.1.0   | Scaffolding | Initial project structure, security analyzers |

---

## Contributors

This project is part of the Meridian Console (Dhadgar) platform developed by the Meridian Console team.

**IMPORTANT**: All code changes to agent projects MUST be reviewed using the `agent-service-guardian` specialized agent. Agent code runs on customer hardware with elevated privileges and represents the highest trust boundary in the platform.

---

_This documentation was generated with assistance from Claude Code. Last updated: January 2026_
