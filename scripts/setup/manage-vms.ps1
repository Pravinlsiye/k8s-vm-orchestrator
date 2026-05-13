param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("stop", "start", "status")]
    [string]$Action,
    
    [string]$ResourceGroup = "k8s-vmjob-workers",
    
    [string[]]$VMNames = @(
        "k8s-vmjob-workers-vm-1",
        "k8s-vmjob-workers-vm-2", 
        "k8s-vmjob-workers-vm-3"
    ),
    
    [switch]$WhatIf
)

Write-Host "VM Management Script" -ForegroundColor Cyan
Write-Host "===================" -ForegroundColor Cyan
Write-Host "Action: $Action" -ForegroundColor Yellow
Write-Host "Resource Group: $ResourceGroup" -ForegroundColor Yellow
Write-Host ""

# Check if Azure CLI is installed
if (!(Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Host "Error: Azure CLI not found. Please install Azure CLI." -ForegroundColor Red
    exit 1
}

# Check if logged in to Azure
$account = az account show 2>$null | ConvertFrom-Json
if (!$account) {
    Write-Host "Error: Not logged in to Azure. Please run 'az login' first." -ForegroundColor Red
    exit 1
}

Write-Host "Current Azure Account: $($account.user.name)" -ForegroundColor Gray
Write-Host "Subscription: $($account.name)" -ForegroundColor Gray
Write-Host ""

# Function to get VM status
function Get-VMStatus {
    param([string]$VMName)
    
    $vm = az vm show -g $ResourceGroup -n $VMName --show-details 2>$null | ConvertFrom-Json
    if ($vm) {
        return @{
            Name = $VMName
            PowerState = $vm.powerState
            ProvisioningState = $vm.provisioningState
            PrivateIP = $vm.privateIps
        }
    }
    return $null
}

# Function to update Kubernetes VMNode status
function Update-K8sVMNode {
    param(
        [string]$VMName,
        [string]$Status
    )
    
    # Check if kubectl is available
    if (Get-Command kubectl -ErrorAction SilentlyContinue) {
        $nodeName = $VMName
        
        # Update VMNode status
        $patch = @{
            status = @{
                phase = $Status
                lastHeartbeat = (Get-Date -Format "o")
                ready = if ($Status -eq "Running") { $true } else { $false }
            }
        } | ConvertTo-Json -Compress
        
        kubectl patch vmnode $nodeName --type=merge -p $patch 2>$null
    }
}

switch ($Action) {
    "status" {
        Write-Host "Checking VM Status..." -ForegroundColor Yellow
        Write-Host ""
        
        $allVMs = @()
        foreach ($vmName in $VMNames) {
            $status = Get-VMStatus -VMName $vmName
            if ($status) {
                $allVMs += $status
                $color = switch ($status.PowerState) {
                    "VM running" { "Green" }
                    "VM deallocated" { "Yellow" }
                    default { "Red" }
                }
                Write-Host "$($status.Name): " -NoNewline
                Write-Host $status.PowerState -ForegroundColor $color
                Write-Host "  Private IP: $($status.PrivateIP)" -ForegroundColor Gray
            } else {
                Write-Host "${vmName}: Not found" -ForegroundColor Red
            }
        }
        
        # Summary
        $running = ($allVMs | Where-Object { $_.PowerState -eq "VM running" }).Count
        $stopped = ($allVMs | Where-Object { $_.PowerState -eq "VM deallocated" }).Count
        
        Write-Host ""
        Write-Host "Summary: $running running, $stopped stopped" -ForegroundColor Cyan
    }
    
    "stop" {
        Write-Host "WARNING: This will deallocate (stop) the VMs." -ForegroundColor Yellow
        Write-Host "- Compute charges will stop" -ForegroundColor Green
        Write-Host "- Disks and licenses will be preserved" -ForegroundColor Green
        Write-Host "- VMs can be restarted later" -ForegroundColor Green
        Write-Host "- Private IPs will be retained" -ForegroundColor Green
        Write-Host ""
        
        if (!$WhatIf) {
            Write-Host "Are you sure you want to stop these VMs? (y/N): " -NoNewline -ForegroundColor Yellow
            $confirm = Read-Host
            if ($confirm -ne 'y') {
                Write-Host "Operation cancelled." -ForegroundColor Red
                exit 0
            }
        }
        
        # First, delete all VMJobs in Kubernetes to prevent new jobs
        if (Get-Command kubectl -ErrorAction SilentlyContinue) {
            Write-Host ""
            Write-Host "Cleaning up Kubernetes VMJobs..." -ForegroundColor Yellow
            kubectl delete vmjobs --all 2>$null
        }
        
        foreach ($vmName in $VMNames) {
            Write-Host ""
            Write-Host "Stopping $vmName..." -ForegroundColor Yellow
            
            if ($WhatIf) {
                Write-Host "[WhatIf] Would execute: az vm deallocate -g $ResourceGroup -n $vmName" -ForegroundColor Gray
            } else {
                # Update K8s VMNode to NotReady
                Update-K8sVMNode -VMName $vmName -Status "Stopped"
                
                # Deallocate the VM
                az vm deallocate -g $ResourceGroup -n $vmName --no-wait
                Write-Host "Deallocate command sent for $vmName" -ForegroundColor Green
            }
        }
        
        if (!$WhatIf) {
            Write-Host ""
            Write-Host "Waiting for VMs to stop..." -ForegroundColor Yellow
            
            # Wait for all VMs to be deallocated
            foreach ($vmName in $VMNames) {
                az vm wait -g $ResourceGroup -n $vmName --custom "powerState=='VM deallocated'"
                Write-Host "$vmName stopped successfully" -ForegroundColor Green
            }
            
            Write-Host ""
            Write-Host "All VMs have been stopped (deallocated)." -ForegroundColor Green
            Write-Host "Your licenses and data are preserved." -ForegroundColor Green
            Write-Host "To restart, run: .\manage-vms.ps1 -Action start" -ForegroundColor Cyan
        }
    }
    
    "start" {
        Write-Host "Starting VMs..." -ForegroundColor Yellow
        Write-Host "- This will resume compute charges" -ForegroundColor Yellow
        Write-Host "- All data and licenses are intact" -ForegroundColor Green
        Write-Host ""
        
        if (!$WhatIf) {
            Write-Host "Proceed with starting VMs? (y/N): " -NoNewline -ForegroundColor Yellow
            $confirm = Read-Host
            if ($confirm -ne 'y') {
                Write-Host "Operation cancelled." -ForegroundColor Red
                exit 0
            }
        }
        
        foreach ($vmName in $VMNames) {
            Write-Host ""
            Write-Host "Starting $vmName..." -ForegroundColor Yellow
            
            if ($WhatIf) {
                Write-Host "[WhatIf] Would execute: az vm start -g $ResourceGroup -n $vmName" -ForegroundColor Gray
            } else {
                az vm start -g $ResourceGroup -n $vmName --no-wait
                Write-Host "Start command sent for $vmName" -ForegroundColor Green
            }
        }
        
        if (!$WhatIf) {
            Write-Host ""
            Write-Host "Waiting for VMs to start..." -ForegroundColor Yellow
            
            # Wait for all VMs to be running
            foreach ($vmName in $VMNames) {
                az vm wait -g $ResourceGroup -n $vmName --custom "powerState=='VM running'"
                Write-Host "$vmName started successfully" -ForegroundColor Green
                
                # Update K8s VMNode to Ready
                Update-K8sVMNode -VMName $vmName -Status "Running"
            }
            
            Write-Host ""
            Write-Host "All VMs are now running." -ForegroundColor Green
            Write-Host ""
            Write-Host "Next steps:" -ForegroundColor Cyan
            Write-Host "1. Wait 2-3 minutes for Windows to fully boot" -ForegroundColor White
            Write-Host "2. The controller will automatically detect the VMs" -ForegroundColor White
            Write-Host "3. Check status: kubectl get vmnodes" -ForegroundColor White
        }
    }
}

# Cost estimation
if ($Action -eq "status") {
    Write-Host ""
    Write-Host "Cost Information (Standard_B2s):" -ForegroundColor Cyan
    Write-Host "- Running: ~\$0.0416/hour per VM (East US)" -ForegroundColor Gray
    Write-Host "- Stopped: \$0/hour (only disk storage charges apply)" -ForegroundColor Gray
    
    $running = ($allVMs | Where-Object { $_.PowerState -eq "VM running" }).Count
    if ($running -gt 0) {
        $hourly = $running * 0.0416
        $daily = $hourly * 24
        $monthly = $daily * 30
        Write-Host ""
        Write-Host "Current estimated compute cost:" -ForegroundColor Yellow
        Write-Host "- Hourly: \$$([math]::Round($hourly, 2))" -ForegroundColor White
        Write-Host "- Daily: \$$([math]::Round($daily, 2))" -ForegroundColor White
        Write-Host "- Monthly: \$$([math]::Round($monthly, 2))" -ForegroundColor White
    }
}
