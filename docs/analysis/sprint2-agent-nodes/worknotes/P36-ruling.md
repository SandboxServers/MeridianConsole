# P36 ruling: the MSI never installs the service, and its verbose log holds the enrollment token

Architect ruling, 2026-10-27. Input: the UAT runner's elevated Msi run at w1 `321ae59` (after P34 and P35). This note makes decisions **D-S244** and **D-S245** (D-S245 amends D-S229) and specifies fix packets **P36** and **P37**.

| Run | Mode | Result |
| --- | --- | --- |
| `20261027-192738-7347` | Msi | **fail at U11a**: `msiexec /i` exit 1603 in `InstallFinalize`, rolled back. **U9 secret scan fail**: the token is in `logs\msi-install.log`. Cleanup clean, footprint empty. |

P34 works: `LaunchConditions` passed and `Property(S): WINDOWS_BUILD_NUMBER = 26300` (log line 694). P35 held: cleanup was clean.

The run folder is `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-w1\.uat\agent-windows\20261027-192738-7347\`. I only read it. I ran no wrapper and no `msiexec`, and wrote nothing to HKLM. The MSI tables were read from temp copies (see § Executed checks).

## (1) ExecServiceConfig: the service is never installed

### Evidence

From `logs\msi-install.log`:

```text
472  Doing action: InstallServices
478  Action ended 14:28:47: InstallServices. Return value 1.
552  Executing op: ComponentRegister(ComponentId={F6A7B8C9-D0E1-2345-F012-678901234567},KeyPath=22:\SOFTWARE\Meridian Console\Agent\ServiceInstalled,State=3,...)
589  Executing op: CustomActionSchedule(Action=Wix4ExecServiceConfig_X64,ActionType=3073,Source=BinaryData,Target=ExecServiceConfig,CustomActionData=SchedServiceConfigDhadgarAgent1restartrestartrestart15)
595  ExecServiceConfig:  Error 0x80070424: Service 'DhadgarAgent' does not exist on this system.
597  CustomAction Wix4ExecServiceConfig_X64 returned actual error code 1603
601  Action ended 14:28:48: InstallFinalize. Return value 3.
```

The script contains no `ServiceInstall(` op at all. `InstallServices` "succeeded" without scheduling anything.

The table dump of the run's MSI (`P36-msi-tables.ps1`, below) shows the cause:

```text
ServiceInstall DhadgarAgent -> component ServiceComponent, KeyPath 'reg0Ft74ZmbcuV8ZvWyP9v_pcGBWa8', Attributes 260
Wix4ServiceConfig DhadgarAgent -> component ServiceComponent
ServiceControl DhadgarAgent -> component ServiceComponent
```

Attributes 260 is 256 (64-bit) plus 4 (registry key path).

### Root cause (F28)

`ServiceInstall`, `util:ServiceConfig` and `ServiceControl` sit in `ServiceComponent` (`Package.wxs:234-298`), and that component's KeyPath is the registry value `ServiceInstalled`.

- **Windows Installer skips the row.** It installs a `ServiceInstall` row only when the row's component has the service executable as its key file. Otherwise `InstallServices` skips the row and reports success.
- **WiX's deferred action then fails.** `Wix4ExecServiceConfig` opens `DhadgarAgent` to set the failure actions and gets `ERROR_SERVICE_DOES_NOT_EXIST` (0x80070424), which becomes 1603 and a rollback.

The brief's other candidates are fine:

- The names match: `ServiceInstall/@Name`, `ServiceControl/@Name`, the `Wix4ServiceConfig` row and `Program.ServiceName` are all `DhadgarAgent`.
- The order is right: WiX schedules `Wix4ExecServiceConfig` after `InstallServices`.

The Service leg never showed this, because it installs with `sc.exe`. The MSI had never reached `InstallServices` before this run (F25, F26).

### Fix (D-S244)

The service elements move into `MainExecutableComponent`, whose KeyPath is `File MainExecutable` (`Dhadgar.Agent.Windows.exe`).

- `ServiceInstall` (with its `util:ServiceConfig`) and `ServiceControl` move unchanged.
- The `ServiceInstalled` value (`#1`) moves into the same component as a plain value under `<RegistryKey Root="HKLM" Key="SOFTWARE\Meridian Console\Agent">`, without `KeyPath`. The UAT's `ServiceInstalled` checks and `scripts/uat/README.md:173` stay true.
- `ServiceComponent` and its `ComponentRef` are deleted.
- **Component GUIDs are unchanged.** No install of this MSI has ever completed, so no installed product carries component-rule obligations. The placeholder GUID `A1B2C3D4-E5F6-7890-ABCD-EF1234567890` on `MainExecutableComponent` (and the other patterned GUIDs) should become real GUIDs before the first release. This is a P15 follow-up issue, not this fix.

Rejected alternatives:

- **Moving the `File` into `ServiceComponent`.** It is the same fix with more churn, and two components would claim the exe's lifecycle across the wxs.
- **Replacing `util:ServiceConfig` with core `ServiceConfigFailureActions`.** It does not fix the key-path rule. `ServiceControl Start="install"` would still fail with 1920 against a service that was never created.
- **Dropping the recovery settings.** The agent re-applies them at start (`ServiceInstaller.ConfigureRecovery`), but the MSI would still never create the service.

## (2) The enrollment token in the verbose log

### Evidence

From `logs\msi-install.log`:

```text
47   Command Line: REBOOT=ReallySuppress ENROLLMENT_TOKEN=********** CONTROL_PLANE_ENDPOINT=https://localhost:59735/ ...
70   PROPERTY CHANGE: Adding ENROLLMENT_TOKEN property. Its value is '**********'.
580  Executing op: RegAddValue(Name=ServiceInstalled,Value=#1,)
581  Executing op: RegAddValue(Name=EnrollmentToken,Value=***EnrollmentToken***,)
621  Executing op: RegRemoveValue(Name=EnrollmentToken,Value=***EnrollmentToken***,)
689  Property(S): MsiHiddenProperties = ENROLLMENT_TOKEN
690  Property(S): ENROLLMENT_TOKEN = **********
```

- Lines 581 and 621 show the UAT scanner's redaction. The file as msiexec wrote it held the token in plain text there.
- The command line, the property change and the final property dump are masked by msiexec itself (`**********`).
- The table dump shows `Registry row 'regnYtZYhNkZFMyux2adiEAIMwh9UA' formats [ENROLLMENT_TOKEN]`.

### Root cause (F29)

`Hidden="yes"` works: it puts the property into `MsiHiddenProperties` (line 689), and msiexec masks it wherever it logs **the property**. It does not mask a **formatted table value**.

- `EnrollmentTokenComponent` (`Package.wxs:323-330`) writes the token with `RegistryValue Value="[ENROLLMENT_TOKEN]"`.
- `WriteRegistryValues` formats that cell into the install script. The verbose log prints every script op with its arguments: `RegAddValue` on install, and the rollback twin `RegRemoveValue`.
- The same holds for any table msiexec formats into script ops: Registry, IniFile, Environment, ServiceInstall arguments, and the data of a deferred custom action without `HideTarget`.

**So the leak is in the product, not the UAT.** Every customer who installs with `ENROLLMENT_TOKEN=` and `/l*v` (which `Installer/README.md:68` and `:219` recommend) gets the token in a log file. The same is true with the machine `Logging` policy set, which many enterprises enable.

D-S229 said "`Hidden="yes"` masks it in the MSI log". That premise was false.

### Fix (D-S245, amends D-S229)

The token never enters a table that msiexec formats into a logged script op.

- **A dedicated custom action writes the token.** It is a hidden, deferred, no-impersonate DTF managed action, `WriteEnrollmentToken`, in the new project `src/Agents/Dhadgar.Agent.Windows.InstallerActions` (net462, x64 SfxCA).
  - An immediate type-51 action, `SetWriteEnrollmentToken`, copies `[ENROLLMENT_TOKEN]` into the deferred action's data. It is marked `HideTarget`.
  - The deferred action is marked `HideTarget` too, so neither its target nor its `CustomActionData` is logged.
  - WiX v5 adds the deferred action's property to `MsiHiddenProperties` automatically. The prototype's table is `ENROLLMENT_TOKEN;WriteEnrollmentToken`, so the property-change line is masked as well.
  - The action writes `HKLM\SOFTWARE\Meridian Console\Agent\EnrollmentToken` (64-bit view, REG_SZ), the same value D-S214's agent reads.
  - The action logs fixed messages only, never the value or its length. It refuses a token that is longer than 4096 characters or contains a control character (fail closed, install rolls back).
- **Rollback and uninstall delete the token.**
  - `RollbackEnrollmentToken` is a rollback action sequenced before the write. It deletes the value.
  - `DeleteEnrollmentToken` is deferred, no-impersonate and runs on `REMOVE~="ALL"`, before `RemoveRegistryValues`. It deletes an unconsumed token on uninstall. Before, `RemoveRegistryValues` did this for the Registry row.
- **The `EnrollmentToken` Registry row and `EnrollmentTokenComponent` are deleted.**
- **The key ACL moves to an unconditional component.** The protected SDDL `D:P(A;OICI;KA;;;SY)(A;OICI;KA;;;BA)` and `ForceCreateOnInstall="yes"` move from the token component's `RegistryKey` to `InstallPathComponent`'s.
  - This also fixes a latent gap. The ACL was applied only when a token was passed, so an install without a token left the agent key (endpoint, data path) with inherited ACLs.
  - The `+` create row of that key then carries the `MsiLockPermissionsEx` row in an unconditional component.
- **Sequencing.** The order is `SetWriteEnrollmentToken` → `RollbackEnrollmentToken` → `WriteEnrollmentToken`, with the write after `WriteRegistryValues` (the key exists, with its ACL). The deferred write therefore runs before `StartServices`, so the token is in place before the service first starts, as before. The three run on `ENROLLMENT_TOKEN AND NOT REMOVE~="ALL"`.
- **The interface does not change.** It is still `msiexec /i ... ENROLLMENT_TOKEN=<token>`, the same value name and the same agent code. D-S229's command-line exception stands. Only its "the MSI log masks it" premise becomes true now.

**Other sensitive properties.** Only `ENROLLMENT_TOKEN` is a secret. `CONTROL_PLANE_ENDPOINT`, `CMDLINE_CONTROL_PLANE_ENDPOINT`, `WINDOWS_BUILD_NUMBER`, `INSTALLFOLDER` and the ARP properties are public facts. No other property is hidden, and none needs to be.

**Caveats** (documented in `Installer/README.md` by P37; not defects):

- An administrator who sets the Windows Installer `Debug` machine policy to 7 asks msiexec to log values it otherwise masks, the command line included. This is an admin opt-in.
- While msiexec runs, the token is still on its command line, visible to administrators (D-S229, D-S239 R5).
- A major upgrade without a new token runs the old product's uninstall, which deletes a pending, unconsumed token. Tokens are single-use and expire within minutes, so the operator passes a fresh one.
- A failed repair that passed a new token deletes the token on rollback rather than restoring the earlier pending one. The old one would have expired.

**The run's own exposure is closed.** The leaked token was single-use with a 10-minute lifetime. The install rolled back before the agent ran, so it was never consumed. It lived only in the UAT's throwaway Postgres, which the run tore down. The scanner redacted the log file in place. Nothing needs revoking.

Rejected alternatives:

- **Dropping `/l*v` from the UAT.** It hides the leak from the test and leaves every customer's verbose log with the token. The UAT keeps `/l*v` precisely so U9 can catch this.
- **Relying on `MsiHiddenProperties` alone, or adding the Registry row's formatted value to it.** The run disproves the first. Script-op arguments are not property-masked.
- **A type-18 exe custom action, or the agent exe with a `--write-token` argument.** The token would be on another command line and in the CA target, and the exe would need its own hidden handling. It also adds an install-time entry point to the agent binary.
- **WiX util `RegistryConfig` or a C++ CA.** WiX util has no hidden registry writer. A native CA is more code to review than the 70-line DTF action, for no gain.
- **Changing the product interface** (a token file path, or a transform holding the token). That is an owner decision and is unnecessary, because the hidden CA fixes the existing interface.

## Executed checks (architect, 2026-10-27, Windows 11 build 26300, non-elevated)

[P36-msi-tables.ps1](P36-msi-tables.ps1) reads an MSI's tables from a temp copy (`OpenDatabase(copy, 0)`, read-only) and never installs. It exits 0 on pass, 1 on fail and 2 when blocked.

- `-Checks P36` covers S1-S3:
  - S1: exactly one `ServiceInstall`, named `DhadgarAgent`;
  - S2: its component's KeyPath is the File `Dhadgar.Agent.Windows.exe`, and the component is not registry-keyed;
  - S3: the `Wix4ServiceConfig` and `ServiceControl` rows are in the same component.
- `-Checks P37` covers T1-T6:
  - T1: the token is in `MsiHiddenProperties` and `SecureCustomProperties`;
  - T2: no cell formats a hidden property, except a `HideTarget` CustomAction target;
  - T3: no Registry row named `EnrollmentToken`;
  - T4: the setter and the deferred action are hidden, and the deferred action is deferred and no-impersonate;
  - T5: the sequencing and conditions above;
  - T6: an unconditional component creates the agent key with the protected SDDL.
- `-Checks All` runs both.

**The run's MSI fails** (`...\20261027-192738-7347\msi\MeridianConsoleAgent.msi`, `-Checks All`, exit 1):

```text
FAIL: S2: the component of ServiceInstall DhadgarAgent (ServiceComponent) has key path 'reg0Ft74ZmbcuV8ZvWyP9v_pcGBWa8', not the File Dhadgar.Agent.Windows.exe; Windows Installer skips the service install
FAIL: T2: Registry row 'regnYtZYhNkZFMyux2adiEAIMwh9UA' formats [ENROLLMENT_TOKEN]; the verbose log writes it in plain text
FAIL: T3: Registry row regnYtZYhNkZFMyux2adiEAIMwh9UA writes EnrollmentToken through the table
FAIL: T5: WriteEnrollmentToken is not defined and sequenced
FAIL: T5: RollbackEnrollmentToken is not defined and sequenced
FAIL: T5: DeleteEnrollmentToken is not defined and sequenced
FAIL: T6: no unconditional component creates SOFTWARE\Meridian Console\Agent with the protected SDDL
failures = 7
```

With `-Checks P36` it fails with exactly the S2 line, `failures = 1`, exit 1.

**A scratch prototype passes.** It is a WiX v5.0.2 package built with the repo's `Directory.Build.props` and `Directory.Packages.props` under `CI=true`, with 0 warnings. Its shapes are exactly the ones P36 and P37 specify (the real CA project at net462, the wxs elements, the wixproj reference). `-Checks All` passes with exit 0:

```text
ServiceInstall DhadgarAgent -> component MainExecutableComponent, KeyPath 'MainExecutable', Attributes 256
Wix4ServiceConfig DhadgarAgent -> component MainExecutableComponent
ServiceControl DhadgarAgent -> component MainExecutableComponent
MsiHiddenProperties = ENROLLMENT_TOKEN;WriteEnrollmentToken
cell formats [ENROLLMENT_TOKEN]: CustomAction row 'SetWriteEnrollmentToken' allowed=True
deferred WriteEnrollmentToken Type 11265
WriteRegistryValues 5000, RollbackEnrollmentToken 5002, WriteEnrollmentToken 5003, InstallFinalize 6600
agent key create row regxpKXtnZdDAV30YM6M84CgDlv3uo in InstallPathComponent (Condition ''), locks 1
failures = 0
```

- The SfxCA `Dhadgar.Agent.Windows.InstallerActions.CA.dll` is PE machine `8664` (x64).
- Type 11265 is 1 (dll) + 1024 (deferred) + 2048 (no-impersonate) + 8192 (HideTarget). The setter is 8243 (51 + 8192).

**Build facts found on the way** (they go into P37 as rules):

- **The SfxCA platform comes from MSBuild `Platform`.** `Platform=x64` gives the x64 SfxCA. The managed assembly must stay `PlatformTarget=AnyCPU`, because MakeSfxCA cannot load an x64-only assembly.
- **MakeSfxCA fails on paths of 260 characters or more** with a misleading "Could not load file or assembly". The p37 worktree's longest CA path is 207 characters.
- **The lock file must not depend on the machine.** Without an explicit `Microsoft.NETFramework.ReferenceAssemblies`, the CA's lock file differs between machines with and without the .NET Framework targeting pack. With it (1.0.3, CPM), the lock file is deterministic: `.NETFramework,Version=v4.6.2` with `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3, `...net462` 1.0.3, `WixToolset.Dtf.CustomAction` 5.0.2 and `WixToolset.Dtf.WindowsInstaller` 5.0.2.
- **The installer's tracked lock file does not change.** A wixproj lock file lists packages only, not project references.
- **Why net462, not net472.** .NET Framework 4.6.2 is in-box on Windows 10 1607 and later and on Server 2016, the oldest Windows 10 releases still supported. 4.7.2 is in-box only from 1803. The CA uses nothing newer than 4.0 APIs.

**What the tables cannot show** is msiexec's runtime log. `HideTarget` and `MsiHiddenProperties` masking are documented Windows Installer behaviour, and the prototype has the bits. The proof is the Msi rerun's U9 scan, as for P34's server-side value.

## (3) Effect on the UAT

- **Process and Service legs are unaffected.** P36 and P37 change only `Installer/`, a new installer-only project, `Directory.Packages.props` (two entries no sln project uses) and tests. The agent binary is byte-for-byte the same source. The campaign review's diff rule from the P34 ruling (§ Effect) extends to these two packets: `6530253..<new tip>` restricted to `src/`, minus `src/Agents/Dhadgar.Agent.Windows/Installer/` and `src/Agents/Dhadgar.Agent.Windows.InstallerActions/`, must be empty.
- **Rerun `-Mode Msi` only, after both P36 and P37 merge.** Do not rerun after P36 alone: that MSI installs and would write a live token into the log again.
- **The Msi leg needs no new assertion.** Each defect is already caught:
  - U11a's exit 0 requires `ExecServiceConfig` to succeed. Its "service exists as LocalSystem, AUTO_START, RUNNING" check proves `InstallServices` ran.
  - U3 enrolls with the token the CA wrote. A CA that wrote the wrong key, the wrong view or nothing fails U3. This is the installer-agent seam test for D-S245.
  - U11b checks that `EnrollmentToken` is gone after uninstall: consumed by the agent, or deleted by `DeleteEnrollmentToken`.
  - U9 value-scans `msi-install.log` and `msi-uninstall.log`, UTF-16 included. It caught this leak and fails on any return of it.
- **What the runner reports from the rerun's `logs\msi-install.log`** (as data, read-only):
  - `Property(S): WINDOWS_BUILD_NUMBER = 26300`;
  - a `ServiceInstall(` script op;
  - `WriteEnrollmentToken: token written.`;
  - no `RegAddValue(Name=EnrollmentToken`.

  The orchestrator checks these when it reads the result. They are not new driver assertions.

## Decisions

| ID | Decision | Reason |
| --- | --- | --- |
| D-S244 | **The service is installed from the component whose key path is the agent exe.** `ServiceInstall` (with `util:ServiceConfig`) and `ServiceControl` live in `MainExecutableComponent` (KeyPath `File MainExecutable`); the `ServiceInstalled` value moves there without `KeyPath`; `ServiceComponent` is deleted. `InstallerPackageTests` pins that the `ServiceInstall`'s component has exactly one key path, the File named `<AssemblyName>.exe`, and holds the `ServiceControl` and `util:ServiceConfig`. | Fixes F28. Windows Installer skips a `ServiceInstall` row whose component is registry-keyed, so `Wix4ExecServiceConfig` failed with 0x80070424 and every MSI install rolled back (Msi run `20261027-192738-7347`). Verified on a scratch MSI with `P36-msi-tables.ps1`. Built by **P36**. |
| D-S245 | **The enrollment token never enters a table msiexec formats into the log** (amends D-S229). A hidden immediate setter and a hidden, deferred, no-impersonate DTF action (`Dhadgar.Agent.Windows.InstallerActions`, net462, x64 SfxCA) write `HKLM\SOFTWARE\Meridian Console\Agent\EnrollmentToken`. A rollback action and an uninstall action delete it. No Registry row names the token. The agent key's protected SDDL moves to the unconditional `InstallPathComponent`. Only `ENROLLMENT_TOKEN` is secret. The interface (`ENROLLMENT_TOKEN=` on the command line) is unchanged. | Fixes F29. `Hidden="yes"` masks the property but not formatted Registry-table values, so `/l*v` wrote the token at `RegAddValue` and `RegRemoveValue`. The fix is in the product, because customers' logs matter. Verified on a scratch MSI (`P36-msi-tables.ps1` T1-T6, exit 0); runtime proof is the Msi rerun's U9. Built by **P37**. |

No BlockedDecision.

## Packets

- **Order.** P36 and P37 share `Package.wxs`, `InstallerPackageTests.cs` and `Installer/README.md`, so **P37 follows P36**.
  - P36 branches from the w1 tip, `1159f36` when this was written. P38 merged after the run's `321ae59` and touches only `tests/Dhadgar.Agent.Core.Tests/Hosting/AgentBootstrapServiceTests.cs`, so it is disjoint from both packets.
  - P37 branches from the w1 tip **after P36 merges**. If ops already created `sprint2-agent-nodes-p37` earlier, it fast-forwards that worktree's branch to the new w1 tip before the coder starts (`git -C <p37> merge --ff-only campaign/sprint2-agent-nodes-w1`).
- **Security review.** Both packets need one: the installer runs elevated on customer hardware, and P37 handles the enrollment token and adds a custom action (CLAUDE.md: "All custom actions must be reviewed").
- **No Msi rerun between the two** (§ 3).

### Inline rules for both packets

| Key | Rule |
| --- | --- |
| R-RESULT | Product C# returns `Result`/`Result<T>` and never throws for I/O. The DTF action is the exception the framework forces: it returns `ActionResult` and catches only the I/O exception types shown. Never `Result<T>.Success(null)`. |
| R-USING | `using` on every `IDisposable` (`RegistryKey`). |
| R-CPM | No `Version=` in any `.csproj` or `.wixproj`. P37 adds exactly the two `PackageVersion` entries it names to `Directory.Packages.props`, and nothing else. |
| R-NULL | Nullable on. Build with `-p:TreatWarningsAsErrors=true`. |
| R-SECRET | Never log, format or put in an exception message the token, any part of it, or its length. In WiX, a hidden property is never formatted into a Registry, IniFile, Environment, ServiceInstall or deferred-CA-without-`HideTarget` cell. |
| R-XUNIT | xUnit v2. Tests that read sources are plain `[Fact]`/`[Theory]` (they run on Linux CI). Names follow `Subject_Condition_Expectation`. |
| R-UAT | **No machine-changing run.** Never run `msiexec` (not even `/a`), never run the UAT wrapper, never pass `-AcceptMachineChanges` or `-CleanupOnly`, never write HKLM. Build the MSI only with `scripts\uat\Build-AgentWindowsUat.ps1`, never a bare `dotnet publish -r` (D-S241). If `git status` shows a dirty tracked lock file after a check, restore it with `git checkout -- <file>` and report it. |
| R-SCOPE | Edit only the files your packet names. Anything else is `blocked spec`. |
| R-DOCS | Doc tables have spaces around pipes, and every code block names its language. |

### P36: install the service from the agent exe's component (fix packet)

- **Branch** `s2/p36-msi-service-keypath`. **Worktree** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p36`, from the w1 tip (`1159f36`).
- **Commit subject** `fix(agent-windows): install the service from the agent exe's component so ExecServiceConfig finds it (P36)`
- **Security review** yes (installer; service account and recovery unchanged, the reviewer confirms that).
- **Files (only these):**
  - `src/Agents/Dhadgar.Agent.Windows/Installer/Package.wxs`
  - `tests/Dhadgar.Agent.Windows.Tests/Installation/InstallerPackageTests.cs`
  - `src/Agents/Dhadgar.Agent.Windows/Installer/README.md`

#### Step 1: `Package.wxs`

**(a)** Replace `MainExecutableComponent` (lines 160-166, the comment and the component) with the shape below. Move the existing `ServiceInstall` element, its LocalSystem comment, its `util:ServiceConfig` with its "WIX LIMITATION" comment, and the `ServiceControl` element **verbatim** from `ServiceComponent`. Do not change one attribute.

```xml
      <!--
        Main executable - self-contained single file. The service elements live here because Windows Installer
        installs a ServiceInstall row only when its component's key path is the service's executable; in a
        registry-keyed component InstallServices skips it and Wix4ExecServiceConfig fails with 0x80070424 (D-S244).
      -->
      <Component Id="MainExecutableComponent" Guid="A1B2C3D4-E5F6-7890-ABCD-EF1234567890">
        <File Id="MainExecutable"
              Name="Dhadgar.Agent.Windows.exe"
              Source="Dhadgar.Agent.Windows.exe"
              KeyPath="yes" />

        <!-- (the existing "Service installation with recovery configuration. SECURITY: Runs as LocalSystem ..." comment) -->
        <ServiceInstall Id="AgentServiceInstall" ...>   <!-- moved verbatim, with its util:ServiceConfig child -->
        </ServiceInstall>

        <!-- Service control during install/uninstall -->
        <ServiceControl Id="AgentServiceControl" ... />   <!-- moved verbatim -->

        <!-- Install marker read by the UAT; not a key path (the exe is) -->
        <RegistryKey Root="HKLM" Key="SOFTWARE\Meridian Console\Agent">
          <RegistryValue Name="ServiceInstalled" Type="integer" Value="1" />
        </RegistryKey>
      </Component>
```

**(b)** Delete the whole "Windows Service installation" fragment (lines 233-298, `<!-- Windows Service installation -->` through its `</Fragment>`). Delete `<ComponentRef Id="ServiceComponent" />` (line 121).

**(c)** Change nothing else. Leave the token component, the GUIDs and the launch conditions alone; P37 owns the token.

#### Step 2: `InstallerPackageTests.cs`

Add this test after `PackageWxs_ServiceElements_UseProgramServiceName`:

```csharp
    [Fact]
    public void PackageWxs_ServiceInstall_IsInTheComponentWhoseKeyPathIsTheAgentExe()
    {
        var doc = LoadPackage();
        var exeName = LoadAgentCsproj().Descendants("AssemblyName").Single().Value + ".exe";
        var install = doc.Descendants(Wix + "ServiceInstall").Single();
        var component = install.Parent;

        // D-S244: Windows Installer installs a ServiceInstall row only from a component keyed by the service exe.
        Assert.NotNull(component);
        Assert.Equal(Wix + "Component", component.Name);
        Assert.Null(component.Attribute("Condition"));
        var keyPath = Assert.Single(component.Descendants(), e => (string?)e.Attribute("KeyPath") == "yes");
        Assert.Equal(Wix + "File", keyPath.Name);
        Assert.Equal(exeName, (string?)keyPath.Attribute("Name"));
        Assert.Same(component, doc.Descendants(Wix + "ServiceControl").Single().Parent);
        Assert.Single(install.Elements(Util + "ServiceConfig"));
    }
```

**Fails on revert.** With the old wxs, the component is `ServiceComponent`, whose only key path is a `RegistryValue`, so `Assert.Equal(Wix + "File", ...)` fails. With the File's `KeyPath` removed, `Assert.Single` fails. With `ServiceControl` left behind in another component, `Assert.Same` fails.

`PackageWxs_HklmRegistryKeys_UseAgentRegistryKeyPath` keeps passing: the new `RegistryKey` uses `AgentRegistry.KeyPath`. If it does not, stop with `blocked spec`.

#### Step 3: `Installer/README.md`

Under "### Windows Service", after the "Recovery" bullet, add:

> The service is installed by the same component as `Dhadgar.Agent.Windows.exe`: Windows Installer installs a service only from the component whose key path is the service's executable (D-S244).

#### Checks, in order (Windows, non-elevated)

1. `dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p36\tests\Dhadgar.Agent.Windows.Tests\Dhadgar.Agent.Windows.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`
2. `dotnet test <same csproj> -c Release --no-build --filter "FullyQualifiedName~InstallerPackageTests"`, then the same command without `--filter`.
3. **Source mutation.** Run `git -C <worktree> stash push -m p36-mutation -- src/Agents/Dhadgar.Agent.Windows/Installer/Package.wxs`, then step 2's filtered test. It must fail **only** `PackageWxs_ServiceInstall_IsInTheComponentWhoseKeyPathIsTheAgentExe`. The tests read the source at run time, so no rebuild is needed. Then run `git -C <worktree> stash pop` and confirm `git diff --stat` shows your three files.
4. **Build the MSI.** `pwsh -NoProfile -File C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p36\scripts\uat\Build-AgentWindowsUat.ps1 -Verb run -Mode Msi` must exit 0 with no ICE error. Then run `git -C <worktree> status --porcelain=v1 --untracked-files=all`: only your three files.
5. **Executed check.** `pwsh -NoProfile -File C:\Users\Steve\source\projects\MeridianConsole\docs\analysis\sprint2-agent-nodes\worknotes\P36-msi-tables.ps1 -MsiPath <worktree>\src\Agents\Dhadgar.Agent.Windows\Installer\bin\Release\MeridianConsoleAgent.msi -Checks P36`
   - It must exit 0, with `ServiceInstall DhadgarAgent -> component MainExecutableComponent, KeyPath 'MainExecutable'`.
   - Then run it once with `-Checks All`. The expected result is exit 1 with only T-lines (P37's work). Paste it as information.
6. **Executed mutation.** Run the same script with `-Checks P36` against the failed run's MSI, `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-w1\.uat\agent-windows\20261027-192738-7347\msi\MeridianConsoleAgent.msi`. It reads a temp copy. It must exit 1 with exactly the S2 `FAIL:` line quoted in this ruling.
7. `git status` clean apart from the commit. Commit locally only.

Paste the output of steps 3-6 into the report. If the MSI is not at that path, find it with Glob under `Installer\bin` and report the real path. Exit 2 from the script means `blocked`: quote the output.

**Owed to the UAT run:** none on its own. The Msi rerun waits for P37 (§ 3).

**Docs owed:** step 3 only.

### P37: write the enrollment token through a hidden custom action (fix packet)

- **Branch** `s2/p37-msi-token-hidden-ca`. **Worktree** `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p37`, from the w1 tip **after P36 merges**.
- **Commit subject** `fix(agent-windows): write the enrollment token through a hidden custom action so no MSI log records it (P37)`
- **Security review** yes (a new elevated custom action that handles the enrollment token; the agent key ACL).
- **Files (only these):**
  - new folder `src/Agents/Dhadgar.Agent.Windows.InstallerActions/`:
    - `Dhadgar.Agent.Windows.InstallerActions.csproj`
    - `CustomAction.config`
    - `EnrollmentTokenData.cs`
    - `EnrollmentTokenActions.cs`
    - `packages.lock.json` (generated in step 1e)
  - `Directory.Packages.props`
  - `src/Agents/Dhadgar.Agent.Windows/Installer/Dhadgar.Agent.Windows.Installer.wixproj`
  - `src/Agents/Dhadgar.Agent.Windows/Installer/Package.wxs`
  - `tests/Dhadgar.Agent.Windows.Tests/Dhadgar.Agent.Windows.Tests.csproj`
  - `tests/Dhadgar.Agent.Windows.Tests/Installation/InstallerPackageTests.cs`
  - new `tests/Dhadgar.Agent.Windows.Tests/Installation/EnrollmentTokenDataTests.cs`
  - `src/Agents/Dhadgar.Agent.Windows/Installer/README.md`
  - `scripts/uat/README.md`
- **Must not change:**
  - `Dhadgar.sln`: the CA project stays out of it, like the wixproj. It is net462 and Windows-only; CI compiles its `EnrollmentTokenData.cs` through the test project's link.
  - The agent's own code (`Installation/AgentRegistry.cs`, the token source, the token cleanup).
  - `scripts/uat/Build-AgentWindowsUat.ps1`: the wixproj's locked restore covers the new project reference.
  - Any other lock file.
- **Why the project sits in `src/Agents/`:** the agent csproj compiles every `.cs` under its own folder, `Installer/` included, so the CA must be a sibling, not a child.

#### Step 1: the custom action project

**(a)** `src/Agents/Dhadgar.Agent.Windows.InstallerActions/Dhadgar.Agent.Windows.InstallerActions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!--
    DTF managed custom actions for the agent MSI (D-S245). Built only through the installer's ProjectReference
    (scripts\uat\Build-AgentWindowsUat.ps1); not in Dhadgar.sln. net462 is in-box on every supported Windows 10/Server.
    Platform x64 makes MakeSfxCA wrap the action in the x64 SfxCA host; the managed assembly itself must stay AnyCPU
    (MakeSfxCA cannot load an x64-only assembly). MakeSfxCA fails on paths of 260 characters or more.
  -->
  <PropertyGroup>
    <TargetFramework>net462</TargetFramework>
    <Platform>x64</Platform>
    <Platforms>x64</Platforms>
    <PlatformTarget>AnyCPU</PlatformTarget>
    <ImplicitUsings>disable</ImplicitUsings>
    <AnalysisMode>All</AnalysisMode>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="WixToolset.Dtf.CustomAction" />
    <!-- Explicit, so the lock file does not depend on whether the machine has the 4.6.2 targeting pack -->
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="CustomAction.config" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

**(b)** `CustomAction.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0" />
  </startup>
</configuration>
```

**(c)** `EnrollmentTokenData.cs`. It has no dependencies, because the test project links it:

```csharp
namespace Dhadgar.Agent.Windows.InstallerActions;

/// <summary>
/// Where the installer writes the enrollment token, and what it accepts (D-S245). Dependency-free, so
/// Dhadgar.Agent.Windows.Tests links this file and pins it against AgentRegistry (the installer-agent seam).
/// </summary>
internal static class EnrollmentTokenData
{
    /// <summary>The agent key under HKLM (64-bit view). Must equal AgentRegistry.KeyPath.</summary>
    internal const string KeyPath = @"SOFTWARE\Meridian Console\Agent";

    /// <summary>The token value name. Must equal AgentRegistry.EnrollmentTokenValueName.</summary>
    internal const string ValueName = "EnrollmentToken";

    /// <summary>Upper bound on the token length the installer writes.</summary>
    internal const int MaxLength = 4096;

    /// <summary>True when the token is non-empty, at most <see cref="MaxLength"/> characters and has no control characters.</summary>
    internal static bool IsAcceptable(string? token)
    {
        if (token is null || token.Length == 0 || token.Length > MaxLength)
        {
            return false;
        }

        foreach (var c in token)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }
}
```

**(d)** `EnrollmentTokenActions.cs`:

```csharp
using System;
using System.IO;
using System.Security;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace Dhadgar.Agent.Windows.InstallerActions;

/// <summary>
/// Writes and deletes the enrollment token outside the MSI Registry table, so no MSI log records it (D-S245).
/// Never log the token, its length or any part of it.
/// </summary>
public static class EnrollmentTokenActions
{
    /// <summary>Deferred, no-impersonate, HideTarget. CustomActionData is the raw token.</summary>
    [CustomAction]
    public static ActionResult WriteEnrollmentToken(Session session)
    {
        if (session is null)
        {
            return ActionResult.Failure;
        }

        var token = session["CustomActionData"];
        if (token is null || token.Length == 0)
        {
            session.Log("WriteEnrollmentToken: no token supplied; nothing written.");
            return ActionResult.Success;
        }

        if (!EnrollmentTokenData.IsAcceptable(token))
        {
            session.Log("WriteEnrollmentToken: the token is malformed; nothing written.");
            return ActionResult.Failure;
        }

        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(EnrollmentTokenData.KeyPath, writable: true);
            if (key is null)
            {
                session.Log("WriteEnrollmentToken: the agent key is missing.");
                return ActionResult.Failure;
            }

            key.SetValue(EnrollmentTokenData.ValueName, token, RegistryValueKind.String);
            session.Log("WriteEnrollmentToken: token written.");
            return ActionResult.Success;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            session.Log("WriteEnrollmentToken: write failed: " + ex.GetType().Name);
            return ActionResult.Failure;
        }
    }

    /// <summary>Deferred (uninstall) and rollback: deletes the value. A missing key or value is success.</summary>
    [CustomAction]
    public static ActionResult DeleteEnrollmentToken(Session session)
    {
        if (session is null)
        {
            return ActionResult.Failure;
        }

        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(EnrollmentTokenData.KeyPath, writable: true);
            key?.DeleteValue(EnrollmentTokenData.ValueName, throwOnMissingValue: false);
            return ActionResult.Success;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            session.Log("DeleteEnrollmentToken: delete failed: " + ex.GetType().Name);
            return ActionResult.Failure;
        }
    }
}
```

The architect built this exact code (all four files) in the scratch prototype under the repo's `Directory.Build.props` with `CI=true`: 0 warnings, 0 errors.

- Write `token is null || token.Length == 0`, not `string.IsNullOrEmpty`, because net462's reference assemblies carry no nullable flow annotations.
- If the analyzers still raise a warning in the worktree, fix the code, not `NoWarn`. If you cannot fix it, stop with `blocked spec`.

**(e)** Add to `Directory.Packages.props`, directly after the `WixToolset.Util.wixext` line (143):

```xml
    <PackageVersion Include="WixToolset.Dtf.CustomAction" Version="5.0.2" />
    <PackageVersion Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" />
```

Then generate the lock file with a plain RID-less restore, `dotnet restore C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p37\src\Agents\Dhadgar.Agent.Windows.InstallerActions\Dhadgar.Agent.Windows.InstallerActions.csproj`. Commit the resulting `packages.lock.json`. It must hold one section, `.NETFramework,Version=v4.6.2`, with exactly these packages:

- `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3 (Direct);
- `WixToolset.Dtf.CustomAction` 5.0.2 (Direct);
- `Microsoft.NETFramework.ReferenceAssemblies.net462` 1.0.3 (Transitive);
- `WixToolset.Dtf.WindowsInstaller` 5.0.2 (Transitive).

Anything else is `blocked spec`.

#### Step 2: the wixproj

In `Dhadgar.Agent.Windows.Installer.wixproj`, add a second `ProjectReference` in the existing build-dependency `ItemGroup` (after the agent reference), with a one-line comment:

```xml
    <!-- DTF custom actions (D-S245); WiX defines $(Dhadgar.Agent.Windows.InstallerActions.TargetDir) for the Binary -->
    <ProjectReference Include="..\..\Dhadgar.Agent.Windows.InstallerActions\Dhadgar.Agent.Windows.InstallerActions.csproj" />
```

The path is relative to `src/Agents/Dhadgar.Agent.Windows/Installer/`. Verify that it resolves before building.

#### Step 3: `Package.wxs`

**(a)** In `<Package>`, directly after the `Privileged` launch condition, add:

```xml
    <!--
      Enrollment token (D-S245). Hidden="yes" masks the property, but NOT a formatted table value: a Registry row
      with [ENROLLMENT_TOKEN] puts the token in every /l*v log (RegAddValue/RegRemoveValue script ops). So the token
      reaches HKLM only through these actions: a HideTarget setter copies it into the hidden data of a HideTarget,
      deferred, no-impersonate DTF action. Rollback and uninstall delete the value. Never add a Registry, IniFile or
      Environment row, or a deferred action without HideTarget, that formats ENROLLMENT_TOKEN.
    -->
    <Binary Id="AgentInstallerActions"
            SourceFile="$(Dhadgar.Agent.Windows.InstallerActions.TargetDir)Dhadgar.Agent.Windows.InstallerActions.CA.dll" />
    <CustomAction Id="SetWriteEnrollmentToken" Property="WriteEnrollmentToken" Value="[ENROLLMENT_TOKEN]"
                  HideTarget="yes" />
    <CustomAction Id="WriteEnrollmentToken" BinaryRef="AgentInstallerActions" DllEntry="WriteEnrollmentToken"
                  Execute="deferred" Impersonate="no" Return="check" HideTarget="yes" />
    <CustomAction Id="RollbackEnrollmentToken" BinaryRef="AgentInstallerActions" DllEntry="DeleteEnrollmentToken"
                  Execute="rollback" Impersonate="no" Return="ignore" />
    <CustomAction Id="DeleteEnrollmentToken" BinaryRef="AgentInstallerActions" DllEntry="DeleteEnrollmentToken"
                  Execute="deferred" Impersonate="no" Return="ignore" />
    <InstallExecuteSequence>
      <Custom Action="SetWriteEnrollmentToken" Before="RollbackEnrollmentToken"
              Condition="ENROLLMENT_TOKEN AND NOT REMOVE~=&quot;ALL&quot;" />
      <Custom Action="RollbackEnrollmentToken" Before="WriteEnrollmentToken"
              Condition="ENROLLMENT_TOKEN AND NOT REMOVE~=&quot;ALL&quot;" />
      <Custom Action="WriteEnrollmentToken" After="WriteRegistryValues"
              Condition="ENROLLMENT_TOKEN AND NOT REMOVE~=&quot;ALL&quot;" />
      <Custom Action="DeleteEnrollmentToken" Before="RemoveRegistryValues" Condition="REMOVE~=&quot;ALL&quot;" />
    </InstallExecuteSequence>
```

**(b)** Replace `InstallPathComponent`'s `RegistryKey` (lines 224-227 before P36; find it by `Id="InstallPathComponent"`) with:

```xml
        <!-- The agent key's ACL: SYSTEM and Administrators only, protected from inheritance (MsiLockPermissionsEx).
             Unconditional, so the key is protected whether or not a token is passed (D-S245). -->
        <RegistryKey Root="HKLM" Key="SOFTWARE\Meridian Console\Agent" ForceCreateOnInstall="yes">
          <RegistryValue Name="InstallPath" Type="string" Value="[INSTALLFOLDER]" KeyPath="yes" />
          <RegistryValue Name="Version" Type="string" Value="[ProductVersion]" />
          <PermissionEx Sddl="D:P(A;OICI;KA;;;SY)(A;OICI;KA;;;BA)" />
        </RegistryKey>
```

**(c)** Delete the "Enrollment Token Component" fragment (`<!-- Enrollment Token Component -->` through its `</Fragment>`), and delete `<ComponentRef Id="EnrollmentTokenComponent" />`.

**(d)** In the header comment, replace "EnrollmentToken is stored in HKLM under that ACL." with: "EnrollmentToken is stored in HKLM under that ACL, written only by the hidden deferred custom action WriteEnrollmentToken (D-S245), never through the Registry table."

**(e)** Leave `<Property Id="ENROLLMENT_TOKEN" Secure="yes" Hidden="yes" />` as it is.

#### Step 4: tests

**(a)** In `Dhadgar.Agent.Windows.Tests.csproj`, add an `ItemGroup` that links the dependency-free file:

```xml
  <ItemGroup>
    <!-- Installer custom action <-> agent seam (D-S245): the CA's key and value name are pinned against AgentRegistry -->
    <Compile Include="..\..\src\Agents\Dhadgar.Agent.Windows.InstallerActions\EnrollmentTokenData.cs"
             Link="Installation\EnrollmentTokenData.cs" />
  </ItemGroup>
```

**(b)** New `tests/Dhadgar.Agent.Windows.Tests/Installation/EnrollmentTokenDataTests.cs`:

```csharp
using Dhadgar.Agent.Windows.Installation;
using Dhadgar.Agent.Windows.InstallerActions;
using Xunit;

namespace Dhadgar.Agent.Windows.Tests.Installation;

/// <summary>
/// The installer custom action writes where the agent reads (D-S214, D-S245). The CA's file is linked into this
/// project, so this compiles and runs on Linux CI. The live seam is the UAT Msi leg's U3.
/// </summary>
public sealed class EnrollmentTokenDataTests
{
    [Fact]
    public void KeyAndValueName_MatchAgentRegistry()
    {
        Assert.Equal(AgentRegistry.KeyPath, EnrollmentTokenData.KeyPath);
        Assert.Equal(AgentRegistry.EnrollmentTokenValueName, EnrollmentTokenData.ValueName);
    }

    [Theory]
    [InlineData("abcDEF123-_.~")]
    [InlineData("x")]
    public void IsAcceptable_PrintableToken_IsTrue(string token) =>
        Assert.True(EnrollmentTokenData.IsAcceptable(token));

    [Fact]
    public void IsAcceptable_TokenAtMaxLength_IsTrue() =>
        Assert.True(EnrollmentTokenData.IsAcceptable(new string('a', EnrollmentTokenData.MaxLength)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsAcceptable_NullOrEmpty_IsFalse(string? token) =>
        Assert.False(EnrollmentTokenData.IsAcceptable(token));

    [Fact]
    public void IsAcceptable_TokenOverMaxLength_IsFalse() =>
        Assert.False(EnrollmentTokenData.IsAcceptable(new string('a', EnrollmentTokenData.MaxLength + 1)));

    [Theory]
    [InlineData("abc\ndef")]
    [InlineData("abc\0def")]
    [InlineData("\tabc")]
    public void IsAcceptable_ControlCharacter_IsFalse(string token) =>
        Assert.False(EnrollmentTokenData.IsAcceptable(token));
}
```

**(c)** In `InstallerPackageTests.cs`:

- **Delete** `PackageWxs_TokenValue_UsesEnrollmentTokenValueName` (its Registry row is gone; the value name is now pinned by `EnrollmentTokenDataTests`).
- **Delete** `PackageWxs_TokenKey_HasProtectedSddl` (replaced by the key test below).
- **Keep** `PackageWxs_TokenProperty_IsSecureAndHidden`.
- **Add** the members below. Add `using System.Text.RegularExpressions;` at the top.

```csharp
    private const string TokenInstallCondition = "ENROLLMENT_TOKEN AND NOT REMOVE~=\"ALL\"";

    private static XElement CustomAction(XDocument doc, string id) =>
        doc.Descendants(Wix + "CustomAction").Single(a => (string?)a.Attribute("Id") == id);

    private static XElement Custom(XDocument doc, string action) =>
        doc.Descendants(Wix + "Custom").Single(c => (string?)c.Attribute("Action") == action);

    [Fact]
    public void PackageWxs_EnrollmentToken_IsFormattedOnlyIntoTheHiddenSetter()
    {
        // F29/D-S245: Hidden="yes" does not mask a formatted Registry-table value; /l*v logged the token.
        var doc = LoadPackage();
        var use = Assert.Single(doc.Descendants().Attributes(),
            a => a.Value.Contains("[ENROLLMENT_TOKEN]", System.StringComparison.Ordinal));
        var setter = use.Parent!;

        Assert.Equal("Value", use.Name.LocalName);
        Assert.Equal(Wix + "CustomAction", setter.Name);
        Assert.Equal("SetWriteEnrollmentToken", (string?)setter.Attribute("Id"));
        Assert.Equal("WriteEnrollmentToken", (string?)setter.Attribute("Property"));
        Assert.Equal("yes", (string?)setter.Attribute("HideTarget"));
        Assert.DoesNotContain(doc.Descendants(Wix + "RegistryValue"),
            v => (string?)v.Attribute("Name") == AgentRegistry.EnrollmentTokenValueName);
    }

    [Fact]
    public void PackageWxs_TokenActions_AreHiddenDeferredAndRunAsSystem()
    {
        var doc = LoadPackage();
        var write = CustomAction(doc, "WriteEnrollmentToken");
        var rollback = CustomAction(doc, "RollbackEnrollmentToken");
        var delete = CustomAction(doc, "DeleteEnrollmentToken");

        Assert.Equal("AgentInstallerActions", (string?)write.Attribute("BinaryRef"));
        Assert.Equal("WriteEnrollmentToken", (string?)write.Attribute("DllEntry"));
        Assert.Equal("deferred", (string?)write.Attribute("Execute"));
        Assert.Equal("no", (string?)write.Attribute("Impersonate"));
        Assert.Equal("check", (string?)write.Attribute("Return"));
        Assert.Equal("yes", (string?)write.Attribute("HideTarget"));
        Assert.Equal("DeleteEnrollmentToken", (string?)rollback.Attribute("DllEntry"));
        Assert.Equal("rollback", (string?)rollback.Attribute("Execute"));
        Assert.Equal("no", (string?)rollback.Attribute("Impersonate"));
        Assert.Equal("DeleteEnrollmentToken", (string?)delete.Attribute("DllEntry"));
        Assert.Equal("deferred", (string?)delete.Attribute("Execute"));
        Assert.Equal("no", (string?)delete.Attribute("Impersonate"));
    }

    [Fact]
    public void PackageWxs_TokenActions_AreSequencedAroundTheRegistryWrite()
    {
        var doc = LoadPackage();

        Assert.Equal("RollbackEnrollmentToken", (string?)Custom(doc, "SetWriteEnrollmentToken").Attribute("Before"));
        Assert.Equal("WriteEnrollmentToken", (string?)Custom(doc, "RollbackEnrollmentToken").Attribute("Before"));
        Assert.Equal("WriteRegistryValues", (string?)Custom(doc, "WriteEnrollmentToken").Attribute("After"));
        Assert.Equal("RemoveRegistryValues", (string?)Custom(doc, "DeleteEnrollmentToken").Attribute("Before"));
        foreach (var id in new[] { "SetWriteEnrollmentToken", "RollbackEnrollmentToken", "WriteEnrollmentToken" })
        {
            Assert.Equal(TokenInstallCondition, (string?)Custom(doc, id).Attribute("Condition"));
        }

        Assert.Equal("REMOVE~=\"ALL\"", (string?)Custom(doc, "DeleteEnrollmentToken").Attribute("Condition"));
    }

    [Fact]
    public void PackageWxs_AgentKey_IsProtectedByAnUnconditionalComponent()
    {
        var doc = LoadPackage();
        var key = Assert.Single(doc.Descendants(Wix + "RegistryKey"), k => k.Elements(Wix + "PermissionEx").Any());
        var component = key.Parent;
        var sddl = (string?)key.Elements(Wix + "PermissionEx").Single().Attribute("Sddl");

        // D-S245: the ACL sat on the token component, so an install without a token left the key unprotected.
        Assert.Equal(AgentRegistry.KeyPath, (string?)key.Attribute("Key"));
        Assert.Equal("yes", (string?)key.Attribute("ForceCreateOnInstall"));
        Assert.Equal("D:P(A;OICI;KA;;;SY)(A;OICI;KA;;;BA)", sddl);
        Assert.NotNull(component);
        Assert.Equal("InstallPathComponent", (string?)component.Attribute("Id"));
        Assert.Null(component.Attribute("Condition"));
    }

    [Fact]
    public void Installer_BindsTheInstallerActionsSfxDll()
    {
        var binary = LoadPackage().Descendants(Wix + "Binary")
            .Single(b => (string?)b.Attribute("Id") == "AgentInstallerActions");
        var csproj = XDocument.Load(Path.Combine(RepoRoot(), "src", "Agents", "Dhadgar.Agent.Windows.InstallerActions",
            "Dhadgar.Agent.Windows.InstallerActions.csproj"));

        Assert.Equal("$(Dhadgar.Agent.Windows.InstallerActions.TargetDir)Dhadgar.Agent.Windows.InstallerActions.CA.dll",
            (string?)binary.Attribute("SourceFile"));
        Assert.Single(LoadWixproj().Descendants("ProjectReference"),
            r => ((string?)r.Attribute("Include") ?? string.Empty)
                .EndsWith(@"\Dhadgar.Agent.Windows.InstallerActions\Dhadgar.Agent.Windows.InstallerActions.csproj",
                    System.StringComparison.Ordinal));
        // MakeSfxCA: the x64 host comes from Platform; the managed assembly must stay AnyCPU.
        Assert.Equal("net462", csproj.Descendants("TargetFramework").Single().Value);
        Assert.Equal("x64", csproj.Descendants("Platform").Single().Value);
        Assert.Equal("AnyCPU", csproj.Descendants("PlatformTarget").Single().Value);
    }

    [Fact]
    public void InstallerActions_NeverLogTheToken()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Agents",
            "Dhadgar.Agent.Windows.InstallerActions", "EnrollmentTokenActions.cs"));
        var calls = Regex.Matches(source, @"session\.Log\((?<args>[^;]*)\);");

        Assert.NotEmpty(calls);
        foreach (Match call in calls)
        {
            // Strip string literals ("token written." is fine); what is left must not use the token variable.
            var code = Regex.Replace(call.Groups["args"].Value, @"""(?:[^""\\]|\\.)*""", string.Empty);
            Assert.DoesNotMatch(@"\btoken\b", code);
        }
    }
```

What each test catches on revert:

| Test | Fails when |
| --- | --- |
| `PackageWxs_EnrollmentToken_IsFormattedOnlyIntoTheHiddenSetter` | The Registry row returns (two attributes format the token, or the one that does is a `RegistryValue`), or the setter loses `HideTarget`. |
| `PackageWxs_TokenActions_AreHiddenDeferredAndRunAsSystem` | The writer loses `HideTarget`, `Impersonate="no"` or `Execute="deferred"`. |
| `PackageWxs_TokenActions_AreSequencedAroundTheRegistryWrite` | The sequence or a condition changes. |
| `PackageWxs_AgentKey_IsProtectedByAnUnconditionalComponent` | The SDDL goes back to a conditional component, or there are two. |
| `Installer_BindsTheInstallerActionsSfxDll` | The binding or the SfxCA platform settings drift. |
| `InstallerActions_NeverLogTheToken` | Any `session.Log(...)` uses `token`. |
| `KeyAndValueName_MatchAgentRegistry` | Either side's key or value name changes. This is the seam pin. |

`PackageWxs_HklmRegistryKeys_UseAgentRegistryKeyPath` and `PackageWxs_ServiceInstall_IsInTheComponentWhoseKeyPathIsTheAgentExe` (P36) keep passing.

#### Step 5: docs

**(a)** In `Installer/README.md`:

- In "Silent Install", after the existing SECURITY NOTE comment lines (61-64), add a comment line: `# The MSI log never contains the token, even with /l*v: it is written by a hidden custom action (D-S245).`
- In the Properties table, change the `ENROLLMENT_TOKEN` description to: `One-time enrollment token from the control plane. Written to HKLM\SOFTWARE\Meridian Console\Agent\EnrollmentToken by a hidden custom action, never through the Registry table, so no MSI log holds it (D-S245). Deleted on rollback and uninstall.`
- Change line 112's parenthesis "(which holds the enrollment token)" to "(which holds the enrollment token; protected on every install, with or without a token)".
- In "Development", add a `### Custom actions` subsection before "Debugging Custom Actions":

  > The installer's only custom actions are in `src/Agents/Dhadgar.Agent.Windows.InstallerActions` (DTF, net462). They write the enrollment token, and delete it on rollback and uninstall. The wixproj builds them through a project reference; the project is not in `Dhadgar.sln`. MakeSfxCA wraps the AnyCPU assembly in the x64 host (`Platform=x64`) and fails on paths of 260 characters or more. A secret installer property must never be formatted into a Registry, IniFile or Environment row, or into a deferred action without `HideTarget`: `Hidden="yes"` does not mask those in `/l*v` logs (D-S245).

- Replace the "Enrollment tokens are stored in registry..." security bullet (line 247) with:

  > Enrollment tokens are stored in the registry under a protected SDDL ACL (SYSTEM and Administrators only). They are written by a hidden custom action, so MSI logs never contain them. An administrator who sets the Windows Installer `Debug` policy to 7 opts in to logging otherwise masked values, the command line included.

**(b)** In `scripts/uat/README.md`, replace "The MSI log masks it (the property is `Hidden="yes"`), and" (line 178) with the following. Keep the U9 sentence after it.

> The MSI never formats it into a logged table: a hidden custom action writes it (D-S245; the run `20261027-192738-7347` found the old Registry row logging it in `RegAddValue`), and

#### Checks, in order (Windows, non-elevated)

1. `dotnet restore <worktree>\src\Agents\Dhadgar.Agent.Windows.InstallerActions\Dhadgar.Agent.Windows.InstallerActions.csproj` (step 1e). Check the lock file content against step 1e.
2. Build the CA project:
   - Run `dotnet build <same csproj> -c Release --no-restore -p:CI=true`. It must report 0 warnings.
   - Confirm `bin\x64\Release\net462\Dhadgar.Agent.Windows.InstallerActions.CA.dll` exists, and that it is x64 with:

     ```powershell
     $b = [IO.File]::ReadAllBytes('<that CA.dll>'); $pe = [BitConverter]::ToInt32($b, 0x3c); '{0:X4}' -f [BitConverter]::ToUInt16($b, $pe + 4)
     ```

     The output must be `8664`. A MakeSfxCA "Could not load file or assembly" error means the path is too long: report the path, `blocked`.
3. `dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p37\tests\Dhadgar.Agent.Windows.Tests\Dhadgar.Agent.Windows.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`
4. `dotnet test <same csproj> -c Release --no-build --filter "FullyQualifiedName~InstallerPackageTests|FullyQualifiedName~EnrollmentTokenDataTests"`, then the same command without `--filter`.
5. **Mutations.** Restore the file after each one, and confirm with `git diff`.
   - **M1:** add `<RegistryValue Name="EnrollmentToken" Type="string" Value="[ENROLLMENT_TOKEN]" />` inside `InstallPathComponent`'s `RegistryKey`. `PackageWxs_EnrollmentToken_IsFormattedOnlyIntoTheHiddenSetter` must fail. No rebuild is needed: the test reads the source.
   - **M2:** remove `HideTarget="yes"` from `WriteEnrollmentToken`. `PackageWxs_TokenActions_AreHiddenDeferredAndRunAsSystem` must fail.
   - **M3:** add `session.Log("t " + token);` before `key.SetValue(...)`. `InstallerActions_NeverLogTheToken` must fail.
   - **M4:** set `ValueName = "EnrollmentTokens"` in `EnrollmentTokenData.cs`, rebuild step 3, and run step 4's filter. `KeyAndValueName_MatchAgentRegistry` must fail.
6. **Build the MSI.** `pwsh -NoProfile -File <worktree>\scripts\uat\Build-AgentWindowsUat.ps1 -Verb run -Mode Msi` must exit 0 with no ICE error. Its locked restore of the wixproj also checks the CA lock file.
7. **Executed check.** `pwsh -NoProfile -File C:\Users\Steve\source\projects\MeridianConsole\docs\analysis\sprint2-agent-nodes\worknotes\P36-msi-tables.ps1 -MsiPath <worktree>\src\Agents\Dhadgar.Agent.Windows\Installer\bin\Release\MeridianConsoleAgent.msi -Checks All` must exit 0, and show:
   - `MsiHiddenProperties = ENROLLMENT_TOKEN;WriteEnrollmentToken`;
   - `deferred WriteEnrollmentToken Type 11265`;
   - the agent-key create row in `InstallPathComponent`.
8. **Executed mutation.** Run the same script with `-Checks All` against the failed run's MSI, `C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-w1\.uat\agent-windows\20261027-192738-7347\msi\MeridianConsoleAgent.msi`. It must exit 1 with exactly the seven `FAIL:` lines quoted in this ruling.
9. **Repo-wide lock check.** `dotnet restore C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\sprint2-agent-nodes-p37\Dhadgar.sln --locked-mode` must succeed. The two new `PackageVersion` entries must not move any sln lock file.
10. `git -C <worktree> status --porcelain=v1 --untracked-files=all`: only the files this packet names. Commit locally only.

Paste the output of steps 1, 2 and 5-9 into the report. Exit 2 from the script means `blocked`: quote the output.

**Owed to the UAT run (in the report).** The runner reruns `-Mode Msi` at the new w1 tip, and every leg must pass: U11a, U3, U4, U11b and U9. Its `logs\msi-install.log` must show:

- `Property(S): WINDOWS_BUILD_NUMBER = 26300`;
- a `ServiceInstall(` op;
- `WriteEnrollmentToken: token written.`;
- no `RegAddValue(Name=EnrollmentToken`.

**Docs owed:** step 5. `docs/PROJECT-STATE.md` and the installer reference chapter (`docs/meta/doc-update-map.md:43`) go to P15.

## Owner actions (not blocking)

| ID | Action |
| --- | --- |
| O2 | Amend the WiX secrets rule; agents may not edit it on an agent's say-so. `CLAUDE.md:80` says "Mark sensitive properties with `Hidden="yes"` to prevent logging", and `.claude/agents/security-advisor.md:21` says the same. Suggested text: "`Hidden="yes"` masks the property only. Never format a secret property into a Registry, IniFile, Environment or ServiceInstall row, or into a deferred custom action without `HideTarget="yes"`; write secrets through a `HideTarget` deferred action (D-S245)." Until then, reviewers apply D-S245 (R-SECRET). |

## Follow-ups for P15 (issues, not this fix)

- Replace the patterned placeholder component GUIDs in `Package.wxs` (`A1B2C3D4-…`, `B2C3D4E5-…` and so on) with generated GUIDs before the first release.
- Add the installer reference chapter `docs/reference/agents/windows-installer.md` (doc-update map row 43). It should cover D-S242, D-S244 and D-S245.
