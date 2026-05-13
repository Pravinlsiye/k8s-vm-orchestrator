# Stage-based VMJob Execution Script
# Job: finally-job
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
    Write-Host 'Main task'

    Write-Host '===== Completed Stage: test =====' -ForegroundColor Green
    $stageResults['test'] = 'Success'
} catch {
    Write-Host '===== Failed Stage: test =====' -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    $stageResults['test'] = 'Failed'
    throw
}
# Finally Block
try {
    Write-Host '===== Finally Block =====' -ForegroundColor Magenta
    Write-Host 'Cleanup task'
} catch {
    Write-Host 'Finally block error (non-fatal)' -ForegroundColor Yellow
}
# Summary
Write-Host '===== Job Summary =====' -ForegroundColor Cyan
$stageResults.GetEnumerator() | ForEach-Object {
    Write-Host "Stage $($_.Key): $($_.Value)" -ForegroundColor $(if($_.Value -eq 'Success'){'Green'}else{'Red'})
}
