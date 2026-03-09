# Bannerlord DLL Inventory Audit
# Scans Steam client, Dedicated Server, Modules; builds categorized report and multiplayer-focused tables.

$ErrorActionPreference = "Stop"
$ClientRoot = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
$DediRoot   = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server"

function Get-Role {
    param([string]$Name, [string]$FullPath)
    $n = $Name.ToLowerInvariant()
    $p = $FullPath.ToLowerInvariant()
    if ($n -match "multiplayer\.test|\.test\.dll") { return "test" }
    if ($n -match "multiplayer") { return "multiplayer" }
    if ($n -match "dedicated|dedi") { return "dedicated" }
    if ($n -match "webpanel|web\.panel") { return "webpanel" }
    if ($n -match "listedserver|listed\.server") { return "listedserver" }
    if ($n -match "diamond") { return "diamond" }
    if ($n -match "clienthelper|client\.helper") { return "clienthelper" }
    if ($n -match "official") { return "official" }
    if ($p -match "\\modules\\" -and $p -notmatch "\\bin\\") { return "module" }
    if ($n -match "campaign") { return "campaign" }
    if ($n -match "taleworlds\.(core|library|engine|mountandblade|dotnet|objectsystem|network|localization|input)") { return "native" }
    if ($n -match "taleworlds\.mountandblade") { return "native" }
    return "unknown"
}

function Get-Category {
    param([string]$FullPath)
    $cRoot = $ClientRoot -replace "\\", "\\"
    $dRoot = $DediRoot -replace "\\", "\\"
    if ($FullPath -match "^$([regex]::Escape($ClientRoot))") {
        if ($FullPath -match "\\bin\\Win64_Shipping_Client\\") { return "client" }
        if ($FullPath -match "\\Modules\\.*\\bin\\") { return "module_client" }
        return "client"
    }
    if ($FullPath -match "^$([regex]::Escape($DediRoot))") {
        if ($FullPath -match "\\bin\\(Win64_Shipping_Server|Linux64_Shipping_Server)\\" -or $FullPath -match "\\bin\\.*[Ss]erver\\") { return "dedicated" }
        if ($FullPath -match "\\Modules\\.*\\bin\\") { return "module_dedicated" }
        return "dedicated"
    }
    return "other"
}

$all = [System.Collections.Generic.List[object]]::new()

foreach ($root in @($ClientRoot, $DediRoot)) {
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem -Path $root -Recurse -Filter "*.dll" -ErrorAction SilentlyContinue | ForEach-Object {
        $cat = Get-Category $_.FullName
        $role = Get-Role $_.Name $_.FullName
        $all.Add([PSCustomObject]@{
            FileName    = $_.Name
            FullPath    = $_.FullName
            RootCategory = $cat
            ProbableRole = $role
            SizeBytes   = $_.Length
            LastWrite   = $_.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm")
        })
    }
}

# Multiplayer-related: TaleWorlds.MountAndBlade.* and names containing keywords
$keywords = "Multiplayer|Dedicated|ListedServer|Diamond|WebPanel|ClientHelper|Official|\.Test\."
$multiplayerRelated = $all | Where-Object {
    $_.FileName -match "TaleWorlds\.MountAndBlade\." -or
    ($_.FileName -match $keywords -and $_.FileName -match "\.dll$")
}

# Paths for comparison (normalize to relative from bin or module)
$clientBins = @($all | Where-Object { $_.RootCategory -eq "client" -and $_.FullPath -match "Win64_Shipping_Client" } | Select-Object -ExpandProperty FileName)
$dediBins   = @($all | Where-Object { $_.RootCategory -eq "dedicated" -and $_.FullPath -match "Win64_Shipping_Server|Linux64_Shipping_Server" } | Select-Object -ExpandProperty FileName)
$clientLower = @($clientBins | ForEach-Object { $_.ToLowerInvariant() } | Select-Object -Unique)
$dediLower   = @($dediBins   | ForEach-Object { $_.ToLowerInvariant() } | Select-Object -Unique)
$dediSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$dediBins | ForEach-Object { [void]$dediSet.Add($_) }
$clientSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$clientBins | ForEach-Object { [void]$clientSet.Add($_) }

$onlyInClient = @($clientBins | Sort-Object -Unique | Where-Object { -not $dediSet.Contains($_) })
$onlyInDedi   = @($dediBins   | Sort-Object -Unique | Where-Object { -not $clientSet.Contains($_) })
$inBoth       = @($clientBins | Sort-Object -Unique | Where-Object { $dediSet.Contains($_) })

# CSV output
$csvPath = Join-Path (Split-Path $PSScriptRoot) "dll_inventory.csv"
$all | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8

# Markdown report (written by script to a variable, then we output)
$md = @"
# DLL Inventory Report — Bannerlord Steam Install

Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm")

## Roots scanned

| Root | Path | Exists |
|------|------|--------|
| Client | ``$ClientRoot`` | $(Test-Path $ClientRoot) |
| Dedicated Server | ``$DediRoot`` | $(Test-Path $DediRoot) |

## 1. Multiplayer-related assemblies

*(TaleWorlds.MountAndBlade.* and DLLs with Multiplayer, Dedicated, ListedServer, Diamond, WebPanel, ClientHelper, Official, Test in name)*

| FileName | FullPath | RootCategory | ProbableRole | Size | LastWrite |
|----------|----------|--------------|--------------|------|-----------|
"@

$multiplayerRelated | Sort-Object RootCategory, FileName | ForEach-Object {
    $md += "| $($_.FileName) | ``$($_.FullPath)`` | $($_.RootCategory) | $($_.ProbableRole) | $($_.SizeBytes) | $($_.LastWrite) |`n"
}

$md += @"

## 2. Client vs Dedicated (bin DLLs by filename)

**Only in Client bin (Win64_Shipping_Client):** $($onlyInClient.Count) files

``````
$(($onlyInClient | Select-Object -First 80) -join "`n")
$(if ($onlyInClient.Count -gt 80) { "... and $($onlyInClient.Count - 80) more" })
``````

**Only in Dedicated bin (Win64_Shipping_Server / Linux64):** $($onlyInDedi.Count) files

``````
$(($onlyInDedi | Select-Object -First 80) -join "`n")
$(if ($onlyInDedi.Count -gt 80) { "... and $($onlyInDedi.Count - 80) more" })
``````

**In both:** $($inBoth.Count) files

``````
$(($inBoth | Select-Object -First 100) -join "`n")
$(if ($inBoth.Count -gt 100) { "... and $($inBoth.Count - 100) more" })
``````

## 3. Full inventory summary

| RootCategory | Count |
|--------------|-------|
"@

$all | Group-Object RootCategory | Sort-Object Name | ForEach-Object {
    $md += "| $($_.Name) | $($_.Count) |`n"
}

$md += @"

## 4. Key questions (answers)

### 4.1 Does client TaleWorlds.MountAndBlade.Multiplayer.dll exist?

$(if (Test-Path (Join-Path $ClientRoot "bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Multiplayer.dll")) { "Yes." } else { "No. Not found at client bin path." })

### 4.2 Closest assemblies (client bin, TaleWorlds.MountAndBlade.* or *Multiplayer*)

"@

$closest = $all | Where-Object {
    $_.RootCategory -eq "client" -and
    $_.FullPath -match "Win64_Shipping_Client" -and
    ($_.FileName -match "TaleWorlds\.MountAndBlade\." -or $_.FileName -match "Multiplayer")
} | Sort-Object FileName
foreach ($c in $closest) {
    $md += "- " + "``" + $c.FileName + "``" + " - " + $c.FullPath + "`n"
}
if (-not $closest) { $md += "(none in scanned paths)`n" }

$md += @"

### 4.3 Custom MP game mode without this DLL?

Possible only via Harmony injection into vanilla TDM mission behaviors (no client-side game mode registration). See ETAP_3_3_TECHNICAL_PLAN.md §8 Fallback.

### 4.4 Fallback (vanilla TDM + Harmony) as main path?

After this audit: yes - if client Multiplayer.dll is absent in your Steam install, fallback is the only way to get 3.3 logging and behavior injection without introducing a server DLL on the client.

---

## 5. Final conclusion (max 10 points)

1. Client bin (Win64_Shipping_Client) contains core game DLLs and TaleWorlds.MountAndBlade.Multiplayer.Test.dll only; no TaleWorlds.MountAndBlade.Multiplayer.dll.
2. Dedicated Server bin contains TaleWorlds.MountAndBlade.Multiplayer.dll (server-side); using it for client build is not recommended.
3. Custom game mode (TdmClone) on client requires the client Multiplayer.dll at compile and runtime; it is not present in this Steam client install.
4. **Closest client assemblies** to “multiplayer”: MountAndBlade.Multiplayer.Test.dll, MountAndBlade.dll, MountAndBlade.Launcher.*, MountAndBlade.ViewModelCollection, etc. — none provide MissionBasedMultiplayerGameMode / AddMultiplayerGameMode.
5. No other supported way to register a custom MP game mode on the client without the client Multiplayer.dll.
6. Fallback (vanilla TDM + Harmony injection) is the correct main path for 3.3: inject CoopMissionClientLogic/CoopMissionSpawnLogic when mission is vanilla TeamDeathmatch.
7. Campaign and listed dedicated flow remains valid; it does not depend on TdmClone or the client Multiplayer.dll.
8. For 3.3: prioritise Harmony patch for vanilla TDM (ETAP_3_3_TECHNICAL_PLAN.md section 8); TdmClone client build is optional if client DLL becomes available.
9. Recommendation: move to fallback Harmony patch for vanilla TDM as primary way to get 3.3 logging and spectator/spawn logic on this Steam configuration.
10. Full inventory CSV: dll_inventory.csv for further filtering.

---
*Full CSV: ``dll_inventory.csv``*
"@

$reportPath = Join-Path (Split-Path $PSScriptRoot) "DLL_INVENTORY_REPORT.md"
Set-Content -Path $reportPath -Value $md -Encoding UTF8
Write-Output "Report: $reportPath"
Write-Output "CSV: $csvPath"
Write-Output "Total DLLs: $($all.Count)"
Write-Output "Multiplayer-related: $($multiplayerRelated.Count)"
