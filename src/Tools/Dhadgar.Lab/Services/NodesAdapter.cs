using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Services;

/// <summary>
/// Nodes behind the lab proxies (C1 port 15040). Postgres and RabbitMQ are reached only through Toxiproxy, and the CA
/// lives under lab state so the lab never uses the dev CA.
/// </summary>
public sealed class NodesAdapter : ILabServiceAdapter
{
    /// <summary>Path inside the container where Nodes keeps its CA (bind mount of <c>LabPaths.State\nodes-ca</c>).</summary>
    public const string ContainerCaPath = "/lab-state/nodes-ca";

    /// <summary>Nodes to RabbitMQ proxy port (C10 <c>nodes-rabbitmq</c>).</summary>
    public const int NodesRabbitMqProxyPort = 20021;

    /// <inheritdoc />
    public string Name => "nodes";

    /// <inheritdoc />
    public string Container => "meridian-lab-nodes";

    /// <inheritdoc />
    public string Project => "nodes";

    /// <inheritdoc />
    public int HostPort => 15040;

    /// <inheritdoc />
    public IReadOnlyList<string> Proxies { get; } = ["nodes-postgres", "nodes-rabbitmq"];

    /// <inheritdoc />
    public string HealthPath => "/healthz";

    /// <inheritdoc />
    public IReadOnlySet<string> OverlayKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Logging:LogLevel:Default",
        "Logging:LogLevel:Dhadgar",
        "Logging:LogLevel:Microsoft.AspNetCore",
        "Mtls:RenewalGraceMinutes",
    };

    /// <inheritdoc />
    public Result<IReadOnlyDictionary<string, string>> Environment(LabServiceContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!ctx.ConnectionStrings.TryGetValue("nodes", out string? postgres) || string.IsNullOrWhiteSpace(postgres))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.InvalidArgument] nodes connection string is missing");
        }
        if (string.IsNullOrWhiteSpace(ctx.CaStateDirectory))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.InvalidArgument] nodes CA state directory is missing");
        }

        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Logging__Console__FormatterName"] = "json",
            ["Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information",
            ["ConnectionStrings__Postgres"] = postgres,
            ["RabbitMq__Host"] = GatewayAdapter.ToxiproxyHost,
            ["RabbitMq__Username"] = ctx.RabbitMqUsername,
            ["RabbitMq__Password"] = ctx.RabbitMqPassword,
            ["Nodes__CaStoragePath"] = ContainerCaPath,
        };
        return Result<IReadOnlyDictionary<string, string>>.Success(env);
    }
}
