using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.ServiceDefaults.Security;

/// <summary>
/// Options for admin API key authentication.
/// </summary>
public sealed class AdminApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The expected API key value. Configure via AdminApiKey configuration key.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The header name to look for the API key. Defaults to "X-Admin-Api-Key".
    /// </summary>
    public string HeaderName { get; set; } = "X-Admin-Api-Key";
}

/// <summary>
/// Authentication handler that validates admin API keys.
/// </summary>
public sealed class AdminApiKeyAuthenticationHandler : AuthenticationHandler<AdminApiKeyAuthenticationOptions>
{
    public const string SchemeName = "AdminApiKey";

    public AdminApiKeyAuthenticationHandler(
        IOptionsMonitor<AdminApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // If no API key is configured, allow all requests (dev mode)
        if (string.IsNullOrEmpty(Options.ApiKey))
        {
            Logger.LogWarning("AdminApiKey not configured - allowing unauthenticated access to admin endpoints");
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "anonymous-admin"),
                new Claim(ClaimTypes.Role, "admin"),
                new Claim("auth_mode", "unconfigured")
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }

        // Check for the API key header
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var providedKey))
        {
            return Task.FromResult(AuthenticateResult.Fail($"Missing {Options.HeaderName} header"));
        }

        // Validate the key using constant-time comparison
        if (!CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(Options.ApiKey),
            System.Text.Encoding.UTF8.GetBytes(providedKey.ToString())))
        {
            Logger.LogWarning("Invalid admin API key provided");
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        // Authentication successful
        var validClaims = new[]
        {
            new Claim(ClaimTypes.Name, "service-admin"),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim("auth_mode", "api_key")
        };
        var validIdentity = new ClaimsIdentity(validClaims, SchemeName);
        var validPrincipal = new ClaimsPrincipal(validIdentity);

        Logger.LogDebug("Admin API key authentication successful");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(validPrincipal, SchemeName)));
    }
}

/// <summary>
/// Extension methods for configuring admin API key authentication.
/// </summary>
public static class AdminApiKeyAuthenticationExtensions
{
    /// <summary>
    /// Adds admin API key authentication to the service collection.
    /// Configure the key via "AdminApiKey" in configuration or user-secrets.
    /// </summary>
    public static IServiceCollection AddAdminApiKeyAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthentication(AdminApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<AdminApiKeyAuthenticationOptions, AdminApiKeyAuthenticationHandler>(
                AdminApiKeyAuthenticationHandler.SchemeName,
                options =>
                {
                    options.ApiKey = configuration["AdminApiKey"];
                });

        services.AddAuthorizationBuilder()
            .AddPolicy("AdminApi", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole("admin");
            });

        return services;
    }
}
