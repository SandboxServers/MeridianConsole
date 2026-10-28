using Xunit;

namespace Dhadgar.Tasks.Tests;

[CollectionDefinition("Tasks Integration")]
public class TasksTestCollectionDefinition : ICollectionFixture<TasksWebApplicationFactory>
{
}
