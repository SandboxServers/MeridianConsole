using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using Dhadgar.Lab.Audit;
using Dhadgar.Shared.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Lab.Daemon;

/// <summary>
/// The loopback daemon that fronts the lab's MCP tools. Binds 127.0.0.1 and ::1 only; every request passes the edge guard
/// (Host, Origin, bearer token) before routing. Every refusal from <see cref="StartAsync"/> is a <c>Failure</c>.
/// </summary>
public sealed class LabDaemon : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly EdgeState _edge;
    private readonly SingleInstanceGuard? _guard;
    private int _disposed;

    private LabDaemon(WebApplication app, Uri baseAddress, EdgeState edge, SingleInstanceGuard? guard)
    {
        _app = app;
        BaseAddress = baseAddress;
        _edge = edge;
        _guard = guard;
    }

    /// <summary>
    /// Starts the daemon. Refusals, in order: <c>Lab:BindAddress</c> not loopback, bad token (<c>[Lab.InvalidArgument]</c>),
    /// elevated process (<c>[Lab.Elevated]</c>), second instance (<c>[Lab.AlreadyRunning]</c>), bind failure (<c>[Lab.Unavailable]</c>).
    /// </summary>
    public static async Task<Result<LabDaemon>> StartAsync(LabDaemonOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();

        var settings = LoadSettings();
        var bindAddress = settings["Lab:BindAddress"];
        if (!string.IsNullOrWhiteSpace(bindAddress) && !IsLoopbackBind(bindAddress))
        {
            return Result<LabDaemon>.Failure("[Lab.InvalidArgument] Lab:BindAddress must be loopback; the daemon binds 127.0.0.1 and ::1 only");
        }

        if (options.Port is < 0 or > 65535)
        {
            return Result<LabDaemon>.Failure("[Lab.InvalidArgument] port must be between 0 and 65535");
        }

        var parsed = TokenValidator.ParseToken(options.Token);
        if (parsed.IsFailure)
        {
            return Result<LabDaemon>.Failure(parsed.Error);
        }

        var token = parsed.Value;
        if (options.RefuseElevated && ElevationCheck.IsElevated())
        {
            CryptographicOperations.ZeroMemory(token);
            return Result<LabDaemon>.Failure("[Lab.Elevated] the daemon refuses to run elevated");
        }

        LabPaths paths;
        try
        {
            paths = LabPaths.FromRoot(options.DataRoot, options.RepoRoot ?? settings["Lab:RepoRoot"]);
        }
        catch (ArgumentException)
        {
            CryptographicOperations.ZeroMemory(token);
            return Result<LabDaemon>.Failure("[Lab.InvalidArgument] the data root is not a valid path");
        }

        SingleInstanceGuard? guard = null;
        if (options.EnforceSingleInstance)
        {
            var acquired = SingleInstanceGuard.Acquire(paths);
            if (acquired.IsFailure)
            {
                CryptographicOperations.ZeroMemory(token);
                return Result<LabDaemon>.Failure(acquired.Error);
            }

            guard = acquired.Value;
        }

        var edge = new EdgeState(token);
        WebApplication? app = null;
        var started = false;
        try
        {
            var time = options.Time ?? TimeProvider.System;
            var startedAt = time.GetUtcNow();
            app = BuildApplication(options.Port, settings, edge, time);
            app.UseMiddleware<EdgeGuardMiddleware>();
            app.MapStatus(startedAt);

            await app.StartAsync(ct).ConfigureAwait(false);

            if (!AllAddressesLoopback(app))
            {
                return Result<LabDaemon>.Failure("[Lab.InvalidArgument] the daemon binds loopback only; remove the Kestrel endpoint configuration");
            }

            var port = BoundLoopbackPort(app);
            if (port is null)
            {
                return Result<LabDaemon>.Failure("[Lab.Unavailable] the daemon bound no IPv4 loopback address");
            }

            edge.Bind(port.Value);
            var baseAddress = new Uri("http://127.0.0.1:" + port.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/");
            started = true;
            return Result<LabDaemon>.Success(new LabDaemon(app, baseAddress, edge, guard));
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
        {
            return Result<LabDaemon>.Failure("[Lab.Unavailable] the daemon could not bind its loopback port");
        }
        finally
        {
            if (!started)
            {
                if (app is not null)
                {
                    await app.DisposeAsync().ConfigureAwait(false);
                }

                guard?.Dispose();
                edge.Dispose();
            }
        }
    }

    /// <summary>
    /// Base address of the running daemon: <c>http://127.0.0.1:&lt;bound port&gt;/</c>.
    /// </summary>
    public Uri BaseAddress { get; }

    /// <summary>
    /// The daemon's own service provider. Built with scope and build-time validation.
    /// </summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>
    /// Stops the server, releases the pidfile and mutex, and zeroes the token. Safe to call more than once.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _app.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await _app.DisposeAsync().ConfigureAwait(false);
            _guard?.Dispose();
            _edge.Dispose();
        }
    }

    /// <summary>
    /// Reads <c>labsettings.json</c> next to the executable, then environment variables. Never reads <c>appsettings.json</c>.
    /// </summary>
    internal static IConfiguration LoadSettings() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("labsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

    private static WebApplication BuildApplication(int port, IConfiguration settings, EdgeState edge, TimeProvider time)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Only labsettings.json and the environment feed the daemon; the default appsettings files are dropped.
        // The Kestrel section is dropped too: an endpoint configured there would open a listener this code does not choose.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings.AsEnumerable().Where(entry => !IsKestrelKey(entry.Key)));

        // ASPNETCORE_URLS and hosting URL settings are ignored: the listeners below are the only ones.
        builder.WebHost.UseUrls();
        builder.WebHost.PreferHostingUrls(false);
        builder.WebHost.UseKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Loopback, port);

            // Port 0 is the test-only ephemeral mode; a second listener would bind a different port, so IPv6 is skipped there.
            if (port != 0 && Socket.OSSupportsIPv6)
            {
                options.Listen(IPAddress.IPv6Loopback, port);
            }
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });

        builder.Services.AddSingleton(time);
        builder.Services.AddSingleton(edge);
        builder.Services.AddSingleton<ILabEvents, NullLabEvents>();

        return builder.Build();
    }

    private static bool IsKestrelKey(string key) =>
        string.Equals(key, "Kestrel", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Kestrel:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when every address the server bound is a loopback host. An unparseable address counts as not loopback.
    /// </summary>
    private static bool AllAddressesLoopback(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is null)
        {
            return true;
        }

        foreach (var address in addresses)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || !IsLoopbackBind(uri.Host))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The IPv4 loopback port the server actually bound, or null when there is none.
    /// </summary>
    private static int? BoundLoopbackPort(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is null)
        {
            return null;
        }

        foreach (var address in addresses)
        {
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Host == "127.0.0.1")
            {
                return uri.Port;
            }
        }

        return null;
    }

    private static bool IsLoopbackBind(string value)
    {
        var trimmed = value.Trim();
        if (string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var bare = trimmed.TrimStart('[').TrimEnd(']');
        return IPAddress.TryParse(bare, out var address) && IPAddress.IsLoopback(address);
    }
}

/// <summary>
/// Options for <see cref="LabDaemon.StartAsync"/>. Port 0 binds an ephemeral IPv4 loopback port (tests only).
/// <c>ToString</c> omits <c>Token</c>.
/// </summary>
public sealed record LabDaemonOptions(
    int Port, string Token, string DataRoot,
    bool EnforceSingleInstance = true, bool RefuseElevated = true,
    TimeProvider? Time = null, string? RepoRoot = null)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Port = ").Append(Port)
            .Append(", DataRoot = ").Append(DataRoot)
            .Append(", EnforceSingleInstance = ").Append(EnforceSingleInstance)
            .Append(", RefuseElevated = ").Append(RefuseElevated);
        return true;
    }
}
