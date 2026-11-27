Set-StrictMode -Version Latest

$script:HistoryCases = @('Flow', 'Filters', 'PrintPaging', 'Reload', 'BranchRestart', 'SourceFailure', 'StaleRead')

function Get-HistoryCasePlan {
    param([AllowEmptyCollection()][string[]]$Cases = $script:HistoryCases)
    if (-not $Cases -or $Cases.Count -eq 0) { throw 'Select at least one history case.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($case in $Cases) {
        if ($case -cnotin $script:HistoryCases) { throw 'Unknown or noncanonical history case.' }
        if ($seen.Add($case)) { $case }
    }
}

function Get-HistoryReportPath {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Root)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath($Path)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $full.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, $comparison) -or
        [IO.Path]::GetFileName($full) -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.json$' -or
        [IO.Path]::GetFileName($full) -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') {
        throw 'Report must be a named JSON file under the allowed report directory.'
    }
    if (Test-Path -LiteralPath $full) { throw 'Report already exists.' }
    $parent = [IO.Path]::GetDirectoryName($full)
    $candidate = $parent
    while ($candidate.Length -ge $rootPath.Length) {
        if (Test-Path -LiteralPath $candidate) {
            $item = Get-Item -LiteralPath $candidate -Force
            if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw 'Report directory must not traverse a file or link.'
            }
        }
        if ($candidate.Equals($rootPath, $comparison)) { break }
        $candidate = [IO.Path]::GetDirectoryName($candidate)
    }
    [void][IO.Directory]::CreateDirectory($parent)
    return $full
}

function ConvertTo-HistoryCaseResult {
    param([Parameter(Mandatory)][string]$Case, [int]$ExitCode,
        [AllowEmptyCollection()][string[]]$Lines = @(), [double]$Seconds)
    $evidence = @($Lines | Where-Object { $_.StartsWith('HISTORY_RESULT_JSON: ', [StringComparison]::Ordinal) })
    $valid = $false
    $version = $null
    $phase = 'RunnerProtocol'
    $assertions = 0
    $readOnly = $false
    if ($evidence.Count -eq 1) {
        try {
            $document = [Text.Json.JsonDocument]::Parse($evidence[0].Substring('HISTORY_RESULT_JSON: '.Length))
            try {
                if ($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'Invalid runner object.' }
                $names = @($document.RootElement.EnumerateObject() | ForEach-Object Name)
                $required = @('Case', 'State', 'EdgeVersion', 'ReadOnly', 'Phase', 'Assertions')
                if ($names.Count -ne $required.Count -or @($names | Select-Object -Unique).Count -ne $required.Count -or
                    @($names | Where-Object { $_ -cnotin $required }).Count -gt 0) { throw 'Invalid runner fields.' }
            } finally { $document.Dispose() }
            $raw = $evidence[0].Substring('HISTORY_RESULT_JSON: '.Length) | ConvertFrom-Json -AsHashtable -ErrorAction Stop
            if ($raw -isnot [Collections.IDictionary] -or $raw.Case -cne $Case -or
                $raw.State -cnotin @('Passed', 'Failed') -or $raw.ReadOnly -isnot [bool] -or
                $raw.Assertions -isnot [long] -or $raw.Assertions -lt 0 -or $raw.Assertions -gt [int]::MaxValue -or
                $raw.Phase -isnot [string] -or $raw.Phase -cnotmatch '^[A-Za-z][A-Za-z0-9._-]{0,63}$' -or
                ($null -ne $raw.EdgeVersion -and ($raw.EdgeVersion -isnot [string] -or $raw.EdgeVersion -cnotmatch '^[1-9][0-9]{0,3}\.[0-9]{1,10}\.[0-9]{1,10}\.[0-9]{1,10}$'))) {
                throw 'Invalid runner evidence.'
            }
            $version = $raw.EdgeVersion
            $phase = $raw.Phase
            $assertions = [int]$raw.Assertions
            $readOnly = $raw.ReadOnly
            $valid = $ExitCode -eq 0 -and $raw.State -ceq 'Passed' -and $readOnly -and
                $null -ne $version -and $assertions -gt 0
            if ($ExitCode -eq 0 -and -not $valid) { $phase = 'RunnerProtocol' }
        } catch { $phase = 'RunnerProtocol'; $version = $null; $assertions = 0; $readOnly = $false }
    }
    $effective = if ($valid) { 0 } elseif ($ExitCode -ne 0) { $ExitCode } else { 1 }
    return [pscustomobject]@{
        Case = $Case
        State = if ($valid) { 'Passed' } else { 'Failed' }
        ExitCode = $effective
        RunnerExitCode = $ExitCode
        Seconds = [double][Math]::Round([Math]::Max(0, $Seconds), 2)
        EdgeVersion = $version
        ReadOnly = $readOnly
        Phase = $phase
        Assertions = $assertions
    }
}

function Assert-HistoryReport {
    param([Parameter(Mandatory)]$Report)
    foreach ($field in @('SchemaVersion', 'Selected', 'Attempted', 'Passed', 'Failed')) {
        if (-not (Test-HistoryInteger $Report.$field) -or $Report.$field -lt 0) { throw 'Invalid history report number.' }
    }
    if ($Report.SchemaVersion -ne 1 -or $Report.State -cnotin @('Running', 'Completed', 'StoppedOnFailure', 'BuildFailed') -or
        $Report.Commit -isnot [string] -or $Report.Commit -cnotmatch '^[0-9a-f]{40}$' -or $Report.WorkingTreeClean -isnot [bool] -or
        $Report.ContinueOnFailure -isnot [bool]) { throw 'Invalid history report provenance.' }
    $timestamps = @()
    foreach ($date in @($Report.StartedUtc, $Report.UpdatedUtc)) {
        $parsed = [DateTimeOffset]::MinValue
        if ($date -isnot [string] -or -not [DateTimeOffset]::TryParseExact($date, "yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$parsed)) { throw 'Invalid history report timestamp.' }
        $timestamps += $parsed
    }
    if ($timestamps[1] -lt $timestamps[0]) { throw 'Invalid history report timestamp order.' }
    if ($Report.PlannedCases -isnot [Array] -or $Report.Cases -isnot [Array]) { throw 'Invalid history report arrays.' }
    $plan = @(Get-HistoryCasePlan -Cases @($Report.PlannedCases))
    if ($plan.Count -ne @($Report.PlannedCases).Count -or $Report.Selected -ne $plan.Count -or
        $Report.Attempted -ne @($Report.Cases).Count -or $Report.Attempted -gt $Report.Selected) {
        throw 'Invalid history report plan.'
    }
    $passed = 0
    for ($index = 0; $index -lt $Report.Attempted; $index++) {
        $result = $Report.Cases[$index]
        if ($result.Case -cne $plan[$index] -or -not (Test-HistoryInteger $result.ExitCode) -or -not (Test-HistoryInteger $result.RunnerExitCode) -or
            $result.State -cnotin @('Passed', 'Failed') -or $result.ReadOnly -isnot [bool] -or
            $result.Phase -cnotmatch '^[A-Za-z][A-Za-z0-9._-]{0,63}$' -or
            -not (Test-HistoryInteger $result.Assertions) -or $result.Assertions -lt 0 -or
            ($result.Seconds -isnot [double] -and $result.Seconds -isnot [long] -and $result.Seconds -isnot [int]) -or
            -not [double]::IsFinite($result.Seconds) -or $result.Seconds -lt 0) {
            throw 'Invalid history report case.'
        }
        if ($result.ExitCode -eq 0) {
            if ($result.State -cne 'Passed' -or $result.RunnerExitCode -ne 0 -or -not $result.ReadOnly -or
                $result.EdgeVersion -cnotmatch '^[1-9][0-9]{0,3}\.[0-9]{1,10}\.[0-9]{1,10}\.[0-9]{1,10}$' -or $result.Assertions -lt 1) {
                throw 'Invalid passing history evidence.'
            }
            $passed++
        } elseif ($result.State -cne 'Failed') { throw 'Invalid failed history evidence.' }
        if (-not $Report.ContinueOnFailure -and $result.ExitCode -ne 0 -and $index -ne $Report.Attempted - 1) {
            throw 'Stopped report contains cases after failure.'
        }
    }
    if ($Report.Passed -ne $passed -or $Report.Failed -ne $Report.Attempted - $passed -or
        ($Report.State -ceq 'Completed' -and $Report.Attempted -ne $Report.Selected) -or
        ($Report.State -ceq 'StoppedOnFailure' -and ($Report.ContinueOnFailure -or $Report.Failed -lt 1 -or $Report.Attempted -ge $Report.Selected)) -or
        ($Report.State -ceq 'Running' -and $Report.Attempted -ge $Report.Selected) -or
        ($Report.State -ceq 'BuildFailed' -and $Report.Attempted -ne 0)) { throw 'Invalid history report totals or state.' }
}

function Test-HistoryInteger($Value) {
    return ($Value -is [int] -or $Value -is [long]) -and $Value -ge [int]::MinValue -and $Value -le [int]::MaxValue
}

function Write-HistoryReport {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)]$Report, [switch]$Initial)
    Assert-HistoryReport $Report
    $temp = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $created = $false
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Report | ConvertTo-Json -Depth 7).Replace("`r`n", "`n") + "`n")
        $stream = [IO.FileStream]::new($temp, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $created = $true
        try { $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
        [IO.File]::Move($temp, $Path, -not $Initial)
    } finally { if ($created -and [IO.File]::Exists($temp)) { [IO.File]::Delete($temp) } }
}

function Invoke-HistoryAcceptance {
    param([Parameter(Mandatory)][string[]]$Cases, [Parameter(Mandatory)][string]$Commit,
        [Parameter(Mandatory)][bool]$WorkingTreeClean, [Parameter(Mandatory)][scriptblock]$Build,
        [Parameter(Mandatory)][scriptblock]$RunCase, [string]$ReportPath, [switch]$ContinueOnFailure, [switch]$Quiet)
    $plan = @(Get-HistoryCasePlan $Cases)
    $started = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
    $results = [Collections.Generic.List[object]]::new()
    $report = [pscustomobject]@{
        SchemaVersion = 1; State = 'Running'; Commit = $Commit; WorkingTreeClean = $WorkingTreeClean
        StartedUtc = $started; UpdatedUtc = $started; ContinueOnFailure = [bool]$ContinueOnFailure
        PlannedCases = $plan; Selected = $plan.Count; Attempted = 0; Passed = 0; Failed = 0; Cases = @()
    }
    Assert-HistoryReport $report
    if ($ReportPath) { Write-HistoryReport $ReportPath $report -Initial }
    $built = $false
    try { $buildResult = @(& $Build); $built = $buildResult.Count -eq 1 -and $buildResult[0] -is [bool] -and $buildResult[0] } catch { $built = $false }
    if (-not $built) {
        $report.State = 'BuildFailed'; $report.UpdatedUtc = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
        if ($ReportPath) { Write-HistoryReport $ReportPath $report }
        return [pscustomobject]@{ ExitCode = 1; Report = $report }
    }
    foreach ($case in $plan) {
        $elapsed = [Diagnostics.Stopwatch]::StartNew()
        try {
            $native = & $RunCase $case
            if ($null -eq $native -or $native.ExitCode -isnot [int]) { throw 'Missing runner exit code.' }
            $result = ConvertTo-HistoryCaseResult -Case $case -ExitCode $native.ExitCode -Lines @($native.Lines) -Seconds $elapsed.Elapsed.TotalSeconds
        } catch { $result = ConvertTo-HistoryCaseResult -Case $case -ExitCode 1 -Seconds $elapsed.Elapsed.TotalSeconds }
        $results.Add($result)
        $report.Cases = $results.ToArray(); $report.Attempted = $results.Count
        $report.Passed = @($report.Cases | Where-Object ExitCode -eq 0).Count; $report.Failed = $report.Attempted - $report.Passed
        $stop = $result.ExitCode -ne 0 -and -not $ContinueOnFailure
        $report.State = if ($report.Attempted -eq $report.Selected) { 'Completed' } elseif ($stop) { 'StoppedOnFailure' } else { 'Running' }
        $report.UpdatedUtc = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
        Assert-HistoryReport $report
        if ($ReportPath) { Write-HistoryReport $ReportPath $report }
        if (-not $Quiet) { Write-Host "Edge cash history $case`: $($result.State)" }
        if ($stop) { break }
    }
    return [pscustomobject]@{ ExitCode = if ($report.Passed -eq $report.Selected) { 0 } else { 1 }; Report = $report }
}

Export-ModuleMember -Function Get-HistoryCasePlan, Get-HistoryReportPath, ConvertTo-HistoryCaseResult, Assert-HistoryReport, Write-HistoryReport, Invoke-HistoryAcceptance
