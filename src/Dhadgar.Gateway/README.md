# Dhadgar.Gateway

The **API Gateway** is the single public entry point for all external traffic to the Meridian Console platform. It acts as a reverse proxy, routing requests to appropriate backend microservices while providing cross-cutting concerns such as authentication, rate limiting, security headers, CORS handling, circuit breaking, and distributed tracing.

## Table of Contents

- [Overview](#overview)
- [Tech Stack](#tech-stack)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
  - [Application Settings](#application-settings)
  - [Authentication Configuration](#authentication-configuration)
  - [CORS Configuration](#cors-configuration)
  - [Rate Limiting Configuration](#rate-limiting-configuration)
  - [Circuit Breaker Configuration](#circuit-breaker-configuration)
  - [Cloudflare Configuration](#cloudflare-configuration)
  - [Readiness Configuration](#readiness-configuration)
  - [OpenTelemetry Configuration](#opentelemetry-configuration)
- [Architecture](#architecture)
  - [Middleware Pipeline](#middleware-pipeline)
  - [Key Components](#key-components)
- [YARP Routing](#yarp-routing)
  - [Route Configuration](#route-configuration)
  - [Cluster Configuration](#cluster-configuration)
  - [Service Port Map](#service-port-map)
- [Rate Limiting](#rate-limiting)
  - [Policies](#policies)
  - [IPv6 Security Considerations](#ipv6-security-considerations)
- [Security](#security)
  - [Security Headers](#security-headers)
  - [Request Header Stripping](#request-header-stripping)
  - [Cloudflare Trusted Proxy](#cloudflare-trusted-proxy)
  - [Authorization Policies](#authorization-policies)
- [Health Checks](#health-checks)
- [Middleware](#middleware)
  - [Gateway-Specific Middleware](#gateway-specific-middleware)
  - [Shared Middleware (ServiceDefaults)](#shared-middleware-servicedefaults)
- [Endpoints](#endpoints)
  - [Gateway Endpoints](#gateway-endpoints)
  - [Diagnostics Endpoints (Development Only)](#diagnostics-endpoints-development-only)
- [OpenAPI Aggregation](#openapi-aggregation)
- [Circuit Breaker](#circuit-breaker)
- [Testing](#testing)
- [Docker](#docker)
- [Troubleshooting](#troubleshooting)
- [Related Documentation](#related-documentation)

---

## Overview

The Gateway service is a **stateless** component that provides:

- **Reverse Proxy**: Routes requests to 13+ backend microservices using YARP (Yet Another Reverse Proxy)
- **Authentication**: JWT Bearer token validation with configurable issuer/audience
- **Authorization**: Policy-based authorization (TenantScoped, Agent, DenyAll)
- **Rate Limiting**: Multiple policies (Global, Auth, PerTenant, PerAgent) with IPv6-aware throttling
- **Circuit Breaker**: Prevents cascading failures to unhealthy backend services
- **Security Headers**: Comprehensive protection against XSS, clickjacking, MIME sniffing, etc.
- **CORS Handling**: Explicit preflight handling with origin validation
- **Distributed Tracing**: Full OpenTelemetry integration with correlation IDs
- **Health Checks**: Kubernetes-compatible liveness, readiness, and health probes
- **OpenAPI Aggregation**: Unified Swagger documentation from all services (Development only)

The Gateway runs on port **5000** (development) or **8080** (container).

---

## Tech Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 10.0 | Runtime framework |
| ASP.NET Core | 10.0 | Web framework |
| YARP | 2.3.0 | Reverse proxy |
| OpenTelemetry | 1.14.0 | Distributed tracing and metrics |
| Swashbuckle | Latest | Swagger/OpenAPI documentation |
| MassTransit | 8.3.6 | Message bus abstraction (future use) |

**Project Dependencies**:
- `Dhadgar.Contracts` - Shared DTOs and message contracts
- `Dhadgar.Shared` - Common utilities
- `Dhadgar.Messaging` - MassTransit configuration
- `Dhadgar.ServiceDefaults` - Shared middleware and observability

---

## Quick Start

### Prerequisites

- .NET SDK 10.0.100+ (pinned in `global.json`)
- Docker (for local infrastructure)

### Start Local Infrastructure

```bash
# From repository root
docker compose -f deploy/compose/docker-compose.dev.yml up -d
```

This starts PostgreSQL, RabbitMQ, Redis, and observability stack.

### Run the Gateway

```bash
# Direct run
dotnet run --project src/Dhadgar.Gateway

# With hot reload
dotnet watch --project src/Dhadgar.Gateway

# The gateway will be available at:
# http://localhost:5000
```

### Verify It's Running

```bash
# Health check
curl http://localhost:5000/healthz

# Hello endpoint
curl http://localhost:5000/hello
# Returns: Hello from Dhadgar.Gateway

# Root endpoint
curl http://localhost:5000/
# Returns: {"service":"Dhadgar.Gateway","message":"Hello from Dhadgar.Gateway","version":"1.0.0.0"}
```

### Access Swagger (Development Only)

Open http://localhost:5000/swagger in your browser.

---

## Configuration

The Gateway uses ASP.NET Core's standard configuration hierarchy:
1. `appsettings.json` (base configuration)
2. `appsettings.Development.json` (development overrides)
3. Environment variables
4. User secrets (for sensitive values)
5. Kubernetes ConfigMaps/Secrets (production)

### Application Settings

**File**: `appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Yarp.ReverseProxy": "Information"
    }
  },
  "AllowedHosts": "localhost;api.meridianconsole.com;*.meridianconsole.com"
}
```

### Authentication Configuration

```json
{
  "Authentication": {
    "Enabled": false,
    "EnforcementMode": "optional"
  },
  "Auth": {
    "Issuer": "https://meridianconsole.com/api/v1/identity",
    "Audience": "meridian-api",
    "ClockSkewSeconds": 30
  }
}
```

| Key | Description | Default |
|-----|-------------|---------|
| `Authentication:Enabled` | Enable/disable authentication globally | `false` |
| `Auth:Issuer` | JWT token issuer (Identity service URL) | `https://meridianconsole.com/api/v1/identity` |
| `Auth:Audience` | Expected JWT audience | `meridian-api` |
| `Auth:ClockSkewSeconds` | Allowed clock drift for token validation | `30` |

**User Secrets Setup** (for signing key):
```bash
dotnet user-secrets init --project src/Dhadgar.Gateway
dotnet user-secrets set "Auth:SigningKey" "your-development-signing-key"
```

### CORS Configuration

```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://panel.meridianconsole.com",
      "https://cart.meridianconsole.com",
      "https://meridianconsole.com",
      "https://dev.meridianconsole.com",
      "http://localhost:4321",
      "http://localhost:4322"
    ]
  }
}
```

**Security Notes**:
- In non-Development environments, `AllowedOrigins` **must** be configured
- Wildcard `*` is **prohibited** in non-Development environments
- Credentials are only allowed when specific origins are configured

**Development Overrides** (`appsettings.Development.json`):
```json
{
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:4321",
      "http://localhost:4322",
      "http://localhost:5173",
      "https://localhost:5173"
    ]
  }
}
```

### Rate Limiting Configuration

```json
{
  "RateLimiting": {
    "Policies": {
      "Global": {
        "PermitLimit": 1000,
        "WindowSeconds": 60
      },
      "PerTenant": {
        "PermitLimit": 100,
        "WindowSeconds": 60
      },
      "PerAgent": {
        "PermitLimit": 500,
        "WindowSeconds": 60
      },
      "Auth": {
        "PermitLimit": 30,
        "WindowSeconds": 60
      }
    }
  }
}
```

See [Rate Limiting](#rate-limiting) for detailed policy descriptions.

### Circuit Breaker Configuration

```json
{
  "CircuitBreaker": {
    "FailureThreshold": 5,
    "SuccessThreshold": 2,
    "OpenDurationSeconds": 30,
    "FailureStatusCodes": [500, 502, 503, 504],
    "IncludeServiceNameInErrors": false
  }
}
```

| Key | Description | Default |
|-----|-------------|---------|
| `FailureThreshold` | Consecutive failures before circuit opens | `5` |
| `SuccessThreshold` | Successes in half-open state to close | `2` |
| `OpenDurationSeconds` | How long circuit stays open | `30` |
| `FailureStatusCodes` | HTTP codes treated as failures | `[500, 502, 503, 504]` |
| `IncludeServiceNameInErrors` | Include service name in 503 responses | `false` |

### Cloudflare Configuration

```json
{
  "Cloudflare": {
    "EnableDynamicFetch": true,
    "RefreshIntervalMinutes": 60,
    "FetchTimeoutSeconds": 30
  }
}
```

| Key | Description | Default |
|-----|-------------|---------|
| `EnableDynamicFetch` | Fetch IP ranges from Cloudflare dynamically | `true` |
| `RefreshIntervalMinutes` | How often to refresh IP ranges | `60` |
| `FetchTimeoutSeconds` | Timeout for fetching IP ranges | `30` |
| `FallbackIPv4Ranges` | Static IPv4 ranges for offline scenarios | `[]` |
| `FallbackIPv6Ranges` | Static IPv6 ranges for offline scenarios | `[]` |

### Readiness Configuration

```json
{
  "Readyz": {
    "RequiredClusters": ["identity", "secrets"],
    "MinimumAvailableDestinations": 1,
    "FailOnMissingCluster": true
  }
}
```

| Key | Description | Default |
|-----|-------------|---------|
| `RequiredClusters` | YARP clusters required for readiness | `["identity", "secrets"]` |
| `MinimumAvailableDestinations` | Min healthy destinations per cluster | `1` |
| `FailOnMissingCluster` | Fail readiness if cluster doesn't exist | `true` |

### OpenTelemetry Configuration

```json
{
  "OpenTelemetry": {
    "OtlpEndpoint": ""
  }
}
```

To enable OTLP export:
```bash
dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317"
```

This enables traces, metrics, and logs to flow to the local observability stack (OTLP Collector -> Prometheus/Loki -> Grafana).

---

## Architecture

### Middleware Pipeline

The middleware pipeline order is **critical**. Requests flow through middleware in this exact sequence:

```
Request
   |
   v
1. ForwardedHeaders          - Sets RemoteIpAddress from X-Forwarded-For
   |
   v
2. CorsPreflightMiddleware   - Short-circuits OPTIONS requests
   |
   v
3. SecurityHeadersMiddleware - Adds security headers to all responses
   |
   v
4. CorrelationMiddleware     - Ensures correlation/request/trace IDs exist
   |
   v
5. ProblemDetailsMiddleware  - Catches exceptions, returns RFC 7807 responses
   |
   v
6. RequestLoggingMiddleware  - Logs request/response with timing
   |
   v
7. UseCors                   - Standard CORS handling for non-preflight
   |
   v
8. UseAuthentication         - JWT validation
   |
   v
9. UseAuthorization          - Policy enforcement
   |
   v
10. UseRateLimiter           - Rate limit enforcement (after auth for tenant context)
   |
   v
11. RequestEnrichmentMiddleware - Strips spoofed headers, injects JWT claims
   |
   v
12. YarpCircuitBreakerAdapter   - Extracts cluster ID for circuit breaker
   |
   v
13. CircuitBreakerMiddleware    - Blocks requests to unhealthy services
   |
   v
14. YARP Reverse Proxy          - Routes to backend services
   |
   v
Response
```

### Key Components

**Services**:
- `CloudflareIpService` - Fetches/caches Cloudflare IP ranges
- `CloudflareIpHostedService` - Background service for periodic IP refresh
- `CloudflareForwardedHeadersPostConfigure` - Configures trusted proxies
- `OpenApiAggregationService` - Aggregates OpenAPI specs from all services

**Options Classes**:
- `ReadyzOptions` - Readiness check configuration
- `CloudflareOptions` - Cloudflare integration settings

**Readiness**:
- `YarpReadinessCheck` - Verifies YARP cluster health for Kubernetes probes

---

## YARP Routing

### Route Configuration

Routes are configured in `appsettings.json` under `ReverseProxy:Routes`. Each route specifies:
- Path pattern to match
- Target cluster
- Authorization policy
- Rate limiter policy
- Path transformations

**Route Ordering**: Lower `Order` values have higher priority. The `identity-internal-block` route (Order: 1) takes precedence over the general `identity-route` (Order: 10).

### Complete Route Table

| Route | Path | Cluster | Auth Policy | Rate Policy | Transform |
|-------|------|---------|-------------|-------------|-----------|
| `identity-internal-block` | `/api/v1/identity/internal/{**catch-all}` | identity | **DenyAll** | - | - |
| `betterauth-route` | `/api/v1/betterauth/{**catch-all}` | betterauth | Anonymous | Auth | - |
| `identity-route` | `/api/v1/identity/{**catch-all}` | identity | Anonymous | Auth | PathRemovePrefix |
| `billing-route` | `/api/v1/billing/{**catch-all}` | billing | TenantScoped | PerTenant | PathRemovePrefix |
| `servers-route` | `/api/v1/servers/{**catch-all}` | servers | TenantScoped | PerTenant | PathRemovePrefix |
| `nodes-route` | `/api/v1/nodes/{**catch-all}` | nodes | TenantScoped | PerTenant | PathRemovePrefix |
| `tasks-route` | `/api/v1/tasks/{**catch-all}` | tasks | TenantScoped | PerTenant | PathRemovePrefix |
| `files-route` | `/api/v1/files/{**catch-all}` | files | TenantScoped | PerTenant | PathRemovePrefix |
| `console-api-route` | `/api/v1/console/{**catch-all}` | console | TenantScoped | PerTenant | PathRemovePrefix |
| `console-hub-route` | `/hubs/console/{**catch-all}` | console | TenantScoped | PerTenant | - |
| `mods-route` | `/api/v1/mods/{**catch-all}` | mods | TenantScoped | PerTenant | PathRemovePrefix |
| `notifications-route` | `/api/v1/notifications/{**catch-all}` | notifications | TenantScoped | PerTenant | PathRemovePrefix |
| `firewall-route` | `/api/v1/firewall/{**catch-all}` | firewall | TenantScoped | PerTenant | PathRemovePrefix |
| `secrets-route` | `/api/v1/secrets/{**catch-all}` | secrets | TenantScoped | PerTenant | PathRemovePrefix |
| `discord-route` | `/api/v1/discord/{**catch-all}` | discord | TenantScoped | PerTenant | PathRemovePrefix |
| `agents-route` | `/api/v1/agents/{**catch-all}` | nodes | **Agent** | PerAgent | PathRemovePrefix |

### Cluster Configuration

All clusters share common configuration:

```json
{
  "LoadBalancingPolicy": "RoundRobin",
  "HealthCheck": {
    "Active": {
      "Enabled": true,
      "Interval": "00:00:30",
      "Timeout": "00:00:10",
      "Policy": "ConsecutiveFailures",
      "Path": "/healthz"
    },
    "Passive": {
      "Enabled": true,
      "Policy": "TransportFailureRate",
      "ReactivationPeriod": "00:01:00"
    }
  }
}
```

**Special Cluster Configurations**:

**Files Cluster** - Extended timeout for large uploads:
```json
{
  "HttpRequest": {
    "ActivityTimeout": "00:05:00"
  }
}
```

**Console Cluster** - Session affinity for SignalR:
```json
{
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
```

### Service Port Map

| Cluster | Service | Development Port |
|---------|---------|------------------|
| betterauth | Dhadgar.Identity | 5130 |
| identity | Dhadgar.Identity | 5010 |
| billing | Dhadgar.Billing | 5020 |
| servers | Dhadgar.Servers | 5030 |
| nodes | Dhadgar.Nodes | 5040 |
| tasks | Dhadgar.Tasks | 5050 |
| files | Dhadgar.Files | 5060 |
| console | Dhadgar.Console | 5070 |
| mods | Dhadgar.Mods | 5080 |
| notifications | Dhadgar.Notifications | 5090 |
| secrets | Dhadgar.Secrets | 5110 |
| discord | Dhadgar.Discord | 5120 |

---

## Rate Limiting

### Policies

**Global Policy**
- Applied to ALL proxied requests via `MapReverseProxy().RequireRateLimiting("Global")`
- Fixed window limiter
- Default: 1000 requests per 60 seconds
- Partition key: `"global"` (shared across all clients)

**Auth Policy**
- Applied to authentication endpoints (identity, betterauth)
- Fixed window limiter with **IPv6 /64 prefix normalization**
- Default: 30 requests per 60 seconds per IP
- Partition key: IP address (or /64 prefix for IPv6)

**PerTenant Policy**
- Applied to most tenant-scoped API routes
- Fixed window limiter
- Default: 100 requests per 60 seconds per tenant
- Partition key: `org_id` claim from JWT, falls back to IP address

**PerAgent Policy**
- Applied to agent API routes
- Fixed window limiter
- Default: 500 requests per 60 seconds per agent
- Partition key: `sub` claim from JWT, falls back to IP address

### IPv6 Security Considerations

The Auth rate limiter uses `/64` prefix grouping for IPv6 addresses:

```csharp
// Use /64 prefix for IPv6 to prevent rotation attacks
var bytes = ip.GetAddressBytes();
Array.Clear(bytes, 8, 8); // Zero out host portion (last 64 bits)
partitionKey = new IPAddress(bytes).ToString();
```

**Why?** IPv6 users typically receive at least a `/64` prefix allocation. Without prefix grouping, attackers could rotate through addresses within their allocation to bypass rate limits. By grouping on `/64`, all addresses within a user's allocation share the same rate limit bucket.

**Special Cases**:
- IPv6 link-local addresses (`fe80::`) use partition key `"unknown-linklocal"`
- IPv4-mapped IPv6 addresses use the full address as partition key
- Null IP addresses use partition key `"unknown"`

### Rate Limit Response

When rate limited, clients receive:

```http
HTTP/1.1 429 Too Many Requests
Content-Type: application/problem+json
Retry-After: 60

{
  "type": "https://httpstatuses.com/429",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Request rate limit exceeded. Please retry after the specified time.",
  "instance": "/api/v1/servers"
}
```

---

## Security

### Security Headers

Applied by `SecurityHeadersMiddleware` to ALL responses:

| Header | Value | Purpose |
|--------|-------|---------|
| `X-Content-Type-Options` | `nosniff` | Prevent MIME type sniffing |
| `X-Frame-Options` | `DENY` | Prevent clickjacking |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` | Restrictive CSP (API endpoints only) |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Control referrer information |
| `Permissions-Policy` | `accelerometer=(), camera=(), ...` | Disable unnecessary browser features |
| `Cache-Control` | `no-store, no-cache, must-revalidate` | Prevent caching of API responses |
| `Pragma` | `no-cache` | HTTP/1.0 cache prevention |
| `X-DNS-Prefetch-Control` | `off` | Prevent DNS prefetching |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains; preload` | HSTS (production only) |

**Removed Headers**:
- `X-XSS-Protection` - Removed (deprecated, can cause vulnerabilities)
- `Server` - Removed via Kestrel configuration
- `X-Powered-By` - Removed

**Swagger Exception**: In Development/Testing environments, Swagger/Scalar paths (`/swagger`, `/scalar`, `/openapi`) do not receive the restrictive CSP to allow the UI to function.

### Request Header Stripping

`RequestEnrichmentMiddleware` strips the following headers from incoming requests to prevent spoofing:

```csharp
private static readonly string[] SecurityHeaders = new[]
{
    "X-Tenant-Id",
    "X-User-Id",
    "X-Client-Type",
    "X-Agent-Id",
    "X-Roles",
    "X-Real-IP"
};
```

After stripping, the middleware re-injects these headers from validated JWT claims:
- `X-Tenant-Id` <- `org_id` claim
- `X-User-Id` <- `sub` claim
- `X-Client-Type` <- `client_type` claim
- `X-Agent-Id` <- `agent_id` claim
- `X-Roles` <- `role` claims (comma-separated)
- `X-Real-IP` <- `RemoteIpAddress` (validated by ForwardedHeaders middleware)

### Cloudflare Trusted Proxy

The Gateway trusts `X-Forwarded-For` headers only from Cloudflare IP ranges:

1. `CloudflareIpHostedService` fetches IP ranges from `https://www.cloudflare.com/ips-v4` and `https://www.cloudflare.com/ips-v6` on startup
2. `CloudflareForwardedHeadersPostConfigure` adds these ranges to `ForwardedHeadersOptions.KnownIPNetworks`
3. `UseForwardedHeaders()` validates the source IP before trusting headers

**Fallback**: Configure static IP ranges for offline/air-gapped environments:
```json
{
  "Cloudflare": {
    "EnableDynamicFetch": false,
    "FallbackIPv4Ranges": ["173.245.48.0/20", "103.21.244.0/22", ...],
    "FallbackIPv6Ranges": ["2400:cb00::/32", "2606:4700::/32", ...]
  }
}
```

### Authorization Policies

| Policy | Requirements |
|--------|--------------|
| `TenantScoped` | Authenticated user required |
| `Agent` | Authenticated user + `client_type=agent` claim |
| `DenyAll` | Always fails (blocks internal endpoints) |

---

## Health Checks

Three health check endpoints are available (via `ServiceDefaultsExtensions`):

### `/healthz` - Overall Health
Returns health of all registered checks with detailed status.

```bash
curl http://localhost:5000/healthz
```

Response:
```json
{
  "service": "Dhadgar.Gateway",
  "status": "ok",
  "timestamp": "2026-01-22T12:00:00Z",
  "checks": {
    "self": {
      "status": "Healthy",
      "duration_ms": 0.5
    },
    "yarp_ready": {
      "status": "Healthy",
      "duration_ms": 1.2,
      "data": {
        "requiredClusters": ["identity", "secrets"],
        "minimumAvailable": 1,
        "clusters": [...]
      }
    }
  }
}
```

### `/livez` - Liveness Probe
Returns only checks tagged with "live". Used by Kubernetes to determine if the container should be restarted.

```bash
curl http://localhost:5000/livez
```

### `/readyz` - Readiness Probe
Returns only checks tagged with "ready". Used by Kubernetes to determine if the container should receive traffic.

The `YarpReadinessCheck` verifies:
- Required clusters exist in YARP configuration
- Each cluster has at least `MinimumAvailableDestinations` healthy destinations

```bash
curl http://localhost:5000/readyz
```

---

## Middleware

### Gateway-Specific Middleware

**CorsPreflightMiddleware** (`Middleware/CorsPreflightMiddleware.cs`)
- Handles CORS preflight (OPTIONS) requests explicitly
- Short-circuits before any other middleware can interfere
- Validates origin against allowed list
- Returns 403 for disallowed origins
- Sets appropriate CORS headers for allowed origins

**SecurityHeadersMiddleware** (`Middleware/SecurityHeadersMiddleware.cs`)
- Adds comprehensive security headers to all responses
- Environment-aware CSP (relaxed for Swagger in dev)
- HSTS only in production

**RequestEnrichmentMiddleware** (`Middleware/RequestEnrichmentMiddleware.cs`)
- Strips client-supplied security headers to prevent spoofing
- Injects validated JWT claims as headers for backend services
- Ensures X-Request-Id exists
- Adds X-Real-IP from validated RemoteIpAddress

**YarpCircuitBreakerAdapter** (`Middleware/YarpCircuitBreakerAdapter.cs`)
- Bridges YARP and the shared circuit breaker middleware
- Extracts cluster ID from YARP's `IReverseProxyFeature`
- Sets `CircuitBreaker:ServiceId` in HttpContext.Items

**CorsConfiguration** (`Middleware/CorsConfiguration.cs`)
- Configures CORS policy based on configuration
- Enforces explicit origins in non-Development environments
- Prohibits wildcard `*` in production

### Shared Middleware (ServiceDefaults)

These middleware components come from `Dhadgar.ServiceDefaults`:

**CorrelationMiddleware**
- Ensures every request has `X-Correlation-Id`, `X-Request-Id`, and `X-Trace-Id`
- Integrates with OpenTelemetry Activity
- Propagates W3C trace context headers (`traceparent`, `tracestate`, `baggage`)

**ProblemDetailsMiddleware**
- Catches unhandled exceptions
- Returns RFC 7807 Problem Details responses
- Includes stack traces only in Development/Testing environments

**RequestLoggingMiddleware**
- Logs all HTTP requests with timing
- Uses structured logging with correlation context
- Log level based on response status (Error for 5xx, Warning for 4xx, Info for others)

**CircuitBreakerMiddleware**
- Prevents cascading failures
- Three states: Closed, Open, Half-Open
- Configurable failure thresholds
- Emits metrics via OpenTelemetry

---

## Endpoints

### Gateway Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/` | Anonymous | Service info (name, message, version) |
| GET | `/hello` | Anonymous | Returns "Hello from Dhadgar.Gateway" |
| GET | `/healthz` | Anonymous | Overall health status |
| GET | `/livez` | Anonymous | Liveness probe |
| GET | `/readyz` | Anonymous | Readiness probe |

### Diagnostics Endpoints (Development Only)

These endpoints are only available when `ASPNETCORE_ENVIRONMENT=Development`:

| Method | Path | Description |
|--------|------|-------------|
| GET | `/diagnostics/integration` | End-to-end connectivity test (Gateway -> Identity -> Secrets) |
| GET | `/diagnostics/services` | Health check all configured backend services |
| GET | `/diagnostics/routes` | List all YARP routes with configuration |
| GET | `/diagnostics/clusters` | List all YARP clusters with status |
| GET | `/diagnostics/wif` | Test Workload Identity Federation token flow |

**Integration Check Example**:
```bash
curl http://localhost:5000/diagnostics/integration
```

Returns detailed step-by-step results including:
- Gateway self-check
- Identity service health
- Token acquisition
- Secrets service health
- Authenticated call to Secrets service

---

## OpenAPI Aggregation

In Development mode, the Gateway aggregates OpenAPI specifications from all backend services into a unified document:

**Endpoint**: `/openapi/all.json`

**Swagger UI**: http://localhost:5000/swagger

The `OpenApiAggregationService`:
1. Fetches `/swagger/v1/swagger.json` from each backend service
2. Prefixes all paths with the service's route prefix (e.g., `/api/v1/servers`)
3. Prefixes schema names to avoid conflicts (e.g., `Servers_GameServer`)
4. Updates internal `$ref` references to match prefixed schema names
5. Caches the result for 5 minutes

**Service Mappings**:
```csharp
["identity"] = ("Identity", "/api/v1/identity"),
["servers"] = ("Servers", "/api/v1/servers"),
["nodes"] = ("Nodes", "/api/v1/nodes"),
// ... etc
```

---

## Circuit Breaker

The circuit breaker protects against cascading failures when backend services become unhealthy.

### States

1. **Closed** (Normal)
   - Requests pass through
   - Failures increment counter
   - Successes reset counter
   - Opens when `FailureThreshold` reached

2. **Open** (Blocking)
   - Requests blocked with 503
   - Returns `Retry-After` header
   - Transitions to Half-Open after `OpenDurationSeconds`

3. **Half-Open** (Testing)
   - Limited requests allowed through
   - Failures re-open immediately
   - Closes after `SuccessThreshold` successes

### Metrics

The circuit breaker emits OpenTelemetry metrics:

| Metric | Description |
|--------|-------------|
| `circuit_breaker.opened` | Counter: times circuit opened |
| `circuit_breaker.closed` | Counter: times circuit closed |
| `circuit_breaker.requests_blocked` | Counter: requests blocked by open circuits |
| `circuit_breaker.failures_recorded` | Counter: failures recorded |

---

## Testing

The Gateway has comprehensive tests in `tests/Dhadgar.Gateway.Tests/`:

| Test Class | Coverage |
|------------|----------|
| `HelloWorldTests` | Basic smoke test |
| `GatewayIntegrationTests` | Health endpoints, correlation headers, security headers |
| `RouteConfigurationTests` | YARP route configuration validation |
| `RateLimiterConfigurationTests` | Rate limit policy configuration |
| `RateLimitingBehaviorTests` | Rate limiting behavior |
| `ReadinessTests` | YARP readiness check |
| `MiddlewareUnitTests` | Individual middleware components |
| `CircuitBreakerTests` | Circuit breaker state transitions |
| `SecurityTests` | Security header validation |
| `OpenApiAggregationServiceTests` | OpenAPI aggregation |

### Running Tests

```bash
# All Gateway tests
dotnet test tests/Dhadgar.Gateway.Tests

# Specific test
dotnet test tests/Dhadgar.Gateway.Tests --filter "FullyQualifiedName~HelloWorldTests"

# With verbose output
dotnet test tests/Dhadgar.Gateway.Tests -v n
```

### Test Factory

Tests use `GatewayWebApplicationFactory` which:
- Sets environment to "Testing"
- Configures CORS allowed origins for tests
- Uses `WebApplicationFactory<Program>` for integration testing

---

## Docker

### Development Dockerfile

**File**: `Dockerfile`

Multi-stage build that compiles from source:

```bash
# Build from repository root
docker build -f src/Dhadgar.Gateway/Dockerfile -t dhadgar/gateway:latest .
```

### Pipeline Dockerfile

**File**: `Dockerfile.pipeline`

Uses pre-built artifacts from Azure Pipelines:

```bash
# Used by CI/CD pipeline with pre-built artifacts
docker build \
  -f src/Dhadgar.Gateway/Dockerfile.pipeline \
  --build-arg BUILD_ARTIFACT_PATH=/path/to/artifacts \
  -t dhadgar/gateway:latest .
```

### Runtime Configuration

| Setting | Value |
|---------|-------|
| Base Image | `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` |
| Port | 8080 |
| User | appuser (non-root) |
| Health Check | `curl -f http://localhost:8080/healthz` |

---

## Troubleshooting

### Service Not Starting

**Symptom**: Gateway fails to start with configuration errors.

**Solution**:
1. Check `appsettings.json` syntax
2. Verify CORS configuration exists for non-Development environments
3. Check user secrets are set for sensitive values:
   ```bash
   dotnet user-secrets list --project src/Dhadgar.Gateway
   ```

### 403 on CORS Preflight

**Symptom**: Browser shows CORS error, OPTIONS returns 403.

**Solution**:
1. Check `Cors:AllowedOrigins` includes your frontend origin
2. For development, add localhost ports to `appsettings.Development.json`
3. Check logs for "CORS preflight rejected: origin X not in allowed list"

### 429 Too Many Requests

**Symptom**: Getting rate limited unexpectedly.

**Solution**:
1. Check which rate limit policy applies to your route (see route table)
2. Increase limits in configuration for development:
   ```json
   "RateLimiting": {
     "Policies": {
       "Auth": { "PermitLimit": 100 }
     }
   }
   ```
3. For IPv6, remember /64 prefix grouping applies

### 503 Service Unavailable (Circuit Open)

**Symptom**: Requests returning 503 with circuit breaker message.

**Solution**:
1. Check backend service health: `curl http://localhost:5000/diagnostics/services`
2. Wait for `OpenDurationSeconds` to allow half-open transition
3. Fix underlying backend service issues
4. Temporarily increase `FailureThreshold` if needed

### Backend Service Not Reachable

**Symptom**: Requests to `/api/v1/service/...` timeout or fail.

**Solution**:
1. Check service is running: `curl http://localhost:PORT/healthz`
2. Verify port matches `ReverseProxy:Clusters:SERVICE:Destinations:d1:Address`
3. Check `/diagnostics/clusters` for cluster health
4. Review YARP logs at Debug level

### Missing Correlation Headers

**Symptom**: Responses don't include `X-Correlation-Id` headers.

**Solution**:
1. Ensure `CorrelationMiddleware` is in pipeline (check Program.cs order)
2. Check for exceptions before middleware executes
3. Verify response hasn't already started writing

### OpenTelemetry Not Exporting

**Symptom**: No traces/metrics in observability stack.

**Solution**:
1. Set OTLP endpoint:
   ```bash
   dotnet user-secrets set "OpenTelemetry:OtlpEndpoint" "http://localhost:4317"
   ```
2. Verify OTLP collector is running: `docker ps | grep otel`
3. Check for warnings about invalid endpoint in logs

---

## Related Documentation

- **Root CLAUDE.md**: `/MeridianConsole/CLAUDE.md` - Overall project guidance
- **ServiceDefaults**: `/src/Shared/Dhadgar.ServiceDefaults/` - Shared middleware and extensions
- **Docker Compose**: `/deploy/compose/README.md` - Local infrastructure setup
- **Kubernetes**: `/deploy/kubernetes/` - K8s deployment manifests (planned)
- **Identity Service**: `/src/Dhadgar.Identity/README.md` - Authentication service documentation
- **Container Build**: `/deploy/kubernetes/CONTAINER-BUILD-SETUP.md` - CI/CD pipeline setup
