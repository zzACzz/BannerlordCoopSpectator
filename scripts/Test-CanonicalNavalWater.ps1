param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Test-MapVisualReadiness.ps1') -CandidateDirectory $CandidateDirectory
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    $flags=[Reflection.BindingFlags]'NonPublic,Instance'
    $view=$assembly.GetType('CoopSpectator.UI.CoopCampaignMapPrototypeMissionView',$true)
    $prepare=@(Read-MethodCalls ($view.GetMethod('TryPrepareNavalWater',$flags)))
    if(@($prepare|Where-Object {$_.Type -eq 'CoopSpectator.Infrastructure.CoopNavalClientModuleAdmission' -and $_.Name -eq 'get_IsPrepared'}).Count -ne 1){throw 'Canonical startup admission is not required'}
    $shader=@(Read-MethodCalls ($view.GetMethod('HasCanonicalWaterShader',$flags)))
    $admit=@($shader|Where-Object Name -eq 'get_IsPrepared');$lookup=@($shader|Where-Object Name -eq 'GetFromResource');$mask=@($shader|Where-Object Name -eq 'GetMaterialShaderFlagMask')
    if($admit.Count -ne 1 -or $lookup.Count -ne 1 -or $mask.Count -ne 1 -or $admit[0].Offset -ge $lookup[0].Offset -or $lookup[0].Offset -ge $mask[0].Offset){throw 'Canonical/shader definition prerequisite order changed'}
    $initialize=@(Read-MethodCalls ($view.GetMethod('InitializeCanonicalWater',$flags)))
    $names=@($initialize|Where-Object Type -eq 'TaleWorlds.Engine.Scene'|ForEach-Object Name)
    if(($names -join ',') -ne 'CheckResources,EnsureWaterWakeRenderer,SetWaterWakeWorldSize,SetWaterWakeCameraOffset'){throw 'Stock water preparation sequence changed'}
    $release=@(Read-MethodCalls ($view.GetMethod('ReleaseSceneLayer',$flags)))
    $waterRelease=@($release|Where-Object Name -eq 'ReleaseCanonicalWater')
    $layerRelease=@($release|Where-Object Name -in @('RemoveLayer','ClearAll','Reset'))
    if($waterRelease.Count -ne 1 -or $layerRelease.Count -lt 1 -or @($layerRelease|Where-Object {$_.Offset -le $waterRelease[0].Offset}).Count){throw 'Water retirement must precede layer/scene clearing and gate reset on the common release path'}
    $retire=@(Read-MethodCalls ($view.GetMethod('ReleaseCanonicalWater',$flags)))
    if(@($retire|Where-Object {$_.Type -eq 'CoopSpectator.Infrastructure.CoopNavalMapWaterPreparation' -and $_.Name -eq 'Retire'}).Count -ne 1){throw 'Exact-scene once-only retirement gate missing'}
    $clear=@(Read-MethodCalls ($view.GetMethod('ClearCanonicalWaterView',$flags)))
    $delete=@(Read-MethodCalls ($view.GetMethod('DeleteCanonicalWater',$flags)))
    if(@($clear|Where-Object {$_.Type -eq 'TaleWorlds.Engine.Screens.SceneLayer' -and $_.Name -eq 'ClearRuntimeGPUMemory'}).Count -ne 1 -or @($delete|Where-Object {$_.Type -eq 'TaleWorlds.Engine.Scene' -and $_.Name -eq 'DeleteWaterWakeRenderer'}).Count -ne 1){throw 'Stock public water retirement pair missing'}
    $finalize=@(Read-MethodCalls ($view.GetMethod('OnMissionScreenFinalize',[Reflection.BindingFlags]'Public,Instance')))
    if(@($finalize|Where-Object Name -eq 'ReleaseSceneLayer').Count -ne 1){throw 'All mission scenarios must share owning map release'}
    $all=@($view)+@($view.GetNestedTypes([Reflection.BindingFlags]'Public,NonPublic'))
    foreach($type in $all){foreach($method in $type.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly')){
        if($null -eq $method.GetMethodBody()){continue}
        $calls=@(Read-MethodCalls $method)
        if(@($calls|Where-Object {$_.Name -in @('SetAllocationAlwaysValidScene','SetDoNotWaitForLoadingStatesToRender','DebugSetGlobalLoadingWindowState') -or ($_.Type -eq 'TaleWorlds.Engine.Scene' -and $_.Name -eq 'Tick')}).Count){throw 'Rejected extra tick/global setter found'}
        if($method.Name -ne 'InitializeCanonicalWater' -and @($calls|Where-Object {$_.Type -eq 'TaleWorlds.Engine.Scene' -and $_.Name -eq 'EnsureWaterWakeRenderer'}).Count){throw 'Another native water allocation path exists'}
    }}
    'PASS compiled canonical water lifecycle: constructor/shader admission, once-only stock preparation and exact-scene public retirement before common layer/scene release, no rejected timing/global flag/extra scene tick. Native fidelity remains runtime validation.'
} finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }
