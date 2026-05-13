# Example: Create a file on VM using VMJob API

$apiUrl = "https://localhost:5001/api/vmjobs"

# Skip SSL validation for local testing
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

# API request body to create a file
$apiBody = @{
    name = "create-file-example"
    description = "Creates a test file on the VM"
    
    # Variables for easy customization
    variables = @{
        fileName = "test-output.txt"
        fileContent = "This is a test file created at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    }
    
    # Stage-based execution
    stages = @(
        @{
            name = "create-file"
            displayName = "Create File on VM"
            steps = @(
                @{
                    task = "PowerShell"
                    displayName = "Create directory and file"
                    inputs = @{
                        script = @(
                            'Write-Host "Creating file on VM..." -ForegroundColor Yellow'
                            # Use Path.Combine to avoid backslash issues with WinRM
                            '$dir = [System.IO.Path]::Combine("C:", "vmjob-results")'
                            '[System.IO.Directory]::CreateDirectory($dir) | Out-Null'
                            'Write-Host "Directory created: $dir" -ForegroundColor Green'
                            ''
                            # Create the file
                            '$filePath = [System.IO.Path]::Combine($dir, "$(variables.fileName)")'
                            '"$(variables.fileContent)" | Out-File -FilePath $filePath -Force'
                            'Write-Host "File created: $filePath" -ForegroundColor Green'
                            ''
                            # Display file contents
                            'Write-Host "`nFile contents:" -ForegroundColor Cyan'
                            'Get-Content $filePath'
                        )
                    }
                }
            )
        }
    )
}

Write-Host "Sending request to VMJob API..." -ForegroundColor Cyan

try {
    # Send the request
    $response = Invoke-RestMethod -Uri $apiUrl `
                                 -Method Post `
                                 -Body ($apiBody | ConvertTo-Json -Depth 10) `
                                 -ContentType "application/json"
    
    Write-Host "`n✓ Job created successfully!" -ForegroundColor Green
    Write-Host "  Job Name: $($response.name)"
    Write-Host "  Namespace: $($response.namespace)"
    
    Write-Host "`nTo check job status:" -ForegroundColor Yellow
    Write-Host "  kubectl get vmjob $($response.name) -n $($response.namespace)"
    
    Write-Host "`nTo see job logs:" -ForegroundColor Yellow
    Write-Host "  kubectl get vmjob $($response.name) -n $($response.namespace) -o jsonpath='{.status.logs}'"
    
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}
