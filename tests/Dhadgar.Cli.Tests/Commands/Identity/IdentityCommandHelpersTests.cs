using Dhadgar.Cli.Commands.Identity;
using Dhadgar.Cli.Configuration;
using FluentAssertions;
using Xunit;

namespace Dhadgar.Cli.Tests.Commands.Identity;

public class IdentityCommandHelpersTests
{
    [Fact]
    public void TryEnsureAuthenticated_WithAuthenticatedConfig_ReturnsTrue()
    {
        var config = new CliConfig
        {
            AccessToken = "valid-token",
            TokenExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        var result = IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode);

        result.Should().BeTrue();
        exitCode.Should().Be(0);
    }

    [Fact]
    public void TryEnsureAuthenticated_WithUnauthenticatedConfig_ReturnsFalse()
    {
        var config = new CliConfig
        {
            AccessToken = null,
            TokenExpiresAt = null
        };

        // Capture console output to prevent pollution
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var result = IdentityCommandHelpers.TryEnsureAuthenticated(config, out var exitCode);

            result.Should().BeFalse();
            exitCode.Should().Be(1);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void WriteError_ReturnsExitCode1()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var result = IdentityCommandHelpers.WriteError("test_error", "Test message");

            result.Should().Be(1);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void WriteError_OutputsJsonWithErrorAndMessage()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            IdentityCommandHelpers.WriteError("test_error", "Test message");

            var output = sw.ToString();
            output.Should().Contain("\"error\"");
            output.Should().Contain("test_error");
            output.Should().Contain("\"message\"");
            output.Should().Contain("Test message");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void WriteJson_OutputsFormattedJson()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var testObject = new { name = "test", value = 123 };
            IdentityCommandHelpers.WriteJson(testObject);

            var output = sw.ToString();
            output.Should().Contain("\"name\"");
            output.Should().Contain("\"test\"");
            output.Should().Contain("\"value\"");
            output.Should().Contain("123");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Deserialize_ValidJson_ReturnsObject()
    {
        var json = """{"Name":"test","Value":123}""";

        var result = IdentityCommandHelpers.Deserialize<TestDto>(json);

        result.Should().NotBeNull();
        result!.Name.Should().Be("test");
        result.Value.Should().Be(123);
    }

    [Fact]
    public void Deserialize_EmptyString_ReturnsDefault()
    {
        var result = IdentityCommandHelpers.Deserialize<TestDto>("");

        result.Should().BeNull();
    }

    [Fact]
    public void Deserialize_Whitespace_ReturnsDefault()
    {
        var result = IdentityCommandHelpers.Deserialize<TestDto>("   ");

        result.Should().BeNull();
    }

    [Fact]
    public void Deserialize_CaseInsensitive_ParsesCorrectly()
    {
        var json = """{"NAME":"test","VALUE":123}""";

        var result = IdentityCommandHelpers.Deserialize<TestDto>(json);

        result.Should().NotBeNull();
        result!.Name.Should().Be("test");
        result.Value.Should().Be(123);
    }

    // Using a public class avoids CA1812 warning
    public sealed class TestDto
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }
}
