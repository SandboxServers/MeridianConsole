using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Dhadgar.Gateway.Tests;

/// <summary>
/// Proves that the hub access token, which clients send in <c>?access_token=</c>, never reaches OpenTelemetry trace
/// data (VS-32R, FU-41a N3). The protection is the instrumentation's default URL query redaction, so the tests fail
/// when that redaction is switched off by one of its environment variables.
/// </summary>
public sealed class AccessTokenTraceRedactionTests : IClassFixture<HubTokenWebApplicationFactory>
{
    private const string NegotiatePath = "/hubs/console/negotiate";
    private const string AspNetCoreRedactionSwitch = "OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION";
    private const string HttpClientRedactionSwitch = "OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION";

    private readonly HubTokenWebApplicationFactory _factory;

    public AccessTokenTraceRedactionTests(HubTokenWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HubRequest_IsTraced()
    {
        var (status, activities) = await SendNegotiateAndCaptureActivitiesAsync(
            SeamJwt.CreateUserToken(Guid.NewGuid(), Guid.NewGuid()));

        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
        var traced = activities.Any(activity =>
            activity.Source.Name == "Microsoft.AspNetCore"
            && activity.TagObjects.Any(tag =>
                tag.Key is "url.path" or "http.target"
                && (tag.Value?.ToString() ?? string.Empty).Contains(NegotiatePath, StringComparison.Ordinal)));

        Assert.True(traced, "No Microsoft.AspNetCore activity carried the negotiate path, so tracing did not run in the test host.");
    }

    [Fact]
    public async Task AccessToken_NeverAppearsInTraceTags()
    {
        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), Guid.NewGuid());
        var (status, activities) = await SendNegotiateAndCaptureActivitiesAsync(token);

        // The token must be accepted, otherwise the request never reached the code that traces it.
        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
        Assert.NotEmpty(activities);

        // Leaks are reported by source and tag name only, so a failure never prints the token.
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

        Assert.Empty(leaks);
    }

    [Fact]
    public void RedactionSwitches_AreNotSetInDeployOrConfig()
    {
        var root = FindRepoRoot();
        var scanned = EnumerateScanFiles(root).ToList();
        Assert.NotEmpty(scanned);

        var hits = scanned
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains(AspNetCoreRedactionSwitch, StringComparison.Ordinal)
                    || text.Contains(HttpClientRedactionSwitch, StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();

        Assert.Empty(hits);
    }

    private async Task<(HttpStatusCode Status, List<Activity> Activities)> SendNegotiateAndCaptureActivitiesAsync(string token)
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
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            using var response = await client.GetAsync($"{NegotiatePath}?negotiateVersion=1&access_token={token}");
            status = response.StatusCode;
        }

        return (status, stopped.ToList());
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dhadgar.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Dhadgar.sln not found above the test output directory.");
    }

    private static IEnumerable<string> EnumerateScanFiles(string root)
    {
        foreach (var folder in new[] { "deploy", ".github" })
        {
            var path = Path.Combine(root, folder);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "appsettings*.json", SearchOption.AllDirectories))
        {
            if (!IsBuildOutput(root, file))
            {
                yield return file;
            }
        }

        foreach (var file in Directory.EnumerateFiles(root, "Dockerfile*", SearchOption.AllDirectories))
        {
            if (!IsBuildOutput(root, file))
            {
                yield return file;
            }
        }
    }

    private static bool IsBuildOutput(string root, string file) =>
        Path.GetRelativePath(root, file)
            .Split(Path.DirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or "node_modules" or ".git");
}
