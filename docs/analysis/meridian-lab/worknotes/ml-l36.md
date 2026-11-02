# ml-l36 worknote

Done: part 1 (b45ed76c), part 2 (7f2d4b95), part 3 tests (538c2ed4), part 4 step 5 (I2 notifications target + AllTargets, InvariantsArea uses AllTargets, LabBuilder probe dll name fix, probe.Dockerfile, README Services).
stack_reset_data and db_query already allow notifications and discord (no change).
Done (continuation 3): report written; filtered run found 1 failure (GatewayRouteTableTests). Fixed: test parses appsettings with JsonCommentHandling.Skip; GatewayRouteTable now template-based (Gateway order) and test samples each template.
Left: nothing. Committed as final fix commit (see report).
Last check: filtered run `RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true`: Passed! - Failed: 0, Passed: 1501, Skipped: 5, Total: 1506 (exit 0). Lab.Tests build Release TreatWarningsAsErrors: Build succeeded, 0 errors.
