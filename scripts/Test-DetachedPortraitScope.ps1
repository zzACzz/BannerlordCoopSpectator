param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
$gameRoot='C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
$gameBin=Join-Path $gameRoot 'bin\Win64_Shipping_Client'
foreach($name in @('TaleWorlds.Library','TaleWorlds.ObjectSystem','TaleWorlds.Core','TaleWorlds.Engine','TaleWorlds.MountAndBlade')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $gameBin ($name+'.dll')))
}
[void][Reflection.Assembly]::LoadFrom((Join-Path $gameRoot 'Modules\Native\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.View.dll'))
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $CandidateDirectory 'CoopSpectator.dll'))
$flags=[Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
function Assert-Portrait($value,$message){if(-not $value){throw $message}}
function Uninitialized([type]$type){return ,([Runtime.Serialization.FormatterServices]::GetUninitializedObject($type))}
# All state below is managed and owned by this test process. No Game, manager
# singleton, scene, thumbnail cache or native crafting support is initialized.
$bindingType=$assembly.GetType('CoopSpectator.UI.CoopDetachedPortraitBinding',$true)
$equipment=New-Object TaleWorlds.Core.Equipment
$ctor=$bindingType.GetConstructors($flags)[0]
$binding=$ctor.Invoke([object[]]@('owned-code',$equipment.PSObject.BaseObject,3))
$token=$bindingType.GetField('Token',$flags).GetValue($binding)
$find=$bindingType.GetMethod('Find',$flags)
Assert-Portrait ([object]::ReferenceEquals($binding,$find.Invoke($null,@($token,'owned-code')))) 'Exact local binding admitted'
Assert-Portrait ($null -eq $find.Invoke($null,@($token,'other-code'))) 'Same token cannot supply another character'
Assert-Portrait ($null -eq $find.Invoke($null,@('untrusted','owned-code'))) 'Arbitrary provider argument rejected'
$requestType=$assembly.GetType('CoopSpectator.UI.CoopDetachedPortraitCreationData',$true)
$request=Uninitialized $requestType
$requestType.GetField('Binding',$flags).SetValue($request,$binding)
$code=Uninitialized ([TaleWorlds.Core.CharacterCode])
$otherCode=Uninitialized ([TaleWorlds.Core.CharacterCode])
$requestType.BaseType.GetProperty('CharacterCode').GetSetMethod($true).Invoke($request,@($code)) | Out-Null
$patch=$assembly.GetType('CoopSpectator.Patches.CoopDetachedPortraitEquipmentPatch',$true)
$frameType=$patch.GetNestedType('Frame',[Reflection.BindingFlags]'NonPublic')
$frame=Uninitialized $frameType
$frameType.GetField('Request',$flags).SetValue($frame,$request)
$scope=$patch.GetField('_frame',$flags)
$calculate=$patch.GetMethod('Equipment',$flags)
$end=$patch.GetMethod('End',$flags)
try {
    $scope.SetValue($null,$null)
    $args=[object[]]@($code,$null)
    Assert-Portrait ($calculate.Invoke($null,$args) -eq $true -and $null -eq $args[1]) 'Ordinary portrait passes through with no scope'
    $scope.SetValue($null,$frame)
    $args=[object[]]@($otherCode,$null)
    Assert-Portrait ($calculate.Invoke($null,$args) -eq $true -and $null -eq $args[1]) 'Other CharacterCode reference cannot inherit owned equipment'
    $args=[object[]]@($code,$null)
    Assert-Portrait ($calculate.Invoke($null,$args) -eq $false -and $null -ne $args[1]) 'Exact owned CharacterCode receives equipment'
    Assert-Portrait (-not [object]::ReferenceEquals($equipment.PSObject.BaseObject,$args[1])) 'Renderer receives a clone, not mutable binding equipment'
    # Exercise the actual prefix/finalizer for nested ordinary thumbnail calls.
    # Null request short-circuits before any network-role access.
    $beginArgs=[object[]]@($null,$null,$null)
    $patch.GetMethod('Begin',$flags).Invoke($null,$beginArgs) | Out-Null
    Assert-Portrait ($null -eq $scope.GetValue($null)) 'Nested stock request masks outer detached scope'
    $errorObject=New-Object InvalidOperationException 'owned-test-exception'
    $returned=$end.Invoke($null,[object[]]@($errorObject.PSObject.BaseObject,$beginArgs[2]))
    Assert-Portrait ([object]::ReferenceEquals($errorObject.PSObject.BaseObject,$returned) -and [object]::ReferenceEquals($frame,$scope.GetValue($null))) 'Exception finalizer restores parent scope and preserves error'
    $end.Invoke($null,[object[]]@($null,$null)) | Out-Null
    Assert-Portrait ($null -eq $scope.GetValue($null)) 'Outer finalizer releases scope'
} finally { $scope.SetValue($null,$null);$bindingType.GetMethod('Dispose').Invoke($binding,@()) | Out-Null }
Assert-Portrait ($null -eq $find.Invoke($null,@($token,'owned-code'))) 'Finalized VM revokes future provider lookup'
Assert-Portrait ([object]::ReferenceEquals($binding,$requestType.GetField('Binding',$flags).GetValue($request))) 'In-flight request retains owned resources after lookup revocation'

# Verify read-only counter accessor against the audited installed managed layout.
$managerType=[TaleWorlds.ObjectSystem.MBObjectManager]
$manager=Uninitialized $managerType
$recordsField=$managerType.GetField('ObjectTypeRecords',$flags)
$records=[Activator]::CreateInstance($recordsField.FieldType)
$recordType=$managerType.GetNestedType('ObjectTypeRecord`1',[Reflection.BindingFlags]'NonPublic').MakeGenericType([TaleWorlds.Core.ItemObject])
$record=Uninitialized $recordType
$counter=$recordType.GetField('_objCount',$flags);$counter.SetValue($record,[uint32]123)
$records.Add($record);$recordsField.SetValue($manager,$records)
$read=$assembly.GetType('CoopSpectator.Infrastructure.CoopItemRegistryCounter',$true).GetMethod('TryRead',$flags)
$readArgs=[object[]]@($manager,[uint32]0)
Assert-Portrait ($read.Invoke($null,$readArgs) -eq $true -and $readArgs[1] -eq 123) 'Audited item-type counter is readable'
Assert-Portrait ($counter.GetValue($record) -eq 123) 'Counter observation never writes registry state'
$records.Clear();$readArgs=[object[]]@($manager,[uint32]0)
Assert-Portrait ($read.Invoke($null,$readArgs) -eq $false) 'Missing item record fails closed'
'PASS compiled detached portrait request scope, binding lifetime and read-only registry metadata; native rendering NOT tested'
