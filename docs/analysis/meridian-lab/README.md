# Campaign: meridian-lab

A full MCP lab daemon that lets a Claude session take hold of every real piece of MeridianConsole and work it: drive it, break it on purpose, check that it breaks the way we expect, and hunt for the ways nobody predicted. The model is Microsoft's Windows Test Technologies (WTT/HLK) apparatus: a machine pool with dimensions and constraints, an expected-failure catalog, always-on invariants, a results database with failure buckets, known issues and flake detection. The operating shell is Cimmeria's lab (`cimmeria-lab`): one per-user MCP daemon, a lease, deny-by-default tools, graded UAT with evidence bundles.

- Packets and contract: [work-packets.md](work-packets.md). Resume card: [RESUME.md](RESUME.md).
- Owner goal (Steve, 2026-10-27): "the FULL MCP daemon ... not a lightweight script runner"; and "string up each piece of MeridianConsole like a marionette and make it dance the polka, drag it through the mud, make sure it breaks in exactly the ways we expect it to and find the ways it breaks in ways we could never predict."
- This campaign **supersedes** the lab and UAT-automation scope of `sprint2-agent-nodes` (its wave 2U script, P18 to P23) as the lasting apparatus. Sprint 2 shipped its script and passed UAT in all three modes; wave 8 here ports those legs (D-ML17). This campaign never edits `docs/analysis/sprint2-agent-nodes/`.
- Dev and lab only. Nothing here is referenced by a product project, shipped in a product image or chart, or wired into production (D-ML2, `LabIsolationTests`). Lab images live only under `tools/lab/images/`.
- **Rework 2026-10-27.** The plan was rebuilt before any packet ran. The elevated helper (old L19 to L21, D-ML12, D-ML13) is gone: agents run inside Windows Server Core containers, so nothing touches the workstation's LocalMachine. Added: fault injection, the expected-failure catalog, invariants, seeded hunting, the results DB and triage, the machine pool, Panel UI automation and the real services. L01 to L18 keep their IDs and most of their text.
- **Second pass 2026-10-27.** A review of another model's proposal (taken as input, every claim checked) fixed four defects in the first pass (F50, D-ML46), reserved observation, identity, fingerprint, pairing and replay-fidelity contracts in the foundation waves (C18, L48), and added history checking (wave 4H), twins and planted defects with a self-check (wave 5E), the tenant-isolation twin (L56), behavioural coverage (L52) and wave 9: an adversarial peer, recovery envelopes, synthetic-game continuity, baselines with mixed-version and time rows, and an opt-in guided mode. Faults and the peer target only lab-owned members and identities. 63 packets.

## Scope

| In (real code now) | Out until promoted (D-ML33) |
| --- | --- |
| Gateway, Nodes, Identity, BetterAuth and SharedAuth (through the Panel), Notifications, Discord (webhook path), CLI | Servers, Tasks, Files, Console, Mods, Billing (stubs without auth, DV-13) |
| Windows agent (console, service, MSI) inside Windows Server Core containers; GameServerWrapper beside it | Linux agent (follow-up; `Agent.Linux`) |
| Panel UI flows with Playwright .NET (login, refresh, logout, dashboard guard) | Discord bot gateway (needs a real bot token, D-ML41) |
| Fault injection: Toxiproxy between every service and its dependencies and between services; kill, pause, disk, corrupt-file, corrupt-registry and DB faults | Faults or peer traffic aimed at anything not created by the lab: the dev Aspire stack, its volumes, host processes (D-ML21, D-ML52) |
| History checking over enrollment (Jepsen-style, linearizability with a barrier proxy); twins (equal outcome, tenant isolation); lab-only planted mutants and a self-check | Coyote systematic concurrency testing (stale, F48; D-ML48) |
| An adversarial peer using lab-enrolled identities; recovery envelopes; synthetic-game continuity; baseline builds, mixed-version pairs, interrupted-migration, resurrection and rollback rows | End-to-end gameplay started by the control plane (no command sender, F46); DB-restore resurrection (needs a snapshot tool) |
| Behavioural coverage (`coverage_report`); guided mode behind an A/B (D-ML57) | Code-coverage instrumentation (D-ML55) |
| Expected-failure catalog per real service; invariants I1 to I7 on every run | VM tier (Hyper-V or Windows Sandbox: client SKUs, reboots, clock skew; follow-up, D-ML31) |
| Seeded hunting: model-based testing with shrinking, contract fuzzing, hub and `CommandEnvelope` fuzzing, chaos and soak runs, a fleet of hundreds of simulated agents on the real `Dhadgar.Agent.Core` | Coverage-guided fuzzing (SharpFuzz; follow-up) |
| Results DB, signatures and buckets, known issues, flakes, trends, triage ledger, GitHub issue **drafts** | Filing issues automatically (never; D-ML28) |
| Machine pool with dimensions and constraints: Linux containers, Windows containers, the host (reads only) | Changing the system clock; any MACHINE-tier tool on the workstation (D-ML43) |
| Shared per-user daemon, lease, tool tiers, audit, secret vault, redactor, `uat-driver` agent, `lab-uat` skill, docs, ADR | Remote or LAN access to the daemon |

## Findings

| ID | Finding | Evidence | Packet |
| --- | --- | --- | --- |
| F1 | Cimmeria's lab is one shared per-user daemon (`--http 127.0.0.1:8779`, scheduled task), bearer token compared in constant time with one opaque 401, loopback Host allowlist, every `Origin` refused, named mutex plus pidfile, exit 2 on config refusal and 3 when already running. `/status` is behind the bearer and never returns a lease id. | Cimmeria `crates\lab\src\daemon\mod.rs`, `daemon\auth.rs`, `docs\architecture\live-research-lab.md` §11; survey `docs/analysis/sprint2-agent-nodes/worknotes/cimmeria-lab-survey.md` | L02 |
| F2 | Cimmeria's `LeaseBook`: ttl default 600 s (30 to 3600), owner and purpose required, refusal names holder/purpose/since/expiry, `force` needs a `reason` and logs WARN, history records released/expired/taken_over, guarded calls renew, sweeper every 5 s, watchdog relaunches only under a lease, one `lab.lease` event per change, arguments never logged. | `crates\lab\src\lease\mod.rs` | L03 |
| F3 | Cimmeria classifies every tool (`Gate::Open/Lease/OwnLease`); tests `no_tool_is_in_two_lists` and `every_routed_tool_is_classified`. An unlisted tool is guarded there; we go one step stricter and deny it. | `crates\lab\src\lease\policy.rs` | L05 |
| F4 | Cimmeria UAT: TOML specs (`schema = 1`, `[[row]]`, steps, `[[row.expect]]` clauses with sources and ops), `${var}` and capture, `depends_on`, `plan_only`; grades PASS/FAIL/BLOCKED/SKIPPED/NEEDS_HUMAN/UNVERIFIED; PASS only if every required clause was checked and held. Bundle `run.json` + `rows/` + `ledger.md`. | `docs\guides\automated-uat.md`; survey | L13-L16 |
| F5 | Cimmeria's `lab-driver` agent is Haiku, low effort, 50 turns, MCP tools only, exact steps, raw results, retry once unchanged, stop on surprise; the `lab-uat` skill verifies the driver against the bundle. | Cimmeria `.claude\agents\lab-driver.md`, `.claude\skills\lab-uat\SKILL.md` | L18 |
| F6 | The repo is .NET 10, CI is `ubuntu-24.04` building `Dhadgar.sln` with `CI=true` (warnings as errors, `AnalysisMode AllEnabledByDefault`), tests filtered `RequiresDocker!=true`, lock files, Central Package Management. A .NET daemon builds and tests in the existing CI. | `global.json`, `Directory.Build.props`, `.github/workflows/_dotnet-build-test.yml` | L01 |
| F7 | Latest stable on nuget.org (checked 2026-10-27): `ModelContextProtocol.AspNetCore` 2.3.0, `Tomlyn` 2.10.1, `CsCheck` 5.0.0, `FsCheck` 3.6.0, `Microsoft.Playwright` 1.64.1, `Microsoft.Data.Sqlite` 10.0.14, `Testcontainers.Toxiproxy` 4.16.0, `Toxiproxy.Net` 2.0.1 (last release 2017), `SharpFuzz` 2.3.0. Toxiproxy server v2.13.0. Already in CPM: `System.CommandLine` 2.0.14, `Npgsql` 10.0.3, `Microsoft.AspNetCore.SignalR.Client` 10.0.14, `Xunit.SkippableFact`, `Microsoft.Extensions.TimeProvider.Testing` 10.11.0, the `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5 security pin. | nuget.org, GitHub releases, `Directory.Packages.props` | L01, L19, L25, L27, L35 |
| F8 | No `tools/` folder, no `src/Tools/`, no root `.mcp.json`, no JavaScript test runner (no vitest, jest or Playwright) exist. | repo tree | L01, L06, L35 |
| F9 | Nodes has no user-auth path to mint enrollment tokens. `IEnrollmentTokenService.CreateTokenAsync(orgId, createdByUserId, label, validity, ct)` returns `(EnrollmentToken, PlainTextToken)`, so the lab mints through the product's own service against the lab DB. | `src/Dhadgar.Nodes/Services/IEnrollmentTokenService.cs` | L11 |
| F10 | Nodes maps `POST /agents/enroll` (anonymous, body `Dhadgar.Contracts.Nodes.EnrollNodeRequest`, 201 `EnrollNodeResponse`, 401 `invalid_token`); the Gateway exposes it as `/api/v1/agents/enroll`. This seam exists on `main`. | `src/Dhadgar.Nodes/Endpoints/AgentEndpoints.cs:24`; `src/Shared/Dhadgar.Contracts/Nodes/EnrollmentContracts.cs:62` | L01 (red), L11 (green) |
| F11 | Nodes needs Postgres and RabbitMQ (own MassTransit section `RabbitMq:Host/VirtualHost/Username/Password`, EF outbox `UsePostgres`, tables `InboxState`, `OutboxState`, `OutboxMessage`), auto-migrates in Development only (`Program.cs:171`; `DatabaseMigrationExtensions.cs:27` returns outside Development, line 39 logs `Applying migrations for {DbContext}...`), initialises its CA with `LocalFileCaStorageProvider`. Consumers: CapacityReserved, CapacityReleased, CapacityReservationExpired, NodeDegraded, NodeOffline. | `src/Dhadgar.Nodes/Program.cs` | L08, L09, L21 |
| F12 | The Windows agent writes LocalMachine state: service `DhadgarAgent` (LocalSystem), `HKLM\SOFTWARE\Meridian Console\Agent\EnrollmentToken`, client certificate in `LocalMachine\My`, a CA store, a state file under `%ProgramData%`. Console mode still needs admin. Inside a Windows container all of that is the container's own (own SCM, HKLM, cert stores, ProgramData), so the workstation is never touched. | `src/Agents/Dhadgar.Agent.Windows/Program.cs:40`; Sprint 2 P11, P14 | L31, L32 |
| F13 | Agent footprint names are private constants in the agent. A lab that keeps its own copies would drift: the seam defect class. | `Program.ServiceName`, `WindowsCertificateStore.CaStoreName` (Sprint 2) | L12 (red S-L3), L39 |
| F14 | Agent.Windows targets `net10.0` with RIDs win-x64, win-arm64 and linux-x64, so it builds on ubuntu CI. A RID-specific `dotnet publish -r` rewrites lock files (Sprint 2 D-S241); the lab publishes portable (no `-r`) after a locked restore. | `Dhadgar.Agent.Windows.csproj`; Sprint 2 D-S241 | L09, L31 |
| F15 | Service test hosts run in the `Testing` environment, so DI scope validation is off; a singleton capturing a scoped `DbContext` hid in Nodes until Sprint 2 P07. The daemon adds many singletons. | architect memory `di-scope-validation-off-in-tests` | L05 (R-DI) |
| F16 | Sprint 2 is on `main` (squash `6df3338`, PR #235, 2026-10-27). It added the AgentHub (`/hubs/agent`, `IAgentHub.Heartbeat(AgentHeartbeat) → AgentHeartbeatAck`, `IAgentHubClient.ReceiveCommand(string)`, `ReceivePing`, header `X-Agent-Protocol: 1`, mTLS identity), `AgentCertificate.GraceUntil`, `Mtls:RenewalGraceMinutes`, the renewal threshold, the CA store move, the state directory and the MSI. UAT passed in Process, Service and Msi modes. | `git log` `6df3338`; Sprint 2 README and RESUME | wave 8 (D-ML60) |
| F17 | Sprint 2's owner rulings that this lab adopts: D-S222(A) run-scoped localhost TLS root in `LocalMachine\Root` (here: the container's own Root), D-S223(B) MSI leg included, D-S239 agents run elevated UAT, D-S241 no bare `publish -r`. | Sprint 2 README decisions | D-ML14, D-ML15, D-ML44 |
| F18 | WTT/HLK concepts the owner asked for: machines in a pool described by dimensions, jobs with constraints that select machines, a results database, failure buckets by signature, known issues so known failures don't hide new ones, flake detection by rerun. | owner brief 2026-10-27 | L25, L26, L30 |
| F19 | Toxiproxy's HTTP API: `GET/POST /proxies`, `POST /proxies/{name}` (`enabled=false` takes it down), `POST/DELETE /proxies/{name}/toxics`, `POST /reset`. Toxics: `latency`, `bandwidth`, `timeout`, `reset_peer`, `slow_close`, `slicer`, `limit_data`, each with `stream` upstream or downstream and `toxicity`. `Toxiproxy.Net` is unmaintained, so the lab writes a small client over `HttpClient`. | Toxiproxy README v2.13.0; nuget.org | L08, L19 |
| F20 | CsCheck 5.0.0 has seeded generators, shrinking, `SampleModelBased` and `SampleParallel` in C#; FsCheck 3.6.0 is F#-first. CsCheck 5.0.0 was released 3 days before this plan. | nuget.org, CsCheck README | L27, L28, L44 |
| F21 | Panel: Astro 7 SSR (`@astrojs/node` standalone, `node dist/server/entry.mjs`), React 19, Node `>=22.23.4`. Pages `/`, `/login`, `/callback`, `/logout`, `/dashboard`; only auth calls are real (dashboard shows hardcoded zeros; `/servers`, `/nodes`, `/settings` are 404). `PUBLIC_GATEWAY_URL` (default `http://localhost:5000`) is inlined at build time. Tokens live in `sessionStorage` (`meridian_access_token`, `meridian_refresh_token`, `meridian_expires_at`). | `src/Dhadgar.Panel/package.json`, `src/lib/auth/client.ts:3` | L34, L35 |
| F22 | BetterAuth (Express 5, better-auth ^1.6.25, port 5130) enables `emailAndPassword` with `requireEmailVerification: false` (`auth.js:219-222`), so `POST /api/v1/betterauth/sign-up/email` and `/sign-in/email` work without OAuth. The session cookie domain is hardcoded to `meridianconsole.com` (SameSite=None, Secure, HttpOnly, Partitioned, `auth.js:248-257`), which is why localhost login fails (DV-12). Exchange: `POST /api/v1/betterauth/exchange` → `{exchangeToken}` (60 s) → `POST {gateway}/api/v1/identity/exchange`. It loads secrets from the Secrets service at boot unless env mode is set, and migrates at start. No test login bypass exists. | `src/Dhadgar.BetterAuth/auth.js`; `docs/PROJECT-STATE.md` DV-12 | L34, L35 |
| F23 | Identity (port 5010) needs Postgres, **Redis** (exchange replay store) and RabbitMQ (except `Testing`); `/exchange` and `/refresh` are anonymous; the exchange public key comes from Key Vault, then `Auth:Exchange:PublicKeyPem`, then a path, then an ephemeral key outside Production; issuer and audience must match BetterAuth's; auto-migrates in Development (`Program.cs:801`). SharedAuth refreshes through `{gateway}/api/v1/identity/refresh`. DV-3 (refresh rotation) has no cross-component test. | `src/Dhadgar.Identity/Program.cs`; `docs/PROJECT-STATE.md` DV-3 | L34, L35 |
| F24 | `AddDhadgarMessaging` reads `RabbitMq:Host` (then `ConnectionStrings:RabbitMqHost`, then `localhost`), throws without `RabbitMq:Username`/`Password`, and uses the delayed scheduler, which needs the `rabbitmq_delayed_message_exchange` plugin. Neither the AppHost nor the compose files enable that plugin. | `src/Shared/Dhadgar.Messaging`; grep of `src/Dhadgar.AppHost`, `deploy/compose` | L08, L26 (known issue) |
| F25 | Notifications (`dhadgar_notifications`) registers an EF outbox with `UseBusOutbox`, but its model appears to lack the outbox tables. Email goes only through `Office365EmailProvider` (Graph; cannot be faked); `SmtpEmailSender` and `DiscordWebhookClient` exist but are not registered. Consumers: ServerStarted, ServerStopped, ServerCrashed, SendEmailNotification. | `src/Dhadgar.Notifications/Program.cs` | L36, D-ML40 |
| F26 | Discord's `/healthz` returns 503 unless the Discord.Net bot is connected; `Discord:WebhookUrl` can point at a lab sink; its `SecretsService:Url` fallback is `:5080`, which is the Mods port. | `src/Dhadgar.Discord/Program.cs` | L36, L26 (known issue) |
| F27 | Compose sets `ConnectionStrings__RabbitMqHost`, which loses to appsettings `RabbitMq:Host=localhost`; the lab must set `RabbitMq__Host` explicitly. | `docker-compose.services.yml`; `AddDhadgarMessaging` order | L09, L26 (known issue) |
| F28 | Gateway: no DB or bus, Redis optional, `Readyz:RequiredClusters` is `[identity, secrets]` (`appsettings.json:60`), `Cloudflare:EnableDynamicFetch=true` makes an outbound fetch at start. Health endpoints for every service come from `MapDhadgarDefaultEndpoints`: `/healthz`, `/livez`, `/readyz` (also `/health`, `/alive`). | `src/Dhadgar.Gateway/appsettings.json`; `Dhadgar.ServiceDefaults` | L09, L34 |
| F29 | Error shape: RFC 7807 from `GlobalExceptionHandler` (`traceId`, `correlationId`, `timestamp`, `errors`); `ProblemDetailsHelper` adds `errorCode` and `type` `https://errors.meridianconsole.com/...`; codes in `Problems/ErrorCodes.cs`. This is the oracle for fuzzing and the key for signatures. | `src/Shared/Dhadgar.ServiceDefaults` | L26, L28 |
| F30 | The CLI keeps config in `~/.dhadgar/config.json` through `GetFolderPath(UserProfile)` with no override; `auth login` is client_credentials against `/connect/token`; Notifications and Discord admin calls go direct with `X-Admin-Api-Key`. Running it on the host would touch Steve's real config, so it runs in a Linux container with its own `HOME`. | `src/Dhadgar.Cli` | L37 |
| F31 | `Dhadgar.Agent.Core` can host many agents in one process: one `ServiceProvider` per agent via `AddAgentCore()`, no static mutable state. Per agent, replace `ICertificateStore` (only the Windows one exists; Sprint 2 tests have `InMemoryCertificateStore`), `IAgentStateStore`, `ISystemMetricsCollector`, `IHardwareInventoryCollector`, `IProcessManager`, `IHttpClientFactory`. Avoid `AgentHostBuilderExtensions` (reads the command line). Templates: Sprint 2 `tests/Dhadgar.AgentSeam.Tests/` (`GatewayNodesHarness`, `AgentBootstrapSeamTests`). `HeartbeatService` uses the real clock. | `src/Agents/Dhadgar.Agent.Core`; Sprint 2 tests | L42 |
| F32 | `CommandEnvelope` lives in `Agent.Core/Commands`, not Contracts (`CommandId`, `CommandType`, `NodeId`, `OrganizationId`, `InitiatedByUserId?`, `IssuedAt`, `ExpiresAt?`, `PayloadJson`, `Signature?`, `Priority`, `CorrelationId?`); signature verification is open issue #94. | `src/Agents/Dhadgar.Agent.Core/Commands/` | L44 |
| F33 | Host: Windows 11 Pro for Workstations 26H2 GA, build 26300.9711. The Hyper-V management service (vmms) is absent, so Hyper-V is likely off. Docker Desktop is installed but stopped; WSL Ubuntu is present. Windows Server 2025 container images are build 26100. Whether Server Core containers run here, with which isolation, and beside the Linux engine, is unknown. | host checks 2026-10-27 | L00 |
| F34 | The dev AppHost names its resources `postgres`, `cache`, `messaging`, uses volumes `dhadgar-postgres-data`, `dhadgar-redis-data`, `dhadgar-rabbitmq-data`, and dev RabbitMQ management is on 15672. These names form the blast-radius tripwire; the lab's ports avoid them. | `src/Dhadgar.AppHost/Program.cs` | L08, L19 |
| F35 | Servers, Tasks, Files, Console, Mods and Billing are stubs without auth (DV-13). Testing them now would flood triage with one known gap. | `docs/PROJECT-STATE.md` DV-13 | L24 (manifest) |
| F36 | GameServerWrapper is Windows-only: service `MeridianGS_<id>`, named pipe to the agent's `AgentPipeServer`; it never talks to the control plane. | `src/Agents/Dhadgar.Agent.GameServerWrapper` | L43 |
| F37 | Every service test factory uses EF InMemory and no repo test runs Testcontainers, so migrations, the outbox and real broker behaviour are untested today. The lab is their first real exercise. | `tests/*/…WebApplicationFactory.cs` | L09, L21 |
| F38 | Nodes authenticates only agents. Its one scheme is mTLS (`MtlsMiddleware.cs:318`); it ignores the Gateway's `X-Tenant-Id` (`RequestEnrichmentMiddleware.cs:57`); `TenantScoped` needs an authenticated user with an `org_id` claim (`Program.cs:142-150`) that no scheme produces. Every user route (list nodes, mint tokens, decommission) is 401 in the lab today, so the lab mints and decommissions through adapters, and M2 and M3 are `unreachable` under `KI-NODES-USER-AUTH`. | files named | L11, L56, L58 |
| F39 | Enrollment records the node in `UsedByNodeId` and gives it a name unique per organisation (`EnrollmentService.cs:308-353`; the organisation filter at 320 and 334). | `src/Dhadgar.Nodes/Services/EnrollmentService.cs` | L50 (E6), L56 (M7) |
| F40 | Concurrent enrollment is guarded by the `UsedAt` concurrency token; the loser's `DbUpdateConcurrencyException` is caught and answered 401 `invalid_token` (`EnrollmentService.cs:225`). | same file; `EnrollmentTokenConfiguration.cs` | L50 (E1), L54 (M1, M4) |
| F41 | An enroll whose response is lost, retried with the same token, gets 401 and leaves a node the agent never learned about (an orphan). | F40's code path | L51, `KI-ENROLL-RETRY`, D-ML49 |
| F42 | Hub gaps: a missing `X-Agent-Protocol` header counts as current (`AgentHub.cs:37`); a certificate with no DB row is accepted (`AgentHub.cs:132-133`); two connections with one certificate both join the node group; only `Heartbeat` re-checks the certificate (`AgentHub.cs:75-79`), so an idle connection is never re-checked. | `src/Dhadgar.Nodes/Hubs/AgentHub.cs` | L58, D-ML53, D-ML54 |
| F43 | Renewal supersedes the presented certificate with grace (`AgentEndpoints.cs:257-263`: `IsRevoked`, `RevokedAt`, `GraceUntil = now + RenewalGraceMinutes`) and hard-revokes the node's other certificates (`RevokeWithoutGrace`); `IsUsable` is `!IsRevoked \|\| GraceUntil > now`; a wrong thumbprint is 401 `thumbprint_mismatch`. Decommission hard-revokes every certificate (`NodeService.cs:233-272`). | files named | L58 (M6, `stale-cert-after-renewal`, `post-revocation`) |
| F44 | The Windows process manager puts children in a job object with `KillOnJobClose` (`WindowsProcessManager.cs:584`, `763`), so a game started by the agent dies with the agent process. | `src/Agents/Dhadgar.Agent.Windows` | L59, D-ML56 |
| F45 | GameServerWrapper connects to the agent's pipe once (`GameServerHostedService.cs:139-173`) and its read loop ends when the pipe closes (189-201); it never reconnects. | `src/Agents/Dhadgar.Agent.GameServerWrapper` | L59 |
| F46 | Nothing sends commands: `IAgentHubClient.ReceiveCommand` exists (`AgentHubContracts.cs:52`), but no code uses `IHubContext<AgentHub>`. A control-plane-started game cannot be tested end to end yet. | Grep of `src/` | L59 |
| F47 | The repo has no git tags, so baselines are named by full commit SHA: `sprint2` = `6df3338…`, `pre-sprint2` = `2d8e4d0…`. | `git tag` (empty) | L60 |
| F48 | Microsoft Coyote's last release is 1.7.11 (2024-03-18); it needs binary rewriting of the code under test. Too stale to build on. | nuget.org | D-ML48 |
| F49 | Toxiproxy's README does not say whether a toxic added to a proxy applies to connections already open. **Assumption** until L51 checks it; the barrier proxy does not depend on it. | Toxiproxy README v2.13.0 | L49, L51 |
| F50 | Four defects in the first pass: (1) C8 had no `incomplete` outcome, so an all-skipped run read `pass`; (2) C12 matched known issues by the row's claim, not by its signatures; (3) C11 I1 let any active fault anywhere excuse a 5xx; (4) L19 wrote the fault journal after inject, so a crash could leave an unjournalled fault. | the first-pass C8, C11, C12 and L19 text | D-ML46 (L15, L19, L21, L25) |
| F51 | Sprint 2's `AgentCertificate.GraceUntil` is on `main`, so L11 shifts it from the start and L40 only confirms it against the W8 ruling. | `6df3338` | L11, L40 |

## Decisions

| ID | Decision | Status | Reason |
| --- | --- | --- | --- |
| D-ML1 | Stack: .NET 10 C#, ASP.NET Core minimal hosting, `ModelContextProtocol.AspNetCore` 2.3.0 (streamable HTTP, stateless), `Tomlyn` 2.10.1, `System.CommandLine`. | Accepted | F6, F7. |
| D-ML2 | Layout (reworked): `src/Tools/Dhadgar.Lab` (daemon and `meridian-lab` CLI, one exe), `src/Tools/Dhadgar.Lab.Probe` (small portable exe used inside pool members and as the webhook sink), `tests/Dhadgar.Lab.Tests`, `tests/Dhadgar.Lab.TestChild`, scripts in `tools/lab/`, lab Dockerfiles in `tools/lab/images/`, specs in `docs/guides/uat-specs/`, root `.mcp.json` entry `meridian-lab`. No product project, product Dockerfile, chart or AppHost references them; `LabIsolationTests` fails if one does. No elevated helper. | Accepted | F8; isolation is a hard rule. |
| D-ML3 | Daemon edge: loopback only `127.0.0.1:8789` and `[::1]:8789`, bearer from user env `MERIDIAN_LAB_TOKEN` (32 bytes min, constant time, one opaque 401), Host allowlist else 403, any `Origin` 403, single instance, refuses to start elevated (exit 2), per-user scheduled task. | Accepted | F1. |
| D-ML4 | One lease for the whole lab, Cimmeria semantics, `TimeProvider`-driven, 128-bit id returned only to the acquirer. Lease end (release, expiry, takeover) heals every active fault and returns every pool member. | Accepted (updated) | Two sessions must never share the lab; no fault outlives its owner. |
| D-ML5 | Tool tiers `LabGate { Open, Leased, OwnLease, Machine }`; the policy table is the only source of a gate; unclassified tools are refused (`Lab.Unclassified`). | Accepted | F3, stricter. |
| D-ML6 | The lab owns its stack (reworked): a Docker network `meridian-lab-net`; containers `meridian-lab-*` labelled `meridian-lab=1`; per-service databases `meridian_lab_<service>`; Toxiproxy in front of every dependency; ports per C1 bound to `127.0.0.1` only; no named volumes; every docker call passes an explicit `--context`. Never the AppHost, dev ports or dev volumes. | Accepted (updated) | F11, F34. |
| D-ML7 | The lab references product projects and calls their types (token service, DTOs, `Dhadgar.Agent.Core`). No copies. Entry point is an explicit `Dhadgar.Lab.LabProgram`. | Accepted | Contract first; F9, F10, F31. |
| D-ML8 | Secrets never leave the daemon: tokens, keys and passwords live in `SecretVault` and travel as refs; logs, audit, tool results and bundles pass through `Redactor`; bundles get a secret scan. Invariant I4 scans raw logs **before** redaction. | Accepted (updated) | Most sensitive data the lab touches. |
| D-ML9 | Clock control without touching clocks: config knobs plus `nodes_cert_time_shift` on the lab DB. Containers share the host clock (F33), so per-member clock skew is out. | Accepted | Cheapest honest way to reach grace and expiry. |
| D-ML10 | UAT grading per C8: precedence BLOCKED > FAIL > UNVERIFIED > NEEDS_HUMAN > PASS; PASS needs every required clause evaluated and held; human clauses are attested only by the user. A run with an invariant violation is never `pass` (D-ML24). | Accepted (updated) | F4. |
| D-ML11 | Platform traits: `[SkippableFact]` + `OperatingSystem.IsWindows()` for Windows code; `[Trait("RequiresDocker","true")]` for Linux containers; `[Trait("RequiresWindowsContainers","true")]` plus `MERIDIAN_LAB_WINDOWS_CONTAINERS=1` for Windows members; `[Trait("RequiresNetwork","true")]` for image pulls and npm. CI runs none of them. | Accepted (updated) | F6. |
| D-ML12 | Elevated helper for MACHINE tools. | **Superseded** by D-ML29 | Containers replace it. Revived only if L00 ends in outcome C and the owner declines Hyper-V (D-ML37). |
| D-ML13 | Machine marker for LocalMachine changes on the workstation. | **Superseded** by D-ML43 | No tool changes the workstation. |
| D-ML14 | The agent trusts the lab Gateway TLS root through the container's own `LocalMachine\Root`, the mechanism Sprint 2 D-S222(A) chose; the workstation's Root is never touched. | Accepted | F17; container isolation makes A safe. |
| D-ML15 | The MSI leg runs inside a Windows container member (Sprint 2 D-S223(B)). | Accepted | F17. |
| D-ML16 | One campaign branch `campaign/meridian-lab` cut from `main`, worktree `.claude/worktrees/meridian-lab`; packet branches `ml/lNN-<slug>` merged locally by a Sonnet `general-purpose` agent; pushed once; one PR with `security-review`; merged after the user's yes. Holds until D-ML39 rules. | Accepted | Owner rule D-S215. |
| D-ML17 | Supersedes Sprint 2's wave 2U as the lasting apparatus. Sprint 2 shipped it as a script (`scripts\uat\Invoke-AgentWindowsUat.ps1`) and it passed; L43 ports its legs to TOML rows in containers and L47 records which legs the lab now covers. The orchestrator updates the Sprint 2 ledger. | Accepted (updated) | One lasting UAT design. |
| D-ML18 | `uat-driver`: Haiku, low effort, 50 turns, `omitClaudeMd`, an explicit `mcp__meridian-lab__*` list. It gets pool, fault and invariant tools; it never gets `uat_attest`, triage writes (`triage_*` except reads), `results_known_issue_set`, `fault_reset`, Machine-gate tools, or file and shell tools. Forced lease takeover is forbidden. `DriverToolListTests` enforces it. | Accepted (updated) | F5. |
| D-ML19 | Supervision: host children (log followers, Playwright, fleet jobs) start with `ArgumentList` in a kill-on-close Job Object; lab containers run with `--restart no`; the watchdog restarts a crashed lab container only while a lease is held **and** no fault targets it (the product's recovery is what is under test). | Accepted (updated) | F2. |
| D-ML20 | Fault layer: one Toxiproxy v2.13.0 container (`meridian-lab-toxiproxy`, API on `127.0.0.1:18474`) holding one proxy per service-to-dependency edge (C10 table); services are configured to reach every dependency through their proxy. The lab writes its own `ToxiproxyClient` (F19). Faults: toxics, proxy down, partition, container kill/pause/restart, disk fill in declared tmpfs mounts, corrupt allowlisted files and registry values (with backup), DB faults on `meridian_lab_*` databases. | Accepted | Owner goal; F19. |
| D-ML21 | Blast radius by design and by test: a fault tool takes a **lab target id** from `fault_list_targets`, never a container name, PID, volume or path. `FaultTargetGuard` checks (1) the target registry, (2) live `docker inspect`: label `meridian-lab=1`, name prefix `meridian-lab-`, lab context, (3) a tripwire denylist (`postgres`, `cache`, `messaging`, `dhadgar*`, any volume). Any one failing refuses. `BlastRadiusTests` prove each check refuses on its own with the others removed. No tool deletes a volume. | Accepted | Brief: faults only hit lab-owned things. |
| D-ML22 | Fault lifetime: every fault has an auto-heal deadline (default 120 s, max 900 s); faults heal at row end, lease end, daemon stop and, for leftovers, daemon start (`FaultBook` journal under `LabPaths.State`). | Accepted | A fault must never outlive its purpose. |
| D-ML23 | One spec schema (`schema = 1`; nothing has shipped) carries faults: `[[row.fault]]`, `seed`, `allow_invariants`, `known_issue`, `requires_pool`, step `expect_error`, sources `step` and `invariant`, `since = "fault_heal:<id>"`. The expected-failure catalog is a set of specs under `docs/guides/uat-specs/catalog/`. Never change an expected value to match the product; a disagreement is a finding. | Accepted | One format, one runner. |
| D-ML24 | Invariants I1 to I7 (C11) are checked on every run at baseline, after each row and at run end. A violation fails the row whose window it falls in, or adds a synthetic `invariants/<id>` row; the run's overall result is never `pass` with a violation. `allow_invariants` relaxes a listed invariant only inside that row's fault and recovery window, and the violation is still recorded. | Accepted | Owner goal. |
| D-ML25 | Everything random is seeded: `run.seed` (u64) feeds `LabRandom(seed, stream)` for the runner, hunters, chaos, fuzzers and the fleet. Every failed row or hunt emits `repro.toml` (spec subset, seed, build, fault timeline) that `uat_run` replays. | Accepted | Replayable or it didn't happen. |
| D-ML26 | Property and model-based engine: CsCheck 5.0.0 (C#, seeded, shrinking, `SampleModelBased`). FsCheck 3.6.0 rejected (F#-first). | Accepted | F20. |
| D-ML27 | Results DB: SQLite via `Microsoft.Data.Sqlite` 10.0.14 at `LabPaths.Root\results\lab-results.db` (C12); bundles stay the evidence, the DB is the index. | Accepted | One file, no server, CPM pin already present. |
| D-ML28 | Triage: signatures (C12) bucket failures; known issues come from `docs/guides/uat-specs/known-issues.toml`; flakes by same-seed rerun; trends per row and signature. New buckets go to the triage ledger with a seeded repro; `triage_draft_issue` writes a Markdown draft. **No tool files an issue**; filing follows D-ML42. | Accepted | Owner rule: owner approves before filing. |
| D-ML29 | Machine pool: members have dimensions (`os`, `os_build`, `sku`, `isolation`, `kind`, `arch`, `clock`, `reboot`); specs and jobs state constraints; providers: Linux container, Windows container (after L00), host (reads only), VM (follow-up, returns `Lab.Unavailable`). A member reset is a recreate from its image. | Accepted | F18; containers are cheap, clean and isolated. |
| D-ML30 | Windows containers are proven by the L00 spike before wave 6 is dispatched. Outcome A (process isolation works): wave 6 as written. Outcome B (Hyper-V isolation needed): D-ML37 first. Outcome C (neither): wave 6 is re-sliced to the VM tier or D-ML12 revives. The architect re-specs L31 from the spike's worknote. | Accepted | Don't guess; design for each answer. |
| D-ML31 | VM tier (Hyper-V VMs or Windows Sandbox `.wsb`) for client SKUs, reboots and clock skew. Clock-skew, reboot-mid-operation and client-SKU cases stay here (containers share the host clock and cannot reboot). Recommended: a follow-up campaign after wave 10. | **BlockedDecision** (non-blocking) | Owner priority. |
| D-ML32 | Panel UI automation with Playwright .NET (`Microsoft.Playwright` 1.64.1) inside the daemon, browsers under `LabPaths.Root\playwright`. Login: sign up and sign in through BetterAuth's API, then inject the session cookie into the browser context for `localhost` and drive `/callback`, which runs the real exchange. Node Playwright rejected: a second toolchain and no repo runner (F8). | Accepted | F21, F22. |
| D-ML33 | Scope rule: only real services are in. `src/Tools/Dhadgar.Lab/lab-coverage.json` lists every web project and Node app under `src/` as `real`, `real-pending` (with the packet that makes it real) or `stub`. `LabCoverageTests` fail when a project is missing, or when a `real` entry lacks a lab adapter or a catalog with at least one fault row. Promoting a service from stub means shipping its adapter and catalog rows in the same PR. Owner action O1 adds that line to CLAUDE.md. | Accepted | Owner brief; F35. |
| D-ML34 | Observation paths bypass the fault layer: `db_query`, health probes, log followers, the RabbitMQ management reads and invariant checks reach containers on their direct published ports, never through Toxiproxy. | Accepted | A fault must not blind the judge. |
| D-ML35 | Fleet: hundreds of agents in one daemon job, one `ServiceProvider` per agent built with `AddAgentCore()` plus per-agent fakes (F31), all through the faulted agent ingress. Real enrollment, real mTLS, real hub. | Accepted | Owner goal; F31. |
| D-ML36 | The control plane runs as Linux containers built from a portable `dotnet publish` (locked restore, no `-r`) and lab Dockerfiles under `tools/lab/images/`. The lab RabbitMQ image adds the delayed-message plugin at a pinned version and checksum. Node apps (BetterAuth, Panel) build with `npm ci` in lab Dockerfiles. | Accepted | F14, F24, F27. |
| D-ML37 | If L00 shows that Windows containers need Hyper-V isolation, enable the Windows optional features `Microsoft-Hyper-V` and `Containers` on the workstation (one reboot). Recommended: **yes**. | **BlockedDecision** (conditional on L00) | Owner's machine. Blocks wave 6 only under outcome B. |
| D-ML38 | If Docker Desktop cannot run its Linux and Windows engines at once, install a separate Windows `dockerd` as a service on its own pipe for the Windows pool. Recommended: **yes, as a service**, with the lab using an explicit context for each engine. | **BlockedDecision** (conditional on L00) | Owner's machine. Blocks wave 6 only if side by side fails. |
| D-ML39 | Ship cadence. Recommended: one PR per milestone instead of D-ML16's single PR: M1 waves 0-3, M2 waves 4 and 4H, M3 waves 5 and 5E, M4 wave 6, M5 wave 7, M6 wave 8, M7 waves 9-10. Until ruled, D-ML16 holds. | **BlockedDecision** (non-blocking) | 63 packets is too much for one review. |
| D-ML40 | Small default-off product seams for the lab, config only, no lab references: a Notifications email-provider switch to SMTP (lab points it at a sink) and a BetterAuth cookie-domain override from env. Recommended: **allow**. Without it, email rows stay expected-failure rows and login uses cookie injection (D-ML32). | **BlockedDecision** (non-blocking) | Touches product code. |
| D-ML41 | Discord bot coverage needs a real bot token and Discord's servers. Recommended: **out**; the lab covers the webhook path and asserts the bot-absent 503. | **BlockedDecision** (non-blocking) | External service, real credentials. |
| D-ML42 | Filing approved issue drafts. Recommended: the owner files them (`gh issue create --body-file <draft>`), or ops gains that one verb, run only on the owner's explicit yes per draft. | **BlockedDecision** (non-blocking: until ruled, L26's skill text says "never file; report the draft path to the user", which holds under either answer) | Owner rule: no auto-filing. |
| D-ML43 | The `Machine` gate stays reserved: no tool in this plan uses it, the dispatcher refuses it with `Lab.MachineReserved`, and `ToolPolicyTests` fail if a tool is classified `Machine`. Adding one needs a new decision. | Accepted | The workstation is never a test target. |
| D-ML44 | Live runs (L46) are executed by the UAT runner agent, not the user (Sprint 2 D-S239). Containers need membership of `docker-users`, not elevation. | Accepted | F17. |
| D-ML45 | Reserve in the foundation waves, so later techniques need no schema change: actor and op ids on every observation, an ordered event stream with an `Unknown` outcome (`events.jsonl`), a build and environment fingerprint, run pairing, and replay fidelity (`exact` or `schedule_approximate`). C18, L48. | Accepted | History, twins, mutants and version runs all read these. |
| D-ML46 | Fix the four first-pass defects (F50): `overall` gains `incomplete` (`Overall_AllSkipped_IsIncomplete`); a row is a known failure only when every signature matches the same open issue within its scope; `FaultImpactMap` lets a fault excuse a 502/503/504 only from a service whose impact set holds the faulted target, and never a 500; `FaultBook` writes the journal entry (`Prepared`) before inject and reconciles at start. Each fix has a lab mutant (LM1, LM2, LM5, LM6, LM7) the self-check must catch. | Accepted | A judge that passes on nothing, by claim, or by any fault is no judge. |
| D-ML47 | History clients go through a lab barrier proxy (`127.0.0.1` only, `X-Lab-Op-Id` rules: hold request, hold response, drop response) to force races and lost responses; mTLS paths use a Toxiproxy `timeout` toxic instead. | Accepted | Races must be forced, not hoped for. |
| D-ML48 | Coyote is deferred: stale (F48) and it rewrites binaries. Concurrency is checked from histories (L50). | Accepted | F48. |
| D-ML49 | Make enroll retry idempotent: the same token with the same public key after a success returns the same node and a new certificate bound to that key, superseding the earlier one. Until ruled, E5 is `required = false` under `KI-ENROLL-RETRY`. Recommended: **yes**. | **BlockedDecision** (non-blocking) | F41; a product change. |
| D-ML50 | Twins: paired runs on a reset stack with one seed; relations `equal_outcome` and `isolated`; a twin whose base world did not pass is never a pass (C19). The tenant-isolation twin is the flagship (L56). | Accepted | Owner goal; metamorphic checks find what fixed expectations miss. |
| D-ML51 | Planted defects are lab-only: product mutants are patches under `tools/lab/mutants/`, applied only in a `git archive` copy, built into labelled images and never committed in patched form; lab mutants are judged by `Dhadgar.Lab.SelfCheck`. `LabIsolationTests` and `MutantPatchesTouchOnlyDeclaredFiles` enforce it. Gates MD and LS. | Accepted | Proves the lab can see what it claims to see. |
| D-ML52 | The adversarial peer uses only identities the lab enrolled in the run and connects only to `127.0.0.1:25000`, `25443` or `15040`; `PeerTargetGuard` refuses the rest. `nodes_decommission` acts only on nodes the lease enrolled. | Accepted | Brief: faults and adversaries target only lab-owned members. |
| D-ML53 | Clone policy: what Nodes does when two hub connections present one certificate (F42). Recommended: **the newer connection replaces the older**, which is closed with a logged event. Until ruled, `clone` is observed only. | **BlockedDecision** (non-blocking) | Product behaviour. |
| D-ML54 | Idle post-revocation: an idle hub connection outlives revocation or decommission (F42). Recommended: **Nodes drops the node group's connections within 10 s of revoking or decommissioning**. Until ruled, the idle variant is observed only; the heartbeating variant is required now. | **BlockedDecision** (non-blocking) | Product behaviour. |
| D-ML55 | Coverage is behavioural (C18 keys: endpoint × status, error codes, fault kind × target, scenarios, attacks), reported by `coverage_report`. Code-coverage instrumentation is not adopted. | Accepted | Cheap, explainable, needs no instrumented images. |
| D-ML56 | Continuity promises: what must happen to a running game when the control plane or the agent goes away (F44, F45). Recommended: **a game survives a control-plane outage and an agent restart; an agent crash is allowed to stop it only until the process manager stops using `KillOnJobClose` for games**. L59 runs the `cp-disconnect` experiment first and its worknote informs the ruling; until then continuity rows are `required = false`. | **BlockedDecision** (non-blocking) | Product promise. |
| D-ML57 | Guided mode is opt-in: `random_share` ≥ 0.3, off (`guided.toml enabled = false`) until a paired A/B (100 runs per arm) shows strictly more findings per 100 runs than random. | Accepted | Earn it with findings, never assume it. |
| D-ML58 | Resurrection: an agent restarted with the state of a decommissioned node. Recommended: **refuse the old identity at the hub and require a fresh enrollment**; the agent should log it and stop retrying. Until ruled, the row is `required = false`. | **BlockedDecision** (non-blocking) | Product behaviour. |
| D-ML59 | Rollback: running an older Nodes on a newer schema. Recommended: **unsupported, documented**; the rollback row stays recorded, not required. | **BlockedDecision** (non-blocking) | Product support policy. |
| D-ML60 | Sprint 2 is on `main` (`6df3338`); `campaign/meridian-lab` is cut after it; wave 8 needs only the architect's W8 ruling note, not a merge. | Accepted | F16, F51. |
| D-ML61 | Envelope thresholds: healthy = success ratio ≥ 0.95 over 30 s; recovered within 120 s; metastable when not healthy by max(360 s, 3 × fault duration) after the heal. | Accepted (owner may change) | Starting values; a `metastable` cell is a finding, not a reason to move them. |

## Seam tests

| ID | Seam | Test | Written red in | Green in |
| --- | --- | --- | --- | --- |
| S-L1 | Claude Code (MCP client) ↔ daemon HTTP edge | `Seams/McpEdgeSeamTests.cs`: a real MCP client lists tools and calls `lab_status`; raw calls without token, wrong token, with `Origin`, bad `Host` get 401/401/403/403 | L01 | L05 |
| S-L2 | Lab token mint ↔ Nodes enrollment | `Seams/NodesEnrollSeamTests.cs`: lab-minted token, product `EnrollNodeRequest`, real Nodes app → 201; Docker variant through the lab Gateway | L01 | L11 |
| S-L3 | Lab ↔ Windows agent footprint | `Seams/AgentFootprintSeamTests.cs`: the agent's names equal `Dhadgar.Contracts.Agents.AgentWindowsFootprint` | L12 | L39 |
| S-L5 | `uat-driver` ↔ tool registry | `Agents/DriverToolListTests.cs` (D-ML18) | L18 | L18 |
| S-L6 | Runner ↔ live lab | `Uat/LabSelfTestE2ETests.cs` (`RequiresDocker`): `lab-selftest.toml` all PASS, invariants clean | L17 | L17 (invariants from L21) |
| S-L7 | Lab `ToxiproxyClient` ↔ real Toxiproxy ↔ a real service | `Seams/ToxiproxySeamTests.cs` (`RequiresDocker`): proxy down on `nodes-postgres` makes Nodes `/readyz` fail; heal restores it within 30 s; latency toxic is visible in a timed probe | L19 | L19 |
| S-L8 | Windows agent in a container member ↔ lab Gateway enrollment | `Seams/ContainerAgentEnrollSeamTests.cs` (`RequiresWindowsContainers`): agent in a member enrolls with a lab token; Nodes has the node; the member holds the certificate | L31 | L33 |
| S-L9 | Playwright ↔ Panel ↔ BetterAuth ↔ Gateway ↔ Identity | `Seams/PanelLoginSeamTests.cs` (`RequiresDocker`): `ui_flow_run login_email` reaches `/dashboard` with tokens in session storage; `refresh` rotates the refresh token; replaying the old one is refused (DV-3) | L34 | L35 |
| S-L10 | Fleet agents (real `Agent.Core`) ↔ lab Gateway ↔ Nodes hub | `Seams/FleetSeamTests.cs` (`RequiresDocker`): 20 fleet agents enroll and heartbeat; Nodes shows 20 online nodes | L42 | L42 |
| S-L11 | History clients ↔ barrier proxy ↔ lab Nodes ↔ checker | `Seams/EnrollHistorySeamTests.cs` (`RequiresDocker`): `history_run enroll-race` (4 clients) is `linearizable` with 4 `Invoke` and 4 `Complete` events; `enroll-lost-response` is not `unknown` and E1-E4, E6 hold | L49 | L51 |
| S-L12 | Twin engine ↔ two tenants ↔ Gateway ↔ Nodes | `Seams/TenantTwinSeamTests.cs` (`RequiresDocker`): `twin_run tenant-isolation-enroll` holds | L53 | L56 |
| S-L13 | Adversarial peer ↔ agent ingress and Nodes hub | `Seams/PeerSeamTests.cs` (`RequiresDocker`): `peer_run all` holds every required attack, no `peer:` signature | L58 (first) | L58 |
| S-L14 | Baseline agent ↔ head Gateway and Nodes | `Seams/MixedVersionSeamTests.cs` (`RequiresWindowsContainers`): `version_run old-agent-new-nodes` passes and its fingerprint proves the mix | L60 (first) | L60 |
| BR | Blast-radius guard (not a seam; a gate) | `Faults/BlastRadiusTests.cs` plus `BlastRadiusDockerTests` with decoy containers | L19 | L19 |

S-L4 (the elevated helper pipe) is dropped with D-ML12.

## Packets

| Packet | Wave | Title | Depends on | Security review | Status |
| --- | --- | --- | --- | --- | --- |
| L00 | 0 | Spike: Windows containers on this host (no code; worknote) | - | no | Ready |
| L01 | 1 | Scaffold projects, packages, isolation test, seam stubs, red S-L1/S-L2 | - | yes | Ready |
| L02 | 1 | Daemon edge: loopback Kestrel, token, Host/Origin, mutex, `/status`, CLI | L01 | yes | BlockedDependency |
| L03 | 1 | `LeaseBook`, sweeper, history, `lab.lease` events | L01 | yes | BlockedDependency |
| L04 | 1 | Audit log, `SecretVault`, `Redactor` | L01 | yes | BlockedDependency |
| L05 | 1 | Tool registry, policy gate, MCP projection, lease and status tools; S-L1 green | L02, L03, L04 | yes | BlockedDependency |
| L06 | 1 | `tools/lab` scripts, scheduled task, `.mcp.json` | L05 | yes | BlockedDependency |
| L07 | 2 | Supervisor, Job Object, log capture and container log followers, watchdog, `logs_tail` | L05 | yes | BlockedDependency |
| L08 | 2 | Docker layer with contexts, target registry, lab network, Postgres/RabbitMQ/Redis/Toxiproxy, per-service DBs | L05 | yes | BlockedDependency |
| L09 | 2 | `lab_build` (portable), lab service image, Gateway and Nodes containers behind proxies, overlays | L07, L08 | yes | BlockedDependency |
| L10 | 2 | `db_query` (per-service DB, read-only) and `http_request` (lab hosts only) | L08 | yes | BlockedDependency |
| L11 | 2 | Nodes adapter: token mint, enroll probe, cert time shift, `stack_reset_data`; S-L2 green | L09, L10 | yes | BlockedDependency |
| L12 | 2 | `Dhadgar.Lab.Probe`, `AgentWindowsFootprint`, host reads, `lab_doctor`; red S-L3 | L05 | yes | BlockedDependency |
| L13 | 3 | Spec model (faults included), TOML loader, validator, `uat_list_specs`, `uat_plan` | L05 | yes | BlockedDependency |
| L14 | 3 | Clause evaluator: sources (incl. `step`), ops, `${var}`, capture | L13, L10, L12 | yes | BlockedDependency |
| L15 | 3 | Runner and grader, seeds, `uat_run` (OwnLease); fault rows BLOCKED until L22 | L14 | yes | BlockedDependency |
| L16 | 3 | Evidence bundle, `ledger.md`, secret scan, report, attest, `bundle_read` | L15 | yes | BlockedDependency |
| L17 | 3 | CLI `status`/`uat`, `lab-selftest.toml`, S-L6 | L16, L11 | yes | BlockedDependency |
| L18 | 3 | `uat-driver` agent, `lab-uat` skill, S-L5 | L16 | yes | BlockedDependency |
| L48 | 3 | Observation stream, actor and op ids, fingerprint, pairing, replay fidelity (C18) | L16 | yes | BlockedDependency |
| L19 | 4 | Blast-radius guard, write-ahead `FaultBook`, Toxiproxy faults and partitions; S-L7, BR | L08, L09, L48 | yes | BlockedDependency |
| L20 | 4 | Container, disk, file and DB faults | L19 | yes | BlockedDependency |
| L21 | 4 | Invariant monitors I1-I6, `invariants_check` | L16, L19 | yes | BlockedDependency |
| L22 | 4 | Runner executes faults, `allow_invariants`, invariant grading, `repro.toml` | L20, L21 | yes | BlockedDependency |
| L23 | 4 | Catalogs `gateway.toml`, `nodes.toml`, Nodes invariant checks | L22 | yes | BlockedDependency |
| L24 | 4 | `lab-coverage.json`, `LabCoverageTests`, driver and skill for faults | L23, L18 | yes | BlockedDependency |
| L49 | 4H | Barrier proxy; red S-L11 | L48, L09, L10 | yes | BlockedDependency |
| L50 | 4H | Linearizability checker, enrollment model and rules E1-E6, minimiser | L48 | yes | BlockedDependency |
| L51 | 4H | `history_run`, enrollment scenarios, Toxiproxy open-connection check; S-L11 green | L49, L50, L22, L11 | yes | BlockedDependency |
| L25 | 5 | Results DB, bundle ingest, signatures, known-issue matching, `overall`, `results_query` | L22 | yes | BlockedDependency |
| L26 | 5 | Triage: buckets, known issues, trends, drafts, ledger | L25 | yes | BlockedDependency |
| L27 | 5 | Async jobs, `results_flake_check`, the CsCheck model-based hunter (enrollment lifecycle) | L25 | yes | BlockedDependency |
| L28 | 5 | Contract fuzzer through the Gateway | L27 | yes | BlockedDependency |
| L29 | 5 | Chaos scheduler, soak runs, resource sampler, I7 | L27 | yes | BlockedDependency |
| L52 | 5 | Behavioural coverage and `coverage_report` | L25, L23 | no | BlockedDependency |
| L53 | 5E | Twin engine; red S-L12 | L25, L22 | yes | BlockedDependency |
| L54 | 5E | Mutant infrastructure; M1, M4, M5 | L27, L51, L23 | yes | BlockedDependency |
| L55 | 5E | `Dhadgar.Lab.SelfCheck`: lab mutants LM1-LM8, crash recovery | L54, L19, L21, L25, L03 | yes | BlockedDependency |
| L30 | 6 | Pool model, constraints, Linux and host providers, `pool_*` tools | L19 | yes | BlockedDependency |
| L31 | 6 | Windows container provider, agent image, member reset; red S-L8 | L30, L00 worknote; D-ML37/D-ML38 if L00 says so | yes | BlockedDependency |
| L32 | 6 | Agent-in-member tools: install, start, stop, token, config, footprint reads, agent log | L31, L12 | yes | BlockedDependency |
| L33 | 6 | Member faults (kill, pause, fill, corrupt file and registry), `agent-container-enroll.toml`, S-L8 green | L32 | yes | BlockedDependency |
| L34 | 7 | Identity, BetterAuth and Panel in the lab; red S-L9 | L22 | yes | BlockedDependency |
| L35 | 7 | Playwright .NET, `ui_flow_*` tools, flows; S-L9 green | L34 | yes | BlockedDependency |
| L36 | 7 | Notifications, Discord, webhook sink | L22, L12 | yes | BlockedDependency |
| L37 | 7 | CLI in a Linux member | L30, L34 | yes | BlockedDependency |
| L38 | 7 | Catalogs for Identity, BetterAuth, Panel, Notifications, Discord, CLI; coverage flips | L35, L36, L37 | yes | BlockedDependency |
| L56 | 7 | Tenant isolation twins; M2, M3, M7; S-L12 green | L35, L53, L54, L11 | yes | BlockedDependency |
| L39 | 8 | Agent consumes `AgentWindowsFootprint`; S-L3 green | L12, W8 ruling note | yes | BlockedDependency |
| L40 | 8 | `GraceUntil` confirm, hub events, `hub` and `agent_log` sources | W8 ruling note, L32 | yes | BlockedDependency |
| L41 | 8 | Lab Gateway TLS and mTLS; trust inside members (D-ML14) | L40 | yes | BlockedDependency |
| L42 | 8 | Fleet simulator on real `Agent.Core`; S-L10 | L41, L27 | yes | BlockedDependency |
| L43 | 8 | Sprint 2 specs in containers: console, service, MSI, renewal and grace, GameServerWrapper | L41, L33 | yes | BlockedDependency |
| L44 | 8 | Hub and `CommandEnvelope` fuzzing; fleet chaos catalog | L42, L28, L29 | yes | BlockedDependency |
| L57 | 9 | Recovery envelopes over fault duration and load | L42, L29, L21 | yes | BlockedDependency |
| L58 | 9 | Adversarial peer, `nodes_decommission`, M6; S-L13 | L41, L42, L54 | yes | BlockedDependency |
| L59 | 9 | Gameplay continuity with a synthetic game; `agent_kill` | L43, L33 | yes | BlockedDependency |
| L60 | 9 | Baselines and mixed-version pairs; S-L14 | L54, L43 | yes | BlockedDependency |
| L61 | 9 | Time rows: resurrection, interrupted migration, rollback | L60, L58 | yes | BlockedDependency |
| L62 | 9 | Guided mode and its A/B | L52, L29, L27 | yes | BlockedDependency |
| L45 | 10 | Docs: ADR, guides, catalog and triage guides, threat model | L44, L38, L55-L62 | security-advisor read | BlockedDependency |
| L46 | 10 | Live runs by the UAT runner agent; evidence to `worknotes/` | L45 | n/a | BlockedDependency |
| L47 | 10 | Close-out | campaign review, L46 | security-advisor read | BlockedDependency |

## Waves and merge order

| Wave | Packets | Value delivered | Local merge order into `campaign/meridian-lab` | Parallel start |
| --- | --- | --- | --- | --- |
| 0 | L00 | Facts that decide the pool | no merge (worknote only, committed by L01's branch or ops) | beside wave 1 |
| 1 | L01-L06 | Claude can reach a leased, audited daemon | L01 → L02 → L03 → L04 → L05 → L06 | L02, L03, L04 after L01 |
| 2 | L07-L12 | Real Gateway and Nodes on a wipeable stack, probed and read | L07 → L08 → L12 → L09 → L10 → L11 | L07, L08, L12; L09, L10 after L08 |
| 3 | L13-L18, L48 | Graded, evidenced UAT runs by a driver agent; the observation stream | L13 → L14 → L15 → L16 → L48 → L17 → L18 | L48, L17, L18 after L16 |
| 4 | L19-L24 | Faults, invariants and the first expected-failure catalogs | L19 → L20 → L21 → L22 → L23 → L24 | L21 beside L20 |
| 4H | L49-L51 | Enrollment histories checked for linearizability | L49 → L50 → L51 | L49, L50 together (L49 after L48; L51 after wave 4) |
| 5 | L25-L29, L52 | Results DB, triage, model-based hunting, fuzzing, chaos, soak, behavioural coverage | L25 → L26 → L27 → L52 → L28 → L29 | L26, L27, L52 after L25; L28, L29 after L27 |
| 5E | L53-L55 | Twins, product mutants, the lab self-check | L53 → L54 → L55 | L53, L54 together (L54 after wave 4H) |
| 6 | L30-L33 | Windows agent in container members | L30 → L31 → L32 → L33 | serial; L31 needs the L00 ruling |
| 7 | L34-L38, L56 | Identity, BetterAuth, Panel (Playwright), Notifications, Discord, CLI; the tenant-isolation twin | L34 → L36 → L35 → L37 → L38 → L56 | L34, L36 together; L56 after L35 and wave 5E |
| 8 | L39-L44 | Sprint 2 flows, fleet, agent-protocol fuzzing | L39 → L40 → L41 → L42 → L43 → L44 | L39 any time after L12 and the W8 ruling; L42, L43 after L41 |
| 9 | L57-L62 | Adversarial peer, recovery envelopes, continuity, mixed versions and time, guided mode | L57 → L58 → L59 → L60 → L61 → L62 | L57, L58, L59, L60, L62 together once their dependencies are merged; L61 after L58 and L60 |
| 10 | L45-L47 | Docs, live evidence, close-out | L45 → L46 (no merge) → L47 | serial |

Waves 4H, 5, 5E, 6 and 7 may run in parallel after wave 4 as their dependencies allow; they share `ToolPolicy.cs`, the C4 table and `LabServices.cs`, so their packets merge one at a time and each rebases on the tip first.

Ship: D-ML16 (one PR after L47) until D-ML39 rules; under D-ML39's recommendation ops pushes and opens a PR at the end of each milestone (M1 after wave 3, M2 after wave 4H, M3 after wave 5E, M4 wave 6, M5 wave 7, M6 wave 8, M7 after L47), each with `security-review`, merged on the user's yes.

## Gates

- Wave 0: the L00 worknote records outcome A, B or C with evidence. The orchestrator raises D-ML37 or D-ML38 if the outcome needs them.
- Wave 1: S-L1 green; S-L2 red as expected; wave review `ship`.
- Wave 2: S-L2 green (in-process in CI, Docker variant run once locally); wave review `ship`.
- Wave 3: S-L5 and S-L6 green (S-L6 locally, Docker); a S-L6 run writes `events.jsonl` and a fingerprint (L48); wave review `ship`.
- Wave 4: S-L7 and BR green; S-L6 green with invariants on; both catalogs plan clean; wave review `ship`.
- Wave 4H: S-L11 green; the L51 report answers F49; wave review `ship`.
- Wave 5: a seeded hunt and its `repro.toml` replay to the same failure, or the hunt finds none in its budget and says so; `coverage_report` lists unhit keys; wave review `ship`.
- Wave 5E: S-L12 red as expected; M1, M4 and M5 pass gate MD; gate LS holds; wave review `ship`.
- Wave 6: L00 outcome known and its decisions ruled; S-L8 green on Windows; wave review `ship`.
- Wave 7: S-L9 and S-L12 green; M7 passes gate MD, and M2 and M3 pass it as `detected` or as `unreachable` with `KI-NODES-USER-AUTH` open; `LabCoverageTests` show no `real-pending` owned by waves 4 to 7; wave review `ship`.
- Wave 8: the W8 ruling note exists before L40; S-L3 and S-L10 green; wave review `ship`.
- Wave 9: S-L13 and S-L14 green; M6 passes gate MD; one full run of each envelope with no `metastable` cell, or each such cell reported as a finding with a follow-up; the L59 and L62 worknotes and the L61 report exist; wave review `ship`.
- Wave 10: gates MD and LS hold on the campaign tip; campaign review `ship`.
- **Gate MD** (product mutants): every product mutant's `mutation_run` verdict is `detected` or `detected_other`, or `unreachable` with its named known issue open. `survived` or `stale` fails the gate; the fix is to the lab or the patch, never a weaker mutant.
- **Gate LS** (lab self-check): `selfcheck` reports every lab mutant `caught` after a clean baseline pass, and `--recovery` passes.

## Dependencies on Sprint 2

All satisfied: Sprint 2 is on `main` (`6df3338`, D-ML60), and the campaign branch is cut after it. The table says what each item uses; the W8 ruling note confirms the names before L40.

| Lab item | Uses from Sprint 2 | Why |
| --- | --- | --- |
| L39 | P11 (state directory), P14 (CA store) | edits agent files Sprint 2 changes |
| L40 | P16 (`GraceUntil`), the AgentHub | grace and hub evidence |
| L41 | D-S210 (Gateway Kestrel mTLS), D-S212, D-S221, D-S222(A) | the agent must reach the lab Gateway over mTLS |
| L42 | AgentHub contracts, mTLS enrollment, `ControlPlaneHubTransport` | the fleet speaks the real protocol |
| L43 | all of Sprint 2's agent and Nodes flows, the MSI (D-S223(B)), its UAT script legs | the rows are Sprint 2's acceptance flows |
| L44 | `AgentHubContracts`, `CommandValidator` and `CommandDispatcher` constructors on Sprint 2 | fuzz targets |
| L11, L58 | `GraceUntil`, renewal grace, `RevokeWithoutGrace`, the hub's certificate checks (F43, F42) | the grace shift and the peer's expectations |
| L60, L61 | the `sprint2` baseline commit | mixed-version and time rows |
| Waves 0-7 | nothing beyond `main` | L33 enrolls over plain HTTP, which `main` supports |

## Status honesty

Until L46 records runner-made bundles, `docs/PROJECT-STATE.md` may say only "meridian-lab: code exists". "Confirmed working" needs S-L1, S-L2, S-L3, S-L5 to S-L14 and BR green, gates MD and LS held, plus the L46 ledgers in `worknotes/`. Rows that are `required = false` until an owner ruling (D-ML49, D-ML53, D-ML54, D-ML56, D-ML58, D-ML59) are "observed", never "confirmed". A catalog row that fails is a finding, not a lab defect, unless triage shows the lab caused it.
