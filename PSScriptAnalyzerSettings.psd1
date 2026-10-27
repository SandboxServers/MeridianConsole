# PSScriptAnalyzer settings for every .ps1 in the repo (see docs/DEVELOPMENT_SETUP.md).
# No rules are excluded. CI enforces Error and ParseError only; Warning findings
# are reported by running Invoke-ScriptAnalyzer with this file locally.
@{
    Severity = @('ParseError', 'Error', 'Warning')
}
