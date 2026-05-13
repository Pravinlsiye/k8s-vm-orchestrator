# Architecture

## System

```
┌────────────────────────────────────────────────────────────┐
│                       Client                                │
└──────────────┬─────────────────────────────────────────────┘
               │ HTTP / REST
               ▼
┌────────────────────────────────────────────────────────────┐
│                    VMJob API (src/Api)                      │
│  • Accepts job definitions (commands, scripts, stages)      │
│  • Generates PowerShell                                     │
│  • Creates VMJob CR in Kubernetes                           │
└──────────────┬─────────────────────────────────────────────┘
               │ Kubernetes API
               ▼
┌────────────────────────────────────────────────────────────┐
│                  Kubernetes cluster                         │
│  ┌─────────────────────────────────────────┐               │
│  │   VMJob Controller (src/Controller)     │               │
│  │  • Reconcile loop (5s)                  │               │
│  │  • Monitor loop  (3s)                   │               │
│  │  • VM state loop (30s)                  │               │
│  │  • WinRM connection pool                │               │
│  └─────────────────┬───────────────────────┘               │
└────────────────────┼───────────────────────────────────────┘
                     │ WinRM
                     ▼
┌────────────────────────────────────────────────────────────┐
│                  Windows VM Pool                            │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐                     │
│  │ VMNode1 │  │ VMNode2 │  │ VMNode3 │  ...                │
│  └─────────┘  └─────────┘  └─────────┘                     │
└────────────────────────────────────────────────────────────┘
```

## Custom resources

| CRD | Purpose |
|-----|---------|
| `VMJob` | Job definition + status (phase, assignedVM, duration) |
| `VMNode` | Registered Windows VM (IP, tags, capacity, status) |

API group: `orchestrator.vmjobs.io/v1`.

## Controller loops

Three concurrent loops over a shared in-memory state store:

1. **Reconcile (5s)** — find pending `VMJob`s, match against free `VMNode`s, dispatch.
2. **Monitor (3s)** — poll running jobs, update status, clean up completed.
3. **VM state (30s)** — refresh `VMNode` heartbeat / availability, recover stuck VMs.

Dispatch is non-blocking: VM is marked busy and the job runs on a background task.

## Job dispatch

1. Job submitted → API generates PS script → creates `VMJob`.
2. Controller picks pending `VMJob`, selects free `VMNode` (matching `vmSelector`).
3. WinRM pool checks out a connection to the VM, executes script.
4. Result (`exitCode`, `output`, `duration`, `startTime`, `completionTime`) written to `VMJob.status`.

Large scripts (>7000 chars) are written to a temp file on the VM and executed by path.

## Selectors

`VMJob.spec.vmSelector` supports:

- `nodeName` — pin to a specific VM
- `os` — `windows` / `linux`
- `tags` — key/value match

When multiple VMs match, the controller picks the one with the lowest current load.

## Status fields

| Field | Description |
|-------|-------------|
| `phase` | `Pending` / `Scheduled` / `Running` / `Completed` / `Failed` / `Cancelled` |
| `assignedVM` | VMNode name |
| `startTime` | When execution began on VM |
| `completionTime` | When execution ended |
| `duration` | Formatted (e.g. `2m15s`, `1h30m`) |
| `durationSeconds` | Duration in seconds (integer) |
| `exitCode` | Script exit code |
| `logs` | stdout/stderr tail |
| `currentStage` / `currentTask` | Currently executing stage / step |
| `totalStages` / `completedStages` | Progress counters |
| `retries` | Number of retries attempted |
| `message` | Human-readable status message |

The API's `GET /api/vmjobs/{ns}/{name}` flattens these and also adds `assignedVMIP` and `assignedVMOS` by looking up the assigned `VMNode`.

## Key design choices

| Decision | Rationale |
|----------|-----------|
| One job per VM at a time | Avoid contention; simplifies state |
| Reusable VM pool | VMs not recreated between jobs (cost, speed) |
| PowerShell over WinRM | Native Windows execution path |
| K8s CRDs for state | Use the API server as source of truth |
| Connection pooling | Reduce per-job WinRM handshake (~10s → <1s) |
| API generates scripts | Controller stays simple (just executes) |

## Tech stack

- **.NET 8** (API, Controller)
- **Kubernetes** (orchestration via CRDs)
- **WinRM + PowerShell** (VM execution)
- **Azure / AKS** (reference deployment)
- **Terraform** (infra)
- **xUnit + Moq + FluentAssertions** (tests)
