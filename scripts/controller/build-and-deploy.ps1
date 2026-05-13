#requires -Version 7.0
<#
.SYNOPSIS
Build + push controller image to ACR. Optionally clean old ACR tags and roll out to k8s.

.PARAMETER ACRName
Azure Container Registry short name (without .azurecr.io).

.PARAMETER ImageTag
Tag to apply. Default: latest.

.PARAMETER Cleanup
Before building, prune old image tags in ACR (keeps 3 newest).

.PARAMETER Deploy
After pushing, apply CRDs + rollout deployment.

.EXAMPLE
./build-and-deploy.ps1 -ACRName myacr
./build-and-deploy.ps1 -ACRName myacr -ImageTag v1.2 -Cleanup -Deploy
#>

param(
    [Parameter(Mandatory)]
    [string]$ACRName,

    [string]$ImageTag = 'latest',
    [switch]$Cleanup,
    [switch]$Deploy
)

$ErrorActionPreference = 'Stop'

$ctrlDir = Join-Path $PSScriptRoot '..\..\src\Controller'
$crdDir = Join-Path $PSScriptRoot '..\..\deploy\kubernetes\crds'
$deployFile = Join-Path $PSScriptRoot '..\..\deploy\kubernetes\deployments\vm-job-controller.yaml'

if ($Cleanup) {
    Write-Host "`n[Cleanup] Pruning old ACR images..." -ForegroundColor Yellow
    az acr login --name $ACRName
    $repos = az acr repository list --name $ACRName --output json | ConvertFrom-Json
    foreach ($repo in $repos) {
        $tags = az acr repository show-tags --name $ACRName --repository $repo --output json | ConvertFrom-Json
        $sorted = $tags | Sort-Object -Descending
        $keep = 3
        if ($sorted.Count -gt $keep) {
            Write-Host "  $repo : keeping $($sorted[0..($keep - 1)] -join ', ')" -ForegroundColor Green
            foreach ($t in $sorted[$keep..($sorted.Count - 1)]) {
                Write-Host "  $repo : deleting $t" -ForegroundColor Red
                az acr repository delete --name $ACRName --image "${repo}:${t}" --yes | Out-Null
            }
        }
    }
}

Write-Host "`n[Build] Docker image..." -ForegroundColor Yellow
Push-Location $ctrlDir
try {
    docker build -t "vmjob-controller:$ImageTag" .
    if ($LASTEXITCODE -ne 0) { throw 'Docker build failed' }

    $acrTag = "$ACRName.azurecr.io/vmjob-controller:$ImageTag"
    docker tag "vmjob-controller:$ImageTag" $acrTag
    Write-Host "  Tag: $acrTag" -ForegroundColor Green

    Write-Host "`n[Push] To ACR..." -ForegroundColor Yellow
    docker push $acrTag
    if ($LASTEXITCODE -ne 0) { throw 'Docker push failed' }
    Write-Host "  Pushed $acrTag" -ForegroundColor Green
}
finally {
    Pop-Location
}

if ($Deploy) {
    Write-Host "`n[Deploy] Applying CRDs..." -ForegroundColor Yellow
    kubectl apply -f $crdDir

    Write-Host "`n[Deploy] Updating deployment image..." -ForegroundColor Yellow
    (Get-Content $deployFile -Raw) `
        -replace 'image: [^\s]+\.azurecr\.io/vmjob-controller[^\s]*', "image: $acrTag" `
    | Set-Content $deployFile -NoNewline
    kubectl apply -f $deployFile

    Write-Host "`n[Deploy] Waiting for rollout..." -ForegroundColor Yellow
    kubectl rollout status deployment/vmjob-controller

    Write-Host "`nPods:" -ForegroundColor Cyan
    kubectl get pods -l app=vmjob-controller
}

Write-Host "`nDone. Image: $acrTag" -ForegroundColor Green
if (-not $Deploy) {
    Write-Host "Deploy with: kubectl set image deployment/vmjob-controller vmjob-controller=$acrTag" -ForegroundColor Gray
}
