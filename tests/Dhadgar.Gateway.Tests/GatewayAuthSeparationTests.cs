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
    public async Task QueryToken_OnlyCountsOnConsoleHub()
    {
        var token = SeamJwt.CreateUserToken(Guid.NewGuid(), OrganizationId);
        using var capture = new CaptureHandler();
        using var app = CreateApp(_factory, capture);

        using var rejected = await SendAsync(app, HttpMethod.Get, $"{OrgNodesPath}?access_token={token}", certificate: null);

        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Empty(capture.Requests);

        using var twin = await SendAsync(app, HttpMethod.Get, $"{ConsoleHubNegotiatePath}&access_token={token}", certificate: null);

        Assert.NotEqual(HttpStatusCode.Unauthorized, twin.StatusCode);
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


}
