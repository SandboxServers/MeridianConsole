# Meridian Console Agent - Windows Installer

WiX v5 MSI installer for the Meridian Console Agent Windows Service.

## Prerequisites

- .NET SDK 10.0+
- WiX Toolset v5 (installed via NuGet package references)
- Windows 10/11 or Windows Server 2019+ for building

## Requirements

The MSI installs on 64-bit Windows 10 (build 10240) or later. The installer reads the build number from `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\CurrentBuildNumber`. It cannot use the `WindowsBuild` or `VersionNT` properties, because msiexec reports Windows 8.1 values (9600 and 603) for them on Windows 10 and 11 (D-S242).

## Building the Installer

### From the Installer directory:

```powershell
# Build Debug MSI
dotnet build

# Build Release MSI
dotnet build -c Release
```

The project sets `InstallerPlatform` to `x64` (the agent reads the 64-bit registry view), so no platform switch is needed.

### From the solution root:

```powershell
# Build the agent and the MSI with the UAT build script (it also builds the UAT driver, Nodes and Gateway);
# it restores locked and RID-less, so it does not rewrite lock files (D-S241)
pwsh -NoProfile -File scripts\uat\Build-AgentWindowsUat.ps1 -Verb run -Mode Msi
```

## Output Location

The MSI is output to:

```text
bin\Release\MeridianConsoleAgent.msi
```

## Installation

### Interactive Install

Double-click the MSI file or run:

```powershell
msiexec /i MeridianConsoleAgent.msi
```

### Silent Install

```powershell
# Basic silent install
msiexec /i MeridianConsoleAgent.msi /qn /norestart

# With enrollment token
# SECURITY NOTE: Command-line arguments are visible in process listings (Get-Process),
# command history, and audit logs. For production deployments, consider using a transform
# file (.mst) or clearing command history after installation.
msiexec /i MeridianConsoleAgent.msi /qn ENROLLMENT_TOKEN=your-token-here CONTROL_PLANE_ENDPOINT=https://control.example.com

# With logging
msiexec /i MeridianConsoleAgent.msi /qn /l*v install.log
```

### Upgrade

The installer supports in-place upgrades. Simply run the new MSI:

```powershell
msiexec /i MeridianConsoleAgent-NewVersion.msi /qn
```

### Uninstall

```powershell
# Via Add/Remove Programs, or:
msiexec /x {ProductCode} /qn

# Or by UpgradeCode (recommended for automation):
# Find installed product first
Get-CimInstance -ClassName Win32_Product | Where-Object { $_.Name -like '*Meridian Console Agent*' }
```

## Properties

| Property | Description | Default |
|----------|-------------|---------|
| `INSTALLFOLDER` | Installation directory | `C:\Program Files\Meridian Console\Agent\` |
| `ENROLLMENT_TOKEN` | One-time enrollment token from control plane | (empty) |
| `CONTROL_PLANE_ENDPOINT` | Control plane URL; must be `https://`. Kept across major upgrades when omitted. Overrides `appsettings.json`; environment variables and the agent's command line still win. | (empty; `appsettings.json` value) |

## What Gets Installed

### Files

- `C:\Program Files\Meridian Console\Agent\Dhadgar.Agent.Windows.exe`
- `C:\Program Files\Meridian Console\Agent\appsettings.json`

### Directories

- `C:\ProgramData\Meridian Console\` - Parent of the agent data folders; created and owned by the installer so a standard user cannot pre-create it and keep ownership
- `C:\ProgramData\Meridian Console\Agent\` - Runtime data
- `C:\ProgramData\Meridian Console\Agent\Servers\` - Game server files
- `C:\ProgramData\Meridian Console\Agent\Temp\` - Temporary files

The folders above (the parent folder carries an empty-mask Owner Rights ACE, so an owner has no implicit WRITE_DAC) and the `HKLM\SOFTWARE\Meridian Console\Agent` key (which holds the enrollment token) use protected-DACL SDDL: SYSTEM and Administrators only, with no inherited access.

### Windows Service

- Name: `DhadgarAgent`
- Display Name: `Meridian Console Agent`
- Startup Type: Automatic (Delayed Start)
- Recovery: Restart on first 3 failures

### Event Log

- Source: `Meridian Console Agent`
- Log: `Application`

### Registry

- `HKLM\SOFTWARE\Meridian Console\Agent`

## Uninstall Cleanup

The uninstaller will automatically:
1. Stop the `DhadgarAgent` service
2. Remove the service registration
3. Delete installed files from `C:\Program Files\Meridian Console\Agent\`
4. Remove Event Log source registration
5. Remove registry keys under `HKLM\SOFTWARE\Meridian Console\Agent`

### Manual Cleanup Required

The following resources are **not automatically removed** to prevent data loss and ensure security review:

**Certificates** (run as Administrator in PowerShell):

```powershell
# Remove agent client certificates (issued by the agent CA)
Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Issuer -like '*CN=Meridian Console Agent CA*' } | Remove-Item -Force

# Remove the agent CA from its custom store
Get-ChildItem Cert:\LocalMachine\MeridianConsoleAgent | Where-Object { $_.Subject -like '*CN=Meridian Console Agent CA*' } | Remove-Item -Force
```

**Firewall rules** (run as Administrator):

```powershell
# Remove ALL firewall rules for the agent executable
# This includes any rules created by the agent for game servers
netsh advfirewall firewall delete rule name=all program="C:\Program Files\Meridian Console\Agent\Dhadgar.Agent.Windows.exe"
```

Agent state `C:\ProgramData\Meridian Console\Agent\agent-state.json` and the event source `Meridian Console Agent` also remain.

**Game server data** (WARNING: This deletes all server files!):

```powershell
Remove-Item -Recurse -Force "C:\ProgramData\Meridian Console"
```

Note: Game server data in `C:\ProgramData\Meridian Console\Agent\Servers\` is **preserved** by default to prevent accidental data loss.

## Customization

### Adding a Custom Icon

Place a 256x256 ICO file named `icon.ico` in this directory and uncomment the `<Icon>` and `<Property Id="ARPPRODUCTICON">` elements in `Package.wxs`.

### Signing the MSI

For production deployments, uncomment the `<SignOutput>` property in the `.wixproj` file and configure code signing certificate.

## Troubleshooting

### Build Errors

**"Cannot find file..."**

Ensure you've published the agent first:

```powershell
dotnet publish ..\Dhadgar.Agent.Windows.csproj -c Release -r win-x64
```

**"WiX toolset not found"**

The WiX SDK should be restored automatically. Run:

```powershell
dotnet restore
```

### Installation Errors

**Error 1920: Service failed to start**
Check the Windows Event Log (Application) for startup errors. Common causes:
- Missing configuration in `appsettings.json`
- Network connectivity issues to control plane
- Certificate problems

**Access denied**
Run the installer as Administrator.

## Development

### Debugging Custom Actions

Enable verbose logging:

```powershell
msiexec /i MeridianConsoleAgent.msi /l*v debug.log
```

### Modifying the Installer

1. Edit `Package.wxs` for component changes
2. Edit `Dhadgar.Agent.Windows.Installer.wixproj` for build configuration
3. Rebuild with `dotnet build`

## Security Considerations

### Service Account: LocalSystem

The agent service runs as **LocalSystem** by default. This is a deliberate design decision, not a misconfiguration. LocalSystem is required because the agent must:

- **Create and manage Windows Services** (game server isolation via `sc.exe`)
- **Access the LocalMachine certificate store** (mTLS certificates for control plane communication)
- **Manage Windows Firewall rules** (opening/closing ports for game servers)
- **Set directory ACLs** for game server isolation (granting/denying access to Virtual Service Accounts)
- **Manage Job Objects** for process isolation and resource limits

A least-privilege service account would lack the permissions for these operations. Game server processes themselves run under isolated Virtual Service Accounts (`NT SERVICE\MeridianGS_{serverId}`) with minimal privileges -- LocalSystem is only used by the agent orchestrator.

### Other Security Notes

- The MSI requires administrator privileges to install
- Certificates are stored in the Windows Certificate Store (LocalMachine)
- All custom actions run elevated during install/uninstall
- Enrollment tokens are stored in registry with SDDL ACLs restricting access to SYSTEM and Administrators only
- After uninstall, residual artifacts (certificates, firewall rules, game server data) require manual cleanup -- see Package.wxs comments for details
