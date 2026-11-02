# ml-l36 worknote

Done: part 1 (b45ed76c), part 2 (7f2d4b95), part 3 tests (538c2ed4), part 4 step 5 (I2 notifications target + AllTargets, InvariantsArea uses AllTargets, LabBuilder probe dll name fix, probe.Dockerfile, README Services).
stack_reset_data and db_query already allow notifications and discord (no change).
Done (continuation 3): report written; filtered run found 1 failure (GatewayRouteTableTests). Fixed: test parses appsettings with JsonCommentHandling.Skip; GatewayRouteTable now template-based (Gateway order) and test samples each template.
Left: nothing. Committed as final fix commit (see report).
Last check: filtered run `RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true`: Passed! - Failed: 0, Passed: 1501, Skipped: 5, Total: 1506 (exit 0). Lab.Tests build Release TreatWarningsAsErrors: Build succeeded, 0 errors.

## Ruling 1 (l36-ruling-1)

Superseded: the untracked worknotes/L36.md is stale and is not committed.
Done: E0 (GatewayRouteTable reverted to 8191eb8f, commit a3e5d8ab), E1-E11, T1-T6 (commit 2).
Checks: C1-C3 build succeeded (0 warnings, 0 errors). C4: partial; narrow runs 88 and 443 passed, only the allowed GatewayRouteTable failure (KG-W5-22); Daemon/Faults/Hunt/Soak namespaces too slow to finish here. C5 not run (awaits user yes). C6 clean. See report.NotifyDockerTests (C5): authored/kept; not run (the user's yes is pending).
