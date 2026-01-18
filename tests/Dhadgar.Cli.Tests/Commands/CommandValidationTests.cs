using Dhadgar.Cli.Commands;
using FluentAssertions;
using Xunit;

namespace Dhadgar.Cli.Tests.Commands;

public class CommandValidationTests
{
    [Theory]
    [InlineData("my-vault")]
    [InlineData("MyVault123")]
    [InlineData("abc")]
    [InlineData("123456789012345678901234")] // 24 chars - max
    public void ValidateVaultName_ValidName_ReturnsValid(string vaultName)
    {
        var (isValid, errorMessage) = CommandValidation.ValidateVaultName(vaultName);

        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateVaultName_NullOrWhitespace_ReturnsInvalid(string? vaultName)
    {
        var (isValid, errorMessage) = CommandValidation.ValidateVaultName(vaultName);

        isValid.Should().BeFalse();
        errorMessage.Should().NotBeNull();
    }

    [Theory]
    [InlineData("ab")] // Too short (min 3)
    [InlineData("1234567890123456789012345")] // Too long (max 24)
    public void ValidateVaultName_InvalidLength_ReturnsInvalid(string vaultName)
    {
        var (isValid, errorMessage) = CommandValidation.ValidateVaultName(vaultName);

        isValid.Should().BeFalse();
        errorMessage.Should().NotBeNull();
    }

    [Theory]
    [InlineData("my_vault")] // Underscore not allowed
    [InlineData("my.vault")] // Dot not allowed
    [InlineData("my vault")] // Space not allowed
    [InlineData("my@vault")] // Special chars not allowed
    [InlineData("my!vault")]
    public void ValidateVaultName_InvalidCharacters_ReturnsInvalid(string vaultName)
    {
        var (isValid, errorMessage) = CommandValidation.ValidateVaultName(vaultName);

        isValid.Should().BeFalse();
        errorMessage.Should().NotBeNull();
    }

    [Theory]
    [InlineData("vault-with-hyphens")]
    [InlineData("UPPERCASE")]
    [InlineData("lowercase")]
    [InlineData("MixedCase123")]
    [InlineData("123-numeric")]
    public void ValidateVaultName_AllowedCharacters_ReturnsValid(string vaultName)
    {
        var (isValid, errorMessage) = CommandValidation.ValidateVaultName(vaultName);

        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateVaultName_InvalidName_ReturnsHelpfulErrorMessage()
    {
        var (isValid, errorMessage) = CommandValidation.ValidateVaultName("x");

        isValid.Should().BeFalse();
        errorMessage.Should().Contain("Invalid vault name");
        errorMessage.Should().Contain("3-24 characters");
    }
}
