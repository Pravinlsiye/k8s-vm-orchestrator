# Test File Creation on VM

$apiUrl = "https://localhost:5001/api/vmjobs"
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

Write-Host "`n=== Testing File Creation on VM ===" -ForegroundColor Cyan

# Create a unique filename with timestamp
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$fileName = "test-file-$timestamp.txt"

# API request to create file
$job = @{
    name = "test-file-creation-$timestamp"
    description = "Test if file is actually created on VM"
    
    variables = @{
        fileName = $fileName
        timestamp = $timestamp
        content = "This file was created at $timestamp by VMJob API test"
    }
    
    stages = @(
        @{
            name = "create-and-verify"
            displayName = "Create and Verify File"
            steps = @(
                @{
                    task = "PowerShell"
                    displayName = "Create test file"
                    inputs = @{
                        script = @(
                            'Write-Host "========================================" -ForegroundColor Cyan'
                            'Write-Host "File Creation Test" -ForegroundColor Cyan'
                            'Write-Host "========================================" -ForegroundColor Cyan'
                            ''
                            '# Create directory'
                            '$dir = [System.IO.Path]::Combine("C:", "vmjob-results")'
                            '[System.IO.Directory]::CreateDirectory($dir) | Out-Null'
                            'Write-Host "✓ Directory ready: $dir" -ForegroundColor Green'
                            ''
                            '# Create file'
                            '$filePath = [System.IO.Path]::Combine($dir, "$(variables.fileName)")'
                            'Write-Host "Creating file: $filePath"'
                            '"$(variables.content)" | Out-File -FilePath $filePath -Force'
                            ''
                            '# Verify file exists'
                            'if (Test-Path $filePath) {'
                            '    Write-Host "✓ FILE CREATED SUCCESSFULLY!" -ForegroundColor Green'
                            '    $fileInfo = Get-Item $filePath'
                            '    Write-Host "  Path: $($fileInfo.FullName)"'
                            '    Write-Host "  Size: $($fileInfo.Length) bytes"'
                            '    Write-Host "  Created: $($fileInfo.CreationTime)"'
                            '} else {'
                            '    Write-Host "✗ FILE NOT FOUND!" -ForegroundColor Red'
                            '    throw "File creation failed"'
                            '}'
                            ''
                            '# Display content'
                            'Write-Host "`nFile content:" -ForegroundColor Yellow'
                            'Get-Content $filePath'
                            ''
                            '# List all files in directory'
                            'Write-Host "`nAll files in $dir`:" -ForegroundColor Yellow'
                            'Get-ChildItem $dir | Select-Object Name, Length, CreationTime | Format-Table'
                        )
                    }
                }
            )
        }
    )
}

Write-Host "Creating job to test file creation..."
Write-Host "File name: $fileName" -ForegroundColor Yellow

try {
    # Submit job
    $response = Invoke-RestMethod -Uri $apiUrl -Method Post -Body ($job | ConvertTo-Json -Depth 10) -ContentType "application/json"
    Write-Host "✓ Job created: $($response.name)" -ForegroundColor Green
    
    # Wait for completion
    Write-Host "`nWaiting for job to complete..."
    $maxWait = 20
    $waited = 0
    
    while ($waited -lt $maxWait) {
        Start-Sleep -Seconds 2
        $waited += 2
        
        $status = kubectl get vmjob $response.name -n default -o json 2>$null | ConvertFrom-Json
        
        if ($status.status.phase -eq "Succeeded") {
            Write-Host "`n✓ JOB SUCCEEDED!" -ForegroundColor Green
            Write-Host "  VM: $($status.status.assignedVM)"
            
            # Show the logs
            Write-Host "`n=== Job Output ===" -ForegroundColor Cyan
            if ($status.status.logs) {
                # Extract the important parts
                $logs = $status.status.logs
                if ($logs -match "FILE CREATED SUCCESSFULLY") {
                    Write-Host "✓ FILE WAS SUCCESSFULLY CREATED ON VM!" -ForegroundColor Green -BackgroundColor DarkGreen
                    
                    # Extract file info
                    if ($logs -match "Path: (.+)") {
                        Write-Host "  File path: $($Matches[1])" -ForegroundColor Green
                    }
                    if ($logs -match "Size: (\d+) bytes") {
                        Write-Host "  File size: $($Matches[1]) bytes" -ForegroundColor Green
                    }
                }
                
                # Show directory listing
                if ($logs -match "All files in (.+):") {
                    Write-Host "`nDirectory listing shows these files exist on the VM:"
                    $dirSection = $logs.Substring($logs.IndexOf("All files in"))
                    $lines = $dirSection -split "`n" | Select-Object -Skip 1 -First 10
                    foreach ($line in $lines) {
                        if ($line -match "test-file") {
                            Write-Host "  $line" -ForegroundColor Yellow
                        }
                    }
                }
            }
            break
        }
        elseif ($status.status.phase -eq "Failed") {
            Write-Host "`n✗ Job failed!" -ForegroundColor Red
            if ($status.status.logs) {
                Write-Host $status.status.logs
            }
            break
        }
        else {
            Write-Host "." -NoNewline
        }
    }
    
    Write-Host "`n`nTo manually verify the file on the VM:"
    Write-Host "  The file should be at: C:\vmjob-results\$fileName" -ForegroundColor Cyan
    Write-Host "  On VM: $($status.status.assignedVM)" -ForegroundColor Cyan
    
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}
