using Dhadgar.Secrets.Tests.Security;
using Xunit;

namespace Dhadgar.Secrets.Tests;

/// <summary>
/// Collection definition for Secrets integration tests.
/// Shares a single WebApplicationFactory instance across all tests in the collection.
/// </summary>
[CollectionDefinition("Secrets Integration")]
public class SecretsTestCollectionDefinition : ICollectionFixture<SecretsWebApplicationFactory>
{
}

/// <summary>
/// Collection definition for Secure Secrets integration tests.
/// Shares a single SecureSecretsWebApplicationFactory instance across all tests in the collection.
/// </summary>
[CollectionDefinition("Secure Secrets Integration")]
public class SecureSecretsTestCollectionDefinition : ICollectionFixture<SecureSecretsWebApplicationFactory>
{
}
