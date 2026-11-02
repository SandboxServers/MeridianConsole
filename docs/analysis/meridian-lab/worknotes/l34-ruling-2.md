# L34 ruling 2: Identity crashes on its Key Vault certificate load (Docker run 13)

Architect ruling, 2026-11-02. Input: [wave5-docker-run-13.md](../../../../.claude/reports/meridian-lab/wave5-docker-run-13.md) (campaign worktree `.claude/reports/meridian-lab/`), TRX files and console logs in `%TEMP%\docker-run-13\`, [docker-run-12-ruling.md](docker-run-12-ruling.md) and [l34-ruling-1.md](l34-ruling-1.md). The branch under test is `ml/l34`, head `328942c5`, which contains the campaign tip `a49c1786`.

## 1. Outcome

- The lab already has a non-Key-Vault path: Identity's own `Auth:UseDevelopmentCertificates` switch. The compose dev stack already sets it, together with a blank `Auth:KeyVault:VaultUri`. **D-ML152:** the lab Identity adapter sets both, exactly as compose does. No product file changes and no new stub.
- **F131** is the cause. It is a lab defect: the adapter never selected that path.
- **F132** is a product gap, outside this campaign: nothing stops `Auth:UseDevelopmentCertificates` from being honoured in Production. Recommend a follow-up for the Identity owner. This is not an owner decision and does not block anything.
- **KG-W7-5:** in the lab, Identity's signing key and OpenIddict certificates are ephemeral. L35 owns it.
- **L34c** is one small packet on `ml/l34`. It needs a security-advisor pass (section 6).
- Run 13's other steps stand. S-L15 (red accepted, genuine green), S-L7 and Notify are done. KG-W5-23 closes, and KG-W7-4 closes for L36 but stays live for L34 (section 9).
- Decision needed: **no**.

## 2. Facts

| # | Fact | Evidence |
| --- | --- | --- |
| 1 | Identity exits 139 before it listens. The unhandled stack is `AddServer` (Program.cs:389), then the options callback (485-500), then `LoadKeyVaultCertificate` (1046) and `SecretClient.GetSecret`, ending in a `DefaultAzureCredential` aggregate of `CredentialUnavailableException`s | run-13 record section 5; `%TEMP%\docker-run-13\authstack.txt` |
| 2 | The certificate branch is selected by `if (builder.Environment.IsEnvironment("Testing") \|\| useDevelopmentCertificates)`, where `useDevelopmentCertificates = builder.Configuration.GetValue<bool>("Auth:UseDevelopmentCertificates")`. The else branch requires VaultUri, SigningCertName and EncryptionCertName, then uses `new DefaultAzureCredential()` | `src/Dhadgar.Identity/Program.cs:442-479` (ml-l34) |
| 3 | `appsettings.json` (the only appsettings file) sets `Auth:KeyVault:VaultUri = https://mc-core.vault.azure.net/` and the three certificate and key names. It does not set `UseDevelopmentCertificates` | `src/Dhadgar.Identity/appsettings.json` |
| 4 | The lab adapter sets `ASPNETCORE_ENVIRONMENT=Development` and no `Auth:UseDevelopmentCertificates` or `Auth:KeyVault:*` keys, so the Key Vault branch runs against the production vault URI. Its overlay keys are Logging only | `src/Tools/Dhadgar.Lab/Services/IdentityLabAdapter.cs:52-91` |
| 5 | Compose dev sets `Auth__UseDevelopmentCertificates: ${AUTH_USE_DEV_CERTS:-true}` and `Auth__KeyVault__VaultUri: ${AUTH_KEYVAULT_URI:-}`, with blank certificate names | `deploy/compose/docker-compose.services.yml:56-85`; `docker-compose.services.acr.yml:66` |
| 6 | Identity's README documents `Auth:UseDevelopmentCertificates=true` as the way to run without Key Vault | `src/Dhadgar.Identity/README.md:268-288` |
| 7 | Exactly four Identity files construct `DefaultAzureCredential`. **Program.cs:476** is the startup crash in F131. **SigningKeyProvider.cs:46** uses Key Vault only when VaultUri and `JwtSigningKeyName` are both set; appsettings has no `JwtSigningKeyName`, so it falls back to an ephemeral P-256 key outside Production. **ExchangeTokenValidator.cs:112** runs per exchange: with VaultUri and `ExchangePublicKeyName` set, it tries Key Vault first and falls back to `PublicKeyPem` outside Production. **WebhookSecretProvider.cs:79** runs per webhook: with VaultUri set it calls Key Vault and throws on failure; with VaultUri blank in Development it returns null (unsigned) | grep `new DefaultAzureCredential(` over `src/Dhadgar.Identity`; the four files |
| 8 | With the appsettings VaultUri still in force, every exchange the lab makes would first try `mc-core.vault.azure.net` through a credential chain that has no source, and every lab webhook would throw. The lab README (line 69) says unsigned webhooks are accepted; that is false today | Fact 7; `src/Tools/Dhadgar.Lab/README.md:69` |
| 9 | `Auth:UseDevelopmentCertificates` also turns on `DisableTransportSecurityRequirement` (Program.cs:512). Neither use has a Production guard | `Program.cs:444`, `Program.cs:512` |
| 10 | The lab image is `mcr.microsoft.com/dotnet/aspnet:10.0` with `USER app` (home `/home/app`). `LabServices.StartAsync` adds `--tmpfs /tmp:rw,size=64m` and no `--read-only`, so the CurrentUser X509 store under `$HOME/.dotnet/corefx/cryptography/x509stores` is writable | `tools/lab/images/dotnet-service.Dockerfile`; `LabServices.cs:174` |
| 11 | `LabServices.WriteEnvFile` writes every pair through `LabStack.WriteEntry` and filters nothing, so `Auth__KeyVault__VaultUri=` reaches `docker run --env-file` as a variable that is set but empty. .NET's environment provider then overrides the JSON value with `""`, the same way compose's `${AUTH_KEYVAULT_URI:-}` does | `LabServices.cs:527-564`; `LabStack.cs:663` |
| 12 | The run-12 symptom (`connection refused`, D-ML151) was this same crash. Without proxies and log tails it could not be seen | run-12 ruling; run-13 tails |

## 3. Rulings

### D-ML152: the lab Identity uses its existing development-certificate path, with no Key Vault

The Identity adapter's environment gains two entries:

| Key | Value | Effect |
| --- | --- | --- |
| `Auth__UseDevelopmentCertificates` | `true` | OpenIddict uses `AddDevelopmentSigningCertificate`/`AddDevelopmentEncryptionCertificate`, plus the `SigningKeyProvider` key, and access-token encryption is off (Program.cs:444-458) |
| `Auth__KeyVault__VaultUri` | `""` (empty) | Overrides appsettings. `ExchangeTokenValidator` uses the per-stack `Auth:Exchange:PublicKeyPem`. `WebhookSecretProvider` returns null (unsigned webhooks, Development). `SigningKeyProvider` uses its ephemeral key. Nothing contacts Azure Key Vault or a token endpoint |

Scope and limits:

- Both values live only in `IdentityLabAdapter.Environment`. They are **not** added to `OverlayKeys`, so an overlay cannot flip them back.
- No product file changes: `src/Dhadgar.Identity/**`, its appsettings, its Dockerfile and the compose files stay untouched. Production defaults are unchanged; appsettings still points at Key Vault and sets no development switch.
- The certificate names are left as appsettings sets them. With the development switch on, Program.cs never reads them. The validator and the webhook provider read them only when VaultUri is set, and it is blank.

Rejected:

| Option | Why not |
| --- | --- |
| Loopback Key Vault stub (like D-ML146) | The Azure SDK requires HTTPS and an AAD bearer token through challenge-based auth, so the stub would need a fake authority, a trusted TLS certificate and a PFX served as a secret. That is a large surface to test something the lab does not need to cover |
| `ASPNETCORE_ENVIRONMENT=Testing` | It selects the certificate path, but also mock OAuth, InMemory MassTransit and no rate limiter, which loses the fidelity the lab exists for |
| A file-based certificate setting | Identity has no file certificate option for OpenIddict. `Auth:SigningKeyPem`/`SigningKeyPath` cover the JWT key only |
| A product change (a lab switch in Identity) | Not needed, since the existing switch already does it. It would also put a lab concern into product code |

### F131 (lab defect, cause of run-13 AuthStack red)

"The lab Identity adapter does not select Identity's development-certificate path, so OpenIddict's `AddServer` loads certificates from the appsettings production vault (`mc-core.vault.azure.net`) through `DefaultAzureCredential`. The container has no credential source, `Program.Main` throws, and the container exits 139. The same VaultUri would also send exchanges and webhooks to the production vault at runtime." Fixed by L34c (D-ML152).

### F132 (product gap, outside the campaign)

"`Auth:UseDevelopmentCertificates` is honoured in any environment. Setting it in Production gives ephemeral OpenIddict certificates, unencrypted access tokens and `DisableTransportSecurityRequirement` (Program.cs:444, 512), and nothing refuses or warns." Recommendation for the Identity owner: refuse to start, or fail the readiness check, when `IsProduction()` and the switch is true, and add a test. This is not an L34c item, not a BlockedDecision and not a wave blocker. The orchestrator files it as an issue or a backlog line (section 9).

### KG-W7-5 (new, live)

"**Live:** in the lab, Identity's JWT signing key (`SigningKeyProvider` ephemeral P-256) and its OpenIddict development certificates are per-process. Tokens issued before an Identity restart (fault `restart`, crash, `service_restart`) fail validation afterwards, and an exchange retried across a restart re-signs. Until: L35 either supplies a per-stack `Auth__SigningKeyPem` from the vault (generated at `stack_up` like the exchange pair) or records the restart rows as expected re-login. Owner: L35."

## 4. Other services in the lab stack

| Service | Startup dependency on Key Vault or Secrets | Verdict |
| --- | --- | --- |
| Nodes | none (no Azure.Identity reference) | Started in runs 8-13 |
| Gateway | none | Started in runs 8-13 |
| Notifications, Discord (ml-l36) | Discord reads `SecretsService:Url` and tolerates its absence (Program.cs:29) | Started and passed in run 13 |
| BetterAuth | Secrets service at startup, served by the D-ML146 loopback stub | **Not yet proven live**: the run-13 crash meant it was never started. Run 14 is its first boot |
| Panel | no secrets loader | Not yet proven live; run 14 is its first boot |
| Identity, `OAuthSecretProvider` | `SecretsService:Url` (default `http://localhost:5110`), with `HttpRequestException` caught | Tolerated; run 13 passed this point |

Only `src/Dhadgar.Identity` and `src/Dhadgar.Secrets` reference `DefaultAzureCredential`, `SecretClient` or `CertificateClient`, and Secrets is not part of the lab. No other lab service carries this latent dependency.

## 5. Packet L34c

| Item | Value |
| --- | --- |
| Branch / worktree | `ml/l34` / `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l34` (exists, clean at `328942c5`) |
| Size | small (well under 40k) |
| Security review | **yes**, short pass (section 6) |
| Commits | two: R1, then the fix |
| Docker | none in the packet. Run 14 is the orchestrator's step (section 7) |

### 5.1 Rules for the coder (inlined)

1. Edit only the files named in this section. Do not touch `src/Dhadgar.Identity/**`, `deploy/**` or any appsettings.
2. Return `Result<T>`; never `Success(null)`. The adapter keeps its existing failure paths unchanged.
3. Do not log or assert on secret values. Test failure messages name keys, never values (R-LOG).
4. Use `using` on every `IDisposable`.
5. No package references or versions in `.csproj`. If `ConfigurationBuilder.AddJsonFile` or `AddInMemoryCollection` does not resolve in `Dhadgar.Lab.Tests`, stop and report `blocked`; do not add a package.
6. Nullable is enabled and Release builds treat warnings as errors.
7. Follow the xUnit conventions of `Dhadgar.Lab.Tests`: `[Fact]`, `Assert.*`, file-scoped namespace and a summary on the class.
8. Do not run Docker. Never run `publish -r` or `git worktree prune`. Lock files must not change.

### 5.2 Step R1: the rename that l36-ruling-1 R1 prescribed, now scoped

[l36-ruling-1.md](l36-ruling-1.md) section 1.1 R1 renames L34's D-ML144 to D-ML146. It has not been applied on `ml/l34`. The merged tip also brought ruling worknotes that use **D-ML144 for L19n's decision**, so a whole-tree replace is wrong. Replace `D-ML144` with `D-ML146` in these 8 files only (11 hits):

| File | Hits |
| --- | --- |
| `src/Tools/Dhadgar.Lab/Build/LabBuilder.cs` | 2 |
| `src/Tools/Dhadgar.Lab/Services/BetterAuthLabAdapter.cs` | 2 |
| `src/Tools/Dhadgar.Lab/README.md` | 1 |
| `tests/Dhadgar.Lab.Tests/Build/NodeStagingTests.cs` | 1 |
| `tests/Dhadgar.Lab.Tests/Services/BetterAuthSecretsStubTests.cs` | 1 |
| `tools/lab/images/lab-secrets-stub.mjs` | 1 |
| `tools/lab/images/node-app.Dockerfile` | 2 |
| `docs/analysis/meridian-lab/worknotes/ml-l34.md` | 1 |

**Do not** edit `docs/analysis/meridian-lab/worknotes/{l19n-ruling-1,l34-ruling-1,l36-ruling-1,docker-run-12-ruling}.md`. Afterwards, `git grep -c D-ML144` in the worktree must list exactly those four worknotes, with counts 12, 16, 11 and 4. Commit with subject `docs(lab): renumber L34's decision D-ML144 to D-ML146 [L34]`.

### 5.3 Edit E1: `src/Tools/Dhadgar.Lab/Services/IdentityLabAdapter.cs`

1. Add two constants after `ExchangeAudience`:

   ```csharp
   /// <summary>
   /// <c>Auth:UseDevelopmentCertificates</c>: Identity's own switch for ephemeral OpenIddict certificates instead of Key Vault (D-ML152).
   /// Set only by this adapter; never an overlay key and never a product default.
   /// </summary>
   public const string UseDevelopmentCertificatesKey = "Auth__UseDevelopmentCertificates";

   /// <summary>
   /// <c>Auth:KeyVault:VaultUri</c>, set empty so it overrides appsettings: no Key Vault calls for certificates, exchange keys or webhook secrets (D-ML152).
   /// </summary>
   public const string KeyVaultUriKey = "Auth__KeyVault__VaultUri";
   ```

2. In `Environment`, add two dictionary entries after `["Auth__Exchange__PublicKeyPem"] = ...`:

   ```csharp
   [UseDevelopmentCertificatesKey] = "true",
   [KeyVaultUriKey] = string.Empty,
   ```

3. Replace the class summary (lines 6-9) with:

   ```csharp
   /// <summary>
   /// Identity behind the lab proxies (C1 port 15010). Postgres, Redis and RabbitMQ are reached only through Toxiproxy.
   /// It verifies BetterAuth's exchange tokens with the stack's public key, so the key pair is generated per stack.
   /// It runs Identity's development-certificate path with a blank Key Vault URI, as the compose dev stack does (D-ML152):
   /// certificates and the signing key are ephemeral (KG-W7-5), and webhooks are unsigned.
   /// </summary>
   ```

Leave `OverlayKeys` unchanged.

### 5.4 Edit E2: tests in `tests/Dhadgar.Lab.Tests/Services/AuthStackAdapterTests.cs`

Add `using Microsoft.Extensions.Configuration;` at the top. Add the three facts below after `Identity_WithoutPublicKey_IsInvalidArgument` and reuse the existing private `Context()`.

**T1 `Identity_UsesDevelopmentCertificates_AndBlankVaultUri`**

```csharp
[Fact]
public void Identity_UsesDevelopmentCertificates_AndBlankVaultUri()
{
    var env = new IdentityLabAdapter().Environment(Context());

    Assert.True(env.IsSuccess, env.Error);
    Assert.Equal("true", env.Value![IdentityLabAdapter.UseDevelopmentCertificatesKey]);
    Assert.True(env.Value.TryGetValue(IdentityLabAdapter.KeyVaultUriKey, out string? vault), "the Key Vault URI key must be present (empty) to override appsettings");
    Assert.Equal(string.Empty, vault);
    Assert.DoesNotContain(IdentityLabAdapter.UseDevelopmentCertificatesKey.Replace("__", ":", StringComparison.Ordinal), new IdentityLabAdapter().OverlayKeys);
    Assert.DoesNotContain(IdentityLabAdapter.KeyVaultUriKey.Replace("__", ":", StringComparison.Ordinal), new IdentityLabAdapter().OverlayKeys);
}
```

Red on revert: without E1 the constants do not exist, so the test does not compile. With the constants but without the entries, `KeyNotFoundException` or the `TryGetValue` assertion fails.

**T2 `Identity_OverItsAppsettings_SelectsDevelopmentCertificates_AndNoVault`** (static half of the seam: the lab env against Identity's real configuration file)

```csharp
[Fact]
public void Identity_OverItsAppsettings_SelectsDevelopmentCertificates_AndNoVault()
{
    string appsettings = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "src", "Dhadgar.Identity", "appsettings.json");
    var env = new IdentityLabAdapter().Environment(Context());
    Assert.True(env.IsSuccess, env.Error);

    IConfiguration config = new ConfigurationBuilder()
        .AddJsonFile(appsettings, optional: false, reloadOnChange: false)
        .AddInMemoryCollection(env.Value!.Select(pair => new KeyValuePair<string, string?>(pair.Key.Replace("__", ":", StringComparison.Ordinal), pair.Value)))
        .Build();

    // Mirrors Program.cs: GetValue<bool>("Auth:UseDevelopmentCertificates") picks the certificate branch.
    Assert.True(config.GetValue<bool>("Auth:UseDevelopmentCertificates"), "Identity would take the Key Vault certificate branch (F131)");
    Assert.True(string.IsNullOrWhiteSpace(config["Auth:KeyVault:VaultUri"]), "appsettings' Key Vault URI is still in force");
}
```

The in-memory provider stands in for Docker's environment because a Windows process cannot hold an empty environment variable. Fact 11 covers the env-file side. Red on revert: the switch reads false and the VaultUri is `https://mc-core.vault.azure.net/`. Because the test reads the real appsettings, a later product change that adds a Key Vault default somewhere else will surface here too.

**T3 `Identity_KeyVaultConsumers_AreTheKnownFour`** (drift guard on the product side of D-ML152)

```csharp
[Fact]
public void Identity_KeyVaultConsumers_AreTheKnownFour()
{
    string root = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "src", "Dhadgar.Identity");
    string sep = Path.DirectorySeparatorChar.ToString();
    var consumers = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Contains(sep + "bin" + sep, StringComparison.Ordinal) && !path.Contains(sep + "obj" + sep, StringComparison.Ordinal))
        .Where(path => File.ReadAllText(path).Contains("new DefaultAzureCredential(", StringComparison.Ordinal))
        .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
        .Order(StringComparer.Ordinal)
        .ToList();

    Assert.Equal(new[] { "Program.cs", "Services/ExchangeTokenValidator.cs", "Services/SigningKeyProvider.cs", "Services/WebhookSecretProvider.cs" }, consumers);

    string program = File.ReadAllText(Path.Combine(root, "Program.cs"));
    Assert.Contains("GetValue<bool>(\"Auth:UseDevelopmentCertificates\")", program, StringComparison.Ordinal);
    Assert.Contains("|| useDevelopmentCertificates)", program, StringComparison.Ordinal);
    foreach (string service in consumers.Where(name => name.StartsWith("Services/", StringComparison.Ordinal)))
    {
        Assert.Contains("VaultUri", File.ReadAllText(Path.Combine(root, service)), StringComparison.Ordinal);
    }
}
```

This test is green before and after E1. It is a guard: if Identity gains a fifth Key Vault consumer, or the switch is renamed, D-ML152 has to be re-ruled, and the test says so. To check it by hand, change the expected list to three entries and confirm it fails, then restore it. Do not commit the change.

### 5.5 Edit E3: `tests/Dhadgar.Lab.Tests/Services/AuthStackDockerTests.cs`

Place this immediately after the health loop (after line 119) and before the sign-up:

```csharp
// D-ML152: Identity must not touch Azure Key Vault or the Azure credential chain in the lab.
Result<IReadOnlyList<LogLine>> identityLines = supervisor.ReadLines("identity");
Assert.True(identityLines.IsSuccess, identityLines.IsFailure ? identityLines.Error : string.Empty);
Assert.DoesNotContain(identityLines.Value!, line =>
    line.Message.Contains("DefaultAzureCredential", StringComparison.Ordinal)
    || line.Message.Contains("Azure.Identity", StringComparison.Ordinal)
    || line.Message.Contains("vault.azure.net", StringComparison.Ordinal)
    || line.Category.StartsWith("Azure.", StringComparison.Ordinal));
```

`LogLine` is `Dhadgar.Lab.Processes.LogLine`, and that namespace is already imported. This step runs only under Docker. In run 14 it proves the Key Vault path stayed cold.

### 5.6 Edit E4: `src/Tools/Dhadgar.Lab/README.md` line 69

Replace the paragraph with:

```markdown
Identity runs with `ASPNETCORE_ENVIRONMENT=Development` (auto-migrate), `Auth:UseDevelopmentCertificates=true` and an empty `Auth:KeyVault:VaultUri`, as the compose dev stack does (D-ML152). Its OpenIddict certificates and JWT signing key are ephemeral, so tokens issued before an Identity restart stop validating after it (KG-W7-5). Nothing calls Azure Key Vault. Because the vault URI is empty, `/webhooks/better-auth` (through the Gateway `/api/v1/identity/webhooks/better-auth`) accepts unsigned bodies, so any local process can soft-delete or rewrite a lab user. The OpenAPI and Scalar pages are served on `127.0.0.1:15010`. The lab is not a fidelity target for webhook signing or certificate rotation.
```

### 5.7 Edit E5: `docs/analysis/meridian-lab/worknotes/ml-l34.md`

Append a `## Fix pass 2 (l34-ruling-2)` section that lists R1 and E1-E4, the check results, and "run 14 pending (orchestrator)".

### 5.8 Checks (in order)

1. `dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l34\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true`: 0 errors, 0 warnings.
2. `dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l34\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`: 0 errors, 0 warnings.
3. `dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l34\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~AuthStackAdapterTests"`: all green, including T1-T3.
4. The same project with `--filter "RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"`: 0 failures. L21g is in the tip, so KG-W5-22's red is gone. Any red stops the packet.
5. `git -C <worktree> grep -c D-ML144` gives exactly the four worknotes listed in 5.2.
6. `git -C <worktree> status --porcelain` is empty after both commits. The fix commit changes only `IdentityLabAdapter.cs`, `AuthStackAdapterTests.cs`, `AuthStackDockerTests.cs`, the lab `README.md` and `worknotes/ml-l34.md`. No `packages.lock.json` changes.

Fix commit subject: `fix(lab): lab Identity uses development certificates and no Key Vault [L34c]`.

### 5.9 Docs owed

E4 and E5 cover them. No library chapter describes the lab Identity posture.

## 6. Security-advisor pass (short) on L34c

Scope: the L34c fix commit and R1. The advisor confirms:

1. The development-certificate switch and the blank VaultUri appear only in `IdentityLabAdapter.Environment`. They are not in `OverlayKeys`, and no file under `src/Dhadgar.Identity/**`, `deploy/**` or any appsettings changed, so production defaults are untouched.
2. The lab Identity makes no egress to `*.vault.azure.net`, the IMDS endpoint (169.254.169.254) or `login.microsoftonline.com`. Static evidence is a blank VaultUri together with fact 7; live evidence is E3 in run 14.
3. Unsigned webhooks match the position l34-ruling-1 accepted (security N3, option b). The README states the exposure.
4. Ephemeral certificates with access-token encryption off and the transport-security requirement off are acceptable for a loopback lab: ports bind to `127.0.0.1`, and the users and keys are per-stack.
5. F132 is recorded as a product follow-up, not a lab fix.
6. R1 is a renumbering only: no logic changes in the 8 files.

## 7. Next Docker check: run 14 (AuthStack only; needs the user's yes)

Preconditions are the same as the run-12 ruling, section 5:

- no containers or networks labelled `meridian-lab=1`;
- port 8789 free;
- no `TestResults` folder under `ml-l34\tests\Dhadgar.Lab.Tests`;
- no dirty lock files;
- `ml-l34` builds Release with L34c's head.

Command: the same as the run-13 record, section 5, with TRX `authstack.trx` and results in `%TEMP%\docker-run-14\authstack\`.

Expected, in order:

| Step | Expected |
| --- | --- |
| Identity | 200 on `/healthz` within 90 s. Its tail has no Azure lines (E3), and may show OpenIddict development certificate creation and "Using ephemeral JWT signing key" |
| BetterAuth | 200 on `/healthz`. This is the first live boot of the D-ML146 Secrets stub |
| Panel | 200 on `/login` |
| Gateway | 200 on `/healthz` |
| Sign-up | 200 through the Gateway |
| Teardown | clean; port 8789 free |

Contingencies (the orchestrator applies them without a new ruling):

- **C1:** the Identity tail shows an X509 store or permission error (`CryptographicException`, `UnauthorizedAccessException` under `x509stores`). Dispatch a micro-fix on `ml/l34`: add `["HOME"] = "/tmp"` to the Identity env with a comment that cites D-ML152 and this contingency, and assert it in T1. Rerun run 14.
- **C2:** BetterAuth exits or fails `/healthz`, and its tail names the Secrets fetch or the stub preload. This is a D-ML146 defect. Ask for an l34 ruling 3 and include the tail.
- **C3:** sign-up answers non-200 with all four services healthy. Ask for an l34 ruling 3 and include the Gateway and BetterAuth tails. Do not retry blindly.
- Anything else gets a new ruling.

Record to `.claude/reports/meridian-lab/wave5-docker-run-14.md`, using the run-13 record's format.

## 8. Order for the orchestrator

1. L34c (`packet-coder`) → the `security-advisor` short pass (section 6), plus a `packet-reviewer` pass on the diff.
2. User's yes → Docker run 14 (section 7).
3. If run 14 is green: the L34 and L36 reviews and merges continue as the run-12 ruling planned. L36 is not blocked by L34c; its acceptance passed in run 13.

## 9. Ledger edits (orchestrator)

**First apply the pending edits from earlier rulings.** None of them are in `README.md` at `a49c1786`:

- [l19n-ruling-1.md](l19n-ruling-1.md) section 7;
- [l34-ruling-1.md](l34-ruling-1.md) section 7. Write L34's decision as **D-ML146**, not D-ML144 (l36-ruling-1 R1);
- [l36-ruling-1.md](l36-ruling-1.md) section 8;
- [docker-run-12-ruling.md](docker-run-12-ruling.md) section 7.

Check each one landed before adding the lines below.

Then:

- **Findings:** add F131 and F132 (section 3 text).
- **Decisions:** add `| D-ML152 | The lab Identity runs Identity's existing development-certificate path (`Auth:UseDevelopmentCertificates=true`) with an empty `Auth:KeyVault:VaultUri`, set only in `IdentityLabAdapter`; no product change, no Key Vault stub ([l34 ruling 2](worknotes/l34-ruling-2.md)) | Accepted (architect) |`.
- **Known gaps:**
  - Add KG-W7-5 (section 3 text).
  - Update KG-W5-23 to "**Closed** (run 13: S-L15 red accepted, green genuine; S-L7 passed)".
  - Update KG-W7-4 to "**Live for L34 only (F129, F130, F131):** L36's Docker acceptance passed in run 13; L34's AuthStack has never passed. Closes on run 14 green after L34c."
  - KG-W5-24 is **not used** by this ruling and stays the next free W5 ID.
- **Packet table:** after the L34 row, add `| L34c | 7 (follow-up) | Lab Identity on development certificates with an empty VaultUri; D-ML144→D-ML146 rename on ml/l34 (F131, D-ML152) | L34b | yes (short) | Ready ([l34 ruling 2](worknotes/l34-ruling-2.md)) |`.
- **Backlog / issues:** file F132 as an Identity follow-up ("refuse `Auth:UseDevelopmentCertificates` in Production"). It is outside the campaign's packets.
- **Wave 5 Docker worknote:** add "Run 13 ruled: [l34 ruling 2](l34-ruling-2.md); S-L15, S-L7 and Notify done; AuthStack red, F131."
- **RESUME.md next actions:** (1) L34c → security short pass + review; (2) user's yes → run 14 (AuthStack); (3) L34 and L36 reviews and merges; (4) the earlier ledger edits above. Also refresh its stale tip (`99bb31e3` → current).
- **Next free IDs after this ruling:** F133, D-ML153, KG-W5-24, KG-W7-6.
