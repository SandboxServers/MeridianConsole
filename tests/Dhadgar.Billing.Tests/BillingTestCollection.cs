using Xunit;

namespace Dhadgar.Billing.Tests;

[CollectionDefinition("Billing Integration")]
public class BillingTestCollectionDefinition : ICollectionFixture<BillingWebApplicationFactory>
{
}
