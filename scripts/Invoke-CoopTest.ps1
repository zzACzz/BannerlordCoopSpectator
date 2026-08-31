[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Doctor', 'Contracts', 'CompileOnly', 'Feasibility', 'Inspect', 'Recover')]
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

$protocolMajorVersion = 1
$protocolMinorVersion = 0
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
$lockStream = $null
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

    $status = [ordered]@{
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
        UpdatedUtc = [DateTime]::UtcNow.ToString('O')
        ProcessId = $PID
        ProcessStartUtc = $runnerProcessStartUtc.ToString('O')
        Capabilities = @('Doctor', 'Contracts', 'CompileOnly', 'Feasibility', 'Inspect', 'Recover', 'AtomicManifest', 'LeaseHeartbeat')
        ExecutablePath = $runnerProcess.Path
        ExecutableSha256 = $runnerExecutableSha256
        NonceCorrelation = 'Confirmed'
        CampaignId = ''
        BattleInstanceId = ''
        BattleStage = ''
        AuthoritativeSource = 'scripts/Invoke-CoopTest.ps1'
        State = $State
        Outcome = $Outcome
        Reason = $Reason
        IsTerminal = $IsTerminal
    }
    Write-CoopJsonAtomic -Path $runnerStatusPath -Value $status
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
        [Parameter(Mandatory = $true)][string]$RoleInstanceId
    )

    $process = Get-Process -Id $ProcessId -ErrorAction Stop
    $path = $process.Path
    $startUtc = $process.StartTime.ToUniversalTime()
    $parentProcessId = 0
    try {
        $record = Get-CimInstance -ClassName Win32_Process -Filter ("ProcessId=" + $ProcessId) -ErrorAction Stop
        if ($null -ne $record) { $parentProcessId = [int]$record.ParentProcessId }
    }
    catch { }
    return [ordered]@{
        RoleType = $RoleType
        RoleInstanceId = $RoleInstanceId
        ProcessId = $ProcessId
        ParentProcessId = $parentProcessId
        ProcessStartUtc = $startUtc.ToString('O')
        ExecutablePath = $path
        ExecutableSha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
        RegisteredUtc = [DateTime]::UtcNow.ToString('O')
    }
}

function Test-CoopLiveProcessIdentity {
    param([Parameter(Mandatory = $true)]$Identity)

    if ($null -eq $Identity -or [int]$Identity.ProcessId -le 0) { return $false }
    try {
        $process = Get-Process -Id ([int]$Identity.ProcessId) -ErrorAction Stop
        $actualPath = [System.IO.Path]::GetFullPath($process.Path)
        $expectedPath = [System.IO.Path]::GetFullPath([string]$Identity.ExecutablePath)
        $actualStartUtc = $process.StartTime.ToUniversalTime()
        $expectedStartUtc = [DateTime]::Parse([string]$Identity.ProcessStartUtc).ToUniversalTime()
        return [string]::Equals($actualPath, $expectedPath, [StringComparison]::OrdinalIgnoreCase) -and
            [Math]::Abs(($actualStartUtc - $expectedStartUtc).TotalSeconds) -lt 1.0
    }
    catch {
        return $false
    }
}

function Add-CoopOwnedRuntimeProcess {
    param([Parameter(Mandatory = $true)]$Identity)

    $duplicate = @($ownedRuntimeProcesses | Where-Object {
        [int]$_.ProcessId -eq [int]$Identity.ProcessId -and
            [string]::Equals([string]$_.ProcessStartUtc, [string]$Identity.ProcessStartUtc, [StringComparison]::Ordinal)
    }).Count -gt 0
    if (-not $duplicate) { $ownedRuntimeProcesses.Add($Identity) }
    Write-CoopJsonAtomic -Path (Join-Path $runRoot 'artifacts\processes\runtime-owned-processes.json') -Value ([ordered]@{
        Schema = 'coop-runtime-process-inventory-v1'
        RunId = $RunId
        UpdatedUtc = [DateTime]::UtcNow.ToString('O')
        Processes = $ownedRuntimeProcesses.ToArray()
    })
}

function Add-CoopOwnedDescendants {
    param([Parameter(Mandatory = $true)][int[]]$RootProcessIds)

    $records = @(Get-CimInstance -ClassName Win32_Process -ErrorAction SilentlyContinue)
    foreach ($record in $records) {
        $candidateId = [int]$record.ProcessId
        if ($candidateId -le 0 -or $RootProcessIds -contains $candidateId) { continue }
        foreach ($rootProcessId in $RootProcessIds) {
            if (-not (Test-CoopProcessDescendsFrom -ProcessId $candidateId -AncestorProcessId $rootProcessId)) { continue }
            try {
                $identity = Get-CoopProcessIdentity -ProcessId $candidateId -RoleType 'RuntimeSupport' -RoleInstanceId ('runtime-support-' + $candidateId)
                Add-CoopOwnedRuntimeProcess -Identity $identity
            }
            catch { }
            break
        }
    }
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

    $evidence = [ordered]@{
        RoleType = [string]$Identity.RoleType
        RoleInstanceId = [string]$Identity.RoleInstanceId
        ProcessId = [int]$Identity.ProcessId
        IdentityMatched = $false
        GracefulCloseRequested = $false
        ForcedStopUsed = $false
        Outcome = 'NotRunning'
        CheckedUtc = [DateTime]::UtcNow.ToString('O')
    }
    if (-not (Test-CoopLiveProcessIdentity -Identity $Identity)) {
        $runtimeCleanupEvidence.Add($evidence)
        return
    }

    $evidence.IdentityMatched = $true
    $process = Get-Process -Id ([int]$Identity.ProcessId) -ErrorAction Stop
    try { $evidence.GracefulCloseRequested = [bool]$process.CloseMainWindow() } catch { }
    try { $process.WaitForExit($GraceSeconds * 1000) } catch { }
    if (-not $process.HasExited) {
        if (-not (Test-CoopLiveProcessIdentity -Identity $Identity)) {
            $evidence.Outcome = 'IdentityChangedBeforeForcedStop'
            $runtimeCleanupEvidence.Add($evidence)
            return
        }
        $evidence.ForcedStopUsed = $true
        $process.Kill()
        $process.WaitForExit(10000)
    }
    $evidence.Outcome = if ($process.HasExited) { 'Stopped' } else { 'StopFailed' }
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

function Wait-CoopRuntimeRoleReady {
    param(
        [Parameter(Mandatory = $true)][string]$StatusPath,
        [Parameter(Mandatory = $true)][string]$ExpectedRoleType,
        [Parameter(Mandatory = $true)][string]$ExpectedModuleSha256,
        [Parameter(Mandatory = $true)][DateTime]$DeadlineUtc
    )

    while ([DateTime]::UtcNow -lt $DeadlineUtc) {
        $status = Read-CoopJsonShared -Path $StatusPath
        if ($null -ne $status) {
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

function Wait-CoopClientConnection {
    param(
        [Parameter(Mandatory = $true)][string]$StatusPath,
        [Parameter(Mandatory = $true)][DateTime]$DeadlineUtc
    )

    while ([DateTime]::UtcNow -lt $DeadlineUtc) {
        $status = Read-CoopJsonShared -Path $StatusPath
        if ($null -ne $status) {
            if ([string]$status.RunId -ne $RunId) { throw 'Client join status RunId mismatch.' }
            if ([string]$status.RunTokenSha256 -ne $nonceSha256) { throw 'Client join status token mismatch.' }
            if ([string]$status.State -eq 'Failed') {
                throw ("Client join failed: " + [string]$status.FailureCode + ': ' + [string]$status.FailureMessage)
            }
            if ([string]$status.State -eq 'Connected') { return $status }
        }
        Update-CoopLease
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for the multiplayer client to connect."
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

        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $standardOutput = $standardOutputTask.Result
        $standardError = $standardErrorTask.Result
        $exitCode = $process.ExitCode
    }
    finally {
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
        return [ordered]@{ Outcome = 'EnvironmentBlocked'; Reason = 'dotnet.exe is unavailable.' }
    }

    $inventoryPath = Join-Path $repositoryRoot 'Tests\contract-tests.manifest.json'
    if (-not [System.IO.File]::Exists($inventoryPath)) {
        return [ordered]@{ Outcome = 'PreconditionsFailed'; Reason = 'The canonical contract-test manifest is missing.' }
    }
    $inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
    if ($inventory.SchemaVersion -ne 1 -or $null -eq $inventory.Projects) {
        return [ordered]@{ Outcome = 'PreconditionsFailed'; Reason = 'The canonical contract-test manifest schema is invalid.' }
    }

    $declaredPaths = @($inventory.Projects | ForEach-Object { $_.Path.Replace('/', '\') } | Sort-Object)
    $discoveredPaths = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'Tests') -Recurse -Filter '*.csproj' | ForEach-Object {
        $_.FullName.Substring($repositoryRoot.Length).TrimStart('\', '/')
    } | Sort-Object)
    $inventoryDifference = @(Compare-Object -ReferenceObject $declaredPaths -DifferenceObject $discoveredPaths)
    if ($inventoryDifference.Count -ne 0) {
        return [ordered]@{ Outcome = 'PreconditionsFailed'; Reason = 'The canonical contract-test manifest does not exactly match discovered projects.' }
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
        return [ordered]@{ Outcome = 'EnvironmentBlocked'; Reason = 'dotnet.exe is unavailable.' }
    }
    Resolve-CoopEnvironmentRoots
    if (-not [System.IO.Directory]::Exists($GameRoot) -or -not [System.IO.Directory]::Exists($DedicatedServerRoot)) {
        return [ordered]@{ Outcome = 'EnvironmentBlocked'; Reason = 'The configured game or dedicated installation root is missing.' }
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
    $processes = @(
        if ($inventoryValid -and $null -ne $inventoryProcessesProperty -and $null -ne $inventoryProcessesProperty.Value) {
            @($inventoryProcessesProperty.Value)
        }
    )
    $liveProcesses = @($processes | Where-Object { Test-CoopLiveProcessIdentity -Identity $_ })
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
        RecordedProcessCount = $processes.Count
        LiveExactProcessCount = $liveProcesses.Count
        LiveExactProcesses = $liveProcesses
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

    if (-not $manifestValid -or -not $leaseValid -or -not $inventoryValid) {
        Write-Host '[EnvironmentBlocked] Recovery apply requires readable matching manifest, lease, and any present process inventory.'
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
        $report = [ordered]@{
            Schema = 'coop-runtime-recovery-v1'
            RunId = $RunId
            AppliedUtc = [DateTime]::UtcNow.ToString('O')
            RecordedProcessCount = $processes.Count
            ExactLiveProcessCountBefore = $liveProcesses.Count
            ExactLiveProcessCountAfter = $remaining.Count
            Actions = $runtimeCleanupEvidence.ToArray()
            Outcome = if ($remaining.Count -eq 0) { 'Recovered' } else { 'RecoveryIncomplete' }
        }
        Write-CoopJsonAtomic -Path (Join-Path $runRoot 'artifacts\processes\recovery.json') -Value $report
        Write-Host ($report | ConvertTo-Json -Depth 20)
        return $(if ($remaining.Count -eq 0) { 0 } else { 20 })
    }
    finally {
        $recoveryLock.Dispose()
    }
}

function Invoke-CoopFeasibility {
    $effectiveServerName = if ([string]::IsNullOrWhiteSpace($ServerName)) {
        $candidate = 'AC_COOP_' + $RunId
        if ($candidate.Length -gt 120) { $candidate.Substring(0, 120) } else { $candidate }
    }
    else { $ServerName.Trim() }
    if ([string]::IsNullOrWhiteSpace($effectiveServerName) -or $effectiveServerName.Length -gt 128 -or $effectiveServerName -match '[\x00-\x1F\x7F]') {
        return [ordered]@{ Outcome = 'PreconditionsFailed'; Reason = 'The runtime server name is invalid.'; ArtifactPath = '' }
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
    $dedicatedRoleStatus = $null
    $clientRoleStatus = $null
    $clientJoinStatus = $null
    $ownedHostStatus = $null
    $dedicatedProcess = $null

    try {
        $dedicatedExecutable = Join-Path $DedicatedServerRoot 'bin\Win64_Shipping_Server\DedicatedCustomServer.Starter.exe'
        $dedicatedLogRoot = Join-Path $runRoot 'artifacts\logs\dedicated'
        [System.IO.Directory]::CreateDirectory($dedicatedLogRoot) | Out-Null
        $dedicatedStartInfo = New-Object System.Diagnostics.ProcessStartInfo
        $dedicatedStartInfo.FileName = $dedicatedExecutable
        $dedicatedStartInfo.WorkingDirectory = Split-Path -Parent $dedicatedExecutable
        $dedicatedStartInfo.UseShellExecute = $false
        $dedicatedStartInfo.CreateNoWindow = $false
        $dedicatedStartInfo.RedirectStandardInput = $true
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
        if (-not $dedicatedProcess.Start()) { throw 'Dedicated server process creation returned false.' }
        $dedicatedIdentity = Get-CoopProcessIdentity -ProcessId $dedicatedProcess.Id -RoleType 'DedicatedServer' -RoleInstanceId 'dedicated-server-01'
        Add-CoopOwnedRuntimeProcess -Identity $dedicatedIdentity
        Add-CoopEvent -EventType 'DedicatedProcessStarted' -Message ("PID=" + $dedicatedProcess.Id + '; awaiting exact module identity.')

        $roleDeadline = [DateTime]::UtcNow.AddSeconds([Math]::Min(180, $RuntimeTimeoutSeconds))
        $dedicatedRoleStatus = Wait-CoopRuntimeRoleReady `
            -StatusPath (Join-Path $runRoot 'status\dedicated-server-01.json') `
            -ExpectedRoleType 'DedicatedServer' `
            -ExpectedModuleSha256 $expectedDedicatedHash `
            -DeadlineUtc $roleDeadline

        foreach ($serverCommand in @(
            'ServerName ' + $effectiveServerName,
            'MaxNumberOfPlayers 16',
            'GameType TeamDeathmatch',
            'Map mp_tdm_map_001',
            'add_map_to_usable_maps mp_tdm_map_001 TeamDeathmatch',
            'start_game')) {
            $dedicatedProcess.StandardInput.WriteLine($serverCommand)
            $dedicatedProcess.StandardInput.Flush()
            Add-CoopEvent -EventType 'DedicatedConsoleCommandSent' -Message $serverCommand
        }

        $portDeadline = [DateTime]::UtcNow.AddSeconds([Math]::Min(180, $RuntimeTimeoutSeconds))
        $udpEndpoint = $null
        while ([DateTime]::UtcNow -lt $portDeadline) {
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
                -UniqueMapId 'mp_tdm_map_001' `
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
        $clientIdentity = Get-CoopProcessIdentity -ProcessId ([int]$clientLaunch.EntryPid) -RoleType 'MultiplayerClient' -RoleInstanceId 'multiplayer-client-01'
        if (-not [string]::Equals(
                [string]$clientIdentity.ExecutablePath,
                [string]$clientLaunch.EntryPath,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Live client process path does not match the launcher identity.'
        }
        Add-CoopOwnedRuntimeProcess -Identity $clientIdentity

        $clientDeadline = [DateTime]::UtcNow.AddSeconds($RuntimeTimeoutSeconds)
        $clientRoleStatus = Wait-CoopRuntimeRoleReady `
            -StatusPath (Join-Path $runRoot 'status\multiplayer-client-01.json') `
            -ExpectedRoleType 'MultiplayerClient' `
            -ExpectedModuleSha256 $expectedClientHash `
            -DeadlineUtc $clientDeadline
        $clientJoinStatus = Wait-CoopClientConnection `
            -StatusPath (Join-Path $runRoot 'state\client-join.status.json') `
            -DeadlineUtc $clientDeadline

        $runtimeOutcome = 'Pass'
        $runtimeReason = 'Exact client and dedicated module identities were confirmed and the normal lobby path connected to the owned local server.'
    }
    catch {
        $runtimeReason = $_.Exception.Message
        $runtimeOutcome = if ($runtimeReason -match 'Timed out') { 'Timeout' }
        elseif ($runtimeReason -match 'exited|crash') { 'Crash' }
        else { 'AssertionFailed' }
    }
    finally {
        try {
            $rootProcessIds = @($ownedRuntimeProcesses | Where-Object {
                [string]$_.RoleType -eq 'DedicatedServer' -or [string]$_.RoleType -eq 'MultiplayerClient'
            } | ForEach-Object { [int]$_.ProcessId } | Select-Object -Unique)
            if ($rootProcessIds.Count -gt 0) { Add-CoopOwnedDescendants -RootProcessIds $rootProcessIds }
        }
        catch { }
        try { Stop-CoopOwnedRuntimeProcesses }
        catch {
            $runtimeOutcome = 'RunnerInternalError'
            $runtimeReason = 'Exact runtime cleanup failed: ' + $_.Exception.Message
        }
    }

    $globalResultAfter = Get-CoopFileFact -Path $globalResultPath
    $globalResultUnchanged = ($globalResultBefore.Exists -eq $globalResultAfter.Exists) -and
        (-not $globalResultBefore.Exists -or [string]::Equals(
            [string]$globalResultBefore.Sha256,
            [string]$globalResultAfter.Sha256,
            [StringComparison]::Ordinal))
    if (-not $globalResultUnchanged) {
        $runtimeOutcome = 'AssertionFailed'
        $runtimeReason = 'The protected global battle_result.json changed during connection-only feasibility.'
    }
    $remainingOwnedProcesses = @($ownedRuntimeProcesses | Where-Object { Test-CoopLiveProcessIdentity -Identity $_ })
    if ($remainingOwnedProcesses.Count -gt 0) {
        $runtimeOutcome = 'AssertionFailed'
        $runtimeReason = 'One or more exact owned runtime processes remained after cleanup.'
    }

    $report = [ordered]@{
        Schema = 'coop-runtime-feasibility-v1'
        RunId = $RunId
        Outcome = $runtimeOutcome
        Reason = $runtimeReason
        ServerName = $effectiveServerName
        ServerPort = $Port
        ServerBootstrapGameType = 'TeamDeathmatch'
        ServerBootstrapMap = 'mp_tdm_map_001'
        StartGameIssuedBy = 'RunnerDedicatedStandardInput'
        CampaignStarted = $false
        CampaignBattleFixtureOpened = $false
        L2OrL3PassClaimed = $false
        ResultPolicy = 'Suppress'
        ExpectedClientModuleSha256 = $expectedClientHash
        ExpectedDedicatedModuleSha256 = $expectedDedicatedHash
        DedicatedRoleStatus = $dedicatedRoleStatus
        ClientRoleStatus = $clientRoleStatus
        ClientJoinStatus = $clientJoinStatus
        OwnedHostStatus = $ownedHostStatus
        GlobalBattleResultBefore = $globalResultBefore
        GlobalBattleResultAfter = $globalResultAfter
        GlobalBattleResultUnchanged = $globalResultUnchanged
        Cleanup = $runtimeCleanupEvidence.ToArray()
        RemainingOwnedProcesses = $remainingOwnedProcesses
        CompletedUtc = [DateTime]::UtcNow.ToString('O')
    }
    Write-CoopJsonAtomic -Path $reportPath -Value $report
    return [ordered]@{ Outcome = $runtimeOutcome; Reason = $runtimeReason; ArtifactPath = $reportPath }
}

if (-not (Test-CoopRunId -Value $RunId)) {
    throw 'RunId must contain only ASCII letters, digits, dot, underscore, or hyphen, start with a letter/digit, and not exceed 80 characters.'
}
if ($Command -eq 'Inspect' -or $Command -eq 'Recover') {
    exit (Invoke-CoopExistingRunControl)
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
        RunnerBuildIdentity = 'scripts/Invoke-CoopTest.ps1@protocol-1.0'
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
            Capabilities = @('Doctor', 'Contracts', 'CompileOnly', 'Feasibility', 'Inspect', 'Recover', 'AtomicManifest', 'LeaseHeartbeat')
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
            Commands = if ($Command -eq 'Feasibility') { 'Run-scoped client join request and dedicated console-command evidence.' } else { 'Atomic inbox and processed control records; none are issued by Milestone 2A commands.' }
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
        TerminalOutcome = ''
        TerminalReason = ''
        CompletedUtc = $null
    }
    Write-CoopJsonAtomic -Path $manifestPath -Value $manifest
    Update-CoopLease
    Add-CoopEvent -EventType 'RunStarted' -Message $(if ($Command -eq 'Feasibility') {
        'Feasibility started with exact runtime ownership and ResultPolicy=Suppress.'
    } else {
        $Command + ' started; no product process launch is permitted in Milestone 2A.'
    })
    Write-CoopRunnerStatus -State 'Running' -Outcome '' -Reason ($Command + ' started.')

    $commandResult = switch ($Command) {
        'Doctor' { Invoke-CoopDoctor }
        'Contracts' { Invoke-CoopContracts }
        'CompileOnly' { Invoke-CoopCompileOnly }
        'Feasibility' { Invoke-CoopFeasibility }
    }
    $finalOutcome = $commandResult.Outcome
    $finalReason = $commandResult.Reason
    if ($finalOutcome -ne 'Pass') {
        $manifest.ReproductionDescriptorPath = Write-CoopReproductionDescriptor -Outcome $finalOutcome -Reason $finalReason
    }
}
catch {
    $finalOutcome = 'RunnerInternalError'
    $finalReason = $_.Exception.Message
    if ($null -ne $manifest -and -not [string]::IsNullOrWhiteSpace($nonceSha256)) {
        try { Add-CoopEvent -EventType 'RunnerInternalError' -Message $finalReason } catch { }
    }
}

if ($null -ne $manifest) {
    try {
        Add-CoopEvent -EventType 'RunCompleted' -Message ($finalOutcome + ': ' + $finalReason)
        $manifest.TerminalOutcome = $finalOutcome
        $manifest.TerminalReason = $finalReason
        $manifest.CompletedUtc = [DateTime]::UtcNow.ToString('O')
        Write-CoopJsonAtomic -Path $manifestPath -Value $manifest
        Write-CoopRunnerStatus -State 'Completed' -Outcome $finalOutcome -Reason $finalReason -IsTerminal $true
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
