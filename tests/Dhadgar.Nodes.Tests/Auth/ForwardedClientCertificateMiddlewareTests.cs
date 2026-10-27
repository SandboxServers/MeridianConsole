using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Auth;
using Dhadgar.Nodes.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
        var connectionTls = new SharedConnectionTls();
        var context = CreateContext("10.42.1.5", Convert.ToBase64String(source.RawData));
        context.Features.Set<ITlsConnectionFeature>(connectionTls);
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
        Assert.Null(connectionTls.ClientCertificate);
        Assert.False(context.Request.Headers.ContainsKey(HeaderName));
    }

    [Fact]
    public async Task InvokeAsync_SequentialRequestsOnSharedConnection_EachSeesOnlyItsOwnForwardedCertificate()
    {
        // Arrange: two requests over one connection (keep-alive), each forwarding a different agent certificate.
        using var first = TestCertificateFactory.CreateSelfSignedCertificate();
        using var second = TestCertificateFactory.CreateSelfSignedCertificate();
        var connectionTls = new SharedConnectionTls();
        var firstRequest = CreateContext("10.42.1.5", Convert.ToBase64String(first.RawData));
        var secondRequest = CreateContext("10.42.1.5", Convert.ToBase64String(second.RawData));
        firstRequest.Features.Set<ITlsConnectionFeature>(connectionTls);
        secondRequest.Features.Set<ITlsConnectionFeature>(connectionTls);
        X509Certificate2? seenByFirst = null;
        X509Certificate2? seenBySecond = null;
        var sut = CreateSut(enabled: true, next: ctx =>
        {
            if (ReferenceEquals(ctx, firstRequest))
            {
                seenByFirst = ctx.Connection.ClientCertificate;
            }
            else
            {
                seenBySecond = ctx.Connection.ClientCertificate;
            }

            return Task.CompletedTask;
        });

        // Act
        await sut.InvokeAsync(firstRequest);
        await sut.InvokeAsync(secondRequest);

        // Assert
        Assert.NotNull(seenByFirst);
        Assert.Equal(first.Thumbprint, seenByFirst.Thumbprint);
        Assert.NotNull(seenBySecond);
        Assert.Equal(second.Thumbprint, seenBySecond.Thumbprint);
        Assert.Null(connectionTls.ClientCertificate);
    }

    [Fact]
    public async Task InvokeAsync_ConcurrentStreamsOnOneConnection_EachNextReadsItsOwnCertificate()
    {
        // Arrange: two HTTP/2 streams share one connection. Both are in flight before either reaches next.
        using var first = TestCertificateFactory.CreateSelfSignedCertificate();
        using var second = TestCertificateFactory.CreateSelfSignedCertificate();
        var connectionTls = new SharedConnectionTls();
        var streamA = CreateContext("10.42.1.5", Convert.ToBase64String(first.RawData));
        var streamB = CreateContext("10.42.1.5", Convert.ToBase64String(second.RawData));
        streamA.Features.Set<ITlsConnectionFeature>(connectionTls);
        streamB.Features.Set<ITlsConnectionFeature>(connectionTls);

        var bothInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = 0;
        var seen = new ConcurrentDictionary<HttpContext, X509Certificate2?>();
        var sut = CreateSut(enabled: true, next: async ctx =>
        {
            if (Interlocked.Increment(ref inFlight) == 2)
            {
                bothInFlight.SetResult();
            }

            await bothInFlight.Task.WaitAsync(TimeSpan.FromSeconds(5));
            seen[ctx] = await ctx.Connection.GetClientCertificateAsync(CancellationToken.None);
        });

        // Act
        await Task.WhenAll(sut.InvokeAsync(streamA), sut.InvokeAsync(streamB));

        // Assert
        Assert.NotNull(seen[streamA]);
        Assert.Equal(first.Thumbprint, seen[streamA]!.Thumbprint);
        Assert.NotNull(seen[streamB]);
        Assert.Equal(second.Thumbprint, seen[streamB]!.Thumbprint);
        Assert.Null(connectionTls.ClientCertificate);
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
                TrustedProxies = new Collection<string>(trustedProxies ?? [TrustedCidr]),
                MaxHeaderLength = maxHeaderLength
            }
        };

        return new ForwardedClientCertificateMiddleware(
            next ?? (_ => Task.CompletedTask),
            Options.Create(options),
            NullLogger<ForwardedClientCertificateMiddleware>.Instance);
    }

    /// <summary>
    /// Stands in for the connection-level TLS feature that Kestrel shares across every stream of one connection.
    /// </summary>
    private sealed class SharedConnectionTls : ITlsConnectionFeature
    {
        public X509Certificate2? ClientCertificate { get; set; }

        public Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ClientCertificate);
    }
}
