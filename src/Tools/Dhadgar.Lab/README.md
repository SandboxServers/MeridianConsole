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

`meridian-lab daemon run` starts the loopback daemon. The token comes from `MERIDIAN_LAB_TOKEN` (at least 32 bytes, base64url); `meridian-lab token new` prints a fresh one. The daemon listens on `127.0.0.1` and `[::1]` only; there is no supported way to bind elsewhere, so `Lab:BindAddress` set to anything but loopback is a refusal.

Every request passes one guard before routing, in this order:

1. `Host` must be `127.0.0.1:<port>`, `localhost:<port>` or `[::1]:<port>` (the bound port) → **403**, empty body.
2. Any `Origin` header present → **403**, empty body.
3. Missing, malformed or wrong `Authorization: Bearer <token>` → **401**, empty body, `WWW-Authenticate: Bearer`. The same response for every cause.

Refusals are logged as `lab.edge.refused {reason}` with no values. The token and the `Authorization` header are never logged.

Routes: `GET /status` returns the `StatusDto` (idle until later packets contribute); `/mcp` is reserved for L05; anything else is **404** once the guard passes.

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
