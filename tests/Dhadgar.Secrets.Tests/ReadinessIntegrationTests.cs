using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Dhadgar.Secrets.Options;
using Dhadgar.Secrets.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Dhadgar.Secrets.Tests;

public sealed class ReadinessIntegrationTests : IClassFixture<SecretsWebApplicationFactory>
{
    private readonly SecretsWebApplicationFactory _factory;

    public ReadinessIntegrationTests(SecretsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReadyzReturnsOkWhenDependenciesHealthy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyzReturnsServiceUnavailableWhenSecretProbeFails()
    {
        using var client = _factory.CreateClientWithOverrides(secretProbeSucceeds: false, certificateProbeSucceeds: true);

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ReadyzReturnsServiceUnavailableWhenCertificateProbeFails()
    {
        using var client = _factory.CreateClientWithOverrides(secretProbeSucceeds: true, certificateProbeSucceeds: false);

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}

public sealed class SecretsWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Secrets:KeyVaultUri"] = "https://example.vault.azure.net/",
                ["Readiness:ProbeSecretName"] = "oauth-steam-api-key",
                ["Readiness:CheckCertificates"] = "true"
            };

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISecretProvider>();
            services.AddSingleton<ISecretProvider>(new FakeSecretProvider());

            services.RemoveAll<ICertificateProvider>();
            services.AddSingleton<ICertificateProvider>(new FakeCertificateProvider());
        });
    }

    public HttpClient CreateClientWithOverrides(bool secretProbeSucceeds, bool certificateProbeSucceeds)
    {
        var factory = WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISecretProvider>();
                services.AddSingleton<ISecretProvider>(secretProbeSucceeds
                    ? new FakeSecretProvider()
                    : new ThrowingSecretProvider());

                services.RemoveAll<ICertificateProvider>();
                services.AddSingleton<ICertificateProvider>(certificateProbeSucceeds
                    ? new FakeCertificateProvider()
                    : new ThrowingCertificateProvider());
            });
        });

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private sealed class FakeSecretProvider : ISecretProvider
    {
        public Task<string?> GetSecretAsync(string secretName, CancellationToken ct = default)
        {
            return Task.FromResult<string?>("ok");
        }

        public Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> secretNames, CancellationToken ct = default)
        {
            return Task.FromResult(new Dictionary<string, string>());
        }

        public bool IsAllowed(string secretName) => true;

        public Task<bool> SetSecretAsync(string secretName, string value, CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public Task<(string Version, DateTime CreatedAt)> RotateSecretAsync(string secretName, CancellationToken ct = default)
        {
            return Task.FromResult((Guid.NewGuid().ToString("N"), DateTime.UtcNow));
        }

        public Task<bool> DeleteSecretAsync(string secretName, CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class ThrowingSecretProvider : ISecretProvider
    {
        public Task<string?> GetSecretAsync(string secretName, CancellationToken ct = default)
        {
            throw new InvalidOperationException("secret boom");
        }

        public Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> secretNames, CancellationToken ct = default)
        {
            return Task.FromResult(new Dictionary<string, string>());
        }

        public bool IsAllowed(string secretName) => true;

        public Task<bool> SetSecretAsync(string secretName, string value, CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public Task<(string Version, DateTime CreatedAt)> RotateSecretAsync(string secretName, CancellationToken ct = default)
        {
            return Task.FromResult((Guid.NewGuid().ToString("N"), DateTime.UtcNow));
        }

        public Task<bool> DeleteSecretAsync(string secretName, CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class FakeCertificateProvider : ICertificateProvider
    {
        public Task<List<CertificateInfo>> ListCertificatesAsync(string? vaultName = null, CancellationToken ct = default)
        {
            return Task.FromResult(new List<CertificateInfo>());
        }

        public Task<ImportCertificateResult> ImportCertificateAsync(string name, byte[] certificateData, string? password = null, string? vaultName = null, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteCertificateAsync(string name, string? vaultName = null, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ThrowingCertificateProvider : ICertificateProvider
    {
        public Task<List<CertificateInfo>> ListCertificatesAsync(string? vaultName = null, CancellationToken ct = default)
        {
            throw new InvalidOperationException("certificate boom");
        }

        public Task<ImportCertificateResult> ImportCertificateAsync(string name, byte[] certificateData, string? password = null, string? vaultName = null, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteCertificateAsync(string name, string? vaultName = null, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }
    }
}
