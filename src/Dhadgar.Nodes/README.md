# Dhadgar.Nodes Service

The Nodes service is the **hardware inventory and agent management hub** for the Meridian Console platform. It acts as the bridge between the cloud-hosted control plane and customer-hosted agents running on their physical hardware.

## Table of Contents

1. [Overview](#overview)
2. [Quick Start](#quick-start)
3. [Architecture](#architecture)
4. [API Reference](#api-reference)
5. [Database Schema](#database-schema)
6. [Events and Messaging](#events-and-messaging)
7. [Configuration](#configuration)
8. [Security](#security)
9. [Testing](#testing)
10. [Deployment](#deployment)

---

## Overview

### What is the Nodes Service?

In the Meridian Console architecture, customers own and operate their own hardware (physical servers, VMs, or cloud instances). The platform orchestrates game servers on that hardware through customer-hosted agents. The Nodes service is the central registry for all enrolled hardware.

**Core Responsibilities:**

| Responsibility | Description |
| -------------- | ----------- |
| **Node Registration** | Track all hardware nodes enrolled in the platform |
| **Agent Enrollment** | Manage secure enrollment with one-time tokens and mTLS certificates |
| **Health Monitoring** | Receive and process heartbeats from agents |
| **Capacity Management** | Track available resources and manage reservations for server placement |
| **Status Tracking** | Maintain node lifecycle states (Online, Degraded, Offline, etc.) |
| **Certificate Authority** | Issue, renew, and revoke mTLS certificates for agent authentication |

### Key Concepts

| Concept | Description |
|---------|-------------|
| **Node** | A physical or virtual machine that can host game servers. Owned by the customer. |
| **Agent** | Software installed on customer hardware that communicates with the control plane via outbound HTTPS. |
| **Enrollment Token** | One-time use token for agent enrollment (SHA-256 hashed, never stored in plaintext). |
| **Heartbeat** | Periodic health reports sent by agents (CPU, memory, disk usage, running servers). |
| **Capacity Reservation** | Temporary resource lock to prevent over-provisioning during server deployment. |
| **mTLS Certificate** | Client certificate issued to agents for mutual TLS authentication. |

### Technology Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 10.0 | Runtime and SDK |
| ASP.NET Core | 10.0 | Web framework (Minimal APIs) |
| PostgreSQL | 16 | Primary database |
| Entity Framework Core | 10.0 | ORM |
| MassTransit | 8.3.6 | Message bus (RabbitMQ) |
| OpenTelemetry | 1.14.0 | Distributed tracing, metrics, logging |

**Port:** 5040

---

## Quick Start

### Prerequisites

- .NET SDK 10.0.100 (`dotnet --version`)
- Docker (for local infrastructure)
- Git

### Step 1: Start Local Infrastructure

```bash
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

This starts PostgreSQL, RabbitMQ, Redis, and the observability stack.

### Step 2: Build and Run

```bash
# Build the solution
dotnet build

# Run the Nodes service
dotnet run --project src/Dhadgar.Nodes

# Or with hot reload
dotnet watch --project src/Dhadgar.Nodes
```

The service starts on `http://localhost:5040`.

### Step 3: Verify

```bash
# Service info
curl http://localhost:5040/

# Health check
curl http://localhost:5040/healthz

# Swagger UI (Development only)
open http://localhost:5040/swagger
```

### Running Tests

```bash
# All Nodes tests
dotnet test tests/Dhadgar.Nodes.Tests

# Specific test
dotnet test tests/Dhadgar.Nodes.Tests --filter "FullyQualifiedName~EnrollmentTests"
```

---

## Architecture

### Service Boundaries

```text
┌─────────────────────────────────────────────────────────────────────────┐
│                         Gateway (YARP)                                   │
│                    http://localhost:5000                                 │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
          ┌─────────────────────────┼─────────────────────────┐
          │                         │                         │
    /api/v1/organizations     /api/v1/agents          /api/v1/reservations
    /{orgId}/nodes/*          /enroll, /heartbeat      /{token}/*
          │                         │                         │
          v                         v                         v
┌─────────────────────────────────────────────────────────────────────────┐
│                         Nodes Service                                    │
│                    http://localhost:5040                                 │
│                                                                          │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐          │
│  │  Node CRUD      │  │  Enrollment     │  │  Heartbeat      │          │
│  │  Maintenance    │  │  Tokens & Flow  │  │  Processing     │          │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘          │
│                                                                          │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐          │
│  │  Capacity       │  │  Certificate    │  │  Background     │          │
│  │  Reservations   │  │  Authority      │  │  Services       │          │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘          │
└─────────────────────────────────────────────────────────────────────────┘
          │                         │                         │
          v                         v                         v
    ┌───────────┐            ┌───────────────┐         ┌───────────┐
    │ PostgreSQL│            │   RabbitMQ    │         │   Agents  │
    │  Database │            │   Messages    │         │ (Customer)│
    └───────────┘            └───────────────┘         └───────────┘
```

### Project Structure

```text
src/Dhadgar.Nodes/
├── Auth/                           # mTLS and authorization
│   ├── MtlsMiddleware.cs
│   ├── TenantScopedHandler.cs
│   └── CertificateValidationService.cs
├── BackgroundServices/             # Background workers
│   ├── StaleNodeDetectionService.cs
│   ├── ReservationExpiryService.cs
│   └── AuditLogCleanupService.cs
├── Consumers/                      # MassTransit message consumers
├── Data/
│   ├── Entities/                   # EF Core entities
│   │   ├── Node.cs
│   │   ├── NodeHealth.cs
│   │   ├── NodeHardwareInventory.cs
│   │   ├── NodeCapacity.cs
│   │   ├── NodeStatus.cs
│   │   ├── EnrollmentToken.cs
│   │   ├── AgentCertificate.cs
│   │   ├── CapacityReservation.cs
│   │   └── NodeAuditLog.cs
│   ├── Migrations/
│   └── NodesDbContext.cs
├── Endpoints/                      # API endpoints
│   ├── NodesEndpoints.cs
│   ├── EnrollmentEndpoints.cs
│   ├── AgentEndpoints.cs
│   └── ReservationEndpoints.cs
├── Services/                       # Business logic
│   ├── NodeService.cs
│   ├── HeartbeatService.cs
│   ├── EnrollmentService.cs
│   ├── EnrollmentTokenService.cs
│   ├── CapacityReservationService.cs
│   ├── CertificateAuthorityService.cs
│   └── HealthScoringService.cs
├── Audit/                          # Audit logging
├── Observability/                  # Metrics and tracing
├── appsettings.json
├── Program.cs
└── README.md
```

### Node Lifecycle State Machine

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

| State | Value | Description |
|-------|-------|-------------|
| Enrolling | 0 | Agent is completing enrollment |
| Online | 1 | Healthy, receiving heartbeats |
| Degraded | 2 | Online but reporting issues (high CPU/memory/disk) |
| Offline | 3 | No heartbeat within threshold (default: 5 minutes) |
| Maintenance | 4 | Intentionally taken offline for updates |
| Decommissioned | 5 | Permanently removed (soft-deleted) |

---

## API Reference

### Node Management

**Base path:** `/api/v1/organizations/{organizationId}/nodes`

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/` | List nodes with filtering, sorting, pagination |
| GET | `/{nodeId}` | Get node details |
| PATCH | `/{nodeId}` | Update node (name, displayName) |
| PUT | `/{nodeId}/tags` | Replace node tags |
| DELETE | `/{nodeId}` | Decommission node (soft delete) |
| POST | `/{nodeId}/maintenance` | Enter maintenance mode |
| DELETE | `/{nodeId}/maintenance` | Exit maintenance mode |

**Query Parameters for GET /**:

| Parameter | Type | Description |
|-----------|------|-------------|
| `status` | string | Filter by status (Online, Degraded, etc.) |
| `platform` | string | Filter by platform (linux, windows) |
| `minHealthScore` | int | Minimum health score (0-100) |
| `maxHealthScore` | int | Maximum health score (0-100) |
| `hasActiveServers` | bool | Filter by active servers |
| `search` | string | Search by name |
| `tags` | string[] | Filter by tags |
| `page` | int | Page number (default: 1) |
| `pageSize` | int | Items per page (default: 50, max: 100) |

### Enrollment Tokens

**Base path:** `/api/v1/organizations/{organizationId}/enrollment`

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/tokens` | Create enrollment token |
| GET | `/tokens` | List active tokens |
| DELETE | `/tokens/{tokenId}` | Revoke token |

### Agent Endpoints

**Base path:** `/api/v1/agents`

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/enroll` | Token | Complete agent enrollment |
| POST | `/{nodeId}/heartbeat` | mTLS | Submit health metrics |
| POST | `/{nodeId}/certificates/renew` | mTLS | Renew mTLS certificate |
| GET | `/ca-certificate` | None | Get CA certificate (public) |

### Capacity Reservations

**User-facing:** `/api/v1/organizations/{orgId}/nodes/{nodeId}/reservations`

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/` | Create reservation |
| GET | `/` | List active reservations |
| GET | `/capacity` | Get available capacity |

**Service-to-service:** `/api/v1/reservations/{token}`

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/` | Get reservation by token |
| POST | `/claim` | Claim reservation with server ID |
| DELETE | `/` | Release reservation |

---

## Database Schema

### Primary Entities

#### Node

```text
nodes
├── id (PK)                    UUID
├── organization_id (FK)       UUID
├── name                       VARCHAR(100) - unique within org
├── display_name               VARCHAR(200)
├── status                     INT (NodeStatus enum)
├── platform                   VARCHAR(20) - "linux" or "windows"
├── agent_version              VARCHAR(50)
├── last_heartbeat             TIMESTAMP
├── created_at                 TIMESTAMP
├── updated_at                 TIMESTAMP
├── deleted_at                 TIMESTAMP (soft delete)
├── tags                       JSONB (string array)
└── row_version                UINT (xmin for optimistic concurrency)
```

#### NodeHealth

```text
node_health
├── id (PK)                    UUID
├── node_id (FK)               UUID
├── cpu_usage_percent          DECIMAL (0-100)
├── memory_usage_percent       DECIMAL (0-100)
├── disk_usage_percent         DECIMAL (0-100)
├── active_game_servers        INT
├── health_issues              JSONB (string array)
├── health_score               INT (0-100)
├── health_trend               INT (enum)
└── reported_at                TIMESTAMP
```

#### EnrollmentToken

```text
enrollment_tokens
├── id (PK)                    UUID
├── organization_id (FK)       UUID
├── token_hash                 VARCHAR(64) - SHA-256 hash
├── label                      VARCHAR(200)
├── expires_at                 TIMESTAMP
├── used_at                    TIMESTAMP
├── used_by_node_id (FK)       UUID
├── created_by_user_id         VARCHAR(100)
├── is_revoked                 BOOL
└── created_at                 TIMESTAMP
```

#### CapacityReservation

```text
capacity_reservations
├── id (PK)                    UUID
├── node_id (FK)               UUID
├── reservation_token          UUID (unique)
├── memory_mb                  INT
├── disk_mb                    INT
├── cpu_millicores             INT
├── server_id                  VARCHAR(100)
├── requested_by               VARCHAR(100)
├── correlation_id             VARCHAR(100)
├── status                     INT (Pending/Claimed/Released/Expired)
├── created_at                 TIMESTAMP
├── expires_at                 TIMESTAMP
├── claimed_at                 TIMESTAMP
└── released_at                TIMESTAMP
```

### Database Indexes

```sql
-- Node queries
CREATE INDEX IX_Node_OrganizationId ON nodes(organization_id);
CREATE INDEX IX_Node_Status ON nodes(status) WHERE deleted_at IS NULL;
CREATE INDEX IX_Node_LastHeartbeat ON nodes(last_heartbeat) WHERE status = 1;

-- Enrollment token lookup
CREATE UNIQUE INDEX IX_EnrollmentToken_Hash ON enrollment_tokens(token_hash)
  WHERE used_at IS NULL AND is_revoked = false;

-- Certificate lookup
CREATE INDEX IX_AgentCertificate_NodeId ON agent_certificates(node_id) WHERE is_active = true;
CREATE INDEX IX_AgentCertificate_Thumbprint ON agent_certificates(thumbprint) WHERE revoked_at IS NULL;

-- Reservation lookup
CREATE INDEX IX_CapacityReservation_Token ON capacity_reservations(reservation_token);
CREATE INDEX IX_CapacityReservation_NodeId ON capacity_reservations(node_id) WHERE status = 0;
```

### EF Core Migrations

```bash
# Add a migration
dotnet ef migrations add MigrationName \
  --project src/Dhadgar.Nodes \
  --startup-project src/Dhadgar.Nodes \
  --output-dir Data/Migrations

# Apply migrations
dotnet ef database update \
  --project src/Dhadgar.Nodes \
  --startup-project src/Dhadgar.Nodes
```

**Note:** Migrations are auto-applied in Development mode.

---

## Events and Messaging

### Published Events

All events are published to RabbitMQ via MassTransit. Contracts are defined in `Dhadgar.Contracts.Nodes`.

**Node Lifecycle Events:**

| Event | When Published |
|-------|----------------|
| `NodeEnrolled` | New node completes enrollment |
| `NodeOnline` | Node transitions to online (first heartbeat) |
| `NodeOffline` | Node misses heartbeat threshold |
| `NodeDegraded` | Node reports health issues |
| `NodeRecovered` | Node recovers from degraded state |
| `NodeDecommissioned` | Node is permanently removed |
| `NodeMaintenanceStarted` | Node enters maintenance mode |
| `NodeMaintenanceEnded` | Node exits maintenance mode |

**Certificate Events:**

| Event | When Published |
|-------|----------------|
| `AgentCertificateIssued` | New certificate issued during enrollment |
| `AgentCertificateRevoked` | Certificate is revoked |
| `AgentCertificateRenewed` | Certificate is renewed |

**Capacity Events:**

| Event | When Published |
|-------|----------------|
| `CapacityReserved` | Capacity reservation created |
| `CapacityClaimed` | Reservation claimed by server |
| `CapacityReleased` | Reservation explicitly released |
| `CapacityReservationExpired` | Reservation expired without claim |

### Commands

| Command | Purpose |
|---------|---------|
| `CheckNodeHealth` | Request health check for a node |
| `UpdateNodeCapacity` | Update node's capacity configuration |

---

## Configuration

### Application Settings

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=dhadgar_platform;Username=dhadgar;Password=dhadgar",
    "RabbitMqHost": "localhost"
  },
  "RabbitMq": {
    "Username": "dhadgar",
    "Password": "dhadgar"
  },
  "Nodes": {
    "HeartbeatThresholdMinutes": 5,
    "StaleNodeCheckIntervalMinutes": 1,
    "DegradedCpuThreshold": 90,
    "DegradedMemoryThreshold": 90,
    "DegradedDiskThreshold": 90
  }
}
```

### Certificate Authority Configuration

| Option | Default | Description |
|--------|---------|-------------|
| `CaStorageType` | "local" | "local" or "azurekeyvault" |
| `CaStoragePath` | AppData/MeridianConsole/CA | Local CA storage path |
| `CaKeyPassword` | auto-generated | Password for CA private key |
| `CaKeySize` | 4096 | RSA key size for CA |
| `CaValidityYears` | 10 | CA certificate validity |
| `ClientKeySize` | 2048 | RSA key size for client certs |
| `CertificateValidityDays` | 90 | Agent certificate validity |
| `AzureKeyVaultUrl` | - | Azure Key Vault URL (production) |
| `AzureKeyVaultCaCertName` | "meridian-agent-ca" | CA certificate name in KV |

### Environment Variables

| Variable | Purpose |
|----------|---------|
| `ConnectionStrings__Postgres` | PostgreSQL connection string |
| `ConnectionStrings__RabbitMqHost` | RabbitMQ host |
| `RabbitMq__Username` | RabbitMQ username |
| `RabbitMq__Password` | RabbitMQ password |
| `Nodes__HeartbeatThresholdMinutes` | Offline detection threshold |
| `ASPNETCORE_ENVIRONMENT` | Runtime environment |

---

## Security

### Agent Authentication

Agents authenticate using mutual TLS (mTLS):

1. **Enrollment Token** - Admin creates one-time token (default: 1 hour validity)
2. **Agent Enrollment** - Agent presents token, receives mTLS certificate
3. **Heartbeat Auth** - Agent uses certificate for all subsequent requests
4. **Certificate Renewal** - Auto-renewal before expiration (90 days default)
5. **Revocation** - Certificates can be revoked via API

### Certificate Details

- **SPIFFE ID:** `spiffe://meridianconsole.com/nodes/{nodeId}`
- **Extended Key Usage:** Client Authentication (OID 1.3.6.1.5.5.7.3.2)
- **Key Size:** 2048-bit RSA (client), 4096-bit RSA (CA)

### Rate Limiting

Applied at Gateway level:

| Policy | Limit | Scope |
|--------|-------|-------|
| `PerTenant` | 100 req/sec | Organization |
| `PerAgent` | 500 req/60sec | Per agent |

### Audit Logging

Security-relevant events are logged to `node_audit_logs`:

- Enrollment attempts (success/failure)
- Certificate operations (issue/renew/revoke)
- Node decommissioning
- Maintenance mode changes

---

## Testing

### Test Structure

```text
tests/Dhadgar.Nodes.Tests/
├── Endpoints/
│   ├── NodesEndpointsTests.cs
│   ├── EnrollmentEndpointsTests.cs
│   └── AgentEndpointsTests.cs
├── Services/
│   ├── NodeServiceTests.cs
│   ├── HeartbeatServiceTests.cs
│   └── EnrollmentServiceTests.cs
├── NodesWebApplicationFactory.cs
└── Dhadgar.Nodes.Tests.csproj
```

### Running Tests

```bash
# All tests
dotnet test tests/Dhadgar.Nodes.Tests

# With coverage
dotnet test tests/Dhadgar.Nodes.Tests --collect:"XPlat Code Coverage"

# Specific category
dotnet test --filter "FullyQualifiedName~Enrollment"
```

### WebApplicationFactory

Tests use InMemory database with isolated provider to avoid conflicts:

```csharp
services.AddDbContext<NodesDbContext>(options =>
    options.UseInMemoryDatabase($"nodes-tests-{Guid.NewGuid()}")
        .UseInternalServiceProvider(efProvider));
```

---

## Deployment

### Docker

```bash
# Build image
docker build -f src/Dhadgar.Nodes/Dockerfile -t dhadgar/nodes:latest .

# Run
docker run -p 5040:8080 \
  -e ConnectionStrings__Postgres="Host=host.docker.internal;..." \
  dhadgar/nodes:latest
```

### Container Specifications

| Property | Value |
|----------|-------|
| Base Image | `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` |
| Exposed Port | 8080 |
| User | `appuser` (non-root) |
| Health Check | `curl -f http://localhost:8080/healthz` |

### Health Endpoints

| Endpoint | Purpose |
|----------|---------|
| `/healthz` | Full health check |
| `/livez` | Kubernetes liveness probe |
| `/readyz` | Kubernetes readiness probe |

---

## Related Documentation

| Document | Location |
|----------|----------|
| Main README | `/README.md` |
| Contracts Library | `/docs/libraries/contracts.md` |
| Messaging Library | `/docs/libraries/messaging.md` |
| Gateway Routing | `/docs/gateway-routing-reference.md` |

### Related Services

| Service | Purpose | Port |
|---------|---------|------|
| Gateway | API routing | 5000 |
| Identity | Authentication | 5010 |
| Servers | Server management | 5030 |
| Tasks | Job orchestration | 5050 |

### Agent Projects

| Project | Purpose |
|---------|---------|
| `Agent.Core` | Shared agent logic |
| `Agent.Linux` | Linux-specific agent |
| `Agent.Windows` | Windows-specific agent |
