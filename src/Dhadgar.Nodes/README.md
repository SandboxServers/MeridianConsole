# Dhadgar.Nodes Service

![Status: Production](https://img.shields.io/badge/Status-Production-green)

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
| --- | --- |
| **Node** | A physical or virtual machine that can host game servers. Owned by the customer. |
| **Agent** | Software installed on customer hardware that communicates with the control plane via outbound HTTPS. |
| **Enrollment Token** | One-time use token for agent enrollment (SHA-256 hashed, never stored in plaintext). |
| **Heartbeat** | Periodic health reports sent by agents (CPU, memory, disk usage, running servers). |
| **Capacity Reservation** | Temporary resource lock to prevent over-provisioning during server deployment. |
| **mTLS Certificate** | Client certificate issued to agents for mutual TLS authentication. |

### Technology Stack

| Technology | Version | Purpose |
| --- | --- | --- |
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
| --- | --- | --- |
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
| --- | --- | --- |
| `status` | string | Filter by status (Online, Degraded, etc.) |
| `platform` | string | Filter by platform (linux, windows) |
| `minHealthScore` | int | Minimum health score (0-100) |
| `maxHealthScore` | int | Maximum health score (0-100) |
| `hasActiveServers` | bool | Filter by active servers |
| `search` | string | Search by name |
| `tags` | string (comma-separated) | Filter by tags |
| `page` | int | Page number (default: 1) |
| `pageSize` | int | Items per page (default: 20, max: 100) |

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
node_healths
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

Recent migrations:

- `20261027130000_AddMassTransitOutboxEntities` creates the MassTransit outbox and inbox tables (`InboxState`, `OutboxState`, `OutboxMessage`). `NodesDbContext` registered them earlier, but no migration created them (P16-ruling-1).
- `20261027130100_AddAgentCertificateGraceUntil` adds the nullable `GraceUntil` column to `agent_certificates` (renewal grace window, D-S212).

---

## Events and Messaging

### Published Events

All events are published to RabbitMQ via MassTransit. Contracts are defined in `Dhadgar.Contracts.Nodes`.

**Node Lifecycle Events:**

| Event | When Published |
| --- | --- |
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
| --- | --- |
| `AgentCertificateIssued` | New certificate issued during enrollment |
| `AgentCertificateRevoked` | Certificate is revoked |
| `AgentCertificateRenewed` | Certificate is renewed |

**Capacity Events:**

| Event | When Published |
| --- | --- |
| `CapacityReserved` | Capacity reservation created |
| `CapacityClaimed` | Reservation claimed by server |
| `CapacityReleased` | Reservation explicitly released |
| `CapacityReservationExpired` | Reservation expired without claim |

### Commands

| Command | Purpose |
| --- | --- |
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
| --- | --- | --- |
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
| --- | --- |
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
4. **Certificate Renewal** - Auto-renewal before expiration (90 days default). The agent generates a new key pair and sends only its public key (base64 SubjectPublicKeyInfo) in `RenewCertificateRequest.PublicKey`. Nodes issues a certificate bound to that key. The current thumbprint comes only from the mTLS client certificate (`certificate_thumbprint` claim), never from the body. No private key or PKCS#12 bundle is created or returned. The response carries the new certificate PEM, the CA PEM, and the validity window. Errors: 401 `certificate_required` or `thumbprint_mismatch`, 400 `invalid_public_key` or `certificate_renewal_failed`, 403 `forbidden` when the certificate's node does not match the URL.
5. **Revocation** - Certificates can be revoked via API

#### mTLS Protected Paths

`MtlsMiddleware` validates the client certificate on these paths:

- `/agents/*` (the `Mtls:AgentEndpointPrefix`), except the exempt paths `/enroll` and `/ca-certificate`.
- `/hubs/agent` and its sub-paths (`Mtls:HubPathPrefix`). Matching is by whole path segment, so `/hubs/agentx` is not protected.

Errors on these paths are `application/problem+json`: 401 with an `errorCode` such as `certificate_required` or `invalid_issuer`, or 503 `certificate_validation_unavailable` when certificate trust cannot be determined (CA not loaded or revocation lookup failed). The CA service is a singleton and opens a new DI scope for each revocation lookup.

#### Forwarded Client Certificate (`Mtls:ForwardedCertificate`)

The Gateway terminates TLS for agents on a DNS-only hostname and forwards the client certificate in the `X-Client-Cert` header (base64 DER). Nodes accepts that header only when all of these hold:

- `Mtls:ForwardedCertificate:Enabled` is `true`;
- the remote address is inside one of `Mtls:ForwardedCertificate:TrustedProxies` (CIDR ranges, for example the Gateway pod CIDR);
- the header is present exactly once and is at most `MaxHeaderLength` characters (default 16384);
- the connection has no TLS client certificate of its own;
- the value parses as base64 DER.

The header is removed from every request. Nodes refuses to start when `Enabled` is `true` and `TrustedProxies` is empty or holds an invalid CIDR. The production ingress and trust model is D-S210 (docs/analysis/sprint2-agent-nodes). Nodes still validates the chain, SPIFFE ID and revocation.

#### `AgentMtls` Authentication Scheme

Policies on agent endpoints use the `AgentMtls` scheme (`AgentMtlsAuthenticationHandler.SchemeName`). It does not authenticate by itself. `MtlsMiddleware` sets `HttpContext.User` after `UseAuthentication`. A challenge writes 401 `certificate_required`, and a forbid writes 403 `forbidden`, both as problem+json.

### Certificate Details

- **SPIFFE ID:** `spiffe://meridianconsole.com/nodes/{nodeId}`
- **Extended Key Usage:** Client Authentication (OID 1.3.6.1.5.5.7.3.2)
- **Key Size:** 2048-bit RSA (client), 4096-bit RSA (CA)

### Certificate Renewal and Grace Window

Renewal (`POST /agents/{nodeId}/certificates/renew`) supersedes the presented certificate, but keeps it usable for a short grace window (D-S212), so an agent whose renewal response was lost can renew again with the certificate it still holds.

- **Setting:** `Mtls:RenewalGraceMinutes` (default `60`, from `AgentCertificateRenewalPolicy.DefaultGraceMinutes`). `0` revokes immediately. The value must be in `0..1440`; startup fails otherwise.
- **Usable rule:** a certificate row is usable when it is not revoked, or when its `GraceUntil` is in the future. `AgentCertificateState.IsUsable` is the only implementation. Certificate validation, the hub's connect and per-heartbeat check, and the renew endpoint all use it.
- **Renew from grace:** a certificate in grace may renew. Its `GraceUntil` is not extended. Any other row of the node that is still active or in grace is hard-revoked with no grace.
- **Decommission:** ends any renewal grace on the node's certificates at once.

### Rate Limiting

Applied at Gateway level:

| Policy | Limit | Scope |
|:-------|:------|:------|
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
| --- | --- |
| `/healthz` | Full health check |
| `/livez` | Kubernetes liveness probe |
| `/readyz` | Kubernetes readiness probe |

---

## Agent hub

Agents hold a persistent SignalR connection to the Nodes service for heartbeats.

- **Path:** `/hubs/agent` (`AgentHubProtocol.Path`). The Gateway routes the same path.
- **Auth:** the hub is mapped with `RequireAuthorization("AgentPolicy")`, and mTLS must be enabled. The node ID is taken only from the `node_id` claim of the client certificate, never from the payload. A certificate that is revoked and outside its renewal grace, or past its `NotAfter` time (unless `AllowExpiredCertificates` is set), is refused at connect, and a heartbeat on an open connection is refused and the connection is closed. The hub applies the same usable rule on connect and on every heartbeat, so a certificate in grace keeps its hub connection until `GraceUntil`.
- **Protocol header:** `X-Agent-Protocol`. Only `1` is accepted. A missing header is treated as `1`. Any other value aborts the connection.
- **`Heartbeat(AgentHeartbeat)`:** returns `AgentHeartbeatAck`. Limits are enforced by `AgentHeartbeatValidator` before any database write:

  | Field | Limit |
  |-------|-------|
  | `CpuUsagePercent`, `MemoryUsagePercent`, `DiskUsagePercent` | finite, 0-100 |
  | `ActiveGameServers` | 0-10000 |
  | `AgentVersion` | at most 50 characters, no control characters |
  | `HealthIssues` | at most 20 entries, each non-null, at most 256 characters, no control characters |

- **Error codes:** returned in `AgentHeartbeatAck.ErrorCode`, never as a `HubException`:

  | Code | Meaning |
  |------|---------|
  | `node_not_found` | No node matches the certificate's node ID |
  | `node_decommissioned` | The node is decommissioned; no heartbeat is written |
  | `invalid_heartbeat` | The payload failed validation; nothing was written |
  | `heartbeat_failed` | Any other failure, including a revoked or expired certificate |

---

## Related Documentation

| Document | Location |
| --- | --- |
| Main README | `/README.md` |
| Contracts Library | `/docs/libraries/contracts.md` |
| Messaging Library | `/docs/libraries/messaging.md` |
| Gateway Routing | `/docs/gateway-routing-reference.md` |

### Related Services

| Service | Purpose | Port |
| --- | --- | --- |
| Gateway | API routing | 5000 |
| Identity | Authentication | 5010 |
| Servers | Server management | 5030 |
| Tasks | Job orchestration | 5050 |

### Agent Projects

| Project | Purpose |
| --- | --- |
| `Agent.Core` | Shared agent logic |
| `Agent.Linux` | Linux-specific agent |
| `Agent.Windows` | Windows-specific agent |
