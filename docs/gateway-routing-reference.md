# Gateway Routing Reference

This document provides a complete reference for the Gateway service's YARP routing configuration, rate limiting, health checks, CORS, and security headers.

---

## Table of Contents

1. [YARP Routes](#yarp-routes)
2. [Backend Clusters](#backend-clusters)
3. [Rate Limiting](#rate-limiting)
4. [Health Checks](#health-checks)
5. [CORS Configuration](#cors-configuration)
6. [Security Headers](#security-headers)
7. [Adding New Routes](#adding-new-routes)

---

## YARP Routes

All routes are configured in `src/Dhadgar.Gateway/appsettings.json` under `ReverseProxy.Routes`.

### Route Priority (Order)

Routes are matched by `Order` value (lower = higher priority):

| Order | Purpose | Example |
|-------|---------|---------|
| 1 | Security blocks | Internal endpoint blocking |
| 10 | Authentication routes | Identity, BetterAuth |
| 20 | Standard API routes | Most services |
| 30 | Catch-all routes | Agent endpoints |

### Complete Route Table

| Route ID | Path Pattern | Cluster | Auth Policy | Rate Limiter | Transform |
|----------|--------------|---------|-------------|--------------|-----------|
| `identity-internal-block` | `/api/v1/identity/internal/{**catch-all}` | identity | `DenyAll` | - | - |
| `betterauth-route` | `/api/v1/betterauth/{**catch-all}` | betterauth | `Anonymous` | `Auth` | - |
| `identity-route` | `/api/v1/identity/{**catch-all}` | identity | `Anonymous` | `Auth` | Remove `/api/v1/identity` |
| `billing-route` | `/api/v1/billing/{**catch-all}` | billing | `TenantScoped` | `PerTenant` | Remove `/api/v1/billing` |
| `servers-route` | `/api/v1/servers/{**catch-all}` | servers | `TenantScoped` | `PerTenant` | Remove `/api/v1/servers` |
| `nodes-route` | `/api/v1/nodes/{**catch-all}` | nodes | `TenantScoped` | `PerTenant` | Remove `/api/v1/nodes` |
| `tasks-route` | `/api/v1/tasks/{**catch-all}` | tasks | `TenantScoped` | `PerTenant` | Remove `/api/v1/tasks` |
| `files-route` | `/api/v1/files/{**catch-all}` | files | `TenantScoped` | `PerTenant` | Remove `/api/v1/files` |
| `console-api-route` | `/api/v1/console/{**catch-all}` | console | `TenantScoped` | `PerTenant` | Remove `/api/v1/console` |
| `console-hub-route` | `/hubs/console/{**catch-all}` | console | `TenantScoped` | `PerTenant` | - |
| `mods-route` | `/api/v1/mods/{**catch-all}` | mods | `TenantScoped` | `PerTenant` | Remove `/api/v1/mods` |
| `notifications-route` | `/api/v1/notifications/{**catch-all}` | notifications | `TenantScoped` | `PerTenant` | Remove `/api/v1/notifications` |
| `secrets-route` | `/api/v1/secrets/{**catch-all}` | secrets | `TenantScoped` | `PerTenant` | Remove `/api/v1/secrets` |
| `discord-route` | `/api/v1/discord/{**catch-all}` | discord | `TenantScoped` | `PerTenant` | Remove `/api/v1/discord` |
| `agents-route` | `/api/v1/agents/{**catch-all}` | nodes | `Agent` | `PerAgent` | Remove `/api/v1/agents` |

### Authorization Policies

| Policy | Description | Required Claims |
|--------|-------------|-----------------|
| `Anonymous` | No authentication required | None |
| `TenantScoped` | Requires valid JWT with tenant context | `org_id` claim |
| `Agent` | Requires valid JWT with agent client type | `client_type: agent` |
| `DenyAll` | Always returns 403 Forbidden | - |

### Special Routes

**Internal Endpoint Blocking**

The `identity-internal-block` route (Order: 1) prevents external access to internal service endpoints:

```json
"identity-internal-block": {
  "ClusterId": "identity",
  "Order": 1,
  "Match": { "Path": "/api/v1/identity/internal/{**catch-all}" },
  "AuthorizationPolicy": "DenyAll"
}
```

**Agent Routes**

The `/api/v1/agents/*` route points to the `nodes` cluster intentionally. Agents are customer-hosted components that communicate with the Nodes service for registration, health reporting, and capacity management.

**SignalR Routes**

The Console service has two routes:
- `/api/v1/console/*` - REST API endpoints
- `/hubs/console/*` - SignalR WebSocket connections (no path transform)

---

## Backend Clusters

All clusters are configured in `src/Dhadgar.Gateway/appsettings.json` under `ReverseProxy.Clusters`.

### Cluster Configuration

| Cluster | Port | Load Balancing | Session Affinity | Request Timeout |
|---------|------|----------------|------------------|-----------------|
| `betterauth` | 5130 | RoundRobin | No | 30s (default) |
| `identity` | 5010 | RoundRobin | No | 30s (default) |
| `billing` | 5020 | RoundRobin | No | 30s (default) |
| `servers` | 5030 | RoundRobin | No | 30s (default) |
| `nodes` | 5040 | RoundRobin | No | 30s (default) |
| `tasks` | 5050 | RoundRobin | No | 30s (default) |
| `files` | 5060 | RoundRobin | No | 5 minutes |
| `console` | 5070 | RoundRobin | **Yes (Cookie)** | 30s (default) |
| `mods` | 5080 | RoundRobin | No | 30s (default) |
| `notifications` | 5090 | RoundRobin | No | 30s (default) |
| `secrets` | 5110 | RoundRobin | No | 30s (default) |
| `discord` | 5120 | RoundRobin | No | 30s (default) |

### Session Affinity (Console Cluster)

The Console cluster uses cookie-based session affinity for SignalR connections:

```json
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
```

### Extended Timeout (Files Cluster)

The Files cluster has a 5-minute timeout for large file transfers:

```json
"files": {
  "HttpRequest": {
    "ActivityTimeout": "00:05:00"
  }
}
```

---

## Rate Limiting

Rate limiting is configured in `src/Dhadgar.Gateway/appsettings.json` under `RateLimiting.Policies`.

### Rate Limit Policies

| Policy | Limit | Window/Replenish | Partitioning | Use Case |
|--------|-------|------------------|--------------|----------|
| `Global` | 1000 requests | 60 seconds (fixed) | Per IP | DDoS protection |
| `Auth` | 30 requests | 60 seconds (fixed) | Per IP (/64 for IPv6) | Brute force prevention |
| `PerTenant` | 100 burst, 50/sec replenish | Token bucket | Per Tenant ID | Fair usage |
| `PerAgent` | 500 requests | 60 seconds (fixed) | Per Agent ID | Agent operations |

### Configuration

```json
"RateLimiting": {
  "Policies": {
    "Global": {
      "PermitLimit": 1000,
      "WindowSeconds": 60
    },
    "PerTenant": {
      "PermitLimit": 100,
      "ReplenishPerSecond": 50,
      "QueueLimit": 0
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
```

### IPv6 Rate Limiting

For IPv6 addresses, rate limiting uses the /64 prefix (network portion) to prevent attackers from rotating through addresses:

```
2001:db8:85a3:1234:5678:9abc:def0:1234 -> 2001:db8:85a3:1234::
2001:db8:85a3:1234:ffff:ffff:ffff:ffff -> 2001:db8:85a3:1234::
```

Both addresses above count toward the same rate limit bucket.

### Rate Limit Headers

Responses include standard rate limit headers:

| Header | Description |
|--------|-------------|
| `X-RateLimit-Limit` | Maximum requests per window |
| `X-RateLimit-Remaining` | Requests remaining in current window |
| `X-RateLimit-Reset` | Unix timestamp when window resets |

### 429 Response Format

When rate limited, clients receive an RFC 7807 Problem Details response:

```json
{
  "type": "https://tools.ietf.org/html/rfc6585#section-4",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded. Try again in {seconds} seconds."
}
```

---

## Health Checks

### Gateway Health Endpoints

| Endpoint | Purpose | Response |
|----------|---------|----------|
| `/healthz` | Basic health check | 200 OK with service info |
| `/livez` | Kubernetes liveness probe | 200 OK |
| `/readyz` | Kubernetes readiness probe | 200 OK with dependency status |

### Health Check Response (`/healthz`)

```json
{
  "status": "Healthy",
  "service": "Dhadgar.Gateway",
  "version": "1.0.0",
  "timestamp": "2026-01-22T12:00:00Z"
}
```

### Readiness Requirements (`/readyz`)

The Gateway reports as ready when:
1. All required clusters (identity, secrets) have at least one healthy destination
2. JWT validation configuration is valid
3. CORS configuration is valid (origins required in production)

Configuration:

```json
"Readyz": {
  "RequiredClusters": ["identity", "secrets"],
  "MinimumAvailableDestinations": 1,
  "FailOnMissingCluster": true
}
```

### Backend Health Checks

All clusters use both active and passive health checks:

**Active Health Checks:**
- Interval: 30 seconds
- Timeout: 10 seconds
- Policy: ConsecutiveFailures
- Path: `/healthz`

**Passive Health Checks:**
- Policy: TransportFailureRate
- Reactivation Period: 60 seconds

```json
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
```

### Diagnostic Endpoints

| Endpoint | Description |
|----------|-------------|
| `/diagnostics/services` | Health status of all backend services |
| `/diagnostics/routes` | List all configured routes |
| `/diagnostics/clusters` | YARP cluster status and health |

---

## CORS Configuration

CORS is configured in `src/Dhadgar.Gateway/appsettings.json` under `Cors`.

### Current Configuration

```json
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
```

### CORS Policy Details

| Setting | Value |
|---------|-------|
| Policy Name | `MeridianConsolePolicy` |
| Methods | All methods allowed |
| Headers | All headers allowed |
| Credentials | Allowed (when origins are specified) |
| Exposed Headers | `X-Correlation-Id`, `X-Request-Id`, `X-Trace-Id` |

### Production Requirements

In non-Development environments:
- `Cors:AllowedOrigins` must be configured (cannot be empty)
- Wildcard (`*`) origins are not allowed
- The Gateway will throw an exception on startup if these requirements are not met

### Development Mode

In Development environment:
- If no origins are configured, all origins are allowed (`AllowAnyOrigin`)
- Credentials are not allowed when using `AllowAnyOrigin`

---

## Security Headers

Security headers are applied by `SecurityHeadersMiddleware` to all responses.

### Applied Headers

| Header | Value | Purpose |
|--------|-------|---------|
| `X-Content-Type-Options` | `nosniff` | Prevent MIME type sniffing |
| `X-Frame-Options` | `DENY` | Prevent clickjacking |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Control referrer information |
| `Permissions-Policy` | `accelerometer=(), camera=(), ...` | Disable unnecessary browser features |
| `Cache-Control` | `no-store, no-cache, must-revalidate` | Prevent caching of API responses |
| `Pragma` | `no-cache` | HTTP/1.0 cache prevention |
| `X-DNS-Prefetch-Control` | `off` | Prevent DNS prefetching |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` | Restrictive CSP for API endpoints |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains; preload` | HSTS (production only) |

### Removed Headers

| Header | Reason |
|--------|--------|
| `X-XSS-Protection` | Deprecated, can cause vulnerabilities |
| `X-Powered-By` | Information disclosure |
| `Server` | Information disclosure (via Kestrel config) |

### CSP Exceptions

Swagger/Scalar UI paths (`/swagger`, `/scalar`, `/openapi`) do not receive CSP headers in Development/Testing environments to allow the documentation UI to function properly.

### Stripped Incoming Headers

The following headers are stripped from incoming requests to prevent spoofing:

- `X-Tenant-Id`
- `X-User-Id`
- `X-Client-Type`
- `X-Agent-Id`
- `X-Roles`
- `X-Real-IP`

Backend services should only trust these headers when set by the Gateway after JWT validation.

---

## Adding New Routes

### Step 1: Add Route Configuration

Edit `src/Dhadgar.Gateway/appsettings.json` and add a new route under `ReverseProxy.Routes`:

```json
"newservice-route": {
  "ClusterId": "newservice",
  "Order": 20,
  "Match": { "Path": "/api/v1/newservice/{**catch-all}" },
  "AuthorizationPolicy": "TenantScoped",
  "RateLimiterPolicy": "PerTenant",
  "Transforms": [
    { "PathRemovePrefix": "/api/v1/newservice" }
  ]
}
```

### Step 2: Add Cluster Configuration

Add a new cluster under `ReverseProxy.Clusters`:

```json
"newservice": {
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
  },
  "Destinations": {
    "d1": {
      "Address": "http://localhost:5XXX/"
    }
  }
}
```

### Step 3: Choose Authorization Policy

| Scenario | Policy |
|----------|--------|
| Public endpoints (health, docs) | `Anonymous` |
| Tenant-scoped API endpoints | `TenantScoped` |
| Agent-specific endpoints | `Agent` |
| Internal-only endpoints | Create blocking route with `DenyAll` |

### Step 4: Choose Rate Limiter Policy

| Scenario | Policy |
|----------|--------|
| Authentication endpoints | `Auth` |
| Standard tenant API | `PerTenant` |
| Agent operations | `PerAgent` |
| No specific limiting | Omit (falls back to `Global`) |

### Step 5: Configure Path Transform

Most routes should strip their service prefix:

```json
"Transforms": [
  { "PathRemovePrefix": "/api/v1/newservice" }
]
```

This transforms `/api/v1/newservice/users` to `/users` before forwarding to the backend.

### Step 6: Special Configurations

**For SignalR/WebSocket endpoints:**

Add session affinity to the cluster:

```json
"SessionAffinity": {
  "Enabled": true,
  "Policy": "Cookie",
  "FailurePolicy": "Redistribute",
  "AffinityKeyName": ".Dhadgar.NewService.Affinity",
  "Cookie": {
    "HttpOnly": true,
    "SameSite": "Lax",
    "SecurePolicy": "Always"
  }
}
```

**For file upload/download endpoints:**

Increase the request timeout:

```json
"HttpRequest": {
  "ActivityTimeout": "00:05:00"
}
```

### Step 7: Block Internal Endpoints

If your service has internal-only endpoints, add a blocking route with higher priority:

```json
"newservice-internal-block": {
  "ClusterId": "newservice",
  "Order": 1,
  "Match": { "Path": "/api/v1/newservice/internal/{**catch-all}" },
  "AuthorizationPolicy": "DenyAll"
}
```

### Step 8: Test the Route

1. Build the solution: `dotnet build`
2. Run the Gateway: `dotnet run --project src/Dhadgar.Gateway`
3. Verify the route appears: `curl http://localhost:5000/diagnostics/routes | jq`
4. Test the endpoint: `curl http://localhost:5000/api/v1/newservice/healthz`

---

## Quick Reference

### URL Pattern

```
/api/v{version}/{service}/{resource}
```

### Common Commands

```bash
# List all routes
curl http://localhost:5000/diagnostics/routes | jq

# Check all backend services
curl http://localhost:5000/diagnostics/services | jq

# Check cluster health
curl http://localhost:5000/diagnostics/clusters | jq

# Gateway health
curl http://localhost:5000/healthz

# Gateway readiness
curl http://localhost:5000/readyz
```

### Port Assignments

| Port | Service |
|------|---------|
| 5000 | Gateway |
| 5010 | Identity |
| 5020 | Billing |
| 5030 | Servers |
| 5040 | Nodes |
| 5050 | Tasks |
| 5060 | Files |
| 5070 | Console |
| 5080 | Mods |
| 5090 | Notifications |
| 5110 | Secrets |
| 5120 | Discord |
| 5130 | BetterAuth |

---

## Related Documentation

- [Gateway Authentication](gateway-authentication.md) - JWT validation and authorization policies
- [Gateway Operations Runbook](runbooks/gateway-operations.md) - Operational procedures and troubleshooting
- [Gateway Implementation Plan](implementation-plans/gateway-service.md) - Architecture decisions and implementation details
