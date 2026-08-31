[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RunId,

    [Parameter(Mandatory = $true)]
    [string]$ServerName,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedClientModuleSha256,

    [ValidateRange(1, 65535)]
    [int]$Port = 7210,

    [string]$GameRoot,

    [string]$GameType = '',

    [string]$UniqueMapId = '',

    [ValidateRange(30, 1800)]
    [int]$RequestLifetimeSeconds = 600,

    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$automationFlagName = 'COOPSPECTATOR_TEST_AUTOMATION'
$runIdVariableName = 'COOPSPECTATOR_AUTOMATION_RUN_ID'
$runRootVariableName = 'COOPSPECTATOR_AUTOMATION_RUN_ROOT'
$runTokenVariableName = 'COOPSPECTATOR_AUTOMATION_RUN_TOKEN'
$serverPasswordVariableName = 'COOPSPECTATOR_AUTOMATION_SERVER_PASSWORD'
$modulesArgument = '_MODULES_*Native*SandBoxCore*Sandbox*Multiplayer*CoopSpectator*_MODULES_'

function Test-Sha256Hex {
    param([string]$Value)

    return -not [string]::IsNullOrWhiteSpace($Value) -and
        $Value -match '^[0-9A-Fa-f]{64}$'
}

function Get-StringSha256 {
    param([Parameter(Mandatory = $true)][string]$Value)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
        return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function New-RunToken {
    $bytes = New-Object byte[] 32
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
        return [Convert]::ToBase64String($bytes)
    }
    finally {
        $generator.Dispose()
    }
}

function Write-JsonAtomic {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }

    $temporaryPath = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    try {
        $json = $Value | ConvertTo-Json -Depth 10
        [System.IO.File]::WriteAllText($temporaryPath, $json + [Environment]::NewLine, $utf8WithoutBom)
        if ([System.IO.File]::Exists($Path)) {
            throw "Refusing to replace existing run artifact: $Path"
        }
        [System.IO.File]::Move($temporaryPath, $Path)
    }
    finally {
        if ([System.IO.File]::Exists($temporaryPath)) {
            [System.IO.File]::Delete($temporaryPath)
        }
    }
}

if ($RunId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$') {
    throw 'RunId must contain only ASCII letters, digits, dot, underscore, or hyphen and must not exceed 80 characters.'
}

if ([string]::IsNullOrWhiteSpace($ServerName) -or $ServerName.Length -gt 128 -or $ServerName -match '[\x00-\x1F\x7F]') {
    throw 'ServerName is empty, too long, or contains a control character.'
}

$normalizedExpectedHash = if ($null -eq $ExpectedClientModuleSha256) {
    ''
}
else {
    $ExpectedClientModuleSha256.Trim().ToUpperInvariant()
}
if (-not (Test-Sha256Hex -Value $normalizedExpectedHash)) {
    throw 'ExpectedClientModuleSha256 must be exactly 64 hexadecimal characters.'
}

if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    $configuredRoot = [Environment]::GetEnvironmentVariable('BANNERLORD_GAME_ROOT', 'Process')
    $GameRoot = if ([string]::IsNullOrWhiteSpace($configuredRoot)) {
        'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
    }
    else {
        $configuredRoot
    }
}

$resolvedGameRoot = [System.IO.Path]::GetFullPath($GameRoot).TrimEnd('\', '/')
$gameExecutable = Join-Path $resolvedGameRoot 'bin\Win64_Shipping_Client\Bannerlord.exe'
$moduleDescriptor = Join-Path $resolvedGameRoot 'Modules\CoopSpectator\SubModule.xml'
$moduleAssembly = Join-Path $resolvedGameRoot 'Modules\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll'
$harmonyAssembly = Join-Path $resolvedGameRoot 'Modules\CoopSpectator\bin\Win64_Shipping_Client\0Harmony.dll'

foreach ($requiredFile in @($gameExecutable, $moduleDescriptor, $moduleAssembly, $harmonyAssembly)) {
    if (-not [System.IO.File]::Exists($requiredFile)) {
        throw "Required client file is missing: $requiredFile"
    }
}

$steamProcesses = @(Get-Process -Name 'steam' -ErrorAction SilentlyContinue | Where-Object {
    try {
        -not [string]::IsNullOrWhiteSpace($_.Path) -and
            [string]::Equals([System.IO.Path]::GetFileName($_.Path), 'Steam.exe', [StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        $false
    }
})
if ($steamProcesses.Count -eq 0) {
    throw 'Steam.exe must already be running in the current interactive user session.'
}

$actualModuleHash = (Get-FileHash -LiteralPath $moduleAssembly -Algorithm SHA256).Hash.ToUpperInvariant()
if (-not [string]::Equals($actualModuleHash, $normalizedExpectedHash, [StringComparison]::Ordinal)) {
    throw "Installed client module hash mismatch. Expected=$normalizedExpectedHash Actual=$actualModuleHash Path=$moduleAssembly"
}

$moduleVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($moduleAssembly).ProductVersion
$validation = [ordered]@{
    Validation = 'Passed'
    RunId = $RunId
    ServerName = $ServerName
    ServerPort = $Port
    GameRoot = $resolvedGameRoot
    GameExecutable = $gameExecutable
    ClientModulePath = $moduleAssembly
    ClientModuleSha256 = $actualModuleHash
    ClientModuleProductVersion = $moduleVersion
    SteamProcessIds = @($steamProcesses | ForEach-Object { $_.Id })
}

if ($ValidateOnly) {
    [PSCustomObject]$validation
    return
}

$runRoot = [System.IO.Path]::GetFullPath(
    (Join-Path ([System.IO.Path]::GetTempPath()) (Join-Path 'CoopSpectator\Automation' $RunId)))
$requestPath = Join-Path $runRoot 'commands\client-join.request.json'
$statusPath = Join-Path $runRoot 'state\client-join.status.json'
$launchArtifactPath = Join-Path $runRoot 'artifacts\processes\client-launch.json'
$lockPath = Join-Path $runRoot 'work\client-launch.lock'

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $lockPath)) | Out-Null
$lockStream = $null
try {
    $lockStream = New-Object System.IO.FileStream(
        $lockPath,
        [System.IO.FileMode]::OpenOrCreate,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)

    if ([System.IO.File]::Exists($requestPath) -or [System.IO.File]::Exists($statusPath)) {
        throw "RunId already contains a join request or status. Use a new RunId: $RunId"
    }

    $runToken = New-RunToken
    $runTokenHash = Get-StringSha256 -Value $runToken
    $serverPassword = [Environment]::GetEnvironmentVariable($serverPasswordVariableName, 'Process')
    $createdUtc = [DateTime]::UtcNow
    $expiresUtc = $createdUtc.AddSeconds($RequestLifetimeSeconds)
    $commandId = [Guid]::NewGuid().ToString('D')

    $request = [ordered]@{
        SchemaVersion = 2
        ProtocolMajorVersion = 1
        ProtocolMinorVersion = 0
        RunId = $RunId
        Sequence = 1
        CommandId = $commandId
        SourceRoleType = 'Runner'
        SourceRoleInstanceId = 'runner-01'
        TargetRoleType = 'MultiplayerClient'
        TargetRoleInstanceId = 'multiplayer-client-01'
        CreatedUtc = $createdUtc.ToString('O')
        ExpiresUtc = $expiresUtc.ToString('O')
        RunTokenSha256 = $runTokenHash
        ExpectedClientModuleSha256 = $actualModuleHash
        ServerName = $ServerName
        ServerPort = $Port
        GameType = $GameType
        UniqueMapId = $UniqueMapId
        RequireLocalHostOwnership = $true
        PasswordProvided = -not [string]::IsNullOrEmpty($serverPassword)
        RequestedBy = "$env:COMPUTERNAME\$env:USERNAME"
    }
    Write-JsonAtomic -Path $requestPath -Value $request

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $gameExecutable
    $startInfo.WorkingDirectory = Split-Path -Parent $gameExecutable
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = "/multiplayer $modulesArgument"
    $startInfo.EnvironmentVariables[$automationFlagName] = '1'
    $startInfo.EnvironmentVariables[$runIdVariableName] = $RunId
    $startInfo.EnvironmentVariables[$runRootVariableName] = $runRoot
    $startInfo.EnvironmentVariables[$runTokenVariableName] = $runToken
    if (-not [string]::IsNullOrEmpty($serverPassword)) {
        $startInfo.EnvironmentVariables[$serverPasswordVariableName] = $serverPassword
    }
    else {
        $startInfo.EnvironmentVariables.Remove($serverPasswordVariableName)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw 'Bannerlord process creation returned null.'
    }

    $processStartUtc = $process.StartTime.ToUniversalTime()
    $launchArtifact = [ordered]@{
        Schema = 'coop-automation-client-launch-v2'
        RunId = $RunId
        CommandId = $commandId
        LaunchUtc = [DateTime]::UtcNow.ToString('O')
        EntryPid = $process.Id
        EntryPath = $gameExecutable
        EntryStartUtc = $processStartUtc.ToString('O')
        WorkingDirectory = $startInfo.WorkingDirectory
        Arguments = $startInfo.Arguments
        SteamProcessIds = @($steamProcesses | ForEach-Object { $_.Id })
        ClientModulePath = $moduleAssembly
        ClientModuleSha256 = $actualModuleHash
        ClientModuleProductVersion = $moduleVersion
        ServerName = $ServerName
        ServerPort = $Port
        RequestPath = $requestPath
        StatusPath = $statusPath
        PasswordProvided = -not [string]::IsNullOrEmpty($serverPassword)
        PasswordPersisted = $false
        ShaderCacheHelperUsed = $false
        UiAutomationUsed = $false
        StartGameIssued = $false
        MissionOpenIssued = $false
    }
    Write-JsonAtomic -Path $launchArtifactPath -Value $launchArtifact

    Write-Host "[OK] Bannerlord multiplayer client started. PID=$($process.Id)"
    Write-Host "[OK] RunId=$RunId"
    Write-Host "[OK] Request=$requestPath"
    Write-Host "[OK] Status=$statusPath"
    Write-Host '[INFO] The launcher does not issue start_game or open a mission.'
}
finally {
    if ($null -ne $lockStream) {
        $lockStream.Dispose()
    }
}
