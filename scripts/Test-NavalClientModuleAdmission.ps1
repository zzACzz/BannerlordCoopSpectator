param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Test-MapVisualReadiness.ps1') -CandidateDirectory $CandidateDirectory
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    $flags=[Reflection.BindingFlags]'NonPublic,Public,Static,Instance'
    $type=$assembly.GetType('CoopSpectator.Infrastructure.CoopNavalClientModuleAdmission',$true)
    $prepare=$type.GetMethod('PrepareBeforeSubmoduleConstruction',$flags)
    $calls=@(Read-MethodCalls $prepare)
    $clear=@($calls|Where-Object {$_.Type -eq 'System.Collections.IList' -and $_.Name -eq 'Clear'})
    $validate=@($calls|Where-Object {$_.Name -eq 'ValidateClientCatalog'})
    $known=@($calls|Where-Object {$_.Name -eq 'RequireKnownSubmodules'})
    $prepared=@($calls|Where-Object {$_.Name -eq 'set_IsPrepared'})
    if($clear.Count -ne 1 -or $validate.Count -ne 1 -or $known.Count -ne 2 -or $prepared.Count -ne 1 -or
        $validate[0].Offset -ge $clear[0].Offset -or $known[1].Offset -ge $clear[0].Offset -or $prepared[0].Offset -le $clear[0].Offset){throw 'Admission no longer validates completely before the single managed-list mutation'}
    if(@($calls|Where-Object {$_.Type -like 'TaleWorlds.Engine.*' -or $_.Name -in @('WriteIntPtr','WriteByte','SetCanLoadModules','Patch','CreateInstance')}).Count){throw 'Admission must not set native flags, patch code, invoke engine resources or construct gameplay modules'}
    $ctor=$assembly.GetType('CoopSpectator.SubModule',$true).GetConstructor([Type[]]@())
    $ctorCalls=@(Read-MethodCalls $ctor)
    if(@($ctorCalls|Where-Object {$_.Name -eq 'PrepareBeforeSubmoduleConstruction'}).Count -ne 1){throw 'Missing early constructor-time admission'}
    $module=[TaleWorlds.MountAndBlade.Module]
    $load=$module.GetMethod('LoadSubModules',[Reflection.BindingFlags]'NonPublic,Instance')
    $loader=@(Read-MethodCalls $load)
    $add=@($loader|Where-Object Name -eq 'AddSubModule')
    $init=@($loader|Where-Object Name -eq 'InitializeSubModuleBases')
    if($add.Count -ne 1 -or $init.Count -ne 1 -or $add[0].Offset -ge $init[0].Offset){throw 'Stock construction/OnSubModuleLoad order changed'}
    $manager=[Reflection.Assembly]::LoadFrom((Join-Path $game 'bin/Win64_Shipping_Client/TaleWorlds.ModuleManager.dll'))
    $list=$manager.GetType('TaleWorlds.ModuleManager.ModuleInfo',$true).GetField('SubModules')
    if($null -eq $list -or -not [Collections.IList].IsAssignableFrom($list.FieldType)){throw 'Stock public submodule list contract changed'}
    $policy=$assembly.GetType('CoopSpectator.Infrastructure.CoopNavalClientModulePolicy',$true)
    [xml]$official=[IO.File]::ReadAllText((Join-Path $game 'Modules/NavalDLC/SubModule.xml'))
    $identities=[string[]]@($official.SelectNodes('/Module/SubModules/SubModule')|ForEach-Object {$_.DLLName.value+'|'+$_.SubModuleClassType.value})
    [void]$policy.GetMethod('RequireKnownSubmodules',$flags).Invoke($null,[object[]]@(,$identities))
    'PASS compiled canonical Naval admission: constructor before stock gameplay construction, exact official definitions, complete validation before one public managed-list clear, no native mutation/resource call.'
} finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }
