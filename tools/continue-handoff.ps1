#!/usr/bin/env pwsh
# SPDX-License-Identifier: MPL-2.0
[CmdletBinding()]
param(
    [string]$TrackBranch,
    [int]$Round,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Identity([string]$Branch) {
    if (-not $Branch -or $Branch -ceq 'main' -or $Branch.StartsWith('-')) { throw 'An exact task branch is required.' }
    & git check-ref-format --branch $Branch *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Invalid task branch.' }
    $slug = ([regex]::Replace($Branch, '[^A-Za-z0-9._-]+', '-')).Trim('-').ToLowerInvariant()
    $id = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Branch))).Substring(0, 8).ToLowerInvariant()
    return [pscustomobject]@{ Id = $id; Directory = "$slug--$id" }
}

function Read-Fields([string]$Path) {
    $fields = @{}
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        if ($line -cmatch '^([A-Za-z][A-Za-z -]*): (.+)$') {
            if ($fields.ContainsKey($Matches[1])) { throw "Duplicate header in $Path" }
            $fields[$Matches[1]] = $Matches[2]
        }
        if ($line -eq '') { break }
    }
    return $fields
}

function Assert-Header($Fields, [string]$Branch, [string]$Id, [int]$ExpectedRound, [string[]]$Statuses) {
    foreach ($key in @('Protocol-Version', 'Branch', 'Branch-Id', 'Round', 'Status', 'Updated')) {
        if (-not $Fields.ContainsKey($key)) { throw "Missing $key" }
    }
    $parsed = [DateTimeOffset]::MinValue
    if ($Fields['Protocol-Version'] -cne '1' -or $Fields['Branch'] -cne $Branch -or $Fields['Branch-Id'] -cne $Id -or
        $Fields['Round'] -cne $ExpectedRound.ToString([Globalization.CultureInfo]::InvariantCulture) -or
        $Statuses -cnotcontains $Fields['Status'] -or
        -not [DateTimeOffset]::TryParseExact($Fields['Updated'], "yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$parsed) -or $parsed.Offset -ne [TimeSpan]::Zero) {
        throw 'Handoff identity, round, status, or UTC timestamp mismatch.'
    }
}

function Get-CommitField([string]$Text, [string]$Label) {
    $matchesFound = [regex]::Matches($Text, '(?m)^- ' + [regex]::Escape($Label) + ': ([0-9a-f]{40})\r?$')
    if ($matchesFound.Count -ne 1) { throw "Missing or ambiguous $Label" }
    return $matchesFound[0].Groups[1].Value
}

function Resolve-Continue([string]$Root, $Pointer) {
    $dirty = @(& git -C $Root status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -gt 0) { throw 'Working tree must be clean before continuation.' }
    $identity = Get-Identity $Pointer.Branch
    if ($Pointer.ProtocolVersion -ne 1 -or $Pointer.Repository -cne $Root -or $Pointer.BranchId -cne $identity.Id -or
        $Pointer.Round -isnot [long] -and $Pointer.Round -isnot [int] -or $Pointer.Round -lt 1 -or
        $Pointer.Commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Invalid continuation pointer.' }
    $dir = Join-Path $Root ".handoff/$($identity.Directory)"
    $to = Join-Path $dir 'to-antigravity.md'
    Assert-Header (Read-Fields $to) $Pointer.Branch $identity.Id $Pointer.Round @('READY')
    $toText = [IO.File]::ReadAllText($to)
    if ((Get-CommitField $toText 'Final local commit') -cne $Pointer.Commit) { throw 'Tracked source commit changed.' }
    $from = Join-Path $dir 'from-antigravity.md'
    if (-not (Test-Path -LiteralPath $from)) {
        return [pscustomobject]@{ Branch = $Pointer.Branch; Round = $Pointer.Round; Action = 'AwaitingResult'; ResultPath = $from }
    }
    $fields = Read-Fields $from
    Assert-Header $fields $Pointer.Branch $identity.Id $Pointer.Round @('PR_OPEN', 'NEEDS_CHANGES', 'BLOCKED', 'MERGED')
    $text = [IO.File]::ReadAllText($from)
    # Antigravity may have published explicitly documented correction commits.
    # A different source requires an explicit take-result review before continuation.
    if ((Get-CommitField $text 'Source commit checked') -cne $Pointer.Commit) { throw 'Result source differs; use explicit take result to review corrections.' }
    $action = switch -CaseSensitive ($fields['Status']) {
        'PR_OPEN' { 'WaitForAntigravity' }
        'BLOCKED' { 'UserDecisionRequired' }
        'NEEDS_CHANGES' { 'CorrectAndHandoff' }
        'MERGED' {
            $merge = Get-CommitField $text 'Main commit'
            $main = & git -C $Root rev-parse refs/heads/main
            $remote = & git -C $Root rev-parse refs/remotes/origin/main
            if ($LASTEXITCODE -ne 0 -or $main -cne $remote) { throw 'Local main is not synchronized.' }
            & git -C $Root merge-base --is-ancestor $merge $main
            if ($LASTEXITCODE -ne 0) { throw 'Recorded merge is not on main.' }
            $base = Get-CommitField $toText 'Base commit'
            $paths = @(& git -C $Root diff --name-only $base $Pointer.Commit)
            if ($LASTEXITCODE -ne 0 -or $paths.Count -eq 0) { throw 'Cannot verify task content.' }
            & git -C $Root diff --quiet $Pointer.Commit $merge -- @paths
            if ($LASTEXITCODE -ne 0) { throw 'Merged content does not match source.' }
            if ($text -notmatch '(?m)^- Local main synchronized: yes\r?$' -or
                $text -notmatch '(?m)^- Task branch cleanup: .+\r?$' -or
                $text -match '(?im)^- (Content verification|Task branch cleanup): .*\b(failed|pending)\b') { throw 'Post-merge result needs explicit review.' }
            'TakeResultThenNext'
        }
    }
    return [pscustomobject]@{ Branch = $Pointer.Branch; Round = $Pointer.Round; Action = $action; ResultPath = $from }
}

if ($SelfTest) {
    $id = Get-Identity 'feature/a-b'
    if ($id.Id -ceq (Get-Identity 'feature/a/b').Id) { throw 'Identity collision.' }
    $valid = @{ 'Protocol-Version'='1'; Branch='feature/a-b'; 'Branch-Id'=$id.Id; Round='1'; Status='MERGED'; Updated='2026-10-30T00:00:00Z' }
    Assert-Header $valid 'feature/a-b' $id.Id 1 @('MERGED')
    foreach ($case in @(@('Branch','FEATURE/a-b'), @('Round','01'), @('Round','0'), @('Status','merged'), @('Updated','2026-02-30T00:00:00Z'), @('Updated','2026-10-30T00:00:00+00:00'))) {
        $bad = $valid.Clone(); $bad[$case[0]] = $case[1]
        $rejected = $false
        try { Assert-Header $bad 'feature/a-b' $id.Id 1 @('MERGED') } catch { $rejected = $true }
        if (-not $rejected) { throw "Accepted invalid $($case[0])" }
    }
    $fixture = Join-Path ([IO.Path]::GetTempPath()) ('continue-tests-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($fixture) | Out-Null
    try {
        & git -C $fixture init -q -b main
        [IO.File]::WriteAllText((Join-Path $fixture '.gitignore'), "/.handoff/`n")
        & git -C $fixture add .gitignore
        & git -C $fixture -c user.name=Test -c user.email=test@example.invalid commit -qm base
        $base = (& git -C $fixture rev-parse HEAD).Trim()
        [IO.File]::WriteAllText((Join-Path $fixture 'sample.txt'), 'sample')
        & git -C $fixture add sample.txt
        & git -C $fixture -c user.name=Test -c user.email=test@example.invalid commit -qm task
        $source = (& git -C $fixture rev-parse HEAD).Trim()
        & git -C $fixture update-ref refs/remotes/origin/main $source
        $fixtureRoot = (& git -C $fixture rev-parse --show-toplevel).Trim()
        $fixtureDir = Join-Path $fixture ".handoff/$($id.Directory)"
        [IO.Directory]::CreateDirectory($fixtureDir) | Out-Null
        $header = "Protocol-Version: 1`nBranch: feature/a-b`nBranch-Id: $($id.Id)`nRound: 1`nStatus: READY`nUpdated: 2026-10-30T00:00:00Z`n`n"
        [IO.File]::WriteAllText((Join-Path $fixtureDir 'to-antigravity.md'), $header + "- Base commit: $base`n- Final local commit: $source`n")
        $pointer = [pscustomobject]@{ProtocolVersion=1;Repository=$fixtureRoot;Branch='feature/a-b';BranchId=$id.Id;Round=1;Commit=$source}
        if ((Resolve-Continue $fixtureRoot $pointer).Action -cne 'AwaitingResult') { throw 'Missing result handling failed.' }
        foreach ($pair in @(@('PR_OPEN','WaitForAntigravity'), @('BLOCKED','UserDecisionRequired'), @('NEEDS_CHANGES','CorrectAndHandoff'), @('MERGED','TakeResultThenNext'))) {
            $result = $header.Replace('Status: READY', 'Status: ' + $pair[0]) + "- Source commit checked: $source`n- Main commit: $source`n- Local main synchronized: yes`n- Content verification: passed`n- Task branch cleanup: local deleted; remote deleted`n"
            [IO.File]::WriteAllText((Join-Path $fixtureDir 'from-antigravity.md'), $result)
            if ((Resolve-Continue $fixtureRoot $pointer).Action -cne $pair[1]) { throw "Wrong action for $($pair[0])" }
        }
        [IO.File]::WriteAllText((Join-Path $fixture 'untracked.txt'), 'dirty')
        $rejected = $false
        try { Resolve-Continue $fixtureRoot $pointer | Out-Null } catch { $rejected = $true }
        if (-not $rejected) { throw 'Dirty tree accepted.' }
    } finally {
        $resolvedFixture = [IO.Path]::GetFullPath($fixture)
        if (-not $resolvedFixture.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolvedFixture) -notlike 'continue-tests-*') { throw 'Unsafe fixture cleanup path.' }
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
    Write-Output 'PASS: continuation identity and adversarial header checks'
    exit 0
}

try {
    $root = (& git -C $PSScriptRoot rev-parse --show-toplevel).Trim()
    if (-not $root) { throw 'Repository unavailable.' }
    $tracked = @(& git -C $root ls-files -- .handoff)
    & git -C $root check-ignore --quiet .handoff/probe
    if ($tracked.Count -gt 0 -or $LASTEXITCODE -ne 0) { throw 'Handoff storage is not private ignored state.' }
    $pointerPath = Join-Path $root '.handoff/continue.json'
    if ($TrackBranch) {
        if ($Round -lt 1) { throw '-Round must be at least 1.' }
        $identity = Get-Identity $TrackBranch
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-handoff.ps1') $TrackBranch
        if ($LASTEXITCODE -ne 0) { throw 'Handoff gate rejected tracking.' }
        $commit = (& git -C $root rev-parse "refs/heads/$TrackBranch").Trim()
        $pointer = [ordered]@{ ProtocolVersion=1; Repository=$root; Branch=$TrackBranch; BranchId=$identity.Id; Round=$Round; Commit=$commit }
        $to = Join-Path $root ".handoff/$($identity.Directory)/to-antigravity.md"
        Assert-Header (Read-Fields $to) $TrackBranch $identity.Id $Round @('READY')
        $temp = $pointerPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        [IO.File]::WriteAllText($temp, (($pointer | ConvertTo-Json) -replace "`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temp, $pointerPath, $true)
        Write-Output "Tracking $TrackBranch round $Round"
    } else {
        if ($PSBoundParameters.ContainsKey('Round')) { throw '-Round requires -TrackBranch.' }
        if (-not (Test-Path -LiteralPath $pointerPath)) { throw 'No tracked handoff. Use -TrackBranch <exact-branch> -Round <n>.' }
        Resolve-Continue $root (Get-Content -Raw -LiteralPath $pointerPath | ConvertFrom-Json) | ConvertTo-Json
    }
} catch { Write-Error $_ -ErrorAction Continue; exit 1 }
