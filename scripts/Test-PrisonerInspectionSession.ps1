param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
$gameBin='C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client'
[void][Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.Library.dll'))
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $CandidateDirectory 'CoopSpectator.dll'))
function Assert-Inspection($value,$message){if(-not $value){throw $message}}
function Text-Value($text){$v=New-Object CoopSpectator.Infrastructure.CoopCampaignPresentationValue;$v.Kind='Text';$v.Text=$text;return $v}
function Row-Value($id,$name){
    $row=[CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
    $row.Properties.Add('ReplicaRowId',(Text-Value $id));$row.Properties.Add('Name',(Text-Value $name))
    $count=New-Object CoopSpectator.Infrastructure.CoopCampaignPresentationValue;$count.Kind='Integer';$count.Number=2
    $row.Properties.Add('Number',$count)
    $selected=New-Object CoopSpectator.Infrastructure.CoopCampaignPresentationValue;$selected.Kind='Boolean'
    $row.Properties.Add('IsSelected',$selected);return $row
}
$window='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$a=Row-Value 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' 'First'
$b=Row-Value 'cccccccccccccccccccccccccccccccc' 'Second'
$root=[CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
$list=New-Object CoopSpectator.Infrastructure.CoopCampaignPresentationValue;$list.Kind='List'
$list.Items=New-Object 'System.Collections.Generic.List[CoopSpectator.Infrastructure.CoopCampaignPresentationValue]'
$list.Items.Add($a);$list.Items.Add($b);$root.Properties.Add('OtherPartyPrisoners',$list)
$root.Properties.Add('CurrentCharacter',$a)
$state=New-Object CoopSpectator.Infrastructure.CoopCampaignAftermathPresentation
$state.Kind='Prisoners';$state.WindowId=$window;$state.Root=$root
$vm=New-Object CoopSpectator.UI.CoopCampaignAftermathVM;$vm.Apply($root)
$script:interest=$null;$script:payload=$null
$set=[Action[string,bool]]{param($id,$extended)$script:interest=$id}
$clear=[Action]{$script:interest=$null;$script:payload=$null}
$current=[Func[CoopSpectator.Infrastructure.CoopCampaignMapTooltipPayload]]{return $script:payload}
$type=$assembly.GetType('CoopSpectator.UI.CoopCampaignPrisonerInspection',$true)
$ctor=$type.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic') | Where-Object {$_.GetParameters().Count -eq 5}
$arguments=[object[]]@($vm.PSObject.BaseObject,$window,$set,$clear,$current)
$session=$ctor.Invoke($arguments)
function Tick-Inspection{[void]$type.GetMethod('Tick').Invoke($session,[object[]]@($state.PSObject.BaseObject))}
function Reply-Inspection($id,$row,$tag){
    $card=New-Object CoopSpectator.Infrastructure.CoopCampaignAftermathPresentation;$card.Kind='Prisoners';$card.WindowId=$window
    $card.Root=[CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
    $model=[CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node();$model.Properties.Add('TestMarker',(Text-Value $tag))
    $card.Root.Properties.Add('SelectedCharacter',$model);$card.Root.Properties.Add('CurrentCharacter',$row)
    $response=New-Object CoopSpectator.Infrastructure.CoopCampaignMapTooltipPayload
    $response.Kind='AftermathTroop';$response.EntityId=$id;$response.AftermathCard=$card;return $response
}
Tick-Inspection
Assert-Inspection ($script:interest -like 'troop:*' -and $vm.GetPropertyValue('SelectedCharacter').GetPropertyValue('IsTableauEnabled') -eq $false) 'Initial independent request hides unverified host preview'
$script:payload=Reply-Inspection $script:interest $a 'PreviewA';Tick-Inspection
Assert-Inspection ($vm.GetPropertyValue('SelectedCharacter').GetPropertyValue('TestMarker') -eq 'PreviewA') 'Exact selected row preview admitted'
$old=$script:payload
$rows=$vm.GetPropertyValue('OtherPartyPrisoners');$rows[1].ExecuteSetSelected();Tick-Inspection
Assert-Inspection ($vm.GetPropertyValue('CurrentCharacter').GetPropertyValue('Name') -eq 'Second' -and $vm.GetPropertyValue('SelectedCharacter').GetPropertyValue('IsTableauEnabled') -eq $false) 'Local click changes row without displaying previous model'
$script:payload=$old;Tick-Inspection
Assert-Inspection ($vm.GetPropertyValue('SelectedCharacter').GetPropertyValue('IsTableauEnabled') -eq $false) 'Late first-row reply cannot impersonate second row'
$script:payload=Reply-Inspection $script:interest $b 'PreviewB';Tick-Inspection
Assert-Inspection ($vm.GetPropertyValue('SelectedCharacter').GetPropertyValue('TestMarker') -eq 'PreviewB' -and $a.Properties['IsSelected'].Number -eq 0 -and $b.Properties['IsSelected'].Number -eq 0) 'Independent model does not mutate host row selection'
$b.Properties['Number'].Number=9;$vm.Apply($root);Tick-Inspection
Assert-Inspection ($vm.GetPropertyValue('CurrentCharacter').GetPropertyValue('Name') -eq 'Second' -and $vm.GetPropertyValue('CurrentCharacter').GetPropertyValue('Number') -eq 9 -and $vm.GetPropertyValue('SelectedCharacter').GetPropertyValue('TestMarker') -eq 'PreviewB') 'Host selection echoes preserve local model while host counts update'
$list.Items.Remove($b) | Out-Null;$vm.Apply($root);Tick-Inspection
Assert-Inspection ($null -eq $script:interest -and $vm.GetPropertyValue('SelectedCharacter').GetPropertyValue('IsTableauEnabled') -eq $false) 'Host transfer/removal revokes selected row and request'
$c=Row-Value 'dddddddddddddddddddddddddddddddd' 'Replacement';$list.Items.Add($c);$vm.Apply($root);Tick-Inspection
Assert-Inspection ($null -eq $script:interest) 'New row at reused index cannot revive old selection'
$rows=$vm.GetPropertyValue('OtherPartyPrisoners');$rows[1].ExecuteSetSelected();Tick-Inspection
$script:payload=Reply-Inspection $script:interest $c 'PreviewC';Tick-Inspection
[void]$type.GetMethod('Suspend').Invoke($session,@())
Assert-Inspection ($null -eq $script:interest -and $vm.GetPropertyValue('SelectedCharacter').GetPropertyValue('IsTableauEnabled') -eq $false) 'Modal suspension revokes pending preview'
Tick-Inspection
Assert-Inspection ($null -ne $script:interest) 'Resume requests the same still-disclosed local row'
$state.WindowId='eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee';Tick-Inspection
Assert-Inspection ($null -eq $script:interest) 'Replacement aftermath window rejects old local selection'
[void]$type.GetMethod('Dispose').Invoke($session,@());$vm.OnFinalize()
'PASS detached independent prisoner inspection; no native game, PartyVM setter or campaign command started'
