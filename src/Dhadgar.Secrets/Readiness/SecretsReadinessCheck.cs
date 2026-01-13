using Dhadgar.Secrets.Options;
using Dhadgar.Secrets.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dhadgar.Secrets.Readiness;

public sealed class SecretsReadinessCheck : IHealthCheck
{
    private readonly ISecretProvider _secretProvider;
    private readonly ICertificateProvider _certificateProvider;
    private readonly SecretsOptions _secretsOptions;
    private readonly SecretsReadinessOptions _readinessOptions;
    private readonly ILogger<SecretsReadinessCheck> _logger;

    public SecretsReadinessCheck(
        ISecretProvider secretProvider,
        ICertificateProvider certificateProvider,
        IOptions<SecretsOptions> secretsOptions,
        IOptions<SecretsReadinessOptions> readinessOptions,
        ILogger<SecretsReadinessCheck> logger)
    {
        _secretProvider = secretProvider;
        _certificateProvider = certificateProvider;
        _secretsOptions = secretsOptions.Value;
        _readinessOptions = readinessOptions.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var details = new Dictionary<string, object>();

        var secretsReady = await CheckSecretsAsync(details, cancellationToken);
        var certificatesReady = await CheckCertificatesAsync(details, cancellationToken);

        return secretsReady && certificatesReady
            ? HealthCheckResult.Healthy(data: details)
            : HealthCheckResult.Unhealthy(data: details);
    }

    private async Task<bool> CheckSecretsAsync(Dictionary<string, object> details, CancellationToken ct)
    {
        if (_secretProvider is DevelopmentSecretProvider)
        {
            details["secrets_provider"] = "development";
            details["secrets"] = "ok";
            return true;
        }

        details["secrets_provider"] = "keyvault";

        var probeSecret = ResolveProbeSecretName();
        if (string.IsNullOrWhiteSpace(probeSecret))
        {
            details["secrets"] = "unconfigured";
            details["secrets_error"] = "No allowed secrets configured to probe.";
            return false;
        }

        try
        {
            await _secretProvider.GetSecretAsync(probeSecret, ct);
            details["secrets"] = "ok";
            details["probe_secret"] = probeSecret;
            return true;
        }
        catch (Exception ex)
        {
            details["secrets"] = "error";
            details["probe_secret"] = probeSecret;
            details["secrets_error"] = ex.Message;
            _logger.LogWarning(ex, "Secrets readiness probe failed.");
            return false;
        }
    }

    private async Task<bool> CheckCertificatesAsync(Dictionary<string, object> details, CancellationToken ct)
    {
        if (!_readinessOptions.CheckCertificates)
        {
            details["certificates"] = "skipped";
            return true;
        }

        try
        {
            await _certificateProvider.ListCertificatesAsync(ct: ct);
            details["certificates"] = "ok";
            return true;
        }
        catch (Exception ex)
        {
            details["certificates"] = "error";
            details["certificates_error"] = ex.Message;
            _logger.LogWarning(ex, "Certificate readiness check failed.");
            return false;
        }
    }

    private string? ResolveProbeSecretName()
    {
        if (!string.IsNullOrWhiteSpace(_readinessOptions.ProbeSecretName))
        {
            return _readinessOptions.ProbeSecretName;
        }

        var oauth = _secretsOptions.AllowedSecrets.OAuth.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(oauth))
        {
            return oauth;
        }

        var betterAuth = _secretsOptions.AllowedSecrets.BetterAuth.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(betterAuth))
        {
            return betterAuth;
        }

        return _secretsOptions.AllowedSecrets.Infrastructure.FirstOrDefault();
    }
}
