using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Daemon;

/// <summary>
/// The loopback daemon that fronts the lab's MCP tools. L01 ships the contract only; <see cref="StartAsync"/> refuses until L02.
/// </summary>
public sealed class LabDaemon : IAsyncDisposable
{
    internal LabDaemon(Uri baseAddress, IServiceProvider services)
    {
        BaseAddress = baseAddress;
        Services = services;
    }

    /// <summary>
    /// Starts the daemon. Every refusal is a <c>Failure</c>; nothing throws for configuration errors.
    /// </summary>
    public static Task<Result<LabDaemon>> StartAsync(LabDaemonOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(Result<LabDaemon>.Failure("[Lab.NotImplemented] daemon"));
    }

    /// <summary>
    /// Base address of the running daemon: <c>http://127.0.0.1:&lt;bound port&gt;/</c>.
    /// </summary>
    public Uri BaseAddress { get; }

    public IServiceProvider Services { get; }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Options for <see cref="LabDaemon.StartAsync"/>. Port 0 binds an ephemeral port (tests only).
/// </summary>
public sealed record LabDaemonOptions(
    int Port, string Token, string DataRoot,
    bool EnforceSingleInstance = true, bool RefuseElevated = true,
    TimeProvider? Time = null, string? RepoRoot = null);
