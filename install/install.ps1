#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Builds and installs Turing System Monitor as a Windows service with a tray icon.

.DESCRIPTION
    - Publishes the service and the tray app into -InstallDir.
    - Registers the "MyTuringSystemMonitor" service (LocalSystem, automatic start, restart on failure).
      Running as LocalSystem gives LibreHardwareMonitor the rights it needs for temperatures/fans.
    - Moves settings from the old per-user location to %ProgramData%\MyTuringSystemMonitor and lets
      local users edit them.
    - Starts the tray icon at every logon (all users) and launches it now.
    Re-running the script upgrades an existing installation in place; settings are kept.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File install\install.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File install\install.ps1 -SelfContained
#>
[CmdletBinding()]
param(
    [string] $InstallDir = (Join-Path $env:ProgramFiles 'MyTuringSystemMonitor'),

    # Bundle the .NET runtime so the target PC doesn't need .NET 8 installed.
    [switch] $SelfContained,

    [switch] $NoTray,

    [switch] $NoBrowser
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ServiceName = 'MyTuringSystemMonitor'
$DisplayName = 'Turing System Monitor'
$RunKey      = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$RunValue    = 'MyTuringSystemMonitorTray'
$Repo        = Split-Path -Parent $PSScriptRoot
$DataDir     = Join-Path $env:ProgramData 'MyTuringSystemMonitor'
$ServiceDir  = Join-Path $InstallDir 'service'
$TrayDir     = Join-Path $InstallDir 'tray'
$ServiceExe  = Join-Path $ServiceDir 'MyTuringSystemMonitor.Service.exe'
$TrayExe     = Join-Path $TrayDir 'MyTuringSystemMonitor.Tray.exe'

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK (dotnet) is required to build the app. Install .NET 8 SDK or newer.'
}

# --- stop anything that holds the files or the COM port ------------------------------------------
Step 'Stopping running instances'
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Stop-Service -Name $ServiceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}
# The tray and any console runner/configurator started by hand would lock files or the COM port.
Get-Process -Name 'MyTuringSystemMonitor.Tray', 'MyTuringSystemMonitor', 'MyTuringSystemMonitor.Config' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# --- build ---------------------------------------------------------------------------------------
$publishArgs = @('-c', 'Release', '--nologo', '-v', 'minimal')
if ($SelfContained) { $publishArgs += @('-r', 'win-x64', '--self-contained', 'true') }

Step "Publishing service to $ServiceDir"
& dotnet publish (Join-Path $Repo 'src\MyTuringSystemMonitor.Service\MyTuringSystemMonitor.Service.csproj') @publishArgs -o $ServiceDir
if ($LASTEXITCODE -ne 0) { throw 'Publishing the service failed.' }

if (-not $NoTray) {
    Step "Publishing tray app to $TrayDir"
    & dotnet publish (Join-Path $Repo 'src\MyTuringSystemMonitor.Tray\MyTuringSystemMonitor.Tray.csproj') @publishArgs -o $TrayDir
    if ($LASTEXITCODE -ne 0) { throw 'Publishing the tray app failed.' }
}

# --- settings ------------------------------------------------------------------------------------
Step "Preparing settings in $DataDir"
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null

# Earlier versions kept settings per user; carry them over once so nothing is lost.
$legacy = Join-Path $env:LOCALAPPDATA 'MyTuringSystemMonitor'
if (-not (Test-Path (Join-Path $DataDir 'config.json')) -and (Test-Path (Join-Path $legacy 'config.json'))) {
    Write-Host "    Migrating settings from $legacy"
    Copy-Item (Join-Path $legacy 'config.json') $DataDir
    if (Test-Path (Join-Path $legacy 'themes')) {
        Copy-Item (Join-Path $legacy 'themes') $DataDir -Recurse -Force
    }
}

# The service runs as SYSTEM; let local users (tray, configurator, the web UI's user) edit settings too.
& icacls $DataDir /grant '*S-1-5-32-545:(OI)(CI)M' /T /Q | Out-Null   # BUILTIN\Users: Modify

# --- service -------------------------------------------------------------------------------------
Step "Registering service '$ServiceName'"
$binaryPath = "`"$ServiceExe`""
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    & sc.exe config $ServiceName binPath= $binaryPath start= auto DisplayName= $DisplayName | Out-Null
} else {
    New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName $DisplayName `
        -Description 'Drives the Turing smart screen and hosts its control panel on http://localhost.' `
        -StartupType Automatic | Out-Null
}

# Restart automatically if it ever crashes: after 5 s, 5 s, then 30 s; reset the counter daily.
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null

Step 'Starting service'
Start-Service -Name $ServiceName
(Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))

# --- tray ----------------------------------------------------------------------------------------
if (-not $NoTray) {
    Step 'Enabling the tray icon at logon'
    Set-ItemProperty -Path $RunKey -Name $RunValue -Value "`"$TrayExe`""

    # Started via explorer so it runs as the signed-in user, not elevated like this script.
    Start-Process -FilePath 'explorer.exe' -ArgumentList "`"$TrayExe`""
}

# --- done ----------------------------------------------------------------------------------------
$port = 5321
try {
    $config = Get-Content (Join-Path $DataDir 'config.json') -Raw | ConvertFrom-Json
    $webPort = $config.PSObject.Properties | Where-Object { $_.Name -ieq 'webPort' } | Select-Object -First 1
    if ($webPort) { $port = [int]$webPort.Value }
} catch { }

$url = "http://localhost:$port/"
$deadline = (Get-Date).AddSeconds(20)
do {
    try { Invoke-WebRequest -Uri "${url}api/status" -UseBasicParsing -TimeoutSec 2 | Out-Null; $ready = $true }
    catch { $ready = $false; Start-Sleep -Milliseconds 500 }
} until ($ready -or (Get-Date) -gt $deadline)

Write-Host ''
if ($ready) {
    Write-Host "Installed. Control panel: $url" -ForegroundColor Green
    if (-not $NoBrowser) { Start-Process $url }
} else {
    Write-Warning "The service is installed but the control panel didn't answer at $url yet. See Event Viewer > Windows Logs > Application (source: $ServiceName)."
}
