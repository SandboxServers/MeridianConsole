using System.Net;
using System.Security.Claims;
using System.Text;
using Dhadgar.ServiceDefaults.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests.Security;

public sealed class ServiceJwtAuthenticationExtensionsTests
{
    private const string Issuer = "t";
    private const string Audience = "a";
    private const string HubPrefix = "/hubs/test";

    private static readonly byte[] KeyBytes = Encoding.UTF8.GetBytes("service-jwt-test-key-0123456789abcdef");

    [Fact]
    public async Task QueryToken_IsAccepted_OnHubPath()
    {
        using var app = await BuildAppAsync(ValidConfig());
        using var client = CreateClient(app);

        var token = CreateToken();
        var response = await client.GetAsync($"{HubPrefix}/x?access_token={token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task QueryToken_IsRejected_OutsideHubPaths()
    {
        using var app = await BuildAppAsync(ValidConfig());
        using var client = CreateClient(app);

        var token = CreateToken();
        var response = await client.GetAsync($"/api/x?access_token={token}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HeaderToken_IsAccepted_OnHubAndApiPaths()
    {
        using var app = await BuildAppAsync(ValidConfig());
        using var client = CreateClient(app);
        var token = CreateToken();

        using var hubRequest = new HttpRequestMessage(HttpMethod.Get, $"{HubPrefix}/x");
        hubRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var apiRequest = new HttpRequestMessage(HttpMethod.Get, "/api/x");
        apiRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var hubResponse = await client.SendAsync(hubRequest);
        var apiResponse = await client.SendAsync(apiRequest);

        Assert.Equal(HttpStatusCode.OK, hubResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);
    }

    [Fact]
    public void MissingIssuer_ThrowsInvalidOperationException()
    {
        var config = BuildConfiguration(new Dictionary<string, string?> { ["Auth:Audience"] = Audience });
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() =>
            services.AddDhadgarJwtBearer(config, CreateEnvironment()));
    }

    [Fact]
    public void MissingAudience_ThrowsInvalidOperationException()
    {
        var config = BuildConfiguration(new Dictionary<string, string?> { ["Auth:Issuer"] = Issuer });
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() =>
            services.AddDhadgarJwtBearer(config, CreateEnvironment()));
    }

    [Fact]
    public async Task MetadataAddress_IsAppliedToJwtBearerOptions()
    {
        var settings = ValidConfig();
        settings["Auth:MetadataAddress"] = "http://identity:8080/.well-known/openid-configuration";
        using var app = await BuildAppAsync(settings);

        var options = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal("http://identity:8080/.well-known/openid-configuration", options.MetadataAddress);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static HttpClient CreateClient(WebApplication app) =>
        (app.Services.GetRequiredService<IServer>() as TestServer ?? throw new InvalidOperationException("No TestServer"))
            .CreateClient();

    private static Dictionary<string, string?> ValidConfig() => new()
    {
        ["Auth:Issuer"] = Issuer,
        ["Auth:Audience"] = Audience
    };

    private static string CreateToken()
    {
        var key = new SymmetricSecurityKey(KeyBytes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())])
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static TestHostEnvironment CreateEnvironment() =>
        new TestHostEnvironment();

    private static async Task<WebApplication> BuildAppAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);

        builder.Services.AddAuthorization();
        builder.Services.AddDhadgarJwtBearer(builder.Configuration, builder.Environment, HubPrefix);

        // Replace discovery with a static signing key so no network is needed.
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            o.Authority = null;
            o.ConfigurationManager = null;
            o.Configuration = new OpenIdConnectConfiguration();
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = Issuer,
                ValidAudience = Audience,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(KeyBytes)
            };
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet($"{HubPrefix}/x", () => Results.Ok("hub")).RequireAuthorization();
        app.MapGet("/api/x", () => Results.Ok("api")).RequireAuthorization();
        await app.StartAsync();
        return app;
    }
}
