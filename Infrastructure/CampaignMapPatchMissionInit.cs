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
            BattleSnapshotMessage snapshot = TryResolveSnapshot(source);
            if (snapshot == null)
            {
                ModLogger.Info(source + ": skipped campaign map patch context (battle snapshot missing).");
                BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " skipped-missing-snapshot");
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
                    bool hadPatchBefore = record.SceneHasMapPatch;
                    TryApply(ref record, runtimeScene, source + " initializer");
                    bool writeBackSucceeded = TrySetMissionInitializerRecord(mission, record);

                    initializerPatched = writeBackSucceeded && record.SceneHasMapPatch;
                    changed |= (!hadPatchBefore && record.SceneHasMapPatch);
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
                if (mission.MissionTeamAIType != Mission.MissionTeamAITypeEnum.FieldBattle)
                {
                    Mission.MissionTeamAITypeEnum previousType = mission.MissionTeamAIType;
                    mission.MissionTeamAIType = Mission.MissionTeamAITypeEnum.FieldBattle;
                    changed = true;
                    ModLogger.Info(
                        source + ": forced live mission team AI type to FieldBattle. " +
                        "Scene=" + (mission.SceneName ?? "null") +
                        " PreviousType=" + previousType +
                        " NewType=" + mission.MissionTeamAIType + ".");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    source + ": failed to force live mission team AI type. " +
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
