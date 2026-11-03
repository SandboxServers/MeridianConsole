using System.Text.Json;
using Dhadgar.Identity.Services;
using OpenIddict.Abstractions;
using Xunit;

namespace Dhadgar.Identity.Tests.Services;

public sealed class ClientOrganizationBindingTests
{
    [Fact]
    public void Apply_ThenRead_RoundTripsOrganizationId()
    {
        var organizationId = Guid.NewGuid();
        var descriptor = new OpenIddictApplicationDescriptor();

        ClientOrganizationBinding.Apply(descriptor, organizationId);
        var result = ClientOrganizationBinding.Read(descriptor.Properties);

        Assert.True(result.IsSuccess);
        Assert.Equal(organizationId, result.Value);
    }

    [Fact]
    public void Read_Absent_ReturnsClientUnbound()
    {
        var properties = new Dictionary<string, JsonElement>();

        var result = ClientOrganizationBinding.Read(properties);

        Assert.True(result.IsFailure);
        Assert.Equal("[Identity.ClientUnbound]", result.Error);
    }

    [Theory]
    [InlineData("\"not-a-guid\"")]
    [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("\"\"")]
    [InlineData("42")]
    [InlineData("{\"organization\":\"x\"}")]
    public void Read_InvalidValue_ReturnsClientBindingInvalid(string json)
    {
        var properties = new Dictionary<string, JsonElement>
        {
            [ClientOrganizationBinding.PropertyName] = JsonSerializer.Deserialize<JsonElement>(json),
        };

        var result = ClientOrganizationBinding.Read(properties);

        Assert.True(result.IsFailure);
        Assert.Equal("[Identity.ClientBindingInvalid]", result.Error);
    }

    [Fact]
    public void Apply_NullOrganization_RemovesBinding()
    {
        var descriptor = new OpenIddictApplicationDescriptor();
        ClientOrganizationBinding.Apply(descriptor, Guid.NewGuid());

        ClientOrganizationBinding.Apply(descriptor, null);

        Assert.False(descriptor.Properties.ContainsKey(ClientOrganizationBinding.PropertyName));
        Assert.Equal("[Identity.ClientUnbound]", ClientOrganizationBinding.Read(descriptor.Properties).Error);
    }
}
