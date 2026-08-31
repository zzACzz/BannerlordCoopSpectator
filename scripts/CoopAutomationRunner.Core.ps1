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
