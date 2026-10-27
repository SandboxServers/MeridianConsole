using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Yarp.ReverseProxy.Forwarder;

namespace Dhadgar.Gateway.Tests;

/// <summary>
/// PerTenant token-bucket behaviour on a tenant route (<c>servers-route</c>). Requests are authenticated by a
/// test-only scheme that reads the org from the <c>X-Test-Org-Id</c> header, so the test can pick the org per request.
/// </summary>
[Collection("Gateway Integration")]
public class PerTenantRateLimitingTests
{
    private const string TenantRoute = "/api/v1/servers/x";
    private const string OrgHeader = "X-Test-Org-Id";

    private readonly GatewayWebApplicationFactory _factory;

    public PerTenantRateLimitingTests(GatewayWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PerTenantRoute_Unauthenticated_FloodIsRateLimited()
    {
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture);
        var remoteIp = IPAddress.Parse("203.0.113.50");
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 3; i++)
        {
            using var response = await SendTenantRouteAsync(app, remoteIp);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests },
            statuses);
        Assert.Empty(capture.Requests);
    }

    [Fact]
    public async Task PerTenant_SameOrgFromTwoIps_SharesBucket()
    {
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture);
        var statuses = new List<HttpStatusCode>();
        var ips = new[] { "203.0.113.51", "203.0.113.52", "203.0.113.53" };

        foreach (var ip in ips)
        {
            using var response = await SendTenantRouteAsync(app, IPAddress.Parse(ip), "org-a");
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests },
            statuses);
        Assert.Equal(2, capture.Requests.Count);
    }

    [Fact]
    public async Task PerTenant_TwoOrgsFromOneIp_SeparateBuckets()
    {
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture);
        var remoteIp = IPAddress.Parse("203.0.113.54");
        var statuses = new List<HttpStatusCode>();

        foreach (var org in new[] { "org-a", "org-a", "org-b" })
        {
            using var response = await SendTenantRouteAsync(app, remoteIp, org);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK },
            statuses);

        using (var request = new HttpRequestMessage(HttpMethod.Get, TenantRoute))
        {
            request.Headers.TryAddWithoutValidation(OrgHeader, "org-a");
            using var response = await SendAsync(app, request, remoteIp);
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        Assert.Equal(3, capture.Requests.Count);
    }

    private WebApplicationFactory<Program> CreateApp(CaptureHandler capture)
    {

        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("ReverseProxy:Clusters:servers:Destinations:d1:Address", "http://servers.test/"),
                    new KeyValuePair<string, string?>("ReverseProxy:Clusters:servers:HealthCheck:Active:Enabled", "false"),
                    new KeyValuePair<string, string?>("RateLimiting:Policies:PerTenant:PermitLimit", "2"),
                    new KeyValuePair<string, string?>("RateLimiting:Policies:PerTenant:ReplenishPerSecond", "1"),
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IForwarderHttpClientFactory>(new CaptureForwarderHttpClientFactory(capture));
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestOrgAuthHandler>("Test", _ => { });
                services.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultScheme = "Test";
                    o.DefaultAuthenticateScheme = "Test";
                    o.DefaultChallengeScheme = "Test";
                });
            });
        });
    }

    /// <summary>
    /// Sends one GET to the tenant route. The request is created and disposed here, so loops do not own a disposable.
    /// </summary>
    private static async Task<HttpResponseMessage> SendTenantRouteAsync(
        WebApplicationFactory<Program> app,
        IPAddress remoteIp,
        string? org = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, TenantRoute);
        if (org is not null)
        {
            request.Headers.TryAddWithoutValidation(OrgHeader, org);
        }

        return await SendAsync(app, request, remoteIp);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        WebApplicationFactory<Program> app,
        HttpRequestMessage request,
        IPAddress remoteIp)
    {
        var handler = app.Server.CreateHandler(ctx =>
        {
            ctx.Connection.RemoteIpAddress = remoteIp;
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        return await client.SendAsync(request);
    }

    private sealed record CapturedRequest(Uri? Uri);

    /// <summary>Records every request YARP proxies and answers 200 without touching the network.</summary>
    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly List<CapturedRequest> _requests = [];

        public List<CapturedRequest> Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/healthz")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            _requests.Add(new CapturedRequest(request.RequestUri));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class CaptureForwarderHttpClientFactory(CaptureHandler handler) : IForwarderHttpClientFactory, IDisposable
    {
        private readonly HttpMessageInvoker _invoker = new(handler);

        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => _invoker;

        public void Dispose() => _invoker.Dispose();
    }

    /// <summary>
    /// Test-only authentication. Succeeds when the request carries <c>X-Test-Org-Id</c>, with the org as the
    /// <c>org_id</c> claim; otherwise reports no result so the request stays unauthenticated.
    /// </summary>
    public sealed class TestOrgAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(OrgHeader, out var org) || string.IsNullOrWhiteSpace(org))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim("sub", "test-user"),
                new Claim("org_id", org.ToString()),
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
