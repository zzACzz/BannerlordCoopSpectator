param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
$game='C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
$bin=Join-Path $game 'bin\Win64_Shipping_Client'
foreach($name in @('TaleWorlds.Library','TaleWorlds.ObjectSystem','TaleWorlds.Core','TaleWorlds.Engine','TaleWorlds.InputSystem','TaleWorlds.ScreenSystem','TaleWorlds.CampaignSystem','TaleWorlds.MountAndBlade','TaleWorlds.MountAndBlade.ViewModelCollection')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $bin ($name+'.dll')))
}
[void][Reflection.Assembly]::LoadFrom((Join-Path $CandidateDirectory 'CoopSpectator.dll'))
function Assert-Auto($value,$label){if(-not $value){throw $label}}
function Value($kind,$number,$text){$v=New-Object CoopSpectator.Infrastructure.CoopCampaignPresentationValue;$v.Kind=$kind;$v.Number=$number;$v.Text=$text;return $v}
[void][Reflection.Assembly]::LoadFrom((Join-Path $game 'Modules\Native\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.View.dll'))
$viewAssembly=[Reflection.Assembly]::LoadFrom((Join-Path $game 'Modules\SandBox\bin\Win64_Shipping_Client\SandBox.View.dll'))
$mapType=$viewAssembly.GetType('SandBox.View.Map.MapScreen',$true)
$nativeOwner=$mapType.GetField('_battleSimulationView',[Reflection.BindingFlags]'Instance,NonPublic')
Assert-Auto ($null -ne $nativeOwner -and $nativeOwner.FieldType.FullName -eq 'SandBox.View.Map.MapView') 'Exact installed map-screen simulation owner exists; no placeholder-type lookup'
$root=[CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
$root.Properties.Add('IsSimulation',(Value Boolean 1 ''));$root.Properties.Add('ShowScoreboard',(Value Boolean 1 ''))
$root.Properties.Add('IsOver',(Value Boolean 0 ''));$root.Properties.Add('IsPaused',(Value Boolean 0 ''))
$root.Properties.Add('IsFastForwarding',(Value Boolean 0 ''));$root.Properties.Add('MissionTimeStr',(Value Text 0 '0m 3s'))
foreach($side in @('Attackers','Defenders')){
    $node=[CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
    $score=[CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
    $score.Properties.Add('NameText',(Value Text 0 $side));$score.Properties.Add('Remaining',(Value Integer 18 ''))
    $node.Properties.Add('Score',$score);$root.Properties.Add($side,$node)
}
# Metadata only: never create SPScoreboardVM/BattleSimulation or call Initialize/Tick.
$native=[TaleWorlds.MountAndBlade.ViewModelCollection.Scoreboard.ScoreboardBaseVM]
foreach($key in $root.Properties.Keys){Assert-Auto ($null -ne $native.GetProperty($key)) ('Installed native scoreboard binding: '+$key)}
$vm=New-Object CoopSpectator.UI.CoopCampaignAftermathVM;$vm.Apply($root)
$interface=[TaleWorlds.Library.IViewModel]
$getter=$interface.GetMethod('GetPropertyValue',[type[]]@([string]));$setter=$interface.GetMethod('SetPropertyValue')
$command=[TaleWorlds.Library.ViewModel].GetMethod('ExecuteCommand')
$attacker=$getter.Invoke($vm,@('Attackers'));$scoreVm=$getter.Invoke($attacker,@('Score'))
Assert-Auto ($getter.Invoke($scoreVm,@('Remaining')) -eq 18) 'Nested native side/score reads'
foreach($name in @('ExecutePauseSimulationAction','ExecuteFastForwardAction','ExecuteQuitAction','ExecuteEndSimulationAction')){
    [void]$command.Invoke($vm,@($name,[object[]]@()))
}
[void]$setter.Invoke($vm,@('IsPaused',$true));[void]$setter.Invoke($vm,@('IsOver',$true))
Assert-Auto ($getter.Invoke($vm,@('IsPaused')) -eq $false -and $getter.Invoke($vm,@('IsOver')) -eq $false) 'Client commands/setters cannot alter host simulation'
$root.Properties['Attackers'].Properties['Score'].Properties['Remaining'].Number=16
$root.Properties['IsOver'].Number=1;$root.Properties.Add('BattleResult',(Value Text 0 'Victory'))
$vm.Apply($root)
Assert-Auto ([object]::ReferenceEquals($attacker,$getter.Invoke($vm,@('Attackers'))) -and $getter.Invoke($scoreVm,@('Remaining')) -eq 16) 'Progress updates existing side bindings in place'
Assert-Auto ($getter.Invoke($vm,@('IsOver')) -eq $true -and $getter.Invoke($vm,@('BattleResult')) -eq 'Victory') 'Only host publication changes terminal state'
$vm.OnFinalize()
[void][Reflection.Assembly]::LoadFrom((Join-Path $bin 'TaleWorlds.Localization.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $bin 'TaleWorlds.Core.ViewModelCollection.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $bin 'TaleWorlds.CampaignSystem.ViewModelCollection.dll'))
# Native HintViewModel has only a prepared TextObject field. Its constructor does
# not create a game/simulator; no provider or hover command is evaluated here.
$nativeHint=[TaleWorlds.Core.ViewModelCollection.Information.HintViewModel]::new()
$nativeHint.HintText=[TaleWorlds.Localization.TextObject]::new('Remaining')
$captureType=[CoopSpectator.UI.CoopCampaignAftermathVM].Assembly.GetType('CoopSpectator.Campaign.CoopCampaignAftermathCapture')
$capture=$captureType.GetMethod('CapturePreparedWithPolicy',[Reflection.BindingFlags]'NonPublic,Static')
$hintPolicy=[System.Func[Type,bool]]{param($type) $type -eq [TaleWorlds.Core.ViewModelCollection.Information.HintViewModel]}
$capturedHint=$capture.Invoke($null,@($nativeHint,$hintPolicy))
Assert-Auto ($capturedHint.Properties['ReplicaHintText'].Text -eq 'Remaining') 'Exact prepared native hint field captured without hover/provider invocation'
$denyPolicy=[System.Func[Type,bool]]{param($type) $false}
$deniedHint=$capture.Invoke($null,@($nativeHint,$denyPolicy))
Assert-Auto ($deniedHint.Kind.ToString() -eq 'Null') 'Explicit policy still fences hint capture'
$script:shownHint=$null;$script:hintShows=0;$script:hintHides=0
$show=[System.Action[string]]{param($text) $script:shownHint=$text;$script:hintShows++}
$hide=[System.Action]{$script:shownHint=$null;$script:hintHides++}
$hints=[CoopSpectator.UI.CoopCampaignAutoBattleHints]::new($show,$hide)
$hintVm=[CoopSpectator.UI.CoopCampaignAftermathVM]::new();$hintVm.Apply($capturedHint)
$hintVm.LocalCommand={param($source,$name,$arguments) $hints.Command($source,$name,$arguments)}
[void]$command.Invoke($hintVm,@('ExecuteBeginHint',[object[]]@()))
Assert-Auto ($script:shownHint -eq 'Remaining' -and $script:hintShows -eq 1) 'Native command dispatch opens only detached local hint'
[void]$command.Invoke($hintVm,@('ExecuteBeginHint',[object[]]@()));$hints.Tick()
Assert-Auto ($script:hintShows -eq 1) 'Unchanged hover never restarts hint'
Assert-Auto (-not $hints.Command($hintVm,'ExecuteQuitAction',@())) 'Hint adapter has no simulation endpoint'
$capturedHint.Properties['ReplicaHintText'].Text='Survivors';$hintVm.Apply($capturedHint);$hints.Tick()
Assert-Auto ($script:shownHint -eq 'Survivors' -and $script:hintShows -eq 2) 'Host hint update refreshes held local hover'
$otherHint=[CoopSpectator.UI.CoopCampaignAftermathVM]::new();$otherHint.Apply($capturedHint)
[void]$hints.Command($otherHint,'ExecuteEndHint',@())
Assert-Auto ($script:shownHint -eq 'Survivors') 'Stale unrelated hover end cannot close current hint'
$hints.Suspend();$hints.Suspend()
Assert-Auto ($script:shownHint -eq $null -and $script:hintHides -eq 1) 'Modal suspension releases owned hint once'
[void]$hints.Command($hintVm,'ExecuteBeginHint',@());$hintVm.OnFinalize();$hints.Tick()
Assert-Auto ($script:shownHint -eq $null -and $script:hintHides -eq 2) 'Removed detached node revokes held hover'
$hints.Dispose();Assert-Auto (-not $hints.Command($otherHint,'ExecuteBeginHint',@())) 'Disposed window cannot revive a hint'
$otherHint.OnFinalize();$nativeHint.OnFinalize()
'PASS compiled detached auto-battle scoreboard progress/result bindings and command isolation; no simulator/game constructed'
