using Dhadgar.Secrets.Validation;
using Xunit;

namespace Dhadgar.Secrets.Tests.Validation;

public class SecretNameValidatorTests
{
    #region Valid Names

    [Theory]
    [InlineData("a")]
    [InlineData("A")]
    [InlineData("0")]
    [InlineData("9")]
    public void Validate_SingleAlphanumericCharacter_Succeeds(string name)
    {
        var result = SecretNameValidator.Validate(name);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("secret")]
    [InlineData("my-secret")]
    [InlineData("oauth-steam-api-key")]
    [InlineData("UPPERCASE-NAME")]
    [InlineData("MixedCase-Name")]
    [InlineData("a1")]
    [InlineData("secret123")]
    [InlineData("123secret")]
    [InlineData("a-b")]
    [InlineData("a-b-c-d-e")]
    public void Validate_ValidNames_Succeeds(string name)
    {
        var result = SecretNameValidator.Validate(name);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Validate_MaxLengthName_Succeeds()
    {
        // 127 characters: a + 125 dashes/letters + z
        var name = "a" + new string('b', 125) + "z";
        Assert.Equal(127, name.Length);

        var result = SecretNameValidator.Validate(name);

        Assert.True(result.IsValid);
    }

    #endregion

    #region Empty/Null Names

    [Fact]
    public void Validate_NullName_ReturnsFailure()
    {
        var result = SecretNameValidator.Validate(null);

        Assert.False(result.IsValid);
        Assert.Contains("required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_EmptyString_ReturnsFailure()
    {
        var result = SecretNameValidator.Validate("");

        Assert.False(result.IsValid);
        Assert.Contains("required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_WhitespaceOnly_ReturnsFailure()
    {
        var result = SecretNameValidator.Validate("   ");

        Assert.False(result.IsValid);
        Assert.Contains("required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Length Limits

    [Fact]
    public void Validate_TooLongName_ReturnsFailure()
    {
        var name = new string('a', 128);

        var result = SecretNameValidator.Validate(name);

        Assert.False(result.IsValid);
        Assert.Contains("127", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WayTooLongName_ReturnsFailure()
    {
        var name = new string('a', 1000);

        var result = SecretNameValidator.Validate(name);

        Assert.False(result.IsValid);
    }

    #endregion

    #region Invalid Characters

    [Theory]
    [InlineData("secret_name")]      // underscore
    [InlineData("secret.name")]      // dot
    [InlineData("secret name")]      // space
    [InlineData("secret@name")]      // at sign
    [InlineData("secret#name")]      // hash
    [InlineData("secret$name")]      // dollar
    [InlineData("secret%name")]      // percent
    [InlineData("secret^name")]      // caret
    [InlineData("secret&name")]      // ampersand
    [InlineData("secret*name")]      // asterisk
    [InlineData("secret(name)")]     // parentheses
    [InlineData("secret[name]")]     // brackets
    [InlineData("secret{name}")]     // braces
    [InlineData("secret!name")]      // exclamation
    [InlineData("secret=name")]      // equals
    [InlineData("secret+name")]      // plus
    public void Validate_InvalidCharacters_ReturnsFailure(string name)
    {
        var result = SecretNameValidator.Validate(name);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("-secret")]          // starts with dash
    [InlineData("secret-")]          // ends with dash
    [InlineData("-")]                // just a dash
    [InlineData("--")]               // double dash only
    public void Validate_InvalidDashPlacement_ReturnsFailure(string name)
    {
        var result = SecretNameValidator.Validate(name);

        Assert.False(result.IsValid);
    }

    #endregion

    #region Injection Patterns

    [Theory]
    [InlineData("../secret")]        // path traversal
    [InlineData("secret/../other")]  // path traversal middle
    [InlineData("secret/value")]     // forward slash
    [InlineData("secret\\value")]    // backslash
    public void Validate_PathTraversal_ReturnsFailure(string name)
    {
        var result = SecretNameValidator.Validate(name);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NullByte_ReturnsFailure()
    {
        var name = "secret\0name";

        var result = SecretNameValidator.Validate(name);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("secret'")]          // SQL single quote
    [InlineData("secret;")]          // SQL semicolon
    [InlineData("'; DROP TABLE--")] // SQL injection attempt
    public void Validate_SqlInjectionPatterns_ReturnsFailure(string name)
    {
        var result = SecretNameValidator.Validate(name);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("<script>")]         // XSS open tag
    [InlineData("secret<value")]     // less than
    [InlineData("secret>value")]     // greater than
    public void Validate_XssPatterns_ReturnsFailure(string name)
    {
        var result = SecretNameValidator.Validate(name);

        Assert.False(result.IsValid);
    }

    #endregion

    #region Single Character Edge Cases

    [Theory]
    [InlineData("-")]
    [InlineData(".")]
    [InlineData("_")]
    [InlineData(" ")]
    public void Validate_SingleInvalidCharacter_ReturnsFailure(string name)
    {
        var result = SecretNameValidator.Validate(name);

        Assert.False(result.IsValid);
    }

    #endregion

    #region Unicode and Extended Characters

    [Theory]
    [InlineData("secreté")]          // accented character
    [InlineData("secret名")]          // CJK character
    [InlineData("secret🔑")]         // emoji
    public void Validate_NonAsciiCharacters_ReturnsFailure(string name)
    {
        var result = SecretNameValidator.Validate(name);

        Assert.False(result.IsValid);
    }

    #endregion

    #region ValidationResult Behavior

    [Fact]
    public void ValidationResult_Success_HasCorrectProperties()
    {
        var result = ValidationResult.Success();

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidationResult_Failure_HasCorrectProperties()
    {
        var result = ValidationResult.Failure("Test error message");

        Assert.False(result.IsValid);
        Assert.Equal("Test error message", result.ErrorMessage);
    }

    #endregion

    #region Real-World Secret Names

    [Theory]
    [InlineData("oauth-steam-api-key")]
    [InlineData("oauth-discord-client-secret")]
    [InlineData("betterauth-jwt-secret")]
    [InlineData("infra-db-password")]
    [InlineData("infra-redis-password")]
    [InlineData("connection-string-primary")]
    [InlineData("api-key-production")]
    [InlineData("signing-key-v2")]
    public void Validate_RealWorldSecretNames_Succeeds(string name)
    {
        var result = SecretNameValidator.Validate(name);

        Assert.True(result.IsValid);
    }

    #endregion
}
