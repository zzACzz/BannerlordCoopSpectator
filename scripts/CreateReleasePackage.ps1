param(
    [string]$BannerlordRootDir = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord",
    [string]$DedicatedServerRootDir = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server",
    [switch]$SkipBuild,
    [switch]$LightOnly,
    [switch]$GitHubAssetsOnly,
    [switch]$NexusAssetsOnly,
    [switch]$ReleaseAssetsOnly
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$assetOnlyModeCount = [int][bool]$GitHubAssetsOnly +
    [int][bool]$NexusAssetsOnly +
    [int][bool]$ReleaseAssetsOnly
if ($assetOnlyModeCount -gt 1)
{
    throw "GitHubAssetsOnly, NexusAssetsOnly, and ReleaseAssetsOnly are mutually exclusive."
}

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
$releaseVersion = $moduleVersion.Trim()
$releaseTag = "BannerlordCoopCampaign_{0}" -f $releaseVersion
$releaseAssetsRoot = Join-Path $distRoot "releases"
$releaseAssetsDir = Join-Path $releaseAssetsRoot $releaseVersion
$releaseChangelogTemplate = Join-Path $distRoot ("CHANGELOG_{0}.md" -f $moduleVersion.Trim())
$releaseChangelogEnTemplate = Join-Path $releaseAssetsDir ("CHANGELOG_{0}_EN.md" -f $releaseVersion)
$releaseChangelogUaTemplate = Join-Path $releaseAssetsDir ("CHANGELOG_{0}_UA.md" -f $releaseVersion)
$githubReadmeEnTemplate = Join-Path $releaseAssetsDir ("README_{0}_EN.md" -f $releaseVersion)
$githubReadmeUaTemplate = Join-Path $releaseAssetsDir ("README_{0}_UA.md" -f $releaseVersion)

$legacyClientDir = Join-Path $distRoot "CoopSpectator_ClientPackage"
$legacyClientZip = Join-Path $distRoot "CoopSpectator_ClientPackage.zip"
$releaseDir = Join-Path $distRoot ($releaseTag + "_Release")
$releaseZip = Join-Path $distRoot ($releaseTag + "_Release.zip")
$lightReleaseDir = Join-Path $distRoot ($releaseTag + "_LightRelease")
$lightReleaseZip = Join-Path $distRoot ($releaseTag + "_LightRelease.zip")
$githubClientDir = Join-Path $releaseAssetsRoot (".staging_{0}_Client" -f $releaseTag)
$githubClientZip = Join-Path $releaseAssetsDir ($releaseTag + "_Client.zip")
$githubHostDir = Join-Path $releaseAssetsRoot (".staging_{0}_Host" -f $releaseTag)
$githubHostZip = Join-Path $releaseAssetsDir ($releaseTag + "_Host.zip")
$nexusAssetsDir = Join-Path $releaseAssetsDir "Nexus"
$nexusClientZip = Join-Path $nexusAssetsDir ($releaseTag + "_Client.zip")
$nexusHostZip = Join-Path $nexusAssetsDir ($releaseTag + "_HostLite.zip")
$nexusClientTempZip = Join-Path $nexusAssetsDir (".partial_{0}_Client.zip" -f $releaseTag)
$nexusHostTempZip = Join-Path $nexusAssetsDir (".partial_{0}_HostLite.zip" -f $releaseTag)

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

function Get-ProductVersion([string]$filePath)
{
    if (-not (Test-Path $filePath))
    {
        throw "Cannot read ProductVersion because file does not exist: $filePath"
    }

    return [System.Diagnostics.FileVersionInfo]::GetVersionInfo($filePath).ProductVersion
}

function Get-StreamSha256([System.IO.Stream]$stream)
{
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try
    {
        $hashBytes = $sha256.ComputeHash($stream)
        return (($hashBytes | ForEach-Object { $_.ToString("x2") }) -join "").ToUpperInvariant()
    }
    finally
    {
        $sha256.Dispose()
    }
}

function Get-ZipFileMap([string]$zipPath)
{
    Assert-PathExists $zipPath "ZIP archive"

    $files = @{}
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try
    {
        foreach ($entry in $archive.Entries)
        {
            if ([string]::IsNullOrEmpty($entry.Name))
            {
                continue
            }

            $entryPath = $entry.FullName.Replace('\', '/')
            $stream = $entry.Open()
            try
            {
                $entryHash = Get-StreamSha256 $stream
            }
            finally
            {
                $stream.Dispose()
            }

            $files[$entryPath] = [pscustomobject]@{
                Length = $entry.Length
                Sha256 = $entryHash
            }
        }
    }
    finally
    {
        $archive.Dispose()
    }

    return $files
}

function Get-ZipEntryText([string]$zipPath, [string]$entryPath)
{
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try
    {
        $entry = $archive.GetEntry($entryPath)
        if ($null -eq $entry)
        {
            throw "Required ZIP entry not found: $entryPath in $zipPath"
        }

        $stream = $entry.Open()
        try
        {
            $reader = New-Object System.IO.StreamReader(
                $stream,
                [System.Text.Encoding]::UTF8,
                $true)
            try
            {
                return $reader.ReadToEnd()
            }
            finally
            {
                $reader.Dispose()
            }
        }
        finally
        {
            $stream.Dispose()
        }
    }
    finally
    {
        $archive.Dispose()
    }
}

function New-FilteredZipArchive(
    [string]$sourceZip,
    [string]$destinationZip,
    [string[]]$excludedEntries,
    [object[]]$documents)
{
    Assert-PathExists $sourceZip "source ZIP archive"
    if (Test-Path $destinationZip)
    {
        throw "Refusing to overwrite ZIP archive while creating it: $destinationZip"
    }

    $excluded = New-Object 'System.Collections.Generic.HashSet[string]' (
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entryPath in $excludedEntries)
    {
        [void]$excluded.Add($entryPath.Replace('\', '/'))
    }

    $sourceArchive = [System.IO.Compression.ZipFile]::OpenRead($sourceZip)
    $destinationStream = [System.IO.File]::Open(
        $destinationZip,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
    $destinationArchive = New-Object System.IO.Compression.ZipArchive(
        $destinationStream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false)

    try
    {
        foreach ($sourceEntry in @($sourceArchive.Entries | Sort-Object FullName))
        {
            if ([string]::IsNullOrEmpty($sourceEntry.Name))
            {
                continue
            }

            $entryPath = $sourceEntry.FullName.Replace('\', '/')
            if ($excluded.Contains($entryPath))
            {
                continue
            }

            $destinationEntry = $destinationArchive.CreateEntry(
                $entryPath,
                [System.IO.Compression.CompressionLevel]::Optimal)
            $destinationEntry.LastWriteTime = $sourceEntry.LastWriteTime

            $sourceStream = $sourceEntry.Open()
            $entryStream = $destinationEntry.Open()
            try
            {
                $sourceStream.CopyTo($entryStream)
            }
            finally
            {
                $entryStream.Dispose()
                $sourceStream.Dispose()
            }
        }

        foreach ($document in @($documents | Sort-Object EntryName))
        {
            Assert-PathExists $document.SourcePath "Nexus package document"

            $documentEntry = $destinationArchive.CreateEntry(
                $document.EntryName,
                [System.IO.Compression.CompressionLevel]::Optimal)
            $documentEntry.LastWriteTime = (Get-Item -LiteralPath $document.SourcePath).LastWriteTime

            $documentStream = [System.IO.File]::OpenRead($document.SourcePath)
            $entryStream = $documentEntry.Open()
            try
            {
                $documentStream.CopyTo($entryStream)
            }
            finally
            {
                $entryStream.Dispose()
                $documentStream.Dispose()
            }
        }
    }
    finally
    {
        $destinationArchive.Dispose()
        $destinationStream.Dispose()
        $sourceArchive.Dispose()
    }
}

function Assert-PathExists([string]$targetPath, [string]$label)
{
    if (-not (Test-Path $targetPath))
    {
        throw "Release payload validation failed: missing $label at $targetPath"
    }
}

function Assert-ExactChildNames([string]$rootDir, [string[]]$expectedNames, [string]$label)
{
    Assert-PathExists $rootDir $label

    $actualNames = @(Get-ChildItem -LiteralPath $rootDir -Force | ForEach-Object { $_.Name } | Sort-Object)
    $sortedExpectedNames = @($expectedNames | Sort-Object)
    $differences = @(Compare-Object -ReferenceObject $sortedExpectedNames -DifferenceObject $actualNames)
    if ($differences.Count -ne 0)
    {
        throw "Release payload validation failed: unexpected entries in $label. Expected=$($sortedExpectedNames -join ', ') Actual=$($actualNames -join ', ')"
    }
}

function Assert-ProductVersionMatches([string]$expectedVersion, [string]$targetFile, [string]$label)
{
    $actualVersion = Get-ProductVersion $targetFile
    if (-not [string]::Equals($expectedVersion, $actualVersion, [System.StringComparison]::Ordinal))
    {
        throw "Release payload validation failed: $label version mismatch. Expected=$expectedVersion Actual=$actualVersion File=$targetFile"
    }
}

function Validate-LightReleasePayload([string]$lightRoot)
{
    $clientDll = Join-Path $lightRoot "CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll"
    $clientMultiplayerDll = Join-Path $lightRoot "CoopSpectator\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Multiplayer.dll"
    $dedicatedServerDll = Join-Path $lightRoot "CoopSpectatorDedicated\bin\Win64_Shipping_Server\CoopSpectator.dll"
    $dedicatedClientDll = Join-Path $lightRoot "CoopSpectatorDedicated\bin\Win64_Shipping_Client\CoopSpectator.dll"
    $dedicatedMultiplayerDll = Join-Path $lightRoot "CoopSpectatorDedicated\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Multiplayer.dll"

    Assert-PathExists $clientDll "client CoopSpectator.dll"
    Assert-PathExists $clientMultiplayerDll "client TaleWorlds.MountAndBlade.Multiplayer.dll"
    Assert-PathExists $dedicatedServerDll "dedicated server CoopSpectator.dll"
    Assert-PathExists $dedicatedClientDll "dedicated client CoopSpectator.dll"
    Assert-PathExists $dedicatedMultiplayerDll "dedicated client TaleWorlds.MountAndBlade.Multiplayer.dll"

    $expectedClientVersion = Get-ProductVersion (Join-Path $clientModuleSource "bin\Win64_Shipping_Client\CoopSpectator.dll")
    $expectedDedicatedVersion = Get-ProductVersion (Join-Path $dedicatedModuleSource "bin\Win64_Shipping_Server\CoopSpectator.dll")
    if (-not [string]::Equals($expectedClientVersion, $expectedDedicatedVersion, [System.StringComparison]::Ordinal))
    {
        throw "Release payload validation failed: source client/dedicated product versions do not match. Client=$expectedClientVersion Dedicated=$expectedDedicatedVersion"
    }

    Assert-ProductVersionMatches $expectedClientVersion $clientDll "client CoopSpectator.dll"
    Assert-ProductVersionMatches $expectedDedicatedVersion $dedicatedServerDll "dedicated server CoopSpectator.dll"
    Assert-ProductVersionMatches $expectedDedicatedVersion $dedicatedClientDll "dedicated client CoopSpectator.dll"

    Write-Host ("Validated light release payload. ProductVersion={0}" -f $expectedClientVersion)
}

function Validate-GitHubReleasePayload([string]$clientRoot, [string]$hostRoot)
{
    $clientDll = Join-Path $clientRoot "Modules\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll"
    $clientMultiplayerDll = Join-Path $clientRoot "Modules\CoopSpectator\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Multiplayer.dll"
    $clientModuleXml = Join-Path $clientRoot "Modules\CoopSpectator\SubModule.xml"
    $hostServerDll = Join-Path $hostRoot "Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Server\CoopSpectator.dll"
    $hostClientDll = Join-Path $hostRoot "Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client\CoopSpectator.dll"
    $hostMultiplayerDll = Join-Path $hostRoot "Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Multiplayer.dll"
    $hostModuleXml = Join-Path $hostRoot "Modules\CoopSpectatorDedicated\SubModule.xml"

    Assert-PathExists $clientDll "GitHub client CoopSpectator.dll"
    Assert-PathExists $clientMultiplayerDll "GitHub client TaleWorlds.MountAndBlade.Multiplayer.dll"
    Assert-PathExists $clientModuleXml "GitHub client SubModule.xml"
    Assert-PathExists $hostServerDll "GitHub host server CoopSpectator.dll"
    Assert-PathExists $hostClientDll "GitHub host client CoopSpectator.dll"
    Assert-PathExists $hostMultiplayerDll "GitHub host TaleWorlds.MountAndBlade.Multiplayer.dll"
    Assert-PathExists $hostModuleXml "GitHub host SubModule.xml"
    Assert-PathExists $releaseChangelogEnTemplate "standalone English changelog"
    Assert-PathExists $releaseChangelogUaTemplate "standalone Ukrainian changelog"
    Assert-PathExists $githubReadmeEnTemplate "standalone English README"
    Assert-PathExists $githubReadmeUaTemplate "standalone Ukrainian README"

    Assert-ExactChildNames $clientRoot @("Modules", "run_mp_with_mod_from_game_root.bat") "GitHub client package root"
    Assert-ExactChildNames (Join-Path $clientRoot "Modules") @("CoopSpectator") "GitHub client Modules directory"
    Assert-ExactChildNames $hostRoot @("Modules") "GitHub host package root"
    Assert-ExactChildNames (Join-Path $hostRoot "Modules") @("CoopSpectatorDedicated") "GitHub host Modules directory"

    $expectedClientVersion = Get-ProductVersion (Join-Path $clientModuleSource "bin\Win64_Shipping_Client\CoopSpectator.dll")
    $expectedDedicatedVersion = Get-ProductVersion (Join-Path $dedicatedModuleSource "bin\Win64_Shipping_Server\CoopSpectator.dll")
    if (-not [string]::Equals($expectedClientVersion, $expectedDedicatedVersion, [System.StringComparison]::Ordinal))
    {
        throw "GitHub release validation failed: client/dedicated product versions do not match. Client=$expectedClientVersion Dedicated=$expectedDedicatedVersion"
    }

    Assert-ProductVersionMatches $expectedClientVersion $clientDll "GitHub client CoopSpectator.dll"
    Assert-ProductVersionMatches $expectedDedicatedVersion $hostServerDll "GitHub host server CoopSpectator.dll"
    Assert-ProductVersionMatches $expectedDedicatedVersion $hostClientDll "GitHub host client CoopSpectator.dll"

    [xml]$clientXml = Get-Content $clientModuleXml
    [xml]$hostXml = Get-Content $hostModuleXml
    if ($clientXml.Module.Version.value -ne $moduleVersion -or $hostXml.Module.Version.value -ne $moduleVersion)
    {
        throw "GitHub release validation failed: SubModule versions do not match $moduleVersion."
    }

    $debugSymbols = @(Get-ChildItem -Path $clientRoot, $hostRoot -Recurse -File -Filter "*.pdb")
    if ($debugSymbols.Count -ne 0)
    {
        throw "GitHub release validation failed: debug symbols remain in payload."
    }

    Write-Host ("Validated GitHub release payloads. ModuleVersion={0} ProductVersion={1}" -f $moduleVersion, $expectedClientVersion)
}

function Create-GitHubReleaseAssets
{
    New-Item -ItemType Directory -Path $releaseAssetsDir -Force | Out-Null

    Assert-PathExists $releaseChangelogEnTemplate "English release changelog"
    Assert-PathExists $releaseChangelogUaTemplate "Ukrainian release changelog"
    Assert-PathExists $githubReadmeEnTemplate "English release README"
    Assert-PathExists $githubReadmeUaTemplate "Ukrainian release README"

    Reset-Path $githubClientDir
    Reset-Path $githubClientZip
    Reset-Path $githubHostDir
    Reset-Path $githubHostZip

    try
    {
        New-Item -ItemType Directory -Path (Join-Path $githubClientDir "Modules") -Force | Out-Null
        Copy-DirectoryContent $clientModuleSource (Join-Path $githubClientDir "Modules\CoopSpectator")
        Copy-Item -LiteralPath $portableLauncher -Destination (Join-Path $githubClientDir "run_mp_with_mod_from_game_root.bat") -Force

        New-Item -ItemType Directory -Path (Join-Path $githubHostDir "Modules") -Force | Out-Null
        Copy-HostPayload (Join-Path $githubHostDir "Modules") $false

        Remove-DebugSymbols $githubClientDir
        Remove-DebugSymbols $githubHostDir
        Validate-GitHubReleasePayload $githubClientDir $githubHostDir

        Compress-Archive -Path (Join-Path $githubClientDir "*") -DestinationPath $githubClientZip -CompressionLevel Optimal
        Compress-Archive -Path (Join-Path $githubHostDir "*") -DestinationPath $githubHostZip -CompressionLevel Optimal
    }
    finally
    {
        Reset-Path $githubClientDir
        Reset-Path $githubHostDir
    }

    Write-Host ("Created GitHub client package: {0}" -f $githubClientZip)
    Write-Host ("Created GitHub host package: {0}" -f $githubHostZip)
}

function Assert-ZipPayloadMatches(
    [hashtable]$sourceMap,
    [hashtable]$outputMap,
    [string[]]$excludedSourceEntries,
    [string[]]$documentEntries,
    [string]$label)
{
    $normalizedExclusions = @($excludedSourceEntries | ForEach-Object { $_.Replace('\', '/') })
    $expectedPayloadEntries = @(
        $sourceMap.Keys |
            Where-Object { $_ -notin $normalizedExclusions } |
            Sort-Object)
    $actualPayloadEntries = @(
        $outputMap.Keys |
            Where-Object { $_ -notin $documentEntries } |
            Sort-Object)
    $differences = @(
        Compare-Object -ReferenceObject $expectedPayloadEntries -DifferenceObject $actualPayloadEntries)
    if ($differences.Count -ne 0)
    {
        throw "Nexus payload validation failed for $label. Entry differences: $($differences | Out-String)"
    }

    foreach ($entryPath in $expectedPayloadEntries)
    {
        $sourceEntry = $sourceMap[$entryPath]
        $outputEntry = $outputMap[$entryPath]
        if ($sourceEntry.Length -ne $outputEntry.Length -or
            -not [string]::Equals(
                $sourceEntry.Sha256,
                $outputEntry.Sha256,
                [System.StringComparison]::OrdinalIgnoreCase))
        {
            throw "Nexus payload validation failed for $label. Content mismatch: $entryPath"
        }
    }
}

function Validate-NexusReleasePayload([string]$clientZip, [string]$hostZip)
{
    Assert-PathExists $githubClientZip "GitHub client package used as Nexus source"
    Assert-PathExists $githubHostZip "GitHub host package used as Nexus source"
    Assert-PathExists $clientZip "Nexus client package"
    Assert-PathExists $hostZip "Nexus HostLite package"

    $documents = @(
        [pscustomobject]@{ EntryName = (Split-Path $releaseChangelogEnTemplate -Leaf); SourcePath = $releaseChangelogEnTemplate },
        [pscustomobject]@{ EntryName = (Split-Path $releaseChangelogUaTemplate -Leaf); SourcePath = $releaseChangelogUaTemplate },
        [pscustomobject]@{ EntryName = (Split-Path $githubReadmeEnTemplate -Leaf); SourcePath = $githubReadmeEnTemplate },
        [pscustomobject]@{ EntryName = (Split-Path $githubReadmeUaTemplate -Leaf); SourcePath = $githubReadmeUaTemplate }
    )
    foreach ($document in $documents)
    {
        Assert-PathExists $document.SourcePath "Nexus package document"
    }

    $documentEntries = @($documents | ForEach-Object { $_.EntryName })
    $clientExclusions = @(
        "run_mp_with_mod_from_game_root.bat",
        "Modules/CoopSpectator/CoopShaderCacheModeSwitch.ps1")
    $hostExclusions = @()

    $githubClientMap = Get-ZipFileMap $githubClientZip
    $githubHostMap = Get-ZipFileMap $githubHostZip
    $clientMap = Get-ZipFileMap $clientZip
    $hostMap = Get-ZipFileMap $hostZip

    Assert-ZipPayloadMatches $githubClientMap $clientMap $clientExclusions $documentEntries "Nexus client"
    Assert-ZipPayloadMatches $githubHostMap $hostMap $hostExclusions $documentEntries "Nexus HostLite"

    $payloads = @(
        [pscustomobject]@{
            Label = "Nexus client"
            Map = $clientMap
            SourceMap = $githubClientMap
            Exclusions = $clientExclusions
            ModulePrefix = "Modules/CoopSpectator/"
        },
        [pscustomobject]@{
            Label = "Nexus HostLite"
            Map = $hostMap
            SourceMap = $githubHostMap
            Exclusions = $hostExclusions
            ModulePrefix = "Modules/CoopSpectatorDedicated/"
        })

    foreach ($payload in $payloads)
    {
        $expectedCount = $payload.SourceMap.Count - $payload.Exclusions.Count + $documentEntries.Count
        if ($payload.Map.Count -ne $expectedCount)
        {
            throw "Nexus payload validation failed for $($payload.Label). Expected $expectedCount files, found $($payload.Map.Count)."
        }

        foreach ($document in $documents)
        {
            if (-not $payload.Map.ContainsKey($document.EntryName))
            {
                throw "Nexus payload validation failed for $($payload.Label). Missing document: $($document.EntryName)"
            }

            $sourceHash = (Get-FileHash -LiteralPath $document.SourcePath -Algorithm SHA256).Hash
            if (-not [string]::Equals(
                    $sourceHash,
                    $payload.Map[$document.EntryName].Sha256,
                    [System.StringComparison]::OrdinalIgnoreCase))
            {
                throw "Nexus payload validation failed for $($payload.Label). Document mismatch: $($document.EntryName)"
            }
        }

        $forbiddenEntries = @(
            $payload.Map.Keys |
                Where-Object { $_ -match '(?i)\.(bat|ps1|pdb)$' })
        if ($forbiddenEntries.Count -ne 0)
        {
            throw "Nexus payload validation failed for $($payload.Label). Forbidden files: $($forbiddenEntries -join ', ')"
        }

        $unexpectedEntries = @(
            $payload.Map.Keys |
                Where-Object {
                    $_ -notin $documentEntries -and
                    -not $_.StartsWith(
                        $payload.ModulePrefix,
                        [System.StringComparison]::Ordinal)
                })
        if ($unexpectedEntries.Count -ne 0)
        {
            throw "Nexus payload validation failed for $($payload.Label). Unexpected root entries: $($unexpectedEntries -join ', ')"
        }
    }

    [xml]$clientXml = Get-ZipEntryText $clientZip "Modules/CoopSpectator/SubModule.xml"
    [xml]$hostXml = Get-ZipEntryText $hostZip "Modules/CoopSpectatorDedicated/SubModule.xml"
    if ($clientXml.Module.Version.value -ne $moduleVersion -or
        $hostXml.Module.Version.value -ne $moduleVersion)
    {
        throw "Nexus payload validation failed. SubModule versions do not match $moduleVersion."
    }

    $sourceClientDll = Join-Path $clientModuleSource "bin\Win64_Shipping_Client\CoopSpectator.dll"
    $sourceHostDll = Join-Path $dedicatedModuleSource "bin\Win64_Shipping_Server\CoopSpectator.dll"
    $expectedProductVersion = $releaseVersion -replace '^[vV]', ''
    Assert-ProductVersionMatches $expectedProductVersion $sourceClientDll "source client CoopSpectator.dll"
    Assert-ProductVersionMatches $expectedProductVersion $sourceHostDll "source dedicated CoopSpectator.dll"

    $sourceClientDllHash = (Get-FileHash -LiteralPath $sourceClientDll -Algorithm SHA256).Hash
    $sourceHostDllHash = (Get-FileHash -LiteralPath $sourceHostDll -Algorithm SHA256).Hash
    if (-not [string]::Equals(
            $sourceClientDllHash,
            $clientMap["Modules/CoopSpectator/bin/Win64_Shipping_Client/CoopSpectator.dll"].Sha256,
            [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "Nexus payload validation failed. Client CoopSpectator.dll does not match the source module."
    }
    foreach ($hostDllEntry in @(
            "Modules/CoopSpectatorDedicated/bin/Win64_Shipping_Client/CoopSpectator.dll",
            "Modules/CoopSpectatorDedicated/bin/Win64_Shipping_Server/CoopSpectator.dll"))
    {
        if (-not [string]::Equals(
                $sourceHostDllHash,
                $hostMap[$hostDllEntry].Sha256,
                [System.StringComparison]::OrdinalIgnoreCase))
        {
            throw "Nexus payload validation failed. Host CoopSpectator.dll does not match the source module: $hostDllEntry"
        }
    }

    Write-Host (
        "Validated Nexus release payloads. ModuleVersion={0} ClientFiles={1} HostLiteFiles={2}" -f
            $moduleVersion,
            $clientMap.Count,
            $hostMap.Count)
}

function Create-NexusReleaseAssets
{
    New-Item -ItemType Directory -Path $nexusAssetsDir -Force | Out-Null

    Assert-PathExists $githubClientZip "GitHub client package used as Nexus source"
    Assert-PathExists $githubHostZip "GitHub host package used as Nexus source"
    Assert-PathExists $releaseChangelogEnTemplate "English release changelog"
    Assert-PathExists $releaseChangelogUaTemplate "Ukrainian release changelog"
    Assert-PathExists $githubReadmeEnTemplate "English release README"
    Assert-PathExists $githubReadmeUaTemplate "Ukrainian release README"

    $documents = @(
        [pscustomobject]@{ EntryName = (Split-Path $releaseChangelogEnTemplate -Leaf); SourcePath = $releaseChangelogEnTemplate },
        [pscustomobject]@{ EntryName = (Split-Path $releaseChangelogUaTemplate -Leaf); SourcePath = $releaseChangelogUaTemplate },
        [pscustomobject]@{ EntryName = (Split-Path $githubReadmeEnTemplate -Leaf); SourcePath = $githubReadmeEnTemplate },
        [pscustomobject]@{ EntryName = (Split-Path $githubReadmeUaTemplate -Leaf); SourcePath = $githubReadmeUaTemplate }
    )

    Reset-Path $nexusClientTempZip
    Reset-Path $nexusHostTempZip
    Reset-Path $nexusClientZip
    Reset-Path $nexusHostZip

    try
    {
        New-FilteredZipArchive $githubClientZip $nexusClientTempZip @(
            "run_mp_with_mod_from_game_root.bat",
            "Modules/CoopSpectator/CoopShaderCacheModeSwitch.ps1") $documents
        New-FilteredZipArchive $githubHostZip $nexusHostTempZip @() $documents

        Validate-NexusReleasePayload $nexusClientTempZip $nexusHostTempZip

        Move-Item -LiteralPath $nexusClientTempZip -Destination $nexusClientZip
        Move-Item -LiteralPath $nexusHostTempZip -Destination $nexusHostZip
    }
    finally
    {
        Reset-Path $nexusClientTempZip
        Reset-Path $nexusHostTempZip
    }

    Write-Host ("Created Nexus client package: {0}" -f $nexusClientZip)
    Write-Host ("Created Nexus HostLite package: {0}" -f $nexusHostZip)
    Write-Host ("Nexus client SHA256: {0}" -f (
        Get-FileHash -LiteralPath $nexusClientZip -Algorithm SHA256).Hash)
    Write-Host ("Nexus HostLite SHA256: {0}" -f (
        Get-FileHash -LiteralPath $nexusHostZip -Algorithm SHA256).Hash)
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

if ($ReleaseAssetsOnly)
{
    Create-GitHubReleaseAssets
    Create-NexusReleaseAssets
    return
}

if ($GitHubAssetsOnly)
{
    Create-GitHubReleaseAssets
    return
}

if ($NexusAssetsOnly)
{
    Create-NexusReleaseAssets
    return
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

Copy-DirectoryContent $clientModuleSource (Join-Path $lightReleaseDir "CoopSpectator")
Copy-HostPayload $lightReleaseDir $false
Remove-DebugSymbols $lightReleaseDir
Validate-LightReleasePayload $lightReleaseDir
Copy-Item -LiteralPath $lightReleaseReadmeEnTemplate -Destination (Join-Path $lightReleaseDir "README_EN.md") -Force
Copy-Item -LiteralPath $lightReleaseReadmeUaTemplate -Destination (Join-Path $lightReleaseDir "README_UA.md") -Force
Copy-Item -LiteralPath $releaseChangelogTemplate -Destination (Join-Path $lightReleaseDir (Split-Path $releaseChangelogTemplate -Leaf)) -Force

Compress-Archive -Path (Join-Path $lightReleaseDir "*") -DestinationPath $lightReleaseZip -CompressionLevel Optimal

Write-Host ("Created light release package: {0}" -f $lightReleaseZip)
