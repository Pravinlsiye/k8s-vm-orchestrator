#requires -Version 7.0
# Quick status of the orchestrator deployment.

Write-Host '=============================================='
Write-Host '   k8s-vm-orchestrator - Status Check'
Write-Host '=============================================='

try {
    kubectl cluster-info 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'no cluster' }
}
catch {
    Write-Host "`nNo Kubernetes cluster configured." -ForegroundColor Red
    Write-Host "Run ./setup-all-cloud.ps1 first." -ForegroundColor Yellow
    exit 1
}

Write-Host "`nCluster:" -ForegroundColor Green
kubectl cluster-info | Select-Object -First 1

Write-Host "`nController:" -ForegroundColor Green
$ctrl = kubectl get deployment vmjob-controller 2>$null
if ($LASTEXITCODE -eq 0) { $ctrl } else { Write-Host 'Controller not deployed' -ForegroundColor Yellow }

Write-Host "`nVM Nodes:" -ForegroundColor Green
$nodes = kubectl get vmnodes 2>$null
if ($LASTEXITCODE -eq 0) { $nodes } else { Write-Host 'No VM nodes registered' -ForegroundColor Yellow }

Write-Host "`nCurrent Jobs:" -ForegroundColor Green
$jobs = kubectl get vmjobs 2>$null
if ($LASTEXITCODE -eq 0) { $jobs } else { Write-Host 'No jobs found' -ForegroundColor Yellow }

Write-Host "`nRecent Controller Logs:" -ForegroundColor Green
$logs = kubectl logs -l app=vmjob-controller --tail=5 2>$null
if ($LASTEXITCODE -eq 0) { $logs } else { Write-Host 'No controller logs available' -ForegroundColor Yellow }

Write-Host "`n=============================================="
