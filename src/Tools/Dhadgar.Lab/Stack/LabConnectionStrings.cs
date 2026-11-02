using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Stack;

/// <summary>
/// The secret references of the lab's infrastructure passwords, by credential name. Filled by <see cref="LabStack"/>
/// when the stack is created; the passwords themselves stay in the <see cref="SecretVault"/>.
/// </summary>
public sealed class LabCredentialRefs
{
    /// <summary>The Postgres superuser.</summary>
    public const string Postgres = "postgres";

    /// <summary>The role that owns every lab database.</summary>
    public const string LabOwner = "lab_owner";

    /// <summary>The read-only role used by <c>db_query</c>.</summary>
    public const string LabReader = "lab_reader";

    /// <summary>The lab RabbitMQ user.</summary>
    public const string RabbitMq = "rabbitmq";

    /// <summary>The Redis password.</summary>
    public const string Redis = "redis";

    /// <summary>The Identity exchange signing key (ES256 PKCS8 PEM), generated per stack; BetterAuth holds the private half.</summary>
    public const string IdentityExchange = "identity-exchange";

    /// <summary>The BetterAuth secret (<c>BETTER_AUTH_SECRET</c>).</summary>
    public const string BetterAuthSecret = "betterauth-secret";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _refs = new(StringComparer.Ordinal);

    /// <summary>
    /// Records the vault reference of one credential, replacing any earlier one.
    /// </summary>
    public void Set(string name, string secretRef)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(secretRef);

        lock (_gate)
        {
            _refs[name] = secretRef;
        }
    }

    /// <summary>
    /// The vault reference of one credential, if the stack has created it.
    /// </summary>
    public string? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_gate)
        {
            return _refs.GetValueOrDefault(name);
        }
    }

    /// <summary>
    /// Forgets every reference. The caller removes the vault entries.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _refs.Clear();
        }
    }
}

/// <summary>
/// Builds connection strings for the lab's services and tools (L08). Passwords come from the vault inside
/// <see cref="SecretVault.Use{T}"/>; the strings go only to Npgsql, AMQP or Redis clients and container env files.
/// </summary>
public sealed class LabConnectionStrings
{
    /// <summary>The direct host port for observation and the daemon's own writes (R-OBSERVE).</summary>
    private const string DirectHost = "127.0.0.1";

    private readonly SecretVault _vault;
    private readonly LabCredentialRefs _refs;

    /// <summary>
    /// Creates the builder over the vault and the credential references.
    /// </summary>
    public LabConnectionStrings(SecretVault vault, LabCredentialRefs refs)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _refs = refs ?? throw new ArgumentNullException(nameof(refs));
    }

    /// <summary>
    /// A service's database through its Toxiproxy proxy (<c>&lt;service&gt;-postgres</c>), as <c>lab_owner</c>.
    /// </summary>
    public Result<string> ServiceDb(string service)
    {
        var proxy = ProxyFor(service, "postgres");
        if (proxy.IsFailure)
        {
            return Result<string>.Failure(proxy.Error);
        }

        return WithPassword(LabCredentialRefs.LabOwner, password =>
            $"Host=meridian-lab-toxiproxy;Port={proxy.Value};Database={DatabaseName(service)};Username={LabCredentialRefs.LabOwner};Password={password}");
    }

    /// <summary>
    /// A service's database read-only, direct to the host port (observation path).
    /// </summary>
    public Result<string> Reader(string service) => DirectDb(service, LabCredentialRefs.LabReader);

    /// <summary>
    /// A service's database as its owner, direct to the host port (the daemon's own writes: L11 mint and reset).
    /// </summary>
    public Result<string> OwnerDirect(string service) => DirectDb(service, LabCredentialRefs.LabOwner);

    /// <summary>
    /// The Postgres superuser on the host port, for lab-only faults (L20).
    /// </summary>
    public Result<string> Admin() =>
        WithPassword(LabCredentialRefs.Postgres, password =>
            $"Host={DirectHost};Port={LabPorts.Postgres};Database=postgres;Username=postgres;Password={password}");

    /// <summary>
    /// A service's RabbitMQ through its Toxiproxy proxy (<c>&lt;service&gt;-rabbitmq</c>).
    /// </summary>
    public Result<string> RabbitMq(string service)
    {
        var proxy = ProxyFor(service, "rabbitmq");
        if (proxy.IsFailure)
        {
            return Result<string>.Failure(proxy.Error);
        }

        return WithPassword(LabCredentialRefs.RabbitMq, password =>
            $"amqp://lab:{password}@meridian-lab-toxiproxy:{proxy.Value}/");
    }

    /// <summary>
    /// A service's Redis through its Toxiproxy proxy (<c>&lt;service&gt;-redis</c>).
    /// </summary>
    public Result<string> Redis(string service)
    {
        var proxy = ProxyFor(service, "redis");
        if (proxy.IsFailure)
        {
            return Result<string>.Failure(proxy.Error);
        }

        return WithPassword(LabCredentialRefs.Redis, password =>
            $"meridian-lab-toxiproxy:{proxy.Value},password={password},abortConnect=false");
    }

    /// <summary>
    /// The Identity exchange private key (PKCS8 PEM) for BetterAuth's container env. Only the env file carries it.
    /// </summary>
    public Result<string> ExchangePrivateKeyPem() => WithPassword(LabCredentialRefs.IdentityExchange, pem => pem);

    /// <summary>
    /// The BetterAuth secret for BetterAuth's container env. Only the env file carries it.
    /// </summary>
    public Result<string> BetterAuthSecretValue() => WithPassword(LabCredentialRefs.BetterAuthSecret, secret => secret);

    private static string DatabaseName(string service) => $"meridian_lab_{service}";

    private static Result<int> ProxyFor(string service, string dependency)
    {
        if (string.IsNullOrEmpty(service) || !service.All(c => c is >= 'a' and <= 'z'))
        {
            return Result<int>.Failure("[Lab.InvalidArgument] unknown service");
        }

        var proxy = ProxyCatalog.Find($"{service}-{dependency}");
        return proxy is null
            ? Result<int>.Failure("[Lab.NotFound] no proxy for that service and dependency")
            : Result<int>.Success(proxy.ListenPort);
    }

    private Result<string> DirectDb(string service, string role)
    {
        if (string.IsNullOrEmpty(service) || !service.All(c => c is >= 'a' and <= 'z'))
        {
            return Result<string>.Failure("[Lab.InvalidArgument] unknown service");
        }

        return WithPassword(role, password =>
            $"Host={DirectHost};Port={LabPorts.Postgres};Database={DatabaseName(service)};Username={role};Password={password}");
    }

    private Result<string> WithPassword(string credential, Func<string, string> build)
    {
        var secretRef = _refs.Get(credential);
        if (secretRef is null)
        {
            return Result<string>.Failure("[Lab.NotFound] the stack has no credentials; run stack_up first");
        }

        // The password is copied only into the string being built; the vault keeps the one stored copy.
        return _vault.Use(secretRef, span => build(new string(span)));
    }
}
