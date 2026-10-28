# meridian-lab

Dev and lab only, never production. `meridian-lab` is a local tool that drives the Meridian product services and agents on a developer machine or a lab host so their behaviour can be tested end to end. Nothing in this folder is deployed, referenced by a product project, or shipped in a container image. `LabIsolationTests` enforces that.

## Layout

- `LabProgram.cs`: entry point (`meridian-lab`).
- `LabDefaults.cs`, `LabPorts.cs`, `LabIds.cs`, `LabPaths.cs`, `LabHosts.cs`: constants, ports, identifiers, path rules and the loopback host guard.
- `Daemon/`: the loopback daemon that exposes the lab over HTTP with a bearer token.
- `Policy/`: `ToolPolicy` and `LabGate`, the policy check every tool call passes through.
- `Tools/`: `LabTool`, `LabToolRegistry`, `LabToolDispatcher` (the only call path), `LabJson` and `LabCallContext`, the per-call context.
- `Mcp/`: `LabMcpServer`, the MCP streamable-HTTP projection of the registry.
- `Leasing/`: `LeaseBook`, `LeaseSweeper` and `ILeaseEndHook`.
- `Audit/`: `LabEvent`, `ILabEvents` and `AuditLog`, the daily JSON-lines audit writer.
- `Secrets/`: `SecretVault` (secrets held by reference only) and `Redactor`.
- `Nodes/`: the adapter into the Nodes product (`INodesLabAdapter`, `NodesLabAdapter`, `EnrollProbe`).

## Lease

`Leasing/LeaseBook` holds one lease for the whole lab stack (C3).

- `owner` and `purpose` are required, 1 to 100 characters, with no control characters.
- TTL defaults to 600 s and is clamped to 30 to 3600 s. `Touch` extends expiry by the lease's TTL; a lease is expired at exactly `ExpiresAt`.
- A held lease refuses `Acquire` with `[Lab.LeaseHeld]`, naming the holder, purpose, start and expiry. `force` needs a `reason`; a forced takeover is recorded in history as `TakenOver`.
- Wrong lease ids on `Renew`, `Release` and `Touch` return `[Lab.LeaseRequired]` when free and `[Lab.LeaseHeld]` when another holder has the lease.
- Lease ids are 16 random bytes, hex encoded, and are never written to events, history or messages.
- `LeaseSweeper` expires overdue leases every 5 s. Every change emits one `lab.lease` event (`acquired`, `renewed`, `released`, `expired`, `taken_over`, `refused`) carrying owner, purpose, action and reason.
- History keeps the last `HistorySize` (50) ended leases.

## Ports (C1)

| Service | Port | Owner |
|---|---|---|
| Gateway | 15000 | product |
| GatewayTls | 15443 | L41 |
| Identity | 15010 | L34 |
| Nodes | 15040 | product |
| Notifications | 15090 | L36 |
| Discord | 15120 | L36 |
| BetterAuth | 15130 | L34 |
| Panel | 14321 | L34 |
| AgentIngress | 25000 | Toxiproxy agent-ingress to gateway |
| AgentIngressTls | 25443 | L41 |
| Barrier | 25080 | L49, lab-only barrier proxy (C10) |
| Postgres | 15432 | infra |
| RabbitMq | 15673 | infra |
| RabbitMqManagement | 16672 | L21 |
| Redis | 16379 | infra |
| ToxiproxyApi | 18474 | infra |
| Sink | 18090 | L36 |
| Daemon | 8789 | lab daemon (loopback only) |

## Daemon edge

`meridian-lab daemon run` starts the loopback daemon. The token comes from `MERIDIAN_LAB_TOKEN` (at least 32 bytes, base64url); `meridian-lab token new` prints a fresh one. The daemon listens on `127.0.0.1` and `[::1]` only; there is no supported way to bind elsewhere, so `Lab:BindAddress` set to anything but loopback is a refusal. Kestrel endpoint settings in `labsettings.json` or the environment are ignored, and a start that reports any non-loopback address is refused. `MERIDIAN_LAB_HOME` must be a user-private directory; on Unix the lab root is created 0700 and `daemon.pid` 0600.

Every request passes one guard before routing, in this order:

1. `Host` must be `127.0.0.1:<port>`, `localhost:<port>` or `[::1]:<port>` (the bound port) → **403**, empty body.
2. Any `Origin` header present → **403**, empty body.
3. Missing, malformed or wrong `Authorization: Bearer <token>` → **401**, empty body, `WWW-Authenticate: Bearer`. The same response for every cause.

Refusals are logged as `lab.edge.refused {reason}` with no values. The token and the `Authorization` header are never logged.

Routes: `GET /status` returns the `StatusDto` (idle until later packets contribute); `/mcp` serves the MCP streamable-HTTP endpoint (stateless, guarded by the same Host, Origin and bearer checks); anything else is **404** once the guard passes.

Exit codes of `meridian-lab daemon run`:

| Code | Meaning |
| --- | --- |
| 0 | clean stop (Ctrl+C or SIGTERM) |
| 2 | config refusal: short or missing token, non-loopback bind, elevated process, bad port |
| 3 | already running: the per-user mutex `Local\MeridianLab-<user>` is held |
| 1 | any other failure, such as the loopback port being in use |

A running daemon writes `daemon.pid` under the lab root and removes it on stop. The daemon refuses to run elevated.

## Rules

- The daemon binds to loopback only and requires a bearer token. Host and Origin headers are checked.
- Secrets are referenced by name, never printed or returned as plain text.
- Paths from any input must be validated before use (the product `PathValidator` rules: `..`, null bytes, control characters, roots outside `LabPaths.Root`, symlinks and junctions). L01 ships the fixed folder names only; the containment check lands with the first packet that accepts a path from a tool or from the network.
- Processes are started with `ProcessStartInfo.ArgumentList`.

## Secrets and audit

- `SecretVault` stores each secret as a `char[]` and returns a reference: `tok-N`, `key-N`, `pwd-N` or `brr-N` (a counter per kind). `Use` hands the caller a `ReadOnlySpan<char>` over the stored array and copies nothing out. `Remove` and `Dispose` zero the storage.
- `Redactor` replaces every vault value of 8 or more characters (longest first) with `***<ref>`. It also masks PEM private key blocks (`***pem`), `Bearer <token>` (`Bearer ***`) and `Password=` / `Pwd=` segments (`$1=***`). Each pattern has a 1 second timeout; on timeout the text becomes `***redaction-timeout***`.
- `AuditLog` writes one JSON line per event to `audit/audit-<yyyyMMdd>.jsonl` under the lab root, using the UTC day, and closes the previous day's file on roll-over. A `lab.call` entry records call id, tool, gate, caller, lease owner, outcome and duration. It never records tool arguments. Every value passes through `Redactor`.
- `ILabEvents.Emit` returns void, so a failed audit write is kept in `AuditLog.LastFailure`. `RecordCall` and `TryEmit` return the failure as a `Result`. The dispatcher refuses a call whose `lab.call.begin` cannot be written, deciding on the `TryEmit` result and never on `LastFailure`.
- Every refused attempt is audited as one `lab.call` with the refusal code as its outcome, including unknown names (recorded as `<unknown>`), unclassified or machine-gated tools, and a missing `lease_id`. A tool that throws is audited once with outcome `Lab.Internal`; its exception text is never returned or logged.

## Processes and logs

- `LabProcessSupervisor` starts child processes with `ProcessStartInfo.ArgumentList` and `UseShellExecute=false`, redirects stdout and stderr, and removes `MERIDIAN_LAB_TOKEN` from every child environment. Each child is assigned to a Windows Job Object with kill-on-close, so a daemon exit takes its children with it. Stop uses `Kill(entireProcessTree: true)`.
- Process names match `[A-Za-z0-9_-]` (max 64). Every output line goes through the raw-line observer, then `Redactor`, then `JsonLogLineParser`. Only redacted text reaches the 20 000-line ring and `logs/<name>.jsonl`. Non-JSON lines are kept with level `Raw`.
- `LogPathGuard` validates each log path with `PathValidator` against the logs folder and refuses any reparse point (symlink or junction) on the folder or the file.
- Watchdog (D-ML19): a child that exits unexpectedly is relaunched only when a lease is held, its target is not faulted (`c:<name>` or `proc:<name>`), and fewer than 3 relaunches happened in the last 5 minutes. Otherwise it stays `crashed` (or `exited` when `Relaunch=false`). Faulted and stopped exits are not counted as restarts.
- `ContainerLogFollower` follows a lab container with `docker --context <Lab:Docker:LinuxContext|desktop-linux> logs -f --since <ISO> <container>`, only for names starting `meridian-lab-`, and never relaunches.
- `logs_tail` reads the ring: `process` (required), `since` (ISO or `-<n>m` / `-<n>s`), `contains`, `level`, `category`, `max_lines` (default 200, cap 2000). `daemon` returns an empty list; unknown names return `[Lab.NotFound]`.

## Tool tiers

Every tool has one gate in `Policy/ToolPolicy`. The dispatcher reads the gate before it runs anything.

| Tool | Gate | Lease needed |
| --- | --- | --- |
| `lab_status` | Open | no |
| `lab_doctor` | Open | no |
| `lab_lease_acquire` | Open | no |
| `lab_lease_renew` | Open | no |
| `lab_lease_release` | Open | no |
| `lab_lease_status` | Open | no |
| `logs_tail` | Open | no |
| `stack_status` | Open | no |
| `db_query` | Open | no |
| `certstore_list` | Open | no |
| `registry_read` | Open | no |
| `state_file_read` | Open | no |
| `uat_list_specs` | Open | no |
| `uat_plan` | Open | no |
| `uat_report` | Open | no |
| `bundle_read` | Open | no |
| `fault_list_targets` | Open | no |
| `faults_active` | Open | no |
| `invariants_check` | Open | no |
| `results_query` | Open | no |
| `triage_show` | Open | no |
| `job_status` | Open | no |
| `pool_list` | Open | no |
| `ui_flow_list` | Open | no |
| `events_read` | Open | no |
| `coverage_report` | Open | no |
| `lab_build` | Leased | yes, `lease_id` required |
| `stack_up` | Leased | yes, `lease_id` required |
| `stack_down` | Leased | yes, `lease_id` required |
| `stack_reset_data` | Leased | yes, `lease_id` required |
| `svc_start` | Leased | yes, `lease_id` required |
| `svc_stop` | Leased | yes, `lease_id` required |
| `svc_restart` | Leased | yes, `lease_id` required |
| `svc_config_set` | Leased | yes, `lease_id` required |
| `http_request` | Leased | yes, `lease_id` required |
| `nodes_token_mint` | Leased | yes, `lease_id` required |
| `nodes_enroll_probe` | Leased | yes, `lease_id` required |
| `nodes_cert_time_shift` | Leased | yes, `lease_id` required |
| `nodes_decommission` | Leased | yes, `lease_id` required |
| `fault_toxic_add` | Leased | yes, `lease_id` required |
| `fault_proxy_disable` | Leased | yes, `lease_id` required |
| `fault_partition` | Leased | yes, `lease_id` required |
| `fault_heal` | Leased | yes, `lease_id` required |
| `fault_reset` | Leased | yes, `lease_id` required |
| `fault_kill` | Leased | yes, `lease_id` required |
| `fault_pause` | Leased | yes, `lease_id` required |
| `fault_disk_fill` | Leased | yes, `lease_id` required |
| `fault_corrupt_file` | Leased | yes, `lease_id` required |
| `fault_corrupt_registry` | Leased | yes, `lease_id` required |
| `fault_db` | Leased | yes, `lease_id` required |
| `results_known_issue_set` | Leased | yes, `lease_id` required |
| `triage_draft_issue` | Leased | yes, `lease_id` required |
| `triage_mark` | Leased | yes, `lease_id` required |
| `job_cancel` | Leased | yes, `lease_id` required |
| `pool_acquire` | Leased | yes, `lease_id` required |
| `pool_release` | Leased | yes, `lease_id` required |
| `member_reset` | Leased | yes, `lease_id` required |
| `agent_install` | Leased | yes, `lease_id` required |
| `agent_uninstall` | Leased | yes, `lease_id` required |
| `agent_start` | Leased | yes, `lease_id` required |
| `agent_stop` | Leased | yes, `lease_id` required |
| `agent_kill` | Leased | yes, `lease_id` required |
| `agent_token_write` | Leased | yes, `lease_id` required |
| `agent_config_set` | Leased | yes, `lease_id` required |
| `agent_msi_install` | Leased | yes, `lease_id` required |
| `agent_msi_uninstall` | Leased | yes, `lease_id` required |
| `cli_run` | Leased | yes, `lease_id` required |
| `ui_flow_run` | Leased | yes, `lease_id` required |
| `uat_attest` | Leased | yes, `lease_id` required |
| `identity_user_token` | Leased | yes, `lease_id` required |
| `results_flake_check` | OwnLease | only when `lease_id` is passed; the tool acquires its own lease otherwise |
| `hunt_model_run` | OwnLease | only when `lease_id` is passed; the tool acquires its own lease otherwise |
| `fuzz_run` | OwnLease | only when `lease_id` is passed; the tool acquires its own lease otherwise |
| `chaos_run` | OwnLease | only when `lease_id` is passed; the tool acquires its own lease otherwise |
| `soak_run` | OwnLease | only when `lease_id` is passed; the tool acquires its own lease otherwise |
| `fleet_run` | OwnLease | only when `lease_id` is passed; the tool acquires its own lease otherwise |
| `uat_run` | OwnLease | only when `lease_id` is passed; the tool acquires its own lease otherwise |
| `history_run` | OwnLease | only when `lease_id` is passed; the tool acquires its own lease otherwise |
| `twin_run` | OwnLease | only when `lease_id` is passed; the tool acquires its own lease otherwise |
| `mutation_run` | OwnLease | only when `lease_id` is passed; the tool acquires its own lease otherwise |
| `envelope_run` | OwnLease | only when `lease_id` is passed; the tool acquires its own lease otherwise |
| `peer_run` | OwnLease | only when `lease_id` is passed; the tool acquires its own lease otherwise |
| `version_run` | OwnLease | only when `lease_id` is passed; the tool acquires its own lease otherwise |
| `guided_ab_run` | OwnLease | only when `lease_id` is passed; the tool acquires its own lease otherwise |

`Machine` is reserved. No tool may carry it; the dispatcher refuses any such tool with `[Lab.MachineReserved]`.

A Leased call's lease is watched for the whole call. If the lease expires, is released or is taken over while the tool runs, the tool's token is cancelled and the call returns `[Lab.LeaseLost]`.

## Probe and Windows reads

`src\Tools\Dhadgar.Lab.Probe` builds `meridian-lab-probe.exe`, which the lab runs inside a Windows member. Part of L12: `footprint --json` prints a `ProbeFootprint` (Contracts) to stdout and exits 0; on failure it prints one line and exits 2. Unknown modes exit 2.

The host reads are read-only and run on Windows only; elsewhere they return `[Lab.Unavailable]`:

- `certstore_list` lists metadata for `machine_my`, `machine_root`, `agent_ca` (missing store is empty) and `current_user_my`. No certificate, public key blob or private key leaves the reader.
- `registry_read` reports presence, kind and length of the agent registry values. It never returns a value. An access-denied read returns `[Lab.Unavailable]`.
- `state_file_read` returns the agent state file as JSON. The path comes from `%ProgramData%` and `AgentWindowsFootprint`, is validated by `PathValidator`, and is refused above 1 MiB. Properties named like `token`, `key` or `secret` are blanked, then the whole document goes through `Redactor`.
- `lab_doctor` reports OS, build number, dotnet and the Docker server OS. Ports, docker-users, token length and repo root are listed as `info` (not checked yet).

Every read takes no `member` in L12; a `member` argument returns `[Lab.Unavailable] members arrive in L32`.

## Adding a tool

1. Implement `LabTool<TArgs>` in `Tools\Lab\`, with a name, a description and the argument type. Register it in DI with the other `ILabTool` instances.
2. Add the tool's name and gate to the `ToolPolicy` gate table. A tool missing from the table is refused with `[Lab.Unclassified]`.
3. Add a `ToolPolicyTests` row and a dispatcher test in `LabToolDispatcherTests` that covers its gate.
