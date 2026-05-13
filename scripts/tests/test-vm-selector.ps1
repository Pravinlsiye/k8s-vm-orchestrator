param(
    [string]$ApiUrl = "https://localhost:5001"
)

Write-Host "Testing VM Selector Functionality" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan

# Skip SSL certificate validation for local testing
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

# Test 1: Job with specific node name
Write-Host "`nTest 1: Job targeting specific VM by name" -ForegroundColor Yellow
$job1 = @{
    name = "test-specific-vm"
    advanced = @{
        targetVM = "k8s-vmjob-workers-vm-2"
    }
    stages = @(
        @{
            name = "test-stage"
            steps = @(
                @{
                    task = "PowerShell"
                    inputs = @{
                        script = @(
                            "Write-Host 'Running on VM: $env:COMPUTERNAME'"
                            "Get-NetIPAddress | Where-Object AddressFamily -eq IPv4 | Select-Object IPAddress"
                        )
                    }
                }
            )
        }
    )
}

try {
    $response = Invoke-RestMethod -Uri "$ApiUrl/api/vmjobs" -Method Post -Body ($job1 | ConvertTo-Json -Depth 10) -ContentType "application/json"
    Write-Host "✓ Job created: $($response.name), Assigned to: $($response.assignedVM)" -ForegroundColor Green
} catch {
    Write-Host "✗ Failed to create job: $_" -ForegroundColor Red
}

# Test 2: Job with OS selector
Write-Host "`nTest 2: Job with OS selector (windows)" -ForegroundColor Yellow
$job2 = @{
    name = "test-os-selector"
    spec = @{
        vmSelector = @{
            os = "windows"
        }
        command = "powershell.exe"
        args = @("-Command", "Write-Host 'OS: $env:OS'")
    }
}

try {
    $response = Invoke-RestMethod -Uri "$ApiUrl/api/vmjobs" -Method Post -Body ($job2 | ConvertTo-Json -Depth 10) -ContentType "application/json"
    Write-Host "✓ Job created: $($response.name), Assigned to: $($response.assignedVM)" -ForegroundColor Green
} catch {
    Write-Host "✗ Failed to create job: $_" -ForegroundColor Red
}

# Test 3: Job with tag selector
Write-Host "`nTest 3: Job with tag selector" -ForegroundColor Yellow
$job3 = @{
    name = "test-tag-selector"
    spec = @{
        vmSelector = @{
            tags = @{
                environment = "azure"
                role = "worker"
            }
        }
        command = "powershell.exe"
        args = @("-Command", "Write-Host 'VM with specific tags'")
    }
}

try {
    $response = Invoke-RestMethod -Uri "$ApiUrl/api/vmjobs" -Method Post -Body ($job3 | ConvertTo-Json -Depth 10) -ContentType "application/json"
    Write-Host "✓ Job created: $($response.name), Assigned to: $($response.assignedVM)" -ForegroundColor Green
} catch {
    Write-Host "✗ Failed to create job: $_" -ForegroundColor Red
}

# Test 4: Job with combined selectors
Write-Host "`nTest 4: Job with combined selectors (os + tags)" -ForegroundColor Yellow
$job4 = @{
    name = "test-combined-selector"
    spec = @{
        vmSelector = @{
            os = "windows"
            tags = @{
                vmSize = "Standard_B2s"
            }
        }
        command = "powershell.exe"
        args = @("-Command", "Write-Host 'VM matching OS and tags'")
    }
}

try {
    $response = Invoke-RestMethod -Uri "$ApiUrl/api/vmjobs" -Method Post -Body ($job4 | ConvertTo-Json -Depth 10) -ContentType "application/json"
    Write-Host "✓ Job created: $($response.name), Assigned to: $($response.assignedVM)" -ForegroundColor Green
} catch {
    Write-Host "✗ Failed to create job: $_" -ForegroundColor Red
}

# Wait and check job statuses
Write-Host "`nWaiting for jobs to complete..." -ForegroundColor Gray
Start-Sleep -Seconds 5

Write-Host "`nJob Status:" -ForegroundColor Yellow
kubectl get vmjobs -o wide | Select-String "test-"

Write-Host "`nVM Node Status:" -ForegroundColor Yellow
kubectl get vmnodes

Write-Host "`nTo cleanup test jobs:" -ForegroundColor Gray
Write-Host "kubectl delete vmjobs -l 'metadata.name=~test-'" -ForegroundColor White
