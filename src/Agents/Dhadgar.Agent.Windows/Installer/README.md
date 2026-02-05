# Meridian Console Agent - Windows Installer

WiX v5 MSI installer for the Meridian Console Agent Windows Service.

## Prerequisites

- .NET SDK 10.0+
- WiX Toolset v5 (installed via NuGet package references)
- Windows 10/11 or Windows Server 2019+ for building

## Building the Installer

### From the Installer directory:

```powershell
# Build Debug MSI
dotnet build

# Build Release MSI
dotnet build -c Release

# Build for specific platform
dotnet build -p:InstallerPlatform=x64
dotnet build -p:InstallerPlatform=arm64
```

### From the solution root:

```powershell
# First, publish the agent
dotnet publish src/Agents/Dhadgar.Agent.Windows -c Release -r win-x64 --self-contained

# Then build the installer
dotnet build src/Agents/Dhadgar.Agent.Windows/Installer -c Release
```

## Output Location

MSI files are output to:
```
bin\{Configuration}\{Platform}\en-US\MeridianConsoleAgent-{Version}-{Platform}.msi
```

## Installation

### Interactive Install
Double-click the MSI file or run:
```powershell
msiexec /i MeridianConsoleAgent-1.0.0-x64.msi
```

### Silent Install
```powershell
# Basic silent install
msiexec /i MeridianConsoleAgent-1.0.0-x64.msi /quiet /norestart

# With enrollment token
msiexec /i MeridianConsoleAgent-1.0.0-x64.msi /quiet ENROLLMENT_TOKEN=your-token-here

# Auto-start service after install
msiexec /i MeridianConsoleAgent-1.0.0-x64.msi /quiet ENROLLMENT_TOKEN=xxx AUTOSTART=1

# With logging
msiexec /i MeridianConsoleAgent-1.0.0-x64.msi /quiet /l*v install.log
```

### Upgrade
The installer supports in-place upgrades. Simply run the new MSI:
```powershell
msiexec /i MeridianConsoleAgent-2.0.0-x64.msi /quiet
```

### Uninstall
```powershell
# Via Add/Remove Programs, or:
msiexec /x {ProductCode} /quiet

# Or by UpgradeCode (recommended for automation):
# Find installed product first
Get-WmiObject Win32_Product | Where-Object { $_.Name -like '*Meridian Console Agent*' }
```

## Properties

| Property | Description | Default |
|----------|-------------|---------|
| `INSTALLFOLDER` | Installation directory | `C:\Program Files\Meridian Console\Agent\` |
| `ENROLLMENT_TOKEN` | One-time enrollment token from control plane | (empty) |
| `AUTOSTART` | Start service immediately after install (0 or 1) | 0 |

## What Gets Installed

### Files
- `C:\Program Files\Meridian Console\Agent\Dhadgar.Agent.Windows.exe`
- `C:\Program Files\Meridian Console\Agent\appsettings.json`

### Directories
- `C:\ProgramData\Meridian Console\Agent\` - Runtime data
- `C:\ProgramData\Meridian Console\Agent\Servers\` - Game server files
- `C:\ProgramData\Meridian Console\Agent\Temp\` - Temporary files

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
# Remove agent certificates
Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -like '*dhadgar-agent*' -or $_.FriendlyName -like '*Meridian Console*' } | Remove-Item -Force

# Remove CA certificate
Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -like '*Meridian Console CA*' } | Remove-Item -Force
```

**Firewall rules** (run as Administrator):
```powershell
netsh advfirewall firewall delete rule name=all program="C:\Program Files\Meridian Console\Agent\Dhadgar.Agent.Windows.exe"
```

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
dotnet publish ..\Dhadgar.Agent.Windows.csproj -c Release -r win-x64 --self-contained
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
