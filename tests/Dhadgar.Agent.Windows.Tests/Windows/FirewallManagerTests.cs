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
///
/// The FirewallManager uses Result&lt;T&gt; for railway-oriented error handling.
/// On non-Windows systems, methods return failures instead of throwing exceptions.
/// </remarks>
public sealed class FirewallManagerTests : IDisposable
{
    private readonly ILogger<FirewallManager> _logger;
    private readonly FirewallManager _manager;

    public FirewallManagerTests()
    {
        _logger = Substitute.For<ILogger<FirewallManager>>();
        _manager = new FirewallManager(_logger);
    }

    public void Dispose()
    {
        _manager.Dispose();
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
        using var manager = new FirewallManager(_logger);

        // Assert
        Assert.NotNull(manager);
    }

    #endregion

    #region Port Validation Tests (via AllowInboundPort)

    [Fact]
    public void AllowInboundPort_Port0_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(0, "ValidRuleName", "TCP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Port must be between 1 and 65535", result.Error);
    }

    [Fact]
    public void AllowInboundPort_Port65536_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(65536, "ValidRuleName", "TCP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Port must be between 1 and 65535", result.Error);
    }

    [Fact]
    public void AllowInboundPort_Port1_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(1, "ValidRuleName", "TCP");

        // Assert - On non-Windows, expect failure due to API unavailability, not validation failure
        // On Windows, expect success (unless running without admin privileges)
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Port must be between", result.Error);
        }
    }

    [Fact]
    public void AllowInboundPort_Port65535_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(65535, "ValidRuleName", "TCP");

        // Assert - Validation should pass
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Port must be between", result.Error);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(65537)]
    [InlineData(100000)]
    public void AllowInboundPort_InvalidPorts_ReturnsFailure(int port)
    {
        // Act
        var result = _manager.AllowInboundPort(port, "ValidRuleName", "TCP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Port must be between 1 and 65535", result.Error);
    }

    #endregion

    #region Protocol Validation Tests (via AllowInboundPort)

    [Fact]
    public void AllowInboundPort_ProtocolTCP_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "TCP");

        // Assert - TCP should be valid
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Protocol must be one of", result.Error);
        }
    }

    [Fact]
    public void AllowInboundPort_ProtocolUDP_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "UDP");

        // Assert - UDP should be valid
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Protocol must be one of", result.Error);
        }
    }

    [Fact]
    public void AllowInboundPort_ProtocolICMP_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "ICMP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Protocol must be one of: TCP, UDP", result.Error);
    }

    [Fact]
    public void AllowInboundPort_ProtocolNull_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Protocol cannot be null or empty", result.Error);
    }

    [Fact]
    public void AllowInboundPort_ProtocolEmpty_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Protocol cannot be null or empty", result.Error);
    }

    [Fact]
    public void AllowInboundPort_ProtocolWhitespace_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "   ");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Protocol cannot be null or empty", result.Error);
    }

    [Theory]
    [InlineData("HTTP")]
    [InlineData("HTTPS")]
    [InlineData("SSH")]
    [InlineData("FTP")]
    [InlineData("ANY")]
    public void AllowInboundPort_UnsupportedProtocols_ReturnsFailure(string protocol)
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", protocol);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Protocol must be one of: TCP, UDP", result.Error);
    }

    #endregion

    #region Rule Name Validation Tests (via AllowInboundPort, RemoveRule, RuleExists)

    [Fact]
    public void AllowInboundPort_RuleNameNull_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, null!, "TCP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name cannot be null or empty", result.Error);
    }

    [Fact]
    public void AllowInboundPort_RuleNameEmpty_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "", "TCP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name cannot be null or empty", result.Error);
    }

    [Fact]
    public void AllowInboundPort_RuleNameWhitespace_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "   ", "TCP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name cannot be null or empty", result.Error);
    }

    [Fact]
    public void AllowInboundPort_RuleNameExceedsMaxLength_ReturnsFailure()
    {
        // Arrange
        var longRuleName = new string('a', 257); // MaxRuleNameLength is 256

        // Act
        var result = _manager.AllowInboundPort(8080, longRuleName, "TCP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name must not exceed 256 characters", result.Error);
    }

    [Theory]
    [InlineData("Invalid;Rule")]
    [InlineData("Invalid|Rule")]
    [InlineData("Invalid&Rule")]
    [InlineData("Invalid`Rule")]
    [InlineData("Invalid$Rule")]
    [InlineData("Invalid\"Rule")]
    [InlineData("Invalid'Rule")]
    [InlineData("Invalid<Rule>")]
    [InlineData("Invalid(Rule)")]
    [InlineData("Invalid{Rule}")]
    [InlineData("Invalid!Rule")]
    [InlineData("Invalid@Rule")]
    [InlineData("Invalid#Rule")]
    [InlineData("Invalid%Rule")]
    [InlineData("Invalid^Rule")]
    [InlineData("Invalid*Rule")]
    public void AllowInboundPort_RuleNameContainsSpecialCharacters_ReturnsFailure(string ruleName)
    {
        // Act
        var result = _manager.AllowInboundPort(8080, ruleName, "TCP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name contains invalid characters", result.Error);
    }

    [Fact]
    public void AllowInboundPort_RuleNameValidAlphanumeric_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRule123", "TCP");

        // Assert - Validation should pass
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Rule name", result.Error);
        }
    }

    [Fact]
    public void AllowInboundPort_RuleNameWithSpaces_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "Valid Rule Name", "TCP");

        // Assert - Validation should pass
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Rule name", result.Error);
        }
    }

    [Fact]
    public void AllowInboundPort_RuleNameWithHyphens_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "Valid-Rule-Name", "TCP");

        // Assert - Validation should pass
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Rule name", result.Error);
        }
    }

    [Fact]
    public void AllowInboundPort_RuleNameWithUnderscores_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "Valid_Rule_Name", "TCP");

        // Assert - Validation should pass
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Rule name", result.Error);
        }
    }

    [Fact]
    public void AllowInboundPort_RuleNameAtMaxLength_PassesValidation()
    {
        // Arrange
        var maxLengthRuleName = new string('a', 256); // MaxRuleNameLength is 256

        // Act
        var result = _manager.AllowInboundPort(8080, maxLengthRuleName, "TCP");

        // Assert - Validation should pass
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Rule name must not exceed", result.Error);
        }
    }

    [Fact]
    public void RemoveRule_RuleNameNull_ReturnsFailure()
    {
        // Act
        var result = _manager.RemoveRule(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name cannot be null or empty", result.Error);
    }

    [Fact]
    public void RemoveRule_RuleNameEmpty_ReturnsFailure()
    {
        // Act
        var result = _manager.RemoveRule("");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name cannot be null or empty", result.Error);
    }

    [Fact]
    public void RemoveRule_RuleNameContainsSpecialCharacters_ReturnsFailure()
    {
        // Act
        var result = _manager.RemoveRule("Invalid;Rule");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name contains invalid characters", result.Error);
    }

    [Fact]
    public void RemoveRule_ValidRuleName_PassesValidation()
    {
        // Act
        var result = _manager.RemoveRule("Valid-Rule_Name 123");

        // Assert - Validation should pass
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Rule name", result.Error);
        }
    }

    [Fact]
    public void RuleExists_RuleNameNull_ReturnsFailure()
    {
        // Act
        var result = _manager.RuleExists(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name cannot be null or empty", result.Error);
    }

    [Fact]
    public void RuleExists_RuleNameEmpty_ReturnsFailure()
    {
        // Act
        var result = _manager.RuleExists("");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name cannot be null or empty", result.Error);
    }

    [Fact]
    public void RuleExists_RuleNameContainsSpecialCharacters_ReturnsFailure()
    {
        // Act
        var result = _manager.RuleExists("Invalid|Rule");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name contains invalid characters", result.Error);
    }

    [Fact]
    public void RuleExists_ValidRuleName_PassesValidation()
    {
        // Act
        var result = _manager.RuleExists("Valid-Rule_Name 123");

        // Assert - Validation should pass
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Rule name", result.Error);
        }
    }

    #endregion

    #region Protocol Case Sensitivity Tests

    [Fact]
    public void AllowInboundPort_ProtocolTcpLowercase_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "tcp");

        // Assert - Protocol validation is case-insensitive
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Protocol must be one of", result.Error);
        }
    }

    [Fact]
    public void AllowInboundPort_ProtocolUdpLowercase_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "udp");

        // Assert - Protocol validation is case-insensitive
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Protocol must be one of", result.Error);
        }
    }

    [Fact]
    public void AllowInboundPort_ProtocolMixedCase_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "Tcp");

        // Assert - Protocol validation is case-insensitive
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Protocol must be one of", result.Error);
        }
    }

    [Theory]
    [InlineData("tcp")]
    [InlineData("TCP")]
    [InlineData("Tcp")]
    [InlineData("tCp")]
    [InlineData("udp")]
    [InlineData("UDP")]
    [InlineData("Udp")]
    [InlineData("uDp")]
    public void AllowInboundPort_ProtocolCaseVariations_PassesValidation(string protocol)
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", protocol);

        // Assert - Protocol validation is case-insensitive
        if (result.IsFailure)
        {
            Assert.DoesNotContain("Protocol must be one of", result.Error);
        }
    }

    #endregion

    #region Result Type Tests

    [Fact]
    public void AllowInboundPort_InvalidInput_ReturnsResultWithIsFailureTrue()
    {
        // Act
        var result = _manager.AllowInboundPort(-1, "ValidRule", "TCP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void RemoveRule_InvalidInput_ReturnsResultWithIsFailureTrue()
    {
        // Act
        var result = _manager.RemoveRule("");

        // Assert
        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void RuleExists_InvalidInput_ReturnsResultWithIsFailureTrue()
    {
        // Act
        var result = _manager.RuleExists("Invalid|Rule");

        // Assert
        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        using var manager = new FirewallManager(_logger);

        // Act & Assert - Multiple dispose calls should not throw
        manager.Dispose();
        manager.Dispose();
        manager.Dispose();
    }

    [Fact]
    public void Dispose_ImplementsIDisposable()
    {
        // Assert
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(FirewallManager)));
    }

    #endregion

    #region Non-Windows Behavior Tests

    [Fact]
    public void AllowInboundPort_OnNonWindows_ReturnsAppropriateError()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRule", "TCP");

        // Assert - On non-Windows systems, should indicate API unavailability
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(result.IsFailure);
            Assert.Contains("not available", result.Error);
        }
    }

    [Fact]
    public void RemoveRule_OnNonWindows_ReturnsAppropriateError()
    {
        // Act
        var result = _manager.RemoveRule("ValidRule");

        // Assert - On non-Windows systems, should indicate API unavailability
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(result.IsFailure);
            Assert.Contains("not available", result.Error);
        }
    }

    [Fact]
    public void RuleExists_OnNonWindows_ReturnsAppropriateError()
    {
        // Act
        var result = _manager.RuleExists("ValidRule");

        // Assert - On non-Windows systems, should indicate API unavailability
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(result.IsFailure);
            Assert.Contains("not available", result.Error);
        }
    }

    #endregion
}
