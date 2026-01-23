# Dhadgar.ServiceDefaults

**The foundational shared library for all Meridian Console microservices.**

This library provides standardized middleware, health checks, resilience patterns, security utilities, caching abstractions, multi-tenancy support, and Swagger/OpenAPI configuration. Every microservice in the Meridian Console platform references this library to ensure consistent behavior across the entire system.

---

## Table of Contents

1. [Overview](#overview)
2. [Purpose and Design Philosophy](#purpose-and-design-philosophy)
3. [Installation and Dependencies](#installation-and-dependencies)
4. [Quick Start](#quick-start)
5. [Core Extension Methods](#core-extension-methods)
   - [AddDhadgarServiceDefaults](#adddhadgarservicedefaults)
   - [MapDhadgarDefaultEndpoints](#mapdhadgardefaultendpoints)
6. [Middleware Components](#middleware-components)
   - [CorrelationMiddleware](#correlationmiddleware)
   - [ProblemDetailsMiddleware](#problemdetailsmiddleware)
   - [RequestLoggingMiddleware](#requestloggingmiddleware)
   - [RequestLimitsMiddleware](#requestlimitsmiddleware)
7. [Resilience: Circuit Breaker](#resilience-circuit-breaker)
   - [CircuitBreakerMiddleware](#circuitbreakermiddleware)
   - [CircuitBreakerOptions](#circuitbreakeroptions)
   - [ICircuitBreakerStateStore](#icircuitbreakerstatestore)
   - [Circuit Breaker Metrics](#circuit-breaker-metrics)
8. [Service-to-Service Authentication](#service-to-service-authentication)
   - [ServiceAuthenticationHandler](#serviceauthenticationhandler)
   - [ServiceTokenProvider](#servicetokenprovider)
9. [Swagger/OpenAPI Configuration](#swaggeropenapi-configuration)
   - [AddMeridianSwagger](#addmeridianswagger)
   - [UseMeridianSwagger](#usemeridianswagger)
10. [Security Infrastructure](#security-infrastructure)
    - [ISecurityEventLogger](#isecurityeventlogger)
    - [Request Size Limits](#request-size-limits)
11. [Multi-Tenancy Support](#multi-tenancy-support)
    - [IOrganizationContext](#iorganizationcontext)
12. [Permission Caching](#permission-caching)
    - [IPermissionCache](#ipermissioncache)
    - [DistributedPermissionCache](#distributedpermissioncache)
13. [Health Checks](#health-checks)
14. [Middleware Pipeline Order](#middleware-pipeline-order)
15. [Configuration Reference](#configuration-reference)
16. [Testing](#testing)
17. [Related Documentation](#related-documentation)

---

## Overview

`Dhadgar.ServiceDefaults` is a .NET class library that serves as the common infrastructure layer for all Meridian Console microservices. It encapsulates cross-cutting concerns that every service needs, ensuring:

- **Consistent observability** through correlation tracking and structured logging
- **Standardized error responses** using RFC 7807 Problem Details
- **Resilience patterns** with configurable circuit breakers
- **Security infrastructure** including event logging and request limits
- **Multi-tenancy support** with organization context resolution
- **Health check endpoints** for Kubernetes liveness and readiness probes
- **API documentation** with Swagger/OpenAPI integration

The library is designed to be **referenced but not modified** by individual services. Services consume these defaults through well-defined extension methods, allowing the platform team to evolve common infrastructure without requiring changes to every service.

---

## Purpose and Design Philosophy

### Why This Library Exists

In a microservices architecture, cross-cutting concerns can easily become inconsistent across services. Without a shared library:

- Each service might implement correlation tracking differently
- Error response formats would vary
- Health check endpoints might have different paths or response schemas
- Logging formats would be inconsistent, making observability difficult

`Dhadgar.ServiceDefaults` solves these problems by providing a single source of truth for common infrastructure.

### Design Principles

1. **Convention over Configuration**: Sensible defaults that work out of the box
2. **Extensibility**: Services can customize behavior when needed
3. **Separation of Concerns**: Each component handles one responsibility
4. **Testability**: All components are designed for unit and integration testing
5. **Performance**: Source-generated logging, minimal allocations, async-first APIs

### What This Library Does NOT Do

- **No business logic**: This is purely infrastructure code
- **No database access**: Services own their data; this library provides abstractions
- **No inter-service communication**: Use `Dhadgar.Messaging` for MassTransit/RabbitMQ patterns
- **No contract definitions**: Use `Dhadgar.Contracts` for DTOs and message contracts

---

## Installation and Dependencies

### Project Reference

All Meridian Console services reference this library via a project reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\Shared\Dhadgar.ServiceDefaults\Dhadgar.ServiceDefaults.csproj" />
</ItemGroup>
```

### Framework Dependencies

The library has a single framework reference:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

This provides access to all ASP.NET Core APIs (WebApplication, IServiceCollection, health checks, etc.) without requiring NuGet packages for core functionality.

### Package Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.AspNetCore.OpenApi` | 10.0.0 | OpenAPI endpoint exploration |
| `Microsoft.OpenApi` | 2.3.0 | OpenAPI document types |
| `Swashbuckle.AspNetCore` | 10.1.0 | Swagger generation and UI |

All package versions are managed centrally in `Directory.Packages.props` at the solution root.

---

## Quick Start

The simplest way to use this library follows this pattern in your service's `Program.cs`:

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
app.MapGet("/hello", () => Results.Text("Hello!"));

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

**What it does**:
- Registers ASP.NET Core health checks
- Adds a basic "self" health check that always returns `Healthy`
- Tags the self-check with `["live"]` for Kubernetes liveness probes

**Usage**:
```csharp
builder.Services.AddDhadgarServiceDefaults();
```

**Implementation Details**:

The method is intentionally minimal. It sets up the foundation that `MapDhadgarDefaultEndpoints` builds upon:

```csharp
public static IServiceCollection AddDhadgarServiceDefaults(this IServiceCollection services)
{
    services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

    return services;
}
```

Services can add additional health checks before or after calling this method:

```csharp
builder.Services.AddDhadgarServiceDefaults();
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

**What it does**:
- Maps three health check endpoints: `/healthz`, `/livez`, `/readyz`
- Configures a JSON response writer with detailed health information
- Sets all endpoints to allow anonymous access
- Tags endpoints with "Health" for Swagger grouping

**Endpoints Created**:

| Endpoint | Purpose | Filter |
|----------|---------|--------|
| `/healthz` | Comprehensive health | All registered checks |
| `/livez` | Kubernetes liveness | Only checks tagged `live` |
| `/readyz` | Kubernetes readiness | Only checks tagged `ready` |

**Response Format**:

All endpoints return JSON with this structure:

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

**Usage**:
```csharp
app.MapDhadgarDefaultEndpoints();
```

**Why Three Endpoints?**

Kubernetes distinguishes between liveness and readiness:

- **Liveness (`/livez`)**: "Is the process alive?" If this fails, Kubernetes restarts the pod
- **Readiness (`/readyz`)**: "Can the process accept traffic?" If this fails, the pod is removed from load balancer rotation
- **Health (`/healthz`)**: Comprehensive check for diagnostics and monitoring

Tag your health checks appropriately:
- Database connections: `["ready"]` - service can't do useful work without them
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

The middleware integrates with `System.Diagnostics.Activity`:

```csharp
var activity = Activity.Current;
if (activity is not null)
{
    activity.SetTag("correlation.id", correlationId);
    activity.SetTag("request.id", requestId);
    activity.SetBaggage("correlation.id", correlationId);
}
```

This ensures correlation IDs appear in distributed traces, making it easy to track requests across service boundaries.

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

**Security Considerations**:

The middleware validates incoming header values using a regex pattern:

```csharp
private static readonly Regex CorrelationPattern = new("^[A-Za-z0-9-]+$", RegexOptions.Compiled);
```

Invalid or overly long values are rejected, and new IDs are generated instead. This prevents header injection attacks.

---

### ProblemDetailsMiddleware

**Namespace**: `Dhadgar.ServiceDefaults.Middleware`

**Purpose**: Transforms unhandled exceptions into RFC 7807 Problem Details responses.

**What it does**:

1. **Catches unhandled exceptions**: Wraps the entire downstream pipeline in a try-catch
2. **Logs the error**: Uses structured logging with correlation context
3. **Returns Problem Details**: Formats the response according to RFC 7807
4. **Environment-aware**: Includes stack traces only in Development/Testing environments

**Response Format**:

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

In Development/Testing mode, the response includes more detail:

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

**Content Type**:

Responses use `application/problem+json` as specified by RFC 7807.

**Usage**:
```csharp
app.UseMiddleware<ProblemDetailsMiddleware>();
```

**Important**: This middleware should run early in the pipeline (after CorrelationMiddleware) to catch exceptions from all downstream middleware.

**Headers Already Sent**:

If the response has already started (e.g., during streaming), the middleware logs a warning but cannot write the problem details:

```csharp
if (context.Response.HasStarted)
{
    _logger.LogWarning(
        "Cannot write problem details response after headers sent. Exception: {ExceptionType}",
        exception.GetType().Name);
    return;
}
```

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

```csharp
{
    "CorrelationId": "abc123",
    "RequestId": "def456",
    "RequestMethod": "POST",
    "RequestPath": "/api/servers"
}
```

This enables log aggregation tools like Grafana Loki to filter logs by correlation ID.

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

**Exception Handling**:

When an exception occurs, the middleware logs it and rethrows:

```csharp
catch (Exception ex)
{
    stopwatch.Stop();
    _logger.LogError(ex,
        "HTTP {Method} {Path} failed after {ElapsedMs}ms",
        context.Request.Method,
        context.Request.Path,
        stopwatch.ElapsedMilliseconds);
    throw;
}
```

**Usage**:
```csharp
app.UseMiddleware<RequestLoggingMiddleware>();
```

**Important**: This middleware depends on `CorrelationMiddleware` having already run to populate `HttpContext.Items` with correlation data.

---

### RequestLimitsMiddleware

**Namespace**: `Dhadgar.ServiceDefaults.Security`

**Purpose**: Handles request body size limit exceptions with proper JSON error responses.

**What it does**:

1. **Catches size limit exceptions**: Intercepts `BadHttpRequestException` with status 413
2. **Logs the attempt**: Records client IP and path
3. **Returns JSON error**: Provides machine-readable error response

**Response Format**:

```json
{
  "error": "request_too_large",
  "message": "Request body exceeds maximum allowed size"
}
```

**Related: Request Size Configuration**:

Configure Kestrel limits using the `ConfigureRequestLimits` extension:

```csharp
builder.ConfigureRequestLimits(options =>
{
    options.MaxRequestBodySize = 1_048_576;     // 1 MB default
    options.MaxRequestHeadersTotalSize = 32_768; // 32 KB
    options.MaxRequestLineSize = 8_192;          // 8 KB
    options.MaxFileUploadSize = 52_428_800;      // 50 MB
});
```

**Per-Endpoint Overrides**:

For endpoints that need larger limits (e.g., file uploads):

```csharp
// Disable limit entirely
app.MapPost("/files/upload", HandleUpload)
    .DisableRequestSizeLimit();

// Custom limit
app.MapPost("/files/upload", HandleUpload)
    .WithRequestSizeLimit(100 * 1024 * 1024); // 100 MB
```

---

## Resilience: Circuit Breaker

The circuit breaker pattern prevents cascading failures by temporarily blocking requests to unhealthy backend services.

### CircuitBreakerMiddleware

**Namespace**: `Dhadgar.ServiceDefaults.Resilience`

**Purpose**: Implements the circuit breaker pattern for downstream service calls.

**How it works**:

1. **Closed State** (normal): Requests pass through. Failures are counted.
2. **Open State** (tripped): After N failures, requests are immediately rejected with 503.
3. **Half-Open State** (testing): After a timeout, limited requests are allowed to test recovery.

**Service Identification**:

The middleware reads the service ID from `HttpContext.Items`:

```csharp
var serviceId = context.Items["CircuitBreaker:ServiceId"] as string;
```

If no service ID is set, requests pass through without circuit breaking. This is typically set by:
- YARP reverse proxy configuration
- Custom middleware that identifies the target service

**503 Response**:

When the circuit is open:

```json
{
  "type": "https://httpstatuses.com/503",
  "title": "Service Temporarily Unavailable",
  "status": 503,
  "detail": "The identity service is temporarily unavailable due to circuit breaker activation. Please retry after the specified time.",
  "instance": "/api/users/me"
}
```

The response includes a `Retry-After` header with the number of seconds until the circuit may close.

**Usage**:

```csharp
// Register services
builder.Services.AddCircuitBreaker(builder.Configuration);

// Use middleware
app.UseCircuitBreaker();
```

---

### CircuitBreakerOptions

**Configuration section**: `CircuitBreaker`

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `FailureThreshold` | int | 5 | Consecutive failures before circuit opens |
| `SuccessThreshold` | int | 2 | Successful requests to close circuit from half-open |
| `OpenDurationSeconds` | int | 30 | How long circuit stays open |
| `FailureStatusCodes` | int[] | [500,502,503,504] | Status codes counted as failures |
| `IncludeServiceNameInErrors` | bool | false | Include service name in 503 responses |

**Example configuration**:

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

**Security Note**: Set `IncludeServiceNameInErrors` to `false` in production to prevent information disclosure about internal service architecture.

---

### ICircuitBreakerStateStore

**Purpose**: Abstraction for circuit breaker state storage, enabling distributed scenarios.

**Interface**:

```csharp
public interface ICircuitBreakerStateStore
{
    CircuitState GetOrCreateState(string serviceId);
    void RemoveState(string serviceId);
    IEnumerable<(string ServiceId, CircuitState State)> GetAllStates();
}
```

**Built-in Implementation**:

`InMemoryCircuitBreakerStateStore` is registered by default. It uses a `ConcurrentDictionary` and is suitable for single-instance deployments.

**Custom Implementation**:

For distributed deployments, implement a Redis-backed store:

```csharp
builder.Services.AddCircuitBreaker<RedisCircuitBreakerStateStore>(builder.Configuration);
```

---

### Circuit Breaker Metrics

The middleware exposes OpenTelemetry metrics for monitoring:

| Metric | Type | Description |
|--------|------|-------------|
| `circuit_breaker.opened` | Counter | Times circuits have opened |
| `circuit_breaker.closed` | Counter | Times circuits have closed |
| `circuit_breaker.requests_blocked` | Counter | Requests blocked by open circuits |
| `circuit_breaker.failures_recorded` | Counter | Failures recorded |

All metrics include a `service_id` tag for filtering.

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

## Service-to-Service Authentication

### ServiceAuthenticationHandler

**Namespace**: `Dhadgar.ServiceDefaults`

**Purpose**: A `DelegatingHandler` that automatically attaches bearer tokens to outgoing HTTP requests.

**What it does**:

1. Retrieves an access token from `IServiceTokenProvider`
2. Sets the `Authorization` header to `Bearer {token}`
3. Proceeds with the HTTP request

**Usage**:

```csharp
// Register authentication services
builder.Services.AddServiceAuthentication(builder.Configuration);

// Configure an HttpClient to use the handler
builder.Services.AddHttpClient("IdentityService")
    .AddServiceAuthentication()
    .ConfigureHttpClient(client => client.BaseAddress = new Uri("https://identity.local"));
```

---

### ServiceTokenProvider

**Purpose**: Obtains and caches OAuth2 client credentials tokens.

**Token Flow**:

1. Checks if a cached token exists and is valid (with 30-second skew)
2. If expired/missing, requests a new token from the configured endpoint
3. Caches the token based on `expires_in` from the response

**Configuration**:

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

**Thread Safety**:

Uses `SemaphoreSlim` to prevent concurrent token refreshes. Multiple threads waiting for a token will share the result of a single refresh operation.

**Testability**:

The provider accepts `TimeProvider` for time-based testing:

```csharp
public ServiceTokenProvider(
    HttpClient httpClient,
    IOptions<ServiceAuthenticationOptions> options,
    TimeProvider timeProvider)
```

---

## Swagger/OpenAPI Configuration

### AddMeridianSwagger

**Namespace**: `Dhadgar.ServiceDefaults.Swagger`

**Signature**:
```csharp
public static IServiceCollection AddMeridianSwagger(
    this IServiceCollection services,
    string title,
    string description,
    Action<SwaggerGenOptions>? configureOptions = null)
```

**What it does**:

1. Registers `EndpointsApiExplorer` for endpoint discovery
2. Configures SwaggerGen with standard settings
3. Creates a v1 OpenAPI document with the provided title/description
4. Allows additional configuration via callback

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

---

### UseMeridianSwagger

**Signature**:
```csharp
public static WebApplication UseMeridianSwagger(this WebApplication app)
```

**What it does**:

1. Checks if environment is Development or Testing
2. Enables Swagger middleware
3. Configures Swagger UI at `/swagger`

**Usage**:
```csharp
app.UseMeridianSwagger();
```

**Important**: Swagger is only enabled in Development and Testing environments for security reasons.

---

## Security Infrastructure

### ISecurityEventLogger

**Namespace**: `Dhadgar.ServiceDefaults.Security`

**Purpose**: Provides structured logging for security-relevant events with consistent formatting for SIEM integration.

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

### Request Size Limits

See [RequestLimitsMiddleware](#requestlimitsmiddleware) above for details.

---

## Multi-Tenancy Support

### IOrganizationContext

**Namespace**: `Dhadgar.ServiceDefaults.MultiTenancy`

**Purpose**: Provides the current organization context from the request for tenant isolation.

**Interface**:

```csharp
public interface IOrganizationContext
{
    Guid? OrganizationId { get; }
    bool HasOrganization { get; }
    Guid RequiredOrganizationId { get; } // Throws if not available
}
```

**How it resolves organization**:

1. Checks JWT claim `org_id` (preferred)
2. Checks JWT claim `organization_id` (alternative)
3. Checks `X-Organization-Id` header (for service-to-service calls)
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

**Caching**:

The implementation caches the resolved organization ID per-request to avoid repeated claim parsing:

```csharp
public Guid? OrganizationId
{
    get
    {
        if (!_resolved)
        {
            _cachedOrgId = ResolveOrganizationId();
            _resolved = true;
        }
        return _cachedOrgId;
    }
}
```

---

## Permission Caching

### IPermissionCache

**Namespace**: `Dhadgar.ServiceDefaults.Caching`

**Purpose**: Cache user permissions within an organization to reduce database/Identity service calls.

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

---

### DistributedPermissionCache

**Purpose**: Implementation using `IDistributedCache` (Redis or any compatible backend).

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

**Limitations**:

- `InvalidateUserAsync` and `InvalidateOrganizationAsync` rely on TTL expiration
- Pattern-based invalidation requires Redis-specific implementation (SCAN command)

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

---

## Health Checks

### Standard Endpoints

| Endpoint | Purpose | Predicate |
|----------|---------|-----------|
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

## Middleware Pipeline Order

**ORDER MATTERS!** Middleware executes in the order it's added. The recommended order for Meridian Console services:

```csharp
// 1. ForwardedHeaders - Must be first if behind a proxy
app.UseForwardedHeaders();

// 2. Security headers - Apply to all responses
app.UseMiddleware<SecurityHeadersMiddleware>(); // If using

// 3. Correlation - Needed by all downstream middleware for tracing
app.UseMiddleware<CorrelationMiddleware>();

// 4. Problem Details - Catch exceptions early
app.UseMiddleware<ProblemDetailsMiddleware>();

// 5. Request Logging - Wraps the rest of the pipeline
app.UseMiddleware<RequestLoggingMiddleware>();

// 6. Request Size Limits - Handle 413 errors gracefully
app.UseRequestLimitsMiddleware();

// 7. CORS - Handle cross-origin requests
app.UseCors("PolicyName");

// 8. Authentication - Identify the user
app.UseAuthentication();

// 9. Authorization - Check permissions
app.UseAuthorization();

// 10. Rate Limiting - After auth so tenant context is available
app.UseRateLimiter();

// 11. Circuit Breaker - Protect downstream services
app.UseCircuitBreaker();

// 12. Your endpoints
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

## Testing

### Unit Testing Middleware

Test middleware components in isolation using `DefaultHttpContext`:

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

Use `WebApplicationFactory<Program>` to test services with all middleware:

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

The test project includes examples for testing `ServiceTokenProvider`:

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

### SwaggerTestHelper

A shared helper class for verifying Swagger configuration:

```csharp
[Fact]
public async Task Swagger_endpoint_returns_valid_openapi()
{
    await SwaggerTestHelper.VerifySwaggerEndpointAsync(
        _factory,
        expectedTitle: "Dhadgar Servers API");
}

[Fact]
public async Task Swagger_contains_expected_paths()
{
    await SwaggerTestHelper.VerifySwaggerContainsPathsAsync(
        _factory,
        ["/", "/hello"]);
}
```

---

## Related Documentation

### Other Shared Libraries

- **Dhadgar.Contracts** - DTOs, message contracts, API models shared across services
- **Dhadgar.Messaging** - MassTransit/RabbitMQ conventions for async messaging
- **Dhadgar.Shared** - Utilities and primitives (helpers, constants, etc.)

### Architecture Documentation

- [`/docs/`](/docs/) - Architecture decision records and design documents
- [`/CLAUDE.md`](/CLAUDE.md) - Repository-wide development guide
- [`/deploy/compose/README.md`](/deploy/compose/README.md) - Local infrastructure setup

### External References

- [RFC 7807 - Problem Details for HTTP APIs](https://tools.ietf.org/html/rfc7807)
- [OpenTelemetry .NET Documentation](https://opentelemetry.io/docs/instrumentation/net/)
- [ASP.NET Core Health Checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)
- [W3C Trace Context](https://www.w3.org/TR/trace-context/)
- [Circuit Breaker Pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)

---

## File Structure

```
src/Shared/Dhadgar.ServiceDefaults/
├── Dhadgar.ServiceDefaults.csproj     # Project file
├── Hello.cs                            # Smoke test surface
├── ServiceDefaultsExtensions.cs        # Core extension methods
├── ServiceAuthenticationHandler.cs     # Service-to-service auth
├── Caching/
│   ├── IPermissionCache.cs            # Permission cache interface
│   └── DistributedPermissionCache.cs  # Redis-backed implementation
├── Middleware/
│   ├── CorrelationMiddleware.cs       # Request correlation
│   ├── ProblemDetailsMiddleware.cs    # RFC 7807 errors
│   └── RequestLoggingMiddleware.cs    # Request logging
├── MultiTenancy/
│   └── OrganizationContext.cs         # Tenant context resolution
├── Resilience/
│   ├── CircuitBreakerExtensions.cs    # DI registration
│   ├── CircuitBreakerMiddleware.cs    # Circuit breaker implementation
│   └── CircuitBreakerOptions.cs       # Configuration options
├── Security/
│   ├── RequestLimits.cs               # Request size limits
│   ├── SecurityEventLogger.cs         # Security audit logging
│   └── SecurityExtensions.cs          # DI registration
└── Swagger/
    └── SwaggerExtensions.cs           # OpenAPI configuration
```

---

## Changelog

This library follows the Meridian Console release cycle. See the repository's release notes for version-specific changes.

---

## Contributing

This is a **shared library** that affects all services. Changes should be:

1. Backward compatible (or coordinated across all services)
2. Well-tested with both unit and integration tests
3. Documented in this README
4. Reviewed by the platform team

For questions or suggestions, open an issue in the repository.
