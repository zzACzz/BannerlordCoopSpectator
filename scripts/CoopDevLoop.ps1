param(
    [switch]$BuildClient,
    [switch]$BuildDedicated,
    [switch]$RestartDedicated,
    [switch]$LaunchClient,
    [switch]$RestartClient,
    [switch]$CheckLogs,
    [switch]$UseRelease,
    [int]$StartupWaitSeconds = 8,
    [int]$ClientStartupWaitSeconds = 5,
    [string]$ProjectRoot = "C:\dev\projects\BannerlordCoopSpectator3",
    [string]$BannerlordRoot = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord",
    [string]$DedicatedRoot = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server",
    [string]$DedicatedLogsRoot = "$env:LOCALAPPDATA\Temp\CoopSpectatorDedicated_logs\logs",
    [string]$ClientLogsRoot = "C:\ProgramData\Mount and Blade II Bannerlord\logs",
    [string]$DedicatedExe = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server\bin\Win64_Shipping_Server\DedicatedCustomServer.Starter.exe",
    [string]$DedicatedArgs = "",
    [string]$ClientExe = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\Bannerlord.exe",
    [string]$ClientArgs = "/multiplayer _MODULES_*Native*Multiplayer*Bannerlord.Harmony*CoopSpectator*_MODULES_",
    [string[]]$DedicatedMarkers = @(
        "requested vanilla agent visuals before direct spawn",
        "awaiting agent visuals",
        "spawn agent ownership finalized",
        "SpawnFromVisuals=True",
        "HadVisuals=True"
    ),
    [string[]]$ClientMarkers = @(
        "CreateAgentVisuals",
        "SetAgentOwningMissionPeer",
        "controlled agent changed"
    )
)

$ErrorActionPreference = "Stop"

if (-not ($BuildClient -or $BuildDedicated -or $RestartDedicated -or $LaunchClient -or $RestartClient -or $CheckLogs)) {
    $BuildClient = $true
    $BuildDedicated = $true
    $CheckLogs = $true
}

$configuration = if ($UseRelease) { "Release" } else { "Debug" }
$clientProject = Join-Path $ProjectRoot "CoopSpectator.csproj"
$dedicatedProject = Join-Path $ProjectRoot "DedicatedServer\CoopSpectatorDedicated.csproj"
$clientDll = Join-Path $BannerlordRoot "Modules\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll"
$dedicatedServerDll = Join-Path $DedicatedRoot "Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Server\CoopSpectator.dll"
$dedicatedClientDll = Join-Path $DedicatedRoot "Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client\CoopSpectator.dll"

function Invoke-Build {
    param(
        [string]$ProjectPath,
        [string[]]$ExtraArgs
    )

    $args = @("build", $ProjectPath, "-c", $configuration) + $ExtraArgs
    Write-Host ""
    Write-Host "==> dotnet $($args -join ' ')"
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $ProjectPath"
    }
}

function Show-DllStamp {
    param(
        [string]$Path,
        [string]$Label
    )

    if (-not (Test-Path $Path)) {
        Write-Warning "$Label missing: $Path"
        return
    }

    $item = Get-Item $Path
    Write-Host ("{0}: {1} | {2} bytes | {3}" -f $Label, $item.FullName, $item.Length, $item.LastWriteTime)
}

function Stop-DedicatedIfRunning {
    $processes = Get-Process -Name "DedicatedCustomServer.Starter" -ErrorAction SilentlyContinue
    if ($null -eq $processes) {
        Write-Host "No dedicated process is running."
        return
    }

    foreach ($process in $processes) {
        Write-Host "Stopping dedicated process PID=$($process.Id)"
        Stop-Process -Id $process.Id -Force
    }

    Start-Sleep -Seconds 2
}

function Stop-ClientIfRunning {
    $processes = Get-Process -Name "Bannerlord" -ErrorAction SilentlyContinue
    if ($null -eq $processes) {
        Write-Host "No Bannerlord client process is running."
        return
    }

    foreach ($process in $processes) {
        Write-Host "Stopping Bannerlord client PID=$($process.Id)"
        Stop-Process -Id $process.Id -Force
    }

    Start-Sleep -Seconds 2
}

function Start-DedicatedProcess {
    if (-not (Test-Path $DedicatedExe)) {
        throw "Dedicated executable not found: $DedicatedExe"
    }

    Write-Host "Starting dedicated: $DedicatedExe $DedicatedArgs"
    $process = Start-Process -FilePath $DedicatedExe -ArgumentList $DedicatedArgs -WorkingDirectory (Split-Path $DedicatedExe) -PassThru
    Write-Host "Dedicated PID=$($process.Id)"

    if ($StartupWaitSeconds -gt 0) {
        Write-Host "Waiting $StartupWaitSeconds second(s) for startup..."
        Start-Sleep -Seconds $StartupWaitSeconds
    }
}

function Start-ClientProcess {
    if (-not (Test-Path $ClientExe)) {
        throw "Client executable not found: $ClientExe"
    }

    Write-Host "Starting client: $ClientExe $ClientArgs"
    $process = Start-Process -FilePath $ClientExe -ArgumentList $ClientArgs -WorkingDirectory (Split-Path $ClientExe) -PassThru
    Write-Host "Client PID=$($process.Id)"

    if ($ClientStartupWaitSeconds -gt 0) {
        Write-Host "Waiting $ClientStartupWaitSeconds second(s) for client startup..."
        Start-Sleep -Seconds $ClientStartupWaitSeconds
    }
}

function Get-LatestLogFile {
    param([string]$Root)

    if (-not (Test-Path $Root)) {
        return $null
    }

    Get-ChildItem -Path $Root -Filter "rgl_log_*.txt" -File |
        Where-Object { $_.Name -notlike "rgl_log_errors_*" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

function Show-MarkerScan {
    param(
        [string]$Label,
        [string]$LogPath,
        [string[]]$Markers
    )

    if (-not $LogPath -or -not (Test-Path $LogPath)) {
        Write-Warning "$Label log not found."
        return
    }

    Write-Host ""
    Write-Host "==> $Label log: $LogPath"
    foreach ($marker in $Markers) {
        $matches = Select-String -Path $LogPath -Pattern $marker -SimpleMatch
        if ($matches) {
            $last = $matches | Select-Object -Last 1
            Write-Host ("[FOUND] {0}" -f $marker)
            Write-Host ("        {0}" -f $last.Line.Trim())
        }
        else {
            Write-Host ("[MISS ] {0}" -f $marker)
        }
    }
}

if ($BuildClient) {
    Invoke-Build -ProjectPath $clientProject -ExtraArgs @()
    Show-DllStamp -Path $clientDll -Label "Client DLL"
}

if ($BuildDedicated) {
    Invoke-Build -ProjectPath $dedicatedProject -ExtraArgs @("/p:UseDedicatedServerRefs=true")
    Show-DllStamp -Path $dedicatedServerDll -Label "Dedicated Server DLL"
    Show-DllStamp -Path $dedicatedClientDll -Label "Dedicated Client DLL"
}

if ($RestartDedicated) {
    Stop-DedicatedIfRunning
    Start-DedicatedProcess
}

if ($RestartClient) {
    Stop-ClientIfRunning
    Start-ClientProcess
}
elseif ($LaunchClient) {
    Start-ClientProcess
}

if ($CheckLogs) {
    $latestDedicatedLog = Get-LatestLogFile -Root $DedicatedLogsRoot
    $latestClientLog = Get-LatestLogFile -Root $ClientLogsRoot

    Show-MarkerScan -Label "Dedicated" -LogPath $latestDedicatedLog.FullName -Markers $DedicatedMarkers
    Show-MarkerScan -Label "Client" -LogPath $latestClientLog.FullName -Markers $ClientMarkers
}
