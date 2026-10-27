# P02 review, pass 1 (ded8627..167bad2, head 167bad2)

## Verdict

ship

## Fix notes

None.

Checked:
- The four seam tests fail for the stated reason. I re-ran them: the real-agent test and the node-B test fail with `HttpRequestException ... 404 (Not Found)` at negotiate. The two raw negotiate tests fail with `Expected: Unauthorized, Actual: NotFound`. `AgentEnrollmentContractTests` passes, 2 of 2.
- The Release build of `Dhadgar.Nodes.Tests` with `TreatWarningsAsErrors=true` succeeds, and so does the Agent.Core build it depends on.
- `ControlPlaneHubTransport` matches C7. It is reachable only through the `ControlPlaneClient` constructor and `TryAddSingleton`, never from configuration. `ConnectAsync` still enforces HTTPS.
- Nothing else constructs `ControlPlaneClient`, so the new optional last parameter breaks no caller.
- The seam tests are sound for the later green run:
  - `ForeignCa` issues a cert with the real CA's CN, a correct SPIFFE SAN and the client-auth EKU. Its validity window sits inside the shared `FakeTimeProvider` window, which starts at `UtcNow`. Once P03 lands, test 3 therefore reaches the signature check (`invalid_issuer`) and does not fail earlier with `certificate_not_yet_valid`.
  - Test 4 fails if the hub trusts the payload `NodeId`, because A must be updated and B must stay untouched.
  - Connect and send go through two private helpers, as the spec asks.
- Deviations the coder recorded are acceptable. Time is advanced on `_baseFactory.TimeProvider`, which the derived factory shares. Node B is seeded through the derived factory's `DbContext`, because `SeedNodeAsync` is not available on `WebApplicationFactory<Program>`. `EnrollAgentAsync` returns the unwrapped `EnrollmentResult`.

## Not fixed

- security-review label needed: `ControlPlaneClient` and the Agent.Core project are in the diff.
- Observation, not a defect: test 1 asserts only CPU in `NodeHealth`. The memory and disk values (40% and 70%) are set in the payload but not asserted. The spec lists CPU only. Consider asserting them in P05 or P06, when the hub mapping is written.
