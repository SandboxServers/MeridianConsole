using System.Collections.Generic;
using System.IO;
using System.Text;
using Dhadgar.Agent.Windows.Installation;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Installation;

/// <summary>
/// Tests for <see cref="RegistryEndpointConfiguration"/>: the installer's HKLM endpoint sits between the JSON files
/// and the environment variables and command line (D-S230).
/// </summary>
public sealed class RegistryEndpointConfigurationTests
{
    private const string JsonEndpoint = "https://from-json";
    private const string RegistryEndpoint = "https://from-registry";
    private const string EndpointKey = RegistryEndpointConfiguration.ConfigurationKey;

    private static string JsonWithEndpoint(string endpoint) =>
        "{\"Agent\":{\"ControlPlane\":{\"Endpoint\":\"" + endpoint + "\"}}}";

    [Fact]
    public void Apply_ValuePresent_OverridesAppSettings()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonWithEndpoint(JsonEndpoint)));
        var builder = new ConfigurationBuilder();
        builder.AddJsonStream(stream);
        builder.AddInMemoryCollection(new Dictionary<string, string?>());

        RegistryEndpointConfiguration.Apply(builder, () => RegistryEndpoint);

        IConfigurationRoot root = builder.Build();
        Assert.Equal(RegistryEndpoint, root[EndpointKey]);
    }

    [Fact]
    public void Apply_EnvironmentVariableSet_EnvironmentWins()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonWithEndpoint(JsonEndpoint)));
        var builder = new ConfigurationBuilder();
        builder.AddJsonStream(stream);
        builder.AddInMemoryCollection(new Dictionary<string, string?> { [EndpointKey] = "https://from-env" });

        RegistryEndpointConfiguration.Apply(builder, () => RegistryEndpoint);

        IConfigurationRoot root = builder.Build();
        Assert.Equal("https://from-env", root[EndpointKey]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Apply_ValueMissingOrBlank_AddsNoSource(string? value)
    {
        var builder = new ConfigurationBuilder();
        builder.AddInMemoryCollection(new Dictionary<string, string?>());
        var before = builder.Sources.Count;

        RegistryEndpointConfiguration.Apply(builder, () => value);

        Assert.Equal(before, builder.Sources.Count);
    }

    [Fact]
    public void Apply_NoJsonSource_InsertsFirst()
    {
        var builder = new ConfigurationBuilder();
        builder.AddInMemoryCollection(new Dictionary<string, string?> { [EndpointKey] = "https://from-env" });

        RegistryEndpointConfiguration.Apply(builder, () => RegistryEndpoint);

        // With no JSON source the registry source goes first, so the later environment source still wins
        Assert.Equal(2, builder.Sources.Count);
        IConfigurationRoot root = builder.Build();
        Assert.Equal("https://from-env", root[EndpointKey]);
    }
}
