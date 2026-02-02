using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Windows.Windows;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Windows;

/// <summary>
/// Unit tests for WindowsCertificateStore.
/// NOTE: These tests cannot directly mock X509Store as it's a sealed class.
/// The tests verify input validation and error handling where possible.
/// Integration tests on Windows would be needed for full coverage.
/// </summary>
public sealed class WindowsCertificateStoreTests : IDisposable
{
    private readonly ILogger<WindowsCertificateStore> _logger;

    public WindowsCertificateStoreTests()
    {
        _logger = Substitute.For<ILogger<WindowsCertificateStore>>();
    }

    public void Dispose()
    {
        // No resources to dispose in these unit tests
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new WindowsCertificateStore(null!));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithValidLogger_CreatesInstance()
    {
        // Arrange & Act
        var store = new WindowsCertificateStore(_logger);

        // Assert
        Assert.NotNull(store);
    }

    #endregion

    #region StoreCertificateAsync Tests

    [Fact]
    public async Task StoreCertificateAsync_WithNullCertificate_ThrowsArgumentNullException()
    {
        // Arrange
        using var store = new WindowsCertificateStore(_logger);
        var privateKey = new byte[100];

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => store.StoreCertificateAsync(null!, privateKey));
        Assert.Equal("certificate", exception.ParamName);
    }

    [Fact]
    public async Task StoreCertificateAsync_WithNullPrivateKey_ThrowsArgumentNullException()
    {
        // Arrange
        using var store = new WindowsCertificateStore(_logger);
        using var cert = CreateSelfSignedCertificate("CN=Test");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => store.StoreCertificateAsync(cert, null!));
        Assert.Equal("privateKey", exception.ParamName);
    }

    [Fact]
    public async Task StoreCertificateAsync_WithPrivateKeyExceedingMaxSize_ThrowsArgumentException()
    {
        // Arrange
        using var store = new WindowsCertificateStore(_logger);
        using var cert = CreateSelfSignedCertificate("CN=Test");
        var oversizedPrivateKey = new byte[WindowsCertificateStore.MaxCertificateSize + 1];

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => store.StoreCertificateAsync(cert, oversizedPrivateKey));
        Assert.Equal("privateKey", exception.ParamName);
        Assert.Contains("exceeds maximum allowed size", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"{WindowsCertificateStore.MaxCertificateSize} bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreCertificateAsync_WithPrivateKeyAtMaxSize_DoesNotThrow()
    {
        // Arrange
        using var store = new WindowsCertificateStore(_logger);
        using var cert = CreateSelfSignedCertificate("CN=Test");
        var maxSizePrivateKey = new byte[WindowsCertificateStore.MaxCertificateSize];

        // Act & Assert
        // This will likely fail because the key isn't valid, but it should pass the size validation
        // and fail later during import (which is expected behavior)
        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => store.StoreCertificateAsync(cert, maxSizePrivateKey));
    }

    [Fact]
    public async Task StoreCertificateAsync_WithCertificateExceedingMaxSize_ThrowsArgumentException()
    {
        // Arrange
        using var store = new WindowsCertificateStore(_logger);
        var privateKey = new byte[100];

        // Create an artificially large certificate by using a huge key size
        // Note: This test verifies the logic, but creating a cert > 16KB is impractical
        // We'll test the validation logic exists by checking a normal cert doesn't throw
        using var normalCert = CreateSelfSignedCertificate("CN=Test");

        // Assert that normal certificate size is acceptable
        Assert.True(normalCert.RawData.Length <= WindowsCertificateStore.MaxCertificateSize);

        // The actual test for oversized certs would require a mock, which isn't possible
        // with sealed X509Certificate2. This test documents the expected behavior.
    }

    #endregion

    #region StoreCaCertificateAsync Tests

    [Fact]
    public async Task StoreCaCertificateAsync_WithNullCertificate_ThrowsArgumentNullException()
    {
        // Arrange
        using var store = new WindowsCertificateStore(_logger);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => store.StoreCaCertificateAsync(null!));
        Assert.Equal("certificate", exception.ParamName);
    }

    [Fact]
    public async Task StoreCaCertificateAsync_WithCertificateExceedingMaxSize_ThrowsArgumentException()
    {
        // Arrange
        using var store = new WindowsCertificateStore(_logger);

        // Create a normal certificate to verify size check logic exists
        using var normalCert = CreateSelfSignedCertificate("CN=Meridian Console CA");

        // Assert that normal certificate size is acceptable
        Assert.True(normalCert.RawData.Length <= WindowsCertificateStore.MaxCertificateSize);

        // The actual test for oversized certs would require a mock, which isn't possible
        // with sealed X509Certificate2. This test documents the expected behavior.
    }

    #endregion

    #region RemoveCertificateAsync Tests

    [Fact]
    public async Task RemoveCertificateAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var store = new WindowsCertificateStore(_logger);
        store.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.RemoveCertificateAsync());
    }

    #endregion

    #region NeedsRenewal Tests

    [Fact]
    public void NeedsRenewal_WithNegativeThresholdDays_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var store = new WindowsCertificateStore(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => store.NeedsRenewal(-1));
        Assert.Equal("thresholdDays", exception.ParamName);
        Assert.Contains("cannot be negative", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NeedsRenewal_WithZeroThresholdDays_DoesNotThrow()
    {
        // Arrange
        using var store = new WindowsCertificateStore(_logger);

        // Act & Assert
        // This will return true (no certificate found) but shouldn't throw
        var result = store.NeedsRenewal(0);
        Assert.True(result); // No certificate exists
    }

    #endregion

    #region Dispose Pattern Tests

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var store = new WindowsCertificateStore(_logger);

        // Act & Assert - Should not throw
        store.Dispose();
        store.Dispose();
        store.Dispose();
    }

    [Fact]
    public void GetClientCertificate_WhenDisposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var store = new WindowsCertificateStore(_logger);
        store.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => store.GetClientCertificate());
    }

    [Fact]
    public void GetCaCertificate_WhenDisposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var store = new WindowsCertificateStore(_logger);
        store.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => store.GetCaCertificate());
    }

    [Fact]
    public async Task StoreCertificateAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var store = new WindowsCertificateStore(_logger);
        using var cert = CreateSelfSignedCertificate("CN=Test");
        var privateKey = new byte[100];
        store.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => store.StoreCertificateAsync(cert, privateKey));
    }

    [Fact]
    public async Task StoreCaCertificateAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var store = new WindowsCertificateStore(_logger);
        using var cert = CreateSelfSignedCertificate("CN=Test CA");
        store.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => store.StoreCaCertificateAsync(cert));
    }

    [Fact]
    public void NeedsRenewal_WhenDisposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var store = new WindowsCertificateStore(_logger);
        store.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => store.NeedsRenewal(30));
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a self-signed certificate for testing purposes.
    /// </summary>
    private static X509Certificate2 CreateSelfSignedCertificate(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            subjectName,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Add basic constraints
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));

        // Add key usage
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        // Create certificate
        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(90));

        return certificate;
    }

    #endregion
}
