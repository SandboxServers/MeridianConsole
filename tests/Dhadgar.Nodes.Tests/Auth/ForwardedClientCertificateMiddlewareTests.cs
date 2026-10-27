using System.Net;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Auth;
using Dhadgar.Nodes.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Tests.Auth;

public class ForwardedClientCertificateMiddlewareTests
{
    private const string HeaderName = AgentCertificateProfile.ForwardedCertificateHeader;
    private const string TrustedCidr = "10.42.0.0/16";

    [Fact]
    public async Task InvokeAsync_TrustedIpWithValidHeader_SetsCertificateAndRemovesHeader()
    {
        // Arrange
        using var source = TestCertificateFactory.CreateSelfSignedCertificate();
        var context = CreateContext("10.42.1.5", Convert.ToBase64String(source.RawData));
        X509Certificate2? seenByNext = null;
        var sut = CreateSut(enabled: true, next: _ =>
        {
            seenByNext = context.Connection.ClientCertificate;
            return Task.CompletedTask;
        });

        // Act
        await sut.InvokeAsync(context);

        // Assert
        Assert.NotNull(seenByNext);
        Assert.Equal(source.Thumbprint, seenByNext.Thumbprint);
        Assert.Null(context.Connection.ClientCertificate);
        Assert.False(context.Request.Headers.ContainsKey(HeaderName));
    }

    [Fact]
    public async Task InvokeAsync_SameConnectionNextRequest_SeesOnlyItsOwnForwardedCertificate()
    {
        // Arrange: two requests over one connection (keep-alive or HTTP/2), each forwarding a different agent certificate.
        using var first = TestCertificateFactory.CreateSelfSignedCertificate();
        using var second = TestCertificateFactory.CreateSelfSignedCertificate();
        var context = CreateContext("10.42.1.5", Convert.ToBase64String(first.RawData));
        X509Certificate2? seenByFirst = null;
        X509Certificate2? seenBySecond = null;
        var callCount = 0;
        var sut = CreateSut(enabled: true, next: _ =>
        {
            callCount++;
            if (callCount == 1)
            {
                seenByFirst = context.Connection.ClientCertificate;
            }
            else
            {
                seenBySecond = context.Connection.ClientCertificate;
            }

            return Task.CompletedTask;
        });

        // Act
        await sut.InvokeAsync(context);
        context.Request.Headers[HeaderName] = Convert.ToBase64String(second.RawData);
        await sut.InvokeAsync(context);

        // Assert
        Assert.NotNull(seenByFirst);
        Assert.Equal(first.Thumbprint, seenByFirst.Thumbprint);
        Assert.NotNull(seenBySecond);
        Assert.Equal(second.Thumbprint, seenBySecond.Thumbprint);
        Assert.Null(context.Connection.ClientCertificate);
    }

    [Fact]
    public async Task InvokeAsync_ZeroPrefixTrustedProxy_IsNotTrusted()
    {
        // Arrange: startup validation is bypassed here on purpose; the middleware must still refuse a /0 entry.
        using var source = TestCertificateFactory.CreateSelfSignedCertificate();
        var context = CreateContext("10.42.1.5", Convert.ToBase64String(source.RawData));
        var sut = CreateSut(enabled: true, trustedProxies: ["0.0.0.0/0", "::/0"]);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        Assert.Null(context.Connection.ClientCertificate);
        Assert.False(context.Request.Headers.ContainsKey(HeaderName));
    }

    [Fact]
    public async Task InvokeAsync_UntrustedIp_LeavesNoCertificateRemovesHeaderAndCallsNext()
    {
        // Arrange
        using var source = TestCertificateFactory.CreateSelfSignedCertificate();
        var context = CreateContext("192.168.1.10", Convert.ToBase64String(source.RawData));
        var nextCalled = false;
        var sut = CreateSut(enabled: true, next: _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await sut.InvokeAsync(context);

        // Assert
        Assert.Null(context.Connection.ClientCertificate);
        Assert.False(context.Request.Headers.ContainsKey(HeaderName));
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_Disabled_IgnoresHeaderAndRemovesIt()
    {
        // Arrange
        using var source = TestCertificateFactory.CreateSelfSignedCertificate();
        var context = CreateContext("10.42.1.5", Convert.ToBase64String(source.RawData));
        var sut = CreateSut(enabled: false);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        Assert.Null(context.Connection.ClientCertificate);
        Assert.False(context.Request.Headers.ContainsKey(HeaderName));
    }

    [Fact]
    public async Task InvokeAsync_MalformedBase64_LeavesNoCertificateAndDoesNotThrow()
    {
        // Arrange
        var context = CreateContext("10.42.1.5", "not base64 !!!");
        var nextCalled = false;
        var sut = CreateSut(enabled: true, next: _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await sut.InvokeAsync(context);

        // Assert
        Assert.Null(context.Connection.ClientCertificate);
        Assert.False(context.Request.Headers.ContainsKey(HeaderName));
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_HeaderOverMaxLength_IsIgnored()
    {
        // Arrange
        using var source = TestCertificateFactory.CreateSelfSignedCertificate();
        var value = Convert.ToBase64String(source.RawData);
        var context = CreateContext("10.42.1.5", value);
        var sut = CreateSut(enabled: true, maxHeaderLength: value.Length - 1);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        Assert.Null(context.Connection.ClientCertificate);
        Assert.False(context.Request.Headers.ContainsKey(HeaderName));
    }

    [Fact]
    public async Task InvokeAsync_TwoHeaderValues_AreIgnored()
    {
        // Arrange
        using var source = TestCertificateFactory.CreateSelfSignedCertificate();
        var value = Convert.ToBase64String(source.RawData);
        var context = CreateContext("10.42.1.5", value);
        context.Request.Headers.Append(HeaderName, value);
        var sut = CreateSut(enabled: true);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        Assert.Null(context.Connection.ClientCertificate);
        Assert.False(context.Request.Headers.ContainsKey(HeaderName));
    }

    [Fact]
    public async Task InvokeAsync_ExistingConnectionCertificate_IsKept()
    {
        // Arrange
        using var handshake = TestCertificateFactory.CreateSelfSignedCertificate();
        using var forwarded = TestCertificateFactory.CreateSelfSignedCertificate();
        var context = CreateContext("10.42.1.5", Convert.ToBase64String(forwarded.RawData));
        context.Connection.ClientCertificate = handshake;
        var sut = CreateSut(enabled: true);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        Assert.Same(handshake, context.Connection.ClientCertificate);
        Assert.False(context.Request.Headers.ContainsKey(HeaderName));
    }

    [Fact]
    public async Task InvokeAsync_IPv4MappedIPv6InsideIPv4Cidr_IsTrusted()
    {
        // Arrange
        using var source = TestCertificateFactory.CreateSelfSignedCertificate();
        var context = CreateContext("::ffff:10.42.1.5", Convert.ToBase64String(source.RawData));
        X509Certificate2? seenByNext = null;
        var sut = CreateSut(enabled: true, next: _ =>
        {
            seenByNext = context.Connection.ClientCertificate;
            return Task.CompletedTask;
        });

        // Act
        await sut.InvokeAsync(context);

        // Assert
        Assert.NotNull(seenByNext);
        Assert.Equal(source.Thumbprint, seenByNext.Thumbprint);
    }

    private static DefaultHttpContext CreateContext(string remoteAddress, string? headerValue)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
        if (headerValue is not null)
        {
            context.Request.Headers[HeaderName] = headerValue;
        }

        return context;
    }

    private static ForwardedClientCertificateMiddleware CreateSut(
        bool enabled,
        int maxHeaderLength = 16384,
        RequestDelegate? next = null,
        List<string>? trustedProxies = null)
    {
        var options = new MtlsOptions
        {
            ForwardedCertificate = new ForwardedCertificateOptions
            {
                Enabled = enabled,
                TrustedProxies = trustedProxies ?? [TrustedCidr],
                MaxHeaderLength = maxHeaderLength
            }
        };

        return new ForwardedClientCertificateMiddleware(
            next ?? (_ => Task.CompletedTask),
            Options.Create(options),
            NullLogger<ForwardedClientCertificateMiddleware>.Instance);
    }
}
