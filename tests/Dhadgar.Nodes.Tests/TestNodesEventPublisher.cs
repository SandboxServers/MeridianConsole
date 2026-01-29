using System.Collections.Concurrent;
using MassTransit;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// Test implementation of IPublishEndpoint that captures published messages for verification
/// </summary>
public sealed class TestNodesEventPublisher : IPublishEndpoint
{
    private readonly ConcurrentQueue<object> _publishedMessages = new();

    public IReadOnlyList<object> PublishedMessages => _publishedMessages.ToList();

    public IReadOnlyList<T> GetMessages<T>() where T : class
        => _publishedMessages.OfType<T>().ToList();

    public T? GetLastMessage<T>() where T : class
        => _publishedMessages.OfType<T>().LastOrDefault();

    public bool HasMessage<T>() where T : class
        => _publishedMessages.OfType<T>().Any();

    public bool HasMessage<T>(Func<T, bool> predicate) where T : class
        => _publishedMessages.OfType<T>().Any(predicate);

    public int MessageCount => _publishedMessages.Count;

    public void Clear() => _publishedMessages.Clear();

    public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        _publishedMessages.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
    {
        _publishedMessages.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
    {
        _publishedMessages.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, CancellationToken cancellationToken = default)
    {
        _publishedMessages.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
    {
        _publishedMessages.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
    {
        _publishedMessages.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
    {
        _publishedMessages.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class
    {
        // Validate that values can be cast to T for GetMessages<T>() to find it
        if (values is T typedMessage)
        {
            _publishedMessages.Enqueue(typedMessage);
        }
        else
        {
            throw new ArgumentException($"Cannot publish object of type {values.GetType().Name} as {typeof(T).Name}", nameof(values));
        }
        return Task.CompletedTask;
    }

    public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
    {
        if (values is T typedMessage)
        {
            _publishedMessages.Enqueue(typedMessage);
        }
        else
        {
            throw new ArgumentException($"Cannot publish object of type {values.GetType().Name} as {typeof(T).Name}", nameof(values));
        }
        return Task.CompletedTask;
    }

    public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
    {
        if (values is T typedMessage)
        {
            _publishedMessages.Enqueue(typedMessage);
        }
        else
        {
            throw new ArgumentException($"Cannot publish object of type {values.GetType().Name} as {typeof(T).Name}", nameof(values));
        }
        return Task.CompletedTask;
    }

    public ConnectHandle ConnectPublishObserver(IPublishObserver observer)
    {
        // Return a no-op connect handle for tests
        return new NoOpConnectHandle();
    }

    /// <summary>
    /// No-op implementation of ConnectHandle for test scenarios.
    /// </summary>
    private sealed class NoOpConnectHandle : ConnectHandle
    {
        public void Disconnect()
        {
            // No-op
        }

        public void Dispose()
        {
            // No-op
        }
    }
}
