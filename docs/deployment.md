# Deployment

## Prereqs

- .NET 8 SDK
- Docker
- `kubectl` connected to a cluster
- `terraform` and `az` CLI (only for Azure path)
- A pool of Windows VMs reachable from the cluster, with WinRM enabled

## 1. Build images

```bash
# API
docker build -t <YOUR_ACR>.azurecr.io/vmjob-api:latest src/Api
docker push   <YOUR_ACR>.azurecr.io/vmjob-api:latest

# Controller
docker build -t <YOUR_ACR>.azurecr.io/vmjob-controller:latest src/Controller
docker push   <YOUR_ACR>.azurecr.io/vmjob-controller:latest
```

Or use the helper script (Azure ACR):

```powershell
# Build + push
./scripts/controller/build-and-deploy.ps1 -ACRName <YOUR_ACR_NAME> -ImageTag latest

# Build + push + prune old tags + roll out to k8s
./scripts/controller/build-and-deploy.ps1 -ACRName <YOUR_ACR_NAME> -ImageTag v1.2 -Cleanup -Deploy
```

## 2. Install CRDs and RBAC

```bash
kubectl apply -f deploy/kubernetes/crds/
kubectl apply -f deploy/kubernetes/rbac/
```

## 3. Create the VM credentials secret

Never commit credentials. Create the secret directly:

```bash
kubectl create secret generic vmjob-credentials \
  --from-literal=username=$VM_ADMIN_USERNAME \
  --from-literal=password=$VM_ADMIN_PASSWORD
```

## 4. Deploy controller + API

Edit `deploy/kubernetes/deployments/vm-job-controller.yaml` and `vmjob-api.yaml`, replacing `<YOUR_ACR_NAME>` with your registry.

```bash
kubectl apply -f deploy/kubernetes/deployments/vm-job-controller.yaml
kubectl apply -f deploy/kubernetes/deployments/vmjob-api.yaml
```

## 5. Register VMs as VMNodes

For each Windows VM (WinRM enabled, port 5985):

```yaml
apiVersion: orchestrator.vmjobs.io/v1
kind: VMNode
metadata:
  name: vm-windows-01
spec:
  privateIP: "10.1.1.10"
  os: windows
  osVersion: "Windows Server 2022"
  architecture: amd64
  winrmConfig: { port: 5985, useHTTPS: false }
  maxConcurrentJobs: 1
  tags: { role: worker }
```

```bash
kubectl apply -f vmnode.yaml
kubectl get vmnodes
```

## Azure path (Terraform)

```powershell
Copy-Item deploy/terraform/terraform.tfvars.example deploy/terraform/terraform.tfvars
# Edit terraform.tfvars: subscription_id, admin_password, control_plane_ip

$env:VM_ADMIN_PASSWORD = '<your-strong-password>'
./scripts/setup/setup-all-cloud.ps1
```

The script:
1. Provisions VMs, VNet, NSG, AKS, ACR via Terraform
2. Configures WinRM on each VM
3. Builds + pushes controller image
4. Creates k8s secrets
5. Deploys CRDs, RBAC, controller
6. Registers VMs as `VMNode` resources

## Teardown

```powershell
./scripts/setup/cleanup-all.ps1
```

## Operating

```powershell
# Cluster + controller + VMs + jobs snapshot
./scripts/setup/check-status.ps1

# Stop / start VMs (Azure)
./scripts/setup/manage-vms.ps1 -Action stop
./scripts/setup/manage-vms.ps1 -Action start
./scripts/setup/manage-vms.ps1 -Action status
```
