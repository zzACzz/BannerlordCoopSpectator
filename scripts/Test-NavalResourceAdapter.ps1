param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Use Windows PowerShell 5.1 for the compiled net472 adapter check.' }
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $CandidateDirectory 'CoopSpectator.dll'))
$adapterType=$assembly.GetType('CoopSpectator.DedicatedHelper.CoopNavalResourceAdapter',$true)
$profileType=$assembly.GetType('CoopSpectator.Infrastructure.CoopCampaignContentProfile',$true)
$junction=$assembly.GetType('CoopSpectator.DedicatedHelper.WindowsDirectoryJunction',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$remove=$junction.GetMethod('RemoveExact',$flags)
$readTarget=$junction.GetMethod('ReadTarget',$flags)
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('WarhostNavalCompiled-'+[Guid]::NewGuid().ToString('N'))
$game=Join-Path $fixture 'Game with spaces'
$server=Join-Path $fixture 'Server elsewhere'
function Assert-True($condition,$message) { if (-not $condition) { throw $message } }
function Unlink-OwnedTree([string]$directory) {
    foreach ($child in [IO.Directory]::GetDirectories($directory)) {
        if (([IO.File]::GetAttributes($child) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            $target=[string]$readTarget.Invoke($null,[object[]]@([string]$child))
            if (-not [IO.Path]::GetFullPath($target).StartsWith([IO.Path]::GetFullPath($fixture)+'\',[StringComparison]::OrdinalIgnoreCase)) {
                throw 'Foreign scratch link: preserve the fixture for investigation.'
            }
            [void]$remove.Invoke($null,[object[]]@([string]$child,[string]$target))
        } else { Unlink-OwnedTree $child }
    }
}
try {
    [void][IO.Directory]::CreateDirectory((Join-Path $server 'Modules'))
    foreach ($name in @('Native','SandBoxCore','Sandbox','NavalDLC')) {
        $module=Join-Path $game ('Modules/'+$name)
        foreach ($relative in @('ModuleData','SceneObj/Main_map','AssetPackages')) {
            [void][IO.Directory]::CreateDirectory((Join-Path $module $relative))
        }
        [IO.File]::WriteAllText((Join-Path $module 'SubModule.xml'),('<Module><Id value="'+$name+'"/><Xmls><XmlNode><XmlName id="Items" path="items"/></XmlNode></Xmls></Module>'))
        [IO.File]::WriteAllText((Join-Path $module 'ModuleData/items.xml'),('<Items><Item id="'+$name+'_armor" Type="BodyArmor"/></Items>'))
        [IO.File]::WriteAllText((Join-Path $module 'SceneObj/Main_map/scene.xscene'),('scene-'+$name))
    }
    $story=Join-Path $game 'Modules/StoryMode'
    [void][IO.Directory]::CreateDirectory((Join-Path $story 'Prefabs'))
    [IO.File]::WriteAllText((Join-Path $story 'SubModule.xml'),'<Module><Id value="StoryMode"/></Module>')
    $mapSource=Join-Path $story 'Prefabs/storymode_prefabs.xml'
    [IO.File]::WriteAllText($mapSource,'<prefabs><game_entity name="tutorial_training_field"><transform rotation_euler="0, 0, -2.525"/><children><game_entity name="training"><components><meta_mesh_component name="mi_training_01"/></components></game_entity></children></game_entity><game_entity name="unrelated"/></prefabs>')
    $ownData=Join-Path $game 'Modules/CoopSpectator/ModuleData'
    [void][IO.Directory]::CreateDirectory($ownData)
    foreach ($name in @('coopspectator_items.xml','coopspectator_crafting_pieces.xml','coopspectator_campaign_mirror_items.xml')) {
        [IO.File]::WriteAllText((Join-Path $ownData $name),'<Items/>')
    }
    $naval=Join-Path $game 'Modules/NavalDLC'
    $bin=Join-Path $naval 'bin/Win64_Shipping_Client'
    [void][IO.Directory]::CreateDirectory($bin)
    foreach ($name in @('NavalDLC.dll','NavalDLC.View.dll')) { [IO.File]::WriteAllText((Join-Path $bin $name),'fixture bytes, never loaded') }
    $sourceXml=Join-Path $naval 'ModuleData/items.xml'
    $before=(Get-FileHash -LiteralPath $sourceXml).Hash
    $compute=$profileType.GetMethod('Compute')
    $expected=[string]$compute.Invoke($null,[object[]]@([string]$game,$true,$null))
    $actual=[string]$adapterType.GetMethod('Prepare').Invoke($null,[object[]]@([string]$game,[string]$server))
    Assert-True ($actual -eq $expected -and $actual -match '^naval-v1:[0-9a-f]{64}$') 'Compiled adapter profile mismatch.'
    $target=Join-Path $server 'Modules/CoopSpectatorNavalResources'
    [xml]$descriptor=[IO.File]::ReadAllText((Join-Path $target 'SubModule.xml'))
    Assert-True ($descriptor.SelectNodes('/Module/SubModules/SubModule').Count -eq 0) 'Campaign gameplay submodule leaked into resource adapter.'
    Assert-True (([IO.File]::GetAttributes((Join-Path $target 'SceneObj')) -band [IO.FileAttributes]::ReparsePoint) -ne 0) 'Scene resources were copied instead of shared.'
    Assert-True ([IO.File]::ReadAllText((Join-Path $target 'ModuleData/WarhostPrepared/catalog-0.xml')).Contains('multiplayer_item="true"')) 'Prepared MP item admission missing.'
    Assert-True ((Get-FileHash -LiteralPath $sourceXml).Hash -eq $before) 'Official source XML was modified.'
    $stamp=[IO.File]::GetLastWriteTimeUtc((Join-Path $target 'SubModule.xml'))
    [void]$adapterType.GetMethod('Prepare').Invoke($null,[object[]]@([string]$game,[string]$server))
    Assert-True ([IO.File]::GetLastWriteTimeUtc((Join-Path $target 'SubModule.xml')) -eq $stamp) 'Reuse unexpectedly rewrote adapter.'
    $clientProfile=[string]$adapterType.GetMethod('PrepareClient').Invoke($null,[object[]]@([string]$game,[string]$game))
    Assert-True ($clientProfile -eq $expected) 'Client catalog changed campaign/server content identity.'
    $clientTarget=Join-Path $game 'Modules/CoopSpectatorNavalCatalog'
    foreach($resource in @('AssetPackages','SceneObj','TileSets','bin')){
        Assert-True (-not [IO.Directory]::Exists((Join-Path $clientTarget $resource))) ('Client duplicates official resources: '+$resource)
    }
    [void]$adapterType.GetMethod('ValidateClientCatalog',$flags).Invoke($null,[object[]]@([string]$game,[string]$clientTarget))
    $map=$assembly.GetType('CoopSpectator.DedicatedHelper.CoopMapVisualResourceAdapter',$true).GetMethod('Prepare')
    $mapHash=[string]$map.Invoke($null,[object[]]@([string]$game,[string]$server))
    $mapTarget=Join-Path $server 'Modules/CoopSpectatorMapResources'
    $projectedFile=Join-Path $mapTarget 'Prefabs/training_field.xml'
    [xml]$projected=[IO.File]::ReadAllText($projectedFile)
    [xml]$official=[IO.File]::ReadAllText($mapSource)
    Assert-True ($projected.SelectNodes('/prefabs/game_entity').Count -eq 1 -and $projected.SelectSingleNode('/prefabs/game_entity').OuterXml -eq $official.SelectSingleNode('/prefabs/game_entity[@name="tutorial_training_field"]').OuterXml) 'Authored prefab structure changed.'
    Assert-True ([IO.Directory]::GetFiles($mapTarget,'*',[IO.SearchOption]::AllDirectories).Count -eq 3) 'Unexpected map adapter files.'
    $mapStamp=[IO.File]::GetLastWriteTimeUtc($projectedFile)
    Assert-True ([string]$map.Invoke($null,[object[]]@([string]$game,[string]$server)) -eq $mapHash -and [IO.File]::GetLastWriteTimeUtc($projectedFile) -eq $mapStamp) 'Map adapter reuse rewrote resources.'
    $baseBefore=[string]$compute.Invoke($null,[object[]]@([string]$game,$false,$null))
    [IO.File]::AppendAllText($mapSource,"`n")
    Assert-True ([string]$compute.Invoke($null,[object[]]@([string]$game,$false,$null)) -ne $baseBefore -and [string]$compute.Invoke($null,[object[]]@([string]$game,$true,$null)) -ne $expected) 'Map source omitted from content profile.'
    Assert-True ([string]$map.Invoke($null,[object[]]@([string]$game,[string]$server)) -ne $mapHash) 'Map source update was ignored.'
    Assert-True (@(Get-ChildItem -LiteralPath (Join-Path $server 'CoopSpectator.SharedModuleBackups') -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'previous-adapter/Prefabs/training_field.xml') }).Count -eq 1) 'Previous map adapter was not preserved.'
    [IO.File]::AppendAllText($projectedFile,"`n")
    $rejected=$false
    try { [void]$map.Invoke($null,[object[]]@([string]$game,[string]$server)) } catch { $rejected=$_.Exception.InnerException -is [IO.IOException] }
    Assert-True $rejected 'Modified map adapter was overwritten.'
    'PASS compiled net472 Naval/map resource adapters, exact prefab subtree, profile coverage, reuse and preservation; no game or DLC assemblies started.'
} finally {
    if ([IO.Directory]::Exists($fixture)) {
        Unlink-OwnedTree $fixture
        [IO.Directory]::Delete($fixture,$true)
    }
}
