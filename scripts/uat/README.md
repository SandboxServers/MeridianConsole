# Windows agent UAT

The Windows agent UAT proves the agent on a real Windows machine: it enrolls, starts, reports
heartbeats and cleans up after itself. The driver is `tests/Dhadgar.Agent.Windows.Uat`. The entry
point is `scripts/uat/Invoke-AgentWindowsUat.ps1`.

This README covers the skeleton delivered by P18: `-Preflight`, `-StackOnly` (not built yet, P19),
`-CleanupOnly`, and the guards of `-Mode Process`. Scenario steps and the `Service` and `Msi` modes
are added by later packets.

## Who runs what (D-S225)

- Agents and automated coders may run **only** `-Preflight` and `-StackOnly`.
- Nobody but a person runs a machine-changing mode. `-Mode Process`, `-Mode Service` and
  `-Mode Msi` change this machine: services, HKLM, machine certificate stores and the
  installed MSI. Run them yourself from an elevated PowerShell 7.4+ shell, passing
  `-AcceptMachineChanges`.
- The driver refuses `run` (exit 2) when `CLAUDECODE` is set in the environment. Do not
  clear it to get around that.
- The UAT needs RabbitMQ on 127.0.0.1:5672 and a Docker engine for the local stack. Preflight
  reports both.

## Parameters

| Call | What it does |
| --- | --- |
| `Invoke-AgentWindowsUat.ps1` or `-Preflight` | Checks the machine and writes `U0`. Changes nothing. |
| `-StackOnly` | Starts the local stack (P19). Not built yet: the driver exits 2. |
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
