---
title: Doc-Update Map
doc_id: meta.doc-update-map
type: reference
audience: [engineers, reviewers, agents]
status: draft
implementation: n/a
last_verified: 2026-10-27
verified_by: automated-agent
related: [meta.conventions, meta.how-to-write]
---

# Doc-Update Map

Which docs a change has to update. Read it before you open a PR, and when you review one. A PR that changes behaviour, a contract, a route, configuration, file layout, build steps or test policy includes the matching doc update. CI doesn't check this yet; reviewers do.

Many targets below don't exist yet; the doc-library campaign writes them ([seed backlog](../analysis/doc-library/seed-backlog.md)). Until a target exists, note the owed doc in the PR description instead.

| If you change… | Update… |
| --- | --- |
| A type in `src/Shared/Dhadgar.Contracts` | The seam chapter under `docs/reference/seams/` for every seam that uses it, and the `ref.shared.contracts` chapter. Add or update the seam test listed in the chapter's evidence. |
| A Gateway route, cluster or transform (`src/Dhadgar.Gateway/appsettings*.json`) | `docs/reference/services/gateway.md` (the route table, both route forms), the affected seam chapters, and `docs/reference/platform/ports.md` if an address changes. |
| A service's HTTP endpoints, hub methods or events | That service's chapter in `docs/reference/services/` (Contract and Implementation sections), and the seam chapter if another component calls it. |
| Agent behaviour in `src/Agents/**` | The chapter in `docs/reference/agents/`, plus `docs/security/` if it touches a security-review area. |
| `IControlPlaneClient`, `CommandEnvelope`, `CommandValidator` or the process managers | The agent chapters, `docs/security/agent-security.md`, and the `security-advisor` review. |
| Enrollment, certificates, the agent CA or mTLS | `docs/reference/seams/enrollment.md`, `docs/runbooks/` for the CA and certificate procedures, and `docs/security/agent-security.md`. |
| Auth: BetterAuth, Identity tokens, SharedAuth, service-to-service auth | `docs/reference/seams/login-and-tokens.md`, the service chapters, and `docs/security/auth-model.md`. |
| Configuration keys, options classes or environment variables | `docs/reference/platform/configuration.md` and the "Operating it" section of the component's chapter. |
| Ports or service addresses | [SERVICE-PORTS.md](../../SERVICE-PORTS.md) until `docs/reference/platform/ports.md` replaces it, then that file only. |
| An EF Core model or migration | The service chapter's persistence notes and `docs/reference/platform/persistence.md`. |
| A MassTransit consumer, message or event | `docs/reference/platform/messaging.md` and the producing and consuming service chapters. |
| Telemetry: activity sources, meters, log fields, collector config | `docs/reference/platform/observability.md`. |
| Error codes or problem+json shapes (`ServiceDefaults/Problems/`) | `docs/reference/platform/errors.md`. |
| A NuGet or npm package added, removed or pinned | `docs/reference/platform/dependencies.md` when it carries a rule (like the OpenTelemetry pin), and CLAUDE.md § Package Notes when agents must know it. |
| A decision (new, reversed or deferred) | A new or amended ADR in `docs/adr/` and its index. Mark replaced ADRs `Superseded by`. |
| Code that implements a plan in `docs/plans/` | Write or update the reference chapter, set the plan to `superseded`, and move it to `docs/archive/`. |
| A feature reaching "confirmed working" | `docs/PROJECT-STATE.md` in the campaign close-out, and the chapter's `implementation` and `evidence` at the same time. |
| A new service, agent or shared library project | A reference chapter, a short `src/<Project>/README.md` landing page, `docs/architecture/overview.md`, the glossary, and a row in this map. |
| Local setup steps, scripts in `scripts/`, or prerequisites | `docs/getting-started/local-dev.md` and `docs/runbooks/troubleshooting.md` when it adds a failure mode. |
| Deploy artefacts: `deploy/compose`, Helm, Dockerfiles | `docs/runbooks/` for the procedure and `docs/architecture/deployment.md` for the model. |
| CI workflows in `.github/workflows/` | `docs/guides/ci.md` and [CONTRIBUTING.md](../../CONTRIBUTING.md). |
| Test conventions, fixtures in `Dhadgar.Testing`, or a new seam test | `docs/testing/strategy.md` and `docs/testing/seam-tests.md`. |
| The WiX installer (`Agent.Windows/Installer/`) | `docs/reference/agents/windows-installer.md` and the install runbook. |
| CLAUDE.md, `.claude/agents/`, skills or hooks | [docs/agents/ai-sdlc.md](../agents/ai-sdlc.md). Keep CLAUDE.md short: long rules go in a linked doc with a one-line pointer. |
| A doc added, renamed, moved or archived | [docs/README.md](../README.md), the section `README.md`, and links that point at the old path. |
