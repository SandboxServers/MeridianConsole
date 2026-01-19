# Architecture

**Analysis Date:** 2025-01-19

## Pattern Overview

**Overall:** Microservices with API Gateway

**Key Characteristics:**
- Single entry point via YARP reverse proxy (Gateway)
- Services own their data (database-per-service)
- Async messaging via RabbitMQ/MassTransit for events
- Shared contracts library for DTOs and message definitions
- Frontend applications deployed as static web apps (Astro/React)

## Layers

**Gateway Layer:**
- Purpose: Single public entry point, routing, rate limiting, authentication
- Location: `src/Dhadgar.Gateway/`
- Contains: YARP configuration, rate limiting policies, security middleware
- Depends on: ServiceDefaults middleware
- Used by: All external traffic, frontend applications

**Service Layer (13 Backend Services):**
- Purpose: Domain-specific business logic
- Location: `src/Dhadgar.{ServiceName}/`
- Contains: API endpoints, domain logic, data access
- Depends on: Shared libraries (Contracts, Messaging, ServiceDefaults)
- Used by: Gateway (HTTP proxy), other services (via messaging)

**Shared Libraries:**
- Purpose: Cross-cutting concerns and contracts
- Location: `src/Shared/`
- Contains:
  - `Dhadgar.Contracts/` - DTOs, message contracts, events
  - `Dhadgar.Messaging/` - MassTransit/RabbitMQ configuration
  - `Dhadgar.ServiceDefaults/` - Common middleware, health checks
  - `Dhadgar.Shared/` - Utilities and primitives

**Agent Layer:**
- Purpose: Customer-hosted components for node management
- Location: `src/Agents/`
- Contains: Core logic and platform-specific agents (Linux/Windows)
- Depends on: Contracts, Shared
- Used by: Nodes service (outbound connections only)

**Frontend Layer:**
- Purpose: User interfaces
- Location: `src/Dhadgar.Scope/`, `src/Dhadgar.Panel/`, `src/Dhadgar.ShoppingCart/`
- Contains: Astro pages, React components, Tailwind styling
- Depends on: Backend APIs via Gateway
- Used by: End users

## Data Flow

**HTTP Request Flow:**

1. Client sends request to Gateway (`https://api.meridianconsole.com/api/v1/{service}/*`)
2. Gateway applies: CORS, security headers, correlation ID, rate limiting, authentication
3. YARP routes request to appropriate backend service based on path prefix
4. Backend service processes request and returns response
5. Response flows back through Gateway to client

**Authentication Flow:**

1. User authenticates via Better Auth (Node.js) at `src/Dhadgar.BetterAuth/`
2. Better Auth issues token
3. Token exchanged with Identity service (`POST /exchange`) for JWT
4. JWT used for subsequent API calls via Gateway
5. Gateway validates JWT and enriches request with claims

**Event-Driven Flow (Messaging):**

1. Service publishes event to RabbitMQ via MassTransit
2. Exchange routes message to subscribed queues
3. Consumer services receive and process events
4. Example: Identity publishes `UserAuthenticated` -> Notifications consumes it

**State Management:**
- Each service owns its PostgreSQL database
- Redis used for caching (e.g., token replay prevention in Identity)
- No shared database access between services
- State synchronization via events only

## Key Abstractions

**Service Program.cs Pattern:**
- Purpose: Standardized service initialization
- Examples: `src/Dhadgar.Servers/Program.cs`, `src/Dhadgar.Nodes/Program.cs`
- Pattern: Minimal API with `AddDhadgarServiceDefaults()`, middleware pipeline, `MapDhadgarDefaultEndpoints()`

**Message Contracts:**
- Purpose: Strongly-typed inter-service communication
- Examples:
  - `src/Shared/Dhadgar.Contracts/Identity/IdentityEvents.cs`
  - `src/Shared/Dhadgar.Contracts/Notifications/NotificationContracts.cs`
  - `src/Shared/Dhadgar.Contracts/Discord/DiscordContracts.cs`
- Pattern: Records with immutable properties, `OccurredAtUtc` timestamp

**DbContext Pattern:**
- Purpose: EF Core database access per service
- Examples:
  - `src/Dhadgar.Identity/Data/IdentityDbContext.cs`
  - `src/Dhadgar.Servers/Data/ServersDbContext.cs`
- Pattern: Migrations in `Data/Migrations/`, auto-migrate in Development

## Entry Points

**API Gateway:**
- Location: `src/Dhadgar.Gateway/Program.cs`
- Triggers: All external HTTP traffic
- Responsibilities: Routing, rate limiting, authentication, CORS, security headers

**Identity Service:**
- Location: `src/Dhadgar.Identity/Program.cs`
- Triggers: Authentication requests (`/exchange`, `/connect/token`, OAuth callbacks)
- Responsibilities: Token issuance, user management, RBAC, OpenIddict integration

**CLI Tool:**
- Location: `src/Dhadgar.Cli/Program.cs`
- Triggers: Command-line invocation (`dhadgar <command>`)
- Responsibilities: Administrative operations, service diagnostics

**Message Consumers:**
- Location: `src/Dhadgar.{Service}/Consumers/`
- Triggers: RabbitMQ messages
- Responsibilities: Async event processing
- Examples:
  - `src/Dhadgar.Discord/Consumers/SendDiscordNotificationConsumer.cs`
  - `src/Dhadgar.Notifications/Consumers/ServerStartedConsumer.cs`

## Error Handling

**Strategy:** RFC 7807 Problem Details

**Patterns:**
- `ProblemDetailsMiddleware` catches exceptions and returns structured JSON
- Services return `Results.Problem()` for error conditions
- Correlation IDs tracked across all requests for debugging

## Cross-Cutting Concerns

**Logging:**
- Structured logging via OpenTelemetry
- Correlation IDs added by `CorrelationMiddleware`
- OTLP export to Grafana/Loki stack (optional)

**Validation:**
- Request body validation via data annotations
- Secret name validation in Secrets service (`src/Dhadgar.Secrets/Validation/`)
- Rate limiting on sensitive endpoints

**Authentication:**
- JWT Bearer via OpenIddict (Identity service issues tokens)
- API Key auth for admin endpoints (Discord, Notifications)
- OAuth2/OIDC for external identity providers (Steam, Discord, Epic, etc.)

## Service Dependencies

**Services with Databases (PostgreSQL):**
- Identity, Billing, Servers, Nodes, Tasks, Files, Mods, Discord, Notifications

**Services with Message Consumers:**
- Discord (SendDiscordNotificationConsumer)
- Notifications (ServerStartedConsumer, ServerStoppedConsumer, ServerCrashedConsumer, SendEmailNotificationConsumer)

**Services with Real-Time Features:**
- Console (SignalR hub at `/hubs/console`)

**Service Port Assignments (local dev):**
| Service | Port |
|---------|------|
| Gateway | 5000 |
| Identity | 5010 |
| Billing | 5020 |
| Servers | 5030 |
| Nodes | 5040 |
| Tasks | 5050 |
| Files | 5060 |
| Console | 5070 |
| Mods | 5080 |
| Notifications | 5090 |
| Firewall | 5100 |
| Secrets | 5110 |
| Discord | 5120 |
| BetterAuth | 5130 |

## Routing Configuration

**Gateway YARP Routes (from `src/Dhadgar.Gateway/appsettings.json`):**
- `/api/v1/identity/*` -> identity cluster (Anonymous, Auth rate limit)
- `/api/v1/betterauth/*` -> betterauth cluster (Anonymous, Auth rate limit)
- `/api/v1/servers/*` -> servers cluster (TenantScoped, PerTenant rate limit)
- `/api/v1/nodes/*` -> nodes cluster (TenantScoped, PerTenant rate limit)
- `/api/v1/agents/*` -> nodes cluster (Agent policy, PerAgent rate limit)
- `/hubs/console/*` -> console cluster (TenantScoped, session affinity)

**Authorization Policies:**
- `TenantScoped` - Requires authenticated user
- `Agent` - Requires `client_type: agent` claim
- `DenyAll` - Blocks internal endpoints from external access

---

*Architecture analysis: 2025-01-19*
