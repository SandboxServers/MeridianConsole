# Campaign: meridian-lab

A full MCP lab daemon that lets a Claude session drive and verify the whole MeridianConsole stack from the harness. It runs automated UAT (spec-driven rows with graded evidence) and ad-hoc probes against the control plane (Gateway, Nodes, lab Postgres and RabbitMQ) and the Windows agent. It is modelled on Cimmeria's lab apparatus (`cimmeria-lab`), minus the game-specific parts.

- Packets and contract: [work-packets.md](work-packets.md). Resume card: [RESUME.md](RESUME.md).
- Owner goal (Steve, 2026-10-27): "the FULL MCP daemon ... not a lightweight script runner".
- This campaign **supersedes** any lab or UAT-automation scope inside `sprint2-agent-nodes` (its wave 2U, packets P18 to P23, as listed in its RESUME on 2026-10-27). The orchestrator reconciles the Sprint 2 ledger; this campaign never edits `docs/analysis/sprint2-agent-nodes/`.
- Dev and lab only. Nothing here is referenced by a product project, shipped in an image or chart, or wired into production (D-ML2, test `LabIsolationTests`).

## Scope

| In | Out |
| --- | --- |
| Shared per-user daemon: MCP over streamable HTTP on loopback, bearer token, single instance, scheduled task | Any remote or LAN access to the daemon |
| Lease (one per lab), deny-by-default tool policy in tiers OPEN / LEASED / OWN_LEASE / MACHINE | Driving the real dev Aspire stack or its volumes |
| Lab-owned stack: Docker Postgres, RabbitMQ and Redis plus Gateway and Nodes as supervised child processes | Panel, Identity, Billing and other services (follow-up) |
| Read tools: status, logs, lab DB, cert stores, agent registry key and state file, HTTP probes | Linux agent driving (follow-up; containerised agent) |
| Write tools: build, start/stop/restart, config overlays, enrollment token mint, enroll probe, certificate time shift | Changing the system clock or adding a product clock hook |
| MACHINE tier through a UAC-approved elevated helper: agent service and console, registry token, footprint reset | MSI install leg (D-ML15) |
| UAT: TOML specs, runner, grader, evidence bundle and `ledger.md`, attestation | Screenshots or UI automation |
| `uat-driver` agent, `lab-uat` skill, docs and ADR | |

## Findings

| ID | Finding | Evidence | Packet |
| --- | --- | --- | --- |
| F1 | Cimmeria's lab is one shared per-user daemon (`--http 127.0.0.1:8779`, scheduled task), bearer token compared in constant time with one opaque 401, loopback Host allowlist, every `Origin` refused, named mutex plus pidfile, exit 2 on config refusal and 3 when already running. `/status` is behind the bearer and never returns a lease id. | Cimmeria `crates\lab\src\daemon\mod.rs`, `daemon\auth.rs`, `docs\architecture\live-research-lab.md` §11; survey `docs/analysis/sprint2-agent-nodes/worknotes/cimmeria-lab-survey.md` | L02 |
| F2 | Cimmeria's `LeaseBook`: ttl default 600 s (30 to 3600), owner and purpose required, refusal names holder/purpose/since/expiry, `force` needs a `reason` and logs WARN, history records released/expired/taken_over, guarded calls renew, sweeper every 5 s, watchdog relaunches only under a lease, one `lab.lease` event per change, arguments never logged. | `crates\lab\src\lease\mod.rs` | L03 |
| F3 | Cimmeria classifies every tool (`Gate::Open/Lease/OwnLease`); tests `no_tool_is_in_two_lists` and `every_routed_tool_is_classified`. An unlisted tool is guarded there; we go one step stricter and deny it. | `crates\lab\src\lease\policy.rs` | L05 |
| F4 | Cimmeria UAT: TOML specs (`schema = 1`, `[[row]]`, steps, `[[row.expect]]` clauses with sources and ops), `${var}` and capture, `depends_on`, `plan_only`; grades PASS/FAIL/BLOCKED/SKIPPED/NEEDS_HUMAN/UNVERIFIED; PASS only if every required clause was checked and held. Bundle `run.json` + `rows/` + `ledger.md`; `lab_uat_report`, `lab_uat_attest`. | `docs\guides\automated-uat.md`; survey | L13-L16 |
| F5 | Cimmeria's `lab-driver` agent is Haiku, low effort, 50 turns, MCP tools only, exact steps, raw results, retry once unchanged, stop on surprise; the `lab-uat` skill asks the user before taking the lease and verifies the driver against the bundle. | Cimmeria `.claude\agents\lab-driver.md`, `.claude\skills\lab-uat\SKILL.md`; survey | L18 |
| F6 | The repo is .NET 10 (`global.json` 10.0.100), CI is `ubuntu-24.04` building `Dhadgar.sln` with `CI=true` (warnings as errors, `AnalysisMode AllEnabledByDefault`), tests filtered `RequiresDocker!=true`, `RestorePackagesWithLockFile=true`, Central Package Management. A .NET daemon builds and tests in the existing CI; a Rust or Python daemon would need a second toolchain. | `global.json`, `Directory.Build.props`, `.github/workflows/_dotnet-build-test.yml` | L01 |
| F7 | No MCP SDK or TOML parser is in `Directory.Packages.props`. Latest stable on nuget.org (2026-10-27): `ModelContextProtocol` and `ModelContextProtocol.AspNetCore` 2.3.0, `Tomlyn` 2.10.1. | nuget.org lookup | L01 |
| F8 | No `tools/` folder, no `src/Tools/`, no root `.mcp.json` exist. | repo tree | L01, L06 |
| F9 | Nodes has no user-auth path to mint enrollment tokens (`POST /organizations/{orgId}/enrollment/tokens` needs a user). The product's `IEnrollmentTokenService.CreateTokenAsync(orgId, createdByUserId, label, validity, ct)` returns `(EnrollmentToken, PlainTextToken)`, so the lab mints tokens through the product's own service against the lab DB instead of copying the hashing. | `src/Dhadgar.Nodes/Services/IEnrollmentTokenService.cs`; Sprint 2 README F15 | L11 |
| F10 | Nodes maps `POST /agents/enroll` (anonymous, body `Dhadgar.Contracts.Nodes.EnrollNodeRequest(Token, Platform, Hardware, PublicKey, AgentVersion?)`, 201 `EnrollNodeResponse`, 401 `invalid_token`); the Gateway exposes it as `/api/v1/agents/enroll`. This seam exists on `main`, so the lab's token-mint seam test needs no Sprint 2 code. | `src/Dhadgar.Nodes/Endpoints/AgentEndpoints.cs:24`; `src/Shared/Dhadgar.Contracts/Nodes/EnrollmentContracts.cs:62`; Gateway `appsettings.json` routes | L01 (red), L11 (green) |
| F11 | Nodes needs Postgres (`ConnectionStrings:Postgres`) and RabbitMQ (MassTransit with EF outbox), auto-migrates only in Development, initialises its CA on startup with `LocalFileCaStorageProvider`, registers `TimeProvider.System`. The Gateway's Redis health check is optional (`Redis:ConnectionString`). The dev AppHost uses a persistent Postgres volume `dhadgar-postgres-data` that the lab must never touch. | `src/Dhadgar.Nodes/Program.cs`; `src/Dhadgar.Gateway/Program.cs:71-75`; `src/Dhadgar.AppHost/Program.cs` | L08, L09 |
| F12 | The Windows agent writes LocalMachine state: service `DhadgarAgent` (LocalSystem), `HKLM\SOFTWARE\Meridian Console\Agent\EnrollmentToken`, client certificate in `LocalMachine\My`, CA in `LocalMachine\Root` today (Sprint 2 P14 moves it to a `MeridianConsoleAgent` store), state file under `%ProgramData%\Meridian Console\Agent\`. Console mode (`AddWindowsService` is a no-op outside the SCM) still needs an elevated token. So every agent-driving tool needs elevation; reads of `LocalMachine\My` metadata do not. | `src/Agents/Dhadgar.Agent.Windows/Program.cs:40`; `WindowsCertificateStore.cs:68,235,317` (w1); Sprint 2 P11, P14 | L12, L19-L21 |
| F13 | Agent footprint names are private constants in the agent (service name, CA store name, registry key, state file). A lab that reads them from its own copies would drift: that is the seam defect class. | `Program.ServiceName`, `WindowsCertificateStore.CaStoreName` (w1, internal) | L12 (red S-L3), L23 |
| F14 | Agent.Windows targets `net10.0` with RIDs win-x64, win-arm64 and linux-x64, so it builds on ubuntu CI; Windows behaviour is tested with `[SkippableFact]` and `Skip.IfNot(OperatingSystem.IsWindows())`. | `Dhadgar.Agent.Windows.csproj`, its tests csproj | all Windows packets |
| F15 | Service test hosts run in the `Testing` environment, so DI scope validation is off; a singleton capturing a scoped `DbContext` hid in Nodes until Sprint 2 P07. The lab's daemon host and its Nodes adapter add singletons. | architect memory `di-scope-validation-off-in-tests` | L01, L11 |
| F16 | Sprint 2 (branch `campaign/sprint2-agent-nodes-w1`, not on `main`) changes enrollment, adds the AgentHub (`/hubs/agent`), `AgentCertificate.GraceUntil` (P16), `Mtls:RenewalGraceMinutes` (D-S212), the renewal-threshold knob (D-S221), the CA store move (P14) and the state directory (P11). Grace, hub and footprint rows cannot be written until it merges. | Sprint 2 README and RESUME | wave 5 |
| F17 | Sprint 2 left two owner decisions that also gate this lab's service-mode rows: D-S222 (LocalMachine\Root trust anchor) and D-S223 (MSI in acceptance). | Sprint 2 README decisions | D-ML14, D-ML15 |

## Decisions

| ID | Decision | Status | Reason |
| --- | --- | --- | --- |
| D-ML1 | Stack: .NET 10 C#, ASP.NET Core minimal hosting, `ModelContextProtocol.AspNetCore` 2.3.0 (streamable HTTP, stateless), `Tomlyn` 2.10.1, `System.CommandLine` for the CLI. | Accepted | F6, F7: one toolchain, CI builds it, the lab can reference product types directly. |
| D-ML2 | Layout: `src/Tools/Dhadgar.Lab` (daemon and `meridian-lab` CLI, one exe), `src/Tools/Dhadgar.Lab.Elevated` (elevated helper exe), `tests/Dhadgar.Lab.Tests`, scripts in `tools/lab/`, root `.mcp.json` entry `meridian-lab`. All three projects are in `Dhadgar.sln`. No product project, Dockerfile, Helm chart or AppHost references them; `LabIsolationTests` fails if one does. | Accepted | F8; production isolation is a hard rule. |
| D-ML3 | Daemon edge: Kestrel bound only to `127.0.0.1:8789` (and `[::1]:8789`); non-loopback config exits 2. Bearer token from user env `MERIDIAN_LAB_TOKEN` (at least 32 bytes after base64url decode, `CryptographicOperations.FixedTimeEquals`, one opaque 401). Host allowlist `127.0.0.1:8789`, `localhost:8789`, `[::1]:8789`, else 403; any `Origin` header 403. Mutex `Local\MeridianLab-<user SID>` plus pidfile, exit 3 when running. Refuses to start elevated (exit 2). Per-user scheduled task at logon. | Accepted | F1; the daemon controls everything, so it fails closed. |
| D-ML4 | One lease for the whole lab, Cimmeria semantics (F2), `TimeProvider`-driven. Lease id is 128 random bits, returned only to the acquirer, required as `lease_id` on every LEASED and MACHINE call. Releasing or expiring the lease also revokes any machine approval and stops the watchdog. | Accepted | Two sessions must never share the machine. |
| D-ML5 | Tool tiers `LabGate { Open, Leased, OwnLease, Machine }`; the policy table is the only source of a tool's gate. An unclassified tool is refused at runtime (`Lab.Unclassified`) and `ToolPolicyTests` fails CI. | Accepted | Deny by default (F3, stricter). |
| D-ML6 | The lab owns its stack: containers labelled `meridian-lab=1` on ports 15432 (Postgres), 15673 (RabbitMQ AMQP), 16379 (Redis); Gateway on 15000 and Nodes on 15040 as child processes; binaries from `lab_build` under the lab data root. It never uses the Aspire AppHost, dev ports or dev volumes. | Accepted | F11; deterministic, wipeable, no damage to the dev DB. |
| D-ML7 | The lab references `Dhadgar.Nodes`, `Dhadgar.Agent.Core`, `Dhadgar.Shared` and `Dhadgar.Contracts` and calls the product's own types (token service, DTOs). No copies. Its entry point is an explicit `Dhadgar.Lab.LabProgram` class (no top-level statements) so Nodes' public `Program` does not clash (CS0436). | Accepted | Contract-first rule; F9, F10. |
| D-ML8 | Secrets never leave the daemon: minted tokens, generated keys and DB passwords live in an in-memory `SecretVault` and are handed out as refs (`tok-1`, `key-1`). Logs, audit, tool results and bundles pass through a `Redactor` that knows every vault value; the bundle writer runs a secret scan before finalising. | Accepted | Tokens and keys are the most sensitive data the lab touches. |
| D-ML9 | Clock control without touching clocks: config knobs (`Mtls:RenewalGraceMinutes`, agent renewal threshold 90 days per D-S221) plus a lab-DB fixture tool `nodes_cert_time_shift` that moves a named node's certificate timestamps (`IssuedAt`, `NotBefore`, `NotAfter`, and `GraceUntil` after Sprint 2) into the past. Never the system clock; no product clock hook. | Accepted | Cheapest honest way to reach grace and expiry states. |
| D-ML10 | UAT format and grading per the contract (C6, C7): TOML schema 1, `[[row.step]]`, `[[row.expect]]`; SQL params are bound, never interpolated; grade precedence FAIL > BLOCKED > UNVERIFIED > NEEDS_HUMAN > PASS, SKIPPED when not run; PASS needs every required clause evaluated and held. Human clauses are attested only by the user (`uat_attest` is not in the driver's tool list). | Accepted | F4; missing evidence never passes. |
| D-ML11 | Windows-only parts sit behind `[SupportedOSPlatform("windows")]` plus `OperatingSystem.IsWindows()`. On Linux those tools are still registered and classified and return `Lab.Unavailable`. Windows tests use `[SkippableFact]`; Docker tests carry `[Trait("RequiresDocker","true")]`; tests that need elevation carry `[Trait("RequiresElevation","true")]` and skip unless `MERIDIAN_LAB_ELEVATED_TESTS=1`. | Accepted | F14; CI is ubuntu. |
| D-ML12 | MACHINE tier runs through a separate elevated helper (`Dhadgar.Lab.Elevated`) launched with `runas`, so the owner clicks a UAC consent prompt (secure desktop, an agent cannot click it). It serves one named pipe ACL'd to the user SID, accepts only the daemon's PID as client, executes a fixed verb set with its own argument validation, and exits when its approval window (default 60, max 120 minutes) ends, the lease ends or the daemon exits. Helper binary installed under `%ProgramFiles%\Meridian Lab\` by an elevated `install.ps1 -LabMachine`; the daemon refuses a helper from a user-writable path. Alternatives: a permanently installed elevated Windows service (no prompt, standing privilege) or running the daemon elevated (rejected, D-ML3). | **BlockedDecision** | Owner choice: approval friction versus standing privilege on the machine. Blocks L19-L21. Recommended: as written. |
| D-ML13 | Which machine may run MACHINE-tier tools. Recommended: any machine whose owner ran `install.ps1 -LabMachine` (it writes `%ProgramData%\Meridian Lab\lab-machine.marker`, admin-only ACL); without the marker every MACHINE tool returns `Lab.MachineNotMarked`. `agent_footprint_reset` removes only certificates issued by the lab Nodes CA and only the agent's own key, service and state. Alternative: a dedicated VM only. | **BlockedDecision** | The tier installs services and certificates on LocalMachine of Steve's workstation. Blocks live use of L21 (code can be written); blocks L27. |
| D-ML14 | How the live agent trusts the lab Gateway's TLS endpoint and the lab agent CA: the lab adopts whatever Sprint 2's D-S222 (LocalMachine\Root trust anchor) rules. If it rules "Root", `trust_root_add/remove` helper verbs are added in L25 and remove only the lab CA by thumbprint; otherwise L25 wires the mechanism Sprint 2 ships. | **BlockedDecision** | Owner decision already open in Sprint 2. Blocks L25 and every L26 row that needs a connected agent (console and service); the lab-only rows proceed. |
| D-ML15 | MSI install leg in UAT (Sprint 2 D-S223). Out of scope until decided; a follow-up packet adds `agent_msi_install` if the owner wants it. | **BlockedDecision** | Owner decision already open in Sprint 2. Blocks nothing in this plan. |
| D-ML16 | One local campaign branch `campaign/meridian-lab` cut from `main`, worktree `.claude/worktrees/meridian-lab`; packet branches `ml/lNN-<slug>` merged locally by a Sonnet `general-purpose` agent (ops cannot merge locally). After Sprint 2's PR merges to `main`, `main` is merged into the campaign branch (wave 5 gate). Pushed **once**, **one** PR to `main` with the `security-review` label, merged after the user's yes. | Accepted | Owner rule D-S215 applied to this campaign. |
| D-ML17 | Supersedes Sprint 2's lab/UAT-automation scope: its wave 2U (P18 driver script `scripts\uat\Invoke-AgentWindowsUat.ps1`, P19 stack, P20 agent legs, P21 renewal, P22 service, P23 MSI) as the long-term UAT apparatus. The orchestrator decides whether wave 2U still ships inside Sprint 2 as a stopgap or is dropped; if it ships, L26 ports its scenarios to TOML rows and L28 records which script legs the lab replaces. | Accepted | Brief; avoids two lasting UAT designs. Ownership of the Sprint 2 ledger stays with the orchestrator. |
| D-ML18 | `uat-driver` agent: Haiku, low effort, 50 turns, `omitClaudeMd: true`, tools limited to an explicit list of `mcp__meridian-lab__*` tools that excludes every MACHINE tool, `lab_machine_approval_*`, `uat_attest` and all file/shell tools; `DriverToolListTests` enforces it. MACHINE steps reach the machine only inside `uat_run` while the user's approval window is open. Forced lease takeover is forbidden by the driver's instructions (the daemon cannot tell callers apart on one token). | Accepted | F5; the driver runs steps and reports, nothing else. |
| D-ML19 | Process supervision: children started with `ArgumentList`, assigned to a kill-on-close Job Object on Windows, stdout/stderr captured as JSON console logs (`Logging__Console__FormatterName=json`) into a per-process ring buffer and `logs/<process>.jsonl`. The watchdog relaunches a crashed child (max 3 in 5 minutes) only while a lease is held. | Accepted | F2; logs are the main evidence source. |

## Seam tests

| ID | Seam | Test | Written red in | Green in |
| --- | --- | --- | --- | --- |
| S-L1 | Claude Code (MCP client) ↔ daemon HTTP edge | `tests/Dhadgar.Lab.Tests/Seams/McpEdgeSeamTests.cs`: a real `ModelContextProtocol` client against the daemon on real loopback Kestrel lists tools and calls `lab_status`; raw `HttpClient` calls without token, with a wrong token, with an `Origin` and with a bad `Host` get 401/401/403/403 | L01 | L05 |
| S-L2 | Lab token mint ↔ Nodes enrollment | `Seams/NodesEnrollSeamTests.cs`: the lab's `NodesLabAdapter` mints a token into the Nodes DB the real Nodes app uses (in-process `WebApplicationFactory<Program>`), then posts a `Dhadgar.Contracts.Nodes.EnrollNodeRequest` to `/agents/enroll` and expects 201. A `RequiresDocker` variant runs Nodes on the lab Postgres through the lab Gateway route `/api/v1/agents/enroll`. | L01 | L11 |
| S-L3 | Lab ↔ Windows agent footprint | `Seams/AgentFootprintSeamTests.cs`: the agent's service name, CA store name, registry key path and state file name equal `Dhadgar.Contracts.Agents.AgentWindowsFootprint` (after L23 the agent uses the type, and the test asserts by reference) | L12 | L23 |
| S-L4 | Daemon ↔ elevated helper pipe | `Seams/HelperPipeSeamTests.cs` (Windows, `SkippableFact`): the helper's real pipe server and the daemon's real `HelperClient` in one process exchange `ping` and a refused verb; a second client and a wrong nonce are refused | L19 | L20 |
| S-L5 | `uat-driver` agent ↔ tool registry | `DriverToolListTests`: every tool in `.claude/agents/uat-driver.md` exists in the registry and none is MACHINE tier or excluded (D-ML18) | L18 | L18 |
| S-L6 | Runner ↔ live lab (end to end) | `Uat/LabSelfTestE2ETests.cs` (`RequiresDocker`): `uat_run` of `docs/guides/uat-specs/lab-selftest.toml` grades every row PASS on a fresh lab stack | L17 | L17 |

## Packets

| Packet | Wave | Title | Depends on | Security review | Status |
| --- | --- | --- | --- | --- | --- |
| L01 | 1 | Scaffold projects, packages, isolation test, seam stubs, red S-L1/S-L2 | - | yes | Ready |
| L02 | 1 | Daemon edge: loopback Kestrel, token, Host/Origin, mutex, `/status`, CLI `daemon run`/`token new` | L01 | yes | BlockedDependency |
| L03 | 1 | `LeaseBook`, sweeper, history, `lab.lease` events | L01 | yes | BlockedDependency |
| L04 | 1 | Audit log, `SecretVault`, `Redactor` | L01 | yes | BlockedDependency |
| L05 | 1 | Tool registry, policy gate, MCP projection, lease and status tools; S-L1 green | L02, L03, L04 | yes | BlockedDependency |
| L06 | 1 | `tools/lab` scripts, scheduled task, `.mcp.json`, install | L05 | yes | BlockedDependency |
| L07 | 2 | Process supervisor, Job Object, log capture, watchdog, `logs_tail` | L05 | yes | BlockedDependency |
| L08 | 2 | Docker stack (`stack_up`/`stack_down`/`stack_status`), lab DB roles | L05 | yes | BlockedDependency |
| L09 | 2 | `lab_build`, Gateway and Nodes services (`svc_*`), config overlay allowlist | L07, L08 | yes | BlockedDependency |
| L10 | 2 | `db_query` (read-only role) and `http_request` (lab hosts only) | L08 | yes | BlockedDependency |
| L11 | 2 | Nodes adapter: `nodes_token_mint`, `nodes_enroll_probe`, `nodes_cert_time_shift`; S-L2 green | L09, L10 | yes | BlockedDependency |
| L12 | 2 | Windows reads (`certstore_list`, `registry_read`, `state_file_read`), `lab_doctor`, `AgentWindowsFootprint`, red S-L3 | L05 | yes | BlockedDependency |
| L13 | 3 | Spec model, TOML loader, validator, `uat_list_specs`, `uat_plan` | L05 | yes | BlockedDependency |
| L14 | 3 | Clause evaluator: sources, ops, `${var}`, capture | L13, L10, L12 | yes | BlockedDependency |
| L15 | 3 | Runner and grader, `uat_run` (OWN_LEASE) | L14 | yes | BlockedDependency |
| L16 | 3 | Evidence bundle, `ledger.md`, secret scan, `uat_report`, `uat_attest`, `bundle_read` | L15 | yes | BlockedDependency |
| L17 | 3 | CLI `status`/`uat`, `lab-selftest.toml`, S-L6 | L16, L11 | yes | BlockedDependency |
| L18 | 3 | `uat-driver` agent, `lab-uat` skill, S-L5 | L16 | yes | BlockedDependency |
| L19 | 4 | Elevated helper project, pipe protocol, verb validation, red S-L4 | L05; D-ML12 | yes | BlockedDecision |
| L20 | 4 | Daemon approval flow, helper launcher, machine marker, MACHINE gate; S-L4 green | L19; D-ML12 | yes | BlockedDecision |
| L21 | 4 | MACHINE verbs: agent service/console, registry token, footprint reset | L20, L12; D-ML12 (D-ML13 for live use) | yes | BlockedDecision |
| L22 | 4 | Docs: ADR, guides, security page (documentation-writer) | L18, L21 | security-advisor read | BlockedDependency |
| L23 | 5 | Agent consumes `AgentWindowsFootprint`; S-L3 green | Sprint 2 on `main`, L12 | yes | BlockedDependency |
| L24 | 5 | Sprint 2 evidence: `GraceUntil` shift, hub connect events and `hub` source, `agent_log` source | Sprint 2 on `main`, L21 | yes | BlockedDependency |
| L25 | 5 | Lab Gateway TLS and mTLS, agent trust wiring, Sprint 2 config allowlist | L24; D-ML14 | yes | BlockedDecision |
| L26 | 5 | UAT specs `agent-console.toml`, `agent-service.toml`; every-spec-validates test | L25 | yes | BlockedDependency |
| L27 | 5 | First live UAT run by the user (manual, evidence to `worknotes/`) | L26; D-ML13 | n/a | BlockedDecision |
| L28 | 6 | Close-out: `PROJECT-STATE.md`, docs sweep, handoff | all | security-advisor read | BlockedDependency |

## Waves and merge order

| Wave | Packets | Local merge order into `campaign/meridian-lab` | Parallel start |
| --- | --- | --- | --- |
| 1 | L01-L06 | L01 → L02 → L03 → L04 → L05 → L06 | L02, L03, L04 together after L01 |
| 2 | L07-L12 | L07 → L08 → L12 → L09 → L10 → L11 | L07, L08, L12 together; L09, L10 after L08 |
| 3 | L13-L18 | L13 → L14 → L15 → L16 → L17 → L18 | L13 alone, then serial; L18 beside L17 |
| 4 | L19-L22 | L19 → L20 → L21 → L22 | serial |
| 5 | L23-L27 | (merge `main` with Sprint 2) → L23 → L24 → L25 → L26 → L27 (user run, no merge) | L23, L24 together |
| 6 | L28 | L28 | - |

Ship: after the campaign review returns `ship` and L28 merges, ops pushes `campaign/meridian-lab` once and opens one PR to `main` (`security-review` label); merge after the user's yes. No per-packet or per-wave push or PR.

## Gates

- Wave 1: S-L1 green on the campaign tip; S-L2 red as expected; wave review `ship`.
- Wave 2: S-L2 green (in-process variant in CI, Docker variant run locally once); wave review `ship`.
- Wave 3: S-L5 and S-L6 green (S-L6 locally, Docker); wave review `ship`.
- Wave 4: D-ML12 ruled before dispatch; S-L4 green on Windows; wave review `ship`.
- Wave 5: Sprint 2's PR merged to `main`, then `main` merged into the campaign branch with every lab test green; S-L3 green; D-ML14 ruled before L25; D-ML13 ruled before L27.

## Dependencies on Sprint 2

| Lab item | Needs from Sprint 2 | Why |
| --- | --- | --- |
| L23 | P11 (state directory), P14 (CA store `MeridianConsoleAgent`) | edits agent files Sprint 2 changes |
| L24 | P16 (`GraceUntil`), the AgentHub (`/hubs/agent`) | grace and hub evidence |
| L25 | D-S210 (Gateway Kestrel mTLS), D-S212 (`Mtls:RenewalGraceMinutes`), D-S221 (renewal threshold), D-S222 (trust anchor) | the live agent must reach the lab Gateway |
| L26 | all of Sprint 2's agent and Nodes flows | the rows are Sprint 2's acceptance flows |
| Waves 1-4 | nothing | built and tested against `main` |

## Status honesty

Until L27 records a user-run bundle, `docs/PROJECT-STATE.md` may say "meridian-lab: code exists". "Confirmed working" needs S-L1 to S-L6 green plus the L27 ledger copied to `worknotes/`.
