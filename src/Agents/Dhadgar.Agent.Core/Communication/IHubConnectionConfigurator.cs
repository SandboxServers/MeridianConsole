namespace Dhadgar.Agent.Core.Communication;

/// <summary>
/// Adjusts SignalR connection options after the agent has applied its own (endpoint, client certificate,
/// headers). For tests and diagnostics only; production hosts register none.
/// <para>
/// After the configurator runs, the agent re-checks that the URL is still HTTPS and that the client certificate
/// is still present, and throws otherwise. Replacing <c>HttpMessageHandlerFactory</c> is not checked by the agent;
/// it is permitted only for test harnesses.
/// </para>
/// </summary>
public interface IHubConnectionConfigurator
{
    void Configure(Microsoft.AspNetCore.Http.Connections.Client.HttpConnectionOptions options);
}
