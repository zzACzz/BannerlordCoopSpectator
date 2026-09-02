[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceFixtureRoot,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedSourcePayloadSha256,

    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,

    [switch]$AllowRepositoryOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$supportedSourcePayloadSha256 = 'ECF29661E44B64C1AEE77EC2B44E61F63926287A3A05A9BFD6DC545EC073B9C7'
$payloadFileName = 'battle_roster.sanitized.json'
$metadataFileName = 'fixture.sanitized.metadata.json'
$oracleFileName = 'fixture.oracle.json'
$sanitizationPolicy = 'CoopFieldFixtureSanitizationV1'
$canonicalHeroBodyProperties = '<BodyProperties version="4" age="20" weight="0" build="0" key="00000000000000000000000000000000" />'
$canonicalBannerCodes = @(
    '11.163.166.1528.1528.764.764.1.0.0.133.171.171.483.483.764.764.0.0.0',
    '35.116.116.1528.1528.766.740.1.0.0.510.19.171.1528.353.758.658.0.0.0.510.19.171.1528.398.760.845.0.0.0')
$canonicalColors = @(
    [ordered]@{ Color = 4281545523; Color2 = 4294967295 },
    [ordered]@{ Color = 4286611584; Color2 = 4294967295 })

function Test-Sha256Hex {
    param([string]$Value)
    return -not [string]::IsNullOrWhiteSpace($Value) -and $Value -match '^[0-9A-Fa-f]{64}$'
}

function Get-FileSha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-BytesSha256 {
    param([byte[]]$Bytes)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
    }
}

function ConvertTo-DeterministicJsonBytes {
    param([Parameter(Mandatory = $true)]$Value)
    $text = $Value | ConvertTo-Json -Depth 100 -Compress
    $text = $text.Replace('<', '\u003c').Replace('>', '\u003e')
    $text = $text -replace "`r`n", "`n"
    if (-not $text.EndsWith("`n", [StringComparison]::Ordinal)) {
        $text += "`n"
    }
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($text)
    return ,$bytes
}

function New-SequentialMap {
    param(
        [object[]]$Values,
        [string]$Prefix
    )
    $map = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($candidate in @($Values)) {
        $value = [string]$candidate
        if ([string]::IsNullOrWhiteSpace($value) -or $map.ContainsKey($value)) {
            continue
        }
        $map.Add($value, ('{0}{1:D3}' -f $Prefix, ($map.Count + 1)))
    }
    return ,$map
}

function Get-MappedValue {
    param(
        [Parameter(Mandatory = $true)]$Map,
        [string]$Value,
        [switch]$Required
    )
    if ([string]::IsNullOrWhiteSpace($Value)) {
        if ($Required) { throw 'A required fixture identity is empty.' }
        return $Value
    }
    if ($Map.ContainsKey($Value)) {
        return $Map[$Value]
    }
    if ($Required) {
        throw 'A required fixture identity is absent from the deterministic replacement map.'
    }
    return $Value
}

function Get-JsonStringRecords {
    param(
        [object]$Node,
        [string]$Path,
        [Parameter(Mandatory = $true)]$Target
    )
    if ($null -eq $Node) { return }
    if ($Node -is [string]) {
        $Target.Add([pscustomobject]@{ Path = $Path; Value = [string]$Node })
        return
    }
    if ($Node -is [System.ValueType]) { return }
    if ($Node -is [System.Collections.IDictionary]) {
        foreach ($key in $Node.Keys) {
            $childPath = if ([string]::IsNullOrEmpty($Path)) {
                [string]$key
            }
            else { $Path + '.' + [string]$key }
            Get-JsonStringRecords -Node $Node[$key] -Path $childPath -Target $Target
        }
        return
    }
    if ($Node -is [System.Array]) {
        $index = 0
        foreach ($item in $Node) {
            Get-JsonStringRecords -Node $item -Path ($Path + '[' + $index + ']') -Target $Target
            $index++
        }
        return
    }
    if ($Node -is [System.Management.Automation.PSCustomObject]) {
        foreach ($property in $Node.PSObject.Properties) {
            $childPath = if ([string]::IsNullOrEmpty($Path)) {
                $property.Name
            }
            else { $Path + '.' + $property.Name }
            Get-JsonStringRecords -Node $property.Value -Path $childPath -Target $Target
        }
    }
}

function Assert-NoShareableSecretPatterns {
    param([Parameter(Mandatory = $true)]$Value)
    $records = New-Object System.Collections.Generic.List[object]
    Get-JsonStringRecords -Node $Value -Path '' -Target $records
    $nonempty = @($records | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Value) })
    if (@($nonempty | Where-Object { $_.Value -match '^(?:[A-Za-z]:[\\/]|\\\\)' }).Count -ne 0 -or
        @($nonempty | Where-Object { $_.Value -match '(?i)(?:^|[\\/])Users[\\/][^\\/]+' }).Count -ne 0 -or
        @($nonempty | Where-Object { $_.Value -match '^[^\s@]+@[^\s@]+\.[^\s@]+$' }).Count -ne 0 -or
        @($nonempty | Where-Object { $_.Value -match '^7656\d{13}$' }).Count -ne 0 -or
        @($nonempty | Where-Object { $_.Value -match '^eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$' }).Count -ne 0 -or
        @($nonempty | Where-Object { $_.Value -match 'COOPSPECTATOR_(?:AUTOMATION_RUN_TOKEN|SERVER_PASSWORD)' }).Count -ne 0) {
        throw 'The sanitized derivative still contains a forbidden path, account, credential, or token pattern.'
    }
}

function Assert-EmptyOrMappedPrefix {
    param(
        [string]$Value,
        [string]$Prefix,
        [string]$FieldName
    )
    if (-not [string]::IsNullOrWhiteSpace($Value) -and
        -not $Value.StartsWith($Prefix, [StringComparison]::Ordinal)) {
        throw ($FieldName + ' was not replaced with a deterministic fixture identity.')
    }
}

$normalizedExpectedHash = $ExpectedSourcePayloadSha256.Trim().ToUpperInvariant()
if (-not (Test-Sha256Hex -Value $normalizedExpectedHash) -or
    $normalizedExpectedHash -ne $supportedSourcePayloadSha256) {
    throw 'This sanitizer supports only the independently reviewed m3b-live-capture-02 source payload SHA-256.'
}

$sourceRootFull = [IO.Path]::GetFullPath($SourceFixtureRoot)
$outputRootFull = [IO.Path]::GetFullPath($OutputRoot)
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if ([string]::Equals($sourceRootFull, $outputRootFull, [StringComparison]::OrdinalIgnoreCase) -or
    $outputRootFull.StartsWith($sourceRootFull.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The sanitized output must not overwrite or be created below the private source fixture.'
}
if (-not $AllowRepositoryOutput -and
    ($outputRootFull.Equals($repositoryRoot, [StringComparison]::OrdinalIgnoreCase) -or
     $outputRootFull.StartsWith($repositoryRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase))) {
    throw 'Repository output requires the explicit -AllowRepositoryOutput switch after temporary privacy validation.'
}
if ([IO.Directory]::Exists($outputRootFull) -and
    @(Get-ChildItem -LiteralPath $outputRootFull -Force).Count -ne 0) {
    throw 'The sanitized output directory must be absent or empty.'
}

$sourcePayloadPath = Join-Path $sourceRootFull 'battle_roster.raw.json'
$sourceMetadataPath = Join-Path $sourceRootFull 'fixture.metadata.json'
if (-not [IO.File]::Exists($sourcePayloadPath) -or -not [IO.File]::Exists($sourceMetadataPath)) {
    throw 'The private source payload or metadata is missing.'
}
$sourcePayloadItem = Get-Item -LiteralPath $sourcePayloadPath
$sourcePayloadHash = Get-FileSha256 -Path $sourcePayloadPath
if ($sourcePayloadHash -ne $supportedSourcePayloadSha256) {
    throw 'The private source payload bytes do not match the independently reviewed SHA-256.'
}

$sourceMetadata = Get-Content -LiteralPath $sourceMetadataPath -Raw | ConvertFrom-Json
if ([string]$sourceMetadata.PayloadSha256 -ne $sourcePayloadHash -or
    [long]$sourceMetadata.PayloadLength -ne $sourcePayloadItem.Length -or
    [string]$sourceMetadata.ScenarioKind -ne 'FieldBattle' -or
    [string]$sourceMetadata.BattleStage -ne 'PreMissionCampaignRoster' -or
    [string]$sourceMetadata.SanitizationStatus -ne 'UnreviewedPrivateRunArtifact') {
    throw 'The private source metadata does not match the reviewed field-capture identity.'
}

$fixture = Get-Content -LiteralPath $sourcePayloadPath -Raw | ConvertFrom-Json
if (@($fixture.PSObject.Properties.Name).Count -ne 2 -or
    $null -eq $fixture.Snapshot -or
    [string]$fixture.Snapshot.ScenarioContext.ScenarioKind -ne 'FieldBattle' -or
    [string]$fixture.Snapshot.ScenarioContext.CampaignBattleType -ne 'FieldBattle' -or
    [bool]$fixture.Snapshot.ScenarioContext.IsSiegeBattle -or
    @($fixture.Snapshot.Sides).Count -ne 2 -or
    @($fixture.Snapshot.CraftedWeapons).Count -ne 0 -or
    @($fixture.Snapshot.FrozenCaptainEntryIds).Count -ne 0 -or
    @($fixture.Snapshot.FrozenCaptainCombatGroups).Count -ne 0) {
    throw 'The source payload no longer matches the reviewed first ordinary-field slice.'
}

$sides = @($fixture.Snapshot.Sides)
$parties = @($sides | ForEach-Object { @($_.Parties) })
$troops = @($sides | ForEach-Object { @($_.Troops) })
$positiveTroops = @($troops | Where-Object { $null -ne $_ -and [int]$_.Count -gt 0 })
if ($positiveTroops.Count -ne 47 -or
    ($positiveTroops | Measure-Object -Property Count -Sum).Sum -ne 74 -or
    @($positiveTroops | Where-Object { [bool]$_.IsMounted }).Count -ne 17 -or
    @($positiveTroops | Where-Object { [bool]$_.IsHero }).Count -ne 4) {
    throw 'The source payload no longer matches the independently reviewed field counts.'
}

$sideMap = New-SequentialMap -Values @($sides | ForEach-Object { [string]$_.SideId }) -Prefix 'fixture-side-'
$partyMap = New-SequentialMap -Values @($parties | ForEach-Object { [string]$_.PartyId }) -Prefix 'fixture-party-'
$combatGroupMap = New-SequentialMap -Values @($parties | ForEach-Object { [string]$_.CombatGroupId }) -Prefix 'fixture-combat-group-'
$entryMap = New-SequentialMap -Values @($troops | ForEach-Object { [string]$_.EntryId }) -Prefix 'fixture-entry-'
$heroTroops = @($troops | Where-Object {
        [bool]$_.IsHero -or
        -not [string]::IsNullOrWhiteSpace([string]$_.HeroId) -or
        [bool]$_.IsPlayerCharacter -or
        [bool]$_.IsPlayerClanHero
    })
$modifierHeroProperties = @('LeaderHeroId', 'OwnerHeroId', 'ScoutHeroId', 'QuartermasterHeroId', 'EngineerHeroId', 'SurgeonHeroId')
$heroIds = New-Object System.Collections.Generic.List[object]
foreach ($troop in $troops) { $heroIds.Add([string]$troop.HeroId) }
foreach ($party in $parties) {
    foreach ($propertyName in $modifierHeroProperties) {
        $heroIds.Add([string]$party.Modifiers.$propertyName)
    }
}
$heroMap = New-SequentialMap -Values $heroIds.ToArray() -Prefix 'fixture-hero-'
$heroClanMap = New-SequentialMap -Values @($heroTroops | ForEach-Object { [string]$_.HeroClanId }) -Prefix 'fixture-clan-'
$heroCharacterValues = New-Object System.Collections.Generic.List[object]
foreach ($troop in $heroTroops) {
    $heroCharacterValues.Add([string]$troop.CharacterId)
    $heroCharacterValues.Add([string]$troop.OriginalCharacterId)
}
$heroCharacterMap = New-SequentialMap -Values $heroCharacterValues.ToArray() -Prefix 'fixture-hero-character-'

$fixture.Snapshot.CampaignId = 'fixture-campaign-001'
$fixture.Snapshot.BattleId = 'fixture-battle-001'
$fixture.Snapshot.BattleInstanceId = 'fixture-battle-instance-001'
$fixture.Snapshot.PlayerSide = Get-MappedValue -Map $sideMap -Value ([string]$fixture.Snapshot.PlayerSide) -Required
$fixture.TroopIds = @($fixture.TroopIds | ForEach-Object {
        Get-MappedValue -Map $heroCharacterMap -Value ([string]$_)
    })

$troopOrdinal = 0
for ($sideIndex = 0; $sideIndex -lt $sides.Count; $sideIndex++) {
    $side = $sides[$sideIndex]
    $side.SideId = Get-MappedValue -Map $sideMap -Value ([string]$side.SideId) -Required
    $side.SideText = if ([bool]$side.IsPlayerSide) { 'Fixture Player Side' } else { 'Fixture Opponent Side' }
    $side.LeaderPartyId = Get-MappedValue -Map $partyMap -Value ([string]$side.LeaderPartyId) -Required
    $side.Color = $canonicalColors[$sideIndex].Color
    $side.Color2 = $canonicalColors[$sideIndex].Color2
    $side.BannerCode = $canonicalBannerCodes[$sideIndex]
    $side.AppearanceSource = $sanitizationPolicy
    $side.MissionReadyEntryOrder = @($side.MissionReadyEntryOrder | ForEach-Object {
            Get-MappedValue -Map $entryMap -Value ([string]$_) -Required
        })

    foreach ($party in @($side.Parties)) {
        $party.PartyId = Get-MappedValue -Map $partyMap -Value ([string]$party.PartyId) -Required
        $party.PartyName = 'Fixture Party {0:D2}' -f ([array]::IndexOf($parties, $party) + 1)
        $party.CombatGroupId = Get-MappedValue -Map $combatGroupMap -Value ([string]$party.CombatGroupId)
        foreach ($propertyName in $modifierHeroProperties) {
            $party.Modifiers.$propertyName = Get-MappedValue -Map $heroMap -Value ([string]$party.Modifiers.$propertyName)
        }
    }

    foreach ($troop in @($side.Troops)) {
        $troopOrdinal++
        $troop.EntryId = Get-MappedValue -Map $entryMap -Value ([string]$troop.EntryId) -Required
        $troop.SideId = Get-MappedValue -Map $sideMap -Value ([string]$troop.SideId) -Required
        $troop.PartyId = Get-MappedValue -Map $partyMap -Value ([string]$troop.PartyId) -Required
        $troop.TroopName = 'Fixture Troop {0:D3}' -f $troopOrdinal
        $troop.CharacterId = Get-MappedValue -Map $heroCharacterMap -Value ([string]$troop.CharacterId)
        $troop.OriginalCharacterId = Get-MappedValue -Map $heroCharacterMap -Value ([string]$troop.OriginalCharacterId)
        $troop.SpawnTemplateId = Get-MappedValue -Map $heroCharacterMap -Value ([string]$troop.SpawnTemplateId)
        $troop.HeroTemplateId = Get-MappedValue -Map $heroCharacterMap -Value ([string]$troop.HeroTemplateId)
        $troop.HeroId = Get-MappedValue -Map $heroMap -Value ([string]$troop.HeroId)
        $troop.HeroClanId = Get-MappedValue -Map $heroClanMap -Value ([string]$troop.HeroClanId)
        if (-not [string]::IsNullOrWhiteSpace([string]$troop.HeroBodyProperties)) {
            $troop.HeroBodyProperties = $canonicalHeroBodyProperties
        }
    }
}

foreach ($party in $parties) {
    foreach ($troop in @($party.Troops)) {
        $troop.EntryId = Get-MappedValue -Map $entryMap -Value ([string]$troop.EntryId) -Required
        $troop.SideId = Get-MappedValue -Map $sideMap -Value ([string]$troop.SideId) -Required
        $troop.PartyId = Get-MappedValue -Map $partyMap -Value ([string]$troop.PartyId) -Required
        $sideTroop = @($troops | Where-Object { [string]$_.EntryId -eq [string]$troop.EntryId }) | Select-Object -First 1
        if ($null -eq $sideTroop) {
            throw 'A party troop no longer resolves to the sanitized side entry.'
        }
        foreach ($property in $sideTroop.PSObject.Properties) {
            $troop.($property.Name) = $property.Value
        }
    }
}

foreach ($side in $sides) {
    Assert-EmptyOrMappedPrefix -Value ([string]$side.SideId) -Prefix 'fixture-side-' -FieldName 'SideId'
    Assert-EmptyOrMappedPrefix -Value ([string]$side.LeaderPartyId) -Prefix 'fixture-party-' -FieldName 'LeaderPartyId'
    foreach ($party in @($side.Parties)) {
        Assert-EmptyOrMappedPrefix -Value ([string]$party.PartyId) -Prefix 'fixture-party-' -FieldName 'PartyId'
        Assert-EmptyOrMappedPrefix -Value ([string]$party.CombatGroupId) -Prefix 'fixture-combat-group-' -FieldName 'CombatGroupId'
    }
    foreach ($troop in @($side.Troops)) {
        Assert-EmptyOrMappedPrefix -Value ([string]$troop.EntryId) -Prefix 'fixture-entry-' -FieldName 'EntryId'
        Assert-EmptyOrMappedPrefix -Value ([string]$troop.SideId) -Prefix 'fixture-side-' -FieldName 'Troop.SideId'
        Assert-EmptyOrMappedPrefix -Value ([string]$troop.PartyId) -Prefix 'fixture-party-' -FieldName 'Troop.PartyId'
        Assert-EmptyOrMappedPrefix -Value ([string]$troop.HeroId) -Prefix 'fixture-hero-' -FieldName 'HeroId'
        Assert-EmptyOrMappedPrefix -Value ([string]$troop.HeroClanId) -Prefix 'fixture-clan-' -FieldName 'HeroClanId'
        if (-not ([string]$troop.TroopName).StartsWith('Fixture Troop ', [StringComparison]::Ordinal)) {
            throw 'A troop display name was not sanitized.'
        }
    }
}
Assert-NoShareableSecretPatterns -Value $fixture

$payloadBytes = ConvertTo-DeterministicJsonBytes -Value $fixture
$payloadSha256 = Get-BytesSha256 -Bytes $payloadBytes
$sanitizedMetadata = [ordered]@{
    SchemaVersion = 1
    FixtureId = 'field-current-sanitized-v1'
    DerivativeKind = 'SanitizedCampaignRoster'
    PayloadKind = [string]$sourceMetadata.PayloadKind
    Boundary = [string]$sourceMetadata.Boundary
    SourceRole = [string]$sourceMetadata.SourceRole
    TargetRole = [string]$sourceMetadata.TargetRole
    Encoding = 'UTF-8-no-BOM'
    Compression = 'None'
    PayloadSchema = [string]$sourceMetadata.PayloadSchema
    CompatibilityStatus = [string]$sourceMetadata.CompatibilityStatus
    PayloadFile = $payloadFileName
    PayloadLength = $payloadBytes.LongLength
    PayloadSha256 = $payloadSha256
    SourcePrivatePayloadLength = $sourcePayloadItem.Length
    SourcePrivatePayloadSha256 = $sourcePayloadHash
    SourceCaptureRunId = [string]$sourceMetadata.RunId
    SourceRevision = [string]$sourceMetadata.SourceRevision
    ModuleVersion = [string]$sourceMetadata.ModuleVersion
    ModuleFileName = [string]$sourceMetadata.ModuleFileName
    ModuleSha256 = [string]$sourceMetadata.ModuleSha256
    GameVersion = [string]$sourceMetadata.GameVersion
    ScenarioKind = 'FieldBattle'
    BattleStage = 'PreMissionCampaignRoster'
    SanitizationPolicy = $sanitizationPolicy
    SanitizationStatus = 'ReviewedSanitizedDerivative'
    PrivacyReviewStatus = 'NoRawCampaignAccountPathOrCredentialValues'
    IndependentOracleStatus = 'IndependentAuditRequired'
    OracleFile = $oracleFileName
    FullBattleCompleted = $false
    L2OrL3PassClaimed = $false
}
Assert-NoShareableSecretPatterns -Value $sanitizedMetadata
$metadataBytes = ConvertTo-DeterministicJsonBytes -Value $sanitizedMetadata

[IO.Directory]::CreateDirectory($outputRootFull) | Out-Null
[IO.File]::WriteAllBytes((Join-Path $outputRootFull $payloadFileName), $payloadBytes)
[IO.File]::WriteAllBytes((Join-Path $outputRootFull $metadataFileName), $metadataBytes)

[pscustomobject]@{
    State = 'Sanitized'
    Policy = $sanitizationPolicy
    SourcePayloadSha256 = $sourcePayloadHash
    SanitizedPayloadLength = $payloadBytes.LongLength
    SanitizedPayloadSha256 = $payloadSha256
    OutputFileCount = 2
    RawPayloadCopied = $false
    LogsCopied = $false
    OracleGenerated = $false
} | ConvertTo-Json -Compress
