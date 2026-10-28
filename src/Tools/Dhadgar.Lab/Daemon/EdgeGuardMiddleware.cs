using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Dhadgar.Lab.Daemon;

/// <summary>
/// State every request reads: the expected token bytes and the Host allowlist built from the bound port.
/// Disposing zeroes the token.
/// </summary>
internal sealed class EdgeState : IDisposable
{
    private readonly byte[] _token;
    private volatile IReadOnlySet<string> _allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    internal EdgeState(byte[] token)
    {
        _token = token;
    }

    /// <summary>The expected token bytes. Owned by the daemon; zeroed on dispose.</summary>
    internal byte[] Token => _token;

    /// <summary>The exact <c>Host</c> header values accepted: <c>127.0.0.1:port</c>, <c>localhost:port</c>, <c>[::1]:port</c>.</summary>
    internal IReadOnlySet<string> AllowedHosts => _allowedHosts;

    /// <summary>
    /// Builds the Host allowlist from the port the server bound. Until this runs, every request is refused (fail closed).
    /// </summary>
    internal void Bind(int port)
    {
        var suffix = ":" + port.ToString(CultureInfo.InvariantCulture);
        _allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "127.0.0.1" + suffix,
            "localhost" + suffix,
            "[::1]" + suffix,
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CryptographicOperations.ZeroMemory(_token);
    }
}

/// <summary>
/// The one guard in front of every route, in the refusal order of C2: Host (403), Origin (403), bearer (401).
/// Refusals carry no body and never log the token, the header or any value; they log <c>lab.edge.refused</c> with the reason.
/// </summary>
internal sealed class EdgeGuardMiddleware
{
    private const string BearerPrefix = "Bearer ";

    private readonly RequestDelegate _next;
    private readonly EdgeState _state;
    private readonly ILogger<EdgeGuardMiddleware> _logger;

    public EdgeGuardMiddleware(RequestDelegate next, EdgeState state, ILogger<EdgeGuardMiddleware> logger)
    {
        _next = next;
        _state = state;
        _logger = logger;
    }

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_state.AllowedHosts.Contains(context.Request.Host.Value ?? string.Empty))
        {
            return RefuseAsync(context, StatusCodes.Status403Forbidden, "host");
        }

        if (context.Request.Headers.ContainsKey("Origin"))
        {
            return RefuseAsync(context, StatusCodes.Status403Forbidden, "origin");
        }

        if (!IsAuthorized(context.Request))
        {
            return RefuseAsync(context, StatusCodes.Status401Unauthorized, "auth");
        }

        return _next(context);
    }

    private bool IsAuthorized(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.Length <= BearerPrefix.Length
            || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] presented;
        try
        {
            presented = Base64Url.DecodeFromChars(header.AsSpan(BearerPrefix.Length));
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(presented, _state.Token);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(presented);
        }
    }

    private Task RefuseAsync(HttpContext context, int status, string reason)
    {
        _logger.LogWarning("lab.edge.refused {Reason}", reason);
        context.Response.StatusCode = status;
        if (status == StatusCodes.Status401Unauthorized)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
        }

        return Task.CompletedTask;
    }
}
