[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string[]]$Cases = @('Flow', 'Filters', 'PrintPaging', 'Reload', 'BranchRestart', 'SourceFailure', 'StaleRead'),
    [switch]$ListCases,
    [ValidateNotNullOrEmpty()][string]$ReportPath,
    [switch]$ContinueOnFailure,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CashHistoryAcceptance.psm1') -Force
if ($SelfTest) { & (Join-Path $PSScriptRoot 'Test-CashHistoryAcceptance.ps1'); exit $LASTEXITCODE }
$plan = @(Get-HistoryCasePlan $Cases)
if ($ListCases) { $plan; return }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $repo
try {
    $commit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine repository commit.' }
    $workingTreeClean = -not @(git status --porcelain --untracked-files=all).Count
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine repository state.' }
    $reportFile = if ($ReportPath) { Get-HistoryReportPath -Path $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ReportPath) -Root (Join-Path $repo '.handoff') }
    $npm = if ($IsWindows) { 'npm.cmd' } else { 'npm' }
    function Invoke-CapturedNative([string]$File, [string[]]$Arguments) {
        $PSNativeCommandUseErrorActionPreference = $false
        $lines = @(& $File @Arguments 2>&1 | ForEach-Object { "$_" })
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Lines = $lines }
    }
    $result = Invoke-HistoryAcceptance -Cases $plan -Commit $commit -WorkingTreeClean $workingTreeClean -ReportPath $reportFile -ContinueOnFailure:$ContinueOnFailure -Build {
        $web = Invoke-CapturedNative $npm @('run', 'build', '--workspace', 'apps/pos-web', '--', '--mode', 'browser-fixture')
        if ($web.ExitCode -ne 0) { return $false }
        $hostBuild = Invoke-CapturedNative 'dotnet' @('build', 'tests/Pos.PcPos.BrowserHost', '--verbosity', 'quiet')
        return $hostBuild.ExitCode -eq 0
    } -RunCase {
        param($case)
        Invoke-CapturedNative 'node' @('tests/Pos.PcPos.BrowserHost/browser-history.cjs', 'apps/pos-web/dist', $case)
    }
    Write-Host "Edge cash history: $($result.Report.Passed)/$($result.Report.Selected) passed"
    exit $result.ExitCode
} finally { Pop-Location }
