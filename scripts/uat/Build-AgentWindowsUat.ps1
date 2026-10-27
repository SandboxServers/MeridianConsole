#Requires -Version 7.4
<#
.SYNOPSIS
    Builds what one Invoke-AgentWindowsUat.ps1 call needs without changing a tracked file (P32, D-S241).

.DESCRIPTION
    Each project is restored first, in locked mode and without a runtime identifier, and then built or
    published with --no-restore. A publish with -r win-x64 that restores for itself adds a net10.0/win-x64
    section to the lock files of the agent's RID-agnostic references (Agent.Core, Contracts, Shared). That
    dirties the tree, and the driver then refuses the agent run (D-S239). A lock file that does not match
    its project now fails the restore (NU1004) instead of being rewritten.

    Invoke-AgentWindowsUat.ps1 calls this script. -ListSteps prints the plan as JSON and runs nothing.
    The script never calls exit: every failure throws, and the wrapper turns it into exit 4.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('preflight', 'stack-only', 'run', 'cleanup')][string] $Verb,
    [ValidateSet('Process', 'Service', 'Msi')][string] $Mode,
    [string] $AgentOut,   # run with -Mode Process or Service: the folder the agent is published to
    [switch] $ListSteps
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$driverProject = Join-Path $repo 'tests\Dhadgar.Agent.Windows.Uat\Dhadgar.Agent.Windows.Uat.csproj'
$nodesProject = Join-Path $repo 'src\Dhadgar.Nodes\Dhadgar.Nodes.csproj'
$gatewayProject = Join-Path $repo 'src\Dhadgar.Gateway\Dhadgar.Gateway.csproj'
$agentProject = Join-Path $repo 'src\Agents\Dhadgar.Agent.Windows\Dhadgar.Agent.Windows.csproj'
$installerProject = Join-Path $repo 'src\Agents\Dhadgar.Agent.Windows\Installer\Dhadgar.Agent.Windows.Installer.wixproj'

if ($Verb -eq 'run' -and -not $Mode) {
    throw 'Build-AgentWindowsUat: -Verb run needs -Mode'
}
if ($Verb -eq 'run' -and $Mode -ne 'Msi' -and -not ($AgentOut -and [System.IO.Path]::IsPathFullyQualified($AgentOut))) {
    throw 'Build-AgentWindowsUat: -Mode Process or Service needs a fully qualified -AgentOut'
}

$plan = [System.Collections.Generic.List[object]]::new()

function Add-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)
    $plan.Add([pscustomobject]@{ Kind = 'dotnet'; Arguments = $Arguments; Path = $null })
}

function Add-Remove {
    param([Parameter(Mandatory)][string] $Path)
    $plan.Add([pscustomobject]@{ Kind = 'remove'; Arguments = @(); Path = $Path })
}

Add-DotNet -Arguments @('restore', $driverProject, '--locked-mode')
Add-DotNet -Arguments @('build', $driverProject, '-c', 'Release', '--no-restore')
if ($Verb -eq 'stack-only' -or $Verb -eq 'run') {
    Add-DotNet -Arguments @('restore', $nodesProject, '--locked-mode')
    Add-DotNet -Arguments @('build', $nodesProject, '-c', 'Release', '--no-restore')
    Add-DotNet -Arguments @('restore', $gatewayProject, '--locked-mode')
    Add-DotNet -Arguments @('build', $gatewayProject, '-c', 'Release', '--no-restore')
}
if ($Verb -eq 'run' -and $Mode -eq 'Msi') {
    # Stale output from an earlier build or branch is git-ignored and would be harvested into the MSI, so the
    # publish folder and the installer's Release output are deleted first. Both are checked to be under the repo.
    Add-Remove -Path (Join-Path (Split-Path -Parent $agentProject) 'bin\Release\net10.0\win-x64\publish')
    Add-Remove -Path (Join-Path (Split-Path -Parent $installerProject) 'bin\Release')
    # The MSI harvests the default publish folder (bin\Release\net10.0\win-x64\publish), so no -o (P23).
    Add-DotNet -Arguments @('restore', $agentProject, '--locked-mode')
    Add-DotNet -Arguments @('publish', $agentProject, '-c', 'Release', '-r', 'win-x64', '--no-restore')
    Add-DotNet -Arguments @('restore', $installerProject, '--locked-mode')
    Add-DotNet -Arguments @('build', $installerProject, '-c', 'Release', '--no-restore')
}
elseif ($Verb -eq 'run') {
    Add-DotNet -Arguments @('restore', $agentProject, '--locked-mode')
    Add-DotNet -Arguments @('publish', $agentProject, '-c', 'Release', '-r', 'win-x64', '--no-restore', '-o', $AgentOut)
}

if ($ListSteps) {
    ConvertTo-Json -InputObject $plan.ToArray() -Depth 4
    return
}

$repoPrefix = $repo.TrimEnd('\') + '\'
foreach ($step in $plan) {
    if ($step.Kind -eq 'remove') {
        $resolvedStale = [System.IO.Path]::GetFullPath($step.Path)
        if (-not $resolvedStale.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to delete ${resolvedStale}: it is not under $repo."
        }
        if (Test-Path -LiteralPath $resolvedStale) {
            Remove-Item -LiteralPath $resolvedStale -Recurse -Force
        }
        continue
    }

    $arguments = [string[]] $step.Arguments
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($arguments[0]) failed with exit code $LASTEXITCODE"
    }
}
