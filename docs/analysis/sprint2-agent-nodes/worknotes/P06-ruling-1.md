# P06 ruling 1 (after review pass 1 = decision, security pass 1 = fix)

Reviewed range `ded8627..7335f07` (head `7335f07`), branch `s2/p06-gateway-agent-ingress`, worktree `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p06`.

Inputs: `.claude/reports/sprint2-agent-nodes/P06-review-1.md`, `.claude/reports/sprint2-agent-nodes/P06-security-1.md` (both in the P06 worktree).

## Ruling

**Resolved by re-spec. This does not need the owner.** The open item is the `PerAgent` limiter (review "Not fixed" 2, security N1). It falls under D-S26, which the architect decided. The owner ruled only on D-S210, D-S212 and D-S215, and none of those covers how the Gateway rate-limits. The fix keeps D-S25 (Nodes is still the only certificate validator) and D-S210 (A) exactly as they are.

| Item | Ruling |
| --- | --- |
| Review N1 (README policy table) | Fix as written (step 5). |
| Review NF2 / Security N1 (`PerAgent` bypass and unbounded partitions) | **Option (b): key `PerAgent` by client IP only**, using the same IPv6 /64 logic as the `Auth` policy. Drop the certificate-hash key and the `sub` key. The partition is keyed by something the attacker cannot choose for each request, so the forged-certificate bypass and the partition growth both go away. Option (a) is rejected: anyone who can reach the Gateway can cheaply flood Nodes, and D-S210 (A) makes the agent hostname public in production. Option (c) is rejected because it contradicts D-S25. |
| Security N2 (dev compose trust range) | Bind Nodes' published port to loopback, following the file's own `betterauth` precedent, and add a comment that names the remaining dev-only risk. Do **not** add a fixed compose network or static IP. That would change networking for every service in the merged dev/services project, and nobody can verify it without Docker. The production trust CIDR is already part of the D-S210 follow-up (P15). |
| Security "Test note" (spoofed header alongside a real certificate) | Add it (step 4, second test). It costs little and pins the remove-then-add order. |
| Review NF1 (security-review label) | Already planned (D-S215: the one campaign PR has the label). No action. |
| Review NF3, NF4; security advisories 1-3 | No change. |

**Amendment to D-S26 (supersedes its rate-limit sentence).** `PerAgent` partitions by client IP (`agent-ip:{ip or IPv6 /64}`), not by certificate thumbprint. The Gateway cannot validate the certificate (D-S25), so a key the certificate picks would let the client choose its own bucket. Every agent behind one NAT address shares the 500/min default. The limit is configurable at `RateLimiting:Policies:PerAgent:PermitLimit`. Per-node limiting, if it is ever wanted, belongs in Nodes after chain validation.

## Implementation steps (packet-coder, P06 worktree, same branch)

Rules: R-CONTRACT, R-NULL (warnings as errors), R-XUNIT, R-CPM (no new packages), R-LOG. Edit only the files named below. Do not touch `docs/analysis/` or `docs/PROJECT-STATE.md`.

1. **New file `src/Dhadgar.Gateway/Services/RateLimitPartitionKeys.cs`.** Move the IP-key logic out of the `Auth` policy unchanged, including the link-local branch:

   ```csharp
   using System.Net;
   using System.Net.Sockets;

   namespace Dhadgar.Gateway.Services;

   /// <summary>Client-IP partition keys shared by the Auth and PerAgent rate-limit policies.</summary>
   public static class RateLimitPartitionKeys
   {
       /// <summary>
       /// IPv4 (and IPv4-mapped IPv6) keeps the full address. Other IPv6 is reduced to its /64 prefix so that
       /// rotating addresses inside one allocation does not escape the limit. Link-local and null get fixed buckets.
       /// </summary>
       public static string ClientIp(IPAddress? ip)
       {
           if (ip is null)
           {
               return "unknown";
           }

           if (ip.IsIPv6LinkLocal)
           {
               return "unknown-linklocal";
           }

           if (ip.AddressFamily == AddressFamily.InterNetworkV6 && !ip.IsIPv4MappedToIPv6)
           {
               var bytes = ip.GetAddressBytes();
               Array.Clear(bytes, 8, 8);
               return new IPAddress(bytes).ToString();
           }

           return ip.ToString();
       }
   }
   ```

2. **`src/Dhadgar.Gateway/Program.cs`:**
   - `Auth` policy (about :212-249): replace the `string partitionKey; if … else …` block with `var partitionKey = RateLimitPartitionKeys.ClientIp(httpContext.Connection.RemoteIpAddress);`. Keep the existing `SECURITY` comments above the policy. Keep the limiter factory exactly as it is.
   - `PerAgent` policy (about :269-284): replace the comment and the `partitionKey` expression with the code below. Keep the limiter factory exactly as it is.

     ```csharp
     // Keyed by client IP (IPv6 /64), never by the client certificate: the Gateway does not validate the
     // certificate (Nodes does, D-S25), so a certificate-derived key would let a caller pick a fresh bucket per
     // request with a forged certificate. Agents behind one NAT address share the bucket (D-S26 as amended).
     var partitionKey = $"agent-ip:{RateLimitPartitionKeys.ClientIp(httpContext.Connection.RemoteIpAddress)}";
     ```

   - After both edits, `HashAlgorithmName` is no longer used in `Program.cs`. Remove `using System.Security.Cryptography;` if nothing else in the file uses it, and do the same for `using System.Net;`. Let the Release build decide: keep a `using` only if removing it breaks the build.
3. **`tests/Dhadgar.Gateway.Tests/RateLimitingBehaviorTests.cs`:** replace the body of the private `GetRateLimitPartitionKey(IPAddress? ip)` with `return RateLimitPartitionKeys.ClientIp(ip);` and add `using Dhadgar.Gateway.Services;`. The existing theories then test the real code instead of a copy. Add one `[Fact]`: `LinkLocalIpv6_ReturnsLinkLocalBucket_ForRateLimitPartition` asserts that `ClientIp(IPAddress.Parse("fe80::1"))` equals `"unknown-linklocal"`.
4. **`tests/Dhadgar.Gateway.Tests/AgentCertificateForwardingTests.cs`:**
   - Change `CreateApp(out CaptureHandler capture)` to `CreateApp(out CaptureHandler capture, IEnumerable<KeyValuePair<string, string?>>? extraSettings = null)`. Append `extraSettings` (when not null) to the in-memory collection it already adds.
   - Change the second `SendAsync` overload to take an optional `IPAddress? remoteIp = null`. Its handler becomes `app.Server.CreateHandler(ctx => { ctx.Connection.ClientCertificate = certificate; if (remoteIp is not null) ctx.Connection.RemoteIpAddress = remoteIp; })`. Existing callers do not change.
   - Add test `PerAgent_ForgedIssuerCertificatePerRequest_IsRateLimitedByIp`:
     - `CreateApp` with extra settings `RateLimiting:Policies:PerAgent:PermitLimit = "3"` and `RateLimiting:Policies:PerAgent:WindowSeconds = "60"`.
     - Make 4 requests: `POST /hubs/agent/negotiate`, remote IP `203.0.113.10`. Each uses a **new** certificate from `CreateCertificate(AgentCertificateProfile.CaCommonName)` (every call mints a fresh self-signed "agent CA" and leaf, which is the forgery). Dispose each certificate and each response.
     - Assert: requests 1-3 return 200, request 4 returns 429, and `capture.Requests.Count == 3`.
     - Why it fails on revert: with the old `agent-cert:{hash}` key, each forged certificate gets its own bucket, so request 4 returns 200 and 4 requests are captured.
     - If request 4 returns 200 even with the new key, the in-memory value did not reach the limiter. In that case also call `builder.UseSetting("RateLimiting:Policies:PerAgent:PermitLimit", "3")` (and the same for `WindowSeconds`) inside `WithWebHostBuilder`, and say so in the report.
   - Add test `HubRoute_SpoofedHeaderWithRealCertificate_ForwardsRealCertificate`:
     - One agent-issuer certificate on the connection, and the request also carries `X-Client-Cert: abc`.
     - Assert 200 and a single captured request whose header has exactly one value, equal to `Convert.ToBase64String(cert.RawData)`. Assert the value list too, not just `Header(...)`'s first value: `Assert.Single(proxied.Headers[AgentCertificateProfile.ForwardedCertificateHeader])`.
5. **`src/Dhadgar.Gateway/README.md`:**
   - The policy table at about :672-676 gets a row directly after the `Agent` row: ``| `AgentCertificate` | TLS client certificate whose issuer CN is `AgentCertificateProfile.CaCommonName` (Nodes validates the rest) |``. Leave the table's existing pipe style as it is.
   - Replace :456 with: ``- The `PerAgent` rate limiter keys every request by client IP (`agent-ip:{ip}`, IPv6 reduced to /64), never by the certificate. The Gateway does not validate the certificate, so a certificate-derived key could be forged to get a fresh bucket per request. Agents behind one NAT address share the limit (`RateLimiting:Policies:PerAgent:PermitLimit`, default 500 per 60 s).``
   - Replace :562 (`- Partition key: \`sub\` claim from JWT, falls back to IP address`, under **PerAgent Policy**) with ``- Partition key: client IP (IPv6 /64 prefix), the same key as the Auth policy``.
6. **`deploy/compose/docker-compose.services.yml`, `nodes` service:**
   - Change the port line to `- "127.0.0.1:5040:8080"`.
   - Replace the comment above the two `Mtls__ForwardedCertificate__*` lines with:

     ```yaml
           # dev only; production ingress and trust: D-S210 (docs/analysis/sprint2-agent-nodes).
           # Nodes trusts X-Client-Cert from the whole Docker bridge pool, so any container on this
           # network, or any host process (host traffic arrives from the bridge gateway), can present
           # an agent's certificate. The host port is therefore bound to loopback only; never publish
           # it beyond 127.0.0.1.
     ```

   - Leave the `gateway` service and `docker-compose.services.acr.yml` unchanged. The ACR file does not enable forwarding.
7. **Checks, in order:**

   ```powershell
   dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p06\src\Dhadgar.Gateway\Dhadgar.Gateway.csproj -c Release -p:TreatWarningsAsErrors=true
   dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p06\tests\Dhadgar.Gateway.Tests\Dhadgar.Gateway.Tests.csproj
   ```

   Revert check, recorded in the report: temporarily put the `agent-cert:` key back in `PerAgent`, confirm that `PerAgent_ForgedIssuerCertificatePerRequest_IsRateLimitedByIp` fails, then restore the new key.
8. **Commit** locally on `s2/p06-gateway-agent-ingress` (no push). Subject: `fix(gateway): key PerAgent limiter by client IP; loopback-bind dev Nodes port`. End with the campaign's two trailer lines.

## Ledger follow-ups (orchestrator, not the coder)

- Amend D-S26 in `docs/analysis/sprint2-agent-nodes/README.md`: replace "`PerAgent` partitions by certificate thumbprint." with the amendment text above, and cite this ruling.
- Add to P15's D-S210 follow-up issue:
  - production `TrustedProxies` must be the Gateway pod CIDR or address, never a broad pool;
  - the dev default network may land outside `172.16.0.0/12` (Docker also allocates from `192.168.0.0/16`), which would silently turn off forwarding in dev. Pin the compose network subnet if that happens.
- The re-review (pass 2) runs `packet-reviewer` and `security-advisor` against `7335f07..<new head>`.
