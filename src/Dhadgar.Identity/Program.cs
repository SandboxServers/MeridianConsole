using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.OpenApi;
using Dhadgar.Identity;
using Dhadgar.Identity.Authentication;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Endpoints;
using Dhadgar.Identity.OAuth;
using Dhadgar.Identity.Options;
using Dhadgar.Identity.Services;
using Dhadgar.Identity.Readiness;
using Dhadgar.Messaging;
using Dhadgar.ServiceDefaults.Security;
using MassTransit;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Security.KeyVault.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Dhadgar.ServiceDefaults;
using Dhadgar.ServiceDefaults.Middleware;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using IdentityHello = Dhadgar.Identity.Hello;
using Dhadgar.Identity.Data.Entities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Dhadgar Identity API",
        Version = "v1",
        Description = "Identity and access management API for Meridian Console"
    });

    // Add JWT Bearer authentication to Swagger UI
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter your JWT token"
    });
});

// SECURITY: Configure request body size limits to prevent DoS attacks
builder.ConfigureRequestLimits(options =>
{
    options.MaxRequestBodySize = 1_048_576;    // 1 MB default for API requests
    options.MaxRequestHeadersTotalSize = 32_768; // 32 KB for headers
    options.MaxRequestLineSize = 8_192;         // 8 KB for request line
});

builder.Services.AddDbContext<IdentityDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));

    // Suppress pending model changes warning in development/Docker environments
    // where we want auto-migration without requiring new migration files
    if (builder.Configuration.GetValue<bool>("Database:AutoMigrate"))
    {
        options.ConfigureWarnings(w => w.Ignore(
            Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }
});

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<ExchangeTokenOptions>(builder.Configuration.GetSection("Auth:Exchange"));
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Webhooks"));

// Create signing key provider early so it can be used by both JwtService and OpenIddict
var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
using var tempLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
// CA2000: SigningKeyProvider implements IDisposable and is registered as singleton.
// The DI container will dispose it when the application shuts down.
#pragma warning disable CA2000
var signingKeyProvider = new SigningKeyProvider(authOptions, builder.Environment, tempLoggerFactory.CreateLogger<SigningKeyProvider>());
#pragma warning restore CA2000
builder.Services.AddSingleton<ISigningKeyProvider>(signingKeyProvider);

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var connectionString = builder.Configuration.GetValue<string>("Redis:ConnectionString");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Redis connection string is required.");
    }

    return ConnectionMultiplexer.Connect(connectionString);
});

// ASP.NET Core Identity (no passwords; external + Better Auth exchange)
builder.Services.AddIdentityCore<User>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 12; // unused for Better Auth, but set sane default
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<IdentityDbContext>()
    .AddSignInManager();

builder.Services.AddSingleton<IExchangeTokenValidator, ExchangeTokenValidator>();
builder.Services.AddSingleton<IExchangeTokenReplayStore, RedisExchangeTokenReplayStore>();
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddSingleton<IClientAssertionService, ClientAssertionService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<TokenExchangeService>();
builder.Services.AddScoped<ILinkedAccountService, LinkedAccountService>();
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<MembershipService>();
builder.Services.AddScoped<OrganizationSwitchService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<RoleService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddTokenCleanupService();
builder.Services.AddInvitationCleanupService();
builder.Services.AddHealthChecks()
    .AddCheck<IdentityReadinessCheck>("identity_ready", tags: ["ready"]);

// Memory cache for webhook secret caching
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IWebhookSecretProvider, WebhookSecretProvider>();

var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    // Default to JwtBearer for API endpoints using .RequireAuthorization()
    // This validates tokens from our JwtService (used by /exchange endpoint)
    options.DefaultAuthenticateScheme = AuthSchemes.Bearer;
    options.DefaultChallengeScheme = AuthSchemes.Bearer;
    // External cookie is only used for OAuth provider callbacks
    options.DefaultSignInScheme = AuthSchemes.External;
});

// Add JwtBearer authentication for our custom JWTs (generated by JwtService)
// This is separate from OpenIddict validation which handles OpenIddict-generated tokens
authenticationBuilder.AddJwtBearer(AuthSchemes.Bearer, options =>
{
    var issuer = builder.Configuration["Auth:Issuer"]?.TrimEnd('/') + "/";
    var audience = builder.Configuration["Auth:Audience"] ?? "meridian-api";

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKeyProvider.GetSigningKey(),
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    // Don't require HTTPS for development
    if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
    {
        options.RequireHttpsMetadata = false;
    }
});

authenticationBuilder.AddCookie(AuthSchemes.External, options =>
{
    // __Host- prefix requires: Secure=true, Path="/", no Domain attribute
    options.Cookie.Name = "__Host-dhadgar-external";
    options.Cookie.Path = "/";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    options.SlidingExpiration = false;
});

if (builder.Environment.IsEnvironment("Testing"))
{
    OAuthProviderRegistry.ConfigureMockProviders(authenticationBuilder, AuthSchemes.External);
}
else
{
    // Load gaming OAuth secrets from Secrets Service at startup
    var secretsServiceUrl = builder.Configuration["SecretsService:Url"] ?? "http://localhost:5000";
    using var oauthSecrets = new OAuthSecretProvider(new Uri(secretsServiceUrl));
    await oauthSecrets.LoadSecretsAsync();

    using var oauthLoggerFactory = LoggerFactory.Create(logging =>
    {
        logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        logging.AddConsole();
    });
    var oauthLogger = oauthLoggerFactory.CreateLogger("OAuthProviders");

    OAuthProviderRegistry.ConfigureProviders(
        authenticationBuilder,
        builder.Configuration,
        builder.Environment,
        oauthSecrets,
        AuthSchemes.External,
        oauthLogger);
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter($"auth:{ip}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0
        });
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

        if (path == "/exchange")
        {
            return RateLimitPartition.GetFixedWindowLimiter($"exchange:{ip}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        if (path == "/connect/token" || path == "/refresh")
        {
            return RateLimitPartition.GetFixedWindowLimiter($"token:{ip}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        if (path.StartsWith("/connect/authorize") || path.StartsWith("/oauth/"))
        {
            return RateLimitPartition.GetSlidingWindowLimiter($"authcb:{ip}", _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            });
        }

        if (path.StartsWith("/webhooks/better-auth"))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"webhook:{ip}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        // Rate limit invitations: 10/hour per user globally across all orgs
        // This prevents abuse where a user could spam invites to multiple organizations
        if (path.Contains("/members/invite", StringComparison.OrdinalIgnoreCase) && context.User?.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst("sub")?.Value;

            if (!string.IsNullOrWhiteSpace(userId))
            {
                return RateLimitPartition.GetFixedWindowLimiter($"invite:user:{userId}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0
                });
            }
        }

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var orgId = context.User.FindFirst("org_id")?.Value;
            if (!string.IsNullOrWhiteSpace(orgId))
            {
                return RateLimitPartition.GetFixedWindowLimiter($"tenant:{orgId}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            }

            var clientType = context.User.FindFirst("client_type")?.Value;
            if (string.Equals(clientType, "agent", StringComparison.OrdinalIgnoreCase))
            {
                var agentId = context.User.FindFirst("sub")?.Value ?? ip;
                return RateLimitPartition.GetFixedWindowLimiter($"agent:{agentId}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 500,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            }
        }

        // Default: conservative limit for unmatched/unauthenticated requests
        return RateLimitPartition.GetFixedWindowLimiter($"default:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddMassTransit(x =>
    {
        x.UsingInMemory((ctx, cfg) =>
        {
            cfg.ConfigureEndpoints(ctx);
        });
    });
}
else
{
    builder.Services.AddDhadgarMessaging(builder.Configuration);
}

builder.Services.AddScoped<IIdentityEventPublisher, IdentityEventPublisher>();
builder.Services.AddSecurityEventLogger();

builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
            .UseDbContext<IdentityDbContext>();
    })
    .AddServer(options =>
    {
        var defaultAudience = builder.Configuration["Auth:Audience"];
        var wifAudience = builder.Configuration["OpenIddict:Wif:Audience"] ?? "api://AzureADTokenExchange";

        var issuer = builder.Configuration["Auth:Issuer"];
        if (!string.IsNullOrWhiteSpace(issuer))
        {
            var issuerUri = new Uri(issuer.TrimEnd('/') + "/");
            options.SetIssuer(issuerUri);

            // Set both absolute URIs (for discovery document) and relative paths (for internal routing)
            // OpenIddict uses the first URI for the discovery document and matches requests against all URIs
            options.SetAuthorizationEndpointUris(new Uri(issuerUri, "connect/authorize"), new Uri("connect/authorize", UriKind.Relative))
                .SetTokenEndpointUris(new Uri(issuerUri, "connect/token"), new Uri("connect/token", UriKind.Relative))
                .SetUserInfoEndpointUris(new Uri(issuerUri, "connect/userinfo"), new Uri("connect/userinfo", UriKind.Relative))
                .SetIntrospectionEndpointUris(new Uri(issuerUri, "connect/introspect"), new Uri("connect/introspect", UriKind.Relative))
                .SetRevocationEndpointUris(new Uri(issuerUri, "connect/revocation"), new Uri("connect/revocation", UriKind.Relative))
                .SetJsonWebKeySetEndpointUris(new Uri(issuerUri, ".well-known/jwks.json"), new Uri(".well-known/jwks.json", UriKind.Relative));
        }
        else
        {
            // Fallback to relative URIs if issuer not configured
            options.SetAuthorizationEndpointUris("connect/authorize")
                .SetTokenEndpointUris("connect/token")
                .SetUserInfoEndpointUris("connect/userinfo")
                .SetIntrospectionEndpointUris("connect/introspect")
                .SetRevocationEndpointUris("connect/revocation")
                .SetJsonWebKeySetEndpointUris(".well-known/jwks.json");
        }

        options.AllowAuthorizationCodeFlow()
            .RequireProofKeyForCodeExchange()
            .AllowClientCredentialsFlow()
            .AllowRefreshTokenFlow();

        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            "servers:read",
            "servers:write",
            "nodes:manage",
            "billing:read",
            "secrets:read",
            "wif");

        var useDevelopmentCertificates = builder.Configuration.GetValue<bool>("Auth:UseDevelopmentCertificates");

        if (builder.Environment.IsEnvironment("Testing") || useDevelopmentCertificates)
        {
            // Use ephemeral development certificates for testing or when explicitly enabled via config.
            // This allows Docker containers to run without Key Vault access.
            options.AddDevelopmentSigningCertificate()
                .AddDevelopmentEncryptionCertificate();

            // Also add the shared ECDSA signing key so OpenIddict validation recognizes
            // tokens signed by JwtService (used for the /exchange endpoint)
            options.AddSigningKey(signingKeyProvider.GetSigningKey());

            // Disable access token encryption in development so JWT Bearer auth works
            // without needing the decryption key at each service
            options.DisableAccessTokenEncryption();
        }
        else
        {
            var vaultUri = builder.Configuration["Auth:KeyVault:VaultUri"];
            var signingCertName = builder.Configuration["Auth:KeyVault:SigningCertName"];
            var encryptionCertName = builder.Configuration["Auth:KeyVault:EncryptionCertName"];

            // Always use Key Vault for certificates outside explicit dev overrides
            if (string.IsNullOrWhiteSpace(vaultUri) ||
                string.IsNullOrWhiteSpace(signingCertName) ||
                string.IsNullOrWhiteSpace(encryptionCertName))
            {
                throw new InvalidOperationException(
                    "Key Vault certificate configuration is required. " +
                    "Configure Auth:KeyVault:VaultUri, SigningCertName, and EncryptionCertName. " +
                    "Ensure you are logged in via 'az login' for local development.");
            }

        var credential = new DefaultAzureCredential();
        var vaultUriValue = new Uri(vaultUri);
        var certClient = new CertificateClient(vaultUriValue, credential);
        var secretClient = new SecretClient(vaultUriValue, credential);

        try
        {
            // DownloadCertificate returns X509Certificate2 with private key (required for signing)
            // GetCertificate only returns public cert metadata which cannot be used for signing
            var signingCert = LoadKeyVaultCertificate(certClient, secretClient, signingCertName);
            var encryptionCert = LoadKeyVaultCertificate(certClient, secretClient, encryptionCertName);

            var signingKey = CreateSigningKey(signingCert);
            options.AddSigningKey(signingKey)
                .AddEncryptionCertificate(encryptionCert);

            // Disable access token encryption so:
            // 1. Services can validate tokens without decryption keys (using JWKS)
            // 2. WIF tokens work with Azure AD (which rejects encrypted assertions)
            // The encryption cert is still available for ID tokens or future use.
            options.DisableAccessTokenEncryption();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to load OpenIddict certificates from Key Vault ({vaultUri}). " +
                $"Ensure you are logged in via 'az login' and have Key Vault Certificates User role. " +
                $"If the certificate is not exportable, ensure a private key is available via the Key Vault secret. " +
                $"Error: {ex.Message}", ex);
        }
        }

        var aspNetCoreBuilder = options.UseAspNetCore()
            .EnableStatusCodePagesIntegration();

        // Allow HTTP for development/Docker environments (internal service-to-service calls)
        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") || useDevelopmentCertificates)
        {
            aspNetCoreBuilder.DisableTransportSecurityRequirement();
        }

        options.AddEventHandler<OpenIddictServerEvents.HandleTokenRequestContext>(eventBuilder =>
            eventBuilder.UseInlineHandler(async context =>
            {
                if (context.Request is null || !context.Request.IsClientCredentialsGrantType())
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(context.ClientId))
                {
                    context.Reject(
                        OpenIddictConstants.Errors.InvalidRequest,
                        "ClientId is required for client credentials grant.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(context.Request.ClientSecret))
                {
                    context.Reject(
                        OpenIddictConstants.Errors.InvalidClient,
                        "ClientSecret is required for client credentials grant.");
                    return;
                }

                var httpRequest = OpenIddictServerAspNetCoreHelpers.GetHttpRequest(context.Transaction);
                var httpContext = httpRequest?.HttpContext;
                if (httpContext is null)
                {
                    context.Reject(OpenIddictConstants.Errors.ServerError, "HTTP context unavailable.");
                    return;
                }

                var manager = httpContext.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
                var application = await manager.FindByClientIdAsync(context.ClientId);
                if (application is null ||
                    !await manager.ValidateClientSecretAsync(application, context.Request.ClientSecret))
                {
                    context.Reject(OpenIddictConstants.Errors.InvalidClient, "Invalid client credentials.");
                    return;
                }

                if (!await manager.HasPermissionAsync(application, OpenIddictConstants.Permissions.Endpoints.Token) ||
                    !await manager.HasPermissionAsync(application, OpenIddictConstants.Permissions.GrantTypes.ClientCredentials))
                {
                    context.Reject(OpenIddictConstants.Errors.UnauthorizedClient, "Client is not allowed to use client credentials.");
                    return;
                }

                var permissions = await manager.GetPermissionsAsync(application);
                var allowedScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var permission in permissions)
                {
                    if (permission.StartsWith(OpenIddictConstants.Permissions.Prefixes.Scope, StringComparison.OrdinalIgnoreCase))
                    {
                        allowedScopes.Add(permission[OpenIddictConstants.Permissions.Prefixes.Scope.Length..]);
                    }
                }

                var requestedScopes = context.Request.GetScopes().ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var scope in requestedScopes)
                {
                    if (!allowedScopes.Contains(scope))
                    {
                        context.Reject(OpenIddictConstants.Errors.InvalidScope, $"Scope '{scope}' is not allowed for this client.");
                        return;
                    }
                }

                var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
                identity.AddClaim(OpenIddictConstants.Claims.Subject, context.ClientId);
                identity.AddClaim(OpenIddictConstants.Claims.ClientId, context.ClientId);

                // Add principal_type claim for service accounts
                var principalTypeClaim = new Claim("principal_type", "service");
                principalTypeClaim.SetDestinations(OpenIddictConstants.Destinations.AccessToken);
                identity.AddClaim(principalTypeClaim);

                // Add permission claims based on requested scopes
                // This enables scope-to-permission mapping for service-to-service calls
                foreach (var scope in requestedScopes)
                {
                    Claim? permissionClaim = null;

                    // Map scopes to permission claims
                    // e.g., "secrets:read" -> "permission: secrets:read:*"
                    if (scope.StartsWith("secrets:", StringComparison.OrdinalIgnoreCase))
                    {
                        permissionClaim = new Claim("permission", $"{scope}:*");
                    }
                    else if (scope.EndsWith(":read", StringComparison.OrdinalIgnoreCase) ||
                             scope.EndsWith(":write", StringComparison.OrdinalIgnoreCase) ||
                             scope.EndsWith(":manage", StringComparison.OrdinalIgnoreCase))
                    {
                        // Generic scope-to-permission mapping for service scopes
                        permissionClaim = new Claim("permission", scope);
                    }

                    if (permissionClaim is not null)
                    {
                        permissionClaim.SetDestinations(OpenIddictConstants.Destinations.AccessToken);
                        identity.AddClaim(permissionClaim);
                    }
                }

                var principal = new ClaimsPrincipal(identity);
                principal.SetScopes(requestedScopes);

                if (requestedScopes.Contains("wif", StringComparer.OrdinalIgnoreCase))
                {
                    principal.SetAudiences(wifAudience);
                }
                else if (!string.IsNullOrWhiteSpace(defaultAudience))
                {
                    principal.SetAudiences(defaultAudience);
                }

                context.SignIn(principal);
            }));

        // Handle refresh token grant - reload permissions from database
        options.AddEventHandler<OpenIddictServerEvents.HandleTokenRequestContext>(eventBuilder =>
            eventBuilder.UseInlineHandler(async context =>
            {
                if (context.Request is null || !context.Request.IsRefreshTokenGrantType())
                {
                    return;
                }

                // The refresh token has already been validated by OpenIddict at this point.
                // We need to reload user permissions from the database to ensure they're current.
                var httpRequest = OpenIddictServerAspNetCoreHelpers.GetHttpRequest(context.Transaction);
                var httpContext = httpRequest?.HttpContext;
                if (httpContext is null)
                {
                    context.Reject(OpenIddictConstants.Errors.ServerError, "HTTP context unavailable.");
                    return;
                }

                // Prefer the validated principal provided by OpenIddict for refresh token requests
                // Fall back to transaction properties or HTTP authentication if not available
                var existingPrincipal = context.Principal
                    ?? (context.Transaction.Properties.TryGetValue(
                        OpenIddictServerAspNetCoreConstants.Properties.RefreshTokenPrincipal,
                        out var principal) ? principal as ClaimsPrincipal : null)
                    ?? (await httpContext.AuthenticateAsync(
                        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme) is { Principal: { } p } ? p : null);

                var userIdClaim = existingPrincipal?.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
                if (string.IsNullOrWhiteSpace(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    context.Reject(OpenIddictConstants.Errors.InvalidGrant, "Invalid user identity in refresh token.");
                    return;
                }

                // Get org_id from existing token if available
                Guid? orgId = null;
                var orgIdClaim = existingPrincipal?.FindFirst("org_id")?.Value;
                if (!string.IsNullOrWhiteSpace(orgIdClaim) && Guid.TryParse(orgIdClaim, out var parsedOrgId))
                {
                    orgId = parsedOrgId;
                }

                // Reload user permissions from database
                var refreshTokenService = httpContext.RequestServices.GetRequiredService<IRefreshTokenService>();
                var result = await refreshTokenService.ReloadUserForRefreshAsync(userId, orgId);

                if (result is null)
                {
                    context.Reject(
                        OpenIddictConstants.Errors.InvalidGrant,
                        "User is no longer active or valid for token refresh.");
                    return;
                }

                // Build new claims identity with fresh permissions
                var identity = result.BuildClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
                var newPrincipal = new ClaimsPrincipal(identity);

                // Preserve the scopes from the original token
                var originalScopes = existingPrincipal?.GetScopes() ?? Enumerable.Empty<string>();
                newPrincipal.SetScopes(originalScopes);

                // Preserve audiences
                var originalAudiences = existingPrincipal?.GetAudiences() ?? Enumerable.Empty<string>();
                if (originalAudiences.Any())
                {
                    newPrincipal.SetAudiences(originalAudiences);
                }
                else if (!string.IsNullOrWhiteSpace(defaultAudience))
                {
                    newPrincipal.SetAudiences(defaultAudience);
                }

                context.SignIn(newPrincipal);
            })
            .SetOrder(100)); // Run after built-in refresh token validation

        // Azure WIF compatibility: Replace at+jwt with JWT typ header for WIF access tokens before signing.
        options.AddEventHandler<OpenIddictServerEvents.GenerateTokenContext>(eventBuilder =>
            eventBuilder.UseInlineHandler(AzureWifTokenHandler.ApplyAccessTokenType)
                .SetOrder(OpenIddictServerHandlers.Protection.GenerateIdentityModelToken.Descriptor.Order - 50));
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });
// Configure authorization with default policy requiring authenticated users
// NOTE: FallbackPolicy is NOT used because it blocks OpenIddict's internal endpoints
// Each endpoint group must explicitly call .RequireAuthorization() as needed
// The default authentication scheme (OpenIddict validation) handles bearer tokens automatically
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build())
    .AddPolicy("OrgMember", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("org_id"))
    .AddPolicy("OrgAdmin", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("org_id")
        .RequireAssertion(ctx =>
        {
            var role = ctx.User.FindFirst("role")?.Value;
            return role is "owner" or "admin";
        }))
    .AddPolicy("OrgOwner", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("org_id")
        .RequireAssertion(ctx =>
        {
            var role = ctx.User.FindFirst("role")?.Value;
            return role is "owner";
        }))
    .AddPolicy("EmailVerified", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
        {
            var verified = ctx.User.FindFirst("email_verified")?.Value;
            return string.Equals(verified, "true", StringComparison.OrdinalIgnoreCase);
        }));

var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
var otlpUri = !string.IsNullOrWhiteSpace(otlpEndpoint) ? new Uri(otlpEndpoint) : null;
var resourceBuilder = ResourceBuilder.CreateDefault().AddService("Dhadgar.Identity");

builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(resourceBuilder);
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.ParseStateValues = true;

    if (otlpUri is not null)
    {
        options.AddOtlpExporter(exporter => exporter.Endpoint = otlpUri);
    }
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (otlpUri is not null)
        {
            tracing.AddOtlpExporter(options => options.Endpoint = otlpUri);
        }
        // OTLP export requires explicit endpoint configuration; skipped when not set
    })
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation();

        if (otlpUri is not null)
        {
            metrics.AddOtlpExporter(options => options.Endpoint = otlpUri);
        }
        // OTLP export requires explicit endpoint configuration; skipped when not set
    });

var app = builder.Build();

// Enable Swagger in Development and Testing environments
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// SECURITY: Handle request size limit exceptions with proper JSON responses
app.UseRequestLimitsMiddleware();

app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseRateLimiter();
}

// Ensure OpenIddict server middleware runs to handle token requests
// This must come after UseAuthentication/UseAuthorization but before endpoints
//app.UseMiddleware<OpenIddictServerMiddleware>(); // Not needed - UseAuthentication() triggers it

// Apply EF Core migrations automatically during local/dev runs or when configured.
var autoMigrate = app.Environment.IsDevelopment() ||
    app.Configuration.GetValue<bool>("Database:AutoMigrate");

if (autoMigrate)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("Database migrations applied successfully.");

    await SeedDevOpenIddictClientAsync(app.Services, app.Configuration, app.Logger);
}

// Anonymous endpoints (no authentication required)
app.MapGet("/", () => Results.Ok(new { service = "Dhadgar.Identity", message = IdentityHello.Message }))
    .WithTags("Health")
    .WithName("ServiceInfo")
    .WithDescription("Get service information")
    .AllowAnonymous();
app.MapGet("/hello", () => Results.Text(IdentityHello.Message))
    .WithTags("Health")
    .WithName("Hello")
    .WithDescription("Simple hello endpoint")
    .AllowAnonymous();
app.MapDhadgarDefaultEndpoints(); // Health checks - already configured as anonymous in ServiceDefaults

// Token exchange endpoint - uses its own validation (Better Auth exchange tokens)
// This must be anonymous as it's the entry point for authentication
app.MapPost("/exchange", TokenExchangeEndpoint.Handle)
    .WithTags("Authentication")
    .WithName("TokenExchange")
    .WithDescription("Exchange a Better Auth token for a JWT access token and refresh token")
    .AllowAnonymous();

// OAuth provider endpoints
OAuthEndpoints.Map(app);

// Protected API endpoints - explicitly require authenticated user
// Note: Individual endpoints within each group handle specific authorization via EndpointHelpers
OrganizationEndpoints.Map(app);
MembershipEndpoints.Map(app);
UserEndpoints.Map(app);
RoleEndpoints.Map(app);
MfaPolicyEndpoints.Map(app);
ActivityEndpoints.Map(app);
SearchEndpoints.Map(app);
SessionEndpoints.Map(app);
MeEndpoints.Map(app);

// Internal endpoints for service-to-service communication
// Note: These should be protected by service auth (client credentials) in production
InternalEndpoints.Map(app);

// Webhook endpoint - uses signature validation, not JWT auth
WebhookEndpoint.Map(app);

await app.RunAsync();

static async Task SeedDevOpenIddictClientAsync(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger logger)
{
    var enabled = configuration.GetValue<bool?>("OpenIddict:DevClient:Enabled") ?? true;
    if (!enabled)
    {
        return;
    }

    var clientId = configuration["OpenIddict:DevClient:ClientId"] ?? "dev-client";
    var clientSecret = configuration["OpenIddict:DevClient:ClientSecret"] ?? "dev-secret";

    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
    {
        logger.LogWarning("Dev OpenIddict client is missing ClientId/ClientSecret; skipping seed.");
        return;
    }

    using var scope = services.CreateScope();
    var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

    // Only create dev-client if it doesn't exist, but always seed service accounts
    if (await manager.FindByClientIdAsync(clientId) is null)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            DisplayName = "Dev Client",
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OpenId,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Profile,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Email,
                OpenIddictConstants.Permissions.Prefixes.Scope + "servers:read",
                OpenIddictConstants.Permissions.Prefixes.Scope + "servers:write",
                OpenIddictConstants.Permissions.Prefixes.Scope + "nodes:manage",
                OpenIddictConstants.Permissions.Prefixes.Scope + "billing:read",
                OpenIddictConstants.Permissions.Prefixes.Scope + "secrets:read",
                OpenIddictConstants.Permissions.Prefixes.Scope + "wif"
            }
        };

        var redirectUris = configuration.GetSection("OpenIddict:DevClient:RedirectUris").Get<string[]>() ?? [];
        foreach (var uri in redirectUris)
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            {
                descriptor.RedirectUris.Add(parsed);
            }
        }

        if (descriptor.RedirectUris.Count > 0)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
        }

        await manager.CreateAsync(descriptor);
        logger.LogInformation("Seeded dev OpenIddict client {ClientId}.", clientId);
    }

    // Seed internal service accounts (always, even if dev-client already existed)
    await SeedServiceAccountAsync(
        manager,
        configuration,
        logger,
        serviceKey: "SecretsService",
        defaultClientId: "secrets-service",
        defaultDisplayName: "Secrets Service",
        scopes: ["wif"]); // Only needs WIF for Azure authentication

    await SeedServiceAccountAsync(
        manager,
        configuration,
        logger,
        serviceKey: "BetterAuthService",
        defaultClientId: "betterauth-service",
        defaultDisplayName: "BetterAuth Service",
        scopes: ["secrets:read"]); // Needs to read secrets from Secrets service

    // BetterAuth WIF client for Microsoft federated credentials
    // This client is used to get tokens for authenticating to Microsoft OAuth
    // The client_id matches the "subject" in the Azure federated credential
    await SeedServiceAccountAsync(
        manager,
        configuration,
        logger,
        serviceKey: "BetterAuthWif",
        defaultClientId: "betterauth-client",
        defaultDisplayName: "BetterAuth WIF Client",
        scopes: ["wif"]); // Needs WIF for Microsoft federated credential
}

static async Task SeedServiceAccountAsync(
    IOpenIddictApplicationManager manager,
    IConfiguration configuration,
    ILogger logger,
    string serviceKey,
    string defaultClientId,
    string defaultDisplayName,
    string[] scopes)
{
    var configSection = $"OpenIddict:ServiceAccounts:{serviceKey}";
    var enabled = configuration.GetValue<bool?>($"{configSection}:Enabled") ?? true;
    if (!enabled)
    {
        return;
    }

    var clientId = configuration[$"{configSection}:ClientId"] ?? defaultClientId;
    var clientSecret = configuration[$"{configSection}:ClientSecret"];

    // Generate a deterministic but secure secret if not configured
    // In production, this should be explicitly configured
    if (string.IsNullOrWhiteSpace(clientSecret))
    {
        clientSecret = $"{clientId}-dev-secret-change-in-prod";
    }

    if (await manager.FindByClientIdAsync(clientId) is not null)
    {
        return;
    }

    var descriptor = new OpenIddictApplicationDescriptor
    {
        ClientId = clientId,
        ClientSecret = clientSecret,
        DisplayName = defaultDisplayName,
        Permissions =
        {
            OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
        }
    };

    foreach (var scope in scopes)
    {
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
    }

    await manager.CreateAsync(descriptor);
    logger.LogInformation("Seeded service account {ClientId} ({DisplayName}).", clientId, defaultDisplayName);
}

static X509Certificate2 LoadKeyVaultCertificate(
    CertificateClient certificateClient,
    SecretClient secretClient,
    string certificateName)
{
    var secret = secretClient.GetSecret(certificateName).Value;
    var secretValue = secret.Value;
    if (string.IsNullOrWhiteSpace(secretValue))
    {
        throw new InvalidOperationException(
            $"Key Vault secret '{certificateName}' is empty. " +
            "Ensure the certificate is marked exportable or import a PFX with a private key.");
    }

    byte[] pfxBytes;
    try
    {
        pfxBytes = Convert.FromBase64String(secretValue);
    }
    catch (FormatException ex)
    {
        throw new InvalidOperationException(
            $"Key Vault secret '{certificateName}' is not valid base64 PFX data.", ex);
    }

    var certificate = TryLoadCertificate(pfxBytes);
    if (certificate is not null)
    {
        return certificate;
    }

    // Fall back to the certificate object (public-only) to improve error details.
    var publicOnly = certificateClient.DownloadCertificate(certificateName).Value;
    if (publicOnly.HasPrivateKey)
    {
        return publicOnly;
    }

    throw new InvalidOperationException(
        $"Key Vault secret '{certificateName}' does not contain a usable private key.");
}

static X509Certificate2? TryLoadCertificate(byte[] pfxBytes)
{
    var candidates = new[]
    {
        X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable,
        X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable,
        X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable
    };

    foreach (var flags in candidates)
    {
        try
        {
            var cert = new X509Certificate2(pfxBytes, (string?)null, flags);
            if (cert.HasPrivateKey)
            {
                return cert;
            }
        }
        catch (CryptographicException)
        {
            // Try the next key storage option.
        }
    }

    return null;
}

static SecurityKey CreateSigningKey(X509Certificate2 certificate)
{
    ArgumentNullException.ThrowIfNull(certificate);

    var rsa = certificate.GetRSAPrivateKey();
    if (rsa is not null)
    {
        return new RsaSecurityKey(rsa) { KeyId = certificate.Thumbprint };
    }

    var ecdsa = certificate.GetECDsaPrivateKey();
    if (ecdsa is not null)
    {
        return new ECDsaSecurityKey(ecdsa) { KeyId = certificate.Thumbprint };
    }

    throw new InvalidOperationException(
        $"Signing certificate '{certificate.Subject}' does not expose a usable private key.");
}

// Required for WebApplicationFactory<Program> integration tests.
public partial class Program { }
