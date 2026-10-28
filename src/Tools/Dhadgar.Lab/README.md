# meridian-lab

Dev and lab only, never production. `meridian-lab` is a local tool that drives the Meridian product services and agents on a developer machine or a lab host so their behaviour can be tested end to end. Nothing in this folder is deployed, referenced by a product project, or shipped in a container image. `LabIsolationTests` enforces that.

## Layout

- `LabProgram.cs`: entry point (`meridian-lab`).
- `LabDefaults.cs`, `LabPorts.cs`, `LabIds.cs`, `LabPaths.cs`, `LabHosts.cs`: constants, ports, identifiers, path rules and the loopback host guard.
- `Daemon/`: the loopback daemon that exposes the lab over HTTP with a bearer token.
- `Policy/`: `LabGate`, the policy check every tool call passes through.
- `Tools/`: `LabCallContext`, the per-call context.
- `Audit/`: `LabEvent`, `ILabEvents` and `AuditLog`, the daily JSON-lines audit writer.
- `Secrets/`: `SecretVault` (secrets held by reference only) and `Redactor`.
- `Nodes/`: the adapter into the Nodes product (`INodesLabAdapter`, `NodesLabAdapter`, `EnrollProbe`).

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

## Rules

- The daemon binds to loopback only and requires a bearer token. Host and Origin headers are checked.
- Secrets are referenced by name, never printed or returned as plain text.
- Paths from any input must be validated before use (the product `PathValidator` rules: `..`, null bytes, control characters, roots outside `LabPaths.Root`, symlinks and junctions). L01 ships the fixed folder names only; the containment check lands with the first packet that accepts a path from a tool or from the network.
- Processes are started with `ProcessStartInfo.ArgumentList`.

## Secrets and audit

- `SecretVault` stores each secret as a `char[]` and returns a reference: `tok-N`, `key-N`, `pwd-N` or `brr-N` (a counter per kind). `Use` hands the caller a `ReadOnlySpan<char>` over the stored array and copies nothing out. `Remove` and `Dispose` zero the storage.
- `Redactor` replaces every vault value of 8 or more characters (longest first) with `***<ref>`. It also masks PEM private key blocks (`***pem`), `Bearer <token>` (`Bearer ***`) and `Password=` / `Pwd=` segments (`$1=***`). Each pattern has a 1 second timeout; on timeout the text becomes `***redaction-timeout***`.
- `AuditLog` writes one JSON line per event to `audit/audit-<yyyyMMdd>.jsonl` under the lab root, using the UTC day, and closes the previous day's file on roll-over. A `lab.call` entry records call id, tool, gate, caller, lease owner, outcome and duration. It never records tool arguments. Every value passes through `Redactor`.
- `ILabEvents.Emit` returns void, so a failed audit write is kept in `AuditLog.LastFailure`. `RecordCall` returns the failure as a `Result`.
