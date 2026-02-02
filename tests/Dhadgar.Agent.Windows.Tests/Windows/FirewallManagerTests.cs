using Dhadgar.Agent.Windows.Windows;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Windows;

/// <summary>
/// Unit tests for <see cref="FirewallManager"/>.
/// </summary>
/// <remarks>
/// Tests validation logic through public method interfaces since validation methods are private.
/// Focuses on input validation and security-critical path validation.
/// </remarks>
public sealed class FirewallManagerTests
{
    private readonly ILogger<FirewallManager> _logger;

    public FirewallManagerTests()
    {
        _logger = Substitute.For<ILogger<FirewallManager>>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new FirewallManager(null!));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void Constructor_ValidLogger_CreatesInstance()
    {
        // Act
        var manager = new FirewallManager(_logger);

        // Assert
        Assert.NotNull(manager);
    }

    #endregion

    #region Port Validation Tests (via AllowInboundPort)

    [Fact]
    public void AllowInboundPort_Port0_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            manager.AllowInboundPort(0, "ValidRuleName", "TCP"));

        Assert.Equal("port", exception.ParamName);
        Assert.Contains("Port must be between 1 and 65535", exception.Message);
    }

    [Fact]
    public void AllowInboundPort_Port65536_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            manager.AllowInboundPort(65536, "ValidRuleName", "TCP"));

        Assert.Equal("port", exception.ParamName);
        Assert.Contains("Port must be between 1 and 65535", exception.Message);
    }

    [Fact]
    public void AllowInboundPort_Port1_DoesNotThrow()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert - We expect this to not throw during validation (may fail during execution on non-Windows)
        // The validation should pass, execution failure is acceptable for this test
        try
        {
            manager.AllowInboundPort(1, "ValidRuleName", "TCP");
        }
        catch (ArgumentOutOfRangeException)
        {
            Assert.Fail("Port 1 should be valid and not throw ArgumentOutOfRangeException");
        }
        catch (ArgumentException ex) when (ex.ParamName == "port")
        {
            Assert.Fail("Port 1 should be valid and not throw ArgumentException for port parameter");
        }
    }

    [Fact]
    public void AllowInboundPort_Port65535_DoesNotThrow()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert - Validation should pass
        try
        {
            manager.AllowInboundPort(65535, "ValidRuleName", "TCP");
        }
        catch (ArgumentOutOfRangeException)
        {
            Assert.Fail("Port 65535 should be valid and not throw ArgumentOutOfRangeException");
        }
        catch (ArgumentException ex) when (ex.ParamName == "port")
        {
            Assert.Fail("Port 65535 should be valid and not throw ArgumentException for port parameter");
        }
    }

    #endregion

    #region Protocol Validation Tests (via AllowInboundPort)

    [Fact]
    public void AllowInboundPort_ProtocolTCP_DoesNotThrow()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert - TCP should be valid
        try
        {
            manager.AllowInboundPort(8080, "ValidRuleName", "TCP");
        }
        catch (ArgumentException ex) when (ex.ParamName == "protocol")
        {
            Assert.Fail("Protocol 'TCP' should be valid and not throw ArgumentException");
        }
    }

    [Fact]
    public void AllowInboundPort_ProtocolUDP_DoesNotThrow()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert - UDP should be valid
        try
        {
            manager.AllowInboundPort(8080, "ValidRuleName", "UDP");
        }
        catch (ArgumentException ex) when (ex.ParamName == "protocol")
        {
            Assert.Fail("Protocol 'UDP' should be valid and not throw ArgumentException");
        }
    }

    [Fact]
    public void AllowInboundPort_ProtocolICMP_ThrowsArgumentException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.AllowInboundPort(8080, "ValidRuleName", "ICMP"));

        Assert.Equal("protocol", exception.ParamName);
        Assert.Contains("Protocol must be one of: TCP, UDP", exception.Message);
    }

    [Fact]
    public void AllowInboundPort_ProtocolNull_ThrowsArgumentNullException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            manager.AllowInboundPort(8080, "ValidRuleName", null!));

        Assert.Equal("protocol", exception.ParamName);
    }

    [Fact]
    public void AllowInboundPort_ProtocolEmpty_ThrowsArgumentException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.AllowInboundPort(8080, "ValidRuleName", ""));

        Assert.Equal("protocol", exception.ParamName);
    }

    [Fact]
    public void AllowInboundPort_ProtocolWhitespace_ThrowsArgumentException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.AllowInboundPort(8080, "ValidRuleName", "   "));

        Assert.Equal("protocol", exception.ParamName);
    }

    #endregion

    #region Rule Name Validation Tests (via AllowInboundPort, RemoveRule, RuleExists)

    [Fact]
    public void AllowInboundPort_RuleNameNull_ThrowsArgumentNullException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            manager.AllowInboundPort(8080, null!, "TCP"));

        Assert.Equal("ruleName", exception.ParamName);
    }

    [Fact]
    public void AllowInboundPort_RuleNameEmpty_ThrowsArgumentException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.AllowInboundPort(8080, "", "TCP"));

        Assert.Equal("ruleName", exception.ParamName);
    }

    [Fact]
    public void AllowInboundPort_RuleNameWhitespace_ThrowsArgumentException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.AllowInboundPort(8080, "   ", "TCP"));

        Assert.Equal("ruleName", exception.ParamName);
    }

    [Fact]
    public void AllowInboundPort_RuleNameExceedsMaxLength_ThrowsArgumentException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);
        var longRuleName = new string('a', 257); // MaxRuleNameLength is 256

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.AllowInboundPort(8080, longRuleName, "TCP"));

        Assert.Equal("ruleName", exception.ParamName);
        Assert.Contains("Rule name must not exceed 256 characters", exception.Message);
    }

    [Fact]
    public void AllowInboundPort_RuleNameContainsSemicolon_ThrowsArgumentException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.AllowInboundPort(8080, "Invalid;Rule", "TCP"));

        Assert.Equal("ruleName", exception.ParamName);
        Assert.Contains("Rule name contains invalid characters", exception.Message);
    }

    [Fact]
    public void AllowInboundPort_RuleNameContainsPipe_ThrowsArgumentException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.AllowInboundPort(8080, "Invalid|Rule", "TCP"));

        Assert.Equal("ruleName", exception.ParamName);
        Assert.Contains("Rule name contains invalid characters", exception.Message);
    }

    [Fact]
    public void AllowInboundPort_RuleNameContainsAmpersand_ThrowsArgumentException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.AllowInboundPort(8080, "Invalid&Rule", "TCP"));

        Assert.Equal("ruleName", exception.ParamName);
        Assert.Contains("Rule name contains invalid characters", exception.Message);
    }

    [Fact]
    public void AllowInboundPort_RuleNameContainsBacktick_ThrowsArgumentException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.AllowInboundPort(8080, "Invalid`Rule", "TCP"));

        Assert.Equal("ruleName", exception.ParamName);
        Assert.Contains("Rule name contains invalid characters", exception.Message);
    }

    [Fact]
    public void AllowInboundPort_RuleNameValidAlphanumeric_DoesNotThrow()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        try
        {
            manager.AllowInboundPort(8080, "ValidRule123", "TCP");
        }
        catch (ArgumentException ex) when (ex.ParamName == "ruleName")
        {
            Assert.Fail("Valid alphanumeric rule name should not throw ArgumentException");
        }
    }

    [Fact]
    public void AllowInboundPort_RuleNameWithSpaces_DoesNotThrow()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        try
        {
            manager.AllowInboundPort(8080, "Valid Rule Name", "TCP");
        }
        catch (ArgumentException ex) when (ex.ParamName == "ruleName")
        {
            Assert.Fail("Rule name with spaces should not throw ArgumentException");
        }
    }

    [Fact]
    public void AllowInboundPort_RuleNameWithHyphens_DoesNotThrow()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        try
        {
            manager.AllowInboundPort(8080, "Valid-Rule-Name", "TCP");
        }
        catch (ArgumentException ex) when (ex.ParamName == "ruleName")
        {
            Assert.Fail("Rule name with hyphens should not throw ArgumentException");
        }
    }

    [Fact]
    public void AllowInboundPort_RuleNameWithUnderscores_DoesNotThrow()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        try
        {
            manager.AllowInboundPort(8080, "Valid_Rule_Name", "TCP");
        }
        catch (ArgumentException ex) when (ex.ParamName == "ruleName")
        {
            Assert.Fail("Rule name with underscores should not throw ArgumentException");
        }
    }

    [Fact]
    public void AllowInboundPort_RuleNameAtMaxLength_DoesNotThrow()
    {
        // Arrange
        var manager = new FirewallManager(_logger);
        var maxLengthRuleName = new string('a', 256); // MaxRuleNameLength is 256

        // Act & Assert
        try
        {
            manager.AllowInboundPort(8080, maxLengthRuleName, "TCP");
        }
        catch (ArgumentException ex) when (ex.ParamName == "ruleName")
        {
            Assert.Fail("Rule name at max length (256) should not throw ArgumentException");
        }
    }

    [Fact]
    public void RemoveRule_RuleNameNull_ThrowsArgumentNullException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            manager.RemoveRule(null!));

        Assert.Equal("ruleName", exception.ParamName);
    }

    [Fact]
    public void RemoveRule_RuleNameEmpty_ThrowsArgumentException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.RemoveRule(""));

        Assert.Equal("ruleName", exception.ParamName);
    }

    [Fact]
    public void RemoveRule_RuleNameContainsSpecialCharacters_ThrowsArgumentException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.RemoveRule("Invalid;Rule"));

        Assert.Equal("ruleName", exception.ParamName);
        Assert.Contains("Rule name contains invalid characters", exception.Message);
    }

    [Fact]
    public void RemoveRule_ValidRuleName_DoesNotThrow()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        try
        {
            manager.RemoveRule("Valid-Rule_Name 123");
        }
        catch (ArgumentException ex) when (ex.ParamName == "ruleName")
        {
            Assert.Fail("Valid rule name should not throw ArgumentException");
        }
    }

    [Fact]
    public void RuleExists_RuleNameNull_ThrowsArgumentNullException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            manager.RuleExists(null!));

        Assert.Equal("ruleName", exception.ParamName);
    }

    [Fact]
    public void RuleExists_RuleNameEmpty_ThrowsArgumentException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.RuleExists(""));

        Assert.Equal("ruleName", exception.ParamName);
    }

    [Fact]
    public void RuleExists_RuleNameContainsSpecialCharacters_ThrowsArgumentException()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.RuleExists("Invalid|Rule"));

        Assert.Equal("ruleName", exception.ParamName);
        Assert.Contains("Rule name contains invalid characters", exception.Message);
    }

    [Fact]
    public void RuleExists_ValidRuleName_DoesNotThrow()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert
        try
        {
            manager.RuleExists("Valid-Rule_Name 123");
        }
        catch (ArgumentException ex) when (ex.ParamName == "ruleName")
        {
            Assert.Fail("Valid rule name should not throw ArgumentException");
        }
    }

    #endregion

    #region Protocol Case Sensitivity Tests

    [Fact]
    public void AllowInboundPort_ProtocolTcpLowercase_DoesNotThrow()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert - Protocol validation is case-insensitive
        try
        {
            manager.AllowInboundPort(8080, "ValidRuleName", "tcp");
        }
        catch (ArgumentException ex) when (ex.ParamName == "protocol")
        {
            Assert.Fail("Protocol 'tcp' (lowercase) should be valid and not throw ArgumentException");
        }
    }

    [Fact]
    public void AllowInboundPort_ProtocolUdpLowercase_DoesNotThrow()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert - Protocol validation is case-insensitive
        try
        {
            manager.AllowInboundPort(8080, "ValidRuleName", "udp");
        }
        catch (ArgumentException ex) when (ex.ParamName == "protocol")
        {
            Assert.Fail("Protocol 'udp' (lowercase) should be valid and not throw ArgumentException");
        }
    }

    [Fact]
    public void AllowInboundPort_ProtocolMixedCase_DoesNotThrow()
    {
        // Arrange
        var manager = new FirewallManager(_logger);

        // Act & Assert - Protocol validation is case-insensitive
        try
        {
            manager.AllowInboundPort(8080, "ValidRuleName", "Tcp");
        }
        catch (ArgumentException ex) when (ex.ParamName == "protocol")
        {
            Assert.Fail("Protocol 'Tcp' (mixed case) should be valid and not throw ArgumentException");
        }
    }

    #endregion
}
