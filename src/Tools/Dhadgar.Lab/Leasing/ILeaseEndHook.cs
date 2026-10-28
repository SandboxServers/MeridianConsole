using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Lab.Leasing;

/// <summary>
/// Work to do when a lease ends (released, expired or taken over). L19 registers the FaultBook heal-all, L27 the job cancel
/// and L30 the pool release. Each hook runs in its own try/catch, so no hook can stop another.
/// </summary>
public interface ILeaseEndHook
{
    /// <summary>
    /// Runs after the lease ended. The entry never carries the lease id.
    /// </summary>
    Task OnLeaseEndedAsync(LeaseHistoryEntry entry, CancellationToken ct);
}

/// <summary>
/// Relays every <see cref="LeaseBook.LeaseEnded"/> to each registered <see cref="ILeaseEndHook"/>. Subscribed for the daemon's run.
/// </summary>
public sealed class LeaseEndRelay : IHostedService
{
    private readonly LeaseBook _book;
    private readonly ILeaseEndHook[] _hooks;
    private readonly ILogger<LeaseEndRelay> _logger;

    /// <summary>
    /// Creates the relay over the lease book and the registered hooks.
    /// </summary>
    public LeaseEndRelay(LeaseBook book, IEnumerable<ILeaseEndHook> hooks, ILogger<LeaseEndRelay> logger)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));
        ArgumentNullException.ThrowIfNull(hooks);
        _hooks = hooks.ToArray();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _book.LeaseEnded += OnLeaseEnded;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _book.LeaseEnded -= OnLeaseEnded;
        return Task.CompletedTask;
    }

    private void OnLeaseEnded(object? sender, LeaseHistoryEntry entry)
    {
        foreach (ILeaseEndHook hook in _hooks)
        {
            try
            {
                // The lease book raises this outside its lock, on the caller's thread; hooks finish before the call returns.
                hook.OnLeaseEndedAsync(entry, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Only the names are logged: a failure message can carry lease detail.
                _logger.LogWarning("lab.lease hook {Hook} failed with {ExceptionType}", hook.GetType().Name, ex.GetType().Name);
            }
        }
    }
}
