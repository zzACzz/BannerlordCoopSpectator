param([Parameter(Mandatory=$true)][string]$CandidateDirectory, [switch]$TraceEnabled)
$ErrorActionPreference='Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Use Windows PowerShell 5.1.' }
# A fresh process is required: the diagnostic flag is captured once at type load.
$env:COOPSPECTATOR_MAP_VISUAL_LIFETIME_TRACE = if ($TraceEnabled) {'1'} else {$null}
$env:COOPSPECTATOR_MAP_VISUAL_LIFETIME_TRACE_FILE = $null
. (Join-Path $PSScriptRoot 'Test-MapVisualReadiness.ps1') -CandidateDirectory $CandidateDirectory
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$trace=$assembly.GetType('CoopSpectator.UI.CoopCampaignMapVisualLifetimeTrace',$true)
$events=$trace.GetField('Events',$flags).GetValue($null)
# Null data throws before the stock constructor can call any native function.
try {
    $null=$trace.GetMethod('Create',$flags).Invoke($null,[object[]]@($null,'test',$false,$false,$false))
    throw 'Expected the original null-data exception.'
} catch {
    if ($_.Exception.GetBaseException() -isnot [NullReferenceException]) { throw }
}
$null=$trace.GetMethod('Reset',$flags).Invoke($null,[object[]]@($null))
if (-not $TraceEnabled) {
    if ($events.Count -ne 0) { throw 'Disabled diagnostics retained events.' }
    'PASS default-off creation/reset preserve original outcomes and retain no events.'
    return
}
if ($events.Count -ne 4 -or $events[0] -notmatch 'create-begin' -or
    $events[1] -notmatch 'create-throw.*NullReferenceException' -or
    $events[2] -notmatch 'reset-begin' -or $events[3] -notmatch 'reset-return') {
    throw 'Missing ordered creation-failure/reset evidence.'
}
$record=$trace.GetMethod('Record',$flags)
$longName=('x'*5000)+"`r`n|"
$null=$record.Invoke($null,[object[]]@('test',0,$null,$null,$longName,$null))
if ($events[4].Length -gt 4300 -or $events[4].Contains("`n")) { throw 'Unbounded/multiline event.' }
for ($i=0;$i -lt 2100;$i++) {
    $null=$record.Invoke($null,[object[]]@('test',0,$null,$null,'bounded',$null))
}
if ($events.Count -ne 2048) { throw 'Event limit failed.' }
if ($events.GetType().GetGenericArguments()[0] -ne [string]) { throw 'Trace retains object owners.' }
$allowed=@('System.Threading.Interlocked','System.String','System.Environment',
    'CoopSpectator.UI.CoopCampaignMapVisualLifetimeTrace','System.Object',
    'System.Threading.Monitor','System.Int32','System.DateTime','System.Globalization.CultureInfo',
    'System.UIntPtr','System.UInt64','System.Exception','System.IO.File',
    'System.Collections.Generic.List`1[[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]')
foreach ($call in @(Read-MethodCalls $record)) {
    if ($call.Type -eq 'TaleWorlds.DotNet.NativeObject' -and $call.Name -eq 'get_Pointer') { continue }
    if ($call.Type -notin $allowed) { throw ('Unexpected recording call: '+$call.Type+'.'+$call.Name) }
}
'PASS opt-in ordered failure/reset evidence, bounded single-line strings, 2048-event cap; native access limited to the managed Pointer getter. No native object constructed.'
