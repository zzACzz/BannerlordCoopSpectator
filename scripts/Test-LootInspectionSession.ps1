param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference = 'Stop'
$gameBin = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client'
[void][Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.Library.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $CandidateDirectory 'CoopSpectator.dll'))
function Assert-Session($condition, $message) { if (-not $condition) { throw $message } }
function New-Text($text) {
    $value = New-Object CoopSpectator.Infrastructure.CoopCampaignPresentationValue
    $value.Kind = 'Text'; $value.Text = $text; return $value
}
function New-Row($id, $name) {
    $node = [CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
    $node.Properties.Add('ReplicaRowId', (New-Text $id)); $node.Properties.Add('Name', (New-Text $name))
    $node.Properties.Add('ReplicaLoadoutVersion', (New-Text 'equipment-v1')); return $node
}
function New-List($rows) {
    $value = New-Object CoopSpectator.Infrastructure.CoopCampaignPresentationValue
    $value.Kind = 'List'; $value.Items = New-Object 'System.Collections.Generic.List[CoopSpectator.Infrastructure.CoopCampaignPresentationValue]'
    foreach ($row in $rows) { $value.Items.Add($row) }; return $value
}
$window = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$first = New-Row 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' 'Sword'
$second = New-Row 'cccccccccccccccccccccccccccccccc' 'Bow'
$hero = New-Row 'dddddddddddddddddddddddddddddddd' 'Hero'
$root = [CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
$root.Properties.Add('LeftItemListVM', (New-List @($first, $second)))
$selector = [CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
$selector.Properties.Add('ItemList', (New-List @($hero)))
$root.Properties.Add('CharacterList', $selector)
$state = New-Object CoopSpectator.Infrastructure.CoopCampaignAftermathPresentation
$state.Kind = 'Loot'; $state.WindowId = $window; $state.Root = $root
$vm = New-Object CoopSpectator.UI.CoopCampaignAftermathVM
$vm.Apply($root)
$script:interest = $null; $script:payload = $null; $script:clearCount = 0
$set = [Action[string,bool]] { param($id,$extended) $script:interest = $id }
$clear = [Action] { $script:interest = $null; $script:payload = $null; $script:clearCount++ }
$current = [Func[CoopSpectator.Infrastructure.CoopCampaignMapTooltipPayload]] { return $script:payload }
$type = $assembly.GetType('CoopSpectator.UI.CoopCampaignLootInspection')
$ctor = $type.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic') | Where-Object { $_.GetParameters().Count -eq 5 }
$arguments = [object[]]@($vm, $window, $set, $clear, $current)
for ($i = 0; $i -lt $arguments.Length; $i++) { $arguments[$i] = $arguments[$i].PSObject.BaseObject }
$session = $ctor.Invoke($arguments)
$tick = $type.GetMethod('Tick', [Reflection.BindingFlags]'Instance,NonPublic')
function Tick-Session { [void]$tick.Invoke($session, [object[]]@($state.PSObject.BaseObject)) }
function Reply-Card($id, $name) {
    $card = New-Object CoopSpectator.Infrastructure.CoopCampaignAftermathPresentation
    $card.Kind = 'Loot'; $card.WindowId = $window
    $card.Root = [CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
    $card.Root.Properties.Add('ItemName', (New-Text $name))
    $response = New-Object CoopSpectator.Infrastructure.CoopCampaignMapTooltipPayload
    $response.Kind = 'AftermathItem'; $response.EntityId = $id; $response.AftermathCard = $card
    return $response
}
function Reply-Visual($id, $heroId, $tag) {
    $response = Reply-Card $id 'Visual'
    $response.Kind = 'AftermathLoadout'
    $response.AftermathCard.Root.Properties.Clear()
    $response.AftermathCard.Root.Properties.Add('HeroRowId', (New-Text $heroId))
    $mode = New-Object CoopSpectator.Infrastructure.CoopCampaignPresentationValue
    $mode.Kind = 'Integer'; $mode.Number = 1
    $response.AftermathCard.Root.Properties.Add('EquipmentMode', $mode)
    $model = [CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
    $model.Properties.Add('TestMarker', (New-Text $tag))
    $response.AftermathCard.Root.Properties.Add('MainCharacter', $model)
    $currentHero = $state.Root.Properties['CharacterList'].Properties['ItemList'].Items | Where-Object { $_.Properties['ReplicaRowId'].Text -eq $heroId }
    $response.AftermathCard.Root.Properties.Add('ReplicaLoadoutVersion', (New-Text $currentHero.Properties['ReplicaLoadoutVersion'].Text))
    $response.AftermathCard.Root.Properties.Add('CurrentCharacterTotalEncumbrance', (New-Text $tag))
    return $response
}
Tick-Session
Assert-Session ($script:interest -like 'loadout:*' -and -not $vm.GetPropertyValue('ReplicaHeroVisualVisible')) 'Preview request independent of hover; pending model hidden'
$script:payload = Reply-Visual $script:interest 'dddddddddddddddddddddddddddddddd' 'LocalHero'; Tick-Session
Assert-Session ($vm.GetPropertyValue('ReplicaHeroVisualVisible') -and $vm.GetPropertyValue('MainCharacter').GetPropertyValue('TestMarker') -eq 'LocalHero') 'Matching loadout controls local model'
Assert-Session ($vm.GetPropertyValue('MainCharacter').GetPropertyValue('ReplicaHeroVisualVisible') -eq $true) 'Tableau visibility exists in its own MainCharacter binding context'
$firstVisualResponse = $script:payload
$items = $vm.GetPropertyValue('LeftItemListVM')
$vm.ProcessItemTooltip($items[0]); Tick-Session
$firstInterest = $script:interest
Assert-Session ($firstInterest -like 'loot:*') 'Local hover produces an item/hero-bound interest'
Assert-Session (-not $vm.GetPropertyValue('ReplicaTooltipVisible')) 'Pending response remains hidden'
$script:payload = Reply-Card $firstInterest 'Sword'
Tick-Session
Assert-Session ($vm.GetPropertyValue('ReplicaTooltipVisible') -and $vm.GetPropertyValue('ItemMenu').GetPropertyValue('ItemName') -eq 'Sword') 'Matching response opens local full card'
Assert-Session ($vm.GetPropertyValue('ItemMenu').GetPropertyValue('ReplicaTooltipVisible') -eq $true) 'Tooltip visibility exists in prefab ItemMenu binding context'
$stableClearCount = $script:clearCount
$firstCardVm = $vm.GetPropertyValue('ItemMenu')
for ($frame = 0; $frame -lt 20; $frame++) { Tick-Session }
Assert-Session ($script:clearCount -eq $stableClearCount -and $vm.GetPropertyValue('ReplicaTooltipVisible')) 'Stable hover does not repeatedly revoke the card'
Assert-Session ([object]::ReferenceEquals($firstCardVm, $vm.GetPropertyValue('ItemMenu'))) 'Stable hover retains native property widgets'
$oldResponse = $script:payload
$vm.ProcessItemTooltip($items[1]); Tick-Session
Assert-Session ($script:interest -ne $firstInterest) 'Moving to another item creates another interest'
$script:payload = $oldResponse; Tick-Session
Assert-Session (-not $vm.GetPropertyValue('ReplicaTooltipVisible')) 'Late response for prior item cannot show'
$script:payload = Reply-Card $script:interest 'Bow'; Tick-Session
Assert-Session ($vm.GetPropertyValue('ItemMenu').GetPropertyValue('ItemName') -eq 'Bow') 'New item gets its own card'
Assert-Session (-not [object]::ReferenceEquals($firstCardVm, $vm.GetPropertyValue('ItemMenu'))) 'Different item rebuilds property widgets instead of inheriting previous row layout'
$root.Properties['LeftItemListVM'].Items.RemoveAt(1); $vm.Apply($root); Tick-Session
Assert-Session ($null -eq $script:interest -and -not $vm.GetPropertyValue('ReplicaTooltipVisible')) 'Host removal immediately revokes local inspection'
$selector.Properties['ItemList'].Items.Add((New-Row 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee' 'SecondHero'))
$vm.Apply($root)
Assert-Session ($vm.GetPropertyValue('MainCharacter').GetPropertyValue('TestMarker') -eq 'LocalHero') 'Host updates do not overwrite local model'
$vm.GetPropertyValue('CharacterList').ExecuteSelectNextItem(); Tick-Session
Assert-Session ($script:interest -like 'loadout:*' -and -not $vm.GetPropertyValue('ReplicaHeroVisualVisible')) 'Hero change hides old model pending reply'
Assert-Session ($vm.GetPropertyValue('MainCharacter').GetPropertyValue('ReplicaHeroVisualVisible') -eq $false) 'Previous model is hidden in child context during hero replacement'
Assert-Session ($vm.GetPropertyValue('ItemMenu').GetPropertyValue('ReplicaTooltipVisible') -eq $false) 'Hero replacement hides previous item card in child context'
$secondVisualInterest = $script:interest
$script:payload = $firstVisualResponse; Tick-Session
Assert-Session (-not $vm.GetPropertyValue('ReplicaHeroVisualVisible')) 'Late prior-hero reply cannot reappear'
$script:payload = Reply-Visual $secondVisualInterest 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee' 'SecondLocalHero'; Tick-Session
Assert-Session ($vm.GetPropertyValue('MainCharacter').GetPropertyValue('TestMarker') -eq 'SecondLocalHero') 'Independent reply updates model without host selection'
Assert-Session ($vm.GetPropertyValue('CurrentCharacterTotalEncumbrance') -eq 'SecondLocalHero') 'Equipment weight follows independent selected hero response'
$priorEquipmentReply = $script:payload
$selectorVm = $vm.GetPropertyValue('CharacterList')
$selectorVm.SetPropertyValue('SelectedIndex', $selectorVm.GetPropertyValue('SelectedIndex'))
$vm.ExecuteSelectBattleOutfit(); Tick-Session
Assert-Session ($vm.GetPropertyValue('ReplicaHeroVisualVisible')) 'Equal selector/mode echoes cannot invalidate loaded model'
$state = $state.Clone(); $vm.Apply($state.Root); Tick-Session
Assert-Session ($vm.GetPropertyValue('ReplicaHeroVisualVisible')) 'Repeated identical atomic host snapshot does not blink independent model'
$state = $state.Clone()
$state.Root.Properties.Add('CurrentFocusedItem', (New-Text 'DifferentHover'))
$focus = New-Object CoopSpectator.Infrastructure.CoopCampaignPresentationValue
$focus.Kind = 'Boolean'; $focus.Number = 1
$state.Root.Properties.Add('IsFocusedOnItemList', $focus)
$vm.Apply($state.Root); Tick-Session
Assert-Session ($vm.GetPropertyValue('ReplicaHeroVisualVisible')) 'Native hover/focus changes do not hide model or equipment'
$state = $state.Clone()
$state.Root.Properties['CharacterList'].Properties['ItemList'].Items[0].Properties['ReplicaLoadoutVersion'].Text = 'other-hero-equipment-v2'
$vm.Apply($state.Root); Tick-Session
Assert-Session ($vm.GetPropertyValue('ReplicaHeroVisualVisible') -and $vm.GetPropertyValue('MainCharacter').GetPropertyValue('TestMarker') -eq 'SecondLocalHero') 'Different hero equipment update preserves independent selected preview'
$state = $state.Clone()
$state.Root.Properties['CharacterList'].Properties['ItemList'].Items[1].Properties['ReplicaLoadoutVersion'].Text = 'equipment-v2'
$vm.Apply($state.Root); Tick-Session
Assert-Session ($script:interest -like 'loadout:*' -and -not $vm.GetPropertyValue('ReplicaHeroVisualVisible')) 'Actual changed host snapshot revokes stale equipment generation'
$script:payload = $priorEquipmentReply; Tick-Session
Assert-Session (-not $vm.GetPropertyValue('ReplicaHeroVisualVisible')) 'Late prior-equipment reply for same hero cannot revive stale visual'
$script:payload = Reply-Visual $script:interest 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee' 'UpdatedSharedEquipment'; Tick-Session
Assert-Session ($vm.GetPropertyValue('ReplicaHeroVisualVisible') -and $vm.GetPropertyValue('MainCharacter').GetPropertyValue('TestMarker') -eq 'UpdatedSharedEquipment') 'Fresh shared equipment updates independent local preview'
$slotVm = New-Object CoopSpectator.UI.CoopCampaignAftermathVM
$slotVm.SetLocal('ReplicaSlotIndex', [int]5)
$vm.ProcessItemTooltip($slotVm); Tick-Session
Assert-Session ($script:interest -like 'gear:*') 'Detached equipment slot requests selected hero equipment rather than host item row'
$script:payload = Reply-Card $script:interest 'SelectedHeroHelmet'; Tick-Session
Assert-Session ($vm.GetPropertyValue('ItemMenu').GetPropertyValue('ItemName') -eq 'SelectedHeroHelmet') 'Equipment response opens local card'
$vm.ResetSelectedItem(); Tick-Session
Assert-Session (-not $vm.GetPropertyValue('ReplicaTooltipVisible')) 'Equipment hover end clears local card'
$slotVm.OnFinalize()
$type.GetMethod('Suspend', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($session, @()) | Out-Null
Assert-Session (-not $vm.GetPropertyValue('ReplicaHeroVisualVisible') -and $null -eq $script:interest) 'Modal suspension revokes both card and hero preview'
$type.GetMethod('Dispose', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($session, @()) | Out-Null
$vm.OnFinalize()
'PASS detached loot inspection session; no GameNetwork, mission or game started'
