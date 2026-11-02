using System.Threading;
using System.Threading.Tasks;
using Dhadgar.Lab.Services;
using Dhadgar.Shared.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Dhadgar.Lab.Tests.Services;

/// <summary>
/// The Notifications and Discord adapters (L36): every dependency goes through a Toxiproxy port, the admin key comes from
/// the context, and the Gateway clusters point at the lab proxies.
/// </summary>
public sealed class NotifyAdapterTests
{
    /// <summary>A clock that moves 100 seconds on every read, so one failed attempt passes the 90-second readiness deadline.</summary>
    private sealed class JumpingClock : TimeProvider
    {
        private long _ticks;

        public override long GetTimestamp() => Interlocked.Add(ref _ticks, TimestampFrequency * 100);
    }

    /// <summary>A loopback adapter whose health path is served by a test listener on <see cref="HostPort"/>.</summary>
    private sealed class LoopbackAdapter : ILabServiceAdapter
    {
        public LoopbackAdapter(int hostPort, System.Net.HttpStatusCode readyStatusCode)
        {
            HostPort = hostPort;
            ReadyStatusCode = readyStatusCode;
        }

        public string Name => "loopback";

        public string Container => "meridian-lab-loopback";

        public string Project => "loopback";

        public int HostPort { get; }

        public IReadOnlyList<string> Proxies { get; } = [];

        public string HealthPath => "/healthz";

        public System.Net.HttpStatusCode ReadyStatusCode { get; }

        public IReadOnlySet<string> OverlayKeys { get; } = new HashSet<string>(StringComparer.Ordinal);

        public Result<IReadOnlyDictionary<string, string>> Environment(LabServiceContext ctx) =>
            Result<IReadOnlyDictionary<string, string>>.Success(new Dictionary<string, string>());
    }

    /// <summary>Starts a loopback listener whose <c>/healthz</c> answers 503, the way the Discord bot reports it is up.</summary>
    private static async Task<WebApplication> StartServiceUnavailableListenerAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();
        app.MapGet("/healthz", () => Microsoft.AspNetCore.Http.Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        await app.StartAsync();
        return app;
    }

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
    public async Task WaitReady_ServiceAnswersItsReadyStatus_IsReady()
    {
        await using WebApplication app = await StartServiceUnavailableListenerAsync();
        int port = new Uri(app.Urls.First()).Port;

        Result ready = await LabServices.WaitReadyAsync(
            new LoopbackAdapter(port, System.Net.HttpStatusCode.ServiceUnavailable),
            TimeProvider.System,
            CancellationToken.None);

        Assert.True(ready.IsSuccess, ready.Error);
    }

    [Fact]
    public async Task WaitReady_ServiceAnswersOtherStatusThanReadyStatus_TimesOutNamingReadyStatus()
    {
        await using WebApplication app = await StartServiceUnavailableListenerAsync();
        int port = new Uri(app.Urls.First()).Port;

        Result ready = await LabServices.WaitReadyAsync(
            new LoopbackAdapter(port, System.Net.HttpStatusCode.OK),
            new JumpingClock(),
            CancellationToken.None);

        Assert.True(ready.IsFailure);
        Assert.StartsWith("[Lab.Timeout]", ready.Error, StringComparison.Ordinal);
        Assert.Contains("did not answer 200 within", ready.Error, StringComparison.Ordinal);
        Assert.Contains("last status HTTP 503", ready.Error, StringComparison.Ordinal);
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
    public void Sink_RunsProbeImageAsMeridianLabSink_OnLoopbackPort()
    {
        var sink = new SinkLabAdapter();

        Assert.Equal("meridian-lab-sink", sink.Container);
        Assert.Equal("probe", sink.Project);
        Assert.Equal(LabPorts.Sink, sink.HostPort);
        Assert.Equal("/_sink/requests", sink.HealthPath);
    }

    [Fact]
    public void RunOptions_PublishOnLoopback_AndSinkHasMemoryCap()
    {
        IReadOnlyList<string> sinkArgs = LabServices.RunOptions(new SinkLabAdapter());
        IReadOnlyList<string> discordArgs = LabServices.RunOptions(new DiscordLabAdapter());

        Assert.Contains($"127.0.0.1:{LabPorts.Sink}:8080", sinkArgs);
        Assert.Contains("--memory", sinkArgs);
        Assert.Contains("128m", sinkArgs);
        Assert.Contains($"127.0.0.1:{LabPorts.Discord}:8080", discordArgs);
        Assert.DoesNotContain("--memory", discordArgs);
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
