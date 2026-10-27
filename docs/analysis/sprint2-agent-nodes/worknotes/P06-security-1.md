# P06 security review, pass 1

Range ded8627..7335f07. Reviewed head 7335f07.

## Verdict

fix

Header stripping is correct and covers every route, and the policies fail closed (NoCertificate default leads to 401). Two problems need a change: the cert-keyed rate limiter can be bypassed with forged certificates, and the dev compose trust range lets anyone who can reach the published Nodes port spoof the forwarded certificate.

## Fix notes

N1. src/Dhadgar.Gateway/Program.cs:272-274 — The `PerAgent` partition key is `agent-cert:{SHA-256 of whatever certificate the client presented}`. Kestrel runs `AllowAnyClientCertificate()` and the `AgentCertificate` policy only compares the issuer CN string (`AgentCertificateForwarding.IsAgentIssued`), which anyone can forge. Concrete input: an attacker generates a self-signed CA with CN "Meridian Agent CA" and mints a fresh leaf per request. Each request passes the Gateway policy, lands in a new rate-limit partition (500/min each), and is proxied to Nodes. Result: the PerAgent limit is bypassed entirely, and the number of live limiter partitions is attacker-controlled (memory growth). Before this change the unauthenticated key was the remote IP. There is no `GlobalLimiter`, so nothing else bounds this. — Change: do not let an unvalidated certificate pick its own bucket. Either (a) key on the remote IP (`agent-ip:{RemoteIpAddress}`) and use the cert hash only as a secondary key combined with the IP (`agent-cert:{ip}:{hash}`) plus a separate IP-only limiter chained through `PartitionedRateLimiter.CreateChained` and assigned to `options.GlobalLimiter` for the agent routes, or (b) if per-thumbprint limiting is wanted, apply it in Nodes after chain validation. Record the choice as a decision if it departs from the spec. — verify: add a Gateway test that sends more than the PerAgent `PermitLimit` requests from one connection IP, each with a different forged-issuer certificate (issuer CN = `AgentCertificateProfile.CaCommonName`), and asserts that 429 is returned.

N2. deploy/compose/docker-compose.services.yml:112-120 — `Mtls__ForwardedCertificate__TrustedProxies__0: "172.16.0.0/12"` is set on Nodes, which also publishes `"5040:8080"` to the host. With Docker port publishing, connections from the host or LAN arrive at Nodes from the bridge gateway address (172.x), which falls inside the trusted range. Concrete input: `curl -H "X-Client-Cert: <base64 of any agent's public certificate>" http://localhost:5040/...`. Nodes treats this as the Gateway forwarding that certificate. A certificate is public, and no TLS handshake proves possession of its private key on this path, so this is agent impersonation on any dev machine that exposes the port. Only Nodes' own chain and SPIFFE checks remain, and those pass for a real certificate. — Change: bind the published port to loopback (`"127.0.0.1:5040:8080"`) so only local processes can reach it, and narrow the trusted proxy to the Gateway container only (a fixed compose network with a static Gateway address, or a /32). If a wide range must stay for dev, add a comment that the port must not be exposed beyond loopback. The same applies to any other host that sets the `Mtls__ForwardedCertificate__*` variables. — verify: from the host, `curl -H "X-Client-Cert: abc" http://<lan-ip>:5040/` is refused (connection refused), and a request from a container other than the Gateway is not treated as a trusted proxy.

## Not fixed

- Advisory: `IsAgentIssued` is an issuer-CN string match, so the `AgentCertificate` policy proves nothing about the certificate. This is per D-S25 (Nodes validates). Keep the policy name and README honest on this point: the README says "the Gateway only checks the issuer", which is accurate. No code change.
- Advisory: when TLS is terminated upstream of the Gateway (for example Cloudflare), `Connection.ClientCertificate` is never populated and agent routes return 401. This is D-S210 territory and out of scope for this packet. Production must not satisfy this by forwarding the certificate in a header from outside the Gateway, because the Gateway strips it.
- Advisory: `SessionAffinity` (cookie) is now enabled on the whole `nodes` cluster, including the tenant JWT routes. The cookie carries only a destination id and is HttpOnly and Secure. No auth impact found.
- Checked and clean: the header is removed in `Apply` for every route, and the removal is registered before the add transform. The enroll route has no forwarding metadata and no policy change. `ClientCertificateMode` defaults to `NoCertificate`, and an invalid value fails at startup. No certificate validation callback returns true. No secrets are logged or committed.
- Test note: `EnrollRoute_SpoofedHeader_IsStripped` and `IdentityRoute_SpoofedHeader_IsStripped` cover the strip rule well. There is no test where a spoofed header arrives on a forwarding route along with a real certificate (the real value should win). Optional.

security-review label needed
