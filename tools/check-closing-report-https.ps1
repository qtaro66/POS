#!/usr/bin/env pwsh
# SPDX-License-Identifier: MPL-2.0
[CmdletBinding()]
param([switch]$SelfTest)
$ErrorActionPreference = 'Stop'

# A focused regression guard for cs/user-controlled-bypass, not a local CodeQL replacement.
function Assert-HttpsBoundary([string]$Handler, [string]$Program) {
    if ($Handler -match '\bRequest\s*\.\s*IsHttps\b') {
        throw 'Closing report transport checks belong in middleware, not the sensitive receiver action.'
    }
    $guard = $Program.IndexOf('app.UseCentralClosingReportHttpsOnly(builder.Configuration);', [StringComparison]::Ordinal)
    $redirect = $Program.IndexOf('app.UseHttpsRedirection();', [StringComparison]::Ordinal)
    $security = $Program.IndexOf('app.UseCentralCloudIngestionSecurityIfEnabled(builder.Configuration);', [StringComparison]::Ordinal)
    if ($guard -lt 0 -or $redirect -lt 0 -or $security -lt 0 -or $guard -ge $redirect -or $guard -ge $security) {
        throw 'Closing report HTTPS middleware must run before redirects and authentication/authorization.'
    }
}

$repo = Split-Path -Parent $PSScriptRoot
$handler = [IO.File]::ReadAllText((Join-Path $repo 'src/Central/Pos.Central.Api/Synchronization/CentralClosingReportEndpoints.cs'))
$program = [IO.File]::ReadAllText((Join-Path $repo 'src/Central/Pos.Central.Api/Program.cs'))
Assert-HttpsBoundary $handler $program
if ($SelfTest) {
    $rejected = 0
    try { Assert-HttpsBoundary ($handler + ' if (!context.Request.IsHttps) { return; }') $program } catch { $rejected++ }
    try { Assert-HttpsBoundary $handler ($program.Replace('app.UseCentralClosingReportHttpsOnly(builder.Configuration);', '')) } catch { $rejected++ }
    try { Assert-HttpsBoundary $handler ($program.Replace('app.UseCentralClosingReportHttpsOnly(builder.Configuration);', '') + "`napp.UseCentralClosingReportHttpsOnly(builder.Configuration);") } catch { $rejected++ }
    if ($rejected -ne 3) { throw 'HTTPS boundary guard did not detect a known regression fixture.' }
}
Write-Output 'Closing report HTTPS boundary: PASS'
