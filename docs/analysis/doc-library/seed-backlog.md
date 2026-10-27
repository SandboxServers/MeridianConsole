# Doc library: seed backlog

> **Input for a campaign, not a ledger.** Written 2026-10-27 from a survey of `main` at `2d8e4d0`. The architect turns this into the campaign's `README.md`, `work-packets.md` and `RESUME.md` per the campaign-packet skill; the `documentation-writer` agent writes the docs. Rules: [docs/meta/conventions.md](../../meta/conventions.md) and [how-to-write.md](../../meta/how-to-write.md).

## Goal

Fill the library at [docs/README.md](../../README.md) so a person or a Claude session can answer these four questions from `docs/` alone, without reading code first:

1. What is this platform, and how do the pieces fit? (architecture, glossary)
2. Why is it built this way? (ADRs, including decisions already made but never written down)
3. What does each component do today, and how do we know? (reference chapters with evidence and honest status)
4. What are we about to build, and on what fixed path? (plans)

## Ground rules for the campaign

- **Docs only.** No source, test or CI changes. If a doc finds a bug, it files an issue and records the gap.
- **Status honesty.** Each chapter's `implementation` matches PROJECT-STATE or the doc reports the mismatch. Nothing becomes `confirmed-working` without a named seam test or a recorded manual check.
- **Backfilled ADRs stay `Proposed`** until the owner accepts them. Each cites where the decision was actually made.
- **Move, don't duplicate.** When an old doc has the content, move and tighten it, then mark the original `superseded`. Phase 6 finishes the archive.
- **Security docs get a `security-advisor` read** (anything in `docs/security/`, the agent chapters, the enrollment and login seams).
- **Sprint 2 overlap.** The `sprint2-agent-nodes` campaign is in flight and touches the agent hub, heartbeat, mTLS and renewal. Write those docs as plans now and convert them to chapters at sprint 2 close-out, or schedule them after it. Don't race its packets.

## Size key

| Size | Rough effort |
| --- | --- |
| S | Under 150 lines, mostly moving existing text |
| M | A full chapter from code plus one or two existing docs |
| L | A chapter that needs real code reading across several projects, or a contested topic |

---

## Phase 0: apparatus (done in this change)

| ID | Doc | Path | State |
| --- | --- | --- | --- |
| DL-000a | Documentation-writer agent | `.claude/agents/documentation-writer.md` | Written |
| DL-000b | Conventions, how-to-write, glossary seed, doc-update map, templates | `docs/meta/` | Written, `draft` |
| DL-000c | Master index and section indexes | `docs/README.md`, `docs/*/README.md` | Written |
| DL-000d | Where-things-live guide for agents | `docs/agents/domain.md` | Written |

### Phase 0 follow-ups: tooling (needs code; separate from the doc packets)

| ID | Item | Why |
| --- | --- | --- |
| DL-T1 | `markdownlint-cli2` config and a CI job over `docs/**` | Nothing lints Markdown today. Start warn-only. |
| DL-T2 | Link checker (`lychee`) over `docs/**` and `src/**/README.md` | Moves in Phase 6 will break links. |
| DL-T3 | Frontmatter lint script: required keys present, valid enums, `confirmed-working` has `evidence`, `doc_id` unique, every doc listed in its section index | Enforces conventions.md cheaply. |
| DL-T4 | Remove `!docs/**` from `.coderabbit.yaml` path filters | Docs currently get no automated review. |

---

## Phase 1: orientation (read these first)

| ID | Doc | Path | Type | Size | Mine from |
| --- | --- | --- | --- | --- | --- |
| DL-101 | Architecture overview: services, agents, frontends, data stores, one diagram | `docs/architecture/overview.md` | explanation | M | root `README.md` architecture section, PROJECT-STATE §3 and §5, ADR-0005 |
| DL-102 | Request path: browser or CLI → Gateway → service → database/bus | `docs/architecture/request-path.md` | explanation | M | Gateway `appsettings.json`, `ServiceDefaults`, `docs/gateway-*.md` |
| DL-103 | Agent connection model: outbound-only, mTLS, enrollment, hub, commands | `docs/architecture/agent-connection-model.md` | explanation | M | ADR-0007, ADR-0008, sprint 2 README (F1–F12, D-S2x) |
| DL-104 | Tenancy and data ownership: organizations, which service owns which data, the shared database reality | `docs/architecture/tenancy-and-data.md` | explanation | M | ADR-0006, DV-10, `ServiceDefaults/MultiTenancy` |
| DL-105 | Deployment model: compose for beta, Aspire for dev, Helm deferred | `docs/architecture/deployment.md` | explanation | S | DV-6, DV-7, `deploy/compose/README.md`, `docs/architecture/K8S-*.md` |
| DL-106 | Local development, one path to a running stack (merges `DEVELOPMENT_SETUP.md` and the README's Getting Started) | `docs/getting-started/local-dev.md` | tutorial | M | `docs/DEVELOPMENT_SETUP.md`, `README.md`, `scripts/bootstrap-dev*`, `deploy/compose/` |
| DL-107 | Your first change: build, test, branch, PR, CI | `docs/getting-started/first-change.md` | tutorial | S | `CONTRIBUTING.md` (fix the `develop` branch error), `.github/workflows/ci.yml`, ship-pr skill |
| DL-108 | Glossary: verify the seed and fill gaps | `docs/meta/glossary.md` | reference | S | everything above |

## Phase 2: decisions already made (ADR backfill)

Each ADR cites where the decision was actually made, and stays `Proposed` until the owner accepts it. Numbering continues from 0008; final numbers are assigned at write time.

| ID | Decision | Made in | Notes |
| --- | --- | --- | --- |
| DL-201 | `Result<T>` instead of exceptions for validation, IO and network failures | CLAUDE.md; issue #119 | Records the current split (Identity and Nodes have their own `ServiceResult`) as a known gap. |
| DL-202 | Central Package Management; no versions in `.csproj` | CLAUDE.md; `Directory.Packages.props` | Include the transitive-pinning practice from #215. |
| DL-203 | Contract first: cross-boundary types only in `Dhadgar.Contracts`, every changed seam gets a seam test | `docs/agents/ai-sdlc.md`; D-S21 | Context: the enrollment disagreement (DV-1). |
| DL-204 | Status honesty: "code exists" vs "confirmed working" | `docs/agents/ai-sdlc.md`; PROJECT-STATE | May fold into DL-203. |
| DL-205 | Beta scope is the full vertical slice | PROJECT-STATE D1 | |
| DL-206 | Canonical `50x0` port scheme | PROJECT-STATE D2; `SERVICE-PORTS.md` | |
| DL-207 | Hybrid auth: BetterAuth for social login, Identity (OpenIddict) issues platform tokens via exchange | issue #120; `docs/architecture/authentication-analysis.md` (superseded) | Also records "frontends are Astro, not Blazor". |
| DL-208 | Refresh-token rotation with family revocation on reuse | DV-3 fix, Sprint 1 | |
| DL-209 | Agent generates its own key pair; Nodes only signs it (no server-generated private keys or PKCS#12) | Sprint 1 enrollment fix, PROJECT-STATE status table | |
| DL-210 | Agent identity comes only from the client certificate (`node_id` claim) | D-S23 | |
| DL-211 | Agent CA in a custom machine store, never `LocalMachine\Root`; certificates found by profile, not subject name | D-S219 | Security-advisor read. |
| DL-212 | Agent mTLS terminates at the Gateway with a trusted-proxy CIDR | D-S210 | Security-advisor read. |
| DL-213 | Certificate renewal keeps a short grace window for the old certificate | D-S212 | |
| DL-214 | Agent hub protocol is versioned by the `X-Agent-Protocol` header, not the path | D-S22; #116 | |
| DL-215 | Hub business errors return in the ack's `ErrorCode`; auth failures are 401/403 problem+json | D-S24, D-S27 | |
| DL-216 | Single Nodes replica with cookie affinity for beta; Redis backplane deferred | D-S217; ADR-0008 | |
| DL-217 | Compose is the beta deploy target; Aspire is dev-only; Kubernetes and Helm deferred | DV-6, DV-7; `docs/architecture/K8S-*.md` | Supersedes the never-promoted K8s/Talos doc. |
| DL-218 | GitHub Actions replaces Azure Pipelines for CI | `.github/workflows/`; root `azure-pipelines*.yml` now manual-only | |
| DL-219 | WiX v5 MSI for the Windows agent; Windows 10+ detected by `WindowsBuild >= 10240` | CLAUDE.md; `Installer/Package.wxs` | |
| DL-220 | Windows game-server processes are contained in Job Objects | CLAUDE.md; `WindowsProcessManager` | |
| DL-221 | Shared platform database for most services, for now (database-per-service deferred) | DV-10; ADR-0005/0006 divergence notes | Records the deferral, not a final answer. Owner decision needed. |
| DL-222 | Files service is removed | #115 | Confirm status with owner. |
| DL-223 | Scalar replaces Swagger UI for API docs | service code; README drift | Small; may fold into a reference chapter instead. |

## Phase 3: reference chapters (code already written)

`implementation` below is what PROJECT-STATE implies today; the writer confirms it.

### Services

| ID | Chapter | Path | Expected implementation | Size | Mine from |
| --- | --- | --- | --- | --- | --- |
| DL-301 | Gateway: routes (both forms), clusters, rate limits, circuit breaker, headers, OpenAPI aggregation | `docs/reference/services/gateway.md` | code-exists | L | `src/Dhadgar.Gateway`, `docs/gateway-routing-reference.md` (stale), `docs/gateway-authentication.md`, `docs/implementation-plans/gateway-service.md` |
| DL-302 | Identity | `docs/reference/services/identity.md` | code-exists | L | `docs/identity-*.md` (four files), `src/Dhadgar.Identity/README.md` |
| DL-303 | BetterAuth | `docs/reference/services/betterauth.md` | code-exists | M | `docs/betterauth-api-reference.md`, `src/Dhadgar.BetterAuth/src/*.js` |
| DL-304 | Nodes (inventory, enrollment, CA, heartbeat, reservations) | `docs/reference/services/nodes.md` | mixed | L | `src/Dhadgar.Nodes`, sprint 2 README findings |
| DL-305 | Secrets | `docs/reference/services/secrets.md` | code-exists | M | `docs/secrets-api-reference.md` plus the three secrets planning docs (fold, then archive) |
| DL-306 | Notifications | `docs/reference/services/notifications.md` | code-exists | M | `src/Dhadgar.Notifications` (README badge says Stub; it isn't) |
| DL-307 | Discord | `docs/reference/services/discord.md` | code-exists | M | `src/Dhadgar.Discord` (same badge problem) |
| DL-308 | CLI: command tree and auth | `docs/reference/services/cli.md` | code-exists | M | `src/Dhadgar.Cli` (~70 commands) |
| DL-309 | Stub services in one page: what exists in Servers, Console, Mods, Tasks, Files and Billing today | `docs/reference/services/stubs.md` | code-exists | S | each stub project; their huge READMEs become plans in Phase 5 |

### Agents

| ID | Chapter | Path | Expected implementation | Size | Mine from |
| --- | --- | --- | --- | --- | --- |
| DL-311 | Agent.Core | `docs/reference/agents/core.md` | code-exists | L | `src/Agents/Dhadgar.Agent.Core` (Authentication, Commands, Communication, Files, Process, Health, Telemetry) |
| DL-312 | Agent.Windows: service host, Job Objects, named-pipe IPC, ACLs, firewall, certificate store | `docs/reference/agents/windows.md` | code-exists | L | `src/Agents/Dhadgar.Agent.Windows` |
| DL-313 | Windows installer (WiX MSI) | `docs/reference/agents/windows-installer.md` | code-exists | M | `Installer/Package.wxs`, `Installer/README.md` |
| DL-314 | GameServerWrapper | `docs/reference/agents/game-server-wrapper.md` | code-exists | M | `src/Agents/Dhadgar.Agent.GameServerWrapper` (no README today) |

### Shared libraries and frontends

| ID | Chapter | Path | Expected implementation | Size | Mine from |
| --- | --- | --- | --- | --- | --- |
| DL-321 | Dhadgar.Contracts: layout, rules, what lives where | `docs/reference/shared/contracts.md` | code-exists | M | `docs/libraries/contracts.md`, `src/Shared/Dhadgar.Contracts` |
| DL-322 | Dhadgar.ServiceDefaults | `docs/reference/shared/service-defaults.md` | code-exists | M | `docs/libraries/servicedefaults.md` |
| DL-323 | Dhadgar.Messaging | `docs/reference/shared/messaging.md` | code-exists | S | `docs/libraries/messaging.md` |
| DL-324 | Dhadgar.Shared (`Result<T>`, Guard, EntityId) | `docs/reference/shared/shared.md` | code-exists | S | `docs/libraries/shared.md` |
| DL-325 | Dhadgar.Testing fixtures and fakes | `docs/reference/shared/testing.md` | code-exists | S | `src/Shared/Dhadgar.Testing/README.md` |
| DL-326 | SharedAuth (TypeScript) | `docs/reference/frontends/shared-auth.md` | code-exists | S | `src/Dhadgar.SharedAuth` |
| DL-327 | Panel | `docs/reference/frontends/panel.md` | mixed | M | `src/Dhadgar.Panel` |
| DL-328 | Scope | `docs/reference/frontends/scope.md` | code-exists | S | `src/Dhadgar.Scope` |
| DL-329 | ShoppingCart | `docs/reference/frontends/shopping-cart.md` | mixed | S | `src/Dhadgar.ShoppingCart` (README.SWA.md says Blazor; it's Astro) |

### Seams (the highest-value chapters)

| ID | Chapter | Path | Expected implementation | Size | Mine from |
| --- | --- | --- | --- | --- | --- |
| DL-331 | Agent enrollment | `docs/reference/seams/enrollment.md` | confirmed-working | M | `AgentEnrollmentContractTests`, `EnrollmentContracts.cs`, both `EnrollmentService`s |
| DL-332 | Login and tokens: BetterAuth → Identity exchange → refresh → SharedAuth | `docs/reference/seams/login-and-tokens.md` | code-exists | L | DV-3 status row, token endpoints, `SharedAuth/src/client.ts` |
| DL-333 | Gateway to service routing (the prefix contract) | `docs/reference/seams/gateway-routing.md` | code-exists | M | DV-1 status row, Gateway config test |
| DL-334 | Service-to-service auth and the admin API key | `docs/reference/seams/service-auth.md` | code-exists | M | `ServiceAuthenticationHandler.cs`, DV-13 |
| DL-335 | Events on the bus: every published message, producer and consumers | `docs/reference/seams/events.md` | code-exists | M | `Contracts/*/…Events.cs`, consumers |

### Platform-wide reference

| ID | Chapter | Path | Size | Mine from |
| --- | --- | --- | --- | --- |
| DL-341 | Ports and addresses (replaces `SERVICE-PORTS.md`) | `docs/reference/platform/ports.md` | S | `SERVICE-PORTS.md`, launchSettings, compose |
| DL-342 | Configuration: layering, secrets, every options class | `docs/reference/platform/configuration.md` | M | `docs/CONFIGURATION-MANAGEMENT.md`, `*Options.cs` |
| DL-343 | Error model: problem+json, error codes | `docs/reference/platform/errors.md` | S | `ServiceDefaults/Problems/ErrorCodes.cs`, `GlobalExceptionHandler.cs` |
| DL-344 | Observability: activity sources, meters, log conventions, collector | `docs/reference/platform/observability.md` | M | `ServiceDefaults/Tracing`, `NodesMetrics.cs`, `AgentMeter.cs`, `deploy/compose/otel-collector-config.yml` |
| DL-345 | Persistence: DbContexts, databases, migrations (fix the seven wrong `Migrations/README.md` files) | `docs/reference/platform/persistence.md` | M | `*DbContextFactory.cs`, `Data/Migrations` |
| DL-346 | Messaging: MassTransit setup, retries, faults | `docs/reference/platform/messaging.md` | S | `MessagingExtensions.cs`, `FaultConsumer.cs` |
| DL-347 | Dependencies with rules (OpenTelemetry pin, SIPSorcery status) | `docs/reference/platform/dependencies.md` | S | CLAUDE.md § Package Notes, `Directory.Packages.props` |

## Phase 4: security, testing, guides and runbooks

| ID | Doc | Path | Type | Size | Mine from |
| --- | --- | --- | --- | --- | --- |
| DL-401 | Threat model: trust boundaries, assets, attackers, what each control stops | `docs/security/threat-model.md` | explanation | L | ADR-0007, DV-13, `SECURITY.md` |
| DL-402 | Security rules explained (path validation, command injection, SSRF, sensitive data), each with where it's enforced | `docs/security/rules.md` | reference | M | CLAUDE.md (quote exactly); fix the nonexistent `WindowsPathValidator`/`LinuxPathValidator` reference there |
| DL-403 | Agent security: certificates, command validation, signing status, process containment | `docs/security/agent-security.md` | explanation | M | ADR-0007, #94, #118, D-S219 |
| DL-404 | Auth model: who authenticates how, at which layer | `docs/security/auth-model.md` | explanation | M | DL-207, DL-332, DV-13 |
| DL-405 | Security-review areas and how to request a review | `docs/security/review-areas.md` | reference | S | CLAUDE.md § Code Review, security-advisor agent |
| DL-411 | Test strategy: unit, integration (Testcontainers), seam tests, where each goes | `docs/testing/strategy.md` | explanation | M | `tests/`, `Dhadgar.Testing`, ai-sdlc.md |
| DL-412 | Seam test catalogue: every seam, its test, its status | `docs/testing/seam-tests.md` | reference | S | `AgentEnrollmentContractTests`, sprint 2 S1–S4 |
| DL-421 | Add a contract type and its seam test | `docs/guides/add-a-contract.md` | how-to | S | ai-sdlc.md, `Dhadgar.Contracts` |
| DL-422 | Add a Gateway route | `docs/guides/add-a-gateway-route.md` | how-to | S | DL-301 |
| DL-423 | Add an EF Core migration | `docs/guides/add-a-migration.md` | how-to | S | DL-345 |
| DL-424 | Add a MassTransit consumer | `docs/guides/add-a-consumer.md` | how-to | S | DL-346 |
| DL-425 | Add or update a package (CPM) | `docs/guides/add-a-package.md` | how-to | S | DL-202 |
| DL-426 | Add a CLI command | `docs/guides/add-a-cli-command.md` | how-to | S | DL-308 |
| DL-427 | Add a new service | `docs/guides/add-a-service.md` | how-to | M | ServiceDefaults, Gateway, compose, AppHost |
| DL-428 | CI: what runs, how to read a failure | `docs/guides/ci.md` | how-to | S | `.github/workflows/` |
| DL-431 | Refresh the four existing runbooks to the template and current code | `docs/runbooks/*.md` | runbook | M | existing runbooks (Jan) |
| DL-432 | Run the stack with compose; upgrade; reset | `docs/runbooks/compose-stack.md` | runbook | M | `deploy/compose/README.md` |
| DL-433 | Build, sign and install the Windows agent MSI; uninstall cleanly | `docs/runbooks/windows-agent-install.md` | runbook | M | `Installer/README.md` |
| DL-434 | Agent CA and certificate operations: rotate CA, revoke a node, re-enroll | `docs/runbooks/agent-certificates.md` | runbook | M | Nodes CA services, D-S212 |
| DL-435 | Troubleshooting: first-day failures | `docs/runbooks/troubleshooting.md` | runbook | S | `scripts/verify-environment*`, issues |

## Phase 5: plans (decided path, code not yet written)

Each plan separates what's decided (with its record) from what's open. The big stub READMEs are the main source; their spec content moves here and the READMEs shrink to landing pages.

Rows whose "Decided by" is `tbd` or deferred (DL-506, DL-509, DL-511, DL-512, DL-515) get a plan only if the owner settles the path first. Otherwise they stay as one-line entries under "Unplanned" in [docs/plans/README.md](../../plans/README.md), which already lists them.

| ID | Plan | Path | Decided by | Mine from |
| --- | --- | --- | --- | --- |
| DL-501 | Agent hub, heartbeat over the hub, bootstrap service, agent state store | `docs/plans/agent-hub.md` | ADR-0008, D4, D-S21–D-S29, D-S217 | sprint 2 `work-packets.md`. Converts to `ref.seams.agent-hub` at sprint 2 close-out. |
| DL-502 | Certificate renewal with grace | `docs/plans/certificate-renewal.md` | D-S212 | sprint 2 P16 |
| DL-503 | Command handlers and command signing | `docs/plans/agent-commands.md` | ADR-0007; #94, #118 | PROJECT-STATE Phase 2 |
| DL-504 | Server lifecycle: create, start, stop through the control plane | `docs/plans/server-lifecycle.md` | D1, D3 (revive PR #88) | PROJECT-STATE Phase 3; Servers README |
| DL-505 | Console streaming | `docs/plans/console-streaming.md` | PROJECT-STATE Phase 3 | Console README |
| DL-506 | Mods | `docs/plans/mods.md` | tbd | Mods README |
| DL-507 | Panel pages for the beta slice | `docs/plans/panel-beta.md` | PROJECT-STATE Phase 4 | Panel README |
| DL-508 | API versioning | `docs/plans/api-versioning.md` | #116, D-S22 | PROJECT-STATE Phase 2 |
| DL-509 | Linux agent | `docs/plans/agent-linux.md` | D-S218 (deferred) | `implementation-plans/agent-linux.md`, `Agent.Linux/README.md` |
| DL-510 | Cross-service integration tests | `docs/plans/integration-tests.md` | #121 | PROJECT-STATE Phase 4 |
| DL-511 | Identity MFA (returns 501 today) | `docs/plans/identity-mfa.md` | tbd | Identity docs |
| DL-512 | Tasks, Billing: decide which have a known path | `docs/plans/tasks.md`, `docs/plans/billing.md` | tbd | stub READMEs. Write a plan only where a path is decided; otherwise list the item under "unplanned" in `docs/plans/README.md`. |
| DL-513 | Kubernetes and Helm (post-beta) | `docs/plans/kubernetes.md` | DV-7 | Helm READMEs, K8S doc |
| DL-514 | Aspire AppHost fix | `docs/plans/aspire.md` | DV-6 | `implementation-plans/aspire-orchestration.md` |
| DL-515 | Self-hosted edition (KiP) | `docs/plans/kip.md` | tbd | `docs/kip/README.md` |
| DL-516 | Lint and SAST pipeline (GitHub Actions) | `docs/plans/lint-and-sast.md` | DL-218 | `LINTER_*.md`, `SECURITY-SCANNING-*.md` |

## Phase 6: migrate, trim and archive

| ID | Item | Detail |
| --- | --- | --- |
| DL-601 | Archive superseded planning docs with banners | `docs/implementation-plans/*`, the three secrets docs, `LINTER_*.md`, `SECURITY-SCANNING-*.md`, `CONTAINER-*.md`, `docs/architecture/authentication-analysis.md`, `docs/architecture/K8S-*.md`, `DOCKERFILE-MIGRATION-SUMMARY.md` → `docs/archive/` |
| DL-602 | Fold `docs/libraries/` into `docs/reference/shared/` | Remove the duplicates of `src/Shared/*/README.md` |
| DL-603 | Fold the root-level `docs/*.md` API references into the service chapters | `identity-*`, `betterauth-*`, `secrets-*`, `gateway-*` |
| DL-604 | Trim every `src/<Project>/README.md` to a landing page (≤150 lines) | Purpose, run, test, key config, link to its chapter. Fix stale badges. Add READMEs for AppHost and GameServerWrapper. |
| DL-605 | Trim every `src/<Project>/CLAUDE.md` to agent-only rules | Point to the chapter for everything else. |
| DL-606 | Fix the root `README.md` Documentation section | Point at `docs/README.md`; Scalar not Swagger; drop GEMINI.md as a doc |
| DL-607 | Fix `CONTRIBUTING.md` | No `develop` branch; point at first-change tutorial and ship-pr |
| DL-608 | Fix the seven wrong `src/*/Migrations/README.md` files | Real migrations live in `Data/Migrations` |
| DL-609 | Fix `docs/analysis/README.md` ("No campaigns yet") | |
| DL-610 | Retire `docs/kip/` and `docs/runbooks` duplicates once their content has moved | |
| DL-611 | Decide `.planning/`'s fate | Archive or delete with owner approval; it's legacy input |
| DL-612 | Campaign close-out: PROJECT-STATE §7 notes the library; glossary and indexes complete | |

## Owner decisions this backlog needs

| ID | Question |
| --- | --- |
| OD-1 | Run this campaign now, or after sprint 2 closes? (Recommendation: Phases 1, 2, 4-security and 3-non-agent now; agent and hub docs after sprint 2.) |
| OD-2 | Who reviews `draft` → `current`? One owner, or both owners split by area? |
| OD-3 | May Phase 6 move and archive old docs without asking per file, given banners and redirect links? |
| OD-4 | DL-221 (shared database) and DL-222 (Files removal): are these decided, or should the ADRs record "deferred"? |
| OD-5 | Does Scope (the Astro docs site) eventually publish from `docs/`, or stay separate? |
