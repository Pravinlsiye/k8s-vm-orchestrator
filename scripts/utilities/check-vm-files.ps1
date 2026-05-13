param(
    [Parameter(Mandatory = $true)]
    [string[]]$VMIPs,
    [Parameter(Mandatory = $true)]
    [string]$Username,
    [Parameter(Mandatory = $true)]
    [SecureString]$Password
)

Write-Host "`n========== Checking Files on Windows VMs ==========`n" -ForegroundColor Green

$credential = New-Object System.Management.Automation.PSCredential ($Username, $Password)

function Check-VMFiles {
    param(
        [string]$VMName,
        [string]$VMIP
    )

    Write-Host "`nChecking $VMName ($VMIP)..." -ForegroundColor Yellow

    try {
        $session = New-PSSession -ComputerName $VMIP -Credential $credential -ErrorAction Stop

        Invoke-Command -Session $session -ScriptBlock {
            $resultsPath = "C:\vmjob-results"

            if (Test-Path $resultsPath) {
                Write-Host "Directory exists: $resultsPath" -ForegroundColor Green

                $files = Get-ChildItem $resultsPath -File

                if ($files.Count -gt 0) {
                    Write-Host "`nFiles found:" -ForegroundColor Cyan
                    foreach ($file in $files) {
                        Write-Host "  - $($file.Name) (Size: $($file.Length) bytes, Modified: $($file.LastWriteTime))"
                        Write-Host "    Content preview:" -ForegroundColor Gray
                        Get-Content $file.FullName -TotalCount 5 | ForEach-Object {
                            Write-Host "      $_" -ForegroundColor Gray
                        }
                    }
                }
                else {
                    Write-Host "  No files found in $resultsPath" -ForegroundColor Yellow
                }
            }
            else {
                Write-Host "Directory does not exist: $resultsPath" -ForegroundColor Red
            }

            Write-Host "`nSystem Info:" -ForegroundColor Cyan
            Write-Host "  Computer Name: $env:COMPUTERNAME"
            Write-Host "  Current User: $env:USERNAME"
            Write-Host "  Windows Version: $([System.Environment]::OSVersion.VersionString)"
        }

        Remove-PSSession $session
    }
    catch {
        Write-Host "Failed to connect to $VMName : $_" -ForegroundColor Red
    }
}

$index = 1
foreach ($ip in $VMIPs) {
    Check-VMFiles -VMName "VM-$index" -VMIP $ip
    $index++
}

Write-Host "`n==================================================`n" -ForegroundColor Green
