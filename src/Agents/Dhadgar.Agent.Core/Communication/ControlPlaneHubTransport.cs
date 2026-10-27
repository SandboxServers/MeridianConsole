using System.Net.Http;
using Microsoft.AspNetCore.Http.Connections;

namespace Dhadgar.Agent.Core.Communication;

/// <summary>
/// Transport options for the agent hub connection.
/// <para>
/// Registered through DI or passed to <see cref="ControlPlaneClient"/>'s constructor only. It is never bound from
/// configuration: <see cref="HttpMessageHandlerFactory"/> can bypass TLS, so it must not be reachable from settings.
/// </para>
/// </summary>
public sealed class ControlPlaneHubTransport
{
    /// <summary>Optional wrapper around the SignalR client's HttpClientHandler. Null = default. Tests only.</summary>
    public Func<HttpMessageHandler, HttpMessageHandler>? HttpMessageHandlerFactory { get; init; }

    /// <summary>Transports the SignalR client may use.</summary>
    public HttpTransportType Transports { get; init; } = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
}
