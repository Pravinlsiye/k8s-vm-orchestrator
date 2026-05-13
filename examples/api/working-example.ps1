# Working Example: Create File with New VMJob API Schema

$apiUrl = "https://localhost:5001/api/vmjobs"

# Skip SSL validation
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

Write-Host "`nVMJob API - Working File Creation Example" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# Simple working job that creates a file
$job = @{
    name = "working-file-example"
    description = "Creates a test file using stage-based execution"
    
    variables = @{
        content = "Hello from VMJob API! This file was created using the new stage-based schema."
    }
    
    stages = @(
        @{
            name = "create"
            displayName = "Create Test File"
            steps = @(
                @{
                    task = "PowerShell"
                    inputs = @{
                        script = @(
                            'Write-Host "Creating test file..."'
                            '$dir = [System.IO.Path]::Combine("C:", "vmjob-results")'
                            '[System.IO.Directory]::CreateDirectory($dir) | Out-Null'
                            '$file = [System.IO.Path]::Combine($dir, "test.txt")'
                            '"$(variables.content)" | Out-File -FilePath $file -Force'
                            'Write-Host "File created successfully!"'
                            'Write-Host "Content:"'
                            'Get-Content $file'
                        )
                    }
                }
            )
        }
    )
}

Write-Host "`nSubmitting job..."
kubectl delete vmjob working-file-example -n default --ignore-not-found 2>$null | Out-Null

$response = Invoke-RestMethod -Uri $apiUrl -Method Post -Body ($job | ConvertTo-Json -Depth 10) -ContentType "application/json"
Write-Host "✓ Job created: $($response.name)" -ForegroundColor Green

Write-Host "`nWaiting for completion..."
Start-Sleep -Seconds 8

$result = kubectl get vmjob $response.name -n default -o json | ConvertFrom-Json
Write-Host "`nJob Status: $($result.status.phase)" -ForegroundColor $(if($result.status.phase -eq "Succeeded"){"Green"}else{"Red"})
Write-Host "Assigned VM: $($result.status.assignedVM)"

if ($result.status.phase -eq "Succeeded") {
    Write-Host "`n✓ SUCCESS! File was created on the VM." -ForegroundColor Green
    Write-Host "`nTo verify the file on the VM, you can check:"
    Write-Host "  C:\vmjob-results\test.txt" -ForegroundColor Gray
}
