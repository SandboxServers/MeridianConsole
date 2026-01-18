namespace Dhadgar.Secrets.Services;

public record CertificateInfo(
    string Name,
    string Subject,
    string Issuer,
    DateTime ExpiresAt,
    string Thumbprint,
    bool Enabled);

public record ImportCertificateResult(
    string Name,
    string Subject,
    string Issuer,
    string Thumbprint,
    DateTime ExpiresAt);

public interface ICertificateProvider
{
    Task<List<CertificateInfo>> ListCertificatesAsync(string? vaultName = null, CancellationToken ct = default);
    Task<ImportCertificateResult> ImportCertificateAsync(string name, byte[] certificateData, string? password = null, string? vaultName = null, CancellationToken ct = default);
    Task<bool> DeleteCertificateAsync(string name, string? vaultName = null, CancellationToken ct = default);
}
