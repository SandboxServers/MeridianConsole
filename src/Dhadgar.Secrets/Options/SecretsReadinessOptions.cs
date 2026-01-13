namespace Dhadgar.Secrets.Options;

public sealed class SecretsReadinessOptions
{
    public string? ProbeSecretName { get; init; }
    public bool CheckCertificates { get; init; } = true;
}
