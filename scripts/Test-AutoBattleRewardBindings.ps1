param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Test-AutoBattleBindings.ps1') -CandidateDirectory $CandidateDirectory
function Check-Reward($condition,$label){if(-not $condition){throw $label}}
$game='C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
$sandbox=[Reflection.Assembly]::LoadFrom((Join-Path $game 'Modules\SandBox\bin\Win64_Shipping_Client\SandBox.ViewModelCollection.dll'))
$scoreboard=$sandbox.GetType('SandBox.ViewModelCollection.SPScoreboardVM',$true)
$reader=[CoopSpectator.UI.CoopCampaignAftermathVM].Assembly.GetType('CoopSpectator.Campaign.CoopCampaignAutoBattleRewardCapture',$true)
$read=$reader.GetMethod('TryReadExplanation',[Reflection.BindingFlags]'NonPublic,Static')
$provider=[TaleWorlds.Core.ViewModelCollection.Information.BasicTooltipViewModel].GetField('_tooltipProperties',[Reflection.BindingFlags]'NonPublic,Instance')
$closure=@($scoreboard.GetNestedTypes([Reflection.BindingFlags]'Public,NonPublic') | Where-Object {
    $null -ne $_.GetField('renownExplained',[Reflection.BindingFlags]'Instance,Public,NonPublic')
})
Check-Reward ($closure.Count -eq 1) 'Exact installed prepared reward closure exists'
# Compiler-generated closure only: no SPScoreboardVM, simulation or Game exists.
$holder=[Activator]::CreateInstance($closure[0],$true)
$nativeHint=[TaleWorlds.Core.ViewModelCollection.Information.BasicTooltipViewModel]::new()
foreach($entry in @(@('renownExplained','<GetBattleRewards>b__0'),@('influenceExplained','<GetBattleRewards>b__1'),@('moraleExplained','<GetBattleRewards>b__2'))) {
    $number=[TaleWorlds.CampaignSystem.ExplainedNumber]::new([single]6.4,$false,$null)
    $closure[0].GetField($entry[0],[Reflection.BindingFlags]'Instance,Public,NonPublic').SetValue($holder,$number)
    $method=$closure[0].GetMethod($entry[1],[Reflection.BindingFlags]'Instance,Public,NonPublic')
    $callback=[Delegate]::CreateDelegate($provider.FieldType,$holder,$method)
    $nativeHint.SetToolipCallback($callback)
    $arguments=New-Object object[] 3
    $arguments[0]=$nativeHint;$arguments[1]=$scoreboard;$arguments[2]=[TaleWorlds.CampaignSystem.ExplainedNumber]::new()
    Check-Reward ($read.Invoke($null,$arguments)) ('Audited exact native '+$entry[0]+' accepted without callback invocation')
    Check-Reward ([Math]::Abs($arguments[2].ResultNumber-6.4) -lt 0.001) 'Prepared numeric copy retained'
    # There is deliberately no explainer; invoking the native callback would not
    # succeed. Reader acceptance must not require formatting or global text state.
    Check-Reward ($null -eq $arguments[2].GetType().GetField('_explainer',[Reflection.BindingFlags]'NonPublic,Instance').GetValue($arguments[2])) 'No explanation formatting or construction occurred'
}
$unsupported=$closure[0].GetMethod('<GetBattleRewards>b__3',[Reflection.BindingFlags]'Instance,Public,NonPublic')
$nativeHint.SetToolipCallback([Delegate]::CreateDelegate($provider.FieldType,$holder,$unsupported))
Check-Reward (-not $read.Invoke($null,$arguments)) 'Loot delegate with text side effects is not invoked or admitted'
$nativeHint.SetToolipCallback([Delegate]::Combine($callback,$callback))
Check-Reward (-not $read.Invoke($null,$arguments)) 'Multicast providers cannot disclose a different capture'
$nativeHint.SetToolipCallback($callback)
$arguments[1]=[TaleWorlds.Core.ViewModelCollection.Information.BasicTooltipViewModel]
Check-Reward (-not $read.Invoke($null,$arguments)) 'Foreign scoreboard type rejected'
$nativeHint.OnFinalize()
$nativeRow=[TaleWorlds.Core.ViewModelCollection.Information.TooltipProperty]::new('Base','+6.4',0,$false,[TaleWorlds.Core.ViewModelCollection.Information.TooltipProperty+TooltipPropertyFlags]0)
$captureType=[CoopSpectator.UI.CoopCampaignAftermathVM].Assembly.GetType('CoopSpectator.Campaign.CoopCampaignAftermathCapture')
$capture=$captureType.GetMethod('CapturePrepared',[Reflection.BindingFlags]'NonPublic,Static')
$capturedRow=$capture.Invoke($null,@($nativeRow,$false))
Check-Reward ($capturedRow.Properties['DefinitionLabel'].Text -eq 'Base' -and $capturedRow.Properties['ValueLabel'].Text -eq '+6.4' -and
    $capturedRow.Properties['TextColor'].Kind.ToString() -eq 'Color') 'Actual native prepared reward row captures with typed layout/color'

function Reward-Value($kind,$number,$text){$v=[CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::new();$v.Kind=$kind;$v.Number=$number;$v.Text=$text;return $v}
$root=[CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
$list=Reward-Value List 0 ''; $list.Items=[System.Collections.Generic.List[CoopSpectator.Infrastructure.CoopCampaignPresentationValue]]::new()
$row=[CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
$row.Properties.Add('DefinitionLabel',(Reward-Value Text 0 'Base'))
$row.Properties.Add('ValueLabel',(Reward-Value Text 0 '+4.4'))
$row.Properties.Add('TextHeight',(Reward-Value Integer 0 ''))
$row.Properties.Add('PropertyModifier',(Reward-Value Integer 0 ''))
$row.Properties.Add('TextColor',(Reward-Value Color 4294967295 ''))
$list.Items.Add($row);$root.Properties.Add('ReplicaTooltipRows',$list)
$vm=[CoopSpectator.UI.CoopCampaignAftermathVM]::new();$vm.Apply($root)
$script:rewardShows=0;$script:rewardHides=0;$script:rewardValue=$null
$showPlain=[System.Action[string]]{param($text) throw 'Structured reward must not become a plain approximation'}
$hide=[System.Action]{$script:rewardHides++}
$rowActionType=[CoopSpectator.UI.CoopCampaignAutoBattleHints].GetConstructors() | Where-Object {$_.GetParameters().Count -eq 3} | ForEach-Object {$_.GetParameters()[2].ParameterType}
$showRows={param($rows) $script:rewardShows++;$script:rewardValue=$rows[0].ValueLabel} -as $rowActionType
$hints=[CoopSpectator.UI.CoopCampaignAutoBattleHints]::new($showPlain,$hide,$showRows)
$vm.LocalCommand={param($source,$name,$parameters) $hints.Command($source,$name,$parameters)}
$command=[TaleWorlds.Library.ViewModel].GetMethod('ExecuteCommand')
[void]$command.Invoke($vm,@('ExecuteBeginHint',[object[]]@()))
Check-Reward ($script:rewardShows -eq 1 -and $script:rewardValue -eq '+4.4') 'Native base dispatch renders detached structured reward rows'
$hints.Tick();$hints.Tick()
Check-Reward ($script:rewardShows -eq 1) 'Unchanged reward rows do not flicker'
$row.Properties['ValueLabel'].Text='+6.4';$vm.Apply($root);$hints.Tick()
Check-Reward ($script:rewardShows -eq 2 -and $script:rewardValue -eq '+6.4') 'Host replacement updates exact held reward rows'
$row.Properties['TextHeight'].Number=4097;$vm.Apply($root);$hints.Tick()
Check-Reward ($script:rewardHides -eq 1) 'Malformed row revokes hint before native rendering'
$row.Properties['TextHeight'].Number=0;$vm.Apply($root)
[void]$command.Invoke($vm,@('ExecuteBeginHint',[object[]]@()));$vm.OnFinalize();$hints.Tick()
Check-Reward ($script:rewardHides -eq 2) 'Removed reward node revokes local hint'
$hints.Dispose()
'PASS installed closure IL/field admission and detached reward row lifecycle; no callback/simulation/game execution'
