param(
    [Parameter(Mandatory=$true)][string]$BuildRunRoot,
    [Parameter(Mandatory=$true)][string]$ContractRunRoot,
    [Parameter(Mandatory=$true)][string]$PackageName
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$repo = Split-Path -Parent $PSScriptRoot
if ($PackageName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Unsafe package name' }
$build = Get-Content (Join-Path $BuildRunRoot 'artifacts/results/compile-only.json') -Raw | ConvertFrom-Json
$contracts = Get-Content (Join-Path $ContractRunRoot 'artifacts/results/contracts.json') -Raw | ConvertFrom-Json
if ($build.ClientBuild.ExitCode -ne 0 -or $build.DedicatedBuild.ExitCode -ne 0 -or
    @($build.Assertions | Where-Object Outcome -ne 'Pass').Count) { throw 'Build is not verified' }
if (-not $contracts.IsFullSuite -or $contracts.ProjectCount -lt 26 -or
    $contracts.FailedCount -ne 0 -or $contracts.PassedCount -ne $contracts.ProjectCount -or
    @($contracts.Results | Where-Object Outcome -ne 'Pass').Count) { throw 'Contracts did not pass' }
foreach ($binary in @($build.ClientOutput, $build.DedicatedOutput)) {
    if ((Get-FileHash -LiteralPath $binary.Path).Hash -ne $binary.Sha256) { throw 'Build output changed after verification' }
}
$defaultEvidence=@()
foreach($binary in @($build.ClientOutput,$build.DedicatedOutput)) {
    $defaultEvidence += & (Join-Path $PSScriptRoot 'Test-ReplicaDefaults.ps1') -CandidateDirectory (Split-Path -Parent $binary.Path)
}
$output = Join-Path $repo ('dist/previews/' + $PackageName)
$zip = $output + '.zip'
if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath $zip)) { throw 'Use a fresh package name' }
New-Item -ItemType Directory -Path $output | Out-Null
$client = Join-Path $output 'Client/Modules/CoopSpectator'
$hostModule = Join-Path $output 'Host/Modules/CoopSpectatorDedicated'
function Copy-One([string]$source, [string]$target) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing source: $source" }
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw "Copy mismatch: $target" }
}
function Copy-Static([string]$source, [string]$target) {
    $source = (Resolve-Path -LiteralPath $source).Path
    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
        $relative = $file.FullName.Substring($source.Length + 1)
        if ($relative -match '^(bin|obj)[\\/]' -or $file.Extension -eq '.pdb') { continue }
        # Abandoned, unreferenced tooltip experiment; native tooltips are used.
        if ($relative.Replace('\','/') -eq 'GUI/Prefabs/CoopCampaignLootTooltip.xml') { continue }
        Copy-One $file.FullName (Join-Path $target $relative)
    }
}
Copy-Static (Join-Path $repo 'Module/CoopSpectator') $client
Copy-Static (Join-Path $repo 'Module/CoopSpectatorDedicated') $hostModule
Copy-Static (Join-Path $repo 'Module/CoopSpectator/ModuleData') (Join-Path $hostModule 'ModuleData')
$compiled = Join-Path $BuildRunRoot 'work/compile-only/projects'
foreach ($name in @('CoopSpectator.dll', '0Harmony.dll')) {
    Copy-One (Join-Path $compiled "CoopSpectator/bin/$name") (Join-Path $client "bin/Win64_Shipping_Client/$name")
}
foreach ($folder in @('Win64_Shipping_Server','Win64_Shipping_Client')) {
    foreach ($name in @('CoopSpectator.dll','0Harmony.dll','SandBox.dll','TaleWorlds.CampaignSystem.dll')) {
        Copy-One (Join-Path $compiled "CoopSpectatorDedicated/bin/$name") (Join-Path $hostModule "bin/$folder/$name")
    }
}
$game = 'C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord'
$server = 'C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Dedicated Server'
Copy-One (Join-Path $game 'Modules/Multiplayer/bin/Win64_Shipping_Client/TaleWorlds.MountAndBlade.Multiplayer.dll') (Join-Path $client 'bin/Win64_Shipping_Client/TaleWorlds.MountAndBlade.Multiplayer.dll')
Copy-One (Join-Path $server 'bin/Win64_Shipping_Server/TaleWorlds.MountAndBlade.Multiplayer.dll') (Join-Path $hostModule 'bin/Win64_Shipping_Client/TaleWorlds.MountAndBlade.Multiplayer.dll')
Copy-One (Join-Path $repo 'run_mp_with_mod_from_game_root.bat') (Join-Path $output 'Client/run_mp_with_mod_from_game_root.bat')
Copy-One (Join-Path $repo 'artifacts/hotfix-v032-server-harmony/LICENSE-Harmony.txt') (Join-Path $output 'LICENSE-Harmony.txt')
Copy-One (Join-Path $repo 'docs/TWO_PC_PREVIEW_TEST_EN.md') (Join-Path $output 'README_TEST_EN.md')
Copy-One (Join-Path $BuildRunRoot 'artifacts/results/compile-only.json') (Join-Path $output 'Verification/compile-only.json')
Copy-One (Join-Path $ContractRunRoot 'artifacts/results/contracts.json') (Join-Path $output 'Verification/contracts.json')
$defaultEvidence | Set-Content -LiteralPath (Join-Path $output 'Verification/replica-defaults.txt') -Encoding UTF8
[xml]$clientXml = Get-Content (Join-Path $client 'SubModule.xml')
[xml]$hostXml = Get-Content (Join-Path $hostModule 'SubModule.xml')
if ($clientXml.Module.Version.value -ne $hostXml.Module.Version.value) { throw 'Module versions differ' }
$files = @(Get-ChildItem -LiteralPath $output -Recurse -File | ForEach-Object {
    [pscustomobject]@{ Path = $_.FullName.Substring($output.Length + 1).Replace('\','/'); Length = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
[xml]$nativeXml=Get-Content -LiteralPath (Join-Path $game 'Modules/Native/SubModule.xml')
[pscustomobject]@{ Package=$PackageName; Kind='Development preview, not public v0.3.2 release';
    BuildRun=$build.RunId; ContractRun=$contracts.RunId; NativeVersion=$nativeXml.Module.Version.value;
    SourceRevision=(& git -C $repo rev-parse HEAD); WorkingTreeSnapshot=$true;
    ReplicaEnabledByDefault=$true; AutomaticMissionsEnabledByDefault=$true; SiegeFlightsEnabledByDefault=$true;
    Files=$files } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'MANIFEST.json') -Encoding UTF8
$writer = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -File) {
        $entryName = $file.FullName.Substring($output.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($writer, $file.FullName, $entryName,
            [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $writer.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    if ($archive.Entries.Count -ne $files.Count + 1) { throw 'Unexpected archive entries' }
    foreach ($file in $files) {
        $entry = $archive.GetEntry($file.Path)
        if ($null -eq $entry -or $entry.Length -ne $file.Length) { throw "Missing or wrong ZIP entry: $($file.Path)" }
        $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($hash -ne $file.SHA256) { throw "ZIP hash mismatch: $($file.Path)" }
    }
} finally { $archive.Dispose() }
Get-Item -LiteralPath $zip | Select-Object FullName,Length
Get-FileHash -LiteralPath $zip -Algorithm SHA256
