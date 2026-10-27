# P06 coder report: Gateway client certificate, header transform, hub route and AgentCertificate policy

Branch `s2/p06-gateway-agent-ingress`. Worktree `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p06`.

## Files changed

- `src/Dhadgar.Gateway/Program.cs`: Kestrel `ClientCertificateMode` from `AgentIngress:ClientCertificateMode` (AllowAnyClientCertificate only when not NoCertificate, with the D-S25 comment); `AgentCertificate` policy; `AddTransforms(AgentCertificateForwarding.Apply)` on the YARP builder (global, so every route strips the header); `PerAgent` partition key `agent-cert:{sha256 thumbprint}` when a client certificate is present; `Agent` policy kept.
- `src/Dhadgar.Gateway/Agents/AgentCertificateForwarding.cs` (new): `MetadataKey`, `IsAgentIssued` (issuer CN equals `AgentCertificateProfile.CaCommonName`), `Apply` (always removes `X-Client-Cert`, re-adds base64 DER only on routes with `ForwardClientCertificate=true`).
- `src/Dhadgar.Gateway/appsettings.json`: `agents-route` policy becomes `AgentCertificate` with the forward metadata (the `Agent` policy is no longer used by that route); new `agent-hub-route` (`/hubs/agent/{**catch-all}`, cluster `nodes`, Order 20, `AgentCertificate`, `PerAgent`, metadata, no transforms); `nodes` cluster gets `SessionAffinity` (cookie `.Dhadgar.Nodes.Affinity`); top-level `AgentIngress.ClientCertificateMode: NoCertificate`.
- `deploy/compose/docker-compose.services.yml`: gateway `AgentIngress__ClientCertificateMode: "AllowCertificate"`; nodes gets `<<: *service-env` plus `Mtls__ForwardedCertificate__Enabled` and `Mtls__ForwardedCertificate__TrustedProxies__0`; both carry the `# dev only; production ingress and trust: D-S210 (docs/analysis/sprint2-agent-nodes)` comment.
- `src/Dhadgar.Gateway/README.md`: agent route rows in the route table and a new "Agent Ingress" section (routes, policy, mode, forwarding, strip-everywhere, rate-limit key, D-S210 pointer).
- `tests/Dhadgar.Gateway.Tests/RouteConfigurationTests.cs`: count 17 to 18 (test renamed `ReverseProxyShouldHave18Routes`); new asserts for `agent-hub-route`, `agents-route`, `agents-enroll-route` (anonymous, no metadata), nodes affinity and the AgentIngress default; agent-hub path and order rows added to the existing theories.
- `tests/Dhadgar.Gateway.Tests/AgentCertificateForwardingTests.cs` (new): five-plus cases as listed below. Uses `GatewayWebApplicationFactory` via `WithWebHostBuilder`, replaces `IForwarderHttpClientFactory` with a capture `HttpMessageInvoker`, sends through `Server.CreateHandler(ctx => ctx.Connection.ClientCertificate = cert)`, and builds certificates with `CertificateRequest`.

## Tests added

- `HubRoute_WithAgentCertificate_ForwardsBase64Der`
- `AgentsRoute_WithoutCertificate_Returns401_AndDoesNotProxy`
- `AgentsRoute_WithForeignIssuer_IsRejected`
- `EnrollRoute_SpoofedHeader_IsStripped`
- `IdentityRoute_SpoofedHeader_IsStripped` (extra, see deviations)

## Checks

1. `dotnet build src\Dhadgar.Gateway\Dhadgar.Gateway.csproj -c Release -p:TreatWarningsAsErrors=true`: Build succeeded. 0 Warning(s), 0 Error(s).
2. `dotnet test tests\Dhadgar.Gateway.Tests\Dhadgar.Gateway.Tests.csproj`: Passed! - Failed: 0, Passed: 135, Skipped: 0, Total: 135.

Revert check: with `.AddTransforms(...)` removed, `EnrollRoute_SpoofedHeader_IsStripped`, `HubRoute_WithAgentCertificate_ForwardsBase64Der` and `IdentityRoute_SpoofedHeader_IsStripped` fail (3 of 135). The hook was restored and the checks were rerun green. Without the route, the hub path returns 404 (the route is in config, so this was not separately run).

## Deviations

- `TenantRoute_SpoofedHeader_IsStripped` skipped as the packet allows: tenant routes need a bearer token, which the test host does not issue. Substituted `IdentityRoute_SpoofedHeader_IsStripped` on an anonymous route.
- The capture handler answers and does not record `/healthz` probes. Active health checks on every cluster share the same forwarder client, so without this filter the captured list contained probe requests. This is test-only.
- The `agents-route` `Metadata` block sits after `Transforms` in the JSON. Ordering only.
- The `nodes` cluster `SessionAffinity` cookie uses `.Dhadgar.Nodes.Affinity` (the packet specifies a nodes-specific name but not the exact string).
- Nodes-side env (`Mtls__ForwardedCertificate__*`) is set in compose only, as the packet says; the Nodes config keys are P03's responsibility.
- Commit subject per packet. The commit message uses the Claude Sonnet 5.5 trailer from the brief.
- Report file is under `.claude/`, which `.gitignore` ignores. Not committed.
