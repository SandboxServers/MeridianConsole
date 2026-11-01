using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dhadgar.Gateway.Tests;

/// <summary>
/// Guards the authorization wiring of every Gateway route: each route names a registered policy (the Gateway has
/// no FallbackPolicy), only certificate-forwarding routes use <c>AgentCertificate</c>, the JWT <c>Agent</c> policy
/// is gone, and the internal command path is not routed (C-1).
/// </summary>
[Collection("Gateway Integration")]
public class RouteAuthorizationGuardTests
{
    private const string AnonymousPolicy = "Anonymous";
    private const string AgentCertificatePolicy = "AgentCertificate";

    private readonly GatewayWebApplicationFactory _factory;

    public RouteAuthorizationGuardTests(GatewayWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EveryRoute_NamesARegisteredPolicyOrAnonymous()
    {
        var routes = LoadRoutes();
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        var unnamed = new List<string>();
        var unregistered = new List<string>();
        foreach (var route in routes)
        {
            var policy = route.GetValue<string>("AuthorizationPolicy");
            if (string.IsNullOrWhiteSpace(policy))
            {
                unnamed.Add(route.Key);
                continue;
            }

            if (policy == AnonymousPolicy)
            {
                continue;
            }

            if (await provider.GetPolicyAsync(policy) is null)
            {
                unregistered.Add($"{route.Key}={policy}");
            }
        }

        Assert.Empty(unnamed);
        Assert.Empty(unregistered);
    }

    [Fact]
    public void AgentCertificate_IsUsedExactlyByForwardingRoutes()
    {
        var mismatches = LoadRoutes()
            .Where(route =>
                (route.GetValue<string>("AuthorizationPolicy") == AgentCertificatePolicy) !=
                (route.GetValue<string>("Metadata:ForwardClientCertificate") == "true"))
            .Select(route => route.Key)
            .ToList();

        Assert.Empty(mismatches);
    }

    [Fact]
    public async Task JwtAgentPolicy_IsNotRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.Null(await provider.GetPolicyAsync("Agent"));
    }

    [Fact]
    public async Task InternalCommandsPath_IsNotRouted()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            "/api/v1/internal/organizations/6f1d2c3b-0a9e-4b8d-9c7f-1e2d3c4b5a69/nodes/7a2e3d4c-1b0f-4c9e-8d8a-2f3e4d5c6b7a/commands");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OrgNodesPath_IsRouted()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            "/api/v1/organizations/6f1d2c3b-0a9e-4b8d-9c7f-1e2d3c4b5a69/nodes/7a2e3d4c-1b0f-4c9e-8d8a-2f3e4d5c6b7a");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static IConfigurationSection[] LoadRoutes()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(GetGatewayAppSettingsPath(), optional: false)
            .Build();

        return configuration.GetSection("ReverseProxy:Routes").GetChildren().ToArray();
    }

    private static string GetGatewayAppSettingsPath()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        return Path.Combine(solutionRoot, "src", "Dhadgar.Gateway", "appsettings.json");
    }

    private static string FindSolutionRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Dhadgar.sln")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new InvalidOperationException("Could not locate Dhadgar.sln to load gateway configuration.");
        }

        return directory.FullName;
    }
}
