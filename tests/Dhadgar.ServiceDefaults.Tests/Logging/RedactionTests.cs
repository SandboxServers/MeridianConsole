using Dhadgar.ServiceDefaults.Logging.Redactors;
using FluentAssertions;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests.Logging;

/// <summary>
/// Unit tests for PII redaction components.
/// Verifies that sensitive data is properly redacted before logging.
/// </summary>
public class RedactionTests
{
    #region EmailRedactor Tests

    [Fact]
    public void EmailRedactor_StandardEmail_ReturnsConstantPattern()
    {
        // Arrange
        var redactor = new EmailRedactor();
        var email = "user@example.com";
        var buffer = new char[redactor.GetRedactedLength(email)];

        // Act
        var length = redactor.Redact(email, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Be("***@***.***");
    }

    [Fact]
    public void EmailRedactor_EmailWithSubdomain_ReturnsConstantPattern()
    {
        // Arrange
        var redactor = new EmailRedactor();
        var email = "user@mail.example.com";
        var buffer = new char[redactor.GetRedactedLength(email)];

        // Act
        var length = redactor.Redact(email, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Be("***@***.***");
    }

    [Fact]
    public void EmailRedactor_EmptyString_ReturnsConstantPattern()
    {
        // Arrange
        var redactor = new EmailRedactor();
        var email = "";
        var buffer = new char[redactor.GetRedactedLength(email)];

        // Act
        var length = redactor.Redact(email, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Be("***@***.***");
    }

    [Fact]
    public void EmailRedactor_VeryLongEmail_ReturnsConstantPattern()
    {
        // Arrange
        var redactor = new EmailRedactor();
        var email = "verylongusername.with.dots.and.plus+tag@subdomain.example.organization.com";
        var buffer = new char[redactor.GetRedactedLength(email)];

        // Act
        var length = redactor.Redact(email, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Be("***@***.***");
    }

    [Fact]
    public void EmailRedactor_GetRedactedLength_ReturnsConstantLength()
    {
        // Arrange
        var redactor = new EmailRedactor();

        // Assert - all inputs should return same length
        redactor.GetRedactedLength("a@b.com").Should().Be(11);
        redactor.GetRedactedLength("very.long.email@subdomain.example.com").Should().Be(11);
        redactor.GetRedactedLength("").Should().Be(11);
    }

    #endregion

    #region TokenRedactor Tests

    [Fact]
    public void TokenRedactor_ShortToken_ReturnsRedactedWithLength()
    {
        // Arrange
        var redactor = new TokenRedactor();
        var token = "abc123";
        var buffer = new char[redactor.GetRedactedLength(token)];

        // Act
        var length = redactor.Redact(token, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Be("[REDACTED-TOKEN:len=6]");
    }

    [Fact]
    public void TokenRedactor_JwtLikeToken_ReturnsRedactedWithLength()
    {
        // Arrange
        var redactor = new TokenRedactor();
        // Simulate a JWT-like token (header.payload.signature)
        var token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";
        var buffer = new char[redactor.GetRedactedLength(token)];

        // Act
        var length = redactor.Redact(token, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Be($"[REDACTED-TOKEN:len={token.Length}]");
        result.Should().Contain($"len={token.Length}"); // Verify the length is included dynamically
    }

    [Fact]
    public void TokenRedactor_EmptyString_ReturnsRedactedWithZeroLength()
    {
        // Arrange
        var redactor = new TokenRedactor();
        var token = "";
        var buffer = new char[redactor.GetRedactedLength(token)];

        // Act
        var length = redactor.Redact(token, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Be("[REDACTED-TOKEN:len=0]");
    }

    [Fact]
    public void TokenRedactor_SingleCharacter_ReturnsRedactedWithLength()
    {
        // Arrange
        var redactor = new TokenRedactor();
        var token = "x";
        var buffer = new char[redactor.GetRedactedLength(token)];

        // Act
        var length = redactor.Redact(token, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Be("[REDACTED-TOKEN:len=1]");
    }

    [Theory]
    [InlineData(1, "[REDACTED-TOKEN:len=1]")]
    [InlineData(10, "[REDACTED-TOKEN:len=10]")]
    [InlineData(100, "[REDACTED-TOKEN:len=100]")]
    [InlineData(1000, "[REDACTED-TOKEN:len=1000]")]
    public void TokenRedactor_VariousLengths_ReturnsCorrectFormat(int tokenLength, string expected)
    {
        // Arrange
        var redactor = new TokenRedactor();
        var token = new string('x', tokenLength);
        var buffer = new char[redactor.GetRedactedLength(token)];

        // Act
        var length = redactor.Redact(token, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Be(expected);
    }

    #endregion

    #region ConnectionStringRedactor Tests

    [Fact]
    public void ConnectionStringRedactor_PostgreSQLConnectionString_RedactsCredentials()
    {
        // Arrange
        var redactor = new ConnectionStringRedactor();
        var connectionString = "Host=localhost;Port=5432;Database=dhadgar;Username=admin;Password=secret123";
        var buffer = new char[redactor.GetRedactedLength(connectionString)];

        // Act
        var length = redactor.Redact(connectionString, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Contain("Host=localhost");
        result.Should().Contain("Port=5432");
        result.Should().Contain("Database=dhadgar");
        result.Should().Contain("[CREDENTIALS-REDACTED]");
        result.Should().NotContain("secret123");
        result.Should().NotContain("admin");
        result.Should().NotContain("Username");
        result.Should().NotContain("Password");
    }

    [Fact]
    public void ConnectionStringRedactor_SqlServerConnectionString_RedactsCredentials()
    {
        // Arrange
        var redactor = new ConnectionStringRedactor();
        var connectionString = "Server=myserver;Database=mydb;User Id=admin;Password=mypassword";
        var buffer = new char[redactor.GetRedactedLength(connectionString)];

        // Act
        var length = redactor.Redact(connectionString, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Contain("Host=myserver");
        result.Should().Contain("Database=mydb");
        result.Should().Contain("[CREDENTIALS-REDACTED]");
        result.Should().NotContain("mypassword");
        result.Should().NotContain("admin");
    }

    [Fact]
    public void ConnectionStringRedactor_ConnectionStringWithoutPassword_PreservesHostAndDatabase()
    {
        // Arrange
        var redactor = new ConnectionStringRedactor();
        var connectionString = "Host=localhost;Database=testdb;Pooling=true";
        var buffer = new char[redactor.GetRedactedLength(connectionString)];

        // Act
        var length = redactor.Redact(connectionString, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Contain("Host=localhost");
        result.Should().Contain("Database=testdb");
        result.Should().Contain("[CREDENTIALS-REDACTED]");
    }

    [Fact]
    public void ConnectionStringRedactor_MalformedConnectionString_ReturnsFullyRedacted()
    {
        // Arrange
        var redactor = new ConnectionStringRedactor();
        var connectionString = "this is not a valid connection string at all";
        var buffer = new char[redactor.GetRedactedLength(connectionString)];

        // Act
        var length = redactor.Redact(connectionString, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Be("[CONNECTION-STRING-REDACTED]");
    }

    [Fact]
    public void ConnectionStringRedactor_EmptyString_ReturnsFullyRedacted()
    {
        // Arrange
        var redactor = new ConnectionStringRedactor();
        var connectionString = "";
        var buffer = new char[redactor.GetRedactedLength(connectionString)];

        // Act
        var length = redactor.Redact(connectionString, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Be("[CONNECTION-STRING-REDACTED]");
    }

    [Fact]
    public void ConnectionStringRedactor_WhitespaceString_ReturnsFullyRedacted()
    {
        // Arrange
        var redactor = new ConnectionStringRedactor();
        var connectionString = "   ";
        var buffer = new char[redactor.GetRedactedLength(connectionString)];

        // Act
        var length = redactor.Redact(connectionString, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Be("[CONNECTION-STRING-REDACTED]");
    }

    [Fact]
    public void ConnectionStringRedactor_DataSourceFormat_ExtractsHost()
    {
        // Arrange
        var redactor = new ConnectionStringRedactor();
        var connectionString = "Data Source=mydbserver;Initial Catalog=mydb;Integrated Security=true";
        var buffer = new char[redactor.GetRedactedLength(connectionString)];

        // Act
        var length = redactor.Redact(connectionString, buffer);

        // Assert
        var result = new string(buffer, 0, length);
        result.Should().Contain("Host=mydbserver");
        result.Should().Contain("Database=mydb");
        result.Should().Contain("[CREDENTIALS-REDACTED]");
        result.Should().NotContain("Integrated Security");
    }

    #endregion
}
