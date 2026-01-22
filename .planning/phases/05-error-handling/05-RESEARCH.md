# Phase 5: Error Handling - Research

**Researched:** 2026-01-22
**Domain:** RFC 9457 Problem Details, ASP.NET Core exception handling
**Confidence:** HIGH

## Summary

Phase 5 enhances the existing `ProblemDetailsMiddleware` to achieve full RFC 9457 compliance with exception classification, trace context in responses, and production-safe error handling. The codebase already has a solid foundation: `ProblemDetailsMiddleware` returns the correct content type (`application/problem+json`), includes traceId, and has environment-based detail filtering. The gap is exception classification - currently all exceptions return 500.

The primary work involves:
1. Upgrading the middleware to classify exceptions and map them to appropriate HTTP status codes (ERR-02)
2. Adding the `correlationId` field alongside `traceId` in responses (ERR-04)
3. Verifying production safety (ERR-03) - largely done but needs explicit testing
4. Ensuring all error responses use Problem Details format (ERR-01)

**Primary recommendation:** Implement `IExceptionHandler` pattern with exception taxonomy, leveraging ASP.NET Core 10's built-in `AddProblemDetails()` service while maintaining backward compatibility with existing middleware patterns.

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.AspNetCore.Http.Abstractions | 10.0.0 | ProblemDetails class, IProblemDetailsService | Native ASP.NET Core, RFC 9457 compliant |
| Microsoft.AspNetCore.Diagnostics | 10.0.0 | IExceptionHandler, UseExceptionHandler() | Built-in exception handling pattern |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| FluentValidation | 12.1.1 | Request DTO validation | When validating complex request bodies |
| FluentValidation.DependencyInjectionExtensions | 12.1.1 | DI integration for validators | Alongside FluentValidation |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Custom ProblemDetailsMiddleware | Built-in UseExceptionHandler + AddProblemDetails | Built-in is simpler but less control; custom allows trace ID fallback chain |
| IExceptionHandler | Custom middleware | IExceptionHandler is cleaner separation, supports chaining |
| FluentValidation | DataAnnotations | FluentValidation is more flexible, better async support |

**Installation:**
```bash
# Add to Directory.Packages.props (FluentValidation only - others are built-in)
<PackageVersion Include="FluentValidation" Version="12.1.1" />
<PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
```

## Architecture Patterns

### Current State Analysis

The existing `ProblemDetailsMiddleware` (at `src/Shared/Dhadgar.ServiceDefaults/Middleware/ProblemDetailsMiddleware.cs`) provides:

**Working:**
- Returns `application/problem+json` content type
- Includes `traceId` with fallback chain (Activity.TraceId -> CorrelationId -> TraceIdentifier -> "unknown")
- Environment-based detail filtering (stack traces only in Development/Testing)
- Proper logging of exceptions

**Gaps:**
- All exceptions return HTTP 500 (no exception classification)
- Missing `correlationId` as separate field (only traceId is included)
- Uses anonymous type instead of standard `ProblemDetails` class
- Not integrated with ASP.NET Core's `IProblemDetailsService`

### Recommended Architecture

```
src/Shared/Dhadgar.ServiceDefaults/
├── Errors/
│   ├── ExceptionClassifier.cs          # Maps exceptions to (StatusCode, Type, Title)
│   ├── GlobalExceptionHandler.cs       # IExceptionHandler implementation
│   ├── DomainExceptions.cs             # Base exception types
│   └── ProblemDetailsExtensions.cs     # Extension methods for trace context
├── Middleware/
│   ├── ProblemDetailsMiddleware.cs     # Enhanced (or replaced)
│   └── ...
└── DependencyInjection/
    └── ErrorHandlingExtensions.cs      # AddDhadgarErrorHandling()
```

### Pattern 1: Exception Classification Taxonomy

**What:** Define a hierarchy of exception types that map to HTTP status codes

**When to use:** When throwing exceptions that should result in specific HTTP responses

**Example:**
```csharp
// Source: Adapted from Microsoft guidance on ProblemDetails
// https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api

namespace Dhadgar.ServiceDefaults.Errors;

/// <summary>
/// Base exception for domain errors that map to specific HTTP status codes.
/// </summary>
public abstract class DomainException : Exception
{
    public abstract int StatusCode { get; }
    public abstract string ErrorType { get; }

    protected DomainException(string message) : base(message) { }
    protected DomainException(string message, Exception inner) : base(message, inner) { }
}

public class ValidationException : DomainException
{
    public override int StatusCode => StatusCodes.Status400BadRequest;
    public override string ErrorType => "https://meridian.console/errors/validation";
    public IDictionary<string, string[]>? Errors { get; }

    public ValidationException(string message) : base(message) { }
    public ValidationException(string message, IDictionary<string, string[]> errors)
        : base(message) => Errors = errors;
}

public class NotFoundException : DomainException
{
    public override int StatusCode => StatusCodes.Status404NotFound;
    public override string ErrorType => "https://meridian.console/errors/not-found";
    public string? ResourceType { get; }
    public string? ResourceId { get; }

    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string resourceType, string resourceId)
        : base($"{resourceType} with ID '{resourceId}' was not found.")
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
    }
}

public class ConflictException : DomainException
{
    public override int StatusCode => StatusCodes.Status409Conflict;
    public override string ErrorType => "https://meridian.console/errors/conflict";

    public ConflictException(string message) : base(message) { }
}

public class UnauthorizedException : DomainException
{
    public override int StatusCode => StatusCodes.Status401Unauthorized;
    public override string ErrorType => "https://meridian.console/errors/unauthorized";

    public UnauthorizedException(string message = "Authentication required")
        : base(message) { }
}

public class ForbiddenException : DomainException
{
    public override int StatusCode => StatusCodes.Status403Forbidden;
    public override string ErrorType => "https://meridian.console/errors/forbidden";

    public ForbiddenException(string message = "Access denied") : base(message) { }
}
```

### Pattern 2: IExceptionHandler Implementation

**What:** ASP.NET Core 8+ pattern for exception-to-response mapping

**When to use:** When handling exceptions globally with access to DI services

**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling

namespace Dhadgar.ServiceDefaults.Errors;

internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _environment = environment;
        _timeProvider = timeProvider;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Get trace context using established fallback chain
        var traceId = Activity.Current?.TraceId.ToString()
            ?? httpContext.Items["CorrelationId"]?.ToString()
            ?? httpContext.TraceIdentifier
            ?? "unknown";

        var correlationId = httpContext.Items["CorrelationId"]?.ToString()
            ?? httpContext.TraceIdentifier
            ?? "unknown";

        // Log with full context (server-side)
        _logger.LogError(
            exception,
            "Unhandled exception. TraceId: {TraceId}, CorrelationId: {CorrelationId}, Path: {Path}",
            traceId,
            correlationId,
            httpContext.Request.Path);

        // Classify exception
        var (statusCode, type, title) = ClassifyException(exception);

        var problemDetails = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = statusCode,
            Instance = httpContext.Request.Path,
            Detail = GetSafeDetail(exception, statusCode)
        };

        // Add trace context as extensions (RFC 9457 allows extension members)
        problemDetails.Extensions["traceId"] = traceId;
        problemDetails.Extensions["correlationId"] = correlationId;
        problemDetails.Extensions["timestamp"] = _timeProvider.GetUtcNow();

        // Include validation errors for 400
        if (exception is ValidationException validationEx && validationEx.Errors is not null)
        {
            problemDetails.Extensions["errors"] = validationEx.Errors;
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true; // Exception handled
    }

    private (int StatusCode, string Type, string Title) ClassifyException(Exception exception)
    {
        return exception switch
        {
            // Domain exceptions with explicit status codes
            DomainException domainEx => (domainEx.StatusCode, domainEx.ErrorType, GetTitle(domainEx.StatusCode)),

            // Framework/library exceptions
            ArgumentNullException => (400, "https://meridian.console/errors/bad-request", "Bad Request"),
            ArgumentException => (400, "https://meridian.console/errors/bad-request", "Bad Request"),
            InvalidOperationException => (400, "https://meridian.console/errors/bad-request", "Bad Request"),
            UnauthorizedAccessException => (401, "https://meridian.console/errors/unauthorized", "Unauthorized"),
            KeyNotFoundException => (404, "https://meridian.console/errors/not-found", "Not Found"),
            NotImplementedException => (501, "https://meridian.console/errors/not-implemented", "Not Implemented"),
            TimeoutException => (504, "https://meridian.console/errors/timeout", "Gateway Timeout"),
            OperationCanceledException => (499, "https://meridian.console/errors/cancelled", "Client Closed Request"),

            // Default: internal server error
            _ => (500, "https://meridian.console/errors/internal", "Internal Server Error")
        };
    }

    private string GetSafeDetail(Exception exception, int statusCode)
    {
        // In production, only expose details for client errors (4xx)
        // Never expose details for server errors (5xx)
        var includeDetails = _environment.IsDevelopment()
            || _environment.IsEnvironment("Testing")
            || statusCode < 500;

        if (!includeDetails)
        {
            return "An unexpected error occurred. Please contact support with the trace ID.";
        }

        return exception.Message;
    }

    private static string GetTitle(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        501 => "Not Implemented",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => "Error"
    };
}
```

### Pattern 3: Registration Extension

**What:** Clean DI registration for error handling

**When to use:** In service Program.cs files

**Example:**
```csharp
// Source: Pattern from ASP.NET Core docs
namespace Dhadgar.ServiceDefaults.DependencyInjection;

public static class ErrorHandlingExtensions
{
    public static IServiceCollection AddDhadgarErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                // Ensure trace IDs are always present
                var traceId = Activity.Current?.TraceId.ToString()
                    ?? context.HttpContext.Items["CorrelationId"]?.ToString()
                    ?? context.HttpContext.TraceIdentifier
                    ?? "unknown";

                context.ProblemDetails.Extensions["traceId"] = traceId;
                context.ProblemDetails.Extensions["correlationId"] =
                    context.HttpContext.Items["CorrelationId"]?.ToString() ?? traceId;
            };
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }

    public static IApplicationBuilder UseDhadgarErrorHandling(this IApplicationBuilder app)
    {
        // UseExceptionHandler must come early in the pipeline
        app.UseExceptionHandler();

        // StatusCodePages handles non-exception errors (404 from routing, etc.)
        app.UseStatusCodePages();

        return app;
    }
}
```

### Anti-Patterns to Avoid

- **Exposing stack traces in production:** Never include exception.StackTrace for 5xx errors in production. The existing middleware checks `IsDevelopment()` - maintain this pattern.

- **Using generic `{ error = "..." }` responses:** Many endpoints currently return `Results.BadRequest(new { error = "..." })`. These should use `Results.Problem()` or `Results.ValidationProblem()` for RFC 9457 compliance.

- **Catching and re-throwing exceptions incorrectly:** Don't `throw ex;` which loses stack trace. Use `throw;` to preserve it.

- **Leaking internal paths:** Exception messages may contain file paths like `at /app/src/Service.cs:line 42`. These should be scrubbed in production.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Exception-to-status mapping | Custom switch statement per service | Centralized `GlobalExceptionHandler` | Consistency across 13+ services |
| Validation error format | Custom error objects | `Results.ValidationProblem()` | Built-in RFC 9457 compliant validation format |
| Problem Details serialization | Manual JSON construction | `ProblemDetails` class | Type safety, correct property casing |
| Request validation | Manual checks in endpoints | FluentValidation + filter | Declarative, testable, reusable |

**Key insight:** ASP.NET Core 10 has excellent built-in Problem Details support. The custom middleware was necessary before but can now delegate to framework services while adding trace context.

## Common Pitfalls

### Pitfall 1: Inconsistent Error Response Formats

**What goes wrong:** Some endpoints return `{ error: "..." }`, others return `{ message: "..." }`, others use Problem Details. API consumers can't reliably parse errors.

**Why it happens:** Endpoints were implemented incrementally without a shared error contract.

**How to avoid:**
1. Define `Results.Problem()` or `Results.ValidationProblem()` as the ONLY error return patterns
2. Search codebase for `Results.BadRequest(new {`, `Results.NotFound(new {` and refactor

**Warning signs:** Grep for `new { error =` in endpoint files - these need migration.

### Pitfall 2: Sensitive Data in Exception Messages

**What goes wrong:** Exceptions contain connection strings, internal paths, or user data that gets exposed in responses.

**Why it happens:** Framework exceptions (EF Core, HTTP clients) include detailed messages.

**How to avoid:**
1. Always use `GetSafeDetail()` pattern that filters 5xx error messages
2. For 4xx, validate that DomainException messages are safe by design
3. Never include `exception.ToString()` in responses (includes stack trace)

**Warning signs:** Search for `exception.Message` or `ex.Message` in response-building code.

### Pitfall 3: Missing Trace Context in Error Responses

**What goes wrong:** Error responses don't include traceId/correlationId, making it impossible to correlate with logs.

**Why it happens:** Quick-fix error handling bypasses the middleware.

**How to avoid:**
1. Route ALL errors through the global handler
2. Use `Results.Problem()` with customization that adds trace IDs
3. Never return errors directly without trace context

**Warning signs:** Error responses without `traceId` field.

### Pitfall 4: Validation Exceptions Not Using ValidationProblemDetails

**What goes wrong:** Validation errors return generic Problem Details instead of the richer `ValidationProblemDetails` with field-level errors.

**Why it happens:** Using `ProblemDetails` class instead of `ValidationProblemDetails` for 400 errors.

**How to avoid:**
1. Use `Results.ValidationProblem(errors)` for validation failures
2. `ValidationException` should carry `IDictionary<string, string[]>` of field errors
3. FluentValidation returns this format natively

**Warning signs:** 400 responses without `errors` field in the response body.

## Code Examples

### Example 1: Service Program.cs Integration

```csharp
// Source: Pattern from existing ServiceDefaults usage
var builder = WebApplication.CreateBuilder(args);

// Add standard services
builder.Services.AddDhadgarServiceDefaults();
builder.Services.AddDhadgarErrorHandling(); // NEW: Error handling registration

var app = builder.Build();

// Middleware pipeline order matters!
app.UseMiddleware<CorrelationMiddleware>();        // 1. Set correlation IDs
app.UseMiddleware<TenantEnrichmentMiddleware>();   // 2. Add tenant context
app.UseDhadgarErrorHandling();                     // 3. Exception handler (early!)
app.UseMiddleware<RequestLoggingMiddleware>();     // 4. Log requests
app.UseAuthentication();
app.UseAuthorization();
// ... rest of pipeline
```

### Example 2: Throwing Domain Exceptions

```csharp
// Source: Adapted from Microsoft guidance
public async Task<Server> GetServerAsync(Guid id, CancellationToken ct)
{
    var server = await _dbContext.Servers.FindAsync([id], ct);

    if (server is null)
    {
        throw new NotFoundException("Server", id.ToString());
    }

    return server;
}

public async Task CreateServerAsync(CreateServerRequest request, CancellationToken ct)
{
    if (await _dbContext.Servers.AnyAsync(s => s.Name == request.Name, ct))
    {
        throw new ConflictException($"Server with name '{request.Name}' already exists.");
    }

    // ... create server
}
```

### Example 3: Manual Results.Problem() Usage

```csharp
// Source: ASP.NET Core Minimal APIs documentation
app.MapPost("/api/v1/secrets/{secretName}", async (
    string secretName,
    [FromBody] SetSecretRequest request,
    ISecretProvider provider,
    HttpContext context) =>
{
    var validation = SecretNameValidator.Validate(secretName);
    if (!validation.IsValid)
    {
        return Results.Problem(
            detail: validation.ErrorMessage,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation Error",
            type: "https://meridian.console/errors/validation",
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
                ["correlationId"] = context.Items["CorrelationId"]?.ToString()
            });
    }

    // ... rest of handler
});
```

### Example 4: FluentValidation Integration (Optional)

```csharp
// Source: FluentValidation documentation
// https://docs.fluentvalidation.net/en/latest/aspnet.html

public class CreateServerRequestValidator : AbstractValidator<CreateServerRequest>
{
    public CreateServerRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .Matches("^[a-zA-Z0-9-]+$")
            .WithMessage("Name must contain only alphanumeric characters and dashes");

        RuleFor(x => x.NodeId)
            .NotEmpty();
    }
}

// In endpoint:
app.MapPost("/api/v1/servers", async (
    CreateServerRequest request,
    IValidator<CreateServerRequest> validator,
    CancellationToken ct) =>
{
    var result = await validator.ValidateAsync(request, ct);
    if (!result.IsValid)
    {
        return Results.ValidationProblem(result.ToDictionary());
    }

    // ... rest of handler
});
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Custom exception middleware | IExceptionHandler interface | ASP.NET Core 8 (Nov 2023) | Cleaner separation, DI access, chaining |
| RFC 7807 | RFC 9457 | July 2023 | Registry for type URIs, clearer guidance |
| Minimal ProblemDetails | AddProblemDetails() + CustomizeProblemDetails | ASP.NET Core 7+ | Built-in customization hook |

**Deprecated/outdated:**
- **UseExceptionHandler with lambda only:** Can still use, but IExceptionHandler is preferred
- **Manual JSON serialization:** Use `ProblemDetails` class and built-in serialization

## RFC 9457 vs RFC 7807

RFC 9457 (July 2023) obsoletes RFC 7807 with these key changes:

1. **Type URI Registry:** IANA maintains a registry of common problem types
2. **Multiple Problems Guidance:** Better handling of HTTP 207 Multi-Status
3. **Extension Member Naming:** Clearer rules for custom fields

**Practical impact for this phase:** Minimal - the existing `ProblemDetails` class is RFC 9457 compliant. Key fields remain the same:
- `type` (URI, defaults to "about:blank")
- `title` (short description)
- `status` (HTTP status code)
- `detail` (specific occurrence explanation)
- `instance` (URI for this occurrence)
- Extensions via `Extensions` dictionary

**Content-Type:** Must be `application/problem+json` (already correct in existing middleware).

## Existing Codebase Analysis

### Current Error Response Patterns (Need Migration)

Based on grep results, these patterns exist and need migration to Problem Details:

1. **`Results.BadRequest(new { error = "..." })`** - 60+ occurrences across Identity, Secrets endpoints
2. **`Results.NotFound(new { error = "..." })`** - 30+ occurrences
3. **Custom `{ error = "..." }` anonymous objects** - Inconsistent with RFC 9457

### Files Requiring Changes

| File | Current Pattern | Needed Change |
|------|-----------------|---------------|
| `src/Dhadgar.Identity/Endpoints/*.cs` | `Results.BadRequest(new { error = })` | Use `Results.Problem()` |
| `src/Dhadgar.Secrets/Endpoints/*.cs` | `Results.BadRequest(new { error = })` | Use `Results.Problem()` |
| `src/Shared/Dhadgar.ServiceDefaults/Middleware/ProblemDetailsMiddleware.cs` | Anonymous type, no classification | Refactor to use `IExceptionHandler` or enhance |

## Open Questions

Things that couldn't be fully resolved:

1. **Migrate existing middleware vs create new IExceptionHandler?**
   - What we know: Both approaches work; IExceptionHandler is the modern pattern
   - What's unclear: Whether to keep backward compatibility with existing middleware
   - Recommendation: Create IExceptionHandler, keep middleware as fallback initially

2. **FluentValidation scope**
   - What we know: FluentValidation is recommended for complex DTOs
   - What's unclear: Whether to add it in this phase or defer
   - Recommendation: Defer FluentValidation to a separate task; focus on core error handling first

3. **Error type URI domain**
   - What we know: RFC 9457 recommends absolute URIs
   - What's unclear: Should use `https://meridian.console/errors/...` or `https://httpstatuses.com/...`
   - Recommendation: Use custom domain for domain errors, httpstatuses.com for standard HTTP errors

## Sources

### Primary (HIGH confidence)
- [RFC 9457 Specification](https://www.rfc-editor.org/rfc/rfc9457.html) - Official RFC for Problem Details
- [Microsoft Learn - Handle errors in ASP.NET Core APIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0) - Official documentation
- [Microsoft Learn - Error Handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0) - UseExceptionHandler patterns

### Secondary (MEDIUM confidence)
- [Milan Jovanovic - Global Error Handling in ASP.NET Core 8](https://www.milanjovanovic.tech/blog/global-error-handling-in-aspnetcore-8) - IExceptionHandler implementation patterns
- [Milan Jovanovic - Problem Details for ASP.NET Core APIs](https://www.milanjovanovic.tech/blog/problem-details-for-aspnetcore-apis) - Customization patterns
- [Redocly - RFC 9457 Better information for bad situations](https://redocly.com/blog/problem-details-9457) - RFC 7807 vs 9457 comparison
- [Codecentric - Understanding Problem Details](https://www.codecentric.de/en/knowledge-hub/blog/charge-your-apis-volume-19-understanding-problem-details-for-http-apis-a-deep-dive-into-rfc-7807-and-rfc-9457) - Deep dive comparison

### Tertiary (LOW confidence)
- [GitHub Issue #52414 - Support Latest ProblemDetails RFC](https://github.com/dotnet/aspnetcore/issues/52414) - ASP.NET Core RFC 9457 tracking

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Built-in ASP.NET Core, verified in official docs
- Architecture: HIGH - Patterns from Microsoft documentation and verified community sources
- Pitfalls: HIGH - Based on existing codebase analysis and common patterns

**Research date:** 2026-01-22
**Valid until:** 90 days (stable patterns, RFC 9457 is finalized)
