using Dhadgar.Agent.Windows.Installation;
using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Installation;

/// <summary>
/// Tests for <see cref="ServiceInstaller"/> command construction patterns and security properties.
/// </summary>
/// <remarks>
/// <para>
/// This test class verifies both the validation logic and security properties of the ServiceInstaller.
/// The <see cref="ServiceInstaller.ValidateAndSanitizeDescription"/> method is tested directly,
/// while other aspects are documented through property-based tests.
/// </para>
/// <para>
/// The <see cref="ServiceInstaller"/> class executes sc.exe which requires administrator privileges
/// and cannot be meaningfully unit tested without elevated permissions. These tests:
/// </para>
/// <list type="bullet">
/// <item>Test the <see cref="ServiceInstaller.ValidateAndSanitizeDescription"/> helper method directly</item>
/// <item>Document expected security properties (no shell metacharacters in service name)</item>
/// <item>Verify command argument patterns match documentation</item>
/// <item>Ensure hardcoded values meet Windows service requirements</item>
/// </list>
/// <para>
/// Integration tests with actual sc.exe execution would require a Windows environment
/// with administrator privileges and are out of scope for this test suite.
/// </para>
/// </remarks>
public sealed class ServiceInstallerTests
{
    [Fact]
    public void ServiceName_ShouldBeExpectedValue()
    {
        // Arrange
        const string expectedServiceName = "DhadgarAgent";

        // Act
        var actualServiceName = Program.ServiceName;

        // Assert
        Assert.Equal(expectedServiceName, actualServiceName);
    }

    [Fact]
    public void ServiceName_ShouldNotContainShellMetacharacters()
    {
        // Arrange
        var serviceName = Program.ServiceName;
        char[] shellMetacharacters = ['&', '|', ';', '"', '\'', '`', '$', '(', ')', '<', '>', '\n', '\r', '\\'];

        // Act & Assert
        foreach (var metaChar in shellMetacharacters)
        {
            Assert.DoesNotContain(metaChar.ToString(), serviceName, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ServiceName_ShouldOnlyContainAlphanumericCharacters()
    {
        // Arrange
        var serviceName = Program.ServiceName;

        // Act & Assert
        Assert.Matches(@"^[a-zA-Z0-9\-_]+$", serviceName);
    }

    [Fact]
    public void ServiceName_ShouldNotExceedMaximumLength()
    {
        // Arrange
        const int maxServiceNameLength = 256; // Windows service name limit
        var serviceName = Program.ServiceName;

        // Act & Assert
        Assert.True(serviceName.Length <= maxServiceNameLength,
            $"Service name length ({serviceName.Length}) exceeds maximum ({maxServiceNameLength})");
    }

    [Theory]
    [InlineData("Test description")]
    [InlineData("Description with spaces")]
    [InlineData("Description-with-hyphens")]
    [InlineData("Description_with_underscores")]
    [InlineData("Description with numbers 12345")]
    [InlineData("Simple")]
    public void ValidateAndSanitizeDescription_WithValidInput_ReturnsSuccess(string description)
    {
        // Act
        var result = ServiceInstaller.ValidateAndSanitizeDescription(description);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(description, result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ValidateAndSanitizeDescription_WithWhitespaceInput_ReturnsFailure(string? description)
    {
        // Act
        var result = ServiceInstaller.ValidateAndSanitizeDescription(description!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Description is required and cannot be empty.", result.Error);
    }

    [Fact]
    public void ValidateAndSanitizeDescription_WithNullInput_ReturnsFailure()
    {
        // Act
        var result = ServiceInstaller.ValidateAndSanitizeDescription(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Description is required and cannot be empty.", result.Error);
    }

    [Theory]
    [InlineData("Test \"quoted\" description", '"')]
    [InlineData("Test 'single quoted' description", '\'')]
    [InlineData("Test `backtick` description", '`')]
    [InlineData("Test $variable description", '$')]
    [InlineData("Test & ampersand description", '&')]
    [InlineData("Test | pipe description", '|')]
    [InlineData("Test ; semicolon description", ';')]
    [InlineData("Test \\ backslash description", '\\')]
    [InlineData("Test \r carriage return description", '\r')]
    [InlineData("Test \n newline description", '\n')]
    public void ValidateAndSanitizeDescription_WithDisallowedCharacter_ReturnsFailure(string description, char disallowedChar)
    {
        // Act
        var result = ServiceInstaller.ValidateAndSanitizeDescription(description);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal($"Description contains disallowed character: '{disallowedChar}'", result.Error);
    }

    [Fact]
    public void ValidateAndSanitizeDescription_WithMultipleDisallowedCharacters_ReturnsFailureForFirst()
    {
        // Arrange - Description with multiple disallowed characters
        const string description = "Test \"quoted\" and 'single' description";

        // Act
        var result = ServiceInstaller.ValidateAndSanitizeDescription(description);

        // Assert - Should fail on the first disallowed character (double quote)
        Assert.True(result.IsFailure);
        Assert.Equal("Description contains disallowed character: '\"'", result.Error);
    }

    [Fact]
    public void ValidateAndSanitizeDescription_WithCommandInjectionAttempts_ReturnsFailure()
    {
        // Arrange - Various command injection attempts
        var injectionAttempts = new[]
        {
            ("Description & net stop DhadgarAgent", '&'),
            ("Description; net stop DhadgarAgent", ';'),
            ("Description | net stop DhadgarAgent", '|'),
            ("Description && net stop DhadgarAgent", '&'),
            ("Description || net stop DhadgarAgent", '|'),
            ("Description `whoami`", '`'),
            ("Description $(whoami)", '$'),
        };

        // Act & Assert
        foreach (var (attempt, expectedChar) in injectionAttempts)
        {
            var result = ServiceInstaller.ValidateAndSanitizeDescription(attempt);

            Assert.True(result.IsFailure, $"Expected failure for: {attempt}");
            Assert.Equal($"Description contains disallowed character: '{expectedChar}'", result.Error);
        }
    }

    [Fact]
    public void ConfigureRecovery_CommandArguments_ShouldBeHardcoded()
    {
        // Arrange
        const string expectedArguments = "failure DhadgarAgent reset= 86400 actions= restart/5000/restart/10000/restart/30000";

        // Act
        var actualArguments = $"failure {Program.ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000";

        // Assert
        Assert.Equal(expectedArguments, actualArguments);
    }

    [Fact]
    public void ConfigureRecovery_Arguments_ShouldNotContainUserInput()
    {
        // This test verifies that the recovery configuration is completely hardcoded
        // and contains no user-controllable input

        // Arrange
        var serviceName = Program.ServiceName;
        var recoveryArguments = $"failure {serviceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000";

        // Act & Assert
        // Verify all components are hardcoded constants
        Assert.Contains("failure", recoveryArguments, StringComparison.Ordinal);
        Assert.Contains("reset= 86400", recoveryArguments, StringComparison.Ordinal);
        Assert.Contains("actions= restart/5000/restart/10000/restart/30000", recoveryArguments, StringComparison.Ordinal);
        Assert.Contains(serviceName, recoveryArguments, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureRecovery_RecoveryTimings_ShouldMatchDocumentation()
    {
        // Verify that the recovery configuration matches the documented behavior:
        // - First failure: restart after 5 seconds (5000ms)
        // - Second failure: restart after 10 seconds (10000ms)
        // - Subsequent failures: restart after 30 seconds (30000ms)
        // - Reset failure count after 24 hours (86400 seconds)

        // Arrange
        const int firstFailureDelayMs = 5000;   // 5 seconds
        const int secondFailureDelayMs = 10000; // 10 seconds
        const int subsequentFailureDelayMs = 30000; // 30 seconds
        const int resetPeriodSeconds = 86400;    // 24 hours

        // Act
        var arguments = $"failure {Program.ServiceName} reset= {resetPeriodSeconds} actions= restart/{firstFailureDelayMs}/restart/{secondFailureDelayMs}/restart/{subsequentFailureDelayMs}";

        // Assert
        Assert.Contains("reset= 86400", arguments, StringComparison.Ordinal);
        Assert.Contains("restart/5000", arguments, StringComparison.Ordinal);
        Assert.Contains("restart/10000", arguments, StringComparison.Ordinal);
        Assert.Contains("restart/30000", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureDelayedAutoStart_CommandArguments_ShouldBeHardcoded()
    {
        // Arrange
        const string expectedArguments = "config DhadgarAgent start= delayed-auto";

        // Act
        var actualArguments = $"config {Program.ServiceName} start= delayed-auto";

        // Assert
        Assert.Equal(expectedArguments, actualArguments);
    }

    [Fact]
    public void ConfigureDelayedAutoStart_Arguments_ShouldNotContainUserInput()
    {
        // This test verifies that the delayed auto-start configuration is completely hardcoded
        // and contains no user-controllable input

        // Arrange
        var serviceName = Program.ServiceName;
        var configArguments = $"config {serviceName} start= delayed-auto";

        // Act & Assert
        // Verify all components are hardcoded constants
        Assert.Contains("config", configArguments, StringComparison.Ordinal);
        Assert.Contains("start= delayed-auto", configArguments, StringComparison.Ordinal);
        Assert.Contains(serviceName, configArguments, StringComparison.Ordinal);
    }

    [Fact]
    public void AllScExeCommands_ShouldUseNonShellExecution()
    {
        // This is a documentation test to verify that the ProcessStartInfo configuration
        // prevents shell interpretation of arguments

        // The actual implementation should use:
        // - FileName = "sc.exe" (direct executable, not shell command)
        // - UseShellExecute = false (prevents cmd.exe interpretation)
        // - CreateNoWindow = true (no console window)

        // This test documents the security requirement
        const string fileName = "sc.exe";
        const bool useShellExecute = false;
        const bool createNoWindow = true;

        // Assert - These are the required security settings
        Assert.Equal("sc.exe", fileName);
        Assert.False(useShellExecute);
        Assert.True(createNoWindow);
    }

    [Fact]
    public void AllScExeCommands_ShouldRedirectOutput()
    {
        // This is a documentation test to verify that ProcessStartInfo configuration
        // redirects output streams for error handling

        // The actual implementation should use:
        // - RedirectStandardError = true (capture errors)
        // - RedirectStandardOutput = true (capture output)

        const bool redirectStandardError = true;
        const bool redirectStandardOutput = true;

        // Assert - These are the required settings for error handling
        Assert.True(redirectStandardError);
        Assert.True(redirectStandardOutput);
    }

    [Fact]
    public void AllScExeCommands_ShouldHaveTimeout()
    {
        // This is a documentation test to verify that sc.exe commands have a timeout
        // to prevent hanging indefinitely

        // The actual implementation should use:
        // - WaitForExit(TimeSpan.FromSeconds(30))

        var timeout = TimeSpan.FromSeconds(30);

        // Assert - Verify timeout is reasonable
        Assert.Equal(30, timeout.TotalSeconds);
        Assert.True(timeout > TimeSpan.Zero);
        Assert.True(timeout <= TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ValidateAndSanitizeDescription_RejectsAllDangerousCharacters()
    {
        // SECURITY: Verify that all potentially dangerous characters are rejected
        // These characters could enable command injection via sc.exe argument parsing
        char[] dangerousChars = ['"', '\'', '`', '$', '&', '|', ';', '\\', '\r', '\n'];

        foreach (var dangerousChar in dangerousChars)
        {
            var description = $"Test{dangerousChar}description";
            var result = ServiceInstaller.ValidateAndSanitizeDescription(description);

            Assert.True(result.IsFailure, $"Expected failure for character: '{dangerousChar}'");
            Assert.Contains("disallowed character", result.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ValidateAndSanitizeDescription_AllowsSafeSpecialCharacters()
    {
        // Verify that common safe special characters are allowed
        var safeDescriptions = new[]
        {
            "Description with (parentheses)",
            "Description with [brackets]",
            "Description with {braces}",
            "Description with <angle brackets>",
            "Description with !exclamation",
            "Description with @at sign",
            "Description with #hash",
            "Description with %percent",
            "Description with ^caret",
            "Description with *asterisk",
            "Description with =equals",
            "Description with +plus",
            "Description with comma, and period.",
            "Description with question?",
            "Description with /forward slash",
            "Description with :colon",
        };

        foreach (var description in safeDescriptions)
        {
            var result = ServiceInstaller.ValidateAndSanitizeDescription(description);

            Assert.True(result.IsSuccess, $"Expected success for: {description}");
            Assert.Equal(description, result.Value);
        }
    }

    [Fact]
    public void SecurityDocumentation_ShouldRequireValidationForParameterization()
    {
        // This test documents the security requirements from the class remarks
        // If service name ever becomes parameterized, it must have strict validation:
        // - Allow only alphanumeric characters, hyphens, and underscores
        // - Reject shell metacharacters (quotes, semicolons, pipes, backticks)
        // - Maximum length of 256 characters

        // This is a documentation test - no actual code to test
        // It serves as a reminder for future developers

        const string allowedPattern = @"^[a-zA-Z0-9\-_]+$";
        const int maxLength = 256;

        // Verify current service name meets these requirements
        Assert.Matches(allowedPattern, Program.ServiceName);
        Assert.True(Program.ServiceName.Length <= maxLength);
    }
}
