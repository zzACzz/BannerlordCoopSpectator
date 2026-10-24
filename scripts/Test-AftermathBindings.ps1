param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference = 'Stop'
$candidate = Join-Path $CandidateDirectory 'CoopSpectator.dll'
$gameBin = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client'
$library = Join-Path $gameBin 'TaleWorlds.Library.dll'
if (-not (Test-Path -LiteralPath $candidate) -or -not (Test-Path -LiteralPath $library)) { throw 'Candidate or installed library missing.' }
# Detached VM test only. Reflection avoids compiling a second .NET reference profile.
# No engine/scene/game startup or full PartyVM/InventoryVM creation.
[void][Reflection.Assembly]::LoadFrom($library)
[void][Reflection.Assembly]::LoadFrom($candidate)
function Assert-Binding($condition, $label) { if (-not $condition) { throw $label } }
$root = [CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
$row = [CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
$name = New-Object CoopSpectator.Infrastructure.CoopCampaignPresentationValue
$name.Kind = 'Text'; $name.Text = 'Prisoner'; $row.Properties.Add('Name', $name)
$list = New-Object CoopSpectator.Infrastructure.CoopCampaignPresentationValue
$list.Kind = 'List'
$list.Items = New-Object 'System.Collections.Generic.List[CoopSpectator.Infrastructure.CoopCampaignPresentationValue]'
$list.Items.Add($row); $root.Properties.Add('OtherPartyPrisoners', $list)
$disabled = New-Object CoopSpectator.Infrastructure.CoopCampaignPresentationValue
$disabled.Kind = 'Boolean'; $disabled.Number = 1; $root.Properties.Add('IsDoneDisabled', $disabled)
$vm = New-Object CoopSpectator.UI.CoopCampaignAftermathVM
$vm.Apply($root)
$interface = [TaleWorlds.Library.IViewModel]
$getter = $interface.GetMethod('GetPropertyValue', [type[]]@([string]))
$setter = $interface.GetMethod('SetPropertyValue')
$pathGetter = $interface.GetMethod('GetViewModelAtPath', [type[]]@([TaleWorlds.Library.BindingPath]))
Assert-Binding ($getter.Invoke($vm, @('IsDoneDisabled')) -eq $true) 'Interface dispatch reads detached properties'
[void]$setter.Invoke($vm, @('IsDoneDisabled', $false))
Assert-Binding ($getter.Invoke($vm, @('IsDoneDisabled')) -eq $true) 'Client setter cannot change received state'
$path = New-Object TaleWorlds.Library.BindingPath -ArgumentList 'Root\OtherPartyPrisoners\0'
$pathArguments = New-Object object[] 1
$pathArguments[0] = $path.PSObject.BaseObject
$selected = $pathGetter.Invoke($vm, $pathArguments)
Assert-Binding ($null -ne $selected -and $getter.Invoke($selected, @('Name')) -eq 'Prisoner') 'Stock nested-list binding path'
$name.Text = 'Transferred'; $vm.Apply($root)
Assert-Binding ($getter.Invoke($selected, @('Name')) -eq 'Transferred') 'List row updates in place'
$command = $interface.GetMethod('ExecuteCommand')
[void]$command.Invoke($vm, @('ExecuteTransferSingle', [object[]]@()))
[void][TaleWorlds.Library.ViewModel].GetMethod('ExecuteCommand').Invoke($vm, @('ExecuteDone', [object[]]@()))
Assert-Binding ($getter.Invoke($vm, @('IsDoneDisabled')) -eq $true) 'Commands have no campaign targets'
[void]$root.Properties.Remove('OtherPartyPrisoners'); $vm.Apply($root)
Assert-Binding ($null -eq $pathGetter.Invoke($vm, $pathArguments)) 'Closed list revokes old paths'
$vm.OnFinalize(); $vm.OnFinalize()
# Local presentation overrides survive host publications without changing the
# authoritative tree. Unhandled setters and transfer commands remain inert.
$localVm = New-Object CoopSpectator.UI.CoopCampaignAftermathVM
$localVm.Apply($root)
$localVm.SetLocal('IsDoneDisabled', $false)
$localVm.Apply($root)
Assert-Binding ($getter.Invoke($localVm, @('IsDoneDisabled')) -eq $false -and $root.Properties['IsDoneDisabled'].Number -eq 1) 'Local display state isolated from host updates'
$localVm.ClearLocal('IsDoneDisabled')
Assert-Binding ($getter.Invoke($localVm, @('IsDoneDisabled')) -eq $true) 'Local override release restores received state'
$script:lastInspectionCommand = $null
$script:lastInspectionRow = $null
$localVm.LocalCommand = {
    param($source, $name, $parameters)
    $script:lastInspectionCommand = $name
    if ($parameters.Count -gt 0) { $script:lastInspectionRow = $parameters[0] }
    return $true
}
$nativeDispatch = [TaleWorlds.Library.ViewModel].GetMethod('ExecuteCommand')
$dispatchArgs = New-Object object[] 2
$dispatchArgs[0] = 'ProcessItemTooltip'; $dispatchArgs[1] = [object[]]@($selected)
[void]$nativeDispatch.Invoke($localVm, $dispatchArgs)
Assert-Binding ($script:lastInspectionCommand -eq 'ProcessItemTooltip' -and [object]::ReferenceEquals($script:lastInspectionRow, $selected)) 'Native base-class command dispatch reaches exact detached inspection row'
$dispatchArgs[0] = 'ExecuteTransferSingle'; $dispatchArgs[1] = [object[]]@()
$script:lastInspectionCommand = $null
[void]$nativeDispatch.Invoke($localVm, $dispatchArgs)
Assert-Binding ($null -eq $script:lastInspectionCommand) 'Native base dispatch has no transfer endpoint'
$localVm.OnFinalize()
# Model a widget that coerces an unsupported selection and synchronously writes
# it back from the PropertyChanged notification. Bound the OLD behavior at 10
# notifications so this regression test never deliberately overflows the stack.
$feedbackVm = New-Object CoopSpectator.UI.CoopCampaignAftermathVM
$feedbackNode = [CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
$selection = New-Object CoopSpectator.Infrastructure.CoopCampaignPresentationValue
$selection.Kind = 'Integer'; $selection.Number = 0
$feedbackNode.Properties.Add('AlternativeUsageIndex', $selection); $feedbackVm.Apply($feedbackNode)
$script:feedbackNotifications = 0
$feedbackVm.add_PropertyChanged({
    param($sender, $eventArgs)
    if ($eventArgs.PropertyName -eq 'AlternativeUsageIndex') {
        $script:feedbackNotifications++
        if ($script:feedbackNotifications -lt 10) { [void]$setter.Invoke($sender, @('AlternativeUsageIndex', -1)) }
    }
})
[void]$setter.Invoke($feedbackVm, @('AlternativeUsageIndex', -1))
Assert-Binding ($script:feedbackNotifications -le 1 -and $getter.Invoke($feedbackVm, @('AlternativeUsageIndex')) -eq 0) 'Coerced widget feedback is bounded and cannot mutate host value'
$firstFeedbackCount = $script:feedbackNotifications
[void]$setter.Invoke($feedbackVm, @('AlternativeUsageIndex', 0))
Assert-Binding ($script:feedbackNotifications -eq $firstFeedbackCount) 'Equal-value widget echo does not notify again'
[void]$setter.Invoke($feedbackVm, @('AlternativeUsageIndex', -1))
Assert-Binding ($script:feedbackNotifications -eq ($firstFeedbackCount + 1)) 'Feedback guard releases after each rejected write'
$selection.Number = 2; $feedbackVm.Apply($feedbackNode)
Assert-Binding ($getter.Invoke($feedbackVm, @('AlternativeUsageIndex')) -eq 2 -and $script:feedbackNotifications -le ($firstFeedbackCount + 3)) 'Subsequent host change still propagates through guarded feedback'
$feedbackVm.OnFinalize()
# Identified inventory/prisoner rows survive reordering without retargeting a
# client-held selection. Host removal must invalidate the old detached object.
function New-IdentifiedRow([string]$id, [string]$label) {
    $node = [CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
    foreach ($entry in @(@('ReplicaRowId', $id), @('Name', $label))) {
        $value = New-Object CoopSpectator.Infrastructure.CoopCampaignPresentationValue
        $value.Kind = 'Text'; $value.Text = $entry[1]; $node.Properties.Add($entry[0], $value)
    }
    return $node
}
$a = New-IdentifiedRow 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' 'Sword'
$b = New-IdentifiedRow 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' 'Bow'
$root = [CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node()
$list.Items.Clear(); $list.Items.Add($a); $list.Items.Add($b)
$root.Properties.Add('OtherPartyPrisoners', $list)
$vm = New-Object CoopSpectator.UI.CoopCampaignAftermathVM
$vm.Apply($root)
$rows = $getter.Invoke($vm, @('OtherPartyPrisoners'))
$selectedA = $rows[0]; $selectedB = $rows[1]
$list.Items.Clear(); $list.Items.Add($b); $list.Items.Add($a); $vm.Apply($root)
Assert-Binding ([object]::ReferenceEquals($rows[0], $selectedB) -and [object]::ReferenceEquals($rows[1], $selectedA)) 'Reordering preserves semantic row VM identity'
$a.Properties['Name'].Text = 'Sword updated'; $vm.Apply($root)
Assert-Binding ($getter.Invoke($selectedA, @('Name')) -eq 'Sword updated') 'Host row changes update held inspection target'
$list.Items.RemoveAt(1); $vm.Apply($root)
Assert-Binding ($null -eq $getter.Invoke($selectedA, @('Name')) -and $rows.Count -eq 1) 'Removed host row finalized rather than retargeted'
$list.Items.Add($a); $vm.Apply($root)
Assert-Binding (-not [object]::ReferenceEquals($rows[1], $selectedA)) 'Reintroduced row cannot revive finalized client selection'
$retained = $rows[0]
$list.Items.Clear(); $list.Items.Add($b); $list.Items.Add($b)
$rejected = $false
try { $vm.Apply($root) } catch { $rejected = $true }
Assert-Binding ($rejected -and [object]::ReferenceEquals($rows[0], $retained)) 'Duplicate row identities rejected before list mutation'
$list.Items.Clear(); $list.Items.Add($b); $list.Items.Add([CoopSpectator.Infrastructure.CoopCampaignPresentationValue]::Node())
$rejected = $false
try { $vm.Apply($root) } catch { $rejected = $true }
Assert-Binding $rejected 'Mixed identified/unidentified rows rejected'
$list.Items.Clear(); $vm.Apply($root)
Assert-Binding ($rows.Count -eq 0 -and $null -eq $getter.Invoke($retained, @('Name'))) 'Empty host list revokes all held rows'
$vm.OnFinalize()
[void][Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.Core.ViewModelCollection.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.CampaignSystem.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.CampaignSystem.ViewModelCollection.dll'))
# A detached native row with prepared values proves the unannotated base-property
# capture boundary. Do not call RefreshValue or construct a campaign screen.
$nativeRow = New-Object TaleWorlds.CampaignSystem.ViewModelCollection.Inventory.ItemMenuTooltipPropertyVM
$nativeRow.DefinitionLabel = 'Weight'; $nativeRow.ValueLabel = '0.68'; $nativeRow.TextHeight = 7
$nativeRow.TextColor = [TaleWorlds.Library.Color]::FromUint(4279383126)
$captureType = [CoopSpectator.UI.CoopCampaignAftermathVM].Assembly.GetType('CoopSpectator.Campaign.CoopCampaignAftermathCapture')
$captureMethod = $captureType.GetMethod('Capture', [Reflection.BindingFlags]'NonPublic,Static')
$captureArguments = New-Object object[] 5
$captureArguments[0] = $nativeRow.PSObject.BaseObject; $captureArguments[1] = [int]0
$captureArguments[2] = [int]65536
$captureArguments[3] = (New-Object 'System.Collections.Generic.HashSet[object]').PSObject.BaseObject
$captureArguments[4] = $true
$captured = $captureMethod.Invoke($null, $captureArguments)
Assert-Binding ($captured.Properties['DefinitionLabel'].Text -eq 'Weight' -and $captured.Properties['ValueLabel'].Text -eq '0.68') 'Unannotated native tooltip labels captured'
Assert-Binding ($captured.Properties['TextHeight'].Number -eq 7 -and $captured.Properties['TextColor'].Kind.ToString() -eq 'Color') 'Native tooltip layout/color captured'
$rowVm = New-Object CoopSpectator.UI.CoopCampaignAftermathVM
$rowVm.Apply($captured)
Assert-Binding ($getter.Invoke($rowVm, @('TextColor')) -is [TaleWorlds.Library.Color]) 'Typed color reconstructed for stock widget binding'
$rowVm.OnFinalize()
$emptySlot = New-Object TaleWorlds.CampaignSystem.ViewModelCollection.Inventory.SPItemVM
$captureArguments[0] = $emptySlot.PSObject.BaseObject; $captureArguments[1] = [int]0; $captureArguments[2] = [int]65536
$captureArguments[3] = (New-Object 'System.Collections.Generic.HashSet[object]').PSObject.BaseObject
$captureArguments[4] = $false
$capturedSlot = $captureMethod.Invoke($null, $captureArguments)
Assert-Binding ($capturedSlot.Kind.ToString() -eq 'Node' -and -not $capturedSlot.Properties.ContainsKey('ReplicaRowId')) 'Detached equipment slot does not allocate a live row identity without a campaign window'
$emptySlot.OnFinalize()
'PASS installed-library detached aftermath bindings; no game started'
