using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    public static class CampaignMultiplayerHeroClassResolver
    {
        private const string EmpireCrossbowSurrogateTroopTemplateId = "mp_coop_crossbow_empire_troop";

        private enum RangedWeaponFamily
        {
            Unknown = 0,
            Bow,
            Crossbow
        }

        private sealed class Resolution
        {
            public MultiplayerClassDivisions.MPHeroClass HeroClass { get; set; }

            public bool TreatAsTroop { get; set; }

            public string Diagnostics { get; set; }
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Resolution> ResolutionByCharacterId =
            new Dictionary<string, Resolution>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedCharacterIds =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedBlockedContextKeys =
            new HashSet<string>(StringComparer.Ordinal);

        public static bool TryResolve(
            BasicCharacterObject character,
            out MultiplayerClassDivisions.MPHeroClass heroClass,
            out bool treatAsTroop,
            out string diagnostics)
        {
            heroClass = null;
            treatAsTroop = false;
            diagnostics = "character-null";
            if (!ExperimentalFeatures.EnableCampaignCharacterMpHeroClassFallback || character == null)
                return false;

            string characterId = character.StringId ?? string.Empty;
            if (!ShouldAllowSurrogateResolution(characterId, out string blockedContextDiagnostics))
            {
                diagnostics = blockedContextDiagnostics;
                LogBlockedContextOnce(characterId, blockedContextDiagnostics);
                return false;
            }

            bool isHero =
                TryGetBoolProperty(character, "IsHero") ||
                characterId.EndsWith("_hero", StringComparison.Ordinal);
            bool isMounted = ResolveIsMounted(character);
            bool preferClientMountedHeroTroopSurrogate =
                ShouldPreferClientMountedHeroTroopSurrogate(isHero, isMounted);
            RangedWeaponFamily rangedWeaponFamily = ResolveRangedWeaponFamily(character);
            string resolutionCacheKey =
                characterId +
                "|client-mounted-troop-surrogate=" + preferClientMountedHeroTroopSurrogate +
                "|ranged-weapon-family=" + rangedWeaponFamily;
            lock (Sync)
            {
                if (ResolutionByCharacterId.TryGetValue(resolutionCacheKey, out Resolution cachedResolution))
                {
                    heroClass = cachedResolution?.HeroClass;
                    treatAsTroop = cachedResolution?.TreatAsTroop ?? false;
                    diagnostics = cachedResolution?.Diagnostics ?? "cached-null";
                    return heroClass != null;
                }
            }

            MultiplayerClassDivisions.MPHeroClass resolvedClass = null;
            bool resolvedTreatAsTroop = false;
            string resolvedDiagnostics = "unresolved";

            try
            {
                IReadOnlyList<MultiplayerClassDivisions.MPHeroClass> heroClasses =
                    MultiplayerClassDivisions.GetMPHeroClasses()?.ToArray() ??
                    Array.Empty<MultiplayerClassDivisions.MPHeroClass>();
                if (heroClasses.Count == 0)
                {
                    resolvedDiagnostics = "hero-classes-empty";
                }
                else
                {
                    bool isRanged = ResolveIsRanged(character);
                    int tier = ResolveTier(character, isHero);
                    string cultureToken = ResolveCultureToken(character);
                    CompatibilityShellTemplateResolver.ShellProfile compatibilityShellProfile =
                        CompatibilityShellTemplateResolver.ResolveProfile(character);
                    string troopTemplateId =
                        compatibilityShellProfile?.TroopTemplateId ??
                        ResolveSurrogateTroopTemplateId(characterId, cultureToken, isMounted, isRanged, tier, rangedWeaponFamily);
                    string heroTemplateId = isHero ? TryConvertTroopTemplateToHeroTemplateId(troopTemplateId) : null;

                    List<string> candidateIds = new List<string>();
                    if (preferClientMountedHeroTroopSurrogate)
                    {
                        if (!string.IsNullOrWhiteSpace(troopTemplateId))
                            candidateIds.Add(troopTemplateId);
                        if (!string.IsNullOrWhiteSpace(heroTemplateId))
                            candidateIds.Add(heroTemplateId);
                        if (!string.IsNullOrWhiteSpace(characterId) &&
                            characterId.StartsWith("mp_", StringComparison.Ordinal))
                        {
                            candidateIds.Add(characterId);
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(characterId) &&
                            characterId.StartsWith("mp_", StringComparison.Ordinal))
                        {
                            candidateIds.Add(characterId);
                        }
                        if (!string.IsNullOrWhiteSpace(heroTemplateId))
                            candidateIds.Add(heroTemplateId);
                        if (!string.IsNullOrWhiteSpace(troopTemplateId))
                            candidateIds.Add(troopTemplateId);
                    }

                    foreach (string candidateId in candidateIds.Where(candidate => !string.IsNullOrWhiteSpace(candidate)).Distinct(StringComparer.Ordinal))
                    {
                        resolvedClass = heroClasses.FirstOrDefault(candidate =>
                            string.Equals(candidate?.HeroCharacter?.StringId, candidateId, StringComparison.Ordinal) ||
                            string.Equals(candidate?.TroopCharacter?.StringId, candidateId, StringComparison.Ordinal));
                        if (resolvedClass != null)
                            break;
                    }

                    resolvedTreatAsTroop = preferClientMountedHeroTroopSurrogate || !isHero;
                    resolvedDiagnostics =
                        "Character=" + (characterId ?? "null") +
                        " CultureToken=" + (cultureToken ?? "null") +
                        " Mounted=" + isMounted +
                        " Ranged=" + isRanged +
                        " RangedWeaponFamily=" + rangedWeaponFamily +
                        " CompatibilityShell=" + (compatibilityShellProfile?.TroopTemplateId ?? "null") +
                        " Tier=" + tier +
                        " TroopTemplate=" + (troopTemplateId ?? "null") +
                        " HeroTemplate=" + (heroTemplateId ?? "null") +
                        " ClientMountedTroopSurrogate=" + preferClientMountedHeroTroopSurrogate +
                        " TreatAsTroop=" + resolvedTreatAsTroop +
                        " ResolvedClass=" + (resolvedClass?.StringId ?? "null");
                }
            }
            catch (Exception ex)
            {
                resolvedDiagnostics = "exception:" + ex.GetType().Name + ":" + ex.Message;
            }

            var resolution = new Resolution
            {
                HeroClass = resolvedClass,
                TreatAsTroop = resolvedTreatAsTroop,
                Diagnostics = resolvedDiagnostics
            };

            lock (Sync)
            {
                ResolutionByCharacterId[resolutionCacheKey] = resolution;
            }

            if (resolvedClass != null && LoggedCharacterIds.Add(resolutionCacheKey))
            {
                ModLogger.Info(
                    "CampaignMultiplayerHeroClassResolver: mapped original campaign character to surrogate MPHeroClass. " +
                    resolvedDiagnostics);
            }

            heroClass = resolvedClass;
            treatAsTroop = resolvedTreatAsTroop;
            diagnostics = resolvedDiagnostics;
            return heroClass != null;
        }

        private static bool ShouldAllowSurrogateResolution(string characterId, out string diagnostics)
        {
            Mission mission = Mission.Current;
            string sceneName = mission?.SceneName ?? string.Empty;
            bool isBattleRuntimeScene = SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName);
            diagnostics =
                "blocked-non-battle-context" +
                " Character=" + (string.IsNullOrWhiteSpace(characterId) ? "null" : characterId) +
                " HasMission=" + (mission != null) +
                " Scene=" + (string.IsNullOrWhiteSpace(sceneName) ? "null" : sceneName) +
                " IsBattleRuntimeScene=" + isBattleRuntimeScene +
                " IsClient=" + GameNetwork.IsClient +
                " IsServer=" + GameNetwork.IsServer;
            return mission != null && isBattleRuntimeScene;
        }

        private static void LogBlockedContextOnce(string characterId, string diagnostics)
        {
            string normalizedCharacterId = string.IsNullOrWhiteSpace(characterId) ? "null" : characterId;
            string logKey = normalizedCharacterId + "|" + diagnostics;
            lock (Sync)
            {
                if (!LoggedBlockedContextKeys.Add(logKey))
                    return;
            }

            ModLogger.Info(
                "CampaignMultiplayerHeroClassResolver: skipped surrogate MPHeroClass resolution outside battle runtime. " +
                diagnostics);
        }

        private static bool ShouldPreferClientMountedHeroTroopSurrogate(bool isHero, bool isMounted)
        {
            if (!isHero || !isMounted || GameNetwork.IsServer || !GameNetwork.IsClient)
                return false;

            Mission mission = Mission.Current;
            if (mission == null)
                return false;

            string sceneName = mission.SceneName ?? string.Empty;
            return SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName) &&
                   SceneRuntimeClassifier.IsCampaignBattleScene(sceneName);
        }

        public static bool MatchesTroopClass(
            BasicCharacterObject character,
            MultiplayerClassDivisions.MPHeroClass heroClass,
            out string diagnostics)
        {
            diagnostics = "unresolved";
            if (character == null || heroClass == null)
                return false;

            if (!TryResolve(character, out MultiplayerClassDivisions.MPHeroClass resolvedClass, out bool treatAsTroop, out diagnostics))
                return false;

            return treatAsTroop &&
                   string.Equals(resolvedClass?.StringId, heroClass.StringId, StringComparison.Ordinal);
        }

        private static bool ResolveIsMounted(BasicCharacterObject character)
        {
            if (character == null)
                return false;

            if (TryGetBoolProperty(character, "IsMounted"))
                return true;

            FormationClass formationClass = character.DefaultFormationClass;
            return formationClass == FormationClass.Cavalry || formationClass == FormationClass.HorseArcher;
        }

        private static bool ResolveIsRanged(BasicCharacterObject character)
        {
            if (character == null)
                return false;

            if (TryGetBoolProperty(character, "IsRanged"))
                return true;

            FormationClass formationClass = character.DefaultFormationClass;
            return formationClass == FormationClass.Ranged || formationClass == FormationClass.HorseArcher;
        }

        private static RangedWeaponFamily ResolveRangedWeaponFamily(BasicCharacterObject character)
        {
            if (character == null)
                return RangedWeaponFamily.Unknown;

            foreach (Equipment equipment in EnumerateBattleEquipments(character))
            {
                RangedWeaponFamily family = ResolveRangedWeaponFamily(equipment);
                if (family != RangedWeaponFamily.Unknown)
                    return family;
            }

            string normalizedCharacterId = (character.StringId ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedCharacterId.Contains("crossbow") || normalizedCharacterId.Contains("bolt"))
                return RangedWeaponFamily.Crossbow;
            if (normalizedCharacterId.Contains("archer") || normalizedCharacterId.Contains("bow") || normalizedCharacterId.Contains("arrow"))
                return RangedWeaponFamily.Bow;

            return RangedWeaponFamily.Unknown;
        }

        private static IEnumerable<Equipment> EnumerateBattleEquipments(BasicCharacterObject character)
        {
            if (character == null)
                yield break;

            var yielded = new HashSet<Equipment>();

            if (character.FirstBattleEquipment != null && yielded.Add(character.FirstBattleEquipment))
                yield return character.FirstBattleEquipment;

            if (character.RandomBattleEquipment != null && yielded.Add(character.RandomBattleEquipment))
                yield return character.RandomBattleEquipment;

            IEnumerable<Equipment> battleEquipments = null;
            try
            {
                battleEquipments = character.BattleEquipments;
            }
            catch
            {
                battleEquipments = null;
            }

            if (battleEquipments == null)
                yield break;

            foreach (Equipment equipment in battleEquipments)
            {
                if (equipment != null && yielded.Add(equipment))
                    yield return equipment;
            }
        }

        private static RangedWeaponFamily ResolveRangedWeaponFamily(Equipment equipment)
        {
            if (equipment == null)
                return RangedWeaponFamily.Unknown;

            bool sawBow = false;
            bool sawCrossbow = false;
            for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
            {
                ItemObject item = equipment[slot].Item;
                if (item == null)
                    continue;

                switch (item.ItemType)
                {
                    case ItemObject.ItemTypeEnum.Crossbow:
                    case ItemObject.ItemTypeEnum.Bolts:
                        sawCrossbow = true;
                        break;
                    case ItemObject.ItemTypeEnum.Bow:
                    case ItemObject.ItemTypeEnum.Arrows:
                        sawBow = true;
                        break;
                }
            }

            if (sawCrossbow)
                return RangedWeaponFamily.Crossbow;
            if (sawBow)
                return RangedWeaponFamily.Bow;

            return RangedWeaponFamily.Unknown;
        }

        private static int ResolveTier(BasicCharacterObject character, bool isHero)
        {
            int tier = TryGetIntProperty(character, "Tier");
            if (tier > 0)
                return tier;

            int level = TryGetIntProperty(character, "Level");
            if (level >= 26)
                return 5;
            if (level >= 18)
                return 4;
            if (level >= 10)
                return 3;
            if (level >= 5)
                return 2;

            if (isHero)
                return 4;

            return 1;
        }

        private static string ResolveCultureToken(BasicCharacterObject character)
        {
            string cultureId = character?.Culture?.StringId;
            if (string.IsNullOrWhiteSpace(cultureId))
                cultureId = character?.StringId;

            if (string.IsNullOrWhiteSpace(cultureId))
                return null;

            string normalized = cultureId.Trim().ToLowerInvariant();
            if (normalized.Contains("looter"))
                return "empire";
            if (normalized.Contains("sea_raider"))
                return "sturgia";
            if (normalized.Contains("forest_bandit"))
                return "battania";
            if (normalized.Contains("mountain_bandit"))
                return "vlandia";
            if (normalized.Contains("desert_bandit"))
                return "aserai";
            if (normalized.Contains("steppe_bandit"))
                return "khuzait";
            if (normalized.Contains("empire") || normalized.StartsWith("imperial"))
                return "empire";
            if (normalized.Contains("aserai"))
                return "aserai";
            if (normalized.Contains("battania") || normalized.StartsWith("battanian"))
                return "battania";
            if (normalized.Contains("sturgia") || normalized.StartsWith("sturgian"))
                return "sturgia";
            if (normalized.Contains("vlandia") || normalized.StartsWith("vlandian"))
                return "vlandia";
            if (normalized.Contains("khuzait"))
                return "khuzait";

            return "empire";
        }

        private static string ResolveSurrogateTroopTemplateId(
            string characterId,
            string cultureToken,
            bool isMounted,
            bool isRanged,
            int tier,
            RangedWeaponFamily rangedWeaponFamily)
        {
            if (string.IsNullOrWhiteSpace(cultureToken))
                cultureToken = "empire";

            string normalizedCharacterId = (characterId ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedCharacterId.Contains("looter"))
                return "mp_coop_looter_troop";
            if (normalizedCharacterId.Contains("sea_raider"))
                return "mp_heavy_infantry_sturgia_troop";
            if (normalizedCharacterId.Contains("forest_bandit"))
                return "mp_light_ranged_battania_troop";
            if (normalizedCharacterId.Contains("mountain_bandit"))
                return "mp_light_infantry_vlandia_troop";
            if (normalizedCharacterId.Contains("desert_bandit"))
                return "mp_skirmisher_aserai_troop";
            if (normalizedCharacterId.Contains("steppe_bandit"))
                return "mp_horse_archer_khuzait_troop";

            if (isMounted)
                return NormalizeKnownTemplateId("mp_coop_light_cavalry_" + cultureToken + "_troop");

            if (isRanged)
            {
                if (string.Equals(cultureToken, "empire", StringComparison.Ordinal) &&
                    rangedWeaponFamily == RangedWeaponFamily.Crossbow)
                {
                    return EmpireCrossbowSurrogateTroopTemplateId;
                }

                return NormalizeKnownTemplateId(
                    (tier >= 4 ? "mp_heavy_ranged_" : "mp_light_ranged_") +
                    cultureToken +
                    "_troop");
            }

            if (string.Equals(cultureToken, "empire", StringComparison.Ordinal) && tier >= 4)
                return "mp_coop_heavy_infantry_empire_troop";

            if (tier >= 5)
                return NormalizeKnownTemplateId("mp_shock_infantry_" + cultureToken + "_troop");
            if (tier >= 4)
                return NormalizeKnownTemplateId("mp_heavy_infantry_" + cultureToken + "_troop");

            return NormalizeKnownTemplateId("mp_light_infantry_" + cultureToken + "_troop");
        }

        private static string NormalizeKnownTemplateId(string candidate)
        {
            switch (candidate)
            {
                case "mp_light_infantry_empire_troop":
                    return "mp_coop_light_infantry_empire_troop";
                case "mp_heavy_infantry_aserai_troop":
                    return "mp_shock_infantry_aserai_troop";
                default:
                    return candidate;
            }
        }

        private static string TryConvertTroopTemplateToHeroTemplateId(string troopTemplateId)
        {
            if (string.IsNullOrWhiteSpace(troopTemplateId))
                return null;

            if (!troopTemplateId.EndsWith("_troop", StringComparison.Ordinal))
                return troopTemplateId;

            return troopTemplateId.Substring(0, troopTemplateId.Length - "_troop".Length) + "_hero";
        }

        private static bool TryGetBoolProperty(object instance, string propertyName)
        {
            try
            {
                PropertyInfo property = instance?.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null || property.PropertyType != typeof(bool))
                    return false;

                return (bool)property.GetValue(instance, null);
            }
            catch
            {
                return false;
            }
        }

        private static int TryGetIntProperty(object instance, string propertyName)
        {
            try
            {
                PropertyInfo property = instance?.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null)
                    return 0;

                object value = property.GetValue(instance, null);
                if (value is int intValue)
                    return intValue;

                return Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }
    }
}
