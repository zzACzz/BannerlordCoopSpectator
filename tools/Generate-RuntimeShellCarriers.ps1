param(
    [string]$AuditPath = "C:\Users\Admin\OneDrive\Documents\Mount and Blade II Bannerlord\CoopSpectator\campaign_shell_audit_latest.json",
    [string]$ExistingRuntimeCharactersPath = "C:\dev\projects\BannerlordCoopSpectator3\Module\CoopSpectator\ModuleData\coopspectator_mpcharacters.xml",
    [string]$OutputPath = "C:\dev\projects\BannerlordCoopSpectator3\Module\CoopSpectator\ModuleData\coopspectator_generated_runtime_mpcharacters.xml",
    [string]$ManifestOutputPath = "C:\dev\projects\BannerlordCoopSpectator3\Module\CoopSpectator\ModuleData\coopspectator_generated_runtime_shell_manifest.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Normalize-ShellToken {
    param(
        [AllowNull()]
        [string]$Token,
        [AllowNull()]
        [string]$Fallback
    )

    if ([string]::IsNullOrWhiteSpace($Token)) {
        return $Fallback
    }

    switch ($Token) {
        "OneHandedSword" { return "1hsword" }
        "OneHandedAxe" { return "1haxe" }
        "OneHandedPolearm" { return "polearm" }
        "Mace" { return "mace" }
        "TwoHandedAxe" { return "2haxe" }
        "TwoHandedPolearm" { return "2hpolearm" }
        "TwoHandedSword" { return "2hsword" }
        "TwoHandedMace" { return "2hmace" }
        "Dagger" { return "dagger" }
        "Bow" { return "bow" }
        "Crossbow" { return "crossbow" }
        "Javelin" { return "javelin" }
        "ThrowingAxe" { return "throwingaxe" }
        "ThrowingKnife" { return "throwingknife" }
        "Stone" { return "stone" }
        "Sling" { return "sling" }
        default { return $Token.ToLowerInvariant().Replace(" ", "") }
    }
}

function Get-ShortSignatureHash {
    param([string]$Key)

    if ([string]::IsNullOrWhiteSpace($Key)) {
        return "nosig"
    }

    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Key)
        $hashBytes = $sha1.ComputeHash($bytes)
        return -join ($hashBytes | Select-Object -First 4 | ForEach-Object { $_.ToString("x2") })
    }
    finally {
        $sha1.Dispose()
    }
}

function Get-VariantIdentityKey {
    param($Variant)

    if ($null -eq $Variant) {
        return "null-variant"
    }

    if (-not [string]::IsNullOrWhiteSpace($Variant.VariantSignature)) {
        return "variant:" + $Variant.VariantSignature.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($Variant.RuntimeSignatureKey)) {
        return "runtime:" + $Variant.RuntimeSignatureKey.Trim()
    }

    return "fallback:" + ([Guid]::NewGuid().ToString("N"))
}

function Get-ShellId {
    param($Variant)

    $mountToken = if ($Variant.IsMounted) { "mounted" } else { "foot" }
    $shieldToken = if ($Variant.HasShield) { "shield" } else { "no_shield" }
    $signatureHash = Get-ShortSignatureHash (Get-VariantIdentityKey $Variant)
    $primaryMeleeToken = Normalize-ShellToken $Variant.PrimaryMeleeWeaponClass "unarmed"
    $secondaryMeleeToken = Normalize-ShellToken $Variant.SecondaryMeleeWeaponClass $null
    $horseToken = if ($Variant.IsMounted) { Normalize-ShellToken $Variant.CombatHorseId "horse" } else { $null }

    $combatPrefix = switch ($Variant.RangedFamily) {
        "Bow" { "bow" }
        "Crossbow" { "crossbow" }
        "Thrown" { "thrown_" + (Normalize-ShellToken $Variant.PrimaryRangedWeaponClass "javelin") }
        default { "melee" }
    }

    $resolvedShellId = "mp_coop_{0}_{1}_{2}" -f $mountToken, $combatPrefix, $primaryMeleeToken
    if (-not [string]::IsNullOrWhiteSpace($secondaryMeleeToken)) {
        $resolvedShellId += "_" + $secondaryMeleeToken
    }
    if (-not [string]::IsNullOrWhiteSpace($horseToken)) {
        $resolvedShellId += "_" + $horseToken
    }

    $resolvedShellId += "_" + $signatureHash

    return $resolvedShellId + "_" + $shieldToken
}

function Get-DefaultGroup {
    param($Variant)

    if ($Variant.IsMounted) {
        if ($Variant.RangedFamily -eq "Bow") {
            return "HorseArcher"
        }

        return "Cavalry"
    }

    if ($Variant.RangedFamily -eq "Bow" -or $Variant.RangedFamily -eq "Crossbow") {
        return "Ranged"
    }

    return "Infantry"
}

function Get-Level {
    param($Variant)

    if ($Variant.IsMounted -or $Variant.RangedFamily -eq "Bow" -or $Variant.RangedFamily -eq "Crossbow") {
        return 22
    }

    return 18
}

function Get-Resistance {
    param($Variant)

    if ($Variant.IsMounted) {
        return 75
    }

    return 25
}

function Ensure-ItemPrefix {
    param([AllowNull()][string]$ItemId)

    if ([string]::IsNullOrWhiteSpace($ItemId)) {
        return $null
    }

    if ($ItemId.StartsWith("Item.")) {
        return $ItemId
    }

    return "Item." + $ItemId
}

function Parse-VariantSignatureParts {
    param([string]$VariantSignature)

    $parts = @($VariantSignature -split '\|', 15)
    while ($parts.Count -lt 15) {
        $parts += ""
    }

    return [ordered]@{
        Head = Ensure-ItemPrefix $parts[8]
        Body = Ensure-ItemPrefix $parts[9]
        Leg = Ensure-ItemPrefix $parts[10]
        Gloves = Ensure-ItemPrefix $parts[11]
        Cape = Ensure-ItemPrefix $parts[12]
        Horse = Ensure-ItemPrefix $parts[13]
        HorseHarness = Ensure-ItemPrefix $parts[14]
    }
}

function Get-Skills {
    param($Variant)

    $skills = [ordered]@{
        Riding = if ($Variant.IsMounted) { 180 } else { 25 }
        OneHanded = 45
        TwoHanded = 45
        Polearm = 45
        Crossbow = 20
        Bow = 20
        Throwing = 20
    }

    switch ($Variant.RangedFamily) {
        "Bow" { $skills.Bow = 220 }
        "Crossbow" { $skills.Crossbow = 220 }
        "Thrown" { $skills.Throwing = 180 }
    }

    $primaryMeleeToken = Normalize-ShellToken $Variant.PrimaryMeleeWeaponClass "unarmed"
    $secondaryMeleeToken = Normalize-ShellToken $Variant.SecondaryMeleeWeaponClass $null

    $oneHandedTokens = @("1hsword", "1haxe", "mace", "dagger")
    $polearmTokens = @("polearm", "2hpolearm")
    $twoHandedTokens = @("2haxe", "2hsword", "2hmace")

    if ($oneHandedTokens -contains $primaryMeleeToken) { $skills.OneHanded = [Math]::Max($skills.OneHanded, 180) }
    if ($oneHandedTokens -contains $secondaryMeleeToken) { $skills.OneHanded = [Math]::Max($skills.OneHanded, 150) }
    if ($polearmTokens -contains $primaryMeleeToken) { $skills.Polearm = [Math]::Max($skills.Polearm, 180) }
    if ($polearmTokens -contains $secondaryMeleeToken) { $skills.Polearm = [Math]::Max($skills.Polearm, 150) }
    if ($twoHandedTokens -contains $primaryMeleeToken) { $skills.TwoHanded = [Math]::Max($skills.TwoHanded, 180) }
    if ($twoHandedTokens -contains $secondaryMeleeToken) { $skills.TwoHanded = [Math]::Max($skills.TwoHanded, 150) }

    return $skills
}

function Get-ShellDisplayName {
    param([string]$ShellBaseId)

    $label = $ShellBaseId.Replace("mp_coop_", "").Replace("_", " ")
    $label = (Get-Culture).TextInfo.ToTitleCase($label)
    return "{=!}" + $label + " Shell"
}

function Write-EquipmentSlot {
    param(
        [System.Text.StringBuilder]$Builder,
        [string]$Slot,
        [AllowNull()][string]$ItemId
    )

    if (-not [string]::IsNullOrWhiteSpace($ItemId)) {
        [void]$Builder.AppendLine(("        <equipment slot=""{0}"" id=""{1}"" />" -f $Slot, $ItemId))
    }
}

function Write-NpcCharacter {
    param(
        [System.Text.StringBuilder]$Builder,
        [string]$CharacterId,
        [string]$DisplayName,
        [string]$DefaultGroup,
        [int]$Level,
        [int]$DismountResistance,
        [hashtable]$Skills,
        [hashtable]$Equipment,
        [bool]$HeroFace
    )

    $bodyProperties = if ($HeroFace) {
        '<BodyProperties version="4" age="41.23" weight="0.3963" build="0.3951" key="00000000010003000000000000067000000000000000000000000000000000000006660606000000000000000000000000000000000000000000000001180000" />'
    } else {
        '<BodyProperties version="4" age="44.99" weight="0.3333" build="0.3333" key="00000008800000010000000000050000000000000000000000060000000070000005500505000000000000000000000000000000000000000000000000500000" />'
    }

    $bodyPropertiesMax = if ($HeroFace) {
        '<BodyPropertiesMax version="4" age="51.79" weight="0.6728" build="1" key="0005FC059A0034D0FFFFFFFF887AFFFFFFFEFFFB6FB7FFFFFFFFFFFFFFFFFFFF000FAFFA0FFFFFFF000000000000000000000000000000000000000001680141" />'
    } else {
        '<BodyPropertiesMax version="4" age="55.28" weight="0.7951" build="0.8858" key="0011A80CC03C0010FFFFFFFF887CFFFFFFFFFFFF7FF0F7FFFFFFFFFFFFFFFFFF000DCFF90DFFFFFF000000000000000000000000000000000000000001185142" />'
    }

    [void]$Builder.AppendLine(("  <NPCCharacter id=""{0}"" default_group=""{1}"" level=""{2}"" name=""{3}"" occupation=""Soldier"" culture=""Culture.empire"">" -f $CharacterId, $DefaultGroup, $Level, $DisplayName))
    [void]$Builder.AppendLine("    <face>")
    [void]$Builder.AppendLine(("      {0}" -f $bodyProperties))
    [void]$Builder.AppendLine(("      {0}" -f $bodyPropertiesMax))
    [void]$Builder.AppendLine("      <hair_tags><hair_tag name=""empire"" /></hair_tags>")
    [void]$Builder.AppendLine("      <beard_tags><beard_tag name=""empire"" /></beard_tags>")
    if ($HeroFace) {
        [void]$Builder.AppendLine("      <tattoo_tags><tattoo_tag name=""Cleanface"" /></tattoo_tags>")
    } else {
        [void]$Builder.AppendLine("      <tattoo_tags><tattoo_tag name=""Cleanface"" /><tattoo_tag name=""Scar16"" /></tattoo_tags>")
    }
    [void]$Builder.AppendLine("    </face>")
    [void]$Builder.AppendLine("    <skills>")
    foreach ($skillEntry in $Skills.GetEnumerator()) {
        [void]$Builder.AppendLine(("      <skill id=""{0}"" value=""{1}"" />" -f $skillEntry.Key, $skillEntry.Value))
    }
    [void]$Builder.AppendLine("    </skills>")
    [void]$Builder.AppendLine("    <Equipments>")
    [void]$Builder.AppendLine("      <EquipmentRoster>")
    Write-EquipmentSlot -Builder $Builder -Slot "Item0" -ItemId $Equipment.Item0
    Write-EquipmentSlot -Builder $Builder -Slot "Item1" -ItemId $Equipment.Item1
    Write-EquipmentSlot -Builder $Builder -Slot "Item2" -ItemId $Equipment.Item2
    Write-EquipmentSlot -Builder $Builder -Slot "Item3" -ItemId $Equipment.Item3
    Write-EquipmentSlot -Builder $Builder -Slot "Head" -ItemId $Equipment.Head
    Write-EquipmentSlot -Builder $Builder -Slot "Body" -ItemId $Equipment.Body
    Write-EquipmentSlot -Builder $Builder -Slot "Leg" -ItemId $Equipment.Leg
    Write-EquipmentSlot -Builder $Builder -Slot "Gloves" -ItemId $Equipment.Gloves
    Write-EquipmentSlot -Builder $Builder -Slot "Cape" -ItemId $Equipment.Cape
    Write-EquipmentSlot -Builder $Builder -Slot "Horse" -ItemId $Equipment.Horse
    Write-EquipmentSlot -Builder $Builder -Slot "HorseHarness" -ItemId $Equipment.HorseHarness
    [void]$Builder.AppendLine("      </EquipmentRoster>")
    [void]$Builder.AppendLine("    </Equipments>")
    [void]$Builder.AppendLine(("    <Resistances dismount=""{0}"" />" -f $DismountResistance))
    [void]$Builder.AppendLine("  </NPCCharacter>")
}

$audit = Get-Content -Raw $AuditPath | ConvertFrom-Json
$existingIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

if (Test-Path $ExistingRuntimeCharactersPath) {
    [xml]$existingXml = Get-Content -Raw $ExistingRuntimeCharactersPath
    foreach ($node in $existingXml.SelectNodes("//NPCCharacter[@id]")) {
        [void]$existingIds.Add($node.id)
    }
}

$variantsByIdentityKey = @{}
foreach ($variant in $audit.Variants) {
    if ($null -eq $variant) {
        continue
    }

    $variantIdentityKey = Get-VariantIdentityKey $variant
    if (-not $variantsByIdentityKey.ContainsKey($variantIdentityKey)) {
        $variantsByIdentityKey[$variantIdentityKey] = $variant
    }
}

$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$builder.AppendLine('<MPCharacters>')

$generatedCount = 0
$manifestEntries = New-Object System.Collections.Generic.List[object]
$seenManifestVariantSignatures = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$runtimeShellManifestByRuntimeSignatureKey = @{}

foreach ($entry in ($variantsByIdentityKey.GetEnumerator() | Sort-Object Name)) {
    $variant = $entry.Value
    $runtimeSignatureKey = if ([string]::IsNullOrWhiteSpace($variant.RuntimeSignatureKey)) {
        "unresolved-signature"
    }
    else {
        $variant.RuntimeSignatureKey
    }
    $shellBaseId = Get-ShellId $variant
    $heroId = $shellBaseId + "_hero"
    $troopId = $shellBaseId + "_troop"

    if (-not $runtimeShellManifestByRuntimeSignatureKey.ContainsKey($runtimeSignatureKey)) {
        $runtimeShellManifestByRuntimeSignatureKey[$runtimeSignatureKey] = [ordered]@{
            RuntimeSignatureKey = $runtimeSignatureKey
            TroopTemplateId = $troopId
            HeroTemplateId = $heroId
            IsMounted = [bool]$variant.IsMounted
            HasShield = [bool]$variant.HasShield
            HasThrown = [bool]($variant.RangedFamily -eq "Thrown")
            RangedFamily = $variant.RangedFamily
            MeleeFamily = $variant.MeleeFamily
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($variant.VariantSignature)) {
        $variantSignature = $variant.VariantSignature.Trim()
        if ($seenManifestVariantSignatures.Add($variantSignature)) {
            $manifestEntries.Add([ordered]@{
                VariantSignature = $variantSignature
                RuntimeSignatureKey = $runtimeSignatureKey
                TroopTemplateId = $troopId
                HeroTemplateId = $heroId
                IsMounted = [bool]$variant.IsMounted
                HasShield = [bool]$variant.HasShield
                HasThrown = [bool]($variant.RangedFamily -eq "Thrown")
                RangedFamily = $variant.RangedFamily
                MeleeFamily = $variant.MeleeFamily
            }) | Out-Null
        }
    }

    if ($existingIds.Contains($heroId) -or $existingIds.Contains($troopId)) {
        continue
    }

    $signatureParts = Parse-VariantSignatureParts $variant.VariantSignature
    $skills = Get-Skills $variant
    $defaultGroup = Get-DefaultGroup $variant
    $level = Get-Level $variant
    $dismount = Get-Resistance $variant
    $displayName = Get-ShellDisplayName $shellBaseId

    $equipment = [ordered]@{
        Item0 = Ensure-ItemPrefix $variant.CombatItem0Id
        Item1 = Ensure-ItemPrefix $variant.CombatItem1Id
        Item2 = Ensure-ItemPrefix $variant.CombatItem2Id
        Item3 = Ensure-ItemPrefix $variant.CombatItem3Id
        Head = $signatureParts.Head
        Body = $signatureParts.Body
        Leg = $signatureParts.Leg
        Gloves = $signatureParts.Gloves
        Cape = $signatureParts.Cape
        Horse = Ensure-ItemPrefix $variant.CombatHorseId
        HorseHarness = Ensure-ItemPrefix $variant.CombatHorseHarnessId
    }

    Write-NpcCharacter -Builder $builder -CharacterId $heroId -DisplayName $displayName -DefaultGroup $defaultGroup -Level $level -DismountResistance $dismount -Skills $skills -Equipment $equipment -HeroFace $true
    Write-NpcCharacter -Builder $builder -CharacterId $troopId -DisplayName $displayName -DefaultGroup $defaultGroup -Level $level -DismountResistance $dismount -Skills $skills -Equipment $equipment -HeroFace $false
    $generatedCount++
}

[void]$builder.AppendLine('</MPCharacters>')

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $OutputPath)) | Out-Null
[System.IO.Directory]::CreateDirectory((Split-Path -Parent $ManifestOutputPath)) | Out-Null

[System.IO.File]::WriteAllText($OutputPath, $builder.ToString(), [System.Text.UTF8Encoding]::new($true))
$manifestEntries |
    ConvertTo-Json -Depth 6 |
    Set-Content -Path $ManifestOutputPath -Encoding UTF8

Write-Output ("GeneratedRuntimeShellFamilies={0}" -f $generatedCount)
Write-Output ("OutputPath={0}" -f $OutputPath)
Write-Output ("ManifestOutputPath={0}" -f $ManifestOutputPath)
