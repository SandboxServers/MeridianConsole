using Xunit;

namespace Dhadgar.Files.Tests;

[CollectionDefinition("Files Integration")]
public class FilesTestCollectionDefinition : ICollectionFixture<FilesWebApplicationFactory>
{
}
