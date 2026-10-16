#!/usr/bin/env pwsh
# SPDX-License-Identifier: MPL-2.0
<#
.SYNOPSIS
    Antigravity's pre-push gate: confirms the handoff matches the repository
    before anything leaves the machine.

.DESCRIPTION
    Reads .handoff/<branch-slug>--<branch-id>/to-antigravity.md and validation.md and checks:
      - Status is READY and Branch matches the checked-out branch
      - Final local commit == head of the named local branch (not main); clean tree if checked out
      - Base commit is an ancestor of that branch
      - validation.md was produced by tools/verify.ps1 on this exact commit and passed
      - no .handoff/ path is tracked
    It also lists changed paths in high-risk areas (requirements D21.3: Contract,
    Payment, Security, Printing). Those PRs need user approval before merge.

    With -WritePr, it extracts the suggested PR title and body into
    .handoff/<branch-slug>--<branch-id>/pr-title.txt and pr-body.md for:
        gh pr create --title "$(Get-Content pr-title.txt)" --body-file pr-body.md

    Exit codes: 0 = ready to push, 1 = mismatch (stop and report), 2 = usage error.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Branch,   # mandatory; checked below so agents get a clear error instead of a prompt
    [switch]$WritePr
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

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Host 'This script needs PowerShell 7. Run it with: pwsh tools/check-handoff.ps1' -ForegroundColor Red
    Exit-Script 2
}
$repoRoot = (& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) {
    Write-Host "This copy of check-handoff.ps1 is not inside a git working copy: $PSScriptRoot" -ForegroundColor Red
    Write-Host 'Copy the tools/ folder into the pos-platform repository and run it from there.' -ForegroundColor Red
    Exit-Script 2
}
Set-Location $repoRoot

$current = (& git rev-parse --abbrev-ref HEAD)
if (-not $Branch) {
    Write-Host 'Usage: pwsh tools/check-handoff.ps1 <exact-branch> [-WritePr]' -ForegroundColor Red
    Write-Host 'The branch argument is mandatory (AGENTS.md: take handoff <exact-branch>).' -ForegroundColor Red
    Exit-Script 2
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
$handoffDir = Get-HandoffDir $Branch
$dir = $handoffDir.Path
$handoffPath = Join-Path $dir 'to-antigravity.md'
$validationPath = Join-Path $dir 'validation.md'

$errors = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path $handoffPath)) { Write-Host "No handoff found at $handoffPath" -ForegroundColor Red; Exit-Script 1 }
$text = Get-Content $handoffPath -Raw

function Get-Field([string]$Source, [string]$Pattern) {
    $m = [regex]::Match($Source, $Pattern, 'Multiline')
    if ($m.Success) { $m.Groups[1].Value.Trim().Trim('`') } else { '' }
}

$hProto  = Get-Field $text '^Protocol-Version:\s*(.+)$'
$hId     = Get-Field $text '^Branch-Id:\s*(.+)$'
$hBranch = Get-Field $text '^Branch:\s*(.+)$'
$hRound  = Get-Field $text '^Round:\s*(.+)$'
$hUpdated = Get-Field $text '^Updated:\s*(.+)$'
$hStatus = Get-Field $text '^Status:\s*(.+)$'
$hBase   = Get-Field $text '^-\s*Base commit:\s*(\S+)'
$hFinal  = Get-Field $text '^-\s*Final local commit:\s*(\S+)'
$head = (& git rev-parse --verify --quiet "refs/heads/$Branch")
if (-not $head) { $errors.Add("local branch '$Branch' does not exist") }

if ($hProto -ne '1') { $errors.Add("Protocol-Version is '$hProto', expected 1") }
if ($hId -ne $handoffDir.Id) { $errors.Add("Branch-Id '$hId' != derived '$($handoffDir.Id)'") }
if ($hRound -notmatch '^[1-9][0-9]*$') { $errors.Add("Round is '$hRound', expected an integer starting at 1") }
# Updated must be a real UTC instant in ISO 8601 form, e.g. 2026-10-15T12:30:00Z (optional fraction).
$updatedFormats = [string[]]@("yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'")
$updatedValue = [DateTimeOffset]::MinValue
$updatedOk = [DateTimeOffset]::TryParseExact($hUpdated, $updatedFormats,
    [System.Globalization.CultureInfo]::InvariantCulture,
    [System.Globalization.DateTimeStyles]::AssumeUniversal, [ref]$updatedValue) -and
    $updatedValue.Offset -eq [TimeSpan]::Zero
if (-not $updatedOk) { $errors.Add("Updated is '$hUpdated', expected a valid UTC ISO 8601 timestamp such as 2026-10-15T12:30:00Z") }
if ($hStatus -ne 'READY') { $errors.Add("Status is '$hStatus', expected READY") }
if ($hBranch -ne $Branch) { $errors.Add("handoff Branch '$hBranch' != '$Branch'") }
if ($Branch -in 'main', 'master') { $errors.Add('handoffs must come from a task branch, not main') }

if (-not $hFinal) { $errors.Add('Final local commit is missing') }
else {
    $finalFull = (& git rev-parse --verify --quiet "$hFinal^{commit}")
    if ($finalFull -ne $head) { $errors.Add("Final local commit $hFinal != branch head $head") }
}

if (-not $hBase) { $errors.Add('Base commit is missing') }
else {
    & git merge-base --is-ancestor $hBase $head 2>$null
    if ($LASTEXITCODE -ne 0) { $errors.Add("Base commit $hBase is not an ancestor of $Branch") }
}

if ($current -eq $Branch -and (& git status --porcelain --untracked-files=all)) { $errors.Add('working tree has uncommitted or untracked changes') }
if (& git ls-files -- '.handoff') { $errors.Add('.handoff/ files are tracked by git') }

# validation.md must describe exactly this commit and have passed
if (-not (Test-Path $validationPath)) {
    $errors.Add("missing $validationPath (run: pwsh tools/verify.ps1)")
} else {
    $v = Get-Content $validationPath -Raw
    $vCommit  = Get-Field $v '^Commit:\s*(\S+)'
    $vClean   = Get-Field $v '^Working tree clean:\s*(\S+)'
    $vOverall = Get-Field $v '^Overall:\s*(\S+)'
    if ($vCommit -ne $head) { $errors.Add("validation.md is for $vCommit, not branch head $head (re-run verify)") }
    if ($vClean -ne 'yes') { $errors.Add('validation.md was produced with uncommitted changes') }
    if ($vOverall -ne 'PASS') { $errors.Add("validation.md Overall is '$vOverall'") }
    if ($v -match 'requested with -(Skip|AllowDirty)') { $errors.Add('validation.md has steps skipped on request (-Skip or -AllowDirty); run the full gate on a clean commit') }
}

# High-risk areas (requirements D21.3) -> user approval before merge
$riskRules = [ordered]@{
    'Contract/Schema' = '^packages/(contracts|schemas)/'
    'Payment'         = '(?i)payment|tender|refund|cashdrawer|cash-drawer'
    'Printing'        = '(?i)^services/print-gateway/|printing|printer'
    'Security'        = '(?i)^SECURITY\.md$|^\.github/|auth|certificate|secret|crypto'
    'Database'        = '(?i)^deploy/database/|migrations?/'
    'License'         = '^(LICENSE|LICENSES/|REUSE\.toml|THIRD_PARTY_NOTICES)'
}
$riskHits = [ordered]@{}
if ($hBase) {
    $changed = @(& git diff --name-only "$hBase..$head" 2>$null)
    foreach ($k in $riskRules.Keys) {
        $hits = @($changed | Where-Object { $_ -match $riskRules[$k] })
        if ($hits) { $riskHits[$k] = $hits }
    }
}

# Optional: extract PR title/body for gh pr create
if ($WritePr -and $errors.Count -eq 0) {
    $section = [regex]::Match($text, '(?ms)^## Suggested pull request\s*(.*)\z').Groups[1].Value
    $title = Get-Field $section '^Title:\s*(.+)$'
    $body = [regex]::Match($section, '(?ms)^Body:\s*\r?\n(.*)\z').Groups[1].Value.Trim()
    if (-not $title) { $errors.Add('Suggested pull request Title is empty') }
    else {
        if ($riskHits.Count -gt 0) {
            $body += "`n`n> [!IMPORTANT]`n> Touches high-risk areas: $($riskHits.Keys -join ', '). User approval required before merge."
        }
        Set-Content -Path (Join-Path $dir 'pr-title.txt') -Value $title -Encoding utf8NoBOM
        Set-Content -Path (Join-Path $dir 'pr-body.md') -Value $body -Encoding utf8NoBOM
    }
}

Write-Host "Handoff: $Branch, round $hRound"
if ($riskHits.Count -gt 0) {
    Write-Host "`nHIGH-RISK AREAS (user approval required before merge):" -ForegroundColor Yellow
    foreach ($k in $riskHits.Keys) { Write-Host "  $k : $(@($riskHits[$k] | Select-Object -First 5) -join ', ')" -ForegroundColor Yellow }
}
if ($errors.Count -gt 0) {
    Write-Host "`nNOT READY - stop and report:" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Exit-Script 1
}
Write-Host "`nREADY TO PUSH" -ForegroundColor Green
Exit-Script 0
