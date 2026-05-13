#requires -Version 7.0
# Destroy ALL infra (Terraform) + clean up local state.

param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

Write-Host '=============================================='
Write-Host '   k8s-vm-orchestrator - Complete Cleanup'
Write-Host '=============================================='

Write-Host "`nThis will destroy:" -ForegroundColor Yellow
Write-Host "  - All Azure resources (VMs, AKS, ACR, Storage)" -ForegroundColor Red
Write-Host "  - Virtual networks and security groups" -ForegroundColor Red
Write-Host "  - All Kubernetes resources and data" -ForegroundColor Red
Write-Host "  - Local Terraform state" -ForegroundColor Red

Write-Host "`nWARNING: This action CANNOT be undone." -ForegroundColor Red

if (-not $Force) {
    $confirm = Read-Host 'Type "DELETE ALL" to confirm'
    if ($confirm -ne 'DELETE ALL') {
        Write-Host "`nCancelled - no resources deleted." -ForegroundColor Green
        exit 0
    }
}

$tfDir = Join-Path $PSScriptRoot '..\..\deploy\terraform'
Push-Location $tfDir
try {
    $stateList = terraform state list 2>$null
    if ($LASTEXITCODE -eq 0 -and $stateList) {
        Write-Host "`nResources to be destroyed:" -ForegroundColor Cyan
        $stateList | ForEach-Object { Write-Host "  - $_" }

        Write-Host "`nDestroying..." -ForegroundColor Yellow
        terraform destroy -auto-approve
        if ($LASTEXITCODE -eq 0) {
            Write-Host 'All Azure resources destroyed' -ForegroundColor Green
        }
        else {
            Write-Host 'Some resources failed to destroy. Check Azure Portal.' -ForegroundColor Red
        }
    }
    else {
        Write-Host "`nNo Terraform resources found." -ForegroundColor Yellow
    }

    Write-Host "`nRemoving Terraform state..." -ForegroundColor Yellow
    Remove-Item -Path '.terraform', 'terraform.tfstate*', '.terraform.lock.hcl' -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'Terraform state cleaned' -ForegroundColor Green
}
finally {
    Pop-Location
}

$setupDir = Join-Path $PSScriptRoot '..\setup'
Push-Location $setupDir
try {
    foreach ($file in 'vm_outputs.json', 'kubeconfig.yaml', 'acr_info.json', 'tfplan') {
        if (Test-Path $file) {
            Remove-Item $file -Force
            Write-Host "Removed $file" -ForegroundColor Green
        }
    }
}
finally {
    Pop-Location
}

$currentCtx = kubectl config current-context 2>$null
if ($currentCtx -match 'k8s-vmjob-workers-aks') {
    Write-Host "`nRemoving kubectl context..." -ForegroundColor Yellow
    kubectl config delete-context k8s-vmjob-workers-aks 2>$null | Out-Null
    kubectl config delete-cluster k8s-vmjob-workers-aks 2>$null | Out-Null
    Write-Host 'Kubectl context removed' -ForegroundColor Green
}

Write-Host "`n=============================================="
Write-Host '  Cleanup Complete' -ForegroundColor Green
Write-Host '=============================================='
Write-Host 'To deploy again, run: ./setup-all-cloud.ps1' -ForegroundColor Yellow
