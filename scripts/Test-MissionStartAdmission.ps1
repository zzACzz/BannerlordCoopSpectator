param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
$gameBin='C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client'
foreach($name in @('TaleWorlds.Library','TaleWorlds.ObjectSystem','TaleWorlds.Core','TaleWorlds.CampaignSystem')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $gameBin ($name+'.dll')))
}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $CandidateDirectory 'CoopSpectator.dll'))
$hostType=$assembly.GetType('CoopSpectator.Campaign.CoopCampaignMissionHost',$true)
$state=$hostType.GetField('_state',[Reflection.BindingFlags]'Static,NonPublic')
$fault=$hostType.GetField('_faulted',[Reflection.BindingFlags]'Static,NonPublic')
$campaign=$hostType.GetField('_campaign',[Reflection.BindingFlags]'Static,NonPublic')
$gate=$hostType.GetProperty('CanAttemptBattleStart',[Reflection.BindingFlags]'Static,Public')
$owns=$hostType.GetProperty('OwnsCommands',[Reflection.BindingFlags]'Static,Public')
if($null -eq $gate){throw 'Candidate lacks battle-start preflight'}
function Assert-Gate($value,$message) { if(-not $value){throw $message} }
# No Game/Campaign construction, process access, native calls or static campaign
# replacement. Only the exact owned-session fields and short-circuit exits run.
try {
    Assert-Gate ($gate.GetValue($null)) 'Unowned legacy path preserved'
    $envelope=[Activator]::CreateInstance($state.FieldType)
    $state.SetValue($null,$envelope)
    $fault.SetValue($null,$true)
    Assert-Gate (-not $gate.GetValue($null)) 'Faulted owner cannot prepare another battle'
    Assert-Gate ($owns.GetValue($null)) 'Faulted owner retains command authority; no legacy fallback'
    $fault.SetValue($null,$false)
    Assert-Gate (-not $gate.GetValue($null)) 'Missing bound campaign cannot prepare a battle'
    Assert-Gate ($owns.GetValue($null)) 'Missing campaign does not authorize another server'
    $state.SetValue($null,$null)
    Assert-Gate ($gate.GetValue($null)) 'Reset owner restores the unowned path'
} finally {
    $state.SetValue($null,$null);$fault.SetValue($null,$false);$campaign.SetValue($null,$null)
}
$repo=Split-Path -Parent $PSScriptRoot
$source=[IO.File]::ReadAllText((Join-Path $repo 'Campaign/BattleDetector.cs'))
$begin=$source.IndexOf('private void TryStartMissionForCurrentRole()')
$end=$source.IndexOf('private static bool TryGetUnsupportedCoopMissionReason',$begin)
$body=$source.Substring($begin,$end-$begin)
$preflight=$body.IndexOf('if (!CoopCampaignMissionHost.CanAttemptBattleStart)')
Assert-Gate ($preflight -ge 0 -and $preflight -lt $body.IndexOf('TryGetUnsupportedCoopMissionReason(') -and
    $preflight -lt $body.IndexOf('TrySendBattleStart()') -and $preflight -lt $body.IndexOf('TryStartDedicatedMissionForCampaignHost()')) 'Preflight precedes scene and roster callbacks for both roles'
'PASS actual compiled mission admission short-circuit and shared role ordering; no native game started'
