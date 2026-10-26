using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace Dhadgar.ServiceDefaults.Security;

/// <summary>
/// Registers the shared JWT bearer authentication used by user-facing Dhadgar services.
/// </summary>
public static class ServiceJwtAuthenticationExtensions
{
    /// <summary>
    /// Registers JwtBearer as the default scheme. Reads Auth:Issuer (required), Auth:Audience (required),
    /// Auth:MetadataAddress, Auth:InternalBaseUrl, Auth:ClockSkewSeconds (default 60).
    /// For requests whose path starts with one of hubPathPrefixes, a missing Authorization header
    /// falls back to the access_token query value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Auth:Issuer or Auth:Audience is missing (startup configuration error).</exception>
    public static AuthenticationBuilder AddDhadgarJwtBearer(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        params string[] hubPathPrefixes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(hubPathPrefixes);

        var issuer = configuration["Auth:Issuer"];
        var audience = configuration["Auth:Audience"];
        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new InvalidOperationException("Auth:Issuer must be configured for JWT bearer authentication.");
        }

        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException("Auth:Audience must be configured for JWT bearer authentication.");
        }

        var metadataAddress = configuration["Auth:MetadataAddress"];
        var internalBaseUrl = configuration["Auth:InternalBaseUrl"]; // e.g., http://identity:8080
        var clockSkewSeconds = configuration.GetValue<int?>("Auth:ClockSkewSeconds") ?? 60;
        var prefixes = hubPathPrefixes;

        return services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = issuer;
                options.Audience = audience;
                options.RequireHttpsMetadata = !environment.IsDevelopment();

                // Support separate MetadataAddress for internal service discovery in Docker/K8s
                // Token issuer remains external URL but JWKS is fetched from internal address
                if (!string.IsNullOrWhiteSpace(metadataAddress))
                {
                    options.MetadataAddress = metadataAddress;
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer, // Explicitly set to match token issuer
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(clockSkewSeconds)
                };
                options.RefreshOnIssuerKeyNotFound = true;

                // For local dev/Docker: use a document retriever that rewrites external URLs to internal.
                // This is needed because the OIDC metadata returns external URLs for jwks_uri
                // which containers can't reach in local Docker environments.
                if (!string.IsNullOrWhiteSpace(internalBaseUrl))
                {
                    var docRetriever = new UrlRewritingDocumentRetriever(issuer, internalBaseUrl)
                    {
                        RequireHttps = !environment.IsDevelopment()
                    };

                    options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.ConfigurationManager<OpenIdConnectConfiguration>(
                        metadataAddress ?? $"{issuer}.well-known/openid-configuration",
                        new OpenIdConnectConfigurationRetriever(),
                        docRetriever);
                }

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Browsers cannot set headers on SignalR connections, so hub paths may carry the
                        // token in the query string. Never accept it anywhere else.
                        if (string.IsNullOrEmpty(context.Token)
                            && !context.Request.Headers.ContainsKey(HeaderNames.Authorization)
                            && prefixes.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase))
                            && context.Request.Query.TryGetValue("access_token", out var token)
                            && !StringValues.IsNullOrEmpty(token))
                        {
                            context.Token = token.ToString();
                        }

                        return Task.CompletedTask;
                    }
                };
            });
    }
}
