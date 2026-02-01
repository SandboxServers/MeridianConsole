using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.MultiTenancy;

/// <summary>
/// Authorization requirement for tenant-scoped access.
/// Verifies the user's organization claim matches the route parameter.
/// </summary>
public sealed class TenantScopedRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Handler that validates the user has access to the organization
/// specified in the route parameter.
/// </summary>
public sealed class TenantScopedHandler : AuthorizationHandler<TenantScopedRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<TenantScopedHandler> _logger;

    public TenantScopedHandler(
        IHttpContextAccessor httpContextAccessor,
        ILogger<TenantScopedHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantScopedRequirement requirement)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            _logger.LogWarning("TenantScoped authorization failed: no HTTP context");
            return Task.CompletedTask;
        }

        // Get the organization ID from the route
        if (!httpContext.Request.RouteValues.TryGetValue("organizationId", out var routeOrgIdValue))
        {
            // No organizationId in route - this handler doesn't apply
            // This allows the same policy to work on routes without org context
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (!Guid.TryParse(routeOrgIdValue?.ToString(), out var routeOrgId))
        {
            _logger.LogWarning("TenantScoped authorization failed: invalid organizationId in route");
            return Task.CompletedTask;
        }

        // Get the user's organization claim
        var userOrgClaim = context.User.FindFirst("org_id")?.Value;
        if (string.IsNullOrEmpty(userOrgClaim))
        {
            _logger.LogWarning(
                "TenantScoped authorization failed: user has no org_id claim (requested org: {OrgId})",
                routeOrgId);
            return Task.CompletedTask;
        }

        if (!Guid.TryParse(userOrgClaim, out var userOrgId))
        {
            _logger.LogWarning("TenantScoped authorization failed: invalid org_id claim format");
            return Task.CompletedTask;
        }

        // Verify the user's organization matches the route
        if (userOrgId != routeOrgId)
        {
            _logger.LogWarning(
                "TenantScoped authorization failed: user org {UserOrgId} does not match route org {RouteOrgId}",
                userOrgId, routeOrgId);
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Extension methods for registering tenant-scoped authorization.
/// </summary>
public static class TenantScopedAuthorizationExtensions
{
    /// <summary>
    /// Adds tenant-scoped authorization with proper route validation.
    /// This policy validates that the user's org_id claim matches
    /// the organizationId route parameter.
    /// </summary>
    public static IServiceCollection AddTenantScopedAuthorization(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IAuthorizationHandler, TenantScopedHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy("TenantScoped", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new TenantScopedRequirement());
            });
        return services;
    }
}
