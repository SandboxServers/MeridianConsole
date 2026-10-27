# Windows agent UAT

The Windows agent UAT proves the agent on a real Windows machine: it enrolls, starts, reports
heartbeats and cleans up after itself. The driver is `tests/Dhadgar.Agent.Windows.Uat`. The entry
point is `scripts/uat/Invoke-AgentWindowsUat.ps1`.

This README covers the skeleton delivered by P18: `-Preflight`, `-StackOnly` (P19),
`-CleanupOnly`, and the guards of `-Mode Process`. Scenario steps and the `Service` and `Msi` modes
are added by later packets.

## Who runs what (D-S225)

- Agents and automated coders may run **only** `-Preflight` and `-StackOnly`.
- Nobody but a person runs a machine-changing mode. `-Mode Process`, `-Mode Service` and
  `-Mode Msi` change this machine: services, HKLM, machine certificate stores and the
  installed MSI. Run them yourself from an elevated PowerShell 7.4+ shell, passing
  `-AcceptMachineChanges`.
- The driver refuses `run` (exit 2) when `CLAUDECODE` is set in the environment. It also
  refuses `cleanup` (`-CleanupOnly`) then, so `-CleanupOnly` is a person-only call. Do not
  clear it to get around that.
- The UAT needs RabbitMQ on 127.0.0.1:5672 and a Docker engine for the local stack. Preflight
  reports both.

## Threat model (D-S237)

The driver trusts the account that runs it. Anything running as that user can change the repo, the build
or the driver that the elevated script builds and runs, so the driver does not try to defend against it.

Its guards stop driver bugs, a stale or foreign manifest, and PID reuse from touching anything the run
did not create. Cleanup replays only these items: the named service, the agent registry key and its
`EnrollmentToken` value, agent and UAT certificates in `LocalMachine` `Root`, `My` and
`MeridianConsoleAgent`, the `MeridianConsoleAgent` store, the agent event source, `meridian-uat-*`
containers and CNG keys, files and folders under the run folder (plus the machine agent folder), and
processes whose image is under the run folder or is the Nodes or Gateway build the stack starts.

A process cleanup cannot read is left running and reported, never killed.

## The local stack and `-StackOnly` (P19)

`-StackOnly` proves the control plane end to end without touching the machine outside Docker and the run
folder. It needs no elevation and runs under `CLAUDECODE`. It refuses (exit 2) when Docker is down, port
5672 is busy, the dev HTTPS certificate is not trusted (`dotnet dev-certs https --trust`), or a
`meridian-uat-*` container already exists.

| Part | How it runs | Port |
| --- | --- | --- |
| PostgreSQL 16 | Docker container `meridian-uat-pg-<runId>`, database `dhadgar_nodes_uat` | free loopback port |
| RabbitMQ 3 | Docker container `meridian-uat-mq-<runId>` | **5672** (fixed) |
| Nodes | child process from `src\Dhadgar.Nodesin\Release
et10.0`, plain HTTP on 127.0.0.1 | free loopback port |
| Gateway | child process from `src\Dhadgar.Gatewayin\Release
et10.0`, HTTPS on `localhost` | free loopback port |

RabbitMQ must be on 5672 because `RabbitMqOptions` has no port setting. Stop a dev-compose RabbitMQ first.
Containers bind to 127.0.0.1 only and carry the label `meridian-uat=<runId>`. Passwords are 24 random
bytes as hex, reach Docker and the services through the environment (never the command line) and are
redacted from every log and result.

U1 starts the containers, Nodes and the Gateway and records ports, image IDs and process IDs. The smoke
then runs these sub-steps, recorded as evidence of U1:

- **S-mint:** mints a single-use token through the real `EnrollmentTokenService`.
- **S-enroll:** `POST /api/v1/agents/enroll` through the Gateway (expects 201); checks the certificate's
  SPIFFE SAN and public key.
- **S-negotiate:** `POST /hubs/agent/negotiate`: 200 with the certificate, 401 without.
- **S-hub-ws:** a SignalR connection over WebSockets only, with the certificate, then a heartbeat that
  must be acknowledged. This is the first run of the WebSocket upgrade through YARP.
- **S-db:** the node row, the token's `UsedAt` and `UsedByNodeId`, and the node's `LastHeartbeat`.

Cleanup removes the containers and processes. A passing run exits 0 with `cleanup.status = clean`, and
`docker ps -a --filter label=meridian-uat` prints nothing. Build Nodes and the Gateway in Release first
(the script does this unless `-NoBuild`).

## Parameters

| Call | What it does |
| --- | --- |
| `Invoke-AgentWindowsUat.ps1` or `-Preflight` | Checks the machine and writes `U0`. Changes nothing. |
| `-StackOnly` | Starts the local stack and runs the enrollment smoke (P19). Needs no elevation. |
| `-Mode Process -AcceptMachineChanges` | Runs the scenario as a child process (elevated, machine-changing). |
| `-Mode Service -AcceptMachineChanges` | Runs the scenario as a Windows service (P22, not built yet). |
| `-Mode Msi -AcceptMachineChanges` | Installs the MSI (P23, not built yet). The script refuses. |
| `-CleanupOnly <runFolder>` | Undoes the changes recorded in that run's manifest. Use it after a crash. |
| `-NoBuild` | Skips `dotnet build` and `dotnet publish`. The driver exe must already exist. |

Each run gets a folder `<repo>\.uat\agent-windows\<runId>`, where `runId` is the UTC time
`yyyyMMdd-HHmmss` plus 4 random hex characters. The script prints the folder path first. The
folder is ignored by git (`.uat/`).

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Every step `pass` and cleanup `clean` |
| 1 | A step `fail` or `inconclusive` (cleanup `clean`) |
| 2 | Refused before any machine change: not elevated, switch missing, `CLAUDECODE` set, footprint found, port 5672 busy, Docker down |
| 3 | Footprint left after cleanup (overrides 1) |
| 4 | Internal driver error (cleanup still ran) |

## Results

Each run writes `<runFolder>\results.json` (schema version 1) and `results.md`, which has the same
data as a header and a table of steps. `cleanup.status` is `clean`, `footprint-left` or `not-run`.
A run is a pass only when `overall` is `pass`, which needs every non-skipped step to pass and
cleanup to be `clean`.

Machine changes are recorded in `<runFolder>\cleanup-manifest.jsonl` before each change is made.
The driver undoes them in reverse order at the end of every run, including after Ctrl+C or an
internal error.

## After a crash: `-CleanupOnly`

If a run was killed, or the machine was left changed, undo it with the same run folder:

```powershell
pwsh -NoProfile -File scripts\uat\Invoke-AgentWindowsUat.ps1 -CleanupOnly .uat\agent-windows\<runId>
```

Cleanup reads the manifest, stops and removes what the run created, and writes
`cleanup-results.json`. It exits 0 when the footprint is clean and 3 when something is left. Entries
that were already present before the run (`existedBefore`) are never deleted. Running cleanup twice is
safe.

If the manifest is missing, cleanup treats it as empty, undoes nothing, and reports
`footprint-left` if the machine probe still finds UAT items. If the manifest is unreadable
(corrupt in the middle, not just a truncated last line), cleanup reports `footprint-left` and does
not guess. Remove the leftovers by hand, after checking them against the probe output in
`cleanup-results.json`.
