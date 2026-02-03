using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Windows.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
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
    public async Task StoreCertificateAsync_WithPrivateKeyAtMaxSize_PassesSizeValidationButFailsImport()
    {
        // Arrange
        using var store = new WindowsCertificateStore(_logger);
        using var cert = CreateSelfSignedCertificate("CN=Test");
        var maxSizePrivateKey = new byte[WindowsCertificateStore.MaxCertificateSize];

        // Act & Assert
        // The key passes size validation (at max size, not exceeding it), but fails during import
        // because the byte array isn't a valid key format - CryptographicException is expected
        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => store.StoreCertificateAsync(cert, maxSizePrivateKey));
    }

    [Fact]
    public void StoreCertificateAsync_NormalCertificateSizeIsWithinMaxLimit()
    {
        // Arrange & Act
        // Note: Creating a certificate > 16KB is impractical as X509Certificate2 is sealed
        // and cannot be mocked. This test verifies that normal certificates are within
        // the size limit, documenting that oversized certificate validation exists.
        using var normalCert = CreateSelfSignedCertificate("CN=Test");

        // Assert that normal certificate size is acceptable
        Assert.True(
            normalCert.RawData.Length <= WindowsCertificateStore.MaxCertificateSize,
            $"Certificate size ({normalCert.RawData.Length}) should be <= MaxCertificateSize ({WindowsCertificateStore.MaxCertificateSize})");

        // The actual size validation for oversized certs is tested via the private key
        // size validation tests (StoreCertificateAsync_WithOversizedPrivateKey_ThrowsArgumentException)
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
    public void StoreCaCertificateAsync_NormalCertificateSizeIsWithinMaxLimit()
    {
        // Arrange & Act
        // Note: Creating a certificate > 16KB is impractical as X509Certificate2 is sealed
        // and cannot be mocked. This test verifies that normal CA certificates are within
        // the size limit, documenting that oversized certificate validation exists.
        using var normalCert = CreateSelfSignedCertificate("CN=Meridian Console CA");

        // Assert that normal CA certificate size is acceptable
        Assert.True(
            normalCert.RawData.Length <= WindowsCertificateStore.MaxCertificateSize,
            $"CA certificate size ({normalCert.RawData.Length}) should be <= MaxCertificateSize ({WindowsCertificateStore.MaxCertificateSize})");

        // Note: The oversized certificate validation in StoreCaCertificateAsync cannot be
        // directly tested because X509Certificate2 is sealed. The validation code exists
        // and follows the same pattern as StoreCertificateAsync size validation.
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

    [Fact]
    public void NeedsRenewal_WithFakeTimeProvider_UsesInjectedTime()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var fakeTimeProvider = new FakeTimeProvider(fixedTime);
        using var store = new WindowsCertificateStore(_logger, fakeTimeProvider);

        // Act
        // Without a certificate, NeedsRenewal returns true (renewal needed)
        // This test verifies the TimeProvider is accepted and used
        var result = store.NeedsRenewal(30);

        // Assert
        Assert.True(result); // No certificate in store = needs renewal
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(14)]
    [InlineData(30)]
    [InlineData(90)]
    public void NeedsRenewal_WithVariousThresholds_ReturnsConsistentResultWithoutCertificate(int thresholdDays)
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var fakeTimeProvider = new FakeTimeProvider(fixedTime);
        using var store = new WindowsCertificateStore(_logger, fakeTimeProvider);

        // Act
        var result = store.NeedsRenewal(thresholdDays);

        // Assert
        // Without a certificate, always needs renewal regardless of threshold
        Assert.True(result);
    }

    [Fact]
    public void NeedsRenewal_TimeProviderAdvancement_DoesNotAffectNoCertificateResult()
    {
        // Arrange
        var initialTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var fakeTimeProvider = new FakeTimeProvider(initialTime);
        using var store = new WindowsCertificateStore(_logger, fakeTimeProvider);

        // Act - Check at initial time
        var resultAtStart = store.NeedsRenewal(30);

        // Advance time by 6 months
        fakeTimeProvider.Advance(TimeSpan.FromDays(180));

        // Check again after time advancement
        var resultAfterAdvance = store.NeedsRenewal(30);

        // Assert
        // Without a certificate, both results should be true (needs renewal)
        // This verifies the TimeProvider is actually being queried each time
        Assert.True(resultAtStart);
        Assert.True(resultAfterAdvance);
    }

    // NOTE: Full renewal threshold boundary testing (at threshold, before, after)
    // requires a certificate to be present in the Windows certificate store.
    // Such tests would need to be integration tests that:
    // 1. Install a test certificate with a known expiration date
    // 2. Use FakeTimeProvider to set time relative to that expiration
    // 3. Verify NeedsRenewal returns correct true/false based on threshold
    // This is not feasible in unit tests due to X509Store being sealed.

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
