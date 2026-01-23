# Dhadgar.Servers

Game server lifecycle management service for Meridian Console. This service is the central component responsible for managing the complete lifecycle of game servers across customer-owned hardware, including provisioning, configuration, state management, and orchestration coordination.

## Table of Contents

- [Overview](#overview)
- [Current Status](#current-status)
- [Technology Stack](#technology-stack)
- [Quick Start](#quick-start)
- [Project Structure](#project-structure)
- [API Endpoints](#api-endpoints)
- [Database Schema](#database-schema)
- [Planned Features](#planned-features)
- [Service Integration](#service-integration)
- [Configuration](#configuration)
- [Observability](#observability)
- [Testing](#testing)
- [Development Workflow](#development-workflow)
- [Related Documentation](#related-documentation)

---

## Overview

### Purpose

The Servers service is the core component of Meridian Console's game server orchestration platform. It serves as the single source of truth for all game server definitions, configurations, and state tracking within the multi-tenant SaaS platform.

### Key Responsibilities

**Current (Scaffolding)**:
- Service health monitoring and liveness/readiness probes
- Basic API structure with OpenAPI documentation
- Database connectivity foundation

**Planned (Full Implementation)**:
- **Server Lifecycle Management**: Create, configure, start, stop, restart, update, and delete game servers
- **Configuration Management**: Game-specific settings, command-line arguments, environment variables
- **Server Templates**: Pre-configured templates for supported games (Minecraft, Valheim, ARK, etc.)
- **Resource Allocation**: CPU, memory, disk, and network resource limits
- **Status Tracking**: Real-time server state monitoring (starting, running, stopping, stopped, crashed)
- **Event Publishing**: Async notifications for server state changes via MassTransit/RabbitMQ
- **Node Assignment**: Coordination with Nodes service for server-to-node placement
- **Task Orchestration**: Creating and tracking provisioning/management tasks

### Architecture Context

```
                                    +-----------------+
                                    |    Gateway      |
                                    |   (Port 5000)   |
                                    +--------+--------+
                                             |
                      +----------------------+----------------------+
                      |                      |                      |
              +-------v-------+     +--------v-------+     +--------v-------+
              |   Identity    |     |    Servers     |     |     Nodes      |
              |  (Port 5010)  |     |  (Port 5030)   |     |  (Port 5040)   |
              +---------------+     +--------+-------+     +--------+-------+
                                             |                      |
                                             |    Async Messages    |
                                    +--------v--------+    +--------v--------+
                                    |     Tasks       |    |     Agents      |
                                    |   (Port 5050)   |    | (Customer HW)   |
                                    +-----------------+    +-----------------+
```

The Servers service does NOT directly communicate with customer agents. Instead, it:
1. Accepts server management requests from the Gateway (authenticated/authorized)
2. Persists server state to its own PostgreSQL database
3. Publishes provisioning/lifecycle events to RabbitMQ
4. Coordinates with the Tasks service for long-running operations
5. Queries the Nodes service for placement decisions

---

## Current Status

**Status**: Stub Implementation (Scaffolding Phase)

The service is currently in a scaffolding state, providing the foundational structure for incremental development. This is intentional - the codebase establishes the "shape" of the service while core features remain unimplemented.

### What Exists Today

| Component | Status | Description |
|-----------|--------|-------------|
| ASP.NET Core Host | Complete | Minimal API setup with standard middleware |
| Health Endpoints | Complete | `/healthz`, `/livez`, `/readyz` probes |
| OpenAPI/Swagger | Complete | Development-mode API documentation |
| EF Core Setup | Complete | DbContext with design-time factory |
| OpenTelemetry | Complete | Tracing, metrics, and logging instrumentation |
| Correlation Middleware | Complete | Request/trace ID propagation |
| Problem Details | Complete | RFC 7807 error responses |
| Unit Tests | Complete | Basic test scaffolding with WebApplicationFactory |
| Database Entities | Placeholder | `SampleEntity` placeholder (to be replaced) |

### What Needs Implementation

- Real domain entities (Server, ServerConfig, ServerTemplate)
- CRUD API endpoints for servers
- Lifecycle operations (start, stop, restart, update)
- MassTransit message consumers and publishers
- Business logic and validation
- Integration with Nodes service for placement
- Integration with Tasks service for orchestration

---

## Technology Stack

### Runtime & Framework

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 10.0 | Runtime platform |
| ASP.NET Core | 10.0 | Web framework (Minimal APIs) |
| C# | Latest | Programming language (nullable enabled) |

### Data & Persistence

| Technology | Version | Purpose |
|------------|---------|---------|
| PostgreSQL | 16.x | Primary database |
| Entity Framework Core | 10.0.0 | ORM and migrations |
| Npgsql | 10.0.0 | PostgreSQL provider for EF Core |

### Messaging

| Technology | Version | Purpose |
|------------|---------|---------|
| MassTransit | 8.3.6 | Message bus abstraction |
| RabbitMQ | 3.x | Message broker (via MassTransit.RabbitMQ) |

### Observability

| Technology | Version | Purpose |
|------------|---------|---------|
| OpenTelemetry | 1.14.0 | Distributed tracing and metrics |
| OpenTelemetry.Instrumentation.AspNetCore | 1.14.0 | HTTP instrumentation |
| OpenTelemetry.Instrumentation.Http | 1.14.0 | HttpClient instrumentation |
| OpenTelemetry.Instrumentation.Runtime | 1.14.0 | .NET runtime metrics |
| OpenTelemetry.Instrumentation.Process | 1.14.0-beta.2 | Process-level metrics |
| OTLP Exporter | 1.14.0 | Export to OpenTelemetry Collector |

### API Documentation

| Technology | Version | Purpose |
|------------|---------|---------|
| Swashbuckle.AspNetCore | 10.1.0 | Swagger/OpenAPI generation |
| Microsoft.AspNetCore.OpenApi | 10.0.0 | OpenAPI metadata |

### Shared Libraries

| Library | Purpose |
|---------|---------|
| Dhadgar.Contracts | Shared DTOs, message contracts |
| Dhadgar.Shared | Common utilities and primitives |
| Dhadgar.Messaging | MassTransit configuration and conventions |
| Dhadgar.ServiceDefaults | Common middleware, health checks, Swagger config |

---

## Quick Start

### Prerequisites

1. **.NET SDK 10.0.100** - Pinned in `global.json` at repository root
2. **Docker** - For local infrastructure (PostgreSQL, RabbitMQ, Redis)
3. **IDE** - Visual Studio 2022, VS Code, or JetBrains Rider

### Start Local Infrastructure

```bash
# From repository root
cd deploy/compose

# Create .env file with required password (first time only)
echo "POSTGRES_PASSWORD=dhadgar" > .env

# Start infrastructure services
docker compose -f docker-compose.dev.yml up -d
```

This starts:
- **PostgreSQL** on port 5432 (user: `dhadgar`, password: `dhadgar`, database: `dhadgar_platform`)
- **RabbitMQ** on ports 5672 (AMQP) and 15672 (Management UI)
- **Redis** on port 6379
- **Observability stack** (Grafana, Prometheus, Loki, OTLP Collector)

### Build the Service

```bash
# From repository root
dotnet restore
dotnet build src/Dhadgar.Servers
```

### Run the Service

```bash
# Run normally
dotnet run --project src/Dhadgar.Servers

# Run with hot reload (recommended for development)
dotnet watch --project src/Dhadgar.Servers
```

The service starts on **http://localhost:5030** (configured in Gateway routing).

> **Note**: The `launchSettings.json` may show a different port (5003) for direct debugging. The canonical port 5030 is what the Gateway expects.

### Verify the Service

```bash
# Service info
curl http://localhost:5030/

# Hello endpoint
curl http://localhost:5030/hello

# Health check
curl http://localhost:5030/healthz

# Swagger UI (Development mode only)
# Open in browser: http://localhost:5030/swagger
```

### Run Tests

```bash
# All Servers tests
dotnet test tests/Dhadgar.Servers.Tests

# Specific test
dotnet test tests/Dhadgar.Servers.Tests --filter "FullyQualifiedName~HelloWorldTests"
```

---

## Project Structure

```
src/Dhadgar.Servers/
├── Dhadgar.Servers.csproj      # Project file with dependencies
├── Program.cs                   # Application entry point and configuration
├── Hello.cs                     # Hello world constant for smoke tests
├── appsettings.json             # Configuration (connection strings, logging)
├── CLAUDE.md                    # Service-specific Claude instructions
├── Data/
│   ├── ServersDbContext.cs      # EF Core DbContext definition
│   └── ServersDbContextFactory.cs # Design-time factory for migrations
└── Properties/
    └── launchSettings.json      # Debug/launch configuration
```

### Key Files Explained

**Program.cs**
```csharp
// Main application setup - demonstrates the service structure
var builder = WebApplication.CreateBuilder(args);

// Service defaults (health checks)
builder.Services.AddDhadgarServiceDefaults();

// Swagger configuration
builder.Services.AddMeridianSwagger(
    title: "Dhadgar Servers API",
    description: "Game server lifecycle management for Meridian Console");

// Database configuration
builder.Services.AddDbContext<ServersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// OpenTelemetry instrumentation
// ... (tracing, metrics, logging configuration)

var app = builder.Build();

// Middleware pipeline
app.UseMeridianSwagger();
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// Auto-apply migrations in Development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ServersDbContext>();
    db.Database.Migrate();
}

// Endpoints
app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Servers", message = Hello.Message }));
app.MapGet("/hello", () => Results.Text(Hello.Message));
app.MapDhadgarDefaultEndpoints(); // /healthz, /livez, /readyz

app.Run();
```

**ServersDbContext.cs**
```csharp
// Current placeholder implementation - will be replaced with real entities
public sealed class ServersDbContext : DbContext
{
    public ServersDbContext(DbContextOptions<ServersDbContext> options) : base(options) { }

    // TODO: Replace with real entities
    public DbSet<SampleEntity> Sample => Set<SampleEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<SampleEntity>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200);
        });
    }
}
```

---

## API Endpoints

### Current Endpoints (Scaffolding)

| Method | Path | Description | Tags |
|--------|------|-------------|------|
| GET | `/` | Service information and hello message | Health |
| GET | `/hello` | Simple hello text response | Health |
| GET | `/healthz` | Comprehensive health check (all checks) | Health |
| GET | `/livez` | Liveness probe (is the process alive?) | Health |
| GET | `/readyz` | Readiness probe (ready to accept traffic?) | Health |

### Swagger/OpenAPI

In Development and Testing environments, Swagger UI is available at:
- **Swagger UI**: `http://localhost:5030/swagger`
- **OpenAPI Spec**: `http://localhost:5030/swagger/v1/swagger.json`

### Gateway Routing

When accessed through the Gateway (port 5000), the Servers API is exposed at:
```
/api/v1/servers/{**path}
```

The Gateway strips the `/api/v1/servers` prefix before forwarding to this service.

**Authorization**: Routes to this service require `TenantScoped` authorization policy and are rate-limited by the `PerTenant` policy.

### Planned Endpoints

The following endpoints are planned for implementation:

#### Server CRUD

| Method | Path | Description |
|--------|------|-------------|
| GET | `/servers` | List all servers (paginated, filtered by org) |
| GET | `/servers/{id}` | Get server details |
| POST | `/servers` | Create a new server |
| PUT | `/servers/{id}` | Update server configuration |
| PATCH | `/servers/{id}` | Partial update server |
| DELETE | `/servers/{id}` | Delete (decommission) server |

#### Server Lifecycle

| Method | Path | Description |
|--------|------|-------------|
| POST | `/servers/{id}/start` | Start a stopped server |
| POST | `/servers/{id}/stop` | Stop a running server |
| POST | `/servers/{id}/restart` | Restart a server |
| POST | `/servers/{id}/kill` | Force kill a server |
| POST | `/servers/{id}/update` | Update server files (game updates) |

#### Server Status

| Method | Path | Description |
|--------|------|-------------|
| GET | `/servers/{id}/status` | Get current server status |
| GET | `/servers/{id}/logs` | Get recent server logs |
| GET | `/servers/{id}/stats` | Get resource usage statistics |

#### Server Configuration

| Method | Path | Description |
|--------|------|-------------|
| GET | `/servers/{id}/config` | Get server configuration |
| PUT | `/servers/{id}/config` | Update server configuration |
| GET | `/servers/{id}/ports` | Get port mappings |
| PUT | `/servers/{id}/ports` | Update port mappings |

#### Server Templates

| Method | Path | Description |
|--------|------|-------------|
| GET | `/templates` | List available server templates |
| GET | `/templates/{id}` | Get template details |
| POST | `/templates` | Create custom template |

---

## Database Schema

### Current Schema (Placeholder)

The current implementation contains a placeholder entity for EF Core scaffolding:

```csharp
public sealed class SampleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "hello";
}
```

### Planned Schema

The following entities are planned for implementation:

#### Server Entity

The primary entity representing a game server instance.

```csharp
public sealed class Server
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? NodeId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string GameType { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    public ServerStatus Status { get; set; }
    public ServerPowerState PowerState { get; set; }

    public int CpuLimit { get; set; }
    public int MemoryLimitMb { get; set; }
    public int DiskLimitMb { get; set; }

    public int? PrimaryPort { get; set; }
    public int? QueryPort { get; set; }
    public int? RconPort { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastStartedAt { get; set; }
    public DateTime? LastStoppedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public ServerConfiguration? Configuration { get; set; }
    public ICollection<ServerPort> Ports { get; set; } = new List<ServerPort>();
}

public enum ServerStatus
{
    Unknown = 0,
    Provisioning = 1,
    Installing = 2,
    Ready = 3,
    Starting = 4,
    Running = 5,
    Stopping = 6,
    Stopped = 7,
    Restarting = 8,
    Updating = 9,
    Error = 10,
    Crashed = 11,
    Suspended = 12,
    Deleted = 99
}

public enum ServerPowerState
{
    Off = 0,
    On = 1,
    Suspended = 2
}
```

#### ServerConfiguration Entity

Stores game-specific configuration for a server.

```csharp
public sealed class ServerConfiguration
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }

    // Command-line arguments
    public string StartupCommand { get; set; } = string.Empty;
    public string AdditionalArguments { get; set; } = string.Empty;

    // Game-specific settings (JSON blob)
    public string? GameSettingsJson { get; set; }

    // Environment variables (JSON blob)
    public string? EnvironmentVariablesJson { get; set; }

    // Auto-start configuration
    public bool AutoStart { get; set; }
    public int AutoRestartOnCrash { get; set; }
    public int MaxRestartAttempts { get; set; } = 3;

    // Scheduled restarts
    public string? RestartScheduleCron { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Server Server { get; set; } = null!;
}
```

#### ServerTemplate Entity

Pre-configured templates for supported games.

```csharp
public sealed class ServerTemplate
{
    public Guid Id { get; set; }
    public Guid? OrganizationId { get; set; } // null = global template

    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string GameType { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Default resource allocations
    public int DefaultCpuLimit { get; set; }
    public int DefaultMemoryLimitMb { get; set; }
    public int DefaultDiskLimitMb { get; set; }

    // Default port requirements
    public string? DefaultPortsJson { get; set; }

    // Default startup configuration
    public string? DefaultStartupCommand { get; set; }
    public string? DefaultGameSettingsJson { get; set; }

    // Template metadata
    public bool IsPublic { get; set; }
    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

#### ServerPort Entity

Individual port mappings for a server.

```csharp
public sealed class ServerPort
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }

    public string Name { get; set; } = string.Empty; // e.g., "Game", "Query", "RCON"
    public string Protocol { get; set; } = "UDP"; // TCP, UDP, or Both
    public int InternalPort { get; set; }
    public int ExternalPort { get; set; }

    public Server Server { get; set; } = null!;
}
```

### Database Conventions

- All tables use PostgreSQL with `Guid` primary keys (UUID)
- Timestamps stored as UTC `DateTime` values
- JSON blobs used for flexible configuration (game settings vary widely)
- Soft deletes via `Status = Deleted` (no hard deletes)
- Organization isolation enforced at query level

### Migration Commands

```bash
# Add a new migration
dotnet ef migrations add MigrationName \
  --project src/Dhadgar.Servers \
  --startup-project src/Dhadgar.Servers \
  --output-dir Data/Migrations

# Apply migrations
dotnet ef database update \
  --project src/Dhadgar.Servers \
  --startup-project src/Dhadgar.Servers

# Remove last migration (if not applied)
dotnet ef migrations remove \
  --project src/Dhadgar.Servers \
  --startup-project src/Dhadgar.Servers

# Generate SQL script
dotnet ef migrations script \
  --project src/Dhadgar.Servers \
  --startup-project src/Dhadgar.Servers \
  --output migrations.sql
```

**Note**: In Development mode, migrations are auto-applied on startup. See `Program.cs` for implementation.

---

## Planned Features

### Phase 1: Core CRUD Operations

1. **Server Entity Implementation**
   - Create `Server`, `ServerConfiguration`, `ServerPort` entities
   - Add EF Core migrations
   - Implement basic CRUD endpoints

2. **Multi-Tenancy**
   - Enforce organization-level data isolation
   - Add organization ID to all queries
   - Implement row-level security patterns

3. **Validation**
   - Resource limit validation (CPU, memory, disk)
   - Game type validation against supported games
   - Port conflict detection

### Phase 2: Lifecycle Operations

1. **State Machine**
   - Implement server status transitions
   - Validate allowed state changes
   - Track status history

2. **Async Operations**
   - Start/stop/restart as async operations
   - Return task IDs for tracking
   - Implement polling/webhook for completion

3. **Event Publishing**
   - Publish `ServerProvisionRequested` events
   - Publish `ServerStateChanged` events
   - Publish `ServerConfigurationUpdated` events

### Phase 3: Advanced Features

1. **Server Templates**
   - Pre-configured templates for popular games
   - Organization-specific custom templates
   - Template inheritance and overrides

2. **Resource Management**
   - Resource usage tracking
   - Quota enforcement per organization
   - Overprovision warnings

3. **Scheduled Operations**
   - Scheduled restarts via cron expressions
   - Scheduled updates
   - Maintenance windows

4. **Bulk Operations**
   - Bulk start/stop/restart
   - Organization-wide operations
   - Template-based bulk creation

### Phase 4: Integration Features

1. **Nodes Integration**
   - Query available nodes for placement
   - Node selection based on resources
   - Node failure handling

2. **Tasks Integration**
   - Create tasks for long-running operations
   - Track task progress
   - Handle task failures

3. **Files Integration**
   - File synchronization for server files
   - Mod file deployment
   - Backup/restore operations

4. **Console Integration**
   - Real-time log streaming
   - Command execution via RCON
   - Console output history

---

## Service Integration

### Message Contracts

The Servers service uses message contracts defined in `Dhadgar.Contracts.Servers`:

```csharp
// Strong-typed server identifier
public record ServerId(Guid Value);

// Published when a server needs provisioning
public record ServerProvisionRequested(
    Guid ServerId,
    Guid OrgId,
    string GameType,
    int CpuLimit,
    int MemoryMb);

// Published when provisioning completes
public record ServerProvisioned(
    Guid ServerId,
    Guid OrgId,
    string NodeId,
    IReadOnlyDictionary<string, string> ConnectionInfo);
```

### Integration with Nodes Service

The Nodes service (port 5040) manages the inventory of customer-owned hardware:

**Planned Interactions**:
- Query available nodes with sufficient resources
- Reserve resources on a node during provisioning
- Release resources when a server is deleted
- Handle node failure/recovery

```
Servers Service                      Nodes Service
      |                                   |
      |  GET /nodes?minCpu=X&minMem=Y     |
      |---------------------------------->|
      |  [List of available nodes]        |
      |<----------------------------------|
      |                                   |
      |  POST /nodes/{id}/reserve         |
      |---------------------------------->|
      |  {reservationId}                  |
      |<----------------------------------|
```

### Integration with Tasks Service

The Tasks service (port 5050) handles long-running operations:

**Planned Interactions**:
- Create provisioning tasks
- Create update tasks
- Track task progress
- Handle task completion/failure

```
Servers Service                      Tasks Service
      |                                   |
      |  POST /tasks (provision server)   |
      |---------------------------------->|
      |  {taskId, status: pending}        |
      |<----------------------------------|
      |                                   |
      |  [Task executes on Agent]         |
      |                                   |
      |  WebSocket/Callback: task done    |
      |<----------------------------------|
```

### Integration with Agents

Agents run on customer hardware and execute commands from the control plane:

**Important**: The Servers service does NOT communicate directly with agents. Instead:

1. Servers service publishes events to RabbitMQ
2. Tasks service creates executable tasks
3. Nodes service routes tasks to appropriate agents
4. Agents execute tasks and report back via Nodes service

This architecture ensures:
- Agents only need outbound connectivity
- No direct database access from customer hardware
- Centralized audit logging
- Proper authorization enforcement

### RabbitMQ Exchange Topology

Messages follow the `meridian.*` exchange naming convention:

| Exchange | Type | Purpose |
|----------|------|---------|
| `meridian.serverprovisionrequested` | Topic | Server provisioning requests |
| `meridian.serverprovisioned` | Topic | Provisioning completion events |
| `meridian.serverstatechanged` | Topic | State change notifications |
| `meridian.serverconfigurationupdated` | Topic | Config change notifications |

### Pagination

All list endpoints use the standard pagination contract from `Dhadgar.Contracts`:

```csharp
// Request
public sealed record PaginationRequest
{
    public int Page { get; init; } = 1;        // 1-based
    public int Limit { get; init; } = 50;      // Max 100
    public string? Sort { get; init; }
    public string Order { get; init; } = "asc";
}

// Response
public sealed record PagedResponse<T>
{
    public IReadOnlyCollection<T> Items { get; init; }
    public int Page { get; init; }
    public int Limit { get; init; }
    public int Total { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)Total / Limit);
    public bool HasNext => Page < TotalPages;
    public bool HasPrev => Page > 1;
}
```

---

## Configuration

### appsettings.json

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

### Configuration Options

| Key | Default | Description |
|-----|---------|-------------|
| `ConnectionStrings:Postgres` | (see above) | PostgreSQL connection string |
| `ConnectionStrings:RabbitMqHost` | localhost | RabbitMQ host address |
| `RabbitMq:Username` | dhadgar | RabbitMQ username |
| `RabbitMq:Password` | dhadgar | RabbitMQ password |
| `OpenTelemetry:OtlpEndpoint` | (empty) | OTLP exporter endpoint |
| `Logging:LogLevel:Default` | Information | Default log level |

### User Secrets (Development)

For sensitive configuration in local development:

```bash
# Initialize user secrets
dotnet user-secrets init --project src/Dhadgar.Servers

# Set connection string
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=...;Password=..." --project src/Dhadgar.Servers

# Enable OpenTelemetry export
dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317" --project src/Dhadgar.Servers

# List all secrets
dotnet user-secrets list --project src/Dhadgar.Servers
```

### Environment Variables

In production/Kubernetes, configuration is typically provided via environment variables:

```bash
# Connection strings
ConnectionStrings__Postgres="Host=postgres-svc;..."
ConnectionStrings__RabbitMqHost="rabbitmq-svc"

# RabbitMQ credentials
RabbitMq__Username="prod-user"
RabbitMq__Password="prod-password"

# Observability
OpenTelemetry__OtlpEndpoint="http://otel-collector:4317"
```

---

## Observability

### OpenTelemetry Integration

The service is fully instrumented with OpenTelemetry:

**Tracing**:
- ASP.NET Core request/response traces
- HTTP client call traces
- Custom activity/span support

**Metrics**:
- ASP.NET Core metrics (request duration, count)
- HTTP client metrics
- .NET runtime metrics (GC, thread pool)
- Process metrics (CPU, memory)

**Logging**:
- Structured logging with correlation IDs
- Automatic scope inclusion
- OTLP export when configured

### Correlation IDs

Every request receives correlation and request IDs via `CorrelationMiddleware`:

**Headers**:
- `X-Correlation-Id`: Persists across service calls
- `X-Request-Id`: Unique per request
- `X-Trace-Id`: OpenTelemetry trace ID
- `traceparent`: W3C Trace Context

These IDs are:
- Logged with every log entry
- Attached to OpenTelemetry spans
- Returned in response headers
- Included in error responses

### Health Checks

| Endpoint | Purpose | Tags |
|----------|---------|------|
| `/healthz` | Full health check | all |
| `/livez` | Liveness probe (process alive) | live |
| `/readyz` | Readiness probe (ready for traffic) | ready |

Health check response format:
```json
{
  "service": "Dhadgar.Servers",
  "status": "ok",
  "timestamp": "2026-01-22T10:30:00Z",
  "checks": {
    "self": {
      "status": "Healthy",
      "duration_ms": 0.5
    }
  }
}
```

### Local Observability Stack

Start the full observability stack:

```bash
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

Then configure the service to export telemetry:

```bash
dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317" --project src/Dhadgar.Servers
```

Access dashboards:
- **Grafana**: http://localhost:3000 (admin/admin)
- **Prometheus**: http://localhost:9090
- **RabbitMQ Management**: http://localhost:15672 (dhadgar/dhadgar)

---

## Testing

### Test Project Structure

```
tests/Dhadgar.Servers.Tests/
├── Dhadgar.Servers.Tests.csproj
├── HelloWorldTests.cs              # Basic unit tests
├── SwaggerTests.cs                 # API documentation tests
└── ServersWebApplicationFactory.cs # Test fixture configuration
```

### Running Tests

```bash
# Run all Servers tests
dotnet test tests/Dhadgar.Servers.Tests

# Run with verbose output
dotnet test tests/Dhadgar.Servers.Tests --logger "console;verbosity=detailed"

# Run specific test
dotnet test tests/Dhadgar.Servers.Tests --filter "FullyQualifiedName~HelloWorldTests"

# Run with coverage
dotnet test tests/Dhadgar.Servers.Tests --collect:"XPlat Code Coverage"
```

### WebApplicationFactory

The test project uses `WebApplicationFactory` for integration testing with an in-memory database:

```csharp
public class ServersWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ServersDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Add in-memory database for testing
            services.AddDbContext<ServersDbContext>(options =>
            {
                options.UseInMemoryDatabase("ServersTestDb");
            });
        });
    }
}
```

### Test Categories

| Category | Description | Dependencies |
|----------|-------------|--------------|
| Unit Tests | Pure logic tests | None |
| Integration Tests | API endpoint tests | In-memory database |
| Contract Tests | Message contract validation | None |
| E2E Tests | Full flow tests | External services |

### Current Tests

**HelloWorldTests.cs**:
```csharp
public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Servers", Hello.Message);
    }
}
```

**SwaggerTests.cs**:
```csharp
public class SwaggerTests : IClassFixture<ServersWebApplicationFactory>
{
    [Fact]
    public async Task SwaggerEndpoint_ReturnsValidOpenApiSpec()
    {
        await SwaggerTestHelper.VerifySwaggerEndpointAsync(
            _factory,
            expectedTitle: "Dhadgar Servers API");
    }

    [Fact]
    public async Task SwaggerUi_ReturnsHtml()
    {
        await SwaggerTestHelper.VerifySwaggerUiAsync(_factory);
    }

    [Fact]
    public async Task SwaggerEndpoint_DocumentsHealthEndpoints()
    {
        await SwaggerTestHelper.VerifyHealthEndpointsDocumentedAsync(_factory);
    }
}
```

### Planned Test Coverage

- Server CRUD operation tests
- Lifecycle state machine tests
- Validation logic tests
- Message publishing tests
- Authorization tests (tenant isolation)
- Pagination tests
- Error handling tests

---

## Development Workflow

### Making Changes

1. **Create a feature branch**:
   ```bash
   git checkout -b feature/servers-crud
   ```

2. **Make your changes** to the service code

3. **Add tests** for new functionality

4. **Run tests locally**:
   ```bash
   dotnet test tests/Dhadgar.Servers.Tests
   ```

5. **Build the full solution** to verify no breaks:
   ```bash
   dotnet build
   ```

6. **Commit and push**:
   ```bash
   git add .
   git commit -m "Add server CRUD endpoints"
   git push -u origin feature/servers-crud
   ```

### Adding New Endpoints

1. Define DTOs in `Dhadgar.Contracts.Servers`
2. Add endpoint in `Program.cs`
3. Implement business logic
4. Add validation
5. Add tests
6. Update Swagger documentation

### Adding Database Entities

1. Create entity class in `Data/` folder
2. Add `DbSet<T>` to `ServersDbContext`
3. Configure entity in `OnModelCreating`
4. Create migration:
   ```bash
   dotnet ef migrations add AddServerEntity \
     --project src/Dhadgar.Servers \
     --startup-project src/Dhadgar.Servers \
     --output-dir Data/Migrations
   ```
5. Review generated migration
6. Apply migration (auto in Development, manual in Production)

### Code Style

- Follow existing patterns in the codebase
- Use C# 12+ features where appropriate
- Enable nullable reference types
- Use `sealed` for classes not designed for inheritance
- Prefer `record` types for DTOs
- Use meaningful names (no abbreviations)

---

## Related Documentation

### Repository Documentation

| Document | Path | Description |
|----------|------|-------------|
| Main CLAUDE.md | `/CLAUDE.md` | Repository-wide Claude instructions |
| Development Setup | `/docs/DEVELOPMENT_SETUP.md` | Local development environment |
| Configuration Management | `/docs/CONFIGURATION-MANAGEMENT.md` | Config patterns |
| Architecture Overview | `/docs/architecture/README.md` | Architecture decisions |

### Shared Library Documentation

| Library | Path | Description |
|---------|------|-------------|
| ServiceDefaults | `/src/Shared/Dhadgar.ServiceDefaults/` | Common middleware |
| Contracts | `/src/Shared/Dhadgar.Contracts/` | Shared DTOs |
| Messaging | `/src/Shared/Dhadgar.Messaging/` | MassTransit config |

### Related Services

| Service | Port | Path | Relationship |
|---------|------|------|--------------|
| Gateway | 5000 | `/src/Dhadgar.Gateway/` | Routes requests to Servers |
| Identity | 5010 | `/src/Dhadgar.Identity/` | Provides authentication |
| Nodes | 5040 | `/src/Dhadgar.Nodes/` | Manages hardware inventory |
| Tasks | 5050 | `/src/Dhadgar.Tasks/` | Orchestrates operations |
| Files | 5060 | `/src/Dhadgar.Files/` | Manages file transfers |
| Console | 5070 | `/src/Dhadgar.Console/` | Real-time console streaming |

### Infrastructure Documentation

| Document | Path | Description |
|----------|------|-------------|
| Docker Compose | `/deploy/compose/README.md` | Local infrastructure |
| Kubernetes | `/deploy/kubernetes/` | K8s manifests (planned) |

---

## Appendix: Supported Game Types (Planned)

The following game types are planned for initial support:

| Game | GameType Key | Default Ports | Notes |
|------|--------------|---------------|-------|
| Minecraft Java | `minecraft-java` | 25565/TCP | Supports mods via Forge/Fabric |
| Minecraft Bedrock | `minecraft-bedrock` | 19132/UDP | Cross-platform |
| Valheim | `valheim` | 2456-2458/UDP | Requires SteamCMD |
| ARK: Survival Evolved | `ark` | 7777/UDP, 27015/UDP | High resource usage |
| Terraria | `terraria` | 7777/TCP | tModLoader support |
| Rust | `rust` | 28015/UDP, 28016/TCP | RCON support |
| 7 Days to Die | `7dtd` | 26900-26902/UDP | Alpha/stable versions |
| Project Zomboid | `zomboid` | 16261-16262/UDP | Multiplayer |
| Factorio | `factorio` | 34197/UDP | Mod support |
| Satisfactory | `satisfactory` | 7777/UDP, 15000/UDP | Experimental/EA |

Each game type will have:
- Default resource requirements
- Port configuration templates
- Startup command templates
- Update procedures
- Configuration schemas

---

## Troubleshooting

### Common Issues

**Database connection failure**:
```
Npgsql.PostgresException: 28P01: password authentication failed
```
- Verify PostgreSQL is running: `docker ps | grep postgres`
- Check credentials in appsettings.json
- Ensure database exists: `dhadgar_platform`

**Port already in use**:
```
System.IO.IOException: Failed to bind to address http://localhost:5030
```
- Check for existing process: `netstat -ano | findstr 5030`
- Kill the process or use a different port

**Migration errors**:
```
The migration '...' has already been applied to the database
```
- Check migration status: `dotnet ef migrations list`
- Remove problematic migration: `dotnet ef migrations remove`

**RabbitMQ connection failure**:
```
RabbitMQ.Client.Exceptions.BrokerUnreachableException
```
- Verify RabbitMQ is running: `docker ps | grep rabbitmq`
- Check management UI: http://localhost:15672
- Verify credentials match configuration

### Getting Help

1. Check the main [CLAUDE.md](/CLAUDE.md) for repository-wide guidance
2. Review logs with correlation IDs for distributed tracing
3. Use Swagger UI to test endpoints interactively
4. Check Docker container logs: `docker compose -f deploy/compose/docker-compose.dev.yml logs -f`

---

*This documentation was generated for the Dhadgar.Servers service as part of the Meridian Console platform.*
