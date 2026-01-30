using System.Net;
using System.Net.Http.Json;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Microsoft.Extensions.DependencyInjection;

// Alias local models to avoid ambiguity with Contracts types
using RenewCertificateRequest = Dhadgar.Nodes.Models.RenewCertificateRequest;
using RenewCertificateResponse = Dhadgar.Nodes.Models.RenewCertificateResponse;

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
    public async Task RenewCertificate_ValidRequest_ReturnsNewCertificate()
    {
        // Arrange - Create a node with certificate
        var (node, originalCert) = await SeedNodeWithCertificateAsync();
        var client = _factory.CreateAgentClient(node.Id);

        var request = new RenewCertificateRequest(originalCert.Thumbprint);

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/agents/{node.Id}/certificates/renew",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RenewCertificateResponse>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.CertificateThumbprint);
        Assert.NotEqual(originalCert.Thumbprint, result.CertificateThumbprint);
        Assert.Contains("BEGIN CERTIFICATE", result.Certificate, StringComparison.Ordinal);
        Assert.NotEmpty(result.Pkcs12Base64);
        Assert.NotEmpty(result.Pkcs12Password);
    }

    [Fact]
    public async Task RenewCertificate_InvalidThumbprint_ReturnsUnauthorized()
    {
        // Arrange
        var (node, _) = await SeedNodeWithCertificateAsync();
        var client = _factory.CreateAgentClient(node.Id);

        var request = new RenewCertificateRequest("invalid-thumbprint-that-does-not-match");

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/agents/{node.Id}/certificates/renew",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RenewCertificate_NodeIdMismatch_ReturnsForbidden()
    {
        // Arrange
        var (node, originalCert) = await SeedNodeWithCertificateAsync();
        var differentNodeId = Guid.NewGuid();
        var client = _factory.CreateAgentClient(differentNodeId);

        var request = new RenewCertificateRequest(originalCert.Thumbprint);

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/agents/{node.Id}/certificates/renew",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RenewCertificate_NodeNotFound_ReturnsNotFound()
    {
        // Arrange
        var nonExistentNodeId = Guid.NewGuid();
        var client = _factory.CreateAgentClient(nonExistentNodeId);

        var request = new RenewCertificateRequest("some-thumbprint");

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/agents/{nonExistentNodeId}/certificates/renew",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RenewCertificate_RevokesOldCertificate()
    {
        // Arrange
        var (node, originalCert) = await SeedNodeWithCertificateAsync();
        var client = _factory.CreateAgentClient(node.Id);

        var request = new RenewCertificateRequest(originalCert.Thumbprint);

        // Act
        await client.PostAsJsonAsync(
            $"/api/v1/agents/{node.Id}/certificates/renew",
            request);

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
    public async Task RenewCertificate_PublishesEvents()
    {
        // Arrange
        var (node, originalCert) = await SeedNodeWithCertificateAsync();
        var client = _factory.CreateAgentClient(node.Id);
        _factory.EventPublisher.Clear();

        var request = new RenewCertificateRequest(originalCert.Thumbprint);

        // Act
        await client.PostAsJsonAsync(
            $"/api/v1/agents/{node.Id}/certificates/renew",
            request);

        // Assert
        Assert.True(_factory.EventPublisher.HasMessage<AgentCertificateRenewed>());
        Assert.True(_factory.EventPublisher.HasMessage<AgentCertificateRevoked>());
        Assert.True(_factory.EventPublisher.HasMessage<AgentCertificateIssued>());

        var renewedEvent = _factory.EventPublisher.GetLastMessage<AgentCertificateRenewed>()!;
        Assert.Equal(node.Id, renewedEvent.NodeId);
        Assert.Equal(originalCert.Thumbprint, renewedEvent.OldThumbprint);
    }

    [Fact]
    public async Task GetCaCertificate_ReturnsValidPem()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/agents/ca-certificate");

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
        var response = await client.GetAsync("/api/v1/agents/ca-certificate");

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
