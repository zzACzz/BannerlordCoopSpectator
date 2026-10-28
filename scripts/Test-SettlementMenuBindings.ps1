param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
$gameRoot='C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
$gameBin=Join-Path $gameRoot 'bin\Win64_Shipping_Client'
foreach($name in @('TaleWorlds.Library','TaleWorlds.ObjectSystem','TaleWorlds.Core','TaleWorlds.Engine','TaleWorlds.MountAndBlade','TaleWorlds.CampaignSystem','TaleWorlds.CampaignSystem.ViewModelCollection')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $gameBin ($name+'.dll')))
}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $CandidateDirectory 'CoopSpectator.dll'))
$flags=[Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
function Assert-Settlement($value,$message){if(-not $value){throw $message}}
$data=New-Object CoopSpectator.Infrastructure.CoopCampaignSettlementMenuPresentation
$nativeType=[TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay.SettlementMenuOverlayVM]
# Metadata only: never instantiate the native overlay, trigger its getters,
# constructor, tooltip producer, campaign subscriptions or Refresh.
foreach($group in @(@('LabelNames',[string]),@('NumberNames',[int]),@('FlagNames',[bool]))) {
    foreach($name in $data.GetType().GetField($group[0]).GetValue($null)) {
        $property=$nativeType.GetProperty($name)
        Assert-Settlement ($null -ne $property -and $property.PropertyType -eq $group[1]) ('Audited scalar binding matches installed native metadata: '+$name)
    }
}
$data.Labels['SettlementNameLbl']='Avalyps';$data.Labels['MilitasLbl']='41';$data.Labels['ProsperityLbl']='346'
$data.Numbers['ProsperityChangeAmount']=1;$data.Flags['IsInitializationOver']=$true
$row=New-Object CoopSpectator.Infrastructure.CoopCampaignMapEncounterParty;$row.NameText='Losys';$data.Characters.Add($row)
$party=New-Object CoopSpectator.Infrastructure.CoopCampaignMapEncounterParty;$party.NameText='Guest';$party.PartySizeLabel='29';$data.Parties.Add($party)
$vm=New-Object CoopSpectator.UI.CoopCampaignSettlementMenuVM
$apply=$vm.GetType().GetMethod('Apply',$flags)
[void]$apply.Invoke($vm,[object[]]@($data.PSObject.BaseObject,$null))
Assert-Settlement ($vm.SettlementNameLbl -eq 'Avalyps' -and $vm.MilitasLbl -eq '41' -and $vm.ProsperityChangeAmount -eq 1) 'Detached native labels and numeric deltas'
Assert-Settlement ($vm.CharacterList.Count -eq 1 -and $vm.CharacterList[0].NameText -eq 'Losys' -and $vm.PartyList[0].PartySizeLbl -eq '29') 'Ordered character and party lists'
$first=$vm.CharacterList[0];$data.Characters[0].NameText='Changed';$data.Labels['MilitasLbl']='42'
[void]$apply.Invoke($vm,[object[]]@($data.PSObject.BaseObject,$null))
Assert-Settlement ([object]::ReferenceEquals($first,$vm.CharacterList[0]) -and $vm.CharacterList[0].NameText -eq 'Changed' -and $vm.MilitasLbl -eq '42') 'Publication refresh retains existing list item widgets'
# Every direct top-overlay scalar binding must resolve to a detached property.
[xml]$xml=Get-Content -LiteralPath (Join-Path $gameRoot 'Modules\SandBox\GUI\Prefabs\Map\SettlementOverlay.xml') -Raw
$knownChild=@('AdditionalArgs','Id','TextureProviderName','QuestMarkerType')
foreach($attribute in $xml.SelectNodes('//@*') | Where-Object {$_.Value.StartsWith('@')}) {
    $name=$attribute.Value.Substring(1)
    if($knownChild -contains $name){continue}
    Assert-Settlement ($null -ne $vm.GetType().GetProperty($name)) ('Stock top-overlay binding exists: '+$name)
}
foreach($name in @('GarrisonHint','ProsperityHint','CrimeHint')) {
    Assert-Settlement ($null -ne $vm.$name) ('Statistic wrapper retains parent binding context: '+$name)
}
$vm.ExecuteAddCompanion();$vm.ExecuteOnOverlayOpened();$vm.CharacterList[0].ExecuteSetAsContextMenuItem()
Assert-Settlement (-not $vm.IsContextMenuEnabled -and $vm.ContextList.Count -eq 0) 'Native campaign action/context targets are absent'
# Missing nested contexts leave stock widgets at their visible defaults. Require
# the actual installed popup's visibility binding, not just an absent action owner.
[xml]$popupXml=Get-Content -LiteralPath (Join-Path $gameRoot 'Modules\SandBox\GUI\Prefabs\Clan\ClanCardSelectionPopup.xml') -Raw
Assert-Settlement ($popupXml.Prefab.Window.Widget.IsVisible -eq '@IsVisible') 'Installed card popup root requires explicit visibility'
$popup=$vm.CardSelectionPopup
Assert-Settlement ($null -ne $popup -and -not $popup.IsVisible -and -not $popup.IsDoneEnabled -and $popup.Items.Count -eq 0) 'Popup binds a non-null hidden, disabled, empty context before any action'
Assert-Settlement (-not $popup.GetType().GetProperty('IsVisible').CanWrite) 'Remote actions cannot make the hidden popup visible'
foreach($name in @('DoneInputKey','CancelInputKey')) {
    Assert-Settlement ($null -ne $popup.$name -and -not $popup.$name.IsVisible) ('Nested input hint also has an explicitly hidden context: '+$name)
}
$popup.ExecuteDone();$popup.ExecuteCancel()
foreach($kind in @('Village','Town','Castle')) {
    $data.Flags['IsFortification']=$kind -ne 'Village'
    $data.Labels['SettlementNameLbl']=$kind
    [void]$apply.Invoke($vm,[object[]]@($data.PSObject.BaseObject,$null))
    Assert-Settlement ([object]::ReferenceEquals($popup,$vm.CardSelectionPopup) -and -not $popup.IsVisible -and -not $vm.IsContextMenuEnabled) ('Publication retains hidden popup for '+$kind)
}
$data.Characters.Clear();$data.Parties.Clear();[void]$apply.Invoke($vm,[object[]]@($data.PSObject.BaseObject,$null))
Assert-Settlement ($vm.CharacterList.Count -eq 0 -and $vm.PartyList.Count -eq 0) 'Empty native lists revoke previous portraits'
$vm.OnFinalize()
# Exercise the compiled local hover lifecycle without invoking a native provider.
$events=New-Object 'System.Collections.Generic.List[string]'
$callback=[Action[string,bool]]{param($token,$active) $events.Add($token+':'+$active)}
$hoverVm=New-Object CoopSpectator.UI.CoopCampaignSettlementMenuVM -ArgumentList $callback
$data.HintTokens.Clear();$data.Characters.Add($row);$data.Flags['IsInitializationOver']=$true
$statToken='settlementui:'+([guid]::NewGuid().ToString('N'));$rowToken='settlementui:'+([guid]::NewGuid().ToString('N'))
$data.HintTokens.Add('MilitasHint',$statToken);$data.HintTokens.Add('Character_0',$rowToken)
[void]$apply.Invoke($hoverVm,[object[]]@($data.PSObject.BaseObject,$null))
Assert-Settlement ($events.Count -eq 0) 'Receiving host values does not simulate host/player hover'
$hoverVm.MilitasHint.ExecuteBeginHint();$hoverVm.CharacterList[0].ExecuteOpenTooltip()
Assert-Settlement ($events.Contains($statToken+':True') -and $events.Contains($rowToken+':True')) 'Each local statistic/portrait emits only its published token'
$visual=$hoverVm.CharacterList[0].Visual;$eventCount=$events.Count
$data.Labels['MilitasLbl']='43';[void]$apply.Invoke($hoverVm,[object[]]@($data.PSObject.BaseObject,$null))
Assert-Settlement ($events.Count -eq $eventCount -and [object]::ReferenceEquals($visual,$hoverVm.CharacterList[0].Visual)) 'Ordinary publication preserves hovered card and portrait resource'
$nextToken='settlementui:'+([guid]::NewGuid().ToString('N'));$data.HintTokens['MilitasHint']=$nextToken
[void]$apply.Invoke($hoverVm,[object[]]@($data.PSObject.BaseObject,$null))
Assert-Settlement ($events.Contains($statToken+':False') -and $events.Contains($nextToken+':True')) 'Changed source revokes old interest before replacing hovered interest'
$hoverVm.OnFinalize()
Assert-Settlement ($events.Contains($rowToken+':False') -and $events.Contains($nextToken+':False')) 'Finalization revokes both local interests'
$encounterVm=New-Object CoopSpectator.UI.CoopCampaignMapEncounterPartyVM -ArgumentList $callback
$encounterRow=New-Object CoopSpectator.Infrastructure.CoopCampaignMapEncounterParty
$encounterRow.TooltipId='encounter:'+([guid]::NewGuid().ToString('N'))
$encounterVm.Apply($encounterRow);$encounterVm.ExecuteOpenTooltip();$encounterVm.ExecuteCloseTooltip();$encounterVm.OnFinalize()
Assert-Settlement ($events.Contains($encounterRow.TooltipId+':True') -and $events.Contains($encounterRow.TooltipId+':False')) 'Shared portrait VM retains encounter hover without a settlement override'
'PASS compiled settlement overlay bindings, hidden popup, independent hint lifecycle and installed native metadata; no game or native settlement VM constructed'
