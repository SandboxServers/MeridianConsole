using Microsoft.AspNetCore.SignalR;

namespace Dhadgar.Console.Hubs;

public sealed class ConsoleHub : Hub
{
    public Task Ping() => Clients.Caller.SendAsync("pong");
}
