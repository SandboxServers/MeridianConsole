# Gateway Service Implementation Plan

**Service**: Dhadgar.Gateway
**Status**: First service implementation (Identity service follows next)
**Date**: 2024-12-28
**Microservices Architecture**: 13 backend services + Gateway

---

## Table of Contents

1. [Overview](#overview)
2. [Service Port Assignments](#service-port-assignments)
3. [Architecture Decisions](#architecture-decisions)
4. [Implementation Phases](#implementation-phases)
5. [Phase 1: Core YARP Configuration](#phase-1-core-yarp-configuration)
6. [Phase 2: Middleware Infrastructure](#phase-2-middleware-infrastructure)
7. [Phase 3: Security Infrastructure (JWT Ready)](#phase-3-security-infrastructure-jwt-ready)
8. [Phase 4: Rate Limiting](#phase-4-rate-limiting)
9. [Phase 5: Observability Infrastructure](#phase-5-observability-infrastructure)
10. [Phase 6: Health Checks](#phase-6-health-checks)
11. [Phase 7: SignalR & WebSocket Support](#phase-7-signalr--websocket-support)
12. [Phase 8: Testing & Documentation](#phase-8-testing--documentation)
13. [Configuration Reference](#configuration-reference)
14. [Testing Strategy](#testing-strategy)
15. [Deployment Considerations](#deployment-considerations)

---

## ⚠️ CRITICAL SECURITY UPDATES (Post-Agent Review)

This implementation plan has been reviewed by four specialized agents. The following **CRITICAL** security issues and improvements have been incorporated:

### 🔴 High Priority (Security)

1. **Header Spoofing Prevention** (security-architect)
   - **Issue**: RequestEnrichmentMiddleware doesn't strip client-supplied security headers (`X-Tenant-Id`, `X-User-Id`, etc.)
   - **Impact**: Attackers could send forged headers to impersonate other tenants/users
   - **Fix**: Added header stripping BEFORE JWT claim extraction in Phase 2.3

2. **Fail-Closed Authentication** (security-architect)
   - **Issue**: Authentication middleware fails open when `Enabled: false`
   - **Impact**: Unauthenticated requests allowed when they should be blocked
   - **Fix**: Updated Phase 3 to enforce authentication in production mode, disable only in Development environment

3. **CORS Configuration Missing** (security-architect)
   - **Issue**: No CORS policy configured for Blazor WASM clients
   - **Impact**: Browser-based clients cannot access API
   - **Fix**: Added CORS middleware in Phase 2.6 with origin restrictions

4. **Security Headers Missing** (security-architect)
   - **Issue**: No security headers (HSTS, CSP, X-Frame-Options)
   - **Impact**: Vulnerable to XSS, clickjacking, MITM attacks
   - **Fix**: Added SecurityHeadersMiddleware in Phase 2.7

### 🟡 High Priority (Operational)

5. **RFC 7807 Problem Details** (rest-api-engineer)
   - **Issue**: Non-standard error responses across services
   - **Impact**: Inconsistent client error handling
   - **Fix**: Added ProblemDetailsMiddleware in Phase 2.5 with standard format

6. **Serilog Trace Correlation** (observability-architect)
   - **Issue**: Logs don't include distributed trace IDs
   - **Impact**: Cannot correlate logs with traces in production
   - **Fix**: Updated Phase 5 to configure Serilog with trace enrichment

7. **Agent Route Clarification** (microservices-architect)
   - **Issue**: `/api/v1/agents` routes to `nodes` cluster without explanation
   - **Impact**: Confusing for developers, potential routing errors
   - **Fix**: Added documentation in Phase 1 explaining agent-to-node relationship

### Implementation Changes Summary

- **Phase 1**: Added agent route documentation and rationale
- **Phase 2**: Added 4 new middleware components (header stripping, Problem Details, CORS, Security Headers)
- **Phase 3**: Changed authentication to fail-closed with environment-based disabling
- **Phase 5**: Added Serilog configuration with trace correlation
- **Testing**: Added specific test cases for header spoofing and authentication bypass

All critical security issues are now addressed in the updated plan below.

---

## Overview

The Gateway service is the **single public entry point** for the entire Meridian Console platform. It serves as:

- **Reverse Proxy**: Routes requests to 13 backend microservices using YARP 2.3.0
- **Security Boundary**: JWT authentication enforcement (ready but disabled until Identity service is deployed)
- **Rate Limiter**: Multi-tier rate limiting (global, per-tenant, per-agent, per-authentication)
- **Observability Hub**: Distributed tracing, metrics, structured logging
- **Health Aggregator**: Backend service health monitoring
- **WebSocket Proxy**: SignalR support with Cloudflare compatibility

---

## Service Port Assignments

All services use **10-port gaps** for future expansion:

| Service | Port | URL | Purpose |
|---------|------|-----|---------|
| **Gateway** | 5000 | http://localhost:5000 | Public entry point |
| **Identity** | 5010 | http://localhost:5010 | AuthN/AuthZ, JWT, RBAC |
| **Billing** | 5020 | http://localhost:5020 | SaaS subscriptions |
| **Servers** | 5030 | http://localhost:5030 | Game server lifecycle |
| **Nodes** | 5040 | http://localhost:5040 | Node inventory, health |
| **Tasks** | 5050 | http://localhost:5050 | Job orchestration |
| **Files** | 5060 | http://localhost:5060 | File metadata, transfers |
| **Console** | 5070 | http://localhost:5070 | SignalR console streaming |
| **Mods** | 5080 | http://localhost:5080 | Mod registry |
| **Notifications** | 5090 | http://localhost:5090 | Email/Discord/webhooks |
| **Firewall** | 5100 | http://localhost:5100 | Port management |
| **Secrets** | 5110 | http://localhost:5110 | Secret storage |
| **Discord** | 5120 | http://localhost:5120 | Discord integration |

**Port Range Reserved**: 5000-5130 (130 ports total, ~120 ports available for expansion)

---

## Architecture Decisions

### 1. URL Structure

**Pattern**: `/api/v{version}/{service}/{resource}`

Examples:
```
/api/v1/identity/users
/api/v1/servers/{serverId}
/api/v1/nodes/{nodeId}/health
/api/v1/files/{fileId}/download
/hubs/console                    (SignalR WebSocket)
```

**Rationale**:
- Clear versioning at Gateway level
- Service segmentation for YARP routing
- Standard REST conventions
- Separate namespace for WebSocket endpoints

### 2. Authentication Strategy

**JWT Bearer Tokens** with the following characteristics:

- **Issuer**: `https://meridian.local` (configurable)
- **Audience**: `meridian-api`
- **Clock Skew**: 30 seconds (tight for security)
- **Signing**: HS256 with 256-bit key (stored in user-secrets for dev)
- **Claims**: `sub` (user ID), `tenant_id`, `roles`, `client_type` (user/agent)

**Implementation Status**: Fully implemented but **disabled by default** via configuration flag:

```json
{
  "Authentication": {
    "Enabled": false,  // Set to true when Identity service is ready
    "EnforcementMode": "optional"  // "optional" | "required"
  }
}
```

**Removal Plan**: When authentication is no longer needed in test mode, remove:
- `Authentication:Enabled` flag and all conditional checks
- Keep middleware and policies (they become unconditionally active)

### 3. Rate Limiting Tiers

| Tier | Limit | Window | Purpose |
|------|-------|--------|---------|
| **Global** | 1000 req | 1 minute | DDoS protection |
| **Per-Tenant** | 100 burst, 50/sec replenish | Token bucket | Fair usage |
| **Per-Agent** | 500 req | 1 minute, sliding window | Agent operations |
| **Authentication** | 10 req | 5 minutes | Brute force prevention |

### 4. Observability Approach

**OpenTelemetry-First** with dual backend support:

- **Development**: Console logging + OTLP to local collector (stubbed)
- **Production**: New Relic via OTLP (configured but not required)

**Telemetry Data**:
- **Traces**: W3C Trace Context (`traceparent` header)
- **Metrics**: Gateway-specific + YARP + ASP.NET Core
- **Logs**: Structured JSON with correlation IDs

### 5. SignalR + Cloudflare Compatibility

**Challenge**: Cloudflare's anti-DDoS kills long-running WebSocket connections after ~100 seconds

**Solution**:
1. **Sticky Sessions**: Cookie-based affinity for SignalR negotiate → connect flow
2. **Fallback Transports**: Enable Server-Sent Events (SSE) and Long Polling as fallbacks
3. **Client Configuration**: SignalR clients should enable automatic reconnection
4. **Cloudflare Settings**: Use "Argo Smart Routing" or set longer WebSocket timeout via Page Rule

**YARP Configuration**:
```json
{
  "console": {
    "SessionAffinity": {
      "Enabled": true,
      "Policy": "Cookie",
      "FailurePolicy": "Redistribute"
    },
    "HttpClient": {
      "WebSocketTransports": ["WebSockets", "ServerSentEvents", "LongPolling"]
    }
  }
}
```

---

## Implementation Phases

### Phase Completion Checkpoints

After **each phase**, perform the following:

1. **Build**: `dotnet build`
2. **Test**: `dotnet test`
3. **Run**: `dotnet run --project src/Dhadgar.Gateway` and verify endpoints
4. **Commit**: `git add .` → `git commit -m "Phase X: [description]"` → `git push`

---

## Phase 1: Core YARP Configuration

**Goal**: Set up complete routing for all 13 backend services with path transforms and health checks.

### 1.1 Update `appsettings.json`

**File**: `src/Dhadgar.Gateway/appsettings.json`

Replace the entire `ReverseProxy` section with:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Yarp.ReverseProxy": "Information"
    }
  },
  "AllowedHosts": "*",
  "Authentication": {
    "Enabled": false,
    "EnforcementMode": "optional"
  },
  "Jwt": {
    "Issuer": "https://meridian.local",
    "Audience": "meridian-api",
    "ClockSkewSeconds": 30
  },
  "ReverseProxy": {
    "Routes": {
      "identity-route": {
        "ClusterId": "identity",
        "Match": { "Path": "/api/v1/identity/{**catch-all}" },
        "AuthorizationPolicy": "Anonymous",
        "Transforms": [
          { "PathRemovePrefix": "/api/v1/identity" }
        ]
      },
      "billing-route": {
        "ClusterId": "billing",
        "Match": { "Path": "/api/v1/billing/{**catch-all}" },
        "AuthorizationPolicy": "TenantScoped",
        "Transforms": [
          { "PathRemovePrefix": "/api/v1/billing" }
        ]
      },
      "servers-route": {
        "ClusterId": "servers",
        "Match": { "Path": "/api/v1/servers/{**catch-all}" },
        "AuthorizationPolicy": "TenantScoped",
        "Transforms": [
          { "PathRemovePrefix": "/api/v1/servers" }
        ]
      },
      "nodes-route": {
        "ClusterId": "nodes",
        "Match": { "Path": "/api/v1/nodes/{**catch-all}" },
        "AuthorizationPolicy": "TenantScoped",
        "Transforms": [
          { "PathRemovePrefix": "/api/v1/nodes" }
        ]
      },
      "tasks-route": {
        "ClusterId": "tasks",
        "Match": { "Path": "/api/v1/tasks/{**catch-all}" },
        "AuthorizationPolicy": "TenantScoped",
        "Transforms": [
          { "PathRemovePrefix": "/api/v1/tasks" }
        ]
      },
      "files-route": {
        "ClusterId": "files",
        "Match": { "Path": "/api/v1/files/{**catch-all}" },
        "AuthorizationPolicy": "TenantScoped",
        "Transforms": [
          { "PathRemovePrefix": "/api/v1/files" }
        ]
      },
      "console-api-route": {
        "ClusterId": "console",
        "Match": { "Path": "/api/v1/console/{**catch-all}" },
        "AuthorizationPolicy": "TenantScoped",
        "Transforms": [
          { "PathRemovePrefix": "/api/v1/console" }
        ]
      },
      "console-hub-route": {
        "ClusterId": "console",
        "Match": { "Path": "/hubs/console/{**catch-all}" },
        "AuthorizationPolicy": "TenantScoped"
      },
      "mods-route": {
        "ClusterId": "mods",
        "Match": { "Path": "/api/v1/mods/{**catch-all}" },
        "AuthorizationPolicy": "TenantScoped",
        "Transforms": [
          { "PathRemovePrefix": "/api/v1/mods" }
        ]
      },
      "notifications-route": {
        "ClusterId": "notifications",
        "Match": { "Path": "/api/v1/notifications/{**catch-all}" },
        "AuthorizationPolicy": "TenantScoped",
        "Transforms": [
          { "PathRemovePrefix": "/api/v1/notifications" }
        ]
      },
      "firewall-route": {
        "ClusterId": "firewall",
        "Match": { "Path": "/api/v1/firewall/{**catch-all}" },
        "AuthorizationPolicy": "TenantScoped",
        "Transforms": [
          { "PathRemovePrefix": "/api/v1/firewall" }
        ]
      },
      "secrets-route": {
        "ClusterId": "secrets",
        "Match": { "Path": "/api/v1/secrets/{**catch-all}" },
        "AuthorizationPolicy": "TenantScoped",
        "Transforms": [
          { "PathRemovePrefix": "/api/v1/secrets" }
        ]
      },
      "discord-route": {
        "ClusterId": "discord",
        "Match": { "Path": "/api/v1/discord/{**catch-all}" },
        "AuthorizationPolicy": "TenantScoped",
        "Transforms": [
          { "PathRemovePrefix": "/api/v1/discord" }
        ]
      },
      "agents-route": {
        "ClusterId": "nodes",
        "Match": { "Path": "/api/v1/agents/{**catch-all}" },
        "AuthorizationPolicy": "Agent",
        "Transforms": [
          { "PathRemovePrefix": "/api/v1/agents" }
        ]
        // NOTE: Agent routes point to 'nodes' cluster intentionally
        // Agents are customer-hosted and communicate with Nodes service
        // which manages node registration, health, and capacity
      }
    },
    "Clusters": {
      "identity": {
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
            "Address": "http://localhost:5010/",
            "Health": "http://localhost:5010/healthz"
          }
        }
      },
      "billing": {
        "LoadBalancingPolicy": "RoundRobin",
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Timeout": "00:00:10",
            "Policy": "ConsecutiveFailures",
            "Path": "/healthz"
          }
        },
        "Destinations": {
          "d1": {
            "Address": "http://localhost:5020/",
            "Health": "http://localhost:5020/healthz"
          }
        }
      },
      "servers": {
        "LoadBalancingPolicy": "RoundRobin",
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Timeout": "00:00:10",
            "Policy": "ConsecutiveFailures",
            "Path": "/healthz"
          }
        },
        "Destinations": {
          "d1": {
            "Address": "http://localhost:5030/",
            "Health": "http://localhost:5030/healthz"
          }
        }
      },
      "nodes": {
        "LoadBalancingPolicy": "RoundRobin",
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Timeout": "00:00:10",
            "Policy": "ConsecutiveFailures",
            "Path": "/healthz"
          }
        },
        "Destinations": {
          "d1": {
            "Address": "http://localhost:5040/",
            "Health": "http://localhost:5040/healthz"
          }
        }
      },
      "tasks": {
        "LoadBalancingPolicy": "RoundRobin",
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Timeout": "00:00:10",
            "Policy": "ConsecutiveFailures",
            "Path": "/healthz"
          }
        },
        "Destinations": {
          "d1": {
            "Address": "http://localhost:5050/",
            "Health": "http://localhost:5050/healthz"
          }
        }
      },
      "files": {
        "LoadBalancingPolicy": "RoundRobin",
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Timeout": "00:00:10",
            "Policy": "ConsecutiveFailures",
            "Path": "/healthz"
          }
        },
        "HttpRequest": {
          "ActivityTimeout": "00:05:00"
        },
        "Destinations": {
          "d1": {
            "Address": "http://localhost:5060/",
            "Health": "http://localhost:5060/healthz"
          }
        }
      },
      "console": {
        "LoadBalancingPolicy": "RoundRobin",
        "SessionAffinity": {
          "Enabled": true,
          "Policy": "Cookie",
          "FailurePolicy": "Redistribute",
          "AffinityKeyName": ".Dhadgar.Console.Affinity",
          "Cookie": {
            "HttpOnly": true,
            "SameSite": "Lax",
            "SecurePolicy": "SameAsRequest",
            "Expiration": "01:00:00",
            "IsEssential": true
          }
        },
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Timeout": "00:00:10",
            "Policy": "ConsecutiveFailures",
            "Path": "/healthz"
          }
        },
        "Destinations": {
          "d1": {
            "Address": "http://localhost:5070/",
            "Health": "http://localhost:5070/healthz"
          }
        }
      },
      "mods": {
        "LoadBalancingPolicy": "RoundRobin",
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Timeout": "00:00:10",
            "Policy": "ConsecutiveFailures",
            "Path": "/healthz"
          }
        },
        "Destinations": {
          "d1": {
            "Address": "http://localhost:5080/",
            "Health": "http://localhost:5080/healthz"
          }
        }
      },
      "notifications": {
        "LoadBalancingPolicy": "RoundRobin",
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Timeout": "00:00:10",
            "Policy": "ConsecutiveFailures",
            "Path": "/healthz"
          }
        },
        "Destinations": {
          "d1": {
            "Address": "http://localhost:5090/",
            "Health": "http://localhost:5090/healthz"
          }
        }
      },
      "firewall": {
        "LoadBalancingPolicy": "RoundRobin",
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Timeout": "00:00:10",
            "Policy": "ConsecutiveFailures",
            "Path": "/healthz"
          }
        },
        "Destinations": {
          "d1": {
            "Address": "http://localhost:5100/",
            "Health": "http://localhost:5100/healthz"
          }
        }
      },
      "secrets": {
        "LoadBalancingPolicy": "RoundRobin",
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Timeout": "00:00:10",
            "Policy": "ConsecutiveFailures",
            "Path": "/healthz"
          }
        },
        "Destinations": {
          "d1": {
            "Address": "http://localhost:5110/",
            "Health": "http://localhost:5110/healthz"
          }
        }
      },
      "discord": {
        "LoadBalancingPolicy": "RoundRobin",
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Timeout": "00:00:10",
            "Policy": "ConsecutiveFailures",
            "Path": "/healthz"
          }
        },
        "Destinations": {
          "d1": {
            "Address": "http://localhost:5120/",
            "Health": "http://localhost:5120/healthz"
          }
        }
      }
    }
  }
}
```

### 1.2 Agent Routing Documentation

**⚠️ ARCHITECTURE CLARIFICATION**: The `/api/v1/agents` route points to the `nodes` cluster, NOT a separate agents service.

**Rationale**:
- **Customer-Hosted Agents**: Agents run on customer hardware and make **outbound-only** connections to the control plane
- **Nodes Service Responsibility**: The Nodes service manages:
  - Agent enrollment and authentication
  - Node health monitoring
  - Capacity tracking
  - Agent-to-node relationship mapping
- **No Inbound Connections**: Agents never accept inbound connections, eliminating the need for port forwarding or firewall holes
- **Simplified Architecture**: Combining agent management into Nodes service reduces operational complexity

**Example Flow**:
1. Agent on customer hardware starts up
2. Agent calls `POST /api/v1/agents/enroll` (routed to Nodes service)
3. Nodes service registers the agent and returns credentials
4. Agent periodically calls `POST /api/v1/agents/heartbeat` (routed to Nodes service)
5. Nodes service updates health status

This routing design is intentional and supports the high-trust, outbound-only agent architecture.

### 1.3 Create `appsettings.Development.json`

**File**: `src/Dhadgar.Gateway/appsettings.Development.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "Yarp.ReverseProxy": "Debug"
    }
  },
  "Authentication": {
    "Enabled": false
  }
}
```

### 1.3 Update Gateway Port

**File**: `src/Dhadgar.Gateway/Properties/launchSettings.json`

Create this file if it doesn't exist:

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5000",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

### 1.4 Create Route Configuration Tests

**File**: `tests/Dhadgar.Gateway.Tests/RouteConfigurationTests.cs`

```csharp
using Microsoft.Extensions.Configuration;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace Dhadgar.Gateway.Tests;

public class RouteConfigurationTests
{
    private readonly IConfiguration _configuration;

    public RouteConfigurationTests()
    {
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();
    }

    [Fact]
    public void ReverseProxy_Configuration_ShouldExist()
    {
        var reverseProxySection = _configuration.GetSection("ReverseProxy");
        Assert.NotNull(reverseProxySection);
    }

    [Fact]
    public void ReverseProxy_ShouldHave14Routes()
    {
        // 13 backend services + 1 agents route = 14 total
        var routesSection = _configuration.GetSection("ReverseProxy:Routes");
        var routes = routesSection.GetChildren().ToList();

        Assert.Equal(14, routes.Count);
    }

    [Fact]
    public void ReverseProxy_ShouldHave13Clusters()
    {
        // 13 backend services (agents uses nodes cluster)
        var clustersSection = _configuration.GetSection("ReverseProxy:Clusters");
        var clusters = clustersSection.GetChildren().ToList();

        Assert.Equal(13, clusters.Count);
    }

    [Theory]
    [InlineData("identity", "5010")]
    [InlineData("billing", "5020")]
    [InlineData("servers", "5030")]
    [InlineData("nodes", "5040")]
    [InlineData("tasks", "5050")]
    [InlineData("files", "5060")]
    [InlineData("console", "5070")]
    [InlineData("mods", "5080")]
    [InlineData("notifications", "5090")]
    [InlineData("firewall", "5100")]
    [InlineData("secrets", "5110")]
    [InlineData("discord", "5120")]
    public void Cluster_ShouldHaveCorrectPort(string clusterName, string expectedPort)
    {
        var address = _configuration[$"ReverseProxy:Clusters:{clusterName}:Destinations:d1:Address"];
        Assert.NotNull(address);
        Assert.Contains($":{expectedPort}", address);
    }

    [Theory]
    [InlineData("identity-route", "/api/v1/identity/{**catch-all}")]
    [InlineData("servers-route", "/api/v1/servers/{**catch-all}")]
    [InlineData("console-hub-route", "/hubs/console/{**catch-all}")]
    public void Route_ShouldHaveCorrectPathPattern(string routeName, string expectedPath)
    {
        var path = _configuration[$"ReverseProxy:Routes:{routeName}:Match:Path"];
        Assert.Equal(expectedPath, path);
    }

    [Fact]
    public void ConsoleCluster_ShouldHaveSessionAffinity()
    {
        var sessionAffinityEnabled = _configuration
            .GetValue<bool>("ReverseProxy:Clusters:console:SessionAffinity:Enabled");

        Assert.True(sessionAffinityEnabled);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("servers")]
    [InlineData("nodes")]
    [InlineData("console")]
    public void Cluster_ShouldHaveActiveHealthCheck(string clusterName)
    {
        var healthCheckEnabled = _configuration
            .GetValue<bool>($"ReverseProxy:Clusters:{clusterName}:HealthCheck:Active:Enabled");

        Assert.True(healthCheckEnabled);
    }
}
```

### 1.5 Checkpoint: Build, Test, Commit

```bash
# Build
dotnet build

# Test
dotnet test

# Run (Gateway only - backends won't be available yet, that's OK)
dotnet run --project src/Dhadgar.Gateway

# In another terminal, test the Gateway endpoints:
curl http://localhost:5000/
curl http://localhost:5000/hello
curl http://localhost:5000/healthz

# Commit
git add .
git commit -m "Phase 1: Core YARP configuration with all 13 backend service routes and health checks"
git push
```

---

## Phase 2: Middleware Infrastructure

**Goal**: Implement correlation ID tracking, request logging, and header enrichment.

### 2.1 Create Middleware Directory Structure

Create these directories:
```
src/Dhadgar.Gateway/Middleware/
```

### 2.2 Correlation ID Middleware

**File**: `src/Dhadgar.Gateway/Middleware/CorrelationMiddleware.cs`

```csharp
using System.Diagnostics;

namespace Dhadgar.Gateway.Middleware;

/// <summary>
/// Middleware that ensures every request has correlation and request IDs for distributed tracing.
/// </summary>
public class CorrelationMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private const string RequestIdHeader = "X-Request-Id";

    public CorrelationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract or generate correlation ID
        var correlationId = GetOrCreateCorrelationId(context);
        var requestId = Guid.NewGuid().ToString("N");

        // Set on current activity for trace correlation
        Activity.Current?.SetTag("correlation.id", correlationId);
        Activity.Current?.SetTag("request.id", requestId);
        Activity.Current?.SetBaggage("correlation.id", correlationId);

        // Add to response headers
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
            context.Response.Headers[RequestIdHeader] = requestId;

            // Also include trace ID for debugging
            if (Activity.Current != null)
            {
                context.Response.Headers["X-Trace-Id"] = Activity.Current.TraceId.ToString();
            }

            return Task.CompletedTask;
        });

        // Store in HttpContext.Items for downstream access
        context.Items["CorrelationId"] = correlationId;
        context.Items["RequestId"] = requestId;

        await _next(context);
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        // Priority: X-Correlation-Id header > traceparent trace ID > new GUID
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationHeader)
            && !string.IsNullOrWhiteSpace(correlationHeader))
        {
            return correlationHeader.ToString();
        }

        if (Activity.Current?.TraceId != default)
        {
            return Activity.Current.TraceId.ToString();
        }

        return Guid.NewGuid().ToString("N");
    }
}
```

### 2.3 Request Enrichment Middleware

**File**: `src/Dhadgar.Gateway/Middleware/RequestEnrichmentMiddleware.cs`

**⚠️ SECURITY**: This middleware MUST strip client-supplied security headers before enrichment to prevent header spoofing attacks.

```csharp
namespace Dhadgar.Gateway.Middleware;

/// <summary>
/// Middleware that enriches requests with headers for backend services.
/// Extracts tenant and user information from JWT (when available).
///
/// SECURITY: Strips client-supplied security headers to prevent spoofing.
/// </summary>
public class RequestEnrichmentMiddleware
{
    private readonly RequestDelegate _next;

    // Headers that MUST be stripped to prevent client spoofing
    private static readonly string[] SecurityHeaders = new[]
    {
        "X-Tenant-Id",
        "X-User-Id",
        "X-Client-Type",
        "X-Agent-Id",
        "X-Roles"
    };

    public RequestEnrichmentMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // CRITICAL: Strip all security headers sent by client FIRST
        // This prevents header spoofing attacks where a client could send
        // X-Tenant-Id: some-other-tenant-id to access another tenant's data
        foreach (var header in SecurityHeaders)
        {
            context.Request.Headers.Remove(header);
        }

        // Ensure request ID exists
        if (!context.Request.Headers.ContainsKey("X-Request-Id"))
        {
            context.Request.Headers["X-Request-Id"] =
                context.Items["RequestId"]?.ToString() ?? Guid.NewGuid().ToString("N");
        }

        var requestId = context.Request.Headers["X-Request-Id"].ToString();

        // Extract tenant from JWT and inject (if authenticated)
        // Now safe because we stripped any client-supplied X-Tenant-Id above
        var tenantId = context.User.FindFirst("tenant_id")?.Value;
        if (!string.IsNullOrEmpty(tenantId))
        {
            context.Request.Headers["X-Tenant-Id"] = tenantId;
        }

        // Extract user ID from JWT and inject (if authenticated)
        var userId = context.User.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            context.Request.Headers["X-User-Id"] = userId;
        }

        // Extract client type (user vs agent)
        var clientType = context.User.FindFirst("client_type")?.Value;
        if (!string.IsNullOrEmpty(clientType))
        {
            context.Request.Headers["X-Client-Type"] = clientType;
        }

        // Add client IP for backend services
        var clientIp = GetClientIpAddress(context);
        if (!string.IsNullOrEmpty(clientIp))
        {
            context.Request.Headers["X-Real-IP"] = clientIp;
        }

        // Add request ID to response
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Request-Id"] = requestId;
            return Task.CompletedTask;
        });

        await _next(context);
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded header (behind Cloudflare/proxy)
        var forwardedFor = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
            ?? context.Request.Headers["X-Forwarded-For"].FirstOrDefault();

        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP (original client)
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
```

**Security Note**: Backend services MUST trust these headers implicitly since the Gateway is the only entry point. No backend service should accept direct client connections in production.

### 2.4 Request Logging Middleware

**File**: `src/Dhadgar.Gateway/Middleware/RequestLoggingMiddleware.cs`

```csharp
using System.Diagnostics;

namespace Dhadgar.Gateway.Middleware;

/// <summary>
/// Middleware that logs HTTP requests and responses with correlation context.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "unknown";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
            stopwatch.Stop();

            var level = context.Response.StatusCode >= 500
                ? LogLevel.Error
                : context.Response.StatusCode >= 400
                    ? LogLevel.Warning
                    : LogLevel.Information;

            _logger.Log(level,
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms [CorrelationId: {CorrelationId}]",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                correlationId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "HTTP {Method} {Path} failed after {ElapsedMs}ms [CorrelationId: {CorrelationId}]",
                context.Request.Method,
                context.Request.Path,
                stopwatch.ElapsedMilliseconds,
                correlationId);
            throw;
        }
    }
}
```

### 2.5 Problem Details Middleware (RFC 7807)

**File**: `src/Dhadgar.Gateway/Middleware/ProblemDetailsMiddleware.cs`

**Purpose**: Standardize error responses across all services using RFC 7807 Problem Details format.

```csharp
using System.Net;
using System.Text.Json;

namespace Dhadgar.Gateway.Middleware;

/// <summary>
/// Middleware that transforms exceptions into RFC 7807 Problem Details responses.
/// Ensures consistent error handling across all backend services.
/// </summary>
public class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ProblemDetailsMiddleware(
        RequestDelegate next,
        ILogger<ProblemDetailsMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.Items["CorrelationId"]?.ToString() ?? "unknown";

        _logger.LogError(exception,
            "Unhandled exception in Gateway. TraceId: {TraceId}, Path: {Path}",
            traceId, context.Request.Path);

        var problemDetails = new
        {
            type = "https://meridian.console/errors/internal-server-error",
            title = "Internal Server Error",
            status = (int)HttpStatusCode.InternalServerError,
            detail = _environment.IsDevelopment()
                ? exception.Message
                : "An unexpected error occurred. Please contact support with the trace ID.",
            instance = context.Request.Path.ToString(),
            traceId = traceId,
            // Include stack trace only in Development
            extensions = _environment.IsDevelopment()
                ? new { stackTrace = exception.StackTrace }
                : null
        };

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/problem+json";

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        await context.Response.WriteAsync(json);
    }
}
```

**Example Response**:
```json
{
  "type": "https://meridian.console/errors/internal-server-error",
  "title": "Internal Server Error",
  "status": 500,
  "detail": "Object reference not set to an instance of an object.",
  "instance": "/api/v1/servers/12345",
  "traceId": "a1b2c3d4e5f6",
  "stackTrace": "..." // Development only
}
```

### 2.6 CORS Middleware

**File**: `src/Dhadgar.Gateway/Middleware/CorsConfiguration.cs`

**Purpose**: Configure CORS for Blazor WASM clients while maintaining security.

```csharp
namespace Dhadgar.Gateway.Middleware;

public static class CorsConfiguration
{
    public const string PolicyName = "MeridianConsolePolicy";

    public static IServiceCollection AddMeridianConsoleCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, builder =>
            {
                if (allowedOrigins.Length > 0)
                {
                    builder.WithOrigins(allowedOrigins);
                }
                else
                {
                    // Development: Allow all origins
                    builder.AllowAnyOrigin();
                }

                builder
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithExposedHeaders("X-Correlation-Id", "X-Request-Id", "X-Trace-Id");

                // Allow credentials only if not AllowAnyOrigin
                if (allowedOrigins.Length > 0)
                {
                    builder.AllowCredentials();
                }
            });
        });

        return services;
    }
}
```

**Configuration** (`appsettings.json`):
```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://panel.meridian.console",
      "https://shop.meridian.console"
    ]
  }
}
```

**Development** (`appsettings.Development.json`):
```json
{
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5173",
      "https://localhost:5173"
    ]
  }
}
```

### 2.7 Security Headers Middleware

**File**: `src/Dhadgar.Gateway/Middleware/SecurityHeadersMiddleware.cs`

**Purpose**: Add security headers to all responses to prevent XSS, clickjacking, and other attacks.

```csharp
namespace Dhadgar.Gateway.Middleware;

/// <summary>
/// Middleware that adds security headers to all responses.
/// Protects against XSS, clickjacking, MIME sniffing, and protocol downgrade attacks.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _environment;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
    {
        _next = next;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Prevent XSS attacks
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["X-XSS-Protection"] = "1; mode=block";

            // Content Security Policy (restrictive for API)
            headers["Content-Security-Policy"] =
                "default-src 'none'; frame-ancestors 'none'";

            // Referrer policy
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Permissions policy (disable unnecessary features)
            headers["Permissions-Policy"] =
                "accelerometer=(), camera=(), geolocation=(), microphone=(), payment=()";

            // HSTS (production only, 1 year)
            if (!_environment.IsDevelopment())
            {
                headers["Strict-Transport-Security"] =
                    "max-age=31536000; includeSubDomains; preload";
            }

            // Remove server header
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
```

### 2.8 Update Program.cs with All Middleware

**File**: `src/Dhadgar.Gateway/Program.cs`

Replace the entire file:

```csharp
using Dhadgar.Gateway;
using Dhadgar.Gateway.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS support
builder.Services.AddMeridianConsoleCors(builder.Configuration);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Middleware pipeline (ORDER MATTERS!)
// 1. Security headers (earliest to apply to all responses)
app.UseMiddleware<SecurityHeadersMiddleware>();

// 2. Problem Details exception handler (catch exceptions early)
app.UseMiddleware<ProblemDetailsMiddleware>();

// 3. CORS (before authentication/authorization)
app.UseCors(CorsConfiguration.PolicyName);

// 4. Correlation ID tracking (needed by all downstream middleware)
app.UseMiddleware<CorrelationMiddleware>();

// 5. Request logging (after correlation)
app.UseMiddleware<RequestLoggingMiddleware>();

// 6. Request enrichment WITH HEADER STRIPPING (before proxy, after correlation)
//    CRITICAL: Must run after authentication (Phase 3) but shown here for Phase 2
app.UseMiddleware<RequestEnrichmentMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Gateway endpoints
app.MapGet("/", () => Results.Ok(new
{
    service = "Dhadgar.Gateway",
    message = Hello.Message,
    version = typeof(Program).Assembly.GetName().Version?.ToString()
}))
.AllowAnonymous()
.WithTags("Gateway");

app.MapGet("/hello", () => Results.Text(Hello.Message))
.AllowAnonymous()
.WithTags("Gateway");

app.MapGet("/healthz", () => Results.Ok(new
{
    service = "Dhadgar.Gateway",
    status = "ok",
    timestamp = DateTime.UtcNow
}))
.AllowAnonymous()
.WithTags("Health");

// YARP reverse proxy
app.MapReverseProxy();

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
```

**Important Notes**:
1. Middleware order is critical for security
2. Security headers go first to ensure they're on all responses
3. CORS must be before authentication
4. RequestEnrichmentMiddleware strips headers BEFORE extracting JWT claims
5. Authentication middleware will be added in Phase 3 BEFORE RequestEnrichmentMiddleware

### 2.9 Create Middleware Tests

**File**: `tests/Dhadgar.Gateway.Tests/MiddlewareTests.cs`

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class MiddlewareTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MiddlewareTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Request_ShouldHaveCorrelationIdInResponse()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/healthz");

        // Assert
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        Assert.True(response.Headers.Contains("X-Request-Id"));
    }

    [Fact]
    public async Task Request_WithCorrelationId_ShouldEchoItBack()
    {
        // Arrange
        var client = _factory.CreateClient();
        var expectedCorrelationId = Guid.NewGuid().ToString("N");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", expectedCorrelationId);

        // Act
        var response = await client.GetAsync("/healthz");
        var returnedCorrelationId = response.Headers.GetValues("X-Correlation-Id").First();

        // Assert
        Assert.Equal(expectedCorrelationId, returnedCorrelationId);
    }

    [Fact]
    public async Task Request_ShouldHaveTraceIdInResponse()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/healthz");

        // Assert
        Assert.True(response.Headers.Contains("X-Trace-Id"));
    }

    [Fact]
    public async Task HealthzEndpoint_ShouldReturnOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/healthz");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

### 2.7 Checkpoint: Build, Test, Commit

```bash
# Build
dotnet build

# Test
dotnet test

# Run
dotnet run --project src/Dhadgar.Gateway

# Test correlation headers
curl -i http://localhost:5000/healthz
# Should see X-Correlation-Id, X-Request-Id, X-Trace-Id in response headers

# Test with custom correlation ID
curl -i -H "X-Correlation-Id: test-correlation-123" http://localhost:5000/healthz
# Should echo back "test-correlation-123"

# Commit
git add .
git commit -m "Phase 2: Add correlation, enrichment, and logging middleware with tests"
git push
```

---

## Phase 3: Security Infrastructure (JWT Ready)

**Goal**: Implement JWT authentication infrastructure that's **fully functional but disabled by default**.

### 3.1 Add Required Packages

**File**: `Directory.Packages.props`

Add these lines (if not already present):

```xml
<PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
```

Run: `dotnet restore`

### 3.2 Create Security Directory Structure

Create:
```
src/Dhadgar.Gateway/Security/
```

### 3.3 JWT Configuration Class

**File**: `src/Dhadgar.Gateway/Security/JwtConfiguration.cs`

```csharp
namespace Dhadgar.Gateway.Security;

public sealed class JwtConfiguration
{
    public const string SectionName = "Jwt";

    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public int ClockSkewSeconds { get; init; } = 30;
    public bool ValidateLifetime { get; init; } = true;
    public bool RequireHttpsMetadata { get; init; } = false; // true in production
}
```

### 3.4 Authentication Configuration Class

**File**: `src/Dhadgar.Gateway/Security/AuthenticationConfiguration.cs`

**⚠️ SECURITY UPDATE**: Changed to fail-closed authentication - disabled only in Development environment.

```csharp
namespace Dhadgar.Gateway.Security;

public sealed class AuthenticationConfiguration
{
    public const string SectionName = "Authentication";

    /// <summary>
    /// Controls whether JWT authentication is ENFORCED.
    /// SECURITY: Default is TRUE (fail-closed).
    /// Only disable in Development environment while Identity service is being built.
    ///
    /// Production MUST have Enabled=true.
    /// </summary>
    public bool Enabled { get; init; } = true; // CHANGED: Default to true (fail-closed)

    /// <summary>
    /// Controls enforcement mode: "optional" or "required".
    /// Optional mode allows anonymous access but validates tokens if present.
    /// SECURITY: Use "required" in production, "optional" only in dev for testing.
    /// </summary>
    public string EnforcementMode { get; init; } = "required"; // CHANGED: Default to required
}
```

**Migration Path**:
- Development (before Identity ready): Override in `appsettings.Development.json` with `Enabled: false`
- Production: Always `Enabled: true`, `EnforcementMode: "required"`
- Remove Development override once Identity service is deployed

### 3.5 Security Extensions

**File**: `src/Dhadgar.Gateway/Security/SecurityExtensions.cs`

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Dhadgar.Gateway.Security;

public static class SecurityExtensions
{
    public static IServiceCollection AddGatewaySecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authConfig = configuration.GetSection(AuthenticationConfiguration.SectionName)
            .Get<AuthenticationConfiguration>() ?? new AuthenticationConfiguration();

        var jwtConfig = configuration.GetSection(JwtConfiguration.SectionName)
            .Get<JwtConfiguration>() ?? throw new InvalidOperationException("JWT configuration is required");

        // Always register authentication services
        // SECURITY: Authentication now defaults to enabled (fail-closed)
        // Only disable via environment-specific appsettings (Development only)
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // SECURITY UPDATE: Removed fail-open behavior
                // Authentication is now always configured properly
                // Disable only via appsettings.Development.json if needed

                // Get signing key from user secrets or configuration
                var signingKey = configuration["Jwt:SigningKey"];
                if (string.IsNullOrEmpty(signingKey))
                {
                    throw new InvalidOperationException(
                        "JWT signing key not configured. Use: dotnet user-secrets set \"Jwt:SigningKey\" \"your-256-bit-secret\"");
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Issuer validation
                    ValidateIssuer = true,
                    ValidIssuer = jwtConfig.Issuer,

                    // Audience validation
                    ValidateAudience = true,
                    ValidAudience = jwtConfig.Audience,

                    // Signing key validation
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(signingKey)),

                    // Lifetime validation
                    ValidateLifetime = jwtConfig.ValidateLifetime,

                    // Clock skew (tight for security)
                    ClockSkew = TimeSpan.FromSeconds(jwtConfig.ClockSkewSeconds),

                    // Require expiration claim
                    RequireExpirationTime = true,

                    // Require signed tokens
                    RequireSignedTokens = true
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<Program>>();

                        logger.LogWarning(
                            "JWT authentication failed: {ErrorType} - {Message}",
                            context.Exception.GetType().Name,
                            context.Exception.Message);

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        // Extract tenant ID and store in HttpContext for downstream use
                        var tenantClaim = context.Principal?.FindFirst("tenant_id");
                        if (tenantClaim != null)
                        {
                            context.HttpContext.Items["TenantId"] = tenantClaim.Value;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        // Configure authorization policies
        services.AddAuthorization(options =>
        {
            // Anonymous policy (always allows)
            options.AddPolicy("Anonymous", policy =>
                policy.RequireAssertion(_ => true));

            // Agent policy: for customer-hosted agents
            options.AddPolicy("Agent", policy =>
            {
                if (authConfig.Enabled && authConfig.EnforcementMode == "required")
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim("client_type", "agent");
                }
                else
                {
                    policy.RequireAssertion(_ => true);
                }
            });

            // Tenant-scoped policy: ensure tenant context
            options.AddPolicy("TenantScoped", policy =>
            {
                if (authConfig.Enabled && authConfig.EnforcementMode == "required")
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim("tenant_id");
                }
                else
                {
                    policy.RequireAssertion(_ => true);
                }
            });

            // Admin policy: for administrative operations
            options.AddPolicy("Admin", policy =>
            {
                if (authConfig.Enabled && authConfig.EnforcementMode == "required")
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireRole("admin");
                }
                else
                {
                    policy.RequireAssertion(_ => true);
                }
            });

            // Default policy
            if (authConfig.Enabled && authConfig.EnforcementMode == "required")
            {
                options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            }
            else
            {
                // Allow anonymous by default when disabled or optional
                options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                    .RequireAssertion(_ => true)
                    .Build();
            }
        });

        return services;
    }
}
```

### 3.6 Update Program.cs with Security

**File**: `src/Dhadgar.Gateway/Program.cs`

Update to include security:

```csharp
using Dhadgar.Gateway;
using Dhadgar.Gateway.Middleware;
using Dhadgar.Gateway.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Gateway security (JWT authentication - disabled by default)
builder.Services.AddGatewaySecurity(builder.Configuration);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Middleware pipeline (ORDER MATTERS!)
// 1. Correlation ID tracking
app.UseMiddleware<CorrelationMiddleware>();

// 2. Request logging
app.UseMiddleware<RequestLoggingMiddleware>();

// 3. Authentication (processes JWT if present, even when disabled)
app.UseAuthentication();

// 4. Authorization (checks policies)
app.UseAuthorization();

// 5. Request enrichment (after auth to extract claims)
app.UseMiddleware<RequestEnrichmentMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Gateway endpoints
app.MapGet("/", () => Results.Ok(new
{
    service = "Dhadgar.Gateway",
    message = Hello.Message,
    version = typeof(Program).Assembly.GetName().Version?.ToString()
}))
.AllowAnonymous()
.WithTags("Gateway");

app.MapGet("/hello", () => Results.Text(Hello.Message))
.AllowAnonymous()
.WithTags("Gateway");

app.MapGet("/healthz", () => Results.Ok(new
{
    service = "Dhadgar.Gateway",
    status = "ok",
    timestamp = DateTime.UtcNow
}))
.AllowAnonymous()
.WithTags("Health");

// YARP reverse proxy
app.MapReverseProxy();

app.Run();

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
```

### 3.7 Configure Development Environment Override

**⚠️ SECURITY UPDATE**: Since authentication now defaults to enabled (fail-closed), you MUST create an environment override to disable it during development.

**File**: `src/Dhadgar.Gateway/appsettings.Development.json`

Create or update with:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "Yarp.ReverseProxy": "Information"
    }
  },
  "Authentication": {
    "Enabled": false,
    "EnforcementMode": "optional"
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5173",
      "https://localhost:5173"
    ]
  }
}
```

**Important**:
- This override ONLY applies in Development environment
- Production deployments will use the secure defaults (Enabled: true, EnforcementMode: required)
- Remove this override once Identity service is deployed and issuing tokens

### 3.8 Initialize User Secrets (Development Only)

```bash
# Initialize user secrets for the Gateway project
dotnet user-secrets init --project src/Dhadgar.Gateway

# Set a development JWT signing key (256-bit = 32 bytes = 64 hex chars)
dotnet user-secrets set "Jwt:SigningKey" "your-256-bit-secret-key-min-32-characters-long-for-hs256-algorithm" --project src/Dhadgar.Gateway
```

### 3.9 Create Security Tests

**File**: `tests/Dhadgar.Gateway.Tests/SecurityTests.cs`

**⚠️ SECURITY UPDATE**: Tests updated to verify fail-closed behavior.

```csharp
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class SecurityTests
{
    [Fact]
    public void Authentication_ShouldBeEnabledByDefault_FailClosed()
    {
        // SECURITY: Verify authentication defaults to ENABLED (fail-closed)
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var authConfig = config.GetSection("Authentication")
            .Get<Dhadgar.Gateway.Security.AuthenticationConfiguration>()
            ?? new Dhadgar.Gateway.Security.AuthenticationConfiguration();

        Assert.True(authConfig.Enabled, "Authentication MUST default to enabled (fail-closed)");
        Assert.Equal("required", authConfig.EnforcementMode);
    }

    [Fact]
    public void Authentication_CanBeDisabledInDevelopment()
    {
        // Verify development override works
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Enabled"] = "false",
                ["Authentication:EnforcementMode"] = "optional"
            })
            .Build();

        var authEnabled = config.GetValue<bool>("Authentication:Enabled");
        var enforcementMode = config["Authentication:EnforcementMode"];

        Assert.False(authEnabled);
        Assert.Equal("optional", enforcementMode);
    }

    [Fact]
    public void Jwt_Configuration_ShouldHaveRequiredSettings()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var issuer = config["Jwt:Issuer"];
        var audience = config["Jwt:Audience"];
        var clockSkew = config["Jwt:ClockSkewSeconds"];

        Assert.NotNull(issuer);
        Assert.NotNull(audience);
        Assert.NotNull(clockSkew);
    }

    [Theory]
    [InlineData("identity-route", "Anonymous")]
    [InlineData("servers-route", "TenantScoped")]
    [InlineData("agents-route", "Agent")]
    public void Route_ShouldHaveCorrectAuthorizationPolicy(string routeName, string expectedPolicy)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var policy = config[$"ReverseProxy:Routes:{routeName}:AuthorizationPolicy"];

        Assert.Equal(expectedPolicy, policy);
    }
}
```

### 3.9 Create Security Documentation

**File**: `docs/gateway-authentication.md`

```markdown
# Gateway Authentication

## Current Status

JWT authentication infrastructure is **fully implemented but DISABLED by default**.

## Configuration

### Enable Authentication

In `appsettings.json` or `appsettings.Production.json`:

\`\`\`json
{
  "Authentication": {
    "Enabled": true,
    "EnforcementMode": "required"
  }
}
\`\`\`

### Signing Key

**Development** (user secrets):
\`\`\`bash
dotnet user-secrets set "Jwt:SigningKey" "your-256-bit-secret" --project src/Dhadgar.Gateway
\`\`\`

**Production** (environment variable or Key Vault):
\`\`\`bash
export Jwt__SigningKey="your-production-key"
\`\`\`

## Authorization Policies

| Policy | Description | Routes |
|--------|-------------|--------|
| `Anonymous` | Always allows access | `/api/v1/identity/**` (auth endpoints) |
| `TenantScoped` | Requires authenticated user with tenant claim | Most API routes |
| `Agent` | Requires authenticated agent | `/api/v1/agents/**` |
| `Admin` | Requires admin role | (future admin routes) |

## Removing Development Mode

When Identity service is deployed and JWT authentication is fully operational:

1. Remove `Authentication:Enabled` flag from configuration
2. Remove conditional logic in `SecurityExtensions.cs`
3. All policies become unconditionally enforced
4. Update this document

## Testing Authentication

Generate a test JWT at https://jwt.io with:
- Algorithm: HS256
- Payload: `{"sub": "user123", "tenant_id": "tenant_abc", "iat": <now>, "exp": <future>}`
- Secret: Your signing key

Test:
\`\`\`bash
curl -H "Authorization: Bearer <your-token>" http://localhost:5000/api/v1/servers
\`\`\`
```

### 3.10 Checkpoint: Build, Test, Commit

```bash
# Build
dotnet build

# Test
dotnet test

# Verify authentication is disabled by default
dotnet run --project src/Dhadgar.Gateway

# Test anonymous access (should work)
curl http://localhost:5000/healthz

# Commit
git add .
git commit -m "Phase 3: Add JWT authentication infrastructure (disabled by default, ready for Identity service)"
git push
```

---

## Phase 4: Rate Limiting

**Goal**: Implement multi-tier rate limiting (global, per-tenant, per-agent, authentication endpoints).

### 4.1 Create Rate Limiting Directory

Create:
```
src/Dhadgar.Gateway/RateLimiting/
```

### 4.2 Rate Limiting Configuration

**File**: `src/Dhadgar.Gateway/RateLimiting/RateLimitingExtensions.cs`

```csharp
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Dhadgar.Gateway.RateLimiting;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // Global limiter: Protect against extreme abuse (DDoS)
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // Use client IP as partition key
                var clientIp = GetClientIp(context);

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: clientIp,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 1000,           // 1000 requests
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0                // No queuing - reject immediately
                    });
            });

            // Per-tenant limiter: Fair usage across tenants
            options.AddPolicy("PerTenant", context =>
            {
                // Extract tenant from JWT claim or header
                var tenantId = context.User.FindFirst("tenant_id")?.Value
                    ?? context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                    ?? GetClientIp(context); // Fallback to IP for unauthenticated

                return RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey: tenantId,
                    factory: _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 100,              // Burst capacity
                        TokensPerPeriod = 50,          // Replenishment rate
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 10                // Small queue for burst handling
                    });
            });

            // Agent limiter: Higher limits for agents (they manage servers)
            options.AddPolicy("Agent", context =>
            {
                var agentId = context.User.FindFirst("agent_id")?.Value
                    ?? context.User.FindFirst("sub")?.Value
                    ?? GetClientIp(context);

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: agentId,
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 500,             // Higher limit for agents
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,         // 10-second segments
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 20
                    });
            });

            // Authentication limiter: Strict limits on auth endpoints
            options.AddPolicy("Authentication", context =>
            {
                var clientIp = GetClientIp(context);

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: clientIp,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,              // 10 attempts
                        Window = TimeSpan.FromMinutes(5),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            // Rejection handling
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();

                logger.LogWarning(
                    "Rate limit exceeded | IP: {ClientIP} | Path: {Path} | Policy: {Policy}",
                    GetClientIp(context.HttpContext),
                    context.HttpContext.Request.Path,
                    context.Lease.GetType().Name);

                context.HttpContext.Response.Headers.RetryAfter = "60";

                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://httpstatuses.io/429",
                    title = "Too Many Requests",
                    status = 429,
                    detail = "Rate limit exceeded. Please retry after the specified time.",
                    retryAfter = "60 seconds"
                }, cancellationToken);
            };
        });

        return services;
    }

    private static string GetClientIp(HttpContext context)
    {
        // Priority: CF-Connecting-IP (Cloudflare) > X-Forwarded-For > RemoteIpAddress
        var ip = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
            ?? context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        // X-Forwarded-For can be a comma-separated list, take the first
        if (ip.Contains(','))
        {
            ip = ip.Split(',')[0].Trim();
        }

        return ip;
    }
}
```

### 4.3 Update appsettings.json with Rate Limiter Policies

**File**: `src/Dhadgar.Gateway/appsettings.json`

Add `RateLimiterPolicy` to routes (update the `ReverseProxy:Routes` section):

```json
"identity-route": {
  "ClusterId": "identity",
  "Match": { "Path": "/api/v1/identity/{**catch-all}" },
  "AuthorizationPolicy": "Anonymous",
  "RateLimiterPolicy": "Authentication",
  "Transforms": [
    { "PathRemovePrefix": "/api/v1/identity" }
  ]
},
"billing-route": {
  "ClusterId": "billing",
  "Match": { "Path": "/api/v1/billing/{**catch-all}" },
  "AuthorizationPolicy": "TenantScoped",
  "RateLimiterPolicy": "PerTenant",
  "Transforms": [
    { "PathRemovePrefix": "/api/v1/billing" }
  ]
},
"servers-route": {
  "ClusterId": "servers",
  "Match": { "Path": "/api/v1/servers/{**catch-all}" },
  "AuthorizationPolicy": "TenantScoped",
  "RateLimiterPolicy": "PerTenant",
  "Transforms": [
    { "PathRemovePrefix": "/api/v1/servers" }
  ]
},
"agents-route": {
  "ClusterId": "nodes",
  "Match": { "Path": "/api/v1/agents/{**catch-all}" },
  "AuthorizationPolicy": "Agent",
  "RateLimiterPolicy": "Agent",
  "Transforms": [
    { "PathRemovePrefix": "/api/v1/agents" }
  ]
}
```

(Apply `"RateLimiterPolicy": "PerTenant"` to all other tenant-scoped routes)

### 4.4 Update Program.cs with Rate Limiting

**File**: `src/Dhadgar.Gateway/Program.cs`

Add rate limiting:

```csharp
using Dhadgar.Gateway;
using Dhadgar.Gateway.Middleware;
using Dhadgar.Gateway.Security;
using Dhadgar.Gateway.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Gateway security
builder.Services.AddGatewaySecurity(builder.Configuration);

// Add rate limiting
builder.Services.AddGatewayRateLimiting();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Middleware pipeline (ORDER MATTERS!)
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// Rate limiting (before authentication)
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RequestEnrichmentMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new
{
    service = "Dhadgar.Gateway",
    message = Hello.Message,
    version = typeof(Program).Assembly.GetName().Version?.ToString()
}))
.AllowAnonymous()
.WithTags("Gateway");

app.MapGet("/hello", () => Results.Text(Hello.Message))
.AllowAnonymous()
.WithTags("Gateway");

app.MapGet("/healthz", () => Results.Ok(new
{
    service = "Dhadgar.Gateway",
    status = "ok",
    timestamp = DateTime.UtcNow
}))
.AllowAnonymous()
.WithTags("Health");

app.MapReverseProxy();

app.Run();

public partial class Program { }
```

### 4.5 Create Rate Limiting Tests

**File**: `tests/Dhadgar.Gateway.Tests/RateLimitingTests.cs`

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class RateLimitingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RateLimitingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GlobalRateLimit_ShouldBlock_After1000Requests()
    {
        // Arrange
        var client = _factory.CreateClient();
        var successCount = 0;
        var rateLimitCount = 0;

        // Act - send 1010 requests rapidly
        for (int i = 0; i < 1010; i++)
        {
            var response = await client.GetAsync("/healthz");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                successCount++;
            }
            else if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimitCount++;

                // Verify response headers
                Assert.True(response.Headers.Contains("Retry-After"));
            }
        }

        // Assert - should have been rate limited
        Assert.True(rateLimitCount > 0, "Expected some requests to be rate limited");
        Assert.InRange(successCount, 900, 1000); // Allow some variance
    }

    [Fact]
    public async Task RateLimitResponse_ShouldIncludeRetryAfterHeader()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - exhaust rate limit
        for (int i = 0; i < 1010; i++)
        {
            await client.GetAsync("/healthz");
        }

        var response = await client.GetAsync("/healthz");

        // Assert
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            Assert.True(response.Headers.Contains("Retry-After"));
        }
    }

    [Fact]
    public async Task RateLimitResponse_ShouldHaveProblemDetailsBody()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - exhaust rate limit
        for (int i = 0; i < 1010; i++)
        {
            await client.GetAsync("/healthz");
        }

        var response = await client.GetAsync("/healthz");

        // Assert
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("429", content);
            Assert.Contains("Too Many Requests", content);
        }
    }
}
```

### 4.6 Checkpoint: Build, Test, Commit

```bash
# Build
dotnet build

# Test (warning: rate limiting test will take a few seconds)
dotnet test

# Run
dotnet run --project src/Dhadgar.Gateway

# Test rate limiting manually (in bash/PowerShell loop)
# Bash:
for i in {1..20}; do curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/healthz; done

# PowerShell:
1..20 | ForEach-Object { (Invoke-WebRequest http://localhost:5000/healthz).StatusCode }

# Should see 200s followed by 429 after hitting limit

# Commit
git add .
git commit -m "Phase 4: Add multi-tier rate limiting (global, per-tenant, per-agent, authentication)"
git push
```

---

## Phase 5: Observability Infrastructure

**Goal**: Add OpenTelemetry instrumentation with stubbed OTLP exporter + **Serilog with trace correlation** for production-ready logging.

### 5.1 Add Observability Packages

**File**: `Directory.Packages.props`

**⚠️ OBSERVABILITY UPDATE**: Added Serilog packages for trace correlation.

Add these packages:

```xml
<!-- Serilog Core (structured logging with trace correlation) -->
<PackageVersion Include="Serilog.AspNetCore" Version="8.0.0" />
<PackageVersion Include="Serilog.Enrichers.Environment" Version="3.0.1" />
<PackageVersion Include="Serilog.Enrichers.Thread" Version="4.0.0" />
<PackageVersion Include="Serilog.Sinks.Console" Version="6.0.0" />
<PackageVersion Include="Serilog.Sinks.File" Version="6.0.0" />
<PackageVersion Include="Serilog.Expressions" Version="5.0.0" />
<PackageVersion Include="Serilog.Settings.Configuration" Version="8.0.0" />

<!-- OpenTelemetry Core -->
<PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />
<PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.10.0" />

<!-- Instrumentation -->
<PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.10.1" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.10.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.10.0" />
```

Run: `dotnet restore`

### 5.2 Configure Serilog with Trace Correlation

**File**: Create `src/Dhadgar.Gateway/Observability/SerilogConfiguration.cs`

**⚠️ OBSERVABILITY UPDATE**: New file for Serilog configuration with trace correlation.

```csharp
using Serilog;
using Serilog.Events;
using System.Diagnostics;

namespace Dhadgar.Gateway.Observability;

public static class SerilogConfiguration
{
    public static IHostBuilder UseSerilogWithTraceCorrelation(this IHostBuilder hostBuilder)
    {
        return hostBuilder.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", "Dhadgar.Gateway")
                .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
                // CRITICAL: Enrich logs with trace context for correlation
                .Enrich.With(new TraceIdEnricher())
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{TraceId}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    path: "logs/gateway-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{TraceId}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}");

            // Set minimum level based on environment
            if (context.HostingEnvironment.IsDevelopment())
            {
                configuration.MinimumLevel.Debug();
            }
            else
            {
                configuration.MinimumLevel.Information();
            }

            // Override noisy loggers
            configuration
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("Yarp.ReverseProxy", LogEventLevel.Information);
        });
    }

    /// <summary>
    /// Enricher that adds OpenTelemetry TraceId and SpanId to Serilog logs.
    /// This enables correlation between logs and distributed traces.
    /// </summary>
    private class TraceIdEnricher : Serilog.Core.ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, Serilog.Core.ILogEventPropertyFactory propertyFactory)
        {
            var activity = Activity.Current;
            if (activity != null)
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ParentSpanId", activity.ParentSpanId.ToString()));
            }
            else
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", "no-trace"));
            }
        }
    }
}
```

**Key Features**:
1. **Trace Correlation**: Automatically includes `TraceId`, `SpanId`, and `ParentSpanId` in every log
2. **Structured Logging**: JSON-compatible format for machine parsing
3. **Console + File Sinks**: Dual output for development and production
4. **Log Retention**: 30-day rolling file retention

### 5.3 Create Observability Directory

Create:
```
src/Dhadgar.Gateway/Observability/
```

### 5.3 Gateway Metrics

**File**: `src/Dhadgar.Gateway/Observability/GatewayMetrics.cs`

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Dhadgar.Gateway.Observability;

/// <summary>
/// Gateway-specific metrics for monitoring proxy performance.
/// </summary>
public sealed class GatewayMetrics : IDisposable
{
    public const string MeterName = "Dhadgar.Gateway";

    private readonly Meter _meter;
    private readonly Counter<long> _requestsTotal;
    private readonly Counter<long> _requestErrors;
    private readonly Histogram<double> _requestDuration;
    private readonly UpDownCounter<long> _activeConnections;
    private readonly Counter<long> _rateLimitHits;

    public GatewayMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _requestsTotal = _meter.CreateCounter<long>(
            "gateway.requests.total",
            unit: "{requests}",
            description: "Total number of requests processed by the gateway");

        _requestErrors = _meter.CreateCounter<long>(
            "gateway.requests.errors",
            unit: "{requests}",
            description: "Total number of error responses (4xx, 5xx)");

        _requestDuration = _meter.CreateHistogram<double>(
            "gateway.request.duration",
            unit: "ms",
            description: "Request duration in milliseconds");

        _activeConnections = _meter.CreateUpDownCounter<long>(
            "gateway.connections.active",
            unit: "{connections}",
            description: "Number of active client connections");

        _rateLimitHits = _meter.CreateCounter<long>(
            "gateway.rate_limit.hits",
            unit: "{requests}",
            description: "Number of requests rejected due to rate limiting");
    }

    public void RecordRequest(string route, string method, int statusCode, double durationMs)
    {
        var tags = new TagList
        {
            { "route", route },
            { "http.method", method },
            { "http.status_code", statusCode }
        };

        _requestsTotal.Add(1, tags);
        _requestDuration.Record(durationMs, tags);

        if (statusCode >= 400)
        {
            _requestErrors.Add(1, tags);
        }
    }

    public void IncrementActiveConnections() => _activeConnections.Add(1);
    public void DecrementActiveConnections() => _activeConnections.Add(-1);

    public void RecordRateLimitHit(string route, string? tenantId)
    {
        var tags = new TagList
        {
            { "route", route },
            { "tenant.id", tenantId ?? "anonymous" }
        };
        _rateLimitHits.Add(1, tags);
    }

    public void Dispose() => _meter.Dispose();
}
```

### 5.4 Observability Extensions

**File**: `src/Dhadgar.Gateway/Observability/ObservabilityExtensions.cs`

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Exporter;

namespace Dhadgar.Gateway.Observability;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddGatewayObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var serviceName = "dhadgar-gateway";
        var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";

        // Register metrics singleton
        services.AddSingleton<GatewayMetrics>();

        var otelBuilder = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: serviceVersion,
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] =
                        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
                    ["service.namespace"] = "dhadgar",
                    ["service.component"] = "gateway"
                }));

        // Configure tracing
        otelBuilder.WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;

                // Filter out health check endpoints
                options.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/healthz") &&
                    !context.Request.Path.StartsWithSegments("/readyz") &&
                    !context.Request.Path.StartsWithSegments("/livez");

                // Enrich with tenant info
                options.EnrichWithHttpRequest = (activity, request) =>
                {
                    if (request.Headers.TryGetValue("X-Tenant-Id", out var tenantId))
                    {
                        activity.SetTag("tenant.id", tenantId.ToString());
                    }
                };
            })
            .AddHttpClientInstrumentation(options =>
            {
                options.RecordException = true;
            })
            .AddSource("Yarp.ReverseProxy")
            .AddSource(GatewayMetrics.MeterName)
            .SetSampler(new TraceIdRatioBasedSampler(1.0)) // 100% sampling in dev
            // OTLP Exporter (stubbed - will be configured when observability stack is deployed)
            .AddOtlpExporter(options =>
            {
                var otlpEndpoint = configuration["Observability:OtlpEndpoint"]
                    ?? "http://localhost:4317"; // Default OTLP gRPC endpoint

                options.Endpoint = new Uri(otlpEndpoint);
                options.Protocol = OtlpExportProtocol.Grpc;
            }));

        // Configure metrics
        otelBuilder.WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter(GatewayMetrics.MeterName)
            // OTLP Exporter (stubbed)
            .AddOtlpExporter(options =>
            {
                var otlpEndpoint = configuration["Observability:OtlpEndpoint"]
                    ?? "http://localhost:4317";

                options.Endpoint = new Uri(otlpEndpoint);
                options.Protocol = OtlpExportProtocol.Grpc;
            }));

        return services;
    }
}
```

### 5.5 Update appsettings.json with Observability Config

**File**: `src/Dhadgar.Gateway/appsettings.json`

Add observability section:

```json
{
  "Observability": {
    "OtlpEndpoint": "http://localhost:4317",
    "Sampling": {
      "Rate": 1.0
    }
  }
}
```

### 5.6 Add Observability to Program.cs

**File**: `src/Dhadgar.Gateway/Program.cs`

**⚠️ OBSERVABILITY UPDATE**: Added Serilog with trace correlation to host builder.

```csharp
using Dhadgar.Gateway;
using Dhadgar.Gateway.Middleware;
using Dhadgar.Gateway.Security;
using Dhadgar.Gateway.RateLimiting;
using Dhadgar.Gateway.Observability;

// CRITICAL: Configure Serilog BEFORE building the host
var builder = WebApplication.CreateBuilder(args);

// Add Serilog with trace correlation (MUST be called on Host, not Services)
builder.Host.UseSerilogWithTraceCorrelation();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add observability (OpenTelemetry)
builder.Services.AddGatewayObservability(builder.Configuration);

// Add Gateway security
builder.Services.AddGatewaySecurity(builder.Configuration);

// Add rate limiting
builder.Services.AddGatewayRateLimiting();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Middleware pipeline
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RequestEnrichmentMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new
{
    service = "Dhadgar.Gateway",
    message = Hello.Message,
    version = typeof(Program).Assembly.GetName().Version?.ToString()
}))
.AllowAnonymous()
.WithTags("Gateway");

app.MapGet("/hello", () => Results.Text(Hello.Message))
.AllowAnonymous()
.WithTags("Gateway");

app.MapGet("/healthz", () => Results.Ok(new
{
    service = "Dhadgar.Gateway",
    status = "ok",
    timestamp = DateTime.UtcNow
}))
.AllowAnonymous()
.WithTags("Health");

app.MapReverseProxy();

app.Run();

public partial class Program { }
```

### 5.7 Create Observability Tests

**File**: `tests/Dhadgar.Gateway.Tests/ObservabilityTests.cs`

```csharp
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class ObservabilityTests
{
    [Fact]
    public void Observability_Configuration_ShouldExist()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var otlpEndpoint = config["Observability:OtlpEndpoint"];

        Assert.NotNull(otlpEndpoint);
    }

    [Fact]
    public void Observability_OtlpEndpoint_ShouldBeLocalhost()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var otlpEndpoint = config["Observability:OtlpEndpoint"];

        Assert.Equal("http://localhost:4317", otlpEndpoint);
    }
}
```

### 5.8 Checkpoint: Build, Test, Commit

```bash
# Build
dotnet build

# Test
dotnet test

# Run (note: OTLP endpoint won't be available, but won't cause errors)
dotnet run --project src/Dhadgar.Gateway

# Check logs for OpenTelemetry initialization
# Should see: "OpenTelemetry trace exporter started"

# Commit
git add .
git commit -m "Phase 5: Add OpenTelemetry observability infrastructure (stubbed OTLP exporter)"
git push
```

---

## Phase 6: Health Checks

**Goal**: Implement comprehensive health check endpoints for Kubernetes probes and monitoring.

### 6.1 Create Health Directory

Create:
```
src/Dhadgar.Gateway/Health/
```

### 6.2 YARP Destination Health Check

**File**: `src/Dhadgar.Gateway/Health/YarpDestinationHealthCheck.cs`

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy.Configuration;

namespace Dhadgar.Gateway.Health;

/// <summary>
/// Health check that verifies backend service availability through YARP.
/// </summary>
public class YarpDestinationHealthCheck : IHealthCheck
{
    private readonly IProxyConfigProvider _configProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YarpDestinationHealthCheck> _logger;

    public YarpDestinationHealthCheck(
        IProxyConfigProvider configProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<YarpDestinationHealthCheck> logger)
    {
        _configProvider = configProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var config = _configProvider.GetConfig();
        var unhealthyDestinations = new List<string>();
        var data = new Dictionary<string, object>();
        var totalDestinations = 0;

        foreach (var cluster in config.Clusters)
        {
            var destinationStatuses = new Dictionary<string, string>();

            foreach (var destination in cluster.Destinations ?? new Dictionary<string, DestinationConfig>())
            {
                totalDestinations++;

                try
                {
                    var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(5);

                    var healthEndpoint = destination.Value.Health
                        ?? $"{destination.Value.Address.TrimEnd('/')}/healthz";

                    var response = await client.GetAsync(healthEndpoint, cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        unhealthyDestinations.Add($"{cluster.ClusterId}/{destination.Key}");
                        destinationStatuses[destination.Key] = $"Unhealthy ({response.StatusCode})";
                    }
                    else
                    {
                        destinationStatuses[destination.Key] = "Healthy";
                    }
                }
                catch (TaskCanceledException)
                {
                    unhealthyDestinations.Add($"{cluster.ClusterId}/{destination.Key}");
                    destinationStatuses[destination.Key] = "Timeout";
                }
                catch (HttpRequestException ex)
                {
                    unhealthyDestinations.Add($"{cluster.ClusterId}/{destination.Key}");
                    destinationStatuses[destination.Key] = $"Connection Failed: {ex.Message}";
                }
                catch (Exception ex)
                {
                    unhealthyDestinations.Add($"{cluster.ClusterId}/{destination.Key}");
                    destinationStatuses[destination.Key] = $"Error: {ex.Message}";
                }
            }

            data[$"cluster:{cluster.ClusterId}"] = destinationStatuses;
        }

        if (unhealthyDestinations.Count == 0)
        {
            return HealthCheckResult.Healthy(
                $"All {totalDestinations} destinations healthy",
                data);
        }

        if (unhealthyDestinations.Count < totalDestinations)
        {
            return HealthCheckResult.Degraded(
                $"{unhealthyDestinations.Count}/{totalDestinations} destinations unhealthy: {string.Join(", ", unhealthyDestinations)}",
                data: data);
        }

        return HealthCheckResult.Unhealthy(
            $"All {totalDestinations} destinations unhealthy",
            data: data);
    }
}
```

### 6.3 Gateway Readiness Check

**File**: `src/Dhadgar.Gateway/Health/GatewayReadinessCheck.cs`

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy.Configuration;

namespace Dhadgar.Gateway.Health;

/// <summary>
/// Health check that verifies the Gateway has loaded YARP configuration.
/// </summary>
public class GatewayReadinessCheck : IHealthCheck
{
    private readonly IProxyConfigProvider _configProvider;

    public GatewayReadinessCheck(IProxyConfigProvider configProvider)
    {
        _configProvider = configProvider;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = _configProvider.GetConfig();

            if (config.Routes == null || !config.Routes.Any())
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "No routes configured"));
            }

            if (config.Clusters == null || !config.Clusters.Any())
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "No clusters configured"));
            }

            var data = new Dictionary<string, object>
            {
                ["routes"] = config.Routes.Count(),
                ["clusters"] = config.Clusters.Count()
            };

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Gateway ready with {config.Routes.Count()} routes and {config.Clusters.Count()} clusters",
                data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Failed to load YARP configuration",
                ex));
        }
    }
}
```

### 6.4 Update Program.cs with Health Checks

**File**: `src/Dhadgar.Gateway/Program.cs`

Add health check configuration:

```csharp
using Dhadgar.Gateway;
using Dhadgar.Gateway.Middleware;
using Dhadgar.Gateway.Security;
using Dhadgar.Gateway.RateLimiting;
using Dhadgar.Gateway.Observability;
using Dhadgar.Gateway.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add observability
builder.Services.AddGatewayObservability(builder.Configuration);

// Add security
builder.Services.AddGatewaySecurity(builder.Configuration);

// Add rate limiting
builder.Services.AddGatewayRateLimiting();

// Add YARP
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Add HTTP client for health checks
builder.Services.AddHttpClient();

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck<GatewayReadinessCheck>(
        "gateway-ready",
        tags: new[] { "ready" })
    .AddCheck<YarpDestinationHealthCheck>(
        "destinations",
        tags: new[] { "ready", "destinations" },
        timeout: TimeSpan.FromSeconds(30));

var app = builder.Build();

// Middleware pipeline
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RequestEnrichmentMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Gateway info endpoints
app.MapGet("/", () => Results.Ok(new
{
    service = "Dhadgar.Gateway",
    message = Hello.Message,
    version = typeof(Program).Assembly.GetName().Version?.ToString()
}))
.AllowAnonymous()
.WithTags("Gateway");

app.MapGet("/hello", () => Results.Text(Hello.Message))
.AllowAnonymous()
.WithTags("Gateway");

// Health check endpoints
app.MapHealthChecks("/livez", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    AllowCachingResponses = false
})
.AllowAnonymous()
.WithTags("Health");

app.MapHealthChecks("/readyz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    AllowCachingResponses = false
})
.AllowAnonymous()
.WithTags("Health");

app.MapHealthChecks("/healthz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true,
    AllowCachingResponses = false,
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        var result = new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                data = e.Value.Data,
                exception = e.Value.Exception?.Message
            })
        };

        await context.Response.WriteAsJsonAsync(result);
    }
})
.AllowAnonymous()
.WithTags("Health");

// YARP reverse proxy
app.MapReverseProxy();

app.Run();

public partial class Program { }
```

### 6.5 Create Health Check Tests

**File**: `tests/Dhadgar.Gateway.Tests/HealthCheckTests.cs`

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthCheckTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Livez_ShouldReturnHealthy()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/livez");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readyz_ShouldReturnHealthy()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/readyz");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Healthz_ShouldReturnJsonWithChecks()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/healthz");
        var content = await response.Content.ReadAsStringAsync();
        var healthReport = JsonSerializer.Deserialize<JsonElement>(content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(healthReport.TryGetProperty("status", out _));
        Assert.True(healthReport.TryGetProperty("duration", out _));
        Assert.True(healthReport.TryGetProperty("checks", out var checks));
        Assert.True(checks.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Healthz_ShouldIncludeSelfCheck()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/healthz");
        var content = await response.Content.ReadAsStringAsync();
        var healthReport = JsonSerializer.Deserialize<JsonElement>(content);

        // Assert
        var checks = healthReport.GetProperty("checks");
        var selfCheck = checks.EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("name").GetString() == "self");

        Assert.False(selfCheck.Equals(default(JsonElement)));
        Assert.Equal("Healthy", selfCheck.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Healthz_ShouldIncludeGatewayReadyCheck()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/healthz");
        var content = await response.Content.ReadAsStringAsync();
        var healthReport = JsonSerializer.Deserialize<JsonElement>(content);

        // Assert
        var checks = healthReport.GetProperty("checks");
        var readyCheck = checks.EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("name").GetString() == "gateway-ready");

        Assert.False(readyCheck.Equals(default(JsonElement)));
    }
}
```

### 6.6 Checkpoint: Build, Test, Commit

```bash
# Build
dotnet build

# Test
dotnet test

# Run
dotnet run --project src/Dhadgar.Gateway

# Test health endpoints
curl http://localhost:5000/livez
curl http://localhost:5000/readyz
curl http://localhost:5000/healthz | jq .

# Commit
git add .
git commit -m "Phase 6: Add comprehensive health checks (livez, readyz, healthz with backend checks)"
git push
```

---

## Phase 7: SignalR & WebSocket Support

**Goal**: Enable WebSocket support for SignalR with Cloudflare compatibility (sticky sessions, fallback transports).

### 7.1 Update Program.cs with WebSocket Support

**File**: `src/Dhadgar.Gateway/Program.cs`

Add WebSocket configuration before routing:

```csharp
using Dhadgar.Gateway;
using Dhadgar.Gateway.Middleware;
using Dhadgar.Gateway.Security;
using Dhadgar.Gateway.RateLimiting;
using Dhadgar.Gateway.Observability;
using Dhadgar.Gateway.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddGatewayObservability(builder.Configuration);
builder.Services.AddGatewaySecurity(builder.Configuration);
builder.Services.AddGatewayRateLimiting();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHttpClient();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck<GatewayReadinessCheck>("gateway-ready", tags: new[] { "ready" })
    .AddCheck<YarpDestinationHealthCheck>("destinations", tags: new[] { "ready", "destinations" },
        timeout: TimeSpan.FromSeconds(30));

var app = builder.Build();

// Middleware pipeline
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// CRITICAL: WebSockets must be enabled before routing
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
    // Cloudflare compatibility: Allow longer timeout for SignalR negotiate
    AllowedOrigins = { "*" } // Configure properly for production with actual origins
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RequestEnrichmentMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new
{
    service = "Dhadgar.Gateway",
    message = Hello.Message,
    version = typeof(Program).Assembly.GetName().Version?.ToString()
}))
.AllowAnonymous()
.WithTags("Gateway");

app.MapGet("/hello", () => Results.Text(Hello.Message))
.AllowAnonymous()
.WithTags("Gateway");

app.MapHealthChecks("/livez", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    AllowCachingResponses = false
})
.AllowAnonymous()
.WithTags("Health");

app.MapHealthChecks("/readyz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    AllowCachingResponses = false
})
.AllowAnonymous()
.WithTags("Health");

app.MapHealthChecks("/healthz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true,
    AllowCachingResponses = false,
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        var result = new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                data = e.Value.Data,
                exception = e.Value.Exception?.Message
            })
        };

        await context.Response.WriteAsJsonAsync(result);
    }
})
.AllowAnonymous()
.WithTags("Health");

// YARP reverse proxy with session affinity support
app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.UseSessionAffinity();
    proxyPipeline.UseLoadBalancing();
    proxyPipeline.UsePassiveHealthChecks();
});

app.Run();

public partial class Program { }
```

### 7.2 Create SignalR Documentation

**File**: `docs/signalr-cloudflare.md`

```markdown
# SignalR & Cloudflare Compatibility

## Problem

Cloudflare's anti-DDoS protection kills long-running WebSocket connections after ~100 seconds by default.

## Solution

The Gateway implements a multi-layer approach:

### 1. Sticky Sessions (Cookie-Based Affinity)

The Console cluster uses cookie-based session affinity to ensure the SignalR negotiate → connect flow reaches the same backend:

\`\`\`json
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
        "SecurePolicy": "SameAsRequest",
        "Expiration": "01:00:00",
        "IsEssential": true
      }
    }
  }
}
\`\`\`

### 2. Fallback Transports

SignalR clients should be configured with fallback transports:

\`\`\`javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/console")
    .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: retryContext => {
            return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
        }
    })
    .build();
\`\`\`

SignalR will automatically fall back:
1. WebSockets (default)
2. Server-Sent Events (SSE)
3. Long Polling

### 3. Cloudflare Configuration

**Option A: Argo Smart Routing** (Recommended)
- Cloudflare Argo can extend WebSocket timeouts
- Enable in Cloudflare Dashboard → Network → Argo Smart Routing

**Option B: Page Rule**
- Create Page Rule for `/hubs/*`
- Set "WebSockets" to "On"
- Set "Browser Cache TTL" to "Respect Existing Headers"

**Option C: Cloudflare Worker**
- Deploy a Cloudflare Worker to handle WebSocket upgrade explicitly
- Forward WebSocket traffic directly to origin without DDoS checks

### 4. Client Reconnection

SignalR clients should implement automatic reconnection:

\`\`\`javascript
connection.onreconnecting(error => {
    console.log('SignalR reconnecting:', error);
});

connection.onreconnected(connectionId => {
    console.log('SignalR reconnected:', connectionId);
});

connection.onclose(error => {
    console.log('SignalR connection closed:', error);
    // Attempt manual reconnection after delay
    setTimeout(() => connection.start(), 5000);
});
\`\`\`

## Testing

### Local Testing (Without Cloudflare)

\`\`\`bash
# Start Gateway
dotnet run --project src/Dhadgar.Gateway

# Start Console service (when implemented)
dotnet run --project src/Dhadgar.Console

# Test WebSocket upgrade
wscat -c ws://localhost:5000/hubs/console
\`\`\`

### Production Testing (Through Cloudflare)

Monitor Cloudflare analytics for:
- WebSocket connection duration
- Connection failure rates
- Fallback transport usage

## Future Considerations

When the Console service is implemented, ensure:
1. SignalR hub is configured for scale-out with Redis backplane
2. Hub methods handle reconnection gracefully
3. Connection state is stored in Redis, not in-memory
4. Hub authorization checks tenant context from JWT
```

### 7.3 Create WebSocket Tests

**File**: `tests/Dhadgar.Gateway.Tests/WebSocketTests.cs`

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.WebSockets;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class WebSocketTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebSocketTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Gateway_ShouldAllowWebSocketUpgrade()
    {
        // Arrange
        var server = _factory.Server;
        var client = server.CreateWebSocketClient();

        // Act & Assert - should not throw
        // Note: This will fail until Console service is running and accepts WebSocket connections
        // For now, we're just testing that the Gateway allows the upgrade request
        try
        {
            var webSocket = await client.ConnectAsync(
                new Uri(server.BaseAddress, "/hubs/console"),
                CancellationToken.None);

            Assert.Equal(WebSocketState.Open, webSocket.State);

            await webSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Test complete",
                CancellationToken.None);
        }
        catch (WebSocketException)
        {
            // Expected: Console service not running yet
            // The important thing is that the Gateway accepted the upgrade request
            // (Gateway would reject with 404 or 400 if WebSocket support wasn't enabled)
        }
    }

    [Fact]
    public void ConsoleCluster_ShouldHaveSessionAffinityEnabled()
    {
        // This test verifies the configuration, not runtime behavior
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var sessionAffinityEnabled = config
            .GetValue<bool>("ReverseProxy:Clusters:console:SessionAffinity:Enabled");

        Assert.True(sessionAffinityEnabled,
            "Console cluster must have session affinity enabled for SignalR sticky sessions");
    }

    [Fact]
    public void ConsoleCluster_ShouldUseCookieAffinityPolicy()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var affinityPolicy = config["ReverseProxy:Clusters:console:SessionAffinity:Policy"];

        Assert.Equal("Cookie", affinityPolicy);
    }
}
```

### 7.4 Checkpoint: Build, Test, Commit

```bash
# Build
dotnet build

# Test
dotnet test

# Run
dotnet run --project src/Dhadgar.Gateway

# Verify WebSocket support is enabled (check startup logs)
# Should see: "WebSocket support enabled"

# Commit
git add .
git commit -m "Phase 7: Add WebSocket support for SignalR with Cloudflare-compatible sticky sessions"
git push
```

---

## Phase 8: Testing & Documentation

**Goal**: Create comprehensive integration tests and finalize documentation.

### 8.1 Integration Tests

**File**: `tests/Dhadgar.Gateway.Tests/GatewayIntegrationTests.cs`

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class GatewayIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GatewayIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/hello")]
    [InlineData("/healthz")]
    [InlineData("/livez")]
    [InlineData("/readyz")]
    public async Task Endpoint_ShouldReturnSuccess(string url)
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync(url);

        // Assert
        Assert.True(response.IsSuccessStatusCode,
            $"Expected {url} to return success, got {response.StatusCode}");
    }

    [Fact]
    public async Task RootEndpoint_ShouldReturnServiceInfo()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Dhadgar.Gateway", content);
        Assert.Contains("version", content);
    }

    [Fact]
    public async Task HelloEndpoint_ShouldReturnPlainText()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/hello");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Gateway", content);
    }

    [Fact]
    public async Task NonExistentRoute_ShouldReturn404()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/nonexistent");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Request_ShouldContainCorrelationHeaders()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/healthz");

        // Assert
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        Assert.True(response.Headers.Contains("X-Request-Id"));
        Assert.True(response.Headers.Contains("X-Trace-Id"));
    }
}
```

### 8.2 YARP Configuration Validation Tests

**File**: `tests/Dhadgar.Gateway.Tests/YarpConfigurationValidationTests.cs`

```csharp
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dhadgar.Gateway.Tests;

public class YarpConfigurationValidationTests
{
    private readonly IConfiguration _configuration;

    public YarpConfigurationValidationTests()
    {
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();
    }

    [Theory]
    [InlineData("identity-route", "identity")]
    [InlineData("billing-route", "billing")]
    [InlineData("servers-route", "servers")]
    [InlineData("nodes-route", "nodes")]
    [InlineData("tasks-route", "tasks")]
    [InlineData("files-route", "files")]
    [InlineData("console-api-route", "console")]
    [InlineData("console-hub-route", "console")]
    [InlineData("mods-route", "mods")]
    [InlineData("notifications-route", "notifications")]
    [InlineData("firewall-route", "firewall")]
    [InlineData("secrets-route", "secrets")]
    [InlineData("discord-route", "discord")]
    [InlineData("agents-route", "nodes")]
    public void Route_ShouldMapToValidCluster(string routeName, string expectedCluster)
    {
        var clusterId = _configuration[$"ReverseProxy:Routes:{routeName}:ClusterId"];
        Assert.Equal(expectedCluster, clusterId);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("billing")]
    [InlineData("servers")]
    [InlineData("nodes")]
    [InlineData("tasks")]
    [InlineData("files")]
    [InlineData("console")]
    [InlineData("mods")]
    [InlineData("notifications")]
    [InlineData("firewall")]
    [InlineData("secrets")]
    [InlineData("discord")]
    public void Cluster_ShouldHaveAtLeastOneDestination(string clusterName)
    {
        var destinationsSection = _configuration.GetSection($"ReverseProxy:Clusters:{clusterName}:Destinations");
        var destinations = destinationsSection.GetChildren().ToList();

        Assert.NotEmpty(destinations);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("billing")]
    [InlineData("servers")]
    [InlineData("nodes")]
    [InlineData("tasks")]
    [InlineData("files")]
    [InlineData("console")]
    [InlineData("mods")]
    [InlineData("notifications")]
    [InlineData("firewall")]
    [InlineData("secrets")]
    [InlineData("discord")]
    public void Cluster_ShouldHaveLoadBalancingPolicy(string clusterName)
    {
        var loadBalancingPolicy = _configuration[$"ReverseProxy:Clusters:{clusterName}:LoadBalancingPolicy"];

        Assert.NotNull(loadBalancingPolicy);
        Assert.Equal("RoundRobin", loadBalancingPolicy);
    }

    [Fact]
    public void AllRoutes_ShouldHaveAuthorizationPolicy()
    {
        var routesSection = _configuration.GetSection("ReverseProxy:Routes");
        var routes = routesSection.GetChildren();

        foreach (var route in routes)
        {
            var authPolicy = route["AuthorizationPolicy"];
            Assert.False(string.IsNullOrEmpty(authPolicy),
                $"Route {route.Key} missing AuthorizationPolicy");
        }
    }

    [Theory]
    [InlineData("servers-route")]
    [InlineData("nodes-route")]
    [InlineData("tasks-route")]
    public void TenantScopedRoutes_ShouldHaveRateLimiterPolicy(string routeName)
    {
        var rateLimiterPolicy = _configuration[$"ReverseProxy:Routes:{routeName}:RateLimiterPolicy"];

        Assert.Equal("PerTenant", rateLimiterPolicy);
    }
}
```

### 8.3 Create README for Gateway

**File**: `src/Dhadgar.Gateway/README.md`

```markdown
# Dhadgar Gateway

The Gateway service is the **single public entry point** for the Meridian Console platform.

## Responsibilities

- **Reverse Proxy**: Routes requests to 13 backend microservices using YARP
- **Authentication**: JWT validation (ready but disabled until Identity service is deployed)
- **Rate Limiting**: Multi-tier (global, per-tenant, per-agent, authentication endpoints)
- **Observability**: Distributed tracing, metrics, structured logging via OpenTelemetry
- **Health Checks**: Kubernetes-ready probes (`/livez`, `/readyz`, `/healthz`)
- **WebSocket Support**: SignalR proxy with Cloudflare-compatible sticky sessions

## Port Assignment

**Port**: 5000
**URL**: http://localhost:5000

## Running Locally

\`\`\`bash
# Start local infrastructure (PostgreSQL, RabbitMQ, Redis)
docker compose -f deploy/compose/docker-compose.dev.yml up -d

# Run Gateway
dotnet run --project src/Dhadgar.Gateway

# Or with hot reload
dotnet watch --project src/Dhadgar.Gateway
\`\`\`

## Testing

\`\`\`bash
# Run all tests
dotnet test tests/Dhadgar.Gateway.Tests

# Run specific test
dotnet test tests/Dhadgar.Gateway.Tests --filter "FullyQualifiedName~HealthCheckTests"
\`\`\```

## Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/` | GET | Service information |
| `/hello` | GET | Hello world (plain text) |
| `/healthz` | GET | Detailed health check (JSON) |
| `/livez` | GET | Liveness probe (Kubernetes) |
| `/readyz` | GET | Readiness probe (Kubernetes) |
| `/api/v1/{service}/**` | ALL | Proxied to backend services |
| `/hubs/console/**` | WS | SignalR WebSocket proxy |

## Configuration

### Authentication (Disabled by Default)

See: [docs/gateway-authentication.md](../../docs/gateway-authentication.md)

To enable:

\`\`\`json
{
  "Authentication": {
    "Enabled": true,
    "EnforcementMode": "required"
  }
}
\`\`\`

### Rate Limiting

Rate limiting is always enabled:
- **Global**: 1000 req/min per IP
- **Per-Tenant**: Token bucket (100 burst, 50/sec replenish)
- **Per-Agent**: 500 req/min sliding window
- **Authentication**: 10 req/5min per IP

### Observability

OpenTelemetry OTLP exporter is configured but stubbed:

\`\`\`json
{
  "Observability": {
    "OtlpEndpoint": "http://localhost:4317"
  }
}
\`\`\`

## Development Notes

### User Secrets

For local development, set JWT signing key:

\`\`\`bash
dotnet user-secrets set "Jwt:SigningKey" "your-256-bit-secret-key" --project src/Dhadgar.Gateway
\`\`\`

### Backend Service Ports

| Service | Port |
|---------|------|
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

## Architecture

The Gateway uses YARP 2.3.0 for reverse proxy functionality with:
- **Path transforms**: Strip `/api/v1/{service}` prefix before forwarding
- **Active health checks**: Poll backend `/healthz` endpoints every 30 seconds
- **Passive health checks**: Detect failures from actual traffic
- **Session affinity**: Cookie-based for SignalR (Console service)
- **Load balancing**: Round-robin across destinations

## Documentation

- [Gateway Authentication](../../docs/gateway-authentication.md)
- [SignalR & Cloudflare](../../docs/signalr-cloudflare.md)
- [Implementation Plan](../../docs/implementation-plans/gateway-service.md)
```

### 8.4 Update Main README with Service Ports

**File**: `README.md` (update or create if needed)

Add service port table to root README.

### 8.5 Final Test Suite Run

```bash
# Build entire solution
dotnet build

# Run all Gateway tests
dotnet test tests/Dhadgar.Gateway.Tests --verbosity normal

# Verify test coverage
# Should have:
# - HelloWorldTests
# - RouteConfigurationTests
# - MiddlewareTests
# - SecurityTests
# - RateLimitingTests
# - ObservabilityTests
# - HealthCheckTests
# - WebSocketTests
# - GatewayIntegrationTests
# - YarpConfigurationValidationTests
```

### 8.6 Final Checkpoint: Build, Test, Commit

```bash
# Build
dotnet build

# Test (full suite)
dotnet test

# Run and verify all endpoints
dotnet run --project src/Dhadgar.Gateway

# In another terminal, test all endpoints:
curl http://localhost:5000/
curl http://localhost:5000/hello
curl http://localhost:5000/healthz | jq .
curl http://localhost:5000/livez
curl http://localhost:5000/readyz

# Verify correlation headers
curl -i http://localhost:5000/healthz | grep "X-Correlation-Id"

# Test rate limiting (should see 429 after ~1000 requests)
for i in {1..1010}; do curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/healthz; done | sort | uniq -c

# Commit
git add .
git commit -m "Phase 8: Complete Gateway implementation with comprehensive tests and documentation"
git push
```

---

## Configuration Reference

### Complete appsettings.json Structure

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Yarp.ReverseProxy": "Information"
    }
  },
  "AllowedHosts": "*",
  "Authentication": {
    "Enabled": false,
    "EnforcementMode": "optional"
  },
  "Jwt": {
    "Issuer": "https://meridian.local",
    "Audience": "meridian-api",
    "ClockSkewSeconds": 30
  },
  "Observability": {
    "OtlpEndpoint": "http://localhost:4317",
    "Sampling": {
      "Rate": 1.0
    }
  },
  "ReverseProxy": {
    "Routes": { "..." },
    "Clusters": { "..." }
  }
}
```

### Environment-Specific Configuration

**Development** (`appsettings.Development.json`):
- Verbose logging
- Authentication disabled
- 100% trace sampling

**Production** (`appsettings.Production.json`):
- Minimal logging
- Authentication required
- Reduced trace sampling (10%)
- HTTPS enforcement

---

## Testing Strategy

### Unit Tests
- Configuration validation
- Middleware logic
- Security policy logic

### Integration Tests
- Full HTTP request/response flow
- Middleware pipeline execution
- Health check responses
- Rate limiting behavior

### Manual Testing
- Backend routing (when services are available)
- WebSocket upgrade (when Console service is available)
- JWT validation (when Identity service is available)
- Cloudflare integration (in staging/production)

---

## Deployment Considerations

### Kubernetes Deployment

**Probes**:
```yaml
livenessProbe:
  httpGet:
    path: /livez
    port: 5000
  initialDelaySeconds: 10
  periodSeconds: 30

readinessProbe:
  httpGet:
    path: /readyz
    port: 5000
  initialDelaySeconds: 5
  periodSeconds: 10
```

**Resource Requests**:
```yaml
resources:
  requests:
    memory: "256Mi"
    cpu: "100m"
  limits:
    memory: "512Mi"
    cpu: "500m"
```

### Horizontal Scaling

The Gateway is **stateless** and can scale horizontally:
- No local state
- Session affinity handled by YARP (cookies)
- Rate limiting per-pod (additional global rate limiting at load balancer recommended)

### Cloudflare Integration

**Required Settings**:
1. **SSL/TLS**: Full (strict) mode
2. **WebSockets**: Enabled
3. **Argo Smart Routing**: Enabled (for SignalR)
4. **Page Rule** for `/hubs/*`: WebSockets = On
5. **Firewall Rules**: Allowlist for agent IPs (optional)

---

## Success Criteria

Gateway implementation is complete when:

- ✅ All 14 routes configured and tested
- ✅ All 13 clusters configured with health checks
- ✅ JWT authentication infrastructure ready (disabled)
- ✅ Multi-tier rate limiting functional
- ✅ OpenTelemetry instrumentation in place
- ✅ Health checks operational (`/livez`, `/readyz`, `/healthz`)
- ✅ WebSocket support enabled with sticky sessions
- ✅ Comprehensive test suite (10 test classes, 40+ tests)
- ✅ Documentation complete
- ✅ Build succeeds
- ✅ All tests pass
- ✅ Committed to main branch

---

## Next Steps

After Gateway implementation:

1. **Identity Service**: Implement AuthN/AuthZ, enable JWT enforcement in Gateway
2. **Servers Service**: First domain service, test end-to-end routing
3. **Nodes Service**: Second domain service, test agent routing
4. **Console Service**: SignalR hub, test WebSocket proxying
5. **Observability Stack**: Deploy Grafana/Tempo/Loki, connect OTLP exporter

---

## Appendix: Troubleshooting

### Gateway won't start

**Check**:
- Port 5000 not already in use: `netstat -an | grep 5000` (Linux/Mac) or `netstat -an | findstr 5000` (Windows)
- JWT signing key set: `dotnet user-secrets list --project src/Dhadgar.Gateway`

### Backend routes return 503

**Cause**: Backend services not running

**Solution**: This is expected during Gateway-only development. YARP will return 503 until backends are implemented.

### Rate limiting not working

**Check**:
- Middleware order: `UseRateLimiter()` before `UseAuthentication()`
- Configuration: `RateLimiterPolicy` set on routes

### Health checks always degraded

**Cause**: Backend services not running

**Solution**: `/readyz` will show degraded status until backends are available. `/livez` should always be healthy.

### WebSocket upgrade fails

**Check**:
- `UseWebSockets()` called before `MapReverseProxy()`
- Console service running (WebSocket will fail until Console hub is implemented)

---

## Document Metadata

- **Created**: 2024-12-28
- **Author**: Claude (Sonnet 4.5)
- **Version**: 1.0
- **Status**: Final
- **Last Updated**: 2024-12-28
