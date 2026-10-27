# Windows agent UAT

The Windows agent UAT proves the agent on a real Windows machine: it enrolls, starts, reports
heartbeats and cleans up after itself. The driver is `tests/Dhadgar.Agent.Windows.Uat`. The entry
point is `scripts/uat/Invoke-AgentWindowsUat.ps1`.

This README covers `-Preflight`, `-StackOnly` (P19), `-CleanupOnly`, the guards of `-Mode Process`,
and the process-mode agent legs (P20: U0-U4 and U9). The `Service` and `Msi` modes are added by later packets.

## Who runs what (D-S225, D-S239)

| Call | Who | Allowed when |
| --- | --- | --- |
| `-Preflight` | anyone | always; it also reports `agentRunTree` |
| `-StackOnly` | anyone | as today (Docker up, port 5672 free, dev cert trusted, no `meridian-uat-*` container, no run folder); also writes `launch.json` |
| `-Mode Process\|Service\|Msi -AcceptMachineChanges` | user | as today |
| `-Mode Process\|Service\|Msi -AcceptMachineChanges` | agent (D-S239) | elevated, switch given, the tree clean, no `-NoBuild`, no `launch.json` or manifest in the run folder, no footprint, preflight passes |
| `-CleanupOnly <runDir>` | user | as today (any run folder under `.uat\agent-windows`) |
| `-CleanupOnly <runDir>` | agent (D-S239) | the folder's `launch.json` loads, its `runId` equals the folder name, and its `launchedBy` is `agent`; the non-elevated machine-items refusal still applies |

- `CLAUDECODE` set in the environment makes a run an agent run. It is a label, not authentication.
- The machine-changing modes change this machine: services, HKLM, machine certificate stores and the installed
  MSI. They need an elevated PowerShell 7.4+ shell and `-AcceptMachineChanges`.
- The UAT needs RabbitMQ on 127.0.0.1:5672 and a Docker engine for the local stack. Preflight
  reports both.

## Threat model (D-S237, D-S238, D-S239)

The driver trusts the account that runs it. Anything running as that user can change the repo, the build
or the driver that the elevated script builds and runs, so the driver does not try to defend against it.
An agent run is trusted as much as the account that runs it: `CLAUDECODE` is a label that selects the agent
rules and the record, not an authentication. The tree check only vouches for an honest agent: for an honest agent
it checks that tracked and untracked sources match HEAD at the start of the run (`docs` and `.claude` excluded).
It does not detect ignored files, `skip-worktree` or `.git/info/exclude` entries, or planted build output, so
`gitSha` and `agentRun.treeClean` attest only the git index of an honest agent (R7).

Its guards stop driver bugs, a stale or foreign manifest, and PID reuse from touching anything the run
did not create. Cleanup replays only these items: the named service, the agent registry key and its
`EnrollmentToken` value, and the `SOFTWARE\Meridian Console` parent key only when the run created it and it is empty, agent and UAT certificates in `LocalMachine` `Root`, `My` and
`MeridianConsoleAgent`, the `MeridianConsoleAgent` store, the agent event source, `meridian-uat-*`
containers and CNG keys, and the agent's own key, deleted through its certificate in `My` (D-S238), files and folders under the run folder (plus the machine agent folder), and
processes whose image is under the run folder or is the Nodes or Gateway build the stack starts.

A process cleanup cannot read is left running and reported, never killed.

## The local stack and `-StackOnly` (P19)

`-StackOnly` proves the control plane end to end without touching the machine outside Docker and the run
folder. It needs no elevation and runs under `CLAUDECODE` and writes `launch.json`. It refuses (exit 2) when Docker is down, port
5672 is busy, the dev HTTPS certificate is not trusted (`dotnet dev-certs https --trust`), a
`meridian-uat-*` container already exists, or its run folder already exists (the driver creates the folder
itself, so a planted or stale manifest never reaches cleanup).

| Part | How it runs | Port |
| --- | --- | --- |
| PostgreSQL 16 | Docker container `meridian-uat-pg-<runId>`, database `dhadgar_nodes_uat` | free loopback port |
| RabbitMQ 3 | Docker container `meridian-uat-mq-<runId>` | **5672** (fixed) |
| Nodes | child process from `src\Dhadgar.Nodes\bin\Release\net10.0`, plain HTTP on 127.0.0.1 | free loopback port |
| Gateway | child process from `src\Dhadgar.Gateway\bin\Release\net10.0`, HTTPS on `localhost` | free loopback port |

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

## Process mode: the agent legs (P20)

`-Mode Process -AcceptMachineChanges` runs the stack, then the real published agent as a child process
of the driver. It is machine-changing: it writes the enrollment token to HKLM, creates the agent's
machine certificate in `LocalMachine\My` and the `MeridianConsoleAgent` store, and an event source.
Run it yourself from an elevated PowerShell 7.4+ shell. Automated coders never run it.

| Step | What it checks | Pass condition |
| --- | --- | --- |
| U0 | Preflight for run (elevation, footprint, Docker, ports, certificate trust). | Every check required for run passes. |
| U1 | The stack (containers, Nodes, Gateway) and the enrollment smoke (S-mint to S-db). | The smoke passes; see the stack section. |
| U2 | Token: mints token T1 for a new organization and writes it to `HKLM\SOFTWARE\Meridian Console\Agent`. | The registry value exists. |
| U3 | First start: applies the agent config and starts the agent. Polls every 2 s for up to 90 s. | All of these hold: `agent-state.json` has a node ID; exactly one node for the organization, `Online` or `Degraded`, with a heartbeat; T1 is used by that node; the registry value is gone; exactly one agent certificate in `My`, with a machine CNG key that can sign, SAN equal to the node's SPIFFE ID; exactly one unrevoked certificate row with the store certificate's SHA-256 thumbprint; the CA is in `MeridianConsoleAgent` and the client certificate chains to it; no agent CA in `LocalMachine\Root`; the state file's ACL is protected and every Allow ACE is SYSTEM, Administrators or the agent's account (A1). |
| U4 | Restart (#101): mints T2, writes it, stops the agent, starts it again. Polls for up to 60 s. | The heartbeat is later than before the restart; still one node; T2 is not used; the certificate row count is unchanged; the node ID in the state file is the same; the same thumbprint is in `My`. A second token is not consumed. |
| U5 | Lost response (D-S212): the driver renews C1 with its own key, C2 is created, and the response is treated as lost. | C1's row is revoked with a `GraceUntil` about 5 minutes after the renewal (within 60 s); C2's row is active; the Gateway answers 200 for C1 and C2. |
| U6 | Renew from grace (D-S221): sets `CertificateRenewalThresholdDays` to 90 days, starts the agent, and polls for up to 90 s. The key is removed again after U6, pass or fail. | Exactly one agent certificate is in `My` (C3, a machine CNG key that can sign); C1's CNG key is gone (P14 A2); the node heartbeat is later than the baseline; C3's row is active; C2's row is hard-revoked (no grace, reason "Superseded by a later renewal"); C1's `GraceUntil` is unchanged from U5. |
| U7 | Grace over the wire: the Gateway answers 401 for C2 and 200 for C3. The driver waits until C1's grace has passed (plus 10 s), then sends C1 to Nodes directly over loopback. | C2 gets 401 through the Gateway; C3 gets 200; C1 gets 401 from Nodes after its grace. A transport error (no HTTP answer) is recorded as `inconclusive` for that probe, not as a pass. |
| U8 | Restart after renewal: the agent starts again and polls for up to 60 s. | The heartbeat is later than before the restart; the certificate row count is unchanged; only C3 is in `My`. |
| U9 | Cleanup and scan, always last. | See below. |

The agent is stopped at the end of each leg that starts it. U5-U8 run after U4 with the same agent process,
and only when U1-U4 pass.

**Renewal grace and timing.** The Nodes grace is 5 minutes (`Mtls__RenewalGraceMinutes=5` in the stack's
ServiceHost). U5 checks that C1's grace is about 5 minutes away, so a different grace setting fails U5 rather
than silently changing what U7 waits for. U7 checks C1 at Nodes, not through the Gateway, because C1's key
is gone after U6: the Gateway path already proved C1 in U5. The loopback request carries C1 as
`X-Client-Cert`.

**Threshold in days.** `CertificateRenewalThresholdDays` is in whole days, so U6 sets 90, which renews at
start for a certificate issued moments earlier. It is removed after U6 (the published `appsettings.json` is
left as it was built).

**Inconclusive.** `inconclusive` means no HTTP answer reached the probe (connection refused, reset, or TLS
failure). The step still fails the run, but the evidence says the rule under test was not checked, so a
network fault is not read as a renewal bug.

**U9** runs in every `run`, after the manifest undo. It passes when the undo had no failures, the machine
probe finds nothing, and the `LocalMachine\Root` diff has no additions or removals. Then the secret scan
runs over `logs\` (including `logs\eventlog.txt`, the agent's Application events exported before the event
source is deleted), `results.md`, `results.json` and `cleanup-manifest.jsonl`. An enrollment token found
there fails U9, with the file and line number and never the text, and the file is rewritten redacted. A
password found is a warning only, and the file is rewritten too. Last, U9 deletes the run's `ca`,
`agent-state` and `agent-data` folders. `agent` and `logs` stay in the run folder.

## Reading `results.md`

`results.md` has one header block (verb, mode, overall result and exit code, cleanup status, git
revision, Windows build, whether the run was elevated) and one table row per step: step ID, name,
status, evidence and duration. The evidence is `key=value` pairs. The first `fail` names the step and
its condition, and steps after it show `skipped`. `results.json` holds the same data for tools.
`warnings` lists password hits from U9, the expected service-recovery warning, and any export problem.

## Parameters

| Call | What it does |
| --- | --- |
| `Invoke-AgentWindowsUat.ps1` or `-Preflight` | Checks the machine and writes `U0`. Changes nothing. |
| `-StackOnly` | Starts the local stack and runs the enrollment smoke (P19). Needs no elevation. |
| `-Mode Process -AcceptMachineChanges` | Runs the stack and the agent legs U2-U4 as a child process (elevated, machine-changing). |
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
| 2 | Refused before any machine change: not elevated, switch missing, agent run on a dirty tree or with `-NoBuild`, run folder already used, agent cleanup of a non-agent run folder, footprint found, port 5672 busy, Docker down |
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
