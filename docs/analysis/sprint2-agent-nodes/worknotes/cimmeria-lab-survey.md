# Cimmeria lab apparatus: survey for the automated Windows UAT

Source: `C:\Users\Steve\source\projects\Cimmeria` (on this machine). Surveyed 2026-10-27 by an Explore agent; the Rust runner source (`crates\lab\src\uat\runner`) was not read in detail.

## How it works

- `cimmeria-lab` is one shared daemon (`--http 127.0.0.1:8779`), a per-user scheduled task (`tools\lab\daemon.ps1`), loopback only, bearer token, one per logon session (named mutex). The client config is `.mcp.json` with `type: http` and a `headersHelper` that reads the token from the user environment. ADR: `docs\architecture\live-research-lab.md`.
- Lease (`crates\lab\src\lease\mod.rs`) lives in the supervisor process: `lab_lease_acquire {owner, purpose, ttl_s}` (default 600 s, min 30, max 3600), a refusal names the holder, `force` needs a `reason` and is logged, guarded calls renew it, an idle lease expires, and the watchdog does not relaunch a dead client without a lease. `lease\policy.rs` classifies every tool (OPEN, LEASED, OWN_LEASE); an unlisted tool is guarded.
- Two paths: manual (acquire, ensure, drive, release) and spec-driven (`lab_uat_run {sections, rows, specs_dir, run_dir, plan_only, ...}`; run `plan_only` first, then batches of about 5 rows). No-agent CLI: `lab uat <section> --rows ... --json`.
- `lab-driver` agent (`.claude\agents\lab-driver.md`): Haiku, low effort, 50 turns, MCP tools only with about 40 explicitly disallowed. Runs exactly the given steps, reports raw results, no diagnosis or edits, retries a failed step once unchanged, stops on an unexpected state, never calibrates. Operating procedure `.claude\skills\lab-uat\SKILL.md`: ask the user before taking the lease, then verify the driver's claims against server evidence.

## Step-list and report formats (`docs\guides\automated-uat.md`, specs in `docs\guides\uat-specs\*.toml`)

- Spec = TOML: `schema = 1`, `[section]`, `[[row]]` with `id`, `title`, `expected`, `step = [...]`, and `[[row.expect]]` clauses (`id`, `source`, `since`, `op`, `value`; ops include `delta`, `any`, `min_rows`, `max_rows`). `${var}` substitution, `capture` between steps.
- Grades: PASS, FAIL, BLOCKED, SKIPPED, NEEDS_HUMAN, UNVERIFIED. A row passes only if every clause was checked and held; missing evidence never counts as a pass.
- Evidence bundle `%LOCALAPPDATA%\cimmeria-lab\uat-runs\<date>-<build>-<id>\`: `run.json`, `rows\<section>\<row>.json`, screenshots, `ledger.md` (summary table plus one "Recording results" block per row: System, Step id, Result, Time, Build, Saw, Expected). `lab_uat_report` summarises; `lab_uat_attest` records a human verdict and re-grades.

## Reusable for us

Lease semantics (ttl, named holder, forced takeover with reason, auto-expiry); deny-by-default tool classification; spec-as-data with `plan_only`; evidence bundle plus `ledger.md`; the "PASS needs every clause verified" rule; the driver-agent shell (cheap model, restricted tools, exact step list, raw one-line-per-step reports, retry once, stop on surprise); ask-before-lease and verify-the-driver.

## Game-specific, drop

Injected client bridge, CEGUI/Lua, camera and poses, `.bug` anchor, `.gm` commands, character creation, SigNoz via game, calibrate/heal/golden.

## Suggested shape here

A small `meridian-lab` runner on this Windows machine: spec rows (enroll, heartbeat, restart without re-enrolling, renew inside and after the grace window, no agent CA in LocalMachine\Root) with clauses on Nodes DB/log evidence and the cert store; a lease so two sessions never share the machine; a bundle plus `ledger.md`; a `uat-driver` agent restricted to it with an exact row list. Whether it needs an HTTP MCP daemon or just a PowerShell/dotnet runner plus a file-based lease is a design call.
