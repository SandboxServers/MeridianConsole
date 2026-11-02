using System.Globalization;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Services;

/// <summary>
/// Notifications behind the lab proxies (C1 port 15090, L36). Postgres, RabbitMQ and the webhook target are reached only
/// through Toxiproxy (<c>notifications-postgres</c>, <c>notifications-rabbitmq</c>, <c>notifications-webhook</c>), and the
/// admin routes take the vault-held admin key.
/// </summary>
public sealed class NotificationsLabAdapter : ILabServiceAdapter
{
    /// <summary>Notifications to RabbitMQ proxy port (C10 <c>notifications-rabbitmq</c>).</summary>
    public const int NotificationsRabbitMqProxyPort = 20051;

    /// <summary>Notifications to webhook sink proxy port (C10 <c>notifications-webhook</c>).</summary>
    public const int NotificationsWebhookProxyPort = 20052;

    /// <inheritdoc />
    public string Name => "notifications";

    /// <inheritdoc />
    public string Container => "meridian-lab-notifications";

    /// <inheritdoc />
    public string Project => "notifications";

    /// <inheritdoc />
    public int HostPort => LabPorts.Notifications;

    /// <inheritdoc />
    public IReadOnlyList<string> Proxies { get; } = ["notifications-postgres", "notifications-rabbitmq", "notifications-webhook"];

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
        if (!ctx.ConnectionStrings.TryGetValue("notifications", out string? postgres) || string.IsNullOrWhiteSpace(postgres))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.InvalidArgument] notifications connection string is missing");
        }
        if (string.IsNullOrWhiteSpace(ctx.AdminApiKey))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.InvalidArgument] notifications admin API key is missing");
        }

        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Logging__Console__FormatterName"] = "json",
            ["Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information",
            ["ConnectionStrings__Postgres"] = postgres,
            ["RabbitMq__Host"] = GatewayAdapter.ToxiproxyHost,
            ["RabbitMq__Port"] = NotificationsRabbitMqProxyPort.ToString(CultureInfo.InvariantCulture),
            ["RabbitMq__Username"] = ctx.RabbitMqUsername,
            ["RabbitMq__Password"] = ctx.RabbitMqPassword,
            ["Discord__WebhookUrl"] = $"http://{GatewayAdapter.ToxiproxyHost}:{NotificationsWebhookProxyPort}/webhook",
            ["AdminApiKey"] = ctx.AdminApiKey,
        };
        return Result<IReadOnlyDictionary<string, string>>.Success(env);
    }
}
