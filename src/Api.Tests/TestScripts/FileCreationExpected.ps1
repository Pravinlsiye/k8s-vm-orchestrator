# Stage-based VMJob Execution Script
# Job: file-creation-job
# Generated: {TIMESTAMP} UTC

# Variables
$var_outputPath = 'C:\vmjob-results'
$var_fileName = 'test-output.txt'

# Global Environment Variables

# Error Handling
$ErrorActionPreference = 'Stop'
$stageResults = @{}

# Stage 1: Setup Directory
try {
    Write-Host '===== Starting Stage: Setup Directory =====' -ForegroundColor Cyan
    # Create directory
    Write-Host 'Executing: Create directory' -ForegroundColor Yellow
    
if (!(Test-Path 'C:\vmjob-results')) {
    New-Item -ItemType Directory -Path 'C:\vmjob-results' -Force | Out-Null
    Write-Host 'Created directory: C:\vmjob-results'
}

    Write-Host '===== Completed Stage: Setup Directory =====' -ForegroundColor Green
    $stageResults['setup'] = 'Success'
} catch {
    Write-Host '===== Failed Stage: Setup Directory =====' -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    $stageResults['setup'] = 'Failed'
    throw
}
# Stage 2: Create File
try {
    Write-Host '===== Starting Stage: Create File =====' -ForegroundColor Cyan
    # Write file
    Write-Host 'Executing: Write file' -ForegroundColor Yellow
    Set-Location 'C:\vmjob-results'
    
$content = @'
File created by VMJob API test
Date: $(Get-Date)
Path: C:\vmjob-results\test-output.txt
'@
$content | Out-File -FilePath 'test-output.txt' -Force
Write-Host 'File created successfully'

    Write-Host '===== Completed Stage: Create File =====' -ForegroundColor Green
    $stageResults['create-file'] = 'Success'
} catch {
    Write-Host '===== Failed Stage: Create File =====' -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    $stageResults['create-file'] = 'Failed'
    throw
}
# Summary
Write-Host '===== Job Summary =====' -ForegroundColor Cyan
$stageResults.GetEnumerator() | ForEach-Object {
    Write-Host "Stage $($_.Key): $($_.Value)" -ForegroundColor $(if($_.Value -eq 'Success'){'Green'}else{'Red'})
}
