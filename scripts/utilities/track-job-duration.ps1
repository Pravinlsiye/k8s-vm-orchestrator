param(
    [int]$RefreshSeconds = 5,
    [switch]$Continuous = $true  # Default to continuous monitoring
)

Write-Host "`n🚀 VMJob Duration Tracker" -ForegroundColor Cyan
Write-Host "========================" -ForegroundColor Cyan
Write-Host "Tracking all jobs until completion..." -ForegroundColor Yellow
Write-Host "You can walk away - final duration will be shown when done!" -ForegroundColor Gray
Write-Host ""

$scriptStartTime = Get-Date
$lastStatus = ""
$completedJobs = @{} # Track which jobs we've already logged
$summaryFile = "job-completion-log-$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"

# Initialize the log file
@"
VMJob Completion Log
====================
Started: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Tracking all jobs as they complete...

"@ | Out-File $summaryFile

Write-Host "📄 Logging to: $summaryFile" -ForegroundColor Gray
Write-Host ""

do {
    # Get all jobs with kubectl (includes age)
    $rawOutput = kubectl get vmjobs -o wide 2>$null
    $jobs = kubectl get vmjobs -o json 2>$null | ConvertFrom-Json
    
    if (-not $jobs -or $jobs.items.Count -eq 0) {
        Write-Host "`rNo VMJobs found!" -NoNewline -ForegroundColor Red
        Start-Sleep -Seconds $RefreshSeconds
        continue
    }
    
    # Count statuses
    $total = $jobs.items.Count
    $completed = 0
    $failed = 0
    $running = 0
    $pending = 0
    $oldestAge = 0
    
    foreach ($job in $jobs.items) {
        $status = $job.status.phase ?? "Pending"
        $name = $job.metadata.name
        
        switch ($status) {
            "Completed" { $completed++ }
            "Failed" { $failed++ }
            "Running" { $running++ }
            "Pending" { $pending++ }
        }
        
        # Log completed/failed jobs immediately
        if ($status -in @("Completed", "Failed") -and -not $completedJobs.ContainsKey($name)) {
            $completedJobs[$name] = $true
            
            # Get job details
            $vm = $job.status.assignedVM ?? "N/A"
            $exitCode = $job.status.exitCode ?? "N/A"
            $completionTime = Get-Date -Format 'HH:mm:ss'
            
            # Get job age/duration - search in kubectl output
            $jobAge = "Unknown"
            foreach ($line in $rawOutput -split "`n") {
                if ($line -match $name -and $line -match '\s+(\d+[smhd](?:\d+[smhd])?)\s*$') {
                    $jobAge = $matches[1]
                    break
                }
            }
            
            # Write to log file immediately
            $logEntry = @"
[$completionTime] $name
  Status: $status
  VM: $vm
  Exit Code: $exitCode
  Duration: $jobAge

"@
            $logEntry | Out-File $summaryFile -Append
            
            # Also show in console (clear current line first)
            Write-Host "`r" -NoNewline
            Write-Host (" " * 100) -NoNewline  # Clear the line
            Write-Host "`r" -NoNewline
            
            if ($status -eq "Completed") {
                Write-Host "✓ Job completed: $name (Duration: $jobAge)" -ForegroundColor Green
            } else {
                Write-Host "✗ Job failed: $name (Exit: $exitCode, Duration: $jobAge)" -ForegroundColor Red
            }
        }
    }
    
    # Get the oldest job age from kubectl output - handle complex formats like "3h56m"
    foreach ($line in $rawOutput -split "`n") {
        if ($line -match '\s+(\d+[dhms](?:\d+[dhms])*)\s*$') {
            $ageString = $matches[1]
            $totalMinutes = 0
            
            # Parse different time units
            if ($ageString -match '(\d+)d') { $totalMinutes += [int]$matches[1] * 1440 }
            if ($ageString -match '(\d+)h') { $totalMinutes += [int]$matches[1] * 60 }
            if ($ageString -match '(\d+)m') { $totalMinutes += [int]$matches[1] }
            if ($ageString -match '(\d+)s') { $totalMinutes += [int]$matches[1] / 60 }
            
            if ($totalMinutes -gt $oldestAge) {
                $oldestAge = $totalMinutes
            }
        }
    }
    
    # Fallback: If we couldn't parse age, calculate from creation timestamp
    if ($oldestAge -eq 0 -and $jobs.items.Count -gt 0) {
        $oldestCreation = $jobs.items | 
            Where-Object { $_.metadata.creationTimestamp } | 
            ForEach-Object { [DateTime]$_.metadata.creationTimestamp } | 
            Sort-Object | 
            Select-Object -First 1
        
        if ($oldestCreation) {
            $oldestAge = ((Get-Date) - $oldestCreation).TotalMinutes
        }
    }
    
    # Calculate progress
    $progress = if ($total -gt 0) { [int](($completed + $failed) * 100 / $total) } else { 0 }
    
    # Format duration
    $durationHours = [math]::Floor($oldestAge / 60)
    $durationMinutes = [int]($oldestAge % 60)
    $durationString = if ($durationHours -gt 0) {
        "$durationHours hours, $durationMinutes minutes"
    } else {
        "$durationMinutes minutes"
    }
    
    # Build status line with more details
    $currentTime = Get-Date -Format 'HH:mm:ss'
    $currentStatus = "[$currentTime] Duration: $durationString | Jobs: $total | ✓ Completed: $completed | ✗ Failed: $failed | ▶ Running: $running | ◊ Pending: $pending | Progress: $progress%"
    
    # Only update if status changed (reduces flicker)
    if ($currentStatus -ne $lastStatus) {
        Write-Host "`r$currentStatus" -NoNewline -ForegroundColor $(
            if ($progress -eq 100) { "Green" }
            elseif ($failed -gt 0) { "Yellow" }
            else { "Cyan" }
        )
        $lastStatus = $currentStatus
    }
    
    # Check if all done
    if ($completed + $failed -eq $total) {
        Write-Host "" # New line
        Write-Host "`n✅ ALL JOBS COMPLETED!" -ForegroundColor Green
        Write-Host "===========================================" -ForegroundColor Green
        Write-Host ""
        Write-Host "📊 FINAL RESULTS:" -ForegroundColor Cyan
        Write-Host "Total Jobs: $total" -ForegroundColor White
        Write-Host "✓ Completed: $completed ($([int]($completed * 100 / $total))%)" -ForegroundColor Green
        Write-Host "✗ Failed: $failed ($([int]($failed * 100 / $total))%)" -ForegroundColor $(if($failed -gt 0){"Red"}else{"Gray"})
        Write-Host ""
        Write-Host "⏱️  TOTAL DURATION: $durationString" -ForegroundColor Yellow
        Write-Host "   ($([int]$oldestAge) minutes total)" -ForegroundColor Gray
        Write-Host ""
        
        # Show VM distribution
        $vmStats = @{}
        foreach ($job in $jobs.items) {
            $vm = $job.status.assignedVM ?? "Unassigned"
            if (-not $vmStats.ContainsKey($vm)) { $vmStats[$vm] = 0 }
            $vmStats[$vm]++
        }
        
        Write-Host "🖥️  VM Distribution:" -ForegroundColor Cyan
        foreach ($vm in $vmStats.Keys | Sort-Object) {
            Write-Host "$vm : $($vmStats[$vm]) jobs" -ForegroundColor White
        }
        
        # Performance metrics
        if ($oldestAge -gt 0) {
            $jobsPerHour = [math]::Round($total / ($oldestAge / 60), 2)
            Write-Host ""
            Write-Host "⚡ Performance: $jobsPerHour jobs/hour" -ForegroundColor Cyan
        }
        
        Write-Host ""
        Write-Host "✨ Tracking complete at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Green
        Write-Host ""
        
        # Collect individual job details
        $jobDetails = @()
        foreach ($job in $jobs.items | Sort-Object { $_.metadata.name }) {
            $name = $job.metadata.name
            $status = $job.status.phase ?? "Unknown"
            $vm = $job.status.assignedVM ?? "N/A"
            $exitCode = $job.status.exitCode ?? "N/A"
            
            # Get job age from kubectl output
            $jobAge = "Unknown"
            foreach ($line in $rawOutput -split "`n") {
                if ($line -match $name -and $line -match '\s+(\d+[dhms](?:\d+[dhms])*)\s*$') {
                    $jobAge = $matches[1]
                    break
                }
            }
            
            $jobDetails += [PSCustomObject]@{
                Name = $name
                Status = $status
                VM = $vm
                ExitCode = $exitCode
                Duration = $jobAge
            }
        }
        
        # Append final summary to the same log file
        $summaryContent = @"

========================================
FINAL SUMMARY - ALL JOBS COMPLETED
========================================
Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Total Jobs: $total
Completed: $completed
Failed: $failed
Total Duration: $durationString ($([int]$oldestAge) minutes)

VM Distribution:
$(foreach ($vm in $vmStats.Keys | Sort-Object) { "$vm : $($vmStats[$vm]) jobs" })

Performance: $jobsPerHour jobs/hour

Individual Job Details:
=======================
$($jobDetails | ForEach-Object {
    "$($_.Name)`n  Status: $($_.Status)`n  VM: $($_.VM)`n  Exit Code: $($_.ExitCode)`n  Duration: $($_.Duration)`n"
})

Summary by Status:
==================
Completed Jobs:
$($jobDetails | Where-Object { $_.Status -eq "Completed" } | ForEach-Object { "  $($_.Name) - $($_.Duration) on $($_.VM)" } | Out-String)

Failed Jobs:
$($jobDetails | Where-Object { $_.Status -eq "Failed" } | ForEach-Object { "  $($_.Name) - $($_.Duration) on $($_.VM) (Exit: $($_.ExitCode))" } | Out-String)
"@
        
        $summaryContent | Out-File $summaryFile -Append
        
        Write-Host "📄 Complete log saved to: $summaryFile" -ForegroundColor Gray
        break
    }
    
    if ($Continuous) {
        Start-Sleep -Seconds $RefreshSeconds
    }
    
} while ($Continuous)

Write-Host "`nNote: Duration is based on the oldest job's age from kubectl." -ForegroundColor Gray
Write-Host "This gives the most accurate total execution time." -ForegroundColor Gray
