# P26 ruling 1: the MSI baseline (D-S236, F25)

Trigger: P26 returned `blocked spec`. After the coder fixed five WiX v3-to-v5 syntax errors, the unmodified wixproj failed with `WIX0103 Cannot find the File file 'appsettings.json'` (`Package.wxs:140`). Report: `.claude\worktrees\sprint2-agent-nodes-p26\.claude\reports\sprint2-agent-nodes\P26-coder.md`.

**Ruling: fix the baseline inside P26** with the exact steps B1-B3 below. No new packet, no re-slice. The MSI leg (D-S223 (B)) is achievable. **No owner decision.**

## Findings (F25)

The MSI has never built, on `main` or on w1. `git diff --stat main f91bf09 -- src/Agents/Dhadgar.Agent.Windows` is empty for the csproj and `Installer/`, and CI (`azure-pipelines.yml`) never builds the wixproj. This is not a stale publish, a moved file or a harvest-path change. There are four causes, all reproduced in a scratch export of f91bf09:

| # | Cause | Evidence |
| --- | --- | --- |
| 1 | `Package.wxs` is WiX v3 syntax under the v5 SDK | the coder's five errors (WIX0004 `Platform`, WIX0005 `Condition`, WIX5063, WIX0004 `Sddl`, WIX0010 `User`) |
| 2 | `appsettings.json` is never published | the csproj uses `Microsoft.NET.Sdk`, which (unlike the Web SDK) has no copy item for `appsettings.json`. The wixproj BindPath commented "direct output directory for appsettings" (`..\bin\Release\net10.0\win-x64`) does not hold it either. It holds a 162 KB apphost `Dhadgar.Agent.Windows.exe`, which WiX would silently bind if the publish were missing. |
| 3 | Removing `Package/@Platform` without `InstallerPlatform` builds an **x86** package | Template `Intel;1033` and ICE80 errors (32-bit components in 64-bit directories). Even if it built, the registry writes would go to `WOW6432Node`, and the agent reads `Registry64`, which breaks the installer ↔ agent seam. |
| 4 | The coder's `util:PermissionEx User=` replacement drops the protected DACL on the token key | `Wix4SecureObject` merges with the existing DACL and cannot express `D:P`, so the inherited `HKLM\SOFTWARE` `Users:Read` would reach `EnrollmentToken`. D-S214: the agent sets no ACL on that key, so the installer ACL is the token's only protection. This is a security regression. |

The WiX v5 core-namespace `<PermissionEx Sddl="…">` (table `MsiLockPermissionsEx`, Windows Installer 5.0; the package already has `InstallerVersion="500"`) expresses the original SDDL exactly. Windows Installer cannot mix it with the legacy `LockPermissions` table (`<Permission>`): that combination fails ICE104. So the three folder `<Permission>` pairs move to `PermissionEx Sddl` as well.

Verified result of B1-B3 (scratch build, no install):

- the MSI is `Installer\bin\Release\MeridianConsoleAgent.msi` (no `en-US` folder), 36,192,256 bytes;
- Template `x64;1033`, and every component is 64-bit;
- `MsiLockPermissionsEx` has 4 rows: `AgentDataFolder`, `ServersDataFolder` and `TempDataFolder` (CreateFolder, `FA`), and `EnrollmentTokenKey` (Registry, `D:P(A;OICI;KA;;;SY)(A;OICI;KA;;;BA)`). There is no `LockPermissions` table;
- the only WiX warnings are ICE61 and ICE69 (WIX1076). These are accepted. The remaining build warnings are the agent's known IL2026 warnings from the `ProjectReference` build.

A `WIX0216 … 0x57` seen once in scratch came from a scratch path near MAX_PATH. It does not occur at the normal worktree path; if it does, report it and do not shorten anything in the repo.

## Decision D-S236

P26 makes the MSI build for the first time. P26's Change step 1 (the baseline rule) is replaced by B1-B3. These steps are part of P26's single commit, and the commit subject is unchanged. Because B1 changes what `dotnet publish` produces (it now includes `appsettings.json`, which P20's `AgentPublishConfig` edits in `<runDir>\agent`), **P20 depends on P26**, and P26 must merge into w1 before P20 starts. The MSI output path in the contract, in P23 and in P26 is `Installer\bin\Release\MeridianConsoleAgent.msi`.

## Spec for the coder (worktree `sprint2-agent-nodes-p26`, from f91bf09)

Start from the coder's uncommitted `Package.wxs` diff. **Keep** these parts of it:

- the removal of `Platform="x64"` from `<Package>`;
- `Condition="ENROLLMENT_TOKEN"` on `EnrollmentTokenComponent`;
- `Id="EnrollmentTokenKey" ForceCreateOnInstall="yes"` on its `RegistryKey`.

**Replace** the two `util:PermissionEx User=` lines, as B3 says.

### B1. `src/Agents/Dhadgar.Agent.Windows/Dhadgar.Agent.Windows.csproj`

Add one item group after the `PackageReference` group:

```xml
  <ItemGroup>
    <!-- Published beside the single-file exe (content root = exe folder under UseWindowsService); the MSI binds it -->
    <None Update="appsettings.json" CopyToPublishDirectory="PreserveNewest" ExcludeFromSingleFile="true" />
  </ItemGroup>
```

Do not set `CopyToOutputDirectory`. That keeps the build output, `dotnet run` and the test project's output unchanged.

### B2. `src/Agents/Dhadgar.Agent.Windows/Installer/Dhadgar.Agent.Windows.Installer.wixproj`

1. In the `PropertyGroup`, after `<OutputType>Package</OutputType>`, add `<InstallerPlatform>x64</InstallerPlatform>`.
2. Delete the two BindPaths `..\bin\Release\net10.0\win-x64` and `..\bin\Debug\net10.0\win-x64` and their "direct output directory" comment. Keep the two `…\win-x64\publish` BindPaths and `.`. A missing publish must now fail with WIX0103, not bind the apphost.
3. Header comment: the publish command becomes `dotnet publish ../Dhadgar.Agent.Windows.csproj -c Release -r win-x64` (drop the stray `/publish`), and the output line becomes `The MSI will be output to: bin\$(Configuration)\MeridianConsoleAgent.msi`.

### B3. `src/Agents/Dhadgar.Agent.Windows/Installer/Package.wxs`

1. In `EnrollmentTokenComponent`, replace the comment and the two `util:PermissionEx` lines with:

   ```xml
        <!-- ACL: SYSTEM and Administrators only, protected from inheritance (MsiLockPermissionsEx, Windows Installer 5.0) -->
        <PermissionEx Sddl="D:P(A;OICI;KA;;;SY)(A;OICI;KA;;;BA)" />
   ```

   This is the core namespace, with no `util:` prefix.
2. In each of `DataDirectoriesComponent`, `ServersDirectoryComponent` and `TempDirectoryComponent`, replace the `<CreateFolder>` body (the two `<Permission … GenericAll="yes" />` lines) with:

   ```xml
          <PermissionEx Sddl="D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)" />
   ```

   Without this change the build fails with ICE104. Afterwards the file has no `<Permission>` and no `util:PermissionEx`.
3. Header comment `Notes:` (lines 9-10): `Registry key and data-folder ACLs are protected-DACL SDDL (core PermissionEx, MsiLockPermissionsEx): SYSTEM and Administrators only. EnrollmentToken is stored in HKLM under that ACL.`
4. Record each B step in the report next to the coder's five syntax fixes. Then continue with P26 Change steps 2-6 as written.

### Added assertions in `InstallerPackageTests`

These are plain `[Fact]`s, with no MSI build, so they run on Linux CI. Each assertion fails when its fix is reverted.

- `PackageWxs_TokenKey_HasProtectedSddl`: the `RegistryKey` whose `RegistryValue/@Value` is `[ENROLLMENT_TOKEN]` has a child `PermissionEx` in the **wxs** namespace (`http://wixtoolset.org/schemas/v4/wxs`) whose `Sddl` starts with `D:P` and contains `;;;SY)` and `;;;BA)`. Reverting to `util:PermissionEx User=` fails it.
- `PackageWxs_UsesOnlySddlPermissions`: no element named `Permission` (wxs namespace) and no `PermissionEx` in the util namespace (`http://wixtoolset.org/schemas/v4/wxs/util`). This guards ICE104 and the merge-DACL regression.
- `Wixproj_InstallerPlatform_IsX64`: loading `Installer/Dhadgar.Agent.Windows.Installer.wixproj` (no XML namespace) gives `InstallerPlatform` = `x64`. This is the seam with the agent's `RegistryView.Registry64` read: without it the package is x86 and writes to `WOW6432Node`.
- `AgentCsproj_PublishesAppSettings`: `Dhadgar.Agent.Windows.csproj` has a `None` with `Update="appsettings.json"`, `CopyToPublishDirectory` set, and `ExcludeFromSingleFile="true"`. CI never builds the MSI, so this is the only CI guard for WIX0103.

### Checks

The checks are unchanged in order. The last command must produce `<wt>\src\Agents\Dhadgar.Agent.Windows\Installer\bin\Release\MeridianConsoleAgent.msi` (about 36 MB). Paste its size, and the WiX warning codes, which must be exactly ICE61 and ICE69. Any other WiX warning or error is `blocked spec` with the text. Also confirm that `<wt>\src\Agents\Dhadgar.Agent.Windows\bin\Release\net10.0\win-x64\publish\appsettings.json` exists after the publish.

Optional evidence step: it is read-only, opens the database in read-only mode (`0`) and never installs. Paste the output if you run it:

```powershell
$msi = '<wt>\src\Agents\Dhadgar.Agent.Windows\Installer\bin\Release\MeridianConsoleAgent.msi'
$wi = New-Object -ComObject WindowsInstaller.Installer
$db = $wi.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $wi, @($msi, 0))
$si = $db.GetType().InvokeMember('SummaryInformation', 'GetProperty', $null, $db, $null)
$si.GetType().InvokeMember('Property', 'GetProperty', $null, $si, @(7))   # expect x64;1033
$view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @('SELECT `LockObject`, `Table`, `SDDLText` FROM `MsiLockPermissionsEx`'))
[void]$view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
while ($rec = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)) {
  (1..3 | ForEach-Object { $rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, @($_)) }) -join ' | '
}
[void]$view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)
```

Expect 4 rows, as listed in Findings.

### Docs owed (in addition to P26's list)

In `src/Agents/Dhadgar.Agent.Windows/Installer/README.md`:

- the output path becomes `bin\Release\MeridianConsoleAgent.msi` (line 42 is wrong today);
- the build sequence becomes publish (`-c Release -r win-x64`), then `dotnet build -c Release` (`InstallerPlatform` is now set in the project, so `-p:InstallerPlatform=x64` is redundant);
- add one line saying that the folder and token-key ACLs are protected-DACL SDDL.

## Security review attention (P26's `security-advisor` pass)

- The token key ACL is restored to the original intent: protected, with SYSTEM and Administrators only. The coder's interim `util:PermissionEx` version must not ship.
- The folder ACLs change from `LockPermissions` (explicit, non-inheritable ACEs) to a protected DACL with `OICI` (children inherit SYSTEM and Administrators full control). This is consistent with D-S233 (the MSI owns the `%ProgramData%\Meridian Console\Agent` ACL) and with what the agent expects. Confirm that no agent code sets a broader ACL inside these folders.
- Without `ENROLLMENT_TOKEN` the key is created by the non-secret components (`DataPath`, `InstallPath`, `Version`, `ControlPlaneEndpoint`) and keeps the inherited ACL. No secret is written in that case, so this is accepted.
- The x64 platform also places the registry writes in the 64-bit view, which the agent reads.

## Ledger changes made by this ruling

- `README.md`: F25 and D-S236 added. The P26 row points here, the P20 row depends on P26, and the wave 2U merge order merges P26 before P20.
- `work-packets.md`, P26:
  - the header says P26 merges before P20;
  - Change step 1 points to B1-B3;
  - the Tests, Checks and Docs owed sections are amended.
- `work-packets.md`, P20 and paths: the P20 header depends on P19 and P26. The MSI path is corrected in the wave 2U contract `Run` row and in P26 Checks.
- `RESUME.md`: the next actions and watch-outs are updated.
