using Dhadgar.Console.Services;
using Dhadgar.Contracts.Console;
using Microsoft.AspNetCore.SignalR;

namespace Dhadgar.Console.Hubs;

public sealed class ConsoleHub : Hub
{
    private readonly IConsoleSessionManager _sessionManager;
    private readonly IConsoleHistoryService _historyService;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IServerOwnershipValidator _ownershipValidator;
    private readonly ILogger<ConsoleHub> _logger;

    public ConsoleHub(
        IConsoleSessionManager sessionManager,
        IConsoleHistoryService historyService,
        ICommandDispatcher commandDispatcher,
        IServerOwnershipValidator ownershipValidator,
        ILogger<ConsoleHub> logger)
    {
        _sessionManager = sessionManager;
        _historyService = historyService;
        _commandDispatcher = commandDispatcher;
        _ownershipValidator = ownershipValidator;
        _logger = logger;
    }

    public Task Ping(CancellationToken ct = default) => Clients.Caller.SendAsync("pong", ct);

    /// <summary>
    /// Join a server's console session.
    /// </summary>
    public async Task JoinServer(JoinServerRequest request, CancellationToken ct = default)
    {
        var connectionId = Context.ConnectionId;

        // Get user info from context (if authenticated)
        var userId = GetUserId();
        var organizationId = GetOrganizationId();

        if (!organizationId.HasValue)
        {
            await Clients.Caller.SendAsync("error", "Not authenticated", ct);
            return;
        }

        // Validate user's organization matches the requested server's organization
        // The client must pass the correct organization ID for the server
        if (request.OrganizationId != organizationId.Value)
        {
            _logger.LogWarning("User from org {UserOrg} tried to join server in org {ServerOrg}",
                organizationId.Value, request.OrganizationId);
            await Clients.Caller.SendAsync("error", "Access denied", ct);
            return;
        }

        // Verify the server actually belongs to the caller's organization
        var ownsServer = await _ownershipValidator.ValidateOwnershipAsync(
            request.ServerId, organizationId.Value, ct);
        if (!ownsServer)
        {
            _logger.LogWarning("User from org {UserOrg} tried to join server {ServerId} that doesn't belong to their org",
                organizationId.Value, request.ServerId);
            await Clients.Caller.SendAsync("error", "Server not found", ct);
            return;
        }

        // Add to session
        await _sessionManager.AddConnectionAsync(connectionId, request.ServerId, organizationId.Value, userId, ct);

        // Add to SignalR group for this server
        await Groups.AddToGroupAsync(connectionId, GetServerGroup(request.ServerId), ct);

        _logger.LogInformation("Connection {ConnectionId} joined server {ServerId}",
            connectionId, request.ServerId);

        // Send recent history
        var history = await _historyService.GetRecentHistoryAsync(request.ServerId, request.HistoryLines, ct);
        await Clients.Caller.SendAsync("history", new ConsoleHistoryDto(
            request.ServerId,
            history,
            history.Count >= request.HistoryLines,
            history.Count > 0 ? history[^1].Timestamp : null), ct);

        await Clients.Caller.SendAsync("joined", request.ServerId, ct);
    }

    /// <summary>
    /// Leave a server's console session.
    /// </summary>
    public async Task LeaveServer(LeaveServerRequest request, CancellationToken ct = default)
    {
        var connectionId = Context.ConnectionId;

        await _sessionManager.RemoveConnectionAsync(connectionId, request.ServerId, ct);
        await Groups.RemoveFromGroupAsync(connectionId, GetServerGroup(request.ServerId), ct);

        _logger.LogInformation("Connection {ConnectionId} left server {ServerId}",
            connectionId, request.ServerId);

        await Clients.Caller.SendAsync("left", request.ServerId, ct);
    }

    /// <summary>
    /// Execute a command on a server.
    /// </summary>
    public async Task SendCommand(ExecuteCommandRequest request, CancellationToken ct = default)
    {
        var connectionId = Context.ConnectionId;
        var userId = GetUserId();
        var organizationId = GetOrganizationId();

        if (!organizationId.HasValue)
        {
            await Clients.Caller.SendAsync("error", "Not authenticated", ct);
            return;
        }

        // Check if connected to this server and validate tenant access
        var isConnected = await _sessionManager.IsConnectedToServerAsync(connectionId, request.ServerId, ct);
        if (!isConnected)
        {
            await Clients.Caller.SendAsync("error", "Not connected to this server", ct);
            return;
        }

        // Validate user's organization matches the server's organization
        var metadata = await _sessionManager.GetConnectionMetadataAsync(connectionId, request.ServerId, ct);
        if (metadata == null || metadata.Value.OrganizationId != organizationId.Value)
        {
            _logger.LogWarning("User from org {UserOrg} tried to send command to server they don't own",
                organizationId.Value);
            await Clients.Caller.SendAsync("error", "Access denied", ct);
            return;
        }

        var result = await _commandDispatcher.DispatchCommandAsync(
            request.ServerId,
            organizationId.Value,
            request.Command,
            userId,
            Context.User?.Identity?.Name,
            connectionId,
            GetClientIpHash(),
            ct);

        await Clients.Caller.SendAsync("commandResult", result, ct);

        // Broadcast command to all connections on this server
        await Clients.Group(GetServerGroup(request.ServerId)).SendAsync("output", new ConsoleOutputDto(
            request.ServerId,
            ConsoleOutputType.Command,
            $"> {request.Command}",
            DateTime.UtcNow,
            0), ct);
    }

    /// <summary>
    /// Request console history.
    /// </summary>
    public async Task RequestHistory(RequestHistoryRequest request, CancellationToken ct = default)
    {
        var connectionId = Context.ConnectionId;

        // Check if connected to this server
        var isConnected = await _sessionManager.IsConnectedToServerAsync(connectionId, request.ServerId, ct);
        if (!isConnected)
        {
            await Clients.Caller.SendAsync("error", "Not connected to this server", ct);
            return;
        }

        var history = await _historyService.GetRecentHistoryAsync(request.ServerId, request.LineCount, ct);
        await Clients.Caller.SendAsync("history", new ConsoleHistoryDto(
            request.ServerId,
            history,
            history.Count >= request.LineCount,
            history.Count > 0 ? history[^1].Timestamp : null), ct);
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
