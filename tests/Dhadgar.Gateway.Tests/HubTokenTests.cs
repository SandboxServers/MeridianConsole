using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Dhadgar.Gateway.Tests;

/// <summary>
/// Gateway factory that validates JWTs with a static test key instead of OIDC discovery.
/// Issuer and audience match the Auth section in appsettings.json.
/// </summary>
public sealed class HubTokenWebApplicationFactory : WebApplicationFactory<Program>
{
    internal const string Issuer = "https://meridianconsole.com/api/v1/identity";
    internal const string Audience = "meridian-api";

    private static readonly byte[] KeyBytes = Encoding.UTF8.GetBytes("gateway-hub-test-key-0123456789abcdef");

    private readonly CapturingLoggerProvider _logs = new();

    /// <summary>
    /// Every log message and scope rendered by the host during the tests, for asserting that tokens never reach logs.
    /// </summary>
    internal CapturingLoggerProvider Logs => _logs;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(_logs);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
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
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logs.Dispose();
        }

        base.Dispose(disposing);
    }

    internal static string CreateUserToken()
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(KeyBytes), SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())])
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}

/// <summary>
/// Keeps the rendered text of every log entry and scope so tests can search it.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    /// <summary>
    /// Gets the rendered log messages, exceptions and scope states captured so far.
    /// </summary>
    internal IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            entries.Enqueue(state.ToString() ?? string.Empty);
            return NoopScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
        }
    }

    private sealed class NoopScope : IDisposable
    {
        internal static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }
}

public sealed class HubTokenTests : IClassFixture<HubTokenWebApplicationFactory>
{
    private readonly HubTokenWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public HubTokenTests(HubTokenWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task HubNegotiate_WithQueryToken_IsNotUnauthorized()
    {
        var token = HubTokenWebApplicationFactory.CreateUserToken();

        // The console backend is not running in tests, so 502/503 is expected; 401 would mean the token was rejected.
        var response = await _client.GetAsync($"/hubs/console/negotiate?access_token={token}");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HubNegotiate_WithQueryToken_TokenNeverAppearsInLogs()
    {
        var token = HubTokenWebApplicationFactory.CreateUserToken();

        _ = await _client.GetAsync($"/hubs/console/negotiate?access_token={token}");

        var logged = string.Join("\n", _factory.Logs.Entries);
        Assert.DoesNotContain(token, logged, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token=", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HubNegotiate_WithoutToken_IsUnauthorized()
    {
        var response = await _client.GetAsync("/hubs/console/negotiate");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
