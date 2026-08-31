function New-CoopDedicatedBootstrapRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RunId,
        [Parameter(Mandatory = $true)][string]$RunTokenSha256,
        [Parameter(Mandatory = $true)][string]$ExpectedDedicatedModuleSha256,
        [Parameter(Mandatory = $true)][int]$ExpectedProcessId,
        [Parameter(Mandatory = $true)][DateTime]$ExpectedProcessStartUtc,
        [Parameter(Mandatory = $true)][string]$ExpectedExecutablePath,
        [Parameter(Mandatory = $true)][Guid]$CommandId,
        [Parameter(Mandatory = $true)][DateTime]$CreatedUtc,
        [Parameter(Mandatory = $true)][DateTime]$ExpiresUtc,
        [Parameter(Mandatory = $true)][string]$ServerName,
        [ValidateRange(16, 16)][int]$MaxNumberOfPlayers = 16,
        [ValidateSet('TeamDeathmatch')][string]$GameType = 'TeamDeathmatch',
        [ValidateSet('mp_tdm_map_001')][string]$Map = 'mp_tdm_map_001'
    )

    if ($RunId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$') { throw 'RunId is invalid.' }
    if ($RunTokenSha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'RunTokenSha256 is invalid.' }
    if ($ExpectedDedicatedModuleSha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'ExpectedDedicatedModuleSha256 is invalid.' }
    if ($ExpectedProcessId -le 0) { throw 'ExpectedProcessId must be positive.' }
    if ($ExpectedProcessStartUtc -eq [DateTime]::MinValue) { throw 'ExpectedProcessStartUtc is required.' }
    if ([string]::IsNullOrWhiteSpace($ExpectedExecutablePath)) { throw 'ExpectedExecutablePath is required.' }
    if ($CommandId -eq [Guid]::Empty) { throw 'CommandId must be non-empty.' }
    if ($CreatedUtc -eq [DateTime]::MinValue -or $ExpiresUtc -le $CreatedUtc) { throw 'The request lifetime is invalid.' }
    if ($ExpiresUtc.ToUniversalTime() - $CreatedUtc.ToUniversalTime() -gt [TimeSpan]::FromMinutes(10)) {
        throw 'The request lifetime exceeds ten minutes.'
    }
    if ($ServerName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$') {
        throw 'ServerName must contain only ASCII letters, digits, dot, underscore, or hyphen.'
    }

    return [ordered]@{
        SchemaVersion = 1
        ProtocolMajorVersion = 1
        ProtocolMinorVersion = 0
        RunId = $RunId
        Sequence = 1
        CommandId = $CommandId.ToString('D')
        SourceRoleType = 'Runner'
        SourceRoleInstanceId = 'runner-01'
        TargetRoleType = 'DedicatedServer'
        TargetRoleInstanceId = 'dedicated-server-01'
        CreatedUtc = $CreatedUtc.ToUniversalTime().ToString('O')
        ExpiresUtc = $ExpiresUtc.ToUniversalTime().ToString('O')
        RunTokenSha256 = $RunTokenSha256.ToUpperInvariant()
        ExpectedDedicatedModuleSha256 = $ExpectedDedicatedModuleSha256.ToUpperInvariant()
        ExpectedProcessId = $ExpectedProcessId
        ExpectedProcessStartUtc = $ExpectedProcessStartUtc.ToUniversalTime().ToString('O')
        ExpectedExecutablePath = [System.IO.Path]::GetFullPath($ExpectedExecutablePath)
        BootstrapProfile = 'ConnectionFeasibilityV1'
        ServerName = $ServerName
        MaxNumberOfPlayers = $MaxNumberOfPlayers
        GameType = $GameType
        Map = $Map
    }
}

function Assert-CoopDedicatedControlReadyStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Status,
        [Parameter(Mandatory = $true)][string]$ExpectedRunId,
        [Parameter(Mandatory = $true)][string]$ExpectedRunTokenSha256,
        [Parameter(Mandatory = $true)][string]$ExpectedDedicatedModuleSha256,
        [Parameter(Mandatory = $true)][int]$ExpectedProcessId,
        [Parameter(Mandatory = $true)][DateTime]$ExpectedProcessStartUtc,
        [Parameter(Mandatory = $true)][string]$ExpectedExecutablePath
    )

    if ($null -eq $Status) { throw 'Dedicated control readiness status is missing.' }
    if ([int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'SchemaVersion') -ne 1 -or
        [int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ProtocolMajorVersion') -ne 1 -or
        [int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ProtocolMinorVersion') -ne 0) {
        throw 'Dedicated control readiness protocol is unsupported.'
    }
    if (-not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'RunId'), $ExpectedRunId, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'RunTokenSha256'), $ExpectedRunTokenSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Dedicated control readiness run identity mismatch.'
    }
    if (-not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'RoleType'), 'DedicatedServer', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'RoleInstanceId'), 'dedicated-server-01', [StringComparison]::Ordinal)) {
        throw 'Dedicated control readiness role identity mismatch.'
    }
    $state = [string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'State')
    if ([string]::Equals($state, 'Failed', [StringComparison]::Ordinal)) {
        throw ('Dedicated control readiness failed: ' + [string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'FailureCode') + ': ' + [string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'FailureMessage'))
    }
    if (-not [string]::Equals($state, 'Ready', [StringComparison]::Ordinal)) {
        throw 'Dedicated control readiness state is not Ready.'
    }
    if ([int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ProcessId') -ne $ExpectedProcessId) {
        throw 'Dedicated control readiness process ID mismatch.'
    }
    $actualStartUtc = ConvertTo-CoopUtcDateTime -Value (Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ProcessStartUtc')
    if ($null -eq $actualStartUtc -or [Math]::Abs(($actualStartUtc - $ExpectedProcessStartUtc.ToUniversalTime()).TotalSeconds) -ge 1.0) {
        throw 'Dedicated control readiness process start mismatch.'
    }
    $actualPath = [string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ExecutablePath')
    if ([string]::IsNullOrWhiteSpace($actualPath) -or
        -not [string]::Equals([System.IO.Path]::GetFullPath($actualPath), [System.IO.Path]::GetFullPath($ExpectedExecutablePath), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Dedicated control readiness executable path mismatch.'
    }
    if (-not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ModuleSha256'), $ExpectedDedicatedModuleSha256, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ExpectedModuleSha256'), $ExpectedDedicatedModuleSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Dedicated control readiness module identity mismatch.'
    }
    if (-not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'LifecycleSource'), 'InitialListedGameServerState.OnActivated', [StringComparison]::Ordinal)) {
        throw 'Dedicated control readiness lifecycle source is not authoritative.'
    }
    return $Status
}

function Confirm-CoopDedicatedBootstrapStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Status,
        [Parameter(Mandatory = $true)]$Request,
        [Parameter(Mandatory = $true)][string]$ExpectedRunId,
        [Parameter(Mandatory = $true)][string]$ExpectedRunTokenSha256,
        [Parameter(Mandatory = $true)][string]$ExpectedDedicatedModuleSha256,
        [Parameter(Mandatory = $true)][int]$ExpectedProcessId,
        [Parameter(Mandatory = $true)][DateTime]$ExpectedProcessStartUtc,
        [Parameter(Mandatory = $true)][string]$ExpectedExecutablePath
    )

    if ($null -eq $Status -or $null -eq $Request) { throw 'Dedicated bootstrap status validation context is missing.' }
    if ([int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'SchemaVersion') -ne 1 -or
        [int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ProtocolMajorVersion') -ne 1 -or
        [int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ProtocolMinorVersion') -ne 0) {
        throw 'Dedicated bootstrap status protocol is unsupported.'
    }
    if (-not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'RunId'), $ExpectedRunId, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'RunTokenSha256'), $ExpectedRunTokenSha256, [StringComparison]::OrdinalIgnoreCase) -or
        [long](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'Sequence') -ne [long](Get-CoopOptionalPropertyValue -InputObject $Request -Name 'Sequence') -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'CommandId'), [string](Get-CoopOptionalPropertyValue -InputObject $Request -Name 'CommandId'), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Dedicated bootstrap status command identity mismatch.'
    }
    if (-not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'SourceRoleType'), 'DedicatedServer', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'SourceRoleInstanceId'), 'dedicated-server-01', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'TargetRoleType'), 'Runner', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'TargetRoleInstanceId'), 'runner-01', [StringComparison]::Ordinal)) {
        throw 'Dedicated bootstrap status role routing mismatch.'
    }
    if ([int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ProcessId') -ne $ExpectedProcessId) {
        throw 'Dedicated bootstrap status process ID mismatch.'
    }
    $actualStartUtc = ConvertTo-CoopUtcDateTime -Value (Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ProcessStartUtc')
    if ($null -eq $actualStartUtc -or [Math]::Abs(($actualStartUtc - $ExpectedProcessStartUtc.ToUniversalTime()).TotalSeconds) -ge 1.0) {
        throw 'Dedicated bootstrap status process start mismatch.'
    }
    $actualPath = [string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ExecutablePath')
    if ([string]::IsNullOrWhiteSpace($actualPath) -or
        -not [string]::Equals([System.IO.Path]::GetFullPath($actualPath), [System.IO.Path]::GetFullPath($ExpectedExecutablePath), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Dedicated bootstrap status executable path mismatch.'
    }
    if (-not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'DedicatedModuleSha256'), $ExpectedDedicatedModuleSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Dedicated bootstrap status module identity mismatch.'
    }

    $state = [string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'State')
    if ([string]::Equals($state, 'Failed', [StringComparison]::Ordinal)) {
        throw ('Dedicated bootstrap failed: ' + [string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'FailureCode') + ': ' + [string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'FailureMessage'))
    }
    if (-not [bool](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'IsTerminal')) {
        return $false
    }
    if (-not [string]::Equals($state, 'BootstrapAccepted', [StringComparison]::Ordinal)) {
        throw 'Dedicated bootstrap terminal state is not BootstrapAccepted.'
    }

    $acknowledgements = @(Get-CoopOptionalPropertyValue -InputObject $Status -Name 'Acknowledgements')
    $expectedSteps = @('ServerName', 'MaxNumberOfPlayers', 'GameType', 'Map', 'UsableMap', 'StartGameRequested', 'StartGameConfirmed')
    if ($acknowledgements.Count -ne $expectedSteps.Count) { throw 'Dedicated bootstrap acknowledgement history is incomplete.' }
    for ($index = 0; $index -lt $expectedSteps.Count; $index++) {
        $ack = $acknowledgements[$index]
        if ([int](Get-CoopOptionalPropertyValue -InputObject $ack -Name 'StepSequence') -ne ($index + 1) -or
            -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $ack -Name 'Step'), $expectedSteps[$index], [StringComparison]::Ordinal)) {
            throw 'Dedicated bootstrap acknowledgement history is reordered.'
        }
    }
    if (-not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $acknowledgements[0] -Name 'ObservedValue'), [string](Get-CoopOptionalPropertyValue -InputObject $Request -Name 'ServerName'), [StringComparison]::Ordinal) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $acknowledgements[1] -Name 'ObservedValue'), '16', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $acknowledgements[2] -Name 'ObservedValue'), 'TeamDeathmatch', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $acknowledgements[3] -Name 'ObservedValue'), 'mp_tdm_map_001', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $acknowledgements[4] -Name 'ObservedValue'), 'mp_tdm_map_001', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $acknowledgements[5] -Name 'ObservedValue'), 'start_game', [StringComparison]::Ordinal) -or
        [string](Get-CoopOptionalPropertyValue -InputObject $acknowledgements[6] -Name 'ObservedValue') -notmatch '^IsPlaying=true;GameType=TeamDeathmatch;Map=mp_tdm_map_001$') {
        throw 'Dedicated bootstrap acknowledgement values do not match the allowlisted request.'
    }
    return $true
}

function Get-CoopDescendantProcessRecordsFromSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Snapshot,
        [Parameter(Mandatory = $true)][int[]]$RootProcessIds,
        [ValidateRange(1, 4096)][int]$MaximumDescendants = 256
    )

    if ($RootProcessIds.Count -eq 0) { throw 'At least one root process ID is required.' }

    $rootIds = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach ($rootProcessId in $RootProcessIds) {
        if ($rootProcessId -le 0) { throw 'Root process IDs must be positive.' }
        $rootIds.Add($rootProcessId) | Out-Null
    }

    $seenSnapshotIds = New-Object 'System.Collections.Generic.HashSet[int]'
    $childrenByParent = @{}
    foreach ($record in $Snapshot) {
        if ($null -eq $record) { continue }
        $processIdProperty = $record.PSObject.Properties['ProcessId']
        $parentProcessIdProperty = $record.PSObject.Properties['ParentProcessId']
        if ($null -eq $processIdProperty -or $null -eq $parentProcessIdProperty) {
            throw 'Every process snapshot record must contain ProcessId and ParentProcessId.'
        }

        $processId = [int]$processIdProperty.Value
        $parentProcessId = [int]$parentProcessIdProperty.Value
        if ($processId -le 0) { continue }
        if (-not $seenSnapshotIds.Add($processId)) {
            throw "The process snapshot contains duplicate process ID $processId."
        }
        if (-not $childrenByParent.ContainsKey($parentProcessId)) {
            $childrenByParent[$parentProcessId] = New-Object 'System.Collections.Generic.List[object]'
        }
        $childrenByParent[$parentProcessId].Add($record) | Out-Null
    }

    $visited = New-Object 'System.Collections.Generic.HashSet[int]'
    $queue = New-Object 'System.Collections.Generic.Queue[int]'
    foreach ($rootProcessId in $rootIds) {
        $visited.Add($rootProcessId) | Out-Null
        $queue.Enqueue($rootProcessId)
    }

    $descendants = New-Object 'System.Collections.Generic.List[object]'
    while ($queue.Count -gt 0) {
        $parentProcessId = $queue.Dequeue()
        if (-not $childrenByParent.ContainsKey($parentProcessId)) { continue }

        foreach ($child in $childrenByParent[$parentProcessId]) {
            $childProcessId = [int]$child.ProcessId
            if (-not $visited.Add($childProcessId)) { continue }
            if ($descendants.Count -ge $MaximumDescendants) {
                throw "The process snapshot exceeds the maximum owned descendant count of $MaximumDescendants."
            }
            $descendants.Add($child) | Out-Null
            $queue.Enqueue($childProcessId)
        }
    }

    return $descendants.ToArray()
}

function Get-CoopOptionalPropertyValue {
    [CmdletBinding()]
    param(
        [AllowNull()]$InputObject,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [System.Collections.IDictionary]) {
        if ($InputObject.Contains($Name)) { return $InputObject[$Name] }
        return $null
    }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function ConvertTo-CoopUtcDateTime {
    [CmdletBinding()]
    param([AllowNull()]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return $null }
    try {
        if ($Value -is [DateTime]) { return ([DateTime]$Value).ToUniversalTime() }
        return [DateTime]::Parse(
            [string]$Value,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    }
    catch {
        return $null
    }
}

function New-CoopProvisionalProcessIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][ValidateRange(1, [int]::MaxValue)][int]$ProcessId,
        [Parameter(Mandatory = $true)][string]$RoleType,
        [Parameter(Mandatory = $true)][string]$RoleInstanceId,
        [Parameter(Mandatory = $true)][string]$ExpectedExecutablePath,
        [ValidateRange(-1, [int]::MaxValue)][int]$ExpectedParentProcessId = -1,
        [Parameter(Mandatory = $true)][DateTime]$LaunchStartedUtc,
        [Parameter(Mandatory = $true)][DateTime]$LaunchObservedUtc,
        [string]$LaunchOperationId
    )

    if ([string]::IsNullOrWhiteSpace($RoleType)) { throw 'RoleType is required.' }
    if ([string]::IsNullOrWhiteSpace($RoleInstanceId)) { throw 'RoleInstanceId is required.' }
    if ([string]::IsNullOrWhiteSpace($ExpectedExecutablePath)) { throw 'ExpectedExecutablePath is required.' }
    $launchStart = $LaunchStartedUtc.ToUniversalTime()
    $launchObserved = $LaunchObservedUtc.ToUniversalTime()
    if ($launchObserved -lt $launchStart) { throw 'LaunchObservedUtc must not precede LaunchStartedUtc.' }
    if ([string]::IsNullOrWhiteSpace($LaunchOperationId)) {
        $LaunchOperationId = [Guid]::NewGuid().ToString('D')
    }

    return [ordered]@{
        IdentityState = 'Provisional'
        LaunchOperationId = $LaunchOperationId
        RoleType = $RoleType
        RoleInstanceId = $RoleInstanceId
        ProcessId = $ProcessId
        ParentProcessId = $ExpectedParentProcessId
        ExpectedParentProcessId = $ExpectedParentProcessId
        ProcessStartUtc = $null
        ExecutablePath = [System.IO.Path]::GetFullPath($ExpectedExecutablePath)
        ExecutableSha256 = $null
        PathEvidenceSource = 'RequestedLaunchPath'
        LaunchStartedUtc = $launchStart.ToString('O')
        LaunchObservedUtc = $launchObserved.ToString('O')
        RegisteredUtc = [DateTime]::UtcNow.ToString('O')
    }
}

function Resolve-CoopProcessObservation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][ValidateRange(1, [int]::MaxValue)][int]$ProcessId,
        [string]$ExpectedExecutablePath,
        [ValidateRange(-1, [int]::MaxValue)][int]$ExpectedParentProcessId = -1,
        [Nullable[DateTime]]$LaunchStartedUtc,
        [Nullable[DateTime]]$LaunchObservedUtc,
        [ValidateRange(50, 30000)][int]$DeadlineMilliseconds = 5000,
        [scriptblock]$ProcessRecordProvider,
        [scriptblock]$CimRecordProvider
    )

    if ($null -eq $ProcessRecordProvider) {
        $ProcessRecordProvider = {
            param([int]$CandidateProcessId)
            Get-Process -Id $CandidateProcessId -ErrorAction Stop
        }
    }
    if ($null -eq $CimRecordProvider) {
        $CimRecordProvider = {
            param([int]$CandidateProcessId)
            Get-CimInstance -ClassName Win32_Process `
                -Filter ('ProcessId=' + $CandidateProcessId) `
                -OperationTimeoutSec 2 `
                -ErrorAction Stop
        }
    }

    $expectedPath = if ([string]::IsNullOrWhiteSpace($ExpectedExecutablePath)) {
        ''
    }
    else {
        [System.IO.Path]::GetFullPath($ExpectedExecutablePath)
    }
    $deadlineUtc = [DateTime]::UtcNow.AddMilliseconds($DeadlineMilliseconds)
    $lastFailure = ''
    do {
        $processRecord = $null
        $cimRecord = $null
        try {
            $records = @(& $ProcessRecordProvider $ProcessId)
            if ($records.Count -gt 1) { throw "Process provider returned multiple records for PID $ProcessId." }
            if ($records.Count -eq 1) { $processRecord = $records[0] }
        }
        catch { $lastFailure = $_.Exception.Message }
        try {
            $records = @(& $CimRecordProvider $ProcessId)
            if ($records.Count -gt 1) { throw "Win32_Process provider returned multiple records for PID $ProcessId." }
            if ($records.Count -eq 1) { $cimRecord = $records[0] }
        }
        catch { $lastFailure = $_.Exception.Message }

        $processPathValue = Get-CoopOptionalPropertyValue -InputObject $processRecord -Name 'Path'
        $cimPathValue = Get-CoopOptionalPropertyValue -InputObject $cimRecord -Name 'ExecutablePath'
        $processPath = if ([string]::IsNullOrWhiteSpace([string]$processPathValue)) { '' } else { [System.IO.Path]::GetFullPath([string]$processPathValue) }
        $cimPath = if ([string]::IsNullOrWhiteSpace([string]$cimPathValue)) { '' } else { [System.IO.Path]::GetFullPath([string]$cimPathValue) }

        foreach ($observedPath in @($processPath, $cimPath)) {
            if ([string]::IsNullOrWhiteSpace($observedPath) -or [string]::IsNullOrWhiteSpace($expectedPath)) { continue }
            if (-not [string]::Equals($observedPath, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Process $ProcessId executable path '$observedPath' does not match the exact requested path '$expectedPath'."
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($processPath) -and
            -not [string]::IsNullOrWhiteSpace($cimPath) -and
            -not [string]::Equals($processPath, $cimPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Process $ProcessId has conflicting executable paths in Process and Win32_Process observations."
        }

        $resolvedPath = if (-not [string]::IsNullOrWhiteSpace($processPath)) { $processPath } else { $cimPath }
        if ([string]::IsNullOrWhiteSpace($expectedPath) -and -not [string]::IsNullOrWhiteSpace($resolvedPath)) {
            $expectedPath = $resolvedPath
        }
        $processStartUtc = ConvertTo-CoopUtcDateTime -Value (Get-CoopOptionalPropertyValue -InputObject $processRecord -Name 'StartTime')
        $cimStartUtc = ConvertTo-CoopUtcDateTime -Value (Get-CoopOptionalPropertyValue -InputObject $cimRecord -Name 'CreationDate')
        if ($null -ne $processStartUtc -and $null -ne $cimStartUtc -and
            [Math]::Abs(($processStartUtc - $cimStartUtc).TotalSeconds) -ge 1.0) {
            throw "Process $ProcessId has conflicting creation times in Process and Win32_Process observations."
        }
        $resolvedStartUtc = if ($null -ne $processStartUtc) { $processStartUtc } else { $cimStartUtc }
        $parentValue = Get-CoopOptionalPropertyValue -InputObject $cimRecord -Name 'ParentProcessId'
        $resolvedParentProcessId = if ($null -eq $parentValue) { -1 } else { [int]$parentValue }

        if (-not [string]::IsNullOrWhiteSpace($resolvedPath) -and $null -ne $resolvedStartUtc) {
            if ($ExpectedParentProcessId -ge 0) {
                if ($resolvedParentProcessId -lt 0) {
                    $lastFailure = "Win32_Process parent identity for PID $ProcessId is not available yet."
                }
                elseif ($resolvedParentProcessId -ne $ExpectedParentProcessId) {
                    throw "Process $ProcessId parent PID $resolvedParentProcessId does not match expected parent PID $ExpectedParentProcessId."
                }
            }
            if ($null -ne $LaunchStartedUtc -and
                $resolvedStartUtc -lt $LaunchStartedUtc.ToUniversalTime().AddSeconds(-2)) {
                throw "Process $ProcessId predates its exact launch operation."
            }
            if ($null -ne $LaunchObservedUtc -and
                $resolvedStartUtc -gt $LaunchObservedUtc.ToUniversalTime().AddSeconds(2)) {
                throw "Process $ProcessId was created after its exact launch observation and may be a reused PID."
            }
            if ($ExpectedParentProcessId -lt 0 -or $resolvedParentProcessId -ge 0) {
                return [pscustomobject]@{
                    ProcessId = $ProcessId
                    ParentProcessId = $resolvedParentProcessId
                    ProcessStartUtc = $resolvedStartUtc.ToString('O')
                    ExecutablePath = $resolvedPath
                    PathEvidenceSource = if (-not [string]::IsNullOrWhiteSpace($processPath) -and -not [string]::IsNullOrWhiteSpace($cimPath)) {
                        'ProcessAndWin32Process'
                    }
                    elseif (-not [string]::IsNullOrWhiteSpace($processPath)) { 'ProcessPath' }
                    else { 'Win32ProcessFallback' }
                    ObservedUtc = [DateTime]::UtcNow.ToString('O')
                }
            }
        }

        if ([DateTime]::UtcNow -lt $deadlineUtc) { Start-Sleep -Milliseconds 100 }
    } while ([DateTime]::UtcNow -lt $deadlineUtc)

    $detail = if ([string]::IsNullOrWhiteSpace($lastFailure)) { '' } else { ' Last observation error: ' + $lastFailure }
    throw "Timed out after $DeadlineMilliseconds ms while resolving exact process identity for PID $ProcessId.$detail"
}

function Test-CoopProcessObservationMatchesIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Identity,
        [Parameter(Mandatory = $true)]$Observation
    )

    $identityProcessId = [int](Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'ProcessId')
    $observationProcessId = [int](Get-CoopOptionalPropertyValue -InputObject $Observation -Name 'ProcessId')
    if ($identityProcessId -le 0 -or $identityProcessId -ne $observationProcessId) { return $false }
    $expectedPathValue = Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'ExecutablePath'
    $actualPathValue = Get-CoopOptionalPropertyValue -InputObject $Observation -Name 'ExecutablePath'
    if ([string]::IsNullOrWhiteSpace([string]$expectedPathValue) -or [string]::IsNullOrWhiteSpace([string]$actualPathValue)) { return $false }
    $expectedPath = [System.IO.Path]::GetFullPath([string]$expectedPathValue)
    $actualPath = [System.IO.Path]::GetFullPath([string]$actualPathValue)
    if (-not [string]::Equals($actualPath, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) { return $false }

    $actualStartUtc = ConvertTo-CoopUtcDateTime -Value (Get-CoopOptionalPropertyValue -InputObject $Observation -Name 'ProcessStartUtc')
    if ($null -eq $actualStartUtc) { return $false }
    $expectedStartUtc = ConvertTo-CoopUtcDateTime -Value (Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'ProcessStartUtc')
    if ($null -ne $expectedStartUtc) {
        return [Math]::Abs(($actualStartUtc - $expectedStartUtc).TotalSeconds) -lt 1.0
    }

    $launchStartedUtc = ConvertTo-CoopUtcDateTime -Value (Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'LaunchStartedUtc')
    $launchObservedUtc = ConvertTo-CoopUtcDateTime -Value (Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'LaunchObservedUtc')
    if ($null -eq $launchStartedUtc -or $null -eq $launchObservedUtc -or
        $actualStartUtc -lt $launchStartedUtc.AddSeconds(-2) -or
        $actualStartUtc -gt $launchObservedUtc.AddSeconds(2)) {
        return $false
    }
    $expectedParentValue = Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'ExpectedParentProcessId'
    if ($null -ne $expectedParentValue -and [int]$expectedParentValue -ge 0) {
        $actualParentValue = Get-CoopOptionalPropertyValue -InputObject $Observation -Name 'ParentProcessId'
        if ($null -eq $actualParentValue -or [int]$actualParentValue -ne [int]$expectedParentValue) { return $false }
    }
    return $true
}

function Test-CoopLiveProcessIdentityCore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Identity,
        [ValidateRange(50, 30000)][int]$DeadlineMilliseconds = 2500
    )

    $processId = [int](Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'ProcessId')
    if ($processId -le 0) { return $false }
    try {
        $identityState = [string](Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'IdentityState')
        $isProvisional = [string]::Equals($identityState, 'Provisional', [StringComparison]::Ordinal)
        $expectedParent = -1
        $launchStartedUtc = $null
        $launchObservedUtc = $null
        if ($isProvisional) {
            $parentValue = Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'ExpectedParentProcessId'
            if ($null -ne $parentValue) { $expectedParent = [int]$parentValue }
            $launchStartedUtc = ConvertTo-CoopUtcDateTime -Value (Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'LaunchStartedUtc')
            $launchObservedUtc = ConvertTo-CoopUtcDateTime -Value (Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'LaunchObservedUtc')
        }
        $observation = Resolve-CoopProcessObservation `
            -ProcessId $processId `
            -ExpectedExecutablePath ([string](Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'ExecutablePath')) `
            -ExpectedParentProcessId $expectedParent `
            -LaunchStartedUtc $launchStartedUtc `
            -LaunchObservedUtc $launchObservedUtc `
            -DeadlineMilliseconds $DeadlineMilliseconds
        return Test-CoopProcessObservationMatchesIdentity -Identity $Identity -Observation $observation
    }
    catch {
        return $false
    }
}

function Stop-CoopExactProcessIdentityCore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Identity,
        [ValidateRange(1, 60)][int]$GraceSeconds = 15
    )

    $processId = [int](Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'ProcessId')
    $evidence = [ordered]@{
        RoleType = [string](Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'RoleType')
        RoleInstanceId = [string](Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'RoleInstanceId')
        IdentityState = [string](Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'IdentityState')
        LaunchOperationId = [string](Get-CoopOptionalPropertyValue -InputObject $Identity -Name 'LaunchOperationId')
        ProcessId = $processId
        IdentityMatched = $false
        GracefulCloseRequested = $false
        ForcedStopUsed = $false
        Outcome = 'NotRunning'
        CheckedUtc = [DateTime]::UtcNow.ToString('O')
    }
    if (-not (Test-CoopLiveProcessIdentityCore -Identity $Identity)) { return $evidence }

    $evidence.IdentityMatched = $true
    $process = Get-Process -Id $processId -ErrorAction Stop
    try { $evidence.GracefulCloseRequested = [bool]$process.CloseMainWindow() } catch { }
    try { Wait-CoopProcessExitNoOutput -Process $process -TimeoutMilliseconds ($GraceSeconds * 1000) } catch { }
    if (-not $process.HasExited) {
        if (-not (Test-CoopLiveProcessIdentityCore -Identity $Identity)) {
            $evidence.Outcome = 'IdentityChangedBeforeForcedStop'
            return $evidence
        }
        $evidence.ForcedStopUsed = $true
        $process.Kill()
        Wait-CoopProcessExitNoOutput -Process $process -TimeoutMilliseconds 10000
    }
    $evidence.Outcome = if ($process.HasExited) { 'Stopped' } else { 'StopFailed' }
    return $evidence
}

function Wait-CoopProcessExitNoOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [ValidateRange(0, 600000)][int]$TimeoutMilliseconds
    )

    $null = $Process.WaitForExit($TimeoutMilliseconds)
}

function Get-CoopSingularCommandResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Results,
        [Parameter(Mandatory = $true)][string]$CommandName
    )

    if ($Results.Count -ne 1) {
        throw "Aggregate command '$CommandName' must emit exactly one structured result object; observed $($Results.Count)."
    }
    $result = $Results[0]
    if ($null -eq $result) {
        throw "Aggregate command '$CommandName' returned a null result."
    }
    $isDictionary = $result -is [System.Collections.IDictionary]
    foreach ($requiredProperty in @('Outcome', 'Reason', 'ArtifactPath')) {
        $hasProperty = if ($isDictionary) {
            $result.Contains($requiredProperty)
        }
        else {
            $null -ne $result.PSObject.Properties[$requiredProperty]
        }
        if (-not $hasProperty) {
            throw "Aggregate command '$CommandName' result is missing required property '$requiredProperty'."
        }
    }
    if ($isDictionary) {
        return [pscustomobject]@{
            Outcome = $result['Outcome']
            Reason = $result['Reason']
            ArtifactPath = $result['ArtifactPath']
            PrimaryOutcome = $(if ($result.Contains('PrimaryOutcome')) { $result['PrimaryOutcome'] } else { $result['Outcome'] })
            PrimaryReason = $(if ($result.Contains('PrimaryReason')) { $result['PrimaryReason'] } else { $result['Reason'] })
        }
    }
    return $result
}

function New-CoopProcessTextCapture {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][string]$StandardOutputPath,
        [Parameter(Mandatory = $true)][string]$StandardErrorPath,
        [ValidateRange(128, 65536)][int]$MaximumTailLines = 8192
    )

    foreach ($path in @($StandardOutputPath, $StandardErrorPath)) {
        $directory = Split-Path -Parent $path
        if (-not [string]::IsNullOrWhiteSpace($directory)) {
            [System.IO.Directory]::CreateDirectory($directory) | Out-Null
        }
    }

    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    $outputStream = New-Object System.IO.FileStream(
        $StandardOutputPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::Read)
    $errorStream = $null
    $outputWriter = $null
    $errorWriter = $null
    try {
        $errorStream = New-Object System.IO.FileStream(
            $StandardErrorPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::Read)
        $outputWriter = New-Object System.IO.StreamWriter($outputStream, $utf8WithoutBom)
        $errorWriter = New-Object System.IO.StreamWriter($errorStream, $utf8WithoutBom)
        $outputWriter.AutoFlush = $true
        $errorWriter.AutoFlush = $true

        $capture = [pscustomobject]@{
            Process = $Process
            StandardOutputPath = [System.IO.Path]::GetFullPath($StandardOutputPath)
            StandardErrorPath = [System.IO.Path]::GetFullPath($StandardErrorPath)
            OutputWriter = $outputWriter
            ErrorWriter = $errorWriter
            OutputTask = $Process.StandardOutput.ReadLineAsync()
            ErrorTask = $Process.StandardError.ReadLineAsync()
            OutputCompleted = $false
            ErrorCompleted = $false
            Sequence = [long]0
            Tail = New-Object 'System.Collections.Generic.List[object]'
            MaximumTailLines = $MaximumTailLines
            Disposed = $false
        }
        $outputStream = $null
        $errorStream = $null
        $outputWriter = $null
        $errorWriter = $null
        return $capture
    }
    finally {
        if ($null -ne $outputWriter) { $outputWriter.Dispose() }
        elseif ($null -ne $outputStream) { $outputStream.Dispose() }
        if ($null -ne $errorWriter) { $errorWriter.Dispose() }
        elseif ($null -ne $errorStream) { $errorStream.Dispose() }
    }
}

function Update-CoopProcessTextCapture {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Capture,
        [ValidateRange(1, 65536)][int]$MaximumLinesPerStream = 8192
    )

    if ([bool]$Capture.Disposed) { return }
    foreach ($streamName in @('Output', 'Error')) {
        $completedProperty = $streamName + 'Completed'
        $taskProperty = $streamName + 'Task'
        $writerProperty = $streamName + 'Writer'
        $reader = if ($streamName -eq 'Output') {
            $Capture.Process.StandardOutput
        }
        else {
            $Capture.Process.StandardError
        }

        for ($lineIndex = 0; $lineIndex -lt $MaximumLinesPerStream -and -not [bool]$Capture.$completedProperty; $lineIndex++) {
            $task = $Capture.$taskProperty
            if ($null -eq $task -or -not $task.IsCompleted) { break }
            $line = $task.GetAwaiter().GetResult()
            if ($null -eq $line) {
                $Capture.$completedProperty = $true
                $Capture.$taskProperty = $null
                break
            }

            $Capture.$writerProperty.WriteLine($line)
            $Capture.Sequence = [long]$Capture.Sequence + 1L
            $Capture.Tail.Add([pscustomobject]@{
                Sequence = [long]$Capture.Sequence
                Stream = $streamName
                Text = [string]$line
            }) | Out-Null
            while ($Capture.Tail.Count -gt [int]$Capture.MaximumTailLines) {
                $Capture.Tail.RemoveAt(0)
            }
            $Capture.$taskProperty = $reader.ReadLineAsync()
        }
    }
}

function Wait-CoopCapturedTextMarkers {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Capture,
        [Parameter(Mandatory = $true)][string[]]$RequiredSubstrings,
        [ValidateRange(0, [long]::MaxValue)][long]$AfterSequence = 0,
        [Parameter(Mandatory = $true)][DateTime]$DeadlineUtc,
        [Parameter(Mandatory = $true)][string]$EvidenceName,
        [scriptblock]$Heartbeat
    )

    if ($RequiredSubstrings.Count -eq 0) { throw 'At least one captured-text marker is required.' }
    $matches = New-Object 'System.Collections.Generic.List[object]'
    $matchedMarkers = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    while ([DateTime]::UtcNow -lt $DeadlineUtc) {
        Update-CoopProcessTextCapture -Capture $Capture
        foreach ($record in $Capture.Tail) {
            if ([long]$record.Sequence -le $AfterSequence) { continue }
            foreach ($marker in $RequiredSubstrings) {
                if ($matchedMarkers.Contains($marker)) { continue }
                if ($record.Text.IndexOf($marker, [StringComparison]::Ordinal) -ge 0) {
                    $matchedMarkers.Add($marker) | Out-Null
                    $matches.Add([pscustomobject]@{
                        Marker = $marker
                        Sequence = [long]$record.Sequence
                        Stream = [string]$record.Stream
                        Text = [string]$record.Text
                    }) | Out-Null
                }
            }
        }
        if ($matchedMarkers.Count -eq $RequiredSubstrings.Count) {
            return [pscustomobject]@{
                EvidenceName = $EvidenceName
                AfterSequence = $AfterSequence
                ObservedUtc = [DateTime]::UtcNow.ToString('O')
                Matches = $matches.ToArray()
            }
        }
        if ($Capture.Process.HasExited -and
            [bool]$Capture.OutputCompleted -and
            [bool]$Capture.ErrorCompleted) {
            throw "Process exited before captured-text evidence '$EvidenceName' was complete."
        }
        if ($null -ne $Heartbeat) { $null = & $Heartbeat }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for captured-text evidence '$EvidenceName'."
}

function Complete-CoopProcessTextCapture {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Capture,
        [ValidateRange(100, 30000)][int]$DrainTimeoutMilliseconds = 5000
    )

    if ([bool]$Capture.Disposed) { return }
    $deadlineUtc = [DateTime]::UtcNow.AddMilliseconds($DrainTimeoutMilliseconds)
    try {
        while ([DateTime]::UtcNow -lt $deadlineUtc -and
            (-not [bool]$Capture.OutputCompleted -or -not [bool]$Capture.ErrorCompleted)) {
            Update-CoopProcessTextCapture -Capture $Capture
            if ([bool]$Capture.OutputCompleted -and [bool]$Capture.ErrorCompleted) { break }
            Start-Sleep -Milliseconds 25
        }
        Update-CoopProcessTextCapture -Capture $Capture
        if (-not [bool]$Capture.OutputCompleted -or -not [bool]$Capture.ErrorCompleted) {
            throw "Dedicated process output did not reach EOF within $DrainTimeoutMilliseconds ms."
        }
    }
    finally {
        $Capture.OutputWriter.Dispose()
        $Capture.ErrorWriter.Dispose()
        $Capture.Disposed = $true
    }
}

function Get-CoopPidCorrelatedNativeLogNames {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][ValidateRange(1, [int]::MaxValue)][int]$ProcessId)

    return @(
        ('rgl_log_' + $ProcessId.ToString([Globalization.CultureInfo]::InvariantCulture) + '.txt'),
        ('rgl_log_errors_' + $ProcessId.ToString([Globalization.CultureInfo]::InvariantCulture) + '.txt'),
        ('watchdog_log_' + $ProcessId.ToString([Globalization.CultureInfo]::InvariantCulture) + '.txt')
    )
}
