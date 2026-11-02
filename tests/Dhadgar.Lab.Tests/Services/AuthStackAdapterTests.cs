using Dhadgar.Lab.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dhadgar.Lab.Tests.Services;

/// <summary>Environment built by the Identity, BetterAuth and Panel adapters and the Gateway additions (L34). No Docker.</summary>
public sealed class AuthStackAdapterTests
{
    private const string PublicPem = "-----BEGIN PUBLIC KEY-----\nAAAA\nBBBB\n-----END PUBLIC KEY-----\n";
    private const string PrivatePem = "-----BEGIN PRIVATE KEY-----\r\nCCCC\r\nDDDD\r\n-----END PRIVATE KEY-----\r\n";

    private static LabServiceContext Context(bool withKeys = true) =>
        new(
            "a9c623a1",
            new Dictionary<string, string>
            {
                ["identity"] = "Host=meridian-lab-toxiproxy;Port=20030;Database=identity;Username=u;Password=pw",
                ["identity-redis"] = "meridian-lab-toxiproxy:20031,password=p,abortConnect=false",
                ["betterauth"] = "Host=meridian-lab-toxiproxy;Port=20033;Database=betterauth;Username=ba;Password=bapw",
            },
            "C:\\lab\\state\\nodes-ca",
            "lab_user",
            "lab-password-value",
            withKeys ? PublicPem : null,
            withKeys ? PrivatePem : null,
            withKeys ? "ba-secret-value" : null);

    [Fact]
    public void Identity_ReachesDependencies_ThroughToxiproxy_AndEscapesPublicKey()
    {
        var env = new IdentityLabAdapter().Environment(Context());

        Assert.True(env.IsSuccess, env.Error);
        Assert.Contains("Port=20030", env.Value!["ConnectionStrings__Postgres"]);
        Assert.Contains("20031", env.Value["Redis__ConnectionString"]);
        Assert.Equal("20032", env.Value["RabbitMq__Port"]);
        Assert.Equal("-----BEGIN PUBLIC KEY-----\\nAAAA\\nBBBB\\n-----END PUBLIC KEY-----\\n", env.Value["Auth__Exchange__PublicKeyPem"]);
        Assert.Equal(IdentityLabAdapter.ExchangeIssuer, env.Value["Auth__Exchange__Issuer"]);
        Assert.DoesNotContain(env.Value.Values, v => v.Contains('\n', StringComparison.Ordinal));
    }

    [Fact]
    public void Identity_WithoutPublicKey_IsInvalidArgument()
    {
        var env = new IdentityLabAdapter().Environment(Context(withKeys: false));

        Assert.True(env.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", env.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Identity_UsesDevelopmentCertificates_AndBlankVaultUri()
    {
        var env = new IdentityLabAdapter().Environment(Context());

        Assert.True(env.IsSuccess, env.Error);
        Assert.Equal("true", env.Value![IdentityLabAdapter.UseDevelopmentCertificatesKey]);
        Assert.True(env.Value.TryGetValue(IdentityLabAdapter.KeyVaultUriKey, out string? vault), "the Key Vault URI key must be present (empty) to override appsettings");
        Assert.Equal(string.Empty, vault);
        Assert.DoesNotContain(IdentityLabAdapter.UseDevelopmentCertificatesKey.Replace("__", ":", StringComparison.Ordinal), new IdentityLabAdapter().OverlayKeys);
        Assert.DoesNotContain(IdentityLabAdapter.KeyVaultUriKey.Replace("__", ":", StringComparison.Ordinal), new IdentityLabAdapter().OverlayKeys);
    }

    [Fact]
    public void Identity_OverItsAppsettings_SelectsDevelopmentCertificates_AndNoVault()
    {
        string appsettings = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "src", "Dhadgar.Identity", "appsettings.json");
        var env = new IdentityLabAdapter().Environment(Context());
        Assert.True(env.IsSuccess, env.Error);

        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile(appsettings, optional: false, reloadOnChange: false)
            .AddInMemoryCollection(env.Value!.Select(pair => new KeyValuePair<string, string?>(pair.Key.Replace("__", ":", StringComparison.Ordinal), pair.Value)))
            .Build();

        // Mirrors Program.cs: GetValue<bool>("Auth:UseDevelopmentCertificates") picks the certificate branch.
        Assert.True(config.GetValue<bool>("Auth:UseDevelopmentCertificates"), "Identity would take the Key Vault certificate branch (F131)");
        Assert.True(string.IsNullOrWhiteSpace(config["Auth:KeyVault:VaultUri"]), "appsettings' Key Vault URI is still in force");
    }

    [Fact]
    public void Identity_KeyVaultConsumers_AreTheKnownFour()
    {
        string root = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "src", "Dhadgar.Identity");
        string sep = Path.DirectorySeparatorChar.ToString();
        var consumers = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(sep + "bin" + sep, StringComparison.Ordinal) && !path.Contains(sep + "obj" + sep, StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("new DefaultAzureCredential(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "Program.cs", "Services/ExchangeTokenValidator.cs", "Services/SigningKeyProvider.cs", "Services/WebhookSecretProvider.cs" }, consumers);

        string program = File.ReadAllText(Path.Combine(root, "Program.cs"));
        Assert.Contains("GetValue<bool>(\"Auth:UseDevelopmentCertificates\")", program, StringComparison.Ordinal);
        Assert.Contains("|| useDevelopmentCertificates)", program, StringComparison.Ordinal);
        foreach (string service in consumers.Where(name => name.StartsWith("Services/", StringComparison.Ordinal)))
        {
            Assert.Contains("VaultUri", File.ReadAllText(Path.Combine(root, service)), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BetterAuth_PrivateKeyIsSingleLine_AndPostgresPartsAreSplit()
    {
        var env = new BetterAuthLabAdapter().Environment(Context());

        Assert.True(env.IsSuccess, env.Error);
        Assert.Equal("-----BEGIN PRIVATE KEY-----CCCCDDDD-----END PRIVATE KEY-----", env.Value!["EXCHANGE_TOKEN_PRIVATE_KEY"]);
        Assert.Equal("meridian-lab-toxiproxy", env.Value["POSTGRES_HOST"]);
        Assert.Equal("20033", env.Value["POSTGRES_PORT"]);
        Assert.Equal("betterauth", env.Value["POSTGRES_DATABASE"]);
        Assert.Equal("ba-secret-value", env.Value["BETTER_AUTH_SECRET"]);
        Assert.Equal(IdentityLabAdapter.ExchangeIssuer, env.Value["EXCHANGE_TOKEN_ISSUER"]);
        Assert.Equal("http://127.0.0.1:8099", env.Value["SECRETS_SERVICE_URL"]);
        Assert.Equal("8099", env.Value["LAB_SECRETS_STUB_PORT"]);
        Assert.Equal("--import=file:///lab/secrets-stub.mjs", env.Value["NODE_OPTIONS"]);
        Assert.Equal("1", env.Value["USE_ENV_DB_PASSWORD"]);
        Assert.False(env.Value.ContainsKey("IDENTITY_SERVICE_URL"));
        Assert.DoesNotContain(env.Value.Keys, k => k.StartsWith("SERVICE_CLIENT_", StringComparison.Ordinal));
        Assert.DoesNotContain(env.Value.Values, v => v.Contains('\n', StringComparison.Ordinal));
    }

    [Fact]
    public void Context_ToString_PrintsNoSecret()
    {
        string text = Context().ToString();

        Assert.Contains("a9c623a1", text, StringComparison.Ordinal);
        foreach (string secret in new[] { "CCCC", "ba-secret-value", "lab-password-value", "bapw", "Password=" })
        {
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BetterAuth_MissingSecretOrKey_IsInvalidArgument()
    {
        var env = new BetterAuthLabAdapter().Environment(Context(withKeys: false));

        Assert.True(env.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", env.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void BetterAuth_MalformedConnectionString_ErrorDoesNotRepeatPassword()
    {
        var ctx = Context() with
        {
            ConnectionStrings = new Dictionary<string, string> { ["betterauth"] = "Host=h;Password=topsecret-pw" },
        };

        var env = new BetterAuthLabAdapter().Environment(ctx);

        Assert.True(env.IsFailure);
        Assert.DoesNotContain("topsecret-pw", env.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Panel_HasNoProxies_AndListensOnLabPort()
    {
        var adapter = new PanelLabAdapter();

        Assert.Equal(14321, adapter.HostPort);
        Assert.Empty(adapter.Proxies);
        Assert.Equal("/login", adapter.HealthPath);
        Assert.True(adapter.Environment(Context()).IsSuccess);
    }

    [Fact]
    public void Gateway_AddsIdentityAndBetterAuthClusters_AndRequiresIdentity()
    {
        var adapter = new GatewayAdapter();
        var env = adapter.Environment(Context());

        Assert.True(env.IsSuccess, env.Error);
        Assert.Equal("http://meridian-lab-toxiproxy:20011/", env.Value!["ReverseProxy__Clusters__identity__Destinations__d1__Address"]);
        Assert.Equal("http://meridian-lab-toxiproxy:20013/", env.Value["ReverseProxy__Clusters__betterauth__Destinations__d1__Address"]);
        Assert.Equal("nodes", env.Value["Readyz__RequiredClusters__0"]);
        Assert.Equal("identity", env.Value["Readyz__RequiredClusters__1"]);
        Assert.Contains("gateway-identity", adapter.Proxies);
        Assert.Contains("gateway-betterauth", adapter.Proxies);
    }
}
