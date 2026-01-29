using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Dhadgar.Nodes.Auth;
using Dhadgar.Nodes.Services;
using Dhadgar.Nodes.Tests.TestHelpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Dhadgar.Nodes.Tests;

public class MtlsMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NonAgentEndpoint_PassesThrough()
    {
        // Arrange
        var context = CreateHttpContext("/api/v1/organizations/123/nodes");
        var nextCalled = false;
        var next = new RequestDelegate(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var sut = new MtlsMiddleware(
            next,
            Options.Create(new MtlsOptions { Enabled = true }),
            NullLogger<MtlsMiddleware>.Instance);

        // Act
        await sut.InvokeAsync(context, Substitute.For<ICertificateValidationService>());

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_ExemptEnrollPath_PassesThrough()
    {
        // Arrange
        var context = CreateHttpContext("/api/v1/agents/enroll");
        var nextCalled = false;
        var next = new RequestDelegate(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var sut = new MtlsMiddleware(
            next,
            Options.Create(new MtlsOptions { Enabled = true }),
            NullLogger<MtlsMiddleware>.Instance);

        // Act
        await sut.InvokeAsync(context, Substitute.For<ICertificateValidationService>());

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_ExemptCaCertificatePath_PassesThrough()
    {
        // Arrange
        var context = CreateHttpContext("/api/v1/agents/ca-certificate");
        var nextCalled = false;
        var next = new RequestDelegate(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var sut = new MtlsMiddleware(
            next,
            Options.Create(new MtlsOptions { Enabled = true }),
            NullLogger<MtlsMiddleware>.Instance);

        // Act
        await sut.InvokeAsync(context, Substitute.For<ICertificateValidationService>());

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_MtlsDisabled_PassesThroughWithoutValidation()
    {
        // Arrange
        var context = CreateHttpContext("/api/v1/agents/some-node-id/heartbeat");
        var nextCalled = false;
        var next = new RequestDelegate(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var sut = new MtlsMiddleware(
            next,
            Options.Create(new MtlsOptions { Enabled = false }),
            NullLogger<MtlsMiddleware>.Instance);

        var validationService = Substitute.For<ICertificateValidationService>();

        // Act
        await sut.InvokeAsync(context, validationService);

        // Assert
        Assert.True(nextCalled);
        await validationService.DidNotReceive().ValidateClientCertificateAsync(
            Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_NoCertificateAndRequired_Returns401()
    {
        // Arrange
        var context = CreateHttpContext("/api/v1/agents/some-node-id/heartbeat");
        var nextCalled = false;
        var next = new RequestDelegate(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var options = new MtlsOptions
        {
            Enabled = true,
            RequireClientCertificate = true
        };

        var sut = new MtlsMiddleware(
            next,
            Options.Create(options),
            NullLogger<MtlsMiddleware>.Instance);

        // Act
        await sut.InvokeAsync(context, Substitute.For<ICertificateValidationService>());

        // Assert
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_NoCertificateButNotRequired_PassesThrough()
    {
        // Arrange
        var context = CreateHttpContext("/api/v1/agents/some-node-id/heartbeat");
        var nextCalled = false;
        var next = new RequestDelegate(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var options = new MtlsOptions
        {
            Enabled = true,
            RequireClientCertificate = false
        };

        var sut = new MtlsMiddleware(
            next,
            Options.Create(options),
            NullLogger<MtlsMiddleware>.Instance);

        // Act
        await sut.InvokeAsync(context, Substitute.For<ICertificateValidationService>());

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_InvalidCertificate_Returns401()
    {
        // Arrange
        using var certificate = TestCertificateFactory.CreateSelfSignedCertificate();
        var context = CreateHttpContext("/api/v1/agents/some-node-id/heartbeat", certificate);
        var nextCalled = false;
        var next = new RequestDelegate(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var validationService = Substitute.For<ICertificateValidationService>();
        validationService.ValidateClientCertificateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(CertificateValidationResult.Fail("invalid_issuer", "Certificate was not issued by our CA"));

        var sut = new MtlsMiddleware(
            next,
            Options.Create(new MtlsOptions { Enabled = true }),
            NullLogger<MtlsMiddleware>.Instance);

        // Act
        await sut.InvokeAsync(context, validationService);

        // Assert
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_ValidCertificate_StoresNodeIdInContext()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        using var certificate = TestCertificateFactory.CreateSelfSignedCertificate();
        var context = CreateHttpContext("/api/v1/agents/some-node-id/heartbeat", certificate);
        var nextCalled = false;
        var next = new RequestDelegate(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var validationService = Substitute.For<ICertificateValidationService>();
        validationService.ValidateClientCertificateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(CertificateValidationResult.Success(
                nodeId,
                $"spiffe://meridianconsole.com/nodes/{nodeId}",
                "abc123thumbprint",
                DateTime.UtcNow.AddDays(90)));

        var sut = new MtlsMiddleware(
            next,
            Options.Create(new MtlsOptions { Enabled = true }),
            NullLogger<MtlsMiddleware>.Instance);

        // Act
        await sut.InvokeAsync(context, validationService);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal(nodeId, context.Items[MtlsMiddleware.NodeIdItemKey]);
        Assert.Equal("abc123thumbprint", context.Items[MtlsMiddleware.CertificateThumbprintItemKey]);
        Assert.Equal($"spiffe://meridianconsole.com/nodes/{nodeId}", context.Items[MtlsMiddleware.SpiffeIdItemKey]);
    }

    [Fact]
    public async Task InvokeAsync_ValidCertificate_SetsUserWithNodeIdClaim()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        using var certificate = TestCertificateFactory.CreateSelfSignedCertificate();
        var context = CreateHttpContext("/api/v1/agents/some-node-id/heartbeat", certificate);
        var next = new RequestDelegate(_ => Task.CompletedTask);

        var validationService = Substitute.For<ICertificateValidationService>();
        validationService.ValidateClientCertificateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(CertificateValidationResult.Success(
                nodeId,
                $"spiffe://meridianconsole.com/nodes/{nodeId}",
                "abc123thumbprint",
                DateTime.UtcNow.AddDays(90)));

        var sut = new MtlsMiddleware(
            next,
            Options.Create(new MtlsOptions { Enabled = true }),
            NullLogger<MtlsMiddleware>.Instance);

        // Act
        await sut.InvokeAsync(context, validationService);

        // Assert
        Assert.NotNull(context.User);
        Assert.True(context.User.Identity?.IsAuthenticated);
        Assert.Equal("mTLS", context.User.Identity?.AuthenticationType);
        Assert.Equal(nodeId.ToString(), context.User.FindFirst("node_id")?.Value);
        Assert.Equal($"spiffe://meridianconsole.com/nodes/{nodeId}", context.User.FindFirst("spiffe_id")?.Value);
        Assert.Equal("abc123thumbprint", context.User.FindFirst("certificate_thumbprint")?.Value);
    }

    private static DefaultHttpContext CreateHttpContext(string path, X509Certificate2? certificate = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        if (certificate is not null)
        {
            // Mock the connection to return the certificate
            context.Connection.ClientCertificate = certificate;
        }

        return context;
    }
}

public class CertificateValidationServiceTests
{
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.UtcNow);

    [Fact]
    public void ParseNodeIdFromSpiffeId_ValidSpiffeId_ReturnsNodeId()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        var spiffeId = $"spiffe://meridianconsole.com/nodes/{nodeId}";
        var service = CreateService();

        // Act
        var result = service.ParseNodeIdFromSpiffeId(spiffeId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(nodeId, result.Value);
    }

    [Fact]
    public void ParseNodeIdFromSpiffeId_InvalidFormat_ReturnsNull()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        Assert.Null(service.ParseNodeIdFromSpiffeId("not-a-spiffe-id"));
        Assert.Null(service.ParseNodeIdFromSpiffeId("spiffe://wrong-domain.com/nodes/123"));
        Assert.Null(service.ParseNodeIdFromSpiffeId("spiffe://meridianconsole.com/wrong-path/123"));
        Assert.Null(service.ParseNodeIdFromSpiffeId("spiffe://meridianconsole.com/nodes/not-a-guid"));
        Assert.Null(service.ParseNodeIdFromSpiffeId(""));
        Assert.Null(service.ParseNodeIdFromSpiffeId(null!));
    }

    [Fact]
    public async Task ValidateClientCertificateAsync_ExpiredCertificate_ReturnsFailure()
    {
        // Arrange
        using var certificate = CreateExpiredCertificate();
        var service = CreateService(allowExpired: false);

        // Act
        var result = await service.ValidateClientCertificateAsync(certificate);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("certificate_expired", result.ErrorCode);
        Assert.True(result.IsExpired);
    }

    [Fact]
    public async Task ValidateClientCertificateAsync_ExpiredCertificateButAllowed_ContinuesValidation()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        using var certificate = CreateCertificateWithSpiffeId(nodeId, expired: true);
        var caService = CreateMockCaService(validatesTo: true);
        var service = CreateService(caService: caService, allowExpired: true);

        // Act
        var result = await service.ValidateClientCertificateAsync(certificate);

        // Assert
        // Should fail on CA validation since we're using a self-signed cert
        // but the test verifies it didn't fail on expiration
        Assert.NotEqual("certificate_expired", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateClientCertificateAsync_NotYetValidCertificate_ReturnsFailure()
    {
        // Arrange
        using var certificate = CreateNotYetValidCertificate();
        var service = CreateService();

        // Act
        var result = await service.ValidateClientCertificateAsync(certificate);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("certificate_not_yet_valid", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateClientCertificateAsync_InvalidIssuer_ReturnsFailure()
    {
        // Arrange
        using var certificate = TestCertificateFactory.CreateSelfSignedCertificate();
        var caService = CreateMockCaService(validatesTo: false);
        var service = CreateService(caService: caService);

        // Act
        var result = await service.ValidateClientCertificateAsync(certificate);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("invalid_issuer", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateClientCertificateAsync_MissingClientAuthEku_ReturnsFailure()
    {
        // Arrange
        using var certificate = CreateCertificateWithoutClientAuthEku();
        var caService = CreateMockCaService(validatesTo: true);
        var service = CreateService(caService: caService);

        // Act
        var result = await service.ValidateClientCertificateAsync(certificate);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("missing_client_auth_eku", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateClientCertificateAsync_MissingSpiffeId_ReturnsFailure()
    {
        // Arrange
        using var certificate = CreateCertificateWithClientAuthEkuButNoSpiffe();
        var caService = CreateMockCaService(validatesTo: true);
        var service = CreateService(caService: caService);

        // Act
        var result = await service.ValidateClientCertificateAsync(certificate);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("missing_spiffe_id", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateClientCertificateAsync_WrongTrustDomain_ReturnsFailure()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        using var certificate = CreateCertificateWithSpiffeId(nodeId, trustDomain: "wrong-domain.com");
        var caService = CreateMockCaService(validatesTo: true);
        var service = CreateService(caService: caService);

        // Act
        var result = await service.ValidateClientCertificateAsync(certificate);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("trust_domain_mismatch", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateClientCertificateAsync_ValidCertificate_ReturnsSuccess()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        using var certificate = CreateCertificateWithSpiffeId(nodeId);
        var caService = CreateMockCaService(validatesTo: true);
        var service = CreateService(caService: caService);

        // Act
        var result = await service.ValidateClientCertificateAsync(certificate);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(nodeId, result.NodeId);
        Assert.Equal($"spiffe://meridianconsole.com/nodes/{nodeId}", result.SpiffeId);
        Assert.NotNull(result.Thumbprint);
        Assert.NotNull(result.NotAfter);
    }

    [Fact]
    public void ExtractSpiffeId_CertificateWithSpiffeUri_ReturnsSpiffeId()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        using var certificate = CreateCertificateWithSpiffeId(nodeId);
        var service = CreateService();

        // Act
        var result = service.ExtractSpiffeId(certificate);

        // Assert
        Assert.NotNull(result);
        Assert.Equal($"spiffe://meridianconsole.com/nodes/{nodeId}", result);
    }

    [Fact]
    public void ExtractSpiffeId_CertificateWithoutSan_ReturnsNull()
    {
        // Arrange
        using var certificate = TestCertificateFactory.CreateSelfSignedCertificate();
        var service = CreateService();

        // Act
        var result = service.ExtractSpiffeId(certificate);

        // Assert
        Assert.Null(result);
    }

    private CertificateValidationService CreateService(
        ICertificateAuthorityService? caService = null,
        bool allowExpired = false)
    {
        caService ??= CreateMockCaService(validatesTo: false);
        var options = new MtlsOptions
        {
            AllowExpiredCertificates = allowExpired,
            SpiffeTrustDomain = "meridianconsole.com"
        };

        return new CertificateValidationService(
            caService,
            Options.Create(options),
            _timeProvider,
            NullLogger<CertificateValidationService>.Instance);
    }

    private static ICertificateAuthorityService CreateMockCaService(bool validatesTo)
    {
        var mock = Substitute.For<ICertificateAuthorityService>();
        mock.ValidateCertificateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(validatesTo);
        return mock;
    }

    private static X509Certificate2 CreateExpiredCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Expired",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));
    }

    private static X509Certificate2 CreateNotYetValidCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=NotYetValid",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2));
    }

    private static X509Certificate2 CreateCertificateWithoutClientAuthEku()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=NoEKU",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Add a SAN with SPIFFE ID but no EKU
        var nodeId = Guid.NewGuid();
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddUri(new Uri($"spiffe://meridianconsole.com/nodes/{nodeId}"));
        request.CertificateExtensions.Add(sanBuilder.Build());

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static X509Certificate2 CreateCertificateWithClientAuthEkuButNoSpiffe()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=NoSpiffe",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Add Client Authentication EKU
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.2")], // Client Authentication
                critical: true));

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static X509Certificate2 CreateCertificateWithSpiffeId(
        Guid nodeId,
        string trustDomain = "meridianconsole.com",
        bool expired = false)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={nodeId}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Add Client Authentication EKU
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.2")], // Client Authentication
                critical: true));

        // Add SAN with SPIFFE ID
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddUri(new Uri($"spiffe://{trustDomain}/nodes/{nodeId}"));
        request.CertificateExtensions.Add(sanBuilder.Build());

        if (expired)
        {
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));
        }

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90));
    }
}

/// <summary>
/// Integration tests for mTLS middleware with the full web application.
/// </summary>
public class MtlsMiddlewareIntegrationTests : IClassFixture<NodesWebApplicationFactory>
{
    private readonly NodesWebApplicationFactory _factory;

    public MtlsMiddlewareIntegrationTests(NodesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EnrollEndpoint_WithoutCertificate_AllowsAccess()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - just verify the endpoint is accessible (will fail validation but not auth)
        var response = await client.PostAsJsonAsync("/api/v1/agents/enroll", new
        {
            token = "test-token",
            hostname = "test-host",
            platform = "linux"
        });

        // Assert - The mTLS middleware should NOT block this request
        // It should pass through to the actual endpoint handler.
        // We accept any status code that indicates the request reached the handler:
        // - 401 (invalid token)
        // - 400 (validation error)
        // - 500 (internal error in handler - still proves mTLS didn't block)
        // The key test is that we don't get a 401 with "missing_client_certificate" error
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.InternalServerError,
            $"Expected 401, 400, or 500 (request should reach handler), got {response.StatusCode}");

        // If we got 401, verify it's NOT from mTLS middleware
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var content = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("missing_client_certificate", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CaCertificateEndpoint_WithoutCertificate_AllowsAccess()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/agents/ca-certificate");

        // Assert - should successfully return the CA certificate
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HeartbeatEndpoint_WithTestAuth_AllowsAccess()
    {
        // Arrange - create a node first
        var orgId = Guid.NewGuid();
        var node = await _factory.SeedNodeAsync(orgId);
        var client = _factory.CreateAgentClient(node.Id);

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/agents/{node.Id}/heartbeat", new
        {
            cpuUsagePercent = 50.0,
            memoryUsagePercent = 60.0,
            diskUsagePercent = 40.0,
            activeServerCount = 2,
            agentVersion = "1.0.0"
        });

        // Assert - should work with test auth (mTLS is disabled in test factory)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
