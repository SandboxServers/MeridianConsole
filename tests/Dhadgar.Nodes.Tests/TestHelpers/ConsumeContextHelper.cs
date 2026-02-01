using MassTransit;
using NSubstitute;

namespace Dhadgar.Nodes.Tests.TestHelpers;

/// <summary>
/// Provides helper methods for creating MassTransit ConsumeContext mocks in tests.
/// </summary>
public static class ConsumeContextHelper
{
    /// <summary>
    /// Creates a mocked ConsumeContext for the specified message type.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message to wrap in the context.</param>
    /// <returns>A mocked ConsumeContext with the message configured.</returns>
    public static ConsumeContext<T> CreateConsumeContext<T>(T message) where T : class
    {
        var context = Substitute.For<ConsumeContext<T>>();
        context.Message.Returns(message);
        return context;
    }
}
