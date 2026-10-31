param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
# Baseline-only check for b28/b31 without explicit water creation. Canonical b32
# intentionally uses Test-CanonicalNavalWater instead; never report both as green.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Test-MapVisualReadiness.ps1') -CandidateDirectory $CandidateDirectory
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    $view=$assembly.GetType('CoopSpectator.UI.CoopCampaignMapPrototypeMissionView',$true)
    $types=@($view)+@($view.GetNestedTypes([Reflection.BindingFlags]'Public,NonPublic'))
    foreach($type in $types){
        foreach($method in $type.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly')){
            if($null -eq $method.GetMethodBody()){continue}
            $calls=@(Read-MethodCalls $method)
            if(@($calls|Where-Object {$_.Type -eq 'TaleWorlds.Engine.Scene' -and $_.Name -in @('EnsureWaterWakeRenderer','SetWaterWakeWorldSize','SetWaterWakeCameraOffset','Tick')}).Count){
                throw ('Rejected native water allocation/extra scene tick remains: '+$method.Name)
            }
        }
    }
    if($assembly.GetType('CoopSpectator.Infrastructure.CoopNavalMapWaterAdmission',$false)){throw 'Rejected b27 water admission is still shipped'}
    'PASS rollback compiled scene: no rejected early/late water allocation, associated policy or extra Scene.Tick; no native API executed. Black-water fidelity remains OPEN.'
} finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }
