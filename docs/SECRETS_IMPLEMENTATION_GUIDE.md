# Secrets Service Implementation Guide

**Companion to**: SECRETS_SERVICE_ANALYSIS.md
**Purpose**: Code examples and implementation patterns for production-ready Secrets service
**Based on**: Context7 research, Microsoft Learn documentation, and expert agent guidance

---

## Table of Contents

1. [Authorization Infrastructure](#1-authorization-infrastructure)
2. [Tenant Isolation Implementation](#2-tenant-isolation-implementation)
3. [Security Audit Logging](#3-security-audit-logging)
4. [Identity Service Integration](#4-identity-service-integration)
5. [Service-to-Service Authentication](#5-service-to-service-authentication)
6. [Rate Limiting](#6-rate-limiting)
7. [Input Validation](#7-input-validation)
8. [CLI Enhancements](#8-cli-enhancements)
9. [Test Examples](#9-test-examples)

---

## 1. Authorization Infrastructure

### 1.1 Custom Authorization Requirements

Create reusable requirements for secrets access control:

```csharp
// File: src/Dhadgar.Secrets/Authorization/Requirements/TenantSecretRequirement.cs
using Microsoft.AspNetCore.Authorization;

namespace Dhadgar.Secrets.Authorization.Requirements;

/// <summary>
/// Requirement that validates tenant ownership and permission for secret access.
/// </summary>
public sealed class TenantSecretRequirement : IAuthorizationRequirement
{
    public TenantSecretRequirement(SecretAction action, string? category = null)
    {
        Action = action;
        Category = category;
    }

    public SecretAction Action { get; }
    public string? Category { get; }
}

public enum SecretAction
{
    Read,
    Write,
    Rotate,
    Delete,
    List,
    Audit
}

/// <summary>
/// Resource representing a secret for authorization purposes.
/// </summary>
public sealed record SecretResource(
    string SecretName,
    string? TenantId,
    string Category,
    bool IsPlatformSecret);
```

### 1.2 Authorization Handler Implementation

```csharp
// File: src/Dhadgar.Secrets/Authorization/Handlers/TenantSecretAuthorizationHandler.cs
using System.Security.Claims;
using Dhadgar.Secrets.Authorization.Requirements;
using Dhadgar.Secrets.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Dhadgar.Secrets.Authorization.Handlers;

/// <summary>
/// Handler that enforces tenant isolation for secret access.
/// Ensures users can only access secrets within their tenant context.
/// </summary>
public sealed class TenantSecretAuthorizationHandler
    : AuthorizationHandler<TenantSecretRequirement, SecretResource>
{
    private readonly ILogger<TenantSecretAuthorizationHandler> _logger;
    private readonly SecretsOptions _options;

    public TenantSecretAuthorizationHandler(
        ILogger<TenantSecretAuthorizationHandler> logger,
        IOptions<SecretsOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantSecretRequirement requirement,
        SecretResource resource)
    {
        // Always check authentication first
        if (context.User.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning("Unauthenticated access attempt to secret {SecretName}",
                resource.SecretName);
            return Task.CompletedTask;
        }

        var userId = context.User.FindFirstValue("sub");
        var userTenantId = context.User.FindFirstValue("org_id");
        var principalType = context.User.FindFirstValue("principal_type") ?? "user";
        var isBreakGlass = context.User.HasClaim("break_glass", "true");

        // Break-glass access bypasses normal checks but logs everything
        if (isBreakGlass)
        {
            _logger.LogWarning(
                "Break-glass access to secret {SecretName} by user {UserId}",
                resource.SecretName, userId);
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Platform secrets require platform-level permissions (no :tenant suffix)
        if (resource.IsPlatformSecret)
        {
            var platformPermission = BuildPermission(requirement.Action, resource.Category, false);
            if (HasPermission(context.User, platformPermission))
            {
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning(
                    "Denied platform secret access: User {UserId} lacks {Permission}",
                    userId, platformPermission);
            }
            return Task.CompletedTask;
        }

        // Tenant secrets require tenant match AND tenant-scoped permission
        if (string.IsNullOrWhiteSpace(userTenantId))
        {
            _logger.LogWarning("No tenant ID claim found for user {UserId}", userId);
            return Task.CompletedTask;
        }

        // Verify the secret belongs to the user's tenant
        if (!string.Equals(resource.TenantId, userTenantId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Cross-tenant access attempt: User {UserId} (tenant {UserTenant}) tried to access secret in tenant {SecretTenant}",
                userId, userTenantId, resource.TenantId);
            context.Fail(); // Explicitly fail to prevent other handlers from succeeding
            return Task.CompletedTask;
        }

        // Check tenant-scoped permission
        var tenantPermission = BuildPermission(requirement.Action, resource.Category, true);
        if (HasPermission(context.User, tenantPermission))
        {
            context.Succeed(requirement);
        }
        else
        {
            // Check for wildcard permission
            var wildcardPermission = BuildPermission(requirement.Action, "*", true);
            if (HasPermission(context.User, wildcardPermission))
            {
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogDebug(
                    "User {UserId} lacks permission {Permission} for secret {SecretName}",
                    userId, tenantPermission, resource.SecretName);
            }
        }

        return Task.CompletedTask;
    }

    private static string BuildPermission(SecretAction action, string? category, bool tenantScoped)
    {
        var actionStr = action.ToString().ToLowerInvariant();
        var categoryStr = string.IsNullOrWhiteSpace(category) ? "*" : category;
        var suffix = tenantScoped ? ":tenant" : "";

        return $"secrets:{actionStr}:{categoryStr}{suffix}";
    }

    private static bool HasPermission(ClaimsPrincipal user, string permission)
    {
        return user.Claims.Any(c =>
            string.Equals(c.Type, "permission", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(c.Value, permission, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(c.Value, "secrets:*", StringComparison.OrdinalIgnoreCase)));
    }
}
```

### 1.3 Service Account Handler (OR Logic)

```csharp
// File: src/Dhadgar.Secrets/Authorization/Handlers/ServiceAccountSecretHandler.cs
using Dhadgar.Secrets.Authorization.Requirements;
using Microsoft.AspNetCore.Authorization;

namespace Dhadgar.Secrets.Authorization.Handlers;

/// <summary>
/// Handler that allows service accounts to access secrets.
/// Works in conjunction with TenantSecretAuthorizationHandler (OR logic).
/// </summary>
public sealed class ServiceAccountSecretHandler
    : AuthorizationHandler<TenantSecretRequirement, SecretResource>
{
    private readonly ILogger<ServiceAccountSecretHandler> _logger;
    private readonly IConfiguration _configuration;

    public ServiceAccountSecretHandler(
        ILogger<ServiceAccountSecretHandler> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantSecretRequirement requirement,
        SecretResource resource)
    {
        // Check if this is a service account
        var principalType = context.User.FindFirstValue("principal_type");
        if (principalType != "service")
        {
            return Task.CompletedTask; // Not a service account, let other handlers try
        }

        var serviceName = context.User.FindFirstValue("service_name");
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return Task.CompletedTask;
        }

        // Validate against allowed service configurations
        var allowedServices = _configuration
            .GetSection("Auth:AllowedServiceAccounts")
            .Get<Dictionary<string, string[]>>() ?? new();

        if (!allowedServices.TryGetValue(serviceName, out var allowedCategories))
        {
            return Task.CompletedTask;
        }

        // Check if service is allowed to access this category
        var isAllowed = allowedCategories.Contains("*") ||
                       allowedCategories.Contains(resource.Category, StringComparer.OrdinalIgnoreCase);

        if (isAllowed)
        {
            _logger.LogInformation(
                "Service account {ServiceName} granted {Action} access to secret {SecretName}",
                serviceName, requirement.Action, resource.SecretName);
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
```

### 1.4 Policy Registration

```csharp
// File: src/Dhadgar.Secrets/Program.cs (authorization section)

// Add authorization handlers
builder.Services.AddSingleton<IAuthorizationHandler, TenantSecretAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, ServiceAccountSecretHandler>();

// Configure policies
builder.Services.AddAuthorization(options =>
{
    // Tenant-scoped secret access policies
    options.AddPolicy("SecretsRead", policy =>
        policy.Requirements.Add(new TenantSecretRequirement(SecretAction.Read)));

    options.AddPolicy("SecretsWrite", policy =>
        policy.Requirements.Add(new TenantSecretRequirement(SecretAction.Write)));

    options.AddPolicy("SecretsRotate", policy =>
        policy.Requirements.Add(new TenantSecretRequirement(SecretAction.Rotate)));

    options.AddPolicy("SecretsDelete", policy =>
        policy.Requirements.Add(new TenantSecretRequirement(SecretAction.Delete)));

    // Category-specific policies
    options.AddPolicy("OAuthSecretsRead", policy =>
        policy.Requirements.Add(new TenantSecretRequirement(SecretAction.Read, "oauth")));

    options.AddPolicy("InfrastructureSecretsRead", policy =>
        policy.Requirements.Add(new TenantSecretRequirement(SecretAction.Read, "infrastructure")));

    // Audit policy (requires specific permission)
    options.AddPolicy("SecretsAudit", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("permission", "secrets:audit:read");
    });
});
```

---

## 2. Tenant Isolation Implementation

### 2.1 Tenant Context Provider

```csharp
// File: src/Dhadgar.Secrets/Services/TenantContextProvider.cs
namespace Dhadgar.Secrets.Services;

/// <summary>
/// Provides tenant context from JWT claims.
/// </summary>
public interface ITenantContextProvider
{
    string? TenantId { get; }
    string? TenantSlug { get; }
    bool IsPlatformContext { get; }
}

public sealed class TenantContextProvider : ITenantContextProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContextProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? TenantId =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue("org_id");

    public string? TenantSlug =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue("org_slug");

    public bool IsPlatformContext
    {
        get
        {
            var principalType = _httpContextAccessor.HttpContext?.User.FindFirstValue("principal_type");
            return principalType == "service" && TenantId == null;
        }
    }
}
```

### 2.2 Secret Name Resolver

```csharp
// File: src/Dhadgar.Secrets/Services/SecretNameResolver.cs
using Dhadgar.Secrets.Options;
using Microsoft.Extensions.Options;

namespace Dhadgar.Secrets.Services;

/// <summary>
/// Resolves secret names with tenant prefixes and determines categories.
/// </summary>
public interface ISecretNameResolver
{
    /// <summary>
    /// Resolves the actual Key Vault secret name from user input.
    /// </summary>
    SecretResolution Resolve(string secretName, string? tenantSlug = null);

    /// <summary>
    /// Determines if a secret is a platform secret or tenant-scoped.
    /// </summary>
    bool IsPlatformSecret(string secretName);

    /// <summary>
    /// Gets the category of a secret (oauth, betterauth, infrastructure, etc.)
    /// </summary>
    string GetCategory(string secretName);
}

public sealed record SecretResolution(
    string OriginalName,
    string KeyVaultName,
    string Category,
    bool IsPlatformSecret,
    string? TenantSlug);

public sealed class SecretNameResolver : ISecretNameResolver
{
    private readonly SecretsOptions _options;

    public SecretNameResolver(IOptions<SecretsOptions> options)
    {
        _options = options.Value;
    }

    public SecretResolution Resolve(string secretName, string? tenantSlug = null)
    {
        // Check if this is a platform secret
        if (IsPlatformSecret(secretName))
        {
            return new SecretResolution(
                OriginalName: secretName,
                KeyVaultName: secretName,
                Category: GetCategory(secretName),
                IsPlatformSecret: true,
                TenantSlug: null);
        }

        // Tenant-scoped secret - apply prefix
        if (string.IsNullOrWhiteSpace(tenantSlug))
        {
            throw new InvalidOperationException(
                $"Tenant slug required for non-platform secret: {secretName}");
        }

        // Azure Key Vault naming: alphanumeric and dashes only, max 127 chars
        var keyVaultName = $"{tenantSlug}--{secretName}"
            .ToLowerInvariant()
            .Replace("_", "-");

        return new SecretResolution(
            OriginalName: secretName,
            KeyVaultName: keyVaultName,
            Category: GetCategory(secretName),
            IsPlatformSecret: false,
            TenantSlug: tenantSlug);
    }

    public bool IsPlatformSecret(string secretName)
    {
        return _options.PlatformSecrets?.Contains(secretName, StringComparer.OrdinalIgnoreCase) == true ||
               _options.AllowedSecrets.Infrastructure.Contains(secretName, StringComparer.OrdinalIgnoreCase) ||
               _options.AllowedSecrets.BetterAuth.Contains(secretName, StringComparer.OrdinalIgnoreCase);
    }

    public string GetCategory(string secretName)
    {
        var lowerName = secretName.ToLowerInvariant();

        if (_options.AllowedSecrets.OAuth.Any(s =>
            s.Equals(secretName, StringComparison.OrdinalIgnoreCase)))
            return "oauth";

        if (_options.AllowedSecrets.BetterAuth.Any(s =>
            s.Equals(secretName, StringComparison.OrdinalIgnoreCase)))
            return "betterauth";

        if (_options.AllowedSecrets.Infrastructure.Any(s =>
            s.Equals(secretName, StringComparison.OrdinalIgnoreCase)))
            return "infrastructure";

        // Infer from prefix patterns
        if (lowerName.StartsWith("oauth-")) return "oauth";
        if (lowerName.StartsWith("discord-") || lowerName.StartsWith("github-") ||
            lowerName.StartsWith("google-") || lowerName.StartsWith("twitch-"))
            return "oauth";

        return "custom";
    }
}
```

### 2.3 Updated Endpoints with Resource-Based Authorization

```csharp
// File: src/Dhadgar.Secrets/Endpoints/SecretsEndpoints.cs (updated)
using Dhadgar.Secrets.Authorization.Requirements;
using Dhadgar.Secrets.Services;
using Microsoft.AspNetCore.Authorization;

namespace Dhadgar.Secrets.Endpoints;

public static class SecretsEndpoints
{
    public static void MapSecretsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/secrets")
            .WithTags("Secrets")
            .RequireAuthorization();

        group.MapGet("/{secretName}", GetSecret)
            .WithName("GetSecret")
            .Produces<SecretResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/batch", GetSecretsBatch)
            .WithName("GetSecretsBatch")
            .Produces<SecretsResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> GetSecret(
        string secretName,
        [FromServices] IAuthorizationService authorizationService,
        [FromServices] ISecretProvider provider,
        [FromServices] ISecretNameResolver nameResolver,
        [FromServices] ITenantContextProvider tenantContext,
        [FromServices] ISecretsAuditLogger auditLogger,
        HttpContext context,
        CancellationToken ct)
    {
        // Resolve the secret name with tenant context
        var resolution = nameResolver.Resolve(secretName, tenantContext.TenantSlug);

        // Create the resource for authorization
        var resource = new SecretResource(
            SecretName: secretName,
            TenantId: tenantContext.TenantId,
            Category: resolution.Category,
            IsPlatformSecret: resolution.IsPlatformSecret);

        // Perform resource-based authorization
        var requirement = new TenantSecretRequirement(SecretAction.Read, resolution.Category);
        var authResult = await authorizationService.AuthorizeAsync(
            context.User, resource, requirement);

        if (!authResult.Succeeded)
        {
            await auditLogger.LogAccessDeniedAsync(new SecretAccessDeniedEvent(
                SecretName: secretName,
                Action: "read",
                UserId: context.User.FindFirstValue("sub"),
                TenantId: tenantContext.TenantId,
                Reason: "Authorization failed",
                CorrelationId: context.TraceIdentifier), ct);

            return Results.Forbid();
        }

        // Fetch the secret using the resolved Key Vault name
        var value = await provider.GetSecretAsync(resolution.KeyVaultName, ct);

        if (value is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "Secret not found",
                Detail = $"Secret '{secretName}' does not exist.",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Log successful access
        await auditLogger.LogAccessAsync(new SecretAccessEvent(
            SecretName: secretName,
            Action: "read",
            UserId: context.User.FindFirstValue("sub"),
            TenantId: tenantContext.TenantId,
            Success: true,
            CorrelationId: context.TraceIdentifier), ct);

        return Results.Ok(new SecretResponse(secretName, value));
    }

    private static async Task<IResult> GetSecretsBatch(
        [FromBody] BatchSecretsRequest request,
        [FromServices] IAuthorizationService authorizationService,
        [FromServices] ISecretProvider provider,
        [FromServices] ISecretNameResolver nameResolver,
        [FromServices] ITenantContextProvider tenantContext,
        [FromServices] ISecretsAuditLogger auditLogger,
        HttpContext context,
        CancellationToken ct)
    {
        var results = new Dictionary<string, string>();
        var deniedSecrets = new List<string>();

        foreach (var secretName in request.SecretNames)
        {
            var resolution = nameResolver.Resolve(secretName, tenantContext.TenantSlug);

            var resource = new SecretResource(
                SecretName: secretName,
                TenantId: tenantContext.TenantId,
                Category: resolution.Category,
                IsPlatformSecret: resolution.IsPlatformSecret);

            var requirement = new TenantSecretRequirement(SecretAction.Read, resolution.Category);
            var authResult = await authorizationService.AuthorizeAsync(
                context.User, resource, requirement);

            if (!authResult.Succeeded)
            {
                deniedSecrets.Add(secretName);
                continue;
            }

            var value = await provider.GetSecretAsync(resolution.KeyVaultName, ct);
            if (value is not null)
            {
                results[secretName] = value;
            }
        }

        // Log batch access
        await auditLogger.LogBatchAccessAsync(new SecretBatchAccessEvent(
            SecretNames: request.SecretNames,
            AccessedCount: results.Count,
            DeniedCount: deniedSecrets.Count,
            UserId: context.User.FindFirstValue("sub"),
            TenantId: tenantContext.TenantId,
            CorrelationId: context.TraceIdentifier), ct);

        return Results.Ok(new SecretsResponse(results));
    }
}
```

---

## 3. Security Audit Logging

### 3.1 Audit Logger Interface

```csharp
// File: src/Dhadgar.Secrets/Audit/ISecretsAuditLogger.cs
namespace Dhadgar.Secrets.Audit;

public interface ISecretsAuditLogger
{
    Task LogAccessAsync(SecretAccessEvent evt, CancellationToken ct = default);
    Task LogAccessDeniedAsync(SecretAccessDeniedEvent evt, CancellationToken ct = default);
    Task LogModificationAsync(SecretModificationEvent evt, CancellationToken ct = default);
    Task LogBatchAccessAsync(SecretBatchAccessEvent evt, CancellationToken ct = default);
    Task LogRotationAsync(SecretRotationEvent evt, CancellationToken ct = default);
}

public sealed record SecretAccessEvent(
    string SecretName,
    string Action,
    string? UserId,
    string? TenantId,
    bool Success,
    string? CorrelationId,
    string? ServiceAccountName = null,
    bool IsBreakGlass = false,
    bool IsDelegated = false,
    string? DelegatedUserId = null);

public sealed record SecretAccessDeniedEvent(
    string SecretName,
    string Action,
    string? UserId,
    string? TenantId,
    string Reason,
    string? CorrelationId);

public sealed record SecretModificationEvent(
    string SecretName,
    string Action, // "create", "update", "delete"
    string? UserId,
    string? TenantId,
    bool Success,
    string? CorrelationId,
    string? ErrorMessage = null);

public sealed record SecretBatchAccessEvent(
    IEnumerable<string> SecretNames,
    int AccessedCount,
    int DeniedCount,
    string? UserId,
    string? TenantId,
    string? CorrelationId);

public sealed record SecretRotationEvent(
    string SecretName,
    string? UserId,
    string? TenantId,
    string NewVersion,
    string? CorrelationId,
    bool Success,
    string? ErrorMessage = null);
```

### 3.2 Audit Logger Implementation with OpenTelemetry

```csharp
// File: src/Dhadgar.Secrets/Audit/SecretsAuditLogger.cs
using System.Diagnostics;

namespace Dhadgar.Secrets.Audit;

public sealed class SecretsAuditLogger : ISecretsAuditLogger
{
    private readonly ILogger<SecretsAuditLogger> _logger;
    private readonly ActivitySource _activitySource;

    public SecretsAuditLogger(
        ILogger<SecretsAuditLogger> logger,
        SecretsInstrumentation instrumentation)
    {
        _logger = logger;
        _activitySource = instrumentation.ActivitySource;
    }

    public Task LogAccessAsync(SecretAccessEvent evt, CancellationToken ct = default)
    {
        using var activity = _activitySource.StartActivity("secret.access.audit");
        activity?.SetTag("secret.name", evt.SecretName);
        activity?.SetTag("secret.action", evt.Action);
        activity?.SetTag("user.id", evt.UserId);
        activity?.SetTag("tenant.id", evt.TenantId);
        activity?.SetTag("success", evt.Success);
        activity?.SetTag("correlation.id", evt.CorrelationId);

        if (evt.IsBreakGlass)
        {
            activity?.SetTag("break_glass", true);
            _logger.LogWarning(
                "BREAK-GLASS Secret Access: Action={Action} Secret={SecretName} User={UserId} Tenant={TenantId} CorrelationId={CorrelationId}",
                evt.Action, evt.SecretName, evt.UserId, evt.TenantId, evt.CorrelationId);
        }
        else if (evt.IsDelegated)
        {
            _logger.LogInformation(
                "Delegated Secret Access: Action={Action} Secret={SecretName} Service={ServiceAccount} OnBehalfOf={DelegatedUser} Tenant={TenantId} CorrelationId={CorrelationId}",
                evt.Action, evt.SecretName, evt.ServiceAccountName, evt.DelegatedUserId, evt.TenantId, evt.CorrelationId);
        }
        else
        {
            _logger.LogInformation(
                "Secret Access: Action={Action} Secret={SecretName} User={UserId} Tenant={TenantId} Success={Success} CorrelationId={CorrelationId}",
                evt.Action, evt.SecretName, evt.UserId, evt.TenantId, evt.Success, evt.CorrelationId);
        }

        return Task.CompletedTask;
    }

    public Task LogAccessDeniedAsync(SecretAccessDeniedEvent evt, CancellationToken ct = default)
    {
        using var activity = _activitySource.StartActivity("secret.access.denied");
        activity?.SetTag("secret.name", evt.SecretName);
        activity?.SetTag("user.id", evt.UserId);
        activity?.SetTag("tenant.id", evt.TenantId);
        activity?.SetTag("denial.reason", evt.Reason);
        activity?.SetStatus(ActivityStatusCode.Error, evt.Reason);

        _logger.LogWarning(
            "Secret Access DENIED: Action={Action} Secret={SecretName} User={UserId} Tenant={TenantId} Reason={Reason} CorrelationId={CorrelationId}",
            evt.Action, evt.SecretName, evt.UserId, evt.TenantId, evt.Reason, evt.CorrelationId);

        return Task.CompletedTask;
    }

    public Task LogModificationAsync(SecretModificationEvent evt, CancellationToken ct = default)
    {
        using var activity = _activitySource.StartActivity("secret.modification.audit");
        activity?.SetTag("secret.name", evt.SecretName);
        activity?.SetTag("secret.action", evt.Action);
        activity?.SetTag("user.id", evt.UserId);
        activity?.SetTag("tenant.id", evt.TenantId);
        activity?.SetTag("success", evt.Success);

        if (evt.Success)
        {
            _logger.LogInformation(
                "Secret Modified: Action={Action} Secret={SecretName} User={UserId} Tenant={TenantId} CorrelationId={CorrelationId}",
                evt.Action, evt.SecretName, evt.UserId, evt.TenantId, evt.CorrelationId);
        }
        else
        {
            _logger.LogError(
                "Secret Modification FAILED: Action={Action} Secret={SecretName} User={UserId} Tenant={TenantId} Error={Error} CorrelationId={CorrelationId}",
                evt.Action, evt.SecretName, evt.UserId, evt.TenantId, evt.ErrorMessage, evt.CorrelationId);
        }

        return Task.CompletedTask;
    }

    public Task LogBatchAccessAsync(SecretBatchAccessEvent evt, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Batch Secret Access: Requested={RequestedCount} Accessed={AccessedCount} Denied={DeniedCount} User={UserId} Tenant={TenantId} CorrelationId={CorrelationId}",
            evt.SecretNames.Count(), evt.AccessedCount, evt.DeniedCount, evt.UserId, evt.TenantId, evt.CorrelationId);

        return Task.CompletedTask;
    }

    public Task LogRotationAsync(SecretRotationEvent evt, CancellationToken ct = default)
    {
        using var activity = _activitySource.StartActivity("secret.rotation.audit");
        activity?.SetTag("secret.name", evt.SecretName);
        activity?.SetTag("secret.version", evt.NewVersion);
        activity?.SetTag("user.id", evt.UserId);
        activity?.SetTag("tenant.id", evt.TenantId);
        activity?.SetTag("success", evt.Success);

        if (evt.Success)
        {
            // Rotation is a significant security event - always warn level
            _logger.LogWarning(
                "Secret ROTATED: Secret={SecretName} NewVersion={Version} User={UserId} Tenant={TenantId} CorrelationId={CorrelationId}",
                evt.SecretName, evt.NewVersion, evt.UserId, evt.TenantId, evt.CorrelationId);
        }
        else
        {
            _logger.LogError(
                "Secret Rotation FAILED: Secret={SecretName} User={UserId} Tenant={TenantId} Error={Error} CorrelationId={CorrelationId}",
                evt.SecretName, evt.UserId, evt.TenantId, evt.ErrorMessage, evt.CorrelationId);
        }

        return Task.CompletedTask;
    }
}
```

### 3.3 OpenTelemetry Instrumentation

```csharp
// File: src/Dhadgar.Secrets/Telemetry/SecretsInstrumentation.cs
using System.Diagnostics;

namespace Dhadgar.Secrets.Telemetry;

/// <summary>
/// OpenTelemetry instrumentation for secrets service.
/// Provides custom ActivitySource for security audit trails.
/// </summary>
public sealed class SecretsInstrumentation : IDisposable
{
    internal const string ActivitySourceName = "Dhadgar.Secrets";
    internal const string ActivitySourceVersion = "1.0.0";

    public SecretsInstrumentation()
    {
        ActivitySource = new ActivitySource(ActivitySourceName, ActivitySourceVersion);
    }

    public ActivitySource ActivitySource { get; }

    public void Dispose() => ActivitySource.Dispose();
}
```

---

## 4. Identity Service Integration

### 4.1 Add Secrets Permissions to Role Definitions

```csharp
// File: src/Dhadgar.Identity/Authorization/RoleDefinitions.cs (additions)

// Add to existing role definitions
public static class RoleDefinitions
{
    public static readonly IReadOnlyDictionary<string, RoleDefinition> SystemRoles = new Dictionary<string, RoleDefinition>
    {
        ["owner"] = new RoleDefinition
        {
            Name = "Owner",
            Description = "Full control over organization",
            ImpliedClaims = new[]
            {
                // ... existing claims ...
                "secrets:read:oauth:tenant",
                "secrets:write:oauth:tenant",
                "secrets:read:infrastructure:tenant",
                "secrets:rotate:*:tenant",
                "certificates:read",
                "certificates:write"
            }
        },
        ["admin"] = new RoleDefinition
        {
            Name = "Admin",
            Description = "Administrative access",
            ImpliedClaims = new[]
            {
                // ... existing claims ...
                "secrets:read:oauth:tenant",
                "secrets:read:infrastructure:tenant",
                "certificates:read"
            }
        },
        ["operator"] = new RoleDefinition
        {
            Name = "Operator",
            Description = "Day-to-day operations",
            ImpliedClaims = new[]
            {
                // ... existing claims ...
                "secrets:read:oauth:tenant"
            }
        },
        ["viewer"] = new RoleDefinition
        {
            Name = "Viewer",
            Description = "Read-only access",
            ImpliedClaims = new[]
            {
                // ... existing claims ...
                // No secrets access for viewers
            }
        },

        // New secrets-specific roles
        ["secrets-admin"] = new RoleDefinition
        {
            Name = "Secrets Administrator",
            Description = "Full control over secrets within tenant",
            ImpliedClaims = new[]
            {
                "secrets:read:*:tenant",
                "secrets:write:*:tenant",
                "secrets:rotate:*:tenant",
                "secrets:delete:*:tenant",
                "certificates:read",
                "certificates:write"
            }
        },
        ["secrets-rotator"] = new RoleDefinition
        {
            Name = "Secrets Rotator",
            Description = "Can rotate secrets but not read values",
            ImpliedClaims = new[]
            {
                "secrets:rotate:*:tenant",
                "secrets:list:*"
            }
        }
    };
}
```

### 4.2 Add principal_type to JWT Claims

```csharp
// File: src/Dhadgar.Identity/Services/JwtService.cs (modification)

private List<Claim> BuildStandardClaims(
    User user,
    Guid organizationId,
    string role,
    bool isServiceAccount = false)
{
    var claims = new List<Claim>
    {
        new("sub", user.Id.ToString()),
        new("org_id", organizationId.ToString()),
        new("email", user.Email ?? ""),
        new("email_verified", user.EmailConfirmed.ToString().ToLowerInvariant()),
        new("role", role),
        new("principal_type", isServiceAccount ? "service" : "user") // NEW
    };

    // Add org_slug for secret name resolution
    // This would need to be fetched from the organization
    // claims.Add(new Claim("org_slug", organization.Slug));

    return claims;
}
```

---

## 5. Service-to-Service Authentication

### 5.1 Service Account Token Generation

```csharp
// File: src/Dhadgar.Identity/Services/ServiceAccountService.cs
namespace Dhadgar.Identity.Services;

public interface IServiceAccountService
{
    Task<ServiceAccountToken> GenerateTokenAsync(
        string serviceName,
        string[] scopes,
        CancellationToken ct = default);
}

public sealed record ServiceAccountToken(
    string AccessToken,
    int ExpiresIn,
    string TokenType = "Bearer");

public sealed class ServiceAccountService : IServiceAccountService
{
    private readonly IJwtService _jwtService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ServiceAccountService> _logger;

    public ServiceAccountService(
        IJwtService jwtService,
        IConfiguration configuration,
        ILogger<ServiceAccountService> logger)
    {
        _jwtService = jwtService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ServiceAccountToken> GenerateTokenAsync(
        string serviceName,
        string[] scopes,
        CancellationToken ct = default)
    {
        // Validate service is registered
        var allowedServices = _configuration
            .GetSection("Auth:ServiceAccounts")
            .Get<Dictionary<string, ServiceAccountConfig>>() ?? new();

        if (!allowedServices.TryGetValue(serviceName, out var config))
        {
            throw new UnauthorizedAccessException($"Unknown service: {serviceName}");
        }

        // Validate requested scopes
        var validScopes = scopes.Intersect(config.AllowedScopes, StringComparer.OrdinalIgnoreCase).ToArray();

        var claims = new List<Claim>
        {
            new("sub", $"service:{serviceName}"),
            new("service_name", serviceName),
            new("principal_type", "service"),
            new("client_type", "service")
        };

        foreach (var scope in validScopes)
        {
            claims.Add(new Claim("permission", scope));
        }

        var (accessToken, _, expiresIn) = await _jwtService.GenerateTokenPairAsync(
            claims,
            config.TokenLifetimeSeconds,
            includeRefresh: false,
            ct);

        _logger.LogInformation(
            "Service account token generated: Service={ServiceName} Scopes={Scopes}",
            serviceName, string.Join(", ", validScopes));

        return new ServiceAccountToken(accessToken, expiresIn);
    }
}

public sealed record ServiceAccountConfig
{
    public string[] AllowedScopes { get; init; } = Array.Empty<string>();
    public int TokenLifetimeSeconds { get; init; } = 3600; // 1 hour default
}
```

### 5.2 Configuration for Service Accounts

```json
// File: src/Dhadgar.Identity/appsettings.json (additions)
{
  "Auth": {
    "ServiceAccounts": {
      "identity-service": {
        "AllowedScopes": [
          "secrets:read:oauth",
          "secrets:read:betterauth"
        ],
        "TokenLifetimeSeconds": 3600
      },
      "gateway-service": {
        "AllowedScopes": [
          "secrets:read:infrastructure"
        ],
        "TokenLifetimeSeconds": 1800
      },
      "notification-service": {
        "AllowedScopes": [
          "secrets:read:oauth"
        ],
        "TokenLifetimeSeconds": 3600
      }
    }
  }
}
```

---

## 6. Rate Limiting

### 6.1 Secrets-Specific Rate Limiter

```csharp
// File: src/Dhadgar.Secrets/RateLimiting/SecretsRateLimiterExtensions.cs
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Dhadgar.Secrets.RateLimiting;

public static class SecretsRateLimiterExtensions
{
    public static IServiceCollection AddSecretsRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Read operations - higher limit
            options.AddPolicy("SecretsRead", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetPartitionKey(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 10
                    }));

            // Write operations - lower limit
            options.AddPolicy("SecretsWrite", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetPartitionKey(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 5
                    }));

            // Rotate operations - very low limit
            options.AddPolicy("SecretsRotate", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetPartitionKey(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 2
                    }));

            // Batch operations - moderate limit
            options.AddPolicy("SecretsBatch", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetPartitionKey(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 3
                    }));

            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = "Too many requests",
                    retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                        ? retry.TotalSeconds
                        : 60
                }, token);
            };
        });

        return services;
    }

    private static string GetPartitionKey(HttpContext context)
    {
        // Partition by user ID + tenant ID for per-user-per-tenant limiting
        var userId = context.User.FindFirstValue("sub") ?? "anonymous";
        var tenantId = context.User.FindFirstValue("org_id") ?? "no-tenant";
        return $"{tenantId}:{userId}";
    }
}
```

---

## 7. Input Validation

### 7.1 Secret Name Validator

```csharp
// File: src/Dhadgar.Secrets/Validation/SecretNameValidator.cs
using System.Text.RegularExpressions;

namespace Dhadgar.Secrets.Validation;

public static partial class SecretNameValidator
{
    // Azure Key Vault: alphanumeric and dashes, 1-127 characters
    [GeneratedRegex(@"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?$", RegexOptions.Compiled)]
    private static partial Regex ValidNamePattern();

    public static ValidationResult Validate(string? secretName)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return ValidationResult.Failure("Secret name is required");
        }

        if (secretName.Length > 127)
        {
            return ValidationResult.Failure("Secret name must be 127 characters or fewer");
        }

        if (secretName.Length < 1)
        {
            return ValidationResult.Failure("Secret name must be at least 1 character");
        }

        if (!ValidNamePattern().IsMatch(secretName))
        {
            return ValidationResult.Failure(
                "Secret name must contain only alphanumeric characters and dashes, " +
                "and must start and end with an alphanumeric character");
        }

        // Check for injection patterns
        if (ContainsInjectionPattern(secretName))
        {
            return ValidationResult.Failure("Secret name contains invalid characters");
        }

        return ValidationResult.Success();
    }

    private static bool ContainsInjectionPattern(string name)
    {
        var lowerName = name.ToLowerInvariant();

        // Path traversal
        if (name.Contains("..") || name.Contains("/") || name.Contains("\\"))
            return true;

        // Null bytes
        if (name.Contains('\0'))
            return true;

        // SQL injection patterns
        if (lowerName.Contains("'") || lowerName.Contains(";") ||
            lowerName.Contains("--") || lowerName.Contains("/*"))
            return true;

        // Script injection
        if (lowerName.Contains("<") || lowerName.Contains(">"))
            return true;

        return false;
    }
}

public readonly struct ValidationResult
{
    public bool IsValid { get; }
    public string? ErrorMessage { get; }

    private ValidationResult(bool isValid, string? errorMessage)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
    }

    public static ValidationResult Success() => new(true, null);
    public static ValidationResult Failure(string message) => new(false, message);
}
```

### 7.2 Request Validation Filter

```csharp
// File: src/Dhadgar.Secrets/Validation/SecretNameValidationFilter.cs
namespace Dhadgar.Secrets.Validation;

public sealed class SecretNameValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        // Find secretName parameter
        var secretNameIndex = -1;
        for (var i = 0; i < context.Arguments.Count; i++)
        {
            if (context.Arguments[i] is string potentialSecretName &&
                context.HttpContext.GetRouteValue("secretName") as string == potentialSecretName)
            {
                secretNameIndex = i;
                break;
            }
        }

        if (secretNameIndex >= 0)
        {
            var secretName = context.Arguments[secretNameIndex] as string;
            var validation = SecretNameValidator.Validate(secretName);

            if (!validation.IsValid)
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid secret name",
                    Detail = validation.ErrorMessage,
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        return await next(context);
    }
}
```

---

## 8. CLI Enhancements

### 8.1 Add Organization Option

```csharp
// File: src/Dhadgar.Cli/Commands/Secret/GetSecretCommand.cs (updated)
using System.CommandLine;

namespace Dhadgar.Cli.Commands.Secret;

public sealed class GetSecretCommand : Command
{
    public GetSecretCommand() : base("get", "Get a secret value")
    {
        var nameArg = new Argument<string>("name", "The name of the secret to retrieve");
        var revealOption = new Option<bool>("--reveal", "Show the actual secret value");
        var copyOption = new Option<bool>("--copy", "Copy the secret value to clipboard");
        var orgOption = new Option<string?>("--org", "Organization slug (uses default if not specified)");

        AddArgument(nameArg);
        AddOption(revealOption);
        AddOption(copyOption);
        AddOption(orgOption);

        this.SetHandler(ExecuteAsync, nameArg, revealOption, copyOption, orgOption);
    }

    private async Task ExecuteAsync(string name, bool reveal, bool copy, string? org)
    {
        var config = ConfigurationService.Load();
        var orgSlug = org ?? config.DefaultOrganization;

        if (string.IsNullOrWhiteSpace(orgSlug))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No organization specified. Use --org or set default with 'dhadgar config set organization <slug>'");
            return;
        }

        // ... rest of implementation
    }
}
```

### 8.2 New Audit Command

```csharp
// File: src/Dhadgar.Cli/Commands/Secret/AuditSecretsCommand.cs
using System.CommandLine;
using Spectre.Console;

namespace Dhadgar.Cli.Commands.Secret;

public sealed class AuditSecretsCommand : Command
{
    public AuditSecretsCommand() : base("audit", "Query secrets audit log")
    {
        var startOption = new Option<DateTime?>("--start", "Start date for audit query");
        var endOption = new Option<DateTime?>("--end", "End date for audit query");
        var secretOption = new Option<string?>("--secret", "Secret name pattern to filter (supports wildcards)");
        var actionOption = new Option<string?>("--action", "Action to filter (read, write, rotate, delete)");
        var limitOption = new Option<int>("--limit", () => 50, "Maximum number of results");

        AddOption(startOption);
        AddOption(endOption);
        AddOption(secretOption);
        AddOption(actionOption);
        AddOption(limitOption);

        this.SetHandler(ExecuteAsync, startOption, endOption, secretOption, actionOption, limitOption);
    }

    private async Task ExecuteAsync(
        DateTime? start,
        DateTime? end,
        string? secret,
        string? action,
        int limit)
    {
        var config = ConfigurationService.Load();
        var client = ApiClientFactory.CreateSecretsClient(config);

        var request = new AuditQueryRequest
        {
            StartTime = start ?? DateTime.UtcNow.AddDays(-7),
            EndTime = end ?? DateTime.UtcNow,
            SecretName = secret,
            Action = action,
            Limit = limit
        };

        var response = await client.QueryAuditAsync(request);

        var table = new Table();
        table.AddColumn("Timestamp");
        table.AddColumn("Secret");
        table.AddColumn("Action");
        table.AddColumn("User");
        table.AddColumn("Success");

        foreach (var evt in response.Events)
        {
            var successMarkup = evt.Success ? "[green]Yes[/]" : "[red]No[/]";
            table.AddRow(
                evt.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                evt.SecretName,
                evt.Action,
                evt.UserId ?? "N/A",
                successMarkup);
        }

        AnsiConsole.Write(table);

        if (response.HasMore)
        {
            AnsiConsole.MarkupLine($"[yellow]Showing {response.Events.Count} of {response.TotalCount} events. Use --limit to see more.[/]");
        }
    }
}
```

---

## 9. Test Examples

### 9.1 Authorization Handler Tests

```csharp
// File: tests/Dhadgar.Secrets.Tests/Authorization/TenantSecretAuthorizationHandlerTests.cs
using System.Security.Claims;
using Dhadgar.Secrets.Authorization.Handlers;
using Dhadgar.Secrets.Authorization.Requirements;
using Dhadgar.Secrets.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dhadgar.Secrets.Tests.Authorization;

public class TenantSecretAuthorizationHandlerTests
{
    private readonly TenantSecretAuthorizationHandler _handler;

    public TenantSecretAuthorizationHandlerTests()
    {
        var options = Options.Create(new SecretsOptions());
        _handler = new TenantSecretAuthorizationHandler(
            NullLogger<TenantSecretAuthorizationHandler>.Instance,
            options);
    }

    [Fact]
    public async Task HandleRequirementAsync_DeniesUnauthenticatedUser()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // Not authenticated
        var context = CreateContext(user, new TenantSecretRequirement(SecretAction.Read));
        var resource = new SecretResource("test-secret", "tenant-1", "oauth", false);

        // Act
        await _handler.HandleRequirementAsync(
            context,
            new TenantSecretRequirement(SecretAction.Read),
            resource);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_DeniesWhenTenantMismatch()
    {
        // Arrange
        var user = CreateUser("user-1", "tenant-A", new[] { "secrets:read:oauth:tenant" });
        var context = CreateContext(user, new TenantSecretRequirement(SecretAction.Read));
        var resource = new SecretResource("test-secret", "tenant-B", "oauth", false);

        // Act
        await _handler.HandleRequirementAsync(
            context,
            new TenantSecretRequirement(SecretAction.Read),
            resource);

        // Assert
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task HandleRequirementAsync_AllowsWhenTenantMatchesAndHasPermission()
    {
        // Arrange
        var user = CreateUser("user-1", "tenant-A", new[] { "secrets:read:oauth:tenant" });
        var context = CreateContext(user, new TenantSecretRequirement(SecretAction.Read, "oauth"));
        var resource = new SecretResource("discord-client-id", "tenant-A", "oauth", false);

        // Act
        await _handler.HandleRequirementAsync(
            context,
            new TenantSecretRequirement(SecretAction.Read, "oauth"),
            resource);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_AllowsPlatformSecretWithPlatformPermission()
    {
        // Arrange
        var user = CreateUser("service-1", null, new[] { "secrets:read:infrastructure" }, "service");
        var context = CreateContext(user, new TenantSecretRequirement(SecretAction.Read, "infrastructure"));
        var resource = new SecretResource("postgres-password", null, "infrastructure", true);

        // Act
        await _handler.HandleRequirementAsync(
            context,
            new TenantSecretRequirement(SecretAction.Read, "infrastructure"),
            resource);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_AllowsBreakGlassAccess()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("sub", "user-1"),
            new("org_id", "tenant-A"),
            new("principal_type", "user"),
            new("break_glass", "true"),
            new("break_glass_reason", "Emergency rotation - INC-12345")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var context = CreateContext(user, new TenantSecretRequirement(SecretAction.Read));
        var resource = new SecretResource("critical-secret", "tenant-B", "infrastructure", false);

        // Act
        await _handler.HandleRequirementAsync(
            context,
            new TenantSecretRequirement(SecretAction.Read),
            resource);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    private static ClaimsPrincipal CreateUser(
        string userId,
        string? tenantId,
        string[] permissions,
        string principalType = "user")
    {
        var claims = new List<Claim>
        {
            new("sub", userId),
            new("principal_type", principalType)
        };

        if (tenantId != null)
        {
            claims.Add(new Claim("org_id", tenantId));
        }

        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static AuthorizationHandlerContext CreateContext(
        ClaimsPrincipal user,
        IAuthorizationRequirement requirement)
    {
        return new AuthorizationHandlerContext(
            new[] { requirement },
            user,
            null);
    }
}
```

### 9.2 Integration Tests

```csharp
// File: tests/Dhadgar.Secrets.Tests/Integration/TenantIsolationIntegrationTests.cs
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Dhadgar.Secrets.Tests.Integration;

public class TenantIsolationIntegrationTests : IClassFixture<SecretsWebApplicationFactory>
{
    private readonly SecretsWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TenantIsolationIntegrationTests(SecretsWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetSecret_ReturnsForbidden_WhenAccessingOtherTenantSecret()
    {
        // Arrange
        _factory.AuthenticateAs("user-1", "tenant-A", new[] { "secrets:read:oauth:tenant" });

        // Act - Try to access tenant-B's secret
        var response = await _client.GetAsync("/api/v1/secrets/tenant-b--discord-client-id");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetSecret_ReturnsOk_WhenAccessingOwnTenantSecret()
    {
        // Arrange
        _factory.AuthenticateAs("user-1", "tenant-A", new[] { "secrets:read:oauth:tenant" });

        // Seed a test secret
        await _factory.SeedSecretAsync("tenant-a--discord-client-id", "test-value");

        // Act
        var response = await _client.GetAsync("/api/v1/secrets/discord-client-id");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SecretResponse>();
        Assert.NotNull(result);
        Assert.Equal("discord-client-id", result.Name);
    }

    [Fact]
    public async Task GetSecret_ServiceAccount_CanAccessPlatformSecrets()
    {
        // Arrange
        _factory.AuthenticateAsService("identity-service", new[] { "secrets:read:infrastructure" });

        // Seed a platform secret
        await _factory.SeedSecretAsync("postgres-password", "db-password-123");

        // Act
        var response = await _client.GetAsync("/api/v1/secrets/postgres-password");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

---

## Summary

This implementation guide provides concrete code examples for:

1. **Authorization**: Custom requirements, handlers, and policy-based authorization
2. **Tenant Isolation**: Context providers, secret name resolution, resource-based auth
3. **Audit Logging**: Comprehensive event logging with OpenTelemetry integration
4. **Identity Integration**: Role definitions, service accounts, JWT claims
5. **Rate Limiting**: Per-operation limits with user/tenant partitioning
6. **Input Validation**: Regex-based validation with injection protection
7. **CLI**: Organization context and audit commands
8. **Testing**: Unit and integration test examples

All code follows ASP.NET Core best practices from Microsoft Learn and patterns documented via Context7 research.
