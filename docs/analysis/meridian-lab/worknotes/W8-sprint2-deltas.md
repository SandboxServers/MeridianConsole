# W8 ruling: Sprint 2 names for L39 to L44

Architect ruling, 2026-11-02, read at campaign tip `143b5b02`. The campaign already contains `main` at `398e9061` (merge-base = `main`), so no newer Sprint 2 code exists. This note is the wave 8 gate (work-packets, wave 8 header). Packets L39 to L44 use the names below; where this note and a packet differ, this note wins. Paths are repo-relative.

## 1. Names confirmed

| Name | Value | Where |
| --- | --- | --- |
| `AgentWindowsFootprint` | `ServiceName = "DhadgarAgent"`, `RegistryKeyPath = @"SOFTWARE\Meridian Console\Agent"`, `EnrollmentTokenValueName = "EnrollmentToken"`, `CaStoreName = "MeridianConsoleAgent"`, `StateDirectoryRelative = @"Meridian Console\Agent"`, `StateFileName = "agent-state.json"`, `EventLogSourceName = "Meridian Console Agent"` | `src/Shared/Dhadgar.Contracts/Agents/AgentWindowsFootprint.cs:9-15` |
| Hub path | `AgentHubProtocol.Path = "/hubs/agent"` | `src/Shared/Dhadgar.Contracts/Nodes/AgentHubContracts.cs:9` |
| Hub mapping | `app.MapHub<AgentHub>(AgentHubProtocol.Path).RequireAuthorization("AgentPolicy")` | `src/Dhadgar.Nodes/Program.cs:234` |
| Protocol header | `AgentHubProtocol.VersionHeader` (`X-Agent-Protocol`), `CurrentVersion = "1"`; a missing header counts as `"1"` | `AgentHubContracts.cs:12,18`; `src/Dhadgar.Nodes/Hubs/AgentHub.cs:41` |
| Hub client methods | `IAgentHubClient.ReceiveCommand(AgentCommandEnvelope)`, `ReceivePing(string)` | `AgentHubContracts.cs:111` |
| Hub server methods | `IAgentHub.Heartbeat`, `Telemetry`, `CommandResult`, `ServerStatusChanged`, `ConsoleOutput` (the last three return `NotSupported`) | `AgentHubContracts.cs:92` |
| Grace column | `AgentCertificate.GraceUntil` (`DateTime?`), column `"GraceUntil"` | `src/Dhadgar.Nodes/Data/Entities/AgentCertificate.cs:43`; migration `20261027130100_AddAgentCertificateGraceUntil.cs:15` |
| Usable rule | `IsUsable(c, utcNow) => !c.IsRevoked \|\| (c.GraceUntil is { } g && utcNow < g)` | `src/Dhadgar.Nodes/Services/AgentCertificateState.cs:13-14` |
| Grace option | `Mtls:RenewalGraceMinutes` (`MtlsOptions`), 60 in `appsettings.json` | `src/Dhadgar.Nodes/Auth/MtlsOptions.cs:15,43`; `src/Dhadgar.Nodes/appsettings.json:36` |
| Renewal threshold | `Agent:Security:CertificateRenewalThresholdDays` (`SecurityOptions` under `AgentOptions.Security`), default `AgentCertificateProfile.RenewalThresholdDays` = 30 | `src/Agents/Dhadgar.Agent.Core/Configuration/SecurityOptions.cs:68` |
| Certificate profile | static `AgentCertificateProfile`: `SpiffeTrustDomain`, `SpiffeNodeIdPrefix`, `SpiffeId()`, `Organization = "MeridianConsole"`, `CaCommonName = "Meridian Console Agent CA"`, `ValidityDays = 90`, `RenewalThresholdDays = 30`, `ForwardedCertificateHeader = "X-Client-Cert"` | `src/Shared/Dhadgar.Contracts/Nodes/AgentCertificateProfile.cs:7` |
| Gateway agent ingress | one key only: `AgentIngress:ClientCertificateMode`, default `"NoCertificate"` | `src/Dhadgar.Gateway/appsettings.json:432-433`, read at `src/Dhadgar.Gateway/Program.cs:39` |
| Gateway routes | `agents-enroll-route` `/api/v1/agents/enroll` (Anonymous, `Auth` limit); `agents-ca-route` `/api/v1/agents/ca-certificate` (Anonymous); `agents-route` `/api/v1/agents/{**catch-all}` (`AgentCertificate`); `agent-hub-route` `/hubs/agent/{**catch-all}` (`AgentCertificate`, `PerAgent` limit, `ForwardClientCertificate=true`) | `src/Dhadgar.Gateway/appsettings.json:160,198,208,218` |
| MSI | project `src/Agents/Dhadgar.Agent.Windows/Installer/Dhadgar.Agent.Windows.Installer.wixproj`, output `MeridianConsoleAgent.msi`, token property `ENROLLMENT_TOKEN` (`Secure="yes" Hidden="yes"`) | wixproj:20; `Package.wxs:61` |
| `AddAgentCore` | registers `IAgentStateStore` (TryAdd), `IHardwareInventoryCollector`, `ISystemMetricsCollector`; the host registers `ICertificateStore` and `IProcessManager` | `src/Agents/Dhadgar.Agent.Core/Hosting/AgentServiceCollectionExtensions.cs:35-74` |
| Test certificate store | `InMemoryCertificateStore` | `tests/Dhadgar.Nodes.Tests/Integration/AgentSeam/InMemoryCertificateStore.cs:11` |
| Pipe server | `AgentPipeServer` | `src/Agents/Dhadgar.Agent.Windows/IPC/AgentPipeServer.cs:98` |
| Game server service prefix | `GameServerServiceConfig.ServiceNamePrefix = "MeridianGS_"`; also a literal in `src/Agents/Dhadgar.Agent.GameServerWrapper/Program.cs:38` and a regex in `Security/DirectoryAclManager.cs:80` | `src/Agents/Dhadgar.Agent.Windows/Services/GameServerServiceConfig.cs:24` |

## 2. Deltas from the plan, per packet

| Packet | Plan says | Code says | Ruling |
| --- | --- | --- | --- |
| L39 | the key path and `EnrollmentTokenValueName` are in `Installation/EnrollmentTokenCleanup.cs` | they are `AgentRegistry.KeyPath` and `AgentRegistry.EnrollmentTokenValueName` in `Installation/AgentRegistry.cs:10,13`; the cleanup only uses them | L39 aliases them in `AgentRegistry.cs` (`= AgentWindowsFootprint.RegistryKeyPath`, `= AgentWindowsFootprint.EnrollmentTokenValueName`). `ControlPlaneEndpointValueName` (:16) is not in the footprint and stays a literal |
| L39 | the state file name is "in `AgentOptions` or its consumer" | `FileAgentStateStore.FileName` (`Dhadgar.Agent.Core/Identity/FileAgentStateStore.cs:21`) and the directory built inline in `AddAgentCore` (:44-47); Agent.Core also runs on Linux | No Agent.Core change in L39: a Windows footprint must not drive the cross-platform core. S-L3 compares `FileAgentStateStore.FileName` with `AgentWindowsFootprint.StateFileName` directly. L39 edits only `Program.cs:26,31`, `Windows/WindowsCertificateStore.cs:43` and `Installation/AgentRegistry.cs:10,13` |
| L39 | "the MSI's WiX constants if shared through a generated file" | not checked here | the L39 coder greps `Package.wxs` for the footprint values; with no generated file, they are listed in the report as a follow-up, as the packet says |
| L40 | add `AgentHubConnected`/`AgentHubDisconnected` "if not already logged" | `OnConnectedAsync` logs only refusals, with `LogWarning`; `OnDisconnectedAsync` logs nothing; Nodes has no EventIds 4101 or 4102 | add both `LoggerMessage` events (4101, 4102) with `NodeId`; the existing warnings stay |
| L40 | hub path "from the W8 ruling" | `/hubs/agent` (`AgentHubProtocol.Path`) | `HubSource` matches Nodes log lines by EventId and `NodeId` only, not by path |
| L41 | "Sprint 2's Gateway agent-ingress config keys" | one key: `AgentIngress:ClientCertificateMode` | the `svc_config_set` allowlist gains that key only. The forwarded header is `AgentCertificateProfile.ForwardedCertificateHeader` |
| L41 | trust-add the Nodes CA "only if the agent trusts its own CA store" | the agent keeps the Nodes CA in its own `MeridianConsoleAgent` LocalMachine store (`CaStoreName`); `CertificateValidator` uses `CustomRootTrust` with that CA (`Dhadgar.Agent.Core/Authentication/CertificateValidator.cs:73-74`) | `trust-add` adds only the lab TLS CA to the member's Root store. The Nodes CA stays in the agent's own store and is never added to Root. If the L41 coder finds that the agent validates the Gateway's server certificate with `CertificateValidator`, it stops and reports, because the lab TLS CA would then have to go into the agent's own store instead |
| L42 | replace the F31 list per agent | `AddAgentCore` uses `Add` (not `TryAdd`) for the two collectors, so a fake registered before it is overridden | the fleet calls `AddAgentCore()` first, then `services.Replace(...)` for every fake. It registers `ICertificateStore` and `IProcessManager` itself. `InMemoryCertificateStore` is a model to copy into `src/Tools/Dhadgar.Lab/Fleet/`, never a reference to the Nodes test project |
| L43 | `renew-within-grace` sets the threshold to 90; `after-grace` shifts `grace_until` by -120 minutes | 90 = `ValidityDays`, so renewal is immediate; grace is 60 minutes | both values stand. `agent_msi_install` sets `ENROLLMENT_TOKEN`; the MSI is `MeridianConsoleAgent.msi` from the wixproj above |
| L43 | `MeridianGS_<id>` service state | the prefix is `GameServerServiceConfig.ServiceNamePrefix`, duplicated in two places | rows read the prefix from `GameServerServiceConfig.ServiceNamePrefix`. The two duplicates are a product follow-up, not a lab packet |
| L44 | fuzz the hub | five server methods; three return `NotSupported` | fuzz all five; for the three stubs the expected result is the `NotSupported` refusal, never a disconnect or a 500 |

## 3. What this unblocks

- L39 is Ready now (its only dependency was this note).
- L40 still waits on L32 (wave 6, blocked by L00 and D-ML37/D-ML38). L41 to L44 wait on L40 and their other dependencies.
