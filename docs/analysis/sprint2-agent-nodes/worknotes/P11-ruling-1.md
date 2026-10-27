# P11 ruling 1: missing state, state-file trust, and review notes D2-D4

Inputs:

- `.claude/reports/sprint2-agent-nodes/P11-review-1.md` (verdict `decision`: N1, D1-D5);
- `.claude/reports/sprint2-agent-nodes/P11-security-1.md` (verdict `fix`: N1-N4 and "Not fixed");
- `.claude/reports/sprint2-agent-nodes/P11-coder.md`;
- the P11 worktree at `177f66d`;
- C7, P11, P12, P13 and P14 in [work-packets.md](../work-packets.md);
- D-S213, D-S215, D-S218, F10.

All report paths are in the P11 worktree, `.claude/worktrees/sprint2-agent-nodes-p11`.

## Ruling

| Note | Ruling |
| --- | --- |
| Review D1 (`Success(null)` impossible) | **Option (b), refined. Decision D-S232.** `LoadAsync` returns `Result<StoredAgentState>`, a non-null wrapper whose `Identity` is null on first run. The same fix applies to `IEnrollmentTokenSource`, which had the same latent defect: it now returns `Result<string>`, with `string.Empty` meaning that no token is configured. Option (a) is rejected because it makes a caller match on an error string. Option (c) is rejected because it changes the shared `Result<T>`, which affects every service. |
| Security N1-N4 | **All accepted, folded into the P11 fix pass. Decision D-S233.** <ul><li>The state file is created only through a new `IStateFileSecurity` seam (`CreateNew`, write-through, owner-only), and a load checks that the file can be trusted.</li><li>`StateDirectory` must be a fully qualified local path, checked by one predicate.</li><li>`Set` also compares `OrganizationId`.</li><li>The Windows implementation of the seam (a protected DACL, plus an owner and ACE check on load) is the new fix packet **P27f**, specified below.</li><li>P12 gets amendment **A2**: it binds the loaded identity to the certificate's SPIFFE SAN and fails closed on every load failure.</li></ul> |
| Review N1 | Accepted. Add `AgentOptionsValidationTests`, with the review's cases (a) to (f) plus the path cases from security N3. |
| Review D2 | The `security-review` label goes on the single campaign PR (D-S215). No per-packet PR exists. |
| Review D3 | **Confirmed.** P11 and P12 stay in wave 2, merged back to back. See "Merge order" below. |
| Review D4 | The test's 10 s timeout is accepted, and the packet text now says 10 s. The coder's commit subject is accepted as written. The fix-pass subject is given below. |

Open owner decisions: **none**.

## Why

| Fact | Evidence |
| --- | --- |
| `Result<T>.Success(null)` throws `InvalidOperationException("A successful result must have a value.")`. | `src/Shared/Dhadgar.Shared/Results/Result.cs:150-155` |
| C7 specifies `Success(null)` in two places: `IAgentStateStore.LoadAsync` and `IEnrollmentTokenSource.GetToken`. P12 step 1, P13 step 5 and P14 step 3 all rely on it. | `work-packets.md` C7, P12, P13, P14 (before this ruling) |
| The coder's workaround, `Failure("[AgentState.Missing]")`, means P12 must tell "first run" apart from "corrupt" by matching a prefix in the error string. If P12 gets that wrong in the lax direction, a corrupt state file triggers a silent re-enroll. That breaks a rule P11 states outright, and the security advisor raised it too. | `P11-coder.md` deviation 1; `P11-security-1.md` Not fixed, bullet 4 |
| `LoadAsync` checks `File.Exists` before opening. `File.Exists` returns false on access denied, so an unreadable file is treated as "first run". | `FileAgentStateStore.cs:55` |
| When the agent creates the default `%ProgramData%\Meridian Console\Agent` itself (process mode, or a service created with `sc.exe`), the directory inherits the ProgramData ACL. Under that ACL, Users can create files and CREATOR OWNER gets full control. Only the MSI sets a SYSTEM and Administrators ACL on the folder. | `Package.wxs:111-170`; `P11-security-1.md` N1 |
| The temp file is opened with `FileMode.Create`, so a temp file that someone else planted is adopted. `FlushAsync` does not reach the disk. `Path.IsPathRooted` accepts `\foo`, `C:foo` and UNC paths. | `FileAgentStateStore.cs:124-137`, `:168`; `AgentOptions.cs:73` |
| `Set` compares only `NodeId`. | `AgentIdentityProvider.cs:42` |
| No production code consumes `LoadAsync` yet. Only the tests and the DI registration reference it, so changing its type now costs nothing. | Grep over the P11 worktree |

1. **Why a typed wrapper.**
   - The three outcomes are a first run, a valid identity, and "do not trust this, stop". With the wrapper, the first two are success values and the third is a `Failure`.
   - The P12 rule then becomes structural: enroll only when `Success` holds and `Identity` is null; never enroll on a `Failure`.
   - No error-code string is part of the contract.
   - The wrapper is local to `Dhadgar.Agent.Core`. C7 interfaces are agent-internal, so `Dhadgar.Contracts` is not involved.
2. **Why the Windows file ACL lives in `Agent.Windows`.**
   - D-S218 keeps Core platform-neutral, and the Windows host already owns the platform services (`ICertificateStore`, `DirectoryAclManager`).
   - The fix locks the **file**, not the directory. The default state directory is also the parent of `Servers` and `Temp`, and the MSI already sets the folder's ACL.
   - A user who can create files in the folder still cannot replace or delete a file they do not own. A file planted before first run fails `VerifyTrusted`.
3. **Why it is safe to land the Core half before P27f.** Nothing is pushed until the campaign PR (D-S215), and P27f is inside the wave 2 gate. On Windows, the Core default (`PortableStateFileSecurity`) creates the file with `CreateNew` and write-through, but with an inherited ACL. That interim state never ships.
4. **Org integrity.**
   - The certificate does not carry `OrganizationId`.
   - On the server side, D-S23 means the org is never taken from the agent, so the org in the state file only drives the agent's own command gates.
   - The file's integrity rests on D-S233, and the `NodeId` is bound to the certificate by P12 A2.
   - F10 (the fail-open NodeId gates in `CommandValidator` and `ControlPlaneClient`) stays a follow-up for the commands campaign. It is unchanged here.

## What P11 must do now (fix pass 1, same worktree and branch)

Worktree `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p11`, branch `s2/p11-agent-identity`, head `177f66d`. Edit only the files named in this section.

**Rules:**

- **R-RESULT:** validation, IO and trust problems return `Result`, never throw.
- **R-PATH:** keep the `IPathValidator` call.
- **R-USING:** `using` or `await using` on every stream; the caller owns a `FileStream` returned in a `Result`.
- **R-NULL:** nullable is enabled.
- **R-XUNIT:** xunit 2.9.3 in `Dhadgar.Agent.Core.Tests`, with no `Assert.Skip`. A platform-only test starts with `if (OperatingSystem.IsWindows()) return; // Unix-only: …`.
- **R-LOG:** log codes, exception types and lengths, never file content.
- **CA1416 is active in Core.** Every `UnixFileMode` API goes behind an `OperatingSystem.IsWindows()` branch.
- **No package versions in `.csproj`.**

### Step 1. `StoredAgentState` (new file)

`src/Agents/Dhadgar.Agent.Core/Identity/StoredAgentState.cs`:

```csharp
namespace Dhadgar.Agent.Core.Identity;

/// <summary>
/// What the state store holds. <see cref="Identity"/> is null when no state file exists (first run).
/// A non-null wrapper, because the shared Result&lt;T&gt; cannot carry a successful null (D-S232).
/// </summary>
public sealed record StoredAgentState(AgentIdentity? Identity)
{
    /// <summary>No state file exists: the agent has never been enrolled on this machine.</summary>
    public static StoredAgentState None { get; } = new(Identity: null);
}
```

### Step 2. `IAgentStateStore`

Replace the `LoadAsync` member and its doc comment in `Identity/IAgentStateStore.cs`:

```csharp
/// <summary>
/// Loads the persisted state.
/// </summary>
/// <returns>
/// Success(<see cref="StoredAgentState.None"/>) only when the state file does not exist;
/// Success with an identity when the file is valid and trusted;
/// Failure ([AgentState.Corrupt], [AgentState.Io], [AgentState.Config] or [AgentState.Untrusted]) otherwise.
/// Callers enroll only on None and never on a Failure (D-S232).
/// </returns>
Task<Result<StoredAgentState>> LoadAsync(CancellationToken cancellationToken = default);
```

`SaveAsync` is unchanged.

### Step 3. `IStateFileSecurity` (new file)

`src/Agents/Dhadgar.Agent.Core/Identity/IStateFileSecurity.cs`:

```csharp
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Identity;

/// <summary>
/// Creates and checks the agent state file with platform-specific protection (D-S233).
/// Core registers <see cref="PortableStateFileSecurity"/>; the Windows host registers its own before AddAgentCore.
/// </summary>
public interface IStateFileSecurity
{
    /// <summary>
    /// Creates <paramref name="path"/> with FileMode.CreateNew (never opens an existing file), write-through,
    /// accessible only to the agent's account (and SYSTEM and Administrators on Windows). The caller disposes the stream.
    /// </summary>
    Result<FileStream> CreateNew(string path);

    /// <summary>
    /// Success when the existing file at <paramref name="path"/> may be trusted; Failure("[AgentState.Untrusted] …") when
    /// someone other than the agent could have written it; Failure("[AgentState.Io] …") when it cannot be inspected.
    /// </summary>
    Result VerifyTrusted(string path);
}
```

### Step 4. `PortableStateFileSecurity` (new file)

`src/Agents/Dhadgar.Agent.Core/Identity/PortableStateFileSecurity.cs`:

```csharp
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Identity;

/// <summary>
/// Default state-file protection. Unix: file 0600, and a load refuses a group- or world-writable file.
/// Windows: CreateNew and write-through only; the Windows host replaces this with WindowsStateFileSecurity (P27f).
/// The file owner is not checked on Unix (Linux follow-up, D-S218).
/// </summary>
public sealed class PortableStateFileSecurity : IStateFileSecurity
{
    /// <inheritdoc />
    public Result<FileStream> CreateNew(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = System.IO.FileOptions.Asynchronous | System.IO.FileOptions.WriteThrough,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        FileStream? stream = null;
        try
        {
            stream = new FileStream(path, options);
            var created = Result<FileStream>.Success(stream);
            stream = null; // ownership passes to the caller
            return created;
        }
        catch (IOException ex)
        {
            return Result<FileStream>.Failure($"[AgentState.Io] Failed to create state file ({ex.GetType().Name})");
        }
        catch (UnauthorizedAccessException)
        {
            return Result<FileStream>.Failure("[AgentState.Io] Access denied creating state file");
        }
        finally
        {
            stream?.Dispose();
        }
    }

    /// <inheritdoc />
    public Result VerifyTrusted(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (OperatingSystem.IsWindows())
        {
            return Result.Success(); // the Windows host registers WindowsStateFileSecurity (P27f)
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) == 0
                ? Result.Success()
                : Result.Failure("[AgentState.Untrusted] State file is writable by group or others");
        }
        catch (IOException ex)
        {
            return Result.Failure($"[AgentState.Io] Failed to inspect state file ({ex.GetType().Name})");
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure("[AgentState.Io] Access denied inspecting state file");
        }
    }
}
```

Use `System.IO.FileOptions` everywhere, because `Dhadgar.Agent.Core.Configuration.FileOptions` exists. Do not add a `using` for that namespace in this file.

### Step 5. `AgentOptions`: one path predicate

In `Configuration/AgentOptions.cs`:

1. Add this predicate:

   ```csharp
   /// <summary>
   /// Null when <paramref name="directory"/> is an acceptable state directory; otherwise the reason it is not.
   /// Used by Validate and by FileAgentStateStore so both apply the same rule (D-S233).
   /// </summary>
   internal static string? CheckStateDirectory(string directory)
   {
       if (!Path.IsPathFullyQualified(directory))
       {
           return "StateDirectory must be a fully qualified local path";
       }

       // UNC (\\server\share, //server/share) and device (\\?\, \\.\) paths: the agent's machine account would authenticate to them.
       if (directory.StartsWith(@"\\", StringComparison.Ordinal) || directory.StartsWith("//", StringComparison.Ordinal))
       {
           return "StateDirectory must not be a UNC or device path";
       }

       if (directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
       {
           return "StateDirectory must not contain '..' segments";
       }

       return null;
   }
   ```

2. Replace the `StateDirectory` block in `Validate` (`:73-78`) with:

   ```csharp
   if (!string.IsNullOrWhiteSpace(StateDirectory) && CheckStateDirectory(StateDirectory) is { } stateDirectoryError)
   {
       yield return new ValidationResult(stateDirectoryError, [nameof(StateDirectory)]);
   }
   ```

   Leave the NodeId and OrganizationId rule unchanged.

### Step 6. `FileAgentStateStore`

The full method bodies are below; keep the rest of the file.

- Constructor: add `IStateFileSecurity fileSecurity` as the third parameter, after `pathValidator`. Store it in `_fileSecurity`, with the same `ArgumentNullException` guard as the others.
- Replace `LoadAsync`:

```csharp
/// <inheritdoc />
public async Task<Result<StoredAgentState>> LoadAsync(CancellationToken cancellationToken = default)
{
    var pathResult = ResolveStatePath();
    if (pathResult.IsFailure)
    {
        return Result<StoredAgentState>.Failure(pathResult.Error);
    }

    var path = pathResult.Value!;

    try
    {
        // Open directly. File.Exists reports false on access denied, which would turn an unreadable file into "first run".
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, System.IO.FileOptions.Asynchronous);

        var trusted = _fileSecurity.VerifyTrusted(path);
        if (trusted.IsFailure)
        {
            _logger.LogCritical("Agent state file is not trusted: {Error}", trusted.Error);
            return Result<StoredAgentState>.Failure(trusted.Error);
        }

        if (stream.Length > MaxFileBytes)
        {
            _logger.LogWarning("Agent state file is larger than the {Max} byte limit", MaxFileBytes);
            return Result<StoredAgentState>.Failure("[AgentState.Corrupt] State file exceeds the size limit");
        }

        var bytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);

        var state = JsonSerializer.Deserialize<AgentStateFile>(bytes, JsonOptions);
        if (state is null || state.NodeId == Guid.Empty || state.OrganizationId == Guid.Empty)
        {
            _logger.LogWarning("Agent state file is missing a node or organization ID");
            return Result<StoredAgentState>.Failure("[AgentState.Corrupt] State file does not hold a valid identity");
        }

        return Result<StoredAgentState>.Success(
            new StoredAgentState(new AgentIdentity(state.NodeId, state.OrganizationId, state.EnrolledAt)));
    }
    catch (FileNotFoundException)
    {
        return Result<StoredAgentState>.Success(StoredAgentState.None);
    }
    catch (DirectoryNotFoundException)
    {
        return Result<StoredAgentState>.Success(StoredAgentState.None);
    }
    catch (JsonException ex)
    {
        _logger.LogWarning(ex, "Agent state file is not valid JSON");
        return Result<StoredAgentState>.Failure("[AgentState.Corrupt] State file is not valid JSON");
    }
    catch (IOException ex)
    {
        _logger.LogError(ex, "Failed to read agent state file");
        return Result<StoredAgentState>.Failure("[AgentState.Io] Failed to read agent state file");
    }
    catch (UnauthorizedAccessException ex)
    {
        _logger.LogError(ex, "Access denied reading agent state file");
        return Result<StoredAgentState>.Failure("[AgentState.Io] Access denied reading agent state file");
    }
}
```

`FileNotFoundException` and `DirectoryNotFoundException` derive from `IOException`, so they must stay above it.

- Replace the `try` block of `SaveAsync`. Its catches and `finally` are unchanged.

```csharp
try
{
    var directory = Path.GetDirectoryName(finalPath)!;
    if (OperatingSystem.IsWindows())
    {
        Directory.CreateDirectory(directory);
    }
    else
    {
        // 0700 when the agent creates it; an existing directory keeps its mode
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    // A stale or planted temp file is removed, never adopted (CreateNew below fails if one reappears).
    TryDeleteTemp(tempPath);

    var json = JsonSerializer.SerializeToUtf8Bytes(
        new AgentStateFile(identity.NodeId, identity.OrganizationId, identity.EnrolledAt), JsonOptions);

    var created = _fileSecurity.CreateNew(tempPath);
    if (created.IsFailure)
    {
        _logger.LogError("Failed to create agent state temp file: {Error}", created.Error);
        return Result.Failure(created.Error);
    }

    await using (var stream = created.Value!)
    {
        await stream.WriteAsync(json, cancellationToken);
        stream.Flush(flushToDisk: true); // durable before the rename; FlushAsync stops at the OS cache
    }

    File.Move(tempPath, finalPath, overwrite: true);
    return Result.Success();
}
```

If CA1849 fires on `Flush(flushToDisk: true)`, suppress that one line:

```csharp
#pragma warning disable CA1849 // Flush(bool) has no async overload; FlushAsync does not reach the disk
stream.Flush(flushToDisk: true);
#pragma warning restore CA1849
```

Do not replace it with `FlushAsync`.

- Replace `ResolveStatePath`:

```csharp
private Result<string> ResolveStatePath()
{
    var directory = _options.Value.StateDirectory;
    if (string.IsNullOrWhiteSpace(directory))
    {
        return Result<string>.Failure("[AgentState.Config] StateDirectory is not configured");
    }

    if (AgentOptions.CheckStateDirectory(directory) is { } error)
    {
        return Result<string>.Failure("[AgentState.Config] " + error);
    }

    var candidate = Path.Combine(directory, FileName);
    return _pathValidator.ValidatePath(candidate, [directory]);
}
```

- `TryDeleteTemp` is unchanged. It is now called both before the write and in `finally`.

### Step 7. `AgentIdentityProvider.Set`: compare the organization too

In `Identity/AgentIdentityProvider.cs`, replace the comparison at `:42-44`:

```csharp
return _current.NodeId == identity.NodeId && _current.OrganizationId == identity.OrganizationId
    ? Result.Success()
    : Result.Failure("[AgentIdentity.Changed] restart required");
```

In `Identity/IAgentIdentityProvider.cs`, the `Set` doc becomes:

```csharp
/// <summary>
/// Sets the identity. Setting the same NodeId and OrganizationId again succeeds (EnrolledAt is ignored);
/// a different NodeId or OrganizationId fails with [AgentIdentity.Changed] (an identity change needs a restart).
/// </summary>
```

### Step 8. DI

In `Hosting/AgentServiceCollectionExtensions.cs`, add this beside the `IAgentStateStore` registration (`:51`):

```csharp
services.TryAddSingleton<IStateFileSecurity, PortableStateFileSecurity>();
```

Then grep `src/` and `tests/` for `new FileAgentStateStore(` and add the new argument at every site.

### Step 9. Tests

**`tests/Dhadgar.Agent.Core.Tests/Identity/FileAgentStateStoreTests.cs`:**

- Change `CreateStore` and `Load_ValidatorRejection_ReturnsFailure` to pass `new PortableStateFileSecurity()` as the third argument.
- `SaveThenLoad_RoundTripsTheIdentity`: assert `Assert.Equal(identity, load.Value!.Identity)`.
- Replace `Load_MissingFile_ReturnsMissingFailure` with two tests:
  - `Load_MissingFile_ReturnsNone`: `Directory.CreateDirectory(_directory)`, then load. Assert `load.IsSuccess` and `Assert.Null(load.Value!.Identity)`.
  - `Load_MissingDirectory_ReturnsNone`: same, but without creating the directory.
- Add the following tests:

| Test | Arrange and assert | Fails on revert because |
| --- | --- | --- |
| `Load_StatePathIsDirectory_ReturnsIoFailure` | `Directory.CreateDirectory(StatePath())`; load. Assert `IsFailure` and `Contains("[AgentState.Io]")`. | A `File.Exists` precheck returns false for a directory and would give None, so an unreadable state would mean "enroll". |
| `Save_StaleTempFile_IsReplacedNotAdopted` | Create `_directory`, then `File.WriteAllTextAsync(StatePath() + ".tmp", "planted")`; save an identity. Assert the save succeeded, no `.tmp` is left, and a load returns that identity. | Without the `TryDeleteTemp` before the write, `CreateNew` fails on the planted file. |
| `Save_TempPathIsDirectory_FailsWithResult` | `Directory.CreateDirectory(StatePath() + ".tmp")`; save. Assert `IsFailure`, `Contains("[AgentState.Io]")`, and that `File.Exists(StatePath())` is false. | Pins that a create failure is a `Result`, not an exception. |
| `Load_StateDirectoryNotFullyQualifiedLocal_ReturnsConfigFailure` (`[Theory]`) | `[InlineData(@"\foo")]`, `[InlineData("C:foo")]`, `[InlineData(@"\\server\share\x")]`, `[InlineData(@"\\?\C:\x")]`, `[InlineData("//server/share/x")]`, `[InlineData("relative/dir")]`. `CreateStore(dir).LoadAsync()`. Assert `IsFailure` and `Contains("[AgentState.Config]")`. | Under `IsPathRooted`, `\foo` and the UNC forms pass on Windows (security N3). `//server/share/x` covers the Unix side. |
| `Save_OnUnix_FileIs0600AndDirectory0700` | Unix only. Save into a fresh `_directory`. Assert `File.GetUnixFileMode(StatePath()) == (UserRead \| UserWrite)` and `File.GetUnixFileMode(_directory) == (UserRead \| UserWrite \| UserExecute)`. | Under the umask the file would be 0644 and the directory 0755. |
| `Load_OnUnix_GroupWritableFile_ReturnsUntrusted` | Unix only. Save; `File.SetUnixFileMode(StatePath(), UserRead \| UserWrite \| GroupWrite)`; load. Assert `Contains("[AgentState.Untrusted]")`. | Fails if `VerifyTrusted` is not called on load. |

- `Load_StateDirectoryWithParentSegments_ReturnsFailure` stays as it is. The message still contains `'..'`.
- Unix-only tests start with `if (OperatingSystem.IsWindows()) return; // Unix-only: Windows file protection is WindowsStateFileSecurity (P27f)`. Call `File.GetUnixFileMode` and `File.SetUnixFileMode` only after that guard, so CA1416 accepts them.

**New `tests/Dhadgar.Agent.Core.Tests/Identity/PortableStateFileSecurityTests.cs`** (temp directory, `IDisposable` cleanup):

- `CreateNew_ExistingFile_Fails`: write a file, then call `CreateNew` on it. Assert `IsFailure` and `Contains("[AgentState.Io]")`. Revert check: with `FileMode.Create` the existing file is opened and truncated, so the call succeeds. This test pins security N1 (b).
- `CreateNew_NewFile_ReturnsWritableStream`: `using` the stream, write one byte, then assert that the file exists with length 1.

**`tests/Dhadgar.Agent.Core.Tests/Identity/AgentIdentityProviderTests.cs`:**

- Add `Set_SameNodeDifferentOrganization_Fails`: `Set(new(node, orgA, t))`, then `Set(new(node, orgB, t))` gives `IsFailure` and contains `[AgentIdentity.Changed]`, and `Current.OrganizationId` is still `orgA`. Revert check: under the NodeId-only comparison the second call succeeds.

**New `tests/Dhadgar.Agent.Core.Tests/Configuration/AgentOptionsValidationTests.cs`** (review N1). The namespace is `Dhadgar.Agent.Core.Tests.Configuration`. Add a helper:

```csharp
private static List<ValidationResult> Validate(AgentOptions options) =>
    options.Validate(new ValidationContext(options)).ToList();
```

| Test | Options | Expect |
| --- | --- | --- |
| `NodeIdWithoutOrganization_Fails` | `NodeId = Guid.NewGuid()` | One result, `MemberNames` equal to `["NodeId", "OrganizationId"]` |
| `OrganizationWithoutNodeId_Fails` | `OrganizationId = Guid.NewGuid()` | The same |
| `BothIdsWithDefaultSecurity_Passes` | Both IDs set, `Security = new SecurityOptions()` | Empty. Fails if the old "NodeId needs certificate config" rule returns. |
| `NeitherId_Passes` | Defaults | Empty |
| `RelativeStateDirectory_Fails` | `StateDirectory = "relative/dir"` | One result, `MemberNames` equal to `["StateDirectory"]` |
| `FullyQualifiedStateDirectory_Passes` | `StateDirectory = Path.Combine(Path.GetTempPath(), "x")` | Empty |
| `StateDirectory_NotLocalOrNotQualified_Fails` (`[Theory]`) | `@"\foo"`, `"C:foo"`, `@"\\server\share\x"`, `@"\\?\C:\x"`, `"//server/share/x"`, `Path.Combine(Path.GetTempPath(), "..", "x")` | One result naming `StateDirectory` |

### Step 10. Docs

In `src/Agents/Dhadgar.Agent.Core/README.md`, "Identity and state" (`:433-439`), replace the **File**, **Writes**, **Missing file** and **Changing identity** bullets, and add the **Reads** and **Recovery** bullets shown, so that the section reads:

```markdown
- **File:** `agent-state.json` in `AgentOptions.StateDirectory`. The default is `%ProgramData%\Meridian Console\Agent`. `StateDirectory` must be a fully qualified local path: not relative, drive-relative (`C:foo`), rooted without a drive (`\foo`), UNC or device (`\\…`), and with no `..` segments. A bad value is `[AgentState.Config]`. The file path is also checked with `IPathValidator` against that directory.
- **Writes:** any stale `agent-state.json.tmp` is deleted, the temp file is created new (never reused) through `IStateFileSecurity` with write-through and flushed to disk, then moved over the final file. On Unix the file is 0600 and a directory the agent creates is 0700. On Windows the host's `WindowsStateFileSecurity` gives the file a protected ACL (SYSTEM, Administrators and the agent's account only).
- **Reads:** capped at 64 KB. A file that someone else could have written is `[AgentState.Untrusted]` (Unix: group or world writable; Windows: untrusted owner or write access).
- **Missing file:** `Success(StoredAgentState.None)`, which is a first run. This is the only outcome on which the agent enrolls (D-S232). Corrupt, unreadable, untrusted or misconfigured state is a `Failure` and is never re-enrolled over.
- **Changing identity:** setting the same `NodeId` and `OrganizationId` again succeeds. A different `NodeId` or `OrganizationId` gives `Failure("[AgentIdentity.Changed] restart required")`.
- **Recovery:** after a `Failure`, an operator removes `agent-state.json` and supplies a new enrollment token.
```

Keep the **Format** and **Corrupt file** bullets.

### Step 11. Checks (in order)

```powershell
dotnet build <wt>\src\Agents\Dhadgar.Agent.Core\Dhadgar.Agent.Core.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <wt>\src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Agent.Core.Tests\Dhadgar.Agent.Core.Tests.csproj
dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj
dotnet test <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj
```

The Nodes and AgentSeam tests need Docker. On Windows the two Unix-only tests return early, so report them as "ran (no-op on Windows)". They run for real on Linux CI.

### Step 12. Commit

Commit subject: `fix(agent-core): typed state load, hardened state file, org-change check (P11-ruling-1)`. This is a security-review area (agent project), so the orchestrator runs `packet-reviewer` and `security-advisor` on this pass. Stage only the files named in this note.

## Merge order and D3

- P11 → P12 stay serial on w1. P12 merges **directly after** P11, and no other packet merges into w1 between them.
- P12 branches from the w1 tip that contains P11.
- After P12, the order is {P13, P14} → **P27f**.
- Nothing is pushed before the campaign PR (D-S215), and the wave 2 gate needs P11, P12 and P27f, so the "P11 alone" state of review note D3 never ships.

## Added to P12: amendment A2 (D-S232, D-S233)

This is written into [work-packets.md](../work-packets.md) § P12 as Amendment A2. In summary:

- `ResolveIdentityAsync` step 1 changes:
  - Any `LoadAsync` `Failure` is logged Critical with its code and returns null. Never enroll on it.
  - `Success` with `Identity == null` is a first run and goes to step 4.
- Step 2 binds the identity to the certificate:
  - Take the certificate with `using var cert = _certificateStore.GetClientCertificate()`.
  - Use the identity only when the certificate's only URI SAN equals `AgentCertificateProfile.SpiffeId(identity.NodeId)`.
  - On a mismatch, log Critical `[AgentState.CertificateMismatch]` and return null.
  - The step 4 config seed requires the same match.
- The SAN check moves from `EnrollmentService.HasOnlySpiffeUri` (private) to `internal static class AgentCertificateIdentity` in `Authentication/AgentCertificateIdentity.cs`, which both callers use.
- `ConfigurationEnrollmentTokenSource` returns `Success(string.Empty)` for a blank value. The loop treats `string.IsNullOrWhiteSpace(token.Value)` as "no token".
- New tests:
  - in `AgentBootstrapServiceTests`: `LoadFailure_Io_DoesNotEnroll`, `LoadFailure_Untrusted_DoesNotEnroll`, `StoredIdentity_CertificateForOtherNode_DoesNotEnrollOrConnect` and `ConfigSeed_CertificateForOtherNode_DoesNotSeed`;
  - in `ConfigurationEnrollmentTokenSourceTests`: `Blank_ReturnsEmptyNotFailure` and `Value_IsTrimmed`.

## P27f: Windows state-file protection (fix packet)

- **Branch** `s2/p27f-windows-state-acl`.
- **Worktree** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p27f`, from the w1 tip after P14 merges.
- **Depends on** P11 (this fix pass) and P14 (both edit `Program.cs`).
- **Security review** yes (agent project, file ACLs).
- **Commit subject** `fix(agent-windows): protected ACL and trust check for agent-state.json (P27f, D-S233)`

**Rules:**

- R-RESULT, R-USING (`WindowsIdentity` and every stream), R-NULL, R-XUNIT, R-LOG (log SIDs, never file content).
- The project is `net10.0`. Mark the class `[SupportedOSPlatform("windows")]`, as `WindowsCertificateStore` is.
- No package versions in `.csproj`. `System.IO.FileSystem.AccessControl` and `System.Security.Principal.Windows` are in the shared framework, so add no package.

**Change**

1. New `src/Agents/Dhadgar.Agent.Windows/Security/WindowsStateFileSecurity.cs`:

   ```csharp
   using System.Runtime.Versioning;
   using System.Security.AccessControl;
   using System.Security.Principal;
   using Dhadgar.Agent.Core.Identity;
   using Dhadgar.Shared.Results;

   namespace Dhadgar.Agent.Windows.Security;

   /// <summary>
   /// agent-state.json gets a protected DACL (SYSTEM, Administrators, the agent's account; no inheritance),
   /// and a load trusts the file only when its owner is one of those and nobody else can write it (D-S233).
   /// </summary>
   [SupportedOSPlatform("windows")]
   public sealed class WindowsStateFileSecurity : IStateFileSecurity
   {
       private const FileSystemRights WriteLikeRights =
           FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes |
           FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.ChangePermissions |
           FileSystemRights.TakeOwnership;

       private const int GenericWriteOrAll = 0x40000000 | 0x10000000; // GENERIC_WRITE | GENERIC_ALL

       private readonly IReadOnlyList<SecurityIdentifier> _trusted;

       public WindowsStateFileSecurity()
       {
           using var current = WindowsIdentity.GetCurrent();
           var sids = new List<SecurityIdentifier>
           {
               new(WellKnownSidType.LocalSystemSid, null),
               new(WellKnownSidType.BuiltinAdministratorsSid, null),
           };
           if (current.User is { } user && !sids.Contains(user))
           {
               sids.Add(user);
           }

           _trusted = sids;
       }

       public Result<FileStream> CreateNew(string path)
       {
           ArgumentException.ThrowIfNullOrEmpty(path);

           var security = new FileSecurity();
           security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
           foreach (var sid in _trusted)
           {
               security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
           }

           FileStream? stream = null;
           try
           {
               stream = new FileInfo(path).Create(
                   FileMode.CreateNew,
                   FileSystemRights.Write | FileSystemRights.ReadPermissions | FileSystemRights.Synchronize,
                   FileShare.None,
                   4096,
                   FileOptions.Asynchronous | FileOptions.WriteThrough,
                   security);
               var created = Result<FileStream>.Success(stream);
               stream = null;
               return created;
           }
           catch (IOException ex)
           {
               return Result<FileStream>.Failure($"[AgentState.Io] Failed to create state file ({ex.GetType().Name})");
           }
           catch (UnauthorizedAccessException)
           {
               return Result<FileStream>.Failure("[AgentState.Io] Access denied creating state file");
           }
           finally
           {
               stream?.Dispose();
           }
       }

       public Result VerifyTrusted(string path)
       {
           ArgumentException.ThrowIfNullOrEmpty(path);
           try
           {
               var security = new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
               if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !_trusted.Contains(owner))
               {
                   return Result.Failure("[AgentState.Untrusted] State file is owned by an untrusted account");
               }

               foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
               {
                   if (rule.AccessControlType != AccessControlType.Allow)
                   {
                       continue;
                   }

                   var writes = (rule.FileSystemRights & WriteLikeRights) != 0 || ((int)rule.FileSystemRights & GenericWriteOrAll) != 0;
                   if (writes && (rule.IdentityReference is not SecurityIdentifier sid || !_trusted.Contains(sid)))
                   {
                       return Result.Failure("[AgentState.Untrusted] State file is writable by an untrusted account");
                   }
               }

               return Result.Success();
           }
           catch (IOException ex)
           {
               return Result.Failure($"[AgentState.Io] Failed to inspect state file ({ex.GetType().Name})");
           }
           catch (UnauthorizedAccessException)
           {
               return Result.Failure("[AgentState.Io] Access denied inspecting state file");
           }
       }
   }
   ```

   If the analyzers flag `FileOptions` (the Windows project may import a type with that name), qualify it as `System.IO.FileOptions`.

2. `src/Agents/Dhadgar.Agent.Windows/Program.cs`: add `builder.Services.AddSingleton<IStateFileSecurity, WindowsStateFileSecurity>();` immediately **before** `builder.Services.AddAgentCore();`, so that Core's `TryAdd` keeps it. Add the `using` lines it needs.

**Tests.** Add the new file `tests/Dhadgar.Agent.Windows.Tests/Security/WindowsStateFileSecurityTests.cs`. Each test starts with `if (!OperatingSystem.IsWindows()) return; // Windows-only: Windows ACLs` (the same style as `DirectoryAclManagerTests`). Use a temp directory that is cleaned up in `Dispose`.

| Test | Assert | Fails on revert because |
| --- | --- | --- |
| `CreateNew_FileHasProtectedDaclWithOnlyTrustedSids` | Create a file and dispose the stream. Then `GetAccessControl()`: `AreAccessRulesProtected` is true, and every rule's SID is S-1-5-18, S-1-5-32-544 or `WindowsIdentity.GetCurrent().User`. | Without the `FileSecurity`, the file inherits the temp directory's ACL (not protected). |
| `CreateNew_ExistingFile_Fails` | `IsFailure`, `[AgentState.Io]` | `FileMode.Create` adopts the file. |
| `VerifyTrusted_FileItCreated_Succeeds` | `IsSuccess` | - |
| `VerifyTrusted_UsersCanWrite_ReturnsUntrusted` | Add `FileSystemAccessRule(BuiltinUsersSid, Write, Allow)` with `SetAccessControl`, then expect `[AgentState.Untrusted]`. | A `VerifyTrusted` that always succeeds passes this file. |
| `StateStore_WithWindowsSecurity_RoundTrips` | A real `FileAgentStateStore` (`PathValidator`, this class): save then load returns the identity, and the final file's DACL is still protected after `File.Move`. | Pins that the rename keeps the ACL and that `Flush(true)` works with the requested rights. |

An owner that is not trusted cannot be tested without a second account. The UAT covers the wiring instead (P20 amendment A1).

**Checks (in order)**

```powershell
dotnet build <wt>\src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <wt>\tests\Dhadgar.Agent.Windows.Tests\Dhadgar.Agent.Windows.Tests.csproj
dotnet test <wt>\tests\Dhadgar.Agent.Core.Tests\Dhadgar.Agent.Core.Tests.csproj
```

**Docs owed.** In `src/Agents/Dhadgar.Agent.Windows/README.md`, add a "State file" paragraph to the section that P14 writes. It covers the protected ACL on `agent-state.json`, the `[AgentState.Untrusted]` failure and the recovery (remove the file, supply a new token).

## Added to P20: amendment A1 (U3 checks the state-file ACL)

U3 gains one condition: the DACL of `<runDir>\agent-state\agent-state.json` is protected, and every Allow ACE's SID is SYSTEM, Administrators or the agent's account. In process mode that is the elevated user; in service and MSI modes it is SYSTEM. Add a matching `AgentLegsTests` case: a fake ACL that has a Users write ACE fails U3 with that message. This is the published-agent check that `Program` registers `WindowsStateFileSecurity`.

## Owner decisions

None.
