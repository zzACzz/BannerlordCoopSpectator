function New-CoopDedicatedBootstrapCommands {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ServerName,
        [ValidateRange(1, 1024)][int]$MaxNumberOfPlayers = 16,
        [Parameter(Mandatory = $true)][string]$GameType,
        [Parameter(Mandatory = $true)][string]$Map
    )

    if ([string]::IsNullOrWhiteSpace($ServerName)) { throw 'ServerName is required.' }
    if ([string]::IsNullOrWhiteSpace($GameType)) { throw 'GameType is required.' }
    if ([string]::IsNullOrWhiteSpace($Map)) { throw 'Map is required.' }

    $commands = New-Object 'System.Collections.Generic.List[string]'
    $commands.Add('ServerName ' + $ServerName) | Out-Null
    $commands.Add('MaxNumberOfPlayers ' + $MaxNumberOfPlayers.ToString([Globalization.CultureInfo]::InvariantCulture)) | Out-Null
    $commands.Add('GameType ' + $GameType) | Out-Null
    $commands.Add('Map ' + $Map) | Out-Null
    $commands.Add('add_map_to_usable_maps ' + $Map + ' ' + $GameType) | Out-Null
    $commands.Add('start_game') | Out-Null
    return $commands.ToArray()
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
