# Dhadgar.Nodes Service

The Nodes service is a critical component of the Meridian Console (Dhadgar) platform responsible for managing the inventory, health, and capacity of customer-owned hardware nodes. This service acts as the central registry for all nodes that agents connect to and manages the lifecycle of node enrollment, health monitoring, and capacity tracking.

## Table of Contents

1. [Overview](#overview)
2. [Current Implementation Status](#current-implementation-status)
3. [Technology Stack](#technology-stack)
4. [Quick Start](#quick-start)
5. [Architecture](#architecture)
6. [Planned Features](#planned-features)
7. [Database Schema](#database-schema)
8. [API Reference](#api-reference)
9. [Integration Points](#integration-points)
10. [Configuration Reference](#configuration-reference)
11. [Testing](#testing)
12. [Deployment](#deployment)
13. [Observability](#observability)
14. [Security Considerations](#security-considerations)
15. [Related Documentation](#related-documentation)

---

## Overview

### What is the Nodes Service?

The Nodes service serves as the **hardware inventory and agent management hub** for the Meridian Console platform. In the Meridian Console architecture, customers own and operate their own hardware (physical servers, VMs, or cloud instances), and the platform orchestrates game servers on that hardware through customer-hosted agents.

The Nodes service is responsible for:

1. **Node Registration** - Tracking all hardware nodes enrolled in the platform
2. **Agent Enrollment** - Managing the secure enrollment process for customer-hosted agents using mTLS
3. **Hardware Inventory** - Cataloging hardware specifications (CPU, RAM, disk, network interfaces)
4. **Health Monitoring** - Receiving and processing heartbeats from agents to track node health
5. **Capacity Management** - Tracking available resources and calculating capacity for game server placement
6. **Status Tracking** - Maintaining real-time status of all nodes (online, offline, maintenance, etc.)
7. **Maintenance Mode** - Supporting graceful node maintenance without disrupting game servers

### Key Concepts

| Concept | Description |
|---------|-------------|
| **Node** | A physical or virtual machine that can host game servers. Owned by the customer. |
| **Agent** | Software installed on customer hardware that communicates with the control plane. Makes outbound-only connections. |
| **Enrollment** | The secure process by which a new agent registers with the platform and receives credentials. |
| **Heartbeat** | Periodic health reports sent by agents to indicate they are alive and report resource utilization. |
| **Capacity** | The available compute resources (CPU, RAM, disk) on a node for scheduling new game servers. |
| **Maintenance Mode** | A state where no new servers are scheduled on a node, allowing graceful draining for updates. |

### Why Nodes Service Exists

Meridian Console is **not** a hosted game server provider. Instead, it is a **control plane** that orchestrates game servers on hardware that customers control. This architecture provides:

- **Data Sovereignty** - Customer data stays on their own hardware
- **Cost Control** - Customers use their existing infrastructure
- **Network Locality** - Game servers run close to players on customer-chosen networks
- **Compliance** - Easier compliance with data residency requirements

The Nodes service is the bridge between the cloud-hosted control plane and the customer-hosted agents running on their hardware.

---

## Current Implementation Status

**Status: STUB SERVICE**

The Nodes service currently contains scaffolding and foundational structure only. This is intentional - the codebase provides the "shape" for incremental development.

### What Exists Today

| Component | Status | Notes |
|-----------|--------|-------|
| Project structure | Complete | Standard ASP.NET Core Minimal API layout |
| EF Core DbContext | Complete | `NodesDbContext` with placeholder entity |
| Health endpoints | Complete | `/healthz`, `/livez`, `/readyz` via ServiceDefaults |
| Swagger/OpenAPI | Complete | Available at `/swagger` in Development mode |
| Basic endpoints | Complete | `/` (service info), `/hello` (smoke test) |
| Docker support | Complete | Both development and pipeline Dockerfiles |
| Test project | Complete | Basic test scaffolding with WebApplicationFactory |
| OpenTelemetry | Complete | Tracing, metrics, and logging configured |
| Middleware | Complete | Correlation tracking, problem details, request logging |

### What Is Planned

| Component | Status | Priority |
|-----------|--------|----------|
| Node entity and schema | Planned | High |
| Agent enrollment workflow | Planned | High |
| Health monitoring/heartbeats | Planned | High |
| Hardware inventory collection | Planned | Medium |
| Capacity calculation | Planned | Medium |
| Maintenance mode | Planned | Medium |
| mTLS for agent communication | Planned | High |
| MassTransit message consumers | Planned | Medium |
| Node status transitions | Planned | Medium |

---

## Technology Stack

### Runtime

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 10.0.100 | Runtime and SDK |
| ASP.NET Core | 10.0 | Web framework (Minimal APIs) |
| C# | 14 (latest) | Programming language |

### Data & Messaging

| Technology | Version | Purpose |
|------------|---------|---------|
| PostgreSQL | 16 | Primary database |
| Entity Framework Core | 10.0.0 | ORM |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.0 | PostgreSQL EF Core provider |
| MassTransit | 8.3.6 | Message bus abstraction |
| MassTransit.RabbitMQ | 8.3.6 | RabbitMQ transport for MassTransit |

### Observability

| Technology | Version | Purpose |
|------------|---------|---------|
| OpenTelemetry | 1.14.0 | Distributed tracing, metrics, logging |
| OpenTelemetry.Extensions.Hosting | 1.14.0 | Host integration |
| OpenTelemetry.Instrumentation.AspNetCore | 1.14.0 | ASP.NET Core auto-instrumentation |
| OpenTelemetry.Instrumentation.Http | 1.14.0 | HTTP client instrumentation |
| OpenTelemetry.Instrumentation.Runtime | 1.14.0 | .NET runtime metrics |
| OpenTelemetry.Instrumentation.Process | 1.14.0-beta.2 | Process metrics |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.14.0 | OTLP exporter |

### API Documentation

| Technology | Version | Purpose |
|------------|---------|---------|
| Swashbuckle.AspNetCore | 10.1.0 | Swagger/OpenAPI generation |
| Microsoft.AspNetCore.OpenApi | 10.0.0 | OpenAPI endpoint support |

### Shared Libraries

| Library | Purpose |
|---------|---------|
| Dhadgar.Contracts | DTOs and message contracts |
| Dhadgar.Shared | Utilities and primitives |
| Dhadgar.Messaging | MassTransit/RabbitMQ conventions |
| Dhadgar.ServiceDefaults | Common middleware and observability |

---

## Quick Start

### Prerequisites

Before running the Nodes service, ensure you have:

1. **.NET SDK 10.0.100** - Check with `dotnet --version`
2. **Docker** - For local infrastructure (PostgreSQL, RabbitMQ)
3. **Git** - For cloning the repository

### Step 1: Start Local Infrastructure

From the repository root, start the development infrastructure:

```bash
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

This starts:
- **PostgreSQL** on port 5432 (credentials: `dhadgar` / `dhadgar`)
- **RabbitMQ** on port 5672 (management UI: 15672)
- **Redis** on port 6379
- **Observability stack** (Grafana: 3000, Prometheus: 9090, Loki: 3100)

Verify services are running:

```bash
docker ps --filter "name=dhadgar-dev"
```

### Step 2: Build the Solution

From the repository root:

```bash
dotnet restore
dotnet build
```

Or build just the Nodes service:

```bash
dotnet build src/Dhadgar.Nodes
```

### Step 3: Run the Service

```bash
dotnet run --project src/Dhadgar.Nodes
```

Or with hot reload for development:

```bash
dotnet watch --project src/Dhadgar.Nodes
```

The service starts on **http://localhost:5004** (configured in `launchSettings.json`).

### Step 4: Verify the Service

Test the service is running:

```bash
# Service info
curl http://localhost:5004/

# Hello endpoint
curl http://localhost:5004/hello

# Health check
curl http://localhost:5004/healthz
```

### Step 5: Access Swagger UI

Open your browser to:

```
http://localhost:5004/swagger
```

This displays the interactive API documentation (only available in Development environment).

### Running Tests

```bash
# All Nodes tests
dotnet test tests/Dhadgar.Nodes.Tests

# Specific test
dotnet test tests/Dhadgar.Nodes.Tests --filter "FullyQualifiedName~HelloWorldTests"
```

---

## Architecture

### Service Boundaries

The Nodes service follows the microservices pattern established in the Meridian Console platform:

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Gateway (YARP)                               │
│                    Single Public Entry Point                         │
│                    http://localhost:5000                             │
└─────────────────────────────────────────────────────────────────────┘
                                    │
                    ┌───────────────┼───────────────┐
                    │               │               │
              /api/v1/nodes   /api/v1/agents   /api/v1/servers
                    │               │               │
                    v               v               v
              ┌───────────────────────────────────────────────┐
              │             Nodes Service                      │
              │         http://localhost:5004                  │
              │                                                │
              │  • Node registration and inventory             │
              │  • Agent enrollment endpoints                  │
              │  • Health monitoring                           │
              │  • Capacity management                         │
              └───────────────────────────────────────────────┘
                    │                       │
                    v                       v
              ┌───────────┐          ┌───────────────┐
              │ PostgreSQL│          │   RabbitMQ    │
              │  Database │          │   Messages    │
              └───────────┘          └───────────────┘
```

### Gateway Routing

The Gateway routes traffic to the Nodes service via two paths:

1. **`/api/v1/nodes/{**catch-all}`** - Standard node management endpoints (tenant-scoped)
2. **`/api/v1/agents/{**catch-all}`** - Agent enrollment and heartbeat endpoints (agent auth)

From `src/Dhadgar.Gateway/appsettings.json`:

```json
{
  "nodes-route": {
    "ClusterId": "nodes",
    "Order": 20,
    "Match": { "Path": "/api/v1/nodes/{**catch-all}" },
    "AuthorizationPolicy": "TenantScoped",
    "RateLimiterPolicy": "PerTenant",
    "Transforms": [
      { "PathRemovePrefix": "/api/v1/nodes" }
    ]
  },
  "agents-route": {
    "ClusterId": "nodes",
    "Order": 30,
    "Match": { "Path": "/api/v1/agents/{**catch-all}" },
    "AuthorizationPolicy": "Agent",
    "RateLimiterPolicy": "PerAgent",
    "Transforms": [
      { "PathRemovePrefix": "/api/v1/agents" }
    ]
  }
}
```

The `agents-route` uses a special `Agent` authorization policy and `PerAgent` rate limiter, reflecting that agent traffic has different security and traffic patterns than user traffic.

### Project Structure

```
src/Dhadgar.Nodes/
├── Data/
│   ├── NodesDbContext.cs           # EF Core DbContext
│   └── NodesDbContextFactory.cs    # Design-time factory for EF migrations
├── Migrations/
│   └── README.md                   # Instructions for creating migrations
├── Properties/
│   └── launchSettings.json         # Development run configuration
├── appsettings.json                # Service configuration
├── CLAUDE.md                       # AI assistant context file
├── Dhadgar.Nodes.csproj            # Project file
├── Dockerfile                      # Local development container build
├── Dockerfile.pipeline             # CI/CD pipeline container build
├── Hello.cs                        # Hello world surface area
├── Program.cs                      # Application entry point and setup
└── README.md                       # This file
```

### Request Processing Pipeline

Every request through the Nodes service passes through these middleware layers (in order):

1. **CorrelationMiddleware** - Ensures correlation ID, request ID, and trace ID are present
2. **ProblemDetailsMiddleware** - Transforms exceptions to RFC 7807 Problem Details responses
3. **RequestLoggingMiddleware** - Logs HTTP requests and responses with timing

These are inherited from `Dhadgar.ServiceDefaults` and configured in `Program.cs`.

---

## Planned Features

### 1. Node Registration and Enrollment

The node enrollment process establishes trust between the control plane and customer hardware.

#### Planned Enrollment Flow

```
┌─────────────┐      ┌─────────────┐      ┌─────────────┐      ┌─────────────┐
│   Customer  │      │   Identity  │      │    Nodes    │      │   Secrets   │
│   Admin     │      │   Service   │      │   Service   │      │   Service   │
└──────┬──────┘      └──────┬──────┘      └──────┬──────┘      └──────┬──────┘
       │                    │                    │                    │
       │  1. Create enrollment token            │                    │
       │───────────────────────────────────────>│                    │
       │                    │                    │                    │
       │  2. Return token + instructions        │                    │
       │<───────────────────────────────────────│                    │
       │                    │                    │                    │
       │  3. Install agent on hardware          │                    │
       │─────────────────────┐                  │                    │
       │                     │                  │                    │
       │                     │  4. Agent starts with enrollment token│
       │                     │─────────────────>│                    │
       │                     │                  │                    │
       │                     │                  │  5. Validate token │
       │                     │                  │───────────────────>│
       │                     │                  │                    │
       │                     │                  │  6. Issue mTLS certs
       │                     │                  │<───────────────────│
       │                     │                  │                    │
       │                     │  7. Return certs + node ID           │
       │                     │<─────────────────│                    │
       │                     │                  │                    │
       │                     │  8. Agent stores certs, starts heartbeat
       │                     │─────────────────>│                    │
       │                     │                  │                    │
```

#### Planned Enrollment Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/enrollment/tokens` | POST | Create a new enrollment token (admin) |
| `/enrollment/tokens/{id}` | GET | Get enrollment token status |
| `/enrollment/tokens/{id}` | DELETE | Revoke an enrollment token |
| `/enrollment/complete` | POST | Complete enrollment (agent) |

### 2. Agent Enrollment with mTLS

Agents will use mutual TLS (mTLS) for secure communication after enrollment.

#### Planned mTLS Architecture

- **Enrollment token** - One-time use token generated by admin
- **Client certificate** - Issued to agent during enrollment, used for mTLS
- **Certificate rotation** - Automatic renewal before expiration
- **Revocation** - Ability to revoke agent certificates

#### Planned Security Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/agents/{id}/certificates` | GET | Get certificate status |
| `/agents/{id}/certificates/rotate` | POST | Force certificate rotation |
| `/agents/{id}/revoke` | POST | Revoke agent access |

### 3. Hardware Inventory

The Nodes service will maintain a comprehensive inventory of hardware specifications.

#### Planned Inventory Data

```csharp
// Planned entity structure (not yet implemented)
public class NodeHardwareInventory
{
    // CPU
    public int CpuCores { get; set; }
    public int CpuThreads { get; set; }
    public string CpuModel { get; set; }
    public double CpuFrequencyMhz { get; set; }

    // Memory
    public long TotalMemoryBytes { get; set; }
    public long AvailableMemoryBytes { get; set; }

    // Storage
    public List<DiskInfo> Disks { get; set; }

    // Network
    public List<NetworkInterfaceInfo> NetworkInterfaces { get; set; }

    // GPU (optional)
    public List<GpuInfo> Gpus { get; set; }

    // OS
    public string OperatingSystem { get; set; }
    public string OsVersion { get; set; }
    public string Architecture { get; set; }
}
```

### 4. Health Monitoring and Heartbeats

Agents will send periodic heartbeats with resource utilization data.

#### Planned Heartbeat Data

```csharp
// Planned heartbeat structure (not yet implemented)
public class NodeHeartbeat
{
    public Guid NodeId { get; set; }
    public DateTime Timestamp { get; set; }
    public TimeSpan Uptime { get; set; }

    // Resource utilization
    public double CpuUsagePercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryAvailableBytes { get; set; }

    // Disk I/O
    public Dictionary<string, DiskStats> DiskStats { get; set; }

    // Network I/O
    public Dictionary<string, NetworkStats> NetworkStats { get; set; }

    // Running servers
    public List<ServerStatus> RunningServers { get; set; }

    // Agent health
    public string AgentVersion { get; set; }
    public bool AgentHealthy { get; set; }
}
```

#### Planned Heartbeat Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/agents/heartbeat` | POST | Receive heartbeat from agent |
| `/nodes/{id}/health` | GET | Get node health history |
| `/nodes/{id}/health/latest` | GET | Get latest health report |

### 5. Capacity Management

The Nodes service will calculate and track available capacity for server placement.

#### Capacity Calculation Factors

- Total vs allocated CPU cores
- Total vs allocated memory
- Disk space availability
- Network bandwidth
- Current load vs rated capacity
- Reservation buffer (configurable overhead)

#### Planned Capacity Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/nodes/{id}/capacity` | GET | Get node capacity details |
| `/nodes/capacity/available` | GET | List nodes with available capacity |
| `/nodes/capacity/reserve` | POST | Reserve capacity for server (used by Tasks service) |
| `/nodes/capacity/release` | POST | Release reserved capacity |

### 6. Node Status Tracking

Nodes will have a state machine for tracking their lifecycle.

#### Planned Node States

```
                   ┌─────────────────────────────────────────────┐
                   │                                             │
                   v                                             │
┌──────────┐   ┌────────────┐   ┌─────────┐   ┌─────────────┐   │
│ Enrolling│──>│   Online   │<─>│ Degraded│──>│  Offline    │───┘
└──────────┘   └────────────┘   └─────────┘   └─────────────┘
                   │       ^         │              │
                   │       │         │              │
                   v       │         v              v
              ┌────────────┴────┐  ┌───────────────────┐
              │  Maintenance    │  │    Decommissioned │
              └─────────────────┘  └───────────────────┘
```

| State | Description |
|-------|-------------|
| Enrolling | Agent is completing enrollment process |
| Online | Node is healthy and accepting work |
| Degraded | Node is online but experiencing issues |
| Offline | Node has not sent heartbeats within threshold |
| Maintenance | Node is in maintenance mode (no new work) |
| Decommissioned | Node is permanently removed |

### 7. Maintenance Mode

Maintenance mode allows graceful updates to nodes.

#### Planned Maintenance Workflow

1. Admin initiates maintenance mode
2. Service marks node as "draining" - no new servers scheduled
3. Existing servers continue running
4. Admin migrates or stops servers
5. Admin performs maintenance
6. Admin exits maintenance mode
7. Node returns to "online" state

#### Planned Maintenance Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/nodes/{id}/maintenance/enter` | POST | Enter maintenance mode |
| `/nodes/{id}/maintenance/exit` | POST | Exit maintenance mode |
| `/nodes/{id}/maintenance/status` | GET | Get maintenance status |

---

## Database Schema

### Current Schema (Placeholder)

The current `NodesDbContext` contains a placeholder entity for scaffolding purposes:

```csharp
public sealed class NodesDbContext : DbContext
{
    public NodesDbContext(DbContextOptions<NodesDbContext> options) : base(options) { }

    // TODO: Replace with real entities
    public DbSet<SampleEntity> Sample => Set<SampleEntity>();
}

public sealed class SampleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "hello";
}
```

### Planned Schema

The following entity relationship diagram shows the planned database schema:

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                                     Node                                         │
├─────────────────────────────────────────────────────────────────────────────────┤
│ Id (PK)                  Guid                                                    │
│ OrganizationId (FK)      Guid          → Organizations table (Identity service) │
│ Name                     string(200)   Display name                              │
│ Hostname                 string(255)   Machine hostname                          │
│ Status                   NodeStatus    Enum: Enrolling, Online, Degraded, etc.   │
│ CreatedAt                DateTime      UTC timestamp                             │
│ UpdatedAt                DateTime      UTC timestamp                             │
│ LastHeartbeatAt          DateTime?     Last successful heartbeat                 │
│ MaintenanceModeAt        DateTime?     When maintenance mode was entered         │
│ MaintenanceReason        string(500)?  Reason for maintenance                    │
│ AgentVersion             string(50)?   Installed agent version                   │
│ Tags                     jsonb         User-defined tags for filtering           │
└─────────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ 1:1
                                    v
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              NodeHardwareInventory                               │
├─────────────────────────────────────────────────────────────────────────────────┤
│ NodeId (PK, FK)          Guid          → Node.Id                                 │
│ CpuCores                 int           Physical CPU cores                        │
│ CpuThreads               int           Logical CPU threads                       │
│ CpuModel                 string(200)   CPU model name                            │
│ CpuFrequencyMhz          double        Base frequency                            │
│ TotalMemoryBytes         long          Total RAM                                 │
│ OperatingSystem          string(100)   OS name                                   │
│ OsVersion                string(100)   OS version                                │
│ Architecture             string(20)    x64, arm64, etc.                          │
│ CollectedAt              DateTime      When inventory was last updated           │
│ DisksJson                jsonb         Serialized disk information               │
│ NetworkInterfacesJson    jsonb         Serialized NIC information                │
│ GpusJson                 jsonb?        Serialized GPU information (optional)     │
└─────────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ 1:N
                                    v
┌─────────────────────────────────────────────────────────────────────────────────┐
│                                  NodeHealth                                      │
├─────────────────────────────────────────────────────────────────────────────────┤
│ Id (PK)                  Guid                                                    │
│ NodeId (FK)              Guid          → Node.Id                                 │
│ Timestamp                DateTime      When heartbeat was received               │
│ Uptime                   TimeSpan      Node uptime                               │
│ CpuUsagePercent          double        CPU utilization (0-100)                   │
│ MemoryUsedBytes          long          Memory in use                             │
│ MemoryAvailableBytes     long          Memory available                          │
│ DiskStatsJson            jsonb         Per-disk I/O stats                        │
│ NetworkStatsJson         jsonb         Per-NIC stats                             │
│ RunningServerCount       int           Number of game servers running            │
│ AgentHealthy             bool          Agent self-reported health                │
│ HealthScore              int           Computed health score (0-100)             │
└─────────────────────────────────────────────────────────────────────────────────┘
                                    │
┌─────────────────────────────────────────────────────────────────────────────────┐
│                                  NodeCapacity                                    │
├─────────────────────────────────────────────────────────────────────────────────┤
│ NodeId (PK, FK)          Guid          → Node.Id                                 │
│ AllocatedCpuCores        double        CPU cores allocated to servers            │
│ AllocatedMemoryBytes     long          Memory allocated to servers               │
│ AllocatedDiskBytes       long          Disk allocated to servers                 │
│ ReservedCpuCores         double        CPU reserved for pending operations       │
│ ReservedMemoryBytes      long          Memory reserved for pending operations    │
│ ReservedDiskBytes        long          Disk reserved for pending operations      │
│ MaxServers               int?          Optional limit on concurrent servers      │
│ CurrentServerCount       int           Current running server count              │
│ LastCalculatedAt         DateTime      When capacity was last recalculated       │
└─────────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────────┐
│                               EnrollmentToken                                    │
├─────────────────────────────────────────────────────────────────────────────────┤
│ Id (PK)                  Guid                                                    │
│ OrganizationId (FK)      Guid          → Organizations table (Identity service) │
│ Token                    string(64)    Secure random token (hashed in DB)        │
│ CreatedAt                DateTime      When token was created                    │
│ ExpiresAt                DateTime      Token expiration                          │
│ UsedAt                   DateTime?     When token was used (null if unused)      │
│ UsedByNodeId             Guid?         → Node.Id (after enrollment)              │
│ CreatedByUserId          Guid          User who created the token                │
│ Metadata                 jsonb?        Optional metadata (labels, etc.)          │
│ RevokedAt                DateTime?     When token was revoked (if applicable)    │
└─────────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────────┐
│                                AgentCertificate                                  │
├─────────────────────────────────────────────────────────────────────────────────┤
│ Id (PK)                  Guid                                                    │
│ NodeId (FK)              Guid          → Node.Id                                 │
│ Thumbprint               string(64)    Certificate SHA-256 thumbprint            │
│ IssuedAt                 DateTime      When certificate was issued               │
│ ExpiresAt                DateTime      Certificate expiration                    │
│ RevokedAt                DateTime?     When revoked (if applicable)              │
│ RevocationReason         string(200)?  Reason for revocation                     │
│ IsActive                 bool          Whether this is the current cert          │
└─────────────────────────────────────────────────────────────────────────────────┘
```

### Database Indexes (Planned)

```sql
-- Performance indexes for common queries
CREATE INDEX IX_Node_OrganizationId ON nodes(organization_id);
CREATE INDEX IX_Node_Status ON nodes(status) WHERE status IN ('Online', 'Degraded');
CREATE INDEX IX_Node_LastHeartbeatAt ON nodes(last_heartbeat_at) WHERE status = 'Online';

-- Health history queries
CREATE INDEX IX_NodeHealth_NodeId_Timestamp ON node_health(node_id, timestamp DESC);
CREATE INDEX IX_NodeHealth_Timestamp ON node_health(timestamp) WHERE timestamp > NOW() - INTERVAL '24 hours';

-- Enrollment token lookup
CREATE UNIQUE INDEX IX_EnrollmentToken_Token ON enrollment_token(token) WHERE used_at IS NULL AND revoked_at IS NULL;

-- Certificate lookup
CREATE INDEX IX_AgentCertificate_NodeId ON agent_certificate(node_id) WHERE is_active = true;
CREATE INDEX IX_AgentCertificate_Thumbprint ON agent_certificate(thumbprint) WHERE revoked_at IS NULL;
```

### Creating Migrations

From the repository root:

```bash
# Add a new migration
dotnet ef migrations add MigrationName \
  --project src/Dhadgar.Nodes \
  --startup-project src/Dhadgar.Nodes \
  --output-dir Data/Migrations

# Apply migrations
dotnet ef database update \
  --project src/Dhadgar.Nodes \
  --startup-project src/Dhadgar.Nodes

# Remove last migration (if not applied)
dotnet ef migrations remove \
  --project src/Dhadgar.Nodes \
  --startup-project src/Dhadgar.Nodes
```

**Note**: In Development mode, migrations are automatically applied on startup.

---

## API Reference

### Current Endpoints

These endpoints exist today:

| Endpoint | Method | Description | Auth Required |
|----------|--------|-------------|---------------|
| `/` | GET | Service information | No |
| `/hello` | GET | Hello world smoke test | No |
| `/healthz` | GET | Full health check | No |
| `/livez` | GET | Liveness probe | No |
| `/readyz` | GET | Readiness probe | No |
| `/swagger` | GET | Swagger UI (Development only) | No |
| `/swagger/v1/swagger.json` | GET | OpenAPI specification | No |

#### GET /

Returns service information.

**Response:**
```json
{
  "service": "Dhadgar.Nodes",
  "message": "Hello from Dhadgar.Nodes"
}
```

#### GET /hello

Returns a simple text message for smoke testing.

**Response:**
```
Hello from Dhadgar.Nodes
```

#### GET /healthz

Returns comprehensive health check results.

**Response:**
```json
{
  "service": "Dhadgar.Nodes",
  "status": "ok",
  "timestamp": "2024-01-15T10:30:00Z",
  "checks": {
    "self": {
      "status": "Healthy",
      "duration_ms": 0.5
    }
  }
}
```

### Planned Endpoints

These endpoints will be implemented:

#### Node Management

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/nodes` | GET | List all nodes for organization |
| `/nodes` | POST | Create a node (manual registration) |
| `/nodes/{id}` | GET | Get node details |
| `/nodes/{id}` | PATCH | Update node (name, tags, etc.) |
| `/nodes/{id}` | DELETE | Decommission a node |
| `/nodes/{id}/status` | GET | Get node status and health |

#### Enrollment

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/enrollment/tokens` | GET | List enrollment tokens |
| `/enrollment/tokens` | POST | Create enrollment token |
| `/enrollment/tokens/{id}` | GET | Get token details |
| `/enrollment/tokens/{id}` | DELETE | Revoke token |
| `/enrollment/complete` | POST | Complete enrollment (agent) |

#### Health Monitoring

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/agents/heartbeat` | POST | Receive agent heartbeat |
| `/nodes/{id}/health` | GET | Get health history |
| `/nodes/{id}/health/latest` | GET | Get latest health report |
| `/nodes/{id}/health/stats` | GET | Get health statistics |

#### Capacity

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/nodes/{id}/capacity` | GET | Get node capacity |
| `/nodes/capacity/available` | GET | Find nodes with capacity |
| `/nodes/capacity/reserve` | POST | Reserve capacity |
| `/nodes/capacity/release` | POST | Release capacity |

#### Maintenance

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/nodes/{id}/maintenance/enter` | POST | Enter maintenance mode |
| `/nodes/{id}/maintenance/exit` | POST | Exit maintenance mode |

---

## Integration Points

### Upstream Services (Nodes Depends On)

| Service | Integration Type | Purpose |
|---------|-----------------|---------|
| **Identity** | HTTP API | Validate user tokens, get organization context |
| **Secrets** | HTTP API | Store/retrieve agent certificates |
| **Gateway** | YARP Routing | All external traffic comes through Gateway |

### Downstream Services (Depend on Nodes)

| Service | Integration Type | Purpose |
|---------|-----------------|---------|
| **Servers** | HTTP API / Messaging | Query available nodes for server placement |
| **Tasks** | HTTP API / Messaging | Reserve capacity, assign work to nodes |
| **Files** | Messaging | Coordinate file transfers to nodes |
| **Notifications** | Messaging | Alert on node status changes |

### Agent Communication

Agents on customer hardware communicate with the Nodes service:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        Customer Network                                  │
│                                                                          │
│    ┌────────────────┐     ┌────────────────┐     ┌────────────────┐    │
│    │   Agent        │     │   Agent        │     │   Agent        │    │
│    │  (Linux)       │     │  (Windows)     │     │  (Linux)       │    │
│    │                │     │                │     │                │    │
│    │  Node A        │     │  Node B        │     │  Node C        │    │
│    └───────┬────────┘     └───────┬────────┘     └───────┬────────┘    │
│            │                      │                      │              │
│            │    Outbound HTTPS    │                      │              │
│            │    (mTLS planned)    │                      │              │
│            │                      │                      │              │
└────────────┼──────────────────────┼──────────────────────┼──────────────┘
             │                      │                      │
             v                      v                      v
┌─────────────────────────────────────────────────────────────────────────┐
│                        Meridian Console Cloud                            │
│                                                                          │
│    ┌────────────────────────────────────────────────────────────────┐   │
│    │                        Gateway                                  │   │
│    │                   (Rate Limiting, Auth)                        │   │
│    └────────────────────────────────────────────────────────────────┘   │
│                                  │                                       │
│                                  v                                       │
│    ┌────────────────────────────────────────────────────────────────┐   │
│    │                     Nodes Service                               │   │
│    │                                                                 │   │
│    │  /api/v1/agents/heartbeat  - Receive heartbeats                │   │
│    │  /api/v1/agents/status     - Report status changes             │   │
│    │  /api/v1/agents/inventory  - Update hardware inventory         │   │
│    │                                                                 │   │
│    └────────────────────────────────────────────────────────────────┘   │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

**Key Design Points:**
- Agents make **outbound-only** connections (no inbound firewall rules needed)
- Heartbeats sent every 30 seconds (configurable)
- Failed heartbeats trigger offline detection after threshold
- mTLS planned for mutual authentication

### Messaging Integration (MassTransit)

The Nodes service will publish and consume messages via RabbitMQ:

#### Published Events (Planned)

| Event | When Published |
|-------|----------------|
| `NodeEnrolled` | New node completes enrollment |
| `NodeOnline` | Node transitions to online state |
| `NodeOffline` | Node missed heartbeat threshold |
| `NodeDegraded` | Node health score drops below threshold |
| `NodeMaintenanceEntered` | Node enters maintenance mode |
| `NodeMaintenanceExited` | Node exits maintenance mode |
| `NodeDecommissioned` | Node is permanently removed |
| `CapacityReserved` | Capacity reserved for pending work |
| `CapacityReleased` | Reserved capacity released |

#### Consumed Commands (Planned)

| Command | From Service | Purpose |
|---------|-------------|---------|
| `ReserveCapacity` | Tasks | Reserve node capacity for server |
| `ReleaseCapacity` | Tasks | Release previously reserved capacity |
| `QueryAvailableNodes` | Servers | Find nodes matching criteria |

---

## Configuration Reference

### Application Settings

The service is configured via `appsettings.json`:

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

### Configuration Hierarchy

Configuration is loaded in this order (later sources override earlier):

1. `appsettings.json` - Base configuration
2. `appsettings.{Environment}.json` - Environment-specific overrides
3. Environment variables - Production secrets
4. User secrets (Development) - Local development secrets
5. Command-line arguments - Runtime overrides

### Environment Variables

| Variable | Purpose | Example |
|----------|---------|---------|
| `ConnectionStrings__Postgres` | PostgreSQL connection | `Host=...` |
| `ConnectionStrings__RabbitMqHost` | RabbitMQ host | `localhost` |
| `RabbitMq__Username` | RabbitMQ user | `dhadgar` |
| `RabbitMq__Password` | RabbitMQ password | `********` |
| `OpenTelemetry__OtlpEndpoint` | OTLP collector URL | `http://localhost:4317` |
| `ASPNETCORE_ENVIRONMENT` | Runtime environment | `Development` / `Production` |
| `ASPNETCORE_URLS` | Listen URLs | `http://+:8080` |

### User Secrets (Development)

For local development, use user secrets to avoid committing sensitive values:

```bash
# Initialize user secrets
dotnet user-secrets init --project src/Dhadgar.Nodes

# Set secrets
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=...;Password=..." --project src/Dhadgar.Nodes
dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317" --project src/Dhadgar.Nodes

# List secrets
dotnet user-secrets list --project src/Dhadgar.Nodes
```

### Planned Configuration Options

Future configuration sections:

```json
{
  "Nodes": {
    "Heartbeat": {
      "IntervalSeconds": 30,
      "OfflineThresholdSeconds": 120,
      "DegradedThresholdSeconds": 60
    },
    "Enrollment": {
      "TokenExpirationHours": 24,
      "MaxActiveTokensPerOrg": 10
    },
    "Capacity": {
      "ReservationBufferPercent": 10,
      "ReservationTimeoutMinutes": 15
    },
    "HealthRetention": {
      "DetailedHistoryDays": 7,
      "AggregatedHistoryDays": 90
    }
  },
  "mTLS": {
    "CertificateValidityDays": 365,
    "RenewalThresholdDays": 30,
    "CaSecretPath": "nodes/ca-certificate"
  }
}
```

---

## Testing

### Test Project Structure

```
tests/Dhadgar.Nodes.Tests/
├── HelloWorldTests.cs              # Basic smoke tests
├── SwaggerTests.cs                 # API documentation tests
├── NodesWebApplicationFactory.cs   # Test host configuration
└── Dhadgar.Nodes.Tests.csproj      # Test project file
```

### Running Tests

```bash
# All tests
dotnet test tests/Dhadgar.Nodes.Tests

# With verbosity
dotnet test tests/Dhadgar.Nodes.Tests -v normal

# Specific test
dotnet test tests/Dhadgar.Nodes.Tests --filter "FullyQualifiedName~HelloWorldTests"

# With coverage (requires coverlet.collector)
dotnet test tests/Dhadgar.Nodes.Tests --collect:"XPlat Code Coverage"
```

### Test Categories

| Test Class | Description |
|------------|-------------|
| `HelloWorldTests` | Verifies basic service message |
| `SwaggerTests` | Verifies OpenAPI documentation |

### WebApplicationFactory

The `NodesWebApplicationFactory` class configures the test host:

```csharp
public class NodesWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace PostgreSQL with in-memory database
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<NodesDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<NodesDbContext>(options =>
            {
                options.UseInMemoryDatabase("NodesTestDb");
            });
        });
    }
}
```

### Writing New Tests

Example of an integration test:

```csharp
public class NodeEndpointTests : IClassFixture<NodesWebApplicationFactory>
{
    private readonly NodesWebApplicationFactory _factory;

    public NodeEndpointTests(NodesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealthz_ReturnsHealthyStatus()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/healthz");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", content);
    }
}
```

---

## Deployment

### Docker Images

Two Dockerfiles are provided:

#### Development Dockerfile (`Dockerfile`)

Full multi-stage build for local development:

```bash
# Build from solution root
docker build -f src/Dhadgar.Nodes/Dockerfile -t dhadgar/nodes:latest .

# Run
docker run -p 5004:8080 \
  -e ConnectionStrings__Postgres="Host=host.docker.internal;..." \
  dhadgar/nodes:latest
```

#### Pipeline Dockerfile (`Dockerfile.pipeline`)

Optimized for CI/CD - uses pre-built artifacts:

```bash
# Used by Azure Pipelines
# Artifacts are built in separate stage and passed to container build
```

### Container Specifications

| Property | Value |
|----------|-------|
| Base Image | `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` |
| Exposed Port | 8080 |
| User | `appuser` (non-root) |
| Health Check | `curl -f http://localhost:8080/healthz` |

### Environment Configuration

Production environment variables:

```bash
# Required
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__Postgres=Host=...;Port=5432;Database=...;Username=...;Password=...
RabbitMq__Username=...
RabbitMq__Password=...

# Optional
OpenTelemetry__OtlpEndpoint=http://otel-collector:4317
```

### Kubernetes (Planned)

Kubernetes manifests will be provided in `deploy/kubernetes/` including:

- Deployment
- Service
- ConfigMap
- Secret (sealed)
- HorizontalPodAutoscaler
- PodDisruptionBudget

### Health Checks

The service exposes three health endpoints:

| Endpoint | Purpose | Use Case |
|----------|---------|----------|
| `/healthz` | Full health | Debugging, monitoring |
| `/livez` | Liveness | Kubernetes liveness probe |
| `/readyz` | Readiness | Kubernetes readiness probe |

---

## Observability

### OpenTelemetry Integration

The service is fully instrumented with OpenTelemetry:

```csharp
// From Program.cs
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation();
    });
```

### Distributed Tracing

All requests include correlation IDs:

| Header | Description |
|--------|-------------|
| `X-Correlation-ID` | Links related requests across services |
| `X-Request-ID` | Unique ID for this specific request |
| `traceparent` | W3C Trace Context propagation |

### Metrics

Automatically collected metrics:

| Metric | Type | Description |
|--------|------|-------------|
| `http.server.request.duration` | Histogram | Request duration |
| `http.server.active_requests` | Gauge | Active requests |
| `process.cpu.utilization` | Gauge | CPU usage |
| `process.memory.usage` | Gauge | Memory usage |
| `runtime.gc.count` | Counter | GC collections |

### Logging

Structured logging with correlation:

```csharp
logger.LogInformation(
    "Processing heartbeat from node {NodeId}",
    nodeId);
```

Logs include:
- Correlation ID
- Request ID
- Trace ID
- Timestamp
- Log level
- Message template
- Structured properties

### Local Observability Stack

Start the observability stack:

```bash
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

Access:
- **Grafana**: http://localhost:3000 (admin/admin)
- **Prometheus**: http://localhost:9090
- **Loki**: http://localhost:3100

Enable OTLP export:

```bash
dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317" --project src/Dhadgar.Nodes
```

---

## Security Considerations

### Agent Authentication

Agents will authenticate using mTLS (mutual TLS):

1. **Enrollment Token** - One-time token for initial enrollment
2. **Client Certificate** - Issued during enrollment for ongoing auth
3. **Certificate Rotation** - Automatic renewal before expiration
4. **Revocation** - Ability to revoke compromised certificates

### Data Protection

| Data Type | Protection |
|-----------|------------|
| Enrollment tokens | Hashed in database |
| Agent certificates | Stored in Secrets service |
| Hardware inventory | Encrypted at rest (PostgreSQL) |
| Health metrics | Encrypted at rest |

### Rate Limiting

The Gateway applies rate limits:

| Policy | Limit | Window |
|--------|-------|--------|
| `PerTenant` | 100 requests | Per second |
| `PerAgent` | 500 requests | 60 seconds |

### Input Validation

All endpoints will validate:

- Request body size limits
- String length limits
- Required fields
- UUID format
- Numeric ranges

### Audit Logging

Security-relevant events will be logged:

- Enrollment token creation
- Node enrollment completion
- Certificate issuance
- Certificate revocation
- Maintenance mode changes
- Node decommissioning

---

## Related Documentation

### Project Documentation

| Document | Location |
|----------|----------|
| Main README | `/CLAUDE.md` |
| Development Setup | `/docs/DEVELOPMENT_SETUP.md` |
| Configuration Management | `/docs/CONFIGURATION-MANAGEMENT.md` |
| Gateway Operations | `/docs/runbooks/gateway-operations.md` |
| Container Optimization | `/docs/CONTAINER-OPTIMIZATION-GUIDE.md` |

### Related Services

| Service | Purpose | Port |
|---------|---------|------|
| [Gateway](/src/Dhadgar.Gateway/) | API routing, auth | 5000 |
| [Identity](/src/Dhadgar.Identity/) | Authentication | 5010 |
| [Servers](/src/Dhadgar.Servers/) | Server management | 5030 |
| [Tasks](/src/Dhadgar.Tasks/) | Job orchestration | 5050 |
| [Secrets](/src/Dhadgar.Secrets/) | Secret management | 5110 |

### Agent Projects

| Project | Purpose |
|---------|---------|
| [Agent.Core](/src/Agents/Dhadgar.Agent.Core/) | Shared agent logic |
| [Agent.Linux](/src/Agents/Dhadgar.Agent.Linux/) | Linux agent |
| [Agent.Windows](/src/Agents/Dhadgar.Agent.Windows/) | Windows agent |

### Shared Libraries

| Library | Purpose |
|---------|---------|
| [Contracts](/src/Shared/Dhadgar.Contracts/) | DTOs, message contracts |
| [ServiceDefaults](/src/Shared/Dhadgar.ServiceDefaults/) | Common middleware |
| [Messaging](/src/Shared/Dhadgar.Messaging/) | MassTransit conventions |
| [Shared](/src/Shared/Dhadgar.Shared/) | Utilities, primitives |

### External Resources

- [ASP.NET Core Documentation](https://docs.microsoft.com/en-us/aspnet/core/)
- [Entity Framework Core](https://docs.microsoft.com/en-us/ef/core/)
- [MassTransit Documentation](https://masstransit.io/)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/instrumentation/net/)
- [PostgreSQL Documentation](https://www.postgresql.org/docs/)

---

## Appendix: Service Ports Reference

All Meridian Console services and their default ports:

| Service | Port | Description |
|---------|------|-------------|
| Gateway | 5000 | API Gateway (public entry) |
| Identity | 5010 | Authentication/Authorization |
| Billing | 5020 | Subscriptions (SaaS) |
| Servers | 5030 | Server management |
| **Nodes** | **5004** | **Hardware inventory (this service)** |
| Tasks | 5050 | Job orchestration |
| Files | 5060 | File management |
| Console | 5070 | Real-time console (SignalR) |
| Mods | 5080 | Mod registry |
| Notifications | 5090 | Alerts and notifications |
| Firewall | 5100 | Port/policy management |
| Secrets | 5110 | Secret storage |
| Discord | 5120 | Discord integration |
| BetterAuth | 5130 | Alternative auth provider |

**Note**: The CLAUDE.md file indicates port 5040 for Nodes, but `launchSettings.json` configures 5004. The Gateway configuration shows 5040 in the cluster definition. Verify the correct port based on your deployment configuration.
