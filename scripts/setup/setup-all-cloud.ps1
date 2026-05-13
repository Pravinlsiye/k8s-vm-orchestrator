#requires -Version 7.0
# Provision Azure infra + deploy controller in one go.
# Requires: terraform, az, docker, kubectl.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Test-Cmd($name) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        Write-Host "Error: $name not installed" -ForegroundColor Red
        exit 1
    }
    Write-Host "  $name installed" -ForegroundColor Green
}

Write-Host '======================================'
Write-Host 'k8s-vm-orchestrator - Complete Cloud Setup'
Write-Host '======================================'

Write-Host "`nChecking prerequisites..." -ForegroundColor Yellow
foreach ($c in 'terraform', 'az', 'docker', 'kubectl') { Test-Cmd $c }

if (-not $env:VM_ADMIN_PASSWORD) {
    Write-Host "`nError: VM_ADMIN_PASSWORD env var not set." -ForegroundColor Red
    Write-Host '  Set with: $env:VM_ADMIN_PASSWORD = "<password>"' -ForegroundColor Yellow
    exit 1
}

Write-Host "`nChecking Azure auth..." -ForegroundColor Yellow
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host 'Error: Not logged in to Azure. Run: az login' -ForegroundColor Red
    exit 1
}
Write-Host "  Logged in as $($account.user.name)" -ForegroundColor Green

$tfDir = Join-Path $PSScriptRoot '..\..\deploy\terraform'
$k8sDir = Join-Path $PSScriptRoot '..\..\deploy\kubernetes'
$ctrlDir = Join-Path $PSScriptRoot '..\..\src\Controller'

Write-Host "`nStep 1: Provisioning Azure infrastructure..." -ForegroundColor Yellow
Push-Location $tfDir
try {
    terraform init
    terraform plan -out=tfplan

    $confirm = Read-Host "`nProceed with deployment? (yes/no)"
    if ($confirm -notmatch '^(y|yes)$') {
        Write-Host 'Deployment cancelled.' -ForegroundColor Red
        exit 1
    }
    terraform apply tfplan
}
finally {
    Pop-Location
}

Write-Host "`nStep 2: Configuring AKS access..." -ForegroundColor Yellow
$creds = terraform -chdir="$tfDir" output -raw aks_get_credentials_command
Invoke-Expression $creds
kubectl cluster-info

Write-Host "`nStep 3: Installing CRDs..." -ForegroundColor Yellow
kubectl apply -f "$k8sDir\crds\vmjob-crd.yaml"
kubectl apply -f "$k8sDir\crds\vmnode-crd.yaml"
kubectl wait --for=condition=established --timeout=60s crd/vmjobs.orchestrator.vmjobs.io
kubectl wait --for=condition=established --timeout=60s crd/vmnodes.orchestrator.vmjobs.io

Write-Host "`nStep 4: Applying RBAC..." -ForegroundColor Yellow
kubectl apply -f "$k8sDir\rbac\controller-serviceaccount.yaml"
kubectl apply -f "$k8sDir\rbac\controller-role.yaml"
kubectl apply -f "$k8sDir\rbac\controller-rolebinding.yaml"

Write-Host "`nStep 5: Building and pushing controller image..." -ForegroundColor Yellow
$acrName = terraform -chdir="$tfDir" output -raw acr_name
$acrLoginServer = terraform -chdir="$tfDir" output -raw acr_login_server
$acrPassword = terraform -chdir="$tfDir" output -raw acr_admin_password

$acrPassword | docker login $acrLoginServer -u $acrName --password-stdin

$imageTag = "$acrLoginServer/vmjob-controller:latest"
Push-Location $ctrlDir
try {
    docker build -t $imageTag .
    docker push $imageTag
}
finally {
    Pop-Location
}

Write-Host "`nStep 6: Creating Kubernetes secrets..." -ForegroundColor Yellow
$vmUsername = terraform -chdir="$tfDir" output -raw admin_username

kubectl delete secret vmjob-credentials --ignore-not-found
kubectl create secret generic vmjob-credentials `
    --from-literal=username="$vmUsername" `
    --from-literal=password="$env:VM_ADMIN_PASSWORD"

$storageAccount = terraform -chdir="$tfDir" output -raw storage_account_name
$storageKey = terraform -chdir="$tfDir" output -raw storage_account_key
$storageContainer = terraform -chdir="$tfDir" output -raw storage_container_name

kubectl delete secret azure-storage-credentials --ignore-not-found
kubectl create secret generic azure-storage-credentials `
    --from-literal=account_name="$storageAccount" `
    --from-literal=account_key="$storageKey" `
    --from-literal=container_name="$storageContainer"

Write-Host "`nStep 7: Deploying controller..." -ForegroundColor Yellow
$deploymentFile = Join-Path $k8sDir 'deployments\vm-job-controller.yaml'
(Get-Content $deploymentFile -Raw) `
    -replace 'image: [^\s]+\.azurecr\.io/vmjob-controller[^\s]*', "image: $imageTag" `
    | Set-Content $deploymentFile -NoNewline

kubectl apply -f $deploymentFile
kubectl wait --for=condition=available --timeout=300s deployment/vmjob-controller

Write-Host "`nStep 8: Registering VMs as VMNodes..." -ForegroundColor Yellow
$vmCount = [int](terraform -chdir="$tfDir" output -raw vm_count)
$resourceGroup = terraform -chdir="$tfDir" output -raw resource_group_name
$vmDetailsJson = terraform -chdir="$tfDir" output -json vm_details | ConvertFrom-Json

for ($i = 0; $i -lt $vmCount; $i++) {
    $vmName = "$resourceGroup-vm-$($i + 1)"
    $vmIP = $vmDetailsJson[$i].private_ip
    Write-Host "  [$($i + 1)/$vmCount] $vmName ($vmIP)..." -ForegroundColor Yellow

    $winrmScript = @'
try {
    Enable-PSRemoting -Force -SkipNetworkProfileCheck -ErrorAction Stop
    Set-Item WSMan:\localhost\Service\Auth\Basic -Value $true -ErrorAction Stop
    Set-Item WSMan:\localhost\Service\AllowUnencrypted -Value $true -ErrorAction Stop
    New-NetFirewallRule -Name 'WinRM-HTTP' -DisplayName 'WinRM HTTP' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 5985 -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path C:\k8s-worker | Out-Null
    New-Item -ItemType Directory -Force -Path C:\vmjob-results | Out-Null
    Write-Host 'SUCCESS'
} catch { Write-Host "ERROR: $($_.Exception.Message)"; exit 1 }
'@
    az vm run-command invoke --resource-group $resourceGroup --name $vmName `
        --command-id RunPowerShellScript --scripts $winrmScript 2>$null | Out-Null

    $node = @"
apiVersion: orchestrator.vmjobs.io/v1
kind: VMNode
metadata:
  name: $vmName
spec:
  privateIP: "$vmIP"
  os: windows
  osVersion: "Windows Server 2022"
  architecture: amd64
  resources: { cpu: "2", memory: "4Gi", storage: "128Gi" }
  winrmConfig: { port: 5985, useHTTPS: false }
  maxConcurrentJobs: 2
  reuseLimit: 10
  tags: { environment: azure, vmSize: Standard_B2s, role: worker }
"@
    $node | kubectl apply -f -

    $now = (Get-Date -AsUTC).ToString('o')
    $patch = "{`"status`":{`"phase`":`"Ready`",`"currentJobs`":0,`"jobsCompleted`":0,`"lastHeartbeat`":`"$now`",`"registrationTime`":`"$now`"}}"
    kubectl patch vmnode $vmName --type=merge --subresource=status -p $patch
}

Write-Host "`nStep 9: Verifying..." -ForegroundColor Yellow
kubectl get pods -l app=vmjob-controller
kubectl get vmnodes

Write-Host "`n=============================================="
Write-Host '  Setup Complete' -ForegroundColor Green
Write-Host '=============================================='
Write-Host "  Resource Group : $resourceGroup"
Write-Host "  ACR            : $acrName"
Write-Host "  VMs            : $vmCount (Standard_B2s)"
Write-Host "`nVM password stored in k8s secret vmjob-credentials (not printed)." -ForegroundColor Yellow
Write-Host 'Run ./cleanup-all.ps1 to destroy everything.' -ForegroundColor Yellow
