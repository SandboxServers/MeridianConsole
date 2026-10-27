# P07 ruling 1 (after coder continuation 2 = blocked (spec))

P07 branch `s2/p07-gateway-seam`, at w1 tip `cdaa322`. Nothing is committed. Worktree `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p07` has `Dhadgar.sln` modified, `tests/Dhadgar.AgentSeam.Tests/` untracked, and the P07 worknote under `docs/analysis/`.

Inputs: `P07-coder.md` and `P07-coder-2.md` in that worktree's `.claude/reports/sprint2-agent-nodes/`. Code read on w1 (`cdaa322`).

## Finding

The coder's diagnosis is correct. The defect has been on `main` since `6da01b5` ("CodeRabbit review"), so this campaign did not introduce it. The P07 seam test is the first test that exposes it reliably.

- `src/Dhadgar.Nodes/Program.cs:68` registers `CertificateAuthorityService` as a **singleton**. Its constructor (`CertificateAuthorityService.cs:25-37`) takes the **scoped** `NodesDbContext`, so the root provider captures a single context for the life of the process.
- `ValidateCertificateAsync` (`:320-387`) runs on every mTLS request (through `CertificateValidationService` and `MtlsMiddleware`). Two overlapping agent requests share that one context. The SignalR long-poll GET plus the POST is enough, and in production any two agents connecting at once are enough. EF then throws `A second operation was started on this context instance`.
- The catch-all at `:382-386` turns that exception into `false`. `CertificateValidationService.cs:186-199` maps `false` to `invalid_issuer`, and `MtlsMiddleware` writes 401. A database fault therefore looks like a forged certificate. The agent backs off and retries on any status (`ControlPlaneClient.ConnectFailed`), so this shows up as random 401 storms under load, and an operator would chase a CA problem.
- Further harm from the captured context: it is never disposed, it holds a pooled connection for the life of the process, and `ValidateScopes` would reject the registration. In Development, `WebApplicationBuilder` turns on `ValidateScopes`/`ValidateOnBuild`, so `builder.Build()` should fail with "Cannot consume scoped service". The new lifetime test (step 7) shows whether it does.
- It explains the flaky `AgentHubTests`. Those tests use `AgentSeamHarness.CreateFactoryWithRealCa`, which registers the real CA singleton the same way.

**Other singletons holding scoped dependencies: none in Nodes product code.** I checked every singleton and middleware: `LocalFileCaStorageProvider` takes options and a logger. `TenantScopedHandler` takes `IHttpContextAccessor` and a logger. The three hosted services take `IServiceScopeFactory`. `MtlsMiddleware` and `ForwardedClientCertificateMiddleware` receive scoped services through `InvokeAsync`, not their constructors. `NodesOptions` as `IValidateOptions` has no dependencies. The scope-validation test in step 7 keeps it that way.

## Ruling

**Resolved. No owner decision.** A new fix packet **P17f** carries the fix. It branches from w1 `cdaa322` and merges into w1 **before** P07. P07 then resumes on the new tip without changing its tests.

| Option | Verdict |
| --- | --- |
| (A) Keep the CA singleton. Inject `IServiceScopeFactory`, and do the revocation lookup in a fresh scope and `NodesDbContext` per call | **Chosen.** The CA certificate and its private key stay cached once per process. Each lookup gets its own context, so concurrent calls cannot collide. It works with whatever `NodesDbContext` registration the host or a test factory provides (`NodesWebApplicationFactory` swaps the options for InMemory), and `Program.cs` needs no change. The same pattern is already used by `StaleNodeDetectionService`, `ReservationExpiryService` and `AuditLogCleanupService`. |
| (B) Register the CA service as scoped | Rejected. Each request would rebuild the instance and its `_initLock`, and re-read and re-import the CA PFX (private key) from disk. That means more key material in memory, file IO on every agent request, and a per-instance init race. Fixing that properly means splitting out a singleton CA holder, which is a bigger refactor. |
| (C) `IDbContextFactory<NodesDbContext>` | Rejected. `Program.cs` would need `AddDbContextFactory` with singleton options, and `NodesWebApplicationFactory` and every `WithWebHostBuilder` override would need to swap the factory as well as the options. Otherwise the factory keeps building Npgsql contexts in tests. More moving parts for the same result as (A). |

**Infrastructure errors must not be reported as `invalid_issuer` (fixed in P17f).** `ValidateCertificateAsync` returns a three-state `CertificateTrustStatus` (`Trusted`, `Untrusted`, `Unavailable`) instead of `bool`. This is the repo's no-exceptions-for-IO rule, with a status in place of a `Result`. The type is internal to Nodes, so it does not go in Contracts.
- `Untrusted` means bad PEM, chain or crypto failure, wrong issuer, or revoked. The response stays 401 `invalid_issuer`, unchanged.
- `Unavailable` means the CA could not be loaded or the revocation lookup threw. The response is **503** problem+json with `errorCode = "certificate_validation_unavailable"`. It still **fails closed**: the request is refused, but it is reported honestly.

The chain check now runs **before** the database lookup. It is pure CPU, so certificates from a foreign CA never cost a database round trip. Both checks still have to pass.

**Contract C5 amendment (orchestrator integrates into `work-packets.md` C5 and the README decisions under the next free D-S number):**
> "`MtlsMiddleware` returns **503** problem+json, `errorCode = "certificate_validation_unavailable"`, when certificate trust cannot be determined (CA not loaded, or the revocation lookup failed). It never returns 401 for an infrastructure fault. The agent treats it like any other connect failure (retry with backoff)."

The Gateway passes Nodes' status through, and the agent maps every non-success to `[ControlPlane.ConnectFailed]`, so no other component changes.

## P17f: Nodes CA uses a scope per lookup; trust lookup failures are 503, not 401

- **Branch** `s2/p17f-ca-scoped-db`. **Worktree** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p17f`, from the w1 tip `cdaa322`. **Depends on** nothing beyond w1. **Merges** into w1 before P07. **Security review** yes (mTLS validation and certificates): run `security-advisor` beside `packet-reviewer`.
- **Commit subject** `fix(nodes): give the CA a DbContext per lookup; report trust lookup failures as 503`
- **Size** about 35k tokens. 5 product files, 4 test files edited, 1 new test file, 1 README.

**Rules (inlined):**
- R-RESULT: no exceptions for IO or database failures. Return the status value. Only `OperationCanceledException` propagates.
- R-USING: `using` on every `IDisposable` (`X509Certificate2`, `X509Chain`, `ServiceProvider`). Use `await using` on `AsyncServiceScope`.
- R-LOG: never log the PEM or the whole certificate. Thumbprints and node IDs are fine. The 503 `detail` must not contain exception text.
- R-NULL: nullable is on, and Release builds with warnings as errors.
- R-CPM: no new packages and no `Version=` in any `.csproj`.
- R-XUNIT: xUnit v2 `[Fact]` and plain `Assert`. Host tests use `[Collection("Nodes Integration")]` and the `NodesWebApplicationFactory` fixture. Names follow `Subject_Condition_Expectation`.
- Edit only the files named below. Do not touch `docs/analysis/`, `docs/PROJECT-STATE.md`, `Dhadgar.sln` or `Program.cs`. The registration stays a singleton.

### Steps

1. **`src/Dhadgar.Nodes/Services/ICertificateAuthorityService.cs`:** add the status enum next to `CertificateIssuanceResult`, and change the method:

   ```csharp
   /// <summary>Outcome of <see cref="ICertificateAuthorityService.ValidateCertificateAsync"/>.</summary>
   public enum CertificateTrustStatus
   {
       /// <summary>Issued by this CA and not revoked.</summary>
       Trusted,
       /// <summary>Unreadable, not issued by this CA, or revoked.</summary>
       Untrusted,
       /// <summary>Trust could not be determined (CA not loaded, or the revocation lookup failed). Fail closed.</summary>
       Unavailable
   }
   ```

   ```csharp
   /// <summary>Validates a certificate against the CA and the revocation records.</summary>
   /// <param name="certificatePem">The certificate in PEM format.</param>
   /// <returns><see cref="CertificateTrustStatus.Unavailable"/> for infrastructure faults, never <see cref="CertificateTrustStatus.Untrusted"/>.</returns>
   Task<CertificateTrustStatus> ValidateCertificateAsync(string certificatePem, CancellationToken ct = default);
   ```

2. **`src/Dhadgar.Nodes/Services/CertificateAuthorityService.cs`:**
   - Field `private readonly Data.NodesDbContext _dbContext;` becomes `private readonly IServiceScopeFactory _scopeFactory;`. The constructor parameter `Data.NodesDbContext dbContext` becomes `IServiceScopeFactory scopeFactory`, in the same (last) position. Add a comment on the field: `// Singleton: never hold a NodesDbContext. Each lookup gets its own scope (P17f).`
   - Replace `ValidateCertificateAsync` (`:320-387`) with the code below, plus two private helpers. Keep the revoked check as the literal `agentCert is not null && agentCert.IsRevoked`, because P16 step 6 edits that expression.

   ```csharp
   public async Task<CertificateTrustStatus> ValidateCertificateAsync(string certificatePem, CancellationToken ct = default)
   {
       try
       {
           await EnsureInitializedAsync(ct);
       }
       catch (Exception ex) when (ex is not OperationCanceledException)
       {
           _logger.LogError(ex, "Certificate validation unavailable: the CA certificate could not be loaded");
           return CertificateTrustStatus.Unavailable;
       }

       X509Certificate2 certificate;
       try
       {
           certificate = X509Certificate2.CreateFromPem(certificatePem);
       }
       catch (Exception ex) when (ex is CryptographicException or ArgumentException)
       {
           _logger.LogWarning(ex, "Certificate validation failed: the certificate could not be parsed");
           return CertificateTrustStatus.Untrusted;
       }

       using (certificate)
       {
           // Chain first: pure CPU, so certificates from a foreign CA never reach the database.
           if (!IsIssuedByOurCa(certificate))
           {
               return CertificateTrustStatus.Untrusted;
           }

           var thumbprint = CalculateSha256Thumbprint(certificate);
           var (lookupSucceeded, agentCert) = await FindAgentCertificateAsync(thumbprint, ct);
           if (!lookupSucceeded)
           {
               return CertificateTrustStatus.Unavailable;
           }

           if (agentCert is not null && agentCert.IsRevoked)
           {
               // keep the existing LogWarning text and arguments (thumbprint, RevokedAt, RevocationReason)
               return CertificateTrustStatus.Untrusted;
           }

           return CertificateTrustStatus.Trusted;
       }
   }

   private bool IsIssuedByOurCa(X509Certificate2 certificate)
   {
       try
       {
           // move the existing X509Chain block here unchanged: NoCheck revocation,
           // AllowUnknownCertificateAuthority | IgnoreNotTimeValid, ExtraStore.Add(_caCertificate!),
           // Build, ChainElements.Count < 2 → false, issuer thumbprint == _caCertificate!.Thumbprint
       }
       catch (CryptographicException ex)
       {
           _logger.LogWarning(ex, "Certificate validation failed due to cryptographic error");
           return false;
       }
   }

   private async Task<(bool Succeeded, Data.Entities.AgentCertificate? Row)> FindAgentCertificateAsync(
       string thumbprint,
       CancellationToken ct)
   {
       try
       {
           await using var scope = _scopeFactory.CreateAsyncScope();
           var dbContext = scope.ServiceProvider.GetRequiredService<Data.NodesDbContext>();
           var row = await dbContext.AgentCertificates
               .AsNoTracking()
               .FirstOrDefaultAsync(c => c.Thumbprint == thumbprint, ct);
           return (true, row);
       }
       catch (Exception ex) when (ex is not OperationCanceledException)
       {
           // Infrastructure fault: report Unavailable (503), never Untrusted (401 invalid_issuer).
           _logger.LogError(ex, "Certificate revocation lookup failed for {Thumbprint}", thumbprint);
           return (false, null);
       }
   }
   ```

   - There is no other `_dbContext` use in the file. Grep to confirm. The issuance methods are unchanged.

3. **`src/Dhadgar.Nodes/Auth/CertificateValidationService.cs`:**
   - On `CertificateValidationResult`, add `public const string UnavailableErrorCode = "certificate_validation_unavailable";`.
   - At `:185-199`, replace the `bool` handling with:

   ```csharp
   var trust = await _caService.ValidateCertificateAsync(certPem, ct);
   if (trust == CertificateTrustStatus.Unavailable)
   {
       _logger.LogError(
           "Certificate trust could not be determined. Thumbprint: {Thumbprint}",
           certificate.GetCertHashString());

       return CertificateValidationResult.Fail(
           CertificateValidationResult.UnavailableErrorCode,
           "Certificate validation is temporarily unavailable",
           notAfter: certificate.NotAfter);
   }

   if (trust != CertificateTrustStatus.Trusted)
   {
       // existing invalid_issuer LogWarning and Fail(...) unchanged
   }
   ```

4. **`src/Dhadgar.Nodes/Auth/MtlsProblemWriter.cs`:** add the following. It uses the existing private `WriteAsync`.

   ```csharp
   private const string ServiceUnavailableType = "https://tools.ietf.org/html/rfc7231#section-6.6.4";

   /// <summary>Writes a 503 problem+json response with the given machine-readable error code.</summary>
   public static Task WriteServiceUnavailableAsync(HttpContext context, string errorCode, string errorMessage)
   {
       return WriteAsync(context, StatusCodes.Status503ServiceUnavailable, ServiceUnavailableType, "Service Unavailable",
           errorCode, errorMessage);
   }
   ```

5. **`src/Dhadgar.Nodes/Auth/MtlsMiddleware.cs` (`:111-120`):** inside `if (!validationResult.IsValid)`, after the existing `LogWarning`, add the branch below before the 401 write:

   ```csharp
   if (string.Equals(validationResult.ErrorCode, CertificateValidationResult.UnavailableErrorCode, StringComparison.Ordinal))
   {
       await MtlsProblemWriter.WriteServiceUnavailableAsync(context, validationResult.ErrorCode!,
           validationResult.ErrorMessage!);
       return;
   }
   ```

6. **Test doubles and existing tests (mechanical):**
   - `tests/Dhadgar.Nodes.Tests/TestCertificateAuthorityService.cs:120-124`: return `Task.FromResult(certificatePem.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal) ? CertificateTrustStatus.Trusted : CertificateTrustStatus.Untrusted)`.
   - `tests/Dhadgar.Nodes.Tests/MtlsMiddlewareTests.cs:574-580`: `.Returns(validatesTo ? CertificateTrustStatus.Trusted : CertificateTrustStatus.Untrusted)`.
   - `tests/Dhadgar.Nodes.Tests/CertificateAuthorityServiceTests.cs`:
     - Replace `CreateDbContext()` and the `_contexts` list with a `List<ServiceProvider> _providers`, disposed in `Dispose()`, and this helper:

       ```csharp
       private ServiceProvider CreateDbServices(Action<IServiceCollection>? configure = null)
       {
           var databaseName = Guid.NewGuid().ToString();
           var services = new ServiceCollection();
           services.AddDbContext<NodesDbContext>(o => o
               .UseInMemoryDatabase(databaseName)
               .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
           configure?.Invoke(services);
           var provider = services.BuildServiceProvider();
           _providers.Add(provider);
           return provider;
       }
       ```

     - `CreateService(ICaStorageProvider? storageProvider = null, ServiceProvider? dbServices = null)` passes `(dbServices ?? CreateDbServices()).GetRequiredService<IServiceScopeFactory>()` as the last constructor argument.
     - The two tests that pass `dbContext:` (`:457`, `:494`) now call `var dbServices = CreateDbServices();` and `CreateService(dbServices: dbServices)`, and seed through `using var scope = dbServices.CreateScope(); var dbContext = scope.ServiceProvider.GetRequiredService<NodesDbContext>();`.
     - The asserts at `:401`, `:421`, `:487` and `:522` become `Assert.Equal(CertificateTrustStatus.Trusted, …)` or `Assert.Equal(CertificateTrustStatus.Untrusted, …)`.
   - `tests/Dhadgar.Nodes.Tests/CertificateAuthorityPublicKeyIssuanceTests.cs:23-61`: the same swap. The `_dbContext` field becomes a `ServiceProvider` built like `CreateDbServices`, disposed in `Dispose()`, and its `IServiceScopeFactory` is passed to the constructor. If `_dbContext` is used elsewhere in the file, use it through a scope.
   - Grep `tests/` for `ValidateCertificateAsync(` and `new CertificateAuthorityService(` and fix any other hits the same way. Expected: none beyond the above.

7. **New tests (each must fail when the fix is reverted):**
   - **New file `tests/Dhadgar.Nodes.Tests/ServiceLifetimeTests.cs`**, `[Collection("Nodes Integration")]`. Copy the constructor, `_caDirectory` and `Dispose` pattern from `Hubs/AgentHubTests.cs:22-44`.
     - `NodesHost_WithScopeValidation_HasNoSingletonCapturingScopedServices`:

       ```csharp
       using var factory = AgentSeamHarness.CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true)
           .WithWebHostBuilder(b => b.UseDefaultServiceProvider(o =>
           {
               o.ValidateScopes = true;
               o.ValidateOnBuild = true;
           }));

       // Building the host runs ValidateOnBuild; resolving from the root provider runs ValidateScopes.
       var ca = factory.Services.GetRequiredService<ICertificateAuthorityService>();
       Assert.IsType<CertificateAuthorityService>(ca);
       ```

       Why it is red before the fix: the build throws "Cannot consume scoped service 'Dhadgar.Nodes.Data.NodesDbContext' from singleton 'Dhadgar.Nodes.Services.ICertificateAuthorityService'". This test is the deterministic guard for the whole defect class.
     - If `ValidateOnBuild` fails on a different registration:
       - In Nodes product code (`src/Dhadgar.Nodes`), that is another real captive dependency. Stop and report `blocked (spec)` with the exact message. Do not fix it unasked.
       - In third-party or test-factory registrations, drop `ValidateOnBuild`, keep `ValidateScopes = true`, also resolve `ICaStorageProvider` and `IEnumerable<IAuthorizationHandler>` from `factory.Services`, and record the reason in the report.
   - **`CertificateAuthorityServiceTests`:**
     - `ValidateCertificateAsync_ConcurrentCallsOnOneInstance_AllTrusted`: one service, one issued certificate, and a non-revoked row for its thumbprint seeded through a scope. Then:

       ```csharp
       var results = await Task.WhenAll(Enumerable.Range(0, 64)
           .Select(_ => Task.Run(() => service.ValidateCertificateAsync(pem))));
       Assert.All(results, r => Assert.Equal(CertificateTrustStatus.Trusted, r));
       ```

       This is the behaviour test for the reported symptom. Before the fix it fails most runs; after the fix it must be green every run.
     - `ValidateCertificateAsync_WhenRevocationLookupFails_ReturnsUnavailable`: `CreateDbServices(s => s.AddScoped<NodesDbContext>(_ => throw new InvalidOperationException("database unavailable")))`, then validate an issued certificate. Expect `Unavailable`. Today's code returns `false`, which is indistinguishable from untrusted.
     - `ValidateCertificateAsync_ForeignCertificate_DoesNotQueryDatabase`: use the same throwing registration, validate a foreign-CA certificate (reuse the helper from the existing `ReturnsFalseForForeignCertificate` test), and expect `Untrusted`. This pins the chain-first order.
   - **`MtlsMiddlewareTests`:**
     - `InvokeAsync_ValidationUnavailable_Returns503ProblemJson`: copy the `invalid_issuer` test at `:185-212`, but the validation service returns `CertificateValidationResult.Fail(CertificateValidationResult.UnavailableErrorCode, "x")`. Assert `next` is not called, the status is 503, and `context.Response.ContentType` starts with `application/problem+json`.
     - `ValidateClientCertificateAsync_CaUnavailable_ReturnsUnavailableNotInvalidIssuer`: `CreateService(caService: mock)`, where the mock returns `CertificateTrustStatus.Unavailable`, with a certificate from `CreateCertificateWithSpiffeId(Guid.NewGuid())`. Assert `ErrorCode == CertificateValidationResult.UnavailableErrorCode`.

8. **Docs:** in `src/Dhadgar.Nodes/README.md:534`, replace the sentence with: ``Errors on these paths are `application/problem+json`: 401 with an `errorCode` such as `certificate_required` or `invalid_issuer`, or 503 `certificate_validation_unavailable` when certificate trust cannot be determined (CA not loaded or revocation lookup failed). The CA service is a singleton and opens a new DI scope for each revocation lookup.``

9. **Checks, in order** (`<wt>` = the P17f worktree):

   ```powershell
   dotnet build <wt>\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj -c Release -p:TreatWarningsAsErrors=true
   dotnet build <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
   dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj
   dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj
   dotnet test <wt>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj
   ```

   All three full runs must be green. That is the acceptance check for the `AgentHubTests` flake. A failure in any run, including an `AgentHubTests` case, ends the session `blocked` with the test name and the first line of the message. No Docker is needed.

   **Revert checks** (temporary edits, restored after each, recorded in the report):
   1. Put the `NodesDbContext` constructor parameter back, and use it for the lookup instead of the scope. Expect `NodesHost_WithScopeValidation_…` to fail with the captive-dependency message. The unit tests will not compile, which is expected; run only `--filter FullyQualifiedName~ServiceLifetimeTests` after temporarily giving the unit-test helpers the context.
      - If that is too invasive, do this instead: create one scope in the constructor, reuse its context for every lookup, and expect `…ConcurrentCallsOnOneInstance_AllTrusted` to fail in at least 1 of 5 runs.
   2. Make the `FindAgentCertificateAsync` catch return `(true, null)`. Expect `…WhenRevocationLookupFails_ReturnsUnavailable` to fail.
   3. Remove the 503 branch in `MtlsMiddleware`. Expect `InvokeAsync_ValidationUnavailable_Returns503ProblemJson` to fail.

10. **Commit** locally on `s2/p17f-ca-scoped-db`, one commit, no push. Stage explicit paths only:
    - `src/Dhadgar.Nodes/Services/ICertificateAuthorityService.cs`
    - `src/Dhadgar.Nodes/Services/CertificateAuthorityService.cs`
    - `src/Dhadgar.Nodes/Auth/CertificateValidationService.cs`
    - `src/Dhadgar.Nodes/Auth/MtlsProblemWriter.cs`
    - `src/Dhadgar.Nodes/Auth/MtlsMiddleware.cs`
    - `src/Dhadgar.Nodes/README.md`
    - `tests/Dhadgar.Nodes.Tests/TestCertificateAuthorityService.cs`
    - `tests/Dhadgar.Nodes.Tests/MtlsMiddlewareTests.cs`
    - `tests/Dhadgar.Nodes.Tests/CertificateAuthorityServiceTests.cs`
    - `tests/Dhadgar.Nodes.Tests/CertificateAuthorityPublicKeyIssuanceTests.cs`
    - `tests/Dhadgar.Nodes.Tests/ServiceLifetimeTests.cs`

    The body names the pre-existing defect: "Singleton CertificateAuthorityService captured a scoped NodesDbContext (since 6da01b5); concurrent mTLS requests collided and the catch-all reported the EF exception as invalid_issuer (401)." End with the campaign's two trailer lines.

**Security-advisor focus:**
- Every new path still fails closed: `Unavailable` never sets a principal.
- The chain-first reorder does not let any certificate skip the revocation check.
- No exception text reaches the 503 body.
- Nothing sensitive is logged.

## How P07 continues

1. After P17f reaches `ship` (reviewer and security-advisor), the Sonnet `general-purpose` merge agent merges `s2/p17f-ca-scoped-db` into `campaign/sprint2-agent-nodes-w1` and records the new tip.
2. The same merge agent moves P07 onto that tip **without touching its uncommitted files**. P07 has no commits of its own and P17f does not touch `Dhadgar.sln` or `tests/Dhadgar.AgentSeam.Tests/`, so a fast-forward carries the dirty tree:

   ```powershell
   git -C C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p07 merge --ff-only campaign/sprint2-agent-nodes-w1
   git -C C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p07 rev-parse HEAD
   ```

   `HEAD` must equal the new w1 tip. If the fast-forward refuses because of overlapping paths, stop and report. Do not stash, reset or discard anything.
3. One Sonnet ruling-apply session (`packet-coder`) finishes P07 in its worktree:
   - **No test changes.** `Hub_SpoofedHeaderAlongsideRealCert_UsesTlsCert` stays exactly as written. It is now the Gateway-path regression guard for this defect.
   - Run the P07 checks in the packet's order. Then run `dotnet test <wt>\tests\Dhadgar.AgentSeam.Tests\Dhadgar.AgentSeam.Tests.csproj` **5 times in a row**: all 4 tests green every time. Any red run means `blocked`, with the run number and the first failure line.
   - **Clarification of "production code is not changed":** the P07 revert checks may edit product files temporarily (P06 route, the transform, P03 forwarded-certificate middleware). Restore each one, and before committing confirm that `git status --porcelain src/` is empty. Record the three revert results.
   - The `[Collection("Nodes Integration")]` deviation stays accepted (separate assembly, per-test factories).
   - Commit with the packet's subject, staging `Dhadgar.sln` and the four `tests/Dhadgar.AgentSeam.Tests/` files explicitly.
4. Then the normal P07 review loop runs (`packet-reviewer` + `security-advisor`, pass 1), followed by the merge into w1.

## Ledger follow-ups (orchestrator, not a coder)

- `work-packets.md`:
  - Add P17f to wave 1, with the merge order `… P05, P17f, P07`.
  - Amend C5 with the 503 `certificate_validation_unavailable` row.
- `README.md`:
  - Add a decision (next free D-S number): "The CA service stays a singleton and opens a DI scope per revocation lookup. Trust lookup failures are 503 `certificate_validation_unavailable`, never 401 `invalid_issuer`." Cite this ruling.
  - Add the finding: the defect has been on main since `6da01b5`.
- **P16 step 6** (`work-packets.md` ~:1344): the predicate now sits in the revocation check after `FindAgentCertificateAsync` inside `ValidateCertificateAsync`. The expression `agentCert is not null && agentCert.IsRevoked` is unchanged, so only the line reference changes.
- **P16 test** `Decommission_EndsRenewalGrace` (~:1384): "`ValidateCertificateAsync` false" becomes "`ValidateCertificateAsync` returns `Untrusted`".
- **P09 step 3** (~:907) edits `ICertificateAuthorityService.cs` too. The new enum sits beside `CertificateIssuanceResult`, so P09 must keep it when it removes the renewal members.
- **P15:** in `PROJECT-STATE.md`, Nodes mTLS may say "confirmed working under concurrent agent requests" only with the P17f lifetime and concurrency tests and the P07 seam test green.
