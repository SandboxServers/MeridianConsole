<#
.SYNOPSIS
    Bootstrap script for MeridianConsole development environment on Windows.

.DESCRIPTION
    Installs and configures all required tools for MeridianConsole development:
    - .NET SDK 10.0.100
    - Docker Desktop
    - minikube
    - kubectl
    - Git
    - Optional: VS Code, PostgreSQL client tools

    This script is idempotent and safe to re-run.

.PARAMETER SkipDocker
    Skip Docker Desktop installation and configuration.

.PARAMETER SkipMinikube
    Skip minikube installation and configuration.

.PARAMETER SkipInfrastructure
    Skip starting local infrastructure (docker-compose).

.PARAMETER SkipOptional
    Skip optional tools (VS Code, PostgreSQL client tools).

.EXAMPLE
    .\bootstrap-dev.ps1

.EXAMPLE
    .\bootstrap-dev.ps1 -SkipMinikube -SkipOptional

.NOTES
    Requires Windows 10 version 1903 or later.
    Logs output to ~/.meridian-bootstrap.log
#>

[CmdletBinding()]
param(
    [switch]$SkipDocker,
    [switch]$SkipMinikube,
    [switch]$SkipInfrastructure,
    [switch]$SkipOptional
)

# Script configuration
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$LogFile = Join-Path $env:USERPROFILE ".meridian-bootstrap.log"
$MinWindowsVersion = [Version]"10.0.18362"  # Windows 10 1903
$DockerStartupTimeout = 300  # seconds
$MinikubeCpus = 4
$MinikubeMemory = 8192

#region Helper Functions

function Write-Log {
    param(
        [Parameter(Mandatory)]
        [string]$Message,
        [ValidateSet("Info", "Success", "Warning", "Error")]
        [string]$Level = "Info"
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"

    # Append to log file
    Add-Content -Path $LogFile -Value $logMessage -ErrorAction SilentlyContinue

    # Console output with colors
    switch ($Level) {
        "Info"    { Write-Host $Message -ForegroundColor Cyan }
        "Success" { Write-Host $Message -ForegroundColor Green }
        "Warning" { Write-Host $Message -ForegroundColor Yellow }
        "Error"   { Write-Host $Message -ForegroundColor Red }
    }
}

function Write-Banner {
    $banner = @"

    ███╗   ███╗███████╗██████╗ ██╗██████╗ ██╗ █████╗ ███╗   ██╗
    ████╗ ████║██╔════╝██╔══██╗██║██╔══██╗██║██╔══██╗████╗  ██║
    ██╔████╔██║█████╗  ██████╔╝██║██║  ██║██║███████║██╔██╗ ██║
    ██║╚██╔╝██║██╔══╝  ██╔══██╗██║██║  ██║██║██╔══██║██║╚██╗██║
    ██║ ╚═╝ ██║███████╗██║  ██║██║██████╔╝██║██║  ██║██║ ╚████║
    ╚═╝     ╚═╝╚══════╝╚═╝  ╚═╝╚═╝╚═════╝ ╚═╝╚═╝  ╚═╝╚═╝  ╚═══╝
                    Console Dev Environment Bootstrap

"@
    Write-Host $banner -ForegroundColor Magenta
    Write-Host "    Logging to: $LogFile" -ForegroundColor DarkGray
    Write-Host ""
}

function Write-Summary {
    param(
        [hashtable]$Results
    )

    Write-Host ""
    Write-Host "=" * 60 -ForegroundColor Magenta
    Write-Host "  BOOTSTRAP SUMMARY" -ForegroundColor Magenta
    Write-Host "=" * 60 -ForegroundColor Magenta
    Write-Host ""

    foreach ($key in $Results.Keys | Sort-Object) {
        $status = $Results[$key]
        $color = switch ($status) {
            "Installed"   { "Green" }
            "Skipped"     { "Yellow" }
            "Already OK"  { "Cyan" }
            "Failed"      { "Red" }
            default       { "White" }
        }
        Write-Host "  $($key.PadRight(25)) : " -NoNewline
        Write-Host $status -ForegroundColor $color
    }

    Write-Host ""
    Write-Host "=" * 60 -ForegroundColor Magenta
    Write-Host ""
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Request-Elevation {
    if (-not (Test-Administrator)) {
        Write-Log "Requesting administrator privileges..." -Level Warning
        $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""

        if ($SkipDocker) { $arguments += " -SkipDocker" }
        if ($SkipMinikube) { $arguments += " -SkipMinikube" }
        if ($SkipInfrastructure) { $arguments += " -SkipInfrastructure" }
        if ($SkipOptional) { $arguments += " -SkipOptional" }

        Start-Process PowerShell -Verb RunAs -ArgumentList $arguments
        exit
    }
}

function Test-WindowsVersion {
    $osVersion = [Environment]::OSVersion.Version
    if ($osVersion -lt $MinWindowsVersion) {
        throw "Windows 10 version 1903 or later is required. Current version: $osVersion"
    }
    return $true
}

function Test-InternetConnection {
    try {
        $response = Invoke-WebRequest -Uri "https://www.microsoft.com" -UseBasicParsing -TimeoutSec 10
        return $response.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

function Test-WingetAvailable {
    try {
        $null = Get-Command winget -ErrorAction Stop
        return $true
    }
    catch {
        return $false
    }
}

function Install-Winget {
    Write-Log "Installing winget (App Installer)..."

    # Try to install via Microsoft Store first
    try {
        # Use Add-AppxPackage with the Microsoft Store URI
        $progressPreference = 'silentlyContinue'

        # Download the latest App Installer from GitHub releases
        $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/microsoft/winget-cli/releases/latest"
        $msixBundle = $releases.assets | Where-Object { $_.name -match "\.msixbundle$" } | Select-Object -First 1

        if ($msixBundle) {
            $downloadPath = Join-Path $env:TEMP "Microsoft.DesktopAppInstaller.msixbundle"
            Write-Log "Downloading winget from GitHub..."
            Invoke-WebRequest -Uri $msixBundle.browser_download_url -OutFile $downloadPath

            # Also need VCLibs and UI.Xaml dependencies
            Write-Log "Installing dependencies..."

            # VCLibs
            $vcLibsUrl = "https://aka.ms/Microsoft.VCLibs.x64.14.00.Desktop.appx"
            $vcLibsPath = Join-Path $env:TEMP "VCLibs.appx"
            Invoke-WebRequest -Uri $vcLibsUrl -OutFile $vcLibsPath
            Add-AppxPackage -Path $vcLibsPath -ErrorAction SilentlyContinue

            # UI.Xaml
            $xamlUrl = "https://www.nuget.org/api/v2/package/Microsoft.UI.Xaml/2.8.6"
            $xamlPath = Join-Path $env:TEMP "Microsoft.UI.Xaml.zip"
            Invoke-WebRequest -Uri $xamlUrl -OutFile $xamlPath
            Expand-Archive -Path $xamlPath -DestinationPath (Join-Path $env:TEMP "Microsoft.UI.Xaml") -Force
            $xamlAppx = Get-ChildItem -Path (Join-Path $env:TEMP "Microsoft.UI.Xaml\tools\AppX\x64\Release") -Filter "*.appx" | Select-Object -First 1
            if ($xamlAppx) {
                Add-AppxPackage -Path $xamlAppx.FullName -ErrorAction SilentlyContinue
            }

            # Install winget
            Write-Log "Installing winget package..."
            Add-AppxPackage -Path $downloadPath

            # Clean up
            Remove-Item -Path $downloadPath -Force -ErrorAction SilentlyContinue
            Remove-Item -Path $vcLibsPath -Force -ErrorAction SilentlyContinue
            Remove-Item -Path $xamlPath -Force -ErrorAction SilentlyContinue

            return $true
        }
    }
    catch {
        Write-Log "Failed to install winget: $_" -Level Error
        return $false
    }

    return $false
}

function Test-WingetPackageInstalled {
    param(
        [Parameter(Mandatory)]
        [string]$PackageId
    )

    try {
        $result = winget list --id $PackageId --accept-source-agreements 2>$null
        return $result -match $PackageId
    }
    catch {
        return $false
    }
}

function Install-WingetPackage {
    param(
        [Parameter(Mandatory)]
        [string]$PackageId,
        [string]$DisplayName = $PackageId,
        [string]$Version = $null
    )

    if (Test-WingetPackageInstalled -PackageId $PackageId) {
        Write-Log "$DisplayName is already installed" -Level Success
        return "Already OK"
    }

    Write-Log "Installing $DisplayName..."

    try {
        $args = @(
            "install",
            "--id", $PackageId,
            "--accept-source-agreements",
            "--accept-package-agreements",
            "--silent"
        )

        if ($Version) {
            $args += "--version", $Version
        }

        $process = Start-Process -FilePath "winget" -ArgumentList $args -Wait -PassThru -NoNewWindow

        if ($process.ExitCode -eq 0) {
            Write-Log "$DisplayName installed successfully" -Level Success
            return "Installed"
        }
        elseif ($process.ExitCode -eq -1978335189) {
            # Package already installed (different detection)
            Write-Log "$DisplayName is already installed" -Level Success
            return "Already OK"
        }
        else {
            Write-Log "Failed to install $DisplayName (exit code: $($process.ExitCode))" -Level Warning
            return "Failed"
        }
    }
    catch {
        Write-Log "Error installing $DisplayName : $_" -Level Error
        return "Failed"
    }
}

function Wait-ForDocker {
    param(
        [int]$TimeoutSeconds = 300
    )

    Write-Log "Waiting for Docker Desktop to start (timeout: ${TimeoutSeconds}s)..."

    $startTime = Get-Date
    $dockerReady = $false

    while (-not $dockerReady -and ((Get-Date) - $startTime).TotalSeconds -lt $TimeoutSeconds) {
        try {
            $null = docker info 2>$null
            if ($LASTEXITCODE -eq 0) {
                $dockerReady = $true
            }
        }
        catch {
            # Docker not ready yet
        }

        if (-not $dockerReady) {
            Start-Sleep -Seconds 5
            Write-Host "." -NoNewline
        }
    }

    Write-Host ""

    if ($dockerReady) {
        Write-Log "Docker Desktop is ready" -Level Success
        return $true
    }
    else {
        Write-Log "Timed out waiting for Docker Desktop" -Level Warning
        return $false
    }
}

function Start-DockerDesktop {
    # Check if Docker Desktop is running
    $dockerProcess = Get-Process -Name "Docker Desktop" -ErrorAction SilentlyContinue

    if (-not $dockerProcess) {
        Write-Log "Starting Docker Desktop..."

        $dockerPath = "${env:ProgramFiles}\Docker\Docker\Docker Desktop.exe"
        if (-not (Test-Path $dockerPath)) {
            $dockerPath = "${env:LOCALAPPDATA}\Docker\Docker Desktop.exe"
        }

        if (Test-Path $dockerPath) {
            Start-Process -FilePath $dockerPath
        }
        else {
            Write-Log "Could not find Docker Desktop executable" -Level Warning
            return $false
        }
    }

    return Wait-ForDocker -TimeoutSeconds $DockerStartupTimeout
}

function Configure-DockerWSL2 {
    Write-Log "Checking Docker WSL2 configuration..."

    # Docker Desktop settings file location
    $settingsPath = Join-Path $env:APPDATA "Docker\settings.json"

    if (Test-Path $settingsPath) {
        try {
            $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json

            if ($settings.wslEngineEnabled -eq $true) {
                Write-Log "Docker is already configured to use WSL2 backend" -Level Success
                return "Already OK"
            }

            # Enable WSL2 if not already
            Write-Log "Enabling WSL2 backend for Docker..."
            $settings.wslEngineEnabled = $true
            $settings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath

            Write-Log "Docker WSL2 backend enabled. Restart Docker Desktop for changes to take effect." -Level Warning
            return "Configured"
        }
        catch {
            Write-Log "Could not modify Docker settings: $_" -Level Warning
            return "Skipped"
        }
    }
    else {
        Write-Log "Docker settings file not found. Docker may need to be started first." -Level Warning
        return "Skipped"
    }
}

function Configure-Minikube {
    Write-Log "Configuring minikube..."

    try {
        # Set driver to docker
        Write-Log "Setting minikube driver to docker..."
        & minikube config set driver docker 2>$null

        # Check if minikube is already running
        $status = & minikube status 2>$null
        if ($status -match "Running") {
            Write-Log "Minikube is already running" -Level Success
            return "Already OK"
        }

        # Start minikube
        Write-Log "Starting minikube (cpus=$MinikubeCpus, memory=$MinikubeMemory)..."
        & minikube start --cpus=$MinikubeCpus --memory=$MinikubeMemory

        if ($LASTEXITCODE -ne 0) {
            Write-Log "Failed to start minikube" -Level Error
            return "Failed"
        }

        # Enable addons
        Write-Log "Enabling minikube addons..."
        & minikube addons enable ingress
        & minikube addons enable metrics-server

        Write-Log "Minikube configured successfully" -Level Success
        return "Configured"
    }
    catch {
        Write-Log "Error configuring minikube: $_" -Level Error
        return "Failed"
    }
}

function Setup-Project {
    Write-Log "Setting up project..."

    # Check if we're in the repo directory
    $solutionFile = Join-Path $PWD "Dhadgar.sln"
    $composePath = Join-Path $PWD "deploy\compose\docker-compose.dev.yml"

    if (-not (Test-Path $solutionFile)) {
        # Try to find it relative to script location
        $scriptDir = Split-Path -Parent $PSCommandPath
        $repoRoot = Split-Path -Parent $scriptDir

        $solutionFile = Join-Path $repoRoot "Dhadgar.sln"
        $composePath = Join-Path $repoRoot "deploy\compose\docker-compose.dev.yml"
    }

    if (-not (Test-Path $solutionFile)) {
        Write-Log "Not in MeridianConsole repository directory. Skipping project setup." -Level Warning
        return "Skipped"
    }

    $repoRoot = Split-Path -Parent $solutionFile

    try {
        # Restore packages
        Write-Log "Restoring NuGet packages..."
        Push-Location $repoRoot
        & dotnet restore
        if ($LASTEXITCODE -ne 0) {
            Pop-Location
            Write-Log "dotnet restore failed" -Level Error
            return "Failed"
        }
        Pop-Location

        # Start infrastructure
        if (-not $SkipInfrastructure -and (Test-Path $composePath)) {
            Write-Log "Starting local infrastructure..."
            & docker compose -f $composePath up -d
            if ($LASTEXITCODE -ne 0) {
                Write-Log "docker-compose failed" -Level Warning
                return "Partial"
            }
        }

        Write-Log "Project setup complete" -Level Success
        return "Complete"
    }
    catch {
        Write-Log "Error during project setup: $_" -Level Error
        return "Failed"
    }
}

function Verify-Installation {
    Write-Host ""
    Write-Log "Verifying installations..."
    Write-Host ""

    $verifications = @(
        @{ Name = "Git"; Command = "git --version" },
        @{ Name = ".NET SDK"; Command = "dotnet --version" },
        @{ Name = "Docker"; Command = "docker --version" },
        @{ Name = "kubectl"; Command = "kubectl version --client" },
        @{ Name = "minikube"; Command = "minikube version" }
    )

    foreach ($item in $verifications) {
        try {
            $output = Invoke-Expression $item.Command 2>$null
            if ($LASTEXITCODE -eq 0 -or $output) {
                Write-Host "  [OK] $($item.Name): " -NoNewline -ForegroundColor Green
                Write-Host ($output -split "`n")[0] -ForegroundColor Gray
            }
            else {
                Write-Host "  [--] $($item.Name): Not available" -ForegroundColor Yellow
            }
        }
        catch {
            Write-Host "  [--] $($item.Name): Not available" -ForegroundColor Yellow
        }
    }

    Write-Host ""
}

#endregion

#region Main Script

# Initialize log file
$null = New-Item -Path $LogFile -ItemType File -Force -ErrorAction SilentlyContinue
Add-Content -Path $LogFile -Value "=" * 60
Add-Content -Path $LogFile -Value "MeridianConsole Bootstrap - $(Get-Date)"
Add-Content -Path $LogFile -Value "=" * 60

# Display banner
Write-Banner

# Results tracking
$results = @{}

try {
    #region Pre-flight Checks

    Write-Log "Running pre-flight checks..."
    Write-Host ""

    # Check admin privileges
    if (-not (Test-Administrator)) {
        Write-Log "Administrator privileges required. Requesting elevation..." -Level Warning
        Request-Elevation
        exit
    }
    Write-Log "  Running as Administrator" -Level Success

    # Check Windows version
    Write-Log "  Checking Windows version..."
    Test-WindowsVersion
    $osVersion = [Environment]::OSVersion.Version
    Write-Log "  Windows version OK: $osVersion" -Level Success

    # Check internet connectivity
    Write-Log "  Checking internet connectivity..."
    if (-not (Test-InternetConnection)) {
        throw "Internet connection required but not available"
    }
    Write-Log "  Internet connection OK" -Level Success

    # Check/install winget
    Write-Log "  Checking winget availability..."
    if (-not (Test-WingetAvailable)) {
        Write-Log "  winget not found. Installing..." -Level Warning
        if (-not (Install-Winget)) {
            throw "Failed to install winget. Please install App Installer from Microsoft Store manually."
        }

        # Refresh environment
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
    }
    Write-Log "  winget is available" -Level Success

    Write-Host ""
    Write-Log "Pre-flight checks passed" -Level Success
    Write-Host ""

    #endregion

    #region Install Core Tools

    Write-Log "Installing core tools..."
    Write-Host ""

    # Git
    $results["Git"] = Install-WingetPackage -PackageId "Git.Git" -DisplayName "Git"

    # .NET SDK 10
    $results[".NET SDK 10"] = Install-WingetPackage -PackageId "Microsoft.DotNet.SDK.10" -DisplayName ".NET SDK 10"

    # kubectl
    $results["kubectl"] = Install-WingetPackage -PackageId "Kubernetes.kubectl" -DisplayName "kubectl"

    #endregion

    #region Docker

    if ($SkipDocker) {
        Write-Log "Skipping Docker installation (--SkipDocker)" -Level Warning
        $results["Docker Desktop"] = "Skipped"
    }
    else {
        $results["Docker Desktop"] = Install-WingetPackage -PackageId "Docker.DockerDesktop" -DisplayName "Docker Desktop"

        if ($results["Docker Desktop"] -in @("Installed", "Already OK")) {
            # Start Docker and wait for it
            if (Start-DockerDesktop) {
                $results["Docker WSL2"] = Configure-DockerWSL2
            }
            else {
                $results["Docker WSL2"] = "Skipped"
            }
        }
    }

    #endregion

    #region Minikube

    if ($SkipMinikube) {
        Write-Log "Skipping minikube installation (--SkipMinikube)" -Level Warning
        $results["minikube"] = "Skipped"
        $results["minikube Config"] = "Skipped"
    }
    else {
        $results["minikube"] = Install-WingetPackage -PackageId "Kubernetes.minikube" -DisplayName "minikube"

        if ($results["minikube"] -in @("Installed", "Already OK")) {
            # Refresh PATH to include minikube
            $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")

            if (-not $SkipDocker -and $results["Docker Desktop"] -in @("Installed", "Already OK")) {
                $results["minikube Config"] = Configure-Minikube
            }
            else {
                Write-Log "Skipping minikube configuration (Docker not available)" -Level Warning
                $results["minikube Config"] = "Skipped"
            }
        }
    }

    #endregion

    #region Optional Tools

    if ($SkipOptional) {
        Write-Log "Skipping optional tools (--SkipOptional)" -Level Warning
        $results["VS Code"] = "Skipped"
        $results["PostgreSQL Tools"] = "Skipped"
    }
    else {
        Write-Host ""
        Write-Log "Installing optional tools..."

        # VS Code
        $results["VS Code"] = Install-WingetPackage -PackageId "Microsoft.VisualStudioCode" -DisplayName "Visual Studio Code"

        # PostgreSQL client tools (pgAdmin or just psql via PostgreSQL package)
        $results["PostgreSQL Tools"] = Install-WingetPackage -PackageId "PostgreSQL.pgAdmin" -DisplayName "pgAdmin 4"
    }

    #endregion

    #region Project Setup

    Write-Host ""
    if ($SkipInfrastructure) {
        Write-Log "Skipping infrastructure setup (--SkipInfrastructure)" -Level Warning
        $results["Project Setup"] = "Skipped"
    }
    else {
        # Refresh PATH for newly installed tools
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")

        $results["Project Setup"] = Setup-Project
    }

    #endregion

    #region Verification

    Verify-Installation

    #endregion
}
catch {
    Write-Log "Bootstrap failed: $_" -Level Error
    $results["OVERALL"] = "FAILED"
}
finally {
    # Display summary
    Write-Summary -Results $results

    # Final message
    $failures = $results.Values | Where-Object { $_ -eq "Failed" }
    if ($failures.Count -gt 0) {
        Write-Log "Bootstrap completed with $($failures.Count) failure(s). Check the log for details: $LogFile" -Level Warning
        Write-Host ""
        Write-Host "NEXT STEPS:" -ForegroundColor Yellow
        Write-Host "  1. Review the log file for error details" -ForegroundColor Gray
        Write-Host "  2. Address any failed installations manually" -ForegroundColor Gray
        Write-Host "  3. Re-run this script to verify" -ForegroundColor Gray
    }
    else {
        Write-Log "Bootstrap completed successfully!" -Level Success
        Write-Host ""
        Write-Host "NEXT STEPS:" -ForegroundColor Green
        Write-Host "  1. Open a new terminal to refresh environment variables" -ForegroundColor Gray
        Write-Host "  2. Navigate to the MeridianConsole repository" -ForegroundColor Gray
        Write-Host "  3. Run: dotnet build" -ForegroundColor Gray
        Write-Host "  4. Run: dotnet test" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Local infrastructure should be running at:" -ForegroundColor Cyan
        Write-Host "  - PostgreSQL: localhost:5432 (user: dhadgar, pass: dhadgar)" -ForegroundColor Gray
        Write-Host "  - RabbitMQ:   localhost:5672 (user: dhadgar, pass: dhadgar)" -ForegroundColor Gray
        Write-Host "  - RabbitMQ UI: http://localhost:15672" -ForegroundColor Gray
        Write-Host "  - Redis:      localhost:6379 (pass: dhadgar)" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "Log file: $LogFile" -ForegroundColor DarkGray
    Write-Host ""
}

#endregion
