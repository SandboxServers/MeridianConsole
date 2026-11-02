using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Dhadgar.Gateway.Tests;

/// <summary>
/// Proves that the hub access token, which clients send in <c>?access_token=</c>, never reaches OpenTelemetry trace
/// data (VS-32R, FU-41a N3). The protection is the default URL query redaction of the ASP.NET Core instrumentation and
/// of the runtime's <c>System.Net.Http</c> activity for the forwarded request. The tests fail when either redaction is
/// switched off: the ASP.NET Core one by its OpenTelemetry environment variable, the forwarded request by the runtime
/// switch <c>DOTNET_SYSTEM_NET_HTTP_DISABLEURIREDACTION</c>. The OpenTelemetry HttpClient variable is a no-op for the
/// forwarded request on .NET 10, so it is scanned for but not used as a detector.
/// </summary>
public sealed class AccessTokenTraceRedactionTests : IClassFixture<HubTokenWebApplicationFactory>
{
    private const string NegotiatePath = "/hubs/console/negotiate";
    private const string AspNetCoreRedactionSwitch = "OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION";
    private const string HttpClientRedactionSwitch = "OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION";
    private const string RuntimeHttpEnvSwitch = "DOTNET_SYSTEM_NET_HTTP_DISABLEURIREDACTION";
    private const string RuntimeHttpContextSwitch = "System.Net.Http.DisableUriRedaction";

    private static readonly string[] RedactionSwitches =
    [
        AspNetCoreRedactionSwitch,
        HttpClientRedactionSwitch,
        RuntimeHttpEnvSwitch,
        RuntimeHttpContextSwitch
    ];

    private readonly HubTokenWebApplicationFactory _factory;

    public AccessTokenTraceRedactionTests(HubTokenWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HubRequest_IsTraced()
    {
        var (status, activities) = await SendNegotiateAndCaptureActivitiesAsync(
            _factory, SeamJwt.CreateUserToken(Guid.NewGuid(), Guid.NewGuid()));

        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
        var traced = activities.Any(activity =>
            activity.Source.Name == "Microsoft.AspNetCore"
            && activity.TagObjects.Any(tag =>
                tag.Key is "url.path" or "http.target"
                && (tag.Value?.ToString() ?? string.Empty).Contains(NegotiatePath, StringComparison.Ordinal)));

        Assert.True(traced, "No Microsoft.AspNetCore activity carried the negotiate path, so tracing did not run in the test host.");
    }

    [Fact]
    public async Task ForwardedRequest_IsTraced()
    {
        var (status, activities) = await SendNegotiateAndCaptureActivitiesAsync(
            _factory, SeamJwt.CreateUserToken(Guid.NewGuid(), Guid.NewGuid()));

        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
        // The YARP forwarder's outbound request is a client span, so the redaction check below covers it. Without one
        // the token check would pass without ever looking at the forwarded URL.
        var forwarded = activities.Any(activity =>
            activity.Source.Name != "Microsoft.AspNetCore"
            && activity.TagObjects.Any(tag =>
                tag.Key is "url.full" or "http.url"
                && (tag.Value?.ToString() ?? string.Empty).Contains(NegotiatePath, StringComparison.Ordinal)));

        Assert.True(forwarded, "No outbound activity carried the negotiate URL, so the forwarded request was not traced.");
    }

    [Fact]
    public async Task AccessToken_NeverAppearsInTraceTags()
    {
        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), Guid.NewGuid());
        var (status, activities) = await SendNegotiateAndCaptureActivitiesAsync(_factory, token);

        // The token must be accepted, otherwise the request never reached the code that traces it.
        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
        Assert.NotEmpty(activities);
        Assert.Empty(FindTraceLeaks(activities, token));
    }

    [Fact]
    public void LeakScan_DetectsTokenWhenQueryIsNotRedacted()
    {
        // Self-test: an activity that carries the unredacted query must be reported, otherwise the scan above could
        // pass without being able to fail. The activity is built by hand because the redaction switches are read from
        // the process environment, which a per-factory setting cannot change safely.
        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), Guid.NewGuid());
        using var activity = new Activity("LeakScanSelfTest");
        activity.SetTag("url.query", $"?negotiateVersion=1&access_token={token}");

        Assert.NotEmpty(FindTraceLeaks([activity], token));
    }

    [Fact]
    public void RedactionSwitches_AreNotSetInDeployOrConfig()
    {
        var root = RepoFiles.FindRepoRoot();
        var scanned = RepoFiles.Enumerate(root, file => RepoFiles.IsScanned(root, file)).ToList();
        var scannedNames = scanned.Select(file => Path.GetRelativePath(root, file)).ToList();

        // The scan must see the pipeline and compose files, so a root or path bug cannot empty it silently.
        Assert.Contains("azure-pipelines.yml", scannedNames);
        Assert.Contains(Path.Combine("deploy", "compose", "docker-compose.services.yml"), scannedNames);

        var hits = scanned
            .Where(file => RedactionSwitches.Any(switchName => File.ReadAllText(file).Contains(switchName, StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();

        Assert.Empty(hits);
    }

    /// <summary>
    /// Returns one entry per activity tag or display name that carries the token, or a query value that is not
    /// <c>Redacted</c>. Entries name the source and tag only, so a failure never prints the token.
    /// </summary>
    private static List<string> FindTraceLeaks(IEnumerable<Activity> activities, string token)
    {
        var leaks = new List<string>();
        foreach (var activity in activities)
        {
            if (activity.DisplayName.Contains(token, StringComparison.Ordinal))
            {
                leaks.Add($"{activity.Source.Name}: DisplayName");
            }

            foreach (var tag in activity.TagObjects)
            {
                var value = tag.Value?.ToString() ?? string.Empty;
                if (value.Contains(token, StringComparison.Ordinal) || Regex.IsMatch(value, "access_token=(?!Redacted)"))
                {
                    leaks.Add($"{activity.Source.Name}: tag {tag.Key}");
                }
            }
        }

        return leaks;
    }

    private static async Task<(HttpStatusCode Status, List<Activity> Activities)> SendNegotiateAndCaptureActivitiesAsync(
        HubTokenWebApplicationFactory factory, string token)
    {
        var stopped = new ConcurrentBag<Activity>();
        HttpStatusCode status;

        using (var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add
        })
        {
            ActivitySource.AddActivityListener(listener);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            using var response = await client.GetAsync($"{NegotiatePath}?negotiateVersion=1&access_token={token}");
            status = response.StatusCode;
        }

        return (status, stopped.ToList());
    }
}
