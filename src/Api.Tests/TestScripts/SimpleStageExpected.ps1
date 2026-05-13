# Stage-based VMJob Execution Script
# Job: stage-job
# Generated: {TIMESTAMP} UTC

# Variables
$var_testVar = 'TestValue'

# Global Environment Variables

# Error Handling
$ErrorActionPreference = 'Stop'
$stageResults = @{}

# Stage 1: Build Stage
try {
    Write-Host '===== Starting Stage: Build Stage =====' -ForegroundColor Cyan
    # Echo test
    Write-Host 'Executing: Echo test' -ForegroundColor Yellow
    Write-Host 'Building with TestValue'

    Write-Host '===== Completed Stage: Build Stage =====' -ForegroundColor Green
    $stageResults['build'] = 'Success'
} catch {
    Write-Host '===== Failed Stage: Build Stage =====' -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    $stageResults['build'] = 'Failed'
    throw
}
# Summary
Write-Host '===== Job Summary =====' -ForegroundColor Cyan
$stageResults.GetEnumerator() | ForEach-Object {
    Write-Host "Stage $($_.Key): $($_.Value)" -ForegroundColor $(if($_.Value -eq 'Success'){'Green'}else{'Red'})
}
