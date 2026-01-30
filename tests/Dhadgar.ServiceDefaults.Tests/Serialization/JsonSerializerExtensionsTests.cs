using System.Text.Json;
using Dhadgar.ServiceDefaults.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests.Serialization;

/// <summary>
/// Tests for JSON serialization configuration in ServiceDefaults.
/// </summary>
public class JsonSerializerExtensionsTests
{
    [Fact]
    public void AddStrictJsonSerialization_ConfiguresDuplicatePropertyHandling()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddStrictJsonSerialization();
        using var serviceProvider = services.BuildServiceProvider();
        var jsonOptions = serviceProvider.GetRequiredService<IOptions<JsonOptions>>().Value;

        // Assert
        Assert.False(jsonOptions.SerializerOptions.AllowDuplicateProperties);
    }

    [Fact]
    public void AddStrictJsonSerialization_ConfiguresCamelCaseNaming()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddStrictJsonSerialization();
        using var serviceProvider = services.BuildServiceProvider();
        var jsonOptions = serviceProvider.GetRequiredService<IOptions<JsonOptions>>().Value;

        // Assert
        Assert.Equal(JsonNamingPolicy.CamelCase, jsonOptions.SerializerOptions.PropertyNamingPolicy);
    }

    [Fact]
    public void AddStrictJsonSerialization_RejectsPayloadWithDuplicateProperties()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddStrictJsonSerialization();
        using var serviceProvider = services.BuildServiceProvider();
        var jsonOptions = serviceProvider.GetRequiredService<IOptions<JsonOptions>>().Value;

        var jsonWithDuplicates = """
        {
            "isAdmin": false,
            "isAdmin": true
        }
        """;

        // Act & Assert
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TestDto>(jsonWithDuplicates, jsonOptions.SerializerOptions));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddStrictJsonSerialization_AllowsExtraFields()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddStrictJsonSerialization();
        using var serviceProvider = services.BuildServiceProvider();
        var jsonOptions = serviceProvider.GetRequiredService<IOptions<JsonOptions>>().Value;

        var jsonWithExtraFields = """
        {
            "isAdmin": true,
            "extraField": "should be ignored"
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<TestDto>(jsonWithExtraFields, jsonOptions.SerializerOptions);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsAdmin);
    }

    [Fact]
    public void AddStrictJsonSerialization_UsesCamelCaseForSerialization()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddStrictJsonSerialization();
        using var serviceProvider = services.BuildServiceProvider();
        var jsonOptions = serviceProvider.GetRequiredService<IOptions<JsonOptions>>().Value;

        var dto = new TestDto { IsAdmin = true };

        // Act
        var json = JsonSerializer.Serialize(dto, jsonOptions.SerializerOptions);

        // Assert
        Assert.Contains("\"isAdmin\"", json, StringComparison.Ordinal); // camelCase
        Assert.DoesNotContain("\"IsAdmin\"", json, StringComparison.Ordinal); // PascalCase should not appear
    }

    private sealed class TestDto
    {
        public bool IsAdmin { get; set; }
    }
}
