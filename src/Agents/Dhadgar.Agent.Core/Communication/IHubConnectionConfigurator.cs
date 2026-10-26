namespace Dhadgar.Agent.Core.Communication;

/// <summary>
/// Adjusts SignalR connection options after the agent has applied its own (endpoint, client certificate,
/// headers). For tests and diagnostics only; production hosts register none.
/// </summary>
public interface IHubConnectionConfigurator
{
    void Configure(Microsoft.AspNetCore.Http.Connections.Client.HttpConnectionOptions options);
}
