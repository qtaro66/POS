#!/usr/bin/env pwsh
# SPDX-License-Identifier: MPL-2.0
<#
.SYNOPSIS
    The single verification gate for pos-platform.

.DESCRIPTION
    OpenAI runs this before every handoff, and CI (.github/workflows/verify.yml)
    runs the same script on windows-latest and ubuntu-latest. If it passes locally,
    the pull request checks should pass too.

    Checks, in order:
      0. working-tree     no uncommitted or untracked files (fails unless -AllowDirty)
      1. repo-hygiene     forbidden files tracked/staged, .handoff/ ignored
         whitespace       git diff --check against main (as in ci.yml)
      2. secret-scan      gitleaks (history + staged changes)
      3. license-headers  reuse lint (once REUSE.toml or LICENSES/ exists)
      4. dotnet-*         restore, format, build (-warnaserror), test (no hardware), vulnerable packages
      5. node-*           install, lint, typecheck, test, build, contracts:check, audit

    A step is SKIPPED only when the component does not exist yet (for example,
    no .sln before the backend is bootstrapped) or when it is listed in -Skip.
    A required tool that is missing is a FAILURE, never a silent skip.

    Hardware tests are never run here. .NET tests tagged [Trait("Category","Hardware")]
    are excluded; hardware tests are started manually with explicit operator confirmation.

.PARAMETER Skip
    Step names to skip on purpose (they are reported as SKIPPED (requested)).
    CI uses this on Windows for OS-independent steps already run on Linux.

.PARAMETER AllowDirty
    Run the checks on uncommitted work while iterating. The working-tree step is
    then reported as SKIPPED (requested), so the report is never valid for handoff.

.PARAMETER Quiet
    For agents: print one line per step instead of full tool output. A failing
    step also prints a short excerpt (error lines, then the last lines). Full
    output of every step is saved under .handoff/<branch-dir>/verify-logs/.
    The checks and the report are identical to a normal run.

.PARAMETER ReportPath
    Where to write the markdown report. Defaults to .handoff/<branch-slug>--<branch-id>/validation.md
    outside CI. Use '' to disable.

.EXAMPLE
    pwsh tools/verify.ps1
.EXAMPLE
    pwsh tools/verify.ps1 -Skip secret-scan,license-headers
#>
[CmdletBinding()]
param(
    [string[]]$Skip = @(),
    [string]$ReportPath,
    [switch]$AllowDirty,
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# When started by double-click or "Run with PowerShell", keep the window open
# so the result can be read. Agents and terminals start the script from another
# process, so they never wait. Set VERIFY_PAUSE=1 to force the pause.
function Test-LaunchedFromExplorer {
    try { return ((Get-Process -Id $PID).Parent.ProcessName -eq 'explorer') } catch { }
    try {
        $ppid = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId
        return ((Get-Process -Id $ppid).ProcessName -eq 'explorer')
    } catch { return $false }
}
function Exit-Script([int]$Code) {
    if ($env:VERIFY_PAUSE -eq '1' -or (Test-LaunchedFromExplorer)) {
        Write-Host "`nPress Enter to close this window..." -ForegroundColor Gray
        [void](Read-Host)
    }
    exit $Code
}

# Windows PowerShell 5.1 ("Run with PowerShell" on many PCs): hand over to pwsh 7 if installed.
if ($PSVersionTable.PSVersion.Major -lt 7 -and $PSBoundParameters.Count -eq 0 -and (Get-Command pwsh -ErrorAction SilentlyContinue)) {
    if (Test-LaunchedFromExplorer) { $env:VERIFY_PAUSE = '1' }
    & pwsh -NoProfile -File $PSCommandPath
    exit $LASTEXITCODE
}
$PSNativeCommandUseErrorActionPreference = $false

Write-Host "verify.ps1 starting (PowerShell $($PSVersionTable.PSVersion), in $((Get-Location).Path))" -ForegroundColor Cyan

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Host 'This script needs PowerShell 7. Run it with: pwsh tools/verify.ps1 (not powershell.exe).' -ForegroundColor Red
    Exit-Script 2
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Host 'git is not on PATH. Install Git for Windows or reopen the terminal after installing it.' -ForegroundColor Red
    Exit-Script 2
}
$repoRoot = (& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) {
    Write-Host "This copy of verify.ps1 is not inside a git working copy:" -ForegroundColor Red
    Write-Host "  $PSScriptRoot" -ForegroundColor Red
    Write-Host 'Copy the tools/ folder into the pos-platform repository (the folder that contains .git) and run it from there.' -ForegroundColor Red
    Exit-Script 2
}
Set-Location $repoRoot
Write-Host "Repository: $repoRoot" -ForegroundColor Cyan

# Captured before any step runs, so the report describes the committed state.
# Includes untracked files; ignored files (bin/, obj/, node_modules/, .handoff/) do not count.
$statusLines = @(& git status --porcelain --untracked-files=all)
$dirty = $statusLines.Count -gt 0

$isCI = [bool]$env:CI
$results = [System.Collections.Generic.List[object]]::new()

function Add-Result([string]$Name, [string]$Command, [string]$Status, [string]$Note, [double]$Seconds) {
    $results.Add([pscustomobject]@{ Name = $Name; Command = $Command; Status = $Status; Note = $Note; Seconds = [math]::Round($Seconds, 1) })
    $color = switch ($Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } default { 'Yellow' } }
    $suffix = if ($Note) { " - $Note" } else { '' }
    Write-Host ("[{0}] {1}{2}" -f $Status, $Name, $suffix) -ForegroundColor $color
}

# Handoff directory per AGENTS.md "Storage and identity":
# .handoff/<branch-slug>--<branch-id>/, branch-id = first 8 hex of SHA-256(UTF-8 branch name).
function Get-HandoffDir([string]$BranchName) {
    $slug = ([regex]::Replace($BranchName, '[^A-Za-z0-9._-]+', '-')).Trim('-').ToLowerInvariant()
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($BranchName)) } finally { $sha.Dispose() }
    $id = (-join ($bytes | ForEach-Object { $_.ToString('x2') })).Substring(0, 8)
    [pscustomobject]@{ Path = Join-Path '.handoff' "$slug--$id"; Id = $id }
}

function Test-Tool([string]$Name) { [bool](Get-Command $Name -ErrorAction SilentlyContinue) }

# Runs a native command, streams its output, and records PASS/FAIL from the exit code.
# -Check lets a step fail on output content even when the exit code is 0.
function Invoke-Step {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Display,
        [Parameter(Mandatory)][scriptblock]$Run,
        [string]$Tool,
        [scriptblock]$Check
    )
    if ($Skip -contains $Name) { Add-Result $Name $Display 'SKIPPED' 'requested with -Skip' 0; return }
    if ($Tool -and -not (Test-Tool $Tool)) { Add-Result $Name $Display 'FAIL' "required tool '$Tool' is not installed" 0; return }

    if (-not $Quiet) { Write-Host "`n==> $Name : $Display" -ForegroundColor Cyan }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $global:LASTEXITCODE = 0
    $captured = @()
    if ($Quiet) { $captured = @(& $Run *>&1) }
    else { & $Run *>&1 | Tee-Object -Variable captured | Out-Host }
    $code = $global:LASTEXITCODE
    $sw.Stop()
    $text = ($captured | Out-String -Width 400)
    if ($Quiet) { Save-StepLog $Name $text }

    $note = ''
    if ($code -eq 0 -and $Check) { $note = & $Check $text }
    if ($code -ne 0) { Add-Result $Name $Display 'FAIL' "exit code $code" $sw.Elapsed.TotalSeconds }
    elseif ($note) { Add-Result $Name $Display 'FAIL' $note $sw.Elapsed.TotalSeconds }
    else { Add-Result $Name $Display 'PASS' '' $sw.Elapsed.TotalSeconds }
    if ($Quiet -and $results[$results.Count - 1].Status -eq 'FAIL') { Write-Excerpt $Name $text }
}

# --- Quiet mode helpers --------------------------------------------------------
$script:logDir = $null
function Save-StepLog([string]$Name, [string]$Text) {
    if ($isCI) { return }
    if (-not $script:logDir) {
        $script:logDir = Join-Path (Get-HandoffDir (& git rev-parse --abbrev-ref HEAD)).Path 'verify-logs'
        New-Item -ItemType Directory -Force -Path $script:logDir | Out-Null
    }
    Set-Content -Path (Join-Path $script:logDir "$Name.log") -Value $Text -Encoding utf8NoBOM
}
# Error-looking lines first (max 25), then the last 15 lines, without duplicates.
function Write-Excerpt([string]$Name, [string]$Text) {
    $all = @($Text -split "`r?`n" | Where-Object { $_.Trim() })
    $pattern = '(?i)\berror\b|\bfailed\b|\bfail\b|exception|\bwarn(ing)?\b|: (CS|NU|MSB|NETSDK|IDE|CA)\d{4}|leaks? found|vulnerab|trailing whitespace|blank line at EOF'
    $picked = [System.Collections.Generic.List[string]]::new()
    foreach ($l in ($all | Where-Object { $_ -match $pattern } | Select-Object -First 25)) { if (-not $picked.Contains($l)) { $picked.Add($l) } }
    foreach ($l in ($all | Select-Object -Last 15)) { if (-not $picked.Contains($l)) { $picked.Add($l) } }
    foreach ($l in $picked) { Write-Host "    | $($l.TrimEnd())" -ForegroundColor DarkGray }
    if ($script:logDir) { Write-Host "    full log: $(Join-Path $script:logDir "$Name.log")" -ForegroundColor DarkGray }
}

function Skip-Absent([string]$Name, [string]$Display, [string]$Why) { Add-Result $Name $Display 'SKIPPED' $Why 0 }

# ---------------------------------------------------------------------------
# 0. Working tree must be clean: a report is only evidence for a commit
# ---------------------------------------------------------------------------
if (-not $dirty) {
    Add-Result 'working-tree' 'git status --porcelain --untracked-files=all' 'PASS' '' 0
} elseif ($AllowDirty) {
    Add-Result 'working-tree' 'git status --porcelain --untracked-files=all' 'SKIPPED' "requested with -AllowDirty ($($statusLines.Count) changed/untracked paths)" 0
} else {
    $statusLines | Select-Object -First 15 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Add-Result 'working-tree' 'git status --porcelain --untracked-files=all' 'FAIL' "$($statusLines.Count) uncommitted or untracked paths; commit first, or use -AllowDirty while iterating" 0
}

# ---------------------------------------------------------------------------
# 1. Repository hygiene (AGENTS.md rules + requirements section 3.7 / D21.1)
# ---------------------------------------------------------------------------
# Superset of the "Reject runtime and secret files" rule in .github/workflows/ci.yml,
# so anything CI rejects is rejected here first.
$forbidden = @(
    '^\.handoff/',
    '(^|/)(node_modules|bin|obj|data|backups|logs|secrets|dist|publish|TestResults)(/|$)',
    '\.(db|db-wal|db-shm|sqlite|sqlite3|db3)$',
    '-(wal|shm)$',
    '\.(pfx|p12|key|pem|jks|keystore|snk|mobileprovision)$',
    '\.(bak|backup|dump|log)$',
    '(^|/)\.env(\.[^/]*)?$'
)
$allowed = @('\.example$', '\.sample$', '\.template$')

if ($Skip -contains 'repo-hygiene') {
    Add-Result 'repo-hygiene' 'tracked/staged path rules' 'SKIPPED' 'requested with -Skip' 0
} else {
    $tracked = @(& git ls-files) + @(& git diff --cached --name-only --diff-filter=ACMR)
    $bad = $tracked | Sort-Object -Unique | Where-Object {
        $p = $_
        ($forbidden | Where-Object { $p -match $_ }) -and -not ($allowed | Where-Object { $p -match $_ })
    }
    & git check-ignore -q '.handoff/probe' 2>$null
    $handoffIgnored = ($LASTEXITCODE -eq 0)

    $problems = @()
    if ($bad) { $problems += "forbidden paths tracked or staged: $(@($bad | Select-Object -First 10) -join ', ')" }
    if (-not $handoffIgnored) { $problems += '.handoff/ is not in .gitignore' }
    if ($problems) {
        $bad | ForEach-Object { Write-Host "  forbidden: $_" -ForegroundColor Red }
        Add-Result 'repo-hygiene' 'tracked/staged path rules' 'FAIL' ($problems -join '; ') 0
    } else {
        Add-Result 'repo-hygiene' 'tracked/staged path rules' 'PASS' '' 0
    }
}

# Same whitespace rule as ci.yml ("git diff --check origin/main...HEAD"), plus uncommitted changes.
$diffBase = $null
foreach ($ref in 'origin/main', 'main') {
    $mb = (& git merge-base HEAD $ref 2>$null)
    if ($mb) { $diffBase = $mb; break }
}
if ($diffBase) {
    Invoke-Step -Name 'whitespace' -Tool 'git' -Display "git diff --check $($diffBase.Substring(0, 9))" `
        -Run { & git diff --check $diffBase }
} else {
    Skip-Absent 'whitespace' 'git diff --check' 'no main or origin/main to compare against'
}

# ---------------------------------------------------------------------------
# 2. Secret scan (gitleaks >= 8.19)
# ---------------------------------------------------------------------------
# Locally, scan only the commits this branch adds on top of main (fast).
# CI and runs on main scan the full history.
$scanRange = $null
if (-not $isCI) {
    foreach ($ref in 'origin/main', 'main') {
        $mb = (& git merge-base HEAD $ref 2>$null)
        if ($mb -and $mb -ne (& git rev-parse HEAD)) { $scanRange = "$mb..HEAD"; break }
    }
}
$scanCount = if ($scanRange) { & git rev-list --count $scanRange } else { & git rev-list --count HEAD 2>$null }
$scanLabel = if ($scanRange) { "branch commits ($scanCount)" } else { "full history ($scanCount commits)" }

Invoke-Step -Name 'secret-scan' -Tool 'gitleaks' `
    -Display "gitleaks git --redact [$scanLabel]; gitleaks git --staged --redact" `
    -Run {
        Write-Host "  scanning $scanLabel - large histories can take a few minutes..."
        if ($scanRange) { & gitleaks git --redact --no-banner -v "--log-opts=$scanRange" . }
        else { & gitleaks git --redact --no-banner -v . }
        if ($LASTEXITCODE -ne 0) { return }
        & gitleaks git --staged --redact --no-banner -v .
    }

# ---------------------------------------------------------------------------
# Requirements reading copy (tools/split-requirements.ps1) must match its source
# ---------------------------------------------------------------------------
$reqSources = @()
if ((Test-Path 'tools/split-requirements.ps1') -and (Test-Path 'docs/requirements')) {
    $reqSources = @(Get-ChildItem -Path 'docs/requirements' -File -Filter '*.md' | Sort-Object Name)
}
if ($reqSources.Count -eq 0) {
    Skip-Absent 'requirements-split' 'split-requirements.ps1 -Check' 'no split script or no requirements documents'
}
foreach ($src in $reqSources) {
    $rel = "docs/requirements/$($src.Name)"
    $stepName = 'requirements-split-' + ([IO.Path]::GetFileNameWithoutExtension($src.Name) -replace '[^A-Za-z0-9.]+', '-').ToLowerInvariant()
    Invoke-Step -Name $stepName -Tool 'pwsh' -Display "split-requirements.ps1 -Check -Source $rel" `
        -Run { & pwsh -NoProfile -File tools/split-requirements.ps1 -Check -Source $rel }.GetNewClosure()
}

# ---------------------------------------------------------------------------
# 3. License headers (D21.2 / D21.4: REUSE / SPDX)
# ---------------------------------------------------------------------------
if ((Test-Path 'REUSE.toml') -or (Test-Path 'LICENSES')) {
    Invoke-Step -Name 'license-headers' -Tool 'reuse' -Display 'reuse lint' -Run { & reuse lint }
} else {
    Skip-Absent 'license-headers' 'reuse lint' 'no REUSE.toml or LICENSES/ yet'
}

# ---------------------------------------------------------------------------
# 4. .NET (services/*, packages shared libraries)
# ---------------------------------------------------------------------------
$solution = Get-ChildItem -Path . -File | Where-Object Extension -in '.slnx', '.sln' | Sort-Object Name | Select-Object -First 1
if ($solution) {
    $sln = $solution.Name
    Invoke-Step -Name 'dotnet-restore' -Tool 'dotnet' -Display "dotnet restore $sln" -Run { & dotnet restore $sln }
    Invoke-Step -Name 'dotnet-format'  -Tool 'dotnet' -Display "dotnet format $sln --verify-no-changes" `
        -Run { & dotnet format $sln --verify-no-changes --no-restore }
    Invoke-Step -Name 'dotnet-build'   -Tool 'dotnet' -Display "dotnet build $sln -c Release -warnaserror" `
        -Run { & dotnet build $sln -c Release --no-restore -warnaserror }
    Invoke-Step -Name 'dotnet-test'    -Tool 'dotnet' -Display "dotnet test $sln --filter Category!=Hardware" `
        -Run { & dotnet test $sln -c Release --no-build --filter 'Category!=Hardware' }
    Invoke-Step -Name 'dotnet-vulnerable' -Tool 'dotnet' `
        -Display "dotnet list $sln package --vulnerable --include-transitive" `
        -Run { & dotnet list $sln package --vulnerable --include-transitive } `
        -Check { param($out) if ($out -match '\b(Critical|High)\b') { 'Critical/High vulnerable package found' } }
} else {
    Skip-Absent 'dotnet' 'dotnet restore/format/build/test' 'no .sln/.slnx at repository root yet'
}

# ---------------------------------------------------------------------------
# 5. Node workspaces (apps/*, packages/contracts, packages/ui)
# ---------------------------------------------------------------------------
if (Test-Path 'package.json') {
    $pm = if (Test-Path 'pnpm-lock.yaml') { 'pnpm' } elseif (Test-Path 'package-lock.json') { 'npm' } else { $null }
    if (-not $pm) {
        Add-Result 'node-install' 'lockfile' 'FAIL' 'package.json exists but no pnpm-lock.yaml or package-lock.json is committed' 0
    } else {
        $scripts = $null
        try { $scripts = (Get-Content 'package.json' -Raw | ConvertFrom-Json).scripts }
        catch { Add-Result 'node-package-json' 'parse package.json' 'FAIL' "package.json is not valid JSON: $($_.Exception.Message)" 0 }
        $has = { param($s) $scripts -and ($scripts.PSObject.Properties.Name -contains $s) }

        if ($pm -eq 'pnpm') {
            Invoke-Step -Name 'node-install' -Tool 'pnpm' -Display 'pnpm install --frozen-lockfile' -Run { & pnpm install --frozen-lockfile }
        } else {
            Invoke-Step -Name 'node-install' -Tool 'npm' -Display 'npm ci' -Run { & npm ci }
        }

        # 'contracts:check' = OpenAPI/schema compatibility check required by D21.1
        foreach ($s in 'lint', 'typecheck', 'test', 'build', 'contracts:check') {
            $stepName = 'node-' + ($s -replace ':', '-')
            if (& $has $s) {
                Invoke-Step -Name $stepName -Tool $pm -Display "$pm run $s" -Run { & $pm run $s }.GetNewClosure()
            } else {
                Skip-Absent $stepName "$pm run $s" "no '$s' script in root package.json"
            }
        }

        if ($pm -eq 'pnpm') {
            Invoke-Step -Name 'node-audit' -Tool 'pnpm' -Display 'pnpm audit --audit-level high' -Run { & pnpm audit --audit-level high }
        } else {
            Invoke-Step -Name 'node-audit' -Tool 'npm' -Display 'npm audit --audit-level=high' -Run { & npm audit --audit-level=high }
        }
    }
} else {
    Skip-Absent 'node' 'install/lint/typecheck/test/build' 'no root package.json yet'
}

# ---------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------
# Always Gregorian ISO 8601 and '.' decimals, whatever the machine culture (th-TH uses the Buddhist calendar).
$invariant = [System.Globalization.CultureInfo]::InvariantCulture
$head = (& git rev-parse HEAD 2>$null)
$branch = (& git rev-parse --abbrev-ref HEAD)
$failed = @($results | Where-Object Status -eq 'FAIL')
$overall = if ($failed.Count -gt 0) { 'FAIL' } else { 'PASS' }

$lines = @(
    "Branch: $branch",
    "Commit: $head",
    "Working tree clean: $(if ($dirty) { 'no (results do not describe a commit)' } else { 'yes' })",
    "OS: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)",
    "Generated: $([DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", $invariant))",
    "Overall: $overall",
    '',
    '| Check | Command | Result | Seconds |',
    '| --- | --- | --- | --- |'
)
foreach ($r in $results) {
    $res = if ($r.Note) { "$($r.Status): $($r.Note)" } else { $r.Status }
    $lines += "| $($r.Name) | ``$($r.Command)`` | $($res -replace '\|', '/') | $($r.Seconds.ToString('0.0', $invariant)) |"
}

if (-not $PSBoundParameters.ContainsKey('ReportPath') -and -not $isCI) {
    $ReportPath = Join-Path (Get-HandoffDir $branch).Path 'validation.md'
}
if ($ReportPath) {
    $reportDir = Split-Path $ReportPath -Parent
    if ($reportDir) { New-Item -ItemType Directory -Force -Path $reportDir | Out-Null }
    ($lines -join "`n") + "`n" | Set-Content -Path $ReportPath -Encoding utf8NoBOM -NoNewline
    Write-Host "`nReport written to $ReportPath"
}
if ($env:GITHUB_STEP_SUMMARY) { $lines -join "`n" | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8NoBOM }

Write-Host "`nOverall: $overall" -ForegroundColor $(if ($overall -eq 'PASS') { 'Green' } else { 'Red' })
if ($dirty) { Write-Host 'Working tree is not clean: this report is not valid for handoff. Commit, then run verify again.' -ForegroundColor Yellow }
Exit-Script $(if ($overall -eq 'PASS') { 0 } else { 1 })
