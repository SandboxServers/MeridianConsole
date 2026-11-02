using System.Data.Common;
using System.Globalization;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Services;

/// <summary>
/// BetterAuth behind the lab Gateway (C1 port 15130). It runs in env mode (no Secrets service, F22), so its database
/// and secrets come from the env file. The exchange private key reaches it only through that env file.
/// </summary>
public sealed class BetterAuthLabAdapter : ILabServiceAdapter
{
    /// <summary>BetterAuth to Postgres proxy port (C10 <c>betterauth-postgres</c>).</summary>
    public const int PostgresProxyPort = 20040;

    /// <summary>The Panel origin BetterAuth trusts for browser sign-in (lab Panel on the loopback host port).</summary>
    public const string TrustedOrigin = "http://localhost:14321";

    /// <summary>The public Gateway URL of BetterAuth's routes, as the lab Gateway exposes it.</summary>
    public const string PublicUrl = "http://localhost:15000/api/v1/betterauth";

    /// <summary>Exchange token issuer and audience; they must equal Identity's <c>Auth:Exchange</c> settings.</summary>
    public const string ExchangeAudience = IdentityLabAdapter.ExchangeAudience;

    /// <summary>The <c>kid</c> written into exchange token headers.</summary>
    public const string ExchangeKeyId = "betterauth-exchange-v1";

    /// <inheritdoc />
    public string Name => "betterauth";

    /// <inheritdoc />
    public string Container => "meridian-lab-betterauth";

    /// <inheritdoc />
    public string Project => "betterauth";

    /// <inheritdoc />
    public int HostPort => 15130;

    /// <inheritdoc />
    public IReadOnlyList<string> Proxies { get; } = ["betterauth-postgres"];

    /// <inheritdoc />
    public string HealthPath => "/healthz";

    /// <inheritdoc />
    public IReadOnlySet<string> OverlayKeys { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public Result<IReadOnlyDictionary<string, string>> Environment(LabServiceContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!ctx.ConnectionStrings.TryGetValue("betterauth", out string? postgres) || string.IsNullOrWhiteSpace(postgres))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.InvalidArgument] betterauth connection string is missing");
        }
        if (string.IsNullOrWhiteSpace(ctx.ExchangePrivateKeyPem))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.InvalidArgument] the BetterAuth exchange private key is missing; run stack_up first");
        }
        if (string.IsNullOrWhiteSpace(ctx.BetterAuthSecret))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.InvalidArgument] the BetterAuth secret is missing; run stack_up first");
        }

        Result<DbConnectionStringBuilder> parts = ParsePostgres(postgres);
        if (parts.IsFailure)
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure(parts.Error);
        }

        DbConnectionStringBuilder db = parts.Value!;
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PORT"] = "8080",
            ["POSTGRES_HOST"] = Part(db, "Host"),
            ["POSTGRES_PORT"] = Part(db, "Port"),
            ["POSTGRES_DATABASE"] = Part(db, "Database"),
            ["POSTGRES_USERNAME"] = Part(db, "Username"),
            ["POSTGRES_PASSWORD"] = Part(db, "Password"),
            ["BETTER_AUTH_URL"] = PublicUrl,
            ["BETTER_AUTH_SECRET"] = ctx.BetterAuthSecret,
            ["BETTER_AUTH_TRUSTED_ORIGINS"] = TrustedOrigin,
            ["BETTER_AUTH_CLIENT_APPS"] = "panel",
            ["EXCHANGE_TOKEN_PRIVATE_KEY"] = LabEnvValue.SingleLinePem(ctx.ExchangePrivateKeyPem),
            ["EXCHANGE_TOKEN_ISSUER"] = IdentityLabAdapter.ExchangeIssuer,
            ["EXCHANGE_TOKEN_AUDIENCE"] = ExchangeAudience,
            ["EXCHANGE_TOKEN_KID"] = ExchangeKeyId,
        };
        return Result<IReadOnlyDictionary<string, string>>.Success(env);
    }

    /// <summary>
    /// Reads the Npgsql-style <c>Host=…;Port=…</c> string the lab builds for BetterAuth into the parts its env needs.
    /// The string carries the password, so the error text never repeats it.
    /// </summary>
    private static Result<DbConnectionStringBuilder> ParsePostgres(string connectionString)
    {
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            foreach (string key in new[] { "Host", "Port", "Database", "Username", "Password" })
            {
                if (!builder.ContainsKey(key) || string.IsNullOrEmpty(Convert.ToString(builder[key], CultureInfo.InvariantCulture)))
                {
                    return Result<DbConnectionStringBuilder>.Failure($"[Lab.InvalidArgument] betterauth connection string has no {key}");
                }
            }
            return Result<DbConnectionStringBuilder>.Success(builder);
        }
        catch (ArgumentException)
        {
            return Result<DbConnectionStringBuilder>.Failure("[Lab.InvalidArgument] betterauth connection string is malformed");
        }
    }

    private static string Part(DbConnectionStringBuilder db, string key) =>
        Convert.ToString(db[key], CultureInfo.InvariantCulture) ?? string.Empty;
}
