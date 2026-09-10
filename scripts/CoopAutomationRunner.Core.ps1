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
        [int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ProtocolMinorVersion') -ne 1) {
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
        [Parameter(Mandatory = $true)][string]$ExpectedExecutablePath,
        [string]$RunRoot
    )

    if ($null -eq $Status -or $null -eq $Request) { throw 'Dedicated bootstrap status validation context is missing.' }
    if ([int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'SchemaVersion') -ne 1 -or
        [int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ProtocolMajorVersion') -ne 1 -or
        [int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ProtocolMinorVersion') -ne 1) {
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
    $smoke = [string](Get-CoopOptionalPropertyValue -InputObject $Request -Name 'BootstrapProfile') -ceq 'FieldDedicatedSpawnSmokeV1'
    $expectedTerminal = if ($smoke) { 'SpawnSmokePassed' } else { 'BootstrapAccepted' }
    $expectedGame = if ($smoke) { 'CoopBattle' } else { 'TeamDeathmatch' }
    $expectedMap = if ($smoke) { 'battle_terrain_029' } else { 'mp_tdm_map_001' }
    if (-not [string]::Equals($state, $expectedTerminal, [StringComparison]::Ordinal)) {
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
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $acknowledgements[2] -Name 'ObservedValue'), $expectedGame, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $acknowledgements[3] -Name 'ObservedValue'), $expectedMap, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $acknowledgements[4] -Name 'ObservedValue'), $expectedMap, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string](Get-CoopOptionalPropertyValue -InputObject $acknowledgements[5] -Name 'ObservedValue'), 'start_game', [StringComparison]::Ordinal) -or
        [string](Get-CoopOptionalPropertyValue -InputObject $acknowledgements[6] -Name 'ObservedValue') -cne ('IsPlaying=true;GameType=' + $expectedGame + ';Map=' + $expectedMap)) {
        throw 'Dedicated bootstrap acknowledgement values do not match the allowlisted request.'
    }
    if ($smoke) {
        if ([string]::IsNullOrWhiteSpace($RunRoot)) { throw 'Spawn smoke requires its exact run root.' }
        Assert-CoopSpawnSmokeEvidenceCore -Evidence $Status.SmokeEvidence -RunRoot $RunRoot
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

function Get-CoopBoundedProcessSnapshotCore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ShellExecutablePath,
        [ValidateRange(250, 30000)][int]$DeadlineMilliseconds = 5000,
        [ValidateRange(33554432, 1073741824)][long]$PrivateMemoryLimitBytes = 268435456,
        [ValidateRange(4096, 16777216)][int]$OutputLimitBytes = 4194304,
        [AllowEmptyString()][string]$CollectorScriptText = '',
        [AllowNull()][scriptblock]$CollectorStartedAction = $null
    )

    if ([string]::IsNullOrWhiteSpace($ShellExecutablePath)) {
        throw 'ShellExecutablePath is required.'
    }
    $shellPath = [System.IO.Path]::GetFullPath($ShellExecutablePath)
    if (-not [System.IO.File]::Exists($shellPath)) {
        throw "Process-snapshot shell does not exist: $shellPath"
    }
    if ([string]::IsNullOrWhiteSpace($CollectorScriptText)) {
        $CollectorScriptText = @'
$ErrorActionPreference = 'Stop'
$records = @(Get-CimInstance -ClassName Win32_Process -Property ProcessId,ParentProcessId,ExecutablePath,CommandLine,CreationDate -OperationTimeoutSec 2 -ErrorAction Stop | ForEach-Object {
    [pscustomobject][ordered]@{
        ProcessId = [int]$_.ProcessId
        ParentProcessId = [int]$_.ParentProcessId
        ExecutablePath = [string]$_.ExecutablePath
        CommandLine = [string]$_.CommandLine
        CreationDate = if ($null -eq $_.CreationDate) { $null } else { ([DateTime]$_.CreationDate).ToUniversalTime().ToString('O') }
    }
})
$payload = [pscustomobject][ordered]@{
    Schema = 'coop-lightweight-process-snapshot-v1'
    Records = $records
}
[Console]::Out.Write(($payload | ConvertTo-Json -Depth 4 -Compress))
'@
    }

    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($CollectorScriptText))
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $shellPath
    $startInfo.Arguments = '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ' + $encodedCommand
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $startedUtc = [DateTime]::UtcNow
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $collector = $null
    $stdoutTask = $null
    $stderrTask = $null
    $state = 'CollectorFailed'
    $failure = ''
    $records = @()
    $peakPrivateMemoryBytes = 0L
    $collectorProcessId = 0
    $forcedStopUsed = $false
    try {
        $collector = [Diagnostics.Process]::Start($startInfo)
        if ($null -eq $collector) { throw 'Process-snapshot collector did not return a process handle.' }
        $collectorProcessId = $collector.Id
        if ($null -ne $CollectorStartedAction) {
            & $CollectorStartedAction `
                $collectorProcessId `
                $startedUtc `
                ([DateTime]::UtcNow) `
                $shellPath
        }
        $stdoutTask = $collector.StandardOutput.ReadToEndAsync()
        $stderrTask = $collector.StandardError.ReadToEndAsync()

        while (-not $collector.WaitForExit(50)) {
            try {
                $collector.Refresh()
                $privateBytes = [long]$collector.PrivateMemorySize64
                if ($privateBytes -gt $peakPrivateMemoryBytes) { $peakPrivateMemoryBytes = $privateBytes }
                if ($privateBytes -gt $PrivateMemoryLimitBytes) {
                    $state = 'MemoryLimitExceeded'
                    $failure = "Process-snapshot collector exceeded its $PrivateMemoryLimitBytes-byte private-memory limit."
                    break
                }
            }
            catch {
                $state = 'CollectorFailed'
                $failure = 'Process-snapshot collector memory observation failed: ' + $_.Exception.Message
                break
            }
            if ($stopwatch.ElapsedMilliseconds -ge $DeadlineMilliseconds) {
                $state = 'TimedOut'
                $failure = "Process-snapshot collector exceeded its $DeadlineMilliseconds-millisecond deadline."
                break
            }
        }

        if (-not $collector.HasExited) {
            $forcedStopUsed = $true
            try { $collector.Kill() }
            catch { $failure += ' Exact collector termination failed: ' + $_.Exception.Message }
            $null = $collector.WaitForExit(2000)
        }
        else {
            try {
                $collector.Refresh()
                $privateBytes = [long]$collector.PrivateMemorySize64
                if ($privateBytes -gt $peakPrivateMemoryBytes) { $peakPrivateMemoryBytes = $privateBytes }
            }
            catch { }
        }

        if (($null -ne $stdoutTask -and -not $stdoutTask.Wait(2000)) -or
            ($null -ne $stderrTask -and -not $stderrTask.Wait(2000))) {
            throw 'Process-snapshot collector output did not close within 2000 milliseconds after process exit.'
        }
        $stdout = if ($null -ne $stdoutTask) { $stdoutTask.Result } else { '' }
        $stderr = if ($null -ne $stderrTask) { $stderrTask.Result } else { '' }
        if ($state -eq 'TimedOut' -or $state -eq 'MemoryLimitExceeded') {
            # The bounded failure state already contains the authoritative reason.
        }
        elseif ($collector.ExitCode -ne 0) {
            $state = 'CollectorFailed'
            $failure = "Process-snapshot collector exited with code $($collector.ExitCode). " + $stderr.Trim()
        }
        elseif ([Text.Encoding]::UTF8.GetByteCount($stdout) -gt $OutputLimitBytes) {
            $state = 'OutputLimitExceeded'
            $failure = "Process-snapshot collector exceeded its $OutputLimitBytes-byte output limit."
        }
        else {
            $payload = $stdout | ConvertFrom-Json -ErrorAction Stop
            if ($null -eq $payload -or
                -not [string]::Equals([string]$payload.Schema, 'coop-lightweight-process-snapshot-v1', [StringComparison]::Ordinal)) {
                throw 'Process-snapshot collector returned an unsupported payload.'
            }
            $records = @($payload.Records)
            if ($records.Count -gt 4096) { throw 'Process-snapshot collector returned more than 4096 records.' }
            foreach ($record in $records) {
                if ($null -eq $record.PSObject.Properties['ProcessId'] -or
                    $null -eq $record.PSObject.Properties['ParentProcessId']) {
                    throw 'A lightweight process-snapshot record is missing its process identity fields.'
                }
            }
            $state = 'Captured'
        }
    }
    catch {
        $state = 'CollectorFailed'
        $failure = $_.Exception.Message
        if ($null -ne $collector) {
            try {
                if (-not $collector.HasExited) {
                    $forcedStopUsed = $true
                    $collector.Kill()
                    $null = $collector.WaitForExit(2000)
                }
            }
            catch { $failure += ' Exact collector termination failed: ' + $_.Exception.Message }
        }
    }
    finally {
        $stopwatch.Stop()
        if ($null -ne $collector) { $collector.Dispose() }
    }

    return [pscustomobject][ordered]@{
        Schema = 'coop-bounded-process-snapshot-v1'
        State = $state
        Failure = $failure.Trim()
        StartedUtc = $startedUtc.ToString('O')
        CompletedUtc = [DateTime]::UtcNow.ToString('O')
        DurationMilliseconds = [long]$stopwatch.ElapsedMilliseconds
        DeadlineMilliseconds = $DeadlineMilliseconds
        PrivateMemoryLimitBytes = $PrivateMemoryLimitBytes
        PeakPrivateMemoryBytes = $peakPrivateMemoryBytes
        OutputLimitBytes = $OutputLimitBytes
        CollectorProcessId = $collectorProcessId
        ForcedStopUsed = $forcedStopUsed
        RecordCount = $records.Count
        Records = $records
    }
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

if ($null -eq ('CoopAutomationCancellationSignal' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Threading;

public static class CoopAutomationCancellationSignal
{
    private static int _requested;
    private static ConsoleCancelEventHandler _handler;

    public static bool IsRequested { get { return Volatile.Read(ref _requested) != 0; } }

    public static void Install()
    {
        Interlocked.Exchange(ref _requested, 0);
        if (_handler != null) return;
        _handler = delegate(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            Interlocked.Exchange(ref _requested, 1);
        };
        Console.CancelKeyPress += _handler;
    }

    public static void Uninstall()
    {
        ConsoleCancelEventHandler handler = _handler;
        if (handler != null)
        {
            Console.CancelKeyPress -= handler;
            _handler = null;
        }
    }

    public static void RequestForTest()
    {
        Interlocked.Exchange(ref _requested, 1);
    }
}
'@
}

function Initialize-CoopCancellationSignalCore {
    [CmdletBinding()]
    param()

    [CoopAutomationCancellationSignal]::Install()
}

function Remove-CoopCancellationSignalCore {
    [CmdletBinding()]
    param()

    [CoopAutomationCancellationSignal]::Uninstall()
}

function Test-CoopConsoleCancellationRequestedCore {
    [CmdletBinding()]
    param()

    return [CoopAutomationCancellationSignal]::IsRequested
}

function Request-CoopConsoleCancellationForTestCore {
    [CmdletBinding()]
    param()

    [CoopAutomationCancellationSignal]::RequestForTest()
}

function Get-CoopRoleHealthClassificationCore {
    [CmdletBinding()]
    param(
        [AllowNull()]$Status,
        [Parameter(Mandatory = $true)][DateTime]$NowUtc,
        [ValidateRange(1, 3600)][int]$HeartbeatDeadlineSeconds = 5,
        [ValidateRange(1, 86400)][int]$ProgressDeadlineSeconds = 180
    )

    if ($null -eq $Status) { return 'NoHeartbeat' }
    if ([int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'SchemaVersion') -ne 2 -or
        [int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ProtocolMajorVersion') -ne 1 -or
        [int](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'ProtocolMinorVersion') -ne 1) {
        throw 'RoleHealthV1 requires exact role-status schema 2 and protocol 1.1.'
    }
    $heartbeatUtc = ConvertTo-CoopUtcDateTime -Value (
        Get-CoopOptionalPropertyValue -InputObject $Status -Name 'HeartbeatUtc')
    $progressUtc = ConvertTo-CoopUtcDateTime -Value (
        Get-CoopOptionalPropertyValue -InputObject $Status -Name 'LastProgressUtc')
    $enteredUtc = ConvertTo-CoopUtcDateTime -Value (
        Get-CoopOptionalPropertyValue -InputObject $Status -Name 'StateEnteredUtc')
    $revision = [long](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'StateRevision')
    $source = [string](Get-CoopOptionalPropertyValue -InputObject $Status -Name 'AuthoritativeSource')
    if ($null -eq $heartbeatUtc -or $null -eq $progressUtc -or $null -eq $enteredUtc -or
        $revision -le 0 -or [string]::IsNullOrWhiteSpace($source) -or
        $progressUtc -gt $heartbeatUtc -or $enteredUtc -gt $heartbeatUtc -or
        $heartbeatUtc -gt $NowUtc.ToUniversalTime().AddMinutes(1)) {
        throw 'RoleHealthV1 contains an invalid identity or timeline.'
    }
    if ($NowUtc.ToUniversalTime() - $heartbeatUtc -gt [TimeSpan]::FromSeconds($HeartbeatDeadlineSeconds)) {
        return 'NoHeartbeat'
    }
    if ($NowUtc.ToUniversalTime() - $progressUtc -gt [TimeSpan]::FromSeconds($ProgressDeadlineSeconds)) {
        return 'NoProgress'
    }
    return 'Healthy'
}

function New-CoopRoleHealthFailureEvidenceCore {
    [CmdletBinding()]
    param(
        [AllowNull()]$Status,
        [AllowNull()]$ReadEvidence,
        [Parameter(Mandatory = $true)][DateTime]$DecisionUtc,
        [Parameter(Mandatory = $true)][string]$Rejection,
        [Parameter(Mandatory = $true)][string]$ExpectedRunId,
        [Parameter(Mandatory = $true)][string]$ExpectedTokenSha256,
        [Parameter(Mandatory = $true)][string]$ExpectedRoleType,
        [Parameter(Mandatory = $true)][string]$ExpectedRoleInstanceId,
        [int]$HeartbeatDeadlineSeconds,
        [int]$ProgressDeadlineSeconds
    )

    # Failure-only, fixed field set. No raw status, token, exception or provider object escapes.
    # Each string is at most 256 UTF-16 units; the whole projection remains below 64 KiB of JSON.
    $boundedScalar = {
        param($Value)
        if ($null -eq $Value) { return $null }
        if ($Value -is [DateTime]) { return $Value.ToUniversalTime().ToString('O') }
        if ($Value -is [string]) {
            $length = [Math]::Min(256, $Value.Length)
            if ($length -gt 0 -and [char]::IsHighSurrogate($Value[$length - 1])) { $length-- }
            return [string]::new($Value.Substring(0, $length).ToCharArray())
        }
        if ($Value -is [bool] -or $Value -is [byte] -or $Value -is [int16] -or
            $Value -is [int] -or $Value -is [long] -or $Value -is [decimal]) { return $Value }
        if ($Value -is [double] -and -not [double]::IsNaN($Value) -and -not [double]::IsInfinity($Value)) { return $Value }
        return '[UnsupportedValueType]'
    }
    $read = [ordered]@{}
    foreach ($name in @('StartedUtc', 'CompletedUtc', 'Outcome', 'Phase', 'ExceptionType', 'HResult')) {
        $read[$name] = & $boundedScalar (Get-CoopOptionalPropertyValue -InputObject $ReadEvidence -Name $name)
    }
    $observed = [ordered]@{}
    foreach ($name in @('SchemaVersion', 'ProtocolMajorVersion', 'ProtocolMinorVersion', 'RunId',
        'RoleType', 'RoleInstanceId', 'State', 'StateRevision', 'UpdatedUtc', 'HeartbeatUtc',
        'LastProgressUtc', 'StateEnteredUtc', 'AuthoritativeSource')) {
        $observed[$name] = & $boundedScalar (Get-CoopOptionalPropertyValue -InputObject $Status -Name $name)
    }
    $heartbeat = ConvertTo-CoopUtcDateTime -Value $observed.HeartbeatUtc
    $progress = ConvertTo-CoopUtcDateTime -Value $observed.LastProgressUtc
    $token = Get-CoopOptionalPropertyValue -InputObject $Status -Name 'RunTokenSha256'
    return [pscustomobject][ordered]@{
        Schema = 'coop-role-health-failure-v1'
        DecisionUtc = $DecisionUtc.ToUniversalTime().ToString('O')
        Rejection = & $boundedScalar $Rejection
        ExpectedRunId = & $boundedScalar $ExpectedRunId
        ExpectedRoleType = & $boundedScalar $ExpectedRoleType
        ExpectedRoleInstanceId = & $boundedScalar $ExpectedRoleInstanceId
        TokenMatches = ($token -is [string] -and [string]::Equals($token, $ExpectedTokenSha256, [StringComparison]::OrdinalIgnoreCase))
        HeartbeatDeadlineSeconds = $HeartbeatDeadlineSeconds
        ProgressDeadlineSeconds = $ProgressDeadlineSeconds
        HeartbeatAgeSeconds = if ($null -eq $heartbeat) { $null } else { ($DecisionUtc.ToUniversalTime() - $heartbeat).TotalSeconds }
        ProgressAgeSeconds = if ($null -eq $progress) { $null } else { ($DecisionUtc.ToUniversalTime() - $progress).TotalSeconds }
        StatusPresent = ($null -ne $Status)
        Read = [pscustomobject]$read
        ObservedStatus = [pscustomobject]$observed
    }
}

function Get-CoopSpawnSmokeParentAdmissionCore {
    [CmdletBinding()]
    param(
        [AllowNull()]$ParentManifest,
        [AllowNull()]$ParentLease,
        [AllowNull()]$AttemptIntent,
        [Parameter(Mandatory = $true)][string]$ExpectedParentRunId,
        [Parameter(Mandatory = $true)][string]$ExpectedChildRunId,
        [ValidateRange(1, 2)][int]$ExpectedAttempt,
        [ValidateRange(1, [int]::MaxValue)][int]$ExpectedParentProcessId,
        [Parameter(Mandatory = $true)][DateTime]$NowUtc,
        [ValidateRange(1, 3600)][int]$HeartbeatDeadlineSeconds = 10
    )

    $observedUtc = $NowUtc.ToUniversalTime()
    $manifestReadable = $null -ne $ParentManifest
    $leaseReadable = $null -ne $ParentLease
    $intentReadable = $null -ne $AttemptIntent
    $manifestCommandMatches = $manifestReadable -and
        [string]::Equals(
            [string](Get-CoopOptionalPropertyValue -InputObject $ParentManifest -Name 'RequestedCommand'),
            'DedicatedSpawnSmoke',
            [StringComparison]::Ordinal)
    $manifestRunIdMatches = $manifestReadable -and
        [string]::Equals(
            [string](Get-CoopOptionalPropertyValue -InputObject $ParentManifest -Name 'RunId'),
            $ExpectedParentRunId,
            [StringComparison]::Ordinal)
    $manifestNonce = if ($manifestReadable) {
        [string](Get-CoopOptionalPropertyValue -InputObject $ParentManifest -Name 'NonceSha256')
    }
    else { '' }
    $manifestNonceValid = $manifestReadable -and $manifestNonce -match '^[A-Fa-f0-9]{64}$'
    $leaseRunIdMatches = $leaseReadable -and
        [string]::Equals(
            [string](Get-CoopOptionalPropertyValue -InputObject $ParentLease -Name 'RunId'),
            $ExpectedParentRunId,
            [StringComparison]::Ordinal)
    $leaseNonceMatches = $leaseReadable -and $manifestReadable -and
        [string]::Equals(
            [string](Get-CoopOptionalPropertyValue -InputObject $ParentLease -Name 'NonceSha256'),
            $manifestNonce,
            [StringComparison]::Ordinal)
    $intentParentRunIdMatches = $intentReadable -and
        [string]::Equals(
            [string](Get-CoopOptionalPropertyValue -InputObject $AttemptIntent -Name 'ParentRunId'),
            $ExpectedParentRunId,
            [StringComparison]::Ordinal)
    $intentChildRunIdMatches = $intentReadable -and
        [string]::Equals(
            [string](Get-CoopOptionalPropertyValue -InputObject $AttemptIntent -Name 'RunId'),
            $ExpectedChildRunId,
            [StringComparison]::Ordinal)
    $intentAttempt = 0
    $intentAttemptParsed = $intentReadable -and [int]::TryParse(
        [string](Get-CoopOptionalPropertyValue -InputObject $AttemptIntent -Name 'Attempt'),
        [ref]$intentAttempt)
    $intentAttemptMatches = $intentAttemptParsed -and $intentAttempt -eq $ExpectedAttempt
    $intentNonceMatches = $intentReadable -and $manifestReadable -and
        [string]::Equals(
            [string](Get-CoopOptionalPropertyValue -InputObject $AttemptIntent -Name 'ParentNonceSha256'),
            $manifestNonce,
            [StringComparison]::Ordinal)
    $leaseOwnerProcessId = 0
    $leaseOwnerProcessIdParsed = $leaseReadable -and [int]::TryParse(
        [string](Get-CoopOptionalPropertyValue -InputObject $ParentLease -Name 'OwnerProcessId'),
        [ref]$leaseOwnerProcessId)
    $leaseOwnerProcessIdMatches = $leaseOwnerProcessIdParsed -and
        $leaseOwnerProcessId -eq $ExpectedParentProcessId
    $leaseStatusActive = $leaseReadable -and
        [string]::Equals(
            [string](Get-CoopOptionalPropertyValue -InputObject $ParentLease -Name 'Status'),
            'Active',
            [StringComparison]::Ordinal)

    $heartbeatUtc = $null
    if ($leaseReadable) {
        $heartbeatUtc = ConvertTo-CoopUtcDateTime -Value (
            Get-CoopOptionalPropertyValue -InputObject $ParentLease -Name 'LastHeartbeatUtc')
    }
    $heartbeatTimelineValid = $null -ne $heartbeatUtc -and $heartbeatUtc -le $observedUtc.AddMinutes(1)
    $heartbeatAgeMilliseconds = if ($null -ne $heartbeatUtc) {
        [long][Math]::Round(($observedUtc - $heartbeatUtc).TotalMilliseconds)
    }
    else { $null }
    $heartbeatFresh = $heartbeatTimelineValid -and
        $observedUtc - $heartbeatUtc -le [TimeSpan]::FromSeconds($HeartbeatDeadlineSeconds)

    $failureCode = ''
    $failureMessage = ''
    if (-not $manifestReadable) {
        $failureCode = 'ParentManifestUnreadable'
        $failureMessage = 'The parent manifest was unavailable during admission.'
    }
    elseif (-not $leaseReadable) {
        $failureCode = 'ParentLeaseUnreadable'
        $failureMessage = 'The parent lease was unavailable during admission.'
    }
    elseif (-not $intentReadable) {
        $failureCode = 'ParentAttemptIntentUnreadable'
        $failureMessage = 'The parent attempt intent was unavailable during admission.'
    }
    elseif (-not $manifestCommandMatches) {
        $failureCode = 'ParentCommandMismatch'
        $failureMessage = 'The parent manifest command did not match DedicatedSpawnSmoke.'
    }
    elseif (-not $manifestRunIdMatches) {
        $failureCode = 'ParentManifestRunIdMismatch'
        $failureMessage = 'The parent manifest RunId did not match the requested parent.'
    }
    elseif (-not $manifestNonceValid) {
        $failureCode = 'ParentManifestNonceInvalid'
        $failureMessage = 'The parent manifest nonce was not an exact SHA-256 value.'
    }
    elseif (-not $leaseRunIdMatches) {
        $failureCode = 'ParentLeaseRunIdMismatch'
        $failureMessage = 'The parent lease RunId did not match the requested parent.'
    }
    elseif (-not $leaseNonceMatches) {
        $failureCode = 'ParentLeaseNonceMismatch'
        $failureMessage = 'The parent lease nonce did not match the parent manifest.'
    }
    elseif (-not $intentParentRunIdMatches) {
        $failureCode = 'ParentIntentRunIdMismatch'
        $failureMessage = 'The attempt intent did not match the requested parent RunId.'
    }
    elseif (-not $intentChildRunIdMatches) {
        $failureCode = 'ParentIntentChildRunIdMismatch'
        $failureMessage = 'The attempt intent did not match the child RunId.'
    }
    elseif (-not $intentAttemptMatches) {
        $failureCode = 'ParentIntentAttemptMismatch'
        $failureMessage = 'The attempt intent did not match the child attempt number.'
    }
    elseif (-not $intentNonceMatches) {
        $failureCode = 'ParentIntentNonceMismatch'
        $failureMessage = 'The attempt intent nonce did not match the parent manifest.'
    }
    elseif (-not $leaseOwnerProcessIdMatches) {
        $failureCode = 'ParentLeaseProcessIdMismatch'
        $failureMessage = 'The parent lease owner PID did not match the child process parent.'
    }
    elseif (-not $leaseStatusActive) {
        $failureCode = 'ParentLeaseNotActive'
        $failureMessage = 'The parent lease was not Active.'
    }
    elseif (-not $heartbeatTimelineValid) {
        $failureCode = 'ParentHeartbeatInvalid'
        $failureMessage = 'The parent lease heartbeat was missing or outside the valid timeline.'
    }
    elseif (-not $heartbeatFresh) {
        $failureCode = 'ParentHeartbeatStale'
        $failureMessage = 'The parent lease heartbeat exceeded the admission deadline.'
    }

    return [pscustomobject][ordered]@{
        Schema = 'coop-spawn-smoke-parent-admission-v1'
        Accepted = [string]::IsNullOrWhiteSpace($failureCode)
        RetryableReadFailure = $failureCode -in @(
            'ParentManifestUnreadable',
            'ParentLeaseUnreadable',
            'ParentAttemptIntentUnreadable')
        FailureCode = $failureCode
        FailureMessage = $failureMessage
        ObservedUtc = $observedUtc.ToString('O')
        HeartbeatUtc = if ($null -ne $heartbeatUtc) { $heartbeatUtc.ToString('O') } else { $null }
        HeartbeatAgeMilliseconds = $heartbeatAgeMilliseconds
        HeartbeatDeadlineSeconds = $HeartbeatDeadlineSeconds
        Facts = [pscustomobject][ordered]@{
            ParentManifestReadable = $manifestReadable
            ParentLeaseReadable = $leaseReadable
            ParentAttemptIntentReadable = $intentReadable
            ParentCommandMatches = $manifestCommandMatches
            ParentManifestRunIdMatches = $manifestRunIdMatches
            ParentManifestNonceValid = $manifestNonceValid
            ParentLeaseRunIdMatches = $leaseRunIdMatches
            ParentLeaseNonceMatches = $leaseNonceMatches
            ParentIntentRunIdMatches = $intentParentRunIdMatches
            ParentIntentChildRunIdMatches = $intentChildRunIdMatches
            ParentIntentAttemptParsed = $intentAttemptParsed
            ParentIntentAttemptMatches = $intentAttemptMatches
            ParentIntentNonceMatches = $intentNonceMatches
            ParentLeaseProcessIdParsed = $leaseOwnerProcessIdParsed
            ParentLeaseProcessIdMatches = $leaseOwnerProcessIdMatches
            ParentLeaseStatusActive = $leaseStatusActive
            ParentHeartbeatTimelineValid = $heartbeatTimelineValid
            ParentHeartbeatFresh = $heartbeatFresh
        }
    }
}

function Get-CoopSpawnSmokeParentReadFallbackCore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Admission,
        [AllowNull()]$LastAcceptedUtc,
        [Parameter(Mandatory = $true)][bool]$CachedParentProcessIdentityMatched,
        [Parameter(Mandatory = $true)][DateTime]$NowUtc,
        [ValidateRange(1, 3600)][int]$HeartbeatDeadlineSeconds = 10
    )

    if (-not [string]::Equals(
            [string](Get-CoopOptionalPropertyValue -InputObject $Admission -Name 'Schema'),
            'coop-spawn-smoke-parent-admission-v1',
            [StringComparison]::Ordinal)) {
        throw 'Parent read fallback requires an exact parent-admission-v1 result.'
    }

    $observedUtc = $NowUtc.ToUniversalTime()
    $acceptedUtc = ConvertTo-CoopUtcDateTime -Value $LastAcceptedUtc
    $acceptedTimelineValid = $null -ne $acceptedUtc -and $acceptedUtc -le $observedUtc.AddMinutes(1)
    $acceptedAgeMilliseconds = if ($null -ne $acceptedUtc) {
        [long][Math]::Round(($observedUtc - $acceptedUtc).TotalMilliseconds)
    }
    else { $null }
    $insideWindow = $acceptedTimelineValid -and
        $observedUtc - $acceptedUtc -le [TimeSpan]::FromSeconds($HeartbeatDeadlineSeconds)
    $retryableReadFailure = [bool](
        Get-CoopOptionalPropertyValue -InputObject $Admission -Name 'RetryableReadFailure')
    $allowed = $retryableReadFailure -and $CachedParentProcessIdentityMatched -and $insideWindow

    $failureCode = ''
    if (-not $allowed) {
        if (-not $retryableReadFailure) { $failureCode = 'ParentAdmissionNotRetryable' }
        elseif (-not $CachedParentProcessIdentityMatched) { $failureCode = 'ParentProcessIdentityLost' }
        else { $failureCode = 'ParentAdmissionReadGraceExpired' }
    }

    return [pscustomobject][ordered]@{
        Schema = 'coop-spawn-smoke-parent-read-fallback-v1'
        Allowed = $allowed
        FailureCode = $failureCode
        AdmissionFailureCode = [string](
            Get-CoopOptionalPropertyValue -InputObject $Admission -Name 'FailureCode')
        CachedParentProcessIdentityMatched = $CachedParentProcessIdentityMatched
        LastAcceptedUtc = if ($null -ne $acceptedUtc) { $acceptedUtc.ToString('O') } else { $null }
        LastAcceptedTimelineValid = $acceptedTimelineValid
        LastAcceptedAgeMilliseconds = $acceptedAgeMilliseconds
        HeartbeatDeadlineSeconds = $HeartbeatDeadlineSeconds
        ObservedUtc = $observedUtc.ToString('O')
    }
}

function Get-CoopFileSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not [System.IO.File]::Exists($fullPath)) { throw "File does not exist: $fullPath" }
    $stream = New-Object System.IO.FileStream(
        $fullPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

function Get-CoopSharedRuntimeResourceIdsCore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$AutomationRoot,
        [Parameter(Mandatory = $true)][string]$GameRoot,
        [Parameter(Mandatory = $true)][string]$DedicatedServerRoot,
        [Parameter(Mandatory = $true)][string]$ComputerName,
        [Parameter(Mandatory = $true)][string]$MachineProfileName,
        [Parameter(Mandatory = $true)][int[]]$UdpPorts
    )

    if ([string]::IsNullOrWhiteSpace($ComputerName) -or
        [string]::IsNullOrWhiteSpace($MachineProfileName)) {
        throw 'Shared runtime lock machine and profile identities must be non-empty.'
    }

    $uniquePorts = @($UdpPorts | Sort-Object -Unique)
    if ($uniquePorts.Count -eq 0 -or @($uniquePorts | Where-Object { $_ -lt 1 -or $_ -gt 65535 }).Count -ne 0) {
        throw 'Shared runtime lock UDP ports must contain at least one valid port.'
    }

    $resourceIds = New-Object 'System.Collections.Generic.List[string]'
    $resourceIds.Add([string]::Concat(
        'bridge-root:',
        ([System.IO.Path]::GetFullPath($AutomationRoot)).ToUpperInvariant())) | Out-Null
    $resourceIds.Add([string]::Concat(
        'game-install:',
        ([System.IO.Path]::GetFullPath($GameRoot)).ToUpperInvariant())) | Out-Null
    $resourceIds.Add([string]::Concat(
        'dedicated-install:',
        ([System.IO.Path]::GetFullPath($DedicatedServerRoot)).ToUpperInvariant())) | Out-Null
    $resourceIds.Add([string]::Concat(
        'machine-profile:',
        $ComputerName.ToUpperInvariant(),
        ':',
        $MachineProfileName.ToUpperInvariant())) | Out-Null
    foreach ($udpPort in $uniquePorts) {
        $resourceIds.Add([string]::Concat('udp-port:', [string]$udpPort)) | Out-Null
    }

    $canonicalIds = @($resourceIds.ToArray() | Sort-Object -Unique)
    $expectedCount = 4 + $uniquePorts.Count
    if ($canonicalIds.Count -ne $expectedCount) {
        throw "Shared runtime resource construction produced $($canonicalIds.Count) ids; expected $expectedCount."
    }
    return $canonicalIds
}

function Enter-CoopSharedRuntimeLocksCore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$LockRoot,
        [Parameter(Mandatory = $true)][string[]]$ResourceIds,
        [Parameter(Mandatory = $true)][string]$RunId,
        [Parameter(Mandatory = $true)][int]$OwnerProcessId,
        [Parameter(Mandatory = $true)][DateTime]$OwnerProcessStartUtc
    )

    $fullLockRoot = [System.IO.Path]::GetFullPath($LockRoot)
    [System.IO.Directory]::CreateDirectory($fullLockRoot) | Out-Null
    $streams = New-Object 'System.Collections.Generic.List[System.IO.FileStream]'
    $records = New-Object 'System.Collections.Generic.List[object]'
    try {
        foreach ($resourceId in @($ResourceIds | Sort-Object -Unique)) {
            if ([string]::IsNullOrWhiteSpace($resourceId)) { throw 'Shared runtime lock resource ids must be non-empty.' }
            $hashBytes = [System.Text.Encoding]::UTF8.GetBytes($resourceId)
            $algorithm = [System.Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($algorithm.ComputeHash($hashBytes)).Replace('-', '') }
            finally { $algorithm.Dispose() }
            $lockPath = Join-Path $fullLockRoot ($hash + '.lock')
            try {
                $stream = New-Object System.IO.FileStream(
                    $lockPath,
                    [System.IO.FileMode]::OpenOrCreate,
                    [System.IO.FileAccess]::ReadWrite,
                    [System.IO.FileShare]::None)
            }
            catch {
                throw "Shared runtime resource is already locked: $resourceId. $($_.Exception.Message)"
            }
            $streams.Add($stream) | Out-Null
            $record = [ordered]@{
                ResourceId = $resourceId
                LockPath = [System.IO.Path]::GetFullPath($lockPath)
                RunId = $RunId
                OwnerProcessId = $OwnerProcessId
                OwnerProcessStartUtc = $OwnerProcessStartUtc.ToUniversalTime().ToString('O')
                AcquiredUtc = [DateTime]::UtcNow.ToString('O')
            }
            $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes(
                (($record | ConvertTo-Json -Depth 10) + [Environment]::NewLine))
            $stream.SetLength(0)
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
            $records.Add([pscustomobject]$record) | Out-Null
        }
        return [pscustomobject]@{
            Streams = $streams
            Records = $records.ToArray()
        }
    }
    catch {
        foreach ($stream in $streams) { $stream.Dispose() }
        throw
    }
}

function Exit-CoopSharedRuntimeLocksCore {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$LockSet)

    foreach ($stream in @($LockSet.Streams)) { if ($null -ne $stream) { $stream.Dispose() } }
    $probes = New-Object 'System.Collections.Generic.List[object]'
    foreach ($record in @($LockSet.Records)) {
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
        $probes.Add([pscustomobject][ordered]@{
            ResourceId = [string]$record.ResourceId
            LockPath = [string]$record.LockPath
            ReleasedAndReacquired = $released
            Failure = $failure
        }) | Out-Null
    }
    return $probes.ToArray()
}

function Get-CoopFatalHelperExecutablePathsCore {
    [CmdletBinding()]
    param(
        [AllowEmptyString()][string]$GameRoot,
        [AllowEmptyString()][string]$DedicatedServerRoot,
        [Parameter(Mandatory = $true)][string]$SystemRoot
    )

    if ([string]::IsNullOrWhiteSpace($SystemRoot)) { throw 'SystemRoot is required.' }
    $paths = New-Object 'System.Collections.Generic.List[string]'
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in @(
        $(if (-not [string]::IsNullOrWhiteSpace($GameRoot)) {
            Join-Path $GameRoot 'bin\CrashUploader.Publish\CrashUploader.Publish.exe'
        }),
        $(if (-not [string]::IsNullOrWhiteSpace($DedicatedServerRoot)) {
            Join-Path $DedicatedServerRoot 'bin\CrashUploader.Publish\CrashUploader.Publish.exe'
        }),
        (Join-Path $SystemRoot 'System32\WerFault.exe'))) {
        if ([string]::IsNullOrWhiteSpace([string]$path)) { continue }
        $fullPath = [System.IO.Path]::GetFullPath([string]$path)
        if ($seen.Add($fullPath)) { $paths.Add($fullPath) | Out-Null }
    }
    return $paths.ToArray()
}

function Get-CoopRuntimeCleanupGraceSecondsCore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RoleType,
        [ValidateRange(1, 60)][int]$DefaultGraceSeconds = 15,
        [ValidateRange(1, 60)][int]$SupportGraceSeconds = 1
    )

    if ([string]::Equals($RoleType, 'RuntimeSupport', [StringComparison]::Ordinal) -or
        [string]::Equals($RoleType, 'RuntimeFailureSupport', [StringComparison]::Ordinal)) {
        return $SupportGraceSeconds
    }
    return $DefaultGraceSeconds
}

function Get-CoopCorrelatedFailureProcessesFromSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Snapshot,
        [Parameter(Mandatory = $true)][int[]]$OwnedRootProcessIds,
        [Parameter(Mandatory = $true)][string[]]$AllowedExecutablePaths
    )

    if ($OwnedRootProcessIds.Count -eq 0) { return @() }
    $allowed = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $AllowedExecutablePaths) {
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            $allowed.Add([System.IO.Path]::GetFullPath($path)) | Out-Null
        }
    }
    $descendants = @(Get-CoopDescendantProcessRecordsFromSnapshot `
        -Snapshot $Snapshot `
        -RootProcessIds $OwnedRootProcessIds `
        -MaximumDescendants 256)
    $descendantIds = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach ($record in $descendants) { $descendantIds.Add([int]$record.ProcessId) | Out-Null }
    $correlatedProcesses = New-Object 'System.Collections.Generic.List[object]'
    foreach ($record in $Snapshot) {
        $pathValue = Get-CoopOptionalPropertyValue -InputObject $record -Name 'ExecutablePath'
        if ([string]::IsNullOrWhiteSpace([string]$pathValue)) {
            $pathValue = Get-CoopOptionalPropertyValue -InputObject $record -Name 'Path'
        }
        if ([string]::IsNullOrWhiteSpace([string]$pathValue) -or
            -not $allowed.Contains([System.IO.Path]::GetFullPath([string]$pathValue))) { continue }

        $processId = [int](Get-CoopOptionalPropertyValue -InputObject $record -Name 'ProcessId')
        $commandLine = [string](Get-CoopOptionalPropertyValue -InputObject $record -Name 'CommandLine')
        $correlationSource = if ($descendantIds.Contains($processId)) { 'OwnedProcessTree' } else { '' }
        if ([string]::IsNullOrEmpty($correlationSource) -and -not [string]::IsNullOrWhiteSpace($commandLine)) {
            foreach ($rootId in $OwnedRootProcessIds) {
                if ($commandLine -match ('(?<!\d)' + [Regex]::Escape([string]$rootId) + '(?!\d)')) {
                    $correlationSource = 'ExactCommandLinePid'
                    break
                }
            }
        }
        if ([string]::IsNullOrEmpty($correlationSource)) { continue }
        $match = [pscustomobject][ordered]@{
            ProcessId = $processId
            ParentProcessId = [int](Get-CoopOptionalPropertyValue -InputObject $record -Name 'ParentProcessId')
            ExecutablePath = [System.IO.Path]::GetFullPath([string]$pathValue)
            CommandLine = $commandLine
            CreationDate = Get-CoopOptionalPropertyValue -InputObject $record -Name 'CreationDate'
            CorrelationSource = $correlationSource
        }
        $correlatedProcesses.Add($match) | Out-Null
    }
    return $correlatedProcesses.ToArray()
}

function Assert-CoopClientLaunchArtifact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Artifact,
        [Parameter(Mandatory = $true)][string]$ExpectedRunId,
        [Parameter(Mandatory = $true)][string]$ExpectedClientModuleSha256,
        [Parameter(Mandatory = $true)][string]$ExpectedExecutablePath,
        [Parameter(Mandatory = $true)][ValidateRange(1, [int]::MaxValue)][int]$ExpectedParentProcessId
    )

    if ([string]::IsNullOrWhiteSpace($ExpectedRunId)) { throw 'ExpectedRunId is required.' }
    if ($ExpectedClientModuleSha256 -notmatch '^[A-Fa-f0-9]{64}$') {
        throw 'ExpectedClientModuleSha256 is invalid.'
    }

    $schema = [string](Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'Schema')
    if (-not [string]::Equals($schema, 'coop-automation-client-launch-v4', [StringComparison]::Ordinal)) {
        throw "Client launch artifact schema '$schema' is not supported."
    }
    if (-not [string]::Equals(
        [string](Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'RunId'),
        $ExpectedRunId,
        [StringComparison]::Ordinal)) {
        throw 'Client launch artifact RunId does not match the active run.'
    }
    if (-not [string]::Equals(
        [string](Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'ClientModuleSha256'),
        $ExpectedClientModuleSha256,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Client launch artifact module hash does not match the selected client identity.'
    }
    if (-not [string]::Equals(
        [string](Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'IdentityState'),
        'Verified',
        [StringComparison]::Ordinal)) {
        throw 'Client launch artifact is not verified.'
    }
    if (-not [bool](Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'ExistingRunContractUsed') -or
        -not [string]::Equals(
            [string](Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'ResultPolicy'),
            'Suppress',
            [StringComparison]::Ordinal) -or
        [bool](Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'UiAutomationUsed') -or
        [bool](Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'StartGameIssued') -or
        [bool](Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'MissionOpenIssued')) {
        throw 'Client launch artifact violates the connection-only automation boundary.'
    }

    $identity = Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'ProcessIdentity'
    if ($null -eq $identity) { throw 'Client launch artifact process identity is missing.' }
    if (-not [string]::Equals(
        [string](Get-CoopOptionalPropertyValue -InputObject $identity -Name 'IdentityState'),
        'Verified',
        [StringComparison]::Ordinal) -or
        -not [string]::Equals(
            [string](Get-CoopOptionalPropertyValue -InputObject $identity -Name 'RoleType'),
            'MultiplayerClient',
            [StringComparison]::Ordinal) -or
        -not [string]::Equals(
            [string](Get-CoopOptionalPropertyValue -InputObject $identity -Name 'RoleInstanceId'),
            'multiplayer-client-01',
            [StringComparison]::Ordinal)) {
        throw 'Client launch artifact process role identity is invalid.'
    }

    $entryProcessId = [int](Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'EntryPid')
    $identityProcessId = [int](Get-CoopOptionalPropertyValue -InputObject $identity -Name 'ProcessId')
    if ($entryProcessId -le 0 -or $identityProcessId -ne $entryProcessId) {
        throw 'Client launch artifact process IDs do not match.'
    }
    $entryParentProcessId = [int](Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'EntryParentPid')
    $identityParentProcessId = [int](Get-CoopOptionalPropertyValue -InputObject $identity -Name 'ParentProcessId')
    $identityExpectedParentProcessId = [int](Get-CoopOptionalPropertyValue -InputObject $identity -Name 'ExpectedParentProcessId')
    if ($entryParentProcessId -ne $ExpectedParentProcessId -or
        $identityParentProcessId -ne $ExpectedParentProcessId -or
        $identityExpectedParentProcessId -ne $ExpectedParentProcessId) {
        throw 'Client launch artifact parent process identity does not match the aggregate runner.'
    }

    $expectedPath = [System.IO.Path]::GetFullPath($ExpectedExecutablePath)
    $entryPath = [System.IO.Path]::GetFullPath(
        [string](Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'EntryPath'))
    $identityPath = [System.IO.Path]::GetFullPath(
        [string](Get-CoopOptionalPropertyValue -InputObject $identity -Name 'ExecutablePath'))
    if (-not [string]::Equals($entryPath, $expectedPath, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals($identityPath, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Client launch artifact executable path does not match the requested client executable.'
    }

    $artifactLaunchOperationId = [string](Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'LaunchOperationId')
    $identityLaunchOperationId = [string](Get-CoopOptionalPropertyValue -InputObject $identity -Name 'LaunchOperationId')
    if ([string]::IsNullOrWhiteSpace($artifactLaunchOperationId) -or
        -not [string]::Equals($artifactLaunchOperationId, $identityLaunchOperationId, [StringComparison]::Ordinal)) {
        throw 'Client launch operation identity is missing or inconsistent.'
    }

    $entryStartUtc = ConvertTo-CoopUtcDateTime -Value (
        Get-CoopOptionalPropertyValue -InputObject $Artifact -Name 'EntryStartUtc')
    $identityStartUtc = ConvertTo-CoopUtcDateTime -Value (
        Get-CoopOptionalPropertyValue -InputObject $identity -Name 'ProcessStartUtc')
    $launchStartedUtc = ConvertTo-CoopUtcDateTime -Value (
        Get-CoopOptionalPropertyValue -InputObject $identity -Name 'LaunchStartedUtc')
    $launchObservedUtc = ConvertTo-CoopUtcDateTime -Value (
        Get-CoopOptionalPropertyValue -InputObject $identity -Name 'LaunchObservedUtc')
    if ($null -eq $entryStartUtc -or $null -eq $identityStartUtc -or
        $null -eq $launchStartedUtc -or $null -eq $launchObservedUtc) {
        throw 'Client launch artifact process times are incomplete or invalid.'
    }
    if ($launchObservedUtc -lt $launchStartedUtc -or
        [Math]::Abs(($entryStartUtc - $identityStartUtc).TotalSeconds) -ge 1.0 -or
        $identityStartUtc -lt $launchStartedUtc.AddSeconds(-2) -or
        $identityStartUtc -gt $launchObservedUtc.AddSeconds(2)) {
        throw 'Client launch artifact process times do not match the exact launch window.'
    }

    $identityExecutableSha256 = [string](
        Get-CoopOptionalPropertyValue -InputObject $identity -Name 'ExecutableSha256')
    if ($identityExecutableSha256 -notmatch '^[A-Fa-f0-9]{64}$') {
        throw 'Client launch artifact executable hash is invalid.'
    }
    $actualExecutableSha256 = Get-CoopFileSha256 -Path $expectedPath
    if (-not [string]::Equals(
        $identityExecutableSha256,
        $actualExecutableSha256,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Client launch artifact executable hash does not match the requested client executable.'
    }

    return [ordered]@{
        IdentityState = 'Verified'
        LaunchOperationId = $identityLaunchOperationId
        RoleType = 'MultiplayerClient'
        RoleInstanceId = 'multiplayer-client-01'
        ProcessId = $identityProcessId
        ParentProcessId = $identityParentProcessId
        ExpectedParentProcessId = $identityExpectedParentProcessId
        ProcessStartUtc = $identityStartUtc.ToString('O')
        ExecutablePath = $expectedPath
        ExecutableSha256 = $actualExecutableSha256
        PathEvidenceSource = [string](Get-CoopOptionalPropertyValue -InputObject $identity -Name 'PathEvidenceSource')
        LaunchStartedUtc = $launchStartedUtc.ToString('O')
        LaunchObservedUtc = $launchObservedUtc.ToString('O')
        RegisteredUtc = [string](Get-CoopOptionalPropertyValue -InputObject $identity -Name 'RegisteredUtc')
        VerifiedUtc = [string](Get-CoopOptionalPropertyValue -InputObject $identity -Name 'VerifiedUtc')
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
        [ValidateRange(1, 65536)][int]$MaximumLinesPerStream = 8192,
        [ValidateRange(10, 5000)][int]$MaximumDrainMillisecondsPerStream = 100
    )

    if ([bool]$Capture.Disposed) { return }
    foreach ($streamName in @('Output', 'Error')) {
        $streamDrainStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
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
            if ($streamDrainStopwatch.ElapsedMilliseconds -ge $MaximumDrainMillisecondsPerStream) { break }
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

function Get-CoopPidCorrelatedNativeLogDescriptors {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][ValidateRange(1, [int]::MaxValue)][int]$ProcessId)

    $names = @(Get-CoopPidCorrelatedNativeLogNames -ProcessId $ProcessId)
    return @(
        [pscustomobject][ordered]@{ FileName = $names[0]; Required = $true; Kind = 'Native' },
        [pscustomobject][ordered]@{ FileName = $names[1]; Required = $true; Kind = 'NativeErrors' },
        [pscustomobject][ordered]@{ FileName = $names[2]; Required = $false; Kind = 'Watchdog' }
    )
}

function Get-CoopSpawnSmokeProfileCore {
    return [ordered]@{
        Profile = 'FieldDedicatedSpawnSmokeV1'
        FixtureId = 'field-current-sanitized-v1'
        Scene = 'battle_terrain_029'
        GameType = 'CoopBattle'
        MissionShell = 'MultiplayerBattle'
        CampaignId = 'fixture-campaign-001'
        BattleId = 'fixture-battle-001'
        BattleInstanceId = 'fixture-battle-instance-001'
        Stage = 'PreBattleHold'
        FixtureRelativeRoot = 'payloads/field-current'
        PayloadFile = 'battle_roster.sanitized.json'
        PayloadLength = 259744
        PayloadSha256 = 'B47D7AF7FA057C36CA8EF759A6D597C00007158A22E3A556AC57A1299579D49D'
        MetadataSha256 = '06169055E66E4DC0719AF3A8CB5A5E082CAF487DD18B1E4D18317B5C80973950'
        OracleSha256 = 'D9F593D17BEA35A8D8717867C6F7AE79BC721C2FA3A9FFD90F474A001FD023DA'
    }
}

function Assert-CoopNoReparsePathCore {
    param([Parameter(Mandatory = $true)][string]$Path)
    for ($current = [IO.Path]::GetFullPath($Path); -not [string]::IsNullOrEmpty($current); $current = [IO.Path]::GetDirectoryName($current)) {
        if (([IO.File]::Exists($current) -or [IO.Directory]::Exists($current)) -and
            (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw "FixtureReparsePointRejected: $current"
        }
    }
}

function Copy-CoopSpawnSmokeFixtureCore {
    param([string]$RepositoryRoot, [string]$RunRoot)
    $profile = Get-CoopSpawnSmokeProfileCore
    $sourceRoot = Join-Path $RepositoryRoot 'Tests\Fixtures\Automation\field-current'
    $targetRoot = Join-Path $RunRoot 'payloads\field-current'
    Assert-CoopNoReparsePathCore -Path $sourceRoot
    Assert-CoopNoReparsePathCore -Path $targetRoot
    if ([IO.Directory]::Exists($targetRoot)) { throw 'Fixture target must be fresh.' }
    $files = [ordered]@{
        'battle_roster.sanitized.json' = $profile.PayloadSha256
        'fixture.sanitized.metadata.json' = $profile.MetadataSha256
        'fixture.oracle.json' = $profile.OracleSha256
    }
    $bytesByName = @{}
    foreach ($name in $files.Keys) {
        $source = Join-Path $sourceRoot $name
        Assert-CoopNoReparsePathCore -Path $source
        $info = [IO.FileInfo]::new($source)
        if (-not $info.Exists -or $info.Length -le 0 -or $info.Length -gt 1048576) { throw 'Fixture file missing or oversized.' }
        $bytes = [IO.File]::ReadAllBytes($source)
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '') } finally { $sha.Dispose() }
        if ($hash -cne $files[$name] -or ($name -eq $profile.PayloadFile -and $bytes.Length -ne $profile.PayloadLength)) {
            throw "Fixture integrity mismatch: $name"
        }
        $bytesByName[$name] = $bytes
    }
    [IO.Directory]::CreateDirectory($targetRoot) | Out-Null
    foreach ($name in $files.Keys) {
        $stream = [IO.File]::Open((Join-Path $targetRoot $name), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($bytesByName[$name], 0, $bytesByName[$name].Length) } finally { $stream.Dispose() }
    }
    return $profile
}

function New-CoopSpawnSmokeRequestCore {
    param([Parameter(Mandatory = $true)]$BootstrapRequest)
    $p = Get-CoopSpawnSmokeProfileCore
    $request = [ordered]@{}
    foreach ($key in $BootstrapRequest.Keys) { $request[$key] = $BootstrapRequest[$key] }
    $request.BootstrapProfile = $p.Profile
    $request.GameType = $p.GameType
    $request.Map = $p.Scene
    foreach ($name in @('FixtureId', 'FixtureRelativeRoot', 'CampaignId', 'BattleId', 'BattleInstanceId', 'Stage')) {
        $request[$name] = $p[$name]
    }
    $request.FixtureLength = $p.PayloadLength
    $request.FixtureSha256 = $p.PayloadSha256
    $request.OracleSha256 = $p.OracleSha256
    return $request
}

function Get-CoopSpawnSmokeLocalResultPathCore {
    param([Parameter(Mandatory = $true)][string]$RunRoot)
    $path = [IO.Path]::GetFullPath((Join-Path $RunRoot 'state\bridge\battle_result.json'))
    Assert-CoopNoReparsePathCore -Path $path
    return $path
}

function Get-CoopSpawnSmokeSentinelTextCore {
    param([Parameter(Mandatory = $true)][string]$RunId)
    if ($RunId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$') { throw 'Invalid local sentinel RunId.' }
    return 'CoopSpectator local result publication sentinel' + [char]10 + 'RunId=' + $RunId + [char]10
}

function Get-CoopSpawnSmokeSentinelSha256Core {
    param([Parameter(Mandatory = $true)][string]$RunId)
    $bytes = [Text.Encoding]::UTF8.GetBytes((Get-CoopSpawnSmokeSentinelTextCore -RunId $RunId))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '') }
    finally { $sha.Dispose() }
}

function Initialize-CoopSpawnSmokeLocalResultCore {
    param([Parameter(Mandatory = $true)][string]$RunRoot, [Parameter(Mandatory = $true)][string]$RunId)
    $expected = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('CoopSpectator\Automation\' + $RunId))).TrimEnd('\','/')
    if ([IO.Path]::GetFullPath($RunRoot).TrimEnd('\','/') -cne $expected) { throw 'Local sentinel root does not match RunId.' }
    $path = Get-CoopSpawnSmokeLocalResultPathCore -RunRoot $RunRoot
    [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
    $bytes = [Text.Encoding]::UTF8.GetBytes((Get-CoopSpawnSmokeSentinelTextCore -RunId $RunId))
    $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
    if ((Get-CoopFileSha256 -Path $path) -cne (Get-CoopSpawnSmokeSentinelSha256Core -RunId $RunId)) {
        throw 'Local result sentinel integrity mismatch.'
    }
    return $path
}


function Assert-CoopSpawnSmokeEvidenceCore {
    param([Parameter(Mandatory = $true)]$Evidence, [Parameter(Mandatory = $true)][string]$RunRoot)
    $p = Get-CoopSpawnSmokeProfileCore
    if ($Evidence.Profile -cne $p.Profile -or $Evidence.FixtureId -cne $p.FixtureId -or
        $Evidence.PayloadSha256 -cne $p.PayloadSha256 -or $Evidence.OracleSha256 -cne $p.OracleSha256 -or
        $Evidence.AuthoritativeSource -cne 'CoopMissionSpawnLogic.TryCaptureAutomationSpawnSmokeEvidence' -or
        $Evidence.StartMissionRequests -ne 1 -or $Evidence.EndMissionRequests -ne 1 -or
        -not $Evidence.InitialStateWasClean -or -not $Evidence.MissionDisposed -or -not $Evidence.ProtectedResultUnchanged -or
        $Evidence.ProtectedResultScope -cne 'RunLocal' -or
        $Evidence.ProtectedResultRelativePath -cne 'state/bridge/battle_result.json' -or
        $Evidence.PhaseBeforeEnd -cne 'PreBattleHold' -or $Evidence.ResultAttempts -lt 1 -or
        $Evidence.ResultAttempts -ne $Evidence.SuppressedResults -or $Evidence.ResultEntriesAtAttempt -ne 47) {
        throw 'Spawn smoke terminal/abort/result evidence is incomplete.'
    }
    $o = $Evidence.Observation
    if ($null -eq $o -or $o.CampaignId -cne $p.CampaignId -or $o.BattleId -cne $p.BattleId -or
        $o.BattleInstanceId -cne $p.BattleInstanceId -or $o.Stage -cne $p.Stage -or
        $o.Scene -cne $p.Scene -or $o.MissionShell -cne $p.MissionShell -or
        $o.ScenarioKind -cne 'FieldBattle' -or $o.CampaignBattleType -cne 'FieldBattle' -or $o.IsSiegeBattle -or
        $o.Phase -cne 'PreBattleHold' -or $o.ConnectedClientCount -ne 0 -or -not $o.NativeMaterializationComplete -or
        @($o.Violations).Count -ne 0 -or @($o.Agents).Count -ne 74 -or $o.ActiveMountCount -ne 21 -or
        $o.ResultEntryCount -ne 47 -or -not $o.ResultGuardWasClear) { throw 'Spawn smoke mission observation is invalid.' }
    foreach ($controller in @('MissionMultiplayerCoopBattle','CoopMissionSpawnLogic','CoopMissionNetworkBridge',
        'MissionLobbyComponent','MissionAgentSpawnLogic','BannerBearerLogic')) {
        if (-not (@($o.Controllers) -ccontains $controller)) { throw "Missing native controller: $controller" }
    }
    $fixturePath = Join-Path $RunRoot 'payloads\field-current\battle_roster.sanitized.json'
    Assert-CoopNoReparsePathCore -Path $fixturePath
    if ((Get-CoopFileSha256 -Path $fixturePath) -cne $p.PayloadSha256) { throw 'Retained fixture hash changed.' }
    foreach ($companion in @(
        [pscustomobject]@{ Name='fixture.sanitized.metadata.json'; Hash=$p.MetadataSha256 },
        [pscustomobject]@{ Name='fixture.oracle.json'; Hash=$p.OracleSha256 }
    )) {
        $companionPath = Join-Path ([IO.Path]::GetDirectoryName($fixturePath)) $companion.Name
        Assert-CoopNoReparsePathCore -Path $companionPath
        if ((Get-CoopFileSha256 -Path $companionPath) -cne $companion.Hash) { throw 'Retained fixture companion hash changed.' }
    }
    $snapshot = ([IO.File]::ReadAllText($fixturePath) | ConvertFrom-Json).Snapshot
    $entries = @{}
    foreach ($side in $snapshot.Sides) { foreach ($entry in $side.Troops) { $entries[$entry.EntryId] = $entry } }
    $indices = @{}; $mounts = @{}; $counts = @{}; $teams = @{}; $orientations = @{}
    $heroCount = 0
    foreach ($agent in $o.Agents) {
        $entry = $entries[[string]$agent.EntryId]
        if ($null -eq $entry -or $indices.ContainsKey([int]$agent.AgentIndex) -or $agent.AgentIndex -lt 0 -or
            -not $agent.Active -or -not $agent.NativeOriginAndLedgerMatch -or -not $agent.ExactContractValid -or
            -not $agent.PreSpawnEquipmentInjected) { throw 'Native agent identity or generation mismatch.' }
        $indices[[int]$agent.AgentIndex] = $true
        if (-not $counts.ContainsKey($agent.EntryId)) { $counts[$agent.EntryId] = 0 }
        $counts[$agent.EntryId]++
        if ($agent.SideId -cne $entry.SideId -or @('Attacker','Defender') -cnotcontains $agent.Side -or
            $agent.TeamIndex -lt 0 -or -not $agent.FormationTeamMatches -or $agent.Formation -cne $entry.CampaignFormationClass) {
            throw 'Side/team/formation mismatch.'
        }
        if (($teams.ContainsKey($agent.SideId) -and $teams[$agent.SideId] -ne $agent.TeamIndex) -or
            ($orientations.ContainsKey($agent.SideId) -and $orientations[$agent.SideId] -cne $agent.Side)) { throw 'Side orientation mismatch.' }
        $teams[$agent.SideId] = $agent.TeamIndex; $orientations[$agent.SideId] = $agent.Side
        if ($agent.OriginalCharacterId -cne $entry.OriginalCharacterId -or $agent.IsHero -ne $entry.IsHero -or
            [string]$agent.HeroId -cne [string]$entry.HeroId -or [string]::IsNullOrEmpty($agent.NativeCharacterId) -or
            $agent.NativeCharacterId -cne $agent.ContractNativeCharacterId) { throw 'Character/hero mismatch.' }
        if ($agent.IsHero) { $heroCount++ }
        foreach ($slot in @('Item0','Item1','Item2','Item3','Head','Body','Leg','Gloves','Cape','Horse','HorseHarness')) {
            $actual = Get-CoopOptionalPropertyValue -InputObject $agent.Equipment -Name $slot
            $item = Get-CoopOptionalPropertyValue -InputObject $entry -Name ('Combat' + $slot + 'Id')
            $modifier = Get-CoopOptionalPropertyValue -InputObject $entry -Name ('Combat' + $slot + 'ModifierId')
            $amount = Get-CoopOptionalPropertyValue -InputObject $entry -Name ('Combat' + $slot + 'Amount')
            if ($null -eq $actual -or [string]$actual.ItemId -cne [string]$item -or
                [string]$actual.ModifierId -cne [string]$modifier -or ($null -ne $amount -and $actual.Amount -ne $amount)) {
                throw "Equipment mismatch: $($agent.EntryId)/$slot"
            }
        }
        if ($agent.Mounted -ne $entry.IsMounted) { throw 'Mount policy mismatch.' }
        if ($agent.Mounted) {
            if ($agent.MountAgentIndex -lt 0 -or -not $agent.ReciprocalMountLink -or $mounts.ContainsKey([int]$agent.MountAgentIndex) -or
                $agent.MountHorseId -cne $entry.CombatHorseId -or $agent.MountHarnessId -cne $entry.CombatHorseHarnessId) { throw 'Mount link mismatch.' }
            $mounts[[int]$agent.MountAgentIndex] = $true
        } elseif ($agent.MountAgentIndex -ne -1) { throw 'Unexpected mount.' }
    }
    if ($counts.Count -ne 47 -or $heroCount -ne 4 -or $mounts.Count -ne 21 -or $teams.Count -ne 2 -or
        @($teams.Values | Sort-Object -Unique).Count -ne 2 -or @($orientations.Values | Sort-Object -Unique).Count -ne 2) {
        throw 'Army composition mismatch.'
    }
    foreach ($id in $entries.Keys) {
        if ($counts[$id] -ne ([int]$entries[$id].Count - [int]$entries[$id].WoundedCount)) { throw "Entry multiplicity mismatch: $id" }
    }
}
function Assert-CoopSpawnSmokeAttemptArtifactsCore {
    param($Report, $Manifest, $RunnerRelease, $SharedRelease, [string]$ExpectedRunId, [string]$ExpectedParentRunId, [int]$ExpectedAttempt)
    if ($null -eq $Report -or $null -eq $Manifest -or $null -eq $RunnerRelease -or $null -eq $SharedRelease -or
        $Manifest.RequestedCommand -cne 'DedicatedSpawnSmoke' -or $Manifest.TerminalOutcome -cne 'Pass' -or
        $Manifest.RunId -cne $ExpectedRunId -or $Manifest.ParentRunId -cne $ExpectedParentRunId -or
        $Manifest.SpawnSmokeAttempt -ne $ExpectedAttempt -or $Manifest.RepositoryDirty -or
        $Report.RunId -cne $ExpectedRunId -or $Report.ParentRunId -cne $ExpectedParentRunId -or
        $Report.Attempt -ne $ExpectedAttempt -or $Report.NonceSha256 -cne $Manifest.NonceSha256 -or
        $Report.Schema -cne 'coop-field-spawn-smoke-attempt-v2' -or
        $Report.ResultProtectionScope -cne 'RunLocal' -or $Report.ProductionBattleResultAccess -cne 'NotAccessed' -or
        $Report.Outcome -cne 'Pass' -or $Report.ResultPolicy -cne 'Suppress' -or
        -not $Report.LocalBattleResultUnchanged -or -not $Report.NoFatalHelpersConfirmed -or @($Report.RemainingOwnedProcesses).Count -ne 0 -or
        @($Report.RemainingRequiredPorts).Count -ne 0 -or
        $RunnerRelease.RunId -cne $ExpectedRunId -or -not $RunnerRelease.ReleasedAndReacquired -or
        $SharedRelease.RunId -cne $ExpectedRunId -or @($SharedRelease.Locks).Count -lt 5 -or
        @($SharedRelease.Locks | Where-Object { -not $_.ReleasedAndReacquired }).Count -ne 0) {
        throw 'Child smoke identity, terminal result, process/port cleanup, or lock release is invalid.'
    }
    $expectedRoot = Join-Path ([IO.Path]::GetTempPath()) ('CoopSpectator\Automation\' + $ExpectedRunId)
    $expectedPath = Get-CoopSpawnSmokeLocalResultPathCore -RunRoot $expectedRoot
    $expectedHash = Get-CoopSpawnSmokeSentinelSha256Core -RunId $ExpectedRunId
    foreach ($fact in @($Report.LocalBattleResultBefore, $Report.LocalBattleResultAfter)) {
        if ($null -eq $fact -or -not $fact.Exists -or $fact.Path -ine $expectedPath -or $fact.Sha256 -cne $expectedHash) {
            throw 'Local result before/after evidence is missing, redirected, or changed.'
        }
    }
}

function Assert-CoopSpawnSmokePairCore {
    param([Parameter(Mandatory = $true)][object[]]$Reports)
    if ($Reports.Count -ne 2) { throw 'Exactly two successful smoke attempts are required.' }
    $a = $Reports[0]; $b = $Reports[1]
    if ($a.RunId -ceq $b.RunId -or $a.NonceSha256 -ceq $b.NonceSha256 -or
        $a.BootstrapRequest.CommandId -ceq $b.BootstrapRequest.CommandId -or
        ($a.DedicatedIdentity.ProcessId -eq $b.DedicatedIdentity.ProcessId -and
         $a.DedicatedIdentity.ProcessStartUtc -ceq $b.DedicatedIdentity.ProcessStartUtc) -or
        $a.Outcome -cne 'Pass' -or $b.Outcome -cne 'Pass' -or
        -not $a.DedicatedBootstrapStatus.SmokeEvidence.InitialStateWasClean -or
        -not $b.DedicatedBootstrapStatus.SmokeEvidence.InitialStateWasClean -or
        @($a.RemainingOwnedProcesses).Count -ne 0 -or @($b.RemainingOwnedProcesses).Count -ne 0 -or
        @($a.RemainingRequiredPorts).Count -ne 0 -or @($b.RemainingRequiredPorts).Count -ne 0) {
        throw 'Cross-run smoke isolation is invalid or cleanup is incomplete.'
    }
}
