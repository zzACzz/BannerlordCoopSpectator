param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run this compiled net472 check with Windows PowerShell 5.1.' }
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $CandidateDirectory 'CoopSpectator.dll'))
$flags = [Reflection.BindingFlags]'NonPublic,Static'
$prepare = $assembly.GetType('CoopSpectator.DedicatedHelper.SharedServerModules',$true).GetMethod('Prepare',$flags)
$junction = $assembly.GetType('CoopSpectator.DedicatedHelper.WindowsDirectoryJunction',$true)
$remove = $junction.GetMethod('RemoveExact',$flags)
$create = $junction.GetMethod('Create',$flags)
$resolve = $junction.GetMethod('ResolveDirectory',$flags)
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('CoopSharedModulesCompiled-' + [Guid]::NewGuid().ToString('N'))
$game = Join-Path $fixture ("Game " + [char]0x0406 + [char]0x0433 + [char]0x0440 + [char]0x0438)
$server = Join-Path $fixture 'Dedicated elsewhere'
$lease = $null
try {
    foreach ($name in @('SandBox','SandBoxCore')) {
        $source = Join-Path $game ('Modules/' + $name)
        [IO.Directory]::CreateDirectory((Join-Path $source 'ModuleData')) | Out-Null
        [IO.Directory]::CreateDirectory((Join-Path $source 'SceneObj')) | Out-Null
        [IO.File]::WriteAllText((Join-Path $source 'SubModule.xml'),('<Module><Id value="' + $name + '" /></Module>'))
        [IO.File]::WriteAllText((Join-Path $source 'SceneObj/scene.txt'),('source-' + $name))
        $old = Join-Path $server ('Modules/' + $name)
        [IO.Directory]::CreateDirectory($old) | Out-Null
        [IO.File]::WriteAllText((Join-Path $old 'old.txt'),('old-' + $name))
    }
    $sourceHashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath $game -Recurse -File) { $sourceHashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash }
    $lease = $prepare.Invoke($null,[object[]]@([string]$game,[string]$server,[Func[bool]]{ $false },$null))
    foreach ($name in @('SandBox','SandBoxCore')) {
        $link = Join-Path $server ('Modules/' + $name)
        $source = Join-Path $game ('Modules/' + $name)
        if (-not ((Get-Item -LiteralPath $link).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Compiled helper copied a module instead of linking it.' }
        if ($resolve.Invoke($null,[object[]]@([string]$link)) -ne $resolve.Invoke($null,[object[]]@([string]$source))) { throw 'Compiled helper linked an incorrect source.' }
        if ([IO.File]::ReadAllText((Join-Path $link 'SceneObj/scene.txt')) -ne ('source-' + $name)) { throw 'Scene data is unreadable through compiled link.' }
    }
    $lease.Dispose(); $lease = $null
    $transactions = @(Get-ChildItem -LiteralPath (Join-Path $server 'CoopSpectator.SharedModuleBackups') -Directory)
    if ($transactions.Count -ne 1) { throw 'Unexpected migration count.' }
    foreach ($name in @('SandBox','SandBoxCore')) {
        if ([IO.File]::ReadAllText((Join-Path $transactions[0].FullName ($name + '/old.txt'))) -ne ('old-' + $name)) { throw 'Old module content was not preserved.' }
    }
    $lease = $prepare.Invoke($null,[object[]]@([string]$game,[string]$server,[Func[bool]]{ $false },$null))
    $lease.Dispose(); $lease = $null
    if (@(Get-ChildItem -LiteralPath (Join-Path $server 'CoopSpectator.SharedModuleBackups') -Directory).Count -ne 1) { throw 'Repeated compiled preparation was not idempotent.' }
    foreach ($path in $sourceHashes.Keys) { if ((Get-FileHash -LiteralPath $path).Hash -ne $sourceHashes[$path]) { throw 'Source game file changed.' } }
    $naval=Join-Path $game 'Modules/NavalDLC'
    [void][IO.Directory]::CreateDirectory((Join-Path $naval 'ModuleData'))
    [void][IO.Directory]::CreateDirectory((Join-Path $naval 'SceneObj'))
    [IO.File]::WriteAllText((Join-Path $naval 'SubModule.xml'),'<Module><Id value="NavalDLC"/></Module>')
    $navalFile=Join-Path $naval 'SceneObj/source.txt'
    [IO.File]::WriteAllText($navalFile,'original Naval source')
    $navalHash=(Get-FileHash -LiteralPath $navalFile).Hash
    $officialLink=Join-Path $server 'Modules/NavalDLC'
    [void]$create.Invoke($null,[object[]]@([string]$officialLink,[string]$naval))
    $journal=Join-Path $server 'CoopSpectator.SharedModuleBackups/prior-naval-link'
    [void][IO.Directory]::CreateDirectory($journal)
    [IO.File]::WriteAllLines((Join-Path $journal 'RESTORE.txt'),[string[]]@(
        'CoopSpectator shared modules v1','State=committed',
        ('NavalDLC | GameSource='+$naval+' | ServerLink='+$officialLink+' | PreviousFolder=none | Reused=False')))
    $lease=$prepare.Invoke($null,[object[]]@([string]$game,[string]$server,[Func[bool]]{$false},$null))
    $lease.Dispose(); $lease=$null
    if(Test-Path -LiteralPath $officialLink){throw 'Official Naval module remains visible to the native server scanner.'}
    $saved=@(Get-ChildItem -LiteralPath (Join-Path $server 'CoopSpectator.SharedModuleBackups') -Directory |
        ForEach-Object {Join-Path $_.FullName 'LegacyNavalDLCLink'} | Where-Object {Test-Path -LiteralPath $_})
    if($saved.Count -ne 1 -or (Get-Item -LiteralPath $saved[0]).LinkType -ne 'Junction' -or
        $resolve.Invoke($null,[object[]]@([string]$saved[0])) -ne $resolve.Invoke($null,[object[]]@([string]$naval))){throw 'Original Naval junction was not preserved exactly.'}
    if((Get-FileHash -LiteralPath $navalFile).Hash -ne $navalHash){throw 'Naval source changed during link relocation.'}
    $lease=$prepare.Invoke($null,[object[]]@([string]$game,[string]$server,[Func[bool]]{$false},$null))
    $lease.Dispose(); $lease=$null
    if(Test-Path -LiteralPath $officialLink){throw 'Repeated preparation recreated the official DLC module.'}
    'PASS compiled net472 shared-module migration, exact links, source preservation, journal-owned official Naval link relocation/reuse; no native game constructed.'
} finally {
    if ($null -ne $lease) { $lease.Dispose() }
    if (Test-Path -LiteralPath $fixture) {
        $absolute = [IO.Path]::GetFullPath($fixture)
        $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\CoopSharedModulesCompiled-'
        if (-not $absolute.StartsWith($tempPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe scratch cleanup target.' }
        foreach ($name in @('SandBox','SandBoxCore')) {
            $link = Join-Path $server ('Modules/' + $name)
            if ((Test-Path -LiteralPath $link) -and ((Get-Item -LiteralPath $link).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                $remove.Invoke($null,[object[]]@([string]$link,[string](Join-Path $game ('Modules/' + $name))))
            }
        }
        foreach($transaction in @(Get-ChildItem -LiteralPath (Join-Path $server 'CoopSpectator.SharedModuleBackups') -Directory -ErrorAction SilentlyContinue)) {
            $savedLink=Join-Path $transaction.FullName 'LegacyNavalDLCLink'
            if((Test-Path -LiteralPath $savedLink) -and ((Get-Item -LiteralPath $savedLink).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                [void]$remove.Invoke($null,[object[]]@([string]$savedLink,[string](Join-Path $game 'Modules/NavalDLC')))
            }
        }
        $officialLink=Join-Path $server 'Modules/NavalDLC'
        if((Test-Path -LiteralPath $officialLink) -and ((Get-Item -LiteralPath $officialLink).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            [void]$remove.Invoke($null,[object[]]@([string]$officialLink,[string](Join-Path $game 'Modules/NavalDLC')))
        }
        if (@(Get-ChildItem -LiteralPath $fixture -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Refusing recursive cleanup with an unexpected remaining link.' }
        Remove-Item -LiteralPath $absolute -Recurse -Force
    }
}
