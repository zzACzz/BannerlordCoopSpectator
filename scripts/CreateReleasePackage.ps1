param(
    [string]$BannerlordRootDir = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord",
    [string]$DedicatedServerRootDir = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server",
    [switch]$SkipBuild,
    [switch]$LightOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $repoRoot "dist"
$clientModuleSource = Join-Path $repoRoot "Module\CoopSpectator"
$dedicatedModuleSource = Join-Path $repoRoot "Module\CoopSpectatorDedicated"
$portableLauncher = Join-Path $repoRoot "run_mp_with_mod_from_game_root.bat"
$clientReadmeTemplate = Join-Path $distRoot "README_CLIENT_PACKAGE.txt"
$releaseReadmeEnTemplate = Join-Path $distRoot "README_RELEASE_EN.md"
$releaseReadmeUaTemplate = Join-Path $distRoot "README_RELEASE_UA.md"
$lightReleaseReadmeEnTemplate = Join-Path $distRoot "README_LIGHT_RELEASE_EN.md"
$lightReleaseReadmeUaTemplate = Join-Path $distRoot "README_LIGHT_RELEASE_UA.md"

[xml]$moduleXml = Get-Content (Join-Path $clientModuleSource "SubModule.xml")
$moduleVersion = $moduleXml.Module.Version.value
$releaseTag = "BannerlordCoopCampaign_{0}" -f $moduleVersion.Trim()

$legacyClientDir = Join-Path $distRoot "CoopSpectator_ClientPackage"
$legacyClientZip = Join-Path $distRoot "CoopSpectator_ClientPackage.zip"
$releaseDir = Join-Path $distRoot ($releaseTag + "_Release")
$releaseZip = Join-Path $distRoot ($releaseTag + "_Release.zip")
$lightReleaseDir = Join-Path $distRoot ($releaseTag + "_LightRelease")
$lightReleaseZip = Join-Path $distRoot ($releaseTag + "_LightRelease.zip")

function Reset-Path([string]$targetPath)
{
    if (Test-Path $targetPath)
    {
        Remove-Item -LiteralPath $targetPath -Recurse -Force
    }
}

function Copy-DirectoryContent([string]$sourceDir, [string]$destinationDir)
{
    New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceDir "*") -Destination $destinationDir -Recurse -Force
}

function Copy-RequiredFile([string]$sourceFile, [string]$destinationDir)
{
    if (-not (Test-Path $sourceFile))
    {
        throw "Required source file not found: $sourceFile"
    }

    New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
    Copy-Item -LiteralPath $sourceFile -Destination (Join-Path $destinationDir (Split-Path $sourceFile -Leaf)) -Force
}

function Copy-FirstExistingFile([string[]]$sourceFiles, [string]$destinationDir)
{
    foreach ($sourceFile in $sourceFiles)
    {
        if (Test-Path $sourceFile)
        {
            Copy-RequiredFile $sourceFile $destinationDir
            return
        }
    }

    throw "Required source file not found in any known location: $($sourceFiles -join '; ')"
}

function Copy-MatchingFilesRelative([string]$sourceRoot, [string]$destinationRoot, [string]$filter)
{
    if (-not (Test-Path $sourceRoot))
    {
        throw "Required source directory not found: $sourceRoot"
    }

    New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null

    $resolvedSourceRoot = (Resolve-Path $sourceRoot).Path.TrimEnd('\', '/')
    Get-ChildItem -LiteralPath $resolvedSourceRoot -Recurse -File -Filter $filter | ForEach-Object {
        $relativePath = $_.FullName.Substring($resolvedSourceRoot.Length).TrimStart('\', '/')
        $destinationFile = Join-Path $destinationRoot $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $destinationFile) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destinationFile -Force
    }
}

function Copy-ChildDirectories([string]$sourceParent, [string]$destinationParent, [string]$filter)
{
    if (-not (Test-Path $sourceParent))
    {
        throw "Required source directory not found: $sourceParent"
    }

    $directories = @(Get-ChildItem -LiteralPath $sourceParent -Directory -Filter $filter)
    if ($directories.Count -eq 0)
    {
        throw "No source directories matched '$filter' under: $sourceParent"
    }

    New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    foreach ($directory in $directories)
    {
        Copy-DirectoryContent $directory.FullName (Join-Path $destinationParent $directory.Name)
    }
}

function Remove-DebugSymbols([string]$rootDir)
{
    if (-not (Test-Path $rootDir))
    {
        return
    }

    Get-ChildItem -Path $rootDir -Recurse -File -Filter "*.pdb" | Remove-Item -Force
}

function Copy-HostPayload([string]$hostModulesDir, [bool]$includeBaseSceneModules)
{
    $hostDedicatedModuleDir = Join-Path $hostModulesDir "CoopSpectatorDedicated"
    $hostDedicatedBinServer = Join-Path $hostDedicatedModuleDir "bin\Win64_Shipping_Server"
    $hostDedicatedBinClient = Join-Path $hostDedicatedModuleDir "bin\Win64_Shipping_Client"
    $sandboxSourceDir = Join-Path $BannerlordRootDir "Modules\SandBox"
    $sandboxCoreSourceDir = Join-Path $BannerlordRootDir "Modules\SandBoxCore"

    Copy-DirectoryContent $dedicatedModuleSource $hostDedicatedModuleDir
    Copy-MatchingFilesRelative (Join-Path $clientModuleSource "ModuleData") (Join-Path $hostDedicatedModuleDir "ModuleData") "*.xml"
    Copy-DirectoryContent $hostDedicatedBinServer $hostDedicatedBinClient
    Copy-FirstExistingFile @(
        (Join-Path $DedicatedServerRootDir "Modules\Multiplayer\bin\Win64_Shipping_Server\TaleWorlds.MountAndBlade.Multiplayer.dll"),
        (Join-Path $DedicatedServerRootDir "Modules\Multiplayer\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Multiplayer.dll"),
        (Join-Path $DedicatedServerRootDir "bin\Win64_Shipping_Server\TaleWorlds.MountAndBlade.Multiplayer.dll"),
        (Join-Path $DedicatedServerRootDir "bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Multiplayer.dll")
    ) $hostDedicatedBinClient

    if (-not $includeBaseSceneModules)
    {
        return
    }

    $hostSandboxDir = Join-Path $hostModulesDir "SandBox"
    $hostSandboxCoreDir = Join-Path $hostModulesDir "SandBoxCore"
    Copy-RequiredFile (Join-Path $sandboxSourceDir "SubModule.xml") $hostSandboxDir
    Copy-MatchingFilesRelative (Join-Path $sandboxSourceDir "ModuleData") (Join-Path $hostSandboxDir "ModuleData") "*.xml"
    Copy-RequiredFile (Join-Path $sandboxCoreSourceDir "SubModule.xml") $hostSandboxCoreDir
    Copy-MatchingFilesRelative (Join-Path $sandboxCoreSourceDir "ModuleData") (Join-Path $hostSandboxCoreDir "ModuleData") "*.xml"
    Copy-ChildDirectories (Join-Path $sandboxCoreSourceDir "SceneObj") (Join-Path $hostSandboxCoreDir "SceneObj") "battle_terrain*"
}

if (-not $SkipBuild)
{
    Push-Location $repoRoot
    try
    {
        dotnet build .\CoopSpectator.csproj -c Release /p:BuildDedicatedServerModule=false /p:BannerlordRootDir="$BannerlordRootDir"
        if ($LASTEXITCODE -ne 0) { throw "Client Release build failed." }

        dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Release /p:UseDedicatedServerRefs=true /p:BannerlordRootDir="$BannerlordRootDir" /p:DedicatedServerRootDir="$DedicatedServerRootDir"
        if ($LASTEXITCODE -ne 0) { throw "Dedicated Release build failed." }
    }
    finally
    {
        Pop-Location
    }
}

if (-not $LightOnly)
{
    Reset-Path $legacyClientDir
    Reset-Path $legacyClientZip
    Reset-Path $releaseDir
    Reset-Path $releaseZip

    New-Item -ItemType Directory -Path $legacyClientDir -Force | Out-Null
    Copy-DirectoryContent $clientModuleSource (Join-Path $legacyClientDir "Modules\CoopSpectator")
    Remove-DebugSymbols $legacyClientDir
    Copy-Item -LiteralPath $portableLauncher -Destination (Join-Path $legacyClientDir "run_mp_with_mod_from_game_root.bat") -Force
    Copy-Item -LiteralPath $clientReadmeTemplate -Destination (Join-Path $legacyClientDir "README_CLIENT_PACKAGE.txt") -Force
    Compress-Archive -Path $legacyClientDir -DestinationPath $legacyClientZip -CompressionLevel Optimal

    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $releaseDir "Client") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $releaseDir "Host") -Force | Out-Null

    Copy-DirectoryContent $clientModuleSource (Join-Path $releaseDir "Client\Modules\CoopSpectator")
    Copy-Item -LiteralPath $portableLauncher -Destination (Join-Path $releaseDir "Client\run_mp_with_mod_from_game_root.bat") -Force
    Copy-HostPayload (Join-Path $releaseDir "Host\Modules") $true
    Remove-DebugSymbols $releaseDir

    Copy-Item -LiteralPath $releaseReadmeEnTemplate -Destination (Join-Path $releaseDir "README_EN.md") -Force
    Copy-Item -LiteralPath $releaseReadmeUaTemplate -Destination (Join-Path $releaseDir "README_UA.md") -Force
    Compress-Archive -Path (Join-Path $releaseDir "*") -DestinationPath $releaseZip -CompressionLevel Optimal

    Write-Host ("Created legacy client package: {0}" -f $legacyClientZip)
    Write-Host ("Created unified release package: {0}" -f $releaseZip)
}

Reset-Path $lightReleaseDir
Reset-Path $lightReleaseZip

New-Item -ItemType Directory -Path $lightReleaseDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $lightReleaseDir "Client") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $lightReleaseDir "Host") -Force | Out-Null

Copy-DirectoryContent $clientModuleSource (Join-Path $lightReleaseDir "Client\Modules\CoopSpectator")
Copy-Item -LiteralPath $portableLauncher -Destination (Join-Path $lightReleaseDir "Client\run_mp_with_mod_from_game_root.bat") -Force
Copy-HostPayload (Join-Path $lightReleaseDir "Host\Modules") $false
Remove-DebugSymbols $lightReleaseDir

Copy-Item -LiteralPath $lightReleaseReadmeEnTemplate -Destination (Join-Path $lightReleaseDir "README_EN.md") -Force
Copy-Item -LiteralPath $lightReleaseReadmeUaTemplate -Destination (Join-Path $lightReleaseDir "README_UA.md") -Force
Compress-Archive -Path (Join-Path $lightReleaseDir "*") -DestinationPath $lightReleaseZip -CompressionLevel Optimal

Write-Host ("Created light release package: {0}" -f $lightReleaseZip)
