# L34 ruling 1 (review pass 1 `decision`, security pass 1 `fix`)

Range `8191eb8f..7429671e`, branch `ml/l34`, worktree `.claude/worktrees/ml-l34` (`<W>` below). Inputs: `.claude/reports/meridian-lab/ml-l34-review-1.md`, `ml-l34-security-1.md`, the L34 spec in `work-packets.md`, worknote `ml-l34.md`.

## 1. Outcome

L34 is **fixable in one pass by one implementer; no owner question**. The open decision (review D1, security N1) is that BetterAuth has no env mode. `server.js:22` always runs `await loadSecrets()`. `secrets-client.js:272-275` throws without `SECRETS_SERVICE_URL`, and lines 280-307 always replace `BETTER_AUTH_SECRET` and `EXCHANGE_TOKEN_PRIVATE_KEY` with the Secrets service's answer. The lab does not run Dhadgar.Secrets (ledger README line 16), and the packet forbids product changes. Ruling D-ML144: the lab supplies a **loopback Secrets stub preloaded into the BetterAuth process**. BetterAuth's real `loadSecrets` then runs unchanged against a fake for a dependency the lab does not cover. F22 was wrong and is corrected (section 7).

Every other fix note is applied as prescribed or with the changes stated, except security N6, parked as KG-W7-1 (owner L35). The edits are in section 4.

## 2. Dispositions

| Note | Disposition | Edit |
| --- | --- | --- |
| Review N1 (Dockerfile `SharedAuth` path) | Apply | E1 |
| Review N2 (sign-up through Gateway) | Apply, with an `Origin` header and the wipe guard | E10 |
| Review N3 (S-L9 is not the specified test) | Apply; S-L9 also checks that `ui_flow_run` is registered **before** any Docker work, so it is red with or without Docker. Step names are fixed for L35 (section 7, C14) | E11 |
| Review N4 (no tests on the secret paths) | Apply: (a), (b) in `LabStackTests` (it already has the fake Docker and Toxiproxy harness); (c), (d) in a new `NodeStagingTests` | E8, E9 |
| Review N5 (shell-form CMD) | Apply | E1 |
| Review N6 (`csproj =validator`) | Apply | E3 |
| Review N7 (README blank line) | Apply | E13 |
| Review D1 (BetterAuth cannot boot) | Ruled: D-ML144 stub | E1, E2, E3, E4, E7, E12 |
| Review D2 / security label | `security-review` label on the PR; a security pass 2 is required (stub carries the private key; `SecretForms` changes) | none |
| Review "checked": default `lab_build` now builds the Node images | Accept the behaviour (`svc_start betterauth/panel` needs those images); document it | E13 |
| Review "checked": `MutationRunTool` can resolve `panel`/`betterauth` | Accept as is (returns a Result failure) | none |
| Security N1 | Ruled: D-ML144 (in-process loopback variant of the advisor's option 1) | as D1 |
| Security N2 (`LabServiceContext.ToString`) | Apply | E5, E7 |
| Security N3 (unsigned Identity webhooks in Development) | Option (b): README statement. Option (a) is rejected because a lab-only `DenyAll` would make the lab Gateway differ from the product Gateway on a real route | E13 |
| Security N4 (staging deny-list) | Apply the extended deny-list, skip reparse points, and exclude `bin`/`obj` too. An allow-list is rejected because it is brittle against the Panel's layout (`Pages/`, `Shared/`, `public/`) | E3, E9 |
| Security N5 (comment overclaims; single-line PEM) | Apply: comment fix, plus PEM-derived forms in `SecretForms` | E6, E7 |
| Security N6 (`db_query` token columns) | **Park as KG-W7-1** (owner L35, where the flows first create sessions and refresh tokens under R-LOG); README states the exposure now | E13, section 7 |
| Security N7 (wipe guard, unguarded delete) | Apply to both Docker tests | E10, E11 |
| Security "not fixed" (earlier rounds) | Unchanged; already owned by L54b (H3, H4) | none |

## 3. D-ML144: BetterAuth's Secrets dependency in the lab

- `tools/lab/images/lab-secrets-stub.mjs` is a lab file, never shipped. It is staged into every Node image at `/lab/secrets-stub.mjs`. The BetterAuth adapter alone loads it, with `NODE_OPTIONS=--import=file:///lab/secrets-stub.mjs`.
- It listens on `127.0.0.1:8099` **inside the container's network namespace**. No port is published, it is not on the lab network and it has no Toxiproxy edge. It serves `POST /connect/token` and `GET /api/v1/secrets/{betterauth,oauth,infrastructure}`, using only values the env file already gave this container (`BETTER_AUTH_SECRET`, `EXCHANGE_TOKEN_PRIVATE_KEY`). The private key therefore reaches no new process, file or network path.
- It fails closed at preload when `SECRETS_SERVICE_URL` is anything but its own loopback URL, when `IDENTITY_SERVICE_URL` is set (`secrets-client.js:35` would send the token request out of the container), or when either secret is missing. It logs nothing.
- `USE_ENV_DB_PASSWORD=1` keeps `loadSecrets` off the infrastructure path (`secrets-client.js:322`), because the image fixes `NODE_ENV=production`.
- Rejected:
  - A product env-mode switch: a product change on the auth path, which the packet rule forbids. It would also widen BlockedDecision D-ML40.
  - A separate stub container: it would put the key on the lab network.
  - Running Dhadgar.Secrets: needs Key Vault and a seeded OpenIddict client.
  - Re-slicing without BetterAuth: S-L9 and L35 need it.

## 4. Exact edits (all in `<W>`)

### E1. `tools/lab/images/node-app.Dockerfile` (replace the whole file)

```dockerfile
# syntax=docker/dockerfile:1
# L34: one image for the Node lab apps (BetterAuth, Panel). The build context is the folder LabBuilder.StageNodeApp fills:
# app/ (the app's source), SharedAuth/ (the file: dependency the Panel names) and lab/ (lab-only files, D-ML144).
# Node major matches the Panel's engines (>=22.23.4) and the BetterAuth Dockerfile (node:22).
FROM node:22-bookworm-slim

ARG APP_DIR=app
ARG APP_ENTRY=src/server.js
ARG PUBLIC_GATEWAY_URL=http://localhost:15000

WORKDIR /src
# The Panel's package.json and package-lock.json name file:../Dhadgar.SharedAuth, resolved from /src/app.
COPY SharedAuth /src/Dhadgar.SharedAuth
# Lab-only Secrets stub; loaded only where the adapter sets NODE_OPTIONS (BetterAuth, D-ML144).
COPY lab /lab
COPY ${APP_DIR} /src/app
WORKDIR /src/app

# npm ci needs the npm registry; the lab build is RequiresNetwork.
RUN npm ci

ENV PUBLIC_GATEWAY_URL=${PUBLIC_GATEWAY_URL}
RUN if grep -q '"build"' package.json; then npm run build && npm prune --omit=dev; fi

ENV NODE_ENV=production
ENV HOST=0.0.0.0
ENV PORT=8080
ENV APP_ENTRY=${APP_ENTRY}

USER node
EXPOSE 8080

# exec so node is PID 1 and receives docker stop's SIGTERM.
CMD ["sh", "-c", "exec node \"$APP_ENTRY\""]
```

### E2. New `tools/lab/images/lab-secrets-stub.mjs`

```javascript
// Lab only (meridian-lab L34, D-ML144). Never shipped with BetterAuth.
// Preloaded into the lab BetterAuth container with NODE_OPTIONS=--import=file:///lab/secrets-stub.mjs.
// It stands in for Dhadgar.Secrets and its /connect/token, which the lab does not run, so BetterAuth's real
// loadSecrets() (src/Dhadgar.BetterAuth/src/secrets-client.js) runs unchanged. It serves only values the lab
// already put in this container's environment, listens on loopback inside the container, and logs nothing.
import http from "node:http";

const port = Number(process.env.LAB_SECRETS_STUB_PORT ?? "");
const secret = process.env.BETTER_AUTH_SECRET;
const privateKey = process.env.EXCHANGE_TOKEN_PRIVATE_KEY;

if (!Number.isInteger(port) || port <= 0 || port > 65535) {
  throw new Error("lab secrets stub: LAB_SECRETS_STUB_PORT is required");
}
if (process.env.SECRETS_SERVICE_URL !== `http://127.0.0.1:${port}`) {
  throw new Error("lab secrets stub: SECRETS_SERVICE_URL must be the loopback stub");
}
if (process.env.IDENTITY_SERVICE_URL) {
  throw new Error("lab secrets stub: IDENTITY_SERVICE_URL must be unset so token requests stay in the container");
}
if (!secret || !privateKey) {
  throw new Error("lab secrets stub: BETTER_AUTH_SECRET and EXCHANGE_TOKEN_PRIVATE_KEY are required");
}

const accessToken = globalThis.crypto.randomUUID();
const bodies = {
  "/api/v1/secrets/betterauth": {
    secrets: { "betterauth-secret": secret, "betterauth-exchange-private-key": privateKey },
  },
  "/api/v1/secrets/oauth": { secrets: {} },
  "/api/v1/secrets/infrastructure": { secrets: {} },
};

const server = http.createServer((req, res) => {
  const send = (status, body) => {
    res.writeHead(status, { "Content-Type": "application/json" });
    res.end(JSON.stringify(body));
  };
  if (req.method === "POST" && req.url === "/connect/token") {
    req.resume();
    return send(200, { access_token: accessToken, token_type: "Bearer", expires_in: 3600 });
  }
  if (req.method !== "GET" || !Object.hasOwn(bodies, req.url ?? "")) {
    return send(404, { error: "not_found" });
  }
  if (req.headers.authorization !== `Bearer ${accessToken}`) {
    return send(401, { error: "unauthorized" });
  }
  return send(200, bodies[req.url]);
});

await new Promise((resolve, reject) => {
  server.once("error", reject);
  server.listen(port, "127.0.0.1", resolve);
});
```

### E3. `src/Tools/Dhadgar.Lab/Build/LabBuilder.cs`

1. Line 199 (review N6): `csproj =validator` → `csproj = validator`.
2. Replace `NodeExcludedNames` with:

```csharp
/// <summary>Folder names never staged into a Node build context (installed, generated or .NET shim output).</summary>
private static readonly string[] NodeExcludedDirectories = ["node_modules", "dist", ".git", ".astro", "coverage", "bin", "obj"];

/// <summary>File names never staged: package-manager credentials and local secret files (security N4).</summary>
private static readonly string[] NodeExcludedFileNames = [".npmrc", ".yarnrc", ".yarnrc.yml", ".dev.vars", ".secrets", "secrets.json"];

/// <summary>Key and certificate extensions never staged.</summary>
private static readonly string[] NodeExcludedExtensions = [".pem", ".key", ".pfx", ".p12"];

/// <summary>The lab-only Secrets stub (D-ML144), relative to the checkout, and where it lands in the stage.</summary>
private const string SecretsStubSource = "tools/lab/images/lab-secrets-stub.mjs";

/// <summary>True when a file name may be staged: not an env file, not a credential file, not a key or certificate.</summary>
internal static bool IsStagedFile(string name) =>
    !name.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
    && !NodeExcludedFileNames.Contains(name, StringComparer.OrdinalIgnoreCase)
    && !NodeExcludedExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
```

3. Split the staging out of `BuildNodeAppAsync` into a new method. `BuildNodeAppAsync` calls `StageNodeApp(project, checkoutPath, outputRoot, paths, validator)`, returns its failure as is, and then runs the existing `docker build` with `stage.Value!` as the context. Shape:

```csharp
/// <summary>
/// Stages a Node app under <c>&lt;outputRoot&gt;\&lt;project&gt;</c>: <c>app/</c> (the app), <c>SharedAuth/</c> and
/// <c>lab/secrets-stub.mjs</c> (D-ML144). Env files, credential files, keys, installed or generated folders and reparse
/// points are never copied. Returns the stage folder.
/// </summary>
internal static Result<string> StageNodeApp(BuildProject project, string checkoutPath, string outputRoot, LabPaths paths, IPathValidator validator)
{
    // The existing appSource / sharedSource / stage / appDest / sharedDest validation moves here unchanged, plus:
    Result<string> stubSource = validator.ValidatePath(Path.Combine(checkoutPath, SecretsStubSource.Replace('/', Path.DirectorySeparatorChar)), [checkoutPath]);
    Result<string> stubDest = validator.ValidatePath(Path.Combine(stage.Value!, "lab", "secrets-stub.mjs"), [paths.Builds]);
    // any failure -> Result<string>.Failure("[Lab.InvalidArgument] build path is not valid")
    // CopyTree(app), CopyTree(shared) as today; then, inside try/catch (IOException or UnauthorizedAccessException ->
    // "[Lab.Io] build source could not be staged"):
    //     Directory.CreateDirectory(Path.GetDirectoryName(stubDest.Value!)!);
    //     File.Copy(stubSource.Value!, stubDest.Value!, overwrite: true);
    return Result<string>.Success(stage.Value!);
}
```

   `BuildNodeAppAsync` then needs no `dockerfile` validation change. It keeps validating `node-app.Dockerfile` itself.
4. `CopyTree` becomes `internal static`. Its loops become:

```csharp
foreach (string file in Directory.GetFiles(source))
{
    string name = Path.GetFileName(file);
    if (IsStagedFile(name) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
    {
        File.Copy(file, Path.Combine(destination, name), overwrite: true);
    }
}

foreach (string directory in Directory.GetDirectories(source))
{
    string name = Path.GetFileName(directory);
    if (NodeExcludedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase)
        || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
    {
        continue;
    }
    // recursion unchanged
}
```

   Update the summary on `CopyTree` to name the three lists and the reparse-point skip.

### E4. `src/Tools/Dhadgar.Lab/Services/BetterAuthLabAdapter.cs`

1. Add constants:

```csharp
/// <summary>Loopback port of the lab Secrets stub inside the BetterAuth container (D-ML144); never published.</summary>
public const int SecretsStubPort = 8099;

/// <summary>Where the node-app image holds the stub (<c>COPY lab /lab</c>).</summary>
public const string SecretsStubPath = "/lab/secrets-stub.mjs";
```

2. Add to the env dictionary in `Environment`:

```csharp
["SECRETS_SERVICE_URL"] = $"http://127.0.0.1:{SecretsStubPort}",
["LAB_SECRETS_STUB_PORT"] = SecretsStubPort.ToString(CultureInfo.InvariantCulture),
["NODE_OPTIONS"] = $"--import=file://{SecretsStubPath}",
["USE_ENV_DB_PASSWORD"] = "1",
```

   Do not set `IDENTITY_SERVICE_URL`, `SERVICE_CLIENT_ID` or `SERVICE_CLIENT_SECRET`.
3. Replace the class summary with: "BetterAuth behind the lab Gateway (C1 port 15130). BetterAuth always loads its secret and exchange key from the Secrets service at boot (`secrets-client.js` `loadSecrets`). The lab does not run Secrets, so the image preloads the loopback stub `tools/lab/images/lab-secrets-stub.mjs` (D-ML144), which serves the values this env file carries. The exchange private key reaches the container only through the env file."

### E5. `src/Tools/Dhadgar.Lab/Services/ILabServiceAdapter.cs` (security N2)

Give `LabServiceContext` a body (same pattern as `Tools/LabCallContext.cs:9`):

```csharp
public sealed record LabServiceContext(
    /* parameters unchanged */)
{
    // ToString prints only the build id: the context carries the exchange private key, the BetterAuth secret and the RabbitMQ password.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("BuildId = ").Append(BuildId);
        return true;
    }
}
```

### E6. `src/Tools/Dhadgar.Lab/Services/LabServices.cs` (security N5)

Replace the third summary line of `WriteEnvFile` ("Each pair goes through the same zeroing writer ... no secret stays in a managed string buffer.") with: "Each pair goes through `LabStack.WriteEntry`. The secret strings that `ComposeEnvironment` and the adapters build (the private key PEM, its single-line form, the BetterAuth secret) are immutable managed strings and are not zeroed. This is accepted: they live only in same-user daemon memory until the call returns."

### E7. `src/Tools/Dhadgar.Lab/Secrets/SecretForms.cs` (security N5)

1. In `ForEachForm`, replace the `return true;` after the encoder loop (line 75) with `return ForEachPemForm(secret, use);`.
2. Add:

```csharp
private const int MinPemLine = 16;

/// <summary>
/// For a PEM private key, the forms a service can receive or print without the multi-line block: the key on one line
/// (what <c>LabEnvValue.SingleLinePem</c> sends to BetterAuth), the base64 body alone, and each body line of at least
/// 16 characters. The PEM regex in <see cref="Redactor"/> only matches when the header is present. The buffer is zeroed.
/// </summary>
private static bool ForEachPemForm(ReadOnlySpan<char> secret, SecretFormVisitor use)
{
    if (!secret.StartsWith("-----BEGIN ", StringComparison.Ordinal) || secret.IndexOf("PRIVATE KEY-----", StringComparison.Ordinal) < 0)
    {
        return true;
    }

    char[] single = ArrayPool<char>.Shared.Rent(secret.Length);
    try
    {
        int length = 0;
        foreach (char c in secret)
        {
            if (c is not ('\r' or '\n'))
            {
                single[length++] = c;
            }
        }

        ReadOnlySpan<char> line = single.AsSpan(0, length);
        if (!line.SequenceEqual(secret) && !use(line))
        {
            return false;
        }

        int headerEnd = line.IndexOf("KEY-----", StringComparison.Ordinal);
        int footer = line.IndexOf("-----END ", StringComparison.Ordinal);
        if (headerEnd >= 0 && footer > headerEnd + 8)
        {
            ReadOnlySpan<char> body = line[(headerEnd + 8)..footer];
            if (body.Length >= MinPemLine && !use(body))
            {
                return false;
            }
        }

        foreach (Range range in secret.Split('\n'))
        {
            ReadOnlySpan<char> part = secret[range].TrimEnd('\r');
            if (part.Length >= MinPemLine && !part.StartsWith("-----", StringComparison.Ordinal) && !use(part))
            {
                return false;
            }
        }

        return true;
    }
    finally
    {
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(single.AsSpan()));
        ArrayPool<char>.Shared.Return(single);
    }
}
```

   Update the class summary to add: "and, for PEM private keys, the single-line, body and body-line forms."

3. Test, in `tests/Dhadgar.Lab.Tests/Secrets/RedactorTests.cs`: add `Redact_SingleLinePemAndBody_MaskedAsTheVaultRef`.
   - Create a P-256 PKCS8 PEM (`ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem()`, disposed) and `string reference = vault.Add(SecretKind.PrivateKey, pem.AsSpan())`.
   - Assert `redactor.Redact($"x {LabEnvValue.SingleLinePem(pem)} y") == $"x ***{reference} y"`.
   - With `body` = the base64 between header and footer with line breaks removed, assert `Redact(body)` equals `***{reference}`.
   - Assert `Redact(firstBodyLine)` equals `***{reference}`.
   - It fails when E7 is reverted: the single-line form is then masked as `***pem`, not the vault ref, and the body is left unmasked.

### E8. Tests: `tests/Dhadgar.Lab.Tests/Stack/LabStackTests.cs` (review N4 a, b)

Add an overload `private LabStack CreateStack(out LabTargetRegistry registry, out LabCredentialRefs refs)`. The existing `CreateStack(out registry)` calls it and discards `refs`. Add two facts:

- `UpAsync_WritesExchangePublicKey_MatchingTheVaultPrivateKey`: `UpAsync(wipe: false)`. Read `Path.Combine(paths.State, "identity", "exchange-public.pem")`. Assert it contains `BEGIN PUBLIC KEY` and not `PRIVATE KEY`. Get the private PEM from `new LabConnectionStrings(_vault, refs).ExchangePrivateKeyPem()`, then import each side into its own `using var` `ECDsa` with `ImportFromPem` and `Assert.Equal(priv.ExportSubjectPublicKeyInfo(), pub.ExportSubjectPublicKeyInfo())`. Fails when `WriteExchangePublicKey` writes another key, or the private PEM.
- `StartBetterAuth_FakeDocker_SecretsOnlyInTheEnvFile_AndFileDeleted`:
  - Run `UpAsync(wipe: false)` first. **Only then** set `_docker.Answer`, because `UpAsync`'s own inspects must keep the defaults.
    - A `run` call containing `meridian-lab-betterauth`: record the args, read the file after `--env-file` with `File.ReadAllText` into `envText`, keep its path, return `new DockerOutput(125, "", "refused by the test")`.
    - A `container inspect` whose args contain `.Config.Labels`: return `new DockerOutput(1, "", "No such container")`.
    - Anything else: return `null`.
  - Build `LabServices` with `[new BetterAuthLabAdapter()]`, the same `_docker`, `registry`, `stack`, `new LabConnectionStrings(_vault, refs)`, `refs` and `_vault`. Construct the `ContainerLogFollower` (with a `LabProcessSupervisor`) as `AuthStackDockerTests.cs:43-53` does, plus `new ConfigOverlay(paths, validator)`, the validator, `paths` and `_time`. Then call `StartAsync("betterauth", "a9c623a1", CancellationToken.None)`.
  - Assert: the result failed with `could not start`; the env file no longer exists. `envText` contains `EXCHANGE_TOKEN_PRIVATE_KEY=-----BEGIN PRIVATE KEY-----` and `SECRETS_SERVICE_URL=http://127.0.0.1:8099`. The recorded args contain `--env-file`. For every reference in `_vault.Snapshot()` with length >= 8, its value (`_vault.Use(reference, v => v.ToString())`) appears in neither `string.Join(' ', args)` nor `string.Join(' ', args).Replace("\n", "")`.
  - Fails if a secret is passed as `-e KEY=value` or the `finally` delete is removed.

### E9. Tests: new `tests/Dhadgar.Lab.Tests/Build/NodeStagingTests.cs` (review N4 c, d; security N4)

- `NodeDockerBuildArgs_IsTheExactList`: `LabBuilder.NodeDockerBuildArgs("t", "dist/server/entry.mjs", "ctx", "df")` equals `["build", "--build-arg", "APP_DIR=app", "--build-arg", "APP_ENTRY=dist/server/entry.mjs", "--label", "meridian-lab=1", "-f", "df", "-t", "t", "ctx"]`.
- `StageNodeApp_CopiesSourceOnly`:
  - Under a temp `checkout`, create:
    - `src/Dhadgar.Fake/`: `package.json`, `src/a.ts`, `.env`, `.env.local`, `.npmrc`, `key.pem`, `cert.pfx`, `secrets.json`, `node_modules/x.js`, `dist/y.js`, `bin/z.dll`, `obj/w.json`
    - `src/Dhadgar.SharedAuth/src/index.ts`
    - `tools/lab/images/lab-secrets-stub.mjs`
  - `paths = LabPaths.FromRoot(home, checkout)`.
  - Call `LabBuilder.StageNodeApp(new BuildProject("fake", "src/Dhadgar.Fake/package.json", string.Empty, "src/Dhadgar.Fake", "src/a.ts"), checkout, Path.Combine(paths.Builds, "b1"), paths, new PathValidator(NullLogger<PathValidator>.Instance))`.
  - Assert the set of staged files, relative to the returned folder with `/` separators, equals exactly `{ "app/package.json", "app/src/a.ts", "SharedAuth/src/index.ts", "lab/secrets-stub.mjs" }`.
  - Fails when any exclusion is removed or the stub is not staged.
  - Clean up the temp folders in `finally`, inside try/catch for `IOException`.
- `IsStagedFile_RefusesCredentialAndKeyNames`: `[Theory]` over `.env`, `.env.example`, `.npmrc`, `.yarnrc.yml`, `.dev.vars`, `secrets.json`, `a.PEM`, `a.key`, `a.pfx`, `a.p12` → false; `package.json`, `index.ts` → true.

### E10. Tests: `tests/Dhadgar.Lab.Tests/Services/AuthStackDockerTests.cs` (review N2, security N7)

1. Rename the test to `AuthStack_SignUpThroughGateway` and make it `[SkippableFact]` with the existing traits.
2. Use the Docker context the way `Seams/EnrollHistorySeamTests.cs:29-40` does: `Lab__Docker__LinuxContext` or `ContainerLogFollower.DefaultLinuxContext`, and `Skip.IfNot` when `docker version` fails. Copy that file's `MERIDIAN_LAB_S6_WIPE` pre-check (`Assert.Fail` when labelled `meridian-lab-` containers exist and the variable is not `1`) and its private `RunDocker` helper. All of this goes **before** `stack.UpAsync(wipe: true)`.
3. After the health loop:

```csharp
string email = $"lab-{Guid.NewGuid():N}@lab.invalid";
string password = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));
using var signUp = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{LabPorts.Gateway}/api/v1/betterauth/sign-up/email")
{
    Content = JsonContent.Create(new { email, password, name = "lab" }),
};
signUp.Headers.Add("Origin", BetterAuthLabAdapter.TrustedOrigin);
using HttpResponseMessage signedUp = await http.SendAsync(signUp, ct);
// The body carries a session token: never print it.
Assert.True(signedUp.StatusCode == HttpStatusCode.OK, $"sign-up through the Gateway answered {(int)signedUp.StatusCode}");
```

   The Gateway route is `betterauth-route` (`src/Dhadgar.Gateway/appsettings.json:93-98`): `Anonymous`, `Auth` rate limiter, no prefix strip. The client already sends `LabServices.ProbeHostHeader`.
4. Wrap `Directory.Delete(home, recursive: true)` in `try { … } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }`.

### E11. Tests: S-L9 rewritten and moved (review N3, security N7)

`git mv tests/Dhadgar.Lab.Tests/Services/PanelLoginSeamTests.cs tests/Dhadgar.Lab.Tests/Seams/PanelLoginSeamTests.cs`, namespace `Dhadgar.Lab.Tests.Seams`. Replace the body with a daemon-driven test modelled on `Seams/EnrollHistorySeamTests.cs`. Copy its private helpers `CallToolAsync`, `TryReleaseAsync`, `RemoveLabContainers`, `RunDocker` and `NewToken`. One `[SkippableFact]` `PanelLogin_ReachesDashboard_RefreshRotates_ReplayRefused`, traits `Seam=S-L9`, `RequiresDocker=true`, `RequiresNetwork=true`, in this order:

1. Start the daemon (`LabDaemonOptions(Port: 0, Token: token, DataRoot: home, EnforceSingleInstance: false, RefuseElevated: false, RepoRoot: repoRoot)`). List its tools with a new private `ToolNamesAsync` helper: the same transport as `CallToolAsync`, then `client.ListToolsAsync(cancellationToken: CancellationToken.None)` and select the names. Assert `Assert.True(names.Contains("ui_flow_run"), "ui_flow_run is not registered; S-L9 stays red until L35")`. This comes **before** any Docker check, so the test is red with or without Docker.
2. Docker skip check and `MERIDIAN_LAB_S6_WIPE` guard (as in E10).
3. Under one lease: `lab_build` (`checkout = repoRoot`), `stack_up` (`wipe = true`), then `svc_start` for `nodes`, `identity`, `betterauth`, `panel`, `gateway`.
4. `ui_flow_run` with `flow = "login_email"`, `seed = 9`. Assert:
   - `ok` is true.
   - `new Uri(steps[^1].url).AbsolutePath == "/dashboard"`.
   - `storage.access_token_present` is true.
   - `storage.refresh_token_sha256_prefix` matches `^[0-9a-f]{8}$`.
5. `ui_flow_run` with `flow = "refresh"`, `seed = 9`. Assert:
   - `ok` is true.
   - The step named `refresh` has `ok` true (rotated: new prefix differs from old).
   - The step named `replay_refused` has `ok` true (old token refused).
   - The prefix is 8 hex.
6. `finally`: release the lease, run `RemoveLabContainers`, and delete `home` inside the same try/catch as E10.

Field names are LabJson snake_case, as in C14 with the amendment in section 7. The class summary says the test is red until L35 and that L35 must not change these assertions.

### E12. Tests: `tests/Dhadgar.Lab.Tests/Services/AuthStackAdapterTests.cs` and new `Services/BetterAuthSecretsStubTests.cs`

1. In `BetterAuth_PrivateKeyIsSingleLine_AndPostgresPartsAreSplit`, replace line 63 with:

```csharp
Assert.Equal("http://127.0.0.1:8099", env.Value["SECRETS_SERVICE_URL"]);
Assert.Equal("8099", env.Value["LAB_SECRETS_STUB_PORT"]);
Assert.Equal("--import=file:///lab/secrets-stub.mjs", env.Value["NODE_OPTIONS"]);
Assert.Equal("1", env.Value["USE_ENV_DB_PASSWORD"]);
Assert.False(env.Value.ContainsKey("IDENTITY_SERVICE_URL"));
Assert.DoesNotContain(env.Value.Keys, k => k.StartsWith("SERVICE_CLIENT_", StringComparison.Ordinal));
```

2. Add `Context_ToString_PrintsNoSecret` (security N2): `string text = Context().ToString();` contains `a9c623a1` and contains none of `CCCC`, `ba-secret-value`, `lab-password-value`, `bapw`, `Password=`.
3. New `BetterAuthSecretsStubTests.cs`. Repo root comes from `LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory)`.
   - `Stub_AndBetterAuthClient_AgreeOnPathsAndNames` (no Docker; the static half of the seam):
     - Read `tools/lab/images/lab-secrets-stub.mjs` and `src/Dhadgar.BetterAuth/src/secrets-client.js`. Assert each file contains `"betterauth-secret"`, `"betterauth-exchange-private-key"`, `/api/v1/secrets/betterauth`, `/api/v1/secrets/oauth` and `/connect/token`.
     - The stub contains `"127.0.0.1"`.
     - `node-app.Dockerfile` contains `COPY lab /lab` and `COPY SharedAuth /src/Dhadgar.SharedAuth`.
     - `src/Dhadgar.Panel/package.json` contains `file:../Dhadgar.SharedAuth`.
     - `BetterAuthLabAdapter.SecretsStubPath == "/lab/secrets-stub.mjs"`.
   - `Stub_FeedsTheRealLoadSecrets` (`[SkippableFact]`; `Skip.If` when starting `node --version` throws `Win32Exception` or exits non-zero; no new trait). It runs the product client against the stub, both sides together, without Docker.
     - Find a free loopback port with a `TcpListener` on `IPAddress.Loopback:0`, then stop it.
     - Start `node` with `ArgumentList` `--input-type=module`, `-e`, script. The script is:

       ```javascript
       import { loadSecrets } from "<file URL of secrets-client.js>";
       await loadSecrets();
       console.log(process.env.BETTER_AUTH_SECRET === process.env.EXPECT_SECRET && process.env.EXCHANGE_TOKEN_PRIVATE_KEY === process.env.EXPECT_KEY ? "ok" : "mismatch");
       ```

       The file URL comes from `new Uri(path).AbsoluteUri`.
     - Set in `psi.Environment`:
       - `NODE_OPTIONS=--import=<file URL of lab-secrets-stub.mjs>`
       - `LAB_SECRETS_STUB_PORT=<port>`
       - `SECRETS_SERVICE_URL=http://127.0.0.1:<port>`
       - `USE_ENV_DB_PASSWORD=1`
       - `NODE_ENV=production`
       - `BETTER_AUTH_SECRET` and `EXPECT_SECRET`: one 32-char random base64url value
       - `EXCHANGE_TOKEN_PRIVATE_KEY` and `EXPECT_KEY`: `LabEnvValue.SingleLinePem` of a fresh `ECDsa` P-256 `ExportPkcs8PrivateKeyPem()`
     - Remove `IDENTITY_SERVICE_URL`, `SERVICE_CLIENT_ID` and `SERVICE_CLIENT_SECRET` from `psi.Environment`.
     - `using var process`. Read stdout and stderr with `ReadToEndAsync` before `WaitForExitAsync` under a 60 s `CancellationTokenSource` (kill the process on timeout).
     - Assert exit code 0, stdout contains `Secrets loaded successfully`, and the last non-empty line is `ok`.
     - Fails when the stub drops a path or a secret name, or binds elsewhere than `SECRETS_SERVICE_URL`.

### E13. `src/Tools/Dhadgar.Lab/README.md`

1. Insert a blank line before `## Daemon edge` (review N7).
2. Replace the paragraph under the Services table with these paragraphs:
   - `stack_up` generates a per-stack ES256 exchange key pair. BetterAuth signs exchange tokens with the private key; Identity verifies them with the public key (`state\identity\exchange-public.pem`). The private key and the BetterAuth secret live only in the vault and reach the container through an env file. BetterAuth and the Panel are Node apps built with `tools/lab/images/node-app.Dockerfile`. Staging skips env files, `.npmrc`/`.yarnrc*`, `.dev.vars`, `secrets.json`, key and certificate files, `node_modules`/`dist`/`bin`/`obj` and reparse points. `lab_build` without `projects` builds these images too, which needs the npm registry.
   - BetterAuth always loads its secret and exchange key from the Secrets service at boot. The lab does not run Secrets: the image preloads `tools/lab/images/lab-secrets-stub.mjs` (`NODE_OPTIONS`), which answers `/connect/token` and `/api/v1/secrets/{betterauth,oauth,infrastructure}` on `127.0.0.1:8099` inside the container. It serves only values the env file carries. No product code changes (D-ML144).
   - Identity runs with `ASPNETCORE_ENVIRONMENT=Development` (auto-migrate, ephemeral signing key). Without Key Vault, `/webhooks/better-auth` (through the Gateway `/api/v1/identity/webhooks/better-auth`) accepts unsigned bodies, so any local process can soft-delete or rewrite a lab user. The OpenAPI and Scalar pages are served on `127.0.0.1:15010`. The lab is not a fidelity target for webhook signing.
   - `db_query service=betterauth` or `service=identity` returns session, account-token, verification and refresh-token columns unmasked (lab users, per-stack secrets). Masking is KG-W7-1 (L35).
   - Identity has no outbox tables, so the I2 outbox invariant covers Nodes only.

### E14. Worknote `docs/analysis/meridian-lab/worknotes/ml-l34.md`

In the session 3 line, replace "Identity /healthz and BetterAuth /healthz verified." with "Identity /healthz and BetterAuth /healthz read in source only (not run). BetterAuth has no env mode; it needs the lab Secrets stub (l34-ruling-1, D-ML144)." Append a `## Fix pass 1 (l34-ruling-1)` section listing E1-E15 as done, with the check results.

### E15. Commit

One commit on `ml/l34`: `fix(lab): apply L34 ruling 1 (betterauth secrets stub, staging, tests) [L34]`. It covers explicit paths only: the files named in E1-E14.

## 5. Checks (in order; `<W>` = `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l34`)

```powershell
dotnet build <W>\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "Seam!=S-L9&RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true"
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "Seam=S-L9"    # expected RED: message names ui_flow_run
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~AuthStackDockerTests"    # Docker (desktop-linux) running, network
```

- Check 3: the only allowed failure is `GatewayRouteTableTests.EveryGatewayRoute_IsInTheTable_WithTheSameCluster` (base `//` comments, not L34). Report its name from the output, not by count. `Stub_FeedsTheRealLoadSecrets` must **pass** where `node` is on PATH; report whether it ran or skipped.
- Check 5 is the acceptance check for D1. If Docker is unavailable, or labelled lab containers exist and `MERIDIAN_LAB_S6_WIPE` is not `1`, return `blocked` naming check 5. Do not report done. The orchestrator then runs check 5 before review pass 2.
- No `.csproj` or `Directory.Packages.props` change is expected. `System.Net.Http.Json` is in the shared framework. If a restore changes `packages.lock.json`, stop and report it.

## 6. How a reviewer checks this ruling was applied

1. `git diff 7429671e..HEAD --stat` touches only the files named in E1-E14 (plus the `git mv`).
2. Reading checks:
   - The Dockerfile has `COPY SharedAuth /src/Dhadgar.SharedAuth`, `COPY lab /lab` and the exec-form `CMD`.
   - The stub matches E2. In particular it checks `IDENTITY_SERVICE_URL`, binds `127.0.0.1` and has no `console.` call.
   - `BetterAuthLabAdapter` sets the four keys from E4 and none of `IDENTITY_SERVICE_URL`/`SERVICE_CLIENT_*`.
   - `LabServiceContext` has `PrintMembers`.
   - `grep -n "{ctx}\|{privateKey}\|{secret}" src/Tools/Dhadgar.Lab` is empty.
   - `csproj = validator` is fixed. The README has the blank line and the five paragraphs.
3. Mutation spot checks (run, then revert):
   - Delete the `"/api/v1/secrets/oauth"` entry from the stub: `Stub_FeedsTheRealLoadSecrets` and `Stub_AndBetterAuthClient_AgreeOnPathsAndNames` fail.
   - Remove `.npmrc` from `NodeExcludedFileNames`: `StageNodeApp_CopiesSourceOnly` fails.
   - Change `SecretForms` back to `return true;`: the new Redactor test (below) fails.
   - Delete `PrintMembers`: `Context_ToString_PrintsNoSecret` fails.
4. Runs: checks 3 and 4 as in section 5 (4 red, naming `ui_flow_run`). Check 5 green on a Docker machine. It is the proof that BetterAuth boots and that `POST /api/v1/betterauth/sign-up/email` through the Gateway answers 200.
5. A security pass 2 on the same range confirms D-ML144's properties (loopback-only, fail-closed preload, no new key path) and the E7 forms.

## 7. Ledger changes (orchestrator applies in the campaign worktree)

1. README findings, F22: replace "It loads secrets from the Secrets service at boot unless env mode is set" with "It always loads `BETTER_AUTH_SECRET` and `EXCHANGE_TOKEN_PRIVATE_KEY` from the Secrets service at boot (`server.js:22`, `secrets-client.js:269-307`; there is no env mode). `IDENTITY_SERVICE_URL ?? SECRETS_SERVICE_URL` serves `/connect/token`, and `USE_ENV_DB_PASSWORD` affects only the DB password." Also replace the cookie sentence with: "Cookies are host-only since HF-02 (no `Domain`, prefix `meridian`, `__Secure-meridian.session_token`; `src/Dhadgar.BetterAuth/CLAUDE.md`)."
2. Decisions: add **D-ML144**: "The lab BetterAuth gets its secrets from a loopback stub preloaded into its process (`tools/lab/images/lab-secrets-stub.mjs`, `NODE_OPTIONS=--import`, `127.0.0.1:8099` in the container, fail-closed). No product change; the real `loadSecrets` runs. Rejected: a product env mode (packet rule, D-ML40), a stub container (key on the lab network), running Secrets (Key Vault). | Accepted (architect) | F22; [l34-ruling-1](worknotes/l34-ruling-1.md)."
3. Known gaps: add **KG-W7-1**: "`db_query service=betterauth|identity` returns session/account tokens, verification values and refresh-token rows unmasked; persisted results can carry them (security L34 N6). | L35 | L35". L35 Files item 5 gains: "`LabDb` masks the values (keeps column names) of `session.token`, `account.accessToken`, `account.refreshToken`, `account.idToken`, `account.password`, `verification.value` and Identity's refresh-token hash column; test `DbQuery_TokenColumns_Masked`."
4. C14: in the `refresh` row, append: "Steps include `refresh` (ok when the refresh answered 200 and the stored refresh token's prefix changed) and `replay_refused` (ok when the old token, replayed to `{gateway}/api/v1/identity/refresh`, was refused with 400 or 401). Tool payload fields are LabJson snake_case (`steps[].url`, `storage.access_token_present`, `storage.refresh_token_sha256_prefix`)." L35's S-L9 line: "S-L9 (as written in L34, `Seams/PanelLoginSeamTests.cs`) goes green unchanged."
5. Packet table, L34 row: status "Fix pass 1 ([l34-ruling-1](worknotes/l34-ruling-1.md); D-ML144)". The L34 spec, Files item 3: replace "env mode (no Secrets service, F22)" with "Secrets through the loopback stub (D-ML144)".
6. PR: `security-review` label; security pass 2 required.
