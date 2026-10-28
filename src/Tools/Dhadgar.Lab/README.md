# meridian-lab

Dev and lab only, never production. `meridian-lab` is a local tool that drives the Meridian product services and agents on a developer machine or a lab host so their behaviour can be tested end to end. Nothing in this folder is deployed, referenced by a product project, or shipped in a container image. `LabIsolationTests` enforces that.

## Layout

- `LabProgram.cs`: entry point (`meridian-lab`).
- `LabDefaults.cs`, `LabPorts.cs`, `LabIds.cs`, `LabPaths.cs`, `LabHosts.cs`: constants, ports, identifiers, path rules and the loopback host guard.
- `Daemon/`: the loopback daemon that exposes the lab over HTTP with a bearer token.
- `Policy/`: `LabGate`, the policy check every tool call passes through.
- `Tools/`: `LabCallContext`, the per-call context.
- `Audit/`: `LabEvent` and the event sink.
- `Secrets/`: `SecretVault`, which holds secrets by reference only.
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

## Rules

- The daemon binds to loopback only and requires a bearer token. Host and Origin headers are checked.
- Secrets are referenced by name, never printed or returned as plain text.
- Paths from any input must be validated before use (the product `PathValidator` rules: `..`, null bytes, control characters, roots outside `LabPaths.Root`, symlinks and junctions). L01 ships the fixed folder names only; the containment check lands with the first packet that accepts a path from a tool or from the network.
- Processes are started with `ProcessStartInfo.ArgumentList`.
