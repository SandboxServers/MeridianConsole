#Requires -Version 7.4
<#
.SYNOPSIS
    Checks the service and secret wiring of a built agent MSI from its tables, without installing anything (P36, P37).

.DESCRIPTION
    The script copies the MSI to a temp file, opens the copy read-only and queries its tables. It never calls
    msiexec, opens no install session, writes nothing outside the temp copy and deletes that copy on exit.

    Exit codes: 0 = pass, 1 = a check failed, 2 = could not evaluate (report it as blocked).

    -Checks P36 (D-S244):
      S1. Exactly one ServiceInstall row, named DhadgarAgent.
      S2. Its component's KeyPath is a File row named Dhadgar.Agent.Windows.exe, and the component is not a
          registry or ODBC key path (Attributes bits 4 and 32 clear). Windows Installer installs a ServiceInstall row
          only when its component's key path is the service executable; otherwise InstallServices skips it silently
          and Wix4ExecServiceConfig fails with 0x80070424 (run 20261027-192738-7347).
      S3. Every Wix4ServiceConfig row for DhadgarAgent and every ServiceControl row for DhadgarAgent sit in that
          same component.

    -Checks P37 (D-S245):
      T1. ENROLLMENT_TOKEN is in MsiHiddenProperties and SecureCustomProperties.
      T2. No table cell formats a hidden property ("[NAME]"), except the Target of a CustomAction row that has
          msidbCustomActionTypeHideTarget (0x2000). Registry, IniFile, Environment and ServiceInstall values are
          written to the verbose log's script ops whether or not the property is hidden.
      T3. No Registry row is named EnrollmentToken.
      T4. Every CustomAction row whose Target formats a hidden property has the 0x2000 bit. When it is a type-51
          set-property action, the property it sets is in MsiHiddenProperties, and the action of that name is
          deferred (0x400), no-impersonate (0x800) and has the 0x2000 bit.
      T5. WriteEnrollmentToken runs after WriteRegistryValues and before InstallFinalize; RollbackEnrollmentToken
          (rollback, 0x100) runs before it; DeleteEnrollmentToken (deferred, no-impersonate) is conditioned on
          REMOVE.
      T6. The agent key's create row (Registry Name "+", Key SOFTWARE\Meridian Console\Agent) carries the
          protected SDDL in MsiLockPermissionsEx, and its component has no Condition, so the ACL applies on every
          install, with or without a token.

    Limit: tables prove what the installer is told to do, not what msiexec logs. The proof that no log holds the
    token is the UAT Msi run's U9 scan.
#>
param(
    [Parameter(Mandatory)][string] $MsiPath,
    [ValidateSet('P36', 'P37', 'All')][string] $Checks = 'All'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$serviceName = 'DhadgarAgent'
$serviceExe = 'Dhadgar.Agent.Windows.exe'
$agentKey = 'SOFTWARE\Meridian Console\Agent'
$agentSddl = 'D:P(A;OICI;KA;;;SY)(A;OICI;KA;;;BA)'
$hideTarget = 0x2000
$deferred = 0x400
$noImpersonate = 0x800
$rollback = 0x100

function Invoke-Com($obj, [string] $name, [System.Reflection.BindingFlags] $kind, [object[]] $a = @()) {
    return $obj.GetType().InvokeMember($name, $kind, $null, $obj, $a)
}
$M = [System.Reflection.BindingFlags]::InvokeMethod
$G = [System.Reflection.BindingFlags]::GetProperty

# Returns each row as a string array (one entry per column); stream columns read as '<stream>'.
function Get-Rows($db, [string] $sql) {
    $rows = [System.Collections.Generic.List[string[]]]::new()
    $view = Invoke-Com $db 'OpenView' $M @($sql)
    Invoke-Com $view 'Execute' $M @($null) | Out-Null
    while ($true) {
        $rec = Invoke-Com $view 'Fetch' $M
        if ($null -eq $rec) { break }
        $n = [int](Invoke-Com $rec 'FieldCount' $G)
        $cells = [string[]]::new($n)
        for ($i = 1; $i -le $n; $i++) {
            try { $cells[$i - 1] = [string](Invoke-Com $rec 'StringData' $G @($i)) } catch { $cells[$i - 1] = '<stream>' }
        }
        $rows.Add($cells)
    }
    Invoke-Com $view 'Close' $M | Out-Null
    return , $rows
}

function Test-Table($db, [string] $table) {
    return (Get-Rows $db "SELECT ``Name`` FROM ``_Tables`` WHERE ``Name`` = '$table'").Count -gt 0
}

if (-not (Test-Path -LiteralPath $MsiPath -PathType Leaf)) {
    "blocked: no MSI at $MsiPath"
    exit 2
}

[string] $copy = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'p36-' + [guid]::NewGuid().ToString('N') + '.msi')
Copy-Item -LiteralPath $MsiPath -Destination $copy
$failures = [System.Collections.Generic.List[string]]::new()
$code = 0
$installer = $null
$db = $null
try {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = Invoke-Com $installer 'OpenDatabase' $M @($copy, 0)   # msiOpenDatabaseModeReadOnly

    $components = @{}
    foreach ($r in (Get-Rows $db 'SELECT `Component`, `Attributes`, `Condition`, `KeyPath` FROM `Component`')) {
        $components[$r[0]] = [pscustomobject]@{ Attributes = [int]$r[1]; Condition = $r[2]; KeyPath = $r[3] }
    }
    $files = @{}
    foreach ($r in (Get-Rows $db 'SELECT `File`, `Component_`, `FileName` FROM `File`')) {
        $long = ($r[2] -split '\|')[-1]
        $files[$r[0]] = [pscustomobject]@{ Component = $r[1]; Name = $long }
    }

    if ($Checks -in 'P36', 'All') {
        $services = Get-Rows $db 'SELECT `ServiceInstall`, `Name`, `Component_` FROM `ServiceInstall`'
        "ServiceInstall rows: $($services.Count)"
        if ($services.Count -ne 1 -or $services[0][1] -cne $serviceName) {
            $failures.Add("S1: expected one ServiceInstall row named $serviceName, found $($services.Count)")
        }
        foreach ($s in $services) {
            $component = $s[2]
            $c = $components[$component]
            $keyFile = if ($null -ne $c -and $files.ContainsKey($c.KeyPath)) { $files[$c.KeyPath] } else { $null }
            "ServiceInstall $($s[1]) -> component $component, KeyPath '$($c.KeyPath)', Attributes $($c.Attributes)"
            if ($null -eq $keyFile -or $keyFile.Name -cne $serviceExe -or ($c.Attributes -band 4) -ne 0 -or ($c.Attributes -band 32) -ne 0) {
                $failures.Add("S2: the component of ServiceInstall $($s[1]) ($component) has key path '$($c.KeyPath)', not the File $serviceExe; Windows Installer skips the service install")
            }
            if (Test-Table $db 'Wix4ServiceConfig') {
                foreach ($sc in (Get-Rows $db 'SELECT `ServiceName`, `Component_` FROM `Wix4ServiceConfig`')) {
                    "Wix4ServiceConfig $($sc[0]) -> component $($sc[1])"
                    if ($sc[0] -ceq $s[1] -and $sc[1] -cne $component) {
                        $failures.Add("S3: Wix4ServiceConfig $($sc[0]) is in $($sc[1]), not in $component with the ServiceInstall")
                    }
                }
            }
            foreach ($ctl in (Get-Rows $db 'SELECT `ServiceControl`, `Name`, `Component_` FROM `ServiceControl`')) {
                "ServiceControl $($ctl[1]) -> component $($ctl[2])"
                if ($ctl[1] -ceq $s[1] -and $ctl[2] -cne $component) {
                    $failures.Add("S3: ServiceControl $($ctl[0]) is in $($ctl[2]), not in $component with the ServiceInstall")
                }
            }
        }
    }

    if ($Checks -in 'P37', 'All') {
        $props = @{}
        foreach ($r in (Get-Rows $db 'SELECT `Property`, `Value` FROM `Property`')) { $props[$r[0]] = $r[1] }
        $hidden = if ($props.ContainsKey('MsiHiddenProperties')) { @($props['MsiHiddenProperties'] -split ';' | Where-Object { $_ }) } else { @() }
        $secure = if ($props.ContainsKey('SecureCustomProperties')) { @($props['SecureCustomProperties'] -split ';' | Where-Object { $_ }) } else { @() }
        "MsiHiddenProperties = $($hidden -join ';')"
        if ($hidden -cnotcontains 'ENROLLMENT_TOKEN' -or $secure -cnotcontains 'ENROLLMENT_TOKEN') {
            $failures.Add('T1: ENROLLMENT_TOKEN is not in both MsiHiddenProperties and SecureCustomProperties')
        }

        $actions = @{}
        foreach ($r in (Get-Rows $db 'SELECT `Action`, `Type`, `Source`, `Target` FROM `CustomAction`')) {
            $actions[$r[0]] = [pscustomobject]@{ Type = [int]$r[1]; Source = $r[2]; Target = $r[3] }
        }

        # T2: every cell of every table, for "[HIDDEN]".
        $patterns = $hidden | ForEach-Object { '[' + $_ + ']' }
        foreach ($t in (Get-Rows $db 'SELECT `Name` FROM `_Tables`')) {
            $table = $t[0]
            if ($table -in 'Binary', 'Icon', 'MsiDigitalCertificate', 'MsiPatchHeaders', 'MsiFileHash') { continue }
            foreach ($row in (Get-Rows $db "SELECT * FROM ``$table``")) {
                foreach ($cell in $row) {
                    foreach ($p in $patterns) {
                        if (-not $cell.Contains($p, [System.StringComparison]::Ordinal)) { continue }
                        $allowed = $table -ceq 'CustomAction' -and $actions.ContainsKey($row[0]) -and ($actions[$row[0]].Type -band $hideTarget) -ne 0 -and $row[3] -ceq $cell
                        "cell formats ${p}: $table row '$($row[0])' allowed=$allowed"
                        if (-not $allowed) {
                            $failures.Add("T2: $table row '$($row[0])' formats $p; the verbose log writes it in plain text")
                        }
                    }
                }
            }
        }

        # T3
        foreach ($r in (Get-Rows $db 'SELECT `Registry`, `Name` FROM `Registry`')) {
            if ($r[1] -ceq 'EnrollmentToken') { $failures.Add("T3: Registry row $($r[0]) writes EnrollmentToken through the table") }
        }

        # T4
        foreach ($name in $actions.Keys) {
            $a = $actions[$name]
            $formatsHidden = @($patterns | Where-Object { $a.Target.Contains($_, [System.StringComparison]::Ordinal) }).Count -gt 0
            if (-not $formatsHidden) { continue }
            if (($a.Type -band $hideTarget) -eq 0) { $failures.Add("T4: $name formats a hidden property without HideTarget") }
            if (($a.Type -band 0x3F) -eq 51) {
                $target = $a.Source
                if ($hidden -cnotcontains $target) { $failures.Add("T4: $name sets $target, which is not in MsiHiddenProperties") }
                if ($actions.ContainsKey($target)) {
                    $d = $actions[$target]
                    "deferred $target Type $($d.Type)"
                    if (($d.Type -band $deferred) -eq 0 -or ($d.Type -band $noImpersonate) -eq 0 -or ($d.Type -band $hideTarget) -eq 0) {
                        $failures.Add("T4: $target must be deferred, no-impersonate and HideTarget; Type is $($d.Type)")
                    }
                }
            }
        }

        # T5
        $seq = @{}
        foreach ($r in (Get-Rows $db 'SELECT `Action`, `Condition`, `Sequence` FROM `InstallExecuteSequence`')) {
            $seq[$r[0]] = [pscustomobject]@{ Condition = $r[1]; Sequence = [int]$r[2] }
        }
        foreach ($n in 'WriteEnrollmentToken', 'RollbackEnrollmentToken', 'DeleteEnrollmentToken') {
            if (-not $seq.ContainsKey($n) -or -not $actions.ContainsKey($n)) { $failures.Add("T5: $n is not defined and sequenced") }
        }
        if ($failures.Where({ $_.StartsWith('T5') }).Count -eq 0) {
            $w = $seq['WriteEnrollmentToken'].Sequence
            "WriteRegistryValues $($seq['WriteRegistryValues'].Sequence), RollbackEnrollmentToken $($seq['RollbackEnrollmentToken'].Sequence), WriteEnrollmentToken $w, InstallFinalize $($seq['InstallFinalize'].Sequence)"
            if (-not ($w -gt $seq['WriteRegistryValues'].Sequence -and $w -lt $seq['InstallFinalize'].Sequence)) {
                $failures.Add('T5: WriteEnrollmentToken is not between WriteRegistryValues and InstallFinalize')
            }
            if (-not ($seq['RollbackEnrollmentToken'].Sequence -lt $w) -or ($actions['RollbackEnrollmentToken'].Type -band $rollback) -eq 0) {
                $failures.Add('T5: RollbackEnrollmentToken is not a rollback action sequenced before WriteEnrollmentToken')
            }
            $del = $actions['DeleteEnrollmentToken']
            if (($del.Type -band $deferred) -eq 0 -or ($del.Type -band $noImpersonate) -eq 0 -or $seq['DeleteEnrollmentToken'].Condition -cnotmatch 'REMOVE') {
                $failures.Add('T5: DeleteEnrollmentToken is not a deferred no-impersonate action conditioned on REMOVE')
            }
        }

        # T6
        # Get-Rows returns the List wrapped in an array; take the List before filtering its rows.
        $registryRows = Get-Rows $db 'SELECT `Registry`, `Root`, `Key`, `Name`, `Component_` FROM `Registry`'
        $createRows = @($registryRows.Where({ $_[1] -eq '2' -and $_[2] -ceq $agentKey -and $_[3] -ceq '+' }))
        $lockRows = if (Test-Table $db 'MsiLockPermissionsEx') { Get-Rows $db 'SELECT `LockObject`, `Table`, `SDDLText`, `Condition` FROM `MsiLockPermissionsEx`' } else { [System.Collections.Generic.List[string[]]]::new() }
        $ok = $false
        foreach ($cr in $createRows) {
            $lock = @($lockRows.Where({ $_[0] -ceq $cr[0] -and $_[1] -ceq 'Registry' -and $_[2] -ceq $agentSddl }))
            $cond = $components[$cr[4]].Condition
            "agent key create row $($cr[0]) in $($cr[4]) (Condition '$cond'), locks $($lock.Count)"
            if ($lock.Count -gt 0 -and [string]::IsNullOrEmpty($cond)) { $ok = $true }
        }
        if (-not $ok) { $failures.Add("T6: no unconditional component creates $agentKey with the protected SDDL") }
    }

    foreach ($f in $failures) { "FAIL: $f" }
    "failures = $($failures.Count)"
    $code = if ($failures.Count -eq 0) { 0 } else { 1 }
}
catch {
    "blocked: could not evaluate: $($_.Exception.GetType().Name): $($_.Exception.Message)"
    $code = 2
}
finally {
    if ($null -ne $db) { [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) | Out-Null }
    if ($null -ne $installer) { [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null }
    $db = $null; $installer = $null
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()
    Remove-Item -LiteralPath $copy -Force -ErrorAction SilentlyContinue
}
exit $code
