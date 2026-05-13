#requires -Version 7.0
<#
.SYNOPSIS
Submit a VMJob via the API. Choose one of -Command, -ScriptFile, or -JobFile.

.PARAMETER Name
Job name. Auto-generated if omitted.

.PARAMETER Command
Inline PowerShell command to run on the VM.

.PARAMETER ScriptFile
Path to a .ps1 file. Contents sent as job script.

.PARAMETER JobFile
Path to a .json file containing a full job definition (see examples/api/).

.PARAMETER VMSelector
JSON object selecting which VM(s) to target, e.g. '{"os":"windows","tags":{"role":"worker"}}'.

.PARAMETER ApiUrl
Base API URL. Default https://localhost:5001.

.PARAMETER Wait
Poll the job after submitting and print final status.

.PARAMETER DryRun
Print the job body that would be submitted without sending it.

.EXAMPLE
./submit-job.ps1 -Command "Get-Date"
./submit-job.ps1 -Name hello -Command "Write-Host 'Hello VM'" -Wait
./submit-job.ps1 -ScriptFile ./build.ps1 -VMSelector '{"tags":{"role":"build"}}'
./submit-job.ps1 -JobFile ./examples/api/stage-job.json -Wait
#>

[CmdletBinding(DefaultParameterSetName = 'Command')]
param(
    [string]$Name,

    [Parameter(ParameterSetName = 'Command', Mandatory)]
    [string]$Command,

    [Parameter(ParameterSetName = 'Script', Mandatory)]
    [string]$ScriptFile,

    [Parameter(ParameterSetName = 'JobFile', Mandatory)]
    [string]$JobFile,

    [string]$VMSelector,
    [string]$ApiUrl = 'https://localhost:5001',
    [switch]$Wait,
    [switch]$DryRun
)

[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

switch ($PSCmdlet.ParameterSetName) {
    'Command' {
        if (-not $Name) { $Name = "job-$(Get-Random)" }
        $job = @{ name = $Name; command = $Command }
    }
    'Script' {
        if (-not (Test-Path $ScriptFile)) {
            Write-Host "Error: script file not found: $ScriptFile" -ForegroundColor Red
            exit 1
        }
        if (-not $Name) { $Name = "job-$([IO.Path]::GetFileNameWithoutExtension($ScriptFile))-$(Get-Random)" }
        $job = @{
            name   = $Name
            script = @{ type = 'powershell'; content = (Get-Content $ScriptFile -Raw) }
        }
    }
    'JobFile' {
        if (-not (Test-Path $JobFile)) {
            Write-Host "Error: job file not found: $JobFile" -ForegroundColor Red
            exit 1
        }
        $job = Get-Content $JobFile -Raw | ConvertFrom-Json -AsHashtable
        if ($Name) { $job.name = $Name }
        elseif (-not $job.name) { $job.name = "job-$(Get-Random)" }
    }
}

if ($VMSelector) {
    $job.vmSelector = $VMSelector | ConvertFrom-Json -AsHashtable
}

if ($DryRun) {
    Write-Host "[DRY] Would submit to $ApiUrl/api/vmjobs :" -ForegroundColor Yellow
    $job | ConvertTo-Json -Depth 10
    exit 0
}

try {
    $resp = Invoke-RestMethod -Uri "$ApiUrl/api/vmjobs" -Method Post `
        -Body ($job | ConvertTo-Json -Depth 10) -ContentType 'application/json' -ErrorAction Stop
    $jobName = $resp.metadata.name ?? $resp.name ?? $job.name
    Write-Host "[OK] $jobName -> $($resp.assignedVM ?? '(pending)')" -ForegroundColor Green
}
catch {
    Write-Host "[FAIL] $($job.name) : $_" -ForegroundColor Red
    exit 1
}

if (-not $Wait) { exit 0 }

Write-Host "`nWaiting for $jobName..." -ForegroundColor Yellow
$start = Get-Date
while ($true) {
    Start-Sleep -Seconds 3
    $k = kubectl get vmjob $jobName -o json 2>$null | ConvertFrom-Json
    if (-not $k) { Write-Host 'Job not found' -ForegroundColor Red; exit 1 }

    $elapsed = [int]((Get-Date) - $start).TotalSeconds
    $phase = $k.status.phase ?? 'Pending'
    Write-Host "`r[${elapsed}s] $phase" -NoNewline

    if ($phase -in 'Completed', 'Succeeded', 'Failed') {
        Write-Host ''
        Write-Host "Status   : $phase" -ForegroundColor $(if ($phase -eq 'Failed') { 'Red' } else { 'Green' })
        Write-Host "Exit code: $($k.status.exitCode)"
        Write-Host "VM       : $($k.status.assignedVM)"
        Write-Host "Duration : ${elapsed}s"
        if ($k.status.logs) { Write-Host "`nLogs:"; Write-Host $k.status.logs }
        if ($phase -eq 'Failed') { exit 1 }
        exit 0
    }
}
