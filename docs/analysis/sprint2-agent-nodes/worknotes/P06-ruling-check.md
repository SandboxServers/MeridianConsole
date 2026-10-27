# P06 ruling check (range ded8627..356eeb1, reviewed head 356eeb1)

## Verdict

fix

## Fix notes

N1. src/Dhadgar.Gateway/README.md:541 — the Global Policy bullet ends "so on those routes `Global` is the only limiter that runs". "Those routes" are the `PerAgent` routes, where `Global` does not run, so the sentence says the opposite of what the code does. P06-ruling-2 step 5 dictated this wording, so the coder applied it as written, but it is wrong. — In the line `- Attached to every proxied route except those whose \`RateLimiterPolicy\` is \`PerAgent\` (\`RouteRateLimiting.UsesGlobal\`). ASP.NET Core enforces only the last rate-limit policy on an endpoint, so on those routes \`Global\` is the only limiter that runs.`, replace the final clause `so on those routes \`Global\` is the only limiter that runs.` with `so on every other route \`Global\` is the only limiter that runs, and on \`PerAgent\` routes \`PerAgent\` is.` — verify: Grep README.md for `on those routes` and expect no match; Read lines 540-545.

## Not fixed

- Ruling-1 and ruling-2 steps are all applied. Checked in the diff 7335f07..356eeb1:
  - `RateLimitPartitionKeys.ClientIp` is the moved Auth logic with the link-local branch. `Auth` and `PerAgent` both call it, and `PerAgent` is keyed `agent-ip:{ip}`. The unused `using`s were removed from Program.cs.
  - `RouteRateLimiting` and the `ConfigureEndpoints` convention match the ruling. The `Global` policy name uses the constant.
  - The compose Nodes port is `127.0.0.1:5040:8080` with the ruled comment. The `gateway` service and the ACR compose file are unchanged.
  - Tests added and matching the ruling: `PerAgent_ForgedIssuerCertificatePerRequest_IsRateLimitedByIp`, `NonAgentRoute_StillLimitedByGlobal_NotByRoutePolicy`, `AgentRoute_NotLimitedByGlobal`, `HubRoute_SpoofedHeaderWithRealCertificate_ForwardsRealCertificate` (asserts `Assert.Single` on the header values), the `UsesGlobal` theory, and `LinkLocalIpv6_ReturnsLinkLocalBucket_ForRateLimitPartition`. `RateLimitingBehaviorTests` now calls the real `ClientIp`, so the `fe80::1` theory row changed from `fe80::` to `unknown-linklocal`. That follows from calling the real code and is correct.
  - README: the `AgentCertificate` policy row, the PerAgent bullet at :456, and the Auth, PerTenant and PerAgent bullets in the policy section are all updated. Only the Global bullet has a wording error (N1).
- Review N1, review NF2 and security N1 (the PerAgent bypass and unbounded partitions), security N2, and the security test note are all resolved. Nothing the ruling accepted (NF1, NF3, NF4, the advisories) is raised again.
- I re-ran the Release build with `TreatWarningsAsErrors=true`: 0 warnings, 0 errors. I re-ran the Gateway tests: 144 passed. I did not re-run the implementer's three revert checks. The tests assert the exact 200/429 sequences and capture counts, and the implementer's report records each of them failing.
- Minor: the ruling asked the coder to report whether the `builder.UseSetting` fallback was needed. `CreateApp` in AgentCertificateForwardingTests.cs now applies `extraSettings` through both `UseSetting` and the in-memory collection. The report does not say whether the in-memory path alone worked. The duplication is harmless and needs no change.
- security-review label needed: Gateway auth path, rate limiting and client-certificate forwarding (the packet is marked security review: yes, D-S215).
- Owner item, not blocking P06: D-S220 (the dormant `Auth` and `PerTenant` limiters, and the single `"global"` partition) goes to P15 and the closing report, as ruling 2 says.
