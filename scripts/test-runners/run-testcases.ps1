#requires -Version 7.0
<#
.SYNOPSIS
Submit a batch of jobs from a definition file and report progress.

.PARAMETER JobsFile
Path to a JSON file. Either:
  - Array of full job objects: [ { "name": "...", "command": "..." }, ... ]
  - Array of strings (treated as inline PS commands; auto-named job-1, job-2, ...)

.PARAMETER Mode
Standard: single-line progress + final summary.
Live    : real-time job status table.

.EXAMPLE
./run-testcases.ps1 -JobsFile jobs.json
./run-testcases.ps1 -JobsFile jobs.json -Mode Live

Sample jobs.json:
[
  { "name": "hello",  "command": "Write-Host 'hi'" },
  { "name": "uptime", "command": "(Get-Uptime).ToString()" }
]
#>

param(
    [Parameter(Mandatory)]
    [string]$JobsFile,

    [ValidateSet('Standard', 'Live')]
    [string]$Mode = 'Standard',

    [string]$ApiUrl = 'https://localhost:5001/api/vmjobs',
    [int]$MaxWaitMinutes = 10,
    [int]$RefreshIntervalSeconds = 2,
    [switch]$ShowDetails
)

[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

if (-not (Test-Path $JobsFile)) {
    Write-Host "Error: jobs file not found: $JobsFile" -ForegroundColor Red
    exit 1
}

$raw = Get-Content $JobsFile -Raw | ConvertFrom-Json -AsHashtable
$jobs = @()
$i = 0
foreach ($entry in $raw) {
    $i++
    if ($entry -is [string]) {
        $jobs += @{ name = "job-$i"; command = $entry }
    }
    elseif ($entry -is [hashtable]) {
        if (-not $entry.name) { $entry.name = "job-$i" }
        $jobs += $entry
    }
}

Write-Host ('=' * 60) -ForegroundColor Cyan
Write-Host "  Batch runner | $($jobs.Count) jobs | mode: $Mode" -ForegroundColor Cyan
Write-Host ('=' * 60) -ForegroundColor Cyan

Write-Host "`nSubmitting..." -ForegroundColor Yellow
$tracked = @()
foreach ($j in $jobs) {
    $name = $j.name
    kubectl delete vmjob $name --ignore-not-found 2>$null | Out-Null
    try {
        $resp = Invoke-RestMethod -Uri $ApiUrl -Method Post `
            -Body ($j | ConvertTo-Json -Depth 10) -ContentType 'application/json' -ErrorAction Stop
        $tracked += [pscustomobject]@{ name = $name; status = 'Pending'; vm = $resp.assignedVM; exitCode = $null }
        if ($ShowDetails) { Write-Host "  [OK] $name" -ForegroundColor Green }
    }
    catch {
        $tracked += [pscustomobject]@{ name = $name; status = 'SubmitFailed'; vm = $null; exitCode = $null }
        Write-Host "  [FAIL] $name : $_" -ForegroundColor Red
    }
    Start-Sleep -Milliseconds 100
}

$ok = ($tracked.Where{ $_.status -ne 'SubmitFailed' }).Count
Write-Host "`nSubmitted: $ok / $($jobs.Count)" -ForegroundColor Green
Write-Host "`nMonitoring..." -ForegroundColor Yellow

$startTime = Get-Date
$timeout = $startTime.AddMinutes($MaxWaitMinutes)

function Update-Status {
    foreach ($t in $tracked) {
        if ($t.status -in 'Completed', 'Succeeded', 'Failed', 'SubmitFailed') { continue }
        $k = kubectl get vmjob $t.name -o json 2>$null | ConvertFrom-Json
        if ($k) {
            $t.status = $k.status.phase ?? 'Pending'
            $t.vm = $k.status.assignedVM
            $t.exitCode = $k.status.exitCode
        }
    }
}

function Show-Live {
    Clear-Host
    Write-Host ('=' * 90) -ForegroundColor Cyan
    Write-Host '  LIVE JOB STATUS' -ForegroundColor Cyan
    Write-Host ('=' * 90) -ForegroundColor Cyan
    Write-Host ('{0,-30} {1,-12} {2,-25} {3,-10}' -f 'Name', 'Status', 'VM', 'Exit')
    Write-Host ('-' * 90)
    foreach ($t in $tracked | Sort-Object name) {
        $color = switch ($t.status) {
            'Completed' { 'Green' }; 'Succeeded' { 'Green' }
            'Failed' { 'Red' }; 'SubmitFailed' { 'Red' }
            'Running' { 'Yellow' }
            default { 'Gray' }
        }
        Write-Host ('{0,-30} {1,-12} {2,-25} {3,-10}' -f $t.name, $t.status, ($t.vm ?? '-'), ($t.exitCode ?? '-')) -ForegroundColor $color
    }
    $done = ($tracked.Where{ $_.status -in 'Completed', 'Succeeded', 'Failed', 'SubmitFailed' }).Count
    $elapsed = [int]((Get-Date) - $startTime).TotalSeconds
    Write-Host ('-' * 90)
    Write-Host "Progress: $done / $($tracked.Count) | Elapsed: ${elapsed}s" -ForegroundColor Cyan
}

while ((Get-Date) -lt $timeout) {
    Update-Status
    $done = ($tracked.Where{ $_.status -in 'Completed', 'Succeeded', 'Failed', 'SubmitFailed' }).Count

    if ($Mode -eq 'Live') {
        Show-Live
    }
    else {
        $elapsed = [int]((Get-Date) - $startTime).TotalSeconds
        Write-Host "`r[${elapsed}s] $done / $($tracked.Count) complete" -NoNewline -ForegroundColor Cyan
    }

    if ($done -eq $tracked.Count) { break }
    Start-Sleep -Seconds $RefreshIntervalSeconds
}

Write-Host "`n"
$succeeded = ($tracked.Where{ $_.status -in 'Completed', 'Succeeded' }).Count
$failed = ($tracked.Where{ $_.status -in 'Failed', 'SubmitFailed' }).Count
Write-Host '== Final ==' -ForegroundColor Cyan
Write-Host "  Total     : $($tracked.Count)"
Write-Host "  Succeeded : $succeeded" -ForegroundColor Green
Write-Host "  Failed    : $failed" -ForegroundColor $(if ($failed) { 'Red' } else { 'Gray' })
Write-Host "  Duration  : $([int]((Get-Date) - $startTime).TotalSeconds) s"

$vmDist = $tracked.Where{ $_.vm } | Group-Object vm
if ($vmDist) {
    Write-Host "`nVM distribution:" -ForegroundColor Cyan
    foreach ($g in $vmDist) { Write-Host "  $($g.Name) : $($g.Count)" }
}
