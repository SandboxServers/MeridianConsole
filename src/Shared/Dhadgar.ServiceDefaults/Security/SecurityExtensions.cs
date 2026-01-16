using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.ServiceDefaults.Security;

/// <summary>
/// Extension methods for registering security services.
/// </summary>
public static class SecurityExtensions
{
    /// <summary>
    /// Adds the security event logger for consistent security audit logging.
    /// Call this in services that need to log security-relevant events.
    /// </summary>
    public static IServiceCollection AddSecurityEventLogger(this IServiceCollection services)
    {
        services.AddSingleton<ISecurityEventLogger, SecurityEventLogger>();
        return services;
    }
}
