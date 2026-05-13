# Create 100 VMJobs for File Creation

$apiUrl = "https://localhost:5001/api/vmjobs"
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

Write-Host "`n=== Creating 100 VMJobs ===" -ForegroundColor Cyan
Write-Host "This will create 100 files on the VMs" -ForegroundColor Yellow

$startTime = Get-Date
$baseTimestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$successCount = 0
$failCount = 0
$jobNames = @()

Write-Host "`nSubmitting jobs..." -ForegroundColor Cyan

for ($i = 1; $i -le 100; $i++) {
    $jobName = "job-$baseTimestamp-$i"
    $fileName = "file-$i-$baseTimestamp.txt"
    
    $job = @{
        name = $jobName
        stages = @(
            @{
                name = "create"
                steps = @(
                    @{
                        task = "PowerShell"
                        inputs = @{
                            script = @(
                                "`$dir = Join-Path 'C:' 'vmjob-results'",
                                "New-Item -Path `$dir -ItemType Directory -Force | Out-Null",
                                "`$file = Join-Path `$dir '$fileName'",
                                "'File #$i created at $(Get-Date)' | Out-File -FilePath `$file -Force",
                                "Write-Host 'Created: $fileName'"
                            )
                        }
                    }
                )
            }
        )
    }
    
    try {
        $response = Invoke-RestMethod -Uri $apiUrl `
                                     -Method Post `
                                     -Body ($job | ConvertTo-Json -Depth 10) `
                                     -ContentType "application/json" `
                                     -ErrorAction Stop
        $successCount++
        $jobNames += $jobName
        Write-Host "[$i/100] ✓ Created: $jobName" -ForegroundColor Green
    } catch {
        $failCount++
        Write-Host "[$i/100] ✗ Failed: $jobName - $_" -ForegroundColor Red
    }
    
    # Small delay to avoid overwhelming the API
    if ($i % 10 -eq 0) {
        Start-Sleep -Milliseconds 100
    }
}

$endTime = Get-Date
$duration = $endTime - $startTime

Write-Host "`n=== Submission Complete ===" -ForegroundColor Cyan
Write-Host "Total time: $($duration.TotalSeconds) seconds"
Write-Host "Success: $successCount" -ForegroundColor Green
Write-Host "Failed: $failCount" -ForegroundColor Red

Write-Host "`nWaiting for jobs to complete..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

# Check job statuses
Write-Host "`nChecking job statuses..." -ForegroundColor Cyan
$completed = 0
$running = 0
$failed = 0
$pending = 0

$allJobs = kubectl get vmjob -n default -o json | ConvertFrom-Json
$ourJobs = $allJobs.items | Where-Object { $_.metadata.name -like "job-$baseTimestamp-*" }

foreach ($job in $ourJobs) {
    switch ($job.status.phase) {
        "Succeeded" { $completed++ }
        "Running" { $running++ }
        "Failed" { $failed++ }
        "Pending" { $pending++ }
    }
}

Write-Host "`n=== Job Status Summary ===" -ForegroundColor Cyan
Write-Host "Total jobs: $($ourJobs.Count)"
Write-Host "Completed: $completed" -ForegroundColor Green
Write-Host "Running: $running" -ForegroundColor Yellow
Write-Host "Failed: $failed" -ForegroundColor Red
Write-Host "Pending: $pending" -ForegroundColor Gray

# Check VM distribution
Write-Host "`n=== VM Distribution ===" -ForegroundColor Cyan
$vmDistribution = $ourJobs | Group-Object -Property { $_.status.assignedVM } | 
    Select-Object @{Name='VM';Expression={$_.Name}}, Count |
    Sort-Object Count -Descending

$vmDistribution | Format-Table -AutoSize

Write-Host "`nTo check if files were created:" -ForegroundColor Yellow
Write-Host "  Look for files in C:\vmjob-results\ on the VMs"
Write-Host "  Files are named: file-[1-100]-$baseTimestamp.txt"

Write-Host "`nTo clean up all jobs:" -ForegroundColor Yellow
Write-Host "  kubectl delete vmjob --all -n default"
