using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Testing.Authentication;

/// <summary>
/// Extension methods for adding fake authentication to the service collection.
/// </summary>
public static class FakeAuthenticationExtensions
{
    /// <summary>
    /// Adds fake authentication handler for testing that reads user identity from HTTP headers.
    /// Use <see cref="FakeClaimsAuthenticationHandler.UserIdHeader"/>,
    /// <see cref="FakeClaimsAuthenticationHandler.OrgIdHeader"/>, and
    /// <see cref="FakeClaimsAuthenticationHandler.RoleHeader"/> to set test user identity.
    /// </summary>
    /// <param name="services">The service collection to add authentication to</param>
    /// <param name="scheme">The authentication scheme name (default: "TestScheme")</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddFakeAuthentication(
        this IServiceCollection services,
        string scheme = "TestScheme")
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = scheme;
            options.DefaultChallengeScheme = scheme;
            options.DefaultScheme = scheme;
        })
        .AddScheme<AuthenticationSchemeOptions, FakeClaimsAuthenticationHandler>(scheme, _ => { });

        return services;
    }
}
