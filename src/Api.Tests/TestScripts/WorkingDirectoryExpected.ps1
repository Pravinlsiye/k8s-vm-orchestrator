# Stage-based VMJob Execution Script
# Job: workdir-job
# Generated: {TIMESTAMP} UTC

# Variables

# Global Environment Variables

# Error Handling
$ErrorActionPreference = 'Stop'
$stageResults = @{}

# Stage 1: test
try {
    Write-Host '===== Starting Stage: test =====' -ForegroundColor Cyan
    # Step 1
    Write-Host 'Executing: Step 1' -ForegroundColor Yellow
    Set-Location 'C:\custom\path'
    Get-Location

    Write-Host '===== Completed Stage: test =====' -ForegroundColor Green
    $stageResults['test'] = 'Success'
} catch {
    Write-Host '===== Failed Stage: test =====' -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    $stageResults['test'] = 'Failed'
    throw
}
# Summary
Write-Host '===== Job Summary =====' -ForegroundColor Cyan
$stageResults.GetEnumerator() | ForEach-Object {
    Write-Host "Stage $($_.Key): $($_.Value)" -ForegroundColor $(if($_.Value -eq 'Success'){'Green'}else{'Red'})
}
