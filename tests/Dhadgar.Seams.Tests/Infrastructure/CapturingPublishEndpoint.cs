using System.Collections.Concurrent;
using MassTransit;

namespace Dhadgar.Seams.Tests.Infrastructure;

/// <summary>
/// Test replacement for MassTransit's <see cref="IPublishEndpoint"/>. Records every published message so a
/// seam test can assert on events without a broker.
/// </summary>
public sealed class CapturingPublishEndpoint : IPublishEndpoint
{
    private readonly ConcurrentQueue<object> _messages = new();

    /// <summary>
    /// Waits until a message of type <typeparamref name="T"/> satisfying <paramref name="match"/> is published.
    /// </summary>
    /// <exception cref="TimeoutException">No matching message was published within <paramref name="timeout"/>.</exception>
    public async Task<T> WaitForAsync<T>(Func<T, bool> match, TimeSpan timeout) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var hit = _messages.OfType<T>().FirstOrDefault(match);
            if (hit is not null)
            {
                return hit;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"No {typeof(T).Name} matching the predicate was published within {timeout}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    private void Record(object message) => _messages.Enqueue(message);

    public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        Record(message);
        return Task.CompletedTask;
    }

    public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
    {
        Record(message);
        return Task.CompletedTask;
    }

    public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
    {
        Record(message);
        return Task.CompletedTask;
    }

    public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class
    {
        Record(values);
        return Task.CompletedTask;
    }

    public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
    {
        Record(values);
        return Task.CompletedTask;
    }

    public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
    {
        Record(values);
        return Task.CompletedTask;
    }

    public Task Publish(object message, CancellationToken cancellationToken = default)
    {
        Record(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
    {
        Record(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
    {
        Record(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
    {
        Record(message);
        return Task.CompletedTask;
    }

    public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => new NoOpConnectHandle();

    private sealed class NoOpConnectHandle : ConnectHandle
    {
        public void Disconnect()
        {
        }

        public void Dispose()
        {
        }
    }
}
