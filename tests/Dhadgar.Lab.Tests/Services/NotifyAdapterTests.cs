using Dhadgar.Lab.Services;
using Xunit;

namespace Dhadgar.Lab.Tests.Services;

/// <summary>
/// The Notifications and Discord adapters (L36): every dependency goes through a Toxiproxy port, the admin key comes from
/// the context, and the Gateway clusters point at the lab proxies.
/// </summary>
public sealed class NotifyAdapterTests
{
    private static LabServiceContext Context(bool withAdminKey = true) =>
        new(
            "a9c623a1",
            new Dictionary<string, string>
            {
                ["notifications"] = "Host=meridian-lab-toxiproxy;Port=20050;Database=dhadgar_notifications",
                ["discord"] = "Host=meridian-lab-toxiproxy;Port=20060;Database=dhadgar_discord",
            },
            "C:\\lab\\state\\nodes-ca",
            "lab_user",
            "lab-password-value",
            withAdminKey ? "lab-admin-key-value" : null);

    [Fact]
    public void ReadyStatus_DiscordCountsAs503_OthersAs200()
    {
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, new DiscordLabAdapter().ReadyStatusCode);
        ILabServiceAdapter notifications = new NotificationsLabAdapter();
        Assert.Equal(System.Net.HttpStatusCode.OK, notifications.ReadyStatusCode);
    }

    [Fact]
    public void Context_ToString_NeverPrintsSecretsOrConnectionStrings()
    {
        string text = Context().ToString();

        Assert.DoesNotContain("lab-admin-key-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("lab-password-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Database=dhadgar_notifications", text, StringComparison.Ordinal);
        Assert.Contains("AdminApiKey = [redacted]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Notifications_RabbitMqAndWebhook_GoThroughProxies()
    {
        var env = new NotificationsLabAdapter().Environment(Context());

        Assert.True(env.IsSuccess, env.Error);
        Assert.Equal("meridian-lab-toxiproxy", env.Value!["RabbitMq__Host"]);
        Assert.Equal("20051", env.Value["RabbitMq__Port"]);
        Assert.Equal("http://meridian-lab-toxiproxy:20052/webhook", env.Value["Discord__WebhookUrl"]);
        Assert.Contains("Port=20050", env.Value["ConnectionStrings__Postgres"], StringComparison.Ordinal);
        Assert.Equal("lab-admin-key-value", env.Value["AdminApiKey"]);
    }

    [Fact]
    public void Discord_RabbitMqAndWebhook_GoThroughProxies()
    {
        var env = new DiscordLabAdapter().Environment(Context());

        Assert.True(env.IsSuccess, env.Error);
        Assert.Equal("meridian-lab-toxiproxy", env.Value!["RabbitMq__Host"]);
        Assert.Equal("20062", env.Value["RabbitMq__Port"]);
        Assert.Equal("http://meridian-lab-toxiproxy:20061/webhook", env.Value["Discord__WebhookUrl"]);
        Assert.Contains("Port=20060", env.Value["ConnectionStrings__Postgres"], StringComparison.Ordinal);
        Assert.Equal("lab-admin-key-value", env.Value["AdminApiKey"]);
    }

    [Fact]
    public void Discord_DoesNotSetSecretsServiceUrl_SoTheFallbackIsVisible()
    {
        var env = new DiscordLabAdapter().Environment(Context());

        Assert.True(env.IsSuccess, env.Error);
        Assert.False(env.Value!.ContainsKey("SecretsService__Url"));
    }

    [Fact]
    public void Notifications_MissingAdminKey_IsInvalidArgument()
    {
        var env = new NotificationsLabAdapter().Environment(Context(withAdminKey: false));

        Assert.True(env.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", env.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Discord_MissingAdminKey_IsInvalidArgument()
    {
        var env = new DiscordLabAdapter().Environment(Context(withAdminKey: false));

        Assert.True(env.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", env.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Gateway_ClustersForNotificationsAndDiscord_PointAtLabProxies()
    {
        var env = new GatewayAdapter().Environment(Context());

        Assert.True(env.IsSuccess, env.Error);
        Assert.Equal("http://meridian-lab-toxiproxy:20014/", env.Value!["ReverseProxy__Clusters__notifications__Destinations__d1__Address"]);
        Assert.Equal("http://meridian-lab-toxiproxy:20015/", env.Value["ReverseProxy__Clusters__discord__Destinations__d1__Address"]);
    }
}
