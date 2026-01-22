namespace Dhadgar.ServiceDefaults.Errors;

/// <summary>
/// Base class for domain-specific exceptions that map to HTTP status codes.
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>
    /// Gets the HTTP status code this exception maps to.
    /// </summary>
    public abstract int StatusCode { get; }

    /// <summary>
    /// Gets the RFC 9457 error type URI.
    /// </summary>
    public abstract string ErrorType { get; }

    protected DomainException(string message) : base(message)
    {
    }

    protected DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception for validation errors (HTTP 400).
/// </summary>
public class ValidationException : DomainException
{
    /// <summary>
    /// Gets the field-level validation errors.
    /// </summary>
    public IDictionary<string, string[]>? Errors { get; }

    public override int StatusCode => 400;
    public override string ErrorType => "https://meridian.console/errors/validation";

    public ValidationException(string message) : base(message)
    {
    }

    public ValidationException(string message, IDictionary<string, string[]> errors) : base(message)
    {
        Errors = errors;
    }

    public ValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception for resource not found errors (HTTP 404).
/// </summary>
public class NotFoundException : DomainException
{
    /// <summary>
    /// Gets the type of resource that was not found.
    /// </summary>
    public string? ResourceType { get; }

    /// <summary>
    /// Gets the identifier of the resource that was not found.
    /// </summary>
    public string? ResourceId { get; }

    public override int StatusCode => 404;
    public override string ErrorType => "https://meridian.console/errors/not-found";

    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string resourceType, string resourceId)
        : base($"{resourceType} with identifier '{resourceId}' was not found.")
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
    }

    public NotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception for conflict/duplicate errors (HTTP 409).
/// </summary>
public class ConflictException : DomainException
{
    public override int StatusCode => 409;
    public override string ErrorType => "https://meridian.console/errors/conflict";

    public ConflictException(string message) : base(message)
    {
    }

    public ConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception for authentication failures (HTTP 401).
/// </summary>
public class UnauthorizedException : DomainException
{
    public override int StatusCode => 401;
    public override string ErrorType => "https://meridian.console/errors/unauthorized";

    public UnauthorizedException(string message) : base(message)
    {
    }

    public UnauthorizedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception for authorization failures (HTTP 403).
/// </summary>
public class ForbiddenException : DomainException
{
    public override int StatusCode => 403;
    public override string ErrorType => "https://meridian.console/errors/forbidden";

    public ForbiddenException(string message) : base(message)
    {
    }

    public ForbiddenException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
