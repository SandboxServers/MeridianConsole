# Dhadgar.Shared Library

Cross-cutting utilities, primitives, and helper functions for the Meridian Console platform.

## Overview

`Dhadgar.Shared` is the foundational utility library in the Dhadgar codebase. It provides generic, reusable components that any service, agent, or shared library can depend on without introducing coupling to specific domains or external frameworks.

**Location**: `src/Shared/Dhadgar.Shared/`

### Key Characteristics

| Aspect | Value |
|--------|-------|
| Target Framework | .NET 10.0 |
| Dependencies | None (BCL only) |
| Assembly Name | `Dhadgar.Shared` |
| Namespace Root | `Dhadgar.Shared` |

---

## Purpose

This library is the home for **generic utilities** that:

1. **Have no domain-specific logic** - Utilities are general-purpose, not tied to game servers, identity, billing, or any specific Meridian Console domain.

2. **Have no external dependencies** - Everything uses only the .NET Base Class Library (BCL). If a utility requires a NuGet package, it belongs elsewhere.

3. **Are reusable across all projects** - Any service, agent, or library might reasonably need these utilities.

4. **Are stable and unlikely to change frequently** - Since this library is referenced everywhere, changes have wide-reaching impact.

### What Does NOT Belong Here

| Category | Correct Location | Reason |
|----------|------------------|--------|
| DTOs, request/response models | `Dhadgar.Contracts` | Domain-specific data structures |
| Message contracts | `Dhadgar.Contracts` | MassTransit integration |
| ASP.NET Core middleware | `Dhadgar.ServiceDefaults` | Framework-specific |
| OpenTelemetry utilities | `Dhadgar.ServiceDefaults` | Requires external packages |
| MassTransit consumers/publishers | `Dhadgar.Messaging` | Requires MassTransit package |
| Service-specific helpers | The service itself | Domain coupling |

---

## Dependency Hierarchy

`Dhadgar.Shared` sits at the bottom of the dependency hierarchy:

```
                    +-----------------------------+
                    |        Services             |
                    |  (Gateway, Identity, etc.)  |
                    +-------------+---------------+
                                  | references
        +-------------------------+-------------------------+
        |                         |                         |
        v                         v                         v
+---------------+       +-----------------+       +--------------+
| ServiceDefaults|       |    Messaging    |       |   Contracts  |
| (middleware,  |       |  (MassTransit)  |       |   (DTOs)     |
|  observability)|       +--------+--------+       +--------------+
+-------+-------+                 |
        |                         |
        | can reference           | can reference
        |                         |
        v                         v
+-------------------------------------------------------------+
|                      Dhadgar.Shared                         |
|               (utilities, primitives)                       |
|                                                             |
|       *** NO DEPENDENCIES - BOTTOM OF THE HIERARCHY ***    |
+-------------------------------------------------------------+
```

Every project in the solution can safely reference `Dhadgar.Shared`.

---

## Current Contents

The library is in scaffolding stage with minimal implementation:

```
src/Shared/Dhadgar.Shared/
+-- Dhadgar.Shared.csproj    # Project file (no dependencies)
+-- Hello.cs                 # Smoke-test surface area
+-- CLAUDE.md                # AI assistant instructions
+-- README.md                # Project documentation
```

### Hello Class

The only source file currently in the library:

```csharp
namespace Dhadgar.Shared;

/// <summary>
/// "Hello world" surface area used by tests and quick smoke-checks.
/// </summary>
public static class Hello
{
    public const string Message = "Hello from Dhadgar.Shared";
}
```

**Purpose**: This class exists to:
1. Verify the library builds and can be referenced
2. Provide a simple smoke-test target for the test project
3. Serve as a template for future utility classes

---

## Usage Examples

### Referencing the Library

Add a project reference in your `.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../Shared/Dhadgar.Shared/Dhadgar.Shared.csproj" />
</ItemGroup>
```

### Using Current Utilities

```csharp
using Dhadgar.Shared;

// Simple smoke-check
Console.WriteLine(Hello.Message);  // "Hello from Dhadgar.Shared"
```

### Planned Extension Methods

When extension methods are added, usage will look like:

```csharp
using Dhadgar.Shared.Extensions;

// String extensions
string? name = null;
string displayName = name.OrDefault("Anonymous");  // "Anonymous"

string truncated = "Hello, World!".Truncate(5);    // "Hello..."

// Collection extensions
var items = new List<int> { 1, 2, 3, 4, 5 };
var batches = items.Batch(2);  // [[1, 2], [3, 4], [5]]

// DateTime extensions
var timestamp = DateTime.UtcNow.ToUnixTimestamp();  // 1705936800
```

### Planned Result Type

When the Result monad is added:

```csharp
using Dhadgar.Shared.Primitives;

public Result<User, string> GetUser(Guid id)
{
    var user = _repository.Find(id);
    if (user is null)
        return Result<User, string>.Failure($"User {id} not found");

    return Result<User, string>.Success(user);
}

// Usage
var result = GetUser(userId);
result.Match(
    success: user => Console.WriteLine($"Found: {user.Name}"),
    failure: error => Console.WriteLine($"Error: {error}")
);
```

### Planned Guard Utilities

When argument guards are added:

```csharp
using Dhadgar.Shared.Primitives;

public void CreateServer(string name, int port)
{
    Guard.NotNullOrWhiteSpace(name, nameof(name));
    Guard.InRange(port, 1024, 65535, nameof(port));

    // Proceed with validated arguments...
}
```

---

## Planned Utilities

### Extension Methods

| Class | Methods | Description |
|-------|---------|-------------|
| `StringExtensions` | `OrDefault`, `Truncate`, `IsValidEmail`, `ToSlug`, `Sanitize` | Common string operations |
| `DateTimeExtensions` | `ToUnixTimestamp`, `FromUnixTimestamp`, `ToIso8601`, `StartOfDay`, `EndOfDay` | Date/time conversions |
| `CollectionExtensions` | `Batch`, `IsNullOrEmpty`, `OrEmpty`, `Shuffle`, `DistinctBy` | Collection operations |
| `TaskExtensions` | `WithTimeout`, `WithCancellation`, `FireAndForget` | Async helpers |
| `GuidExtensions` | `ToShortString`, `IsEmpty` | GUID utilities |

### Primitive Types

| Type | Description |
|------|-------------|
| `Result<T, TError>` | Discriminated union for success/failure without exceptions |
| `Option<T>` | Explicit nullable alternative for domain clarity |
| `Guard` | Fluent argument validation (throws `ArgumentException`) |
| `Clock` | Abstraction over `DateTime.UtcNow` for testing |

### Utility Classes

| Class | Description |
|-------|-------------|
| `Retry` | Simple retry logic without external dependencies |
| `HashHelper` | SHA256/SHA512 hashing utilities |
| `Base64Url` | URL-safe Base64 encoding/decoding |
| `PathHelper` | Cross-platform path normalization |
| `SlugGenerator` | URL-friendly string generation |

---

## Common Patterns

### Recommended Project Structure

As the library grows, organize utilities into logical namespaces:

```
src/Shared/Dhadgar.Shared/
+-- Dhadgar.Shared.csproj
+-- README.md
+-- CLAUDE.md
|
+-- Extensions/                    # Extension methods
|   +-- StringExtensions.cs
|   +-- DateTimeExtensions.cs
|   +-- CollectionExtensions.cs
|   +-- TaskExtensions.cs
|
+-- Primitives/                    # Custom primitive types
|   +-- Result.cs                  # Result<T, TError> monad
|   +-- Option.cs                  # Option<T> for nullable alternatives
|   +-- Guard.cs                   # Argument validation
|
+-- Utilities/                     # Standalone utility classes
|   +-- Retry.cs                   # Simple retry logic (no Polly)
|   +-- HashHelper.cs              # Hashing utilities
|   +-- PathHelper.cs              # Cross-platform path utilities
|
+-- Constants/                     # Shared constants
    +-- DhadgarConstants.cs
```

### Naming Conventions

| Type | Convention | Example |
|------|------------|---------|
| Extension classes | `{Type}Extensions` | `StringExtensions`, `TaskExtensions` |
| Primitive types | Descriptive noun | `Result`, `Option`, `Guard` |
| Utility classes | `{Purpose}Helper` or descriptive | `HashHelper`, `Retry` |
| Constants | `{Domain}Constants` | `DhadgarConstants` |

### Code Style Example

```csharp
namespace Dhadgar.Shared.Extensions;

/// <summary>
/// Extension methods for <see cref="string"/> operations.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Returns the specified default value if the string is null or whitespace.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <param name="defaultValue">The value to return if <paramref name="value"/> is null or whitespace.</param>
    /// <returns>The original string or the default value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="defaultValue"/> is null.</exception>
    public static string OrDefault(this string? value, string defaultValue)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }
}
```

---

## Guidelines for Adding New Utilities

### Before Adding a Utility

Ask yourself:

1. **Is it generic?** Would at least 3 different services reasonably use this?
2. **Is it dependency-free?** Can it be implemented with BCL only?
3. **Does it already exist?** Check the .NET BCL first - modern .NET has many utilities.
4. **Is it stable?** Will the API need to change frequently?

### Implementation Guidelines

1. **Make it static when possible** - Utility classes should typically be static
2. **Use XML documentation** - Every public member needs `<summary>` docs
3. **Provide overloads** - Consider common use cases and provide convenient overloads
4. **Handle nulls explicitly** - Use nullable annotations and document null behavior
5. **Keep it simple** - Prefer simple, obvious implementations over clever ones
6. **Write tests first** - Add tests to `Dhadgar.Shared.Tests` before implementing

### Checklist for New Utilities

- [ ] Determine the correct namespace/folder (Extensions, Primitives, Utilities)
- [ ] Verify no external dependencies are required
- [ ] Write XML documentation for all public members
- [ ] Add unit tests in `Dhadgar.Shared.Tests`
- [ ] Run `dotnet test` to verify all tests pass
- [ ] Update the README's utilities catalog section

### Adding Dependencies

**Do not add dependencies to this library.** If you need functionality that requires a NuGet package:

1. Evaluate if the utility truly belongs in a different shared library
2. Consider if the utility can be implemented using only BCL types
3. If a package is absolutely necessary, propose moving the utility to `ServiceDefaults` or creating a new shared library

---

## Testing

### Test Project

Tests are in `tests/Dhadgar.Shared.Tests/`:

```bash
# Run all Shared tests
dotnet test tests/Dhadgar.Shared.Tests

# Run specific test
dotnet test tests/Dhadgar.Shared.Tests --filter "FullyQualifiedName~HelloWorldTests"

# Run with verbose output
dotnet test tests/Dhadgar.Shared.Tests -v normal
```

### Test Guidelines

1. Follow the pattern: `{ClassName}Tests.cs`
2. Use descriptive test method names: `MethodName_Scenario_ExpectedBehavior`
3. Test edge cases: null inputs, empty collections, boundary values
4. Avoid testing implementation details - test behavior

Example test structure:

```csharp
public class StringExtensionsTests
{
    [Fact]
    public void OrDefault_WithNullString_ReturnsDefault()
    {
        string? value = null;
        var result = value.OrDefault("default");
        Assert.Equal("default", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void OrDefault_WithWhitespaceString_ReturnsDefault(string value)
    {
        var result = value.OrDefault("default");
        Assert.Equal("default", result);
    }

    [Fact]
    public void OrDefault_WithValidString_ReturnsOriginal()
    {
        var result = "hello".OrDefault("default");
        Assert.Equal("hello", result);
    }
}
```

---

## Related Documentation

| Document | Description |
|----------|-------------|
| [Dhadgar.Contracts](./contracts.md) | DTOs and message contracts |
| [Dhadgar.Messaging](./messaging.md) | MassTransit/RabbitMQ conventions |
| [Dhadgar.ServiceDefaults](./service-defaults.md) | ASP.NET Core middleware and observability |
| [Root CLAUDE.md](/CLAUDE.md) | Main project instructions and architecture overview |
| [Root README.md](/README.md) | Project overview and getting started |

---

## Summary

`Dhadgar.Shared` is the foundational utility library for the Meridian Console platform:

1. **Zero dependencies** - Only BCL, no packages
2. **Universal compatibility** - Any project can reference it
3. **Generic utilities** - No domain-specific code
4. **Stability** - Changes affect the entire codebase

Currently minimal (scaffolding stage), this library will grow to include common extensions, primitive types, and utilities as the platform matures. When contributing, prioritize simplicity, documentation, and thorough testing.
