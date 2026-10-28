using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Dhadgar.Contracts.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using CaptureHandler = Dhadgar.Gateway.Tests.GatewayCaptureHost.CaptureHandler;

namespace Dhadgar.Gateway.Tests;

/// <summary>
/// Proves that user JWTs and agent certificates do not open each other's routes, and that neither
/// credential reaches the Gateway logs. Every rejection asserts the Gateway's status and that the
/// forwarder recorded nothing; every rejection has a positive twin that differs only in the credential.
/// </summary>
[Collection("Gateway Integration")]
public class GatewayAuthSeparationTests
{
    private const string AgentHeartbeatPath = "/api/v1/agents/3f2a/heartbeat";
    private const string AgentHubNegotiatePath = "/hubs/agent/negotiate";
    private const string ConsoleHubNegotiatePath = "/hubs/console/negotiate?negotiateVersion=1";
    private const string ConsoleOrgSessionPath = "/api/v1/organizations/6b1c0f7e-3d2a-4c8e-9f10-2a5b7c9d1e3f/servers/1d9e4a7b-5c2f-4e8a-8b3d-7f6a2c1e9b04/console/session";
    private const string SecretsPath = "/api/v1/secrets/vault-key";
    private const string NotificationsPath = "/api/v1/notifications/inbox";
    private static readonly Guid OrganizationId = Guid.Parse("6b1c0f7e-3d2a-4c8e-9f10-2a5b7c9d1e3f");
    private static readonly string OrgNodesPath = $"/api/v1/organizations/{OrganizationId}/nodes";

    private readonly GatewayWebApplicationFactory _factory;

    public GatewayAuthSeparationTests(GatewayWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UserJwt_OnAgentRoute_IsRejected()
    {
        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), OrganizationId);
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture);

        using var rejected = await SendAsync(app, HttpMethod.Post, AgentHeartbeatPath, certificate: null, bearerToken: token);

        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Empty(capture.Requests);

        using var cert = GatewayCaptureHost.CreateCertificate(AgentCertificateProfile.CaCommonName);
        using var twin = await SendAsync(app, HttpMethod.Post, AgentHeartbeatPath, cert);

        Assert.Equal(HttpStatusCode.OK, twin.StatusCode);
        Assert.Single(capture.Requests);
    }

    [Fact]
    public async Task UserJwt_OnAgentHub_IsRejected()
    {
        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), OrganizationId);
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture);

        using var rejected = await SendAsync(app, HttpMethod.Post, AgentHubNegotiatePath, certificate: null, bearerToken: token);

        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Empty(capture.Requests);

        using var cert = GatewayCaptureHost.CreateCertificate(AgentCertificateProfile.CaCommonName);
        using var twin = await SendAsync(app, HttpMethod.Post, AgentHubNegotiatePath, cert);

        Assert.Equal(HttpStatusCode.OK, twin.StatusCode);
        Assert.Single(capture.Requests);
    }

    [Fact]
    public async Task AgentCertificate_OnUserRoute_IsRejected()
    {
        using var cert = GatewayCaptureHost.CreateCertificate(AgentCertificateProfile.CaCommonName);
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture);

        using var rejected = await SendAsync(app, HttpMethod.Get, OrgNodesPath, cert);

        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Empty(capture.Requests);

        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), OrganizationId);
        using var twin = await SendAsync(app, HttpMethod.Get, OrgNodesPath, certificate: null, bearerToken: token);

        Assert.Equal(HttpStatusCode.OK, twin.StatusCode);
        Assert.Single(capture.Requests);
    }

    [Theory]
    [InlineData(ConsoleHubNegotiatePath)]
    [InlineData(ConsoleOrgSessionPath)]
    [InlineData(SecretsPath)]
    [InlineData(NotificationsPath)]
    public async Task AgentCertificate_OnOtherTenantRoutes_IsRejected(string path)
    {
        using var cert = GatewayCaptureHost.CreateCertificate(AgentCertificateProfile.CaCommonName);
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture);

        using var rejected = await SendAsync(app, HttpMethod.Get, path, cert);

        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Empty(capture.Requests);

        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), OrganizationId);
        using var twin = await SendAsync(app, HttpMethod.Get, path, certificate: null, bearerToken: token);

        Assert.Equal(HttpStatusCode.OK, twin.StatusCode);
        Assert.Single(capture.Requests);
    }

    [Fact]
    public async Task BothCredentials_OnUserRoute_ForwardNoClientCertHeader()
    {
        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), OrganizationId);
        using var cert = GatewayCaptureHost.CreateCertificate(AgentCertificateProfile.CaCommonName);
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture);

        using var response = await SendAsync(app, HttpMethod.Get, OrgNodesPath, cert, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proxied = Assert.Single(capture.Requests);
        Assert.False(proxied.HasHeader(AgentCertificateProfile.ForwardedCertificateHeader));
    }

    [Fact]
    public async Task BothCredentials_OnAgentRoute_ForwardsAuthorizationHeader()
    {
        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), OrganizationId);
        using var cert = GatewayCaptureHost.CreateCertificate(AgentCertificateProfile.CaCommonName);
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture);

        using var response = await SendAsync(app, HttpMethod.Post, AgentHeartbeatPath, cert, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proxied = Assert.Single(capture.Requests);
        Assert.True(proxied.HasHeader("Authorization"));
    }

    [Fact]
    public async Task SpoofedClientCertHeader_OnUserRoute_IsStripped()
    {
        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), OrganizationId);
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture);

        using var response = await SendWithHeaderAsync(
            app, HttpMethod.Get, OrgNodesPath, certificate: null, token,
            AgentCertificateProfile.ForwardedCertificateHeader, "abc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proxied = Assert.Single(capture.Requests);
        Assert.False(proxied.HasHeader(AgentCertificateProfile.ForwardedCertificateHeader));
    }

    [Fact]
    public async Task SpoofedClientCertHeader_OnAgentRoute_IsReplacedByConnectionCertificate()
    {
        using var cert = GatewayCaptureHost.CreateCertificate(AgentCertificateProfile.CaCommonName);
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture);

        using var response = await SendWithHeaderAsync(
            app, HttpMethod.Post, AgentHeartbeatPath, cert, bearerToken: null, "x-client-cert", "spoofed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proxied = Assert.Single(capture.Requests);
        Assert.Equal(Convert.ToBase64String(cert.RawData), proxied.Header(AgentCertificateProfile.ForwardedCertificateHeader));
    }

    [Fact]
    public async Task SpoofedClientCertHeader_OnAgentRoute_WithoutCertificate_IsRejected()
    {
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture);

        using var rejected = await SendWithHeaderAsync(
            app, HttpMethod.Post, AgentHeartbeatPath, certificate: null, bearerToken: null,
            AgentCertificateProfile.ForwardedCertificateHeader, "abc");

        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Empty(capture.Requests);

        using var cert = GatewayCaptureHost.CreateCertificate(AgentCertificateProfile.CaCommonName);
        using var twin = await SendAsync(app, HttpMethod.Post, AgentHeartbeatPath, cert);

        Assert.Equal(HttpStatusCode.OK, twin.StatusCode);
        Assert.Single(capture.Requests);
    }

    [Fact]
    public async Task QueryToken_OnlyCountsOnConsoleHub()
    {
        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), OrganizationId);
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture);

        using var rejected = await SendAsync(app, HttpMethod.Get, $"{OrgNodesPath}?access_token={token}", certificate: null);

        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Empty(capture.Requests);

        // The query token authenticates the console hub; the forwarder then reaches the console cluster,
        // which the capture handler answers 200.
        using var twin = await SendAsync(app, HttpMethod.Post, $"{ConsoleHubNegotiatePath}&access_token={token}", certificate: null);

        Assert.Equal(HttpStatusCode.OK, twin.StatusCode);
        Assert.Single(capture.Requests);
    }

    [Fact]
    public async Task QueryToken_IsIgnoredOnRestConsoleRoute()
    {
        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), OrganizationId);
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture);

        using var rejected = await SendAsync(app, HttpMethod.Get, $"{ConsoleOrgSessionPath}?access_token={token}", certificate: null);

        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Empty(capture.Requests);

        using var twin = await SendAsync(app, HttpMethod.Get, ConsoleOrgSessionPath, certificate: null, bearerToken: token);

        Assert.Equal(HttpStatusCode.OK, twin.StatusCode);
        Assert.Single(capture.Requests);
    }

    [Fact]
    public async Task QueryToken_IgnoredWhenAuthorizationHeaderIsPresent()
    {
        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), OrganizationId);
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture);

        // An Authorization header that carries no bearer token must not fall back to the query token.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ConsoleHubNegotiatePath}&access_token={token}");
        request.Headers.TryAddWithoutValidation("Authorization", "Basic c2VjcmV0");
        using var rejected = await GatewayCaptureHost.SendAsync(app, request, certificate: null);

        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Empty(capture.Requests);

        using var twin = await SendAsync(app, HttpMethod.Post, ConsoleHubNegotiatePath, certificate: null, bearerToken: token);

        Assert.Equal(HttpStatusCode.OK, twin.StatusCode);
        Assert.Single(capture.Requests);
    }

    [Fact]
    public async Task QueryToken_DoesNotOpenAgentRoutes()
    {
        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), OrganizationId);
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture);

        using var rejected = await SendAsync(app, HttpMethod.Post, $"{AgentHubNegotiatePath}?access_token={token}", certificate: null);

        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Empty(capture.Requests);

        using var cert = GatewayCaptureHost.CreateCertificate(AgentCertificateProfile.CaCommonName);
        using var twin = await SendAsync(app, HttpMethod.Post, AgentHubNegotiatePath, cert);

        Assert.Equal(HttpStatusCode.OK, twin.StatusCode);
        Assert.Single(capture.Requests);
    }

    [Fact]
    public async Task NoSecretsInGatewayLogs()
    {
        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), OrganizationId);
        using var cert = GatewayCaptureHost.CreateCertificate(AgentCertificateProfile.CaCommonName);
        var der = Convert.ToBase64String(cert.RawData);
        var pemBodyLines = cert.ExportCertificatePem()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("-----", StringComparison.Ordinal))
            .ToArray();

        var logs = new CapturingLoggerProvider();
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture, logs);

        using (await SendAsync(app, HttpMethod.Get, OrgNodesPath, cert, token)) { }
        using (await SendAsync(app, HttpMethod.Get, $"{OrgNodesPath}?access_token={token}", certificate: null)) { }
        using (await SendAsync(app, HttpMethod.Post, $"{AgentHubNegotiatePath}?access_token={token}", cert)) { }
        using (await SendAsync(app, HttpMethod.Post, AgentHeartbeatPath, certificate: null, bearerToken: token)) { }
        using (await SendAsync(app, HttpMethod.Post, $"{ConsoleHubNegotiatePath}&access_token={token}", certificate: null)) { }

        Assert.NotEmpty(logs.Entries);
        var rendered = string.Join("\n", logs.Entries);
        Assert.DoesNotContain(token, rendered);
        Assert.DoesNotContain(der, rendered);
        Assert.DoesNotContain("-----BEGIN CERTIFICATE-----", rendered);
        foreach (var line in pemBodyLines)
        {
            Assert.DoesNotContain(line, rendered);
        }
    }

    private static WebApplicationFactory<Program> CreateApp(
        GatewayWebApplicationFactory factory,
        CaptureHandler capture,
        CapturingLoggerProvider? logs = null)
    {
        return GatewayCaptureHost.CreateApp(factory, capture, configureServices: services =>
        {
            SeamJwt.Apply(services);
            if (logs is not null)
            {
                services.AddSingleton<ILoggerProvider>(logs);
            }
        });
    }

    private static async Task<HttpResponseMessage> SendAsync(
        WebApplicationFactory<Program> app,
        HttpMethod method,
        string path,
        X509Certificate2? certificate,
        string? bearerToken = null)
    {
        using var request = new HttpRequestMessage(method, path);
        return await GatewayCaptureHost.SendAsync(app, request, certificate, bearerToken: bearerToken);
    }

    private static async Task<HttpResponseMessage> SendWithHeaderAsync(
        WebApplicationFactory<Program> app,
        HttpMethod method,
        string path,
        X509Certificate2? certificate,
        string? bearerToken,
        string headerName,
        string headerValue)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(headerName, headerValue);
        return await GatewayCaptureHost.SendAsync(app, request, certificate, bearerToken: bearerToken);
    }
}
