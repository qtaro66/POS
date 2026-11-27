Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CashHistoryAcceptance.psm1') -Force
$PSDefaultParameterValues = @{ 'Invoke-HistoryAcceptance:Quiet' = $true }
$count = 0
function Check([string]$Name, [scriptblock]$Assertion) {
    & $Assertion
    $script:count++
}
function Equal($Expected, $Actual) {
    if (($Expected | ConvertTo-Json -Depth 8 -Compress) -cne ($Actual | ConvertTo-Json -Depth 8 -Compress)) { throw 'Self-test equality failed.' }
}
function Reject([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test expected rejection.' }
}
function Evidence([string]$Case = 'Flow', [string]$State = 'Passed', $Version = '143.0.3650.139', [bool]$ReadOnly = $true, [int]$Assertions = 12) {
    return 'HISTORY_RESULT_JSON: ' + (@{ Case = $Case; State = $State; EdgeVersion = $Version; ReadOnly = $ReadOnly; Assertions = $Assertions; Phase = 'Complete' } | ConvertTo-Json -Compress)
}
$root = Join-Path ([IO.Path]::GetTempPath()) ('pc-pos-history-wrapper-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
try {
    Check 'default plan' { Equal 7 @(Get-HistoryCasePlan).Count }
    Check 'stable deduplication' { Equal @('Reload', 'Flow') @(Get-HistoryCasePlan @('Reload', 'Flow', 'Reload')) }
    Check 'empty plan' { Reject { Get-HistoryCasePlan @() } }
    Check 'unknown plan' { Reject { Get-HistoryCasePlan @('Flow', 'Unknown') } }
    Check 'case-sensitive canonical plan' { Reject { Get-HistoryCasePlan @('flow') } }
    Check 'parent traversal' { Reject { Get-HistoryReportPath -Path (Join-Path $root '../outside.json') -Root $root } }
    Check 'source extension' { Reject { Get-HistoryReportPath -Path (Join-Path $root 'source.ps1') -Root $root } }
    Check 'reserved Windows output name' { Reject { Get-HistoryReportPath -Path (Join-Path $root 'CON.json') -Root $root } }
    Check 'root is not report' { Reject { Get-HistoryReportPath -Path $root -Root $root } }
    Check 'valid report path' { Equal (Join-Path $root 'nested/report.json') (Get-HistoryReportPath -Path (Join-Path $root 'nested/report.json') -Root $root) }
    Check 'native failure cannot pass' { Equal 7 (ConvertTo-HistoryCaseResult Flow 7 @((Evidence)) 1).ExitCode }
    Check 'exit zero without result cannot pass' { Equal 1 (ConvertTo-HistoryCaseResult Flow 0 @('secret discarded') 1).ExitCode }
    Check 'missing browser version cannot pass' { Equal 1 (ConvertTo-HistoryCaseResult Flow 0 @((Evidence -Version $null)) 1).ExitCode }
    Check 'unknown browser cannot pass' { Equal 1 (ConvertTo-HistoryCaseResult Flow 0 @((Evidence -Version 'unknown')) 1).ExitCode }
    Check 'missing read fingerprint cannot pass' { Equal 1 (ConvertTo-HistoryCaseResult Flow 0 @((Evidence -ReadOnly $false)) 1).ExitCode }
    Check 'zero assertions cannot pass' { Equal 1 (ConvertTo-HistoryCaseResult Flow 0 @((Evidence -Assertions 0)) 1).ExitCode }
    Check 'wrong case cannot pass' { Equal 1 (ConvertTo-HistoryCaseResult Flow 0 @((Evidence -Case 'Reload')) 1).ExitCode }
    Check 'failure result cannot pass' { Equal 1 (ConvertTo-HistoryCaseResult Flow 0 @((Evidence -State 'Failed')) 1).ExitCode }
    Check 'two evidence lines cannot pass' { Equal 1 (ConvertTo-HistoryCaseResult Flow 0 @((Evidence), (Evidence)) 1).ExitCode }
    Check 'duplicate JSON fields cannot pass' {
        $line = 'HISTORY_RESULT_JSON: {"Case":"Flow","Case":"Flow","State":"Passed","EdgeVersion":"143.0.3650.139","ReadOnly":true,"Assertions":12,"Phase":"Complete"}'
        Equal 1 (ConvertTo-HistoryCaseResult Flow 0 @($line) 1).ExitCode
    }
    Check 'valid pass preserves observed version' {
        $result = ConvertTo-HistoryCaseResult Flow 0 @('raw private noise', (Evidence)) 1.23
        Equal 0 $result.ExitCode; Equal '143.0.3650.139' $result.EdgeVersion
        if (($result | ConvertTo-Json -Compress).Contains('private')) { throw 'Raw output escaped into evidence.' }
    }
    $commit = 'a' * 40
    $pass = { param($case) [pscustomobject]@{ ExitCode = 0; Lines = @((Evidence -Case $case)) } }
    Check 'successful plan and atomic report round trip' {
        $file = Get-HistoryReportPath -Path (Join-Path $root 'pass.json') -Root $root
        $run = Invoke-HistoryAcceptance @('Flow', 'Reload') $commit $true { $true } $pass -ReportPath $file
        Equal 0 $run.ExitCode; Equal 'Completed' $run.Report.State; Equal 2 $run.Report.Passed
        Assert-HistoryReport (Get-Content -LiteralPath $file -Raw | ConvertFrom-Json -DateKind String)
        $bytes = [IO.File]::ReadAllBytes($file)
        if (($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) -or
            [Text.Encoding]::UTF8.GetString($bytes).Contains("`r")) { throw 'Invalid report encoding.' }
        Reject { Get-HistoryReportPath -Path $file -Root $root }
        Reject { Write-HistoryReport $file $run.Report -Initial }
    }
    Check 'stop on native failure retains complete plan' {
        $failed = { param($case) [pscustomobject]@{ ExitCode = 23; Lines = @((Evidence -Case $case -State 'Failed' -ReadOnly $false)) } }
        $run = Invoke-HistoryAcceptance @('Flow', 'Reload') $commit $true { $true } $failed
        Equal 1 $run.ExitCode; Equal 'StoppedOnFailure' $run.Report.State
        Equal 2 $run.Report.Selected; Equal 1 $run.Report.Attempted; Equal 23 $run.Report.Cases[0].ExitCode
    }
    Check 'continue on failure retains all outcomes' {
        $mixed = { param($case) [pscustomobject]@{ ExitCode = if ($case -eq 'Flow') { 9 } else { 0 }; Lines = @((Evidence -Case $case)) } }
        $run = Invoke-HistoryAcceptance @('Flow', 'Reload') $commit $true { $true } $mixed -ContinueOnFailure
        Equal 'Completed' $run.Report.State; Equal 2 $run.Report.Attempted; Equal 1 $run.Report.Passed; Equal 1 $run.ExitCode
    }
    Check 'build failure reports no attempted cases' {
        $run = Invoke-HistoryAcceptance @('Flow') $commit $false { $false } { throw 'Case must not run.' }
        Equal 1 $run.ExitCode; Equal 'BuildFailed' $run.Report.State; Equal 0 $run.Report.Attempted
    }
    Check 'ambiguous build output cannot pass' {
        $run = Invoke-HistoryAcceptance @('Flow') $commit $false { 'noise'; $false } $pass
        Equal 'BuildFailed' $run.Report.State
    }
    Check 'corrupt plan totals rejected' {
        $run = Invoke-HistoryAcceptance @('Flow') $commit $true { $true } $pass
        $run.Report.Selected = 2
        Reject { Assert-HistoryReport $run.Report }
    }
    Check 'corrupt successful browser version rejected' {
        $run = Invoke-HistoryAcceptance @('Flow') $commit $true { $true } $pass
        $run.Report.Cases[0].EdgeVersion = $null
        Reject { Assert-HistoryReport $run.Report }
    }
    Check 'report cannot reorder results' {
        $run = Invoke-HistoryAcceptance @('Flow', 'Reload') $commit $true { $true } $pass
        $run.Report.Cases[0].Case = 'Reload'
        Reject { Assert-HistoryReport $run.Report }
    }
    Check 'invalid commit rejected before build' {
        Reject { Invoke-HistoryAcceptance @('Flow') 'not-a-commit' $true { throw 'Must not run.' } $pass }
    }
    Check 'report number types cannot be strings' {
        $run = Invoke-HistoryAcceptance @('Flow') $commit $true { $true } $pass
        $run.Report.Passed = '1'
        Reject { Assert-HistoryReport $run.Report }
    }
    Check 'report array cannot become a string' {
        $run = Invoke-HistoryAcceptance @('Flow') $commit $true { $true } $pass
        $run.Report.PlannedCases = 'Flow'
        Reject { Assert-HistoryReport $run.Report }
    }
    Check 'timestamp order must be coherent' {
        $run = Invoke-HistoryAcceptance @('Flow') $commit $true { $true } $pass
        $run.Report.UpdatedUtc = '2020-01-01T00:00:00Z'
        Reject { Assert-HistoryReport $run.Report }
    }
    Check 'native nonzero propagation' {
        $native = @(& pwsh -NoProfile -Command 'exit 19' 2>&1)
        $code = $LASTEXITCODE
        Equal 19 (ConvertTo-HistoryCaseResult Flow $code @($native) 1).ExitCode
    }
    Write-Host "Cash history wrapper self-test: $count/$count passed"
    exit 0
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    $allowed = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar + 'pc-pos-history-wrapper-'
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe self-test cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
