namespace Dhadgar.Lab.Leasing;

/// <summary>
/// Calls <see cref="LeaseBook.Sweep"/> every 5 seconds on the injected <see cref="TimeProvider"/>, so an overdue lease ends
/// even when nobody calls the daemon.
/// </summary>
public sealed class LeaseSweeper : BackgroundService
{
    /// <summary>
    /// How often the sweep runs.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly LeaseBook _book;
    private readonly TimeProvider _time;
    private readonly ILogger<LeaseSweeper> _logger;

    public LeaseSweeper(LeaseBook book, TimeProvider time, ILogger<LeaseSweeper> logger)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _book = book;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval, _time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    _book.Sweep();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Only the exception type is logged: messages from hooks may carry arguments.
                    _logger.LogWarning("Lease sweep failed with {ExceptionType}", ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
