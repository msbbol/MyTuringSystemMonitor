#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes the Turing System Monitor service, tray autostart and installed files.

.PARAMETER RemoveData
    Also delete settings and themes in %ProgramData%\MyTuringSystemMonitor (kept by default).
#>
[CmdletBinding()]
param(
    [string] $InstallDir = (Join-Path $env:ProgramFiles 'MyTuringSystemMonitor'),
    [switch] $RemoveData
)

$ErrorActionPreference = 'Stop'

$ServiceName = 'MyTuringSystemMonitor'
$RunKey      = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$RunValue    = 'MyTuringSystemMonitorTray'
$DataDir     = Join-Path $env:ProgramData 'MyTuringSystemMonitor'

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

Step 'Removing tray autostart'
Remove-ItemProperty -Path $RunKey -Name $RunValue -ErrorAction SilentlyContinue
Get-Process -Name 'MyTuringSystemMonitor.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    Step "Stopping and deleting service '$ServiceName'"
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    & sc.exe delete $ServiceName | Out-Null
}

if (Test-Path $InstallDir) {
    Step "Deleting $InstallDir"
    Start-Sleep -Milliseconds 500   # let the service process release its files
    Remove-Item -Path $InstallDir -Recurse -Force
}

if ($RemoveData -and (Test-Path $DataDir)) {
    Step "Deleting settings in $DataDir"
    Remove-Item -Path $DataDir -Recurse -Force
} elseif (Test-Path $DataDir) {
    Write-Host "Settings kept in $DataDir (use -RemoveData to delete them)."
}

Write-Host 'Uninstalled.' -ForegroundColor Green
