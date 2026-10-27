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
public sealed class TestServerClientFactory(TestServer server, bool stripApiPrefix = true) : IHttpClientFactory
{
    /// <inheritdoc />
    public HttpClient CreateClient(string name)
    {
        // Ownership passes to the HttpClient (disposeHandler: true) and then to the agent,
        // which disposes the client after each request.
#pragma warning disable CA2000
        HttpMessageHandler handler = server.CreateHandler();
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
