using System.Security.Claims;
using Dhadgar.ServiceDefaults.Audit;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

// NSubstitute mock setup pattern: mockQueue.QueueAsync(...).Returns(...) is valid
// but CA2012 doesn't recognize NSubstitute's fluent API for ValueTask-returning methods
#pragma warning disable CA2012

namespace Dhadgar.ServiceDefaults.Tests.Audit;

/// <summary>
/// Tests for <see cref="AuditMiddleware"/> behavior.
/// </summary>
public class AuditMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_UnauthenticatedRequest_DoesNotQueueRecord()
    {
        // Arrange
        var mockQueue = Substitute.For<IAuditQueue>();
        var middleware = new AuditMiddleware(
            next: _ => Task.CompletedTask,
            mockQueue,
            Substitute.For<ILogger<AuditMiddleware>>());

        var context = new DefaultHttpContext();
        // User.Identity.IsAuthenticated is false by default

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        await mockQueue.DidNotReceive()
            .QueueAsync(Arg.Any<ApiAuditRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedRequest_QueuesRecordWithCorrectFields()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        ApiAuditRecord? capturedRecord = null;

        var mockQueue = Substitute.For<IAuditQueue>();
        _ = mockQueue.QueueAsync(Arg.Any<ApiAuditRecord>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRecord = callInfo.ArgAt<ApiAuditRecord>(0);
                return ValueTask.CompletedTask;
            });

        var middleware = new AuditMiddleware(
            next: _ => Task.CompletedTask,
            mockQueue,
            Substitute.For<ILogger<AuditMiddleware>>());

        var context = CreateAuthenticatedContext(userId, tenantId);
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/servers";
        context.Response.StatusCode = 201;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.1");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedRecord.Should().NotBeNull();
        capturedRecord!.UserId.Should().Be(userId);
        capturedRecord.TenantId.Should().Be(tenantId);
        capturedRecord.HttpMethod.Should().Be("POST");
        capturedRecord.Path.Should().Be("/api/v1/servers");
        capturedRecord.StatusCode.Should().Be(201);
        capturedRecord.ClientIp.Should().Be("192.168.1.1");
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/healthz/live")]
    [InlineData("/livez")]
    [InlineData("/readyz")]
    [InlineData("/HEALTHZ")]
    public async Task InvokeAsync_HealthEndpoint_DoesNotQueueRecord(string path)
    {
        // Arrange
        var mockQueue = Substitute.For<IAuditQueue>();
        var middleware = new AuditMiddleware(
            next: _ => Task.CompletedTask,
            mockQueue,
            Substitute.For<ILogger<AuditMiddleware>>());

        var context = CreateAuthenticatedContext(Guid.NewGuid(), Guid.NewGuid());
        context.Request.Path = path;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        await mockQueue.DidNotReceive()
            .QueueAsync(Arg.Any<ApiAuditRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_ExtractsResourceIdFromPath()
    {
        // Arrange
        var resourceId = Guid.NewGuid();
        ApiAuditRecord? capturedRecord = null;

        var mockQueue = Substitute.For<IAuditQueue>();
        _ = mockQueue.QueueAsync(Arg.Any<ApiAuditRecord>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRecord = callInfo.ArgAt<ApiAuditRecord>(0);
                return ValueTask.CompletedTask;
            });

        var middleware = new AuditMiddleware(
            next: _ => Task.CompletedTask,
            mockQueue,
            Substitute.For<ILogger<AuditMiddleware>>());

        var context = CreateAuthenticatedContext(Guid.NewGuid(), Guid.NewGuid());
        context.Request.Path = $"/api/v1/servers/{resourceId}";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedRecord.Should().NotBeNull();
        capturedRecord!.ResourceId.Should().Be(resourceId);
        capturedRecord.ResourceType.Should().Be("servers");
    }

    [Theory]
    [InlineData("/api/v1/nodes/12345678-1234-1234-1234-123456789012", "nodes")]
    [InlineData("/api/v1/users/12345678-1234-1234-1234-123456789012", "users")]
    [InlineData("/api/v1/organizations/12345678-1234-1234-1234-123456789012", "organizations")]
    [InlineData("/api/v1/tasks/12345678-1234-1234-1234-123456789012", "tasks")]
    [InlineData("/api/v1/files/12345678-1234-1234-1234-123456789012", "files")]
    [InlineData("/api/v1/mods/12345678-1234-1234-1234-123456789012", "mods")]
    [InlineData("/servers/12345678-1234-1234-1234-123456789012", "servers")]
    public async Task InvokeAsync_ExtractsResourceTypeFromVariousPaths(string path, string expectedResourceType)
    {
        // Arrange
        ApiAuditRecord? capturedRecord = null;

        var mockQueue = Substitute.For<IAuditQueue>();
        _ = mockQueue.QueueAsync(Arg.Any<ApiAuditRecord>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRecord = callInfo.ArgAt<ApiAuditRecord>(0);
                return ValueTask.CompletedTask;
            });

        var middleware = new AuditMiddleware(
            next: _ => Task.CompletedTask,
            mockQueue,
            Substitute.For<ILogger<AuditMiddleware>>());

        var context = CreateAuthenticatedContext(Guid.NewGuid(), Guid.NewGuid());
        context.Request.Path = path;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedRecord.Should().NotBeNull();
        capturedRecord!.ResourceType.Should().Be(expectedResourceType);
        capturedRecord.ResourceId.Should().Be(Guid.Parse("12345678-1234-1234-1234-123456789012"));
    }

    [Fact]
    public async Task InvokeAsync_PathWithoutResourceId_SetsNullResourceIdAndType()
    {
        // Arrange
        ApiAuditRecord? capturedRecord = null;

        var mockQueue = Substitute.For<IAuditQueue>();
        _ = mockQueue.QueueAsync(Arg.Any<ApiAuditRecord>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRecord = callInfo.ArgAt<ApiAuditRecord>(0);
                return ValueTask.CompletedTask;
            });

        var middleware = new AuditMiddleware(
            next: _ => Task.CompletedTask,
            mockQueue,
            Substitute.For<ILogger<AuditMiddleware>>());

        var context = CreateAuthenticatedContext(Guid.NewGuid(), Guid.NewGuid());
        context.Request.Path = "/api/v1/servers"; // List endpoint, no resource ID

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedRecord.Should().NotBeNull();
        capturedRecord!.ResourceId.Should().BeNull();
        capturedRecord.ResourceType.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_CapturesStatusCodeAfterPipelineCompletion()
    {
        // Arrange
        ApiAuditRecord? capturedRecord = null;

        var mockQueue = Substitute.For<IAuditQueue>();
        _ = mockQueue.QueueAsync(Arg.Any<ApiAuditRecord>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRecord = callInfo.ArgAt<ApiAuditRecord>(0);
                return ValueTask.CompletedTask;
            });

        var middleware = new AuditMiddleware(
            next: ctx =>
            {
                ctx.Response.StatusCode = 404;
                return Task.CompletedTask;
            },
            mockQueue,
            Substitute.For<ILogger<AuditMiddleware>>());

        var context = CreateAuthenticatedContext(Guid.NewGuid(), Guid.NewGuid());

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedRecord.Should().NotBeNull();
        capturedRecord!.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task InvokeAsync_ExtractsUserIdFromSubClaim()
    {
        // Arrange
        var userId = Guid.NewGuid();
        ApiAuditRecord? capturedRecord = null;

        var mockQueue = Substitute.For<IAuditQueue>();
        _ = mockQueue.QueueAsync(Arg.Any<ApiAuditRecord>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRecord = callInfo.ArgAt<ApiAuditRecord>(0);
                return ValueTask.CompletedTask;
            });

        var middleware = new AuditMiddleware(
            next: _ => Task.CompletedTask,
            mockQueue,
            Substitute.For<ILogger<AuditMiddleware>>());

        // Use only "sub" claim
        var context = new DefaultHttpContext();
        var claims = new[] { new Claim("sub", userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        context.User = new ClaimsPrincipal(identity);
        context.Request.Method = "GET";
        context.Request.Path = "/test";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedRecord.Should().NotBeNull();
        capturedRecord!.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task InvokeAsync_ExtractsTenantIdFromOrgIdClaim()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        ApiAuditRecord? capturedRecord = null;

        var mockQueue = Substitute.For<IAuditQueue>();
        _ = mockQueue.QueueAsync(Arg.Any<ApiAuditRecord>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRecord = callInfo.ArgAt<ApiAuditRecord>(0);
                return ValueTask.CompletedTask;
            });

        var middleware = new AuditMiddleware(
            next: _ => Task.CompletedTask,
            mockQueue,
            Substitute.For<ILogger<AuditMiddleware>>());

        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("org_id", tenantId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "Test");
        context.User = new ClaimsPrincipal(identity);
        context.Request.Method = "GET";
        context.Request.Path = "/test";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedRecord.Should().NotBeNull();
        capturedRecord!.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task InvokeAsync_ExtractsTenantIdFromTenantIdClaimAsFallback()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        ApiAuditRecord? capturedRecord = null;

        var mockQueue = Substitute.For<IAuditQueue>();
        _ = mockQueue.QueueAsync(Arg.Any<ApiAuditRecord>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRecord = callInfo.ArgAt<ApiAuditRecord>(0);
                return ValueTask.CompletedTask;
            });

        var middleware = new AuditMiddleware(
            next: _ => Task.CompletedTask,
            mockQueue,
            Substitute.For<ILogger<AuditMiddleware>>());

        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("tenant_id", tenantId.ToString()) // Use tenant_id instead of org_id
        };
        var identity = new ClaimsIdentity(claims, "Test");
        context.User = new ClaimsPrincipal(identity);
        context.Request.Method = "GET";
        context.Request.Path = "/test";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedRecord.Should().NotBeNull();
        capturedRecord!.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task InvokeAsync_TruncatesLongUserAgent()
    {
        // Arrange
        ApiAuditRecord? capturedRecord = null;

        var mockQueue = Substitute.For<IAuditQueue>();
        _ = mockQueue.QueueAsync(Arg.Any<ApiAuditRecord>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRecord = callInfo.ArgAt<ApiAuditRecord>(0);
                return ValueTask.CompletedTask;
            });

        var middleware = new AuditMiddleware(
            next: _ => Task.CompletedTask,
            mockQueue,
            Substitute.For<ILogger<AuditMiddleware>>());

        var context = CreateAuthenticatedContext(Guid.NewGuid(), Guid.NewGuid());
        var longUserAgent = new string('X', 500); // Longer than 256 limit
        context.Request.Headers.UserAgent = longUserAgent;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedRecord.Should().NotBeNull();
        capturedRecord!.UserAgent.Should().HaveLength(256);
    }

    [Fact]
    public async Task InvokeAsync_CapturesCorrelationIdFromHttpContextItems()
    {
        // Arrange
        const string correlationId = "test-correlation-123";
        ApiAuditRecord? capturedRecord = null;

        var mockQueue = Substitute.For<IAuditQueue>();
        _ = mockQueue.QueueAsync(Arg.Any<ApiAuditRecord>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRecord = callInfo.ArgAt<ApiAuditRecord>(0);
                return ValueTask.CompletedTask;
            });

        var middleware = new AuditMiddleware(
            next: _ => Task.CompletedTask,
            mockQueue,
            Substitute.For<ILogger<AuditMiddleware>>());

        var context = CreateAuthenticatedContext(Guid.NewGuid(), Guid.NewGuid());
        context.Items["CorrelationId"] = correlationId;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedRecord.Should().NotBeNull();
        capturedRecord!.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public async Task InvokeAsync_RecordsDurationMs()
    {
        // Arrange
        ApiAuditRecord? capturedRecord = null;

        var mockQueue = Substitute.For<IAuditQueue>();
        _ = mockQueue.QueueAsync(Arg.Any<ApiAuditRecord>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRecord = callInfo.ArgAt<ApiAuditRecord>(0);
                return ValueTask.CompletedTask;
            });

        var middleware = new AuditMiddleware(
            next: async ctx =>
            {
                await Task.Delay(50); // Simulate some processing time
            },
            mockQueue,
            Substitute.For<ILogger<AuditMiddleware>>());

        var context = CreateAuthenticatedContext(Guid.NewGuid(), Guid.NewGuid());

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedRecord.Should().NotBeNull();
        capturedRecord!.DurationMs.Should().BeGreaterThanOrEqualTo(40); // Allow some variance
    }

    [Fact]
    public async Task InvokeAsync_NextThrowsException_StillQueuesRecord()
    {
        // Arrange
        ApiAuditRecord? capturedRecord = null;

        var mockQueue = Substitute.For<IAuditQueue>();
        _ = mockQueue.QueueAsync(Arg.Any<ApiAuditRecord>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRecord = callInfo.ArgAt<ApiAuditRecord>(0);
                return ValueTask.CompletedTask;
            });

        var middleware = new AuditMiddleware(
            next: _ => throw new InvalidOperationException("Test exception"),
            mockQueue,
            Substitute.For<ILogger<AuditMiddleware>>());

        var context = CreateAuthenticatedContext(Guid.NewGuid(), Guid.NewGuid());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await middleware.InvokeAsync(context));

        // Record should still be queued
        capturedRecord.Should().NotBeNull();
    }

    /// <summary>
    /// Creates an authenticated HTTP context with the specified user and tenant IDs.
    /// </summary>
    private static DefaultHttpContext CreateAuthenticatedContext(Guid userId, Guid tenantId)
    {
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("org_id", tenantId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "Test");
        context.User = new ClaimsPrincipal(identity);
        context.Request.Method = "GET";
        context.Request.Path = "/test";
        return context;
    }
}
