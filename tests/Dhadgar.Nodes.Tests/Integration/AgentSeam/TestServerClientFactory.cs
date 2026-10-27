using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.TestHost;

namespace Dhadgar.Nodes.Tests.Integration.AgentSeam;

/// <summary>
/// Rewrites <c>/api/v1/...</c> to <c>/...</c>, as the Gateway does.
/// </summary>
public sealed class StripApiPrefixHandler : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        if (uri.AbsolutePath.StartsWith("/api/v1/", StringComparison.Ordinal))
        {
            request.RequestUri = new UriBuilder(uri) { Path = uri.AbsolutePath["/api/v1".Length..] }.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Like the real IHttpClientFactory, returns a new client per call: the agent disposes
/// the client after each request. The agent calls https://&lt;gateway&gt;/api/v1/agents/...;
/// the Gateway strips /api/v1 (pinned by Gateway RouteConfigurationTests), reproduced here
/// when <paramref name="stripApiPrefix"/> is true.
/// </summary>
/// <param name="server">The TestServer to route requests to.</param>
/// <param name="stripApiPrefix">When true, strips <c>/api/v1</c> the way the Gateway does.</param>
/// <param name="certificate">
/// Optional source of the client certificate the agent's <c>ControlPlaneMtls</c> client presents on the
/// TLS connection. Read on every request, so it follows the store's current certificate.
/// </param>
/// <param name="wrapMtlsHandler">Optional handler layer placed around the <c>ControlPlaneMtls</c> handler.</param>
public sealed class TestServerClientFactory(
    TestServer server,
    bool stripApiPrefix = true,
    Func<X509Certificate2?>? certificate = null,
    Func<HttpMessageHandler, HttpMessageHandler>? wrapMtlsHandler = null) : IHttpClientFactory
{
    /// <inheritdoc />
    public HttpClient CreateClient(string name)
    {
        // Ownership passes to the HttpClient (disposeHandler: true) and then to the agent,
        // which disposes the client after each request.
        var isMtls = string.Equals(name, "ControlPlaneMtls", StringComparison.Ordinal);
#pragma warning disable CA2000
        HttpMessageHandler handler = isMtls && certificate is not null
            ? server.CreateHandler(ctx => ctx.Connection.ClientCertificate = certificate())
            : server.CreateHandler();
        if (isMtls && wrapMtlsHandler is not null)
        {
            handler = wrapMtlsHandler(handler);
        }

        if (stripApiPrefix)
        {
            handler = new StripApiPrefixHandler { InnerHandler = handler };
        }
#pragma warning restore CA2000

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("https://localhost/")
        };
    }
}
