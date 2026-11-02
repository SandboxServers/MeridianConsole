using System.Globalization;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Services;

/// <summary>
/// Discord behind the lab proxies (C1 port 15120, L36). The bot stays absent (D-ML41), so <c>/healthz</c> answers 503 and
/// that is the asserted state (F26). Postgres, RabbitMQ and the webhook sink are reached only through Toxiproxy. The
/// <c>SecretsService__Url</c> key is left unset so the <c>:5080</c> fallback stays visible (KI-DISCORD-SECRETS-PORT).
/// </summary>
public sealed class DiscordLabAdapter : ILabServiceAdapter
{
    /// <summary>Discord to webhook sink proxy port (C10 <c>discord-webhook</c>).</summary>
    public const int DiscordWebhookProxyPort = 20061;

    /// <summary>Discord to RabbitMQ proxy port (C10 <c>discord-rabbitmq</c>).</summary>
    public const int DiscordRabbitMqProxyPort = 20062;

    /// <inheritdoc />
    public string Name => "discord";

    /// <inheritdoc />
    public string Container => "meridian-lab-discord";

    /// <inheritdoc />
    public string Project => "discord";

    /// <inheritdoc />
    public int HostPort => LabPorts.Discord;

    /// <inheritdoc />
    public IReadOnlyList<string> Proxies { get; } = ["discord-postgres", "discord-rabbitmq", "discord-webhook"];

    /// <inheritdoc />
    public string HealthPath => "/healthz";

    /// <summary>The bot absent (D-ML41, F26): the 503 body is the asserted state, so 503 counts as ready.</summary>
    public System.Net.HttpStatusCode ReadyStatusCode => System.Net.HttpStatusCode.ServiceUnavailable;

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
        if (!ctx.ConnectionStrings.TryGetValue("discord", out string? postgres) || string.IsNullOrWhiteSpace(postgres))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.InvalidArgument] discord connection string is missing");
        }
        if (string.IsNullOrWhiteSpace(ctx.AdminApiKey))
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure("[Lab.InvalidArgument] discord admin API key is missing");
        }

        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Logging__Console__FormatterName"] = "json",
            ["Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information",
            ["ConnectionStrings__Postgres"] = postgres,
            ["RabbitMq__Host"] = GatewayAdapter.ToxiproxyHost,
            ["RabbitMq__Port"] = DiscordRabbitMqProxyPort.ToString(CultureInfo.InvariantCulture),
            ["RabbitMq__Username"] = ctx.RabbitMqUsername,
            ["RabbitMq__Password"] = ctx.RabbitMqPassword,
            ["Discord__WebhookUrl"] = $"http://{GatewayAdapter.ToxiproxyHost}:{DiscordWebhookProxyPort}/webhook",
            ["AdminApiKey"] = ctx.AdminApiKey,
        };
        return Result<IReadOnlyDictionary<string, string>>.Success(env);
    }
}
