using Dhadgar.ServiceDefaults.Errors;
using FluentAssertions;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests.Errors;

public class DomainExceptionTests
{
    [Fact]
    public void ValidationException_ReturnsCorrectStatusCode()
    {
        // Arrange
        var exception = new ValidationException("Validation failed");

        // Act & Assert
        exception.StatusCode.Should().Be(400);
    }

    [Fact]
    public void ValidationException_ReturnsCorrectErrorType()
    {
        // Arrange
        var exception = new ValidationException("Validation failed");

        // Act & Assert
        exception.ErrorType.Should().Be("https://meridian.console/errors/validation");
    }

    [Fact]
    public void ValidationException_StoresFieldErrors()
    {
        // Arrange
        var errors = new Dictionary<string, string[]>
        {
            ["email"] = ["Invalid email format"],
            ["password"] = ["Password too short", "Password must contain a number"]
        };

        // Act
        var exception = new ValidationException("Validation failed", errors);

        // Assert
        exception.Errors.Should().NotBeNull();
        exception.Errors.Should().ContainKey("email");
        exception.Errors!["email"].Should().ContainSingle("Invalid email format");
        exception.Errors.Should().ContainKey("password");
        exception.Errors["password"].Should().HaveCount(2);
    }

    [Fact]
    public void ValidationException_WithoutErrors_HasNullErrors()
    {
        // Arrange
        var exception = new ValidationException("Validation failed");

        // Act & Assert
        exception.Errors.Should().BeNull();
    }

    [Fact]
    public void NotFoundException_ReturnsCorrectStatusCode()
    {
        // Arrange
        var exception = new NotFoundException("Resource not found");

        // Act & Assert
        exception.StatusCode.Should().Be(404);
    }

    [Fact]
    public void NotFoundException_ReturnsCorrectErrorType()
    {
        // Arrange
        var exception = new NotFoundException("Resource not found");

        // Act & Assert
        exception.ErrorType.Should().Be("https://meridian.console/errors/not-found");
    }

    [Fact]
    public void NotFoundException_WithResourceInfo_BuildsCorrectMessage()
    {
        // Arrange & Act
        var exception = new NotFoundException("User", "12345");

        // Assert
        exception.ResourceType.Should().Be("User");
        exception.ResourceId.Should().Be("12345");
        exception.Message.Should().Contain("User");
        exception.Message.Should().Contain("12345");
    }

    [Fact]
    public void NotFoundException_WithMessage_HasNoResourceInfo()
    {
        // Arrange
        var exception = new NotFoundException("Resource not found");

        // Act & Assert
        exception.ResourceType.Should().BeNull();
        exception.ResourceId.Should().BeNull();
    }

    [Fact]
    public void ConflictException_ReturnsCorrectStatusCode()
    {
        // Arrange
        var exception = new ConflictException("Resource already exists");

        // Act & Assert
        exception.StatusCode.Should().Be(409);
    }

    [Fact]
    public void ConflictException_ReturnsCorrectErrorType()
    {
        // Arrange
        var exception = new ConflictException("Resource already exists");

        // Act & Assert
        exception.ErrorType.Should().Be("https://meridian.console/errors/conflict");
    }

    [Fact]
    public void UnauthorizedException_ReturnsCorrectStatusCode()
    {
        // Arrange
        var exception = new UnauthorizedException("Invalid credentials");

        // Act & Assert
        exception.StatusCode.Should().Be(401);
    }

    [Fact]
    public void UnauthorizedException_ReturnsCorrectErrorType()
    {
        // Arrange
        var exception = new UnauthorizedException("Invalid credentials");

        // Act & Assert
        exception.ErrorType.Should().Be("https://meridian.console/errors/unauthorized");
    }

    [Fact]
    public void ForbiddenException_ReturnsCorrectStatusCode()
    {
        // Arrange
        var exception = new ForbiddenException("Access denied");

        // Act & Assert
        exception.StatusCode.Should().Be(403);
    }

    [Fact]
    public void ForbiddenException_ReturnsCorrectErrorType()
    {
        // Arrange
        var exception = new ForbiddenException("Access denied");

        // Act & Assert
        exception.ErrorType.Should().Be("https://meridian.console/errors/forbidden");
    }

    [Theory]
    [InlineData(typeof(ValidationException), 400)]
    [InlineData(typeof(NotFoundException), 404)]
    [InlineData(typeof(ConflictException), 409)]
    [InlineData(typeof(UnauthorizedException), 401)]
    [InlineData(typeof(ForbiddenException), 403)]
    public void AllDomainExceptions_ReturnCorrectStatusCodes(Type exceptionType, int expectedStatusCode)
    {
        // Arrange
        var exception = (DomainException)Activator.CreateInstance(exceptionType, "Test message")!;

        // Act & Assert
        exception.StatusCode.Should().Be(expectedStatusCode);
    }

    [Fact]
    public void DomainException_InnerException_IsPreserved()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new ValidationException("Outer error", innerException);

        // Assert
        exception.InnerException.Should().BeSameAs(innerException);
    }
}
