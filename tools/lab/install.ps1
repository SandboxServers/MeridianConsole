#requires -Version 7.0
<#
.SYNOPSIS
Installs the meridian-lab daemon for the current user.

.DESCRIPTION
Publishes Dhadgar.Lab to %LOCALAPPDATA%\meridian-lab\bin, writes Lab:RepoRoot into the published labsettings.json,
creates MERIDIAN_LAB_TOKEN in the user environment when it is empty, then calls daemon.ps1 -Install.
The script may run elevated. The scheduled task it registers always runs as the logged-on user with the
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
$exe = Join-Path $binDir 'meridian-lab.exe'
$settingsPath = Join-Path $binDir 'labsettings.json'
$daemonScript = Join-Path $PSScriptRoot 'daemon.ps1'

$dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    Exit-Lab '[Lab.Unavailable] dotnet was not found on PATH' 1
}

# Only the state of the token is reported; its value is never read into output.
$tokenPresent = -not [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($tokenVar, 'User'))

if ($Preflight) {
    Write-Output "preflight: repository  $repoRoot"
    Write-Output "preflight: project     $project"
    Write-Output "preflight: publish to  $binDir"
    Write-Output "preflight: dotnet      $($dotnet.Source)"
    Write-Output "preflight: $tokenVar in user environment: $(if ($tokenPresent) { 'present (kept)' } else { 'empty (would create)' })"
    Write-Output 'preflight: would write Lab:RepoRoot into the published labsettings.json'
    Write-Output 'preflight: would call daemon.ps1 -Install (logon task, Limited run level)'
    Write-Output 'preflight: nothing changed'
    exit 0
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
    $tokenLines = & $exe token new
    if ($LASTEXITCODE -ne 0) {
        Exit-Lab "[Lab.Internal] meridian-lab token new failed with exit code $LASTEXITCODE" 1
    }

    $token = [string]($tokenLines | Select-Object -First 1)
    $tokenLines = $null
    # A 32-byte base64url token without padding is 43 characters.
    if ($token -notmatch '^[A-Za-z0-9_-]{43}$') {
        $token = $null
        Exit-Lab '[Lab.Internal] meridian-lab token new did not print a 43-character token' 1
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
exit 0
