#requires -Version 7.0
<#
.SYNOPSIS
Installs the meridian-lab daemon for the current user.

.DESCRIPTION
Publishes Dhadgar.Lab to %LOCALAPPDATA%\meridian-lab\bin, writes Lab:RepoRoot into the published labsettings.json,
creates MERIDIAN_LAB_TOKEN in the user environment when it is empty, then calls daemon.ps1 -Install.
The script refuses to run elevated ([Lab.Elevated]). It runs repository build code (dotnet publish) and writes
into a directory that a medium-integrity process of the same user can change, so an elevated session would hand
that process administrator rights. The scheduled task it registers always runs as the logged-on user with the
Limited run level, so the daemon never runs elevated. There is no machine-wide switch.

.PARAMETER Preflight
Safe mode. Checks the repository, the tools and the token state, prints the plan and changes nothing.
#>
[CmdletBinding()]
param(
    [switch]$Preflight
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Mirrors LabDefaults.TokenEnvVar in src\Tools\Dhadgar.Lab.
$tokenVar = 'MERIDIAN_LAB_TOKEN'

function Exit-Lab {
    param([string]$Message, [int]$Code = 1)
    [Console]::Error.WriteLine($Message)
    exit $Code
}

if (-not $IsWindows) {
    Exit-Lab '[Lab.Unavailable] install.ps1 installs a Windows daemon' 1
}

# R-PATH: refuse to run unless this script sits in <repo>\tools\lab.
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$solution = Join-Path $repoRoot 'Dhadgar.sln'
$project = Join-Path $repoRoot 'src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj'
if (-not (Test-Path -LiteralPath $solution -PathType Leaf) -or -not (Test-Path -LiteralPath $project -PathType Leaf)) {
    Exit-Lab '[Lab.PathRefused] tools\lab must sit inside the Meridian repository (Dhadgar.sln or src\Tools\Dhadgar.Lab not found)' 2
}

if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    Exit-Lab '[Lab.Unavailable] LOCALAPPDATA is not set' 1
}

$binDir = Join-Path $env:LOCALAPPDATA 'meridian-lab\bin'
$settingsPath = Join-Path $binDir 'labsettings.json'
$daemonScript = Join-Path $PSScriptRoot 'daemon.ps1'

$dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    Exit-Lab '[Lab.Unavailable] dotnet was not found on PATH' 1
}

# Only the state of the token is reported; its value is never read into output.
$tokenPresent = -not [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($tokenVar, 'User'))

# Elevation rule: install.ps1 never runs elevated. Only the state of the session is read here.
$isElevated = $false
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $isElevated = [Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
finally {
    $identity.Dispose()
}

if ($Preflight) {
    Write-Output "preflight: repository  $repoRoot"
    Write-Output "preflight: project     $project"
    Write-Output "preflight: publish to  $binDir"
    Write-Output "preflight: dotnet      $($dotnet.Source)"
    Write-Output "preflight: $tokenVar in user environment: $(if ($tokenPresent) { 'present (kept)' } else { 'empty (would create)' })"
    Write-Output 'preflight: elevation rule: install.ps1 refuses to run elevated [Lab.Elevated]; run it from a normal PowerShell 7 window'
    Write-Output "preflight: this session  $(if ($isElevated) { 'elevated (a real run would be refused)' } else { 'not elevated' })"
    Write-Output 'preflight: would generate the token in PowerShell (32 random bytes, base64url) when it is empty'
    Write-Output 'preflight: would write Lab:RepoRoot into the published labsettings.json'
    Write-Output 'preflight: would call daemon.ps1 -Install (logon task, Limited run level)'
    Write-Output 'preflight: would stop a running MeridianLabDaemon before publishing'
    Write-Output 'preflight: nothing changed'
    exit 0
}

if ($isElevated) {
    Exit-Lab '[Lab.Elevated] install.ps1 must run from a non-elevated PowerShell 7 window; nothing was changed' 1
}

$existing = Get-ScheduledTask -TaskName 'MeridianLabDaemon' -ErrorAction SilentlyContinue
if ($null -ne $existing -and $existing.State -eq 'Running') {
    Stop-ScheduledTask -TaskName 'MeridianLabDaemon'
    Start-Sleep -Seconds 2
}

$publishArgs = @('publish', $project, '-c', 'Release', '-o', $binDir)
& $dotnet.Source @publishArgs
if ($LASTEXITCODE -ne 0) {
    Exit-Lab "[Lab.Build] dotnet publish failed with exit code $LASTEXITCODE" 1
}

if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    Exit-Lab '[Lab.Build] the published labsettings.json is missing' 1
}

$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$settings.Lab | Add-Member -NotePropertyName 'RepoRoot' -NotePropertyValue $repoRoot -Force
$json = $settings | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($settingsPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Write-Output 'wrote Lab:RepoRoot into the published labsettings.json'

if ($tokenPresent) {
    Write-Output "$tokenVar is already set in the user environment; kept"
}
else {
    # The token is generated here, not by the published binary, so no binary runs before the user can review the plan.
    $tokenBytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    try {
        # Base64url without padding: a 32-byte token is 43 characters.
        $token = [Convert]::ToBase64String($tokenBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }
    finally {
        [Array]::Clear($tokenBytes, 0, $tokenBytes.Length)
    }

    if ($token -notmatch '^[A-Za-z0-9_-]{43}$') {
        $token = $null
        Exit-Lab '[Lab.Internal] the generated token has an unexpected shape' 1
    }

    [Environment]::SetEnvironmentVariable($tokenVar, $token, 'User')
    $token = $null
    Remove-Variable -Name token -ErrorAction SilentlyContinue
    Write-Output "stored $tokenVar in the user environment"
}

& $daemonScript -Install
if ($LASTEXITCODE -ne 0) {
    Exit-Lab '[Lab.Internal] daemon.ps1 -Install failed' 1
}

Write-Output 'install complete; restart your terminal so Claude Code sees MERIDIAN_LAB_TOKEN'
Write-Output 'the daemon starts at your next logon; run tools\lab\daemon.ps1 -Start to start it now'
exit 0
