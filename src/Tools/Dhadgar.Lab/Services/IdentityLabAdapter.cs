using System.Globalization;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Services;

/// <summary>
/// Identity behind the lab proxies (C1 port 15010). Postgres, Redis and RabbitMQ are reached only through Toxiproxy.
/// It verifies BetterAuth's exchange tokens with the stack's public key, so the key pair is generated per stack.
/// It runs Identity's development-certificate path with a blank Key Vault URI, as the compose dev stack does (D-ML152):
/// certificates and the signing key are ephemeral (KG-W7-5), and webhooks are unsigned.
/// </summary>
public sealed class IdentityLabAdapter : ILabServiceAdapter
{
    /// <summary>Identity's own issuer (<c>Auth:Issuer</c>).</summary>
    public const string Issuer = "https://meridianconsole.com/api/v1/identity";

    /// <summary>Identity's API audience (<c>Auth:Audience</c>).</summary>
    public const string Audience = "meridian-api";

    /// <summary>BetterAuth's exchange issuer; Identity accepts exchange tokens only from it.</summary>
    public const string ExchangeIssuer = "https://meridianconsole.com/api/v1/betterauth";

    /// <summary>Audience of the exchange tokens; equals the path Identity serves <c>/exchange</c> on.</summary>
    public const string ExchangeAudience = "https://meridianconsole.com/api/v1/identity/exchange";

    /// <summary>
    /// <c>Auth:UseDevelopmentCertificates</c>: Identity's own switch for ephemeral OpenIddict certificates instead of Key Vault (D-ML152).
    /// Set only by this adapter; never an overlay key and never a product default.
    /// </summary>
    public const string UseDevelopmentCertificatesKey = "Auth__UseDevelopmentCertificates";

    /// <summary>
    /// <c>Auth:KeyVault:VaultUri</c>, set empty so it overrides appsettings: no Key Vault calls for certificates, exchange keys or webhook secrets (D-ML152).
    /// </summary>
    public const string KeyVaultUriKey = "Auth__KeyVault__VaultUri";

    /// <summary>Identity to Postgres proxy port (C10 <c>identity-postgres</c>).</summary>
    public const int PostgresProxyPort = 20030;

    /// <summary>Identity to Redis proxy port (C10 <c>identity-redis</c>).</summary>
    public const int RedisProxyPort = 20031;

    /// <summary>Identity to RabbitMQ proxy port (C10 <c>identity-rabbitmq</c>).</summary>
    public const int RabbitMqProxyPort = 20032;

    /// <inheritdoc />
    public string Name => "identity";

    /// <inheritdoc />
    public string Container => "meridian-lab-identity";

    /// <inheritdoc />
    public string Project => "identity";

    /// <inheritdoc />
    public int HostPort => 15010;

    /// <inheritdoc />
    public IReadOnlyList<string> Proxies { get; } = ["identity-postgres", "identity-redis", "identity-rabbitmq"];

    /// <inheritdoc />
    public string HealthPath => "/healthz";

    /// <inheritdoc />
    public IReadOnlySet<string> OverlayKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Logging:LogLevel:Default",
        "Logging:LogLevel:Dhadgar",
        "Logging:LogLevel:Microsoft.AspNetCore",
    };

    /// <inheritdoc />
    public Result<IReadOnlyDictionary<string, string>> Environment(LabServiceContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!ctx.ConnectionStrings.TryGetValue("identity", out string? postgres) || string.IsNullOrWhiteSpace(postgres))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.InvalidArgument] identity connection string is missing");
        }
        if (!ctx.ConnectionStrings.TryGetValue("identity-redis", out string? redis) || string.IsNullOrWhiteSpace(redis))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.InvalidArgument] identity Redis connection string is missing");
        }
        if (string.IsNullOrWhiteSpace(ctx.ExchangePublicKeyPem))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.InvalidArgument] the identity exchange public key is missing; run stack_up first");
        }

        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Logging__Console__FormatterName"] = "json",
            ["ConnectionStrings__Postgres"] = postgres,
            ["Redis__ConnectionString"] = redis,
            ["Auth__Issuer"] = Issuer,
            ["Auth__Audience"] = Audience,
            ["Auth__Exchange__Issuer"] = ExchangeIssuer,
            ["Auth__Exchange__Audience"] = ExchangeAudience,
            ["Auth__Exchange__PublicKeyPem"] = LabEnvValue.EscapedNewlinePem(ctx.ExchangePublicKeyPem),
            [UseDevelopmentCertificatesKey] = "true",
            [KeyVaultUriKey] = string.Empty,
            ["RabbitMq__Host"] = GatewayAdapter.ToxiproxyHost,
            ["RabbitMq__Port"] = RabbitMqProxyPort.ToString(CultureInfo.InvariantCulture),
            ["RabbitMq__Username"] = ctx.RabbitMqUsername,
            ["RabbitMq__Password"] = ctx.RabbitMqPassword,
        };
        return Result<IReadOnlyDictionary<string, string>>.Success(env);
    }
}
