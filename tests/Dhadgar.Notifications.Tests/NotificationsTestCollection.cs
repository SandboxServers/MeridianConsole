using Xunit;

namespace Dhadgar.Notifications.Tests;

[CollectionDefinition("Notifications Integration")]
public class NotificationsTestCollectionDefinition : ICollectionFixture<NotificationsWebApplicationFactory>
{
}
