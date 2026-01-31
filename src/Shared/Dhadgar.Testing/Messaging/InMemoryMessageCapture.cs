using System.Collections.Concurrent;

namespace Dhadgar.Testing.Messaging;

/// <summary>
/// Thread-safe in-memory message capture utility for testing message publishing.
/// Captures messages of any type and provides methods to retrieve and inspect them.
/// </summary>
public class InMemoryMessageCapture : IDisposable
{
    private readonly ConcurrentBag<object> _messages = new();
    private bool _disposed;

    /// <summary>
    /// Captures a message for later inspection.
    /// </summary>
    /// <typeparam name="T">The type of message to capture</typeparam>
    /// <param name="message">The message to capture</param>
    public void Capture<T>(T message) where T : notnull
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _messages.Add(message);
    }

    /// <summary>
    /// Retrieves all captured messages of a specific type.
    /// </summary>
    /// <typeparam name="T">The type of messages to retrieve</typeparam>
    /// <returns>Read-only list of all messages of the specified type</returns>
    public IReadOnlyList<T> GetMessages<T>()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _messages.OfType<T>().ToList();
    }

    /// <summary>
    /// Retrieves the last captured message of a specific type.
    /// </summary>
    /// <typeparam name="T">The type of message to retrieve</typeparam>
    /// <returns>The last message of the specified type, or null if no messages exist</returns>
    public T? GetLastMessage<T>() where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _messages.OfType<T>().LastOrDefault();
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

        var messages = _messages.OfType<T>();
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
    public async Task<T> WaitForMessageAsync<T>(TimeSpan? timeout = null) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var actualTimeout = timeout ?? TimeSpan.FromSeconds(5);
        var cancellationTokenSource = new CancellationTokenSource(actualTimeout);
        var token = cancellationTokenSource.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var message = GetLastMessage<T>();
                if (message != null)
                {
                    return message;
                }

                await Task.Delay(50, token);
            }

            throw new TimeoutException($"No message of type {typeof(T).Name} was captured within {actualTimeout.TotalSeconds} seconds.");
        }
        finally
        {
            cancellationTokenSource.Dispose();
        }
    }

    /// <summary>
    /// Clears all captured messages.
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _messages.Clear();
    }

    /// <summary>
    /// Disposes of the message capture instance.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _messages.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
