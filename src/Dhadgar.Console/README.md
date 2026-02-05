# Dhadgar.Console

![Status: Alpha](https://img.shields.io/badge/Status-Alpha-yellow)

The **Console Service** provides real-time, bidirectional communication between the Meridian Console control plane and game servers running on customer nodes. Using ASP.NET Core SignalR, this service enables live console output streaming, command execution, and interactive terminal sessions for game server management.

## Table of Contents

- [Overview](#overview)
- [Current Status](#current-status)
- [Tech Stack](#tech-stack)
- [Quick Start](#quick-start)
  - [Prerequisites](#prerequisites)
  - [Running Locally](#running-locally)
  - [Verify the Service](#verify-the-service)
- [Architecture](#architecture)
  - [Data Flow](#data-flow)
  - [Session Affinity](#session-affinity)
- [SignalR Hubs](#signalr-hubs)
  - [ConsoleHub (Current Implementation)](#consolehub-current-implementation)
  - [Planned Hub Methods](#planned-hub-methods)
  - [Client Events](#client-events)
  - [Server Events](#server-events)
- [API Endpoints](#api-endpoints)
  - [Current Endpoints](#current-endpoints)
  - [Planned REST Endpoints](#planned-rest-endpoints)
- [Planned Features](#planned-features)
  - [Real-Time Console Output Streaming](#real-time-console-output-streaming)
  - [Command Execution](#command-execution)
  - [Console History](#console-history)
  - [Multiple Console Sessions](#multiple-console-sessions)
  - [Access Control](#access-control)
  - [Console Recording and Playback](#console-recording-and-playback)
- [Integration Points](#integration-points)
  - [Agent Integration](#agent-integration)
  - [Servers Service Integration](#servers-service-integration)
  - [Identity Service Integration](#identity-service-integration)
  - [Gateway Routing](#gateway-routing)
- [Configuration](#configuration)
  - [Application Settings](#application-settings)
  - [OpenTelemetry Configuration](#opentelemetry-configuration)
  - [SignalR Configuration](#signalr-configuration)
  - [Planned Configuration Options](#planned-configuration-options)
- [Testing](#testing)
  - [Running Existing Tests](#running-existing-tests)
  - [Testing SignalR Connections](#testing-signalr-connections)
  - [Integration Testing with WebApplicationFactory](#integration-testing-with-webapplicationfactory)
  - [Planned Test Coverage](#planned-test-coverage)
- [Docker](#docker)
  - [Development Dockerfile](#development-dockerfile)
  - [Pipeline Dockerfile](#pipeline-dockerfile)
  - [Runtime Configuration](#runtime-configuration)
- [Security Considerations](#security-considerations)
- [Observability](#observability)
- [Troubleshooting](#troubleshooting)
- [Related Documentation](#related-documentation)

---

## Overview

The Console service is the real-time communication backbone for interactive game server management. It bridges the gap between users viewing console output in the Panel UI and the actual game server processes running on customer-owned nodes via agents.

**Key Responsibilities**:

- **Live Output Streaming**: Real-time streaming of game server console output (stdout/stderr) to connected clients via WebSocket (SignalR)
- **Command Execution**: Send commands to game servers through the agent and receive responses
- **Session Management**: Track multiple concurrent console sessions per server and per user
- **History Management**: Store and retrieve console output history for debugging and auditing
- **Access Control**: Enforce permissions on who can view or execute commands

**What This Service is NOT**:

- This is NOT where game servers run - servers run on customer nodes via agents
- This is NOT a terminal emulator - it relays data between agents and clients
- This is NOT responsible for server lifecycle - that is the Servers service

---

## Current Status

**Status**: Alpha - Core functionality implemented including SignalR hub, session management, command dispatch, and console history.

### What Exists Today

| Component             | Status      | Description                                                           |
| --------------------- | ----------- | --------------------------------------------------------------------- |
| `Program.cs`          | Implemented | ASP.NET Core application with SignalR, Redis, MassTransit configured  |
| `ConsoleHub.cs`       | Implemented | SignalR hub with JoinServer, LeaveServer, SendCommand methods         |
| Session Management    | Implemented | Redis-backed console session tracking with distributed locking        |
| Console History       | Implemented | Hot/cold storage pattern with Redis (recent) and PostgreSQL (archive) |
| Command Dispatch      | Implemented | Command execution via MassTransit with audit logging                  |
| Health Endpoints      | Implemented | `/healthz`, `/livez`, `/readyz` via ServiceDefaults                   |
| Swagger UI            | Implemented | OpenAPI documentation at `/swagger`                                   |
| OpenTelemetry         | Implemented | Tracing, metrics, and logging configured                              |
| Docker Support        | Implemented | Both development and pipeline Dockerfiles                             |
| Test Project          | Implemented | Unit and integration tests with WebApplicationFactory                 |

### What is Planned

| Feature                    | Priority | Description                                         |
| -------------------------- | -------- | --------------------------------------------------- |
| Agent Response Consumers   | High     | MassTransit consumers for agent output events       |
| Rate Limiting              | Medium   | Prevent command flooding per user/server            |
| Recording/Playback         | Low      | Record and playback console sessions                |
| Advanced Access Control    | Low      | Fine-grained command permissions                    |

---

## Tech Stack

| Technology    | Version  | Purpose                                        |
| ------------- | -------- | ---------------------------------------------- |
| .NET          | 10.0     | Runtime framework                              |
| ASP.NET Core  | 10.0     | Web framework                                  |
| SignalR       | Built-in | Real-time bidirectional communication          |
| OpenTelemetry | 1.14.0   | Distributed tracing and metrics                |
| MassTransit   | 8.3.6    | Message bus (for agent communication, planned) |
| RabbitMQ      | -        | Message transport (planned)                    |
| Swashbuckle   | Latest   | Swagger/OpenAPI documentation                  |

**Project Dependencies**:

```xml
<ProjectReference Include="..\Shared\Dhadgar.Contracts\Dhadgar.Contracts.csproj" />
<ProjectReference Include="..\Shared\Dhadgar.Shared\Dhadgar.Shared.csproj" />
<ProjectReference Include="..\Shared\Dhadgar.Messaging\Dhadgar.Messaging.csproj" />
<ProjectReference Include="..\Shared\Dhadgar.ServiceDefaults\Dhadgar.ServiceDefaults.csproj" />
```

---

## Quick Start

### Prerequisites

- .NET SDK 10.0.100+ (pinned in `global.json`)
- Docker (for local infrastructure, optional but recommended)
- Node.js 20+ (if running Panel UI for end-to-end testing)

### Running Locally

```bash
# 1. Start local infrastructure (optional, for full stack)
docker compose -f deploy/compose/docker-compose.dev.yml up -d

# 2. Run the Console service directly
dotnet run --project src/Dhadgar.Console

# 3. Or with hot reload for development
dotnet watch --project src/Dhadgar.Console
```

The service runs on `http://localhost:5070` by default.

### Verify the Service

```bash
# Health check
curl http://localhost:5070/healthz
# Returns: {"service":"Dhadgar.Console","status":"ok","timestamp":"...","checks":{...}}

# Hello endpoint
curl http://localhost:5070/hello
# Returns: Hello from Dhadgar.Console

# Root endpoint
curl http://localhost:5070/
# Returns: {"service":"Dhadgar.Console","message":"Hello from Dhadgar.Console"}

# Swagger UI
open http://localhost:5070/swagger
```

### Test SignalR Connection

You can test the SignalR hub using a simple JavaScript client:

```javascript
// In browser console or Node.js with @microsoft/signalr package
const signalR = require("@microsoft/signalr");

const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5070/hubs/console")
  .build();

connection.on("pong", () => {
  console.log("Received pong!");
});

await connection.start();
await connection.invoke("Ping");
// Console logs: "Received pong!"
```

---

## Architecture

### Data Flow

The Console service acts as a relay between three parties:

```text
+------------------+      SignalR/WebSocket      +------------------+
|   Panel UI       | <-------------------------> |  Console Service |
| (Browser Client) |                             | (This Service)   |
+------------------+                             +------------------+
                                                        ^
                                                        |
                                                        | MassTransit/RabbitMQ
                                                        | (Planned)
                                                        v
+------------------+      gRPC/WebSocket         +------------------+
|   Game Server    | <-------------------------> |     Agent        |
| (Customer Node)  |                             | (Customer Node)  |
+------------------+                             +------------------+
```

**Flow for Console Output**:

1. Game server process writes to stdout/stderr
2. Agent captures output and sends to Console service via message queue
3. Console service broadcasts to all connected SignalR clients watching that server
4. Panel UI displays output in real-time terminal component

**Flow for Command Execution**:

1. User types command in Panel UI terminal
2. Panel sends command to Console service via SignalR
3. Console service validates permissions and queues command via MassTransit
4. Agent receives command and writes to game server stdin
5. Game server processes command; output flows back through output pipeline

### Session Affinity

Because SignalR maintains persistent WebSocket connections, the Gateway is configured with session affinity for the console cluster:

```json
{
  "console": {
    "SessionAffinity": {
      "Enabled": true,
      "Policy": "Cookie",
      "FailurePolicy": "Redistribute",
      "AffinityKeyName": ".Dhadgar.Console.Affinity",
      "Cookie": {
        "HttpOnly": true,
        "SameSite": "Lax",
        "SecurePolicy": "Always",
        "Expiration": "01:00:00",
        "IsEssential": true
      }
    }
  }
}
```

This ensures that once a client connects to a Console service instance, subsequent requests are routed to the same instance, maintaining the SignalR connection state.

---

## SignalR Hubs

### ConsoleHub (Current Implementation)

**Location**: `src/Dhadgar.Console/Hubs/ConsoleHub.cs`

**Endpoint**: `/hubs/console`

The current implementation is a minimal scaffold:

```csharp
public sealed class ConsoleHub : Hub
{
    public Task Ping() => Clients.Caller.SendAsync("pong");
}
```

This provides:

- Basic connectivity testing between clients and the hub
- Foundation for adding real console functionality

### Planned Hub Methods

The following hub methods are planned for implementation:

#### Server Methods (Client-to-Server)

| Method           | Parameters                        | Description                                       |
| ---------------- | --------------------------------- | ------------------------------------------------- |
| `JoinServer`     | `serverId: Guid`                  | Subscribe to console output for a specific server |
| `LeaveServer`    | `serverId: Guid`                  | Unsubscribe from server console output            |
| `SendCommand`    | `serverId: Guid, command: string` | Execute a command on the game server              |
| `RequestHistory` | `serverId: Guid, lineCount: int`  | Request recent console history                    |
| `Ping`           | -                                 | Connection keepalive (implemented)                |

#### Example Planned Implementation

```csharp
public sealed class ConsoleHub : Hub
{
    private readonly IConsoleSessionManager _sessions;
    private readonly ICommandDispatcher _commands;
    private readonly IConsoleHistoryService _history;
    private readonly IAuthorizationService _authz;

    public async Task JoinServer(Guid serverId)
    {
        // Verify user has permission to view this server's console
        var userId = Context.User?.FindFirst("sub")?.Value;
        var orgId = Context.User?.FindFirst("org_id")?.Value;

        if (!await _authz.CanViewConsole(userId, orgId, serverId))
            throw new HubException("Access denied");

        // Join SignalR group for this server
        await Groups.AddToGroupAsync(Context.ConnectionId, $"server:{serverId}");

        // Track session
        await _sessions.AddSession(Context.ConnectionId, serverId, userId);

        // Notify client of successful join
        await Clients.Caller.SendAsync("JoinedServer", serverId);

        // Send recent history
        var history = await _history.GetRecentLines(serverId, 100);
        await Clients.Caller.SendAsync("ConsoleHistory", history);
    }

    public async Task SendCommand(Guid serverId, string command)
    {
        var userId = Context.User?.FindFirst("sub")?.Value;
        var orgId = Context.User?.FindFirst("org_id")?.Value;

        if (!await _authz.CanExecuteCommands(userId, orgId, serverId))
            throw new HubException("Command execution not permitted");

        // Validate and sanitize command
        command = SanitizeCommand(command);

        // Dispatch to agent via message queue
        await _commands.DispatchAsync(new ExecuteCommandRequest
        {
            ServerId = serverId,
            Command = command,
            RequestedBy = userId,
            RequestedAt = DateTimeOffset.UtcNow
        });

        // Echo command to all viewers (prefix with "> " to indicate input)
        await Clients.Group($"server:{serverId}")
            .SendAsync("ConsoleOutput", $"> {command}");
    }
}
```

### Client Events

Events sent from server to clients:

| Event                 | Payload             | Description                                    |
| --------------------- | ------------------- | ---------------------------------------------- |
| `pong`                | -                   | Response to Ping (implemented)                 |
| `ConsoleOutput`       | `string line`       | Single line of console output                  |
| `ConsoleBatch`        | `string[] lines`    | Batch of console lines (for efficiency)        |
| `ConsoleHistory`      | `ConsoleHistoryDto` | Recent console history                         |
| `JoinedServer`        | `Guid serverId`     | Confirmation of server join                    |
| `LeftServer`          | `Guid serverId`     | Confirmation of server leave                   |
| `ServerStatusChanged` | `ServerStatusDto`   | Server status update (started/stopped/crashed) |
| `CommandResult`       | `CommandResultDto`  | Result of command execution                    |
| `Error`               | `ErrorDto`          | Error notification                             |

### Server Events

Events received from clients:

| Event                   | Handler                               | Description          |
| ----------------------- | ------------------------------------- | -------------------- |
| Invoke `Ping`           | `Ping()`                              | Keepalive ping       |
| Invoke `JoinServer`     | `JoinServer(serverId)`                | Join server console  |
| Invoke `LeaveServer`    | `LeaveServer(serverId)`               | Leave server console |
| Invoke `SendCommand`    | `SendCommand(serverId, command)`      | Execute command      |
| Invoke `RequestHistory` | `RequestHistory(serverId, lineCount)` | Request history      |

---

## API Endpoints

### Current Endpoints

| Method    | Path            | Auth         | Description                          |
| --------- | --------------- | ------------ | ------------------------------------ |
| GET       | `/`             | Anonymous    | Service info                         |
| GET       | `/hello`        | Anonymous    | Returns "Hello from Dhadgar.Console" |
| GET       | `/healthz`      | Anonymous    | Overall health status                |
| GET       | `/livez`        | Anonymous    | Liveness probe                       |
| GET       | `/readyz`       | Anonymous    | Readiness probe                      |
| WebSocket | `/hubs/console` | TenantScoped | SignalR hub endpoint                 |

### Planned REST Endpoints

REST endpoints for operations that don't require real-time communication:

| Method | Path                                 | Auth         | Description                    |
| ------ | ------------------------------------ | ------------ | ------------------------------ |
| GET    | `/servers/{serverId}/history`        | TenantScoped | Get paginated console history  |
| GET    | `/servers/{serverId}/history/search` | TenantScoped | Search console history         |
| GET    | `/servers/{serverId}/sessions`       | TenantScoped | List active console sessions   |
| POST   | `/servers/{serverId}/command`        | TenantScoped | Execute command (non-realtime) |
| GET    | `/servers/{serverId}/recordings`     | TenantScoped | List console recordings        |
| GET    | `/recordings/{recordingId}`          | TenantScoped | Download recording             |
| DELETE | `/recordings/{recordingId}`          | TenantScoped | Delete recording               |

---

## Planned Features

### Real-Time Console Output Streaming

The primary feature of this service is streaming game server console output to connected clients in real-time.

**Implementation Plan**:

1. **Agent Output Capture**: Agents capture stdout/stderr from game server processes
2. **Message Queue Delivery**: Output is sent to Console service via MassTransit/RabbitMQ
3. **SignalR Broadcast**: Console service broadcasts to SignalR groups (one group per server)
4. **Client Rendering**: Panel UI renders output in a terminal-like component

**Performance Considerations**:

- Batch output lines to reduce network overhead
- Implement backpressure when clients can't keep up
- Configurable output buffer size per server
- Rate limiting on output to prevent flooding

**Message Contract** (planned):

```csharp
public record ConsoleOutputReceived
{
    public Guid ServerId { get; init; }
    public Guid NodeId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string[] Lines { get; init; } = [];
    public ConsoleOutputType OutputType { get; init; } // StdOut, StdErr
}
```

### Command Execution

Allow users to execute commands on game servers through the console interface.

**Security Measures**:

- Permission check before every command
- Command sanitization to prevent injection
- Audit logging of all commands
- Rate limiting per user/server
- Optional command allowlist per game type

**Command Flow**:

```text
User Input -> Hub.SendCommand() -> Permission Check -> MassTransit Publish
                                                              |
                                                              v
                                                    Agent Consumer
                                                              |
                                                              v
                                                    Game Server Stdin
```

### Console History

Persist console output for later retrieval.

**Storage Strategy**:

- Recent history in Redis (last N lines per server, configurable)
- Long-term history in PostgreSQL (with retention policy)
- Optional export to blob storage for very large histories

**Schema** (planned):

```sql
CREATE TABLE console_history (
    id UUID PRIMARY KEY,
    server_id UUID NOT NULL,
    timestamp TIMESTAMPTZ NOT NULL,
    output_type SMALLINT NOT NULL, -- 0=stdout, 1=stderr
    line TEXT NOT NULL,
    INDEX idx_console_history_server_timestamp (server_id, timestamp DESC)
);
```

### Multiple Console Sessions

Support multiple users viewing the same server's console simultaneously.

**Session Tracking**:

- Track connected clients per server
- Show "X users watching" indicator
- Notify when users join/leave (optional)
- Graceful handling of connection drops

**SignalR Groups**:

- One group per server: `server:{serverId}`
- Clients join/leave groups via hub methods
- Broadcasts go to entire group

### Access Control

Integrate with Identity service for permission checks.

**Permission Model** (planned):

| Permission                  | Description                                |
| --------------------------- | ------------------------------------------ |
| `console:view`              | View console output                        |
| `console:execute`           | Execute commands                           |
| `console:execute:dangerous` | Execute dangerous commands (stop, restart) |
| `console:history:view`      | View console history                       |
| `console:history:search`    | Search console history                     |

**Role Defaults**:

- `viewer`: `console:view`, `console:history:view`
- `operator`: All viewer permissions + `console:execute`
- `admin`: All permissions
- `owner`: All permissions

### Console Recording and Playback

Record console sessions for training, debugging, or compliance.

**Features** (planned):

- Start/stop recording manually or on schedule
- Playback with speed control
- Export to text or video format
- Storage in Azure Blob Storage or S3-compatible
- Retention policy enforcement

---

## Integration Points

### Agent Integration

Agents running on customer nodes communicate with the Console service to relay game server I/O.

**Communication Pattern**:

- **Outbound-only** from agents (agents don't accept inbound connections)
- **MassTransit/RabbitMQ** for reliable message delivery
- **Bidirectional messages**: Output flows from agent to service; commands flow from service to agent

**Message Types** (planned):

```csharp
// Agent -> Console Service
public record ConsoleOutputReceived
{
    public Guid ServerId { get; init; }
    public Guid AgentId { get; init; }
    public string[] Lines { get; init; }
    public ConsoleOutputType OutputType { get; init; }
}

// Console Service -> Agent
public record ExecuteCommand
{
    public Guid ServerId { get; init; }
    public string Command { get; init; }
    public string RequestedBy { get; init; }
    public Guid CorrelationId { get; init; }
}
```

### Servers Service Integration

The Console service works closely with the Servers service.

**Coordination Points**:

- Server status changes (started/stopped) should update console UI
- When server stops, notify console clients
- Command execution may trigger status polling

**Event Subscription** (planned):

```csharp
public class ServerStatusChangedConsumer : IConsumer<ServerStatusChanged>
{
    private readonly IHubContext<ConsoleHub> _hubContext;

    public async Task Consume(ConsumeContext<ServerStatusChanged> context)
    {
        var message = context.Message;

        // Notify all clients watching this server
        await _hubContext.Clients
            .Group($"server:{message.ServerId}")
            .SendAsync("ServerStatusChanged", new
            {
                message.ServerId,
                message.Status,
                message.Timestamp
            });
    }
}
```

### Identity Service Integration

Authentication and authorization flow through the Identity service.

**JWT Claims Used**:

- `sub` - User ID
- `org_id` - Current organization context
- `role` - User roles
- Custom claims for fine-grained permissions

**Permission Checking**:

- Hub methods validate permissions before executing
- REST endpoints use `[Authorize]` attributes with policies

### Gateway Routing

The Console service is accessible through the Gateway at two routes:

**API Route**: `/api/v1/console/{**catch-all}`

- Path prefix removed before forwarding
- TenantScoped authorization policy
- PerTenant rate limiting

**Hub Route**: `/hubs/console/{**catch-all}`

- No path transformation (SignalR needs exact path)
- TenantScoped authorization policy
- PerTenant rate limiting
- Session affinity enabled for WebSocket connections

---

## Configuration

### Application Settings

**File**: `appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=dhadgar_platform;Username=dhadgar;Password=dhadgar",
    "RabbitMqHost": "localhost"
  },
  "RabbitMq": {
    "Username": "dhadgar",
    "Password": "dhadgar"
  }
}
```

### OpenTelemetry Configuration

Enable distributed tracing export:

```bash
# Set OTLP endpoint for telemetry export
dotnet user-secrets init --project src/Dhadgar.Console
dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317" --project src/Dhadgar.Console
```

This enables traces, metrics, and logs to flow to the local observability stack (OTLP Collector -> Prometheus/Loki -> Grafana).

### SignalR Configuration

SignalR can be configured through standard ASP.NET Core options:

```json
{
  "SignalR": {
    "HubOptions": {
      "EnableDetailedErrors": true,
      "KeepAliveInterval": "00:00:15",
      "ClientTimeoutInterval": "00:00:30",
      "StreamBufferCapacity": 10,
      "MaximumReceiveMessageSize": 65536
    }
  }
}
```

**Note**: `EnableDetailedErrors` should be `false` in production.

### Planned Configuration Options

```json
{
  "Console": {
    "History": {
      "Enabled": true,
      "RetentionDays": 30,
      "MaxLinesInMemory": 1000,
      "PersistToDisk": true
    },
    "Commands": {
      "Enabled": true,
      "RateLimitPerMinute": 60,
      "MaxCommandLength": 1000,
      "Allowlist": [],
      "Blocklist": ["shutdown", "rm -rf"]
    },
    "Output": {
      "BatchSize": 10,
      "BatchIntervalMs": 100,
      "MaxLineLength": 4096,
      "BackpressureThreshold": 1000
    },
    "Recording": {
      "Enabled": false,
      "StorageProvider": "azure-blob",
      "StorageConnectionString": "",
      "MaxRecordingDurationMinutes": 60
    }
  }
}
```

---

## Testing

### Running Existing Tests

```bash
# Run all Console tests
dotnet test tests/Dhadgar.Console.Tests

# Run specific test
dotnet test tests/Dhadgar.Console.Tests --filter "FullyQualifiedName~HelloWorldTests"

# With verbose output
dotnet test tests/Dhadgar.Console.Tests -v n
```

### Current Test Coverage

**File**: `tests/Dhadgar.Console.Tests/`

| Test Class        | Description                          |
| ----------------- | ------------------------------------ |
| `HelloWorldTests` | Verifies the static hello message    |
| `SwaggerTests`    | Verifies OpenAPI spec and Swagger UI |

### Testing SignalR Connections

SignalR hubs can be tested using the `Microsoft.AspNetCore.SignalR.Client` package:

```csharp
[Fact]
public async Task Ping_ReturnsPong()
{
    await using var application = new WebApplicationFactory<Program>();
    var server = application.Server;

    var hubConnection = new HubConnectionBuilder()
        .WithUrl(
            $"{server.BaseAddress}hubs/console",
            options => options.HttpMessageHandlerFactory = _ => server.CreateHandler())
        .Build();

    var pongReceived = new TaskCompletionSource<bool>();
    hubConnection.On("pong", () => pongReceived.TrySetResult(true));

    await hubConnection.StartAsync();
    await hubConnection.InvokeAsync("Ping");

    var result = await Task.WhenAny(
        pongReceived.Task,
        Task.Delay(TimeSpan.FromSeconds(5)));

    Assert.True(pongReceived.Task.IsCompleted);
}
```

### Integration Testing with WebApplicationFactory

The Console service includes `public partial class Program` to enable `WebApplicationFactory` integration testing:

```csharp
public class ConsoleIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ConsoleIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
        });
    }

    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        response.EnsureSuccessStatusCode();
    }
}
```

### Planned Test Coverage

| Test Area                | Description                                          |
| ------------------------ | ---------------------------------------------------- |
| `ConsoleHubTests`        | Test all hub methods (join, leave, command, history) |
| `ConsoleOutputTests`     | Test output streaming and batching                   |
| `CommandExecutionTests`  | Test command validation and dispatch                 |
| `PermissionTests`        | Test access control for all operations               |
| `SessionManagementTests` | Test session tracking and cleanup                    |
| `HistoryTests`           | Test history storage and retrieval                   |
| `IntegrationTests`       | End-to-end tests with mock agent                     |

---

## Docker

### Development Dockerfile

**File**: `Dockerfile`

Multi-stage build that compiles from source:

```bash
# Build from repository root
docker build -f src/Dhadgar.Console/Dockerfile -t dhadgar/console:latest .
```

### Pipeline Dockerfile

**File**: `Dockerfile.pipeline`

Uses pre-built artifacts from Azure Pipelines:

```bash
# Used by CI/CD pipeline with pre-built artifacts
docker build \
  -f src/Dhadgar.Console/Dockerfile.pipeline \
  --build-arg BUILD_ARTIFACT_PATH=/path/to/artifacts \
  -t dhadgar/console:latest .
```

### Runtime Configuration

| Setting      | Value                                         |
| ------------ | --------------------------------------------- |
| Base Image   | `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` |
| Port         | 8080                                          |
| User         | appuser (non-root)                            |
| Health Check | `curl -f http://localhost:8080/healthz`       |

**Environment Variables**:

| Variable                 | Default         | Description      |
| ------------------------ | --------------- | ---------------- |
| `ASPNETCORE_URLS`        | `http://+:8080` | Listen address   |
| `ASPNETCORE_ENVIRONMENT` | `Production`    | Environment name |

---

## Security Considerations

### Authentication

- All SignalR connections must be authenticated
- JWT tokens are passed via query string for WebSocket upgrade
- Tokens are validated against Identity service public keys

### Authorization

- Hub methods check permissions before executing
- Users can only view consoles for servers in their organization
- Command execution requires elevated permissions
- Dangerous commands (stop, restart) require additional permissions

### Input Validation

- Commands are sanitized before forwarding to agents
- Maximum command length enforced
- Optional allowlist/blocklist for commands
- No shell metacharacters without explicit permission

### Rate Limiting

- Commands rate limited per user and per server
- Output rate limited to prevent flooding
- Connection limits per user

### Audit Logging

- All commands logged with user, timestamp, and server
- Connection events logged
- Failed permission checks logged

---

## Observability

The Console service is instrumented with OpenTelemetry for full observability:

### Tracing

- HTTP request traces (ASP.NET Core instrumentation)
- SignalR hub method traces
- MassTransit message traces (when implemented)
- Custom spans for command execution

### Metrics

| Metric                          | Type    | Description                  |
| ------------------------------- | ------- | ---------------------------- |
| `console_connections_active`    | Gauge   | Active SignalR connections   |
| `console_commands_total`        | Counter | Total commands executed      |
| `console_output_lines_total`    | Counter | Total output lines processed |
| `console_history_queries_total` | Counter | Total history queries        |

### Logging

- Structured logging with correlation IDs
- Log levels configurable per component
- Integration with Loki for log aggregation

---

## Troubleshooting

### SignalR Connection Issues

**Symptom**: Client fails to connect to `/hubs/console`.

**Solutions**:

1. Verify the service is running: `curl http://localhost:5070/healthz`
2. Check CORS configuration allows your client origin
3. Ensure WebSocket upgrade is not blocked by proxy
4. Check for authentication issues (invalid/expired token)

### Console Output Not Appearing

**Symptom**: Connected to console but no output appears.

**Solutions**:

1. Verify server is running and producing output
2. Check agent is connected and forwarding output
3. Verify you called `JoinServer` for the correct server ID
4. Check for permission issues

### Commands Not Executing

**Symptom**: Commands sent but no response received.

**Solutions**:

1. Verify user has `console:execute` permission
2. Check command is not on blocklist
3. Verify agent is connected and healthy
4. Check RabbitMQ for queued messages

### High Memory Usage

**Symptom**: Service memory usage growing over time.

**Solutions**:

1. Check for disconnected clients not cleaned up
2. Verify history retention is configured correctly
3. Check output buffer sizes
4. Review connection limits

---

## Related Documentation

- **Root CLAUDE.md**: `/MeridianConsole/CLAUDE.md` - Overall project guidance
- **Gateway README**: `/src/Dhadgar.Gateway/README.md` - Gateway routing and session affinity
- **ServiceDefaults**: `/src/Shared/Dhadgar.ServiceDefaults/` - Shared middleware and health checks
- **Identity Service**: `/src/Dhadgar.Identity/README.md` - Authentication and authorization
- **Servers Service**: `/src/Dhadgar.Servers/` - Server lifecycle management
- **Agent Core**: `/src/Agents/Dhadgar.Agent.Core/` - Agent-side console handling
- **Docker Compose**: `/deploy/compose/README.md` - Local infrastructure setup
- **SignalR Documentation**: <https://learn.microsoft.com/aspnet/core/signalr/>
