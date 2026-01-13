using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dhadgar.Secrets.Options;
using Dhadgar.Secrets.Readiness;
using Dhadgar.Secrets.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace Dhadgar.Secrets.Tests;

public class ReadinessTests
{
    [Fact]
    public async Task ReadyWhenSecretsAndCertificatesAreHealthy()
    {
        var secretsOptions = BuildSecretsOptions();
        var readinessOptions = new SecretsReadinessOptions();
        var check = new SecretsReadinessCheck(
            new FakeSecretProvider(),
            new FakeCertificateProvider(),
            OptionsFactory.Create(secretsOptions),
            OptionsFactory.Create(readinessOptions),
            CreateLogger());

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task NotReadyWhenSecretsProbeFails()
    {
        var secretsOptions = BuildSecretsOptions();
        var readinessOptions = new SecretsReadinessOptions();
        var check = new SecretsReadinessCheck(
            new ThrowingSecretProvider(),
            new FakeCertificateProvider(),
            OptionsFactory.Create(secretsOptions),
            OptionsFactory.Create(readinessOptions),
            CreateLogger());

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task ReadyWhenCertificatesCheckIsDisabled()
    {
        var secretsOptions = BuildSecretsOptions();
        var readinessOptions = new SecretsReadinessOptions
        {
            CheckCertificates = false
        };
        var check = new SecretsReadinessCheck(
            new FakeSecretProvider(),
            new ThrowingCertificateProvider(),
            OptionsFactory.Create(secretsOptions),
            OptionsFactory.Create(readinessOptions),
            CreateLogger());

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private static SecretsOptions BuildSecretsOptions()
    {
        return new SecretsOptions
        {
            KeyVaultUri = "https://example.vault.azure.net/",
            AllowedSecrets = new AllowedSecretsOptions
            {
                OAuth = new List<string> { "oauth-steam-api-key" },
                BetterAuth = new List<string>(),
                Infrastructure = new List<string>()
            }
        };
    }

    private static NullLogger<SecretsReadinessCheck> CreateLogger()
    {
        return NullLogger<SecretsReadinessCheck>.Instance;
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
            throw new InvalidOperationException("boom");
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
            throw new InvalidOperationException("cert boom");
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
