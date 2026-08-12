[CmdletBinding()]
param(
    [ValidateSet("BeforeMultiplayer", "AfterMultiplayer", "ContractTest", "RunMultiplayer", "WaitForMultiplayerExit")]
    [string]$Phase = "BeforeMultiplayer",

    [string]$ProgramDataRoot = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::CommonApplicationData),

    [string]$GameExecutable = "",

    [string]$GameArguments = "",

    [string]$GameWorkingDirectory = "",

    [ValidateRange(0, 120)]
    [int]$ChildProcessStartTimeoutSeconds = 15,

    [ValidateRange(0, [int]::MaxValue)]
    [int]$GameProcessId = 0,

    [ValidateRange(0, [long]::MaxValue)]
    [long]$GameProcessStartTimeUtcTicks = 0,

    [ValidateRange(1, 120)]
    [int]$CleanupRetrySeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-NormalizedFullPath
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return [IO.Path]::GetFullPath($Path).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
}

function Assert-PathIsNotReparsePoint
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path))
    {
        return
    }

    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
    {
        throw "Refusing to traverse a reparse point while clearing the Bannerlord shader cache: $Path"
    }
}

function Clear-CoopShaderCache
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$CleanupPhase
    )

    if ([string]::IsNullOrWhiteSpace($ProgramDataRoot))
    {
        throw "The system ProgramData path is empty."
    }

    $normalizedProgramData = Get-NormalizedFullPath $ProgramDataRoot
    $bannerlordData = Join-Path $normalizedProgramData "Mount and Blade II Bannerlord"
    $shadersRoot = Join-Path $bannerlordData "Shaders"
    $coreShadersRoot = Join-Path $shadersRoot "CoreShaders"
    $target = Get-NormalizedFullPath (Join-Path $coreShadersRoot "D3D11")
    $expectedTarget = Get-NormalizedFullPath (
        Join-Path $normalizedProgramData (
            "Mount and Blade II Bannerlord\Shaders\CoreShaders\D3D11"))

    if (-not [string]::Equals(
            $target,
            $expectedTarget,
            [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Shader cache target validation failed. Target=$target Expected=$expectedTarget"
    }

    if (-not [string]::Equals(
            [IO.Path]::GetFileName($target),
            "D3D11",
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals(
            [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($target)),
            "CoreShaders",
            [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Shader cache target does not have the required CoreShaders\D3D11 suffix: $target"
    }

    foreach ($pathToValidate in @(
            $normalizedProgramData,
            $bannerlordData,
            $shadersRoot,
            $coreShadersRoot,
            $target))
    {
        Assert-PathIsNotReparsePoint $pathToValidate
    }

    if (Test-Path -LiteralPath $target)
    {
        Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $target)
        {
            throw "The exact Bannerlord shader cache target could not be removed: $target"
        }

        Write-Output "[CoopSpectator] Cleared Bannerlord runtime shader cache for phase '$CleanupPhase': $target"
    }
    else
    {
        Write-Output "[CoopSpectator] Bannerlord runtime shader cache is already clear for phase '$CleanupPhase': $target"
    }
}

function Wait-ForGameProcessAndClearCache
{
    if ($GameProcessId -le 0)
    {
        throw "GameProcessId is required for the WaitForMultiplayerExit phase."
    }

    $gameProcess = Get-Process -Id $GameProcessId -ErrorAction SilentlyContinue
    if ($null -ne $gameProcess -and $GameProcessStartTimeUtcTicks -gt 0)
    {
        try
        {
            $actualStartTimeUtcTicks = $gameProcess.StartTime.ToUniversalTime().Ticks
            if ($actualStartTimeUtcTicks -ne $GameProcessStartTimeUtcTicks)
            {
                $gameProcess = $null
            }
        }
        catch
        {
            $gameProcess = $null
        }
    }

    if ($null -ne $gameProcess)
    {
        $gameProcess.WaitForExit()
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($CleanupRetrySeconds)
    $lastFailure = $null
    do
    {
        try
        {
            Clear-CoopShaderCache "AfterMultiplayerWatcher"
            return
        }
        catch
        {
            $lastFailure = $_.Exception
        }

        if ([DateTime]::UtcNow -ge $deadline)
        {
            break
        }

        Start-Sleep -Milliseconds 250
    }
    while ($true)

    throw "The background cleanup watcher exhausted its retries: $($lastFailure.Message)"
}

function Start-DetachedCleanupWatcher
{
    param(
        [Parameter(Mandatory = $true)]
        [Diagnostics.Process]$LifecycleProcess
    )

    if ([string]::IsNullOrWhiteSpace($PSCommandPath))
    {
        throw "The shader-cache helper script path is unavailable."
    }

    $scriptPathLiteral = $PSCommandPath.Replace("'", "''")
    $programDataLiteral = $ProgramDataRoot.Replace("'", "''")
    $processStartTimeUtcTicks = $LifecycleProcess.StartTime.ToUniversalTime().Ticks
    $watcherCommandTemplate = (
        "& '{0}' -Phase WaitForMultiplayerExit -ProgramDataRoot '{1}' " +
        "-GameProcessId {2} -GameProcessStartTimeUtcTicks {3} -CleanupRetrySeconds {4}")
    $watcherCommand = $watcherCommandTemplate -f $scriptPathLiteral, $programDataLiteral, $LifecycleProcess.Id, $processStartTimeUtcTicks, $CleanupRetrySeconds
    $encodedCommand = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($watcherCommand))
    $powershellExecutable = Join-Path $PSHOME "powershell.exe"

    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $powershellExecutable
    $startInfo.Arguments = (
        "-NoProfile -NonInteractive -WindowStyle Hidden " +
        "-ExecutionPolicy Bypass -EncodedCommand $encodedCommand")
    $startInfo.UseShellExecute = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden

    $watcher = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $watcher)
    {
        throw "Could not start the detached shader-cache cleanup watcher."
    }

    Write-Output "[CoopSpectator] Started detached shader-cache cleanup watcher PID=$($watcher.Id) for game PID=$($LifecycleProcess.Id)."
}

function Get-ExistingGameProcessIds
{
    $result = @{}
    foreach ($processName in @(
            "TaleWorlds.MountAndBlade.Launcher",
            "Bannerlord.Native"))
    {
        foreach ($process in @(Get-Process -Name $processName -ErrorAction SilentlyContinue))
        {
            $result[[int]$process.Id] = $true
        }
    }

    return $result
}

function Find-NewGameProcess
{
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$ExistingProcessIds,

        [Parameter(Mandatory = $true)]
        [DateTime]$LaunchStartedAt,

        [Parameter(Mandatory = $true)]
        [string[]]$ExpectedExecutablePaths
    )

    foreach ($processName in @(
            "TaleWorlds.MountAndBlade.Launcher",
            "Bannerlord.Native"))
    {
        foreach ($process in @(Get-Process -Name $processName -ErrorAction SilentlyContinue))
        {
            if ($ExistingProcessIds.ContainsKey([int]$process.Id))
            {
                continue
            }

            try
            {
                if ($process.StartTime -lt $LaunchStartedAt)
                {
                    continue
                }
            }
            catch
            {
                continue
            }

            try
            {
                $processPath = Get-NormalizedFullPath $process.Path
                $pathMatches = $false
                foreach ($expectedPath in $ExpectedExecutablePaths)
                {
                    if ([string]::Equals(
                            $processPath,
                            $expectedPath,
                            [StringComparison]::OrdinalIgnoreCase))
                    {
                        $pathMatches = $true
                        break
                    }
                }

                if (-not $pathMatches)
                {
                    continue
                }
            }
            catch
            {
                # The path may be unavailable for a process owned by another security
                # context. Its new process ID, exact name, and start time still make it
                # safe to use as the lifecycle boundary without modifying the process.
            }

            return $process
        }
    }

    return $null
}

function Start-CoopMultiplayerAndWait
{
    if ([string]::IsNullOrWhiteSpace($GameExecutable))
    {
        throw "GameExecutable is required for the RunMultiplayer phase."
    }

    $normalizedGameExecutable = Get-NormalizedFullPath $GameExecutable
    if (-not (Test-Path -LiteralPath $normalizedGameExecutable -PathType Leaf))
    {
        throw "The Bannerlord executable was not found: $normalizedGameExecutable"
    }

    if ([string]::IsNullOrWhiteSpace($GameWorkingDirectory))
    {
        $normalizedWorkingDirectory = Get-NormalizedFullPath (
            [IO.Path]::GetDirectoryName($normalizedGameExecutable))
    }
    else
    {
        $normalizedWorkingDirectory = Get-NormalizedFullPath $GameWorkingDirectory
    }

    if (-not (Test-Path -LiteralPath $normalizedWorkingDirectory -PathType Container))
    {
        throw "The Bannerlord working directory was not found: $normalizedWorkingDirectory"
    }

    $expectedExecutablePaths = @(
        (Get-NormalizedFullPath (
            Join-Path $normalizedWorkingDirectory "TaleWorlds.MountAndBlade.Launcher.exe"))
        (Get-NormalizedFullPath (
            Join-Path $normalizedWorkingDirectory "Bannerlord.Native.exe"))
    )
    $existingProcessIds = Get-ExistingGameProcessIds
    $launchStartedAt = [DateTime]::Now.AddSeconds(-1)

    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $normalizedGameExecutable
    $startInfo.Arguments = $GameArguments
    $startInfo.WorkingDirectory = $normalizedWorkingDirectory
    $startInfo.UseShellExecute = $false

    $starter = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $starter)
    {
        throw "Could not start Bannerlord: $normalizedGameExecutable"
    }

    Write-Output "[CoopSpectator] Started Bannerlord entry process PID=$($starter.Id)."

    try
    {
        Start-DetachedCleanupWatcher $starter
    }
    catch
    {
        Write-Warning ("[CoopSpectator] Could not start the detached shader-cache cleanup watcher for the entry process: {0}" -f $_.Exception.Message)
    }

    $actualGameProcess = $null
    $deadline = [DateTime]::UtcNow.AddSeconds($ChildProcessStartTimeoutSeconds)
    do
    {
        $actualGameProcess = Find-NewGameProcess `
            -ExistingProcessIds $existingProcessIds `
            -LaunchStartedAt $launchStartedAt `
            -ExpectedExecutablePaths $expectedExecutablePaths
        if ($null -ne $actualGameProcess)
        {
            break
        }

        if ([DateTime]::UtcNow -ge $deadline)
        {
            break
        }

        Start-Sleep -Milliseconds 200
    }
    while ($true)

    if ($null -ne $actualGameProcess)
    {
        try
        {
            Start-DetachedCleanupWatcher $actualGameProcess
        }
        catch
        {
            Write-Warning ("[CoopSpectator] Could not start the detached shader-cache cleanup watcher: {0}" -f $_.Exception.Message)
        }

        Write-Output "[CoopSpectator] Waiting for the actual game process PID=$($actualGameProcess.Id) Name=$($actualGameProcess.ProcessName)."
        $actualGameProcess.WaitForExit()
        $actualGameProcess.Refresh()
        $exitCode = $actualGameProcess.ExitCode

        if (-not $starter.HasExited)
        {
            $starter.WaitForExit()
        }

        return $exitCode
    }

    Write-Output "[CoopSpectator] No separate game process was detected; waiting for the entry process PID=$($starter.Id)."
    if (-not $starter.HasExited)
    {
        $starter.WaitForExit()
    }

    $starter.Refresh()
    return $starter.ExitCode
}

try
{
    if ($Phase -eq "WaitForMultiplayerExit")
    {
        Wait-ForGameProcessAndClearCache
        exit 0
    }

    if ($Phase -ne "RunMultiplayer")
    {
        Clear-CoopShaderCache $Phase
        exit 0
    }

    $gameExitCode = 1
    $cleanupFailed = $false
    Clear-CoopShaderCache "BeforeMultiplayer"
    try
    {
        $gameExitCode = Start-CoopMultiplayerAndWait
    }
    finally
    {
        try
        {
            Clear-CoopShaderCache "AfterMultiplayer"
        }
        catch
        {
            $cleanupFailed = $true
            Write-Error ("[CoopSpectator] Could not clear the Bannerlord runtime shader cache after multiplayer: {0}" -f $_.Exception.Message)
        }
    }

    if ($cleanupFailed)
    {
        exit 1
    }

    exit $gameExitCode
}
catch
{
    Write-Error ("[CoopSpectator] Shader-cache mode switch failed for phase '{0}': {1}" -f $Phase, $_.Exception.Message)
    exit 1
}
