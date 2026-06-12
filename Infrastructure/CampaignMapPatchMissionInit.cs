using System;
using CoopSpectator.Campaign;
using CoopSpectator.Network.Messages;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Shared helper that copies campaign encounter patch context into a mission
    /// initializer record for battle-map runtime scenes, regardless of whether
    /// startup currently goes through CoopBattle or stable vanilla Battle.
    /// </summary>
    public static class CampaignMapPatchMissionInit
    {
        private static readonly FieldInfo MissionInitializerRecordBackingField =
            typeof(Mission).GetField("<InitializerRecord>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo MissionInitializerRecordProperty =
            typeof(Mission).GetProperty("InitializerRecord", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo BattleSpawnPathSelectorField =
            typeof(Mission).GetField("_battleSpawnPathSelector", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void TryApply(ref MissionInitializerRecord record, string runtimeScene, string logSource)
        {
            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(runtimeScene))
                return;

            string source = string.IsNullOrWhiteSpace(logSource) ? "CampaignMapPatchMissionInit" : logSource;
            BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " pre-apply");
            ApplyVillageBattleSceneContext(ref record, runtimeScene, source);
            BattleSnapshotMessage snapshot = TryResolveSnapshot(source);
            if (snapshot == null)
            {
                ModLogger.Info(source + ": skipped campaign map patch context (battle snapshot missing).");
                BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " skipped-missing-snapshot");
                return;
            }

            ApplyCampaignDifficultyContext(ref record, snapshot, runtimeScene, source);
            ApplySiegeSceneLevelContext(ref record, snapshot, runtimeScene, source);

            if (IsSiegeScenario(snapshot))
            {
                string siegeSubtype = snapshot?.ScenarioContext?.SiegeContext?.SiegeSubtype ?? string.Empty;
                if (string.Equals(siegeSubtype, "LordsHall", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(siegeSubtype, "Blockade", StringComparison.OrdinalIgnoreCase))
                {
                    ModLogger.Info(
                        source + ": skipped campaign map patch context for closed siege runtime. " +
                        "RuntimeScene=" + (runtimeScene ?? "unknown") +
                        " SiegeSubtype=" + (string.IsNullOrWhiteSpace(siegeSubtype) ? "unknown" : siegeSubtype) + ".");
                    BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " skipped-closed-siege-map-patch");
                    return;
                }

                ModLogger.Info(
                    source + ": enabling campaign map patch context for siege runtime. " +
                    "RuntimeScene=" + (runtimeScene ?? "unknown") +
                    " SiegeSubtype=" + (string.IsNullOrWhiteSpace(siegeSubtype) ? "unknown" : siegeSubtype) + ".");
            }

            if (SceneRuntimeClassifier.IsVillageBattleScene(runtimeScene))
            {
                ModLogger.Info(
                    source + ": skipped campaign map patch context for village battle runtime. " +
                    "RuntimeScene=" + (runtimeScene ?? "unknown") +
                    " SceneLevels=" + (record.SceneLevels ?? "null") + ".");
                BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " skipped-village-map-patch");
                return;
            }

            if (snapshot.MapPatchSceneIndex < 0)
            {
                ModLogger.Info(
                    source + ": skipped campaign map patch context (MapPatchSceneIndex missing). " +
                    "RuntimeScene=" + (runtimeScene ?? "unknown") + ".");
                BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " skipped-missing-scene-index");
                return;
            }

            if (!snapshot.HasPatchEncounterDirection)
            {
                ModLogger.Info(
                    source + ": skipped campaign map patch context (PatchEncounterDirection missing). " +
                    "RuntimeScene=" + (runtimeScene ?? "unknown") +
                    " MapPatchSceneIndex=" + snapshot.MapPatchSceneIndex + ".");
                BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " skipped-missing-direction");
                return;
            }

            float dirX = snapshot.PatchEncounterDirX;
            float dirY = snapshot.PatchEncounterDirY;
            double directionLength = Math.Sqrt(dirX * dirX + dirY * dirY);
            if (directionLength <= 0.001d)
            {
                ModLogger.Info(
                    source + ": skipped campaign map patch context (PatchEncounterDirection too small). " +
                    "RuntimeScene=" + (runtimeScene ?? "unknown") +
                    " MapPatchSceneIndex=" + snapshot.MapPatchSceneIndex +
                    " PatchEncounterDir=(" + dirX.ToString("0.###") + ", " + dirY.ToString("0.###") + ").");
                BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " skipped-small-direction");
                return;
            }

            record.PlayingInCampaignMode = false;
            record.SceneHasMapPatch = true;
            record.PatchCoordinates = new Vec2(
                Clamp01(snapshot.MapPatchNormalizedX),
                Clamp01(snapshot.MapPatchNormalizedY));
            record.PatchEncounterDir = new Vec2(
                (float)(dirX / directionLength),
                (float)(dirY / directionLength));

            ModLogger.Info(
                source + ": applied campaign map patch context. " +
                "RuntimeScene=" + (runtimeScene ?? "unknown") +
                " WorldMapScene=" + (snapshot.WorldMapScene ?? "unknown") +
                " MapPatchSceneIndex=" + snapshot.MapPatchSceneIndex +
                " PatchCoordinates=(" + record.PatchCoordinates.x.ToString("0.###") + ", " + record.PatchCoordinates.y.ToString("0.###") + ")" +
                " PatchEncounterDir=(" + record.PatchEncounterDir.x.ToString("0.###") + ", " + record.PatchEncounterDir.y.ToString("0.###") + ")" +
                " DirectionSource=" + (snapshot.PatchEncounterDirectionSource ?? "unknown") + ".");
            BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " post-apply");
        }

        private static void ApplyCampaignDifficultyContext(
            ref MissionInitializerRecord record,
            BattleSnapshotMessage snapshot,
            string runtimeScene,
            string source)
        {
            float playerTroopsReceivedDamageMultiplier = snapshot?.PlayerTroopsReceivedDamageMultiplier ?? 1f;
            if (playerTroopsReceivedDamageMultiplier <= 0f)
                playerTroopsReceivedDamageMultiplier = 1f;

            record.DamageToFriendsMultiplier = playerTroopsReceivedDamageMultiplier;
            record.DamageFromPlayerToFriendsMultiplier = playerTroopsReceivedDamageMultiplier;

            ModLogger.Info(
                source + ": applied campaign player-troops damage multiplier. " +
                "RuntimeScene=" + (runtimeScene ?? "unknown") +
                " Multiplier=" + playerTroopsReceivedDamageMultiplier.ToString("0.###") + ".");
        }

        private static void ApplyVillageBattleSceneContext(
            ref MissionInitializerRecord record,
            string runtimeScene,
            string source)
        {
            if (!SceneRuntimeClassifier.RequiresLandRaidSceneLevel(runtimeScene))
                return;

            if (string.Equals(record.SceneLevels, "land_raid", StringComparison.OrdinalIgnoreCase))
                return;

            record.SceneLevels = "land_raid";
            ModLogger.Info(
                source + ": applied village battle scene-level context. " +
                "RuntimeScene=" + (runtimeScene ?? "unknown") +
                " SceneLevels=" + (record.SceneLevels ?? "null") + ".");
        }

        private static void ApplySiegeSceneLevelContext(
            ref MissionInitializerRecord record,
            BattleSnapshotMessage snapshot,
            string runtimeScene,
            string source)
        {
            if (snapshot?.ScenarioContext?.IsSiegeBattle != true)
                return;

            string siegeSubtype = snapshot.ScenarioContext.SiegeContext?.SiegeSubtype ?? string.Empty;
            if (string.Equals(siegeSubtype, "LordsHall", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(siegeSubtype, "Blockade", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            int wallLevel = snapshot.ScenarioContext.SiegeContext?.WallLevel ?? 0;
            if (wallLevel < 1)
                wallLevel = 1;
            if (wallLevel > 3)
                wallLevel = 3;

            string desiredSceneLevels = "level_" + wallLevel + " siege";
            if (string.Equals(record.SceneLevels, desiredSceneLevels, StringComparison.OrdinalIgnoreCase))
                return;

            record.SceneLevels = desiredSceneLevels;
            ModLogger.Info(
                source + ": applied siege scene-level context. " +
                "RuntimeScene=" + (runtimeScene ?? "unknown") +
                " SiegeSubtype=" + (string.IsNullOrWhiteSpace(siegeSubtype) ? "unknown" : siegeSubtype) +
                " WallLevel=" + wallLevel +
                " SceneLevels=" + (record.SceneLevels ?? "null") + ".");
        }

        public static bool TryRepairLiveMissionContract(Mission mission, string logSource)
        {
            if (mission == null)
                return false;

            string runtimeScene = mission.SceneName ?? string.Empty;
            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(runtimeScene))
                return false;

            string source = string.IsNullOrWhiteSpace(logSource) ? "CampaignMapPatchMissionInit.LiveMissionRepair" : logSource;
            bool changed = false;
            bool initializerPatched = false;

            try
            {
                if (TryGetMissionInitializerRecord(mission, out MissionInitializerRecord record))
                {
                    string previousSceneLevels = record.SceneLevels;
                    bool hadPatchBefore = record.SceneHasMapPatch;
                    TryApply(ref record, runtimeScene, source + " initializer");
                    bool writeBackSucceeded = TrySetMissionInitializerRecord(mission, record);

                    initializerPatched = writeBackSucceeded && record.SceneHasMapPatch;
                    changed |= (!hadPatchBefore && record.SceneHasMapPatch);
                    changed |= !string.Equals(previousSceneLevels, record.SceneLevels, StringComparison.Ordinal);
                    if (TryGetMissionInitializerRecord(mission, out MissionInitializerRecord storedRecord))
                        BattleMapContractDiagnostics.LogMissionInitializerRecordState(storedRecord, source + " live-mission-record");
                    else
                        BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " live-mission-record-local");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    source + ": live mission initializer repair failed. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " Message=" + ex.Message);
            }

            try
            {
                BattleSnapshotMessage snapshot = TryResolveSnapshot(source + " team-ai");
                Mission.MissionTeamAITypeEnum targetType = ResolveMissionTeamAiType(snapshot?.ScenarioContext);
                if (mission.MissionTeamAIType != targetType)
                {
                    Mission.MissionTeamAITypeEnum previousType = mission.MissionTeamAIType;
                    mission.MissionTeamAIType = targetType;
                    changed = true;
                    ModLogger.Info(
                        source + ": repaired live mission team AI type. " +
                        "Scene=" + (mission.SceneName ?? "null") +
                        " PreviousType=" + previousType +
                        " NewType=" + mission.MissionTeamAIType +
                        " SiegeSubtype=" + (snapshot?.ScenarioContext?.SiegeContext?.SiegeSubtype ?? "none") + ".");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    source + ": failed to repair live mission team AI type. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " Message=" + ex.Message);
            }

            bool spawnPathReinitialized = false;
            try
            {
                object spawnPathSelectorObject = BattleSpawnPathSelectorField?.GetValue(mission);
                if (spawnPathSelectorObject is BattleSpawnPathSelector selector)
                {
                    selector.Initialize();
                    spawnPathReinitialized = selector.IsInitialized;
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    source + ": live mission spawn-path reinitialize failed. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " Message=" + ex.Message);
            }

            ModLogger.Info(
                source + ": live mission contract repair applied. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " InitializerPatched=" + initializerPatched +
                " MissionTeamAIType=" + mission.MissionTeamAIType +
                " HasSceneMapPatch=" + SafeHasSceneMapPatch(mission) +
                " HasSpawnPath=" + SafeHasSpawnPath(mission) +
                " SpawnPathReinitialized=" + spawnPathReinitialized +
                " Changed=" + changed + ".");

            return changed;
        }

        private static BattleSnapshotMessage TryResolveSnapshot(string source)
        {
            try
            {
                BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
                if (snapshot != null)
                    return snapshot;
            }
            catch
            {
            }

            if (GameNetwork.IsClient && !CustomGameJoinContextState.ShouldAllowLocalBattleRosterFileFallback())
            {
                ModLogger.Info(
                    (string.IsNullOrWhiteSpace(source) ? "CampaignMapPatchMissionInit" : source) +
                    ": skipped local battle roster snapshot fallback for remote custom-game join.");
                return null;
            }

            try
            {
                return BattleRosterFileHelper.ReadSnapshot();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSiegeScenario(BattleSnapshotMessage snapshot)
        {
            return snapshot?.ScenarioContext?.IsSiegeBattle == true;
        }

        private static Mission.MissionTeamAITypeEnum ResolveMissionTeamAiType(BattleScenarioContextMessage scenarioContext)
        {
            if (scenarioContext?.IsSiegeBattle != true)
                return Mission.MissionTeamAITypeEnum.FieldBattle;

            string siegeSubtype = scenarioContext.SiegeContext?.SiegeSubtype ?? string.Empty;
            if (string.Equals(siegeSubtype, "LordsHall", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(siegeSubtype, "Blockade", StringComparison.OrdinalIgnoreCase))
                return Mission.MissionTeamAITypeEnum.NoTeamAI;

            if (string.Equals(siegeSubtype, "SallyOut", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(siegeSubtype, "BlockadeSallyOut", StringComparison.OrdinalIgnoreCase))
            {
                return Mission.MissionTeamAITypeEnum.SallyOut;
            }

            // Native siege no-deployment assault currently runs through
            // MissionCombatantsLogic(FieldBattle), not Siege TeamAI.
            if (string.Equals(siegeSubtype, "SiegeAssault", StringComparison.OrdinalIgnoreCase))
                return Mission.MissionTeamAITypeEnum.FieldBattle;

            return Mission.MissionTeamAITypeEnum.Siege;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }

        private static bool TryGetMissionInitializerRecord(Mission mission, out MissionInitializerRecord record)
        {
            record = default;
            if (mission == null)
                return false;

            try
            {
                if (MissionInitializerRecordBackingField != null)
                {
                    object boxed = MissionInitializerRecordBackingField.GetValue(mission);
                    if (boxed is MissionInitializerRecord fieldRecord)
                    {
                        record = fieldRecord;
                        return true;
                    }
                }
            }
            catch
            {
            }

            try
            {
                object boxed = MissionInitializerRecordProperty?.GetValue(mission, null);
                if (boxed is MissionInitializerRecord propertyRecord)
                {
                    record = propertyRecord;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TrySetMissionInitializerRecord(Mission mission, MissionInitializerRecord record)
        {
            if (mission == null)
                return false;

            try
            {
                if (MissionInitializerRecordBackingField != null)
                {
                    MissionInitializerRecordBackingField.SetValue(mission, record);
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                if (MissionInitializerRecordProperty != null)
                {
                    MissionInitializerRecordProperty.SetValue(mission, record, null);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool SafeHasSceneMapPatch(Mission mission)
        {
            try
            {
                return mission != null && mission.HasSceneMapPatch();
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeHasSpawnPath(Mission mission)
        {
            try
            {
                return mission != null && mission.HasSpawnPath;
            }
            catch
            {
                return false;
            }
        }
    }
}
