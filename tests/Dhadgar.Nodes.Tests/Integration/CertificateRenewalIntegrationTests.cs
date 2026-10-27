using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Microsoft.Extensions.DependencyInjection;

// Alias contract DTOs explicitly; the Models namespace declares types with the same names.
using RenewCertificateRequest = Dhadgar.Contracts.Nodes.RenewCertificateRequest;
using RenewCertificateResponse = Dhadgar.Contracts.Nodes.RenewCertificateResponse;

namespace Dhadgar.Nodes.Tests.Integration;

[Collection("Nodes Integration")]
public sealed class CertificateRenewalIntegrationTests
{
    private readonly NodesWebApplicationFactory _factory;
    private static readonly Guid TestOrgId = Guid.NewGuid();

    public CertificateRenewalIntegrationTests(NodesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Renew_ValidKey_IssuesCertBoundToRequestKey()
    {
        // Arrange - the agent generates a new key pair and sends only its public half
        var (node, originalCert) = await SeedNodeWithCertificateAsync();
        var client = _factory.CreateAgentClient(node.Id);
        client.DefaultRequestHeaders.Add(TestNodesAuthHandler.CertThumbprintHeader, originalCert.Thumbprint);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var spki = ecdsa.ExportSubjectPublicKeyInfo();

        // Act
        var response = await client.PostAsJsonAsync(
            $"/agents/{node.Id}/certificates/renew",
            new RenewCertificateRequest(Convert.ToBase64String(spki)));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RenewCertificateResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(originalCert.Thumbprint, result.CertificateThumbprint);
        Assert.Contains("BEGIN CERTIFICATE", result.Certificate, StringComparison.Ordinal);
        Assert.Contains("BEGIN CERTIFICATE", result.CaCertificate, StringComparison.Ordinal);

        using var issued = X509Certificate2.CreateFromPem(result.Certificate);
        Assert.Equal(spki, issued.PublicKey.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public async Task Renew_ResponseHasNoPkcs12()
    {
        // Arrange
        var (node, originalCert) = await SeedNodeWithCertificateAsync();
        var client = _factory.CreateAgentClient(node.Id);
        client.DefaultRequestHeaders.Add(TestNodesAuthHandler.CertThumbprintHeader, originalCert.Thumbprint);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        // Act
        var response = await client.PostAsJsonAsync(
            $"/agents/{node.Id}/certificates/renew",
            new RenewCertificateRequest(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo())));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("pkcs12", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Renew_RevokesPreviousCertificate()
    {
        // Arrange
        var (node, originalCert) = await SeedNodeWithCertificateAsync();
        var client = _factory.CreateAgentClient(node.Id);
        client.DefaultRequestHeaders.Add(TestNodesAuthHandler.CertThumbprintHeader, originalCert.Thumbprint);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        // Act
        await client.PostAsJsonAsync(
            $"/agents/{node.Id}/certificates/renew",
            new RenewCertificateRequest(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo())));

        // Assert - Check that old certificate is revoked
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        var revokedCert = await db.AgentCertificates.FindAsync(originalCert.Id);

        Assert.NotNull(revokedCert);
        Assert.True(revokedCert.IsRevoked);
        Assert.NotNull(revokedCert.RevokedAt);
        Assert.Contains("Renewed", revokedCert.RevocationReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renew_PublishesEvents()
    {
        // Arrange
        var (node, originalCert) = await SeedNodeWithCertificateAsync();
        var client = _factory.CreateAgentClient(node.Id);
        client.DefaultRequestHeaders.Add(TestNodesAuthHandler.CertThumbprintHeader, originalCert.Thumbprint);
        _factory.EventPublisher.Clear();
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        // Act
        await client.PostAsJsonAsync(
            $"/agents/{node.Id}/certificates/renew",
            new RenewCertificateRequest(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo())));

        // Assert
        Assert.True(_factory.EventPublisher.HasMessage<AgentCertificateRenewed>());
        Assert.True(_factory.EventPublisher.HasMessage<AgentCertificateRevoked>());
        Assert.True(_factory.EventPublisher.HasMessage<AgentCertificateIssued>());

        var renewedEvent = _factory.EventPublisher.GetLastMessage<AgentCertificateRenewed>()!;
        Assert.Equal(node.Id, renewedEvent.NodeId);
        Assert.Equal(originalCert.Thumbprint, renewedEvent.OldThumbprint);
    }

    [Fact]
    public async Task Renew_MissingThumbprintClaim_Returns401CertificateRequired()
    {
        // Arrange - no X-Test-Cert-Thumbprint header, so no certificate_thumbprint claim
        var (node, _) = await SeedNodeWithCertificateAsync();
        var client = _factory.CreateAgentClient(node.Id);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        // Act
        var response = await client.PostAsJsonAsync(
            $"/agents/{node.Id}/certificates/renew",
            new RenewCertificateRequest(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo())));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("certificate_required", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renew_ThumbprintMismatch_Returns401()
    {
        // Arrange
        var (node, _) = await SeedNodeWithCertificateAsync();
        var client = _factory.CreateAgentClient(node.Id);
        client.DefaultRequestHeaders.Add(TestNodesAuthHandler.CertThumbprintHeader, "not-the-current-thumbprint");
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        // Act
        var response = await client.PostAsJsonAsync(
            $"/agents/{node.Id}/certificates/renew",
            new RenewCertificateRequest(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo())));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("thumbprint_mismatch", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renew_InvalidPublicKey_Returns400()
    {
        // Arrange
        var (node, originalCert) = await SeedNodeWithCertificateAsync();
        var client = _factory.CreateAgentClient(node.Id);
        client.DefaultRequestHeaders.Add(TestNodesAuthHandler.CertThumbprintHeader, originalCert.Thumbprint);
        using var rsa1024 = RSA.Create(1024);

        // Act - garbage bytes, then a well-formed but too-small RSA key
        var garbage = await client.PostAsJsonAsync(
            $"/agents/{node.Id}/certificates/renew",
            new RenewCertificateRequest("not-a-key!!"));
        var weakKey = await client.PostAsJsonAsync(
            $"/agents/{node.Id}/certificates/renew",
            new RenewCertificateRequest(Convert.ToBase64String(rsa1024.ExportSubjectPublicKeyInfo())));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
        Assert.Contains("invalid_public_key", await garbage.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, weakKey.StatusCode);
        Assert.Contains("invalid_public_key", await weakKey.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renew_OtherNode_Returns403ProblemJson()
    {
        // Arrange - the certificate belongs to a different node than the URL
        var (node, originalCert) = await SeedNodeWithCertificateAsync();
        var differentNodeId = Guid.NewGuid();
        var client = _factory.CreateAgentClient(differentNodeId);
        client.DefaultRequestHeaders.Add(TestNodesAuthHandler.CertThumbprintHeader, originalCert.Thumbprint);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        // Act
        var response = await client.PostAsJsonAsync(
            $"/agents/{node.Id}/certificates/renew",
            new RenewCertificateRequest(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo())));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Renew_NodeNotFound_ReturnsNotFound()
    {
        // Arrange
        var nonExistentNodeId = Guid.NewGuid();
        var client = _factory.CreateAgentClient(nonExistentNodeId);
        client.DefaultRequestHeaders.Add(TestNodesAuthHandler.CertThumbprintHeader, "some-thumbprint");
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        // Act
        var response = await client.PostAsJsonAsync(
            $"/agents/{nonExistentNodeId}/certificates/renew",
            new RenewCertificateRequest(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo())));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCaCertificate_ReturnsValidPem()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/agents/ca-certificate");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("BEGIN CERTIFICATE", content, StringComparison.Ordinal);
        Assert.Contains("END CERTIFICATE", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCaCertificate_NoAuthRequired()
    {
        // Arrange - Create client without any auth headers
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/agents/ca-certificate");

        // Assert - Should succeed without authentication
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(Node Node, AgentCertificate Certificate)> SeedNodeWithCertificateAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = _factory.TimeProvider.GetUtcNow().UtcDateTime;

        var node = new Node
        {
            Id = Guid.NewGuid(),
            OrganizationId = TestOrgId,
            Name = $"test-node-{Guid.NewGuid():N}",
            DisplayName = "Test Node",
            Status = NodeStatus.Online,
            Platform = "linux",
            AgentVersion = "1.0.0",
            LastHeartbeat = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        var certificate = new AgentCertificate
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            Thumbprint = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            SerialNumber = Guid.NewGuid().ToString("N"),
            NotBefore = now,
            NotAfter = now.AddDays(90),
            IsRevoked = false,
            IssuedAt = now
        };

        db.Nodes.Add(node);
        db.AgentCertificates.Add(certificate);
        await db.SaveChangesAsync();

        return (node, certificate);
    }
}
