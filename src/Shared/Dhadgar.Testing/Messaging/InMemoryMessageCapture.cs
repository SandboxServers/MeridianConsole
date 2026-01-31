using System.Collections.Concurrent;

namespace Dhadgar.Testing.Messaging;

/// <summary>
/// Thread-safe in-memory message capture utility for testing message publishing.
/// Captures messages of any type and provides methods to retrieve and inspect them.
/// Messages are stored in FIFO (first-in, first-out) order.
/// </summary>
/// <remarks>
/// This class uses a bounded capacity to prevent memory exhaustion during tests.
/// When the capacity is reached, the oldest messages are dropped to make room for new ones.
/// </remarks>
public sealed class InMemoryMessageCapture : IDisposable
{
    private readonly int _capacity;
    private readonly ConcurrentQueue<object> _messages = new();
    private readonly SemaphoreSlim _messageAvailable = new(0);
    private int _count;
    private bool _disposed;

    /// <summary>
    /// Creates a new instance of <see cref="InMemoryMessageCapture"/> with the specified capacity.
    /// </summary>
    /// <param name="capacity">Maximum number of messages to retain (default: 10000). When exceeded, oldest messages are dropped.</param>
    public InMemoryMessageCapture(int capacity = 10000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);
        _capacity = capacity;
    }

    /// <summary>
    /// Captures a message for later inspection.
    /// </summary>
    /// <typeparam name="T">The type of message to capture</typeparam>
    /// <param name="message">The message to capture</param>
    /// <remarks>
    /// If the capture is at capacity, the oldest message will be dropped to make room.
    /// </remarks>
    public void Capture<T>(T message) where T : notnull
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Enforce capacity - drop oldest if at limit
        while (Interlocked.CompareExchange(ref _count, 0, 0) >= _capacity)
        {
            if (_messages.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _count);
            }
            else
            {
                break;
            }
        }

        _messages.Enqueue(message);
        Interlocked.Increment(ref _count);
        _messageAvailable.Release();
    }

    /// <summary>
    /// Retrieves all captured messages of a specific type in insertion order.
    /// </summary>
    /// <typeparam name="T">The type of messages to retrieve</typeparam>
    /// <returns>Read-only list of all messages of the specified type</returns>
    public IReadOnlyList<T> GetMessages<T>()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var snapshot = _messages.ToArray();
        return snapshot.OfType<T>().ToList();
    }

    /// <summary>
    /// Retrieves the last captured message of a specific type.
    /// </summary>
    /// <typeparam name="T">The type of message to retrieve</typeparam>
    /// <returns>The last message of the specified type, or null if no messages exist</returns>
    public T? GetLastMessage<T>() where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var snapshot = _messages.ToArray();
        return snapshot.OfType<T>().LastOrDefault();
    }

    /// <summary>
    /// Checks if any message of the specified type exists, optionally matching a predicate.
    /// </summary>
    /// <typeparam name="T">The type of message to check for</typeparam>
    /// <param name="predicate">Optional predicate to filter messages</param>
    /// <returns>True if a matching message exists, false otherwise</returns>
    public bool HasMessage<T>(Func<T, bool>? predicate = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var snapshot = _messages.ToArray();
        var messages = snapshot.OfType<T>();
        return predicate == null
            ? messages.Any()
            : messages.Any(predicate);
    }

    /// <summary>
    /// Waits asynchronously for a message of the specified type to be captured.
    /// </summary>
    /// <typeparam name="T">The type of message to wait for</typeparam>
    /// <param name="timeout">Maximum time to wait (default: 5 seconds)</param>
    /// <returns>The captured message</returns>
    /// <exception cref="TimeoutException">Thrown if the message is not captured within the timeout period</exception>
    /// <remarks>
    /// This method uses efficient signaling instead of polling. It will return immediately
    /// if a message of the specified type already exists.
    /// </remarks>
    public async Task<T> WaitForMessageAsync<T>(TimeSpan? timeout = null) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var actualTimeout = timeout ?? TimeSpan.FromSeconds(5);
        using var cts = new CancellationTokenSource(actualTimeout);

        while (!cts.Token.IsCancellationRequested)
        {
            var message = GetLastMessage<T>();
            if (message != null)
            {
                return message;
            }

            try
            {
                await _messageAvailable.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        throw new TimeoutException($"No message of type {typeof(T).Name} was captured within {actualTimeout.TotalSeconds} seconds.");
    }

    /// <summary>
    /// Clears all captured messages.
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (_messages.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _count);
        }
        Interlocked.Exchange(ref _count, 0); // Ensure reset
    }

    /// <summary>
    /// Disposes of the message capture instance.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes of managed resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer</param>
    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _messages.Clear();
            Interlocked.Exchange(ref _count, 0);
            _messageAvailable.Dispose();
        }

        _disposed = true;
    }
}
