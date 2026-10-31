param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [ValidateSet('Auto','Base','Naval')][string]$ContentProfile='Auto',
    [switch]$UseExternalHarmony
)
$ErrorActionPreference='Stop'
$GameRoot=[IO.Path]::GetFullPath($GameRoot)
$mode=$ContentProfile
if ($mode -eq 'Auto') {
    $mode='Base'
    $hostMarker=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Mount and Blade II Bannerlord\CoopSpectator\content-profile-host.txt'
    $observedHost=$false
    if ([IO.File]::Exists($hostMarker) -and (Get-Item -LiteralPath $hostMarker).Length -lt 4096) {
        $lines=[IO.File]::ReadAllLines($hostMarker)
        if ($lines.Count -eq 4 -and $lines[0] -eq 'warhost-local-content-v1') {
            $ownerId=0
            if ([int]::TryParse($lines[1],[ref]$ownerId)) {
                $owner=Get-Process -Id $ownerId -ErrorAction SilentlyContinue
                if ($owner -and $owner.ProcessName -eq 'Bannerlord' -and
                    $owner.StartTime.ToUniversalTime() -eq ([DateTimeOffset]::Parse($lines[2])).UtcDateTime -and
                    $lines[3] -match '^(base|naval)-v1:[0-9a-f]{64}$') {
                    $mode=if ($lines[3].StartsWith('naval-')) {'Naval'} else {'Base'}
                    $observedHost=$true
                }
            }
        }
    }
    if (-not $observedHost -and (Test-Path -LiteralPath (Join-Path $GameRoot 'Modules\NavalDLC\SubModule.xml'))) {
        Write-Host 'Choose the same campaign resources as the host: 1 = Base game, 2 = War Sails.'
        $choice=Read-Host 'Content profile [1/2]'
        if ($choice -notin @('1','2')) { throw 'Choose 1 or 2, or launch with --base / --naval.' }
        $mode=if ($choice -eq '2') {'Naval'} else {'Base'}
    }
}
$moduleRoot=Join-Path $GameRoot 'Modules\CoopSpectator'
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $moduleRoot 'bin\Win64_Shipping_Client\CoopSpectator.dll'))
$running=@(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'Bannerlord.exe' -and
    $_.CommandLine -match '(?i)/multiplayer' })
if ($running.Count) { throw 'Close the existing multiplayer client before preparing map resources.' }
$mapVisuals=$assembly.GetType('CoopSpectator.DedicatedHelper.CoopMapVisualResourceAdapter',$true)
[void]$mapVisuals.GetMethod('Prepare').Invoke($null,[object[]]@([string]$GameRoot,[string]$GameRoot))
if ($mode -eq 'Naval') {
    # Creating/replacing the resource adapter while an MP process is running is
    # forbidden. A campaign on this PC can remain open: it does not load it.
    $running=@(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'Bannerlord.exe' -and
        $_.CommandLine -match '(?i)/multiplayer' })
    if ($running.Count) { throw 'Close the existing multiplayer client before preparing War Sails resources.' }
    $type=$assembly.GetType('CoopSpectator.DedicatedHelper.CoopNavalResourceAdapter',$true)
    [void]$type.GetMethod('PrepareClient').Invoke($null,[object[]]@([string]$GameRoot,[string]$GameRoot))
}
$modules='_MODULES_*Native*SandBoxCore*Sandbox*Multiplayer*'
if ($mode -eq 'Naval') { $modules+='CoopSpectatorNavalCatalog*' }
$modules+='CoopSpectatorMapResources*'
if ($UseExternalHarmony) { $modules+='Bannerlord.Harmony*' }
$modules+='CoopSpectator*'
if ($mode -eq 'Naval') { $modules+='NavalDLC*' }
$modules+='_MODULES_'
if (-not [IO.Path]::IsPathRooted($OutputPath)) { throw 'Absolute launcher response path required.' }
[IO.File]::WriteAllText($OutputPath,$modules,[Text.UTF8Encoding]::new($false))
Write-Host ('[INFO] Campaign content profile: '+$mode)
