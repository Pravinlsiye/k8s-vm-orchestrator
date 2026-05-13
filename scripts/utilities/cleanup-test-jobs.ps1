# Cleanup VMJobs by status.
# Default scope: all VMJobs in the namespace. Use -NamePrefix to filter.

param(
    [string]$NamePrefix,
    [string]$Namespace = 'default',
    [switch]$All,
    [switch]$CompletedOnly,
    [switch]$FailedOnly,
    [switch]$Force
)

Write-Host "╔════════════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                        TEST JOB CLEANUP UTILITY                        ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Get all test jobs
$jobs = kubectl get vmjobs -n $Namespace -o json | ConvertFrom-Json | Select-Object -ExpandProperty items
if ($NamePrefix) {
    $jobs = $jobs | Where-Object { $_.metadata.name -like "$NamePrefix*" }
}

if ($jobs.Count -eq 0) {
    Write-Host "No VMJobs found to clean up." -ForegroundColor Yellow
    exit 0
}

# Filter jobs based on parameters
$jobsToDelete = @()

if ($All) {
    $jobsToDelete = $jobs
    Write-Host "Mode: Delete ALL test jobs" -ForegroundColor Red
} elseif ($CompletedOnly) {
    $jobsToDelete = $jobs | Where-Object { $_.status.phase -eq "Succeeded" }
    Write-Host "Mode: Delete only SUCCEEDED jobs" -ForegroundColor Green
} elseif ($FailedOnly) {
    $jobsToDelete = $jobs | Where-Object { $_.status.phase -eq "Failed" }
    Write-Host "Mode: Delete only FAILED jobs" -ForegroundColor Red
} else {
    # Default: Delete completed and failed jobs
    $jobsToDelete = $jobs | Where-Object { $_.status.phase -eq "Succeeded" -or $_.status.phase -eq "Failed" }
    Write-Host "Mode: Delete COMPLETED jobs (succeeded + failed)" -ForegroundColor Yellow
}

# Show summary
Write-Host "`nJob Summary:" -ForegroundColor Cyan
$statusGroups = $jobs | Group-Object { $_.status.phase }
foreach ($group in $statusGroups) {
    Write-Host "  $($group.Name): $($group.Count)"
}

Write-Host "`nJobs to delete: $($jobsToDelete.Count)" -ForegroundColor Yellow

if ($jobsToDelete.Count -eq 0) {
    Write-Host "No jobs match the deletion criteria." -ForegroundColor Green
    exit 0
}

# Show jobs to be deleted
Write-Host "`nJobs that will be deleted:" -ForegroundColor Red
foreach ($job in $jobsToDelete | Sort-Object { $_.metadata.name }) {
    $name = $job.metadata.name
    $status = $job.status.phase
    $vm = if ($job.status.assignedVM) { $job.status.assignedVM } else { "-" }
    
    $statusSymbol = switch ($status) {
        "Succeeded" { "✓" }
        "Failed" { "✗" }
        "Running" { "→" }
        default { "?" }
    }
    
    Write-Host "  $statusSymbol $name [$status] on $vm"
}

# Confirm deletion
if (-not $Force) {
    Write-Host "`nDo you want to proceed with deletion?" -ForegroundColor Yellow
    $confirm = Read-Host "Type 'yes' to confirm"
    
    if ($confirm -ne "yes") {
        Write-Host "Deletion cancelled." -ForegroundColor Gray
        exit 0
    }
}

# Delete jobs
Write-Host "`nDeleting jobs..." -ForegroundColor Red
$deleted = 0
$failed = 0

foreach ($job in $jobsToDelete) {
    try {
        kubectl delete vmjob $job.metadata.name -n $Namespace --wait=false 2>$null | Out-Null
        $deleted++
        Write-Host "  ✓ Deleted: $($job.metadata.name)" -ForegroundColor Green
    } catch {
        $failed++
        Write-Host "  ✗ Failed to delete: $($job.metadata.name)" -ForegroundColor Red
    }
}

Write-Host "`nCleanup Summary:" -ForegroundColor Cyan
Write-Host "  Deleted: $deleted jobs" -ForegroundColor Green
if ($failed -gt 0) {
    Write-Host "  Failed: $failed jobs" -ForegroundColor Red
}

Write-Host "`nCleanup complete!" -ForegroundColor Green
