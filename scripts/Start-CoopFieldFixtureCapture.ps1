[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RunId,

    [Parameter(Mandatory = $true)]
    [string]$FixtureId,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedClientModuleSha256,

    [Parameter(Mandatory = $true)]
    [string]$SourceRevision,

    [Parameter(Mandatory = $true)]
    [string]$GameVersion,

    [string]$GameRoot,

    [switch]$UseExistingRunContract,

    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runnerCorePath = Join-Path $PSScriptRoot 'CoopAutomationRunner.Core.ps1'
if (-not [System.IO.File]::Exists($runnerCorePath)) {
    throw "Runner core helper is missing: $runnerCorePath"
}
. $runnerCorePath

$automationFlagName = 'COOPSPECTATOR_TEST_AUTOMATION'
$runIdVariableName = 'COOPSPECTATOR_AUTOMATION_RUN_ID'
$runRootVariableName = 'COOPSPECTATOR_AUTOMATION_RUN_ROOT'
$runTokenVariableName = 'COOPSPECTATOR_AUTOMATION_RUN_TOKEN'
$expectedModuleSha256VariableName = 'COOPSPECTATOR_AUTOMATION_EXPECTED_MODULE_SHA256'
$resultPolicyVariableName = 'COOPSPECTATOR_AUTOMATION_RESULT_POLICY'
$fixtureRecordVariableName = 'COOPSPECTATOR_AUTOMATION_FIXTURE_RECORD'
$fixtureIdVariableName = 'COOPSPECTATOR_AUTOMATION_FIXTURE_ID'
$sourceRevisionVariableName = 'COOPSPECTATOR_AUTOMATION_SOURCE_REVISION'
$gameVersionVariableName = 'COOPSPECTATOR_AUTOMATION_GAME_VERSION'
$serverPasswordVariableName = 'COOPSPECTATOR_AUTOMATION_SERVER_PASSWORD'
$modulesArgument = '_MODULES_*Native*SandBoxCore*CustomBattle*Sandbox*StoryMode*CoopSpectator*_MODULES_'
$gameArguments = "/singleplayer $modulesArgument"

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
        $json = $Value | ConvertTo-Json -Depth 12
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

function Read-JsonShared {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not [System.IO.File]::Exists($Path)) {
        throw "Required run-contract artifact is missing: $Path"
    }
    $stream = New-Object System.IO.FileStream(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
    try {
        $reader = New-Object System.IO.StreamReader($stream)
        try { return ($reader.ReadToEnd() | ConvertFrom-Json) }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

if ($RunId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$') {
    throw 'RunId must contain only ASCII letters, digits, dot, underscore, or hyphen and must not exceed 80 characters.'
}
if ($FixtureId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$') {
    throw 'FixtureId must contain only ASCII letters, digits, dot, underscore, or hyphen and must not exceed 80 characters.'
}
if ($SourceRevision -notmatch '^(?:[0-9A-Fa-f]{40}|[0-9A-Fa-f]{64})$') {
    throw 'SourceRevision must be exactly 40 or 64 hexadecimal characters.'
}
if ([string]::IsNullOrWhiteSpace($GameVersion) -or $GameVersion.Length -gt 64 -or
    $GameVersion -notmatch '^v?[0-9]+(?:\.[0-9]+){1,3}(?:[-+][A-Za-z0-9._-]+)?$') {
    throw 'GameVersion must be a short version identifier such as v1.4.8.'
}

$normalizedExpectedHash = $ExpectedClientModuleSha256.Trim().ToUpperInvariant()
if (-not (Test-Sha256Hex -Value $normalizedExpectedHash)) {
    throw 'ExpectedClientModuleSha256 must be exactly 64 hexadecimal characters.'
}

if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    $configuredRoot = [Environment]::GetEnvironmentVariable('BANNERLORD_GAME_ROOT', 'Process')
    $GameRoot = if ([string]::IsNullOrWhiteSpace($configuredRoot)) {
        'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
    }
    else { $configuredRoot }
}

$resolvedGameRoot = [System.IO.Path]::GetFullPath($GameRoot).TrimEnd('\', '/')
$gameExecutable = Join-Path $resolvedGameRoot 'bin\Win64_Shipping_Client\Bannerlord.exe'
$moduleDescriptor = Join-Path $resolvedGameRoot 'Modules\CoopSpectator\SubModule.xml'
$moduleAssembly = Join-Path $resolvedGameRoot 'Modules\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll'
$nativeDescriptor = Join-Path $resolvedGameRoot 'Modules\Native\SubModule.xml'

foreach ($requiredFile in @($gameExecutable, $moduleDescriptor, $moduleAssembly, $nativeDescriptor)) {
    if (-not [System.IO.File]::Exists($requiredFile)) {
        throw "Required campaign-capture file is missing: $requiredFile"
    }
}

$steamProcesses = @(Get-Process -Name 'steam' -ErrorAction SilentlyContinue | Where-Object {
    try {
        -not [string]::IsNullOrWhiteSpace($_.Path) -and
        [string]::Equals([System.IO.Path]::GetFileName($_.Path), 'Steam.exe', [StringComparison]::OrdinalIgnoreCase)
    }
    catch { $false }
})
if ($steamProcesses.Count -eq 0) {
    throw 'Steam.exe must already be running in the current interactive user session.'
}

$actualModuleHash = (Get-FileHash -LiteralPath $moduleAssembly -Algorithm SHA256).Hash.ToUpperInvariant()
if (-not [string]::Equals($actualModuleHash, $normalizedExpectedHash, [StringComparison]::Ordinal)) {
    throw "Installed client module hash mismatch. Expected=$normalizedExpectedHash Actual=$actualModuleHash Path=$moduleAssembly"
}

[xml]$nativeModuleXml = [System.IO.File]::ReadAllText($nativeDescriptor)
$observedGameVersion = [string]$nativeModuleXml.Module.Version.value
if ([string]::IsNullOrWhiteSpace($observedGameVersion) -or
    -not [string]::Equals($observedGameVersion.Trim(), $GameVersion.Trim(), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Observed Native module version does not match GameVersion. Expected=$GameVersion Actual=$observedGameVersion"
}

$moduleVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($moduleAssembly).ProductVersion
$validation = [ordered]@{
    Validation = 'Passed'
    RunId = $RunId
    FixtureId = $FixtureId
    GameRoot = $resolvedGameRoot
    GameExecutable = $gameExecutable
    GameArguments = $gameArguments
    ClientModulePath = $moduleAssembly
    ClientModuleSha256 = $actualModuleHash
    ClientModuleProductVersion = $moduleVersion
    SourceRevision = $SourceRevision.ToLowerInvariant()
    GameVersion = $observedGameVersion.Trim()
    SteamProcessIds = @($steamProcesses | ForEach-Object { $_.Id })
    UiAutomationUsed = $false
}

if ($ValidateOnly) {
    [PSCustomObject]$validation
    return
}
if (-not $UseExistingRunContract) {
    throw 'Live fixture capture requires -UseExistingRunContract from scripts/Invoke-CoopTest.ps1 -Command Record.'
}

$runRoot = [System.IO.Path]::GetFullPath(
    (Join-Path ([System.IO.Path]::GetTempPath()) (Join-Path 'CoopSpectator\Automation' $RunId)))
$manifestPath = Join-Path $runRoot 'manifest.json'
$runnerLockPath = Join-Path $runRoot 'work\runner.lock'
$captureLockPath = Join-Path $runRoot 'work\fixture-capture-launch.lock'
$provisionalLaunchArtifactPath = Join-Path $runRoot 'artifacts\processes\campaign-capture-launch.provisional.json'
$launchArtifactPath = Join-Path $runRoot 'artifacts\processes\campaign-capture-launch.json'
$launchCleanupArtifactPath = Join-Path $runRoot 'artifacts\processes\campaign-capture-launch.cleanup.json'
$fixtureStatusPath = Join-Path $runRoot 'state\fixture-record.status.json'
$fixturePayloadPath = Join-Path $runRoot 'artifacts\fixtures\field-current\battle_roster.raw.json'
$fixtureMetadataPath = Join-Path $runRoot 'artifacts\fixtures\field-current\fixture.metadata.json'

$runToken = [Environment]::GetEnvironmentVariable($runTokenVariableName, 'Process')
$existingRunId = [Environment]::GetEnvironmentVariable($runIdVariableName, 'Process')
$existingRunRoot = [Environment]::GetEnvironmentVariable($runRootVariableName, 'Process')
$existingExpectedModuleHash = [Environment]::GetEnvironmentVariable($expectedModuleSha256VariableName, 'Process')
$existingResultPolicy = [Environment]::GetEnvironmentVariable($resultPolicyVariableName, 'Process')
if (-not [string]::Equals([Environment]::GetEnvironmentVariable($automationFlagName, 'Process'), '1', [StringComparison]::Ordinal)) {
    throw 'UseExistingRunContract requires the inherited automation flag.'
}
if ([string]::IsNullOrEmpty($runToken) -or $runToken.Length -lt 32) {
    throw 'UseExistingRunContract requires an inherited automation run token of at least 32 characters.'
}
if (-not [string]::Equals($existingRunId, $RunId, [StringComparison]::Ordinal)) {
    throw 'UseExistingRunContract inherited RunId does not match the requested RunId.'
}
if ([string]::IsNullOrWhiteSpace($existingRunRoot) -or
    -not [string]::Equals(
        [System.IO.Path]::GetFullPath($existingRunRoot).TrimEnd('\', '/'),
        $runRoot.TrimEnd('\', '/'),
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'UseExistingRunContract inherited run root does not match the required RunId-scoped root.'
}
if (-not [string]::Equals($existingExpectedModuleHash, $actualModuleHash, [StringComparison]::Ordinal)) {
    throw 'UseExistingRunContract inherited expected module hash does not match the validated installed client module.'
}
if (-not [string]::Equals($existingResultPolicy, 'Suppress', [StringComparison]::Ordinal)) {
    throw 'UseExistingRunContract requires ResultPolicy=Suppress.'
}

$runTokenHash = Get-StringSha256 -Value $runToken
$existingManifest = Read-JsonShared -Path $manifestPath
if ([string]$existingManifest.RunId -ne $RunId -or
    [string]$existingManifest.NonceSha256 -ne $runTokenHash -or
    [string]$existingManifest.RequestedCommand -ne 'Record' -or
    [string]$existingManifest.ResultPolicy -ne 'Suppress' -or
    [string]$existingManifest.RepositoryRevision -ne $SourceRevision) {
    throw 'UseExistingRunContract manifest identity, command, revision, token fingerprint, or result policy is invalid.'
}

$runnerLockHeld = $false
$runnerLockProbe = $null
if (-not [System.IO.File]::Exists($runnerLockPath)) {
    throw 'UseExistingRunContract requires the aggregate runner lock file.'
}
try {
    $runnerLockProbe = New-Object System.IO.FileStream(
        $runnerLockPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
}
catch [System.IO.IOException] {
    $runnerLockHeld = $true
}
finally {
    if ($null -ne $runnerLockProbe) { $runnerLockProbe.Dispose() }
}
if (-not $runnerLockHeld) {
    throw 'UseExistingRunContract requires an active aggregate runner lock.'
}

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $captureLockPath)) | Out-Null
$lockStream = $null
$process = $null
$provisionalIdentity = $null
$finalLaunchArtifactPublished = $false
$launchStartedUtc = $null
$launchObservedUtc = $null
try {
    $lockStream = New-Object System.IO.FileStream(
        $captureLockPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)

    foreach ($immutablePath in @(
        $provisionalLaunchArtifactPath,
        $launchArtifactPath,
        $fixtureStatusPath,
        $fixturePayloadPath,
        $fixtureMetadataPath)) {
        if ([System.IO.File]::Exists($immutablePath)) {
            throw "RunId already contains a fixture-capture artifact. Use a new RunId: $immutablePath"
        }
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $gameExecutable
    $startInfo.WorkingDirectory = Split-Path -Parent $gameExecutable
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = $gameArguments
    $startInfo.EnvironmentVariables[$automationFlagName] = '1'
    $startInfo.EnvironmentVariables[$runIdVariableName] = $RunId
    $startInfo.EnvironmentVariables[$runRootVariableName] = $runRoot
    $startInfo.EnvironmentVariables[$runTokenVariableName] = $runToken
    $startInfo.EnvironmentVariables[$expectedModuleSha256VariableName] = $actualModuleHash
    $startInfo.EnvironmentVariables[$resultPolicyVariableName] = 'Suppress'
    $startInfo.EnvironmentVariables[$fixtureRecordVariableName] = '1'
    $startInfo.EnvironmentVariables[$fixtureIdVariableName] = $FixtureId
    $startInfo.EnvironmentVariables[$sourceRevisionVariableName] = $SourceRevision.ToLowerInvariant()
    $startInfo.EnvironmentVariables[$gameVersionVariableName] = $observedGameVersion.Trim()
    $startInfo.EnvironmentVariables.Remove($serverPasswordVariableName)

    $launchStartedUtc = [DateTime]::UtcNow
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw 'Bannerlord campaign process creation returned null.'
    }
    $launchObservedUtc = [DateTime]::UtcNow

    $provisionalIdentity = New-CoopProvisionalProcessIdentity `
        -ProcessId $process.Id `
        -RoleType 'CampaignHost' `
        -RoleInstanceId 'campaign-host-01' `
        -ExpectedExecutablePath $gameExecutable `
        -ExpectedParentProcessId $PID `
        -LaunchStartedUtc $launchStartedUtc `
        -LaunchObservedUtc $launchObservedUtc
    Write-JsonAtomic -Path $provisionalLaunchArtifactPath -Value ([ordered]@{
        Schema = 'coop-campaign-capture-launch-provisional-v1'
        RunId = $RunId
        FixtureId = $FixtureId
        Identity = $provisionalIdentity
        PublishedUtc = [DateTime]::UtcNow.ToString('O')
    })

    $processObservation = Resolve-CoopProcessObservation `
        -ProcessId $process.Id `
        -ExpectedExecutablePath $gameExecutable `
        -ExpectedParentProcessId $PID `
        -LaunchStartedUtc $launchStartedUtc `
        -LaunchObservedUtc $launchObservedUtc `
        -DeadlineMilliseconds 5000

    $verifiedProcessIdentity = [ordered]@{
        IdentityState = 'Verified'
        LaunchOperationId = [string]$provisionalIdentity.LaunchOperationId
        RoleType = 'CampaignHost'
        RoleInstanceId = 'campaign-host-01'
        ProcessId = $process.Id
        ParentProcessId = [int]$processObservation.ParentProcessId
        ExpectedParentProcessId = $PID
        ProcessStartUtc = [string]$processObservation.ProcessStartUtc
        ExecutablePath = [string]$processObservation.ExecutablePath
        ExecutableSha256 = Get-CoopFileSha256 -Path ([string]$processObservation.ExecutablePath)
        PathEvidenceSource = [string]$processObservation.PathEvidenceSource
        LaunchStartedUtc = [string]$provisionalIdentity.LaunchStartedUtc
        LaunchObservedUtc = [string]$provisionalIdentity.LaunchObservedUtc
        RegisteredUtc = [string]$provisionalIdentity.RegisteredUtc
        VerifiedUtc = [DateTime]::UtcNow.ToString('O')
    }

    $launchArtifact = [ordered]@{
        Schema = 'coop-campaign-capture-launch-v1'
        RunId = $RunId
        FixtureId = $FixtureId
        LaunchOperationId = [string]$provisionalIdentity.LaunchOperationId
        IdentityState = 'Verified'
        LaunchUtc = [DateTime]::UtcNow.ToString('O')
        ProcessIdentity = $verifiedProcessIdentity
        ProvisionalArtifactPath = $provisionalLaunchArtifactPath
        WorkingDirectory = $startInfo.WorkingDirectory
        Arguments = $startInfo.Arguments
        ModulesArgument = $modulesArgument
        SteamProcessIds = @($steamProcesses | ForEach-Object { $_.Id })
        ClientModulePath = $moduleAssembly
        ClientModuleSha256 = $actualModuleHash
        ClientModuleProductVersion = $moduleVersion
        SourceRevision = $SourceRevision.ToLowerInvariant()
        GameVersion = $observedGameVersion.Trim()
        FixtureStatusPath = $fixtureStatusPath
        FixturePayloadPath = $fixturePayloadPath
        FixtureMetadataPath = $fixtureMetadataPath
        ResultPolicy = 'Suppress'
        RunTokenPersisted = $false
        CredentialsPersisted = $false
        UiAutomationUsed = $false
        SaveFileModifiedByLauncher = $false
        DedicatedStartedByLauncher = $false
        MultiplayerClientStartedByLauncher = $false
    }
    Write-JsonAtomic -Path $launchArtifactPath -Value $launchArtifact
    $finalLaunchArtifactPublished = $true
}
catch {
    $primaryException = $_.Exception
    $cleanupEvidence = $null
    $cleanupFailure = ''
    if ($null -ne $process -and -not $finalLaunchArtifactPublished) {
        if ($null -eq $provisionalIdentity) {
            try {
                if ($null -eq $launchStartedUtc) { $launchStartedUtc = [DateTime]::UtcNow }
                if ($null -eq $launchObservedUtc) { $launchObservedUtc = [DateTime]::UtcNow }
                $provisionalIdentity = New-CoopProvisionalProcessIdentity `
                    -ProcessId $process.Id `
                    -RoleType 'CampaignHost' `
                    -RoleInstanceId 'campaign-host-01' `
                    -ExpectedExecutablePath $gameExecutable `
                    -ExpectedParentProcessId $PID `
                    -LaunchStartedUtc $launchStartedUtc `
                    -LaunchObservedUtc $launchObservedUtc
            }
            catch { $cleanupFailure = 'Fallback provisional identity construction failed: ' + $_.Exception.Message }
        }
        if ($null -ne $provisionalIdentity) {
            try {
                $cleanupEvidence = Stop-CoopExactProcessIdentityCore -Identity $provisionalIdentity -GraceSeconds 5
                if ([string]$cleanupEvidence.Outcome -ne 'Stopped' -and
                    [string]$cleanupEvidence.Outcome -ne 'NotRunning') {
                    $cleanupFailure = 'Exact campaign cleanup outcome was ' + [string]$cleanupEvidence.Outcome + '.'
                }
            }
            catch { $cleanupFailure = 'Exact campaign cleanup failed: ' + $_.Exception.Message }
        }
        try {
            Write-JsonAtomic -Path $launchCleanupArtifactPath -Value ([ordered]@{
                Schema = 'coop-campaign-capture-launch-cleanup-v1'
                RunId = $RunId
                FixtureId = $FixtureId
                LaunchArtifactPublished = $finalLaunchArtifactPublished
                PrimaryError = $primaryException.Message
                CleanupError = $cleanupFailure
                ProvisionalIdentity = $provisionalIdentity
                CleanupEvidence = $cleanupEvidence
                CompletedUtc = [DateTime]::UtcNow.ToString('O')
            })
        }
        catch { }
    }

    $message = 'Post-start campaign capture handoff failed: ' + $primaryException.Message
    if (-not [string]::IsNullOrWhiteSpace($cleanupFailure)) { $message += ' ' + $cleanupFailure }
    $wrapped = [System.InvalidOperationException]::new($message, $primaryException)
    $wrapped.Data['CoopRuntimeOutcome'] = 'RunnerInternalError'
    throw $wrapped
}
finally {
    if ($null -ne $process) {
        try { $process.Dispose() } catch { }
    }
    if ($null -ne $lockStream) {
        try { $lockStream.Dispose() } catch { }
    }
}
