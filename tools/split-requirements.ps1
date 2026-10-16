#!/usr/bin/env pwsh
# SPDX-License-Identifier: MPL-2.0
<#
.SYNOPSIS
    Splits a large requirements document into small section files plus an index,
    so agents read only the sections they need instead of the whole document.

.DESCRIPTION
    The source document stays the single source of truth (architecture records
    link to it). This script writes a read-only "reading copy" next to it:

        docs/requirements/POS-Requirements-v0.2.md        <- source, edit this
        docs/requirements/POS-Requirements-v0.2/README.md <- index: ID -> file
        docs/requirements/POS-Requirements-v0.2/NNN-*.md   <- exact slices

    Each part is an exact byte slice of the source, in order, so concatenating the
    parts reproduces the source byte for byte. Sections larger than -MaxBytes are
    split further at their next heading level. Headings inside code fences are ignored.

    -Check regenerates in memory and fails if the reading copy is missing or
    differs from the source (verify.ps1 runs this), so the copy cannot drift.

.EXAMPLE
    pwsh tools/split-requirements.ps1
    pwsh tools/split-requirements.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Source = 'docs/requirements/POS-Requirements-v0.2.md',
    [string]$OutDir,
    [int]$MaxBytes = 16000,
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
if ($repoRoot) { Set-Location $repoRoot }
if (-not (Test-Path $Source)) { Write-Host "Source not found: $Source" -ForegroundColor Red; exit 2 }
$Source = (Resolve-Path $Source -Relative) -replace '\\', '/' -replace '^\./', ''
if (-not $OutDir) { $OutDir = [IO.Path]::ChangeExtension($Source, $null).TrimEnd('.') }
$OutDir = $OutDir -replace '\\', '/'

$utf8 = [Text.UTF8Encoding]::new($false)
$bytes = [IO.File]::ReadAllBytes((Resolve-Path $Source))
$text = $utf8.GetString($bytes)
if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { Write-Host 'Source starts with a BOM; remove it first.' -ForegroundColor Red; exit 2 }
$sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()

# --- Lines (with their terminators) and headings outside code fences ---------
$lines = [regex]::Split($text, '(?<=\n)') | Where-Object { $_ -ne '' }
$headings = [System.Collections.Generic.List[object]]::new()
$inFence = $false
for ($i = 0; $i -lt $lines.Count; $i++) {
    $l = $lines[$i].TrimEnd("`r", "`n")
    if ($l -match '^\s{0,3}(```|~~~)') { $inFence = -not $inFence; continue }
    if (-not $inFence -and $l -match '^(#{2,6})\s+(.+?)\s*#*\s*$') {
        $headings.Add([pscustomobject]@{ Line = $i; Level = $Matches[1].Length; Title = $Matches[2] })
    }
}

function Get-Bytes([int]$Start, [int]$End) {
    if ($End -le $Start) { return 0 }
    $utf8.GetByteCount(-join $lines[$Start..($End - 1)])
}
function Get-SectionEnd($h) {
    foreach ($n in $headings) { if ($n.Line -gt $h.Line -and $n.Level -le $h.Level) { return $n.Line } }
    $lines.Count
}
function Get-Ids([string]$Title) {
    $ids = [System.Collections.Generic.List[string]]::new()
    $num = [regex]::Match($Title, '^(\d+(?:\.\d+)*)\.?\s')
    if ($num.Success) { $ids.Add($num.Groups[1].Value) }
    foreach ($m in [regex]::Matches($Title, '\bD\d+(?:\.\d+)*\b')) { if (-not $ids.Contains($m.Value)) { $ids.Add($m.Value) } }
    if ($Title -match '^(Phase \d+)') { $ids.Add($Matches[1]) }
    , $ids
}
function Get-Slug([string]$Title) {
    $ids = Get-Ids $Title
    $words = [regex]::Matches(($Title -replace '\bD\d+(?:\.\d+)*\b', '' -replace '^\d+(?:\.\d+)*\.?\s', ''), '[A-Za-z][A-Za-z0-9]*') |
        ForEach-Object { $_.Value.ToLowerInvariant() }
    $parts = @($ids | ForEach-Object { $_.ToLowerInvariant() -replace '[^a-z0-9]+', '-' }) + @($words)
    $slug = (($parts -join '-') -replace '-+', '-').Trim('-')
    if ($slug.Length -gt 60) { $slug = $slug.Substring(0, 60).TrimEnd('-') }
    if (-not $slug) { $slug = 'section' }
    $slug
}

# --- Chunking ------------------------------------------------------------------
$chunks = [System.Collections.Generic.List[object]]::new()
function Add-Chunk([int]$Start, [int]$End, [string[]]$Path, [string]$Title) {
    if ($End -le $Start) { return }
    $ids = [System.Collections.Generic.List[string]]::new()
    foreach ($h in $headings) {
        if ($h.Line -ge $Start -and $h.Line -lt $End) { foreach ($x in (Get-Ids $h.Title)) { if (-not $ids.Contains($x)) { $ids.Add($x) } } }
    }
    $chunks.Add([pscustomobject]@{ Start = $Start; End = $End; Path = $Path; Title = $Title; Ids = $ids })
}
function Split-Section($h, [string[]]$ParentPath) {
    $end = Get-SectionEnd $h
    $path = @($ParentPath) + @($h.Title)   # kept for debugging; the index shows the own heading only
    $inner = @($headings | Where-Object { $_.Line -gt $h.Line -and $_.Line -lt $end })
    if ((Get-Bytes $h.Line $end) -le $MaxBytes -or $inner.Count -eq 0) { Add-Chunk $h.Line $end $path $h.Title; return }
    $childLevel = ($inner | Measure-Object -Property Level -Minimum).Minimum
    $children = @($inner | Where-Object Level -eq $childLevel)
    Add-Chunk $h.Line $children[0].Line $path $h.Title          # section intro (may be just the heading)
    foreach ($c in $children) { Split-Section $c $path }
}

$top = @($headings | Where-Object Level -eq 2)
if ($top.Count -eq 0) { Write-Host 'No "## " headings found.' -ForegroundColor Red; exit 2 }
Add-Chunk 0 $top[0].Line @('(preamble)') '(preamble)'
foreach ($h in $top) { Split-Section $h @() }

# Move blank lines at the end of a part to the start of the next part, so no part ends
# with a blank line (git diff --check "blank line at EOF"). Concatenation is unchanged.
for ($k = 0; $k -lt $chunks.Count - 1; $k++) {
    $c = $chunks[$k]; $next = $chunks[$k + 1]
    while ($c.End - 1 -gt $c.Start -and $lines[$c.End - 1].Trim() -eq '') { $c.End--; $next.Start-- }
}

# Sanity: chunks must tile the document exactly.
$pos = 0
foreach ($c in $chunks) { if ($c.Start -ne $pos) { throw "internal: gap/overlap at line $pos" }; $pos = $c.End }
if ($pos -ne $lines.Count) { throw 'internal: chunks do not reach the end of the document' }

# --- Build outputs in memory ---------------------------------------------------
$expected = [ordered]@{}
$rows = [System.Collections.Generic.List[string]]::new()
$n = 0
foreach ($c in $chunks) {
    $n++
    $name = '{0:D3}-{1}.md' -f $n, $(if ($c.Title -eq '(preamble)') { 'preamble' } else { Get-Slug $c.Title })
    $content = -join $lines[$c.Start..($c.End - 1)]
    $expected[$name] = $utf8.GetBytes($content)
    # Own heading only, without the confirmation-status suffix, kept short to keep the index cheap to read.
    $title = ($c.Title -replace '\s+—\s+(ยืนยัน|เลื่อน|ยืนยันบางส่วน|ยืนยันเบื้องต้น|ยืนยันว่า|ไม่ใช้|Manual).*$', '').Trim() -replace '\|', '/'
    if ($title.Length -gt 48) { $title = $title.Substring(0, 47) + '…' }
    $kb = [math]::Round($expected[$name].Length / 1024, 1).ToString('0.0', [Globalization.CultureInfo]::InvariantCulture)
    $rows.Add("| $name | $(@($c.Ids) -join ', ') | $title | $kb |")
}

$index = @(
    "# Reading copy: $(Split-Path $Source -Leaf)",
    '',
    '<!-- Generated by tools/split-requirements.ps1. Do not edit; edit the source and re-run. -->',
    '',
    "- Source of truth: ``$Source``",
    "- Source SHA-256: ``$sha``",
    "- Parts: $($chunks.Count), each an exact slice of the source in order (max $MaxBytes bytes unless a section has no sub-headings).",
    '',
    'Agents: do not open the source file. Find the requirement ID (for example `D12.3`) or section number in this table and read only that part.',
    '',
    '| File | IDs | Heading | KB |',
    '| --- | --- | --- | --- |'
) + $rows
$expected['README.md'] = $utf8.GetBytes(($index -join "`n") + "`n")

# --- Check or write -------------------------------------------------------------
if ($Check) {
    $problems = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path $OutDir)) { $problems.Add("missing $OutDir") }
    else {
        $actual = @(Get-ChildItem -Path $OutDir -File | ForEach-Object Name)
        foreach ($k in $expected.Keys) {
            $p = Join-Path $OutDir $k
            if (-not (Test-Path $p)) { $problems.Add("missing $k"); continue }
            $b = [IO.File]::ReadAllBytes((Resolve-Path $p))
            if (-not [Linq.Enumerable]::SequenceEqual([byte[]]$b, [byte[]]$expected[$k])) { $problems.Add("differs: $k") }
        }
        foreach ($a in $actual) { if (-not $expected.Contains($a)) { $problems.Add("unexpected file: $a") } }
    }
    if ($problems.Count -gt 0) {
        $problems | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        Write-Host "Reading copy is out of date. Run: pwsh tools/split-requirements.ps1 -Source $Source" -ForegroundColor Red
        exit 1
    }
    Write-Host "Reading copy of $Source is up to date ($($chunks.Count) parts)." -ForegroundColor Green
    exit 0
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Get-ChildItem -Path $OutDir -File -Filter '*.md' | Where-Object { -not $expected.Contains($_.Name) } | Remove-Item
foreach ($k in $expected.Keys) { [IO.File]::WriteAllBytes((Join-Path (Resolve-Path $OutDir) $k), [byte[]]$expected[$k]) }
Write-Host "Wrote $($chunks.Count) parts and README.md to $OutDir" -ForegroundColor Green
