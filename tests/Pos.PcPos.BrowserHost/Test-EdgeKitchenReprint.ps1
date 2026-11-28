[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string[]]$Cases = @('Queue', 'RejectedPin', 'LostResponse', 'Unknown'),
    [switch]$ListCases,
    [ValidateNotNullOrEmpty()][string]$ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$allowed = @('Queue', 'RejectedPin', 'LostResponse', 'Unknown')
if (-not $Cases.Count -or @($Cases | Where-Object { $_ -cnotin $allowed }).Count -or
    @($Cases | Select-Object -Unique).Count -ne $Cases.Count) { throw 'Select unique canonical kitchen reprint cases.' }
if ($ListCases) { $Cases; return }
Import-Module (Join-Path $PSScriptRoot 'CashHistoryAcceptance.psm1') -Force
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $repo
try {
    $commit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Cannot determine repository commit.' }
    $clean = -not @(git status --porcelain --untracked-files=all).Count
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine repository state.' }
    $destination = if ($ReportPath) { Get-HistoryReportPath -Path $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ReportPath) -Root (Join-Path $repo '.handoff') }
    $invariant = [Globalization.CultureInfo]::InvariantCulture
    $started = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", $invariant)
    $results = [Collections.Generic.List[object]]::new()
    $state = 'BuildFailed'
    function Invoke-Captured([string]$File, [string[]]$Arguments) {
        $PSNativeCommandUseErrorActionPreference = $false
        $lines = @(& $File @Arguments 2>&1 | ForEach-Object { "$_" })
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Lines = $lines }
    }
    $npm = if ($IsWindows) { 'npm.cmd' } else { 'npm' }
    $built = Invoke-Captured $npm @('run', 'build', '--workspace', 'apps/pos-web', '--', '--mode', 'browser-fixture')
    if ($built.ExitCode -eq 0) { $built = Invoke-Captured 'dotnet' @('build', 'tests/Pos.PcPos.BrowserHost', '--verbosity', 'quiet') }
    if ($built.ExitCode -eq 0) {
        $state = 'Running'
        foreach ($case in $Cases) {
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $run = Invoke-Captured 'node' @('tests/Pos.PcPos.BrowserHost/browser-reprint.cjs', 'apps/pos-web/dist', $case)
            $watch.Stop()
            $evidence = @($run.Lines | Where-Object { $_.StartsWith('REPRINT_RESULT_JSON: ', [StringComparison]::Ordinal) })
            $value = $null
            try {
                if ($evidence.Count -ne 1) { throw 'Missing runner evidence.' }
                $json = $evidence[0].Substring('REPRINT_RESULT_JSON: '.Length)
                $document = [Text.Json.JsonDocument]::Parse($json)
                try {
                    if ($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'Invalid runner evidence.' }
                    $names = @($document.RootElement.EnumerateObject() | ForEach-Object Name)
                    $expected = @('Case', 'State', 'EdgeVersion', 'Phase', 'Assertions', 'OriginalBusinessUnchanged', 'HardwareFree')
                    if ($names.Count -ne $expected.Count -or @($names | Select-Object -Unique).Count -ne $expected.Count -or
                        @($names | Where-Object { $_ -cnotin $expected }).Count) { throw 'Invalid runner fields.' }
                } finally { $document.Dispose() }
                $raw = $json | ConvertFrom-Json -AsHashtable
                if ($raw.Case -cne $case -or $raw.State -cnotin @('Passed', 'Failed') -or
                    $raw.Phase -isnot [string] -or $raw.Phase -cnotmatch '^[A-Za-z][A-Za-z0-9._-]{0,63}$' -or
                    $raw.Assertions -isnot [long] -or $raw.Assertions -lt 0 -or $raw.Assertions -gt [int]::MaxValue -or
                    $raw.OriginalBusinessUnchanged -isnot [bool] -or $raw.HardwareFree -isnot [bool]) { throw 'Invalid runner types.' }
                $passed = $run.ExitCode -eq 0 -and $raw.State -ceq 'Passed' -and $raw.Phase -ceq 'completed' -and
                    $raw.Assertions -gt 0 -and $raw.OriginalBusinessUnchanged -and $raw.HardwareFree -and
                    $raw.EdgeVersion -is [string] -and $raw.EdgeVersion -cmatch '^[1-9][0-9]{0,3}\.[0-9]{1,10}\.[0-9]{1,10}\.[0-9]{1,10}$'
                $value = [ordered]@{ Case = $case; State = $(if ($passed) { 'Passed' } else { 'Failed' }); Phase = $raw.Phase;
                    Assertions = $raw.Assertions; EdgeVersion = $(if ($passed) { $raw.EdgeVersion } else { $null });
                    OriginalBusinessUnchanged = $passed; HardwareFree = $passed; RunnerExitCode = $run.ExitCode; Seconds = $watch.Elapsed.TotalSeconds }
            } catch {
                $value = [ordered]@{ Case = $case; State = 'Failed'; Phase = 'RunnerProtocol'; Assertions = 0;
                    EdgeVersion = $null; OriginalBusinessUnchanged = $false; HardwareFree = $false; RunnerExitCode = $run.ExitCode; Seconds = $watch.Elapsed.TotalSeconds }
            }
            $results.Add($value)
            Write-Host "Edge kitchen reprint ${case}: $($value.State) ($($value.Phase))"
            if ($value.State -cne 'Passed') { $state = 'StoppedOnFailure'; break }
        }
        if ($results.Count -eq $Cases.Count -and -not @($results | Where-Object State -cne 'Passed').Count) { $state = 'Completed' }
    }
    $passed = @($results | Where-Object State -ceq 'Passed').Count
    $report = [ordered]@{ SchemaVersion = 1; State = $state; Commit = $commit; WorkingTreeClean = $clean;
        StartedUtc = $started; UpdatedUtc = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", $invariant);
        PlannedCases = @($Cases); Selected = $Cases.Count; Attempted = $results.Count; Passed = $passed;
        Failed = $results.Count - $passed; Cases = @($results.ToArray()) }
    if ($destination) {
        $temporary = $destination + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($report | ConvertTo-Json -Depth 8).Replace("`r`n", "`n") + "`n")
        $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
        [IO.File]::Move($temporary, $destination)
    }
    Write-Host "Edge kitchen reprint: $passed/$($Cases.Count) passed"
    exit $(if ($state -ceq 'Completed') { 0 } else { 1 })
} finally { Pop-Location }
