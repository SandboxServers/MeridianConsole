# P06 security review, pass 2

Range ded8627..356eeb1 (pass 2 delta 7335f07..356eeb1). Reviewed head 356eeb1.

## Verdict

ship

Both pass 1 notes are fixed, and I found no regression in the rate limiting or in the certificate forwarding. The `PerAgent` bucket is now chosen by the connection IP, which the client cannot vary per request. The dev Nodes port is bound to loopback.

## Fix notes

None.

Pass 1 notes, each checked against the code:

- N1 (`PerAgent` key picked by a forged certificate): fixed. `Program.cs:247` keys on `agent-ip:{RateLimitPartitionKeys.ClientIp(RemoteIpAddress)}`. Neither the certificate nor `sub` is read. The `System.Security.Cryptography` using is gone. The partition count is bounded by the number of source IPs (IPv6 reduced to /64, link-local and null get fixed buckets).
  - Real effect, not just config. The ruling-2 defect is also fixed: `MapReverseProxy().ConfigureEndpoints` (`Program.cs:388-396`) attaches `Global` only where `RouteRateLimiting.UsesGlobal(route.RateLimiterPolicy)` is true. Only `agents-route` and `agent-hub-route` carry `PerAgent` (`appsettings.json:183,241`). `agents-enroll-route` stays on `Auth` and therefore `Global`, which is unchanged from `main`.
  - Tests pin it: `PerAgent_ForgedIssuerCertificatePerRequest_IsRateLimitedByIp` (4 forged certificates from one IP: 200, 200, 200, 429, with 3 captured), `AgentRoute_NotLimitedByGlobal` and `NonAgentRoute_StillLimitedByGlobal_NotByRoutePolicy`. I did not run them; the coder's revert checks are in the packet report.
- N2 (dev compose trust range): fixed. `docker-compose.services.yml` now publishes `"127.0.0.1:5040:8080"`, and the comment names the residual risk (any container on the bridge, and any local host process, is inside `172.16.0.0/12`). No other file sets `Mtls__ForwardedCertificate__*`; I searched the whole worktree for yml, yaml, json, env, sh, ps1, bicep and tf. The ACR compose file does not enable forwarding.
- Test note (spoofed header together with a real certificate): added as `HubRoute_SpoofedHeaderWithRealCertificate_ForwardsRealCertificate`. It asserts a single header value equal to the real certificate.

Regression checks:

- `RemoteIpAddress` is set by `UseForwardedHeaders` (first in the pipeline). Production trusts only Cloudflare ranges. Development trusts only loopback proxies. A client-supplied `X-Forwarded-For` therefore cannot choose its own bucket. If the Gateway is reached through a proxy that is not trusted, all clients share the proxy's bucket. That fails closed and is not a security problem.
- Header stripping and the policies are not touched by this delta. The pass 1 "checked and clean" items still hold. Authorization runs before the rate limiter, so a request with no certificate gets 401 without being proxied or counted. That is cheap and harmless.
- No certificate validation callback was added, and nothing secret is logged or committed in the delta.

## Not fixed

- Advisory: agent routes are no longer in the shared `Global` bucket (the intended ruling-2 option 1). The only limit on agent traffic to Nodes is therefore 500 per 60 s per source IP (or per /64). An attacker with many source addresses can push more than 1000 per minute in total to Nodes, which `Global` used to cap. Nodes validates every request, so this is a load concern and not an auth concern. It belongs with D-S220 (aggregate or global limiting, and the single `"global"` partition being a gateway-wide DoS). Add it to the P15 follow-up issue. No change in this packet.
- Advisory (unchanged from pass 1): the `AgentCertificate` policy checks only the issuer CN, and Nodes does the real validation (D-S25). TLS terminated upstream of the Gateway leaves `Connection.ClientCertificate` empty, so agent routes return 401 (D-S210). The dev trust range `172.16.0.0/12` still covers every container on the bridge and any local host process, as the compose comment says. The production `TrustedProxies` must be the Gateway pod address or CIDR, never a broad pool (P15, D-S210).
- Advisory: when the `Auth` and `PerTenant` limiters are eventually enforced under D-S220, `PerTenant` falls back to the raw `RemoteIpAddress.ToString()` and does not use `RateLimitPartitionKeys.ClientIp`. That is IPv6 rotation exposure for unauthenticated requests. Dormant today, so no change now.

security-review label needed
