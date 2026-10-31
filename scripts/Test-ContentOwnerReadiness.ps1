param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Use Windows PowerShell 5.1 for compiled net472 metadata checks.' }
$gameBin='C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord/bin/Win64_Shipping_Client'
$registry=[Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.ModuleManager.dll'))
$helper=$registry.GetType('TaleWorlds.ModuleManager.ModuleHelper',$true)
$getActive=$helper.GetMethod('GetActiveModules',[Reflection.BindingFlags]'Public,Static',$null,[Type[]]@(),$null)
if ($null -eq $getActive -or -not $getActive.ReturnType.IsGenericType) { throw 'Active-module registry signature changed.' }
$info=$getActive.ReturnType.GetGenericArguments()[0]
foreach ($name in @('Id','FolderPath','IsActive')) {
    $property=$info.GetProperty($name)
    $expected=if($name -eq 'IsActive'){[bool]}else{[string]}
    if ($null -eq $property -or $property.PropertyType -ne $expected -or -not $property.GetGetMethod().IsPublic) {
        throw ('Native module metadata signature changed: '+$name)
    }
}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $CandidateDirectory 'CoopSpectator.dll'))
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$reader=$assembly.GetType('CoopSpectator.Infrastructure.ModulePathHelper',$true).GetMethod('ReadActiveModulePaths',$flags)
$resolver=$assembly.GetType('CoopSpectator.Infrastructure.CoopCampaignMapResourceOwner',$true).GetMethod('Resolve',$flags)
$arguments=New-Object object[] 1
$arguments[0]=[object[]]@()
$empty=$reader.Invoke($null,$arguments)
$arguments[0]=$empty
if ($null -ne $resolver.Invoke($null,$arguments)) { throw 'Empty native registry was assigned a guessed map owner.' }
if ($null -eq $assembly.GetType('CoopSpectator.Infrastructure.CoopCampaignContentIdentityCache',$true).GetMethod('GetOrCreate',[Reflection.BindingFlags]'NonPublic,Instance')) {
    throw 'Compiled retry-capable content cache is missing.'
}
Write-Output 'PASS compiled content-owner integration: installed public registry/property signatures, empty admission, retry-cache binding; registry not initialized or invoked, no native game constructed.'
