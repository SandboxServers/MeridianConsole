---
title: Windows Agent Installer
doc_id: ref.agents.windows-installer
type: reference
audience: [engineers, operators, agents]
status: draft
implementation: mixed
last_verified: 2026-10-27
verified_by: automated-agent
sources:
  - src/Agents/Dhadgar.Agent.Windows/Installer/Package.wxs
  - src/Agents/Dhadgar.Agent.Windows/Installer/Dhadgar.Agent.Windows.Installer.wixproj
  - src/Agents/Dhadgar.Agent.Windows/Installer/README.md
  - src/Agents/Dhadgar.Agent.Windows.InstallerActions/EnrollmentTokenActions.cs
  - src/Agents/Dhadgar.Agent.Windows.InstallerActions/EnrollmentTokenData.cs
  - src/Agents/Dhadgar.Agent.Windows/Installation/RegistryEnrollmentTokenSource.cs
  - src/Agents/Dhadgar.Agent.Windows/Installation/RegistryEndpointConfiguration.cs
  - src/Agents/Dhadgar.Agent.Windows/Installation/AgentRegistry.cs
evidence:
  - test: tests/Dhadgar.Agent.Windows.Tests/Installation/InstallerPackageTests.cs
  - test: tests/Dhadgar.Agent.Windows.Tests/Installation/EnrollmentTokenDataTests.cs
  - test: tests/Dhadgar.Agent.Windows.Tests/Installation/RegistryEndpointConfigurationTests.cs
  - manual: 2026-10-27 agent-run UAT 20261027-203859-0f1b (Msi mode, commit c0a641e, Windows build 26300, elevated, tree clean) installed the built MSI, enrolled, connected to the hub over WebSockets, heartbeated and uninstalled; cleanup clean, 0 secret findings. Record at docs/analysis/sprint2-agent-nodes/uat/windows-uat-msi-20261027-203859-0f1b.md
decisions: [adr-0007, D-S224, D-S230, D-S236, D-S239, D-S241, D-S242, D-S243, D-S244, D-S245]
divergences: []
issues: []
related: [ref.agents.index, adr-0008]
supersedes: []
---

# Windows Agent Installer

What does the Windows MSI install, what does it refuse, and how do we know it works? The installer is a WiX v5 package that puts the Meridian Console agent on a customer's Windows machine as a service and, optionally, hands it a one-time enrollment token and the control plane URL. It runs elevated on customer hardware, so most of this chapter is about what it writes, where, and what it must never log.

## Purpose and boundaries

This chapter owns:

- The MSI package (`Package.wxs`, the wixproj) and its properties.
- The two DTF custom actions that write and delete the enrollment token (`Dhadgar.Agent.Windows.InstallerActions`).
- The list of artifacts that remain after uninstall.

It does not own:

- What the agent does once it runs (enrollment, the hub, heartbeats). See [ADR-0008 as built](../../adr/0008-agent-transport-signalr.md#as-built-sprint-2) and the agent security model in [ADR-0007](../../adr/0007-agent-security-model.md).
- The Windows UAT harness that drives the MSI. See the [campaign ledger](../../analysis/sprint2-agent-nodes/README.md) and the [UAT run records](../../analysis/sprint2-agent-nodes/uat/).
- Distribution, signing and release. The MSI is unsigned and no release process exists.

`src/Agents/Dhadgar.Agent.Windows/Installer/README.md` stays as the landing page for the project (build, install and uninstall commands). This chapter is the specification.

## Decisions

The decisions below were made during the Sprint 2 campaign. They are recorded in the [campaign README](../../analysis/sprint2-agent-nodes/README.md) and its worknotes, not in an ADR. No ADR exists for the installer; see Gaps.

| Decision | What it fixes | Record |
| --- | --- | --- |
| Endpoint through HKLM (D-S230) | `CONTROL_PLANE_ENDPOINT` is written to `HKLM\SOFTWARE\Meridian Console\Agent\ControlPlaneEndpoint`, must be `https://`, and is kept across major upgrades when omitted. | D-S230 |
| Protected ACLs (D-S236) | The data folders and the agent registry key use protected-DACL SDDL: SYSTEM and Administrators only. | D-S236 |
| UAT build restores locked and RID-less (D-S241) | The UAT build script never rewrites lock files and never runs a bare `publish -r`. | D-S241 |
| Windows version from the registry (D-S242) | `WINDOWS_BUILD_NUMBER` is read from `CurrentBuildNumber`, because msiexec reports Windows 8.1 values. | D-S242 |
| Cleanup is idempotent (D-S243) | The UAT cleanup can run twice and leave the machine in the same state. | D-S243 |
| Service in the exe-keyed component (D-S244) | `ServiceInstall` lives in `MainExecutableComponent`, whose key path is the agent executable. | D-S244 |
| Token only through a hidden custom action (D-S245) | The enrollment token reaches HKLM only through a `HideTarget` deferred custom action, never a Registry table row. | D-S245 |
| Evidence rules for "confirmed working on Windows" (D-S224, D-S239) | Agent-run UAT from an elevated session counts only with `launchedBy = agent`, `elevated = true` and a clean tree. | D-S224, D-S239 |
| The agent runs as LocalSystem | The agent needs the services, certificate store, firewall and ACL rights listed in the installer README. | [ADR-0007](../../adr/0007-agent-security-model.md); [Installer README § Service Account](../../../src/Agents/Dhadgar.Agent.Windows/Installer/README.md#service-account-localsystem) |

## Contract

The installer has no network contract. Its interface is the MSI properties, the registry values the agent reads and the service it registers.

### MSI properties

| Property | Meaning | Default |
| --- | --- | --- |
| `INSTALLFOLDER` | Installation directory | `C:\Program Files\Meridian Console\Agent\` |
| `ENROLLMENT_TOKEN` | One-time enrollment token from the control plane. Declared `Secure="yes" Hidden="yes"`. Written to `HKLM\SOFTWARE\Meridian Console\Agent\EnrollmentToken` by a hidden custom action only. | (empty) |
| `CONTROL_PLANE_ENDPOINT` | Control plane URL. Must be `https://`. The command-line value wins; otherwise the value the previous install stored is kept. | (empty; the agent falls back to `appsettings.json`) |
| `WINDOWS_BUILD_NUMBER` | Not set by the user. Read from `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\CurrentBuildNumber` (64-bit view). | (read at install) |

### Registry values the agent reads

| Value under `HKLM\SOFTWARE\Meridian Console\Agent` | Written by | Read by |
| --- | --- | --- |
| `EnrollmentToken` | `WriteEnrollmentToken` custom action | `RegistryEnrollmentTokenSource`; removed by `EnrollmentTokenCleanup` after use |
| `ControlPlaneEndpoint` | `ControlPlaneEndpointComponent` (only when the property is set) | `RegistryEndpointConfiguration` |
| `InstallPath`, `Version`, `DataPath`, `ServiceInstalled` | Component registry values | Operators and the UAT |

The value names are constants in `AgentRegistry` (`src/Agents/Dhadgar.Agent.Windows/Installation/AgentRegistry.cs`); the custom actions use `EnrollmentTokenData.KeyPath` and `EnrollmentTokenData.ValueName` (`src/Agents/Dhadgar.Agent.Windows.InstallerActions/EnrollmentTokenData.cs`). `InstallerPackageTests.PackageWxs_HklmRegistryKeys_UseAgentRegistryKeyPath` and `PackageWxs_EndpointValue_UsesControlPlaneEndpointValueName` pin the key path and the endpoint value name against `AgentRegistry`.

### What gets installed

| Item | Value |
| --- | --- |
| Files | `Dhadgar.Agent.Windows.exe`, `appsettings.json` under `INSTALLFOLDER` |
| Folders | `C:\ProgramData\Meridian Console\` and `Agent\`, `Agent\Servers\`, `Agent\Temp\` below it |
| Folder ACL | `D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;;;;OW)` (SYSTEM and Administrators full control; an empty-mask Owner Rights entry removes an owner's implicit WRITE_DAC) |
| Agent registry key ACL | `D:P(A;OICI;KA;;;SY)(A;OICI;KA;;;BA)` |
| Service | `DhadgarAgent`, display name `Meridian Console Agent`, `LocalSystem`, `Start="auto"`, recovery restart on all three failures |
| Event source | `Meridian Console Agent` in the `Application` log |
| Package | Per-machine, 64-bit, `MajorUpgrade` with `AllowSameVersionUpgrades="yes"` and a downgrade error |

### Custom actions

| Action | Kind | Purpose |
| --- | --- | --- |
| `SetWriteEnrollmentToken` | Property setter, `HideTarget="yes"` | Copies `[ENROLLMENT_TOKEN]` into the hidden data of the deferred action |
| `WriteEnrollmentToken` | DTF, deferred, no impersonation, `HideTarget="yes"` | Writes the value after `WriteRegistryValues` |
| `RollbackEnrollmentToken` | DTF, rollback | Deletes the value |
| `DeleteEnrollmentToken` | DTF, deferred | Deletes the value on uninstall, before `RemoveRegistryValues` |

The project is `Dhadgar.Agent.Windows.InstallerActions` (net462; not in `Dhadgar.sln`). The wixproj builds it through a project reference. MakeSfxCA wraps the AnyCPU assembly in the x64 host and fails on paths of 260 characters or more.

## Expected behaviour

1. The MSI installs only on 64-bit Windows 10 (build 10240) or later, on a first install. The check reads `WINDOWS_BUILD_NUMBER`; it does not use `WindowsBuild` or `VersionNT`.
2. A non-`https://` `CONTROL_PLANE_ENDPOINT` is refused before anything is written. The check is skipped only on uninstall (`REMOVE="ALL"`), so a bad stored value cannot block removal.
3. A `CONTROL_PLANE_ENDPOINT` given on the command line wins. When it is omitted, the value stored by a previous install is kept.
4. The MSI creates the folders and the agent registry key with the protected SDDL listed above, whether or not a token is passed.
5. The service `DhadgarAgent` is installed from `MainExecutableComponent`, runs as LocalSystem, starts automatically and is started at the end of install.
6. The enrollment token is written to HKLM only by the hidden deferred custom action `WriteEnrollmentToken`. No MSI log, including `/l*v`, contains it.
7. A failed or rolled-back install, and an uninstall, delete the `EnrollmentToken` value.
8. After first start the agent enrolls with the token, deletes the registry value, persists its identity and heartbeats over the hub.
9. A same-version or newer MSI upgrades in place; an older MSI is refused.
10. Uninstall stops and removes the service, the files and the registry values. It leaves the residue listed under "After uninstall".

### After uninstall

These remain by design, to avoid data loss and allow a security review. Operators clean them by hand (commands are in the [installer README](../../../src/Agents/Dhadgar.Agent.Windows/Installer/README.md#manual-cleanup-required)).

| Residue | Location |
| --- | --- |
| Agent client certificates, each with a persisted machine key | `Cert:\LocalMachine\My`, issuer `CN=Meridian Console Agent CA` |
| The agent CA | Custom store `MeridianConsoleAgent`, never `Root` |
| Firewall rules the agent created | Windows Firewall, program `Dhadgar.Agent.Windows.exe` |
| Game server data | `C:\ProgramData\Meridian Console\Agent\Servers` |
| Game server services | `MeridianGS_*` |
| Agent state and the event source | `C:\ProgramData\Meridian Console\Agent\agent-state.json`, source `Meridian Console Agent` |

## Implementation

| Claim | Where |
| --- | --- |
| 1 | `src/Agents/Dhadgar.Agent.Windows/Installer/Package.wxs` (`WINDOWS_BUILD_NUMBER` property, the `Installed OR WINDOWS_BUILD_NUMBER >= 10240` and `VersionNT64` launch conditions) |
| 2, 3 | `Package.wxs` (`Launch` on `CONTROL_PLANE_ENDPOINT`; `CMDLINE_CONTROL_PLANE_ENDPOINT` and the two `SetProperty` rows around `AppSearch`; `ControlPlaneEndpointComponent`) |
| 4 | `Package.wxs` (`PermissionEx` on the data folder components and the `InstallPathComponent` registry key) |
| 5 | `Package.wxs` (`MainExecutableComponent`, `AgentServiceInstall`, `AgentServiceControl`); `src/Agents/Dhadgar.Agent.Windows/Installation/ServiceInstaller.cs` (`ServiceInstaller.ConfigureRecoveryAsync`, progressive delays at startup, called from `Program.cs`) |
| 6, 7 | `Package.wxs` (the four custom actions and their `InstallExecuteSequence` rows); `EnrollmentTokenActions.WriteEnrollmentToken` and `EnrollmentTokenActions.DeleteEnrollmentToken` |
| 8 | `RegistryEnrollmentTokenSource`, `EnrollmentTokenCleanup`, `src/Agents/Dhadgar.Agent.Core/Hosting/AgentBootstrapService.cs` |
| 9 | `Package.wxs` (`MajorUpgrade`) |
| 10 | `Package.wxs` (`ServiceControl Remove="uninstall"`, component removal; the residue comment block) |

## Evidence and status

The static tests parse `Package.wxs` as text and check its structure. They prove the package is built the way this chapter says, not that Windows Installer behaves that way. The behaviour claims rest on the Msi-mode UAT run. Its record: [windows-uat-msi-20261027-203859-0f1b.md](../../analysis/sprint2-agent-nodes/uat/windows-uat-msi-20261027-203859-0f1b.md).

| Claim | Implementation | Evidence |
| --- | --- | --- |
| 1 (accepts a supported machine) | confirmed-working | The Msi run installed on Windows build 26300, where msiexec reports 9600. `InstallerPackageTests.PackageWxs_WindowsVersionCondition_UsesRegistryBuildNumber`, `PackageWxs_WindowsBuildNumber_IsReadFromCurrentVersionInThe64BitView` |
| 1 (refuses an old machine) | code-exists | Static tests only. No pre-Windows 10 machine was tried. |
| 2 | code-exists | `InstallerPackageTests.PackageWxs_EndpointLaunchCondition_AppliesOutsideUninstall`. The refusal path was not run against Windows Installer. |
| 3 | code-exists | `PackageWxs_EndpointProperties_AreSecure`, `RegistryEndpointConfigurationTests`. No upgrade run kept a stored endpoint. |
| 4 | code-exists | `PackageWxs_CreateFolders_HaveProtectedSddlWithOwnerRightsAce`, `PackageWxs_UsesOnlySddlPermissions`, `PackageWxs_AgentKey_IsProtectedByAnUnconditionalComponent` pin the SDDL. The Msi run's U3 step checked the ACL of the agent state file (`stateFileAclSids=S-1-5-18,S-1-5-32-544`), which is related but is not a check of the MSI-set folder and key ACLs. |
| 5 | confirmed-working | The Msi run installed the service, enrolled and heartbeated through it. `PackageWxs_ServiceInstall_IsInTheComponentWhoseKeyPathIsTheAgentExe`, `PackageWxs_ServiceElements_UseProgramServiceName`. Progressive recovery delays: code-exists. |
| 6 | confirmed-working | The Msi run found the token masked in `msi-install.log` and 0 secret findings (U9). `PackageWxs_EnrollmentToken_IsFormattedOnlyIntoTheHiddenSetter`, `PackageWxs_TokenActions_AreHiddenDeferredAndRunAsSystem`, `PackageWxs_TokenActions_AreSequencedAroundTheRegistryWrite`, `InstallerActions_NeverLogTheToken`. |
| 7 | mixed | Uninstall: confirmed by the Msi run (uninstall exit 0, no token left, cleanup clean). Rollback: code-exists; no failed install was forced. |
| 8 | confirmed-working | The Msi run's U1, U3 and U4 steps passed; S-hub-ws passed over WebSockets (`hubTransport=WebSockets`). This exercises the agent as built at `c0a641e`; the later P39 and P40 packets do not touch the Windows agent source. |
| 9 | code-exists | Static (`MajorUpgrade`). No upgrade leg is recorded. |
| 10 | confirmed-working | The Msi run recorded the residue exactly: one certificate in `LocalMachine\My`, the `MeridianConsoleAgent` store and `%ProgramData%\Meridian Console\Agent`. |

The Msi run is one machine (Windows build 26300), one MSI version (`1.0.0.0`), one run. It is an agent-run UAT under D-S239. Process and Service mode runs exercise the same agent without the MSI; see the [campaign README](../../analysis/sprint2-agent-nodes/README.md). The product's overall status is in [PROJECT-STATE](../../PROJECT-STATE.md#status-update-2026-10-27-sprint-2-the-agent-talks-to-nodes).

## Gaps and divergences

| Gap | Detail | Tracked as |
| --- | --- | --- |
| CLAUDE.md WiX version rule contradicts D-S242 | CLAUDE.md says to use `WindowsBuild >= 10240` for Windows 10+. msiexec reports 9600 for `WindowsBuild`, so that condition refuses every Windows 10 and 11 machine. The code follows D-S242. | Owner action O1: correct the CLAUDE.md rule |
| CLAUDE.md `Hidden="yes"` rule is incomplete | CLAUDE.md says: "**WiX:** Mark sensitive properties with `Hidden="yes"` to prevent logging." `Hidden="yes"` does not mask a property formatted into a table row or a deferred action without `HideTarget`. The rule in this chapter is stricter. `security-advisor.md` carries the same sentence. | Owner action O2 |
| The token is an unzeroable managed string | The custom action receives the token as a .NET `string`, which cannot be zeroed. CLAUDE.md requires sensitive data to be zeroed immediately after use. | Owner decision (P36/P37 residual); see the campaign README |
| Table checks are not in CI | The checks that read the built MSI's tables (the real `Registry`, `CustomAction` and `ServiceInstall` rows) ran in the packet, not in CI. CI runs the static tests on `Package.wxs`. | Follow-up issue (campaign report) |
| Placeholder component GUIDs | `MainExecutableComponent` uses `A1B2C3D4-E5F6-7890-ABCD-EF1234567890`, and several others follow a pattern. They should become real GUIDs before the first release. No install of this MSI has shipped, so no component-rule obligation exists yet. | Follow-up issue (P36 ruling) |
| WiX warnings ICE61 and ICE69 | The build emits exactly these two (WIX1076). Accepted in the P26 ruling. | Follow-up issue |
| Service start type documented wrongly | `Package.wxs` sets `Start="auto"`. The installer README says "Automatic (Delayed Start)". The code is the source. | Doc debt: `src/Agents/Dhadgar.Agent.Windows/Installer/README.md` |
| No upgrade, rollback or old-OS leg | See claims 1, 3, 7 and 9 above. | Follow-up: extend the Msi UAT |
| No Windows UAT in CI | The UAT needs an elevated Windows session and changes the machine. It runs on request, from the runner. | Follow-up issue |
| No signing, no release process | The MSI is unsigned. The installer README tells you to uncomment a `SignOutput` property in the wixproj, but the wixproj has no such property. | Out of scope for Sprint 2; doc debt in the installer README |
| No ADR for the installer | The decisions above live in the campaign ledger. | ADR to backfill: installer token handling (D-S245) |
| Residue is manual | Certificates, firewall rules, game server data and services are left behind by design. | Future enhancement noted in `Package.wxs` |

## Operating it

Rules for anyone changing the installer.

> A secret installer property must never be formatted into a Registry, IniFile or Environment row, or into a deferred action without `HideTarget`: `Hidden="yes"` does not mask those in `/l*v` logs (D-S245).
>
> (Installer README, "Custom actions".)

The wider rules still apply to the custom actions and the agent. They are quoted from [CLAUDE.md](../../../CLAUDE.md):

> Same rule applies to WiX CustomActions - validate/encode before injection.

> Zero out sensitive data immediately after use

An administrator who sets the Windows Installer `Debug` policy to 7 opts in to logging otherwise masked values, the command line included. A token on the `msiexec` command line is visible in process listings, command history and audit logs. The installer README advises a transform (`.mst`) for production.

| Item | Value |
| --- | --- |
| Build the MSI | `dotnet build -c Release` in `src/Agents/Dhadgar.Agent.Windows/Installer`. Output: `bin\Release\MeridianConsoleAgent.msi`. `InstallerPlatform` is `x64`. |
| Build for the UAT | `pwsh -NoProfile -File scripts\uat\Build-AgentWindowsUat.ps1 -Verb run -Mode Msi`. It restores locked and RID-less (D-S241). |
| Silent install | `msiexec /i MeridianConsoleAgent.msi /qn /norestart` |
| Install with token and endpoint | `msiexec /i MeridianConsoleAgent.msi /qn ENROLLMENT_TOKEN=<token> CONTROL_PLANE_ENDPOINT=https://<host>` |
| Install with a log | `msiexec /i MeridianConsoleAgent.msi /qn /l*v install.log` |
| Upgrade | Run the newer MSI with the same command. Same-version upgrades are allowed. |
| Uninstall (rollback of an install) | `msiexec /x {ProductCode} /qn`. Only the ProductCode or the MSI path works with `/x`. |
| Configuration | `HKLM\SOFTWARE\Meridian Console\Agent` values above; `appsettings.json` is the fallback; environment variables and the agent's command line still win over the registry endpoint. |
| Port | N/A. The agent connects outward to the Gateway; the installer opens no port. |
| Health | Service `DhadgarAgent` in the Service Control Manager; Event Log source `Meridian Console Agent` in `Application` |
| Telemetry | The MSI log (`/l*v`); the Event Log |
| Runbook | None yet. Troubleshooting notes are in the [installer README](../../../src/Agents/Dhadgar.Agent.Windows/Installer/README.md#troubleshooting). |

Do not run `msiexec` or the UAT wrapper outside the UAT runner when you are working in the repository: the runner owns the machine changes and the cleanup (D-S239).

## Related

- [Installer README](../../../src/Agents/Dhadgar.Agent.Windows/Installer/README.md): landing page with the build and install commands.
- [ADR-0007](../../adr/0007-agent-security-model.md): the agent security model the installer sits under.
- [ADR-0008 as built](../../adr/0008-agent-transport-signalr.md#as-built-sprint-2): the hub the installed agent connects to.
- [UAT run records](../../analysis/sprint2-agent-nodes/uat/): the evidence behind the confirmed rows.
- [PROJECT-STATE](../../PROJECT-STATE.md): the status of record.
