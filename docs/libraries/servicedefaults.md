# Dhadgar.ServiceDefaults Library

The foundational shared library for all Meridian Console microservices, providing standardized middleware, health checks, resilience patterns, security utilities, caching abstractions, multi-tenancy support, and Swagger/OpenAPI configuration.

**Location**: `src/Shared/Dhadgar.ServiceDefaults/`

---

## Table of Contents

1. [Purpose](#purpose)
2. [Installation](#installation)
3. [Quick Start](#quick-start)
4. [Core Extension Methods](#core-extension-methods)
   - [AddDhadgarServiceDefaults](#adddhadgarservicedefaults)
   - [MapDhadgarDefaultEndpoints](#mapdhadgardefaultendpoints)
5. [Middleware Components](#middleware-components)
   - [CorrelationMiddleware](#correlationmiddleware)
   - [ProblemDetailsMiddleware](#problemdetailsmiddleware)
   - [RequestLoggingMiddleware](#requestloggingmiddleware)
   - [RequestLimitsMiddleware](#requestlimitsmiddleware)
6. [Health Check Setup](#health-check-setup)
7. [OpenTelemetry Configuration](#opentelemetry-configuration)
8. [Resilience: Circuit Breaker](#resilience-circuit-breaker)
9. [Service-to-Service Authentication](#service-to-service-authentication)
10. [Swagger/OpenAPI Configuration](#swaggeropenapi-configuration)
11. [Security Infrastructure](#security-infrastructure)
12. [Multi-Tenancy Support](#multi-tenancy-support)
13. [Permission Caching](#permission-caching)
14. [Middleware Pipeline Order](#middleware-pipeline-order)
15. [Configuration Reference](#configuration-reference)
16. [Extension Points and Customization](#extension-points-and-customization)
17. [Usage Examples](#usage-examples)
18. [Testing](#testing)
19. [File Structure](#file-structure)

---

## Purpose

`Dhadgar.ServiceDefaults` serves as the common infrastructure layer for all Meridian Console microservices. It encapsulates cross-cutting concerns that every service needs:

- **Consistent observability** through correlation tracking and structured logging
- **Standardized error responses** using RFC 7807 Problem Details
- **Resilience patterns** with configurable circuit breakers
- **Security infrastructure** including event logging and request limits
- **Multi-tenancy support** with organization context resolution
- **Health check endpoints** for Kubernetes liveness and readiness probes
- **API documentation** with Swagger/OpenAPI integration

### Design Philosophy

1. **Convention over Configuration**: Sensible defaults that work out of the box
2. **Extensibility**: Services can customize behavior when needed
3. **Separation of Concerns**: Each component handles one responsibility
4. **Testability**: All components are designed for unit and integration testing
5. **Performance**: Source-generated logging, minimal allocations, async-first APIs

### What This Library Does NOT Do

- **No business logic**: Purely infrastructure code
- **No database access**: Services own their data; this library provides abstractions
- **No inter-service communication**: Use `Dhadgar.Messaging` for MassTransit/RabbitMQ patterns
- **No contract definitions**: Use `Dhadgar.Contracts` for DTOs and message contracts

---

## Installation

All Meridian Console services reference this library via a project reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\Shared\Dhadgar.ServiceDefaults\Dhadgar.ServiceDefaults.csproj" />
</ItemGroup>
```

### Dependencies

The library has minimal dependencies:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
  <PackageReference Include="Microsoft.OpenApi" />
  <PackageReference Include="Swashbuckle.AspNetCore" />
</ItemGroup>
```

All package versions are managed centrally in `Directory.Packages.props` at the solution root.

---

## Quick Start

The simplest way to use this library in your service's `Program.cs`:

```csharp
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Swagger;

var builder = WebApplication.CreateBuilder(args);

// 1. Add service defaults (health checks)
builder.Services.AddDhadgarServiceDefaults();

// 2. Add Swagger (optional, but recommended)
builder.Services.AddMeridianSwagger(
    title: "My Service API",
    description: "Service description here");

var app = builder.Build();

// 3. Enable Swagger UI (Development/Testing only)
app.UseMeridianSwagger();

// 4. Configure middleware pipeline (ORDER MATTERS!)
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// 5. Map your endpoints
app.MapGet("/", () => Results.Ok(new { service = "My.Service" }));

// 6. Map default health check endpoints
app.MapDhadgarDefaultEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> integration tests
public partial class Program { }
```

---

## Core Extension Methods

### AddDhadgarServiceDefaults

**Namespace**: `Dhadgar.ServiceDefaults`

**Signature**:
```csharp
public static IServiceCollection AddDhadgarServiceDefaults(this IServiceCollection services)
```

**What it registers**:
- ASP.NET Core health checks infrastructure
- A basic "self" health check that always returns `Healthy`
- Tags the self-check with `["live"]` for Kubernetes liveness probes

**Implementation**:
```csharp
public static IServiceCollection AddDhadgarServiceDefaults(this IServiceCollection services)
{
    services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

    return services;
}
```

**Usage**:
```csharp
builder.Services.AddDhadgarServiceDefaults();

// Add additional health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"])
    .AddRedis(redisConnectionString, name: "redis", tags: ["ready"]);
```

---

### MapDhadgarDefaultEndpoints

**Namespace**: `Dhadgar.ServiceDefaults`

**Signature**:
```csharp
public static WebApplication MapDhadgarDefaultEndpoints(this WebApplication app)
```

**What it creates**:

| Endpoint | Purpose | Predicate |
|----------|---------|-----------|
| `/healthz` | Comprehensive health status | All registered checks |
| `/livez` | Kubernetes liveness probe | Only checks tagged `live` |
| `/readyz` | Kubernetes readiness probe | Only checks tagged `ready` |

**Response Format**:

All endpoints return JSON with a consistent structure:

```json
{
  "service": "Dhadgar.Servers",
  "status": "ok",
  "timestamp": "2024-01-15T10:30:00Z",
  "checks": {
    "self": {
      "status": "Healthy",
      "duration_ms": 0.5
    },
    "postgres": {
      "status": "Healthy",
      "duration_ms": 15.2,
      "data": {
        "server_version": "16.1"
      }
    }
  }
}
```

**Why Three Endpoints?**

Kubernetes distinguishes between liveness and readiness:

- **Liveness (`/livez`)**: "Is the process alive?" If this fails, Kubernetes restarts the pod
- **Readiness (`/readyz`)**: "Can the process accept traffic?" If this fails, the pod is removed from load balancer rotation
- **Health (`/healthz`)**: Comprehensive check for diagnostics and monitoring

Tag your health checks appropriately:
- Database connections: `["ready"]` - service cannot do useful work without them
- Self-check: `["live"]` - always passes if the process is running
- External services: `["ready"]` - optional dependencies

---

## Middleware Components

### CorrelationMiddleware

**Namespace**: `Dhadgar.ServiceDefaults.Middleware`

**Purpose**: Ensures every request has unique correlation, request, and trace IDs for distributed tracing.

**What it does**:

1. **Extracts or generates correlation ID**: Looks for `X-Correlation-Id` header; if missing, uses the OpenTelemetry trace ID or generates a new GUID
2. **Generates request ID**: Each request gets a unique ID via `X-Request-Id` header
3. **Integrates with OpenTelemetry**: Sets tags and baggage on the current `Activity`
4. **Propagates context**: Sets response headers and ensures downstream middleware sees the IDs
5. **Validates input**: Sanitizes header values to prevent injection attacks (alphanumeric and dashes only, max 64 chars)

**Headers Managed**:

| Header | Direction | Purpose |
|--------|-----------|---------|
| `X-Correlation-Id` | Request/Response | Links all requests in a user flow |
| `X-Request-Id` | Request/Response | Unique per-request identifier |
| `X-Trace-Id` | Response | OpenTelemetry trace ID |
| `traceparent` | Request/Response | W3C Trace Context standard |
| `tracestate` | Request/Response | W3C Trace Context vendor-specific |
| `baggage` | Request/Response | OpenTelemetry baggage propagation |

**OpenTelemetry Integration**:

```csharp
var activity = Activity.Current;
if (activity is not null)
{
    activity.SetTag("correlation.id", correlationId);
    activity.SetTag("request.id", requestId);
    activity.SetBaggage("correlation.id", correlationId);
}
```

**Context Storage**:

IDs are stored in `HttpContext.Items` for access by other middleware:

```csharp
context.Items["CorrelationId"] = correlationId;
context.Items["RequestId"] = requestId;
```

**Usage**:
```csharp
app.UseMiddleware<CorrelationMiddleware>();
```

**Security**: The middleware validates incoming header values using a regex pattern (`^[A-Za-z0-9-]+$`). Invalid or overly long values (>64 chars) are rejected and new IDs are generated instead.

---

### ProblemDetailsMiddleware

**Namespace**: `Dhadgar.ServiceDefaults.Middleware`

**Purpose**: Transforms unhandled exceptions into RFC 7807 Problem Details responses.

**What it does**:

1. **Catches unhandled exceptions**: Wraps the entire downstream pipeline in a try-catch
2. **Logs the error**: Uses structured logging with correlation context
3. **Returns Problem Details**: Formats the response according to RFC 7807
4. **Environment-aware**: Includes stack traces only in Development/Testing environments

**Response Format** (Production):

```json
{
  "type": "https://meridian.console/errors/internal-server-error",
  "title": "Internal Server Error",
  "status": 500,
  "detail": "An unexpected error occurred. Please contact support with the trace ID.",
  "instance": "/api/servers/123",
  "traceId": "abc123def456",
  "extensions": null
}
```

**Response Format** (Development/Testing):

```json
{
  "type": "https://meridian.console/errors/internal-server-error",
  "title": "Internal Server Error",
  "status": 500,
  "detail": "Object reference not set to an instance of an object.",
  "instance": "/api/servers/123",
  "traceId": "abc123def456",
  "extensions": {
    "stackTrace": "   at MyService.GetServer(Int32 id) in ..."
  }
}
```

**Content Type**: `application/problem+json`

**Usage**:
```csharp
app.UseMiddleware<ProblemDetailsMiddleware>();
```

**Important**: This middleware should run early in the pipeline (after CorrelationMiddleware) to catch exceptions from all downstream middleware.

---

### RequestLoggingMiddleware

**Namespace**: `Dhadgar.ServiceDefaults.Middleware`

**Purpose**: Logs HTTP requests and responses with correlation context and timing information.

**What it does**:

1. **Creates logging scope**: Adds correlation context to all log messages within the request
2. **Times the request**: Uses `Stopwatch` for accurate timing
3. **Logs with appropriate level**: Information for success, Warning for 4xx, Error for 5xx
4. **Handles exceptions**: Logs errors before rethrowing

**Logging Scope**:

Every log message within the request automatically includes:

```json
{
  "CorrelationId": "abc123",
  "RequestId": "def456",
  "RequestMethod": "POST",
  "RequestPath": "/api/servers"
}
```

**Log Message Format**:

```
HTTP POST /api/servers responded 201 in 45ms
```

**Log Levels**:

| Status Code Range | Log Level |
|-------------------|-----------|
| 1xx-3xx | Information |
| 4xx | Warning |
| 5xx | Error |

**Usage**:
```csharp
app.UseMiddleware<RequestLoggingMiddleware>();
```

**Important**: This middleware depends on `CorrelationMiddleware` having already run to populate `HttpContext.Items` with correlation data.

---

### RequestLimitsMiddleware

**Namespace**: `Dhadgar.ServiceDefaults.Security`

**Purpose**: Handles request body size limit exceptions with proper JSON error responses.

**Response Format**:

```json
{
  "error": "request_too_large",
  "message": "Request body exceeds maximum allowed size"
}
```

**Related Configuration** (`RequestLimitsOptions`):

```csharp
builder.ConfigureRequestLimits(options =>
{
    options.MaxRequestBodySize = 1_048_576;      // 1 MB default
    options.MaxRequestHeadersTotalSize = 32_768; // 32 KB
    options.MaxRequestLineSize = 8_192;          // 8 KB
    options.MaxFileUploadSize = 52_428_800;      // 50 MB
});
```

**Per-Endpoint Overrides**:

```csharp
// Disable limit entirely
app.MapPost("/files/upload", HandleUpload)
    .DisableRequestSizeLimit();

// Custom limit
app.MapPost("/files/upload", HandleUpload)
    .WithRequestSizeLimit(100 * 1024 * 1024); // 100 MB
```

**Usage**:
```csharp
app.UseRequestLimitsMiddleware();
```

---

## Health Check Setup

### Standard Endpoints

| Endpoint | Purpose | Filter |
|----------|---------|--------|
| `/healthz` | Full health status | All checks |
| `/livez` | Kubernetes liveness | Checks tagged `live` |
| `/readyz` | Kubernetes readiness | Checks tagged `ready` |

### Adding Custom Health Checks

```csharp
builder.Services.AddDhadgarServiceDefaults();

// Add database check
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"])
    .AddRedis(redisConnection, name: "redis", tags: ["ready"]);

// Add custom check
builder.Services.AddHealthChecks()
    .AddCheck<ExternalServiceHealthCheck>("external-api", tags: ["ready"]);
```

### Response Schema

```json
{
  "service": "Dhadgar.Identity",
  "status": "ok",
  "timestamp": "2024-01-15T10:30:00Z",
  "checks": {
    "self": {
      "status": "Healthy",
      "duration_ms": 0.1
    },
    "postgres": {
      "status": "Healthy",
      "duration_ms": 5.2,
      "description": "Connected successfully"
    },
    "redis": {
      "status": "Degraded",
      "duration_ms": 102.5,
      "description": "High latency detected"
    }
  }
}
```

---

## OpenTelemetry Configuration

The library integrates with OpenTelemetry through the `CorrelationMiddleware`:

### Activity Integration

```csharp
var activity = Activity.Current;
if (activity is not null)
{
    activity.SetTag("correlation.id", correlationId);
    activity.SetTag("request.id", requestId);
    activity.SetBaggage("correlation.id", correlationId);
}
```

### W3C Trace Context Headers

The middleware propagates W3C Trace Context headers automatically:
- `traceparent` - Trace ID and span ID
- `tracestate` - Vendor-specific trace state
- `baggage` - Key-value pairs for context propagation

### Circuit Breaker Metrics

The circuit breaker middleware exposes OpenTelemetry metrics:

| Metric | Type | Description |
|--------|------|-------------|
| `circuit_breaker.opened` | Counter | Times circuits have opened |
| `circuit_breaker.closed` | Counter | Times circuits have closed |
| `circuit_breaker.requests_blocked` | Counter | Requests blocked by open circuits |
| `circuit_breaker.failures_recorded` | Counter | Failures recorded |

**Meter Name**: `Dhadgar.ServiceDefaults.CircuitBreaker`

Configure metric collection:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Dhadgar.ServiceDefaults.CircuitBreaker");
    });
```

---

## Resilience: Circuit Breaker

The circuit breaker pattern prevents cascading failures by temporarily blocking requests to unhealthy backend services.

### How It Works

1. **Closed State** (normal): Requests pass through. Failures are counted.
2. **Open State** (tripped): After N failures, requests are immediately rejected with 503.
3. **Half-Open State** (testing): After a timeout, limited requests are allowed to test recovery.

### Registration

```csharp
// Register services
builder.Services.AddCircuitBreaker(builder.Configuration);

// Use middleware
app.UseCircuitBreaker();
```

### CircuitBreakerOptions

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `FailureThreshold` | int | 5 | Consecutive failures before circuit opens |
| `SuccessThreshold` | int | 2 | Successful requests to close circuit from half-open |
| `OpenDurationSeconds` | int | 30 | How long circuit stays open |
| `FailureStatusCodes` | int[] | [500,502,503,504] | Status codes counted as failures |
| `IncludeServiceNameInErrors` | bool | false | Include service name in 503 responses |

**Configuration**:

```json
{
  "CircuitBreaker": {
    "FailureThreshold": 10,
    "SuccessThreshold": 3,
    "OpenDurationSeconds": 60,
    "FailureStatusCodes": [500, 502, 503, 504, 408],
    "IncludeServiceNameInErrors": true
  }
}
```

### Service Identification

The middleware reads the service ID from `HttpContext.Items`:

```csharp
var serviceId = context.Items["CircuitBreaker:ServiceId"] as string;
```

If no service ID is set, requests pass through without circuit breaking. Typically set by YARP reverse proxy configuration or custom middleware.

### 503 Response Format

```json
{
  "type": "https://httpstatuses.com/503",
  "title": "Service Temporarily Unavailable",
  "status": 503,
  "detail": "The identity service is temporarily unavailable due to circuit breaker activation. Please retry after the specified time.",
  "instance": "/api/users/me"
}
```

The response includes a `Retry-After` header.

### Custom State Store

For distributed deployments, implement a custom state store:

```csharp
public interface ICircuitBreakerStateStore
{
    CircuitState GetOrCreateState(string serviceId);
    void RemoveState(string serviceId);
    IEnumerable<(string ServiceId, CircuitState State)> GetAllStates();
}

// Register with custom implementation
builder.Services.AddCircuitBreaker<RedisCircuitBreakerStateStore>(builder.Configuration);
```

---

## Service-to-Service Authentication

### ServiceAuthenticationHandler

A `DelegatingHandler` that automatically attaches bearer tokens to outgoing HTTP requests.

### ServiceTokenProvider

Obtains and caches OAuth2 client credentials tokens with automatic refresh.

**Token Flow**:

1. Checks if a cached token exists and is valid (with 30-second skew)
2. If expired/missing, requests a new token from the configured endpoint
3. Caches the token based on `expires_in` from the response

### Configuration

```json
{
  "ServiceAuth": {
    "TokenEndpoint": "https://identity.local/connect/token",
    "ClientId": "servers-service",
    "ClientSecret": "use-user-secrets-for-this",
    "Scope": "identity:read servers:write",
    "Audience": "meridian-api"
  }
}
```

### Usage

```csharp
// Register authentication services
builder.Services.AddServiceAuthentication(builder.Configuration);

// Configure an HttpClient to use the handler
builder.Services.AddHttpClient("IdentityService")
    .AddServiceAuthentication()
    .ConfigureHttpClient(client => client.BaseAddress = new Uri("https://identity.local"));
```

---

## Swagger/OpenAPI Configuration

### AddMeridianSwagger

```csharp
public static IServiceCollection AddMeridianSwagger(
    this IServiceCollection services,
    string title,
    string description,
    Action<SwaggerGenOptions>? configureOptions = null)
```

**Usage**:

```csharp
builder.Services.AddMeridianSwagger(
    title: "Dhadgar Servers API",
    description: "Game server lifecycle management for Meridian Console",
    configureOptions: options =>
    {
        // Add JWT authentication to Swagger UI
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });
    });
```

### UseMeridianSwagger

Enables Swagger UI in Development and Testing environments only.

```csharp
app.UseMeridianSwagger();
```

**Swagger is only enabled in Development and Testing environments for security reasons.**

---

## Security Infrastructure

### ISecurityEventLogger

Provides structured logging for security-relevant events with consistent formatting for SIEM integration.

**Events Logged**:

| Method | Event ID | Level | Description |
|--------|----------|-------|-------------|
| `LogAuthenticationSuccess` | 5001 | Info | Successful login |
| `LogAuthenticationFailure` | 5002 | Warning | Failed login attempt |
| `LogPrivilegeEscalationAttempt` | 5003 | Warning | Blocked escalation attempt |
| `LogRoleAssignment` | 5004 | Info | Role granted |
| `LogRoleRevocation` | 5005 | Info | Role removed |
| `LogCustomRoleCreated` | 5006 | Info | Custom role definition |
| `LogOAuthAccountLinked` | 5007 | Info | OAuth provider linked |
| `LogOAuthAccountUnlinked` | 5008 | Info | OAuth provider unlinked |
| `LogTokenRefresh` | 5009 | Debug | Token refreshed |
| `LogTokenRevocation` | 5010 | Info | Token revoked |
| `LogOrgMembershipChange` | 5011 | Info | Organization membership change |
| `LogEmailVerificationChange` | 5012 | Info | Email verification status |
| `LogSuspiciousActivity` | 5013 | Warning | Suspicious activity detected |
| `LogAuthorizationDenied` | 5014 | Warning | Authorization denied |
| `LogResourceAccess` | 5015 | Debug | Resource access audit |
| `LogApiKeyUsage` | 5016 | Info | API key usage |
| `LogRateLimitExceeded` | 5017 | Warning | Rate limit exceeded |

**Usage**:

```csharp
// Register
builder.Services.AddSecurityEventLogger();

// Inject and use
public class AuthController(ISecurityEventLogger securityLogger)
{
    public IActionResult Login(LoginRequest request)
    {
        if (success)
        {
            securityLogger.LogAuthenticationSuccess(
                userId,
                email,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers.UserAgent);
        }
        else
        {
            securityLogger.LogAuthenticationFailure(
                email,
                "Invalid password",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers.UserAgent);
        }
    }
}
```

**Implementation Note**: Uses source-generated logging (`[LoggerMessage]`) for optimal performance with zero allocations for disabled log levels.

---

## Multi-Tenancy Support

### IOrganizationContext

Provides the current organization context from the request for tenant isolation.

**Interface**:

```csharp
public interface IOrganizationContext
{
    Guid? OrganizationId { get; }
    bool HasOrganization { get; }
    Guid RequiredOrganizationId { get; } // Throws if not available
}
```

**Resolution Order**:

1. JWT claim `org_id` (preferred)
2. JWT claim `organization_id` (alternative)
3. `X-Organization-Id` header (for service-to-service calls)
4. Returns null if none found

**Usage**:

```csharp
// Register
builder.Services.AddOrganizationContext();

// Use in endpoint
app.MapGet("/servers", (IOrganizationContext org, ServersDbContext db) =>
{
    if (!org.HasOrganization)
        return Results.Unauthorized();

    var servers = db.Servers
        .Where(s => s.OrganizationId == org.RequiredOrganizationId)
        .ToList();

    return Results.Ok(servers);
});
```

**Caching**: The implementation caches the resolved organization ID per-request to avoid repeated claim parsing.

---

## Permission Caching

### IPermissionCache

Cache user permissions within an organization to reduce database/Identity service calls.

**Interface**:

```csharp
public interface IPermissionCache
{
    Task<IReadOnlyCollection<string>?> GetPermissionsAsync(Guid userId, Guid organizationId, CancellationToken ct = default);
    Task SetPermissionsAsync(Guid userId, Guid organizationId, IReadOnlyCollection<string> permissions, CancellationToken ct = default);
    Task InvalidateAsync(Guid userId, Guid organizationId, CancellationToken ct = default);
    Task InvalidateUserAsync(Guid userId, CancellationToken ct = default);
    Task InvalidateOrganizationAsync(Guid organizationId, CancellationToken ct = default);
}
```

### DistributedPermissionCache

Implementation using `IDistributedCache` (Redis or any compatible backend).

**Configuration**:

```csharp
public sealed class PermissionCacheOptions
{
    public string KeyPrefix { get; set; } = "permissions";
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
    public bool Enabled { get; set; } = true;
}
```

**Key Format**: `{KeyPrefix}:{userId}:{organizationId}`

**Usage**:

```csharp
// Register with Redis
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});
builder.Services.Configure<PermissionCacheOptions>(config.GetSection("PermissionCache"));
builder.Services.AddSingleton<IPermissionCache, DistributedPermissionCache>();

// Use in authorization handler
public class PermissionHandler(IPermissionCache cache)
{
    public async Task<bool> HasPermissionAsync(Guid userId, Guid orgId, string permission)
    {
        var cached = await cache.GetPermissionsAsync(userId, orgId);
        if (cached is not null)
            return cached.Contains(permission);

        // Fetch from Identity service and cache...
    }
}
```

**Limitations**: `InvalidateUserAsync` and `InvalidateOrganizationAsync` rely on TTL expiration. Pattern-based invalidation requires Redis-specific implementation (SCAN command).

---

## Middleware Pipeline Order

**ORDER MATTERS!** The recommended order for Meridian Console services:

```csharp
// 1. ForwardedHeaders - Must be first if behind a proxy
app.UseForwardedHeaders();

// 2. Correlation - Needed by all downstream middleware for tracing
app.UseMiddleware<CorrelationMiddleware>();

// 3. Problem Details - Catch exceptions early
app.UseMiddleware<ProblemDetailsMiddleware>();

// 4. Request Logging - Wraps the rest of the pipeline
app.UseMiddleware<RequestLoggingMiddleware>();

// 5. Request Size Limits - Handle 413 errors gracefully
app.UseRequestLimitsMiddleware();

// 6. CORS - Handle cross-origin requests
app.UseCors("PolicyName");

// 7. Authentication - Identify the user
app.UseAuthentication();

// 8. Authorization - Check permissions
app.UseAuthorization();

// 9. Rate Limiting - After auth so tenant context is available
app.UseRateLimiter();

// 10. Circuit Breaker - Protect downstream services
app.UseCircuitBreaker();

// 11. Your endpoints
app.MapGet("/", () => "Hello");
app.MapDhadgarDefaultEndpoints();
```

**Why this order?**

1. **ForwardedHeaders first**: Sets the correct `RemoteIpAddress` before anything else uses it
2. **Correlation early**: All other middleware and logging needs correlation IDs
3. **ProblemDetails before logging**: Ensures exceptions are formatted before being logged
4. **Logging wraps everything**: Captures timing for the entire request
5. **Auth before rate limiting**: Rate limiting can use user/tenant identity
6. **Circuit breaker last**: Only affects actual service calls, not auth/rate limiting

---

## Configuration Reference

### CircuitBreaker Section

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

### ServiceAuth Section

```json
{
  "ServiceAuth": {
    "TokenEndpoint": "https://identity/connect/token",
    "ClientId": "my-service",
    "ClientSecret": "secret-from-user-secrets",
    "Scope": "api:read api:write",
    "Audience": "meridian-api"
  }
}
```

### PermissionCache Section

```json
{
  "PermissionCache": {
    "KeyPrefix": "permissions",
    "CacheDuration": "00:05:00",
    "Enabled": true
  }
}
```

### RequestLimits (via Kestrel)

```json
{
  "Kestrel": {
    "Limits": {
      "MaxRequestBodySize": 1048576,
      "MaxRequestHeadersTotalSize": 32768,
      "MaxRequestLineSize": 8192
    }
  }
}
```

---

## Extension Points and Customization

### Custom Circuit Breaker State Store

```csharp
public class RedisCircuitBreakerStateStore : ICircuitBreakerStateStore
{
    private readonly IConnectionMultiplexer _redis;

    public CircuitState GetOrCreateState(string serviceId)
    {
        // Implement Redis-backed state storage
    }
    // ... other methods
}

builder.Services.AddCircuitBreaker<RedisCircuitBreakerStateStore>(builder.Configuration);
```

### Custom Organization Context Resolution

```csharp
public class CustomOrganizationContext : IOrganizationContext
{
    public Guid? OrganizationId => // Custom resolution logic
}

builder.Services.AddScoped<IOrganizationContext, CustomOrganizationContext>();
```

### Additional Health Checks

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<CustomHealthCheck>("custom", tags: ["ready"]);
```

### Swagger Customization

```csharp
builder.Services.AddMeridianSwagger(
    title: "My API",
    description: "Description",
    configureOptions: options =>
    {
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { /* ... */ });
        options.OperationFilter<AuthorizationOperationFilter>();
    });
```

---

## Usage Examples

### Minimal Service

```csharp
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDhadgarServiceDefaults();

var app = builder.Build();

app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.MapGet("/", () => "Hello");
app.MapDhadgarDefaultEndpoints();

app.Run();
```

### Service with Database and Security Logging

```csharp
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.MultiTenancy;
using Dhadgar.ServiceDefaults.Security;
using Dhadgar.ServiceDefaults.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDhadgarServiceDefaults();
builder.Services.AddOrganizationContext();
builder.Services.AddSecurityEventLogger();
builder.Services.AddMeridianSwagger("Servers API", "Game server management");

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

var app = builder.Build();

app.UseMeridianSwagger();
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.MapGet("/servers", (IOrganizationContext org) =>
{
    if (!org.HasOrganization)
        return Results.Unauthorized();
    // ... fetch servers for org
});

app.MapDhadgarDefaultEndpoints();
app.Run();
```

### Gateway with Circuit Breaker

```csharp
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Middleware;
using Dhadgar.ServiceDefaults.Resilience;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDhadgarServiceDefaults();
builder.Services.AddCircuitBreaker(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseCircuitBreaker();

// YARP or other proxy middleware
app.MapDhadgarDefaultEndpoints();
app.Run();
```

---

## Testing

### Unit Testing Middleware

```csharp
[Fact]
public async Task CorrelationMiddleware_generates_correlation_id_when_missing()
{
    // Arrange
    var context = new DefaultHttpContext();
    var middleware = new CorrelationMiddleware(next: _ => Task.CompletedTask);

    // Act
    await middleware.InvokeAsync(context);

    // Assert
    Assert.True(context.Response.Headers.ContainsKey("X-Correlation-Id"));
    Assert.True(context.Items.ContainsKey("CorrelationId"));
}
```

### Integration Testing with WebApplicationFactory

```csharp
public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthCheckTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
        });
    }

    [Fact]
    public async Task Healthz_returns_ok()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

### Testing Service Authentication

```csharp
[Fact]
public async Task TokenProvider_caches_token_until_expiry()
{
    using var handler = new TokenEndpointHandler();
    using var httpClient = new HttpClient(handler);
    var options = Options.Create(new ServiceAuthenticationOptions
    {
        TokenEndpoint = "https://identity.test/connect/token",
        ClientId = "client",
        ClientSecret = "secret"
    });

    using var provider = new ServiceTokenProvider(httpClient, options, TimeProvider.System);

    var first = await provider.GetAccessTokenAsync();
    var second = await provider.GetAccessTokenAsync();

    Assert.Equal("token-1", first);
    Assert.Equal(first, second);
    Assert.Equal(1, handler.CallCount); // Only one token fetch
}
```

---

## File Structure

```
src/Shared/Dhadgar.ServiceDefaults/
|-- Dhadgar.ServiceDefaults.csproj     # Project file
|-- Hello.cs                            # Smoke test surface
|-- ServiceDefaultsExtensions.cs        # Core extension methods
|-- ServiceAuthenticationHandler.cs     # Service-to-service auth
|-- Caching/
|   |-- IPermissionCache.cs            # Permission cache interface
|   |-- DistributedPermissionCache.cs  # Redis-backed implementation
|-- Middleware/
|   |-- CorrelationMiddleware.cs       # Request correlation
|   |-- ProblemDetailsMiddleware.cs    # RFC 7807 errors
|   |-- RequestLoggingMiddleware.cs    # Request logging
|-- MultiTenancy/
|   |-- OrganizationContext.cs         # Tenant context resolution
|-- Resilience/
|   |-- CircuitBreakerExtensions.cs    # DI registration
|   |-- CircuitBreakerMiddleware.cs    # Circuit breaker implementation
|   |-- CircuitBreakerOptions.cs       # Configuration options
|-- Security/
|   |-- RequestLimits.cs               # Request size limits
|   |-- SecurityEventLogger.cs         # Security audit logging
|   |-- SecurityExtensions.cs          # DI registration
|-- Swagger/
    |-- SwaggerExtensions.cs           # OpenAPI configuration
```

---

## Related Documentation

- [Dhadgar.Contracts](/docs/libraries/contracts.md) - DTOs, message contracts, API models
- [Dhadgar.Messaging](/docs/libraries/messaging.md) - MassTransit/RabbitMQ conventions
- [Dhadgar.Shared](/docs/libraries/shared.md) - Utilities and primitives
- [RFC 7807 - Problem Details for HTTP APIs](https://tools.ietf.org/html/rfc7807)
- [OpenTelemetry .NET Documentation](https://opentelemetry.io/docs/instrumentation/net/)
- [ASP.NET Core Health Checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)
- [W3C Trace Context](https://www.w3.org/TR/trace-context/)
- [Circuit Breaker Pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)
