# k8s-vm-orchestrator

Kubernetes-native job orchestrator that runs workloads on **Windows VMs** instead of containers. Submit jobs via a REST API or `kubectl`; a controller dispatches them to a pool of VMs over WinRM.

## Why

Kubernetes natively schedules containers. Some workloads (Windows-only tooling, GUI-bound tests, legacy installers) must run on full VMs. This project treats VMs as a custom Kubernetes resource (`VMNode`) and jobs as `VMJob` resources, giving you a declarative, parallel, queue-backed execution model on top of Windows VMs.

## Components

| Component | Path | Role |
|-----------|------|------|
| API | `src/Api` | REST API: accept job definitions, generate PowerShell, create `VMJob` resources |
| Controller | `src/Controller` | Watches `VMJob`/`VMNode`, dispatches to free VMs via WinRM, tracks status |
| CRDs | `deploy/kubernetes/crds` | `VMJob`, `VMNode` custom resources |
| Infra | `deploy/terraform` | Azure: VMs, VNet, AKS, ACR |

## Architecture

```
client ──HTTP──▶ Api ──k8s API──▶ VMJob (CR)
                                    │
                                    ▼
                                Controller ──WinRM──▶ Windows VM pool
                                    │
                                    ▼
                              status / results
```

See [`ARCHITECTURE.md`](ARCHITECTURE.md) for details.

## Quick start (local)

Prereqs: .NET 8 SDK, Docker, `kubectl`, a Kubernetes cluster.

```bash
# 1. Install CRDs
kubectl apply -f deploy/kubernetes/crds/

# 2. RBAC
kubectl apply -f deploy/kubernetes/rbac/

# 3. Create VM credentials secret (never commit these)
kubectl create secret generic vmjob-credentials \
  --from-literal=username=$VM_ADMIN_USERNAME \
  --from-literal=password=$VM_ADMIN_PASSWORD

# 4. Build + run API locally (http://localhost:5000, swagger at /swagger)
dotnet run --project src/Api

# 5. Submit a sample job (either path)
kubectl apply -f deploy/examples/simple-test.yaml             # direct CRD
./scripts/tests/submit-job.ps1 -Command "Get-Date" -Wait      # via API
kubectl get vmjobs
```

## Cloud deploy (Azure)

```powershell
Copy-Item deploy/terraform/terraform.tfvars.example deploy/terraform/terraform.tfvars
# Fill in real values in terraform.tfvars

$env:VM_ADMIN_PASSWORD = '<your-strong-password>'
./scripts/setup/setup-all-cloud.ps1
```

See [`docs/deployment.md`](docs/deployment.md).

## Docs

- [`ARCHITECTURE.md`](ARCHITECTURE.md) — system design
- [`docs/api.md`](docs/api.md) — API reference and job formats
- [`docs/deployment.md`](docs/deployment.md) — deploy to Azure / any k8s
- [`docs/testing.md`](docs/testing.md) — unit + integration tests

## Repo layout

```
k8s-vm-orchestrator/
├── src/                       # .NET source
│   ├── Api/                   # REST API
│   ├── Api.Tests/
│   ├── Controller/            # K8s controller
│   └── Controller.Tests/
├── deploy/
│   ├── kubernetes/
│   │   ├── crds/              # VMJob, VMNode CRDs
│   │   ├── rbac/              # ServiceAccount, Role, RoleBinding
│   │   └── deployments/       # API + Controller deployments
│   ├── examples/              # Sample VMJob YAMLs (apply via kubectl)
│   └── terraform/             # Azure VMs, AKS, ACR
├── scripts/
│   ├── setup/                 # setup-all-cloud, cleanup-all, check-status, manage-vms
│   ├── controller/            # build-and-deploy (image build + ACR push + rollout)
│   ├── test-runners/          # run-testcases (batch runner)
│   ├── tests/                 # submit-job, test-api-suite, verify-vm-file, test-vm-selector
│   └── utilities/             # check-test-status, cleanup-test-jobs, track-job-duration, check-vm-files
├── examples/api/              # API request examples (JSON + PS1)
└── docs/                      # api.md, deployment.md, testing.md
```

## Security

- No secrets in source. `terraform.tfvars`, `*.rdp`, kubeconfigs are gitignored.
- VM credentials read from `VM_ADMIN_PASSWORD` env var or `vmjob-credentials` k8s secret.
- Restrict `control_plane_ip` in `terraform.tfvars` to your IP only.
