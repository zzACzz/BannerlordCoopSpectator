param(
    [Parameter(Mandatory=$true)][string]$CandidateDirectory,
    [switch]$Probe,
    [ValidateSet('Default','MapOff','AutomaticOff','FlightsOff','ExplicitOn')][string]$Case='Default'
)
$ErrorActionPreference='Stop'
$cases=@{
    Default=@($null,$null,$null,$true,$true,$true)
    MapOff=@('0','1','1',$false,$false,$false)
    AutomaticOff=@($null,'0',$null,$true,$false,$true)
    FlightsOff=@($null,$null,'0',$true,$true,$false)
    ExplicitOn=@('1','1','1',$true,$true,$true)
}
$environmentNames=@('COOPSPECTATOR_CAMPAIGN_MAP_PROTOTYPE','COOPSPECTATOR_AUTOMATIC_CAMPAIGN_MISSIONS','COOPSPECTATOR_SIEGE_FLIGHT_OBSERVER')
if($Probe) {
    $assembly=[Reflection.Assembly]::LoadFrom((Join-Path $CandidateDirectory 'CoopSpectator.dll'))
    $featureType=$assembly.GetType('CoopSpectator.Infrastructure.ExperimentalFeatures',$true)
    $names=@('EnableCampaignMapPrototype','EnableAutomaticCampaignMissions','EnableCampaignMapSiegeFlights')
    for($i=0;$i -lt $names.Count;$i++) {
        $actual=$featureType.GetField($names[$i]).GetValue($null)
        if($actual -ne $cases[$Case][$i+3]) {throw ($Case+': unexpected '+$names[$i]+'='+$actual)}
    }
    foreach($name in @('EnableVerboseDiagnostics','EnableTestAutomation')) {
        if($featureType.GetField($name).GetValue($null)) {throw ($Case+': default-off diagnostic/automation gate enabled')}
    }
    'PASS '+$Case+' compiled flags; no game constructed'
    exit 0
}
# Fresh process per case: static readonly production fields must not be reused.
# No user/machine environment changes and no native/game startup.
foreach($caseName in @('Default','MapOff','AutomaticOff','FlightsOff','ExplicitOn')) {
    $start=[Diagnostics.ProcessStartInfo]::new()
    $start.FileName=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $start.Arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$PSCommandPath+'" -CandidateDirectory "'+$CandidateDirectory+'" -Probe -Case '+$caseName
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    for($i=0;$i -lt $environmentNames.Count;$i++) {
        $start.EnvironmentVariables.Remove($environmentNames[$i])
        if($null -ne $cases[$caseName][$i]) {$start.EnvironmentVariables[$environmentNames[$i]]=$cases[$caseName][$i]}
    }
    foreach($name in @('COOPSPECTATOR_VERBOSE_DIAGNOSTICS','COOPSPECTATOR_TEST_AUTOMATION')) {$start.EnvironmentVariables.Remove($name)}
    $child=[Diagnostics.Process]::Start($start)
    try {
        $output=$child.StandardOutput.ReadToEnd();$errors=$child.StandardError.ReadToEnd();$child.WaitForExit()
        if($child.ExitCode -ne 0) {throw ($caseName+': '+$errors)}
        $output.Trim()
    } finally {$child.Dispose()}
}
