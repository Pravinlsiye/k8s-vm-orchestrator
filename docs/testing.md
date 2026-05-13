# Testing

## Unit tests

```bash
# Whole solution
dotnet test k8s-vm-orchestrator.sln

# Single project
dotnet test src/Api.Tests/VMJobAPI.Tests.csproj
dotnet test src/Controller.Tests/VMJobOrchestrator.Tests.csproj

# Single test
dotnet test --filter "FullyQualifiedName~DurationTrackingTests"

# With coverage
dotnet test --collect:"XPlat Code Coverage"
```

| Project | Covers |
|---------|--------|
| `Api.Tests` | Script generation, stage compilation, finally, env vars, file ops, working dir |
| `Controller.Tests` | Duration tracking, VM executor mocks, controller loop integration |

Mocks via Moq: `IKubernetes`, `IVMExecutor`, `ILogger`.

## API integration tests

Require API running at `https://localhost:5001` and `VMNode`s registered.

```powershell
# All suites
./scripts/tests/test-api-suite.ps1

# Pick one
./scripts/tests/test-api-suite.ps1 -Suite basic
./scripts/tests/test-api-suite.ps1 -Suite stages
./scripts/tests/test-api-suite.ps1 -Suite files
./scripts/tests/test-api-suite.ps1 -Suite advanced

# Custom API URL
./scripts/tests/test-api-suite.ps1 -ApiUrl https://api.example.com/api/vmjobs
```

## VM-targeted tests

```powershell
# Verify file roundtrip via kubectl
./scripts/tests/verify-vm-file.ps1

# vmSelector targeting
./scripts/tests/test-vm-selector.ps1
```

## Submit a single job

```powershell
# Inline command
./scripts/tests/submit-job.ps1 -Command "Get-Date" -Wait

# PowerShell script file
./scripts/tests/submit-job.ps1 -ScriptFile ./my-script.ps1 -Wait

# Full job definition (stages, variables, etc.)
./scripts/tests/submit-job.ps1 -JobFile ./examples/api/stage-job.json -Wait

# Target a specific kind of VM
./scripts/tests/submit-job.ps1 -Command "Get-Date" `
    -VMSelector '{"os":"windows","tags":{"role":"worker"}}'

# Dry run (print payload, don't submit)
./scripts/tests/submit-job.ps1 -Command "Get-Date" -DryRun
```

## Batch job runner

Submit many jobs from a JSON file and watch progress:

```powershell
# Sample file: examples/api/jobs-batch.json (array of job objects)
./scripts/test-runners/run-testcases.ps1 -JobsFile examples/api/jobs-batch.json

# Live status table
./scripts/test-runners/run-testcases.ps1 -JobsFile jobs.json -Mode Live

# Tune monitoring
./scripts/test-runners/run-testcases.ps1 -JobsFile jobs.json -Mode Live `
    -MaxWaitMinutes 15 -RefreshIntervalSeconds 1
```

The JSON file is either an array of job objects (same schema as the API), or an array of strings (each treated as an inline command).

## Monitoring + cleanup utilities

```powershell
# Snapshot of all VMJobs
./scripts/utilities/check-test-status.ps1
./scripts/utilities/check-test-status.ps1 -Watch
./scripts/utilities/check-test-status.ps1 -NamePrefix build- -Watch

# Continuous duration tracking + log file
./scripts/utilities/track-job-duration.ps1

# WinRM into VMs to inspect result files
./scripts/utilities/check-vm-files.ps1 -VMIPs 10.1.1.10,10.1.1.11 `
    -Username vmjobadmin -Password (Read-Host -AsSecureString)

# Clean up completed VMJobs
./scripts/utilities/cleanup-test-jobs.ps1                  # completed + failed
./scripts/utilities/cleanup-test-jobs.ps1 -All -Force      # everything
./scripts/utilities/cleanup-test-jobs.ps1 -FailedOnly
./scripts/utilities/cleanup-test-jobs.ps1 -NamePrefix build-
```

## CI

```yaml
- task: DotNetCoreCLI@2
  inputs:
    command: test
    projects: '**/*.Tests.csproj'
    arguments: '--configuration Release --logger trx --collect:"XPlat Code Coverage"'
```
