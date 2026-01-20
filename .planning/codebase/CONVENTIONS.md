# Coding Conventions

**Analysis Date:** 2026-01-19

## Naming Patterns

**Files:**
- PascalCase for all C# files: `OrganizationService.cs`, `TokenExchangeEndpoint.cs`
- Suffix pattern: `*Service.cs`, `*Endpoint.cs`, `*Tests.cs`, `*Middleware.cs`, `*Options.cs`
- Entity files match class name: `User.cs`, `Organization.cs`, `RefreshToken.cs`

**Functions:**
- PascalCase for all public methods: `CreateAsync`, `TryGetUserId`, `ValidateClientSecretAsync`
- Async suffix for async methods: `ListForUserAsync`, `GetSecretAsync`, `LoadSecretsAsync`
- `Try*` prefix for methods returning bool with out parameter: `TryGetUserId`, `TryGetOrganizationId`

**Variables:**
- camelCase for local variables and parameters: `userId`, `organizationId`, `secretName`
- `_` prefix for private fields: `_dbContext`, `_timeProvider`, `_logger`
- PascalCase for constants: `CorrelationIdHeader`, `TestSigningKey`

**Types:**
- PascalCase for all types: `OrganizationService`, `SecretsOptions`, `AuthorizationResult`
- `I` prefix for interfaces: `ISecretProvider`, `IPermissionService`, `IRefreshTokenService`
- Suffix conventions: `*Service`, `*Options`, `*Request`, `*Result`, `*Handler`, `*Middleware`

**Namespaces:**
- Root namespace: `Dhadgar.{ServiceName}`
- Sub-namespaces: `Dhadgar.{ServiceName}.{Feature}` (e.g., `Dhadgar.Identity.Services`, `Dhadgar.Secrets.Authorization`)

## Code Style

**Formatting:**
- EditorConfig at `.editorconfig`
- 4-space indentation for C# files
- 2-space indentation for JSON, YAML, Markdown
- UTF-8 encoding with LF line endings
- Insert final newline, trim trailing whitespace

**Linting:**
- .NET SDK analyzers enabled: `EnableNETAnalyzers=true`
- Analysis level: `latest`
- Analysis mode: `AllEnabledByDefault`
- Code style enforced in build: `EnforceCodeStyleInBuild=true`
- Warnings treated as errors in CI only

**Suppressed Warnings:**
```
CA1707, CA1716, CA1515, CA2007, CA1308, CA1062, CA1031, CA1056,
CA2227, CA1861, CA1310, CA1034, CA1848, CA2234, CS8600, CS8604, SYSLIB0057
```

## Import Organization

**Order:**
1. System namespaces (`System.*`)
2. Microsoft namespaces (`Microsoft.*`)
3. Third-party namespaces (alphabetical)
4. Project namespaces (`Dhadgar.*`)

**Path Aliases:**
- Use aliasing for disambiguating same-named types:
```csharp
using GatewayHello = Dhadgar.Gateway.Hello;
using IdentityHello = Dhadgar.Identity.Hello;
using OptionsFactory = Microsoft.Extensions.Options.Options;
```

## Error Handling

**Patterns:**

**Service Layer - ServiceResult pattern:**
```csharp
public sealed record ServiceResult<T>(bool Success, T? Value, string? Error)
{
    public static ServiceResult<T> Ok(T value) => new(true, value, null);
    public static ServiceResult<T> Fail(string error) => new(false, default, error);
}

// Usage:
if (string.IsNullOrWhiteSpace(request.Name))
    return ServiceResult.Fail<Organization>("name_required");
return ServiceResult.Ok(org);
```

**Endpoints - IResult pattern:**
```csharp
if (!TryGetUserId(context, out var userId))
    return Results.Unauthorized();

if (org is null)
    return Results.NotFound(new { error = "org_not_found" });

return Results.Forbid();
```

**Middleware - RFC 7807 Problem Details:**
```csharp
// See: src/Shared/Dhadgar.ServiceDefaults/Middleware/ProblemDetailsMiddleware.cs
var problemDetails = new
{
    type = "https://meridian.console/errors/internal-server-error",
    title = "Internal Server Error",
    status = (int)HttpStatusCode.InternalServerError,
    detail = includeDetails ? exception.Message : "An unexpected error occurred.",
    instance = context.Request.Path.ToString(),
    traceId = traceId
};
```

**Validation - Dedicated Validator Classes:**
```csharp
// See: src/Dhadgar.Secrets/Validation/SecretNameValidator.cs
public static class SecretNameValidator
{
    public static ValidationResult Validate(string? name) { ... }
}

public readonly record struct ValidationResult(bool IsValid, string? ErrorMessage)
{
    public static ValidationResult Success() => new(true, null);
    public static ValidationResult Failure(string message) => new(false, message);
}
```

## Logging

**Framework:** Microsoft.Extensions.Logging + OpenTelemetry

**Patterns:**
- Use structured logging with named parameters:
```csharp
_logger.LogError(exception,
    "Unhandled exception. TraceId: {TraceId}, Path: {Path}",
    traceId, context.Request.Path);
```

- Audit logging with standardized event names:
```csharp
// See: src/Dhadgar.Secrets/Audit/SecretsAuditLogger.cs
AUDIT:SECRETS:ACCESS    - Successful read operations
AUDIT:SECRETS:DENIED    - Access denied
AUDIT:SECRETS:MODIFY    - Write/delete operations
AUDIT:SECRETS:ROTATED   - Secret rotation events
AUDIT:SECRETS:BREAKGLASS - Break-glass access (WARNING level)
```

- Include correlation context:
```csharp
_logger.LogInformation(
    "Database migrations applied successfully.");
```

## Comments

**When to Comment:**
- XML doc comments on public interfaces and service methods
- `// SECURITY:` prefix for security-critical decisions
- Inline comments explaining non-obvious business logic

**JSDoc/TSDoc:**
- XML documentation with `<summary>`, `<param>`, `<returns>` tags:
```csharp
/// <summary>
/// Extracts user ID from authenticated JWT claims only.
/// SECURITY: This method does NOT trust headers - only validated JWT claims.
/// </summary>
public static bool TryGetUserId(HttpContext context, out Guid userId)
```

**Security Comments:**
```csharp
// SECURITY: Use /64 prefix for IPv6 to prevent address rotation attacks
// SECURITY: Only trust X-Forwarded-* headers from known Cloudflare IP ranges
// SECURITY FIX: Only trust authenticated JWT claims, never headers
```

## Function Design

**Size:**
- Methods generally under 50 lines
- Extract helper methods for complex logic

**Parameters:**
- Use record types for complex requests: `OrganizationCreateRequest`, `OrganizationUpdateRequest`
- Include `CancellationToken ct = default` for async methods
- Out parameters for `Try*` pattern methods

**Return Values:**
- `ServiceResult<T>` for service layer operations
- `IResult` for endpoint handlers (via Minimal APIs)
- `Task<T>` for async methods, `ValueTask<T>` where appropriate
- Nullable returns (`T?`) for "not found" scenarios

## Module Design

**Exports:**
- One primary class per file
- Related records/DTOs can be in same file as service (see `OrganizationService.cs`)

**Barrel Files:**
- Not used; explicit using statements preferred

## Entity Design

**Conventions:**
- `Id` property as primary key (Guid)
- Timestamp properties: `CreatedAt`, `UpdatedAt`, `DeletedAt` (for soft delete)
- Navigation properties at bottom of class
- `[Required]` and `[MaxLength]` attributes for validation
- PostgreSQL xmin-based optimistic concurrency via `uint Version`

**Example:**
```csharp
// See: src/Dhadgar.Identity/Data/Entities/User.cs
public sealed class User : IdentityUser<Guid>
{
    [Required]
    [MaxLength(255)]
    public string ExternalAuthId { get; set; } = null!;

    [MaxLength(200)]
    public string? DisplayName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public uint Version { get; set; }  // Concurrency token

    public ICollection<UserOrganization> Organizations { get; set; } = new List<UserOrganization>();
}
```

## Endpoint Design

**Minimal API Pattern:**
```csharp
// Static Map method to register endpoints
public static class OrganizationEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/organizations", ListHandler)
            .RequireAuthorization()
            .WithTags("Organizations");
    }

    private static async Task<IResult> ListHandler(
        HttpContext context,
        OrganizationService service,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
            return Results.Unauthorized();

        var orgs = await service.ListForUserAsync(userId, ct);
        return Results.Ok(orgs);
    }
}
```

**Authorization Patterns:**
- `.RequireAuthorization()` for authenticated endpoints
- `.RequireAuthorization("OrgAdmin")` for role-based access
- `.AllowAnonymous()` for public endpoints
- Use `EndpointHelpers.TryGetUserId()` to extract user from JWT claims (never headers)

## Middleware Ordering

**Documented order in Program.cs:**
```csharp
// 0. ForwardedHeaders MUST run first
app.UseForwardedHeaders();

// 1. CORS preflight handler
app.UseMiddleware<CorsPreflightMiddleware>();

// 2. Security headers
app.UseMiddleware<SecurityHeadersMiddleware>();

// 3. Correlation ID tracking
app.UseMiddleware<CorrelationMiddleware>();

// 4. Problem Details exception handler
app.UseMiddleware<ProblemDetailsMiddleware>();

// 5. Request logging
app.UseMiddleware<RequestLoggingMiddleware>();

// 6. CORS for non-preflight requests
app.UseCors(CorsConfiguration.PolicyName);

// 7. Authentication/authorization
app.UseAuthentication();
app.UseAuthorization();

// 8. Rate limiting
app.UseRateLimiter();
```

## Dependency Injection

**Registration Patterns:**
```csharp
// Singleton for stateless services
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ...);

// Scoped for per-request services (especially those with DbContext)
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();

// Transient for lightweight stateless utilities
builder.Services.AddTransient<...>();

// Configuration binding with validation
builder.Services.AddOptions<ReadyzOptions>()
    .Bind(builder.Configuration.GetSection("Readyz"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

## Security Conventions

**Never trust headers for identity:**
```csharp
// WRONG - vulnerable to impersonation
var userId = context.Request.Headers["X-User-Id"];

// CORRECT - only trust validated JWT claims
var userId = context.User.GetUserId();
```

**Claims-based authorization:**
```csharp
// Permission claim format: "{resource}:{action}:{scope}"
// Example: "secrets:read:oauth"
var permissions = user.Claims.Where(c => c.Type == "permission");
```

**Input validation:**
- Validate all external input before processing
- Use dedicated validator classes for complex validation
- Return 400 Bad Request for invalid input (not 500)

---

*Convention analysis: 2026-01-19*
