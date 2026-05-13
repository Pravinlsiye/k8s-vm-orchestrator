# Snapshot / watch VMJob statuses.
# Default scope: all VMJobs in the namespace. Use -NamePrefix to filter.

param(
    [string]$NamePrefix,
    [string]$Namespace = 'default',
    [switch]$Watch,
    [int]$RefreshSeconds = 3
)

function Show-TestJobStatus {
    Clear-Host
    
    Write-Host "╔════════════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║                          TEST JOB STATUS                               ║" -ForegroundColor Cyan
    Write-Host "╚════════════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
    
    $jobs = kubectl get vmjobs -n $Namespace -o json | ConvertFrom-Json | Select-Object -ExpandProperty items
    if ($NamePrefix) {
        $jobs = $jobs | Where-Object { $_.metadata.name -like "$NamePrefix*" }
    }

    if (-not $jobs -or $jobs.Count -eq 0) {
        Write-Host "No VMJobs found." -ForegroundColor Yellow
        return
    }
    
    # Group by status
    $statusGroups = $jobs | Group-Object { $_.status.phase }
    
    Write-Host "Summary:" -ForegroundColor Cyan
    foreach ($group in $statusGroups) {
        $color = switch ($group.Name) {
            "Succeeded" { "Green" }
            "Failed" { "Red" }
            "Running" { "Yellow" }
            default { "Gray" }
        }
        Write-Host "  $($group.Name): $($group.Count)" -ForegroundColor $color
    }
    
    Write-Host "`nActive Jobs:" -ForegroundColor Cyan
    $activeJobs = $jobs | Where-Object { $_.status.phase -eq "Running" -or $_.status.phase -eq "Pending" }
    
    if ($activeJobs.Count -eq 0) {
        Write-Host "  No active jobs" -ForegroundColor Gray
    } else {
        Write-Host "┌─────────────────────────────┬────────────┬─────────────────────────────┐"
        Write-Host "│ Job Name                    │ Status     │ VM                          │"
        Write-Host "├─────────────────────────────┼────────────┼─────────────────────────────┤"
        
        foreach ($job in $activeJobs | Sort-Object { $_.metadata.name }) {
            $name = $job.metadata.name
            $status = $job.status.phase
            $vm = if ($job.status.assignedVM) { $job.status.assignedVM } else { "Pending" }
            
            $statusColor = if ($status -eq "Running") { "Yellow" } else { "Gray" }
            
            Write-Host ("│ {0,-27} │ " -f $name.Substring(0, [Math]::Min(27, $name.Length))) -NoNewline
            Write-Host ("{0,-10} " -f $status) -NoNewline -ForegroundColor $statusColor
            Write-Host ("│ {0,-27} │" -f $vm)
        }
        
        Write-Host "└─────────────────────────────┴────────────┴─────────────────────────────┘"
    }
    
    # Show recent completions
    $recentJobs = $jobs | Where-Object { $_.status.phase -ne "Running" -and $_.status.phase -ne "Pending" } | Sort-Object { $_.status.completionTime } -Descending | Select-Object -First 5
    
    if ($recentJobs.Count -gt 0) {
        Write-Host "`nRecent Completions:" -ForegroundColor Cyan
        foreach ($job in $recentJobs) {
            $name = $job.metadata.name
            $status = $job.status.phase
            $exitCode = $job.status.exitCode
            $vm = $job.status.assignedVM
            
            $statusSymbol = if ($status -eq "Succeeded") { "✓" } else { "✗" }
            $color = if ($status -eq "Succeeded") { "Green" } else { "Red" }
            
            Write-Host "  $statusSymbol $name - Exit: $exitCode - VM: $vm" -ForegroundColor $color
        }
    }
    
    Write-Host "`nLast update: $(Get-Date -Format 'HH:mm:ss')" -ForegroundColor Gray
}

# Main execution
if ($Watch) {
    Write-Host "Watching test job status (Press Ctrl+C to exit)..." -ForegroundColor Yellow
    Start-Sleep -Seconds 1
    
    while ($true) {
        Show-TestJobStatus
        Start-Sleep -Seconds $RefreshSeconds
    }
} else {
    Show-TestJobStatus
    Write-Host "`nTip: Use -Watch parameter for continuous monitoring" -ForegroundColor Gray
}
