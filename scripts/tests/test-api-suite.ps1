#requires -Version 7.0
# VMJob API integration tests. Pick a suite or run all.

param(
    [ValidateSet('basic', 'stages', 'files', 'advanced', 'all')]
    [string]$Suite = 'all',

    [string]$ApiUrl = 'https://localhost:5001/api/vmjobs'
)

[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

function Submit-Job($job, $label) {
    try {
        $body = $job | ConvertTo-Json -Depth 10
        $resp = Invoke-RestMethod -Uri $ApiUrl -Method Post -Body $body -ContentType 'application/json'
        Write-Host "  [OK] $label -> $($resp.metadata.name ?? $resp.name)" -ForegroundColor Green
        return $resp
    }
    catch {
        Write-Host "  [FAIL] $label : $_" -ForegroundColor Red
        return $null
    }
}

function Test-Basic {
    Write-Host "`n== basic ==" -ForegroundColor Cyan

    Submit-Job @{
        name    = "echo-$(Get-Random)"
        command = "Write-Host 'Hello from VMJob API'"
    } 'single command'

    Submit-Job @{
        name     = "multi-$(Get-Random)"
        commands = @(
            "Write-Host 'Step 1'",
            'Get-Date | Out-String',
            "Write-Host 'Step 2'"
        )
    } 'multi command'

    Submit-Job @{
        name   = "script-$(Get-Random)"
        script = @{
            type    = 'powershell'
            content = "Get-Process | Select-Object -First 5 | ConvertTo-Json"
        }
    } 'inline script'
}

function Test-Stages {
    Write-Host "`n== stages ==" -ForegroundColor Cyan

    $job = @{
        name        = "stage-$(Get-Random)"
        variables   = @{ project = 'TestApp'; version = '1.0.0' }
        environment = @( @{ name = 'BUILD_TYPE'; value = 'Release' } )
        stages      = @(
            @{
                name  = 'prepare'
                steps = @(@{
                        task   = 'PowerShell'
                        inputs = @{ script = "Write-Host 'Prep `$(variables.project) v`$(variables.version)'" }
                    })
            },
            @{
                name      = 'build'
                dependsOn = @('prepare')
                steps     = @(@{
                        task   = 'PowerShell'
                        inputs = @{ script = "Write-Host 'Build `$(variables.project)'" }
                    })
            },
            @{
                name      = 'test'
                dependsOn = @('build')
                steps     = @(@{
                        task   = 'PowerShell'
                        inputs = @{ script = "Write-Host 'Test `$(variables.project)'" }
                    })
            }
        )
        finally     = @{
            steps = @(@{
                    task   = 'PowerShell'
                    inputs = @{ script = "Write-Host 'cleanup'" }
                })
        }
    }

    Submit-Job $job 'multi-stage with finally'
}

function Test-Files {
    Write-Host "`n== files ==" -ForegroundColor Cyan

    Submit-Job @{
        name     = "fileops-$(Get-Random)"
        commands = @(
            "New-Item -ItemType Directory -Path 'C:\vmjob-results\test' -Force | Out-Null",
            "Write-Output 'created' | Out-File -FilePath 'C:\vmjob-results\test\out.txt' -Force",
            "Get-Content 'C:\vmjob-results\test\out.txt'"
        )
    } 'create + read file'

    $job = @{
        name      = "stage-file-$(Get-Random)"
        variables = @{ dir = 'C:\\vmjob-results\\staged'; fileName = 'out.json' }
        stages    = @(
            @{ name = 'prepare'; steps = @(@{ task = 'PowerShell'; inputs = @{ script = "New-Item -ItemType Directory -Path '`$(variables.dir)' -Force | Out-Null" } }) },
            @{ name = 'write'; dependsOn = @('prepare'); steps = @(@{ task = 'PowerShell'; workingDirectory = "`$(variables.dir)"; inputs = @{ script = "@{ t = Get-Date } | ConvertTo-Json | Out-File '`$(variables.fileName)' -Force" } }) },
            @{ name = 'verify'; dependsOn = @('write'); steps = @(@{ task = 'PowerShell'; workingDirectory = "`$(variables.dir)"; inputs = @{ script = "if (-not (Test-Path '`$(variables.fileName)')) { throw 'missing' }" } }) }
        )
    }
    Submit-Job $job 'stage-based file workflow'
}

function Test-Advanced {
    Write-Host "`n== advanced ==" -ForegroundColor Cyan

    $largeScript = "Write-Host 'large'`n" + (1..50 | ForEach-Object { "# pad line $_ " + ('x' * 100) } | Join-String -Separator "`n")
    Submit-Job @{
        name    = "large-$(Get-Random)"
        command = $largeScript
    } 'large script'

    $errJob = @{
        name    = "err-$(Get-Random)"
        stages  = @(
            @{ name = 'setup'; steps = @(@{ task = 'PowerShell'; inputs = @{ script = "Write-Host 'ok'" } }) },
            @{ name = 'fail'; dependsOn = @('setup'); steps = @(@{ task = 'PowerShell'; inputs = @{ script = "throw 'simulated'" } }) },
            @{ name = 'skipped'; dependsOn = @('fail'); steps = @(@{ task = 'PowerShell'; inputs = @{ script = "Write-Host 'should not run'" } }) }
        )
        finally = @{ steps = @(@{ task = 'PowerShell'; inputs = @{ script = "Write-Host 'finally ran'" } }) }
    }
    Submit-Job $errJob 'error + finally'

    Submit-Job @{
        name      = "special-$(Get-Random)"
        variables = @{ message = "Test with 'quotes'"; path = 'C:\\Program Files\\Test' }
        command   = "Write-Host `"`$(variables.message)`"; Write-Host `"`$(variables.path)`""
    } 'special chars'
}

$suites = if ($Suite -eq 'all') { 'basic', 'stages', 'files', 'advanced' } else { @($Suite) }

Write-Host "VMJob API tests | Suite: $($suites -join ', ') | $ApiUrl" -ForegroundColor Cyan
Write-Host '================================================='

foreach ($s in $suites) {
    switch ($s) {
        'basic' { Test-Basic }
        'stages' { Test-Stages }
        'files' { Test-Files }
        'advanced' { Test-Advanced }
    }
}

Write-Host "`nDone." -ForegroundColor Green
