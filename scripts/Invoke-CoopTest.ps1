[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Doctor', 'Contracts', 'CompileOnly', 'Feasibility', 'Inspect', 'Recover', 'Cancel')]
    [string]$Command,

    [Parameter(Mandatory = $true)]
    [string]$RunId,

    [switch]$All,

    [string]$GameRoot,

    [string]$DedicatedServerRoot,

    [string]$MachineProfileName,

    [ValidateRange(1, 65535)]
    [int]$Port = 7210,

    [string]$ServerName,

    [string]$ExpectedClientModuleSha256,

    [string]$ExpectedDedicatedModuleSha256,

    [ValidateRange(60, 1800)]
    [int]$RuntimeTimeoutSeconds = 420,

    [switch]$ApplyRecovery
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runnerCorePath = Join-Path $PSScriptRoot 'CoopAutomationRunner.Core.ps1'
if (-not [System.IO.File]::Exists($runnerCorePath)) {
    throw "Runner core helper is missing: $runnerCorePath"
}
. $runnerCorePath

$protocolMajorVersion = 1
$protocolMinorVersion = 1
$manifestSchemaVersion = 1
$runnerRoleType = 'Runner'
$runnerRoleInstanceId = 'runner-01'
$leaseLifetimeMinutes = 60
$requiredPorts = @($Port, 7777) | Select-Object -Unique
$outcomeExitCodes = @{
    Pass = 0
    EnvironmentBlocked = 10
    PreconditionsFailed = 11
    AssertionFailed = 20
    Crash = 30
    Timeout = 31
    RunnerInternalError = 40
    Cancelled = 50
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\', '/')
$expectedRunRoot = [System.IO.Path]::GetFullPath(
    (Join-Path ([System.IO.Path]::GetTempPath()) (Join-Path 'CoopSpectator\Automation' $RunId)))
$runRoot = $expectedRunRoot.TrimEnd('\', '/')
$manifestPath = Join-Path $runRoot 'manifest.json'
$leasePath = Join-Path $runRoot 'work\runner.lease.json'
$lockPath = Join-Path $runRoot 'work\runner.lock'
$runnerStatusPath = Join-Path $runRoot 'status\runner-01.json'
$eventsPath = Join-Path $runRoot 'events\events.jsonl'
$cancellationRequestPath = Join-Path $runRoot 'commands\inbox\cancel.request.json'
$processedCancellationRequestPath = Join-Path $runRoot 'commands\processed\cancel.request.json'
$cancellationStatusPath = Join-Path $runRoot 'status\cancellation.status.json'
$lockStream = $null
$sharedRuntimeLockSet = $null
$sharedRuntimeLockReleaseEvidence = @()
$cancellationSignalInstalled = $false
$noncePlaintext = $null
$nonceSha256 = $null
$eventSequence = 0L
$runStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$runnerProcess = Get-Process -Id $PID
$runnerProcessStartUtc = $runnerProcess.StartTime.ToUniversalTime()
$runnerParentProcessId = 0
$runnerExecutableSha256 = ''
$runCreatedUtc = [DateTime]::UtcNow
$manifest = $null
$finalOutcome = 'RunnerInternalError'
$finalReason = 'Runner initialization did not complete.'
$releaseVerified = $false
$ownedRuntimeProcesses = New-Object System.Collections.Generic.List[object]
$runtimeCleanupEvidence = New-Object System.Collections.Generic.List[object]
$runnerCapabilities = @(
    'Doctor', 'Contracts', 'CompileOnly', 'Feasibility', 'Inspect', 'Recover', 'Cancel',
    'AtomicManifest', 'LeaseHeartbeat', 'RoleHealthV1', 'CancellationV1', 'RecoveryV2', 'FailureEvidenceV1')
$runnerState = 'Initializing'
$runnerStateEnteredUtc = [DateTime]::UtcNow
$runnerLastProgressUtc = $runnerStateEnteredUtc
$runnerStateRevision = 1L
$automationFlagName = 'COOPSPECTATOR_TEST_AUTOMATION'
$automationRunIdVariableName = 'COOPSPECTATOR_AUTOMATION_RUN_ID'
$automationRunRootVariableName = 'COOPSPECTATOR_AUTOMATION_RUN_ROOT'
$automationRunTokenVariableName = 'COOPSPECTATOR_AUTOMATION_RUN_TOKEN'
$automationExpectedModuleHashVariableName = 'COOPSPECTATOR_AUTOMATION_EXPECTED_MODULE_SHA256'
$automationResultPolicyVariableName = 'COOPSPECTATOR_AUTOMATION_RESULT_POLICY'

if ([string]::IsNullOrWhiteSpace($MachineProfileName)) {
    $configuredMachineProfileName = [Environment]::GetEnvironmentVariable('COOPSPECTATOR_MACHINE_PROFILE', 'Process')
    $MachineProfileName = if ([string]::IsNullOrWhiteSpace($configuredMachineProfileName)) {
        'LOCAL-' + $env:COMPUTERNAME + '-UNVERIFIED'
    }
    else { $configuredMachineProfileName }
}
if ($MachineProfileName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,119}$') {
    throw 'MachineProfileName must contain only ASCII letters, digits, dot, underscore, or hyphen, start with a letter/digit, and not exceed 120 characters.'
}

function Test-CoopRunId {
    param([string]$Value)

    return -not [string]::IsNullOrWhiteSpace($Value) -and
        $Value -match '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$'
}

function Get-CoopSha256Text {
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

function New-CoopNonce {
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

function Write-CoopJsonAtomic {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }

    $operationId = [Guid]::NewGuid().ToString('N')
    $temporaryPath = $Path + '.' + $operationId + '.tmp'
    $backupPath = $Path + '.' + $operationId + '.bak'
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    try {
        $json = $Value | ConvertTo-Json -Depth 30
        [System.IO.File]::WriteAllText($temporaryPath, $json + [Environment]::NewLine, $utf8WithoutBom)
        if ([System.IO.File]::Exists($Path)) {
            [System.IO.File]::Replace($temporaryPath, $Path, $backupPath, $true)
        }
        else {
            [System.IO.File]::Move($temporaryPath, $Path)
        }
    }
    finally {
        if ([System.IO.File]::Exists($temporaryPath)) {
            [System.IO.File]::Delete($temporaryPath)
        }
        if ([System.IO.File]::Exists($backupPath)) {
            [System.IO.File]::Delete($backupPath)
        }
    }
}

function Add-CoopEvent {
    param(
        [Parameter(Mandatory = $true)][string]$EventType,
        [Parameter(Mandatory = $true)][string]$Message
    )

    $script:eventSequence++
    $record = [ordered]@{
        ProtocolMajorVersion = $protocolMajorVersion
        ProtocolMinorVersion = $protocolMinorVersion
        RunId = $RunId
        NonceSha256 = $nonceSha256
        RoleType = $runnerRoleType
        RoleInstanceId = $runnerRoleInstanceId
        Sequence = $script:eventSequence
        TimestampUtc = [DateTime]::UtcNow.ToString('O')
        ElapsedMilliseconds = $runStopwatch.ElapsedMilliseconds
        EventType = $EventType
        Message = $Message
    }
    $line = ($record | ConvertTo-Json -Depth 10 -Compress) + "`n"
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($line)
    $directory = Split-Path -Parent $eventsPath
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null

    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        try {
            $stream = New-Object System.IO.FileStream(
                $eventsPath,
                [System.IO.FileMode]::Append,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::Read)
            try {
                $stream.Write($bytes, 0, $bytes.Length)
                $stream.Flush($true)
            }
            finally {
                $stream.Dispose()
            }
            return
        }
        catch [System.IO.IOException] {
            if ($attempt -eq 99) { throw }
            Start-Sleep -Milliseconds 5
        }
        catch [System.UnauthorizedAccessException] {
            if ($attempt -eq 99) { throw }
            Start-Sleep -Milliseconds 5
        }
    }
}

function Update-CoopLease {
    param([string]$Status = 'Active')

    if ([string]::Equals($Status, 'Active', [StringComparison]::Ordinal) -and
        -not [string]::IsNullOrWhiteSpace($nonceSha256)) {
        Assert-CoopRunNotCancelled
    }
    $now = [DateTime]::UtcNow
    $lease = [ordered]@{
        ProtocolMajorVersion = $protocolMajorVersion
        ProtocolMinorVersion = $protocolMinorVersion
        RunId = $RunId
        NonceSha256 = $nonceSha256
        OwnerRoleType = $runnerRoleType
        OwnerRoleInstanceId = $runnerRoleInstanceId
        OwnerProcessId = $PID
        OwnerProcessStartUtc = $runnerProcessStartUtc.ToString('O')
        CreatedUtc = $runCreatedUtc.ToString('O')
        LastHeartbeatUtc = $now.ToString('O')
        ExpiresUtc = $now.AddMinutes($leaseLifetimeMinutes).ToString('O')
        Status = $Status
    }
    Write-CoopJsonAtomic -Path $leasePath -Value $lease
}

function Write-CoopRunnerStatus {
    param(
        [Parameter(Mandatory = $true)][string]$State,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Outcome,
        [Parameter(Mandatory = $true)][string]$Reason,
        [bool]$IsTerminal = $false
    )

    $now = [DateTime]::UtcNow
    if (-not [string]::Equals($script:runnerState, $State, [StringComparison]::Ordinal)) {
        $script:runnerState = $State
        $script:runnerStateEnteredUtc = $now
        $script:runnerStateRevision++
    }
    $script:runnerLastProgressUtc = $now
    $status = [ordered]@{
        SchemaVersion = 2
        ProtocolMajorVersion = $protocolMajorVersion
        ProtocolMinorVersion = $protocolMinorVersion
        RunId = $RunId
        NonceSha256 = $nonceSha256
        RoleType = $runnerRoleType
        RoleInstanceId = $runnerRoleInstanceId
        SourceRoleType = $runnerRoleType
        SourceRoleInstanceId = $runnerRoleInstanceId
        TargetRoleType = $runnerRoleType
        TargetRoleInstanceId = $runnerRoleInstanceId
        Sequence = $eventSequence
        UpdatedUtc = $now.ToString('O')
        HeartbeatUtc = $now.ToString('O')
        LastProgressUtc = $runnerLastProgressUtc.ToString('O')
        StateEnteredUtc = $runnerStateEnteredUtc.ToString('O')
        StateRevision = $runnerStateRevision
        MonotonicElapsedMilliseconds = $runStopwatch.ElapsedMilliseconds
        MonotonicSinceProgressMilliseconds = 0L
        ProcessId = $PID
        ProcessStartUtc = $runnerProcessStartUtc.ToString('O')
        Capabilities = $runnerCapabilities
        ExecutablePath = $runnerProcess.Path
        ExecutableSha256 = $runnerExecutableSha256
        NonceCorrelation = 'Confirmed'
        CampaignId = ''
        BattleInstanceId = ''
        BattleStage = ''
        AuthoritativeSource = 'scripts/Invoke-CoopTest.ps1'
        LastStructuredError = $(if ([string]::IsNullOrWhiteSpace($Outcome) -or $Outcome -eq 'Pass') { '' } else { $Outcome + ': ' + $Reason })
        State = $State
        Outcome = $Outcome
        Reason = $Reason
        IsTerminal = $IsTerminal
    }
    Write-CoopJsonAtomic -Path $runnerStatusPath -Value $status
}

function Get-CoopValidatedCancellationRequest {
    if (-not [System.IO.File]::Exists($cancellationRequestPath)) { return $null }
    $request = Read-CoopJsonShared -Path $cancellationRequestPath
    if ($null -eq $request) { throw 'Cancellation request exists but is unreadable.' }
    if ([int](Get-CoopOptionalPropertyValue -InputObject $request -Name 'SchemaVersion') -ne 1 -or
        [int](Get-CoopOptionalPropertyValue -InputObject $request -Name 'ProtocolMajorVersion') -ne 1 -or
        [int](Get-CoopOptionalPropertyValue -InputObject $request -Name 'ProtocolMinorVersion') -ne 1) {
        throw 'CancellationV1 requires exact schema 1 and protocol 1.1.'
    }
    if (-not [string]::Equals([string]$request.RunId, $RunId, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$request.NonceSha256, $nonceSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Cancellation request run identity mismatch.'
    }
    $requestId = [Guid]::Empty
    if (-not [Guid]::TryParse([string]$request.RequestId, [ref]$requestId) -or $requestId -eq [Guid]::Empty) {
        throw 'Cancellation request id is invalid.'
    }
    if (-not [string]::Equals([string]$request.TargetRoleType, 'Runner', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$request.TargetRoleInstanceId, 'runner-01', [StringComparison]::Ordinal)) {
        throw 'Cancellation request target identity mismatch.'
    }
    $requestedUtc = ConvertTo-CoopUtcDateTime -Value $request.RequestedUtc
    $nowUtc = [DateTime]::UtcNow
    if ($null -eq $requestedUtc -or $requestedUtc -gt $nowUtc.AddMinutes(1) -or
        $nowUtc - $requestedUtc -gt [TimeSpan]::FromHours(1)) {
        throw 'Cancellation request timestamp is invalid, stale, or from the future.'
    }
    return $request
}

function Assert-CoopRunNotCancelled {
    $request = Get-CoopValidatedCancellationRequest
    $consoleRequested = Test-CoopConsoleCancellationRequestedCore
    if ($null -eq $request -and -not $consoleRequested) { return }

    $failure = [System.OperationCanceledException]::new(
        $(if ($consoleRequested) { 'Runner cancellation was requested by Ctrl+C.' } else { 'Runner cancellation was requested by an exact run-scoped command.' }))
    $failure.Data['CoopRuntimeOutcome'] = 'Cancelled'
    if ($null -ne $request) { $failure.Data['CoopCancellationRequest'] = $request }
    throw $failure
}

function Complete-CoopCancellation {
    param([Parameter(Mandatory = $true)][string]$Reason)

    $request = $null
    try { $request = Get-CoopValidatedCancellationRequest } catch { }
    if ($null -ne $request -and [System.IO.File]::Exists($cancellationRequestPath) -and
        -not [System.IO.File]::Exists($processedCancellationRequestPath)) {
        [System.IO.File]::Move($cancellationRequestPath, $processedCancellationRequestPath)
    }
    Write-CoopJsonAtomic -Path $cancellationStatusPath -Value ([ordered]@{
        SchemaVersion = 1
        ProtocolMajorVersion = 1
        ProtocolMinorVersion = 1
        RunId = $RunId
        NonceSha256 = $nonceSha256
        RequestId = if ($null -ne $request) { [string]$request.RequestId } else { '' }
        State = 'Cancelled'
        IsTerminal = $true
        Reason = $Reason
        AcknowledgedUtc = [DateTime]::UtcNow.ToString('O')
    })
}

function Get-CoopGitValue {
    param([string[]]$Arguments)

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $result = & git.exe -C $repositoryRoot @Arguments 2>$null
        if ($LASTEXITCODE -ne 0) { return '' }
        return (($result | ForEach-Object { $_.ToString() }) -join "`n").Trim()
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
}

function Get-CoopFileFact {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not [System.IO.File]::Exists($fullPath)) {
        return [ordered]@{ Path = $fullPath; Exists = $false }
    }

    $info = New-Object System.IO.FileInfo($fullPath)
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($fullPath)
    return [ordered]@{
        Path = $fullPath
        Exists = $true
        Length = $info.Length
        LastWriteUtc = $info.LastWriteTimeUtc.ToString('O')
        Sha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToUpperInvariant()
        FileVersion = $version.FileVersion
        ProductVersion = $version.ProductVersion
    }
}

function Get-CoopParentProcessId {
    try {
        $record = Get-CimInstance -ClassName Win32_Process -Filter ("ProcessId=" + $PID) -ErrorAction Stop
        if ($null -ne $record -and $null -ne $record.ParentProcessId) {
            return [int]$record.ParentProcessId
        }
    }
    catch {
    }
    return 0
}

function Get-CoopPortOwnership {
    $tcpCommand = Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue
    $udpCommand = Get-Command Get-NetUDPEndpoint -ErrorAction SilentlyContinue
    if ($null -eq $tcpCommand -or $null -eq $udpCommand) {
        return [ordered]@{
            InspectionAvailable = $false
            RequiredPorts = $requiredPorts
            Entries = @()
        }
    }

    $entries = @()
    $endpoints = @(
        Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
            Where-Object { $requiredPorts -contains [int]$_.LocalPort } |
            ForEach-Object {
                [ordered]@{
                    Protocol = 'TCP'
                    LocalAddress = $_.LocalAddress
                    LocalPort = [int]$_.LocalPort
                    OwnerProcessId = [int]$_.OwningProcess
                }
            }
        Get-NetUDPEndpoint -ErrorAction SilentlyContinue |
            Where-Object { $requiredPorts -contains [int]$_.LocalPort } |
            ForEach-Object {
                [ordered]@{
                    Protocol = 'UDP'
                    LocalAddress = $_.LocalAddress
                    LocalPort = [int]$_.LocalPort
                    OwnerProcessId = [int]$_.OwningProcess
                }
            }
    )

    foreach ($endpoint in $endpoints) {
        $owner = Get-Process -Id $endpoint.OwnerProcessId -ErrorAction SilentlyContinue
        $ownerName = ''
        $ownerPath = ''
        $ownerStartUtc = $null
        if ($null -ne $owner) {
            $ownerName = $owner.ProcessName
            try { $ownerPath = $owner.Path } catch { }
            try { $ownerStartUtc = $owner.StartTime.ToUniversalTime().ToString('O') } catch { }
        }
        $entries += [ordered]@{
            Port = $endpoint.LocalPort
            Protocol = $endpoint.Protocol
            LocalAddress = $endpoint.LocalAddress
            OwnerProcessId = $endpoint.OwnerProcessId
            OwnerProcessName = $ownerName
            OwnerExecutablePath = $ownerPath
            OwnerProcessStartUtc = $ownerStartUtc
        }
    }

    return [ordered]@{
        InspectionAvailable = $true
        RequiredPorts = $requiredPorts
        Entries = @($entries | Sort-Object Port, Protocol, LocalAddress, OwnerProcessId)
    }
}

function Test-CoopSha256Hex {
    param([string]$Value)

    return -not [string]::IsNullOrWhiteSpace($Value) -and
        $Value -match '^[0-9A-Fa-f]{64}$'
}

function Read-CoopJsonShared {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not [System.IO.File]::Exists($Path)) { return $null }
    try {
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
    catch {
        return $null
    }
}

function Get-CoopProcessIdentity {
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][string]$RoleType,
        [Parameter(Mandatory = $true)][string]$RoleInstanceId,
        [string]$ExpectedExecutablePath,
        [int]$ObservedParentProcessId = -1,
        $ProvisionalIdentity,
        [ValidateRange(50, 30000)][int]$DeadlineMilliseconds = 5000
    )

    try {
        $expectedParentProcessId = $ObservedParentProcessId
        if ($expectedParentProcessId -lt 0 -and $null -ne $ProvisionalIdentity) {
            $provisionalParent = Get-CoopOptionalPropertyValue -InputObject $ProvisionalIdentity -Name 'ExpectedParentProcessId'
            if ($null -ne $provisionalParent) { $expectedParentProcessId = [int]$provisionalParent }
        }
        if ([string]::IsNullOrWhiteSpace($ExpectedExecutablePath) -and $null -ne $ProvisionalIdentity) {
            $ExpectedExecutablePath = [string](Get-CoopOptionalPropertyValue -InputObject $ProvisionalIdentity -Name 'ExecutablePath')
        }
        $launchStartedUtc = if ($null -eq $ProvisionalIdentity) {
            $null
        }
        else {
            ConvertTo-CoopUtcDateTime -Value (Get-CoopOptionalPropertyValue -InputObject $ProvisionalIdentity -Name 'LaunchStartedUtc')
        }
        $launchObservedUtc = if ($null -eq $ProvisionalIdentity) {
            $null
        }
        else {
            ConvertTo-CoopUtcDateTime -Value (Get-CoopOptionalPropertyValue -InputObject $ProvisionalIdentity -Name 'LaunchObservedUtc')
        }
        $observation = Resolve-CoopProcessObservation `
            -ProcessId $ProcessId `
            -ExpectedExecutablePath $ExpectedExecutablePath `
            -ExpectedParentProcessId $expectedParentProcessId `
            -LaunchStartedUtc $launchStartedUtc `
            -LaunchObservedUtc $launchObservedUtc `
            -DeadlineMilliseconds $DeadlineMilliseconds
        $launchOperationId = if ($null -eq $ProvisionalIdentity) {
            ''
        }
        else {
            [string](Get-CoopOptionalPropertyValue -InputObject $ProvisionalIdentity -Name 'LaunchOperationId')
        }
        $registeredUtc = if ($null -eq $ProvisionalIdentity) {
            [DateTime]::UtcNow.ToString('O')
        }
        else {
            [string](Get-CoopOptionalPropertyValue -InputObject $ProvisionalIdentity -Name 'RegisteredUtc')
        }
        return [ordered]@{
            IdentityState = 'Verified'
            LaunchOperationId = $launchOperationId
            RoleType = $RoleType
            RoleInstanceId = $RoleInstanceId
            ProcessId = $ProcessId
            ParentProcessId = if ([int]$observation.ParentProcessId -ge 0) { [int]$observation.ParentProcessId } else { 0 }
            ExpectedParentProcessId = $expectedParentProcessId
            ProcessStartUtc = [string]$observation.ProcessStartUtc
            ExecutablePath = [string]$observation.ExecutablePath
            ExecutableSha256 = (Get-FileHash -LiteralPath ([string]$observation.ExecutablePath) -Algorithm SHA256).Hash.ToUpperInvariant()
            PathEvidenceSource = [string]$observation.PathEvidenceSource
            LaunchStartedUtc = if ($null -eq $launchStartedUtc) { $null } else { $launchStartedUtc.ToString('O') }
            LaunchObservedUtc = if ($null -eq $launchObservedUtc) { $null } else { $launchObservedUtc.ToString('O') }
            RegisteredUtc = $registeredUtc
            VerifiedUtc = [DateTime]::UtcNow.ToString('O')
        }
    }
    catch {
        $message = "Post-start process identity enrichment failed for $RoleType/$RoleInstanceId PID ${ProcessId}: " + $_.Exception.Message
        $wrapped = [System.InvalidOperationException]::new($message, $_.Exception)
        $wrapped.Data['CoopRuntimeOutcome'] = 'RunnerInternalError'
        throw $wrapped
    }
}

function Test-CoopLiveProcessIdentity {
    param([Parameter(Mandatory = $true)]$Identity)

    if ($null -eq $Identity) { return $false }
    return Test-CoopLiveProcessIdentityCore -Identity $Identity
}

function Add-CoopOwnedRuntimeProcess {
    param([Parameter(Mandatory = $true)]$Identity)

    try {
        $incomingProcessId = [int](Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'ProcessId')
        $incomingStartUtc = [string](Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'ProcessStartUtc')
        $incomingLaunchOperationId = [string](Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'LaunchOperationId')
        $replacementIndex = -1
        $duplicate = $false
        for ($index = 0; $index -lt $ownedRuntimeProcesses.Count; $index++) {
            $existing = $ownedRuntimeProcesses[$index]
            $existingLaunchOperationId = [string](Get-CoopOptionalPropertyValue -InputObject $existing -Name 'LaunchOperationId')
            if (-not [string]::IsNullOrWhiteSpace($incomingLaunchOperationId) -and
                [string]::Equals($existingLaunchOperationId, $incomingLaunchOperationId, [StringComparison]::Ordinal)) {
                $replacementIndex = $index
                break
            }
            $existingStartUtc = [string](Get-CoopOptionalPropertyValue -InputObject $existing -Name 'ProcessStartUtc')
            if ([int](Get-CoopOptionalPropertyValue -InputObject $existing -Name 'ProcessId') -eq $incomingProcessId -and
                -not [string]::IsNullOrWhiteSpace($incomingStartUtc) -and
                [string]::Equals($existingStartUtc, $incomingStartUtc, [StringComparison]::Ordinal)) {
                $duplicate = $true
                break
            }
        }
        if ($replacementIndex -ge 0) { $ownedRuntimeProcesses[$replacementIndex] = $Identity }
        elseif (-not $duplicate) { $ownedRuntimeProcesses.Add($Identity) }
        Write-CoopJsonAtomic -Path (Join-Path $runRoot 'artifacts\processes\runtime-owned-processes.json') -Value ([ordered]@{
            Schema = 'coop-runtime-process-inventory-v1'
            RunId = $RunId
            UpdatedUtc = [DateTime]::UtcNow.ToString('O')
            Processes = $ownedRuntimeProcesses.ToArray()
        })
    }
    catch {
        $wrapped = [System.InvalidOperationException]::new(
            'Runtime ownership registration failed: ' + $_.Exception.Message,
            $_.Exception)
        $wrapped.Data['CoopRuntimeOutcome'] = 'RunnerInternalError'
        throw $wrapped
    }
}

function Add-CoopOwnedDescendants {
    param(
        [Parameter(Mandatory = $true)][int[]]$RootProcessIds,
        [ValidateRange(1, 120)][int]$DeadlineSeconds = 30
    )

    $snapshotPath = Join-Path $runRoot 'artifacts\processes\runtime-process-tree-snapshot.json'
    $startedUtc = [DateTime]::UtcNow
    $deadlineUtc = $startedUtc.AddSeconds($DeadlineSeconds)
    $snapshot = @()
    $descendants = @()
    $registered = New-Object System.Collections.Generic.List[object]
    $failures = New-Object System.Collections.Generic.List[object]
    try {
        $snapshot = @(Get-CimInstance -ClassName Win32_Process -OperationTimeoutSec 10 -ErrorAction Stop)
        $descendants = @(Get-CoopDescendantProcessRecordsFromSnapshot `
            -Snapshot $snapshot `
            -RootProcessIds $RootProcessIds `
            -MaximumDescendants 256)

        foreach ($record in $descendants) {
            $candidateId = [int]$record.ProcessId
            if ([DateTime]::UtcNow -ge $deadlineUtc) {
                $failures.Add([ordered]@{
                    ProcessId = $candidateId
                    ParentProcessId = [int]$record.ParentProcessId
                    Reason = "Owned descendant registration exceeded its $DeadlineSeconds-second deadline."
                }) | Out-Null
                break
            }
            try {
                $identity = Get-CoopProcessIdentity `
                    -ProcessId $candidateId `
                    -RoleType 'RuntimeSupport' `
                    -RoleInstanceId ('runtime-support-' + $candidateId) `
                    -ObservedParentProcessId ([int]$record.ParentProcessId)
                if ($null -ne $record.CreationDate) {
                    $observedStartUtc = ([DateTime]$record.CreationDate).ToUniversalTime()
                    $identityStartUtc = [DateTime]::ParseExact(
                        [string]$identity.ProcessStartUtc,
                        'O',
                        [Globalization.CultureInfo]::InvariantCulture,
                        [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
                    if ([Math]::Abs(($identityStartUtc - $observedStartUtc).TotalSeconds) -ge 1.0) {
                        throw "Process $candidateId changed identity after the process-tree snapshot."
                    }
                }
                Add-CoopOwnedRuntimeProcess -Identity $identity
                $registered.Add($identity) | Out-Null
            }
            catch {
                $stillRunning = $null -ne (Get-Process -Id $candidateId -ErrorAction SilentlyContinue)
                if ($stillRunning) {
                    $failures.Add([ordered]@{
                        ProcessId = $candidateId
                        ParentProcessId = [int]$record.ParentProcessId
                        Reason = $_.Exception.Message
                    }) | Out-Null
                }
            }
        }
    }
    catch {
        $failures.Add([ordered]@{
            ProcessId = 0
            ParentProcessId = 0
            Reason = $_.Exception.Message
        }) | Out-Null
    }

    $completedUtc = [DateTime]::UtcNow
    $snapshotReport = [ordered]@{
        Schema = 'coop-runtime-process-tree-snapshot-v1'
        RunId = $RunId
        RootProcessIds = $RootProcessIds
        StartedUtc = $startedUtc.ToString('O')
        CompletedUtc = $completedUtc.ToString('O')
        DurationMilliseconds = [long]($completedUtc - $startedUtc).TotalMilliseconds
        DeadlineSeconds = $DeadlineSeconds
        SnapshotProcessCount = $snapshot.Count
        DescendantCandidateCount = $descendants.Count
        RegisteredDescendantCount = $registered.Count
        RegisteredDescendants = $registered.ToArray()
        Failures = $failures.ToArray()
        Outcome = if ($failures.Count -eq 0) { 'Pass' } else { 'Failed' }
    }
    Write-CoopJsonAtomic -Path $snapshotPath -Value $snapshotReport
    if ($failures.Count -gt 0) {
        throw ('Owned descendant discovery failed: ' + (($failures | ForEach-Object { $_.Reason }) -join '; '))
    }
    return $snapshotReport
}

function Test-CoopProcessDescendsFrom {
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][int]$AncestorProcessId
    )

    $current = $ProcessId
    $visited = New-Object 'System.Collections.Generic.HashSet[int]'
    for ($depth = 0; $depth -lt 16 -and $current -gt 0; $depth++) {
        if ($current -eq $AncestorProcessId) { return $true }
        if (-not $visited.Add($current)) { return $false }
        try {
            $record = Get-CimInstance -ClassName Win32_Process -Filter ("ProcessId=" + $current) -ErrorAction Stop
            if ($null -eq $record) { return $false }
            $current = [int]$record.ParentProcessId
        }
        catch { return $false }
    }
    return $false
}

function Stop-CoopExactProcessIdentity {
    param(
        [Parameter(Mandatory = $true)]$Identity,
        [ValidateRange(1, 60)][int]$GraceSeconds = 15
    )

    $evidence = Stop-CoopExactProcessIdentityCore -Identity $Identity -GraceSeconds $GraceSeconds
    $runtimeCleanupEvidence.Add($evidence)
}

function Stop-CoopOwnedRuntimeProcesses {
    $ordered = @($ownedRuntimeProcesses | Sort-Object {
        if ([string]$_.RoleType -eq 'MultiplayerClient') { 0 }
        elseif ([string]$_.RoleType -eq 'DedicatedServer') { 1 }
        else { 2 }
    })
    foreach ($identity in $ordered) {
        Stop-CoopExactProcessIdentity -Identity $identity
    }
    Write-CoopJsonAtomic -Path (Join-Path $runRoot 'artifacts\processes\runtime-cleanup.json') -Value ([ordered]@{
        Schema = 'coop-runtime-cleanup-v1'
        RunId = $RunId
        CompletedUtc = [DateTime]::UtcNow.ToString('O')
        Processes = $runtimeCleanupEvidence.ToArray()
        RemainingOwnedProcesses = @($ownedRuntimeProcesses | Where-Object { Test-CoopLiveProcessIdentity -Identity $_ })
    })
}

function Copy-CoopPidCorrelatedNativeLogs {
    param(
        [Parameter(Mandatory = $true)]$ProcessIdentity,
        [Parameter(Mandatory = $true)][string]$DestinationRoot
    )

    $sourceRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) `
        'Mount and Blade II Bannerlord\logs'
    if (-not [System.IO.Directory]::Exists($sourceRoot)) {
        throw "Bannerlord native log root does not exist: $sourceRoot"
    }
    [System.IO.Directory]::CreateDirectory($DestinationRoot) | Out-Null

    $processId = [int]$ProcessIdentity.ProcessId
    $processStartUtc = ConvertTo-CoopUtcDateTime -Value (
        Get-CoopOptionalPropertyValue -InputObject $ProcessIdentity -Name 'ProcessStartUtc')
    if ($null -eq $processStartUtc) {
        throw 'Exact process start time is unavailable for PID-correlated native log capture.'
    }
    $files = New-Object 'System.Collections.Generic.List[object]'
    foreach ($descriptor in @(Get-CoopPidCorrelatedNativeLogDescriptors -ProcessId $processId)) {
        $fileName = [string]$descriptor.FileName
        $required = [bool]$descriptor.Required
        $kind = [string]$descriptor.Kind
        $sourcePath = Join-Path $sourceRoot $fileName
        $destinationPath = Join-Path $DestinationRoot $fileName
        if (-not [System.IO.File]::Exists($sourcePath)) {
            if ($required) {
                throw "Required PID-correlated native log is missing: $sourcePath"
            }
            $files.Add([ordered]@{
                FileName = $fileName
                Kind = $kind
                Required = $false
                State = 'NotProduced'
                SourcePath = [System.IO.Path]::GetFullPath($sourcePath)
                DestinationPath = [System.IO.Path]::GetFullPath($destinationPath)
                Length = 0L
                LastWriteTimeUtc = $null
                Sha256 = $null
            }) | Out-Null
            continue
        }
        $sourceItem = Get-Item -LiteralPath $sourcePath
        if ($sourceItem.LastWriteTimeUtc -lt $processStartUtc.AddSeconds(-10)) {
            throw "PID-correlated native log predates the exact dedicated process identity: $sourcePath"
        }

        $copied = $false
        for ($attempt = 0; $attempt -lt 30 -and -not $copied; $attempt++) {
            try {
                [System.IO.File]::Copy($sourcePath, $destinationPath, $true)
                $copied = $true
            }
            catch [System.IO.IOException] {
                if ($attempt -eq 29) { throw }
                Start-Sleep -Milliseconds 100
            }
            catch [System.UnauthorizedAccessException] {
                if ($attempt -eq 29) { throw }
                Start-Sleep -Milliseconds 100
            }
        }
        $destinationItem = Get-Item -LiteralPath $destinationPath
        $files.Add([ordered]@{
            FileName = $fileName
            Kind = $kind
            Required = $required
            State = 'Captured'
            SourcePath = [System.IO.Path]::GetFullPath($sourcePath)
            DestinationPath = [System.IO.Path]::GetFullPath($destinationPath)
            Length = [long]$destinationItem.Length
            LastWriteTimeUtc = $destinationItem.LastWriteTimeUtc.ToString('O')
            Sha256 = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash
        }) | Out-Null
    }

    $inventory = [ordered]@{
        Schema = 'coop-native-log-inventory-v2'
        RunId = $RunId
        RoleType = [string]$ProcessIdentity.RoleType
        RoleInstanceId = [string]$ProcessIdentity.RoleInstanceId
        ProcessId = $processId
        ProcessStartUtc = $processStartUtc.ToString('O')
        SourceRoot = [System.IO.Path]::GetFullPath($sourceRoot)
        CapturedUtc = [DateTime]::UtcNow.ToString('O')
        Files = $files.ToArray()
    }
    Write-CoopJsonAtomic -Path (Join-Path $DestinationRoot 'inventory.json') -Value $inventory
    return $inventory
}

function Write-CoopRuntimeFailureEvidence {
    param(
        [Parameter(Mandatory = $true)][ValidateSet('Crash', 'Timeout')][string]$Outcome,
        [Parameter(Mandatory = $true)][string]$FailureCode,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    $rootProcessIds = @($ownedRuntimeProcesses | Where-Object {
        [string]$_.RoleType -eq 'DedicatedServer' -or [string]$_.RoleType -eq 'MultiplayerClient'
    } | ForEach-Object { [int]$_.ProcessId } | Select-Object -Unique)
    $snapshot = @()
    $correlatedFailureProcesses = @()
    $snapshotFailure = ''
    try {
        $snapshot = @(Get-CimInstance -ClassName Win32_Process -OperationTimeoutSec 10 -ErrorAction Stop)
        $allowedPaths = @(
            (Join-Path $GameRoot 'bin\CrashUploader.Publish\CrashUploader.Publish.exe'),
            (Join-Path $GameRoot 'bin\Win64_Shipping_Client\Watchdog\Watchdog.exe'),
            (Join-Path $DedicatedServerRoot 'bin\CrashUploader.Publish\CrashUploader.Publish.exe'),
            (Join-Path $DedicatedServerRoot 'bin\Win64_Shipping_Server\Watchdog\Watchdog.exe'),
            (Join-Path $env:SystemRoot 'System32\WerFault.exe'))
        if ($rootProcessIds.Count -gt 0) {
            $correlatedFailureProcesses = @(Get-CoopCorrelatedFailureProcessesFromSnapshot `
                -Snapshot $snapshot `
                -OwnedRootProcessIds $rootProcessIds `
                -AllowedExecutablePaths $allowedPaths)
        }
    }
    catch { $snapshotFailure = $_.Exception.Message }

    $roleStatuses = @(
        Read-CoopJsonShared -Path (Join-Path $runRoot 'status\dedicated-server-01.json')
        Read-CoopJsonShared -Path (Join-Path $runRoot 'status\multiplayer-client-01.json')
    ) | Where-Object { $null -ne $_ }
    $lastRoleStatus = @($roleStatuses | Sort-Object {
        $value = ConvertTo-CoopUtcDateTime -Value $_.UpdatedUtc
        if ($null -eq $value) { [DateTime]::MinValue } else { $value }
    } -Descending | Select-Object -First 1)
    $eventTail = @()
    if ([System.IO.File]::Exists($eventsPath)) {
        $eventTail = @(Get-Content -LiteralPath $eventsPath -Tail 25 -ErrorAction SilentlyContinue)
    }
    $correlationOwnershipFailures = New-Object 'System.Collections.Generic.List[object]'
    foreach ($record in $correlatedFailureProcesses) {
        try {
            $identity = Get-CoopProcessIdentity `
                -ProcessId ([int]$record.ProcessId) `
                -RoleType 'RuntimeFailureSupport' `
                -RoleInstanceId ('runtime-failure-support-' + [int]$record.ProcessId) `
                -ExpectedExecutablePath ([string]$record.ExecutablePath) `
                -ObservedParentProcessId ([int]$record.ParentProcessId)
            Add-CoopOwnedRuntimeProcess -Identity $identity
        }
        catch {
            $correlationOwnershipFailures.Add([pscustomobject][ordered]@{
                ProcessId = [int]$record.ProcessId
                ExecutablePath = [string]$record.ExecutablePath
                Failure = $_.Exception.Message
            }) | Out-Null
        }
    }
    $effectiveOutcome = if ($correlatedFailureProcesses.Count -gt 0) { 'Crash' } else { $Outcome }
    $effectiveCode = if ($correlatedFailureProcesses.Count -gt 0) { 'CrashReporterDetected' }
        elseif ([string]::IsNullOrWhiteSpace($FailureCode)) {
            if ($Outcome -eq 'Crash') { 'FatalAutomationFailure' } else { 'DeadlineExceeded' }
        }
        else { $FailureCode }
    $evidence = [ordered]@{
        SchemaVersion = 1
        ProtocolMajorVersion = 1
        ProtocolMinorVersion = 1
        RunId = $RunId
        Outcome = $effectiveOutcome
        FailureCode = $effectiveCode
        FailureMessage = $FailureMessage
        LastRoleState = if ($lastRoleStatus.Count -eq 1) { [string]$lastRoleStatus[0].State } else { '' }
        LastStateRevision = if ($lastRoleStatus.Count -eq 1) { [long]$lastRoleStatus[0].StateRevision } else { 0L }
        LastHeartbeatUtc = if ($lastRoleStatus.Count -eq 1) { [string]$lastRoleStatus[0].HeartbeatUtc } else { $null }
        LastProgressUtc = if ($lastRoleStatus.Count -eq 1) { [string]$lastRoleStatus[0].LastProgressUtc } else { $null }
        RoleStatuses = $roleStatuses
        LastEvents = $eventTail
        OwnedProcessIdentities = $ownedRuntimeProcesses.ToArray()
        CorrelatedFailureProcesses = $correlatedFailureProcesses
        CorrelationOwnershipFailures = $correlationOwnershipFailures.ToArray()
        ProcessSnapshotFailure = $snapshotFailure
        DumpAttemptState = 'NotAttemptedNoConfiguredDumpCollector'
        CapturedUtc = [DateTime]::UtcNow.ToString('O')
    }
    $fileName = if ($effectiveOutcome -eq 'Crash') { 'crash.json' } else { 'hang.json' }
    $path = Join-Path $runRoot ('artifacts\crashes\' + $fileName)
    Write-CoopJsonAtomic -Path $path -Value $evidence
    return [pscustomobject]@{ Path = $path; Evidence = [pscustomobject]$evidence }
}

function Assert-CoopRuntimeRoleHealth {
    param(
        [Parameter(Mandatory = $true)][string]$StatusPath,
        [Parameter(Mandatory = $true)][string]$ExpectedRoleType,
        [Parameter(Mandatory = $true)][string]$ExpectedRoleInstanceId,
        [ValidateRange(1, 3600)][int]$HeartbeatDeadlineSeconds = 5,
        [ValidateRange(1, 86400)][int]$ProgressDeadlineSeconds = 180
    )

    $status = Read-CoopJsonShared -Path $StatusPath
    $classification = Get-CoopRoleHealthClassificationCore `
        -Status $status `
        -NowUtc ([DateTime]::UtcNow) `
        -HeartbeatDeadlineSeconds $HeartbeatDeadlineSeconds `
        -ProgressDeadlineSeconds $ProgressDeadlineSeconds
    if ($null -ne $status) {
        if (-not (@($status.Capabilities) -contains 'RoleHealthV1')) {
            throw "RoleHealthV1 capability is missing: $StatusPath"
        }
        if (-not [string]::Equals([string]$status.RunId, $RunId, [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$status.RunTokenSha256, $nonceSha256, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals([string]$status.RoleType, $ExpectedRoleType, [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$status.RoleInstanceId, $ExpectedRoleInstanceId, [StringComparison]::Ordinal)) {
            throw "RoleHealthV1 identity mismatch: $StatusPath"
        }
    }
    if ($classification -ne 'Healthy') {
        $failure = [System.TimeoutException]::new("$classification for runtime role $ExpectedRoleInstanceId.")
        $failure.Data['CoopRuntimeOutcome'] = 'Timeout'
        $failure.Data['CoopFailureCode'] = $classification
        if ($null -ne $status) { $failure.Data['CoopRoleHealthStatus'] = $status }
        throw $failure
    }
    return $status
}

function Wait-CoopRuntimeRoleReady {
    param(
        [Parameter(Mandatory = $true)][string]$StatusPath,
        [Parameter(Mandatory = $true)][string]$ExpectedRoleType,
        [Parameter(Mandatory = $true)][string]$ExpectedRoleInstanceId,
        [Parameter(Mandatory = $true)][string]$ExpectedModuleSha256,
        [Parameter(Mandatory = $true)][DateTime]$DeadlineUtc,
        $ProcessTextCapture
    )

    while ([DateTime]::UtcNow -lt $DeadlineUtc) {
        if ($null -ne $ProcessTextCapture) {
            Update-CoopProcessTextCapture -Capture $ProcessTextCapture
        }
        $status = Read-CoopJsonShared -Path $StatusPath
        if ($null -ne $status) {
            $null = Assert-CoopRuntimeRoleHealth `
                -StatusPath $StatusPath `
                -ExpectedRoleType $ExpectedRoleType `
                -ExpectedRoleInstanceId $ExpectedRoleInstanceId
            if ([string]$status.RunId -ne $RunId) { throw "Runtime role status RunId mismatch: $StatusPath" }
            if ([string]$status.RunTokenSha256 -ne $nonceSha256) { throw "Runtime role status token mismatch: $StatusPath" }
            if ([string]$status.RoleType -ne $ExpectedRoleType) { throw "Runtime role type mismatch: $StatusPath" }
            if ([string]$status.State -eq 'Failed') {
                throw ("Runtime role rejected configuration: " + [string]$status.FailureCode + ': ' + [string]$status.FailureMessage)
            }
            if ([string]$status.State -eq 'ModuleReady') {
                if (-not [string]::Equals(
                        ([string]$status.ModuleSha256).ToUpperInvariant(),
                        $ExpectedModuleSha256.ToUpperInvariant(),
                        [StringComparison]::Ordinal)) {
                    throw "Runtime role loaded module hash mismatch: $StatusPath"
                }
                return $status
            }
        }
        Update-CoopLease
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for runtime role status: $StatusPath"
}

function Wait-CoopDedicatedControlReady {
    param(
        [Parameter(Mandatory = $true)][string]$StatusPath,
        [Parameter(Mandatory = $true)][string]$RoleStatusPath,
        [Parameter(Mandatory = $true)][string]$ExpectedModuleSha256,
        [Parameter(Mandatory = $true)]$DedicatedIdentity,
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$DedicatedProcess,
        [Parameter(Mandatory = $true)][DateTime]$DeadlineUtc,
        $ProcessTextCapture
    )

    $expectedStartUtc = [DateTime]::Parse(
        [string]$DedicatedIdentity.ProcessStartUtc,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    while ([DateTime]::UtcNow -lt $DeadlineUtc) {
        $null = Assert-CoopRuntimeRoleHealth `
            -StatusPath $RoleStatusPath `
            -ExpectedRoleType 'DedicatedServer' `
            -ExpectedRoleInstanceId 'dedicated-server-01'
        if ($null -ne $ProcessTextCapture) {
            Update-CoopProcessTextCapture -Capture $ProcessTextCapture
        }
        if ($DedicatedProcess.HasExited) {
            throw 'Dedicated server exited before publishing the authoritative control readiness acknowledgement.'
        }
        $status = Read-CoopJsonShared -Path $StatusPath
        if ($null -ne $status) {
            $validated = Assert-CoopDedicatedControlReadyStatus `
                -Status $status `
                -ExpectedRunId $RunId `
                -ExpectedRunTokenSha256 $nonceSha256 `
                -ExpectedDedicatedModuleSha256 $ExpectedModuleSha256 `
                -ExpectedProcessId ([int]$DedicatedIdentity.ProcessId) `
                -ExpectedProcessStartUtc $expectedStartUtc `
                -ExpectedExecutablePath ([string]$DedicatedIdentity.ExecutablePath)
            return $validated
        }
        Update-CoopLease
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for the run-scoped dedicated control readiness acknowledgement: $StatusPath"
}

function Wait-CoopDedicatedBootstrapAccepted {
    param(
        [Parameter(Mandatory = $true)][string]$StatusPath,
        [Parameter(Mandatory = $true)][string]$RoleStatusPath,
        [Parameter(Mandatory = $true)]$Request,
        [Parameter(Mandatory = $true)][string]$ExpectedModuleSha256,
        [Parameter(Mandatory = $true)]$DedicatedIdentity,
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$DedicatedProcess,
        [Parameter(Mandatory = $true)][DateTime]$DeadlineUtc,
        $ProcessTextCapture
    )

    $expectedStartUtc = [DateTime]::Parse(
        [string]$DedicatedIdentity.ProcessStartUtc,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    while ([DateTime]::UtcNow -lt $DeadlineUtc) {
        $null = Assert-CoopRuntimeRoleHealth `
            -StatusPath $RoleStatusPath `
            -ExpectedRoleType 'DedicatedServer' `
            -ExpectedRoleInstanceId 'dedicated-server-01'
        if ($null -ne $ProcessTextCapture) {
            Update-CoopProcessTextCapture -Capture $ProcessTextCapture
        }
        if ($DedicatedProcess.HasExited) {
            throw 'Dedicated server exited before terminal bootstrap acknowledgement.'
        }
        $status = Read-CoopJsonShared -Path $StatusPath
        if ($null -ne $status) {
            $accepted = Confirm-CoopDedicatedBootstrapStatus `
                -Status $status `
                -Request $Request `
                -ExpectedRunId $RunId `
                -ExpectedRunTokenSha256 $nonceSha256 `
                -ExpectedDedicatedModuleSha256 $ExpectedModuleSha256 `
                -ExpectedProcessId ([int]$DedicatedIdentity.ProcessId) `
                -ExpectedProcessStartUtc $expectedStartUtc `
                -ExpectedExecutablePath ([string]$DedicatedIdentity.ExecutablePath)
            if ($accepted) { return $status }
        }
        Update-CoopLease
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for the run-scoped dedicated bootstrap acknowledgement: $StatusPath"
}

function Wait-CoopClientConnection {
    param(
        [Parameter(Mandatory = $true)][string]$StatusPath,
        [Parameter(Mandatory = $true)][string]$RoleStatusPath,
        [Parameter(Mandatory = $true)][DateTime]$DeadlineUtc,
        $ProcessTextCapture
    )

    $lastValidatedStatus = $null
    while ([DateTime]::UtcNow -lt $DeadlineUtc) {
        $null = Assert-CoopRuntimeRoleHealth `
            -StatusPath $RoleStatusPath `
            -ExpectedRoleType 'MultiplayerClient' `
            -ExpectedRoleInstanceId 'multiplayer-client-01'
        if ($null -ne $ProcessTextCapture) {
            Update-CoopProcessTextCapture -Capture $ProcessTextCapture
        }
        $status = Read-CoopJsonShared -Path $StatusPath
        if ($null -ne $status) {
            if ([string]$status.RunId -ne $RunId) { throw 'Client join status RunId mismatch.' }
            if ([string]$status.RunTokenSha256 -ne $nonceSha256) { throw 'Client join status token mismatch.' }
            $lastValidatedStatus = $status
            if ([string]$status.State -eq 'Failed') {
                $failure = [System.InvalidOperationException]::new(
                    "Client join failed: " + [string]$status.FailureCode + ': ' + [string]$status.FailureMessage)
                $failure.Data['CoopClientJoinStatus'] = $status
                throw $failure
            }
            if ([string]$status.State -eq 'Cancelled') {
                $failure = [System.InvalidOperationException]::new(
                    "Client join cancelled: " + [string]$status.FailureCode + ': ' + [string]$status.FailureMessage)
                $failure.Data['CoopClientJoinStatus'] = $status
                throw $failure
            }
            if ([string]$status.State -eq 'Connected') { return $status }
        }
        Update-CoopLease
        Start-Sleep -Milliseconds 250
    }
    $timeout = [System.TimeoutException]::new('Timed out waiting for the multiplayer client to connect.')
    if ($null -ne $lastValidatedStatus) {
        $timeout.Data['CoopClientJoinStatus'] = $lastValidatedStatus
    }
    throw $timeout
}

function Test-CoopWritableDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $probePath = Join-Path $Path ('.coop-write-probe-' + [Guid]::NewGuid().ToString('N'))
    try {
        [System.IO.Directory]::CreateDirectory($Path) | Out-Null
        [System.IO.File]::WriteAllText($probePath, 'probe', (New-Object System.Text.UTF8Encoding($false)))
        [System.IO.File]::Delete($probePath)
        return [ordered]@{ Path = [System.IO.Path]::GetFullPath($Path); Writable = $true; Failure = '' }
    }
    catch {
        try { if ([System.IO.File]::Exists($probePath)) { [System.IO.File]::Delete($probePath) } } catch { }
        return [ordered]@{ Path = [System.IO.Path]::GetFullPath($Path); Writable = $false; Failure = $_.Exception.Message }
    }
}

function Test-CoopEnvironmentFlag {
    param([Parameter(Mandatory = $true)][string]$Name)

    $value = [Environment]::GetEnvironmentVariable($Name, 'Process')
    return $value -in @('1', 'true', 'TRUE', 'yes', 'YES', 'on', 'ON')
}

function New-CoopAssertionRecord {
    param(
        [Parameter(Mandatory = $true)][string]$AssertionId,
        [Parameter(Mandatory = $true)][string]$RequiredEvidenceLevel,
        [Parameter(Mandatory = $true)][string]$ExpectedFact,
        [Parameter(Mandatory = $true)][string]$ObservedFact,
        [Parameter(Mandatory = $true)][string]$AuthoritativeSource,
        [Parameter(Mandatory = $true)][long]$FirstRelevantEventSequence,
        [Parameter(Mandatory = $true)][long]$LastRelevantEventSequence,
        [Parameter(Mandatory = $true)][string]$Outcome,
        [string[]]$ArtifactLinks = @()
    )

    return [ordered]@{
        AssertionId = $AssertionId
        RequiredEvidenceLevel = $RequiredEvidenceLevel
        ExpectedFact = $ExpectedFact
        ObservedFact = $ObservedFact
        SourceRoleType = $runnerRoleType
        SourceRoleInstanceId = $runnerRoleInstanceId
        AuthoritativeSource = $AuthoritativeSource
        FirstRelevantEventSequence = $FirstRelevantEventSequence
        LastRelevantEventSequence = $LastRelevantEventSequence
        Outcome = $Outcome
        ArtifactLinks = $ArtifactLinks
    }
}

function Write-CoopReproductionDescriptor {
    param(
        [Parameter(Mandatory = $true)][string]$Outcome,
        [Parameter(Mandatory = $true)][string]$Reason
    )

    $retryPrefixLength = [Math]::Min(69, $RunId.Length)
    $suggestedRetryRunId = $RunId.Substring(0, $retryPrefixLength).TrimEnd([char[]]@('.', '_', '-')) + '-retry-01'
    $arguments = @(
        '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
        '-File', '.\scripts\Invoke-CoopTest.ps1',
        '-Command', $Command,
        '-RunId', $suggestedRetryRunId,
        '-MachineProfileName', $MachineProfileName,
        '-GameRoot', $GameRoot,
        '-DedicatedServerRoot', $DedicatedServerRoot
    )
    if ($All) { $arguments += '-All' }
    $suggestedCommand = 'powershell.exe ' + (($arguments | ForEach-Object {
        ConvertTo-CoopCommandLineArgument -Value $_
    }) -join ' ')
    $descriptorPath = Join-Path $runRoot 'artifacts\results\reproduction.json'
    Write-CoopJsonAtomic -Path $descriptorPath -Value ([ordered]@{
        Schema = 'coop-reproduction-v1'
        OriginalRunId = $RunId
        FirstAttemptOutcome = $Outcome
        FirstAttemptReason = $Reason
        SuggestedRetryRunId = $suggestedRetryRunId
        SuggestedCommand = $suggestedCommand
        MachineProfileName = $MachineProfileName
        BuildProfile = $manifest.BuildProfile
        ExpectedArtifactSource = $manifest.ExpectedArtifactSource
        RepositoryRevision = $manifest.RepositoryRevision
        RepositoryDirty = $manifest.RepositoryDirty
        ClientModuleVersion = $manifest.ClientModuleVersion
        ClientModuleSha256 = $manifest.ClientModuleSha256
        DedicatedModuleVersion = $manifest.DedicatedModuleVersion
        DedicatedModuleSha256 = $manifest.DedicatedModuleSha256
        ContainsPlaintextNonce = $false
        ContainsCredential = $false
        AutomaticRetryPerformed = $false
    })
    return $descriptorPath
}

function Get-CoopDirectoryInventory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if (-not [System.IO.Directory]::Exists($fullPath)) {
        return [ordered]@{ Path = $fullPath; Exists = $false; Entries = @() }
    }

    $entries = @(
        Get-ChildItem -LiteralPath $fullPath -File -Recurse -Force | Sort-Object FullName | ForEach-Object {
            [ordered]@{
                RelativePath = $_.FullName.Substring($fullPath.Length).TrimStart('\', '/')
                Length = $_.Length
                LastWriteUtc = $_.LastWriteTimeUtc.ToString('O')
                Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
            }
        }
    )
    return [ordered]@{ Path = $fullPath; Exists = $true; Entries = $entries }
}

function Test-CoopInventoryEqual {
    param($Before, $After)

    $beforeCanonical = $Before | ConvertTo-Json -Depth 20 -Compress
    $afterCanonical = $After | ConvertTo-Json -Depth 20 -Compress
    return [string]::Equals($beforeCanonical, $afterCanonical, [StringComparison]::Ordinal)
}

function ConvertTo-CoopCommandLineArgument {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)

    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') {
        return $Value
    }

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    $backslashCount = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') {
            $backslashCount++
            continue
        }

        if ($character -eq '"') {
            if ($backslashCount -gt 0) {
                [void]$builder.Append(('\' * ($backslashCount * 2)))
            }
            [void]$builder.Append('\')
            [void]$builder.Append('"')
        }
        else {
            if ($backslashCount -gt 0) {
                [void]$builder.Append(('\' * $backslashCount))
            }
            [void]$builder.Append($character)
        }
        $backslashCount = 0
    }

    if ($backslashCount -gt 0) {
        [void]$builder.Append(('\' * ($backslashCount * 2)))
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-CoopExternal {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    Update-CoopLease
    $startedUtc = [DateTime]::UtcNow
    $exitCode = $outcomeExitCodes.RunnerInternalError
    $commandLineArguments = (($Arguments | ForEach-Object {
        ConvertTo-CoopCommandLineArgument -Value $_
    }) -join ' ')
    $standardOutput = ''
    $standardError = ''
    $process = New-Object System.Diagnostics.Process
    $processStarted = $false
    try {
        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = $Executable
        $startInfo.Arguments = $commandLineArguments
        $startInfo.WorkingDirectory = $repositoryRoot
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $process.StartInfo = $startInfo
        if (-not $process.Start()) {
            throw "Could not start external command: $Executable"
        }
        $processStarted = $true

        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        while (-not $process.WaitForExit(500)) {
            Update-CoopLease
        }
        $standardOutput = $standardOutputTask.Result
        $standardError = $standardErrorTask.Result
        $exitCode = $process.ExitCode
    }
    finally {
        if ($processStarted -and -not $process.HasExited -and
            ((Test-CoopConsoleCancellationRequestedCore) -or [System.IO.File]::Exists($cancellationRequestPath))) {
            try { $process.Kill() } catch { }
        }
        $process.Dispose()
    }
    $endedUtc = [DateTime]::UtcNow
    $directory = Split-Path -Parent $LogPath
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $standardOutputLogPath = $LogPath + '.stdout.txt'
    $standardErrorLogPath = $LogPath + '.stderr.txt'
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($standardOutputLogPath, $standardOutput, $utf8WithoutBom)
    [System.IO.File]::WriteAllText($standardErrorLogPath, $standardError, $utf8WithoutBom)
    $logText = $standardOutput
    if (-not [string]::IsNullOrWhiteSpace($standardError)) {
        if (-not [string]::IsNullOrEmpty($logText) -and -not $logText.EndsWith("`n")) {
            $logText += [Environment]::NewLine
        }
        $logText += '[stderr]' + [Environment]::NewLine + $standardError
    }
    [System.IO.File]::WriteAllText($LogPath, $logText, $utf8WithoutBom)
    Update-CoopLease

    return [ordered]@{
        Command = $Executable + ' ' + $commandLineArguments
        StartedUtc = $startedUtc.ToString('O')
        EndedUtc = $endedUtc.ToString('O')
        DurationMilliseconds = [long]($endedUtc - $startedUtc).TotalMilliseconds
        ExitCode = $exitCode
        LogPath = $LogPath
        StandardOutputLogPath = $standardOutputLogPath
        StandardErrorLogPath = $standardErrorLogPath
    }
}

function Resolve-CoopEnvironmentRoots {
    if ([string]::IsNullOrWhiteSpace($GameRoot)) {
        $configuredGameRoot = [Environment]::GetEnvironmentVariable('BANNERLORD_GAME_ROOT', 'Process')
        $script:GameRoot = if ([string]::IsNullOrWhiteSpace($configuredGameRoot)) {
            'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
        }
        else { $configuredGameRoot }
    }
    if ([string]::IsNullOrWhiteSpace($DedicatedServerRoot)) {
        $configuredDedicatedRoot = [Environment]::GetEnvironmentVariable('BANNERLORD_DEDICATED_ROOT', 'Process')
        $script:DedicatedServerRoot = if ([string]::IsNullOrWhiteSpace($configuredDedicatedRoot)) {
            'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server'
        }
        else { $configuredDedicatedRoot }
    }

    $script:GameRoot = [System.IO.Path]::GetFullPath($GameRoot).TrimEnd('\', '/')
    $script:DedicatedServerRoot = [System.IO.Path]::GetFullPath($DedicatedServerRoot).TrimEnd('\', '/')
}

function Invoke-CoopDoctor {
    Resolve-CoopEnvironmentRoots
    Add-CoopEvent -EventType 'DoctorStarted' -Message 'Environment inspection started without product process launch.'
    $doctorStartedSequence = $eventSequence
    Write-CoopRunnerStatus -State 'Doctor' -Outcome '' -Reason 'Inspecting environment.'

    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    $steamProcesses = @(Get-Process -Name steam -ErrorAction SilentlyContinue | ForEach-Object {
        [ordered]@{ ProcessId = $_.Id; StartUtc = $_.StartTime.ToUniversalTime().ToString('O') }
    })
    $hygieneScript = Join-Path $repositoryRoot 'scripts\Test-RepositoryHygiene.ps1'
    $hygieneLog = Join-Path $runRoot 'artifacts\logs\doctor\repository-hygiene.log'
    $hygiene = if ([System.IO.File]::Exists($hygieneScript)) {
        Invoke-CoopExternal -Executable $runnerProcess.Path -Arguments @(
            '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
            '-File', $hygieneScript, '-AllowDirty') -LogPath $hygieneLog
    }
    else {
        [ordered]@{
            Command = ''
            ExitCode = $outcomeExitCodes.PreconditionsFailed
            LogPath = $hygieneLog
            StandardOutputLogPath = $hygieneLog + '.stdout.txt'
            StandardErrorLogPath = $hygieneLog + '.stderr.txt'
        }
    }

    $gameExecutable = Join-Path $GameRoot 'bin\Win64_Shipping_Client\Bannerlord.exe'
    $launcherExecutable = Join-Path $GameRoot 'bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Launcher.exe'
    $dedicatedExecutable = Join-Path $DedicatedServerRoot 'bin\Win64_Shipping_Server\DedicatedCustomServer.Starter.exe'
    $installedClientModule = Join-Path $GameRoot 'Modules\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll'
    $installedDedicatedModule = Join-Path $DedicatedServerRoot 'Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Server\CoopSpectator.dll'
    $installedDedicatedLoadedCandidate = Join-Path $DedicatedServerRoot 'Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client\CoopSpectator.dll'
    $sourceClientModule = Join-Path $repositoryRoot 'Module\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll'
    $sourceDedicatedModule = Join-Path $repositoryRoot 'Module\CoopSpectatorDedicated\bin\Win64_Shipping_Server\CoopSpectator.dll'
    $clientDependencies = @(
        Get-CoopFileFact -Path (Join-Path $GameRoot 'bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.dll')
        Get-CoopFileFact -Path (Join-Path $GameRoot 'bin\Win64_Shipping_Client\TaleWorlds.CampaignSystem.dll')
        Get-CoopFileFact -Path (Join-Path $GameRoot 'bin\Win64_Shipping_Client\Newtonsoft.Json.dll')
    )
    $dedicatedMultiplayerCandidates = @(
        Get-CoopFileFact -Path (Join-Path $DedicatedServerRoot 'Modules\Multiplayer\bin\Win64_Shipping_Server\TaleWorlds.MountAndBlade.Multiplayer.dll')
        Get-CoopFileFact -Path (Join-Path $DedicatedServerRoot 'bin\Win64_Shipping_Server\TaleWorlds.MountAndBlade.Multiplayer.dll')
    )
    $selectedDedicatedMultiplayer = @($dedicatedMultiplayerCandidates | Where-Object { $_.Exists } | Select-Object -First 1)
    $dedicatedDependencies = @(
        Get-CoopFileFact -Path (Join-Path $DedicatedServerRoot 'bin\Win64_Shipping_Server\TaleWorlds.MountAndBlade.dll')
        if ($selectedDedicatedMultiplayer.Count -gt 0) { $selectedDedicatedMultiplayer[0] } else { $dedicatedMultiplayerCandidates[0] }
    )
    $portOwnership = Get-CoopPortOwnership
    $runRootWrite = Test-CoopWritableDirectory -Path $runRoot
    $artifactRootWrite = Test-CoopWritableDirectory -Path (Join-Path $runRoot 'artifacts')

    $facts = [ordered]@{
        Schema = 'coop-environment-doctor-v1'
        RunId = $RunId
        MachineProfileName = $MachineProfileName
        ObservedUtc = [DateTime]::UtcNow.ToString('O')
        RepositoryRoot = $repositoryRoot
        RepositoryRevision = $manifest.RepositoryRevision
        RepositoryDirty = $manifest.RepositoryDirty
        RepositoryHygiene = $hygiene
        SourceCompletionEligible = ($hygiene.ExitCode -eq 0 -and -not $manifest.RepositoryDirty)
        OperatingSystem = [Environment]::OSVersion.VersionString
        Is64BitOperatingSystem = [Environment]::Is64BitOperatingSystem
        DotnetPath = if ($null -eq $dotnetCommand) { '' } else { $dotnetCommand.Source }
        BuildProfile = 'Release'
        ExpectedArtifactSource = 'RepositoryCompileOnly'
        GameRoot = $GameRoot
        DedicatedServerRoot = $DedicatedServerRoot
        GameExecutable = Get-CoopFileFact -Path $gameExecutable
        LauncherExecutable = Get-CoopFileFact -Path $launcherExecutable
        DedicatedExecutable = Get-CoopFileFact -Path $dedicatedExecutable
        InstalledClientModule = Get-CoopFileFact -Path $installedClientModule
        InstalledDedicatedModule = Get-CoopFileFact -Path $installedDedicatedModule
        InstalledDedicatedLoadedPathCandidate = Get-CoopFileFact -Path $installedDedicatedLoadedCandidate
        RepositoryClientOutput = Get-CoopFileFact -Path $sourceClientModule
        RepositoryDedicatedOutput = Get-CoopFileFact -Path $sourceDedicatedModule
        SelectedClientDependencies = $clientDependencies
        SelectedDedicatedDependencies = $dedicatedDependencies
        DedicatedMultiplayerDependencyCandidates = $dedicatedMultiplayerCandidates
        SteamProcesses = $steamProcesses
        RequiredPortOwnership = $portOwnership
        RunRootWriteProbe = $runRootWrite
        ArtifactRootWriteProbe = $artifactRootWrite
        ConflictingRunIdOwnerDetected = $false
        EffectiveFeatureFlags = [ordered]@{
            TestAutomation = Test-CoopEnvironmentFlag -Name 'COOPSPECTATOR_TEST_AUTOMATION'
            VerboseDiagnostics = Test-CoopEnvironmentFlag -Name 'COOPSPECTATOR_VERBOSE_DIAGNOSTICS'
            CampaignMapPrototype = Test-CoopEnvironmentFlag -Name 'COOPSPECTATOR_CAMPAIGN_MAP_PROTOTYPE'
        }
        VersionMatrixStatus = 'UnverifiedClientDedicatedCombination'
        ProductProcessLaunched = $false
    }

    $blockers = New-Object System.Collections.Generic.List[string]
    $warnings = New-Object System.Collections.Generic.List[string]
    if ($null -eq $dotnetCommand) { $blockers.Add('DotnetSdkMissing') }
    if ($hygiene.ExitCode -ne 0) { $blockers.Add('RepositoryLineEndingOrGitPolicyInvalid') }
    if ($manifest.RepositoryDirty) { $warnings.Add('RepositoryDirtyForSourceCompletion') }
    if (-not $facts.GameExecutable.Exists) { $blockers.Add('GameExecutableMissing') }
    if (-not $facts.LauncherExecutable.Exists) { $blockers.Add('LauncherExecutableMissing') }
    if (-not $facts.DedicatedExecutable.Exists) { $blockers.Add('DedicatedExecutableMissing') }
    if (-not $facts.InstalledClientModule.Exists) { $blockers.Add('InstalledClientModuleMissing') }
    if (-not $facts.InstalledDedicatedModule.Exists) { $blockers.Add('InstalledDedicatedModuleMissing') }
    if (@($clientDependencies | Where-Object { -not $_.Exists }).Count -gt 0) { $blockers.Add('RequiredClientDependencyMissing') }
    if (@($dedicatedDependencies | Where-Object { -not $_.Exists }).Count -gt 0) { $blockers.Add('RequiredDedicatedDependencyMissing') }
    if ($steamProcesses.Count -eq 0) { $blockers.Add('SteamNotRunningForFutureClientLaunch') }
    if (-not $portOwnership.InspectionAvailable) { $blockers.Add('RequiredPortInspectionUnavailable') }
    if (@($portOwnership.Entries).Count -gt 0) { $blockers.Add('RequiredPortAlreadyOwned') }
    if (-not $runRootWrite.Writable -or -not $artifactRootWrite.Writable) { $blockers.Add('RunArtifactRootNotWritable') }
    if ($facts.InstalledClientModule.Exists -and $facts.RepositoryClientOutput.Exists -and
        -not [string]::Equals($facts.InstalledClientModule.Sha256, $facts.RepositoryClientOutput.Sha256, [StringComparison]::Ordinal)) {
        $blockers.Add('InstalledClientHashDiffersFromRepositoryOutput')
    }
    if ($facts.InstalledDedicatedModule.Exists -and $facts.RepositoryDedicatedOutput.Exists -and
        -not [string]::Equals($facts.InstalledDedicatedModule.Sha256, $facts.RepositoryDedicatedOutput.Sha256, [StringComparison]::Ordinal)) {
        $blockers.Add('InstalledDedicatedHashDiffersFromRepositoryOutput')
    }
    $blockers.Add('RuntimeVersionCombinationNotYetVerified')
    $facts['Blockers'] = @($blockers)
    $facts['Warnings'] = @($warnings)

    $doctorPath = Join-Path $runRoot 'artifacts\identity\environment-doctor.json'
    Add-CoopEvent -EventType 'DoctorCompleted' -Message ('Environment inspection completed with ' + $blockers.Count + ' runtime blocker(s).')
    $doctorCompletedSequence = $eventSequence
    $identityBlockers = @($blockers | Where-Object { $_ -match 'Missing|HashDiffers|VersionCombination' })
    $portBlockers = @($blockers | Where-Object { $_ -match 'Port' })
    $facts['Assertions'] = @(
        New-CoopAssertionRecord -AssertionId 'M2A.L0.REPOSITORY_HYGIENE' -RequiredEvidenceLevel 'L0' `
            -ExpectedFact 'Repository-local Git and line-ending policy is valid.' `
            -ObservedFact ('HygieneExitCode=' + $hygiene.ExitCode + '; RepositoryDirty=' + $manifest.RepositoryDirty) `
            -AuthoritativeSource 'scripts/Test-RepositoryHygiene.ps1' `
            -FirstRelevantEventSequence $doctorStartedSequence -LastRelevantEventSequence $doctorCompletedSequence `
            -Outcome $(if ($hygiene.ExitCode -eq 0) { 'Pass' } else { 'PreconditionsFailed' }) `
            -ArtifactLinks @($hygiene.LogPath, $hygiene.StandardOutputLogPath, $hygiene.StandardErrorLogPath)
        New-CoopAssertionRecord -AssertionId 'M2A.L0.BINARY_AND_VERSION_IDENTITY' -RequiredEvidenceLevel 'L0' `
            -ExpectedFact 'Required executables, modules, and dependencies exist and the explicit version matrix is supported.' `
            -ObservedFact $(if ($identityBlockers.Count -eq 0) { 'All required identities exist and match the supported matrix.' } else { $identityBlockers -join ', ' }) `
            -AuthoritativeSource 'FileVersionInfo plus SHA-256 inventory' `
            -FirstRelevantEventSequence $doctorStartedSequence -LastRelevantEventSequence $doctorCompletedSequence `
            -Outcome $(if ($identityBlockers.Count -eq 0) { 'Pass' } else { 'EnvironmentBlocked' }) `
            -ArtifactLinks @($doctorPath)
        New-CoopAssertionRecord -AssertionId 'M2A.L0.PORT_OWNERSHIP' -RequiredEvidenceLevel 'L0' `
            -ExpectedFact 'Required ports 7210 and 7777 are inspectable and unowned.' `
            -ObservedFact $(if ($portBlockers.Count -eq 0) { 'Required ports are inspectable and unowned.' } else { $portBlockers -join ', ' }) `
            -AuthoritativeSource 'Get-NetTCPConnection/Get-NetUDPEndpoint' `
            -FirstRelevantEventSequence $doctorStartedSequence -LastRelevantEventSequence $doctorCompletedSequence `
            -Outcome $(if ($portBlockers.Count -eq 0) { 'Pass' } else { 'EnvironmentBlocked' }) `
            -ArtifactLinks @($doctorPath)
        New-CoopAssertionRecord -AssertionId 'M2A.L0.NO_PRODUCT_PROCESS_LAUNCH' -RequiredEvidenceLevel 'L0' `
            -ExpectedFact 'Doctor launches no Bannerlord or dedicated-server product process.' `
            -ObservedFact 'ProductProcessLaunched=false.' -AuthoritativeSource 'scripts/Invoke-CoopTest.ps1' `
            -FirstRelevantEventSequence $doctorStartedSequence -LastRelevantEventSequence $doctorCompletedSequence `
            -Outcome 'Pass' -ArtifactLinks @($doctorPath)
    )
    Write-CoopJsonAtomic -Path $doctorPath -Value $facts
    if ($blockers.Count -gt 0) {
        return [ordered]@{ Outcome = 'EnvironmentBlocked'; Reason = ($blockers -join ', '); ArtifactPath = $doctorPath }
    }
    return [ordered]@{ Outcome = 'Pass'; Reason = 'Environment doctor found no blocker.'; ArtifactPath = $doctorPath }
}

function Invoke-CoopContracts {
    Add-CoopEvent -EventType 'ContractsStarted' -Message 'Full canonical contract-test inventory started.'
    Write-CoopRunnerStatus -State 'Contracts' -Outcome '' -Reason 'Running canonical contract-test inventory.'

    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        return [ordered]@{ Outcome = 'EnvironmentBlocked'; Reason = 'dotnet.exe is unavailable.'; ArtifactPath = '' }
    }

    $inventoryPath = Join-Path $repositoryRoot 'Tests\contract-tests.manifest.json'
    if (-not [System.IO.File]::Exists($inventoryPath)) {
        return [ordered]@{ Outcome = 'PreconditionsFailed'; Reason = 'The canonical contract-test manifest is missing.'; ArtifactPath = '' }
    }
    $inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
    if ($inventory.SchemaVersion -ne 1 -or $null -eq $inventory.Projects) {
        return [ordered]@{ Outcome = 'PreconditionsFailed'; Reason = 'The canonical contract-test manifest schema is invalid.'; ArtifactPath = '' }
    }

    $declaredPaths = @($inventory.Projects | ForEach-Object { $_.Path.Replace('/', '\') } | Sort-Object)
    $discoveredPaths = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'Tests') -Recurse -Filter '*.csproj' | ForEach-Object {
        $_.FullName.Substring($repositoryRoot.Length).TrimStart('\', '/')
    } | Sort-Object)
    $inventoryDifference = @(Compare-Object -ReferenceObject $declaredPaths -DifferenceObject $discoveredPaths)
    if ($inventoryDifference.Count -ne 0) {
        return [ordered]@{ Outcome = 'PreconditionsFailed'; Reason = 'The canonical contract-test manifest does not exactly match discovered projects.'; ArtifactPath = '' }
    }

    $contractBuildRoot = Join-Path $runRoot 'work\contract-build'
    $logsRoot = Join-Path $runRoot 'artifacts\logs\contracts'
    $results = New-Object System.Collections.Generic.List[object]
    $previousRepositoryRoot = [Environment]::GetEnvironmentVariable('COOPSPECTATOR_REPOSITORY_ROOT', 'Process')
    [Environment]::SetEnvironmentVariable('COOPSPECTATOR_REPOSITORY_ROOT', $repositoryRoot, 'Process')
    try {
        foreach ($project in $inventory.Projects) {
            $projectPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $project.Path))
            $safeName = ($project.Name -replace '[^A-Za-z0-9._-]', '_')
            $logPath = Join-Path $logsRoot ($safeName + '.log')
            Add-CoopEvent -EventType 'ContractStarted' -Message $project.Name
            $contractStartedSequence = $eventSequence
            $arguments = @(
                'run', '--project', $projectPath,
                '--configuration', 'Release',
                '--property:CoopCompileOnly=true',
                ('--property:CoopCompileOutputRoot=' + $contractBuildRoot),
                '--nologo'
            )
            $execution = Invoke-CoopExternal -Executable $dotnetCommand.Source -Arguments $arguments -LogPath $logPath
            $outcome = if ($execution.ExitCode -eq 0) { 'Pass' } else { 'AssertionFailed' }
            Add-CoopEvent -EventType 'ContractCompleted' -Message ($project.Name + ' outcome=' + $outcome + ' exit=' + $execution.ExitCode)
            $contractCompletedSequence = $eventSequence
            $assertion = New-CoopAssertionRecord `
                -AssertionId ('M2A.L1.CONTRACT.' + $safeName.ToUpperInvariant()) `
                -RequiredEvidenceLevel 'L1' `
                -ExpectedFact 'Contract project exits with code 0.' `
                -ObservedFact ('ExitCode=' + $execution.ExitCode + '; Outcome=' + $outcome) `
                -AuthoritativeSource $project.Path `
                -FirstRelevantEventSequence $contractStartedSequence `
                -LastRelevantEventSequence $contractCompletedSequence `
                -Outcome $outcome `
                -ArtifactLinks @($execution.LogPath, $execution.StandardOutputLogPath, $execution.StandardErrorLogPath)
            $results.Add([ordered]@{
                ProjectName = $project.Name
                ProjectPath = $project.Path
                Command = $execution.Command
                StartedUtc = $execution.StartedUtc
                EndedUtc = $execution.EndedUtc
                DurationMilliseconds = $execution.DurationMilliseconds
                ExitCode = $execution.ExitCode
                Outcome = $outcome
                LogPath = $execution.LogPath
                StandardOutputLogPath = $execution.StandardOutputLogPath
                StandardErrorLogPath = $execution.StandardErrorLogPath
                FirstRelevantEventSequence = $contractStartedSequence
                LastRelevantEventSequence = $contractCompletedSequence
                Assertion = $assertion
            })
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable('COOPSPECTATOR_REPOSITORY_ROOT', $previousRepositoryRoot, 'Process')
    }

    $failed = @($results | Where-Object { $_.ExitCode -ne 0 })
    $report = [ordered]@{
        Schema = 'coop-contract-aggregate-v1'
        RunId = $RunId
        StartedUtc = $runCreatedUtc.ToString('O')
        CompletedUtc = [DateTime]::UtcNow.ToString('O')
        RequestedAllSwitch = [bool]$All
        SuiteSelection = 'FullCanonicalInventory'
        IsFullSuite = $true
        InventoryPath = $inventoryPath
        ProjectCount = $results.Count
        PassedCount = $results.Count - $failed.Count
        FailedCount = $failed.Count
        Results = $results.ToArray()
        Assertions = @($results | ForEach-Object { $_.Assertion })
    }
    $reportPath = Join-Path $runRoot 'artifacts\results\contracts.json'
    Write-CoopJsonAtomic -Path $reportPath -Value $report

    $summaryLines = New-Object System.Collections.Generic.List[string]
    $summaryLines.Add('# Contract Test Aggregate')
    $summaryLines.Add('')
    $summaryLines.Add('- RunId: `' + $RunId + '`')
    $summaryLines.Add('- Projects: ' + $results.Count)
    $summaryLines.Add('- Passed: ' + ($results.Count - $failed.Count))
    $summaryLines.Add('- Failed: ' + $failed.Count)
    $summaryLines.Add('')
    $summaryLines.Add('| Project | Outcome | Exit code | Duration ms | Log |')
    $summaryLines.Add('|---|---|---:|---:|---|')
    foreach ($result in $results) {
        $summaryLines.Add('| ' + $result.ProjectName + ' | ' + $result.Outcome + ' | ' + $result.ExitCode + ' | ' + $result.DurationMilliseconds + ' | `' + $result.LogPath + '` |')
    }
    [System.IO.File]::WriteAllLines(
        (Join-Path $runRoot 'artifacts\results\contracts.md'),
        $summaryLines,
        (New-Object System.Text.UTF8Encoding($false)))

    if ($failed.Count -gt 0) {
        return [ordered]@{ Outcome = 'AssertionFailed'; Reason = ($failed.Count.ToString() + ' contract project(s) failed.'); ArtifactPath = $reportPath }
    }
    return [ordered]@{ Outcome = 'Pass'; Reason = ('All ' + $results.Count + ' contract projects passed.'); ArtifactPath = $reportPath }
}

function Get-CoopProtectedInventories {
    Resolve-CoopEnvironmentRoots
    return [ordered]@{
        ClientModule = Get-CoopDirectoryInventory -Path (Join-Path $GameRoot 'Modules\CoopSpectator')
        LegacyClientModule = Get-CoopDirectoryInventory -Path (Join-Path $GameRoot 'Modules\CoopSpectatorMP')
        DedicatedModule = Get-CoopDirectoryInventory -Path (Join-Path $DedicatedServerRoot 'Modules\CoopSpectatorDedicated')
    }
}

function Invoke-CoopCompileOnly {
    Add-CoopEvent -EventType 'CompileOnlyStarted' -Message 'Client and dedicated compile-only verification started.'
    $compileStartedSequence = $eventSequence
    Write-CoopRunnerStatus -State 'CompileOnly' -Outcome '' -Reason 'Compiling into the run root with deployment disabled.'

    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        return [ordered]@{ Outcome = 'EnvironmentBlocked'; Reason = 'dotnet.exe is unavailable.'; ArtifactPath = '' }
    }
    Resolve-CoopEnvironmentRoots
    if (-not [System.IO.Directory]::Exists($GameRoot) -or -not [System.IO.Directory]::Exists($DedicatedServerRoot)) {
        return [ordered]@{ Outcome = 'EnvironmentBlocked'; Reason = 'The configured game or dedicated installation root is missing.'; ArtifactPath = '' }
    }

    $identityRoot = Join-Path $runRoot 'artifacts\identity'
    $before = Get-CoopProtectedInventories
    Write-CoopJsonAtomic -Path (Join-Path $identityRoot 'installed-before.json') -Value $before

    $buildRoot = Join-Path $runRoot 'work\compile-only'
    $logsRoot = Join-Path $runRoot 'artifacts\logs\compile-only'
    $commonArguments = @(
        '--configuration', 'Release', '--nologo',
        '--property:CoopCompileOnly=true',
        ('--property:CoopCompileOutputRoot=' + $buildRoot),
        ('--property:BannerlordRootDir=' + $GameRoot),
        ('--property:DedicatedServerRootDir=' + $DedicatedServerRoot)
    )

    $clientArguments = @('build', (Join-Path $repositoryRoot 'CoopSpectator.csproj')) + $commonArguments + @('--property:BuildDedicatedServerModule=false')
    $client = Invoke-CoopExternal -Executable $dotnetCommand.Source -Arguments $clientArguments -LogPath (Join-Path $logsRoot 'client.log')
    Add-CoopEvent -EventType 'CompileProjectCompleted' -Message ('Client exit=' + $client.ExitCode)

    $dedicatedArguments = @('build', (Join-Path $repositoryRoot 'DedicatedServer\CoopSpectatorDedicated.csproj')) + $commonArguments + @('--property:UseDedicatedServerRefs=true')
    $dedicated = Invoke-CoopExternal -Executable $dotnetCommand.Source -Arguments $dedicatedArguments -LogPath (Join-Path $logsRoot 'dedicated.log')
    Add-CoopEvent -EventType 'CompileProjectCompleted' -Message ('Dedicated exit=' + $dedicated.ExitCode)

    $after = Get-CoopProtectedInventories
    Write-CoopJsonAtomic -Path (Join-Path $identityRoot 'installed-after.json') -Value $after
    $unchanged = Test-CoopInventoryEqual -Before $before -After $after

    $clientOutput = Join-Path $buildRoot 'projects\CoopSpectator\bin\CoopSpectator.dll'
    $dedicatedOutput = Join-Path $buildRoot 'projects\CoopSpectatorDedicated\bin\CoopSpectator.dll'
    $clientOutputFact = Get-CoopFileFact -Path $clientOutput
    $dedicatedOutputFact = Get-CoopFileFact -Path $dedicatedOutput
    if ($clientOutputFact.Exists) {
        $manifest.ClientModuleVersion = $clientOutputFact.ProductVersion
        $manifest.ClientModuleSha256 = $clientOutputFact.Sha256
    }
    if ($dedicatedOutputFact.Exists) {
        $manifest.DedicatedModuleVersion = $dedicatedOutputFact.ProductVersion
        $manifest.DedicatedModuleSha256 = $dedicatedOutputFact.Sha256
    }
    Add-CoopEvent -EventType 'CompileOnlyCompleted' -Message ('ClientExit=' + $client.ExitCode + '; DedicatedExit=' + $dedicated.ExitCode + '; InstalledUnchanged=' + $unchanged)
    $compileCompletedSequence = $eventSequence
    $beforeInventoryPath = Join-Path $identityRoot 'installed-before.json'
    $afterInventoryPath = Join-Path $identityRoot 'installed-after.json'
    $assertions = @(
        New-CoopAssertionRecord -AssertionId 'M2A.L1.COMPILE.CLIENT' -RequiredEvidenceLevel 'L1' `
            -ExpectedFact 'Client project compiles independently into the run root with exit code 0.' `
            -ObservedFact ('ExitCode=' + $client.ExitCode + '; OutputExists=' + $clientOutputFact.Exists) `
            -AuthoritativeSource 'CoopSpectator.csproj build result and output SHA-256' `
            -FirstRelevantEventSequence $compileStartedSequence -LastRelevantEventSequence $compileCompletedSequence `
            -Outcome $(if ($client.ExitCode -eq 0 -and $clientOutputFact.Exists) { 'Pass' } else { 'AssertionFailed' }) `
            -ArtifactLinks @($client.LogPath, $client.StandardOutputLogPath, $client.StandardErrorLogPath, $clientOutput)
        New-CoopAssertionRecord -AssertionId 'M2A.L1.COMPILE.DEDICATED' -RequiredEvidenceLevel 'L1' `
            -ExpectedFact 'Dedicated project compiles independently into the run root with exit code 0.' `
            -ObservedFact ('ExitCode=' + $dedicated.ExitCode + '; OutputExists=' + $dedicatedOutputFact.Exists) `
            -AuthoritativeSource 'CoopSpectatorDedicated.csproj build result and output SHA-256' `
            -FirstRelevantEventSequence $compileStartedSequence -LastRelevantEventSequence $compileCompletedSequence `
            -Outcome $(if ($dedicated.ExitCode -eq 0 -and $dedicatedOutputFact.Exists) { 'Pass' } else { 'AssertionFailed' }) `
            -ArtifactLinks @($dedicated.LogPath, $dedicated.StandardOutputLogPath, $dedicated.StandardErrorLogPath, $dedicatedOutput)
        New-CoopAssertionRecord -AssertionId 'M2A.L1.COMPILE.NO_INSTALLED_MUTATION' -RequiredEvidenceLevel 'L1' `
            -ExpectedFact 'Installed client, legacy-client, and dedicated module inventories are byte-for-byte unchanged.' `
            -ObservedFact ('InstalledInventoriesUnchanged=' + $unchanged) `
            -AuthoritativeSource 'Before/after recursive SHA-256 inventories' `
            -FirstRelevantEventSequence $compileStartedSequence -LastRelevantEventSequence $compileCompletedSequence `
            -Outcome $(if ($unchanged) { 'Pass' } else { 'AssertionFailed' }) `
            -ArtifactLinks @($beforeInventoryPath, $afterInventoryPath)
        New-CoopAssertionRecord -AssertionId 'M2A.L1.COMPILE.NO_PRODUCT_PROCESS_LAUNCH' -RequiredEvidenceLevel 'L1' `
            -ExpectedFact 'Compile-only mode launches no Bannerlord or dedicated-server product process.' `
            -ObservedFact 'ProductProcessLaunched=false.' -AuthoritativeSource 'scripts/Invoke-CoopTest.ps1' `
            -FirstRelevantEventSequence $compileStartedSequence -LastRelevantEventSequence $compileCompletedSequence `
            -Outcome 'Pass' -ArtifactLinks @($beforeInventoryPath, $afterInventoryPath)
    )
    $report = [ordered]@{
        Schema = 'coop-compile-only-v1'
        RunId = $RunId
        CoopCompileOnly = $true
        OutputRoot = $buildRoot
        ProductProcessLaunched = $false
        ClientBuild = $client
        DedicatedBuild = $dedicated
        ClientOutput = $clientOutputFact
        DedicatedOutput = $dedicatedOutputFact
        InstalledInventoriesUnchanged = $unchanged
        BeforeInventoryPath = $beforeInventoryPath
        AfterInventoryPath = $afterInventoryPath
        Assertions = $assertions
    }
    $reportPath = Join-Path $runRoot 'artifacts\results\compile-only.json'
    Write-CoopJsonAtomic -Path $reportPath -Value $report

    if (-not $unchanged) {
        return [ordered]@{ Outcome = 'AssertionFailed'; Reason = 'Compile-only verification changed a protected installed module inventory.'; ArtifactPath = $reportPath }
    }
    if ($client.ExitCode -ne 0 -or $dedicated.ExitCode -ne 0) {
        return [ordered]@{ Outcome = 'AssertionFailed'; Reason = 'One or both compile-only project builds failed.'; ArtifactPath = $reportPath }
    }
    if (-not $report.ClientOutput.Exists -or -not $report.DedicatedOutput.Exists) {
        return [ordered]@{ Outcome = 'AssertionFailed'; Reason = 'A compile-only output assembly is missing from the run root.'; ArtifactPath = $reportPath }
    }
    return [ordered]@{ Outcome = 'Pass'; Reason = 'Client and dedicated compiled under the run root and installed module inventories were unchanged.'; ArtifactPath = $reportPath }
}

function Invoke-CoopExistingRunControl {
    if (-not [System.IO.Directory]::Exists($runRoot)) {
        Write-Host "[EnvironmentBlocked] Existing run root does not exist: $runRoot"
        return 10
    }

    $manifestFilePresent = [System.IO.File]::Exists($manifestPath)
    $leaseFilePresent = [System.IO.File]::Exists($leasePath)
    $existingManifest = Read-CoopJsonShared -Path $manifestPath
    $existingLease = Read-CoopJsonShared -Path $leasePath
    $inventoryPath = Join-Path $runRoot 'artifacts\processes\runtime-owned-processes.json'
    $inventoryFilePresent = [System.IO.File]::Exists($inventoryPath)
    $inventory = Read-CoopJsonShared -Path $inventoryPath
    $inventoryProcessesProperty = if ($null -ne $inventory) { $inventory.PSObject.Properties['Processes'] } else { $null }
    $inventoryRunIdProperty = if ($null -ne $inventory) { $inventory.PSObject.Properties['RunId'] } else { $null }
    $manifestRunIdProperty = if ($null -ne $existingManifest) { $existingManifest.PSObject.Properties['RunId'] } else { $null }
    $leaseRunIdProperty = if ($null -ne $existingLease) { $existingLease.PSObject.Properties['RunId'] } else { $null }
    $manifestValid = $manifestFilePresent -and $null -ne $manifestRunIdProperty -and
        [string]::Equals([string]$manifestRunIdProperty.Value, $RunId, [StringComparison]::Ordinal)
    $leaseValid = $leaseFilePresent -and $null -ne $leaseRunIdProperty -and
        [string]::Equals([string]$leaseRunIdProperty.Value, $RunId, [StringComparison]::Ordinal)
    $inventoryValid = -not $inventoryFilePresent -or
        ($null -ne $inventoryProcessesProperty -and $null -ne $inventoryRunIdProperty -and
            [string]::Equals([string]$inventoryRunIdProperty.Value, $RunId, [StringComparison]::Ordinal))
    $manifestRunnerRoles = @(
        if ($null -ne $existingManifest) {
            @(Get-CoopOptionalPropertyValue -InputObject $existingManifest -Name 'Roles') | Where-Object {
                [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $_ -Name 'RoleType'), 'Runner', [StringComparison]::Ordinal) -and
                [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $_ -Name 'RoleInstanceId'), 'runner-01', [StringComparison]::Ordinal)
            }
        }
    )
    $recoveryCapabilityDeclared = $manifestRunnerRoles.Count -eq 1 -and
        (@(Get-CoopOptionalPropertyValue -InputObject $manifestRunnerRoles[0] -Name 'Capabilities') -contains 'RecoveryV2')
    $processes = @(
        if ($inventoryValid -and $null -ne $inventoryProcessesProperty -and $null -ne $inventoryProcessesProperty.Value) {
            @($inventoryProcessesProperty.Value)
        }
    )
    $liveProcesses = @($processes | Where-Object { Test-CoopLiveProcessIdentity -Identity $_ })
    $rejectedIdentities = @($processes | Where-Object {
        $recordedPid = [int](Get-CoopOptionalPropertyValue -InputObject $_ -Name 'ProcessId')
        $recordedPid -gt 0 -and $null -ne (Get-Process -Id $recordedPid -ErrorAction SilentlyContinue) -and
            -not (Test-CoopLiveProcessIdentity -Identity $_)
    } | ForEach-Object {
        [pscustomobject][ordered]@{
            ProcessId = [int](Get-CoopOptionalPropertyValue -InputObject $_ -Name 'ProcessId')
            RecordedExecutablePath = [string](Get-CoopOptionalPropertyValue -InputObject $_ -Name 'ExecutablePath')
            RecordedProcessStartUtc = [string](Get-CoopOptionalPropertyValue -InputObject $_ -Name 'ProcessStartUtc')
            RejectionReason = 'PID reuse or exact path/start/parent identity mismatch.'
        }
    })
    $previewActions = @($liveProcesses | ForEach-Object {
        [pscustomobject][ordered]@{
            Action = 'StopExactOwnedProcess'
            ProcessId = [int](Get-CoopOptionalPropertyValue -InputObject $_ -Name 'ProcessId')
            ExecutablePath = [string](Get-CoopOptionalPropertyValue -InputObject $_ -Name 'ExecutablePath')
            ProcessStartUtc = [string](Get-CoopOptionalPropertyValue -InputObject $_ -Name 'ProcessStartUtc')
            RevalidateImmediatelyBeforeAction = $true
        }
    })
    $manifestOutcomeProperty = if ($null -ne $existingManifest) { $existingManifest.PSObject.Properties['TerminalOutcome'] } else { $null }
    $leaseStatusProperty = if ($null -ne $existingLease) { $existingLease.PSObject.Properties['Status'] } else { $null }
    $summary = [ordered]@{
        Command = $Command
        RunId = $RunId
        RunRoot = $runRoot
        ManifestPresent = $manifestFilePresent
        ManifestValid = $manifestValid
        ManifestOutcome = if ($null -ne $manifestOutcomeProperty) { [string]$manifestOutcomeProperty.Value } else { '' }
        LeasePresent = $leaseFilePresent
        LeaseValid = $leaseValid
        LeaseStatus = if ($null -ne $leaseStatusProperty) { [string]$leaseStatusProperty.Value } else { '' }
        ProcessInventoryPresent = $inventoryFilePresent
        ProcessInventoryValid = $inventoryValid
        RecoveryV2Declared = $recoveryCapabilityDeclared
        RecordedProcessCount = $processes.Count
        LiveExactProcessCount = $liveProcesses.Count
        LiveExactProcesses = $liveProcesses
        RejectedIdentities = $rejectedIdentities
        PreviewActions = $previewActions
        DeletesRunRoot = $false
        ApplyRecovery = [bool]$ApplyRecovery
        InspectedUtc = [DateTime]::UtcNow.ToString('O')
    }

    if ($Command -eq 'Inspect' -or -not $ApplyRecovery) {
        Write-Host ($summary | ConvertTo-Json -Depth 20)
        if ($Command -eq 'Recover' -and -not $ApplyRecovery) {
            Write-Host '[INFO] Recovery preview only. Re-run with -ApplyRecovery to stop exact matching owned processes.'
        }
        return 0
    }

    if (-not $manifestValid -or -not $leaseValid -or -not $inventoryValid -or -not $recoveryCapabilityDeclared) {
        Write-Host '[EnvironmentBlocked] Recovery apply requires readable matching manifest/lease/inventory artifacts and an explicit RecoveryV2 capability.'
        return 10
    }

    $recoveryLock = $null
    try {
        $recoveryLock = New-Object System.IO.FileStream(
            $lockPath,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
    }
    catch {
        Write-Host '[EnvironmentBlocked] The run lock is still held; recovery will not interrupt an active runner.'
        return 10
    }

    try {
        foreach ($identity in $liveProcesses) {
            $ownedRuntimeProcesses.Add($identity)
        }
        Stop-CoopOwnedRuntimeProcesses
        $remaining = @($ownedRuntimeProcesses | Where-Object { Test-CoopLiveProcessIdentity -Identity $_ })
        $sharedLockReport = Read-CoopJsonShared -Path (Join-Path $runRoot 'artifacts\processes\shared-runtime-locks.json')
        $sharedLockRelease = New-Object 'System.Collections.Generic.List[object]'
        $sharedLockRecords = Get-CoopOptionalPropertyValue -InputObject $sharedLockReport -Name 'Locks'
        foreach ($record in $(if ($null -eq $sharedLockRecords) { @() } else { @($sharedLockRecords) })) {
            $released = $false
            $failure = ''
            try {
                $probe = New-Object System.IO.FileStream(
                    ([string]$record.LockPath),
                    [System.IO.FileMode]::Open,
                    [System.IO.FileAccess]::ReadWrite,
                    [System.IO.FileShare]::None)
                $probe.Dispose()
                $released = $true
            }
            catch { $failure = $_.Exception.Message }
            $releaseRecord = [pscustomobject][ordered]@{
                ResourceId = [string]$record.ResourceId
                LockPath = [string]$record.LockPath
                ReleasedAndReacquired = $released
                Failure = $failure
            }
            $sharedLockRelease.Add($releaseRecord) | Out-Null
        }
        $sharedLocksReleased = @($sharedLockRelease | Where-Object { -not $_.ReleasedAndReacquired }).Count -eq 0
        $recoveryComplete = $remaining.Count -eq 0 -and $sharedLocksReleased
        $report = [ordered]@{
            Schema = 'coop-runtime-recovery-v2'
            ProtocolMajorVersion = 1
            ProtocolMinorVersion = 1
            RunId = $RunId
            AppliedUtc = [DateTime]::UtcNow.ToString('O')
            RecordedProcessCount = $processes.Count
            ExactLiveProcessCountBefore = $liveProcesses.Count
            ExactLiveProcessCountAfter = $remaining.Count
            PreviewActions = $previewActions
            RejectedIdentities = $rejectedIdentities
            Actions = $runtimeCleanupEvidence.ToArray()
            SharedRuntimeLockRelease = $sharedLockRelease.ToArray()
            SharedRuntimeLocksReleased = $sharedLocksReleased
            DeletedRunRoot = $false
            Outcome = if ($recoveryComplete) { 'Recovered' } else { 'RecoveryIncomplete' }
        }
        Write-CoopJsonAtomic -Path (Join-Path $runRoot 'artifacts\processes\recovery.json') -Value $report
        Write-Host ($report | ConvertTo-Json -Depth 20)
        return $(if ($recoveryComplete) { 0 } else { 20 })
    }
    finally {
        $recoveryLock.Dispose()
    }
}

function Invoke-CoopExistingRunCancellation {
    if (-not [System.IO.Directory]::Exists($runRoot)) {
        Write-Host "[EnvironmentBlocked] Existing run root does not exist: $runRoot"
        return 10
    }

    $existingManifest = Read-CoopJsonShared -Path $manifestPath
    $existingLease = Read-CoopJsonShared -Path $leasePath
    if ($null -eq $existingManifest -or $null -eq $existingLease -or
        -not [string]::Equals([string]$existingManifest.RunId, $RunId, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$existingLease.RunId, $RunId, [StringComparison]::Ordinal)) {
        Write-Host '[EnvironmentBlocked] Cancellation requires readable matching manifest and lease artifacts.'
        return 10
    }
    if ([int]$existingManifest.ProtocolMajorVersion -ne 1 -or [int]$existingManifest.ProtocolMinorVersion -ne 1) {
        Write-Host '[EnvironmentBlocked] CancellationV1 is not declared by this run protocol.'
        return 10
    }
    $runnerRole = @($existingManifest.Roles | Where-Object {
        [string]::Equals([string]$_.RoleType, 'Runner', [StringComparison]::Ordinal) -and
        [string]::Equals([string]$_.RoleInstanceId, 'runner-01', [StringComparison]::Ordinal)
    })
    if ($runnerRole.Count -ne 1 -or -not (Test-CoopLiveProcessIdentity -Identity $runnerRole[0]) -or
        [int]$existingLease.OwnerProcessId -ne [int]$runnerRole[0].ProcessId) {
        Write-Host '[EnvironmentBlocked] The exact runner process identity is not live; use Recover preview instead.'
        return 10
    }
    if (-not (@(Get-CoopOptionalPropertyValue -InputObject $runnerRole[0] -Name 'Capabilities') -contains 'CancellationV1')) {
        Write-Host '[EnvironmentBlocked] The exact runner role did not declare CancellationV1.'
        return 10
    }
    $leaseHeartbeatUtc = ConvertTo-CoopUtcDateTime -Value $existingLease.LastHeartbeatUtc
    $leaseExpiresUtc = ConvertTo-CoopUtcDateTime -Value $existingLease.ExpiresUtc
    if ($null -eq $leaseHeartbeatUtc -or $null -eq $leaseExpiresUtc -or
        $leaseExpiresUtc -le [DateTime]::UtcNow -or
        [DateTime]::UtcNow - $leaseHeartbeatUtc -gt [TimeSpan]::FromSeconds(10)) {
        Write-Host '[EnvironmentBlocked] The runner lease heartbeat is stale; cancellation will not target uncertain ownership.'
        return 10
    }

    try {
        $inactiveProbe = New-Object System.IO.FileStream(
            $lockPath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
        $inactiveProbe.Dispose()
        Write-Host '[EnvironmentBlocked] The runner lock is not held; cancellation will not target an inactive run.'
        return 10
    }
    catch [System.IO.IOException] { }
    catch {
        Write-Host ('[EnvironmentBlocked] Runner lock ownership could not be verified: ' + $_.Exception.Message)
        return 10
    }

    $existingRequest = Read-CoopJsonShared -Path $cancellationRequestPath
    if ($null -ne $existingRequest) {
        if ([string]::Equals([string]$existingRequest.RunId, $RunId, [StringComparison]::Ordinal) -and
            [string]::Equals([string]$existingRequest.NonceSha256, [string]$existingManifest.NonceSha256, [StringComparison]::OrdinalIgnoreCase)) {
            Write-Host ($existingRequest | ConvertTo-Json -Depth 10)
            return 0
        }
        Write-Host '[EnvironmentBlocked] A conflicting cancellation request already exists.'
        return 10
    }

    $request = [ordered]@{
        SchemaVersion = 1
        ProtocolMajorVersion = 1
        ProtocolMinorVersion = 1
        RunId = $RunId
        NonceSha256 = [string]$existingManifest.NonceSha256
        RequestId = [Guid]::NewGuid().ToString('D')
        SourceRoleType = 'ExternalController'
        SourceRoleInstanceId = 'cancel-command'
        TargetRoleType = 'Runner'
        TargetRoleInstanceId = 'runner-01'
        RequestedUtc = [DateTime]::UtcNow.ToString('O')
        Reason = 'Explicit run-scoped Cancel command.'
    }
    Write-CoopJsonAtomic -Path $cancellationRequestPath -Value $request
    Write-Host ($request | ConvertTo-Json -Depth 10)
    return 0
}

function Invoke-CoopFeasibility {
    $effectiveServerName = if ([string]::IsNullOrWhiteSpace($ServerName)) {
        $candidate = 'AC_COOP_' + $RunId
        if ($candidate.Length -gt 120) { $candidate.Substring(0, 120) } else { $candidate }
    }
    else { $ServerName.Trim() }
    if ($effectiveServerName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$') {
        return [ordered]@{ Outcome = 'PreconditionsFailed'; Reason = 'The runtime server name must contain only ASCII letters, digits, dot, underscore, or hyphen.'; ArtifactPath = '' }
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedClientModuleSha256) -and
        -not (Test-CoopSha256Hex -Value $ExpectedClientModuleSha256)) {
        return [ordered]@{ Outcome = 'PreconditionsFailed'; Reason = 'ExpectedClientModuleSha256 must be exactly 64 hexadecimal characters.'; ArtifactPath = '' }
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedDedicatedModuleSha256) -and
        -not (Test-CoopSha256Hex -Value $ExpectedDedicatedModuleSha256)) {
        return [ordered]@{ Outcome = 'PreconditionsFailed'; Reason = 'ExpectedDedicatedModuleSha256 must be exactly 64 hexadecimal characters.'; ArtifactPath = '' }
    }

    $expectedClientHash = if (Test-CoopSha256Hex -Value $ExpectedClientModuleSha256) {
        $ExpectedClientModuleSha256.Trim().ToUpperInvariant()
    }
    elseif ($repositoryClientFact.Exists) { [string]$repositoryClientFact.Sha256 }
    else { '' }
    $expectedDedicatedHash = if (Test-CoopSha256Hex -Value $ExpectedDedicatedModuleSha256) {
        $ExpectedDedicatedModuleSha256.Trim().ToUpperInvariant()
    }
    elseif ($repositoryDedicatedFact.Exists) { [string]$repositoryDedicatedFact.Sha256 }
    else { '' }
    if (-not (Test-CoopSha256Hex -Value $expectedClientHash) -or -not (Test-CoopSha256Hex -Value $expectedDedicatedHash)) {
        return [ordered]@{ Outcome = 'PreconditionsFailed'; Reason = 'Expected client and dedicated module hashes are required for Feasibility.'; ArtifactPath = '' }
    }

    $installedDedicatedLoadedPath = Join-Path $DedicatedServerRoot 'Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client\CoopSpectator.dll'
    $installedDedicatedLoadedFact = Get-CoopFileFact -Path $installedDedicatedLoadedPath
    if (-not $installedClientFact.Exists -or
        -not [string]::Equals([string]$installedClientFact.Sha256, $expectedClientHash, [StringComparison]::Ordinal) -or
        -not $installedDedicatedFact.Exists -or
        -not [string]::Equals([string]$installedDedicatedFact.Sha256, $expectedDedicatedHash, [StringComparison]::Ordinal) -or
        -not $installedDedicatedLoadedFact.Exists -or
        -not [string]::Equals([string]$installedDedicatedLoadedFact.Sha256, $expectedDedicatedHash, [StringComparison]::Ordinal)) {
        return [ordered]@{ Outcome = 'EnvironmentBlocked'; Reason = 'Installed client/dedicated module hashes do not match the explicitly selected runtime identities.'; ArtifactPath = '' }
    }

    $productProcesses = @(Get-Process -Name @(
        'Bannerlord',
        'TaleWorlds.MountAndBlade.Launcher',
        'DedicatedCustomServer.Starter',
        'DedicatedCustomServer',
        'TaleWorlds.CrashReporter') -ErrorAction SilentlyContinue)
    if ($productProcesses.Count -gt 0) {
        return [ordered]@{ Outcome = 'EnvironmentBlocked'; Reason = 'A Bannerlord, dedicated-server, launcher, or crash-reporter process is already running.'; ArtifactPath = '' }
    }

    $steamProcesses = @(Get-Process -Name 'steam' -ErrorAction SilentlyContinue | Where-Object {
        try { [string]::Equals([System.IO.Path]::GetFileName($_.Path), 'Steam.exe', [StringComparison]::OrdinalIgnoreCase) }
        catch { $false }
    })
    if ($steamProcesses.Count -eq 0) {
        return [ordered]@{ Outcome = 'EnvironmentBlocked'; Reason = 'Steam.exe must already be running for the multiplayer client.'; ArtifactPath = '' }
    }

    $existingPortOwner = @(Get-NetUDPEndpoint -LocalPort $Port -ErrorAction SilentlyContinue)
    if ($existingPortOwner.Count -gt 0) {
        return [ordered]@{ Outcome = 'EnvironmentBlocked'; Reason = "UDP port $Port is already owned before launch."; ArtifactPath = '' }
    }

    $globalResultPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)) 'Mount and Blade II Bannerlord\CoopSpectator\battle_result.json'
    $globalResultBefore = Get-CoopFileFact -Path $globalResultPath
    $reportPath = Join-Path $runRoot 'artifacts\results\feasibility.json'
    $runtimeOutcome = 'RunnerInternalError'
    $runtimeReason = 'Runtime feasibility did not complete.'
    $primaryRuntimeOutcome = 'RunnerInternalError'
    $primaryRuntimeReason = 'Runtime feasibility did not complete.'
    $runtimeSupersession = ''
    $runtimeFailureCode = ''
    $runtimeFailureEvidence = $null
    $dedicatedRoleStatus = $null
    $clientRoleStatus = $null
    $clientJoinStatus = $null
    $ownedHostStatus = $null
    $dedicatedProcess = $null
    $dedicatedIdentity = $null
    $clientIdentity = $null
    $dedicatedTextCapture = $null
    $dedicatedControlReadinessEvidence = $null
    $dedicatedBootstrapStatus = $null
    $bootstrapCommandEvidence = New-Object 'System.Collections.Generic.List[object]'
    $dedicatedNativeLogInventory = $null
    $clientNativeLogInventory = $null

    try {
        $dedicatedExecutable = Join-Path $DedicatedServerRoot 'bin\Win64_Shipping_Server\DedicatedCustomServer.Starter.exe'
        $dedicatedLogRoot = Join-Path $runRoot 'artifacts\logs\dedicated'
        [System.IO.Directory]::CreateDirectory($dedicatedLogRoot) | Out-Null
        $dedicatedStartInfo = New-Object System.Diagnostics.ProcessStartInfo
        $dedicatedStartInfo.FileName = $dedicatedExecutable
        $dedicatedStartInfo.WorkingDirectory = Split-Path -Parent $dedicatedExecutable
        $dedicatedStartInfo.UseShellExecute = $false
        $dedicatedStartInfo.CreateNoWindow = $false
        $dedicatedStartInfo.RedirectStandardInput = $false
        $dedicatedStartInfo.RedirectStandardOutput = $true
        $dedicatedStartInfo.RedirectStandardError = $true
        $dedicatedStartInfo.Arguments = '--multihome 0.0.0.0 --port ' + $Port +
            ' _MODULES_*Native*SandBoxCore*Sandbox*Multiplayer*CoopSpectatorDedicated*_MODULES_' +
            ' /LogOutputPath "' + $dedicatedLogRoot + '"'
        $dedicatedStartInfo.EnvironmentVariables[$automationFlagName] = '1'
        $dedicatedStartInfo.EnvironmentVariables[$automationRunIdVariableName] = $RunId
        $dedicatedStartInfo.EnvironmentVariables[$automationRunRootVariableName] = $runRoot
        $dedicatedStartInfo.EnvironmentVariables[$automationRunTokenVariableName] = $noncePlaintext
        $dedicatedStartInfo.EnvironmentVariables[$automationExpectedModuleHashVariableName] = $expectedDedicatedHash
        $dedicatedStartInfo.EnvironmentVariables[$automationResultPolicyVariableName] = 'Suppress'
        $dedicatedStartInfo.EnvironmentVariables['BANNERLORD_GAME_ROOT'] = $GameRoot

        $dedicatedProcess = New-Object System.Diagnostics.Process
        $dedicatedProcess.StartInfo = $dedicatedStartInfo
        $dedicatedLaunchStartedUtc = [DateTime]::UtcNow
        if (-not $dedicatedProcess.Start()) { throw 'Dedicated server process creation returned false.' }
        $dedicatedLaunchObservedUtc = [DateTime]::UtcNow
        $dedicatedIdentity = New-CoopProvisionalProcessIdentity `
            -ProcessId $dedicatedProcess.Id `
            -RoleType 'DedicatedServer' `
            -RoleInstanceId 'dedicated-server-01' `
            -ExpectedExecutablePath $dedicatedExecutable `
            -ExpectedParentProcessId $PID `
            -LaunchStartedUtc $dedicatedLaunchStartedUtc `
            -LaunchObservedUtc $dedicatedLaunchObservedUtc
        Add-CoopOwnedRuntimeProcess -Identity $dedicatedIdentity
        try {
            Add-CoopEvent -EventType 'DedicatedProcessProvisionallyOwned' -Message `
                ("PID=" + $dedicatedProcess.Id + '; exact requested path and launch window recorded before identity enrichment.')
        }
        catch {
            $wrapped = [System.InvalidOperationException]::new(
                'Provisional process-ownership event publication failed: ' + $_.Exception.Message,
                $_.Exception)
            $wrapped.Data['CoopRuntimeOutcome'] = 'RunnerInternalError'
            throw $wrapped
        }
        $dedicatedIdentity = Get-CoopProcessIdentity `
            -ProcessId $dedicatedProcess.Id `
            -RoleType 'DedicatedServer' `
            -RoleInstanceId 'dedicated-server-01' `
            -ExpectedExecutablePath $dedicatedExecutable `
            -ObservedParentProcessId $PID `
            -ProvisionalIdentity $dedicatedIdentity
        Add-CoopOwnedRuntimeProcess -Identity $dedicatedIdentity
        $dedicatedTextCapture = New-CoopProcessTextCapture `
            -Process $dedicatedProcess `
            -StandardOutputPath (Join-Path $dedicatedLogRoot 'stdout.txt') `
            -StandardErrorPath (Join-Path $dedicatedLogRoot 'stderr.txt')
        Add-CoopEvent -EventType 'DedicatedProcessStarted' -Message ("PID=" + $dedicatedProcess.Id + '; awaiting exact module identity.')

        $roleDeadline = [DateTime]::UtcNow.AddSeconds([Math]::Min(180, $RuntimeTimeoutSeconds))
        $dedicatedRoleStatus = Wait-CoopRuntimeRoleReady `
            -StatusPath (Join-Path $runRoot 'status\dedicated-server-01.json') `
            -ExpectedRoleType 'DedicatedServer' `
            -ExpectedRoleInstanceId 'dedicated-server-01' `
            -ExpectedModuleSha256 $expectedDedicatedHash `
            -DeadlineUtc $roleDeadline `
            -ProcessTextCapture $dedicatedTextCapture

        $controlReadyDeadline = [DateTime]::UtcNow.AddSeconds([Math]::Min(180, $RuntimeTimeoutSeconds))
        $dedicatedControlReadinessEvidence = Wait-CoopDedicatedControlReady `
            -StatusPath (Join-Path $runRoot 'state\dedicated-control.ready.json') `
            -RoleStatusPath (Join-Path $runRoot 'status\dedicated-server-01.json') `
            -ExpectedModuleSha256 $expectedDedicatedHash `
            -DedicatedIdentity $dedicatedIdentity `
            -DedicatedProcess $dedicatedProcess `
            -DeadlineUtc $controlReadyDeadline `
            -ProcessTextCapture $dedicatedTextCapture
        Add-CoopEvent -EventType 'DedicatedControlReady' -Message `
            'The exact dedicated role published the InitialListedGameServerState.OnActivated acknowledgement.'

        $bootstrapCreatedUtc = [DateTime]::UtcNow
        $bootstrapDeadline = $bootstrapCreatedUtc.AddSeconds([Math]::Min(180, $RuntimeTimeoutSeconds))
        $dedicatedStartUtc = [DateTime]::Parse(
            [string]$dedicatedIdentity.ProcessStartUtc,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
        $dedicatedBootstrapRequest = New-CoopDedicatedBootstrapRequest `
            -RunId $RunId `
            -RunTokenSha256 $nonceSha256 `
            -ExpectedDedicatedModuleSha256 $expectedDedicatedHash `
            -ExpectedProcessId ([int]$dedicatedIdentity.ProcessId) `
            -ExpectedProcessStartUtc $dedicatedStartUtc `
            -ExpectedExecutablePath ([string]$dedicatedIdentity.ExecutablePath) `
            -CommandId ([Guid]::NewGuid()) `
            -CreatedUtc $bootstrapCreatedUtc `
            -ExpiresUtc $bootstrapDeadline `
            -ServerName $effectiveServerName
        $dedicatedBootstrapRequestPath = Join-Path $runRoot 'commands\dedicated-bootstrap.request.json'
        Write-CoopJsonAtomic -Path $dedicatedBootstrapRequestPath -Value $dedicatedBootstrapRequest
        Add-CoopEvent -EventType 'DedicatedBootstrapRequested' -Message `
            ('CommandId=' + [string]$dedicatedBootstrapRequest.CommandId + '; Profile=ConnectionFeasibilityV1.')

        $dedicatedBootstrapStatus = Wait-CoopDedicatedBootstrapAccepted `
            -StatusPath (Join-Path $runRoot 'state\dedicated-bootstrap.status.json') `
            -RoleStatusPath (Join-Path $runRoot 'status\dedicated-server-01.json') `
            -Request $dedicatedBootstrapRequest `
            -ExpectedModuleSha256 $expectedDedicatedHash `
            -DedicatedIdentity $dedicatedIdentity `
            -DedicatedProcess $dedicatedProcess `
            -DeadlineUtc $bootstrapDeadline `
            -ProcessTextCapture $dedicatedTextCapture
        foreach ($acknowledgement in @($dedicatedBootstrapStatus.Acknowledgements)) {
            $bootstrapCommandEvidence.Add($acknowledgement) | Out-Null
            Add-CoopEvent -EventType 'DedicatedBootstrapStepAcknowledged' -Message `
                ('Step=' + [string]$acknowledgement.Step + '; State=' + [string]$acknowledgement.State + '.')
        }

        $portDeadline = [DateTime]::UtcNow.AddSeconds([Math]::Min(180, $RuntimeTimeoutSeconds))
        $udpEndpoint = $null
        while ([DateTime]::UtcNow -lt $portDeadline) {
            $null = Assert-CoopRuntimeRoleHealth `
                -StatusPath (Join-Path $runRoot 'status\dedicated-server-01.json') `
                -ExpectedRoleType 'DedicatedServer' `
                -ExpectedRoleInstanceId 'dedicated-server-01'
            Update-CoopProcessTextCapture -Capture $dedicatedTextCapture
            $matches = @(Get-NetUDPEndpoint -LocalPort $Port -ErrorAction SilentlyContinue)
            if ($matches.Count -eq 1) {
                $candidate = $matches[0]
                if ([int]$candidate.OwningProcess -eq $dedicatedProcess.Id -or
                    (Test-CoopProcessDescendsFrom -ProcessId ([int]$candidate.OwningProcess) -AncestorProcessId $dedicatedProcess.Id)) {
                    $udpEndpoint = $candidate
                    break
                }
                throw "UDP port $Port was acquired by a process outside the owned dedicated process tree."
            }
            if ($matches.Count -gt 1) { throw "UDP port $Port has ambiguous ownership." }
            if ($dedicatedProcess.HasExited) { throw "Dedicated server exited before binding UDP port $Port." }
            Update-CoopLease
            Start-Sleep -Milliseconds 250
        }
        if ($null -eq $udpEndpoint) { throw "Timed out waiting for owned UDP port $Port." }

        $portOwnerIdentity = Get-CoopProcessIdentity -ProcessId ([int]$udpEndpoint.OwningProcess) -RoleType 'DedicatedServer' -RoleInstanceId 'dedicated-port-owner-01'
        Add-CoopOwnedRuntimeProcess -Identity $portOwnerIdentity
        $ownedHostStatus = [ordered]@{
            SchemaVersion = 1
            ProtocolMajorVersion = $protocolMajorVersion
            ProtocolMinorVersion = $protocolMinorVersion
            RunId = $RunId
            RunTokenSha256 = $nonceSha256
            ServerName = $effectiveServerName
            ServerPort = $Port
            OwnerProcessId = [int]$portOwnerIdentity.ProcessId
            OwnerProcessStartUtc = [string]$portOwnerIdentity.ProcessStartUtc
            OwnerExecutablePath = [string]$portOwnerIdentity.ExecutablePath
            Protocol = 'UDP'
            ConfirmedUtc = [DateTime]::UtcNow.ToString('O')
        }
        Write-CoopJsonAtomic -Path (Join-Path $runRoot 'state\dedicated-host.json') -Value $ownedHostStatus

        $oldAutomationEnvironment = [ordered]@{}
        foreach ($name in @(
            $automationFlagName,
            $automationRunIdVariableName,
            $automationRunRootVariableName,
            $automationRunTokenVariableName,
            $automationExpectedModuleHashVariableName,
            $automationResultPolicyVariableName)) {
            $oldAutomationEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        }
        try {
            [Environment]::SetEnvironmentVariable($automationFlagName, '1', 'Process')
            [Environment]::SetEnvironmentVariable($automationRunIdVariableName, $RunId, 'Process')
            [Environment]::SetEnvironmentVariable($automationRunRootVariableName, $runRoot, 'Process')
            [Environment]::SetEnvironmentVariable($automationRunTokenVariableName, $noncePlaintext, 'Process')
            [Environment]::SetEnvironmentVariable($automationExpectedModuleHashVariableName, $expectedClientHash, 'Process')
            [Environment]::SetEnvironmentVariable($automationResultPolicyVariableName, 'Suppress', 'Process')
            & (Join-Path $PSScriptRoot 'Start-CoopBattleTestClient.ps1') `
                -RunId $RunId `
                -ServerName $effectiveServerName `
                -ExpectedClientModuleSha256 $expectedClientHash `
                -Port $Port `
                -GameRoot $GameRoot `
                -GameType 'TeamDeathmatch' `
                -RequestLifetimeSeconds $RuntimeTimeoutSeconds `
                -UseExistingRunContract
        }
        finally {
            foreach ($name in $oldAutomationEnvironment.Keys) {
                [Environment]::SetEnvironmentVariable($name, $oldAutomationEnvironment[$name], 'Process')
            }
        }

        $clientLaunch = Read-CoopJsonShared -Path (Join-Path $runRoot 'artifacts\processes\client-launch.json')
        if ($null -eq $clientLaunch) { throw 'Client launch identity artifact is missing.' }
        $expectedClientExecutable = Join-Path $GameRoot 'bin\Win64_Shipping_Client\Bannerlord.exe'
        try {
            $clientIdentity = Assert-CoopClientLaunchArtifact `
                -Artifact $clientLaunch `
                -ExpectedRunId $RunId `
                -ExpectedClientModuleSha256 $expectedClientHash `
                -ExpectedExecutablePath $expectedClientExecutable `
                -ExpectedParentProcessId $PID
        }
        catch {
            $wrapped = [System.InvalidOperationException]::new(
                'Client launch handoff validation failed: ' + $_.Exception.Message,
                $_.Exception)
            $wrapped.Data['CoopRuntimeOutcome'] = 'RunnerInternalError'
            throw $wrapped
        }
        Add-CoopOwnedRuntimeProcess -Identity $clientIdentity
        $clientIdentity = Get-CoopProcessIdentity `
            -ProcessId ([int]$clientLaunch.EntryPid) `
            -RoleType 'MultiplayerClient' `
            -RoleInstanceId 'multiplayer-client-01' `
            -ExpectedExecutablePath $expectedClientExecutable `
            -ObservedParentProcessId $PID `
            -ProvisionalIdentity $clientIdentity
        Add-CoopOwnedRuntimeProcess -Identity $clientIdentity

        $clientDeadline = [DateTime]::UtcNow.AddSeconds($RuntimeTimeoutSeconds)
        $clientRoleStatus = Wait-CoopRuntimeRoleReady `
            -StatusPath (Join-Path $runRoot 'status\multiplayer-client-01.json') `
            -ExpectedRoleType 'MultiplayerClient' `
            -ExpectedRoleInstanceId 'multiplayer-client-01' `
            -ExpectedModuleSha256 $expectedClientHash `
            -DeadlineUtc $clientDeadline `
            -ProcessTextCapture $dedicatedTextCapture
        $clientJoinStatus = Wait-CoopClientConnection `
            -StatusPath (Join-Path $runRoot 'state\client-join.status.json') `
            -RoleStatusPath (Join-Path $runRoot 'status\multiplayer-client-01.json') `
            -DeadlineUtc $clientDeadline `
            -ProcessTextCapture $dedicatedTextCapture

        $runtimeOutcome = 'Pass'
        $runtimeReason = 'Exact client and dedicated module identities were confirmed and the normal lobby path connected to the owned local server.'
    }
    catch {
        $runtimeReason = $_.Exception.Message
        $statusHint = $_.Exception.Data['CoopClientJoinStatus']
        if ($null -ne $statusHint) {
            $clientJoinStatus = $statusHint
        }
        $outcomeHint = [string]$_.Exception.Data['CoopRuntimeOutcome']
        $runtimeFailureCode = [string]$_.Exception.Data['CoopFailureCode']
        $runtimeOutcome = if ($outcomeExitCodes.ContainsKey($outcomeHint)) { $outcomeHint }
        elseif ($runtimeReason -match 'Timed out') { 'Timeout' }
        elseif ($runtimeReason -match 'exited|crash') { 'Crash' }
        else { 'AssertionFailed' }
    }
    finally {
        $primaryRuntimeOutcome = $runtimeOutcome
        $primaryRuntimeReason = $runtimeReason
        if ($null -eq $clientJoinStatus) {
            try {
                $terminalClientStatus = Read-CoopJsonShared -Path (Join-Path $runRoot 'state\client-join.status.json')
                if ($null -ne $terminalClientStatus -and [bool]$terminalClientStatus.IsTerminal) {
                    if ([string]$terminalClientStatus.RunId -ne $RunId) {
                        throw 'Terminal client join status RunId mismatch during final evidence capture.'
                    }
                    if ([string]$terminalClientStatus.RunTokenSha256 -ne $nonceSha256) {
                        throw 'Terminal client join status token mismatch during final evidence capture.'
                    }
                    $clientJoinStatus = $terminalClientStatus
                }
            }
            catch {
                $runtimeOutcome = 'RunnerInternalError'
                $runtimeReason = 'Terminal client join status capture failed: ' + $_.Exception.Message
                $runtimeSupersession = 'ClientJoinStatusCaptureFailed'
            }
        }
        try {
            $rootProcessIds = @($ownedRuntimeProcesses | Where-Object {
                [string]$_.RoleType -eq 'DedicatedServer' -or [string]$_.RoleType -eq 'MultiplayerClient'
            } | ForEach-Object { [int]$_.ProcessId } | Select-Object -Unique)
            if ($rootProcessIds.Count -gt 0) { $null = Add-CoopOwnedDescendants -RootProcessIds $rootProcessIds }
        }
        catch {
            $runtimeOutcome = 'RunnerInternalError'
            $runtimeReason = 'Owned descendant discovery failed before cleanup: ' + $_.Exception.Message
            $runtimeSupersession = 'OwnedDescendantDiscoveryFailed'
            try { Add-CoopEvent -EventType 'OwnedDescendantDiscoveryFailed' -Message $runtimeReason } catch { }
        }
        if ($runtimeOutcome -eq 'Crash' -or $runtimeOutcome -eq 'Timeout') {
            try {
                $runtimeFailureEvidence = Write-CoopRuntimeFailureEvidence `
                    -Outcome $runtimeOutcome `
                    -FailureCode $runtimeFailureCode `
                    -FailureMessage $runtimeReason
                if (@($runtimeFailureEvidence.Evidence.CorrelatedFailureProcesses).Count -gt 0) {
                    $runtimeOutcome = 'Crash'
                    $runtimeFailureCode = 'CrashReporterDetected'
                    $runtimeReason = 'An exact path- and PID-correlated crash/modal helper was detected. ' + $runtimeReason
                }
            }
            catch {
                $runtimeOutcome = 'RunnerInternalError'
                $runtimeReason = 'Structured failure-evidence publication failed: ' + $_.Exception.Message
                $runtimeSupersession = 'FailureEvidencePublicationFailed'
            }
        }
        try { Stop-CoopOwnedRuntimeProcesses }
        catch {
            $runtimeOutcome = 'RunnerInternalError'
            $runtimeReason = 'Exact runtime cleanup failed: ' + $_.Exception.Message
            $runtimeSupersession = 'RuntimeCleanupFailed'
        }
        if ($null -ne $dedicatedTextCapture) {
            try { Complete-CoopProcessTextCapture -Capture $dedicatedTextCapture }
            catch {
                $runtimeOutcome = 'RunnerInternalError'
                $runtimeReason = 'Dedicated stdout/stderr capture finalization failed: ' + $_.Exception.Message
                $runtimeSupersession = 'DedicatedTextCaptureFailed'
            }
        }
        if ($null -ne $dedicatedIdentity -and
            -not [string]::IsNullOrWhiteSpace([string](Get-CoopOptionalPropertyValue -InputObject $dedicatedIdentity -Name 'ProcessStartUtc'))) {
            try {
                $dedicatedNativeLogInventory = Copy-CoopPidCorrelatedNativeLogs `
                    -ProcessIdentity $dedicatedIdentity `
                    -DestinationRoot (Join-Path $runRoot 'artifacts\logs\dedicated\native')
            }
            catch {
                $runtimeOutcome = 'RunnerInternalError'
                $runtimeReason = 'PID-correlated native log capture failed: ' + $_.Exception.Message
                $runtimeSupersession = 'NativeLogCaptureFailed'
            }
        }
        if ($null -ne $clientIdentity -and
            -not [string]::IsNullOrWhiteSpace([string](Get-CoopOptionalPropertyValue -InputObject $clientIdentity -Name 'ProcessStartUtc'))) {
            try {
                $clientNativeLogInventory = Copy-CoopPidCorrelatedNativeLogs `
                    -ProcessIdentity $clientIdentity `
                    -DestinationRoot (Join-Path $runRoot 'artifacts\logs\client\native')
            }
            catch {
                $runtimeOutcome = 'RunnerInternalError'
                $runtimeReason = 'Client PID-correlated native log capture failed: ' + $_.Exception.Message
                $runtimeSupersession = 'ClientNativeLogCaptureFailed'
            }
        }
    }

    $globalResultAfter = Get-CoopFileFact -Path $globalResultPath
    $globalResultUnchanged = ($globalResultBefore.Exists -eq $globalResultAfter.Exists) -and
        (-not $globalResultBefore.Exists -or [string]::Equals(
            [string]$globalResultBefore.Sha256,
            [string]$globalResultAfter.Sha256,
            [StringComparison]::Ordinal))
    if (-not $globalResultUnchanged) {
        if ($runtimeOutcome -ne 'RunnerInternalError') {
            $runtimeOutcome = 'AssertionFailed'
            $runtimeReason = 'The protected global battle_result.json changed during connection-only feasibility.'
        }
        else {
            $runtimeReason += ' The protected global battle_result.json also changed during connection-only feasibility.'
        }
    }
    $remainingOwnedProcesses = @($ownedRuntimeProcesses | Where-Object { Test-CoopLiveProcessIdentity -Identity $_ })
    if ($remainingOwnedProcesses.Count -gt 0) {
        if ($runtimeOutcome -ne 'RunnerInternalError') {
            $runtimeOutcome = 'RunnerInternalError'
            $runtimeReason = 'One or more exact owned runtime processes remained after cleanup.'
            $runtimeSupersession = 'OwnedProcessesRemaining'
        }
        else {
            $runtimeReason += ' One or more exact owned runtime processes also remained after cleanup.'
        }
    }
    if ([string]::IsNullOrWhiteSpace($runtimeSupersession)) {
        $primaryRuntimeOutcome = $runtimeOutcome
        $primaryRuntimeReason = $runtimeReason
    }

    $report = [ordered]@{
        Schema = 'coop-runtime-feasibility-v2'
        RunId = $RunId
        PrimaryOutcome = $primaryRuntimeOutcome
        PrimaryReason = $primaryRuntimeReason
        Outcome = $runtimeOutcome
        Reason = $runtimeReason
        OutcomeSupersession = $runtimeSupersession
        ServerName = $effectiveServerName
        ServerPort = $Port
        ServerBootstrapGameType = 'TeamDeathmatch'
        ServerBootstrapMap = 'mp_tdm_map_001'
        StartGameIssuedBy = 'DedicatedModuleNativeCommandHandler'
        CampaignStarted = $false
        CampaignBattleFixtureOpened = $false
        L2OrL3PassClaimed = $false
        ResultPolicy = 'Suppress'
        ExpectedClientModuleSha256 = $expectedClientHash
        ExpectedDedicatedModuleSha256 = $expectedDedicatedHash
        DedicatedRoleStatus = $dedicatedRoleStatus
        DedicatedControlReadinessEvidence = $dedicatedControlReadinessEvidence
        DedicatedBootstrapStatus = $dedicatedBootstrapStatus
        BootstrapAcknowledgementEvidence = $bootstrapCommandEvidence.ToArray()
        ClientRoleStatus = $clientRoleStatus
        ClientJoinStatus = $clientJoinStatus
        OwnedHostStatus = $ownedHostStatus
        GlobalBattleResultBefore = $globalResultBefore
        GlobalBattleResultAfter = $globalResultAfter
        GlobalBattleResultUnchanged = $globalResultUnchanged
        Cleanup = $runtimeCleanupEvidence.ToArray()
        NativeLogInventory = $dedicatedNativeLogInventory
        DedicatedNativeLogInventory = $dedicatedNativeLogInventory
        ClientNativeLogInventory = $clientNativeLogInventory
        RemainingOwnedProcesses = $remainingOwnedProcesses
        FailureEvidence = $runtimeFailureEvidence
        CompletedUtc = [DateTime]::UtcNow.ToString('O')
    }
    Write-CoopJsonAtomic -Path $reportPath -Value $report
    return [ordered]@{
        PrimaryOutcome = $primaryRuntimeOutcome
        PrimaryReason = $primaryRuntimeReason
        Outcome = $runtimeOutcome
        Reason = $runtimeReason
        ArtifactPath = $reportPath
    }
}

if (-not (Test-CoopRunId -Value $RunId)) {
    throw 'RunId must contain only ASCII letters, digits, dot, underscore, or hyphen, start with a letter/digit, and not exceed 80 characters.'
}
if ($Command -eq 'Inspect' -or $Command -eq 'Recover') {
    exit (Invoke-CoopExistingRunControl)
}
if ($Command -eq 'Cancel') {
    exit (Invoke-CoopExistingRunCancellation)
}
if ([System.IO.Directory]::Exists($runRoot)) {
    throw "Run root already exists. Use a fresh RunId or inspect/recover it explicitly: $runRoot"
}

try {
    foreach ($relativeDirectory in @(
        'commands\inbox', 'commands\processed', 'status', 'events', 'payloads',
        'artifacts\logs', 'artifacts\crashes', 'artifacts\results', 'artifacts\processes',
        'artifacts\identity', 'work')) {
        [System.IO.Directory]::CreateDirectory((Join-Path $runRoot $relativeDirectory)) | Out-Null
    }

    $lockStream = New-Object System.IO.FileStream(
        $lockPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
    Initialize-CoopCancellationSignalCore
    $cancellationSignalInstalled = $true
    $noncePlaintext = New-CoopNonce
    $nonceSha256 = Get-CoopSha256Text -Value $noncePlaintext
    $repositoryRevision = Get-CoopGitValue -Arguments @('rev-parse', 'HEAD')
    $repositoryDirty = -not [string]::IsNullOrWhiteSpace((Get-CoopGitValue -Arguments @('status', '--porcelain')))
    Resolve-CoopEnvironmentRoots
    $runnerParentProcessId = Get-CoopParentProcessId
    $runnerExecutableFact = Get-CoopFileFact -Path $runnerProcess.Path
    $runnerExecutableSha256 = if ($runnerExecutableFact.Exists) { $runnerExecutableFact.Sha256 } else { '' }
    $manifestGameExecutable = Get-CoopFileFact -Path (Join-Path $GameRoot 'bin\Win64_Shipping_Client\Bannerlord.exe')
    $manifestDedicatedExecutable = Get-CoopFileFact -Path (Join-Path $DedicatedServerRoot 'bin\Win64_Shipping_Server\DedicatedCustomServer.Starter.exe')
    $installedClientFact = Get-CoopFileFact -Path (Join-Path $GameRoot 'Modules\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll')
    $installedDedicatedFact = Get-CoopFileFact -Path (Join-Path $DedicatedServerRoot 'Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Server\CoopSpectator.dll')
    $repositoryClientFact = Get-CoopFileFact -Path (Join-Path $repositoryRoot 'Module\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll')
    $repositoryDedicatedFact = Get-CoopFileFact -Path (Join-Path $repositoryRoot 'Module\CoopSpectatorDedicated\bin\Win64_Shipping_Server\CoopSpectator.dll')
    $selectedClientFact = if ($Command -eq 'Doctor' -or $Command -eq 'Feasibility') { $installedClientFact } else { $repositoryClientFact }
    $selectedDedicatedFact = if ($Command -eq 'Doctor' -or $Command -eq 'Feasibility') { $installedDedicatedFact } else { $repositoryDedicatedFact }
    $expectedArtifactSource = if ($Command -eq 'Doctor') { 'InstalledAndRepositoryDiagnostic' }
        elseif ($Command -eq 'CompileOnly') { 'RunOwnedCompileOnlyOutput' }
        elseif ($Command -eq 'Feasibility') { 'ExplicitInstalledRuntimeIdentity' }
        else { 'RepositorySourceContracts' }
    $manifestPortOwnership = Get-CoopPortOwnership

    $manifest = [ordered]@{
        ManifestSchemaVersion = $manifestSchemaVersion
        ProtocolMajorVersion = $protocolMajorVersion
        ProtocolMinorVersion = $protocolMinorVersion
        RunId = $RunId
        CreatedUtc = $runCreatedUtc.ToString('O')
        RequestedCommand = $Command
        RequestedLevel = if ($Command -eq 'Doctor') { 'L0' } elseif ($Command -eq 'Feasibility') { 'Feasibility' } else { 'L1' }
        ScenarioKind = if ($Command -eq 'Feasibility') { 'ConnectionOnlyRuntimeFoundation' } else { 'NonRuntime' }
        Stage = if ($Command -eq 'Feasibility') { 'Milestone2B' } else { 'Milestone2A' }
        MachineProfileName = $MachineProfileName
        BuildProfile = 'Release'
        ExpectedArtifactSource = $expectedArtifactSource
        NonceSha256 = $nonceSha256
        RepositoryRevision = $repositoryRevision
        RepositoryDirty = $repositoryDirty
        RunnerBuildIdentity = 'scripts/Invoke-CoopTest.ps1@protocol-1.1'
        ClientModuleVersion = if ($selectedClientFact.Exists) { $selectedClientFact.ProductVersion } else { '' }
        ClientModuleSha256 = if ($selectedClientFact.Exists) { $selectedClientFact.Sha256 } else { '' }
        DedicatedModuleVersion = if ($selectedDedicatedFact.Exists) { $selectedDedicatedFact.ProductVersion } else { '' }
        DedicatedModuleSha256 = if ($selectedDedicatedFact.Exists) { $selectedDedicatedFact.Sha256 } else { '' }
        GameExecutableVersion = if ($manifestGameExecutable.Exists) { $manifestGameExecutable.ProductVersion } else { '' }
        DedicatedExecutableVersion = if ($manifestDedicatedExecutable.Exists) { $manifestDedicatedExecutable.ProductVersion } else { '' }
        EffectiveFeatureFlags = [ordered]@{
            CoopCompileOnly = ($Command -eq 'CompileOnly')
            TestAutomation = ($Command -eq 'Feasibility') -or (Test-CoopEnvironmentFlag -Name 'COOPSPECTATOR_TEST_AUTOMATION')
            VerboseDiagnostics = Test-CoopEnvironmentFlag -Name 'COOPSPECTATOR_VERBOSE_DIAGNOSTICS'
            CampaignMapPrototype = Test-CoopEnvironmentFlag -Name 'COOPSPECTATOR_CAMPAIGN_MAP_PROTOTYPE'
        }
        ResultPolicy = if ($Command -eq 'Feasibility') { 'Suppress' } else { 'NotApplicable' }
        CompletionMode = 'NotApplicable'
        Roles = @([ordered]@{
            RoleType = $runnerRoleType
            RoleInstanceId = $runnerRoleInstanceId
                Capabilities = $runnerCapabilities
            ExecutablePath = $runnerProcess.Path
            ExecutableSha256 = $runnerExecutableSha256
            ProcessId = $PID
            ParentProcessId = $runnerParentProcessId
            ProcessStartUtc = $runnerProcessStartUtc.ToString('O')
        })
        RequiredPorts = $requiredPorts
        PortInspectionAvailable = $manifestPortOwnership.InspectionAvailable
        Ports = @($manifestPortOwnership.Entries)
        InputFixtures = @()
        ArtifactCategories = [ordered]@{
            Manifest = 'Run identity, profile, binary identity, and terminal outcome; retained with the run.'
            Commands = if ($Command -eq 'Feasibility') { 'Run-scoped dedicated bootstrap and client-join requests with atomic acknowledgements.' } else { 'Atomic inbox and processed control records; none are issued by Milestone 2A commands.' }
            Status = 'Atomic role-instance state snapshots; retained with the run.'
            Events = 'Append-only ordered role event journal; retained with the run.'
            Payloads = 'Exact future fixture payloads; empty in Milestone 2A.'
            Logs = 'Per-command combined, stdout, and stderr logs; retained with the run.'
            Results = 'Aggregate and assertion reports; retained with the run.'
            Processes = 'Ownership and lock-release evidence; retained with the run.'
            Identity = 'Environment and installed-module identity evidence; retained with the run.'
            Work = 'Run-owned temporary outputs and package cache; explicitly cleaned only by exact-root future cleanup.'
        }
        StateDeadlinesSeconds = [ordered]@{ Lease = $leaseLifetimeMinutes * 60 }
        ReproductionDescriptorPath = ''
        PrimaryOutcome = ''
        PrimaryReason = ''
        TerminalOutcome = ''
        TerminalReason = ''
        CompletedUtc = $null
    }
    Write-CoopJsonAtomic -Path $manifestPath -Value $manifest
    Update-CoopLease
    if ($Command -eq 'Feasibility') {
        $sharedLockRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'CoopSpectator\Automation\_locks'
        $sharedRuntimePorts = [int[]]@($Port, 7777)
        $expectedSharedRuntimeResourceCount = 4 + @($sharedRuntimePorts | Sort-Object -Unique).Count
        $sharedRuntimeResourceIds = @(Get-CoopSharedRuntimeResourceIdsCore `
            -AutomationRoot ([System.IO.Path]::GetDirectoryName($runRoot)) `
            -GameRoot $GameRoot `
            -DedicatedServerRoot $DedicatedServerRoot `
            -ComputerName $env:COMPUTERNAME `
            -MachineProfileName $MachineProfileName `
            -UdpPorts $sharedRuntimePorts)
        if ($sharedRuntimeResourceIds.Count -ne $expectedSharedRuntimeResourceCount) {
            throw "Shared runtime resource construction produced $($sharedRuntimeResourceIds.Count) ids; expected $expectedSharedRuntimeResourceCount."
        }
        try {
            $sharedRuntimeLockSet = Enter-CoopSharedRuntimeLocksCore `
                -LockRoot $sharedLockRoot `
                -ResourceIds $sharedRuntimeResourceIds `
                -RunId $RunId `
                -OwnerProcessId $PID `
                -OwnerProcessStartUtc $runnerProcessStartUtc
        }
        catch {
            $blocked = [System.InvalidOperationException]::new(
                'Shared runtime lock acquisition failed: ' + $_.Exception.Message,
                $_.Exception)
            $blocked.Data['CoopRuntimeOutcome'] = 'EnvironmentBlocked'
            throw $blocked
        }
        $acquiredSharedRuntimeResourceIds = @(
            $sharedRuntimeLockSet.Records | ForEach-Object { [string]$_.ResourceId })
        if ($acquiredSharedRuntimeResourceIds.Count -ne $expectedSharedRuntimeResourceCount -or
            @(Compare-Object $sharedRuntimeResourceIds $acquiredSharedRuntimeResourceIds).Count -ne 0) {
            $unexpectedLockRelease = @(Exit-CoopSharedRuntimeLocksCore -LockSet $sharedRuntimeLockSet)
            $sharedRuntimeLockSet = $null
            throw 'Shared runtime lock acquisition did not retain every exact requested resource id.'
        }
        Write-CoopJsonAtomic -Path (Join-Path $runRoot 'artifacts\processes\shared-runtime-locks.json') -Value ([ordered]@{
            Schema = 'coop-shared-runtime-locks-v1'
            RunId = $RunId
            CanonicalOrder = @($sharedRuntimeLockSet.Records | ForEach-Object { $_.ResourceId })
            Locks = @($sharedRuntimeLockSet.Records)
        })
    }
    Add-CoopEvent -EventType 'RunStarted' -Message $(if ($Command -eq 'Feasibility') {
        'Feasibility started with exact runtime ownership and ResultPolicy=Suppress.'
    } else {
        $Command + ' started; no product process launch is permitted in Milestone 2A.'
    })
    Write-CoopRunnerStatus -State 'Running' -Outcome '' -Reason ($Command + ' started.')

    $commandResults = @(switch ($Command) {
        'Doctor' { Invoke-CoopDoctor }
        'Contracts' { Invoke-CoopContracts }
        'CompileOnly' { Invoke-CoopCompileOnly }
        'Feasibility' { Invoke-CoopFeasibility }
    })
    $commandResult = Get-CoopSingularCommandResult -Results $commandResults -CommandName $Command
    $finalOutcome = $commandResult.Outcome
    $finalReason = $commandResult.Reason
    $primaryOutcomeProperty = $commandResult.PSObject.Properties['PrimaryOutcome']
    $primaryReasonProperty = $commandResult.PSObject.Properties['PrimaryReason']
    $manifest.PrimaryOutcome = if ($null -ne $primaryOutcomeProperty) {
        [string]$primaryOutcomeProperty.Value
    }
    else { [string]$commandResult.Outcome }
    $manifest.PrimaryReason = if ($null -ne $primaryReasonProperty) {
        [string]$primaryReasonProperty.Value
    }
    else { [string]$commandResult.Reason }
    if ($finalOutcome -ne 'Pass') {
        $manifest.ReproductionDescriptorPath = Write-CoopReproductionDescriptor -Outcome $finalOutcome -Reason $finalReason
    }
}
catch {
    $outcomeHint = [string]$_.Exception.Data['CoopRuntimeOutcome']
    $finalOutcome = if ($outcomeExitCodes.ContainsKey($outcomeHint)) { $outcomeHint }
        elseif ($_.Exception -is [System.OperationCanceledException]) { 'Cancelled' }
        else { 'RunnerInternalError' }
    $finalReason = $_.Exception.Message
    if ($null -ne $manifest -and -not [string]::IsNullOrWhiteSpace($nonceSha256)) {
        try { Add-CoopEvent -EventType $finalOutcome -Message $finalReason } catch { }
    }
}

if ($null -ne $sharedRuntimeLockSet) {
    try {
        $sharedRuntimeLockReleaseEvidence = @(Exit-CoopSharedRuntimeLocksCore -LockSet $sharedRuntimeLockSet)
        $sharedRuntimeLockSet = $null
        Write-CoopJsonAtomic -Path (Join-Path $runRoot 'artifacts\processes\shared-runtime-lock-release.json') -Value ([ordered]@{
            Schema = 'coop-shared-runtime-lock-release-v1'
            RunId = $RunId
            VerifiedUtc = [DateTime]::UtcNow.ToString('O')
            Locks = $sharedRuntimeLockReleaseEvidence
        })
        if (@($sharedRuntimeLockReleaseEvidence | Where-Object { -not $_.ReleasedAndReacquired }).Count -gt 0) {
            throw 'One or more shared runtime locks could not be reacquired after release.'
        }
    }
    catch {
        $finalOutcome = 'RunnerInternalError'
        $finalReason = 'Shared runtime lock release could not be verified: ' + $_.Exception.Message
    }
}

if ($finalOutcome -eq 'Cancelled' -and $null -ne $manifest) {
    try { Complete-CoopCancellation -Reason $finalReason }
    catch {
        $finalOutcome = 'RunnerInternalError'
        $finalReason = 'Cancellation acknowledgement failed: ' + $_.Exception.Message
    }
}

if ($null -ne $manifest) {
    try {
        Add-CoopEvent -EventType 'RunCompleted' -Message ($finalOutcome + ': ' + $finalReason)
        if ([string]::IsNullOrWhiteSpace([string]$manifest.PrimaryOutcome)) {
            $manifest.PrimaryOutcome = $finalOutcome
            $manifest.PrimaryReason = $finalReason
        }
        $manifest.TerminalOutcome = $finalOutcome
        $manifest.TerminalReason = $finalReason
        $manifest.CompletedUtc = [DateTime]::UtcNow.ToString('O')
        Write-CoopJsonAtomic -Path $manifestPath -Value $manifest
        Write-CoopRunnerStatus `
            -State $(if ($finalOutcome -eq 'Cancelled') { 'Cancelled' } else { 'Completed' }) `
            -Outcome $finalOutcome `
            -Reason $finalReason `
            -IsTerminal $true
        Update-CoopLease -Status 'Completed'
    }
    catch {
        $finalOutcome = 'RunnerInternalError'
        $finalReason = 'Final artifact publication failed: ' + $_.Exception.Message
    }
}

if ($null -ne $lockStream) {
    $lockStream.Dispose()
    $lockStream = $null
    try {
        $releaseProbe = New-Object System.IO.FileStream(
            $lockPath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
        $releaseProbe.Dispose()
        $releaseVerified = $true
    }
    catch {
        $finalOutcome = 'RunnerInternalError'
        $finalReason = 'Runner lock release could not be verified: ' + $_.Exception.Message
    }
}

if ($cancellationSignalInstalled) {
    try { Remove-CoopCancellationSignalCore } catch { }
    $cancellationSignalInstalled = $false
}

if ($null -ne $manifest) {
    try {
        Write-CoopJsonAtomic -Path (Join-Path $runRoot 'artifacts\processes\runner-lock-release.json') -Value ([ordered]@{
            RunId = $RunId
            LockPath = $lockPath
            ReleasedAndReacquired = $releaseVerified
            VerifiedUtc = [DateTime]::UtcNow.ToString('O')
        })
    }
    catch {
        $finalOutcome = 'RunnerInternalError'
        $finalReason = 'Lock-release artifact could not be written: ' + $_.Exception.Message
    }
}

$exitCode = if ($outcomeExitCodes.ContainsKey($finalOutcome)) { $outcomeExitCodes[$finalOutcome] } else { 40 }
Write-Host ("[{0}] {1}: {2}" -f $finalOutcome, $Command, $finalReason)
if ($null -ne $manifest) {
    Write-Host ("[ARTIFACTS] {0}" -f $runRoot)
}
exit $exitCode
