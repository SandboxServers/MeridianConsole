using Dhadgar.Console.Services;
using Dhadgar.Contracts.Console;
using Microsoft.AspNetCore.SignalR;

namespace Dhadgar.Console.Hubs;

public sealed class ConsoleHub : Hub
{
    private readonly IConsoleSessionManager _sessionManager;
    private readonly IConsoleHistoryService _historyService;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly ILogger<ConsoleHub> _logger;

    public ConsoleHub(
        IConsoleSessionManager sessionManager,
        IConsoleHistoryService historyService,
        ICommandDispatcher commandDispatcher,
        ILogger<ConsoleHub> logger)
    {
        _sessionManager = sessionManager;
        _historyService = historyService;
        _commandDispatcher = commandDispatcher;
        _logger = logger;
    }

    public Task Ping() => Clients.Caller.SendAsync("pong");

    /// <summary>
    /// Join a server's console session.
    /// </summary>
    public async Task JoinServer(JoinServerRequest request)
    {
        var connectionId = Context.ConnectionId;

        // Get user info from context (if authenticated)
        var userId = GetUserId();
        var organizationId = GetOrganizationId();

        if (!organizationId.HasValue)
        {
            await Clients.Caller.SendAsync("error", "Not authenticated");
            return;
        }

        // TODO: Validate user has access to this server

        // Add to session
        await _sessionManager.AddConnectionAsync(connectionId, request.ServerId, organizationId.Value, userId);

        // Add to SignalR group for this server
        await Groups.AddToGroupAsync(connectionId, GetServerGroup(request.ServerId));

        _logger.LogInformation("Connection {ConnectionId} joined server {ServerId}",
            connectionId, request.ServerId);

        // Send recent history
        var history = await _historyService.GetRecentHistoryAsync(request.ServerId, request.HistoryLines);
        await Clients.Caller.SendAsync("history", new ConsoleHistoryDto(
            request.ServerId,
            history,
            history.Count >= request.HistoryLines,
            history.Count > 0 ? history[^1].Timestamp : null));

        await Clients.Caller.SendAsync("joined", request.ServerId);
    }

    /// <summary>
    /// Leave a server's console session.
    /// </summary>
    public async Task LeaveServer(LeaveServerRequest request)
    {
        var connectionId = Context.ConnectionId;

        await _sessionManager.RemoveConnectionAsync(connectionId, request.ServerId);
        await Groups.RemoveFromGroupAsync(connectionId, GetServerGroup(request.ServerId));

        _logger.LogInformation("Connection {ConnectionId} left server {ServerId}",
            connectionId, request.ServerId);

        await Clients.Caller.SendAsync("left", request.ServerId);
    }

    /// <summary>
    /// Execute a command on a server.
    /// </summary>
    public async Task SendCommand(ExecuteCommandRequest request)
    {
        var connectionId = Context.ConnectionId;
        var userId = GetUserId();
        var organizationId = GetOrganizationId();

        if (!organizationId.HasValue)
        {
            await Clients.Caller.SendAsync("error", "Not authenticated");
            return;
        }

        // Check if connected to this server
        var isConnected = await _sessionManager.IsConnectedToServerAsync(connectionId, request.ServerId);
        if (!isConnected)
        {
            await Clients.Caller.SendAsync("error", "Not connected to this server");
            return;
        }

        // TODO: Validate user has permission to execute commands

        var result = await _commandDispatcher.DispatchCommandAsync(
            request.ServerId,
            organizationId.Value,
            request.Command,
            userId,
            Context.User?.Identity?.Name,
            connectionId,
            GetClientIpHash());

        await Clients.Caller.SendAsync("commandResult", result);

        // Broadcast command to all connections on this server
        await Clients.Group(GetServerGroup(request.ServerId)).SendAsync("output", new ConsoleOutputDto(
            request.ServerId,
            ConsoleOutputType.Command,
            $"> {request.Command}",
            DateTime.UtcNow,
            0));
    }

    /// <summary>
    /// Request console history.
    /// </summary>
    public async Task RequestHistory(RequestHistoryRequest request)
    {
        var connectionId = Context.ConnectionId;

        // Check if connected to this server
        var isConnected = await _sessionManager.IsConnectedToServerAsync(connectionId, request.ServerId);
        if (!isConnected)
        {
            await Clients.Caller.SendAsync("error", "Not connected to this server");
            return;
        }

        var history = await _historyService.GetRecentHistoryAsync(request.ServerId, request.LineCount);
        await Clients.Caller.SendAsync("history", new ConsoleHistoryDto(
            request.ServerId,
            history,
            history.Count >= request.LineCount,
            history.Count > 0 ? history[^1].Timestamp : null));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;

        // Remove from all sessions
        await _sessionManager.RemoveAllConnectionsAsync(connectionId);

        _logger.LogInformation("Connection {ConnectionId} disconnected", connectionId);

        await base.OnDisconnectedAsync(exception);
    }

    private static string GetServerGroup(Guid serverId) => $"server-{serverId}";

    private Guid? GetUserId()
    {
        var userIdClaim = Context.User?.FindFirst("sub")?.Value
            ?? Context.User?.FindFirst("user_id")?.Value;

        if (Guid.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }

        return null;
    }

    private Guid? GetOrganizationId()
    {
        var orgIdClaim = Context.User?.FindFirst("org_id")?.Value;

        if (Guid.TryParse(orgIdClaim, out var orgId))
        {
            return orgId;
        }

        return null;
    }

    private string? GetClientIpHash()
    {
        // Get client IP from HttpContext and hash it
        var httpContext = Context.GetHttpContext();
        var clientIp = httpContext?.Connection.RemoteIpAddress?.ToString();

        if (string.IsNullOrEmpty(clientIp))
        {
            return null;
        }

        // Simple hash (in production, use a proper hashing algorithm)
        return Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(clientIp)))[..16];
    }
}
