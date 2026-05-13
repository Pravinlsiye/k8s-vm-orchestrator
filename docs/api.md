# API Reference

The API is the simpler of the two entry points (the other is `kubectl apply` against a `VMJob` YAML). It accepts a request, generates the PowerShell for the VM, and creates the `VMJob` custom resource for the controller to dispatch.

## Base URL

| Context | URL |
|---------|-----|
| Local dev (http) | `http://localhost:5000` |
| Local dev (https) | `https://localhost:5001` |
| In cluster | `http://vmjob-api:80` (service) |
| Swagger UI | `/swagger` |

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/vmjobs` | Submit a job |
| `GET`  | `/api/vmjobs/{namespace}/{name}` | Get job status |
| `GET`  | `/health` | Health check |

## Request formats

Four progressively richer formats. Pick the simplest that fits.

### 1. Single command

```json
{ "command": "Get-Date" }
```

### 2. Multiple commands

```json
{
  "name": "setup",
  "commands": [
    "New-Item -Path C:\\app -ItemType Directory -Force",
    "Write-Output 'ready' | Out-File C:\\app\\status.txt"
  ]
}
```

### 3. Inline script

```json
{
  "name": "system-info",
  "script": {
    "type": "powershell",
    "content": "Get-ComputerInfo | Select-Object CsName, OsName | ConvertTo-Json"
  }
}
```

`script.type` accepts `powershell`, `batch`, `python`.

### 4. Stage-based workflow

```json
{
  "name": "deploy-app",
  "variables": { "appName": "MyApp", "version": "1.0.0" },
  "environment": [ { "name": "DEPLOY_ENV", "value": "production" } ],
  "stages": [
    {
      "name": "prepare",
      "steps": [
        { "task": "PowerShell",
          "inputs": { "script": "New-Item -Path 'C:\\Apps\\$(variables.appName)' -ItemType Directory -Force" } }
      ]
    },
    {
      "name": "deploy",
      "dependsOn": ["prepare"],
      "steps": [
        { "task": "PowerShell",
          "workingDirectory": "C:\\Apps\\$(variables.appName)",
          "inputs": { "script": "Write-Host 'Deploying $(variables.appName) v$(variables.version)'" } }
      ]
    }
  ],
  "finally": {
    "steps": [
      { "task": "PowerShell", "inputs": { "script": "Write-Host 'cleanup'" } }
    ]
  }
}
```

## Stage features

| Feature | Syntax |
|---------|--------|
| Variables | `"variables": { "k": "v" }` → use as `$(variables.k)` |
| Job env | `"environment": [{ "name": "K", "value": "v" }]` (all stages) |
| Stage env | Same shape inside a stage (stage scope) |
| Task env | Same shape inside a step (task scope) |
| Dependencies | `"dependsOn": ["stage1", "stage2"]` |
| Parallel | Stages without `dependsOn` run concurrently |
| Working dir | `"workingDirectory": "C:\\path"` |
| Fresh shell | `"newTerminal": true` |
| Finally block | Top-level `"finally": { "steps": [ ... ] }` always runs |
| Continue on error | `"continueOnError": true` on a step |
| Per-step retry | `"retryCountOnFailure": 2` on a step |

## VM selection

Two surfaces, depending on which path you use:

### REST API (via `/api/vmjobs`)

Use the `advanced` block:

```json
{
  "command": "Get-Date",
  "advanced": {
    "targetVM": "vm-windows-01",
    "tags": { "role": "worker" }
  }
}
```

### Direct CRD (`kubectl apply`)

Use `spec.vmSelector`:

```yaml
apiVersion: orchestrator.vmjobs.io/v1
kind: VMJob
spec:
  vmSelector:
    nodeName: vm-windows-01
    os: windows
    tags: { role: worker }
  command: powershell.exe
  args: ["-Command", "Get-Date"]
```

When multiple VMs match, the controller picks the one with the lowest current load.

## Other options

```json
{
  "command": "...",
  "advanced": {
    "workingDir": "C:\\work",
    "timeout": "30m",
    "priority": 50,
    "env": { "MY_VAR": "value" }
  }
}
```

Timeout format: `30s`, `5m`, `2h`. Default: `2h`.

## Responses

### POST `/api/vmjobs` → 201 Created

```json
{
  "name": "deploy-app",
  "namespace": "default",
  "uid": "ab12cd34-...",
  "resourceVersion": "12345",
  "creationTimestamp": "2026-05-13T10:00:00Z",
  "spec": { "...": "echoed back" },
  "status": { "phase": "Pending" }
}
```

### GET `/api/vmjobs/{namespace}/{name}` → 200 OK

```json
{
  "name": "deploy-app",
  "namespace": "default",
  "phase": "Running",
  "assignedVM": "vm-windows-01",
  "assignedVMIP": "10.1.1.10",
  "assignedVMOS": "windows",
  "exitCode": null,
  "logs": null,
  "startTime": "2026-05-13T10:00:05Z",
  "completionTime": null,
  "stages": [],
  "currentStage": "deploy",
  "currentTask": null,
  "totalStages": 2,
  "completedStages": 1
}
```

Phase values: `Pending`, `Scheduled`, `Running`, `Completed`, `Failed`, `Cancelled`.

When the job finishes, `kubectl get vmjob <name>` also shows `duration` (formatted, e.g. `2m15s`) — that's a status field on the CR itself, populated by the controller.

## Submitting from PowerShell

See [`examples/api/working-example.ps1`](../examples/api/working-example.ps1) or use the helper:

```powershell
./scripts/tests/submit-job.ps1 -Command "Get-Date" -Wait
./scripts/tests/submit-job.ps1 -JobFile examples/api/stage-job.json -Wait
```
