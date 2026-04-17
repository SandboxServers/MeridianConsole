using Dhadgar.Agent.Windows.Windows;
using Dhadgar.Shared.Results;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Windows;

/// <summary>
/// Unit tests for <see cref="FirewallManager"/>.
/// </summary>
/// <remarks>
/// Tests validation logic and command formation using a mock firewall operations implementation.
/// The mock prevents execution of real firewall commands while verifying correct argument formation.
///
/// The FirewallManager uses Result&lt;T&gt; for railway-oriented error handling.
/// </remarks>
public sealed class FirewallManagerTests : IDisposable
{
    private readonly ILogger<FirewallManager> _logger;
    private readonly MockFirewallOperations _mockOperations;
    private readonly FirewallManager _manager;

    public FirewallManagerTests()
    {
        _logger = Substitute.For<ILogger<FirewallManager>>();
        _mockOperations = new MockFirewallOperations();
        _manager = new FirewallManager(_logger, _mockOperations);
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

    [Fact]
    public void Constructor_WithMockOperations_CreatesInstance()
    {
        // Act
        using var manager = new FirewallManager(_logger, _mockOperations);

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
        Assert.Contains("Port must be between 1 and 65535", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.AddedRules);
    }

    [Fact]
    public void AllowInboundPort_Port65536_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(65536, "ValidRuleName", "TCP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Port must be between 1 and 65535", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.AddedRules);
    }

    [Fact]
    public void AllowInboundPort_Port1_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(1, "ValidRuleName", "TCP");

        // Assert - validation passes, mock is called
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);
        Assert.Equal(1, _mockOperations.AddedRules[0].Port);
    }

    [Fact]
    public void AllowInboundPort_Port65535_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(65535, "ValidRuleName", "TCP");

        // Assert - validation passes
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);
        Assert.Equal(65535, _mockOperations.AddedRules[0].Port);
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
        Assert.Contains("Port must be between 1 and 65535", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.AddedRules);
    }

    #endregion

    #region Protocol Validation Tests (via AllowInboundPort)

    [Fact]
    public void AllowInboundPort_ProtocolTCP_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "TCP");

        // Assert - TCP should be valid
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);
        Assert.Equal("TCP", _mockOperations.AddedRules[0].Protocol);
    }

    [Fact]
    public void AllowInboundPort_ProtocolUDP_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "UDP");

        // Assert - UDP should be valid
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);
        Assert.Equal("UDP", _mockOperations.AddedRules[0].Protocol);
    }

    [Fact]
    public void AllowInboundPort_ProtocolICMP_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "ICMP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Protocol must be one of: TCP, UDP", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.AddedRules);
    }

    [Fact]
    public void AllowInboundPort_ProtocolNull_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Protocol cannot be null or empty", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.AddedRules);
    }

    [Fact]
    public void AllowInboundPort_ProtocolEmpty_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Protocol cannot be null or empty", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.AddedRules);
    }

    [Fact]
    public void AllowInboundPort_ProtocolWhitespace_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "   ");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Protocol cannot be null or empty", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.AddedRules);
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
        Assert.Contains("Protocol must be one of: TCP, UDP", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.AddedRules);
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
        Assert.Contains("Rule name cannot be null or empty", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.AddedRules);
    }

    [Fact]
    public void AllowInboundPort_RuleNameEmpty_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "", "TCP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name cannot be null or empty", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.AddedRules);
    }

    [Fact]
    public void AllowInboundPort_RuleNameWhitespace_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "   ", "TCP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name cannot be null or empty", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.AddedRules);
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
        Assert.Contains("Rule name must not exceed 256 characters", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.AddedRules);
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
        Assert.Contains("Rule name contains invalid characters", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.AddedRules);
    }

    [Fact]
    public void AllowInboundPort_RuleNameValidAlphanumeric_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRule123", "TCP");

        // Assert - Validation should pass
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);
        Assert.Equal("ValidRule123", _mockOperations.AddedRules[0].RuleName);
    }

    [Fact]
    public void AllowInboundPort_RuleNameWithSpaces_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "Valid Rule Name", "TCP");

        // Assert - Validation should pass
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);
        Assert.Equal("Valid Rule Name", _mockOperations.AddedRules[0].RuleName);
    }

    [Fact]
    public void AllowInboundPort_RuleNameWithHyphens_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "Valid-Rule-Name", "TCP");

        // Assert - Validation should pass
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);
        Assert.Equal("Valid-Rule-Name", _mockOperations.AddedRules[0].RuleName);
    }

    [Fact]
    public void AllowInboundPort_RuleNameWithUnderscores_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "Valid_Rule_Name", "TCP");

        // Assert - Validation should pass
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);
        Assert.Equal("Valid_Rule_Name", _mockOperations.AddedRules[0].RuleName);
    }

    [Fact]
    public void AllowInboundPort_RuleNameAtMaxLength_PassesValidation()
    {
        // Arrange
        var maxLengthRuleName = new string('a', 256); // MaxRuleNameLength is 256

        // Act
        var result = _manager.AllowInboundPort(8080, maxLengthRuleName, "TCP");

        // Assert - Validation should pass
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);
        Assert.Equal(maxLengthRuleName, _mockOperations.AddedRules[0].RuleName);
    }

    [Fact]
    public void RemoveRule_RuleNameNull_ReturnsFailure()
    {
        // Act
        var result = _manager.RemoveRule(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name cannot be null or empty", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.RemovedRules);
    }

    [Fact]
    public void RemoveRule_RuleNameEmpty_ReturnsFailure()
    {
        // Act
        var result = _manager.RemoveRule("");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name cannot be null or empty", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.RemovedRules);
    }

    [Fact]
    public void RemoveRule_RuleNameContainsSpecialCharacters_ReturnsFailure()
    {
        // Act
        var result = _manager.RemoveRule("Invalid;Rule");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name contains invalid characters", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.RemovedRules);
    }

    [Fact]
    public void RemoveRule_ValidRuleName_PassesValidation()
    {
        // Act
        var result = _manager.RemoveRule("Valid-Rule_Name 123");

        // Assert - Validation should pass and mock is called
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.RemovedRules);
        Assert.Equal("Valid-Rule_Name 123", _mockOperations.RemovedRules[0]);
    }

    [Fact]
    public void RuleExists_RuleNameNull_ReturnsFailure()
    {
        // Act
        var result = _manager.RuleExists(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name cannot be null or empty", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.CheckedRules);
    }

    [Fact]
    public void RuleExists_RuleNameEmpty_ReturnsFailure()
    {
        // Act
        var result = _manager.RuleExists("");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name cannot be null or empty", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.CheckedRules);
    }

    [Fact]
    public void RuleExists_RuleNameContainsSpecialCharacters_ReturnsFailure()
    {
        // Act
        var result = _manager.RuleExists("Invalid|Rule");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Rule name contains invalid characters", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.CheckedRules);
    }

    [Fact]
    public void RuleExists_ValidRuleName_PassesValidation()
    {
        // Act
        var result = _manager.RuleExists("Valid-Rule_Name 123");

        // Assert - Validation should pass and mock is called
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.CheckedRules);
        Assert.Equal("Valid-Rule_Name 123", _mockOperations.CheckedRules[0]);
    }

    #endregion

    #region Protocol Case Sensitivity Tests

    [Fact]
    public void AllowInboundPort_ProtocolTcpLowercase_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "tcp");

        // Assert - Protocol validation is case-insensitive
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);
        Assert.Equal("tcp", _mockOperations.AddedRules[0].Protocol);
    }

    [Fact]
    public void AllowInboundPort_ProtocolUdpLowercase_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "udp");

        // Assert - Protocol validation is case-insensitive
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);
        Assert.Equal("udp", _mockOperations.AddedRules[0].Protocol);
    }

    [Fact]
    public void AllowInboundPort_ProtocolMixedCase_PassesValidation()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRuleName", "Tcp");

        // Assert - Protocol validation is case-insensitive
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);
        Assert.Equal("Tcp", _mockOperations.AddedRules[0].Protocol);
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
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);
        Assert.Equal(protocol, _mockOperations.AddedRules[0].Protocol);
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

    #region Command Formation Tests

    [Fact]
    public void AllowInboundPort_ValidInput_CallsOperationsWithCorrectArguments()
    {
        // Arrange
        const int expectedPort = 25565;
        const string expectedRuleName = "Minecraft Server";
        const string expectedProtocol = "TCP";

        // Act
        var result = _manager.AllowInboundPort(expectedPort, expectedRuleName, expectedProtocol);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);

        var addedRule = _mockOperations.AddedRules[0];
        Assert.Equal(expectedRuleName, addedRule.RuleName);
        Assert.Equal(expectedPort, addedRule.Port);
        Assert.Equal(expectedProtocol, addedRule.Protocol);
        Assert.Contains("Meridian Console managed rule", addedRule.Description, StringComparison.Ordinal);
        Assert.Contains("25565/TCP", addedRule.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void AllowInboundPort_ValidInputUDP_CallsOperationsWithCorrectProtocol()
    {
        // Arrange
        const int expectedPort = 27015;
        const string expectedRuleName = "Game Server Query";
        const string expectedProtocol = "UDP";

        // Act
        var result = _manager.AllowInboundPort(expectedPort, expectedRuleName, expectedProtocol);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.AddedRules);

        var addedRule = _mockOperations.AddedRules[0];
        Assert.Equal(expectedRuleName, addedRule.RuleName);
        Assert.Equal(expectedPort, addedRule.Port);
        Assert.Equal(expectedProtocol, addedRule.Protocol);
        Assert.Contains("27015/UDP", addedRule.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveRule_ValidInput_CallsOperationsWithCorrectArguments()
    {
        // Arrange
        const string expectedRuleName = "Test Rule To Remove";

        // Act
        var result = _manager.RemoveRule(expectedRuleName);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(_mockOperations.RemovedRules);
        Assert.Equal(expectedRuleName, _mockOperations.RemovedRules[0]);
    }

    [Fact]
    public void RuleExists_ValidInput_CallsOperationsWithCorrectArguments()
    {
        // Arrange
        const string expectedRuleName = "Test Rule To Check";
        _mockOperations.ExistingRules.Add(expectedRuleName);

        // Act
        var result = _manager.RuleExists(expectedRuleName);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Single(_mockOperations.CheckedRules);
        Assert.Equal(expectedRuleName, _mockOperations.CheckedRules[0]);
    }

    [Fact]
    public void RuleExists_RuleDoesNotExist_ReturnsFalse()
    {
        // Arrange
        const string ruleName = "NonExistent Rule";
        // Don't add to ExistingRules

        // Act
        var result = _manager.RuleExists(ruleName);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        Assert.Single(_mockOperations.CheckedRules);
    }

    #endregion

    #region Unavailable Operations Tests

    [Fact]
    public void AllowInboundPort_OperationsUnavailable_ReturnsFailure()
    {
        // Arrange
        _mockOperations.SetAvailable(false);

        // Act
        var result = _manager.AllowInboundPort(8080, "ValidRule", "TCP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("not available", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveRule_OperationsUnavailable_ReturnsFailure()
    {
        // Arrange
        _mockOperations.SetAvailable(false);

        // Act
        var result = _manager.RemoveRule("ValidRule");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("not available", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleExists_OperationsUnavailable_ReturnsFailure()
    {
        // Arrange
        _mockOperations.SetAvailable(false);

        // Act
        var result = _manager.RuleExists("ValidRule");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("not available", result.Error, StringComparison.Ordinal);
    }

    #endregion

    #region Reserved Rule Name Tests

    [Fact]
    public void AllowInboundPort_RuleNameAll_ReturnsFailure()
    {
        // Act
        var result = _manager.AllowInboundPort(8080, "all", "TCP");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("reserved", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.AddedRules);
    }

    [Fact]
    public void RemoveRule_RuleNameAll_ReturnsFailure()
    {
        // Act
        var result = _manager.RemoveRule("ALL");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("reserved", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.RemovedRules);
    }

    [Fact]
    public void RuleExists_RuleNameAll_ReturnsFailure()
    {
        // Act
        var result = _manager.RuleExists("All");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("reserved", result.Error, StringComparison.Ordinal);
        Assert.Empty(_mockOperations.CheckedRules);
    }

    #endregion

    #region Mock Firewall Operations

    /// <summary>
    /// Mock implementation of <see cref="IFirewallOperations"/> for testing.
    /// </summary>
    /// <remarks>
    /// Tracks all operations without executing real firewall commands.
    /// </remarks>
    private sealed class MockFirewallOperations : IFirewallOperations
    {
        private bool _isAvailable = true;

        public bool IsAvailable => _isAvailable;

        /// <summary>
        /// Gets the list of rules that were added.
        /// </summary>
        public List<AddRuleRequest> AddedRules { get; } = [];

        /// <summary>
        /// Gets the list of rule names that were removed.
        /// </summary>
        public List<string> RemovedRules { get; } = [];

        /// <summary>
        /// Gets the list of rule names that were checked for existence.
        /// </summary>
        public List<string> CheckedRules { get; } = [];

        /// <summary>
        /// Gets or sets the set of rules that exist (for RuleExists checks).
        /// </summary>
        public HashSet<string> ExistingRules { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Sets whether the operations are available.
        /// </summary>
        /// <param name="available">Whether operations should be available.</param>
        public void SetAvailable(bool available)
        {
            _isAvailable = available;
        }

        public Result<bool> AddRule(string ruleName, string description, string protocol, int port)
        {
            AddedRules.Add(new AddRuleRequest(ruleName, description, protocol, port));
            return Result<bool>.Success(true);
        }

        public Result<bool> RemoveRule(string ruleName)
        {
            RemovedRules.Add(ruleName);
            return Result<bool>.Success(true);
        }

        public Result<bool> RuleExists(string ruleName)
        {
            CheckedRules.Add(ruleName);
            return Result<bool>.Success(ExistingRules.Contains(ruleName));
        }
    }

    /// <summary>
    /// Represents a request to add a firewall rule.
    /// </summary>
    /// <param name="RuleName">The rule name.</param>
    /// <param name="Description">The rule description.</param>
    /// <param name="Protocol">The protocol.</param>
    /// <param name="Port">The port number.</param>
    private sealed record AddRuleRequest(string RuleName, string Description, string Protocol, int Port);

    #endregion
}
