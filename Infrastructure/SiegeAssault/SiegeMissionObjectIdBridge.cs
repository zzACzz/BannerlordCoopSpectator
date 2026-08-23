using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Objects.Siege;

namespace CoopSpectator.Infrastructure
{
    internal enum SiegeMissionObjectIdBridgeTarget
    {
        AnyMissionObject,
        RangedSiegeWeapon,
        UsableMachine,
        UsableMissionObject,
        SiegeLadder,
        SynchedMissionObject
    }

    internal static class SiegeMissionObjectIdBridge
    {
        private const int SiegeWeaponChildIdWindow = 128;
        private static readonly object Sync = new object();
        private static readonly Dictionary<int, int> ExactMissionObjectIdMap = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> ReverseExactMissionObjectIdMap = new Dictionary<int, int>();
        private static readonly List<SiegeWeaponOffsetMapping> SiegeWeaponOffsetMappings = new List<SiegeWeaponOffsetMapping>();

        private sealed class SiegeWeaponOffsetMapping
        {
            public int ServerWeaponId;
            public int LocalWeaponId;
            public int Offset;
            public BattleSideEnum Side;
            public string WeaponTypeName;
            public DateTime RegisteredUtc;
        }

        public static void Reset(string source)
        {
            lock (Sync)
            {
                ExactMissionObjectIdMap.Clear();
                ReverseExactMissionObjectIdMap.Clear();
                SiegeWeaponOffsetMappings.Clear();
            }
        }

        public static string RegisterDeploymentPointMapping(
            MissionObjectId serverDeploymentPointId,
            DeploymentPoint localDeploymentPoint,
            BattleSideEnum side,
            string source)
        {
            if (localDeploymentPoint == null)
                return "deployment-point-skip:null-local";

            return RegisterExactMapping(
                serverDeploymentPointId,
                localDeploymentPoint.Id,
                side,
                null,
                registerOffset: false,
                source: source,
                label: "deployment-point");
        }

        public static string RegisterSiegeWeaponMapping(
            MissionObjectId serverSiegeWeaponId,
            SiegeWeapon localSiegeWeapon,
            BattleSideEnum side,
            string weaponTypeName,
            string source)
        {
            if (localSiegeWeapon == null)
                return "siege-weapon-skip:null-local";

            return RegisterExactMapping(
                serverSiegeWeaponId,
                localSiegeWeapon.Id,
                side,
                weaponTypeName,
                registerOffset: true,
                source: source,
                label: "siege-weapon");
        }

        public static string RegisterMissionObjectMapping(
            MissionObjectId serverMissionObjectId,
            MissionObject localMissionObject,
            BattleSideEnum side,
            string objectTypeName,
            string source)
        {
            if (localMissionObject == null)
                return "mission-object-skip:null-local";

            return RegisterExactMapping(
                serverMissionObjectId,
                localMissionObject.Id,
                side,
                objectTypeName,
                registerOffset: false,
                source: source,
                label: "mission-object");
        }

        public static bool TryTranslateMissionObjectId(
            Mission mission,
            MissionObjectId serverMissionObjectId,
            SiegeMissionObjectIdBridgeTarget target,
            out MissionObjectId localMissionObjectId,
            out string diagnostics)
        {
            localMissionObjectId = serverMissionObjectId;
            diagnostics = string.Empty;

            if (!IsExactSiegeClientContext(mission))
            {
                diagnostics = "skip-context";
                return false;
            }

            int serverId = serverMissionObjectId.Id;
            if (serverId < 0)
            {
                diagnostics = "skip-invalid-server-id:" + serverId;
                return false;
            }

            int exactLocalId = -1;
            bool hasExactMap;
            List<SiegeWeaponOffsetMapping> offsetMappings;
            lock (Sync)
            {
                hasExactMap = ExactMissionObjectIdMap.TryGetValue(serverId, out exactLocalId);
                offsetMappings = SiegeWeaponOffsetMappings
                    .OrderByDescending(candidate => candidate.RegisteredUtc)
                    .ToList();
            }

            if (hasExactMap &&
                exactLocalId >= 0 &&
                TryCreateValidatedLocalMissionObjectId(
                    mission,
                    exactLocalId,
                    target,
                    out localMissionObjectId,
                    out string exactValidationDiagnostics))
            {
                diagnostics =
                    "exact-map ServerId=" + serverId +
                    " LocalId=" + exactLocalId +
                    " Target=" + target +
                    " Validation={" + exactValidationDiagnostics + "}";
                return true;
            }

            foreach (SiegeWeaponOffsetMapping mapping in offsetMappings)
            {
                if (mapping == null || mapping.Offset == 0)
                    continue;

                if (Math.Abs(serverId - mapping.ServerWeaponId) > SiegeWeaponChildIdWindow)
                    continue;

                int candidateLocalId = serverId + mapping.Offset;
                if (candidateLocalId == serverId || candidateLocalId < 0)
                    continue;

                if (!TryCreateValidatedLocalMissionObjectId(
                        mission,
                        candidateLocalId,
                        target,
                        out localMissionObjectId,
                        out string offsetValidationDiagnostics))
                {
                    continue;
                }

                diagnostics =
                    "offset-map ServerId=" + serverId +
                    " LocalId=" + candidateLocalId +
                    " Offset=" + mapping.Offset +
                    " AnchorServerWeaponId=" + mapping.ServerWeaponId +
                    " AnchorLocalWeaponId=" + mapping.LocalWeaponId +
                    " Side=" + mapping.Side +
                    " WeaponType=" + (mapping.WeaponTypeName ?? string.Empty) +
                    " Target=" + target +
                    " Validation={" + offsetValidationDiagnostics + "}";
                return true;
            }

            diagnostics =
                "not-mapped ServerId=" + serverId +
                " HasExactMap=" + hasExactMap +
                " ExactMap=" + exactLocalId +
                " OffsetCount=" + offsetMappings.Count +
                " Target=" + target;
            return false;
        }

        public static bool TryTranslateLocalMissionObjectId(
            Mission mission,
            MissionObjectId localMissionObjectId,
            SiegeMissionObjectIdBridgeTarget target,
            out MissionObjectId serverMissionObjectId,
            out string diagnostics)
        {
            serverMissionObjectId = localMissionObjectId;
            diagnostics = string.Empty;

            if (!IsExactSiegeClientContext(mission))
            {
                diagnostics = "skip-context";
                return false;
            }

            int localId = localMissionObjectId.Id;
            if (localId < 0)
            {
                diagnostics = "skip-invalid-local-id:" + localId;
                return false;
            }

            if (!TryCreateValidatedLocalMissionObjectId(
                    mission,
                    localId,
                    target,
                    out MissionObjectId validatedLocalMissionObjectId,
                    out string localValidationDiagnostics))
            {
                diagnostics =
                    "local-validation-failed LocalId=" + localId +
                    " Target=" + target +
                    " Validation={" + localValidationDiagnostics + "}";
                return false;
            }

            int exactServerId = -1;
            bool hasExactMap;
            List<SiegeWeaponOffsetMapping> offsetMappings;
            lock (Sync)
            {
                hasExactMap = ReverseExactMissionObjectIdMap.TryGetValue(localId, out exactServerId);
                offsetMappings = SiegeWeaponOffsetMappings
                    .OrderByDescending(candidate => candidate.RegisteredUtc)
                    .ToList();
            }

            if (hasExactMap && exactServerId >= 0)
            {
                serverMissionObjectId = new MissionObjectId(exactServerId, validatedLocalMissionObjectId.CreatedAtRuntime);
                diagnostics =
                    "exact-reverse-map LocalId=" + localId +
                    " ServerId=" + exactServerId +
                    " Target=" + target +
                    " Validation={" + localValidationDiagnostics + "}";
                return true;
            }

            foreach (SiegeWeaponOffsetMapping mapping in offsetMappings)
            {
                if (mapping == null || mapping.Offset == 0)
                    continue;

                if (Math.Abs(localId - mapping.LocalWeaponId) > SiegeWeaponChildIdWindow)
                    continue;

                int candidateServerId = localId - mapping.Offset;
                if (candidateServerId == localId || candidateServerId < 0)
                    continue;

                serverMissionObjectId = new MissionObjectId(candidateServerId, validatedLocalMissionObjectId.CreatedAtRuntime);
                diagnostics =
                    "offset-reverse-map LocalId=" + localId +
                    " ServerId=" + candidateServerId +
                    " Offset=" + mapping.Offset +
                    " AnchorServerWeaponId=" + mapping.ServerWeaponId +
                    " AnchorLocalWeaponId=" + mapping.LocalWeaponId +
                    " Side=" + mapping.Side +
                    " WeaponType=" + (mapping.WeaponTypeName ?? string.Empty) +
                    " Target=" + target +
                    " Validation={" + localValidationDiagnostics + "}";
                return true;
            }

            diagnostics =
                "not-mapped LocalId=" + localId +
                " HasExactMap=" + hasExactMap +
                " ExactMap=" + exactServerId +
                " OffsetCount=" + offsetMappings.Count +
                " Target=" + target +
                " Validation={" + localValidationDiagnostics + "}";
            return false;
        }

        public static bool IsExactSiegeClientContext(Mission mission)
        {
            if (mission == null || !GameNetwork.IsClient || GameNetwork.IsServer)
                return false;

            if (!SceneRuntimeClassifier.IsExactCampaignBattleScene(mission.SceneName ?? string.Empty))
                return false;

            BattleScenarioContextMessage scenarioContext = null;
            try
            {
                scenarioContext =
                    BattleSnapshotRuntimeState.GetScenarioContext() ??
                    BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                    BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            }
            catch
            {
            }

            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext))
                return false;

            return ExactCampaignSiegeAssaultWithDeploymentRuntime.IsExactSiegeWithDeploymentScenario(scenarioContext);
        }

        private static string RegisterExactMapping(
            MissionObjectId serverMissionObjectId,
            MissionObjectId localMissionObjectId,
            BattleSideEnum side,
            string weaponTypeName,
            bool registerOffset,
            string source,
            string label)
        {
            int serverId = serverMissionObjectId.Id;
            int localId = localMissionObjectId.Id;
            if (serverId < 0 || localId < 0)
            {
                return
                    label + "-skip-invalid-id ServerId=" + serverId +
                    " LocalId=" + localId;
            }

            int offset = localId - serverId;
            lock (Sync)
            {
                ExactMissionObjectIdMap[serverId] = localId;
                ReverseExactMissionObjectIdMap[localId] = serverId;

                if (registerOffset && offset != 0)
                {
                    SiegeWeaponOffsetMappings.RemoveAll(
                        candidate => candidate != null &&
                                     candidate.ServerWeaponId == serverId &&
                                     candidate.Side == side);
                    SiegeWeaponOffsetMappings.Add(
                        new SiegeWeaponOffsetMapping
                        {
                            ServerWeaponId = serverId,
                            LocalWeaponId = localId,
                            Offset = offset,
                            Side = side,
                            WeaponTypeName = weaponTypeName ?? string.Empty,
                            RegisteredUtc = DateTime.UtcNow
                        });
                }
            }

            return
                label + "-registered ServerId=" + serverId +
                " LocalId=" + localId +
                " Offset=" + offset +
                " OffsetRegistered=" + (registerOffset && offset != 0) +
                " Side=" + side +
                " WeaponType=" + (weaponTypeName ?? string.Empty) +
                " Source=" + (source ?? "unknown");
        }

        private static bool TryCreateValidatedLocalMissionObjectId(
            Mission mission,
            int localId,
            SiegeMissionObjectIdBridgeTarget target,
            out MissionObjectId localMissionObjectId,
            out string diagnostics)
        {
            localMissionObjectId = new MissionObjectId(localId, false);
            diagnostics = string.Empty;

            MissionObject missionObject = null;
            try
            {
                missionObject = Mission.MissionNetworkHelper.GetMissionObjectFromMissionObjectId(localMissionObjectId);
            }
            catch (Exception ex)
            {
                diagnostics = "lookup-exception:" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }

            if (missionObject == null)
            {
                diagnostics = "missing-local-object";
                return false;
            }

            if (target == SiegeMissionObjectIdBridgeTarget.RangedSiegeWeapon &&
                !(missionObject is RangedSiegeWeapon))
            {
                diagnostics =
                    "type-mismatch Actual=" + missionObject.GetType().FullName +
                    " Expected=" + typeof(RangedSiegeWeapon).FullName;
                return false;
            }

            if (target == SiegeMissionObjectIdBridgeTarget.UsableMachine &&
                !(missionObject is UsableMachine))
            {
                diagnostics =
                    "type-mismatch Actual=" + missionObject.GetType().FullName +
                    " Expected=" + typeof(UsableMachine).FullName;
                return false;
            }

            if (target == SiegeMissionObjectIdBridgeTarget.UsableMissionObject &&
                !(missionObject is UsableMissionObject))
            {
                diagnostics =
                    "type-mismatch Actual=" + missionObject.GetType().FullName +
                    " Expected=" + typeof(UsableMissionObject).FullName;
                return false;
            }

            if (target == SiegeMissionObjectIdBridgeTarget.SiegeLadder &&
                !(missionObject is SiegeLadder))
            {
                diagnostics =
                    "type-mismatch Actual=" + missionObject.GetType().FullName +
                    " Expected=" + typeof(SiegeLadder).FullName;
                return false;
            }

            if (target == SiegeMissionObjectIdBridgeTarget.SynchedMissionObject &&
                !(missionObject is SynchedMissionObject))
            {
                diagnostics =
                    "type-mismatch Actual=" + missionObject.GetType().FullName +
                    " Expected=" + typeof(SynchedMissionObject).FullName;
                return false;
            }

            diagnostics = "ok Actual=" + missionObject.GetType().FullName;
            return true;
        }
    }
}
