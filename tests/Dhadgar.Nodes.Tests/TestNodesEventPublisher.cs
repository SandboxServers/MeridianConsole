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
        _publishedMessages.Enqueue(values);
        return Task.CompletedTask;
    }

    public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
    {
        _publishedMessages.Enqueue(values);
        return Task.CompletedTask;
    }

    public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
    {
        _publishedMessages.Enqueue(values);
        return Task.CompletedTask;
    }

    public ConnectHandle ConnectPublishObserver(IPublishObserver observer)
    {
        throw new NotImplementedException();
    }
}
