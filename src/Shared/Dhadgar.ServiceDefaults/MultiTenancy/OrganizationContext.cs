using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.ServiceDefaults.MultiTenancy;

/// <summary>
/// Provides the current organization context from the request.
/// Used for tenant isolation in multi-tenant services.
/// </summary>
public interface IOrganizationContext
{
    /// <summary>
    /// The current organization ID from the request context.
    /// Returns null if no organization context is available.
    /// </summary>
    Guid? OrganizationId { get; }

    /// <summary>
    /// Whether a valid organization context is available.
    /// </summary>
    bool HasOrganization => OrganizationId.HasValue;

    /// <summary>
    /// Gets the organization ID, throwing if not available.
    /// </summary>
    Guid RequiredOrganizationId => OrganizationId
        ?? throw new InvalidOperationException("Organization context is required but not available");
}

/// <summary>
/// Default implementation that reads organization ID from HttpContext claims.
/// </summary>
public sealed class HttpOrganizationContext : IOrganizationContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private Guid? _cachedOrgId;
    private bool _resolved;

    public HttpOrganizationContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

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

    private Guid? ResolveOrganizationId()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
        {
            return null;
        }

        // Try standard claim first
        var orgClaim = context.User.FindFirst("org_id")
                    ?? context.User.FindFirst("organization_id");

        if (orgClaim is not null && Guid.TryParse(orgClaim.Value, out var orgId))
        {
            return orgId;
        }

        // Try header (for service-to-service calls with forwarded context)
        if (context.Request.Headers.TryGetValue("X-Organization-Id", out var headerValue)
            && Guid.TryParse(headerValue.FirstOrDefault(), out var headerOrgId))
        {
            return headerOrgId;
        }

        return null;
    }
}

/// <summary>
/// Extension methods for registering organization context.
/// </summary>
public static class OrganizationContextExtensions
{
    /// <summary>
    /// Adds the organization context service for multi-tenant scenarios.
    /// Requires AddHttpContextAccessor() to be called first.
    /// </summary>
    public static IServiceCollection AddOrganizationContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IOrganizationContext, HttpOrganizationContext>();
        return services;
    }
}
