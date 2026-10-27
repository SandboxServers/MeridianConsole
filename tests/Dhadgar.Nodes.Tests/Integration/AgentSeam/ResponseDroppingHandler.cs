namespace Dhadgar.Nodes.Tests.Integration.AgentSeam;

/// <summary>
/// Simulates a lost renewal response. For the first request whose path ends in <c>/certificates/renew</c>, it lets
/// the request reach Nodes (so Nodes commits the new certificate), then disposes the response and throws, as a
/// network drop after the server has acted would. Later requests pass through unchanged.
/// </summary>
public sealed class ResponseDroppingHandler : DelegatingHandler
{
    private const string RenewPathSuffix = "/certificates/renew";
    private int _dropped;

    /// <summary>The number of renewal responses dropped so far (0 or 1).</summary>
    public int DroppedCount => Volatile.Read(ref _dropped);

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (request.RequestUri is not null
            && request.RequestUri.AbsolutePath.EndsWith(RenewPathSuffix, StringComparison.Ordinal)
            && Interlocked.CompareExchange(ref _dropped, 1, 0) == 0)
        {
            response.Dispose();
            throw new HttpRequestException("simulated lost renewal response");
        }

        return response;
    }
}
