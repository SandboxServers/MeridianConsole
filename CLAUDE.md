# MeridianConsole - Claude Instructions

Critical patterns, security requirements, and architectural decisions for this codebase.

---

## Architecture

### Error Handling: Result<T> Pattern

**ALWAYS** use `Result<T>` for operations that can fail. Never throw for validation, IO, or network errors.

```csharp
var result = await SomeOperationAsync();
if (!result.IsSuccess) return Result<OtherType>.Failure(result.Error);
return Result<OtherType>.Success(value);
```

---

## Security (Critical)

### Path Validation

**ALL** file operations must use `PathValidator` (or `WindowsPathValidator`/`LinuxPathValidator`):

```csharp
var pathResult = _pathValidator.ValidatePath(userPath);
if (!pathResult.IsSuccess) return Result<Unit>.Failure(pathResult.Error!);
```

**Never trust user input.** Validator protects against: `..` traversal, null bytes, control chars, paths outside allowed directories.

---

### Command Injection Prevention

**NEVER** concatenate command strings. Use `ArgumentList` tokenization:

```csharp
// Correct
var startInfo = new ProcessStartInfo
{
    FileName = executablePath,
    ArgumentList = { arg1, arg2, arg3 }  // Safe
};

// DANGEROUS
var startInfo = new ProcessStartInfo
{
    FileName = "cmd.exe",
    Arguments = $"/c {executablePath} {userArg}"  // Injection risk
};
```

Same rule applies to WiX CustomActions - validate/encode before injection.

---

### SSRF Protection

URLs must be validated against `TrustedHosts` allowlist before outbound requests:

```csharp
if (!_urlValidator.IsTrustedHost(url.Host))
    return Result<Unit>.Failure(Error.Validation("Untrusted host", nameof(url)));
```

---

### Sensitive Data

Zero out sensitive data immediately after use:

```csharp
CryptographicOperations.ZeroMemory(privateKeyBytes);
Array.Clear(passwordBuffer);
```

**WiX:** Mark sensitive properties with `Hidden="yes"` to prevent logging.

---

## Windows-Specific

### Version Detection

For WiX installers: Use `WindowsBuild >= 10240` for Windows 10+, NOT `VersionNT >= 603` (can't distinguish Win8.1).

### Process Management

See `WindowsProcessManager` for Job Objects reference. `DieOnUnhandledException` does NOT enforce memory limits.

---

## Dependencies

### Central Package Management

**RULE:** NEVER hardcode versions in `.csproj` files. All versions in `Directory.Packages.props` only.

```xml
<!-- Correct -->
<PackageReference Include='Microsoft.Extensions.Logging' />

<!-- Incorrect -->
<PackageReference Include='Microsoft.Extensions.Logging' Version='8.0.0' />
```

### Package Notes

- **SIPSorcery:** P2P file transfer. Maintenance uncertain. Avoid for new implementations unless P2P required. Track Issue #95.
- **OpenTelemetry:** Use latest compatible beta (`1.15.0-beta.1`).

---

## Process Management

Wrap `Process` objects in `using` statements:

```csharp
using var process = new Process { StartInfo = /* ... */ };
if (!process.Start()) return Result<Unit>.Failure("Failed to start");
await process.WaitForExitAsync(ct);
```

Return `Result<ProcessInfo>` containing: PID, exit code, duration, output.

---

## Documentation

**Tables:** Spaces around pipes `| Option | Type |`

**Code blocks:** Always specify language ` ```csharp `

---

## Code Review

**Security review required** for changes to:
- `IControlPlaneClient` interface
- `CommandEnvelope` class
- `WindowsProcessManager` / `LinuxProcessManager`
- All agent projects (Core, Windows, Linux)

Use `security-review` label on PRs.

---

## Open Issues

- #94: Implement signature verification in `CommandValidator.Validate`
- #95: Evaluate SIPSorcery replacement
- #96: Update `ICertificateStore` to Result<T> pattern

---

## Pre-Commit Checklist

- [ ] Paths validated with `PathValidator`
- [ ] Commands use `ArgumentList`, not string concat
- [ ] Errors return `Result<T>`, not exceptions
- [ ] Sensitive data zeroed
- [ ] No hardcoded package versions
- [ ] `using` on all `IDisposable`
- [ ] URLs validated against trusted hosts
- [ ] `WindowsBuild` not `VersionNT` in WiX

---

Last updated: 2026-02-05
