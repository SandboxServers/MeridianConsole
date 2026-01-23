# Dhadgar.Shared

**Cross-cutting utilities, primitives, and helper functions for the Meridian Console platform.**

This is the foundational utility library for the Dhadgar codebase. It provides generic, reusable components that any service, agent, or other shared library can depend on without introducing coupling to specific domains or external frameworks.

---

## Table of Contents

1. [Overview](#overview)
2. [Purpose and Design Philosophy](#purpose-and-design-philosophy)
3. [Dependency Rules](#dependency-rules)
4. [Current Contents](#current-contents)
5. [Project Structure](#project-structure)
6. [Usage Examples](#usage-examples)
7. [Utilities Catalog](#utilities-catalog)
8. [Planned Additions](#planned-additions)
9. [Best Practices for Contributors](#best-practices-for-contributors)
10. [Testing](#testing)
11. [Related Documentation](#related-documentation)

---

## Overview

`Dhadgar.Shared` is a .NET 10 class library that serves as the lowest-level shared component in the Meridian Console architecture. It sits at the bottom of the dependency hierarchy, meaning:

- **It has no dependencies** on other Dhadgar libraries
- **It has no external NuGet package dependencies** (pure .NET BCL only)
- **Every other shared library and service can safely reference it**

This design ensures that utilities defined here are universally available without creating circular dependencies or forcing unwanted transitive dependencies onto consuming projects.

### Key Characteristics

| Aspect | Description |
|--------|-------------|
| **Target Framework** | .NET 10.0 |
| **Language Version** | C# Latest (with nullable reference types enabled) |
| **Dependencies** | None (BCL only) |
| **Assembly Name** | `Dhadgar.Shared` |
| **Namespace Root** | `Dhadgar.Shared` |

---

## Purpose and Design Philosophy

### What Belongs in Dhadgar.Shared

This library is the home for **generic utilities** that:

1. **Have no domain-specific logic** - Utilities should be general-purpose, not tied to game servers, identity, billing, or any specific Meridian Console domain.

2. **Have no external dependencies** - If a utility requires a NuGet package (e.g., JSON serialization, HTTP, logging), it belongs elsewhere:
   - JSON/serialization helpers -> `Dhadgar.Contracts`
   - HTTP utilities -> `Dhadgar.ServiceDefaults`
   - Messaging utilities -> `Dhadgar.Messaging`

3. **Are reusable across all projects** - The utility should be something that any service, agent, or library might reasonably need.

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

### Library Hierarchy

Understanding where `Dhadgar.Shared` fits in the dependency graph:

```
                    ┌─────────────────────────────┐
                    │        Services             │
                    │  (Gateway, Identity, etc.)  │
                    └──────────┬──────────────────┘
                               │ references
        ┌──────────────────────┼──────────────────────┐
        │                      │                      │
        ▼                      ▼                      ▼
┌───────────────┐    ┌─────────────────┐    ┌──────────────┐
│ ServiceDefaults│    │    Messaging    │    │   Contracts  │
│ (middleware,  │    │  (MassTransit)  │    │   (DTOs)     │
│  observability)│    └────────┬────────┘    └──────────────┘
└───────┬───────┘              │
        │                      │
        │ can reference        │ can reference
        │                      │
        ▼                      ▼
┌─────────────────────────────────────────────────────┐
│                   Dhadgar.Shared                    │
│            (utilities, primitives)                  │
│                                                     │
│  *** NO DEPENDENCIES - BOTTOM OF THE HIERARCHY *** │
└─────────────────────────────────────────────────────┘
```

---

## Dependency Rules

### This Library's Dependencies

`Dhadgar.Shared` has **zero** project or package references:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>Dhadgar.Shared</AssemblyName>
    <RootNamespace>Dhadgar.Shared</RootNamespace>
  </PropertyGroup>
  <!-- No ItemGroup with PackageReference or ProjectReference -->
</Project>
```

This is intentional and must be maintained. The library relies solely on the .NET Base Class Library (BCL).

### Who Can Reference This Library

Every project in the solution can safely reference `Dhadgar.Shared`:

| Project Type | Can Reference? | Notes |
|--------------|----------------|-------|
| Services (Gateway, Identity, etc.) | Yes | Via ProjectReference |
| Dhadgar.ServiceDefaults | Yes | For common utilities |
| Dhadgar.Messaging | Yes | For common utilities |
| Dhadgar.Contracts | Yes | For common utilities |
| Agent projects | Yes | For shared code with control plane |
| Test projects | Yes | For testing utilities |

### Adding Dependencies to This Library

**Do not add dependencies to this library.** If you need functionality that requires a NuGet package:

1. Evaluate if the utility truly belongs in a different shared library
2. Consider if the utility can be implemented using only BCL types
3. If a package is absolutely necessary, propose moving the utility to `ServiceDefaults` or creating a new shared library

---

## Current Contents

The library is currently in early scaffolding stage with minimal implementation. This is intentional - the codebase provides the "shape" for incremental development.

### File Structure

```
src/Shared/Dhadgar.Shared/
├── Dhadgar.Shared.csproj    # Project file (no dependencies)
├── Hello.cs                 # Smoke-test surface area
├── CLAUDE.md                # AI assistant instructions
└── README.md                # This documentation
```

### Hello.cs

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

## Project Structure

### Recommended Organization

As the library grows, utilities should be organized into logical namespaces and folders:

```
src/Shared/Dhadgar.Shared/
├── Dhadgar.Shared.csproj
├── README.md
├── CLAUDE.md
│
├── Extensions/                    # Extension methods
│   ├── StringExtensions.cs
│   ├── DateTimeExtensions.cs
│   ├── CollectionExtensions.cs
│   └── TaskExtensions.cs
│
├── Primitives/                    # Custom primitive types
│   ├── Result.cs                  # Result<T, TError> monad
│   ├── Option.cs                  # Option<T> for nullable alternatives
│   └── Guard.cs                   # Argument validation
│
├── Utilities/                     # Standalone utility classes
│   ├── Retry.cs                   # Simple retry logic (no Polly)
│   ├── HashHelper.cs              # Hashing utilities
│   └── PathHelper.cs              # Cross-platform path utilities
│
└── Constants/                     # Shared constants
    └── DhadgarConstants.cs
```

### Naming Conventions

| Type | Convention | Example |
|------|------------|---------|
| Extension classes | `{Type}Extensions` | `StringExtensions`, `TaskExtensions` |
| Primitive types | Descriptive noun | `Result`, `Option`, `Guard` |
| Utility classes | `{Purpose}Helper` or descriptive | `HashHelper`, `Retry` |
| Constants | `{Domain}Constants` | `DhadgarConstants` |

---

## Usage Examples

### Referencing the Library

Add a project reference in your `.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\Shared\Dhadgar.Shared\Dhadgar.Shared.csproj" />
</ItemGroup>
```

### Using Current Utilities

```csharp
using Dhadgar.Shared;

// Simple smoke-check
Console.WriteLine(Hello.Message);  // "Hello from Dhadgar.Shared"
```

### Example: Future Extension Methods (Planned)

When extension methods are added, usage will look like:

```csharp
using Dhadgar.Shared.Extensions;

// String extensions
string? name = null;
string displayName = name.OrDefault("Anonymous");  // "Anonymous"

bool isValid = "user@example.com".IsValidEmail();  // true

string truncated = "Hello, World!".Truncate(5);    // "Hello..."

// Collection extensions
var items = new List<int> { 1, 2, 3, 4, 5 };
var batches = items.Batch(2);  // [[1, 2], [3, 4], [5]]

// DateTime extensions
var timestamp = DateTime.UtcNow.ToUnixTimestamp();  // 1705936800
```

### Example: Future Result Type (Planned)

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

### Example: Future Guard Utilities (Planned)

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

## Utilities Catalog

### Current Utilities

#### `Hello` Class

**Namespace**: `Dhadgar.Shared`
**Type**: Static class
**Purpose**: Smoke-test surface area for build verification

| Member | Type | Description |
|--------|------|-------------|
| `Message` | `const string` | Returns "Hello from Dhadgar.Shared" |

**Usage**:
```csharp
using Dhadgar.Shared;

// Verify library is properly referenced
Assert.Equal("Hello from Dhadgar.Shared", Hello.Message);
```

### Planned Utilities

The following utilities are planned for implementation as the platform matures:

#### Extension Methods (Planned)

| Class | Methods | Description |
|-------|---------|-------------|
| `StringExtensions` | `OrDefault`, `Truncate`, `IsValidEmail`, `ToSlug`, `Sanitize` | Common string operations |
| `DateTimeExtensions` | `ToUnixTimestamp`, `FromUnixTimestamp`, `ToIso8601`, `StartOfDay`, `EndOfDay` | Date/time conversions |
| `CollectionExtensions` | `Batch`, `IsNullOrEmpty`, `OrEmpty`, `Shuffle`, `DistinctBy` | Collection operations |
| `TaskExtensions` | `WithTimeout`, `WithCancellation`, `FireAndForget` | Async helpers |
| `GuidExtensions` | `ToShortString`, `IsEmpty` | GUID utilities |

#### Primitive Types (Planned)

| Type | Description |
|------|-------------|
| `Result<T, TError>` | Discriminated union for success/failure without exceptions |
| `Option<T>` | Explicit nullable alternative for domain clarity |
| `Guard` | Fluent argument validation (throws `ArgumentException`) |
| `Clock` | Abstraction over `DateTime.UtcNow` for testing |

#### Utility Classes (Planned)

| Class | Description |
|-------|-------------|
| `Retry` | Simple retry logic without external dependencies |
| `HashHelper` | SHA256/SHA512 hashing utilities |
| `Base64Url` | URL-safe Base64 encoding/decoding |
| `PathHelper` | Cross-platform path normalization |
| `SlugGenerator` | URL-friendly string generation |

---

## Planned Additions

### High Priority

These utilities are commonly needed and should be implemented early:

1. **Guard utilities** - Every service needs argument validation
2. **String extensions** - `Truncate`, `OrDefault`, `Sanitize` are frequently needed
3. **Result type** - Better error handling than exceptions for expected failures
4. **Collection extensions** - `Batch` for pagination, `IsNullOrEmpty` for null checks

### Medium Priority

These are useful but can wait:

1. **DateTime extensions** - Unix timestamp conversions for APIs
2. **HashHelper** - For checksum validation in file transfers
3. **Base64Url** - For token encoding

### Low Priority

These may not be needed:

1. **Option type** - C# nullable reference types may suffice
2. **Clock abstraction** - Can use `TimeProvider` in .NET 8+

---

## Best Practices for Contributors

### Before Adding a Utility

Ask yourself:

1. **Is it generic?** Would at least 3 different services reasonably use this?
2. **Is it dependency-free?** Can it be implemented with BCL only?
3. **Does it already exist?** Check the .NET BCL first - modern .NET has many utilities
4. **Is it stable?** Will the API need to change frequently?

### Implementation Guidelines

1. **Make it static when possible** - Utility classes should typically be static
2. **Use XML documentation** - Every public member needs `<summary>` docs
3. **Provide overloads** - Consider common use cases and provide convenient overloads
4. **Handle nulls explicitly** - Use nullable annotations and document null behavior
5. **Keep it simple** - Prefer simple, obvious implementations over clever ones
6. **Write tests first** - Add tests to `Dhadgar.Shared.Tests` before implementing

### Code Style

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

### Adding a New Utility Checklist

- [ ] Determine the correct namespace/folder (Extensions, Primitives, Utilities)
- [ ] Verify no external dependencies are required
- [ ] Write XML documentation for all public members
- [ ] Add unit tests in `Dhadgar.Shared.Tests`
- [ ] Run `dotnet test` to verify all tests pass
- [ ] Update this README's Utilities Catalog section

### Pull Request Guidelines

When submitting utilities:

1. Keep PRs focused - one utility per PR when possible
2. Include usage examples in the PR description
3. Explain why this utility belongs in Shared vs elsewhere
4. Ensure tests cover edge cases (null inputs, empty collections, etc.)

---

## Testing

### Test Project

Tests for this library are in `tests/Dhadgar.Shared.Tests/`:

```
tests/Dhadgar.Shared.Tests/
├── Dhadgar.Shared.Tests.csproj
└── HelloWorldTests.cs
```

### Running Tests

```bash
# Run all Shared tests
dotnet test tests/Dhadgar.Shared.Tests

# Run specific test
dotnet test tests/Dhadgar.Shared.Tests --filter "FullyQualifiedName~HelloWorldTests"

# Run with verbose output
dotnet test tests/Dhadgar.Shared.Tests -v normal
```

### Current Tests

**HelloWorldTests.cs**:
```csharp
using Xunit;
using Dhadgar.Shared;

namespace Dhadgar.Shared.Tests;

public class HelloWorldTests
{
    [Fact]
    public void Hello_message_is_correct()
    {
        Assert.Equal("Hello from Dhadgar.Shared", Hello.Message);
    }
}
```

### Test Guidelines

When adding tests:

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

### Dhadgar Platform Documentation

| Document | Description |
|----------|-------------|
| [Root CLAUDE.md](../../../CLAUDE.md) | Main project instructions and architecture overview |
| [Root README.md](../../../README.md) | Project overview and getting started |

### Other Shared Libraries

| Library | Purpose | Documentation |
|---------|---------|---------------|
| [Dhadgar.Contracts](../Dhadgar.Contracts/) | DTOs and message contracts | See CLAUDE.md |
| [Dhadgar.Messaging](../Dhadgar.Messaging/) | MassTransit/RabbitMQ conventions | See CLAUDE.md |
| [Dhadgar.ServiceDefaults](../Dhadgar.ServiceDefaults/) | ASP.NET Core middleware and observability | See CLAUDE.md |

### External Resources

| Resource | Description |
|----------|-------------|
| [.NET API Browser](https://docs.microsoft.com/en-us/dotnet/api/) | Check if BCL already provides needed functionality |
| [C# Language Reference](https://docs.microsoft.com/en-us/dotnet/csharp/) | Language features for implementation |
| [.NET Performance Guidelines](https://docs.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/performance-rules) | Performance best practices |

---

## Summary

`Dhadgar.Shared` is the foundational utility library for the Meridian Console platform. Its key principles are:

1. **Zero dependencies** - Only BCL, no packages
2. **Universal compatibility** - Any project can reference it
3. **Generic utilities** - No domain-specific code
4. **Stability** - Changes affect the entire codebase

Currently minimal (scaffolding stage), this library will grow to include common extensions, primitive types, and utilities as the platform matures. When contributing, prioritize simplicity, documentation, and thorough testing.
