# ml-l34 worknote (session 2, continuation 1 of 3)

DONE (committed as part 1): tools/lab/images/node-app.Dockerfile (ARG APP_DIR, APP_ENTRY, PUBLIC_GATEWAY_URL; context = staged folder with app/ and SharedAuth/).

ALREADY IN PLACE (no change needed):
- ProxyCatalog.cs has all L34 proxies (20011, 20013, 20030-20032, 20040).
- LabStack.LabDatabaseServices already has identity and betterauth; LabDataReset lists tables dynamically (stack_reset_data OK); LabDb allows identity/betterauth.
- LabPorts has Identity 15010, BetterAuth 15130, Panel 14321.

FACTS:
- BetterAuth env: EXCHANGE_TOKEN_PRIVATE_KEY (ES256 PKCS8 PEM), EXCHANGE_TOKEN_ISSUER, EXCHANGE_TOKEN_AUDIENCE, EXCHANGE_TOKEN_KID, BETTER_AUTH_SECRET, BETTER_AUTH_URL, BETTER_AUTH_CLIENT_APPS, POSTGRES_HOST/PORT/DATABASE/USERNAME/PASSWORD, IDENTITY_SERVICE_URL, SECRETS_SERVICE_URL (leave unset: env mode), PORT.
- Identity appsettings: ConnectionStrings__Postgres, Redis__ConnectionString, Auth__Exchange__PublicKeyPem (AuthOptions.PublicKeyPem), Auth__Exchange__Issuer/Audience = BetterAuth issuer https://meridianconsole.com/api/v1/betterauth and audience https://meridianconsole.com/api/v1/identity/exchange; Auth Issuer https://meridianconsole.com/api/v1/identity, audience meridian-api. Messaging: RabbitMq__Host/Port/Username/Password.
- Panel: package.json engines node >=22.23.4; file:../Dhadgar.SharedAuth dep; build script astro build; start node dist/server/entry.mjs.
- LabServices.ComposeEnvironment builds LabServiceContext; LabServices.StartAsync: Nodes-only nodes-ca volume; env via --env-file.
- LabBuilder.Projects: BuildProject(Name, Csproj, DllName) only dotnet; Dockerfile = dotnet-service.Dockerfile with context = publish dir.
- Gateway: GatewayAdapter.Environment sets Readyz__RequiredClusters__0=nodes; add __1=identity and ReverseProxy__Clusters__identity__Destinations__d1__Address=http://meridian-lab-toxiproxy:20011/ and betterauth cluster 20013; GatewayAdapter.Proxies add gateway-identity, gateway-betterauth; OverlayKeys add Readyz:RequiredClusters:4 ... (check).
- LabStack.EnsureCredentials creates secrets via NewSecret() for CredentialNames (LabCredentialRefs in Stack/LabConnectionStrings.cs).
- LabStack.cs registers adapters via LabDaemon.cs line ~303 (AddSingleton<ILabServiceAdapter, NodesAdapter>).

PLAN (decided):
1. LabCredentialRefs: add IdentityExchange = "identity-exchange" (ECDSA P-256 PKCS8 PEM in vault) and BetterAuthSecret = "betterauth-secret" (NewSecret). Add both to CredentialNames. EnsureCredentials: IdentityExchange -> NewExchangeKey(): ECDsa.Create(nistP256), ExportPkcs8PrivateKey bytes -> PemEncoding.Write -> vault.Add(kind, span) (zero bytes/chars); write public SPKI PEM to LabPaths.State\identity\exchange-public.pem.
2. LabServiceContext: add optional params ExchangePublicKeyPem=null, ExchangePrivateKeyPem=null, BetterAuthSecret=null (default keeps existing callers compiling).
3. ComposeEnvironment: read public PEM from file, private PEM and secret from vault via _vault.Use(ref, v => v.ToString()).
4. IdentityLabAdapter (Name identity, Container meridian-lab-identity, Project identity, port 15010, proxies identity-postgres, identity-redis, identity-rabbitmq, health /healthz?) env per spec; ASPNETCORE_ENVIRONMENT=Development.
5. BetterAuthLabAdapter (Name betterauth, port 15130, proxy betterauth-postgres; env above; BETTER_AUTH_URL=http://localhost:14321 ? trusted origins http://localhost:14321).
6. PanelLabAdapter (Name panel, port 14321, no proxies, health /login, Project panel, Container meridian-lab-panel).
7. LabBuilder: add BuildProject entries for betterauth (Node) and panel (Node) with a Node path: stage app/ + SharedAuth/ into a temp folder under Builds, docker build with node-app.Dockerfile and --build-arg APP_DIR=app APP_ENTRY=... ; identity as dotnet project src/Dhadgar.Identity/Dhadgar.Identity.csproj (Dhadgar.Identity.dll).
8. Register adapters in LabDaemon.cs (AuthStack area per R-REG) and in LabServices StartAsync: Node builds use image from build; WaitReady uses health path.
9. Gateway adapter update (see above). Readyz RequiredClusters = [nodes, identity].
10. I2OutboxInvariant: check Identity has OutboxMessage model (grep found none in Identity src with pattern 'OutboxMessage' — verify in src/Dhadgar.Identity and BetterAuth is not EF).
11. Tests: adapter env tests (tests/Dhadgar.Lab.Tests/Services/AuthStackAdapterTests.cs), PanelLoginSeamTests (S-L9, RequiresDocker, RequiresNetwork), AuthStackDockerTests (RequiresDocker, RequiresNetwork).
12. README section (src/Tools/Dhadgar.Lab/README.md "Services": Identity, BetterAuth, Panel).

DONE (session 2, part 2): step 1. LabCredentialRefs.IdentityExchange/BetterAuthSecret added; LabStack.EnsureCredentials returns Result, generates P-256 PKCS8 key into vault (NewExchangeKey) and writes state\identity\exchange-public.pem (WriteExchangePublicKey). Caller in StackUp checks Result.

NEXT STEP: step 2 (LabServiceContext optional ExchangePublicKeyPem/ExchangePrivateKeyPem/BetterAuthSecret; ComposeEnvironment fills them), then steps 4-6 adapters.

LAST CHECK: build Dhadgar.Lab Release TreatWarningsAsErrors -> Build succeeded, 0 warnings, 0 errors.

## Session 3 (continuation 3 of 3, last)
DONE: part 3 (a75d6893, env wiring), part 4 (8a63e685): LabEnvValue.cs (EscapedNewlinePem for Identity, SingleLinePem for BetterAuth since jose importPKCS8 needs no literal \n), IdentityLabAdapter (15010), BetterAuthLabAdapter (15130; DATABASE from POSTGRES_* parsed from ServiceDb string; BETTER_AUTH_TRUSTED_ORIGINS=14321, NODE_ENV unset), PanelLabAdapter (14321). Identity /healthz and BetterAuth /healthz read in source only (not run). BetterAuth has no env mode; it needs the lab Secrets stub (l34-ruling-1, D-ML144).
NEXT STEP: (4) register 3 adapters in LabDaemon.cs ~line 303 AddSingleton; LabBuilder.Projects add identity (dotnet src/Dhadgar.Identity/Dhadgar.Identity.csproj) + Node projects betterauth/panel via node-app.Dockerfile (needs BuildProject extension: staging of app + SharedAuth). Then (5) GatewayAdapter, (6), (7) tests, (8) checks.


## Session 4 (fallback implementer)
DONE: part 5 LabBuilder/LabDaemon (6e0d22f2); part 6 GatewayAdapter (e2f1d4d1); part 7 tests + README (293caeb8). I2OutboxInvariant: Identity has no Outbox in src/Dhadgar.Identity, so I2 stays Nodes-only (packet: "if its model has them"). stack_reset_data lists tables dynamically; identity and betterauth are lab DBs already.
Tests authored, not run (Docker): AuthStackDockerTests, PanelLoginSeamTests (S-L9).

## Fix pass 1 (l34-ruling-1)
Applied E1-E15. Production (commit 6fd8c45c): E1-E7, E13 (node-app.Dockerfile, lab-secrets-stub.mjs, LabBuilder staging + deny-list, BetterAuthLabAdapter stub env, LabServiceContext.PrintMembers, SecretForms PEM forms, README). Tests: E8-E12 (RedactorTests, LabStackTests, NodeStagingTests, AuthStackAdapterTests, BetterAuthSecretsStubTests, AuthStackDockerTests, Seams/PanelLoginSeamTests moved from Services/). E14 this note. E15 report.

Checks:
- Build Lab and Lab.Tests Release, TreatWarningsAsErrors: pass, 0 warnings.
- Targeted run (stub, staging, adapters, redactor, stack): 59/59 pass, 0 skipped (node stub test ran).
- Full filtered run: see report. Only allowed failure GatewayRouteTableTests.EveryGatewayRoute_IsInTheTable_WithTheSameCluster.
- No packages.lock.json or csproj changes.
- Check 5 (AuthStackDockerTests, Docker): NOT RUN, Docker forbidden. Orchestrator must run it before review pass 2.
- Deviation: the node script in Stub_FeedsTheRealLoadSecrets exits explicitly after flushing stdout, because the stub listener keeps node alive.
