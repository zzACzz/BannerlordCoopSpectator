param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Test-MapVisualReadiness.ps1') -CandidateDirectory $CandidateDirectory
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    $flags=[Reflection.BindingFlags]'NonPublic,Instance'
    $type=$assembly.GetType('CoopSpectator.Infrastructure.CoopNavalTextureAdmission',$true)
    $ensure=$type.GetMethod('Ensure',$flags)
    foreach($dedicated in @($false,$true)) {
        foreach($campaign in @($false,$true)) {
            foreach($alias in @($false,$true)) {
                foreach($official in @($false,$true)) {
                    $gate=[Activator]::CreateInstance($type,$true)
                    $script:textureCalls=0
                    $loader=[Action[string]]{param($name)
                        if($name -cne 'CoopSpectatorNavalResources'){throw 'Unexpected texture module'}
                        $script:textureCalls++
                    }
                    $modules=[string[]]@('Native',$(if($alias){'CoopSpectatorNavalResources'}else{'Multiplayer'}),$(if($official){'NavalDLC'}else{'Sandbox'}))
                    $expected=-not $dedicated -and -not $campaign -and $alias -and -not $official
                    $arguments=[object[]]@($dedicated,$campaign,$modules,$loader)
                    if([bool]$ensure.Invoke($gate,$arguments) -ne $expected){throw 'Compiled texture admission role mismatch'}
                    if([bool]$ensure.Invoke($gate,$arguments) -or $script:textureCalls -ne [int]$expected){throw 'Duplicate/ineligible texture activation'}
                }
            }
        }
    }
    $root=$assembly.GetType('CoopSpectator.SubModule',$true).GetMethod('OnBeforeInitialModuleScreenSetAsRoot',$flags)
    $calls=@(Read-MethodCalls $root)
    if(@($calls | Where-Object {$_.Type -eq 'CoopSpectator.UI.CoopNavalVisualResources' -and $_.Name -eq 'EnsureTextureTilesetLoaded'}).Count -ne 1){throw 'Early texture admission is not wired exactly once'}
    $method=$assembly.GetType('CoopSpectator.UI.CoopNavalVisualResources',$true).GetMethod('EnsureTextureTilesetLoaded',[Reflection.BindingFlags]'NonPublic,Static')
    $calls=@(Read-MethodCalls $method)
    if(@($calls | Where-Object {$_.Type -eq 'CoopSpectator.UI.CoopNavalVisualResources' -and $_.Name -eq 'ActivateAndRebind'}).Count -ne 1){throw 'Expected activation/rebind delegate missing'}
    $initializer=$assembly.GetType('CoopSpectator.UI.CoopNavalVisualResources',$true).GetMethod('ActivateAndRebind',[Reflection.BindingFlags]'NonPublic,Static')
    $calls=@(Read-MethodCalls $initializer)
    $read=@($calls | Where-Object {$_.Type -eq 'CoopSpectator.Infrastructure.CoopNavalMaterialCatalog' -and $_.Name -eq 'ReadNames'})
    $load=@($calls | Where-Object {$_.Type -eq 'TaleWorlds.Engine.Utilities' -and $_.Name -eq 'LoadVirtualTextureTileset'})
    $get=@($calls | Where-Object {$_.Type -eq 'TaleWorlds.Engine.Material' -and $_.Name -eq 'GetTexture'})
    $set=@($calls | Where-Object {$_.Type -eq 'TaleWorlds.Engine.Material' -and $_.Name -eq 'SetTexture'})
    if($read.Count -ne 1 -or $load.Count -ne 1 -or $get.Count -ne 1 -or $set.Count -ne 1 -or $read[0].Offset -ge $load[0].Offset -or $load[0].Offset -ge $get[0].Offset -or $get[0].Offset -ge $set[0].Offset){throw 'Catalog validation / activation / existing-texture refresh order is incorrect'}
    if(@($calls | Where-Object {$_.Name -in @('CreateCopy','SetShader','SetShaderFlags','set_Name')}).Count){throw 'Rebinding must preserve resource identity, shader and palette'}
    $catalog=$assembly.GetType('CoopSpectator.Infrastructure.CoopNavalMaterialCatalog',$true).GetMethod('ReadNames',[Reflection.BindingFlags]'NonPublic,Static')
    $catalogPath=[string](Join-Path $game 'Modules/NavalDLC/TileSets/ValidMaterials.xml')
    $names=@($catalog.Invoke($null,[object[]]@($catalogPath)))
    foreach($required in @('nord_casual_boots','nord_casual_tunic','nord_sword_pieces','nord_spear_pieces')) { if($names -notcontains $required){throw ('Official catalog is missing affected material: '+$required)} }
    'PASS compiled Naval texture activation:16 role/profile combinations, once-only early admission, official material catalog and ordered stock rebind API wiring; no native texture/scene call executed.'
} finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }
