using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using CoopSpectator.Patches;
using Newtonsoft.Json;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions;
using TaleWorlds.MountAndBlade.Missions.Handlers;
using TaleWorlds.MountAndBlade.Network.Messages;
using TaleWorlds.MountAndBlade.Objects.Siege;
#if !COOPSPECTATOR_DEDICATED
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;
#endif

namespace CoopSpectator.MissionBehaviors
{
    internal static class CoopBattleNetworkRequestTransport
    {
        public static bool TrySelectSide(BattleSideEnum side, string source)
        {
            if (TrySendClientRequest(CoopBattleSelectionRequestKind.SelectSide, side, string.Empty, source))
                return true;

            return CoopBattleSelectionBridgeFile.WriteSelectSideRequest(side.ToString(), source ?? "network-fallback side");
        }

        public static bool TrySelectEntry(BattleSideEnum side, string entryId, string source)
        {
            if (TrySendClientRequest(CoopBattleSelectionRequestKind.SelectEntry, side, entryId, source))
                return true;

            bool wroteSide = CoopBattleSelectionBridgeFile.WriteSelectSideRequest(side.ToString(), source ?? "network-fallback entry-side");
            bool wroteEntry = CoopBattleSelectionBridgeFile.WriteSelectTroopRequest(entryId, source ?? "network-fallback entry");
            return wroteSide || wroteEntry;
        }

        public static bool TryBeginCommanderDeployment(BattleSideEnum side, string entryId, string source)
        {
            TrySelectEntry(side, entryId, (source ?? "commander-deployment") + " SelectEntry");
            return TrySendClientRequest(
                CoopBattleSelectionRequestKind.BeginCommanderDeployment,
                side,
                entryId,
                (source ?? "commander-deployment") + " BeginCommanderDeployment");
        }

        public static bool TryAutoDeployCommanderDeployment(BattleSideEnum side, string entryId, string source)
        {
            return TrySendClientRequest(
                CoopBattleSelectionRequestKind.AutoDeployCommanderDeployment,
                side,
                entryId,
                (source ?? "commander-deployment") + " AutoDeployCommanderDeployment");
        }

        public static bool TryFinishCommanderDeployment(BattleSideEnum side, string entryId, string source)
        {
            return TrySendClientRequest(
                CoopBattleSelectionRequestKind.FinishCommanderDeployment,
                side,
                entryId,
                (source ?? "commander-deployment") + " FinishCommanderDeployment");
        }

        public static bool TryQueueSpawnAfterDeployment(BattleSideEnum side, string entryId, string source)
        {
            TrySelectEntry(side, entryId, (source ?? "deployment-wait") + " SelectEntry");
            return TrySendClientRequest(
                CoopBattleSelectionRequestKind.QueueSpawnAfterDeployment,
                side,
                entryId,
                (source ?? "deployment-wait") + " QueueSpawnAfterDeployment");
        }

        public static bool TrySyncCommanderDeploymentFormationAssignments(
            BattleSideEnum side,
            byte[] assignmentBytes,
            byte[] formationLayoutBytes,
            byte[] captainAssignmentBytes,
            string source)
        {
            if (!GameNetwork.IsClient ||
                !GameNetwork.IsSessionActive ||
                assignmentBytes == null ||
                assignmentBytes.Length <= 0)
            {
                return false;
            }

            if (assignmentBytes.Length > CoopCommanderDeploymentFormationAssignmentsMessage.MaxAssignmentBytes)
            {
                ModLogger.Info(
                    "CoopBattleNetworkRequestTransport: commander deployment formation sync payload is too large. " +
                    "Side=" + side +
                    " Bytes=" + assignmentBytes.Length +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            byte[] safeLayoutBytes = formationLayoutBytes ?? Array.Empty<byte>();
            if (safeLayoutBytes.Length > CoopCommanderDeploymentFormationAssignmentsMessage.MaxFormationLayoutBytes)
            {
                ModLogger.Info(
                    "CoopBattleNetworkRequestTransport: commander deployment formation layout payload is too large. " +
                    "Side=" + side +
                    " Bytes=" + safeLayoutBytes.Length +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            byte[] safeCaptainBytes = captainAssignmentBytes ?? Array.Empty<byte>();
            if (safeCaptainBytes.Length > CoopCommanderDeploymentFormationAssignmentsMessage.MaxCaptainAssignmentBytes)
            {
                ModLogger.Info(
                    "CoopBattleNetworkRequestTransport: commander deployment captain assignment payload is too large. " +
                    "Side=" + side +
                    " Bytes=" + safeCaptainBytes.Length +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopCommanderDeploymentFormationAssignmentsMessage(
                    side,
                    assignmentBytes,
                    safeLayoutBytes,
                    safeCaptainBytes));
                GameNetwork.EndModuleEventAsClient();
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopBattleNetworkRequestTransport: commander deployment formation sync send failed. " +
                    "Side=" + side +
                    " Bytes=" + assignmentBytes.Length +
                    " LayoutBytes=" + safeLayoutBytes.Length +
                    " CaptainBytes=" + safeCaptainBytes.Length +
                    " Source=" + (source ?? "unknown") +
                    " Error=" + ex.Message);
                return false;
            }
        }

        public static bool TrySyncCommanderDeploymentSiegeMachineSelection(
            BattleSideEnum side,
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            bool clearSelection,
            string source)
        {
            if (!GameNetwork.IsClient ||
                !GameNetwork.IsSessionActive ||
                side == BattleSideEnum.None ||
                deploymentPoint == null ||
                (!clearSelection && siegeWeapon == null))
            {
                return false;
            }

            try
            {
                Vec3 deploymentPointPosition = ResolveCommanderDeploymentPointPosition(deploymentPoint);
                string siegeWeaponTypeName = clearSelection
                    ? string.Empty
                    : ResolveCommanderDeploymentSiegeWeaponTypeName(siegeWeapon);
                MissionObjectId siegeWeaponId = clearSelection
                    ? MissionObjectId.Invalid
                    : siegeWeapon.Id;
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopCommanderDeploymentSiegeMachineSelectionMessage(
                    side,
                    deploymentPoint.Id,
                    deploymentPointPosition,
                    siegeWeaponId,
                    siegeWeaponTypeName,
                    clearSelection));
                GameNetwork.EndModuleEventAsClient();
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopBattleNetworkRequestTransport: commander deployment siege machine sync send failed. " +
                    "Side=" + side +
                    " DeploymentPoint=" + FormatCommanderDeploymentPointForClientLog(deploymentPoint) +
                    " SiegeWeaponId=" + (siegeWeapon == null ? MissionObjectId.Invalid.ToString() : siegeWeapon.Id.ToString()) +
                    " SiegeWeaponType=" + ResolveCommanderDeploymentSiegeWeaponTypeName(siegeWeapon) +
                    " Clear=" + clearSelection +
                    " Source=" + (source ?? "unknown") +
                    " Error=" + ex.Message);
                return false;
            }
        }

        private static Vec3 ResolveCommanderDeploymentPointPosition(DeploymentPoint deploymentPoint)
        {
            if (deploymentPoint == null)
                return Vec3.Zero;

            try
            {
                return deploymentPoint.GetDeploymentOrigin();
            }
            catch
            {
            }

            try
            {
                return deploymentPoint.GameEntity.GlobalPosition;
            }
            catch
            {
                return Vec3.Zero;
            }
        }

        private static string ResolveCommanderDeploymentSiegeWeaponTypeName(SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return string.Empty;

            try
            {
                Type weaponType = MissionSiegeWeaponsController.GetWeaponType(siegeWeapon);
                if (weaponType != null)
                    return weaponType.FullName ?? weaponType.Name;
            }
            catch
            {
            }

            return siegeWeapon.GetType().FullName ?? siegeWeapon.GetType().Name;
        }

        private static string FormatCommanderDeploymentPointForClientLog(DeploymentPoint deploymentPoint)
        {
            if (deploymentPoint == null)
                return "<null>";

            Vec3 position = ResolveCommanderDeploymentPointPosition(deploymentPoint);
            return "Id=" + deploymentPoint.Id +
                   "/Side=" + deploymentPoint.Side +
                   "/Position=" + position;
        }

        public static bool TrySelectSpectator(string source)
        {
            if (TrySendClientRequest(CoopBattleSelectionRequestKind.Spectate, BattleSideEnum.None, string.Empty, source))
                return true;

            return CoopBattleSelectionBridgeFile.WriteSpectatorRequest(source ?? "network-fallback spectator");
        }

        public static bool TryRequestSpawn(string source)
        {
            if (TrySendClientRequest(CoopBattleSelectionRequestKind.SpawnNow, BattleSideEnum.None, string.Empty, source))
                return true;

            return CoopBattleSpawnBridgeFile.WriteSpawnNowRequest(source ?? "network-fallback spawn");
        }

        public static bool TryRequestForceRespawnable(string source)
        {
            if (TrySendClientRequest(CoopBattleSelectionRequestKind.ForceRespawnable, BattleSideEnum.None, string.Empty, source))
                return true;

            return CoopBattleSpawnBridgeFile.WriteForceRespawnableRequest(source ?? "network-fallback force-respawnable");
        }

        public static bool TryDelegateCurrentAgentToAi(int expectedAgentIndex, string source)
        {
            return TrySendAgentControlRequest(
                CoopBattleAgentControlRequestKind.DelegateToAi,
                expectedAgentIndex,
                source);
        }

        public static bool TryReclaimAiObservedAgent(int expectedAgentIndex, string source)
        {
            return TrySendAgentControlRequest(
                CoopBattleAgentControlRequestKind.ReclaimFromAi,
                expectedAgentIndex,
                source);
        }

        private static bool TrySendAgentControlRequest(
            CoopBattleAgentControlRequestKind requestKind,
            int expectedAgentIndex,
            string source)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || expectedAgentIndex < 0)
                return false;

            int requestId = CoopBattleAgentControlRuntimeState.BeginClientRequest(requestKind, expectedAgentIndex);
            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopBattleAgentControlRequestMessage(
                    requestKind,
                    expectedAgentIndex,
                    requestId));
                GameNetwork.EndModuleEventAsClient();
                ModLogger.Info(
                    "CoopBattleNetworkRequestTransport: sent agent control request. " +
                    "Kind=" + requestKind +
                    " ExpectedAgentIndex=" + expectedAgentIndex +
                    " RequestId=" + requestId +
                    " Source=" + (source ?? "unknown"));
                return true;
            }
            catch (Exception ex)
            {
                CoopBattleAgentControlRuntimeState.CancelPendingClientRequest(requestId);
                ModLogger.Info(
                    "CoopBattleNetworkRequestTransport: agent control request send failed. " +
                    "Kind=" + requestKind +
                    " ExpectedAgentIndex=" + expectedAgentIndex +
                    " RequestId=" + requestId +
                    " Source=" + (source ?? "unknown") +
                    " Error=" + ex.Message);
                return false;
            }
        }

        public static bool TryAcknowledgeBattleSnapshot(int transmissionId, string source)
        {
            if (transmissionId <= 0)
                return false;

            return TrySendClientRequest(
                CoopBattleSelectionRequestKind.BattleSnapshotReadyAck,
                BattleSideEnum.None,
                transmissionId.ToString(),
                source);
        }

        public static bool TryRequestBattleSnapshotBootstrap(string source)
        {
            return TrySendClientRequest(
                CoopBattleSelectionRequestKind.BattleSnapshotBootstrapRequest,
                BattleSideEnum.None,
                string.Empty,
                source);
        }

        public static bool TryAcknowledgeBattleReconnectFinalize(int transmissionId, string source)
        {
            if (transmissionId <= 0)
                return false;

            return TrySendClientRequest(
                CoopBattleSelectionRequestKind.BattleReconnectFinalizeReadyAck,
                BattleSideEnum.None,
                transmissionId.ToString(),
                source);
        }

        private static bool ShouldSuppressClientSelectionOrSpawnRequest(
            CoopBattleSelectionRequestKind requestKind,
            out string reason)
        {
            reason = null;
            if (requestKind != CoopBattleSelectionRequestKind.SelectEntry &&
                requestKind != CoopBattleSelectionRequestKind.SpawnNow &&
                requestKind != CoopBattleSelectionRequestKind.BeginCommanderDeployment &&
                requestKind != CoopBattleSelectionRequestKind.AutoDeployCommanderDeployment &&
                requestKind != CoopBattleSelectionRequestKind.FinishCommanderDeployment &&
                requestKind != CoopBattleSelectionRequestKind.QueueSpawnAfterDeployment)
            {
                return false;
            }

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (status == null)
                return false;

            string spawnStatus = status.SpawnStatus?.Trim();
            if (string.Equals(spawnStatus, CoopBattleSpawnStatus.Pending.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(spawnStatus, CoopBattleSpawnStatus.Validating.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(spawnStatus, CoopBattleSpawnStatus.Validated.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                reason = "spawn-transition-in-flight:" + spawnStatus;
                return true;
            }

            string lifecycleState = status.LifecycleState?.Trim();
            if (status.HasAgent &&
                !status.CanRespawn &&
                string.Equals(lifecycleState, CoopBattlePeerLifecycleStatus.Alive.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                reason = "peer-still-occupies-active-life";
                return true;
            }

            return false;
        }

        private static bool TrySendClientRequest(
            CoopBattleSelectionRequestKind requestKind,
            BattleSideEnum requestedSide,
            string selectionId,
            string source)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return false;

            if (ShouldSuppressClientSelectionOrSpawnRequest(requestKind, out string suppressionReason))
            {
                ModLogger.Info(
                    "CoopBattleNetworkRequestTransport: suppressed client request while local spawn handoff is still in flight. " +
                    "Kind=" + requestKind +
                    " Side=" + requestedSide +
                    " SelectionId=" + (selectionId ?? string.Empty) +
                    " Reason=" + (suppressionReason ?? "unknown") +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopBattleSelectionClientRequestMessage(requestKind, requestedSide, selectionId));
                GameNetwork.EndModuleEventAsClient();
                ModLogger.Info(
                    "CoopBattleNetworkRequestTransport: sent client request. " +
                    "Kind=" + requestKind +
                    " Side=" + requestedSide +
                    " SelectionId=" + (selectionId ?? string.Empty) +
                    " Source=" + (source ?? "unknown"));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopBattleNetworkRequestTransport: client request send failed. " +
                    "Kind=" + requestKind +
                    " Source=" + (source ?? "unknown") +
                    " Error=" + ex.Message);
                return false;
            }
        }
    }

#if !COOPSPECTATOR_DEDICATED
    internal sealed class CoopSiegeDeploymentBoundaryMarkerView : MissionView
    {
        private static readonly BodyFlags BoundaryHeightBodyFlags = (BodyFlags)540127625;
        private const string BoundaryWallEntityName = "coop_siege_deployment_boundary_wall";
        private const string BoundaryFallbackMarkerMeshName = "order_flag_small";

        private readonly string _prefabName;
        private readonly float _markerInterval;
        private readonly Dictionary<BattleSideEnum, Dictionary<string, List<GameEntity>>> _boundaryMarkersBySide =
            new Dictionary<BattleSideEnum, Dictionary<string, List<GameEntity>>>();
        private readonly HashSet<string> _loggedDiagnosticsKeys = new HashSet<string>(StringComparer.Ordinal);
        private GameEntity _cachedEntity;
        private bool _initialized;
        private bool _boundaryMarkersRemoved = true;
        private string _lastDiagnosticsKey = string.Empty;

        public CoopSiegeDeploymentBoundaryMarkerView(string prefabName, float markerInterval)
        {
            _prefabName = string.IsNullOrWhiteSpace(prefabName) ? "swallowtail_banner" : prefabName;
            _markerInterval = Math.Max(markerInterval, 0.0001f);
        }

        public override void AfterStart()
        {
            if (_initialized)
                return;

            base.AfterStart();
            EnsureSideMarkerMap(BattleSideEnum.Defender);
            EnsureSideMarkerMap(BattleSideEnum.Attacker);
            _boundaryMarkersRemoved = false;
            _initialized = true;
        }

        protected override void OnEndMission()
        {
            base.OnEndMission();
            TryRemoveBoundaryMarkers();
        }

        public override void OnRemoveBehavior()
        {
            TryRemoveBoundaryMarkers();
            base.OnRemoveBehavior();
        }

        public bool TryEnsureBoundaryMarkersForTeam(DefaultMissionDeploymentPlan deploymentPlan, Team team, string source)
        {
            if (deploymentPlan == null || team == null || team.Side == BattleSideEnum.None)
                return false;

            AfterStart();

            Dictionary<string, List<GameEntity>> sideMarkers = EnsureSideMarkerMap(team.Side);
            if (sideMarkers == null)
                return false;

            bool ensuredAny = false;
            int createdMarkerCount = 0;
            int createdWallCount = 0;
            int createdFallbackMarkerCount = 0;
            int boundaryCount = 0;
            int boundaryPointCount = 0;

            try
            {
                Banner banner = ResolveBannerForSide(team.Side);
                foreach (var boundary in deploymentPlan.GetDeploymentBoundaries(team))
                {
                    string key = string.IsNullOrWhiteSpace(boundary.Item1)
                        ? "boundary_" + boundaryCount.ToString()
                        : boundary.Item1;
                    boundaryCount++;

                    List<Vec2> points = boundary.Item2?.ToList();
                    ensuredAny |= TryEnsureBoundaryMarkersForPoints(
                        sideMarkers,
                        team.Side,
                        key,
                        points,
                        banner,
                        ref createdMarkerCount,
                        ref createdWallCount,
                        ref createdFallbackMarkerCount,
                        ref boundaryPointCount);
                }
            }
            catch (Exception ex)
            {
                if (CoopDebugConfig.OrderOfBattleDiagnostics)
                {
                    ModLogger.Info(
                        "CoopSiegeDeploymentBoundaryMarkerView: ensure failed. " +
                        "Side=" + team.Side +
                        " Source=" + (source ?? "unknown") +
                        " Error=" + ex.Message);
                }

                return ensuredAny;
            }

            if (CoopDebugConfig.OrderOfBattleDiagnostics)
            {
                string diagnosticsKey =
                    team.Side + "|" + boundaryCount + "|" + boundaryPointCount + "|" + createdMarkerCount + "|" +
                    createdWallCount + "|" + createdFallbackMarkerCount;
                if (!string.Equals(_lastDiagnosticsKey, diagnosticsKey, StringComparison.Ordinal))
                {
                    _lastDiagnosticsKey = diagnosticsKey;
                    ModLogger.Info(
                        "CoopSiegeDeploymentBoundaryMarkerView: ensured visible deployment boundaries. " +
                        "Side=" + team.Side +
                        " BoundaryCount=" + boundaryCount +
                        " BoundaryPointCount=" + boundaryPointCount +
                        " CreatedMarkerCount=" + createdMarkerCount +
                        " CreatedWallCount=" + createdWallCount +
                        " CreatedFallbackMarkerCount=" + createdFallbackMarkerCount +
                        " Source=" + (source ?? "unknown"));
                }
            }

            return ensuredAny;
        }

        public bool TryEnsureSceneDeploymentBoundaryMarkersForTeam(Team team, string source)
        {
            if (team == null || team.Side == BattleSideEnum.None)
                return false;

            AfterStart();

            Dictionary<string, List<GameEntity>> sideMarkers = EnsureSideMarkerMap(team.Side);
            if (sideMarkers == null)
                return false;

            bool ensuredAny = false;
            int createdMarkerCount = 0;
            int createdWallCount = 0;
            int createdFallbackMarkerCount = 0;
            int boundaryCount = 0;
            int boundaryPointCount = 0;

            try
            {
                Banner banner = ResolveBannerForSide(team.Side);
                foreach (var sceneBoundary in MBSceneUtilities.GetDeploymentBoundaries(team.Side))
                {
                    string key = string.IsNullOrWhiteSpace(sceneBoundary.Item1)
                        ? "scene_boundary_" + boundaryCount.ToString()
                        : "scene_" + sceneBoundary.Item1;
                    boundaryCount++;

                    List<Vec2> points = CreateSceneDeploymentBoundaryPoints(sceneBoundary.Item2);
                    ensuredAny |= TryEnsureBoundaryMarkersForPoints(
                        sideMarkers,
                        team.Side,
                        key,
                        points,
                        banner,
                        ref createdMarkerCount,
                        ref createdWallCount,
                        ref createdFallbackMarkerCount,
                        ref boundaryPointCount);
                }
            }
            catch (Exception ex)
            {
                if (CoopDebugConfig.OrderOfBattleDiagnostics)
                {
                    ModLogger.Info(
                        "CoopSiegeDeploymentBoundaryMarkerView: scene boundary ensure failed. " +
                        "Side=" + team.Side +
                        " Source=" + (source ?? "unknown") +
                        " Error=" + ex.Message);
                }

                return ensuredAny;
            }

            if (CoopDebugConfig.OrderOfBattleDiagnostics)
            {
                string diagnosticsKey =
                    "scene|" + team.Side + "|" + boundaryCount + "|" + boundaryPointCount + "|" +
                    createdMarkerCount + "|" + createdWallCount + "|" + createdFallbackMarkerCount;
                if (!string.Equals(_lastDiagnosticsKey, diagnosticsKey, StringComparison.Ordinal))
                {
                    _lastDiagnosticsKey = diagnosticsKey;
                    ModLogger.Info(
                        "CoopSiegeDeploymentBoundaryMarkerView: ensured visible scene deployment boundaries. " +
                        "Side=" + team.Side +
                        " BoundaryCount=" + boundaryCount +
                        " BoundaryPointCount=" + boundaryPointCount +
                        " CreatedMarkerCount=" + createdMarkerCount +
                        " CreatedWallCount=" + createdWallCount +
                        " CreatedFallbackMarkerCount=" + createdFallbackMarkerCount +
                        " Source=" + (source ?? "unknown"));
                }
            }

            return ensuredAny;
        }

        private bool TryEnsureBoundaryMarkersForPoints(
            Dictionary<string, List<GameEntity>> sideMarkers,
            BattleSideEnum side,
            string key,
            ICollection<Vec2> points,
            Banner banner,
            ref int createdMarkerCount,
            ref int createdWallCount,
            ref int createdFallbackMarkerCount,
            ref int boundaryPointCount)
        {
            if (sideMarkers == null || string.IsNullOrWhiteSpace(key))
                return false;

            if (sideMarkers.ContainsKey(key))
                return true;

            List<Vec2> pointList = points?.ToList();
            if (pointList == null || pointList.Count < 2)
                return false;

            boundaryPointCount += pointList.Count;
            List<GameEntity> markers = new List<GameEntity>();

            for (int i = 0; i < pointList.Count; i++)
            {
                createdFallbackMarkerCount += MarkLine(
                    pointList[i],
                    pointList[(i + 1) % pointList.Count],
                    markers,
                    banner);
            }

            sideMarkers[key] = markers;
            createdMarkerCount += markers.Count;
            if (markers.Count > 0)
            {
                _boundaryMarkersRemoved = false;
                return true;
            }

            return false;
        }

        private static List<Vec2> CreateSceneDeploymentBoundaryPoints(ICollection<Vec2> sourcePoints)
        {
            if (sourcePoints == null || sourcePoints.Count < 2)
                return null;

            try
            {
                MBList<Vec2> boundary = new MBList<Vec2>(sourcePoints);
                MBSceneUtilities.RadialSortBoundary(ref boundary);
                MBSceneUtilities.FindConvexHull(ref boundary);
                return boundary.ToList();
            }
            catch
            {
                return sourcePoints.ToList();
            }
        }

        private Dictionary<string, List<GameEntity>> EnsureSideMarkerMap(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return null;

            if (!_boundaryMarkersBySide.TryGetValue(side, out Dictionary<string, List<GameEntity>> markers))
            {
                markers = new Dictionary<string, List<GameEntity>>(StringComparer.Ordinal);
                _boundaryMarkersBySide[side] = markers;
            }

            return markers;
        }

        private Banner ResolveBannerForSide(BattleSideEnum side)
        {
            try
            {
                if (side == BattleSideEnum.Attacker)
                    return Mission?.AttackerTeam?.Banner;
                if (side == BattleSideEnum.Defender)
                    return Mission?.DefenderTeam?.Banner;
            }
            catch
            {
            }

            return null;
        }

        private int MarkLine(Vec2 startPoint, Vec2 endPoint, List<GameEntity> boundary, Banner banner)
        {
            Scene scene = Mission?.Scene;
            if (scene == null || boundary == null)
                return 0;

            Vec3 start = new Vec3(startPoint, 0f, -1f);
            Vec3 end = new Vec3(endPoint, 0f, -1f);
            Vec3 delta = end - start;
            float length = delta.Length;
            if (length <= 0.001f)
                return 0;

            Vec3 step = delta;
            step.Normalize();
            step *= _markerInterval;

            int fallbackMarkerCount = 0;
            for (float distance = 0f; distance < length; distance += _markerInterval)
            {
                MatrixFrame frame = MatrixFrame.Identity;
                frame.rotation.RotateAboutUp(delta.RotationZ + (float)Math.PI / 2f);
                frame.origin = start;
                frame.origin.z = ResolveMarkerHeight(scene, frame.origin.AsVec2);

                Vec3 scale = Vec3.One * 0.45f;
                frame.Scale(in scale);

                GameEntity marker = MakeEntity(scene, banner);
                if (marker != null)
                {
                    marker.SetFrame(ref frame, true);
                    marker.SetVisibilityExcludeParents(true);
                    boundary.Add(marker);
                }

                GameEntity fallbackMarker = MakeFallbackBoundaryMarkerEntity(scene);
                if (fallbackMarker != null)
                {
                    fallbackMarker.SetFrame(ref frame, true);
                    fallbackMarker.SetVisibilityExcludeParents(true);
                    boundary.Add(fallbackMarker);
                    fallbackMarkerCount++;
                }

                start += step;
            }

            return fallbackMarkerCount;
        }

        private float ResolveMarkerHeight(Scene scene, Vec2 point)
        {
            float height = 0f;
            bool hasHeight = false;
            try
            {
                hasHeight = scene.GetHeightAtPoint(point, BoundaryHeightBodyFlags, ref height);
            }
            catch
            {
                hasHeight = false;
            }

            if (!hasHeight)
            {
                try
                {
                    height = scene.GetTerrainHeight(point);
                    scene.GetHeightAtPoint(point, BodyFlags.None, ref height);
                }
                catch
                {
                    height = 0f;
                }
            }

            return height + 0.2f;
        }

        private bool TryCreateBoundaryWallEntity(
            ICollection<Vec2> points,
            BattleSideEnum side,
            List<GameEntity> boundary,
            string boundaryKey)
        {
            Scene scene = Mission?.Scene;
            if (scene == null || points == null || points.Count < 3 || boundary == null)
                return false;

            try
            {
                Mesh mesh = BoundaryWallView.CreateBoundaryMesh(scene, points, ResolveBoundaryWallColor(side));
                if (mesh == null)
                {
                    LogDiagnosticsOnce(
                        "boundary-wall-null|" + side + "|" + (boundaryKey ?? string.Empty),
                        "CoopSiegeDeploymentBoundaryMarkerView: boundary wall mesh was null. " +
                        "Side=" + side +
                        " BoundaryKey=" + (boundaryKey ?? string.Empty) +
                        " PointCount=" + points.Count);
                    return false;
                }

                GameEntity wall = GameEntity.CreateEmpty(scene, true, true, true);
                if (wall == null)
                {
                    LogDiagnosticsOnce(
                        "boundary-wall-entity-null|" + side + "|" + (boundaryKey ?? string.Empty),
                        "CoopSiegeDeploymentBoundaryMarkerView: boundary wall entity was null. " +
                        "Side=" + side +
                        " BoundaryKey=" + (boundaryKey ?? string.Empty));
                    return false;
                }

                wall.AddMesh(mesh, true);
                MatrixFrame identity = MatrixFrame.Identity;
                wall.SetGlobalFrame(in identity, true);
                wall.Name = BoundaryWallEntityName + "_" + side + "_" + (boundaryKey ?? "boundary");
                wall.SetMobility((GameEntity.Mobility)0);
                wall.EntityFlags = (EntityFlags)(wall.EntityFlags | (EntityFlags)1073741824);
                wall.SetVisibilityExcludeParents(true);
                boundary.Add(wall);
                return true;
            }
            catch (Exception ex)
            {
                LogDiagnosticsOnce(
                    "boundary-wall-exception|" + side + "|" + (boundaryKey ?? string.Empty),
                    "CoopSiegeDeploymentBoundaryMarkerView: boundary wall creation failed. " +
                    "Side=" + side +
                    " BoundaryKey=" + (boundaryKey ?? string.Empty) +
                    " Error=" + ex.Message);
                return false;
            }
        }

        private static uint ResolveBoundaryWallColor(BattleSideEnum side)
        {
            if (side == BattleSideEnum.Attacker)
                return new Color(0f, 0.8f, 0.8f).ToUnsignedInteger();

            return new Color(0f, 0f, 0.8f).ToUnsignedInteger();
        }

        private GameEntity MakeEntity(Scene scene, Banner banner)
        {
            if (scene == null)
                return null;

            if (_cachedEntity == null)
            {
                try
                {
                    _cachedEntity = GameEntity.Instantiate(null, _prefabName, false, true, string.Empty);
                }
                catch (Exception ex)
                {
                    LogDiagnosticsOnce(
                        "boundary-prefab-cache-exception",
                        "CoopSiegeDeploymentBoundaryMarkerView: cached boundary prefab instantiate failed. " +
                        "Prefab=" + _prefabName +
                        " Error=" + ex.Message);
                }
            }

            GameEntity entity = null;
            if (_cachedEntity != null)
            {
                try
                {
                    entity = GameEntity.CopyFrom(scene, _cachedEntity, true, true);
                }
                catch (Exception ex)
                {
                    LogDiagnosticsOnce(
                        "boundary-prefab-copy-exception",
                        "CoopSiegeDeploymentBoundaryMarkerView: boundary prefab copy failed. " +
                        "Prefab=" + _prefabName +
                        " Error=" + ex.Message);
                }
            }

            if (entity == null)
            {
                try
                {
                    entity = GameEntity.Instantiate(scene, _prefabName, false, true, string.Empty);
                }
                catch (Exception ex)
                {
                    LogDiagnosticsOnce(
                        "boundary-prefab-direct-exception",
                        "CoopSiegeDeploymentBoundaryMarkerView: direct boundary prefab instantiate failed. " +
                        "Prefab=" + _prefabName +
                        " Error=" + ex.Message);
                }
            }

            if (entity == null)
            {
                LogDiagnosticsOnce(
                    "boundary-prefab-null",
                    "CoopSiegeDeploymentBoundaryMarkerView: boundary prefab entity was null. Prefab=" + _prefabName);
                return null;
            }

            entity.SetMobility(GameEntity.Mobility.Dynamic);
            ApplyBannerMaterial(entity, banner);
            return entity;
        }

        private GameEntity MakeFallbackBoundaryMarkerEntity(Scene scene)
        {
            if (scene == null)
                return null;

            try
            {
                GameEntity entity = GameEntity.CreateEmpty(scene, true, true, true);
                if (entity == null)
                    return null;

                entity.EntityFlags = (EntityFlags)(entity.EntityFlags | (EntityFlags)4194304);
                MetaMesh markerMesh = MetaMesh.GetCopy(BoundaryFallbackMarkerMeshName, true, false);
                if (markerMesh == null)
                {
                    LogDiagnosticsOnce(
                        "boundary-fallback-mesh-null",
                        "CoopSiegeDeploymentBoundaryMarkerView: fallback boundary marker mesh was null. " +
                        "Mesh=" + BoundaryFallbackMarkerMeshName);
                    entity.Remove(103);
                    return null;
                }

                entity.AddComponent(markerMesh);
                entity.SetMobility(GameEntity.Mobility.Dynamic);
                entity.SetVisibilityExcludeParents(true);
                return entity;
            }
            catch (Exception ex)
            {
                LogDiagnosticsOnce(
                    "boundary-fallback-marker-exception",
                    "CoopSiegeDeploymentBoundaryMarkerView: fallback boundary marker creation failed. " +
                    "Mesh=" + BoundaryFallbackMarkerMeshName +
                    " Error=" + ex.Message);
                return null;
            }
        }

        private void ApplyBannerMaterial(GameEntity entity, Banner banner)
        {
            if (entity == null || banner == null)
                return;

            try
            {
                Mesh firstMesh = entity.GetFirstMesh();
                Material sourceMaterial = firstMesh?.GetMaterial();
                if (sourceMaterial == null)
                    return;

                Material tableauMaterial = sourceMaterial.CreateCopy();
                banner.GetTableauTextureSmall(
                    BannerDebugInfo.CreateManual(GetType().Name),
                    texture =>
                    {
                        if (texture != null)
                            tableauMaterial.SetTexture(Material.MBTextureType.DiffuseMap, texture);
                    });
                firstMesh.SetMaterial(tableauMaterial);
            }
            catch
            {
            }
        }

        private void LogDiagnosticsOnce(string key, string message)
        {
            if (!CoopDebugConfig.OrderOfBattleDiagnostics || string.IsNullOrWhiteSpace(key))
                return;

            try
            {
                if (_loggedDiagnosticsKeys.Add(key))
                    ModLogger.Info(message ?? key);
            }
            catch
            {
            }
        }

        private void TryRemoveBoundaryMarkers()
        {
            if (_boundaryMarkersRemoved)
                return;

            foreach (Dictionary<string, List<GameEntity>> markersByBoundary in _boundaryMarkersBySide.Values)
            {
                foreach (List<GameEntity> markers in markersByBoundary.Values.ToList())
                {
                    foreach (GameEntity marker in markers)
                    {
                        try
                        {
                            marker?.Remove(103);
                        }
                        catch
                        {
                        }
                    }
                }

                markersByBoundary.Clear();
            }

            _boundaryMarkersRemoved = true;
        }
    }
#endif

    internal static class CoopSiegeDeploymentBoundaryRuntime
    {
        private const string BoundaryMarkerTypeFullName =
            "TaleWorlds.MountAndBlade.View.MissionViews.Singleplayer.MissionDeploymentBoundaryMarker";
        private const string BoundaryMarkerAssemblyQualifiedName =
            BoundaryMarkerTypeFullName + ", TaleWorlds.MountAndBlade.View";
        private const string BoundaryMarkerPrefabName = "swallowtail_banner";
        private const float BoundaryMarkerInterval = 2f;

        private static readonly FieldInfo DefaultMissionDeploymentPlanTeamDeploymentPlansField =
            typeof(DefaultMissionDeploymentPlan).GetField("_teamDeploymentPlans", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool IsCoopSiegeDeploymentMission(Mission mission)
        {
            if (mission == null ||
                mission.Scene == null ||
                !MissionMultiplayerCoopSiegeAssaultWithDeploymentMode.HasCoopSiegeRuntimeMarker(mission))
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            if (ExactCampaignCommanderDeploymentRuntime
                    .IsExactLandBattleScenario(mission, scenarioContext))
            {
                return false;
            }

            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext))
                return true;

            try
            {
                return mission.IsSiegeBattle;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsCoopCommanderDeploymentPlacementMission(Mission mission)
        {
            if (IsCoopSiegeDeploymentMission(mission))
                return true;

            if (mission == null ||
                mission.Scene == null ||
                !MissionMultiplayerCoopSiegeAssaultWithDeploymentMode.HasCoopSiegeRuntimeMarker(mission))
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            if (!ExactCampaignCommanderDeploymentRuntime
                    .IsExactLandBattleScenario(mission, scenarioContext))
            {
                return false;
            }

            if (GameNetwork.IsClient && !GameNetwork.IsServer)
            {
                return ExactCampaignCommanderDeploymentRuntime
                    .IsClientManualFormationPlacementActive(mission);
            }

            return ExactCampaignCommanderDeploymentRuntime
                .IsDeploymentRuntimeActive(mission);
        }

        public static bool TryEnsureDeploymentPlanBoundaries(Mission mission, Team team, string source)
        {
            if (!IsCoopCommanderDeploymentPlacementMission(mission) ||
                team == null ||
                team.Side == BattleSideEnum.None ||
                !mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) ||
                deploymentPlan == null)
            {
                return false;
            }

            try
            {
                if (!TryEnsureDeploymentPlanHasTeamPlan(deploymentPlan, team))
                    return false;

                if (!TryHasDeploymentBoundaries(deploymentPlan, team))
                {
                    BattleScenarioContextMessage scenarioContext =
                        BattleSnapshotRuntimeState.GetScenarioContext() ??
                        BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                        BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
                    bool isClientExactLandBattlePlacement =
                        GameNetwork.IsClient &&
                        !GameNetwork.IsServer &&
                        ExactCampaignCommanderDeploymentRuntime.IsExactLandBattleScenario(
                            mission,
                            scenarioContext);

                    if (isClientExactLandBattlePlacement)
                    {
                        if (deploymentPlan.IsPlanMade(team))
                            deploymentPlan.ClearDeploymentPlan(team);

                        deploymentPlan.RemakeDeploymentPlan(team);
                        if (!deploymentPlan.IsPlanMade(team))
                            deploymentPlan.MakeDeploymentPlan(team);
                    }
                    else
                    {
                        deploymentPlan.MakeDeploymentPlan(team);
                    }
                }

                return TryHasDeploymentBoundaries(deploymentPlan, team);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryClampCommanderDeploymentPosition(
            Mission mission,
            Team team,
            ref WorldPosition position,
            string source)
        {
            if (!TryEnsureDeploymentPlanBoundaries(mission, team, source) ||
                !mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) ||
                deploymentPlan == null)
            {
                return false;
            }

            try
            {
                Vec2 originalPosition = position.GetGroundVec3().AsVec2;
                if (!originalPosition.IsValid ||
                    deploymentPlan.IsPositionInsideDeploymentBoundaries(team, in originalPosition))
                {
                    return false;
                }

                deploymentPlan.ProjectPositionToDeploymentBoundaries(team, ref position);
                Vec2 projectedPosition = position.GetGroundVec3().AsVec2;
                if (projectedPosition.IsValid &&
                    deploymentPlan.IsPositionInsideDeploymentBoundaries(team, in projectedPosition))
                {
                    return true;
                }

                Vec2 closestPosition = deploymentPlan.GetClosestDeploymentBoundaryPosition(team, in originalPosition);
                if (!closestPosition.IsValid)
                    return false;

                position = CreateWorldPositionOnMissionGround(mission, closestPosition);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryEnsureVisibleDeploymentBoundaryMarkers(
            Mission mission,
            object missionScreen,
            Team team,
            string source)
        {
            if (!GameNetwork.IsClient ||
                GameNetwork.IsServer ||
                !IsCoopCommanderDeploymentPlacementMission(mission) ||
                team == null ||
                team.Side == BattleSideEnum.None)
            {
                return false;
            }

            bool hasTeamDeploymentPlanBoundaries = TryEnsureDeploymentPlanBoundaries(mission, team, source);

#if !COOPSPECTATOR_DEDICATED
            CoopSiegeDeploymentBoundaryMarkerView coopMarker = ResolveExistingCoopBoundaryMarker(mission);
            if (coopMarker == null)
                coopMarker = TryCreateAndAttachCoopBoundaryMarker(mission, missionScreen);
            if (coopMarker != null)
            {
                bool ensuredAny = false;
                if (hasTeamDeploymentPlanBoundaries &&
                    mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan coopDeploymentPlan) &&
                    coopDeploymentPlan != null)
                {
                    ensuredAny = coopMarker.TryEnsureBoundaryMarkersForTeam(
                        coopDeploymentPlan,
                        team,
                        source ?? "unknown");
                }

                if (!ensuredAny)
                {
                    ensuredAny = coopMarker.TryEnsureSceneDeploymentBoundaryMarkersForTeam(
                        team,
                        (source ?? "unknown") + " scene");
                }

                Team enemyTeamForCoopMarker = mission.PlayerEnemyTeam;
                if (enemyTeamForCoopMarker != null && enemyTeamForCoopMarker != team)
                {
                    bool hasEnemyDeploymentPlanBoundaries = TryEnsureDeploymentPlanBoundaries(
                        mission,
                        enemyTeamForCoopMarker,
                        (source ?? "unknown") + " enemy");
                    bool ensuredEnemy = false;
                    if (hasEnemyDeploymentPlanBoundaries &&
                        mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan enemyDeploymentPlan) &&
                        enemyDeploymentPlan != null)
                    {
                        ensuredEnemy = coopMarker.TryEnsureBoundaryMarkersForTeam(
                            enemyDeploymentPlan,
                            enemyTeamForCoopMarker,
                            (source ?? "unknown") + " enemy");
                    }

                    if (!ensuredEnemy)
                    {
                        ensuredEnemy = coopMarker.TryEnsureSceneDeploymentBoundaryMarkersForTeam(
                            enemyTeamForCoopMarker,
                            (source ?? "unknown") + " enemy scene");
                    }

                    ensuredAny |= ensuredEnemy;
                }

                if (ensuredAny)
                    return true;
            }
#endif

            object marker = ResolveExistingBoundaryMarker(mission);
            if (marker == null)
                marker = TryCreateAndAttachBoundaryMarker(mission, missionScreen);
            if (marker == null)
                return false;

            TryEnsureBoundaryMarkerInitialized(marker);
            if (!TryAddDeploymentBoundaryMarkersDirectly(marker, mission, team))
                TryInvokeInstanceMethod(marker, "OnDeploymentPlanMade", team, true);

            Team enemyTeam = mission.PlayerEnemyTeam;
            if (enemyTeam != null && enemyTeam != team)
            {
                TryEnsureDeploymentPlanBoundaries(mission, enemyTeam, source + " enemy");
                if (!TryAddDeploymentBoundaryMarkersDirectly(marker, mission, enemyTeam))
                    TryInvokeInstanceMethod(marker, "OnDeploymentPlanMade", enemyTeam, true);
            }

            return true;
        }

        public static void TryRemoveVisibleDeploymentBoundaryMarkers(
            Mission mission,
            object missionScreen,
            string source)
        {
            try
            {
                List<MissionBehavior> behaviors = mission?.MissionBehaviors;
                if (behaviors == null)
                    return;

                for (int i = behaviors.Count - 1; i >= 0; i--)
                {
                    MissionBehavior behavior = behaviors[i];
                    if (!IsBoundaryMarker(behavior))
                        continue;

                    TryInvokeInstanceMethod(missionScreen, "UnregisterView", behavior);
                    mission.RemoveMissionBehavior(behavior);
                }
            }
            catch
            {
            }
        }

        private static bool TryEnsureDeploymentPlanHasTeamPlan(DefaultMissionDeploymentPlan deploymentPlan, Team team)
        {
            if (deploymentPlan == null || team == null)
                return false;

            if (TryHasDeploymentPlanTeamEntry(deploymentPlan, team))
                return true;

            try
            {
                deploymentPlan.Initialize();
            }
            catch
            {
                return false;
            }

            return TryHasDeploymentPlanTeamEntry(deploymentPlan, team);
        }

        private static bool TryHasDeploymentPlanTeamEntry(DefaultMissionDeploymentPlan deploymentPlan, Team team)
        {
            if (DefaultMissionDeploymentPlanTeamDeploymentPlansField == null)
                return true;

            try
            {
                if (!(DefaultMissionDeploymentPlanTeamDeploymentPlansField.GetValue(deploymentPlan) is System.Collections.IEnumerable entries))
                    return false;

                foreach (object entry in entries)
                {
                    if (entry == null)
                        continue;

                    FieldInfo item1Field = entry.GetType().GetField("Item1", BindingFlags.Instance | BindingFlags.Public);
                    if (item1Field != null && ReferenceEquals(item1Field.GetValue(entry), team))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryHasDeploymentBoundaries(DefaultMissionDeploymentPlan deploymentPlan, Team team)
        {
            try
            {
                return deploymentPlan.HasDeploymentBoundaries(team);
            }
            catch
            {
                return false;
            }
        }

        private static WorldPosition CreateWorldPositionOnMissionGround(Mission mission, Vec2 position)
        {
            float height = mission.Scene.GetTerrainHeight(position);
            mission.Scene.GetHeightAtPoint(position, BodyFlags.None, ref height);
            return new WorldPosition(
                mission.Scene,
                UIntPtr.Zero,
                new Vec3(position, height),
                hasValidZ: false);
        }

#if !COOPSPECTATOR_DEDICATED
        private static CoopSiegeDeploymentBoundaryMarkerView TryCreateAndAttachCoopBoundaryMarker(
            Mission mission,
            object missionScreen)
        {
            try
            {
                if (mission == null)
                    return null;

                CoopSiegeDeploymentBoundaryMarkerView marker =
                    new CoopSiegeDeploymentBoundaryMarkerView(BoundaryMarkerPrefabName, BoundaryMarkerInterval);

                if (!TryInvokeInstanceMethod(missionScreen, "AddMissionView", marker))
                    mission.AddMissionBehavior(marker);

                marker.AfterStart();
                return marker;
            }
            catch (Exception ex)
            {
                if (CoopDebugConfig.OrderOfBattleDiagnostics)
                    ModLogger.Info("CoopSiegeDeploymentBoundaryRuntime: coop boundary marker attach failed. Error=" + ex.Message);
                return null;
            }
        }

        private static CoopSiegeDeploymentBoundaryMarkerView ResolveExistingCoopBoundaryMarker(Mission mission)
        {
            try
            {
                List<MissionBehavior> behaviors = mission?.MissionBehaviors;
                if (behaviors == null)
                    return null;

                foreach (MissionBehavior behavior in behaviors)
                {
                    if (behavior is CoopSiegeDeploymentBoundaryMarkerView marker)
                        return marker;
                }
            }
            catch
            {
            }

            return null;
        }
#endif

        private static object TryCreateAndAttachBoundaryMarker(Mission mission, object missionScreen)
        {
            try
            {
                Type markerType = ResolveBoundaryMarkerType();
                if (markerType == null)
                    return null;

                object marker = Activator.CreateInstance(markerType, BoundaryMarkerPrefabName, BoundaryMarkerInterval);
                if (!(marker is MissionBehavior markerBehavior))
                    return null;

                if (!TryInvokeInstanceMethod(missionScreen, "AddMissionView", marker))
                {
                    mission.AddMissionBehavior(markerBehavior);
                }

                return marker;
            }
            catch
            {
                return null;
            }
        }

        private static Type ResolveBoundaryMarkerType()
        {
            Type type = Type.GetType(BoundaryMarkerAssemblyQualifiedName, throwOnError: false);
            if (type != null)
                return type;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(BoundaryMarkerTypeFullName, throwOnError: false);
                    if (type != null)
                        return type;
                }
                catch
                {
                }
            }

            return null;
        }

        private static object ResolveExistingBoundaryMarker(Mission mission)
        {
            try
            {
                List<MissionBehavior> behaviors = mission?.MissionBehaviors;
                if (behaviors == null)
                    return null;

                foreach (MissionBehavior behavior in behaviors)
                {
                    if (IsNativeBoundaryMarker(behavior))
                        return behavior;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsBoundaryMarker(object value)
        {
#if !COOPSPECTATOR_DEDICATED
            if (value is CoopSiegeDeploymentBoundaryMarkerView)
                return true;
#endif

            return IsNativeBoundaryMarker(value);
        }

        private static bool IsNativeBoundaryMarker(object value)
        {
            Type type = value?.GetType();
            return type != null &&
                   (string.Equals(type.FullName, BoundaryMarkerTypeFullName, StringComparison.Ordinal) ||
                    string.Equals(type.Name, "MissionDeploymentBoundaryMarker", StringComparison.Ordinal));
        }

        private static void TryEnsureBoundaryMarkerInitialized(object marker)
        {
            try
            {
                FieldInfo markerField = marker
                    .GetType()
                    .GetField("_boundaryMarkersPerSide", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (markerField?.GetValue(marker) is Array markerArray &&
                    markerArray.Length >= 2 &&
                    markerArray.GetValue(0) != null &&
                    markerArray.GetValue(1) != null)
                {
                    return;
                }
            }
            catch
            {
            }

            TryInvokeInstanceMethod(marker, "AfterStart");
        }

        private static bool TryAddDeploymentBoundaryMarkersDirectly(
            object marker,
            Mission mission,
            Team team)
        {
            if (marker == null ||
                !TryEnsureDeploymentPlanBoundaries(mission, team, "visible-marker-direct") ||
                !mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) ||
                deploymentPlan == null)
            {
                return false;
            }

            bool addedAny = false;
            try
            {
                foreach (var boundary in deploymentPlan.GetDeploymentBoundaries(team))
                {
                    var markerBoundary = new KeyValuePair<string, ICollection<Vec2>>(
                        boundary.Item1,
                        boundary.Item2);
                    addedAny |= TryInvokeInstanceMethod(
                        marker,
                        "AddBoundaryMarkerForSide",
                        team.Side,
                        markerBoundary);
                }
            }
            catch
            {
                return false;
            }

            return addedAny;
        }

        private static bool TryInvokeInstanceMethod(object target, string methodName, params object[] arguments)
        {
            if (target == null || string.IsNullOrWhiteSpace(methodName))
                return false;

            try
            {
                MethodInfo method = FindInstanceMethod(target.GetType(), methodName, arguments);
                if (method == null)
                    return false;

                method.Invoke(target, arguments);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static MethodInfo FindInstanceMethod(Type type, string methodName, object[] arguments)
        {
            for (Type currentType = type; currentType != null; currentType = currentType.BaseType)
            {
                MethodInfo[] methods = currentType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (MethodInfo method in methods)
                {
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                        continue;

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != (arguments?.Length ?? 0))
                        continue;

                    bool matches = true;
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        object argument = arguments[i];
                        if (argument != null && !parameters[i].ParameterType.IsInstanceOfType(argument))
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (matches)
                        return method;
                }
            }

            return null;
        }
    }

    public sealed class CoopMissionNetworkBridge : MissionNetwork
    {
        internal readonly struct ClientBattleSnapshotProgressInfo
        {
            public ClientBattleSnapshotProgressInfo(
                int transmissionId,
                int chunkCount,
                int receivedChunkCount,
                int highestContiguousChunkIndex,
                bool isStalled)
            {
                TransmissionId = transmissionId;
                ChunkCount = Math.Max(0, chunkCount);
                ReceivedChunkCount = Math.Max(0, receivedChunkCount);
                HighestContiguousChunkIndex = Math.Max(-1, highestContiguousChunkIndex);
                IsStalled = isStalled;
            }

            public int TransmissionId { get; }
            public int ChunkCount { get; }
            public int ReceivedChunkCount { get; }
            public int HighestContiguousChunkIndex { get; }
            public bool IsStalled { get; }
            public int PercentComplete =>
                ChunkCount <= 0
                    ? 0
                    : Math.Max(0, Math.Min(100, (int)Math.Round((double)ReceivedChunkCount * 100d / ChunkCount)));
        }

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };
        private static readonly bool UseBattleSnapshotTransportV2 = true;
        private const int BattleSnapshotTransportSchemaVersion = 1;
        private const int MaxStatusChunksPerPayloadPerTick = 2;
        private const int MaxBattleSnapshotChunksPerPayloadPerTick = 2;
        private static readonly TimeSpan BattleSnapshotAckRetryDelay = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan BattleSnapshotManifestRetryDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan BattleSnapshotRangeAckStallDelay = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan BattleSnapshotInitialChunkRequestRetryDelay = TimeSpan.FromMilliseconds(350);
        private static readonly TimeSpan BattleSnapshotAssemblyIdleTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan BattleSnapshotBootstrapRequestRetryDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan BattleReconnectFinalizeReadyAckRetryDelay = TimeSpan.FromMilliseconds(900);
        private static readonly TimeSpan BootstrapDependentPeerPayloadSyncInterval = TimeSpan.FromMilliseconds(200);
        private static readonly FieldInfo FormationEnforceNotSplittableByAiField =
            typeof(Formation).GetField("_enforceNotSplittableByAI", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CommanderDeploymentPointWeaponsField =
            typeof(DeploymentPoint).GetField("_weapons", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CommanderDeploymentPointOnDeploymentStateChangedField =
            typeof(DeploymentPoint).GetField(
                "OnDeploymentStateChanged",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo CommanderDeploymentPointDetermineTypeMethod =
            typeof(DeploymentPoint).GetMethod("DetermineDeploymentPointType", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CommanderDeploymentSiegeControllerWeaponsField =
            typeof(MissionSiegeWeaponsController).GetField("_weapons", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CommanderDeploymentSiegeControllerUndeployedWeaponsField =
            typeof(MissionSiegeWeaponsController).GetField("_undeployedWeapons", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CommanderDeploymentSiegeControllerDeployedWeaponsField =
            typeof(MissionSiegeWeaponsController).GetField("_deployedWeapons", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CommanderDeploymentSiegeWeaponRemoveOnDeployEntitiesField =
            typeof(SiegeWeapon).GetField("_removeOnDeployEntities", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CommanderDeploymentSiegeWeaponAddOnDeployEntitiesField =
            typeof(SiegeWeapon).GetField("_addOnDeployEntities", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CommanderDeploymentSiegeWeaponRemoveOnDeployTagField =
            typeof(SiegeWeapon).GetField("RemoveOnDeployTag", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CommanderDeploymentSiegeWeaponAddOnDeployTagField =
            typeof(SiegeWeapon).GetField("AddOnDeployTag", BindingFlags.Instance | BindingFlags.NonPublic);
        private const int BattleSnapshotInitialWindowChunks = 4;
        private const int BattleSnapshotMaxInflightChunksPerPeer = 8;
        private const int BattleSnapshotRangeAckEveryNewChunks = 4;
        private const int BattleSnapshotMaxConcurrentHeavyPeers = 4;

        private readonly Dictionary<int, string> _lastSentStatusPayloadByPeer = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _lastSentMaterializedAgentEntryPayloadByPeer = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _lastSentBattleSnapshotPayloadByPeer = new Dictionary<int, string>();
        private readonly Dictionary<int, DateTime> _lastCompletedBattleSnapshotTransmissionUtcByPeer = new Dictionary<int, DateTime>();
        private readonly Dictionary<int, DateTime> _lastBattleSnapshotRetryUtcByPeer = new Dictionary<int, DateTime>();
        private readonly Dictionary<int, int> _lastSentAgentControlRevisionByPeer = new Dictionary<int, int>();
        private readonly Dictionary<string, PendingPayloadTransmission> _pendingPayloadsByKey = new Dictionary<string, PendingPayloadTransmission>(StringComparer.Ordinal);
        private readonly Dictionary<string, PayloadAssemblyState> _clientPayloadAssemblies = new Dictionary<string, PayloadAssemblyState>(StringComparer.Ordinal);
        private readonly Dictionary<int, BattleSnapshotTransportState> _battleSnapshotTransportStatesByPeer = new Dictionary<int, BattleSnapshotTransportState>();
        private readonly Dictionary<int, BattleSnapshotClientAssemblyState> _clientBattleSnapshotAssembliesByTransmission = new Dictionary<int, BattleSnapshotClientAssemblyState>();
        private readonly Dictionary<BattleSideEnum, Dictionary<int, string>> _delegatedCaptainEntryIdByFormationIndexAndSide =
            new Dictionary<BattleSideEnum, Dictionary<int, string>>();
        private readonly Dictionary<BattleSideEnum, HashSet<int>> _voluntarilyAiControlledFormationIndicesBySide =
            new Dictionary<BattleSideEnum, HashSet<int>>();
        private readonly Dictionary<BattleSideEnum, string> _lastBroadcastDelegatedCaptainAssignmentKeyBySide =
            new Dictionary<BattleSideEnum, string>();
        private readonly Dictionary<string, int> _lastObservedFormationUnitCountByTeamAndIndex =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<NetworkCommunicator, HashSet<string>> _sentSiegeMissionObjectMapKeysByPeer =
            new Dictionary<NetworkCommunicator, HashSet<string>>();
        private static readonly Dictionary<int, int> _expectedBattleSnapshotTransmissionIdByPeer = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> _acknowledgedBattleSnapshotTransmissionIdByPeer = new Dictionary<int, int>();
        private static int _clientObservedBattleSnapshotTransmissionId;
        private static string _clientObservedBattleSnapshotPayloadHash = string.Empty;
        private static int _clientAppliedBattleSnapshotTransmissionId;
        private static string _clientAppliedBattleSnapshotPayloadHash = string.Empty;
        private string _cachedBattleSnapshotComparisonKey = string.Empty;
        private byte[] _cachedBattleSnapshotPayloadBytes = Array.Empty<byte>();
        private int _cachedBattleSnapshotLogicalBytes;
        private string _cachedBattleSnapshotPayloadHash = string.Empty;
        private CoopBattleSnapshotCompressionKind _cachedBattleSnapshotCompressionKind = CoopBattleSnapshotCompressionKind.None;
        private string _cachedBattleSnapshotV2ComparisonKey = string.Empty;
        private byte[] _cachedBattleSnapshotV2PayloadBytes = Array.Empty<byte>();
        private int _cachedBattleSnapshotV2LogicalBytes;
        private string _cachedBattleSnapshotV2PayloadHash = string.Empty;
        private CoopBattleSnapshotCompressionKind _cachedBattleSnapshotV2CompressionKind = CoopBattleSnapshotCompressionKind.None;
        private CoopBattleSnapshotPayloadEncoding _cachedBattleSnapshotV2PayloadEncoding = CoopBattleSnapshotPayloadEncoding.JsonUtf8;
        private long _cachedMaterializedAgentEntryContentRevision = -1;
        private CoopBattleEntryStatusBridgeFile.AuthoritativeMaterializedAgentEntrySnapshot _cachedMaterializedAgentEntrySnapshot;
        private string _cachedMaterializedAgentEntryComparisonJson = string.Empty;
        private int _nextTransmissionId = 1;
        private bool _persistedHostedLocalPeerMarker;
        private DateTime _lastClientBattleSnapshotBootstrapRequestUtc = DateTime.MinValue;
        private DateTime _lastClientBattleReconnectFinalizeReadyAckUtc = DateTime.MinValue;
        private DateTime _nextBootstrapDependentPeerPayloadSyncUtc = DateTime.MinValue;
        private int _lastClientBattleReconnectFinalizeReadyAckTransmissionId;
        private string _lastClientBattleReconnectFinalizeReadinessSummary = string.Empty;
        private bool _delayedClientAgentControlDiagnosticsPending;
        private int _delayedClientAgentControlDiagnosticsTicksRemaining;
        private CoopBattleAgentControlState _delayedClientAgentControlDiagnosticsState;
        private string _delayedClientAgentControlFormationOwnershipSyncResult = string.Empty;
        private string _delayedClientAgentControlMainAgentSyncResult = string.Empty;

        internal static bool TryGetClientBattleSnapshotProgress(out ClientBattleSnapshotProgressInfo progress)
        {
            progress = default(ClientBattleSnapshotProgressInfo);

            if (!GameNetwork.IsClient)
                return false;

            Mission mission = Mission.Current;
            if (mission == null)
                return false;

            CoopMissionNetworkBridge bridge = mission.GetMissionBehavior<CoopMissionNetworkBridge>();
            if (bridge == null || bridge._clientBattleSnapshotAssembliesByTransmission.Count <= 0)
                return false;

            BattleSnapshotClientAssemblyState assemblyState = bridge._clientBattleSnapshotAssembliesByTransmission.Values
                .Where(state => state != null)
                .OrderByDescending(state => state.LastChunkReceivedUtc)
                .ThenByDescending(state => state.LastManifestObservedUtc)
                .FirstOrDefault();
            if (assemblyState == null)
                return false;

            bool isStalled =
                !assemblyState.IsComplete &&
                DateTime.UtcNow - assemblyState.LastUsefulChunkReceivedUtc >= BattleSnapshotRangeAckStallDelay;
            progress = new ClientBattleSnapshotProgressInfo(
                assemblyState.TransmissionId,
                assemblyState.ChunkCount,
                assemblyState.ReceivedChunkCount,
                assemblyState.HighestContiguousChunkIndex,
                isStalled);
            return true;
        }

        public static bool TryBroadcastCommanderDeploymentSiegeMachineState(
            Mission mission,
            BattleSideEnum side,
            out string diagnostics,
            string source)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (!GameNetwork.IsServer || side == BattleSideEnum.None)
            {
                diagnostics =
                    "invalid-context IsServer=" + GameNetwork.IsServer +
                    " Side=" + side;
                return false;
            }

            CoopMissionNetworkBridge bridge = mission.GetMissionBehavior<CoopMissionNetworkBridge>();
            if (bridge == null)
            {
                diagnostics = "bridge-missing";
                return false;
            }

            return bridge.TryBroadcastCommanderDeploymentSiegeMachineState(side, out diagnostics, source);
        }

        protected override void AddRemoveMessageHandlers(GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            if (GameNetwork.IsServer)
            {
                registerer.RegisterBaseHandler<CoopBattleSelectionClientRequestMessage>(HandleClientSelectionRequest);
                registerer.RegisterBaseHandler<CoopBattleAgentControlRequestMessage>(HandleClientAgentControlRequest);
                registerer.RegisterBaseHandler<CoopCommanderDeploymentFormationAssignmentsMessage>(HandleClientCommanderDeploymentFormationAssignments);
                registerer.RegisterBaseHandler<CoopCommanderDeploymentSiegeMachineSelectionMessage>(HandleClientCommanderDeploymentSiegeMachineSelection);
                registerer.RegisterBaseHandler<CoopBattleSnapshotChunkRequestMessage>(HandleClientBattleSnapshotChunkRequest);
                registerer.RegisterBaseHandler<CoopBattleSnapshotRangeAckMessage>(HandleClientBattleSnapshotRangeAck);
                registerer.RegisterBaseHandler<CoopBattleSnapshotCompleteAckMessage>(HandleClientBattleSnapshotCompleteAck);
                registerer.RegisterBaseHandler<CoopBattleSnapshotAbortMessage>(HandleClientBattleSnapshotAbort);
                ModLogger.Info("CoopMissionNetworkBridge: registered server selection request handler.");
            }

            if (GameNetwork.IsClient)
            {
                registerer.RegisterBaseHandler<CoopBattlePayloadChunkMessage>(HandleServerPayloadChunk);
                registerer.RegisterBaseHandler<CoopBattleAgentControlStateMessage>(HandleServerAgentControlState);
                registerer.RegisterBaseHandler<CoopBattleSnapshotManifestMessage>(HandleServerBattleSnapshotManifest);
                registerer.RegisterBaseHandler<CoopBattleSnapshotChunkV2Message>(HandleServerBattleSnapshotChunkV2);
                registerer.RegisterBaseHandler<CoopDelegatedCaptainAssignmentsStateMessage>(HandleServerDelegatedCaptainAssignmentsState);
                registerer.RegisterBaseHandler<CoopCommanderDeploymentSiegeMachineStateMessage>(HandleServerCommanderDeploymentSiegeMachineState);
                registerer.RegisterBaseHandler<CoopSiegeMissionObjectIdMapMessage>(HandleServerSiegeMissionObjectIdMap);
                ModLogger.Info("CoopMissionNetworkBridge: registered client payload chunk handler.");
            }
        }

        protected override void OnUdpNetworkHandlerTick()
        {
            if (!GameNetwork.IsServer || Mission == null)
                return;

            TrySyncBattleSnapshotPayloads();

            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextBootstrapDependentPeerPayloadSyncUtc)
                return;

            _nextBootstrapDependentPeerPayloadSyncUtc = nowUtc + BootstrapDependentPeerPayloadSyncInterval;
            TrySyncAgentControlStates();
            TrySyncMaterializedAgentEntryPayloads();
            TrySyncEntryStatusPayloads();
        }

        public override void OnPreMissionTick(float dt)
        {
            base.OnPreMissionTick(dt);
            TryRunClientBattleSnapshotRecoveryTick();
            TryRunDelayedClientAgentControlDiagnostics();
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
        }

        private void TryRunClientBattleSnapshotRecoveryTick()
        {
            TryPersistHostedLocalPeerMarker();

            if (GameNetwork.IsClient && Mission != null)
            {
                TryRequestClientBattleSnapshotBootstrapIfNeeded();
                TryResendClientBattleSnapshotChunkRequests();
                BattleMapSpawnHandoffPatch.TryProcessDeferredClientCreateAgentMessages(
                    Mission,
                    "CoopMissionNetworkBridge.TryRunClientBattleSnapshotRecoveryTick");
                BattleMapSpawnHandoffPatch.TryProcessDeferredClientMountedHeroCreateAgents(
                    Mission,
                    "CoopMissionNetworkBridge.TryRunClientBattleSnapshotRecoveryTick");
                TrySendClientBattleReconnectFinalizeReadyAckIfNeeded();
            }
        }

        private void TryResendClientBattleSnapshotChunkRequests()
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || _clientBattleSnapshotAssembliesByTransmission.Count <= 0)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            foreach (BattleSnapshotClientAssemblyState assemblyState in _clientBattleSnapshotAssembliesByTransmission.Values
                .Where(state => state != null)
                .ToArray())
            {
                if (assemblyState.IsComplete)
                    continue;

                TimeSpan effectiveRetryDelay =
                    assemblyState.ReceivedChunkCount <= 0
                        ? BattleSnapshotInitialChunkRequestRetryDelay
                        : BattleSnapshotRangeAckStallDelay;
                bool receiveStalled = nowUtc - assemblyState.LastUsefulChunkReceivedUtc >= effectiveRetryDelay;
                bool requestCooldownElapsed =
                    assemblyState.LastControlMessageSentUtc == DateTime.MinValue ||
                    nowUtc - assemblyState.LastControlMessageSentUtc >= effectiveRetryDelay;
                if (!receiveStalled || !requestCooldownElapsed)
                    continue;

                if (assemblyState.ReceivedChunkCount <= 0)
                {
                    SendClientBattleSnapshotChunkRequest(assemblyState, CoopBattleSnapshotAssemblyStateKind.Stalled, "stalled-initial-request");
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: resent stalled initial client V2 battle snapshot chunk request. " +
                        "TransmissionId=" + assemblyState.TransmissionId +
                        " HighestContiguous=" + assemblyState.HighestContiguousChunkIndex +
                        " ReceivedChunkCount=" + assemblyState.ReceivedChunkCount +
                        " ChunkCount=" + assemblyState.ChunkCount);
                    continue;
                }

                SendClientBattleSnapshotRangeAck(assemblyState, CoopBattleSnapshotAssemblyStateKind.Stalled, "stalled-progress-ack");
                ModLogger.Info(
                    "CoopMissionNetworkBridge: resent stalled client V2 battle snapshot range ack. " +
                    "TransmissionId=" + assemblyState.TransmissionId +
                    " HighestContiguous=" + assemblyState.HighestContiguousChunkIndex +
                    " ReceivedChunkCount=" + assemblyState.ReceivedChunkCount +
                    " ChunkCount=" + assemblyState.ChunkCount);
            }
        }

        private void TryRequestClientBattleSnapshotBootstrapIfNeeded()
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || Mission == null)
                return;

            if (_clientObservedBattleSnapshotTransmissionId > 0 ||
                _clientAppliedBattleSnapshotTransmissionId > 0 ||
                _clientBattleSnapshotAssembliesByTransmission.Count > 0)
            {
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            if (_lastClientBattleSnapshotBootstrapRequestUtc != DateTime.MinValue &&
                nowUtc - _lastClientBattleSnapshotBootstrapRequestUtc < BattleSnapshotBootstrapRequestRetryDelay)
            {
                return;
            }

            if (!CoopBattleNetworkRequestTransport.TryRequestBattleSnapshotBootstrap("CoopMissionNetworkBridge bootstrap recovery tick"))
                return;

            _lastClientBattleSnapshotBootstrapRequestUtc = nowUtc;
            ModLogger.Info(
                "CoopMissionNetworkBridge: requested battle snapshot bootstrap from client recovery tick. " +
                "ObservedTransmissionId=" + _clientObservedBattleSnapshotTransmissionId +
                " AppliedTransmissionId=" + _clientAppliedBattleSnapshotTransmissionId +
                " PendingAssemblies=" + _clientBattleSnapshotAssembliesByTransmission.Count);
        }

        private void TrySendClientBattleReconnectFinalizeReadyAckIfNeeded()
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || Mission == null)
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (status?.HasAgent == true)
                return;

            if (_clientAppliedBattleSnapshotTransmissionId <= 0)
                return;

            if (!CoopMissionSpawnLogic.IsClientReconnectFinalizeReady(out string readinessSummary))
            {
                _lastClientBattleReconnectFinalizeReadinessSummary = readinessSummary ?? string.Empty;
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            bool shouldSend =
                _lastClientBattleReconnectFinalizeReadyAckTransmissionId != _clientAppliedBattleSnapshotTransmissionId ||
                _lastClientBattleReconnectFinalizeReadyAckUtc == DateTime.MinValue ||
                nowUtc - _lastClientBattleReconnectFinalizeReadyAckUtc >= BattleReconnectFinalizeReadyAckRetryDelay;
            if (!shouldSend)
                return;

            bool acknowledged = CoopBattleNetworkRequestTransport.TryAcknowledgeBattleReconnectFinalize(
                _clientAppliedBattleSnapshotTransmissionId,
                "CoopMissionNetworkBridge.TryRunClientBattleSnapshotRecoveryTick reconnect-finalize-ready");
            _lastClientBattleReconnectFinalizeReadinessSummary = readinessSummary ?? string.Empty;
            if (!acknowledged)
                return;

            _lastClientBattleReconnectFinalizeReadyAckTransmissionId = _clientAppliedBattleSnapshotTransmissionId;
            _lastClientBattleReconnectFinalizeReadyAckUtc = nowUtc;
            ModLogger.Info(
                "CoopMissionNetworkBridge: sent client reconnect finalize ready ack. " +
                "TransmissionId=" + _clientAppliedBattleSnapshotTransmissionId +
                " ReadinessSummary=" + (_lastClientBattleReconnectFinalizeReadinessSummary ?? "unknown"));
        }

        private void TryPersistHostedLocalPeerMarker()
        {
            if (_persistedHostedLocalPeerMarker || !GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer || string.IsNullOrWhiteSpace(myPeer.UserName))
                return;

            if (HostSelfJoinRedirectState.TryPersistJoinedLocalHostPeer(
                    myPeer.UserName,
                    "CoopMissionNetworkBridge.OnUdpNetworkHandlerTick"))
            {
                _persistedHostedLocalPeerMarker = true;
            }
        }

        protected override void HandleNewClientAfterSynchronized(NetworkCommunicator networkPeer)
        {
            base.HandleNewClientAfterSynchronized(networkPeer);

            if (!GameNetwork.IsServer || networkPeer == null || networkPeer.IsServerPeer)
                return;

            // The authoritative sync path already runs in OnUdpNetworkHandlerTick().
            // Sending chunked payloads directly from the synchronized callback crashes the
            // dedicated runtime while writing EntryStatusSnapshot packets, so only arm the
            // next UDP tick here and let the regular sync loop send the initial payloads.
            ModLogger.Info(
                "CoopMissionNetworkBridge: deferred initial payload sync to UDP tick. " +
                "Peer=" + (networkPeer.UserName ?? "null") +
                " Reason=post-synchronize callback safety.");
        }

        protected override void HandleNewClientAfterLoadingFinished(NetworkCommunicator networkPeer)
        {
            base.HandleNewClientAfterLoadingFinished(networkPeer);
            TryArmActiveBattleReconnectFinalizeGate(
                networkPeer,
                transmissionId: 0,
                "CoopMissionNetworkBridge.HandleNewClientAfterLoadingFinished");
            TryPrimePreSynchronizedBattleSnapshotBootstrap(
                networkPeer,
                "CoopMissionNetworkBridge.HandleNewClientAfterLoadingFinished");
        }

        protected override void HandlePlayerDisconnect(NetworkCommunicator networkPeer)
        {
            base.HandlePlayerDisconnect(networkPeer);

            if (networkPeer == null)
                return;

            _lastSentStatusPayloadByPeer.Remove(networkPeer.Index);
            _lastSentMaterializedAgentEntryPayloadByPeer.Remove(networkPeer.Index);
            _lastSentBattleSnapshotPayloadByPeer.Remove(networkPeer.Index);
            _lastCompletedBattleSnapshotTransmissionUtcByPeer.Remove(networkPeer.Index);
            _lastBattleSnapshotRetryUtcByPeer.Remove(networkPeer.Index);
            _lastSentAgentControlRevisionByPeer.Remove(networkPeer.Index);
            _pendingPayloadsByKey.Remove(BuildPendingTransmissionKey(networkPeer.Index, CoopBattlePayloadKind.AuthoritativeMaterializedAgentEntrySnapshot));
            _pendingPayloadsByKey.Remove(BuildPendingTransmissionKey(networkPeer.Index, CoopBattlePayloadKind.EntryStatusSnapshot));
            _pendingPayloadsByKey.Remove(BuildPendingTransmissionKey(networkPeer.Index, CoopBattlePayloadKind.BattleSnapshot));
            _battleSnapshotTransportStatesByPeer.Remove(networkPeer.Index);
            ClearPeerBattleSnapshotSyncState(networkPeer.Index);
            CoopBattlePeerReconnectState.ObserveDisconnect(
                networkPeer,
                "CoopMissionNetworkBridge.HandlePlayerDisconnect");
            LateJoinPeerBootstrapGatePatch.ClearDeferredPeerBootstrap(
                networkPeer,
                "CoopMissionNetworkBridge.HandlePlayerDisconnect");
        }

        public override void OnRemoveBehavior()
        {
            _lastSentStatusPayloadByPeer.Clear();
            _lastSentMaterializedAgentEntryPayloadByPeer.Clear();
            _lastSentBattleSnapshotPayloadByPeer.Clear();
            _lastCompletedBattleSnapshotTransmissionUtcByPeer.Clear();
            _lastBattleSnapshotRetryUtcByPeer.Clear();
            _lastSentAgentControlRevisionByPeer.Clear();
            _pendingPayloadsByKey.Clear();
            _clientPayloadAssemblies.Clear();
            _battleSnapshotTransportStatesByPeer.Clear();
            _clientBattleSnapshotAssembliesByTransmission.Clear();
            _delegatedCaptainEntryIdByFormationIndexAndSide.Clear();
            _voluntarilyAiControlledFormationIndicesBySide.Clear();
            _lastBroadcastDelegatedCaptainAssignmentKeyBySide.Clear();
            _lastObservedFormationUnitCountByTeamAndIndex.Clear();
            _expectedBattleSnapshotTransmissionIdByPeer.Clear();
            _acknowledgedBattleSnapshotTransmissionIdByPeer.Clear();
            CoopBattlePeerReconnectState.Reset("CoopMissionNetworkBridge.OnRemoveBehavior");
            LateJoinPeerBootstrapGatePatch.ClearAllDeferredPeerBootstrap(
                "CoopMissionNetworkBridge.OnRemoveBehavior");
            ClearClientBattleSnapshotApplicationState("CoopMissionNetworkBridge.OnRemoveBehavior");
            _lastClientBattleSnapshotBootstrapRequestUtc = DateTime.MinValue;
            _lastClientBattleReconnectFinalizeReadyAckUtc = DateTime.MinValue;
            _lastClientBattleReconnectFinalizeReadyAckTransmissionId = 0;
            _lastClientBattleReconnectFinalizeReadinessSummary = string.Empty;
            _cachedMaterializedAgentEntryContentRevision = -1;
            _cachedMaterializedAgentEntrySnapshot = null;
            _cachedMaterializedAgentEntryComparisonJson = string.Empty;
            if (GameNetwork.IsServer)
                CoopBattleAgentControlRuntimeState.ResetServer("CoopMissionNetworkBridge.OnRemoveBehavior");
            if (GameNetwork.IsClient)
                CoopBattleAgentControlRuntimeState.ResetClient("CoopMissionNetworkBridge.OnRemoveBehavior");
            base.OnRemoveBehavior();
        }

        private bool HandleClientAgentControlRequest(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleAgentControlRequestMessage message))
                return false;

            try
            {
                bool applied = CoopMissionSpawnLogic.TryHandleAgentControlRequest(
                    Mission,
                    peer,
                    message.RequestKind,
                    message.ExpectedAgentIndex,
                    message.RequestId,
                    "CoopMissionNetworkBridge",
                    out CoopBattleAgentControlState state,
                    out string reason);

                if (state.PeerIndex < 0)
                {
                    MissionPeer missionPeer = peer?.GetComponent<MissionPeer>();
                    Agent controlledAgent = missionPeer?.ControlledAgent;
                    state = new CoopBattleAgentControlState(
                        peer?.Index ?? -1,
                        CoopBattleAgentControlMode.PlayerControlled,
                        controlledAgent?.Index ?? -1,
                        CoopBattleAuthorityState.GetSelectedEntryId(missionPeer),
                        (controlledAgent?.Character as BasicCharacterObject)?.StringId,
                        controlledAgent?.Team?.Side ?? BattleSideEnum.None,
                        controlledAgent?.Team?.TeamIndex ?? -1,
                        controlledAgent?.Formation?.Index ?? -1,
                        false,
                        false,
                        0,
                        message.RequestId,
                        reason ?? string.Empty,
                        DateTime.UtcNow);
                }

                TrySendAgentControlStateToPeer(peer, state, force: true);
                ModLogger.Info(
                    "CoopMissionNetworkBridge: handled agent control request. " +
                    "Peer=" + (peer?.UserName ?? "null") +
                    " Kind=" + message.RequestKind +
                    " ExpectedAgentIndex=" + message.ExpectedAgentIndex +
                    " RequestId=" + message.RequestId +
                    " Applied=" + applied +
                    " ResultMode=" + state.Mode +
                    " ResultAgentIndex=" + state.AgentIndex +
                    " Reason=" + (reason ?? "unknown"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: agent control request handling failed: " + ex.Message);
            }

            return true;
        }

        private bool HandleClientSelectionRequest(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSelectionClientRequestMessage message))
                return false;

            try
            {
                if (message.RequestKind == CoopBattleSelectionRequestKind.BattleSnapshotBootstrapRequest)
                {
                    TryHandleBattleSnapshotBootstrapRequest(peer);
                    return true;
                }

                if (message.RequestKind == CoopBattleSelectionRequestKind.BattleSnapshotReadyAck)
                {
                    bool acknowledged = TryAcknowledgePeerBattleSnapshot(peer, message.SelectionId);
                    if (acknowledged)
                    {
                        TrySendImmediatePeerStatusPayloads(peer);
                        LateJoinPeerBootstrapGatePatch.TryReplayDeferredPeerBootstrap(
                            peer,
                            "CoopMissionNetworkBridge.HandleClientSelectionRequest BattleSnapshotReadyAck");
                    }
                    return true;
                }

                if (message.RequestKind == CoopBattleSelectionRequestKind.BattleReconnectFinalizeReadyAck)
                {
                    bool acknowledged = TryAcknowledgePeerBattleReconnectFinalize(peer, message.SelectionId);
                    if (acknowledged)
                        TrySendEntryStatusToPeer(peer, force: true);
                    return true;
                }

                bool applied = CoopMissionSpawnLogic.TryHandleNetworkSelectionRequest(
                    Mission,
                    peer,
                    message.RequestKind,
                    message.RequestedSide,
                    message.SelectionId,
                    "CoopMissionNetworkBridge");
                ModLogger.Info(
                    "CoopMissionNetworkBridge: handled client selection request. " +
                    "Peer=" + (peer?.UserName ?? "null") +
                    " Kind=" + message.RequestKind +
                    " Side=" + message.RequestedSide +
                    " SelectionId=" + (message.SelectionId ?? string.Empty) +
                    " Applied=" + applied);
                bool shouldForceImmediateStatus =
                    message.RequestKind == CoopBattleSelectionRequestKind.SpawnNow ||
                    message.RequestKind == CoopBattleSelectionRequestKind.ForceRespawnable ||
                    message.RequestKind == CoopBattleSelectionRequestKind.Spectate ||
                    message.RequestKind == CoopBattleSelectionRequestKind.BeginCommanderDeployment ||
                    message.RequestKind == CoopBattleSelectionRequestKind.AutoDeployCommanderDeployment ||
                    message.RequestKind == CoopBattleSelectionRequestKind.FinishCommanderDeployment ||
                    message.RequestKind == CoopBattleSelectionRequestKind.QueueSpawnAfterDeployment;
                bool shouldForceStatusAfterRejectedInteractiveRequest =
                    !applied &&
                    (message.RequestKind == CoopBattleSelectionRequestKind.SelectSide ||
                     message.RequestKind == CoopBattleSelectionRequestKind.SelectEntry ||
                     message.RequestKind == CoopBattleSelectionRequestKind.SpawnNow ||
                     message.RequestKind == CoopBattleSelectionRequestKind.BeginCommanderDeployment ||
                     message.RequestKind == CoopBattleSelectionRequestKind.AutoDeployCommanderDeployment ||
                     message.RequestKind == CoopBattleSelectionRequestKind.FinishCommanderDeployment ||
                     message.RequestKind == CoopBattleSelectionRequestKind.QueueSpawnAfterDeployment);
                if ((applied && shouldForceImmediateStatus) || shouldForceStatusAfterRejectedInteractiveRequest)
                    TrySendImmediatePeerStatusPayloads(peer);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client selection request handling failed: " + ex.Message);
            }

            return true;
        }

        private bool HandleClientCommanderDeploymentFormationAssignments(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopCommanderDeploymentFormationAssignmentsMessage message))
                return false;

            try
            {
                LogCommanderDeploymentAssignmentDiagnostics(
                    "handler-entry",
                    peer,
                    message,
                    null,
                    0,
                    0,
                    0,
                    0,
                    string.Empty);

                bool applied = TryApplyCommanderDeploymentFormationAssignments(peer, message);
                if (!applied && IsCommanderDeploymentOrderOfBattleDiagnosticsEnabled())
                {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored commander deployment formation assignment sync. " +
                    "Peer=" + (peer?.UserName ?? "null") +
                    " Side=" + message.RequestedSide +
                    " Bytes=" + (message.AssignmentBytes?.Length ?? 0));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: commander deployment formation assignment sync failed. " +
                    "Peer=" + (peer?.UserName ?? "null") +
                    " Error=" + ex.GetType().Name + ":" + ex.Message);
            }

            return true;
        }

        private bool HandleClientCommanderDeploymentSiegeMachineSelection(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopCommanderDeploymentSiegeMachineSelectionMessage message))
                return false;

            try
            {
                bool applied = TryApplyCommanderDeploymentSiegeMachineSelection(peer, message);
                LogCommanderDeploymentSiegeMachineDiagnostics(
                    applied ? "applied" : "ignored",
                    peer,
                    message,
                    null,
                    string.Empty);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: commander deployment siege machine sync failed. " +
                    "Peer=" + (peer?.UserName ?? "null") +
                    " Error=" + ex.GetType().Name + ":" + ex.Message);
            }

            return true;
        }

        private void HandleServerDelegatedCaptainAssignmentsState(GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopDelegatedCaptainAssignmentsStateMessage message) ||
                message.AssignmentSide == BattleSideEnum.None)
            {
                return;
            }

            try
            {
                Team team = Mission?.Teams?.FirstOrDefault(candidate =>
                    candidate != null &&
                    !ReferenceEquals(candidate, Mission.SpectatorTeam) &&
                    candidate.Side == message.AssignmentSide);
                string error = team == null ? "team-missing" : string.Empty;
                Dictionary<int, string> assignments = new Dictionary<int, string>();
                if (team == null ||
                    !TryDecodeDelegatedCaptainAssignments(
                        Mission,
                        team,
                        message.CaptainAssignmentBytes,
                        out assignments,
                        out error,
                        requireMaterializedCaptainAgent: false))
                {
                    if (CoopDebugConfig.OrderOfBattleDiagnostics)
                    {
                        ModLogger.Info(
                            "CoopMissionNetworkBridge: ignored delegated captain assignment state. " +
                            "Side=" + message.AssignmentSide +
                            " TeamNull=" + (team == null) +
                            " Error=" + (error ?? string.Empty));
                    }
                    return;
                }

                _delegatedCaptainEntryIdByFormationIndexAndSide[message.AssignmentSide] =
                    new Dictionary<int, string>(assignments);
                if (CoopDebugConfig.OrderOfBattleDiagnostics)
                {
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: applied delegated captain assignment state. " +
                        "Side=" + message.AssignmentSide +
                        " Assignments=" + assignments.Count);
                }
            }
            catch (Exception ex)
            {
                if (CoopDebugConfig.OrderOfBattleDiagnostics)
                {
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: delegated captain assignment state handling failed. " +
                        "Side=" + message.AssignmentSide +
                        " Error=" + ex.GetType().Name + ":" + ex.Message);
                }
            }
        }

        private void HandleServerAgentControlState(GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleAgentControlStateMessage message))
                return;

            NetworkCommunicator localPeer = GameNetwork.MyPeer;
            bool changed = CoopBattleAgentControlRuntimeState.ApplyClientAuthoritativeState(
                localPeer?.Index ?? -1,
                message.Mode,
                message.AgentIndex,
                message.EntryId,
                message.Side,
                message.TeamIndex,
                message.FormationIndex,
                message.WasGeneral,
                message.WasCaptain,
                message.Revision,
                message.AcknowledgedRequestId,
                "CoopMissionNetworkBridge server state",
                out CoopBattleAgentControlState previousState);

            CoopBattleAgentControlMode synchronizedMode = message.Mode;
            int synchronizedAgentIndex = message.AgentIndex;
            if (CoopBattleAgentControlRuntimeState.TryGetClientState(out CoopBattleAgentControlState acceptedState))
            {
                synchronizedMode = acceptedState.Mode;
                synchronizedAgentIndex = acceptedState.AgentIndex;
            }

            string formationOwnershipSyncResult = NormalizeClientNonCommanderControlOwnership(
                Mission,
                acceptedState);
            TrySynchronizeClientMainAgentWithControlState(
                Mission,
                synchronizedMode,
                synchronizedAgentIndex,
                out string mainAgentSyncResult);
            if (!changed)
                return;

            ModLogger.Info(
                "CoopMissionNetworkBridge: applied authoritative local agent control state. " +
                "PreviousMode=" + previousState.Mode +
                " Mode=" + message.Mode +
                " AgentIndex=" + message.AgentIndex +
                " Revision=" + message.Revision +
                " AckRequestId=" + message.AcknowledgedRequestId +
                " FormationOwnershipSync=" + formationOwnershipSyncResult +
                " MainAgentSync=" + mainAgentSyncResult);
            LogClientAgentControlTransitionDiagnostics(
                Mission,
                acceptedState,
                formationOwnershipSyncResult,
                mainAgentSyncResult,
                "immediate");
            ScheduleDelayedClientAgentControlDiagnostics(
                acceptedState,
                formationOwnershipSyncResult,
                mainAgentSyncResult);
        }

        private void ScheduleDelayedClientAgentControlDiagnostics(
            CoopBattleAgentControlState state,
            string formationOwnershipSyncResult,
            string mainAgentSyncResult)
        {
            if (!CoopDebugConfig.CombatModelDiagnostics ||
                !GameNetwork.IsClient ||
                state.Mode != CoopBattleAgentControlMode.PlayerControlled)
            {
                _delayedClientAgentControlDiagnosticsPending = false;
                return;
            }

            _delayedClientAgentControlDiagnosticsState = state;
            _delayedClientAgentControlFormationOwnershipSyncResult = formationOwnershipSyncResult ?? string.Empty;
            _delayedClientAgentControlMainAgentSyncResult = mainAgentSyncResult ?? string.Empty;
            _delayedClientAgentControlDiagnosticsTicksRemaining = 2;
            _delayedClientAgentControlDiagnosticsPending = true;
        }

        private void TryRunDelayedClientAgentControlDiagnostics()
        {
            if (!_delayedClientAgentControlDiagnosticsPending)
                return;

            if (!CoopDebugConfig.CombatModelDiagnostics || !GameNetwork.IsClient || Mission == null)
            {
                _delayedClientAgentControlDiagnosticsPending = false;
                return;
            }

            if (--_delayedClientAgentControlDiagnosticsTicksRemaining > 0)
                return;

            _delayedClientAgentControlDiagnosticsPending = false;
            if (!CoopBattleAgentControlRuntimeState.TryGetClientState(out CoopBattleAgentControlState currentState) ||
                currentState.Revision != _delayedClientAgentControlDiagnosticsState.Revision ||
                currentState.Mode != CoopBattleAgentControlMode.PlayerControlled)
            {
                return;
            }

            LogClientAgentControlTransitionDiagnostics(
                Mission,
                currentState,
                _delayedClientAgentControlFormationOwnershipSyncResult,
                _delayedClientAgentControlMainAgentSyncResult,
                "delayed-two-pre-mission-ticks");
        }

        private static string NormalizeClientNonCommanderControlOwnership(
            Mission mission,
            CoopBattleAgentControlState state)
        {
            if (!GameNetwork.IsClient || mission == null)
                return "client-mission-unavailable";

            if (state.WasGeneral || state.WasCaptain)
                return "preserved-authoritative-commander-role";

            MissionPeer localMissionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            if (localMissionPeer == null)
                return "local-mission-peer-unavailable";

            if (!CoopBattleAgentControlRuntimeState.TryResolveAgent(
                    mission,
                    state.AgentIndex,
                    requireActive: true,
                    out Agent controlledAgent))
            {
                localMissionPeer.ControlledFormation = null;
                return "controlled-agent-unavailable-peer-formation-cleared";
            }

            Team team = controlledAgent.Team ?? localMissionPeer.Team;
            if (team == null)
            {
                localMissionPeer.ControlledFormation = null;
                return "team-unavailable-peer-formation-cleared";
            }

            int clearedPlayerOwners = 0;
            int clearedCaptains = 0;
            foreach (Formation formation in team.FormationsIncludingSpecialAndEmpty)
            {
                if (formation == null)
                    continue;

                if (AreSameAgents(formation.PlayerOwner, controlledAgent))
                {
                    formation.PlayerOwner = null;
                    clearedPlayerOwners++;
                }

                if (AreSameAgents(formation.Captain, controlledAgent))
                {
                    formation.Captain = null;
                    clearedCaptains++;
                }
            }

            int clearedOrderControllers = 0;
            OrderController playerOrderController = team.PlayerOrderController;
            if (playerOrderController != null && AreSameAgents(playerOrderController.Owner, controlledAgent))
            {
                playerOrderController.ClearSelectedFormations();
                playerOrderController.Owner = null;
                clearedOrderControllers++;
            }

            OrderController agentOrderController = team.GetOrderControllerOf(controlledAgent);
            if (agentOrderController != null &&
                !ReferenceEquals(agentOrderController, playerOrderController) &&
                AreSameAgents(agentOrderController.Owner, controlledAgent))
            {
                agentOrderController.ClearSelectedFormations();
                agentOrderController.Owner = null;
                clearedOrderControllers++;
            }

            localMissionPeer.ControlledFormation = null;
            return
                "cleared-non-commander" +
                ":player-owners=" + clearedPlayerOwners +
                ":captains=" + clearedCaptains +
                ":order-controllers=" + clearedOrderControllers;
        }

        private static bool AreSameAgents(Agent left, Agent right)
        {
            return ReferenceEquals(left, right) ||
                   (left != null && right != null && left.Index == right.Index);
        }

        internal static bool TrySynchronizeClientMainAgentWithControlState(
            Mission mission,
            CoopBattleAgentControlMode mode,
            int agentIndex,
            out string result)
        {
            result = "not-applied";
            if (!GameNetwork.IsClient || mission == null)
            {
                result = "client-mission-unavailable";
                return false;
            }

            try
            {
                if (mode == CoopBattleAgentControlMode.AiObserved)
                {
                    bool controllerDetached = true;
                    if (CoopBattleAgentControlRuntimeState.TryResolveAgent(
                            mission,
                            agentIndex,
                            requireActive: true,
                            out Agent observedAgent))
                    {
                        if (observedAgent.Controller == AgentControllerType.Player)
                            observedAgent.Controller = AgentControllerType.None;

                        controllerDetached = observedAgent.Controller != AgentControllerType.Player;
                    }

                    Agent currentMainAgent = mission.MainAgent;
                    if (currentMainAgent != null && currentMainAgent.Index == agentIndex)
                        mission.MainAgent = null;
                    if (mission.MainAgentServer != null && mission.MainAgentServer.Index == agentIndex)
                        mission.MainAgentServer = null;

                    bool mainAgentDetached =
                        (mission.MainAgent == null || mission.MainAgent.Index != agentIndex) &&
                        (mission.MainAgentServer == null || mission.MainAgentServer.Index != agentIndex);
                    bool detached = controllerDetached && mainAgentDetached;
                    result = detached
                        ? "ai-observed-client-control-detached"
                        : (!controllerDetached
                            ? "ai-observed-controller-detach-pending"
                            : "ai-observed-main-agent-detach-pending");
                    return detached;
                }

                if (mode != CoopBattleAgentControlMode.PlayerControlled || agentIndex < 0)
                {
                    result = "state-does-not-own-agent";
                    return false;
                }

                if (!CoopBattleAgentControlRuntimeState.TryResolveAgent(
                        mission,
                        agentIndex,
                        requireActive: true,
                        out Agent controlledAgent))
                {
                    result = "controlled-agent-unavailable";
                    return false;
                }

                MissionPeer localMissionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
                if (localMissionPeer == null)
                {
                    result = "local-mission-peer-unavailable";
                    return false;
                }

                if (!ReferenceEquals(localMissionPeer.ControlledAgent, controlledAgent) ||
                    !ReferenceEquals(controlledAgent.MissionPeer, localMissionPeer) ||
                    controlledAgent.Controller != AgentControllerType.Player)
                {
                    result = "native-control-attachment-pending";
                    return false;
                }

                controlledAgent.ClearTargetFrame();
                controlledAgent.SetAutomaticTargetSelection(false);

                bool changed = false;
                if (!ReferenceEquals(mission.MainAgent, controlledAgent))
                {
                    mission.MainAgent = controlledAgent;
                    changed = true;
                }
                if (!ReferenceEquals(mission.MainAgentServer, controlledAgent))
                {
                    mission.MainAgentServer = controlledAgent;
                    changed = true;
                }

                bool synchronized =
                    ReferenceEquals(mission.MainAgent, controlledAgent) &&
                    ReferenceEquals(mission.MainAgentServer, controlledAgent);
                result = synchronized
                    ? (changed ? "player-main-agent-restored" : "player-main-agent-already-current")
                    : "player-main-agent-restore-pending";
                return synchronized;
            }
            catch (Exception ex)
            {
                result = "exception:" + ex.GetType().Name;
                return false;
            }
        }

        private static void LogClientAgentControlTransitionDiagnostics(
            Mission mission,
            CoopBattleAgentControlState state,
            string formationOwnershipSyncResult,
            string mainAgentSyncResult,
            string stage)
        {
            if (!CoopDebugConfig.CombatModelDiagnostics || mission == null)
                return;

            try
            {
                MissionPeer localMissionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
                CoopBattleAgentControlRuntimeState.TryResolveAgent(
                    mission,
                    state.AgentIndex,
                    requireActive: false,
                    out Agent agent);
                MissionBehavior mainAgentController = mission.MissionBehaviors?.FirstOrDefault(behavior =>
                    behavior != null &&
                    (behavior.GetType().FullName ?? behavior.GetType().Name)
                        .IndexOf("MissionMainAgentController", StringComparison.OrdinalIgnoreCase) >= 0);

                string mainAgentControllerState = "missing";
                if (mainAgentController != null)
                {
                    Type controllerType = mainAgentController.GetType();
                    object activated = controllerType
                        .GetField("_activated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(mainAgentController);
                    object playerAgentAdded = controllerType
                        .GetField("_isPlayerAgentAdded", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(mainAgentController);
                    object disabled = controllerType
                        .GetProperty("IsDisabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(mainAgentController);
                    mainAgentControllerState =
                        "Activated=" + (activated?.ToString() ?? "unknown") +
                        "/IsDisabled=" + (disabled?.ToString() ?? "unknown") +
                        "/PlayerAgentAdded=" + (playerAgentAdded?.ToString() ?? "unknown");
                }

                ModLogger.Info(
                    "CoopMissionNetworkBridge: client agent control transition diagnostics. " +
                    "Stage=" + (stage ?? "unknown") +
                    " Mode=" + state.Mode +
                    " Revision=" + state.Revision +
                    " AgentIndex=" + state.AgentIndex +
                    " AgentState=" + (agent?.State.ToString() ?? "null") +
                    " Controller=" + (agent?.Controller.ToString() ?? "null") +
                    " IsPlayerControlled=" + (agent?.IsPlayerControlled.ToString() ?? "null") +
                    " IsAIControlled=" + (agent?.IsAIControlled.ToString() ?? "null") +
                    " CombatActionsEnabled=" + (agent?.CombatActionsEnabled.ToString() ?? "null") +
                    " Position=" + (agent?.Position.ToString() ?? "null") +
                    " VisualPosition=" + (agent?.VisualPosition.ToString() ?? "null") +
                    " LookDirection=" + (agent?.LookDirection.ToString() ?? "null") +
                    " FormationIndex=" + (agent?.Formation?.Index.ToString() ?? "null") +
                    " AuthoritativeFormationIndex=" + state.FormationIndex +
                    " PeerControlledFormationIndex=" + (localMissionPeer?.ControlledFormation?.Index.ToString() ?? "null") +
                    " MissionPeerControlledAgentIndex=" + (localMissionPeer?.ControlledAgent?.Index.ToString() ?? "null") +
                    " NetworkPeerControlledAgentIndex=" + (GameNetwork.MyPeer?.ControlledAgent?.Index.ToString() ?? "null") +
                    " AgentMissionPeer=" + (agent?.MissionPeer?.GetNetworkPeer()?.UserName ?? "null") +
                    " IsCameraAttachable=" + (agent?.IsCameraAttachable().ToString() ?? "null") +
                    " MainAgentIndex=" + (mission.MainAgent?.Index.ToString() ?? "null") +
                    " MainAgentServerIndex=" + (mission.MainAgentServer?.Index.ToString() ?? "null") +
                    " MovementFlags=" + (agent?.MovementFlags.ToString() ?? "null") +
                    " EventControlFlags=" + (agent?.EventControlFlags.ToString() ?? "null") +
                    " WieldedMain=" + (agent?.WieldedWeapon.Item?.StringId ?? "none") +
                    " WieldedOffhand=" + (agent?.WieldedOffhandWeapon.Item?.StringId ?? "none") +
                    " Action0=" + (agent?.GetCurrentAction(0).ToString() ?? "null") +
                    " ActionStage0=" + (agent?.GetCurrentActionStage(0).ToString() ?? "null") +
                    " Action1=" + (agent?.GetCurrentAction(1).ToString() ?? "null") +
                    " ActionStage1=" + (agent?.GetCurrentActionStage(1).ToString() ?? "null") +
                    " WasGeneral=" + state.WasGeneral +
                    " WasCaptain=" + state.WasCaptain +
                    " FormationOwnershipSync=" + (formationOwnershipSyncResult ?? "unknown") +
                    " MainAgentSync=" + (mainAgentSyncResult ?? "unknown") +
                    " MainAgentController={" + mainAgentControllerState + "}");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: client agent control transition diagnostics failed. " +
                    "Mode=" + state.Mode +
                    " AgentIndex=" + state.AgentIndex +
                    " Error=" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void HandleServerCommanderDeploymentSiegeMachineState(GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopCommanderDeploymentSiegeMachineStateMessage message))
                return;

            try
            {
                bool applied = TryApplyServerCommanderDeploymentSiegeMachineState(message);
                LogCommanderDeploymentSiegeMachineStateDiagnostics(
                    applied ? "applied" : "ignored",
                    message,
                    string.Empty);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: server commander deployment siege machine state handling failed. " +
                    "Error=" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void HandleServerSiegeMissionObjectIdMap(GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopSiegeMissionObjectIdMapMessage message))
                return;

            try
            {
                bool applied = SiegeMissionObjectIdMapRuntime.TryApplyClientEntry(
                    Mission,
                    message.ObjectSide,
                    message.ServerMissionObjectId,
                    message.Signature,
                    message.ObjectTypeName,
                    out string diagnostics);
                LogSiegeMissionObjectIdMapDiagnostics(
                    applied ? "applied" : "ignored",
                    message,
                    diagnostics);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: siege mission object id map handling failed. " +
                    "Error=" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        private bool TryApplyServerCommanderDeploymentSiegeMachineState(
            CoopCommanderDeploymentSiegeMachineStateMessage message)
        {
            if (!GameNetwork.IsClient || GameNetwork.IsServer || Mission == null || message == null)
            {
                LogCommanderDeploymentSiegeMachineStateDiagnostics(
                    "skip-context",
                    message,
                    "IsClient=" + GameNetwork.IsClient +
                    " IsServer=" + GameNetwork.IsServer +
                    " HasMission=" + (Mission != null));
                return false;
            }

            if (!CoopSiegeDeploymentBoundaryRuntime.IsCoopSiegeDeploymentMission(Mission))
            {
                LogCommanderDeploymentSiegeMachineStateDiagnostics(
                    "skip-not-coop-siege-deployment",
                    message,
                    string.Empty);
                return false;
            }

            BattleSideEnum side = message.RequestedSide;
            if (side == BattleSideEnum.None)
            {
                LogCommanderDeploymentSiegeMachineStateDiagnostics(
                    "skip-side-none",
                    message,
                    string.Empty);
                return false;
            }

            DeploymentPoint deploymentPoint = ResolveCommanderDeploymentPointById(
                message.DeploymentPointId,
                side,
                out string deploymentPointIdResolutionDiagnostics);
            string deploymentPointResolutionDiagnostics = "IdResolution={" + deploymentPointIdResolutionDiagnostics + "}";
            if (deploymentPoint == null)
            {
                deploymentPoint = ResolveCommanderDeploymentPoint(
                    side,
                    message.DeploymentPointPosition,
                    message.SiegeWeaponTypeName,
                    out string fallbackDeploymentPointResolutionDiagnostics);
                deploymentPointResolutionDiagnostics +=
                    " FallbackResolution={" + fallbackDeploymentPointResolutionDiagnostics + "}";
            }

            if (deploymentPoint == null || deploymentPoint.Side != side)
            {
                LogCommanderDeploymentSiegeMachineStateDiagnostics(
                    "skip-invalid-deployment-point",
                    message,
                    "Resolved=" + FormatDeploymentPoint(deploymentPoint) +
                    " Resolution=" + deploymentPointResolutionDiagnostics);
                return false;
            }

            SiegeWeapon siegeWeapon = null;
            string siegeWeaponResolutionDiagnostics = string.Empty;
            if (!message.ClearSelection)
            {
                siegeWeapon = ResolveCommanderDeploymentSiegeWeaponById(
                    deploymentPoint,
                    side,
                    message.SiegeWeaponId,
                    message.SiegeWeaponTypeName,
                    out string siegeWeaponIdResolutionDiagnostics);
                siegeWeaponResolutionDiagnostics = "IdResolution={" + siegeWeaponIdResolutionDiagnostics + "}";
                if (siegeWeapon == null)
                {
                    siegeWeapon = ResolveCommanderDeploymentSiegeWeapon(
                        deploymentPoint,
                        side,
                        message.SiegeWeaponTypeName,
                        allowDisabled: true);
                    siegeWeaponResolutionDiagnostics += " FallbackByType=" + FormatSiegeWeapon(siegeWeapon);
                }

                if (siegeWeapon == null || siegeWeapon.Side != side)
                {
                    LogCommanderDeploymentSiegeMachineStateDiagnostics(
                        "skip-invalid-siege-weapon",
                        message,
                        "DeploymentPoint=" + FormatDeploymentPoint(deploymentPoint) +
                        " SiegeWeapon=" + FormatSiegeWeapon(siegeWeapon) +
                        " SiegeWeaponResolution=" + siegeWeaponResolutionDiagnostics +
                        " DeploymentPointResolution=" + deploymentPointResolutionDiagnostics);
                    return false;
                }
            }

            bool applied = ExactCampaignSiegeAssaultWithDeploymentRuntime.TryApplyCommanderDeploymentSiegeMachineStateLocally(
                Mission,
                side,
                deploymentPoint,
                siegeWeapon,
                message.ClearSelection,
                out string localApplyDiagnostics);
            SiegeMissionObjectIdMapRuntime.ClearClientCache(Mission);
            string idBridgeDiagnostics = TryRegisterCommanderDeploymentSiegeMachineIdBridge(
                message,
                side,
                deploymentPoint,
                siegeWeapon,
                applied);
            LogCommanderDeploymentSiegeMachineStateDiagnostics(
                applied ? "applied-detail" : "apply-failed",
                message,
                "DeploymentPoint=" + FormatDeploymentPoint(deploymentPoint) +
                " SiegeWeapon=" + FormatSiegeWeapon(siegeWeapon) +
                " DeploymentPointResolution=" + deploymentPointResolutionDiagnostics +
                " SiegeWeaponResolution=" + siegeWeaponResolutionDiagnostics +
                " LocalApply={" + localApplyDiagnostics + "}" +
                " IdBridge={" + idBridgeDiagnostics + "}");
            return applied;
        }

        private static string TryRegisterCommanderDeploymentSiegeMachineIdBridge(
            CoopCommanderDeploymentSiegeMachineStateMessage message,
            BattleSideEnum side,
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            bool applied)
        {
            if (!applied)
                return "skip-apply-failed";

            if (message == null)
                return "skip-null-message";

            if (deploymentPoint == null)
                return "skip-null-deployment-point";

            string deploymentPointDiagnostics = SiegeMissionObjectIdBridge.RegisterDeploymentPointMapping(
                message.DeploymentPointId,
                deploymentPoint,
                side,
                "CoopMissionNetworkBridge.TryApplyServerCommanderDeploymentSiegeMachineState");

            string siegeWeaponDiagnostics = "skip-clear-or-null";
            if (!message.ClearSelection && siegeWeapon != null)
            {
                siegeWeaponDiagnostics = SiegeMissionObjectIdBridge.RegisterSiegeWeaponMapping(
                    message.SiegeWeaponId,
                    siegeWeapon,
                    side,
                    message.SiegeWeaponTypeName,
                    "CoopMissionNetworkBridge.TryApplyServerCommanderDeploymentSiegeMachineState");
            }

            return
                "DeploymentPoint={" + deploymentPointDiagnostics + "}" +
                " SiegeWeapon={" + siegeWeaponDiagnostics + "}";
        }

        private bool TryApplyCommanderDeploymentSiegeMachineSelection(
            NetworkCommunicator peer,
            CoopCommanderDeploymentSiegeMachineSelectionMessage message)
        {
            if (!GameNetwork.IsServer || Mission == null || peer == null || message == null)
            {
                LogCommanderDeploymentSiegeMachineDiagnostics(
                    "skip-context",
                    peer,
                    message,
                    null,
                    "IsServer=" + GameNetwork.IsServer);
                return false;
            }

            if (!CoopSiegeDeploymentBoundaryRuntime.IsCoopSiegeDeploymentMission(Mission))
            {
                LogCommanderDeploymentSiegeMachineDiagnostics(
                    "skip-not-coop-siege-deployment",
                    peer,
                    message,
                    null,
                    string.Empty);
                return false;
            }

            if (!CoopMissionSpawnLogic.TryResolveCommanderDeploymentOrderLease(
                    peer,
                    out Team team,
                    out _,
                    out _))
            {
                LogCommanderDeploymentSiegeMachineDiagnostics(
                    "skip-no-order-lease",
                    peer,
                    message,
                    null,
                    string.Empty);
                return false;
            }

            if (team == null ||
                team.Side == BattleSideEnum.None ||
                (message.RequestedSide != BattleSideEnum.None && team.Side != message.RequestedSide))
            {
                LogCommanderDeploymentSiegeMachineDiagnostics(
                    "skip-side-mismatch",
                    peer,
                    message,
                    team,
                    "TeamSide=" + (team?.Side.ToString() ?? "<null>"));
                return false;
            }

            DeploymentPoint deploymentPoint = ResolveCommanderDeploymentPointById(
                message.DeploymentPointId,
                team.Side,
                out string deploymentPointIdResolutionDiagnostics);
            string deploymentPointResolutionDiagnostics = "IdResolution={" + deploymentPointIdResolutionDiagnostics + "}";
            if (deploymentPoint == null)
            {
                deploymentPoint = ResolveCommanderDeploymentPoint(
                    team.Side,
                    message.DeploymentPointPosition,
                    message.SiegeWeaponTypeName,
                    out string fallbackDeploymentPointResolutionDiagnostics);
                deploymentPointResolutionDiagnostics +=
                    " FallbackResolution={" + fallbackDeploymentPointResolutionDiagnostics + "}";
            }

            if (deploymentPoint == null ||
                deploymentPoint.Side != team.Side)
            {
                LogCommanderDeploymentSiegeMachineDiagnostics(
                    "skip-invalid-deployment-point",
                    peer,
                    message,
                    team,
                    "Resolved=" + FormatDeploymentPoint(deploymentPoint) +
                    " Resolution=" + deploymentPointResolutionDiagnostics);
                return false;
            }

            SiegeWeapon siegeWeapon = null;
            Type siegeWeaponType = null;
            DeploymentPoint deploymentPointToDisbandForMove = null;
            string siegeWeaponResolutionDiagnostics = string.Empty;
            string siegeWeaponCountDiagnostics = string.Empty;
            string moveSourceDiagnostics = string.Empty;
            string moveSourcePreparationDiagnostics = string.Empty;
            string siegeControllerDiagnostics = string.Empty;
            string serverPreparationDiagnostics = TryPrepareCommanderDeploymentPointForServer(
                deploymentPoint,
                null,
                enableObjects: false);
            if (!message.ClearSelection)
            {
                siegeWeapon = ResolveCommanderDeploymentSiegeWeaponById(
                    deploymentPoint,
                    team.Side,
                    message.SiegeWeaponId,
                    message.SiegeWeaponTypeName,
                    out string siegeWeaponIdResolutionDiagnostics);
                siegeWeaponResolutionDiagnostics =
                    "IdResolution={" + siegeWeaponIdResolutionDiagnostics + "}";
                if (siegeWeapon == null)
                {
                    siegeWeapon = ResolveCommanderDeploymentSiegeWeapon(
                        deploymentPoint,
                        team.Side,
                        message.SiegeWeaponTypeName,
                        allowDisabled: true);
                    siegeWeaponResolutionDiagnostics += " FallbackByType=" + FormatSiegeWeapon(siegeWeapon);
                }

                if (!IsValidCommanderDeploymentSiegeWeapon(
                        deploymentPoint,
                        siegeWeapon,
                        team.Side,
                        allowDisabled: true,
                        out siegeWeaponType,
                        out string invalidSiegeWeaponReason))
                {
                    LogCommanderDeploymentSiegeMachineDiagnostics(
                        "skip-invalid-siege-weapon",
                        peer,
                        message,
                        team,
                        "DeploymentPoint=" + FormatDeploymentPoint(deploymentPoint) +
                        " SiegeWeapon=" + FormatSiegeWeapon(siegeWeapon) +
                        " SiegeWeaponResolution=" + siegeWeaponResolutionDiagnostics +
                        " ServerPreparation=" + serverPreparationDiagnostics +
                        " Reason=" + (invalidSiegeWeaponReason ?? string.Empty));
                    return false;
                }

                serverPreparationDiagnostics = TryPrepareCommanderDeploymentPointForServer(
                    deploymentPoint,
                    siegeWeapon,
                    enableObjects: true);

                int maxCount = GetCommanderDeploymentMaxSiegeWeaponCount(team.Side, siegeWeaponType);
                if (maxCount <= 0)
                {
                    LogCommanderDeploymentSiegeMachineDiagnostics(
                        "skip-campaign-siege-weapon-not-allowed",
                        peer,
                        message,
                        team,
                        "WeaponType=" + (siegeWeaponType?.Name ?? "<null>") +
                        " MaxCount=" + maxCount);
                    return false;
                }

                if (!ReferenceEquals(deploymentPoint.DeployedWeapon, siegeWeapon))
                {
                    int remainingCount = GetCommanderDeploymentRemainingSiegeWeaponCount(
                        team.Side,
                        siegeWeaponType,
                        deploymentPoint,
                        maxCount,
                        out string remainingCountDiagnostics);
                    siegeWeaponCountDiagnostics =
                        "MaxCount=" + maxCount +
                        " RemainingCount=" + remainingCount +
                        " RemainingDiagnostics={" + remainingCountDiagnostics + "}";
                    if (remainingCount <= 0)
                    {
                        deploymentPointToDisbandForMove = FindCommanderDeploymentPointToDisbandForMove(
                            team.Side,
                            siegeWeaponType,
                            deploymentPoint,
                            out moveSourceDiagnostics);
                        if (deploymentPointToDisbandForMove == null)
                        {
                            LogCommanderDeploymentSiegeMachineDiagnostics(
                                "skip-no-remaining-count",
                                peer,
                                message,
                                team,
                                "WeaponType=" + (siegeWeaponType?.Name ?? "<null>") +
                                " SiegeWeaponCount=" + siegeWeaponCountDiagnostics +
                                " MoveSourceResolution={" + moveSourceDiagnostics + "}");
                            return false;
                        }
                    }
                }
            }

            if (message.ClearSelection)
            {
                siegeWeaponType = null;
            }
            else if (siegeWeaponType == null &&
                     !TryResolveCommanderDeploymentSiegeWeaponType(siegeWeapon, out siegeWeaponType))
            {
                LogCommanderDeploymentSiegeMachineDiagnostics(
                    "skip-unresolved-siege-weapon-type",
                    peer,
                    message,
                    team,
                    "SiegeWeapon=" + FormatSiegeWeapon(siegeWeapon));
                return false;
            }

            SiegeDeploymentHandler siegeDeploymentHandler = Mission.GetMissionBehavior<SiegeDeploymentHandler>();
            string applyStep = "start";
            try
            {
                if (!message.ClearSelection &&
                    siegeWeapon != null)
                {
                    applyStep = "pre-controlled-apply";
                    siegeControllerDiagnostics = BuildCommanderDeploymentSiegeControllerDiagnostics(
                        team.Side,
                        deploymentPoint,
                        siegeWeapon);
                    LogCommanderDeploymentSiegeMachineDiagnostics(
                        "pre-deploy-controller-state",
                        peer,
                        message,
                        team,
                        "DeploymentPoint=" + FormatDeploymentPoint(deploymentPoint) +
                        " SiegeWeapon=" + FormatSiegeWeapon(siegeWeapon) +
                        " SiegeController={" + siegeControllerDiagnostics + "}");
                }

                applyStep = "controlled-siege-machine-apply";
                if (!CoopSiegeMachineDeploymentController.TryApplySelection(
                        Mission,
                        team,
                        deploymentPoint,
                        siegeWeapon,
                        deploymentPointToDisbandForMove,
                        message.ClearSelection,
                        siegeDeploymentHandler,
                        IsCommanderDeploymentOrderOfBattleDiagnosticsEnabled(),
                        out string controlledApplyDiagnostics))
                {
                    throw new InvalidOperationException(
                        "commander deployment controlled siege machine apply failed. " + controlledApplyDiagnostics);
                }

                siegeControllerDiagnostics = AppendCommanderDeploymentDiagnostics(
                    siegeControllerDiagnostics,
                    "ControlledApply",
                    controlledApplyDiagnostics);
                applyStep = "controlled-siege-machine-apply-complete";
                LogCommanderDeploymentSiegeMachineDiagnostics(
                    "applied-detail",
                    peer,
                    message,
                    team,
                    "DeploymentPoint=" + FormatDeploymentPoint(deploymentPoint) +
                    " SiegeWeapon=" + FormatSiegeWeapon(siegeWeapon) +
                    " DisbandedForMove=" + FormatDeploymentPoint(deploymentPointToDisbandForMove) +
                    " SiegeWeaponResolution=" + siegeWeaponResolutionDiagnostics +
                    " SiegeWeaponCount=" + siegeWeaponCountDiagnostics +
                    " MoveSourceResolution={" + moveSourceDiagnostics + "}" +
                    " MoveSourcePreparation={" + moveSourcePreparationDiagnostics + "}" +
                    " ServerPreparation=" + serverPreparationDiagnostics +
                    " Resolution=" + deploymentPointResolutionDiagnostics +
                    " SiegeController={" + siegeControllerDiagnostics + "}" +
                    " ApplyStep=" + applyStep);
                return true;
            }
            catch (Exception ex)
            {
                LogCommanderDeploymentSiegeMachineDiagnostics(
                    "apply-failed",
                    peer,
                    message,
                    team,
                    ex.GetType().Name + ":" + ex.Message +
                    " ApplyStep=" + applyStep +
                    " DeploymentPoint=" + FormatDeploymentPoint(deploymentPoint) +
                    " SiegeWeapon=" + FormatSiegeWeapon(siegeWeapon) +
                    " DisbandedForMove=" + FormatDeploymentPoint(deploymentPointToDisbandForMove) +
                    " SiegeWeaponResolution=" + siegeWeaponResolutionDiagnostics +
                    " SiegeWeaponCount=" + siegeWeaponCountDiagnostics +
                    " MoveSourceResolution={" + moveSourceDiagnostics + "}" +
                    " MoveSourcePreparation={" + moveSourcePreparationDiagnostics + "}" +
                    " ServerPreparation=" + serverPreparationDiagnostics +
                    " Resolution=" + deploymentPointResolutionDiagnostics +
                    " SiegeController={" + siegeControllerDiagnostics + "}");
                return false;
            }
        }

        private bool TryBroadcastCommanderDeploymentSiegeMachineState(
            BattleSideEnum side,
            out string diagnostics,
            string source)
        {
            diagnostics = "invalid-context";
            if (!GameNetwork.IsServer || Mission == null || side == BattleSideEnum.None)
                return false;

            if (GameNetwork.NetworkPeers == null)
            {
                diagnostics = "network-peers-null";
                return false;
            }

            List<NetworkCommunicator> peers = GameNetwork.NetworkPeers
                .Where(IsEligibleRemotePeer)
                .ToList();
            List<DeploymentPoint> deploymentPoints = EnumerateCommanderDeploymentPoints()
                .Where(deploymentPoint => deploymentPoint != null &&
                                          deploymentPoint.Side == side)
                .ToList();

            int sentCount = 0;
            int failedCount = 0;
            int selectedPointCount = 0;
            int clearedPointCount = 0;
            var details = new List<string>();
            foreach (DeploymentPoint deploymentPoint in deploymentPoints)
            {
                SiegeWeapon siegeWeapon = deploymentPoint.IsDeployed
                    ? deploymentPoint.DeployedWeapon as SiegeWeapon
                    : null;
                bool clearSelection = siegeWeapon == null || siegeWeapon.Side != side;
                if (clearSelection)
                    clearedPointCount++;
                else
                    selectedPointCount++;

                MissionObjectId siegeWeaponId = clearSelection
                    ? MissionObjectId.Invalid
                    : siegeWeapon.Id;
                string siegeWeaponTypeName = clearSelection
                    ? string.Empty
                    : BuildCommanderDeploymentSiegeWeaponTypeName(siegeWeapon);
                Vec3 deploymentPointPosition = ResolveCommanderDeploymentPointPosition(deploymentPoint);
                foreach (NetworkCommunicator peer in peers)
                {
                    try
                    {
                        GameNetwork.BeginModuleEventAsServer(peer);
                        GameNetwork.WriteMessage(new CoopCommanderDeploymentSiegeMachineStateMessage(
                            side,
                            deploymentPoint.Id,
                            deploymentPointPosition,
                            siegeWeaponId,
                            siegeWeaponTypeName,
                            clearSelection));
                        GameNetwork.EndModuleEventAsServer();
                        sentCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        details.Add(
                            "SendFailed Peer=" + (peer?.UserName ?? "<null>") +
                            " Point=" + FormatDeploymentPoint(deploymentPoint) +
                            " Weapon=" + FormatSiegeWeapon(siegeWeapon) +
                            " Error=" + ex.GetType().Name + ":" + ex.Message);
                    }
                }
            }

            bool idMapPublished = TryBroadcastSiegeMissionObjectIdMap(
                side,
                peers,
                out string idMapDiagnostics,
                source);
            diagnostics =
                "Side=" + side +
                " Points=" + deploymentPoints.Count +
                " SelectedPoints=" + selectedPointCount +
                " ClearedPoints=" + clearedPointCount +
                " Peers=" + peers.Count +
                " Sent=" + sentCount +
                " Failed=" + failedCount +
                " IdMapPublished=" + idMapPublished +
                " IdMap={" + idMapDiagnostics + "}" +
                " Source=" + (source ?? "unknown") +
                " Details=[" + string.Join("; ", details.ToArray()) + "]";
            return failedCount == 0 &&
                   idMapPublished &&
                   (sentCount > 0 || peers.Count <= 0);
        }

        private bool TryBroadcastSiegeMissionObjectIdMap(
            BattleSideEnum side,
            IReadOnlyCollection<NetworkCommunicator> peers,
            out string diagnostics,
            string source)
        {
            diagnostics = "invalid-context";
            if (!GameNetwork.IsServer || Mission == null)
                return false;

            if (peers == null || peers.Count <= 0)
            {
                diagnostics = "no-peers";
                return true;
            }

            List<SiegeMissionObjectIdMapEntry> entries = SiegeMissionObjectIdMapRuntime.BuildServerEntries(
                Mission,
                side,
                out string mapBuildDiagnostics);
            int sentCount = 0;
            int skippedCount = 0;
            int failedCount = 0;
            var details = new List<string>();
            foreach (NetworkCommunicator peer in peers)
            {
                if (peer == null)
                    continue;

                if (!_sentSiegeMissionObjectMapKeysByPeer.TryGetValue(peer, out HashSet<string> sentKeys))
                {
                    sentKeys = new HashSet<string>(StringComparer.Ordinal);
                    _sentSiegeMissionObjectMapKeysByPeer[peer] = sentKeys;
                }

                foreach (SiegeMissionObjectIdMapEntry entry in entries)
                {
                    if (entry == null || entry.MissionObjectId.Id < 0 || string.IsNullOrWhiteSpace(entry.Signature))
                        continue;

                    string entryKey =
                        entry.ObjectSide + "|" +
                        entry.MissionObjectId.Id + "|" +
                        entry.Signature;
                    if (sentKeys.Contains(entryKey))
                    {
                        skippedCount++;
                        continue;
                    }

                    try
                    {
                        GameNetwork.BeginModuleEventAsServer(peer);
                        GameNetwork.WriteMessage(new CoopSiegeMissionObjectIdMapMessage(
                            entry.ObjectSide,
                            entry.MissionObjectId,
                            entry.Signature,
                            entry.ObjectTypeName,
                            entry.EntityName));
                        GameNetwork.EndModuleEventAsServer();
                        sentKeys.Add(entryKey);
                        sentCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        if (details.Count < 8)
                        {
                            details.Add(
                                "SendFailed Peer=" + (peer.UserName ?? "<null>") +
                                " Id=" + entry.MissionObjectId +
                                " Type=" + (entry.ObjectTypeName ?? string.Empty) +
                                " Entity=" + (entry.EntityName ?? string.Empty) +
                                " Error=" + ex.GetType().Name + ":" + ex.Message);
                        }
                    }
                }
            }

            diagnostics =
                "Side=" + side +
                " Entries=" + entries.Count +
                " Peers=" + peers.Count +
                " Sent=" + sentCount +
                " SkippedAlreadySent=" + skippedCount +
                " Failed=" + failedCount +
                " Source=" + (source ?? "unknown") +
                " Build={" + mapBuildDiagnostics + "}" +
                " Details=[" + string.Join("; ", details.ToArray()) + "]";
            return failedCount == 0;
        }

        private static string BuildCommanderDeploymentSiegeWeaponTypeName(SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return string.Empty;

            if (TryResolveCommanderDeploymentSiegeWeaponType(siegeWeapon, out Type weaponType) &&
                weaponType != null)
            {
                return weaponType.FullName ?? weaponType.Name;
            }

            return siegeWeapon.GetType().FullName ?? siegeWeapon.GetType().Name;
        }

        private static T ResolveMissionObject<T>(MissionObjectId missionObjectId)
            where T : MissionObject
        {
            if (missionObjectId == MissionObjectId.Invalid)
                return null;

            try
            {
                T networkObject = TaleWorlds.MountAndBlade.Mission.MissionNetworkHelper
                    .GetMissionObjectFromMissionObjectId(missionObjectId) as T;
                if (networkObject != null)
                    return networkObject;
            }
            catch
            {
            }

            try
            {
                TaleWorlds.MountAndBlade.Mission mission = TaleWorlds.MountAndBlade.Mission.Current;
                if (mission?.ActiveMissionObjects != null)
                {
                    foreach (T candidate in mission.ActiveMissionObjects.FindAllWithType<T>())
                    {
                        if (candidate != null && candidate.Id == missionObjectId)
                            return candidate;
                    }
                }

                if (mission?.MissionObjects != null)
                {
                    foreach (T candidate in mission.MissionObjects.FindAllWithType<T>())
                    {
                        if (candidate != null && candidate.Id == missionObjectId)
                            return candidate;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private DeploymentPoint ResolveCommanderDeploymentPointById(
            MissionObjectId deploymentPointId,
            BattleSideEnum side,
            out string diagnostics)
        {
            diagnostics = "DeploymentPointId=" + deploymentPointId;
            if (deploymentPointId == MissionObjectId.Invalid)
            {
                diagnostics += " Reason=invalid-id";
                return null;
            }

            DeploymentPoint deploymentPoint = ResolveMissionObject<DeploymentPoint>(deploymentPointId);
            diagnostics += " Resolved=" + FormatDeploymentPoint(deploymentPoint);
            if (deploymentPoint == null)
                return null;

            if (deploymentPoint.Side != side)
            {
                diagnostics += " Reason=side-mismatch ExpectedSide=" + side;
                return null;
            }

            return deploymentPoint;
        }

        private DeploymentPoint ResolveCommanderDeploymentPoint(
            BattleSideEnum side,
            Vec3 requestedPosition,
            string requestedWeaponTypeName,
            out string diagnostics)
        {
            diagnostics = string.Empty;
            if (side == BattleSideEnum.None || Mission == null)
            {
                diagnostics = "invalid-context";
                return null;
            }

            try
            {
                DeploymentPoint nearestEnabledPoint = null;
                DeploymentPoint nearestAnyPoint = null;
                float nearestEnabledDistanceSquared = float.MaxValue;
                float nearestAnyDistanceSquared = float.MaxValue;
                Vec2 requestedPosition2D = requestedPosition.AsVec2;
                bool hasValidRequestedPosition = requestedPosition2D.LengthSquared > 1f;
                int candidateCount = 0;
                int enabledCandidateCount = 0;
                int disabledCandidateCount = 0;
                int skippedNoMatchingWeaponCount = 0;
                List<DeploymentPoint> deploymentPoints = EnumerateCommanderDeploymentPoints().ToList();
                string sourceDiagnostics = BuildCommanderDeploymentPointSourceDiagnostics(deploymentPoints.Count);
                foreach (DeploymentPoint deploymentPoint in deploymentPoints)
                {
                    if (deploymentPoint == null ||
                        deploymentPoint.Side != side)
                    {
                        continue;
                    }

                    if (!HasCommanderDeploymentPointMatchingSiegeWeapon(
                            deploymentPoint,
                            side,
                            requestedWeaponTypeName))
                    {
                        skippedNoMatchingWeaponCount++;
                        continue;
                    }

                    candidateCount++;
                    if (deploymentPoint.IsDisabled)
                        disabledCandidateCount++;
                    else
                        enabledCandidateCount++;

                    Vec3 candidatePosition = ResolveCommanderDeploymentPointPosition(deploymentPoint);
                    float distanceSquared = (candidatePosition.AsVec2 - requestedPosition2D).LengthSquared;
                    if (distanceSquared < nearestAnyDistanceSquared)
                    {
                        nearestAnyPoint = deploymentPoint;
                        nearestAnyDistanceSquared = distanceSquared;
                    }

                    if (!deploymentPoint.IsDisabled &&
                        distanceSquared < nearestEnabledDistanceSquared)
                    {
                        nearestEnabledPoint = deploymentPoint;
                        nearestEnabledDistanceSquared = distanceSquared;
                    }
                }

                bool hasRequestedWeaponType = !string.IsNullOrWhiteSpace(requestedWeaponTypeName);
                float strictDistanceLimitSquared = hasRequestedWeaponType ? 25f : 625f;
                float fallbackDistanceLimitSquared = hasRequestedWeaponType ? strictDistanceLimitSquared : 1000000f;
                bool nearestAnyStrictMatch = nearestAnyPoint != null &&
                    nearestAnyDistanceSquared <= strictDistanceLimitSquared;
                bool nearestEnabledStrictMatch = nearestEnabledPoint != null &&
                    nearestEnabledDistanceSquared <= strictDistanceLimitSquared;

                DeploymentPoint nearestPoint = null;
                float nearestDistanceSquared = float.MaxValue;
                string nearestSelectionReason = "none";
                if (hasRequestedWeaponType)
                {
                    if (nearestAnyStrictMatch)
                    {
                        nearestPoint = nearestAnyPoint;
                        nearestDistanceSquared = nearestAnyDistanceSquared;
                        nearestSelectionReason = nearestAnyPoint.IsDisabled
                            ? "nearest-any-strict-disabled"
                            : "nearest-any-strict-enabled";
                    }
                    else if (nearestEnabledStrictMatch)
                    {
                        nearestPoint = nearestEnabledPoint;
                        nearestDistanceSquared = nearestEnabledDistanceSquared;
                        nearestSelectionReason = "nearest-enabled-strict";
                    }
                }
                else
                {
                    nearestPoint = nearestEnabledPoint ?? nearestAnyPoint;
                    nearestDistanceSquared = nearestEnabledPoint != null
                        ? nearestEnabledDistanceSquared
                        : nearestAnyDistanceSquared;
                    nearestSelectionReason = nearestEnabledPoint != null
                        ? "nearest-enabled-fallback"
                        : (nearestAnyPoint != null ? "nearest-any-fallback" : "none");
                }

                bool disabledPointFallback = nearestPoint != null && nearestPoint.IsDisabled;
                bool strictMatch = nearestPoint != null && nearestDistanceSquared <= strictDistanceLimitSquared;
                bool fallbackMatch =
                    nearestPoint != null &&
                    hasValidRequestedPosition &&
                    nearestDistanceSquared <= fallbackDistanceLimitSquared;
                diagnostics =
                    "Candidates=" + candidateCount +
                    " EnabledCandidates=" + enabledCandidateCount +
                    " DisabledCandidates=" + disabledCandidateCount +
                    " SkippedNoMatchingWeapon=" + skippedNoMatchingWeaponCount +
                    " RequestedPositionValid=" + hasValidRequestedPosition +
                    " RequestedPosition=" + requestedPosition +
                    " RequestedWeaponType=" + (requestedWeaponTypeName ?? string.Empty) +
                    " HasRequestedWeaponType=" + hasRequestedWeaponType +
                    " Sources={" + sourceDiagnostics + "}" +
                    " Nearest=" + FormatDeploymentPoint(nearestPoint) +
                    " NearestDistanceSquared=" + nearestDistanceSquared +
                    " NearestSelectionReason=" + nearestSelectionReason +
                    " NearestAny=" + FormatDeploymentPoint(nearestAnyPoint) +
                    " NearestAnyDistanceSquared=" + nearestAnyDistanceSquared +
                    " NearestAnyStrictMatch=" + nearestAnyStrictMatch +
                    " NearestEnabled=" + FormatDeploymentPoint(nearestEnabledPoint) +
                    " NearestEnabledDistanceSquared=" + nearestEnabledDistanceSquared +
                    " NearestEnabledStrictMatch=" + nearestEnabledStrictMatch +
                    " StrictDistanceLimitSquared=" + strictDistanceLimitSquared +
                    " StrictMatch=" + strictMatch +
                    " FallbackMatch=" + fallbackMatch +
                    " FallbackDistanceLimitSquared=" + fallbackDistanceLimitSquared +
                    " DisabledPointFallback=" + disabledPointFallback;
                return strictMatch || fallbackMatch ? nearestPoint : null;
            }
            catch (Exception ex)
            {
                diagnostics = "exception:" + ex.GetType().Name + ":" + ex.Message;
                return null;
            }
        }

        private static bool HasCommanderDeploymentPointMatchingSiegeWeapon(
            DeploymentPoint deploymentPoint,
            BattleSideEnum side,
            string requestedWeaponTypeName)
        {
            if (deploymentPoint == null)
                return false;

            if (string.IsNullOrWhiteSpace(requestedWeaponTypeName))
                return true;

            try
            {
                foreach (SiegeWeapon siegeWeapon in EnumerateCommanderDeploymentPointSiegeWeapons(deploymentPoint))
                {
                    if (siegeWeapon == null || siegeWeapon.Side != side)
                        continue;

                    if (IsCommanderDeploymentSiegeWeaponTypeMatch(siegeWeapon, requestedWeaponTypeName))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private IEnumerable<DeploymentPoint> EnumerateCommanderDeploymentPoints()
        {
            var result = new List<DeploymentPoint>();

            try
            {
                SiegeDeploymentHandler siegeDeploymentHandler = Mission?.GetMissionBehavior<SiegeDeploymentHandler>();
                if (siegeDeploymentHandler?.AllDeploymentPoints != null)
                {
                    foreach (DeploymentPoint deploymentPoint in siegeDeploymentHandler.AllDeploymentPoints)
                    {
                        if (deploymentPoint != null && !result.Contains(deploymentPoint))
                            result.Add(deploymentPoint);
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (Mission?.ActiveMissionObjects != null)
                {
                    foreach (DeploymentPoint deploymentPoint in Mission.ActiveMissionObjects.FindAllWithType<DeploymentPoint>())
                    {
                        if (deploymentPoint != null && !result.Contains(deploymentPoint))
                            result.Add(deploymentPoint);
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (Mission?.MissionObjects != null)
                {
                    foreach (DeploymentPoint deploymentPoint in Mission.MissionObjects.FindAllWithType<DeploymentPoint>())
                    {
                        if (deploymentPoint != null && !result.Contains(deploymentPoint))
                            result.Add(deploymentPoint);
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private string BuildCommanderDeploymentPointSourceDiagnostics(int enumeratedCount)
        {
            int handlerCount = -1;
            int activeCount = -1;
            int missionObjectCount = -1;

            try
            {
                SiegeDeploymentHandler siegeDeploymentHandler = Mission?.GetMissionBehavior<SiegeDeploymentHandler>();
                if (siegeDeploymentHandler?.AllDeploymentPoints != null)
                    handlerCount = siegeDeploymentHandler.AllDeploymentPoints.Count();
            }
            catch
            {
            }

            try
            {
                if (Mission?.ActiveMissionObjects != null)
                    activeCount = Mission.ActiveMissionObjects.FindAllWithType<DeploymentPoint>().Count();
            }
            catch
            {
            }

            try
            {
                if (Mission?.MissionObjects != null)
                    missionObjectCount = Mission.MissionObjects.FindAllWithType<DeploymentPoint>().Count();
            }
            catch
            {
            }

            return
                "Enumerated=" + enumeratedCount +
                " HandlerAll=" + handlerCount +
                " Active=" + activeCount +
                " MissionObjects=" + missionObjectCount;
        }

        private static Vec3 ResolveCommanderDeploymentPointPosition(DeploymentPoint deploymentPoint)
        {
            if (deploymentPoint == null)
                return Vec3.Zero;

            try
            {
                return deploymentPoint.GetDeploymentOrigin();
            }
            catch
            {
            }

            try
            {
                return deploymentPoint.GameEntity.GlobalPosition;
            }
            catch
            {
                return Vec3.Zero;
            }
        }

        private SiegeWeapon ResolveCommanderDeploymentSiegeWeapon(
            DeploymentPoint deploymentPoint,
            BattleSideEnum side,
            string requestedWeaponTypeName,
            bool allowDisabled = false)
        {
            if (deploymentPoint == null ||
                side == BattleSideEnum.None ||
                string.IsNullOrWhiteSpace(requestedWeaponTypeName))
            {
                return null;
            }

            try
            {
                foreach (SiegeWeapon siegeWeapon in EnumerateCommanderDeploymentPointSiegeWeapons(deploymentPoint))
                {
                    if (siegeWeapon == null ||
                        (!allowDisabled && siegeWeapon.IsDisabled) ||
                        siegeWeapon.Side != side)
                    {
                        continue;
                    }

                    if (IsCommanderDeploymentSiegeWeaponTypeMatch(siegeWeapon, requestedWeaponTypeName))
                        return siegeWeapon;
                }
            }
            catch
            {
            }

            return null;
        }

        private SiegeWeapon ResolveCommanderDeploymentSiegeWeaponById(
            DeploymentPoint deploymentPoint,
            BattleSideEnum side,
            MissionObjectId siegeWeaponId,
            string requestedWeaponTypeName,
            out string diagnostics)
        {
            diagnostics = "SiegeWeaponId=" + siegeWeaponId;
            if (deploymentPoint == null)
            {
                diagnostics += " Reason=null-deployment-point";
                return null;
            }

            if (siegeWeaponId == MissionObjectId.Invalid)
            {
                diagnostics += " Reason=invalid-id";
                return null;
            }

            SiegeWeapon siegeWeapon = ResolveMissionObject<SiegeWeapon>(siegeWeaponId);
            diagnostics += " Resolved=" + FormatSiegeWeapon(siegeWeapon);
            if (siegeWeapon == null)
                return null;

            if (siegeWeapon.Side != side)
            {
                diagnostics += " Reason=side-mismatch ExpectedSide=" + side;
                return null;
            }

            if (!string.IsNullOrWhiteSpace(requestedWeaponTypeName) &&
                !IsCommanderDeploymentSiegeWeaponTypeMatch(siegeWeapon, requestedWeaponTypeName))
            {
                diagnostics += " Reason=type-mismatch RequestedType=" + requestedWeaponTypeName;
                return null;
            }

            bool foundUnderPoint = false;
            try
            {
                foreach (SiegeWeapon candidate in EnumerateCommanderDeploymentPointSiegeWeapons(deploymentPoint))
                {
                    if (ReferenceEquals(candidate, siegeWeapon))
                    {
                        foundUnderPoint = true;
                        break;
                    }
                }
            }
            catch
            {
            }

            diagnostics += " FoundUnderPoint=" + foundUnderPoint;
            return foundUnderPoint ? siegeWeapon : null;
        }

        private static IEnumerable<SiegeWeapon> EnumerateCommanderDeploymentPointSiegeWeapons(DeploymentPoint deploymentPoint)
        {
            var result = new List<SiegeWeapon>();
            if (deploymentPoint == null)
                return result;

            AddCommanderDeploymentPointSiegeWeapons(result, SafeEnumerateDeployableWeapons(deploymentPoint));
            AddCommanderDeploymentPointSiegeWeapons(result, SafeGetWeaponsUnder(deploymentPoint));
            return result;
        }

        private static IEnumerable<SynchedMissionObject> SafeEnumerateDeployableWeapons(DeploymentPoint deploymentPoint)
        {
            try
            {
                return deploymentPoint?.DeployableWeapons?.ToList() ?? new List<SynchedMissionObject>();
            }
            catch
            {
                return new List<SynchedMissionObject>();
            }
        }

        private static IEnumerable<SynchedMissionObject> SafeGetWeaponsUnder(DeploymentPoint deploymentPoint)
        {
            try
            {
                return deploymentPoint?.GetWeaponsUnder()?.ToList() ?? new List<SynchedMissionObject>();
            }
            catch
            {
                return new List<SynchedMissionObject>();
            }
        }

        private static void AddCommanderDeploymentPointSiegeWeapons(
            ICollection<SiegeWeapon> output,
            IEnumerable<SynchedMissionObject> candidates)
        {
            if (output == null || candidates == null)
                return;

            foreach (SynchedMissionObject candidate in candidates)
            {
                if (candidate is SiegeWeapon siegeWeapon && !output.Contains(siegeWeapon))
                    output.Add(siegeWeapon);
            }
        }

        private static bool IsCommanderDeploymentSiegeWeaponTypeMatch(
            SiegeWeapon siegeWeapon,
            string requestedWeaponTypeName)
        {
            if (siegeWeapon == null || string.IsNullOrWhiteSpace(requestedWeaponTypeName))
                return false;

            if (!TryResolveCommanderDeploymentSiegeWeaponType(siegeWeapon, out Type weaponType) ||
                weaponType == null)
            {
                return false;
            }

            return string.Equals(weaponType.FullName, requestedWeaponTypeName, StringComparison.Ordinal) ||
                   string.Equals(weaponType.Name, requestedWeaponTypeName, StringComparison.Ordinal);
        }

        private static string TryPrepareCommanderDeploymentMoveSourceForDisband(
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon)
        {
            if (!IsCommanderDeploymentOrderOfBattleDiagnosticsEnabled() &&
                siegeWeapon == null)
            {
                return string.Empty;
            }

            string error = string.Empty;
            bool tickAuxInvoked = false;
            string removeOnDeployEntitiesDiagnostics = string.Empty;
            string addOnDeployEntitiesDiagnostics = string.Empty;
            try
            {
                removeOnDeployEntitiesDiagnostics = TryEnsureCommanderDeploymentSiegeWeaponDeployEntityList(
                    siegeWeapon,
                    CommanderDeploymentSiegeWeaponRemoveOnDeployEntitiesField,
                    CommanderDeploymentSiegeWeaponRemoveOnDeployTagField,
                    "remove");
            }
            catch (Exception ex)
            {
                error = AppendCommanderDeploymentPreparationError(error, "remove-on-deploy-list", ex);
            }

            try
            {
                addOnDeployEntitiesDiagnostics = TryEnsureCommanderDeploymentSiegeWeaponDeployEntityList(
                    siegeWeapon,
                    CommanderDeploymentSiegeWeaponAddOnDeployEntitiesField,
                    CommanderDeploymentSiegeWeaponAddOnDeployTagField,
                    "add");
            }
            catch (Exception ex)
            {
                error = AppendCommanderDeploymentPreparationError(error, "add-on-deploy-list", ex);
            }

            try
            {
                if (siegeWeapon != null)
                {
                    siegeWeapon.TickAuxForInit();
                    tickAuxInvoked = true;
                }
            }
            catch (Exception ex)
            {
                error = AppendCommanderDeploymentPreparationError(error, "tick-aux", ex);
            }

            if (!IsCommanderDeploymentOrderOfBattleDiagnosticsEnabled())
                return string.Empty;

            return
                "DeploymentPoint=" + FormatDeploymentPoint(deploymentPoint) +
                " SiegeWeapon=" + FormatSiegeWeapon(siegeWeapon) +
                " RemoveOnDeploy={" + removeOnDeployEntitiesDiagnostics + "}" +
                " AddOnDeploy={" + addOnDeployEntitiesDiagnostics + "}" +
                " TickAuxInvoked=" + tickAuxInvoked +
                " Error=" + (string.IsNullOrWhiteSpace(error) ? "<none>" : error);
        }

        private static string TryEnsureCommanderDeploymentSiegeWeaponDeployEntityList(
            SiegeWeapon siegeWeapon,
            FieldInfo listField,
            FieldInfo tagField,
            string label)
        {
            if (siegeWeapon == null)
                return "Skipped=True Reason=siege-weapon-null";

            if (listField == null)
                return "Skipped=True Reason=list-field-missing Label=" + (label ?? string.Empty);

            object existingValue = listField.GetValue(siegeWeapon);
            if (existingValue is List<GameEntity> existingList)
            {
                return
                    "Existing=True Initialized=False Count=" + existingList.Count +
                    " Label=" + (label ?? string.Empty);
            }

            string tag = string.Empty;
            if (tagField != null)
                tag = tagField.GetValue(siegeWeapon) as string ?? string.Empty;

            List<GameEntity> entities = new List<GameEntity>();
            if (!string.IsNullOrWhiteSpace(tag))
            {
                try
                {
                    entities = TaleWorlds.MountAndBlade.Mission.Current?.Scene?
                        .FindEntitiesWithTag(tag)
                        .ToList() ?? new List<GameEntity>();
                }
                catch
                {
                    entities = new List<GameEntity>();
                }
            }

            listField.SetValue(siegeWeapon, entities);
            return
                "Existing=False Initialized=True Count=" + entities.Count +
                " Label=" + (label ?? string.Empty) +
                " Tag=" + (tag ?? string.Empty);
        }

        private DeploymentPoint FindCommanderDeploymentPointToDisbandForMove(
            BattleSideEnum side,
            Type weaponType,
            DeploymentPoint targetDeploymentPoint,
            out string diagnostics)
        {
            diagnostics = string.Empty;
            if (side == BattleSideEnum.None ||
                weaponType == null ||
                Mission == null)
            {
                diagnostics = "invalid-context";
                return null;
            }

            try
            {
                int scannedCount = 0;
                int skippedTargetCount = 0;
                int skippedSideCount = 0;
                int disabledPointCount = 0;
                int skippedNotDeployedCount = 0;
                int skippedInvalidWeaponCount = 0;
                int disabledWeaponCount = 0;
                int skippedTypeMismatchCount = 0;

                foreach (DeploymentPoint deploymentPoint in EnumerateCommanderDeploymentPoints())
                {
                    scannedCount++;
                    if (deploymentPoint == null)
                    {
                        continue;
                    }

                    if (ReferenceEquals(deploymentPoint, targetDeploymentPoint))
                    {
                        skippedTargetCount++;
                        continue;
                    }

                    if (deploymentPoint.Side != side)
                    {
                        skippedSideCount++;
                        continue;
                    }

                    if (deploymentPoint.IsDisabled)
                        disabledPointCount++;

                    if (!deploymentPoint.IsDeployed || deploymentPoint.DeployedWeapon == null)
                    {
                        skippedNotDeployedCount++;
                        continue;
                    }

                    SiegeWeapon deployedSiegeWeapon = deploymentPoint.DeployedWeapon as SiegeWeapon;
                    if (deployedSiegeWeapon == null)
                    {
                        skippedInvalidWeaponCount++;
                        continue;
                    }

                    if (deployedSiegeWeapon.IsDisabled)
                        disabledWeaponCount++;

                    if (TryResolveCommanderDeploymentSiegeWeaponType(deployedSiegeWeapon, out Type deployedType) &&
                        deployedType == weaponType)
                    {
                        diagnostics =
                            "Scanned=" + scannedCount +
                            " SkippedTarget=" + skippedTargetCount +
                            " SkippedSide=" + skippedSideCount +
                            " DisabledPointsSeen=" + disabledPointCount +
                            " SkippedNotDeployed=" + skippedNotDeployedCount +
                            " SkippedInvalidWeapon=" + skippedInvalidWeaponCount +
                            " DisabledWeaponsSeen=" + disabledWeaponCount +
                            " SkippedTypeMismatch=" + skippedTypeMismatchCount +
                            " Selected=" + FormatDeploymentPoint(deploymentPoint) +
                            " SelectedWeapon=" + FormatSiegeWeapon(deployedSiegeWeapon);
                        return deploymentPoint;
                    }

                    skippedTypeMismatchCount++;
                }

                diagnostics =
                    "Scanned=" + scannedCount +
                    " SkippedTarget=" + skippedTargetCount +
                    " SkippedSide=" + skippedSideCount +
                    " DisabledPointsSeen=" + disabledPointCount +
                    " SkippedNotDeployed=" + skippedNotDeployedCount +
                    " SkippedInvalidWeapon=" + skippedInvalidWeaponCount +
                    " DisabledWeaponsSeen=" + disabledWeaponCount +
                    " SkippedTypeMismatch=" + skippedTypeMismatchCount +
                    " Selected=<null>";
            }
            catch (Exception ex)
            {
                diagnostics = "exception:" + ex.GetType().Name + ":" + ex.Message;
            }

            return null;
        }

        private static bool IsValidCommanderDeploymentSiegeWeapon(
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            BattleSideEnum side,
            bool allowDisabled,
            out Type weaponType,
            out string invalidReason)
        {
            weaponType = null;
            invalidReason = string.Empty;

            if (deploymentPoint == null ||
                siegeWeapon == null ||
                (!allowDisabled && siegeWeapon.IsDisabled) ||
                siegeWeapon.Side != side)
            {
                invalidReason = "invalid-context-or-side";
                return false;
            }

            if (!TryResolveCommanderDeploymentSiegeWeaponType(siegeWeapon, out weaponType))
            {
                invalidReason = "unresolved-weapon-type";
                return false;
            }

            try
            {
                foreach (SiegeWeapon deployableWeapon in EnumerateCommanderDeploymentPointSiegeWeapons(deploymentPoint))
                {
                    if (ReferenceEquals(deployableWeapon, siegeWeapon))
                        return true;
                }
            }
            catch
            {
            }

            invalidReason = "not-in-deployment-point-deployable-weapons";
            return false;
        }

        private static string TryPrepareCommanderDeploymentPointForServer(
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            bool enableObjects)
        {
            if (deploymentPoint == null)
                return "DeploymentPoint=<null>";

            int weaponsUnderCount = -1;
            bool weaponsFieldSet = false;
            bool determineTypeInvoked = false;
            bool deploymentPointEnabled = false;
            bool siegeWeaponEnabled = false;
            string error = string.Empty;

            try
            {
                MBList<SynchedMissionObject> weaponsUnder = deploymentPoint.GetWeaponsUnder();
                weaponsUnderCount = weaponsUnder?.Count ?? 0;
                if (CommanderDeploymentPointWeaponsField != null && weaponsUnder != null)
                {
                    CommanderDeploymentPointWeaponsField.SetValue(deploymentPoint, weaponsUnder);
                    weaponsFieldSet = true;
                }
            }
            catch (Exception ex)
            {
                error = AppendCommanderDeploymentPreparationError(error, "weapons", ex);
            }

            try
            {
                if (CommanderDeploymentPointDetermineTypeMethod != null)
                {
                    CommanderDeploymentPointDetermineTypeMethod.Invoke(deploymentPoint, Array.Empty<object>());
                    determineTypeInvoked = true;
                }
            }
            catch (Exception ex)
            {
                error = AppendCommanderDeploymentPreparationError(error, "determine-type", ex);
            }

            try
            {
                if (enableObjects && deploymentPoint.IsDisabled)
                {
                    deploymentPoint.SetEnabledAndMakeVisible();
                    deploymentPointEnabled = true;
                }
            }
            catch (Exception ex)
            {
                error = AppendCommanderDeploymentPreparationError(error, "enable-point", ex);
            }

            try
            {
                if (enableObjects && siegeWeapon != null && siegeWeapon.IsDisabled)
                {
                    siegeWeapon.SetEnabledAndMakeVisible();
                    siegeWeaponEnabled = true;
                }
            }
            catch (Exception ex)
            {
                error = AppendCommanderDeploymentPreparationError(error, "enable-weapon", ex);
            }

            return "WeaponsUnder=" + weaponsUnderCount +
                   " EnableObjects=" + enableObjects +
                   " WeaponsFieldSet=" + weaponsFieldSet +
                   " DetermineTypeInvoked=" + determineTypeInvoked +
                   " DeploymentPointEnabled=" + deploymentPointEnabled +
                   " SiegeWeaponEnabled=" + siegeWeaponEnabled +
                   " Error=" + (string.IsNullOrWhiteSpace(error) ? "<none>" : error);
        }

        private static string AppendCommanderDeploymentPreparationError(
            string current,
            string step,
            Exception exception)
        {
            string next = (step ?? "unknown") + ":" +
                          (exception == null ? "<null>" : exception.GetType().Name + ":" + exception.Message);
            return string.IsNullOrWhiteSpace(current) ? next : current + "|" + next;
        }

        private static bool TryResolveCommanderDeploymentSiegeWeaponType(
            SiegeWeapon siegeWeapon,
            out Type weaponType)
        {
            weaponType = null;
            if (siegeWeapon == null)
                return false;

            try
            {
                weaponType = MissionSiegeWeaponsController.GetWeaponType(siegeWeapon);
            }
            catch
            {
                weaponType = siegeWeapon.GetType();
            }

            return weaponType != null;
        }

        private int GetCommanderDeploymentMaxSiegeWeaponCount(
            BattleSideEnum side,
            Type weaponType)
        {
            if (side == BattleSideEnum.None || weaponType == null || Mission == null)
                return 0;

            try
            {
                MissionSiegeEnginesLogic siegeEnginesLogic = Mission.GetMissionBehavior<MissionSiegeEnginesLogic>();
                IMissionSiegeWeaponsController weaponsController = siegeEnginesLogic?.GetSiegeWeaponsController(side);
                return weaponsController?.GetMaxDeployableWeaponCount(weaponType) ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private int GetCommanderDeploymentRemainingSiegeWeaponCount(
            BattleSideEnum side,
            Type weaponType,
            DeploymentPoint ignoredDeploymentPoint,
            int maxCount,
            out string diagnostics)
        {
            diagnostics = string.Empty;
            if (side == BattleSideEnum.None || weaponType == null || Mission == null)
            {
                diagnostics = "invalid-context";
                return 0;
            }

            try
            {
                int deployedCount = 0;
                int scannedCount = 0;
                int skippedIgnoredCount = 0;
                int skippedSideCount = 0;
                int disabledPointCount = 0;
                int skippedNotDeployedCount = 0;
                int skippedInvalidWeaponCount = 0;
                int disabledWeaponCount = 0;
                int skippedTypeMismatchCount = 0;
                var countedPoints = new List<string>();

                foreach (DeploymentPoint deploymentPoint in EnumerateCommanderDeploymentPoints())
                {
                    scannedCount++;
                    if (deploymentPoint == null)
                    {
                        continue;
                    }

                    if (ReferenceEquals(deploymentPoint, ignoredDeploymentPoint))
                    {
                        skippedIgnoredCount++;
                        continue;
                    }

                    if (deploymentPoint.Side != side)
                    {
                        skippedSideCount++;
                        continue;
                    }

                    if (deploymentPoint.IsDisabled)
                        disabledPointCount++;

                    if (!deploymentPoint.IsDeployed || deploymentPoint.DeployedWeapon == null)
                    {
                        skippedNotDeployedCount++;
                        continue;
                    }

                    SiegeWeapon deployedSiegeWeapon = deploymentPoint.DeployedWeapon as SiegeWeapon;
                    if (deployedSiegeWeapon == null)
                    {
                        skippedInvalidWeaponCount++;
                        continue;
                    }

                    if (deployedSiegeWeapon.IsDisabled)
                        disabledWeaponCount++;

                    if (MissionSiegeWeaponsController.GetWeaponType(deployedSiegeWeapon) == weaponType)
                    {
                        deployedCount++;
                        countedPoints.Add(FormatDeploymentPoint(deploymentPoint) + "/" + FormatSiegeWeapon(deployedSiegeWeapon));
                    }
                    else
                    {
                        skippedTypeMismatchCount++;
                    }
                }

                diagnostics =
                    "Scanned=" + scannedCount +
                    " DeployedCount=" + deployedCount +
                    " SkippedIgnored=" + skippedIgnoredCount +
                    " SkippedSide=" + skippedSideCount +
                    " DisabledPointsSeen=" + disabledPointCount +
                    " SkippedNotDeployed=" + skippedNotDeployedCount +
                    " SkippedInvalidWeapon=" + skippedInvalidWeaponCount +
                    " DisabledWeaponsSeen=" + disabledWeaponCount +
                    " SkippedTypeMismatch=" + skippedTypeMismatchCount +
                    " Counted=[" + string.Join(",", countedPoints) + "]";
                return Math.Max(0, maxCount - deployedCount);
            }
            catch (Exception ex)
            {
                diagnostics = "exception:" + ex.GetType().Name + ":" + ex.Message;
                return 0;
            }
        }

        private static void ForceUpdateCommanderDeploymentTeamUnits(Team team)
        {
            if (team == null)
                return;

            try
            {
                foreach (Formation formation in team.FormationsIncludingEmpty)
                {
                    formation?.ApplyActionOnEachUnit(agent =>
                    {
                        agent?.ForceUpdateCachedAndFormationValues(
                            updateOnlyMovement: false,
                            arrangementChangeAllowed: false);
                    });
                }
            }
            catch
            {
            }
        }

        private static void PrepareCommanderDeploymentFormationsForSiegeMachineAssignment(Team team)
        {
            if (team == null)
                return;

            try
            {
                foreach (Formation formation in team.FormationsIncludingEmpty)
                {
                    if (formation == null)
                        continue;

                    formation.SetControlledByAI(true, true);
                }
            }
            catch
            {
            }
        }

        private static bool TryDisbandCommanderDeploymentPointSafely(
            DeploymentPoint deploymentPoint,
            BattleSideEnum side,
            string reason,
            out string diagnostics)
        {
            diagnostics = "Reason=" + (reason ?? "<null>");
            if (deploymentPoint == null)
            {
                diagnostics += " DeploymentPoint=<null>";
                return true;
            }

            SiegeWeapon sourceSiegeWeapon = deploymentPoint.DeployedWeapon as SiegeWeapon;
            bool wasDeployed = deploymentPoint.IsDeployed;
            Exception disbandException = null;
            try
            {
                if (deploymentPoint.IsDeployed)
                    deploymentPoint.Disband();
            }
            catch (Exception ex)
            {
                disbandException = ex;
            }

            string controllerRecoveryDiagnostics =
                TryRecoverCommanderDeploymentSiegeControllerAfterUndeploy(side, sourceSiegeWeapon);
            bool released =
                !wasDeployed ||
                !deploymentPoint.IsDeployed ||
                deploymentPoint.DeployedWeapon == null ||
                !ReferenceEquals(deploymentPoint.DeployedWeapon, sourceSiegeWeapon);

            diagnostics +=
                " WasDeployed=" + wasDeployed +
                " Released=" + released +
                " DisbandError=" + (disbandException == null ? "<none>" : disbandException.GetType().Name + ":" + disbandException.Message) +
                " ControllerRecovery={" + controllerRecoveryDiagnostics + "}";
            return disbandException == null || released;
        }

        private static bool TryDeployCommanderDeploymentPointSafely(
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            BattleSideEnum side,
            out string diagnostics)
        {
            diagnostics = string.Empty;
            if (deploymentPoint == null || siegeWeapon == null)
            {
                diagnostics = "DeploymentPoint=" + FormatDeploymentPoint(deploymentPoint) +
                              " SiegeWeapon=" + FormatSiegeWeapon(siegeWeapon);
                return false;
            }

            Exception deployException = null;
            try
            {
                deploymentPoint.Deploy(siegeWeapon);
            }
            catch (Exception ex)
            {
                deployException = ex;
            }

            string controllerRecoveryDiagnostics =
                TryRecoverCommanderDeploymentSiegeControllerAfterDeploy(side, siegeWeapon);
            bool deployed =
                deploymentPoint.IsDeployed &&
                ReferenceEquals(deploymentPoint.DeployedWeapon, siegeWeapon);

            diagnostics =
                "Deployed=" + deployed +
                " DeployError=" + (deployException == null ? "<none>" : deployException.GetType().Name + ":" + deployException.Message) +
                " ControllerRecovery={" + controllerRecoveryDiagnostics + "}";
            return deployException == null || deployed;
        }

        private static string TryRecoverCommanderDeploymentSiegeControllerAfterUndeploy(
            BattleSideEnum side,
            SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return "SiegeWeapon=<null>";

            if (!TryResolveCommanderDeploymentSiegeWeaponsController(
                    side,
                    out IMissionSiegeWeaponsController weaponsController,
                    out string controllerDiagnostics))
            {
                return controllerDiagnostics;
            }

            object destructionComponent = TryGetCommanderDeploymentDestructionComponent(siegeWeapon);
            string stateDiagnostics = BuildCommanderDeploymentControllerStateForWeapon(
                weaponsController,
                siegeWeapon,
                destructionComponent,
                out System.Collections.IList allWeapons,
                out System.Collections.IList undeployedWeapons,
                out System.Collections.IDictionary deployedWeapons,
                out MissionSiegeWeapon deployedMissionWeapon,
                out bool containsDeployedDestructionComponent,
                out bool containsUndeployedWeapon);

            string nativeDiagnostics = "<skipped>";
            if (containsDeployedDestructionComponent)
            {
                try
                {
                    weaponsController.OnWeaponUndeployed(siegeWeapon);
                    return "Native=True StateBefore={" + stateDiagnostics + "}";
                }
                catch (Exception ex)
                {
                    nativeDiagnostics = ex.GetType().Name + ":" + ex.Message;
                }
            }

            string manualDiagnostics = TryEnsureCommanderDeploymentUndeployedWeapon(
                siegeWeapon,
                destructionComponent,
                allWeapons,
                undeployedWeapons,
                deployedWeapons,
                deployedMissionWeapon);

            return "Native=" + nativeDiagnostics +
                   " StateBefore={" + stateDiagnostics + "}" +
                   " Manual={" + manualDiagnostics + "}" +
                   " UndeployedBefore=" + containsUndeployedWeapon;
        }

        private static string TryEnsureCommanderDeploymentSiegeControllerReadyForDeploy(
            BattleSideEnum side,
            SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return "SiegeWeapon=<null>";

            if (!TryResolveCommanderDeploymentSiegeWeaponsController(
                    side,
                    out IMissionSiegeWeaponsController weaponsController,
                    out string controllerDiagnostics))
            {
                return controllerDiagnostics;
            }

            object destructionComponent = TryGetCommanderDeploymentDestructionComponent(siegeWeapon);
            string stateDiagnostics = BuildCommanderDeploymentControllerStateForWeapon(
                weaponsController,
                siegeWeapon,
                destructionComponent,
                out System.Collections.IList allWeapons,
                out System.Collections.IList undeployedWeapons,
                out System.Collections.IDictionary deployedWeapons,
                out MissionSiegeWeapon deployedMissionWeapon,
                out bool containsDeployedDestructionComponent,
                out bool containsUndeployedWeapon);

            if (containsUndeployedWeapon)
                return "Ready=True StateBefore={" + stateDiagnostics + "}";

            string undeployRecoveryDiagnostics = containsDeployedDestructionComponent
                ? TryRecoverCommanderDeploymentSiegeControllerAfterUndeploy(side, siegeWeapon)
                : "<skipped>";
            if (FindCommanderDeploymentMissionSiegeWeaponByType(
                    undeployedWeapons,
                    TryGetCommanderDeploymentSiegeEngineType(siegeWeapon)) != null)
            {
                return "Ready=True StateBefore={" + stateDiagnostics + "} UndeployRecovery={" + undeployRecoveryDiagnostics + "}";
            }

            string manualDiagnostics = TryEnsureCommanderDeploymentUndeployedWeapon(
                siegeWeapon,
                destructionComponent,
                allWeapons,
                undeployedWeapons,
                deployedWeapons,
                deployedMissionWeapon);
            return "Ready=False StateBefore={" + stateDiagnostics + "}" +
                   " UndeployRecovery={" + undeployRecoveryDiagnostics + "}" +
                   " Manual={" + manualDiagnostics + "}";
        }

        private static string TryRecoverCommanderDeploymentSiegeControllerAfterDeploy(
            BattleSideEnum side,
            SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return "SiegeWeapon=<null>";

            if (!TryResolveCommanderDeploymentSiegeWeaponsController(
                    side,
                    out IMissionSiegeWeaponsController weaponsController,
                    out string controllerDiagnostics))
            {
                return controllerDiagnostics;
            }

            object destructionComponent = TryGetCommanderDeploymentDestructionComponent(siegeWeapon);
            string stateDiagnostics = BuildCommanderDeploymentControllerStateForWeapon(
                weaponsController,
                siegeWeapon,
                destructionComponent,
                out System.Collections.IList allWeapons,
                out System.Collections.IList undeployedWeapons,
                out System.Collections.IDictionary deployedWeapons,
                out _,
                out bool containsDeployedDestructionComponent,
                out bool containsUndeployedWeapon);

            if (containsDeployedDestructionComponent)
                return "Deployed=True StateBefore={" + stateDiagnostics + "}";

            string nativeDiagnostics = "<skipped>";
            if (containsUndeployedWeapon)
            {
                try
                {
                    weaponsController.OnWeaponDeployed(siegeWeapon);
                    return "Native=True StateBefore={" + stateDiagnostics + "}";
                }
                catch (Exception ex)
                {
                    nativeDiagnostics = ex.GetType().Name + ":" + ex.Message;
                }
            }

            string manualDiagnostics = TryEnsureCommanderDeploymentDeployedWeapon(
                siegeWeapon,
                destructionComponent,
                allWeapons,
                undeployedWeapons,
                deployedWeapons);
            return "Native=" + nativeDiagnostics +
                   " StateBefore={" + stateDiagnostics + "}" +
                   " Manual={" + manualDiagnostics + "}";
        }

        private static bool TryResolveCommanderDeploymentSiegeWeaponsController(
            BattleSideEnum side,
            out IMissionSiegeWeaponsController weaponsController,
            out string diagnostics)
        {
            weaponsController = null;
            diagnostics = string.Empty;
            try
            {
                TaleWorlds.MountAndBlade.Mission mission = TaleWorlds.MountAndBlade.Mission.Current;
                MissionSiegeEnginesLogic siegeEnginesLogic = mission?.GetMissionBehavior<MissionSiegeEnginesLogic>();
                weaponsController = siegeEnginesLogic?.GetSiegeWeaponsController(side);
                diagnostics = weaponsController == null
                    ? "Controller=<null>"
                    : "Controller=" + weaponsController.GetType().FullName;
                return weaponsController != null;
            }
            catch (Exception ex)
            {
                diagnostics = "ControllerError=" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static object TryGetCommanderDeploymentDestructionComponent(SiegeWeapon siegeWeapon)
        {
            try
            {
                return siegeWeapon?.DestructionComponent;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildCommanderDeploymentControllerStateForWeapon(
            IMissionSiegeWeaponsController weaponsController,
            SiegeWeapon siegeWeapon,
            object destructionComponent,
            out System.Collections.IList allWeapons,
            out System.Collections.IList undeployedWeapons,
            out System.Collections.IDictionary deployedWeapons,
            out MissionSiegeWeapon deployedMissionWeapon,
            out bool containsDeployedDestructionComponent,
            out bool containsUndeployedWeapon)
        {
            allWeapons = CommanderDeploymentSiegeControllerWeaponsField?.GetValue(weaponsController) as System.Collections.IList;
            undeployedWeapons = CommanderDeploymentSiegeControllerUndeployedWeaponsField?.GetValue(weaponsController) as System.Collections.IList;
            deployedWeapons = CommanderDeploymentSiegeControllerDeployedWeaponsField?.GetValue(weaponsController) as System.Collections.IDictionary;
            deployedMissionWeapon = null;
            containsDeployedDestructionComponent = false;
            containsUndeployedWeapon = false;

            SiegeEngineType weaponType = TryGetCommanderDeploymentSiegeEngineType(siegeWeapon);
            try
            {
                if (deployedWeapons != null &&
                    destructionComponent != null &&
                    deployedWeapons.Contains(destructionComponent))
                {
                    containsDeployedDestructionComponent = true;
                    deployedMissionWeapon = deployedWeapons[destructionComponent] as MissionSiegeWeapon;
                }
            }
            catch
            {
            }

            containsUndeployedWeapon =
                FindCommanderDeploymentMissionSiegeWeaponByType(undeployedWeapons, weaponType) != null;

            return "All=" + (allWeapons?.Count.ToString() ?? "<null>") +
                   " Undeployed=" + (undeployedWeapons?.Count.ToString() ?? "<null>") +
                   " Deployed=" + (deployedWeapons?.Count.ToString() ?? "<null>") +
                   " ContainsDeployed=" + containsDeployedDestructionComponent +
                   " ContainsUndeployedType=" + containsUndeployedWeapon +
                   " Type=" + FormatCommanderDeploymentSiegeEngineType(weaponType);
        }

        private static string TryEnsureCommanderDeploymentUndeployedWeapon(
            SiegeWeapon siegeWeapon,
            object destructionComponent,
            System.Collections.IList allWeapons,
            System.Collections.IList undeployedWeapons,
            System.Collections.IDictionary deployedWeapons,
            MissionSiegeWeapon preferredMissionWeapon)
        {
            if (undeployedWeapons == null)
                return "UndeployedList=<null>";

            SiegeEngineType weaponType = TryGetCommanderDeploymentSiegeEngineType(siegeWeapon);
            if (FindCommanderDeploymentMissionSiegeWeaponByType(undeployedWeapons, weaponType) != null)
                return "AlreadyUndeployed=True";

            MissionSiegeWeapon missionSiegeWeapon = preferredMissionWeapon ??
                FindCommanderDeploymentReusableMissionSiegeWeapon(allWeapons, undeployedWeapons, deployedWeapons, weaponType);
            if (missionSiegeWeapon == null)
                return "MissionSiegeWeapon=<null>";

            try
            {
                if (!ContainsCommanderDeploymentMissionSiegeWeapon(undeployedWeapons, missionSiegeWeapon))
                    undeployedWeapons.Add(missionSiegeWeapon);

                if (deployedWeapons != null &&
                    destructionComponent != null &&
                    deployedWeapons.Contains(destructionComponent))
                {
                    deployedWeapons.Remove(destructionComponent);
                }

                return "Added=True MissionWeapon=" + FormatCommanderDeploymentMissionSiegeWeapon(missionSiegeWeapon, weaponType);
            }
            catch (Exception ex)
            {
                return "Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string TryEnsureCommanderDeploymentDeployedWeapon(
            SiegeWeapon siegeWeapon,
            object destructionComponent,
            System.Collections.IList allWeapons,
            System.Collections.IList undeployedWeapons,
            System.Collections.IDictionary deployedWeapons)
        {
            if (deployedWeapons == null)
                return "DeployedDictionary=<null>";

            if (destructionComponent == null)
                return "DestructionComponent=<null>";

            SiegeEngineType weaponType = TryGetCommanderDeploymentSiegeEngineType(siegeWeapon);
            MissionSiegeWeapon missionSiegeWeapon =
                FindCommanderDeploymentMissionSiegeWeaponByType(undeployedWeapons, weaponType) ??
                FindCommanderDeploymentReusableMissionSiegeWeapon(allWeapons, undeployedWeapons, deployedWeapons, weaponType);
            if (missionSiegeWeapon == null)
                return "MissionSiegeWeapon=<null>";

            try
            {
                if (undeployedWeapons != null &&
                    ContainsCommanderDeploymentMissionSiegeWeapon(undeployedWeapons, missionSiegeWeapon))
                {
                    undeployedWeapons.Remove(missionSiegeWeapon);
                }

                if (!deployedWeapons.Contains(destructionComponent))
                    deployedWeapons.Add(destructionComponent, missionSiegeWeapon);

                return "Added=True MissionWeapon=" + FormatCommanderDeploymentMissionSiegeWeapon(missionSiegeWeapon, weaponType);
            }
            catch (Exception ex)
            {
                return "Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static MissionSiegeWeapon FindCommanderDeploymentReusableMissionSiegeWeapon(
            System.Collections.IList allWeapons,
            System.Collections.IList undeployedWeapons,
            System.Collections.IDictionary deployedWeapons,
            SiegeEngineType weaponType)
        {
            MissionSiegeWeapon fromDeployed =
                FindCommanderDeploymentMissionSiegeWeaponByType(GetCommanderDeploymentDictionaryValues(deployedWeapons), weaponType);
            if (fromDeployed != null)
                return fromDeployed;

            if (allWeapons == null)
                return null;

            foreach (object entry in allWeapons)
            {
                MissionSiegeWeapon missionSiegeWeapon = TryExtractCommanderDeploymentMissionSiegeWeapon(entry);
                if (!CommanderDeploymentMissionSiegeWeaponMatches(missionSiegeWeapon, weaponType))
                    continue;

                if (ContainsCommanderDeploymentMissionSiegeWeapon(undeployedWeapons, missionSiegeWeapon) ||
                    ContainsCommanderDeploymentMissionSiegeWeapon(GetCommanderDeploymentDictionaryValues(deployedWeapons), missionSiegeWeapon))
                {
                    continue;
                }

                return missionSiegeWeapon;
            }

            return FindCommanderDeploymentMissionSiegeWeaponByType(allWeapons, weaponType);
        }

        private static System.Collections.IEnumerable GetCommanderDeploymentDictionaryValues(
            System.Collections.IDictionary dictionary)
        {
            if (dictionary == null)
                yield break;

            foreach (object entry in dictionary)
            {
                if (entry is System.Collections.DictionaryEntry dictionaryEntry)
                    yield return dictionaryEntry.Value;
            }
        }

        private static MissionSiegeWeapon FindCommanderDeploymentMissionSiegeWeaponByType(
            System.Collections.IEnumerable entries,
            SiegeEngineType weaponType)
        {
            if (entries == null)
                return null;

            foreach (object entry in entries)
            {
                MissionSiegeWeapon missionSiegeWeapon = TryExtractCommanderDeploymentMissionSiegeWeapon(entry);
                if (CommanderDeploymentMissionSiegeWeaponMatches(missionSiegeWeapon, weaponType))
                    return missionSiegeWeapon;
            }

            return null;
        }

        private static bool ContainsCommanderDeploymentMissionSiegeWeapon(
            System.Collections.IEnumerable entries,
            MissionSiegeWeapon missionSiegeWeapon)
        {
            if (entries == null || missionSiegeWeapon == null)
                return false;

            foreach (object entry in entries)
            {
                if (ReferenceEquals(TryExtractCommanderDeploymentMissionSiegeWeapon(entry), missionSiegeWeapon))
                    return true;
            }

            return false;
        }

        private static bool CommanderDeploymentMissionSiegeWeaponMatches(
            MissionSiegeWeapon missionSiegeWeapon,
            SiegeEngineType weaponType)
        {
            if (missionSiegeWeapon == null || weaponType == null)
                return false;

            if (ReferenceEquals(missionSiegeWeapon.Type, weaponType))
                return true;

            return string.Equals(
                missionSiegeWeapon.Type?.StringId,
                weaponType.StringId,
                StringComparison.Ordinal);
        }

        private static string AppendCommanderDeploymentDiagnostics(
            string existing,
            string label,
            string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
                return existing ?? string.Empty;

            string segment = (label ?? "Detail") + "={" + detail + "}";
            if (string.IsNullOrWhiteSpace(existing))
                return segment;

            return existing + " " + segment;
        }

        private static string BuildCommanderDeploymentSiegeControllerDiagnostics(
            BattleSideEnum side,
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon)
        {
            if (!IsCommanderDeploymentOrderOfBattleDiagnosticsEnabled())
                return string.Empty;

            try
            {
                SiegeEngineType missionWeaponType = TryGetCommanderDeploymentSiegeEngineType(siegeWeapon);
                Type weaponBaseType = TryGetCommanderDeploymentSiegeWeaponType(siegeWeapon);
                TaleWorlds.MountAndBlade.Mission mission = TaleWorlds.MountAndBlade.Mission.Current;
                MissionSiegeEnginesLogic siegeEnginesLogic = mission?.GetMissionBehavior<MissionSiegeEnginesLogic>();
                IMissionSiegeWeaponsController weaponsController = null;
                int maxDeployableCount = -1;
                string controllerError = string.Empty;

                try
                {
                    weaponsController = siegeEnginesLogic?.GetSiegeWeaponsController(side);
                }
                catch (Exception ex)
                {
                    controllerError = "get-controller:" + ex.GetType().Name + ":" + ex.Message;
                }

                try
                {
                    if (weaponsController != null && weaponBaseType != null)
                        maxDeployableCount = weaponsController.GetMaxDeployableWeaponCount(weaponBaseType);
                }
                catch (Exception ex)
                {
                    controllerError = AppendCommanderDeploymentPreparationError(
                        controllerError,
                        "max-count",
                        ex);
                }

                return
                    "Event={" + BuildCommanderDeploymentPointEventDiagnostics(deploymentPoint) + "}" +
                    " MissionScene=" + (mission?.SceneName ?? "<null>") +
                    " MissionMode=" + (mission == null ? "<null>" : mission.Mode.ToString()) +
                    " MissionState=" + (mission == null ? "<null>" : mission.CurrentState.ToString()) +
                    " Logic=" + (siegeEnginesLogic == null ? "<null>" : siegeEnginesLogic.GetType().FullName) +
                    " Controller=" + (weaponsController == null ? "<null>" : weaponsController.GetType().FullName) +
                    " RequestedSide=" + side +
                    " MissionWeaponType=" + FormatCommanderDeploymentSiegeEngineType(missionWeaponType) +
                    " WeaponBaseType=" + (weaponBaseType == null ? "<null>" : weaponBaseType.FullName) +
                    " MaxDeployableCount=" + maxDeployableCount +
                    " Weapons={" + BuildCommanderDeploymentSiegeControllerListDiagnostics(
                        CommanderDeploymentSiegeControllerWeaponsField,
                        weaponsController,
                        missionWeaponType) + "}" +
                    " UndeployedWeapons={" + BuildCommanderDeploymentSiegeControllerListDiagnostics(
                        CommanderDeploymentSiegeControllerUndeployedWeaponsField,
                        weaponsController,
                        missionWeaponType) + "}" +
                    " DeployedWeapons={" + BuildCommanderDeploymentSiegeControllerListDiagnostics(
                        CommanderDeploymentSiegeControllerDeployedWeaponsField,
                        weaponsController,
                        missionWeaponType) + "}" +
                    " Error=" + (string.IsNullOrWhiteSpace(controllerError) ? "<none>" : controllerError);
            }
            catch (Exception ex)
            {
                return "exception:" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string BuildCommanderDeploymentPointEventDiagnostics(DeploymentPoint deploymentPoint)
        {
            if (deploymentPoint == null)
                return "DeploymentPoint=<null>";

            if (CommanderDeploymentPointOnDeploymentStateChangedField == null)
                return "FieldMissing=True";

            try
            {
                Delegate handler = CommanderDeploymentPointOnDeploymentStateChangedField.GetValue(deploymentPoint) as Delegate;
                if (handler == null)
                    return "Count=0";

                Delegate[] subscribers = handler.GetInvocationList();
                var subscriberDescriptions = new List<string>();
                int limit = Math.Min(subscribers.Length, 8);
                for (int i = 0; i < limit; i++)
                {
                    Delegate subscriber = subscribers[i];
                    MethodInfo method = subscriber?.Method;
                    object target = subscriber?.Target;
                    subscriberDescriptions.Add(
                        (target == null ? "<static>" : target.GetType().FullName) +
                        "." +
                        (method == null ? "<null>" : method.Name));
                }

                return "Count=" + subscribers.Length +
                       " Subscribers=[" + string.Join(",", subscriberDescriptions.ToArray()) + "]";
            }
            catch (Exception ex)
            {
                return "exception:" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string BuildCommanderDeploymentSiegeControllerListDiagnostics(
            FieldInfo field,
            object weaponsController,
            SiegeEngineType requestedType)
        {
            if (field == null)
                return "FieldMissing=True";

            if (weaponsController == null)
                return "Controller=<null>";

            try
            {
                object rawValue = field.GetValue(weaponsController);
                if (!(rawValue is System.Collections.IEnumerable entries))
                    return "Enumerable=False RawType=" + (rawValue == null ? "<null>" : rawValue.GetType().FullName);

                int count = 0;
                int exactMatches = 0;
                int stringIdMatches = 0;
                var itemDescriptions = new List<string>();
                foreach (object entry in entries)
                {
                    MissionSiegeWeapon missionSiegeWeapon = TryExtractCommanderDeploymentMissionSiegeWeapon(entry);
                    if (missionSiegeWeapon != null)
                    {
                        if (ReferenceEquals(missionSiegeWeapon.Type, requestedType))
                            exactMatches++;

                        if (requestedType != null &&
                            string.Equals(
                                missionSiegeWeapon.Type?.StringId,
                                requestedType.StringId,
                                StringComparison.Ordinal))
                        {
                            stringIdMatches++;
                        }
                    }

                    if (itemDescriptions.Count < 8)
                    {
                        itemDescriptions.Add(
                            "#" + count + "=" +
                            (missionSiegeWeapon == null
                                ? "<" + (entry == null ? "null" : entry.GetType().FullName) + ">"
                                : FormatCommanderDeploymentMissionSiegeWeapon(missionSiegeWeapon, requestedType)));
                    }

                    count++;
                }

                return "Count=" + count +
                       " ExactMatches=" + exactMatches +
                       " StringIdMatches=" + stringIdMatches +
                       " Items=[" + string.Join(",", itemDescriptions.ToArray()) + "]";
            }
            catch (Exception ex)
            {
                return "exception:" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static MissionSiegeWeapon TryExtractCommanderDeploymentMissionSiegeWeapon(object entry)
        {
            if (entry == null)
                return null;

            MissionSiegeWeapon missionSiegeWeapon = entry as MissionSiegeWeapon;
            if (missionSiegeWeapon != null)
                return missionSiegeWeapon;

            try
            {
                PropertyInfo valueProperty = entry.GetType().GetProperty(
                    "Value",
                    BindingFlags.Instance | BindingFlags.Public);
                return valueProperty?.GetValue(entry, null) as MissionSiegeWeapon;
            }
            catch
            {
                return null;
            }
        }

        private static string FormatCommanderDeploymentMissionSiegeWeapon(
            MissionSiegeWeapon missionSiegeWeapon,
            SiegeEngineType requestedType)
        {
            if (missionSiegeWeapon == null)
                return "<null>";

            return "Index=" + missionSiegeWeapon.Index +
                   "/Type=" + FormatCommanderDeploymentSiegeEngineType(missionSiegeWeapon.Type) +
                   "/Exact=" + ReferenceEquals(missionSiegeWeapon.Type, requestedType) +
                   "/Health=" + FormatCommanderDeploymentFloat(missionSiegeWeapon.Health) +
                   "/Initial=" + FormatCommanderDeploymentFloat(missionSiegeWeapon.InitialHealth) +
                   "/Max=" + FormatCommanderDeploymentFloat(missionSiegeWeapon.MaxHealth);
        }

        private static SiegeEngineType TryGetCommanderDeploymentSiegeEngineType(SiegeWeapon siegeWeapon)
        {
            try
            {
                return siegeWeapon?.GetSiegeEngineType();
            }
            catch
            {
                return null;
            }
        }

        private static Type TryGetCommanderDeploymentSiegeWeaponType(SiegeWeapon siegeWeapon)
        {
            try
            {
                return siegeWeapon == null ? null : MissionSiegeWeaponsController.GetWeaponType(siegeWeapon);
            }
            catch
            {
                return null;
            }
        }

        private static string FormatCommanderDeploymentSiegeEngineType(SiegeEngineType siegeEngineType)
        {
            if (siegeEngineType == null)
                return "<null>";

            string stringId = string.IsNullOrWhiteSpace(siegeEngineType.StringId)
                ? "<empty>"
                : siegeEngineType.StringId;
            return stringId +
                   "/InstanceHash=" +
                   System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(siegeEngineType);
        }

        private static string FormatCommanderDeploymentFloat(float value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatDeploymentPoint(DeploymentPoint deploymentPoint)
        {
            if (deploymentPoint == null)
                return "<null>";

            return "Id=" + deploymentPoint.Id +
                   "/Side=" + deploymentPoint.Side +
                   "/Disabled=" + deploymentPoint.IsDisabled +
                   "/Deployed=" + deploymentPoint.IsDeployed;
        }

        private static string FormatSiegeWeapon(SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return "<null>";

            return "Id=" + siegeWeapon.Id +
                   "/Side=" + siegeWeapon.Side +
                   "/Disabled=" + siegeWeapon.IsDisabled +
                   "/Type=" + (siegeWeapon.GetSiegeEngineType()?.StringId ?? "<null>");
        }

        private static void LogCommanderDeploymentSiegeMachineDiagnostics(
            string stage,
            NetworkCommunicator peer,
            CoopCommanderDeploymentSiegeMachineSelectionMessage message,
            Team team,
            string detail)
        {
            if (!IsCommanderDeploymentOrderOfBattleDiagnosticsEnabled())
                return;

            try
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: commander deployment siege machine diagnostics. " +
                    "Stage=" + (stage ?? string.Empty) +
                    " Peer=" + (peer?.UserName ?? "<null>") +
                    " Team=" + (team == null ? "<null>" : team.Side + "#" + team.TeamIndex) +
                    " RequestedSide=" + (message?.RequestedSide.ToString() ?? "<null>") +
                    " DeploymentPointId=" + (message == null ? "<null>" : message.DeploymentPointId.ToString()) +
                    " DeploymentPointPosition=" + (message == null ? "<null>" : message.DeploymentPointPosition.ToString()) +
                    " SiegeWeaponId=" + (message == null ? "<null>" : message.SiegeWeaponId.ToString()) +
                    " SiegeWeaponType=" + (message?.SiegeWeaponTypeName ?? "<null>") +
                    " Clear=" + (message?.ClearSelection.ToString() ?? "<null>") +
                    " Detail=" + (detail ?? string.Empty));
            }
            catch
            {
            }
        }

        private static void LogCommanderDeploymentSiegeMachineStateDiagnostics(
            string stage,
            CoopCommanderDeploymentSiegeMachineStateMessage message,
            string detail)
        {
            if (!IsCommanderDeploymentOrderOfBattleDiagnosticsEnabled())
                return;

            try
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: commander deployment siege machine state diagnostics. " +
                    "Stage=" + (stage ?? string.Empty) +
                    " RequestedSide=" + (message?.RequestedSide.ToString() ?? "<null>") +
                    " DeploymentPointId=" + (message == null ? "<null>" : message.DeploymentPointId.ToString()) +
                    " DeploymentPointPosition=" + (message == null ? "<null>" : message.DeploymentPointPosition.ToString()) +
                    " SiegeWeaponId=" + (message == null ? "<null>" : message.SiegeWeaponId.ToString()) +
                    " SiegeWeaponType=" + (message?.SiegeWeaponTypeName ?? "<null>") +
                    " Clear=" + (message?.ClearSelection.ToString() ?? "<null>") +
                    " Detail=" + (detail ?? string.Empty));
            }
            catch
            {
            }
        }

        private static void LogSiegeMissionObjectIdMapDiagnostics(
            string stage,
            CoopSiegeMissionObjectIdMapMessage message,
            string detail)
        {
            if (!IsCommanderDeploymentOrderOfBattleDiagnosticsEnabled())
                return;

            try
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: siege mission object id map diagnostics. " +
                    "Stage=" + (stage ?? string.Empty) +
                    " Side=" + (message?.ObjectSide.ToString() ?? "<null>") +
                    " ServerMissionObjectId=" + (message == null ? "<null>" : message.ServerMissionObjectId.ToString()) +
                    " ObjectType=" + (message?.ObjectTypeName ?? "<null>") +
                    " Entity=" + (message?.EntityName ?? "<null>") +
                    " SignatureLength=" + (message?.Signature?.Length ?? 0) +
                    " Detail=" + (detail ?? string.Empty));
            }
            catch
            {
            }
        }

        private bool TryApplyCommanderDeploymentFormationAssignments(
            NetworkCommunicator peer,
            CoopCommanderDeploymentFormationAssignmentsMessage message)
        {
            if (!GameNetwork.IsServer || peer == null || message == null)
            {
                LogCommanderDeploymentAssignmentDiagnostics(
                    "skip-context",
                    peer,
                    message,
                    null,
                    0,
                    0,
                    0,
                    0,
                    "IsServer=" + GameNetwork.IsServer);
                return false;
            }

            byte[] assignmentBytes = message.AssignmentBytes ?? Array.Empty<byte>();
            if (assignmentBytes.Length <= 0)
            {
                LogCommanderDeploymentAssignmentDiagnostics(
                    "skip-invalid-payload",
                    peer,
                    message,
                    null,
                    0,
                    0,
                    0,
                    0,
                    "AssignmentBytes=" + assignmentBytes.Length);
                return false;
            }

            if (!CoopMissionSpawnLogic.TryResolveCommanderDeploymentOrderLease(
                    peer,
                    out Team team,
                    out OrderController orderController,
                    out Agent commanderAgent))
            {
                LogCommanderDeploymentAssignmentDiagnostics(
                    "skip-no-order-lease",
                    peer,
                    message,
                    null,
                    0,
                    0,
                    0,
                    0,
                    string.Empty);
                return false;
            }

            if (message.RequestedSide != BattleSideEnum.None &&
                team != null &&
                team.Side != message.RequestedSide)
            {
                LogCommanderDeploymentAssignmentDiagnostics(
                    "skip-side-mismatch",
                    peer,
                    message,
                    team,
                    0,
                    0,
                    0,
                    0,
                    string.Empty);
                return false;
            }

            Mission mission = this.Mission ?? Mission.Current;
            if (mission == null || team == null)
            {
                LogCommanderDeploymentAssignmentDiagnostics(
                    "skip-missing-mission-team",
                    peer,
                    message,
                    team,
                    0,
                    0,
                    0,
                    0,
                    "MissionNull=" + (mission == null));
                return false;
            }

            Dictionary<int, CommanderDeploymentFormationLayout> formationLayouts =
                DecodeCommanderDeploymentFormationLayouts(message.FormationLayoutBytes);
            bool hasCaptainAssignmentPayload = message.CaptainAssignmentBytes != null &&
                                               message.CaptainAssignmentBytes.Length > 0;
            var delegatedCaptainAssignments = new Dictionary<int, string>();
            if (hasCaptainAssignmentPayload &&
                !TryDecodeDelegatedCaptainAssignments(
                    mission,
                    team,
                    message.CaptainAssignmentBytes,
                    out delegatedCaptainAssignments,
                    out string captainDecodeError))
            {
                LogCommanderDeploymentAssignmentDiagnostics(
                    "skip-invalid-captain-payload",
                    peer,
                    message,
                    team,
                    0,
                    0,
                    0,
                    formationLayouts.Count,
                    captainDecodeError ?? string.Empty);
                return false;
            }
            var moves = new List<(Agent Agent, Formation TargetFormation)>();
            var impactedFormations = new HashSet<Formation>();
            var layoutFormations = new HashSet<Formation>();
            int decodedAssignments = 0;
            int rejectedAssignments = 0;

            foreach (KeyValuePair<int, CommanderDeploymentFormationLayout> layout in formationLayouts)
            {
                Formation formation = ResolveCommanderDeploymentFormation(team, layout.Key);
                if (formation != null)
                    layoutFormations.Add(formation);
            }

            LogCommanderDeploymentAssignmentDiagnostics(
                "decoded",
                peer,
                message,
                team,
                0,
                0,
                0,
                formationLayouts.Count,
                "Before=[" + BuildCommanderDeploymentFormationSummary(team) + "]");

            if (IsCommanderDeploymentCompositionAssignmentPayload(assignmentBytes))
            {
                if (!TryDecodeCommanderDeploymentFormationCompositionPayload(
                        assignmentBytes,
                        out List<CommanderDeploymentFormationComposition> compositionRecords,
                        out bool preserveMountedClasses,
                        out string decodeError))
                {
                    LogCommanderDeploymentAssignmentDiagnostics(
                        "skip-invalid-composition-payload",
                        peer,
                        message,
                        team,
                        0,
                        0,
                        0,
                        formationLayouts.Count,
                        decodeError ?? string.Empty);
                    return false;
                }

                bool compositionApplied = TryApplyCommanderDeploymentFormationCompositionAssignments(
                    peer,
                    message,
                    mission,
                    team,
                    orderController,
                    commanderAgent,
                    formationLayouts,
                    layoutFormations,
                    compositionRecords,
                    preserveMountedClasses);
                if (compositionApplied && hasCaptainAssignmentPayload)
                    ApplyDelegatedCaptainAssignmentSnapshot(mission, team, delegatedCaptainAssignments);

                return compositionApplied;
            }

            if (assignmentBytes.Length % CoopCommanderDeploymentFormationAssignmentsMessage.BytesPerAssignment != 0)
            {
                LogCommanderDeploymentAssignmentDiagnostics(
                    "skip-invalid-legacy-payload",
                    peer,
                    message,
                    team,
                    0,
                    0,
                    0,
                    formationLayouts.Count,
                    "AssignmentBytes=" + assignmentBytes.Length);
                return false;
            }

            for (int offset = 0; offset + 2 < assignmentBytes.Length; offset += CoopCommanderDeploymentFormationAssignmentsMessage.BytesPerAssignment)
            {
                int agentIndex = assignmentBytes[offset] | (assignmentBytes[offset + 1] << 8);
                int formationIndex = assignmentBytes[offset + 2];
                decodedAssignments++;

                Agent agent = ResolveMissionAgent(agentIndex);
                Formation targetFormation = ResolveCommanderDeploymentFormation(team, formationIndex);
                if (!IsValidCommanderDeploymentAssignmentAgent(agent, team) || targetFormation == null)
                {
                    rejectedAssignments++;
                    continue;
                }

                Formation sourceFormation = agent.Formation;
                if (ReferenceEquals(sourceFormation, targetFormation))
                    continue;

                if (sourceFormation != null)
                    impactedFormations.Add(sourceFormation);
                impactedFormations.Add(targetFormation);
                moves.Add((agent, targetFormation));
            }

            if (moves.Count <= 0)
            {
                LogCommanderDeploymentAssignmentWarningIfSuspicious(
                    "legacy-no-moves",
                    peer,
                    message,
                    team,
                    decodedAssignments,
                    moves.Count,
                    rejectedAssignments,
                    formationLayouts.Count,
                    "After=[" + BuildCommanderDeploymentFormationSummary(team) + "]");
            }

            if (moves.Count <= 0 && layoutFormations.Count <= 0)
            {
                LogCommanderDeploymentAssignmentDiagnostics(
                    "no-moves",
                    peer,
                    message,
                    team,
                    decodedAssignments,
                    moves.Count,
                    rejectedAssignments,
                    formationLayouts.Count,
                    "After=[" + BuildCommanderDeploymentFormationSummary(team) + "]");
                LogCommanderDeploymentAssignmentWarningIfSuspicious(
                    "no-moves",
                    peer,
                    message,
                    team,
                    decodedAssignments,
                    moves.Count,
                    rejectedAssignments,
                    formationLayouts.Count,
                    "After=[" + BuildCommanderDeploymentFormationSummary(team) + "]");
                return decodedAssignments > 0 && rejectedAssignments < decodedAssignments;
            }

            bool previousTeleportingAgents = mission.IsTeleportingAgents;
            try
            {
                mission.IsTeleportingAgents = true;

                foreach (Formation formation in impactedFormations)
                    TryStartCommanderDeploymentMassTransfer(formation);

                foreach ((Agent agent, Formation targetFormation) in moves)
                    agent.Formation = targetFormation;

                var formationsToFinalize = new HashSet<Formation>(impactedFormations);
                foreach (Formation formation in layoutFormations)
                    formationsToFinalize.Add(formation);

                foreach (Formation formation in formationsToFinalize)
                {
                    formationLayouts.TryGetValue(formation.Index, out CommanderDeploymentFormationLayout layout);
                    FinalizeCommanderDeploymentFormationAssignment(
                        mission,
                        team,
                        formation,
                        commanderAgent,
                        impactedFormations.Contains(formation),
                        layout);
                }

                CoopMissionSpawnLogic.TryResolveCommanderDeploymentOrderLease(
                    peer,
                    out _,
                    out _,
                    out _);
                CommanderDeploymentMissionNetworkComponentPatch.TryRefreshCommanderDeploymentSelection(
                    peer,
                    team,
                    orderController);
            }
            finally
            {
                mission.IsTeleportingAgents = previousTeleportingAgents;
            }

            if (IsCommanderDeploymentOrderOfBattleDiagnosticsEnabled())
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: applied commander deployment formation assignment sync. " +
                    "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                    " Side=" + team.Side +
                    " Decoded=" + decodedAssignments +
                    " AppliedMoves=" + moves.Count +
                    " Layouts=" + formationLayouts.Count +
                    " Rejected=" + rejectedAssignments +
                    " Formations=[" + BuildCommanderDeploymentFormationSummary(team) + "]");
            }

            return true;
        }

        internal static bool TryGetDelegatedFormationIndices(
            Mission mission,
            Team team,
            string entryId,
            out List<int> formationIndices)
        {
            formationIndices = new List<int>();
            if (mission == null || team == null || string.IsNullOrWhiteSpace(entryId))
                return false;

            CoopMissionNetworkBridge bridge = mission.GetMissionBehavior<CoopMissionNetworkBridge>();
            if (bridge == null ||
                !bridge._delegatedCaptainEntryIdByFormationIndexAndSide.TryGetValue(
                    team.Side,
                    out Dictionary<int, string> assignments))
            {
                return false;
            }

            foreach (KeyValuePair<int, string> assignment in assignments)
            {
                if (string.Equals(assignment.Value, entryId, StringComparison.Ordinal))
                    formationIndices.Add(assignment.Key);
            }

            formationIndices.Sort();
            return formationIndices.Count > 0;
        }

        internal static IReadOnlyList<string> GetDelegatedCaptainEntryIds(Mission mission)
        {
            if (mission == null)
                return Array.Empty<string>();

            CoopMissionNetworkBridge bridge = mission.GetMissionBehavior<CoopMissionNetworkBridge>();
            if (bridge == null)
                return Array.Empty<string>();

            return bridge._delegatedCaptainEntryIdByFormationIndexAndSide.Values
                .Where(assignments => assignments != null)
                .SelectMany(assignments => assignments.Values)
                .Where(entryId => !string.IsNullOrWhiteSpace(entryId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        internal static bool TryResolveAuthorizedFormationIndices(
            Mission mission,
            Team team,
            string entryId,
            out List<int> formationIndices,
            out string authorityRole)
        {
            formationIndices = new List<int>();
            authorityRole = "none";
            if (mission == null || team == null || string.IsNullOrWhiteSpace(entryId))
                return false;

            CoopMissionNetworkBridge bridge = mission.GetMissionBehavior<CoopMissionNetworkBridge>();
            Dictionary<int, string> assignments = null;
            if (bridge != null)
            {
                bridge._delegatedCaptainEntryIdByFormationIndexAndSide.TryGetValue(
                    team.Side,
                    out assignments);
            }
            assignments = assignments ?? new Dictionary<int, string>();
            bridge?.ObserveFormationActivationTransitions(
                team,
                "resolve-authorized-formations",
                logTransitions: true);

            RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(
                BattleSnapshotRuntimeState.GetState(),
                team.Side);
            if (commanderEntry != null &&
                !string.IsNullOrWhiteSpace(commanderEntry.EntryId) &&
                string.Equals(commanderEntry.EntryId, entryId, StringComparison.Ordinal))
            {
                authorityRole = "campaign-commander";
                foreach (Formation formation in team.FormationsIncludingEmpty)
                {
                    if (formation == null ||
                        !ReferenceEquals(formation.Team, team) ||
                        formation.Index < 0 ||
                        formation.Index >= (int)FormationClass.NumberOfRegularFormations ||
                        assignments.ContainsKey(formation.Index))
                    {
                        continue;
                    }

                    formationIndices.Add(formation.Index);
                }
            }
            else
            {
                foreach (KeyValuePair<int, string> assignment in assignments)
                {
                    if (string.Equals(assignment.Value, entryId, StringComparison.Ordinal))
                        formationIndices.Add(assignment.Key);
                }

                if (formationIndices.Count > 0)
                    authorityRole = "delegated-captain";
            }

            formationIndices.Sort();
            return true;
        }

        internal static bool TryResolveExactBattleOrderAuthority(
            NetworkCommunicator peer,
            out Team team,
            out OrderController orderController,
            out Agent controlledAgent,
            out List<int> authorizedFormationIndices,
            out string authorityRole)
        {
            team = null;
            orderController = null;
            controlledAgent = null;
            authorizedFormationIndices = new List<int>();
            authorityRole = "none";
            if (!GameNetwork.IsServer || peer == null)
                return false;

            Mission mission = Mission.Current;
            CoopBattlePhase phase = CoopBattlePhaseRuntimeState.GetPhase();
            if (mission == null ||
                (phase != CoopBattlePhase.PreBattleHold && phase != CoopBattlePhase.BattleActive) ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) ||
                !SceneRuntimeClassifier.IsExactCampaignBattleScene(mission.SceneName ?? string.Empty))
            {
                return false;
            }

            MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
            controlledAgent = missionPeer?.ControlledAgent;
            team = missionPeer?.Team ?? controlledAgent?.Team;
            if (controlledAgent == null ||
                !controlledAgent.IsActive() ||
                controlledAgent.IsMount ||
                team == null ||
                !ReferenceEquals(controlledAgent.Team, team))
            {
                return false;
            }

            if (!CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(controlledAgent, out string entryId) ||
                string.IsNullOrWhiteSpace(entryId))
            {
                authorityRole = "unresolved-entry";
                orderController = team.GetOrderControllerOf(controlledAgent) ?? team.PlayerOrderController;
                if (orderController != null)
                    orderController.Owner = controlledAgent;
                return true;
            }

            TryResolveAuthorizedFormationIndices(
                mission,
                team,
                entryId,
                out authorizedFormationIndices,
                out authorityRole);
            orderController = team.GetOrderControllerOf(controlledAgent) ?? team.PlayerOrderController;
            if (orderController != null)
                orderController.Owner = controlledAgent;
            return true;
        }

        internal static void ReapplyDelegatedFormationOwnership(
            Mission mission,
            Team team,
            string source)
        {
            if (mission == null || team == null)
                return;

            CoopMissionNetworkBridge bridge = mission.GetMissionBehavior<CoopMissionNetworkBridge>();
            bridge?.ReapplyDelegatedFormationOwnership(team, source);
        }

        internal static void UpdateVoluntaryFormationAiControl(
            Mission mission,
            Team team,
            IEnumerable<Formation> formations,
            bool isAiControlled,
            string source)
        {
            if (mission == null || team == null || formations == null)
                return;

            CoopMissionNetworkBridge bridge = mission.GetMissionBehavior<CoopMissionNetworkBridge>();
            bridge?.UpdateVoluntaryFormationAiControl(team, formations, isAiControlled, source);
        }

        private bool TryDecodeDelegatedCaptainAssignments(
            Mission mission,
            Team team,
            byte[] payload,
            out Dictionary<int, string> assignments,
            out string error,
            bool requireMaterializedCaptainAgent = true)
        {
            assignments = new Dictionary<int, string>();
            error = string.Empty;
            if (payload == null || payload.Length <= 0)
                return true;

            int offset = 0;
            int recordCount = payload[offset++];
            RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(
                BattleSnapshotRuntimeState.GetState(),
                team.Side);

            for (int recordIndex = 0; recordIndex < recordCount; recordIndex++)
            {
                if (offset + 3 > payload.Length)
                {
                    error = "truncated-header Record=" + recordIndex;
                    return false;
                }

                int formationIndex = payload[offset++];
                int entryIdLength = payload[offset++] | (payload[offset++] << 8);
                if (entryIdLength <= 0 || offset + entryIdLength > payload.Length)
                {
                    error = "invalid-entry-length Record=" + recordIndex + " Length=" + entryIdLength;
                    return false;
                }

                string entryId = Encoding.UTF8.GetString(payload, offset, entryIdLength).Trim();
                offset += entryIdLength;
                if (formationIndex < 0 ||
                    formationIndex >= (int)FormationClass.NumberOfRegularFormations ||
                    string.IsNullOrWhiteSpace(entryId) ||
                    assignments.ContainsKey(formationIndex))
                {
                    error = "invalid-record Record=" + recordIndex + " Formation=" + formationIndex;
                    return false;
                }

                RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
                if (entryState?.IsHero != true ||
                    string.Equals(commanderEntry?.EntryId, entryId, StringComparison.Ordinal) ||
                    (requireMaterializedCaptainAgent && ResolveDelegatedCaptainAgent(mission, team, entryId) == null))
                {
                    error = "invalid-companion Record=" + recordIndex + " EntryId=" + entryId;
                    return false;
                }

                assignments[formationIndex] = entryId;
            }

            if (offset != payload.Length)
            {
                error = "trailing-bytes Bytes=" + (payload.Length - offset);
                return false;
            }

            return true;
        }

        private void ApplyDelegatedCaptainAssignmentSnapshot(
            Mission mission,
            Team team,
            Dictionary<int, string> assignments)
        {
            if (mission == null || team == null || assignments == null)
                return;

            _delegatedCaptainEntryIdByFormationIndexAndSide.TryGetValue(
                team.Side,
                out Dictionary<int, string> previousAssignments);

            if (previousAssignments != null)
            {
                foreach (KeyValuePair<int, string> previous in previousAssignments)
                {
                    if (assignments.ContainsKey(previous.Key))
                        continue;

                    Formation previousFormation = ResolveCommanderDeploymentFormation(team, previous.Key);
                    Agent previousCaptain = ResolveDelegatedCaptainAgent(mission, team, previous.Value);
                    if (previousFormation != null &&
                        previousCaptain != null &&
                        ReferenceEquals(previousFormation.Captain, previousCaptain))
                    {
                        previousFormation.Captain = null;
                    }
                }
            }

            var storedAssignments = new Dictionary<int, string>(assignments);
            _delegatedCaptainEntryIdByFormationIndexAndSide[team.Side] = storedAssignments;
            if (_voluntarilyAiControlledFormationIndicesBySide.TryGetValue(
                    team.Side,
                    out HashSet<int> voluntarilyAiControlledFormationIndices))
            {
                voluntarilyAiControlledFormationIndices.RemoveWhere(storedAssignments.ContainsKey);
            }
            ObserveFormationActivationTransitions(
                team,
                "captain-assignment-baseline",
                logTransitions: false);
            foreach (KeyValuePair<int, string> assignment in storedAssignments)
            {
                Formation formation = ResolveCommanderDeploymentFormation(team, assignment.Key);
                Agent captainAgent = ResolveDelegatedCaptainAgent(mission, team, assignment.Value);
                if (formation == null || captainAgent == null)
                    continue;

                formation.Captain = captainAgent;
                captainAgent.SetCanLeadFormationsRemotely(value: true);
            }

            TryBroadcastDelegatedCaptainAssignmentState(team.Side, force: false);
        }

        private static byte[] EncodeDelegatedCaptainAssignments(Dictionary<int, string> assignments)
        {
            var records = assignments == null
                ? new List<KeyValuePair<int, string>>()
                : assignments
                    .Where(assignment =>
                        assignment.Key >= 0 &&
                        assignment.Key < (int)FormationClass.NumberOfRegularFormations &&
                        !string.IsNullOrWhiteSpace(assignment.Value))
                    .OrderBy(assignment => assignment.Key)
                    .ToList();
            var encodedRecords = new List<byte>();
            int encodedRecordCount = 0;
            foreach (KeyValuePair<int, string> record in records)
            {
                byte[] entryIdBytes = Encoding.UTF8.GetBytes(record.Value.Trim());
                if (entryIdBytes.Length <= 0 || entryIdBytes.Length > ushort.MaxValue)
                    continue;

                if (1 + encodedRecords.Count + 3 + entryIdBytes.Length >
                    CoopCommanderDeploymentFormationAssignmentsMessage.MaxCaptainAssignmentBytes)
                {
                    break;
                }

                encodedRecords.Add((byte)record.Key);
                encodedRecords.Add((byte)(entryIdBytes.Length & 0xFF));
                encodedRecords.Add((byte)((entryIdBytes.Length >> 8) & 0xFF));
                encodedRecords.AddRange(entryIdBytes);
                encodedRecordCount++;
            }

            var bytes = new List<byte>(1 + encodedRecords.Count)
            {
                (byte)Math.Min(byte.MaxValue, encodedRecordCount)
            };
            bytes.AddRange(encodedRecords);
            return bytes.ToArray();
        }

        private void TryBroadcastDelegatedCaptainAssignmentState(BattleSideEnum side, bool force)
        {
            if (!GameNetwork.IsServer || side == BattleSideEnum.None || GameNetwork.NetworkPeers == null)
                return;

            _delegatedCaptainEntryIdByFormationIndexAndSide.TryGetValue(
                side,
                out Dictionary<int, string> assignments);
            byte[] payload = EncodeDelegatedCaptainAssignments(assignments);
            string assignmentKey = Convert.ToBase64String(payload);
            if (!force &&
                _lastBroadcastDelegatedCaptainAssignmentKeyBySide.TryGetValue(side, out string previousKey) &&
                string.Equals(previousKey, assignmentKey, StringComparison.Ordinal))
            {
                return;
            }

            int sentCount = 0;
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (!IsEligibleRemotePeer(peer))
                    continue;

                if (TrySendDelegatedCaptainAssignmentStateToPeer(peer, side, payload))
                    sentCount++;
            }

            _lastBroadcastDelegatedCaptainAssignmentKeyBySide[side] = assignmentKey;
            if (CoopDebugConfig.OrderOfBattleDiagnostics)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: broadcast delegated captain assignment state. " +
                    "Side=" + side +
                    " Assignments=" + (assignments?.Count ?? 0) +
                    " Peers=" + sentCount);
            }
        }

        private bool TrySendDelegatedCaptainAssignmentStateToPeer(
            NetworkCommunicator peer,
            BattleSideEnum side,
            byte[] payload = null)
        {
            if (!GameNetwork.IsServer || !IsEligibleRemotePeer(peer) || side == BattleSideEnum.None)
                return false;

            if (payload == null)
            {
                _delegatedCaptainEntryIdByFormationIndexAndSide.TryGetValue(
                    side,
                    out Dictionary<int, string> assignments);
                payload = EncodeDelegatedCaptainAssignments(assignments);
            }

            try
            {
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(new CoopDelegatedCaptainAssignmentsStateMessage(side, payload));
                GameNetwork.EndModuleEventAsServer();
                return true;
            }
            catch (Exception ex)
            {
                if (CoopDebugConfig.OrderOfBattleDiagnostics)
                {
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: delegated captain assignment state send failed. " +
                        "Peer=" + (peer?.UserName ?? "null") +
                        " Side=" + side +
                        " Error=" + ex.GetType().Name + ":" + ex.Message);
                }
                return false;
            }
        }

        private void ReapplyDelegatedFormationOwnership(Team team, string source)
        {
            Mission mission = this.Mission ?? Mission.Current;
            if (mission == null || team == null)
                return;

            if (!_delegatedCaptainEntryIdByFormationIndexAndSide.TryGetValue(
                    team.Side,
                    out Dictionary<int, string> assignments))
            {
                assignments = new Dictionary<int, string>();
            }

            var assignedFormationIndices = new HashSet<int>(assignments.Keys);
            if (!_voluntarilyAiControlledFormationIndicesBySide.TryGetValue(
                    team.Side,
                    out HashSet<int> voluntarilyAiControlledFormationIndices))
            {
                voluntarilyAiControlledFormationIndices = new HashSet<int>();
            }

            foreach (KeyValuePair<int, string> assignment in assignments)
            {
                Formation formation = ResolveCommanderDeploymentFormation(team, assignment.Key);
                Agent captainAgent = ResolveDelegatedCaptainAgent(mission, team, assignment.Value);
                if (formation == null || captainAgent == null)
                    continue;

                formation.Captain = captainAgent;
                captainAgent.SetCanLeadFormationsRemotely(value: true);
                MissionPeer captainPeer = captainAgent.MissionPeer;
                if (captainPeer?.ControlledAgent != null &&
                    ReferenceEquals(captainPeer.ControlledAgent, captainAgent) &&
                    captainAgent.IsActive())
                {
                    OrderController captainOrderController = team.GetOrderControllerOf(captainAgent);
                    bool requiresNativeSergeantAssignment =
                        !ReferenceEquals(formation.PlayerOwner, captainAgent) ||
                        captainOrderController == null ||
                        !ReferenceEquals(captainOrderController.Owner, captainAgent) ||
                        captainPeer.ControlledFormation == null;
                    if (requiresNativeSergeantAssignment)
                        team.AssignPlayerAsSergeantOfFormation(captainPeer, formation.FormationIndex);

                    formation.PlayerOwner = captainAgent;
                    if (voluntarilyAiControlledFormationIndices.Contains(formation.Index))
                        EnsureDelegatedFormationAiControl(formation);
                    else
                        formation.SetControlledByAI(false, false);
                    continue;
                }

                formation.PlayerOwner = null;
                EnsureDelegatedFormationAiControl(formation);
            }

            Agent campaignCommanderAgent = team.GeneralAgent;
            bool campaignCommanderIsPlayerControlled =
                campaignCommanderAgent != null &&
                campaignCommanderAgent.IsActive() &&
                campaignCommanderAgent.MissionPeer?.ControlledAgent != null &&
                ReferenceEquals(campaignCommanderAgent.MissionPeer.ControlledAgent, campaignCommanderAgent);
            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null ||
                    !ReferenceEquals(formation.Team, team) ||
                    assignedFormationIndices.Contains(formation.Index))
                {
                    continue;
                }

                if (campaignCommanderIsPlayerControlled)
                {
                    if (!ReferenceEquals(formation.PlayerOwner, campaignCommanderAgent))
                        formation.PlayerOwner = campaignCommanderAgent;

                    if (voluntarilyAiControlledFormationIndices.Contains(formation.Index))
                        EnsureDelegatedFormationAiControl(formation);
                    else
                        formation.SetControlledByAI(false, false);
                }
                else
                {
                    formation.PlayerOwner = null;
                    formation.SetControlledByAI(true, true);
                }
            }

            ObserveFormationActivationTransitions(
                team,
                source ?? "reapply-formation-ownership",
                logTransitions: true);

            if (CoopDebugConfig.OrderOfBattleDiagnostics)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: reapplied delegated formation ownership. " +
                    "Side=" + team.Side +
                    " Assignments=" + assignments.Count +
                    " VoluntaryAiFormations=" + voluntarilyAiControlledFormationIndices.Count +
                    " CampaignCommanderPlayerControlled=" + campaignCommanderIsPlayerControlled +
                    " Source=" + (source ?? "unknown"));
            }
        }

        private void UpdateVoluntaryFormationAiControl(
            Team team,
            IEnumerable<Formation> formations,
            bool isAiControlled,
            string source)
        {
            if (team == null || formations == null)
                return;

            if (!_voluntarilyAiControlledFormationIndicesBySide.TryGetValue(
                    team.Side,
                    out HashSet<int> voluntarilyAiControlledFormationIndices))
            {
                voluntarilyAiControlledFormationIndices = new HashSet<int>();
                _voluntarilyAiControlledFormationIndicesBySide[team.Side] =
                    voluntarilyAiControlledFormationIndices;
            }

            int changedCount = 0;
            foreach (Formation formation in formations)
            {
                if (formation == null ||
                    !ReferenceEquals(formation.Team, team) ||
                    formation.Index < 0 ||
                    formation.Index >= (int)FormationClass.NumberOfRegularFormations)
                {
                    continue;
                }

                bool changed = isAiControlled
                    ? voluntarilyAiControlledFormationIndices.Add(formation.Index)
                    : voluntarilyAiControlledFormationIndices.Remove(formation.Index);
                if (changed)
                    changedCount++;
            }

            ReapplyDelegatedFormationOwnership(team, source ?? "voluntary-ai-control-updated");

            if (CoopDebugConfig.OrderOfBattleDiagnostics)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: updated voluntary formation AI control. " +
                    "Side=" + team.Side +
                    " IsAiControlled=" + isAiControlled +
                    " Changed=" + changedCount +
                    " Stored=" + voluntarilyAiControlledFormationIndices.Count +
                    " Source=" + (source ?? "unknown"));
            }
        }

        private static void EnsureDelegatedFormationAiControl(Formation formation)
        {
            if (formation == null)
                return;

            if (formation.IsAIControlled &&
                FormationEnforceNotSplittableByAiField?.GetValue(formation) is bool enforceNotSplittableByAi &&
                enforceNotSplittableByAi)
            {
                return;
            }

            // SetControlledByAI returns early when the requested value already matches.
            // Toggle once so enforceNotSplittableByAI is actually applied.
            if (formation.IsAIControlled)
                formation.SetControlledByAI(false, false);
            formation.SetControlledByAI(true, true);
        }

        private void ObserveFormationActivationTransitions(
            Team team,
            string source,
            bool logTransitions)
        {
            if (!CoopDebugConfig.OrderOfBattleDiagnostics ||
                team?.FormationsIncludingEmpty == null)
            {
                return;
            }

            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null || !ReferenceEquals(formation.Team, team))
                    continue;

                string key = team.TeamIndex + ":" + formation.Index;
                int currentCount = Math.Max(0, formation.CountOfUnits);
                bool hadPreviousCount = _lastObservedFormationUnitCountByTeamAndIndex.TryGetValue(
                    key,
                    out int previousCount);
                _lastObservedFormationUnitCountByTeamAndIndex[key] = currentCount;
                if (!logTransitions || !hadPreviousCount || previousCount != 0 || currentCount <= 0)
                    continue;

                ModLogger.Info(
                    "CoopMissionNetworkBridge: formation became active after deployment baseline. " +
                    "TeamIndex=" + team.TeamIndex +
                    " Side=" + team.Side +
                    " FormationIndex=" + formation.Index +
                    " Count=" + currentCount +
                    " Class=" + formation.FormationIndex +
                    " PlayerOwner=" + (formation.PlayerOwner?.Index.ToString() ?? "null") +
                    " IsAIControlled=" + formation.IsAIControlled +
                    " Captain=" + (formation.Captain?.Index.ToString() ?? "null") +
                    " Source=" + (source ?? "unknown"));
            }
        }

        private static Agent ResolveDelegatedCaptainAgent(Mission mission, Team team, string entryId)
        {
            if (mission?.AllAgents == null || team == null || string.IsNullOrWhiteSpace(entryId))
                return null;

            RosterEntryState expectedEntryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (expectedEntryState?.IsHero != true)
                return null;

            for (int i = 0; i < mission.AllAgents.Count; i++)
            {
                Agent candidate = mission.AllAgents[i];
                if (candidate == null ||
                    !candidate.IsActive() ||
                    candidate.IsMount ||
                    !ReferenceEquals(candidate.Team, team) ||
                    !CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(candidate, out string candidateEntryId) ||
                    !string.Equals(candidateEntryId, entryId, StringComparison.Ordinal))
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private static bool IsCommanderDeploymentCompositionAssignmentPayload(byte[] assignmentBytes)
        {
            return assignmentBytes != null &&
                   assignmentBytes.Length >= CoopCommanderDeploymentFormationAssignmentsMessage.CompositionAssignmentHeaderBytes &&
                   assignmentBytes[0] == CoopCommanderDeploymentFormationAssignmentsMessage.CompositionAssignmentPayloadMarker;
        }

        private static bool TryDecodeCommanderDeploymentFormationCompositionPayload(
            byte[] assignmentBytes,
            out List<CommanderDeploymentFormationComposition> records,
            out bool preserveMountedClasses,
            out string error)
        {
            records = new List<CommanderDeploymentFormationComposition>();
            preserveMountedClasses = false;
            error = string.Empty;

            if (!IsCommanderDeploymentCompositionAssignmentPayload(assignmentBytes))
            {
                error = "missing-marker";
                return false;
            }

            byte payloadVersion = assignmentBytes[1];
            if (payloadVersion != CoopCommanderDeploymentFormationAssignmentsMessage.CompositionAssignmentPayloadVersion1 &&
                payloadVersion != CoopCommanderDeploymentFormationAssignmentsMessage.CompositionAssignmentPayloadVersion &&
                payloadVersion != CoopCommanderDeploymentFormationAssignmentsMessage.MountedCompositionAssignmentPayloadVersion)
            {
                error = "unsupported-version:" + payloadVersion;
                return false;
            }

            int recordCount = assignmentBytes[2];
            preserveMountedClasses =
                payloadVersion == CoopCommanderDeploymentFormationAssignmentsMessage.MountedCompositionAssignmentPayloadVersion;
            int bytesPerRecord;
            if (payloadVersion == CoopCommanderDeploymentFormationAssignmentsMessage.CompositionAssignmentPayloadVersion1)
            {
                bytesPerRecord =
                    CoopCommanderDeploymentFormationAssignmentsMessage.BytesPerCompositionAssignmentVersion1;
            }
            else if (preserveMountedClasses)
            {
                bytesPerRecord =
                    CoopCommanderDeploymentFormationAssignmentsMessage.BytesPerMountedCompositionAssignment;
            }
            else
            {
                bytesPerRecord =
                    CoopCommanderDeploymentFormationAssignmentsMessage.BytesPerCompositionAssignment;
            }
            int expectedLength =
                CoopCommanderDeploymentFormationAssignmentsMessage.CompositionAssignmentHeaderBytes +
                recordCount * bytesPerRecord;
            if (recordCount <= 0 || assignmentBytes.Length != expectedLength)
            {
                error = "invalid-length:" + assignmentBytes.Length + "/" + expectedLength;
                return false;
            }

            int offset = CoopCommanderDeploymentFormationAssignmentsMessage.CompositionAssignmentHeaderBytes;
            for (int i = 0; i < recordCount; i++)
            {
                int formationIndex = assignmentBytes[offset++];
                int infantryCount = ReadUInt16FromPayload(assignmentBytes, ref offset);
                int rangedCount = ReadUInt16FromPayload(assignmentBytes, ref offset);
                int cavalryCount = preserveMountedClasses
                    ? ReadUInt16FromPayload(assignmentBytes, ref offset)
                    : 0;
                int horseArcherCount = preserveMountedClasses
                    ? ReadUInt16FromPayload(assignmentBytes, ref offset)
                    : 0;
                TroopTraitsMask infantryFilter = TroopTraitsMask.Melee;
                TroopTraitsMask rangedFilter = TroopTraitsMask.Ranged;
                TroopTraitsMask cavalryFilter = TroopTraitsMask.Melee | TroopTraitsMask.Mount;
                TroopTraitsMask horseArcherFilter = TroopTraitsMask.Ranged | TroopTraitsMask.Mount;
                if (payloadVersion >= CoopCommanderDeploymentFormationAssignmentsMessage.CompositionAssignmentPayloadVersion)
                {
                    infantryFilter = (TroopTraitsMask)ReadUInt16FromPayload(assignmentBytes, ref offset);
                    rangedFilter = (TroopTraitsMask)ReadUInt16FromPayload(assignmentBytes, ref offset);
                    if (preserveMountedClasses)
                    {
                        cavalryFilter = (TroopTraitsMask)ReadUInt16FromPayload(assignmentBytes, ref offset);
                        horseArcherFilter = (TroopTraitsMask)ReadUInt16FromPayload(assignmentBytes, ref offset);
                    }
                }

                if (formationIndex < 0 ||
                    formationIndex >= (int)FormationClass.NumberOfRegularFormations)
                {
                    continue;
                }

                records.Add(new CommanderDeploymentFormationComposition(
                    formationIndex,
                    infantryCount,
                    rangedCount,
                    cavalryCount,
                    horseArcherCount,
                    SanitizeCommanderDeploymentCompositionFilter(infantryFilter, FormationClass.Infantry),
                    SanitizeCommanderDeploymentCompositionFilter(rangedFilter, FormationClass.Ranged),
                    SanitizeCommanderDeploymentCompositionFilter(cavalryFilter, FormationClass.Cavalry),
                    SanitizeCommanderDeploymentCompositionFilter(horseArcherFilter, FormationClass.HorseArcher)));
            }

            if (records.Count <= 0)
            {
                error = "no-valid-records";
                return false;
            }

            return true;
        }

        private bool TryApplyCommanderDeploymentFormationCompositionAssignments(
            NetworkCommunicator peer,
            CoopCommanderDeploymentFormationAssignmentsMessage message,
            Mission mission,
            Team team,
            OrderController orderController,
            Agent commanderAgent,
            Dictionary<int, CommanderDeploymentFormationLayout> formationLayouts,
            HashSet<Formation> layoutFormations,
            List<CommanderDeploymentFormationComposition> compositionRecords,
            bool preserveMountedClasses)
        {
            if (mission == null ||
                team == null ||
                compositionRecords == null ||
                compositionRecords.Count <= 0)
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            bool scenarioPreservesMountedClasses =
                ExactCampaignCommanderDeploymentRuntime
                    .IsExactLandBattleScenario(mission, scenarioContext);
            if (preserveMountedClasses != scenarioPreservesMountedClasses)
            {
                LogCommanderDeploymentAssignmentDiagnostics(
                    "skip-composition-policy-mismatch",
                    peer,
                    message,
                    team,
                    compositionRecords.Count,
                    0,
                    compositionRecords.Count,
                    formationLayouts?.Count ?? 0,
                    "PayloadPreservesMounted=" + preserveMountedClasses +
                    " ScenarioPreservesMounted=" + scenarioPreservesMountedClasses);
                return false;
            }

            var infantryDesiredCounts = new Dictionary<Formation, int>();
            var rangedDesiredCounts = new Dictionary<Formation, int>();
            var cavalryDesiredCounts = new Dictionary<Formation, int>();
            var horseArcherDesiredCounts = new Dictionary<Formation, int>();
            var infantryFilters = new Dictionary<Formation, TroopTraitsMask>();
            var rangedFilters = new Dictionary<Formation, TroopTraitsMask>();
            var cavalryFilters = new Dictionary<Formation, TroopTraitsMask>();
            var horseArcherFilters = new Dictionary<Formation, TroopTraitsMask>();
            var compositionFormations = new HashSet<Formation>();
            int decodedRecords = 0;
            int rejectedRecords = 0;

            foreach (CommanderDeploymentFormationComposition record in compositionRecords)
            {
                Formation formation = ResolveCommanderDeploymentFormation(team, record.FormationIndex);
                if (formation == null)
                {
                    rejectedRecords++;
                    continue;
                }

                compositionFormations.Add(formation);
                infantryDesiredCounts[formation] = Math.Max(0, record.InfantryCount);
                rangedDesiredCounts[formation] = Math.Max(0, record.RangedCount);
                if (preserveMountedClasses)
                {
                    cavalryDesiredCounts[formation] = Math.Max(0, record.CavalryCount);
                    horseArcherDesiredCounts[formation] = Math.Max(0, record.HorseArcherCount);
                }
                infantryFilters[formation] = SanitizeCommanderDeploymentCompositionFilter(
                    record.InfantryFilter,
                    FormationClass.Infantry);
                rangedFilters[formation] = SanitizeCommanderDeploymentCompositionFilter(
                    record.RangedFilter,
                    FormationClass.Ranged);
                if (preserveMountedClasses)
                {
                    cavalryFilters[formation] = SanitizeCommanderDeploymentCompositionFilter(
                        record.CavalryFilter,
                        FormationClass.Cavalry);
                    horseArcherFilters[formation] = SanitizeCommanderDeploymentCompositionFilter(
                        record.HorseArcherFilter,
                        FormationClass.HorseArcher);
                }
                decodedRecords++;
            }

            if (decodedRecords <= 0)
            {
                LogCommanderDeploymentAssignmentWarningIfSuspicious(
                    "composition-no-valid-records",
                    peer,
                    message,
                    team,
                    compositionRecords.Count,
                    0,
                    rejectedRecords,
                    formationLayouts?.Count ?? 0,
                    "Before=[" + BuildCommanderDeploymentFormationSummary(team) + "]");
                return false;
            }

            List<Agent> infantryAgents = CollectCommanderDeploymentCompositionAgents(
                team,
                FormationClass.Infantry,
                preserveMountedClasses);
            List<Agent> rangedAgents = CollectCommanderDeploymentCompositionAgents(
                team,
                FormationClass.Ranged,
                preserveMountedClasses);
            List<Agent> cavalryAgents = preserveMountedClasses
                ? CollectCommanderDeploymentCompositionAgents(
                    team,
                    FormationClass.Cavalry,
                    preserveMountedClasses: true)
                : new List<Agent>();
            List<Agent> horseArcherAgents = preserveMountedClasses
                ? CollectCommanderDeploymentCompositionAgents(
                    team,
                    FormationClass.HorseArcher,
                    preserveMountedClasses: true)
                : new List<Agent>();
            NormalizeCommanderDeploymentDesiredCounts(infantryDesiredCounts, infantryAgents.Count);
            NormalizeCommanderDeploymentDesiredCounts(rangedDesiredCounts, rangedAgents.Count);
            if (preserveMountedClasses)
            {
                NormalizeCommanderDeploymentDesiredCounts(cavalryDesiredCounts, cavalryAgents.Count);
                NormalizeCommanderDeploymentDesiredCounts(horseArcherDesiredCounts, horseArcherAgents.Count);
            }

            var targetByAgent = new Dictionary<Agent, Formation>();
            int assignedInfantry = BuildCommanderDeploymentCompositionTargets(
                infantryDesiredCounts,
                infantryFilters,
                infantryAgents,
                targetByAgent);
            int assignedRanged = BuildCommanderDeploymentCompositionTargets(
                rangedDesiredCounts,
                rangedFilters,
                rangedAgents,
                targetByAgent);
            int assignedCavalry = preserveMountedClasses
                ? BuildCommanderDeploymentCompositionTargets(
                    cavalryDesiredCounts,
                    cavalryFilters,
                    cavalryAgents,
                    targetByAgent)
                : 0;
            int assignedHorseArchers = preserveMountedClasses
                ? BuildCommanderDeploymentCompositionTargets(
                    horseArcherDesiredCounts,
                    horseArcherFilters,
                    horseArcherAgents,
                    targetByAgent)
                : 0;

            var moves = new List<(Agent Agent, Formation TargetFormation)>();
            var impactedFormations = new HashSet<Formation>();
            foreach (KeyValuePair<Agent, Formation> assignment in targetByAgent)
            {
                Agent agent = assignment.Key;
                Formation targetFormation = assignment.Value;
                if (agent == null || targetFormation == null || ReferenceEquals(agent.Formation, targetFormation))
                    continue;

                if (agent.Formation != null)
                    impactedFormations.Add(agent.Formation);
                impactedFormations.Add(targetFormation);
                moves.Add((agent, targetFormation));
            }

            if (targetByAgent.Count <= 0)
            {
                LogCommanderDeploymentAssignmentWarningIfSuspicious(
                    "composition-no-assigned-agents",
                    peer,
                    message,
                    team,
                    decodedRecords,
                    0,
                    rejectedRecords,
                    formationLayouts?.Count ?? 0,
                    "InfantryAgents=" + infantryAgents.Count +
                    " RangedAgents=" + rangedAgents.Count +
                    " CavalryAgents=" + cavalryAgents.Count +
                    " HorseArcherAgents=" + horseArcherAgents.Count +
                    " Before=[" + BuildCommanderDeploymentFormationSummary(team) + "]");
            }

            bool previousTeleportingAgents = mission.IsTeleportingAgents;
            try
            {
                mission.IsTeleportingAgents = true;

                foreach (Formation formation in impactedFormations)
                    TryStartCommanderDeploymentMassTransfer(formation);

                foreach ((Agent agent, Formation targetFormation) in moves)
                    agent.Formation = targetFormation;

                var formationsToFinalize = new HashSet<Formation>(compositionFormations);
                foreach (Formation formation in layoutFormations ?? new HashSet<Formation>())
                    formationsToFinalize.Add(formation);
                foreach (Formation formation in impactedFormations)
                    formationsToFinalize.Add(formation);

                foreach (Formation formation in formationsToFinalize)
                {
                    if (formation == null)
                        continue;

                    CommanderDeploymentFormationLayout layout = default(CommanderDeploymentFormationLayout);
                    if (formationLayouts != null)
                        formationLayouts.TryGetValue(formation.Index, out layout);
                    FinalizeCommanderDeploymentFormationAssignment(
                        mission,
                        team,
                        formation,
                        commanderAgent,
                        impactedFormations.Contains(formation),
                        layout);
                }

                CommanderDeploymentMissionNetworkComponentPatch.TryRefreshCommanderDeploymentSelection(
                    peer,
                    team,
                    orderController);
            }
            finally
            {
                mission.IsTeleportingAgents = previousTeleportingAgents;
            }

            if (IsCommanderDeploymentOrderOfBattleDiagnosticsEnabled())
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: applied commander deployment formation composition sync. " +
                    "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "<null>") +
                    " Side=" + team.Side +
                    " Records=" + decodedRecords +
                    " AssignedInfantry=" + assignedInfantry +
                    " AssignedRanged=" + assignedRanged +
                    " AssignedCavalry=" + assignedCavalry +
                    " AssignedHorseArchers=" + assignedHorseArchers +
                    " PreserveMountedClasses=" + preserveMountedClasses +
                    " AppliedMoves=" + moves.Count +
                    " Layouts=" + (formationLayouts?.Count ?? 0) +
                    " RejectedRecords=" + rejectedRecords +
                    " Formations=[" + BuildCommanderDeploymentFormationSummary(team) + "]");
            }

            return targetByAgent.Count > 0 || (layoutFormations != null && layoutFormations.Count > 0);
        }

        private static int BuildCommanderDeploymentCompositionTargets(
            Dictionary<Formation, int> desiredCounts,
            Dictionary<Formation, TroopTraitsMask> filters,
            List<Agent> assignableAgents,
            Dictionary<Agent, Formation> targetByAgent)
        {
            if (desiredCounts == null ||
                assignableAgents == null ||
                targetByAgent == null ||
                desiredCounts.Count <= 0 ||
                assignableAgents.Count <= 0)
            {
                return 0;
            }

            var assignedAgents = new HashSet<Agent>();
            var assignedCounts = new Dictionary<Formation, int>();
            List<Formation> orderedFormations = GetCommanderDeploymentDesiredFormationsByPriority(desiredCounts, filters);

            foreach (Formation targetFormation in orderedFormations)
            {
                int desiredCount = Math.Max(0, desiredCounts[targetFormation]);
                if (desiredCount <= 0)
                    continue;

                TroopTraitsMask filter = GetCommanderDeploymentCompositionFilter(filters, targetFormation);
                List<Agent> orderedAgents = GetCommanderDeploymentAgentsByPriority(
                    assignableAgents,
                    assignedAgents,
                    targetByAgent,
                    targetFormation,
                    filter);
                foreach (Agent agent in orderedAgents)
                {
                    if (agent == null ||
                        assignedAgents.Contains(agent) ||
                        targetByAgent.ContainsKey(agent))
                    {
                        continue;
                    }

                    int assignedCount = GetCommanderDeploymentAssignedCount(assignedCounts, targetFormation);
                    if (assignedCount >= desiredCount)
                        break;

                    targetByAgent[agent] = targetFormation;
                    assignedAgents.Add(agent);
                    assignedCounts[targetFormation] = assignedCount + 1;
                }
            }

            foreach (Formation targetFormation in orderedFormations)
            {
                int desiredCount = Math.Max(0, desiredCounts[targetFormation]);
                if (desiredCount <= 0)
                    continue;

                foreach (Agent agent in assignableAgents)
                {
                    if (agent == null ||
                        assignedAgents.Contains(agent) ||
                        targetByAgent.ContainsKey(agent))
                    {
                        continue;
                    }

                    int assignedCount = GetCommanderDeploymentAssignedCount(assignedCounts, targetFormation);
                    if (assignedCount >= desiredCount)
                        break;

                    targetByAgent[agent] = targetFormation;
                    assignedAgents.Add(agent);
                    assignedCounts[targetFormation] = assignedCount + 1;
                }
            }

            return assignedAgents.Count;
        }

        private static TroopTraitsMask SanitizeCommanderDeploymentCompositionFilter(
            TroopTraitsMask filter,
            FormationClass projectedClass)
        {
            const TroopTraitsMask formationClassTraits =
                TroopTraitsMask.Melee |
                TroopTraitsMask.Ranged |
                TroopTraitsMask.Mount;

            TroopTraitsMask safeFilter = filter & TroopTraitsMask.All;
            safeFilter &= ~formationClassTraits;
            bool isRanged = projectedClass == FormationClass.Ranged ||
                            projectedClass == FormationClass.HorseArcher;
            bool isMounted = projectedClass == FormationClass.Cavalry ||
                             projectedClass == FormationClass.HorseArcher;
            safeFilter |= isRanged
                ? TroopTraitsMask.Ranged
                : TroopTraitsMask.Melee;
            if (isMounted)
                safeFilter |= TroopTraitsMask.Mount;
            return safeFilter;
        }

        private static TroopTraitsMask GetCommanderDeploymentCompositionFilter(
            Dictionary<Formation, TroopTraitsMask> filters,
            Formation formation)
        {
            if (filters != null &&
                formation != null &&
                filters.TryGetValue(formation, out TroopTraitsMask filter))
            {
                return filter & TroopTraitsMask.All;
            }

            return TroopTraitsMask.Melee | TroopTraitsMask.Ranged;
        }

        private static List<Agent> GetCommanderDeploymentAgentsByPriority(
            List<Agent> assignableAgents,
            HashSet<Agent> assignedAgents,
            Dictionary<Agent, Formation> targetByAgent,
            Formation targetFormation,
            TroopTraitsMask filter)
        {
            var agents = new List<Agent>();
            if (assignableAgents == null)
                return agents;

            foreach (Agent agent in assignableAgents)
            {
                if (agent != null &&
                    (assignedAgents == null || !assignedAgents.Contains(agent)) &&
                    (targetByAgent == null || !targetByAgent.ContainsKey(agent)))
                {
                    agents.Add(agent);
                }
            }

            TroopFilteringUtilities.GetPriorityFunction(filter, out Func<Agent, int> priorityFunc);
            agents.Sort((left, right) =>
            {
                int priorityCompare = priorityFunc(right).CompareTo(priorityFunc(left));
                if (priorityCompare != 0)
                    return priorityCompare;

                bool leftAlreadyInTarget = ReferenceEquals(left?.Formation, targetFormation);
                bool rightAlreadyInTarget = ReferenceEquals(right?.Formation, targetFormation);
                if (leftAlreadyInTarget != rightAlreadyInTarget)
                    return leftAlreadyInTarget ? -1 : 1;

                int leftFormationIndex = left?.Formation?.Index ?? int.MaxValue;
                int rightFormationIndex = right?.Formation?.Index ?? int.MaxValue;
                int formationCompare = leftFormationIndex.CompareTo(rightFormationIndex);
                if (formationCompare != 0)
                    return formationCompare;

                return (left?.Index ?? int.MaxValue).CompareTo(right?.Index ?? int.MaxValue);
            });

            return agents;
        }

        private static List<Agent> CollectCommanderDeploymentCompositionAgents(
            Team team,
            FormationClass projectedClass,
            bool preserveMountedClasses)
        {
            var agents = new List<Agent>();
            Mission mission = Mission.Current;
            if (team == null || mission?.AllAgents == null)
                return agents;

            for (int i = 0; i < mission.AllAgents.Count; i++)
            {
                Agent agent = mission.AllAgents[i];
                if (agent == null ||
                    agent.IsMount ||
                    !agent.IsActive() ||
                    !ReferenceEquals(agent.Team, team) ||
                    agent.Formation == null ||
                    !ReferenceEquals(agent.Formation.Team, team) ||
                    ResolveCommanderDeploymentProjectedAgentClass(agent, preserveMountedClasses) != projectedClass)
                {
                    continue;
                }

                agents.Add(agent);
            }

            agents.Sort((left, right) =>
            {
                int leftFormationIndex = left?.Formation?.Index ?? int.MaxValue;
                int rightFormationIndex = right?.Formation?.Index ?? int.MaxValue;
                int formationCompare = leftFormationIndex.CompareTo(rightFormationIndex);
                if (formationCompare != 0)
                    return formationCompare;

                return (left?.Index ?? int.MaxValue).CompareTo(right?.Index ?? int.MaxValue);
            });
            return agents;
        }

        private static FormationClass ResolveCommanderDeploymentProjectedAgentClass(
            Agent agent,
            bool preserveMountedClasses)
        {
            if (agent == null || agent.IsMount)
                return FormationClass.NumberOfAllFormations;

            if (preserveMountedClasses)
            {
                if (agent.HasMount)
                {
                    return agent.IsRangedCached
                        ? FormationClass.HorseArcher
                        : FormationClass.Cavalry;
                }

                return agent.IsRangedCached
                    ? FormationClass.Ranged
                    : FormationClass.Infantry;
            }

            if (!agent.HasMount && agent.IsRangedCached)
                return FormationClass.Ranged;

            FormationClass formationClass = FormationClass.NumberOfAllFormations;
            BasicCharacterObject character = agent.Character;
            if (character != null)
            {
                try
                {
                    BattleSideEnum side = agent.Team?.Side ?? BattleSideEnum.None;
                    if (Mission.Current != null && side != BattleSideEnum.None)
                        formationClass = Mission.Current.GetAgentTroopClass(side, character);
                    else
                        formationClass = character.DefaultFormationClass;
                }
                catch
                {
                    formationClass = character.DefaultFormationClass;
                }
            }

            if (!IsCommanderDeploymentDefaultFormationClass(formationClass))
                return agent.IsRangedCached ? FormationClass.Ranged : FormationClass.Infantry;

            formationClass = DismountCommanderDeploymentSiegeFormationClass(formationClass.FallbackClass());
            if (formationClass == FormationClass.Ranged || formationClass == FormationClass.Infantry)
                return formationClass;

            return agent.IsRangedCached ? FormationClass.Ranged : FormationClass.Infantry;
        }

        private static FormationClass DismountCommanderDeploymentSiegeFormationClass(FormationClass formationClass)
        {
            if (formationClass == FormationClass.Cavalry)
                return FormationClass.Infantry;

            if (formationClass == FormationClass.HorseArcher)
                return FormationClass.Ranged;

            return formationClass;
        }

        private static bool IsCommanderDeploymentDefaultFormationClass(FormationClass formationClass)
        {
            return formationClass >= FormationClass.Infantry &&
                   formationClass < FormationClass.NumberOfDefaultFormations;
        }

        private static void NormalizeCommanderDeploymentDesiredCounts(
            Dictionary<Formation, int> desiredCounts,
            int availableUnits)
        {
            if (desiredCounts == null || desiredCounts.Count <= 0)
                return;

            availableUnits = Math.Max(0, availableUnits);
            var keys = new List<Formation>(desiredCounts.Keys);
            foreach (Formation formation in keys)
                desiredCounts[formation] = Math.Max(0, desiredCounts[formation]);

            int assignedUnits = 0;
            foreach (int count in desiredCounts.Values)
                assignedUnits += count;

            while (assignedUnits > availableUnits)
            {
                Formation formation = FindCommanderDeploymentDesiredCountExtremum(desiredCounts, findMaximum: true);
                if (formation == null || desiredCounts[formation] <= 0)
                    break;

                desiredCounts[formation]--;
                assignedUnits--;
            }

            while (assignedUnits < availableUnits)
            {
                Formation formation = FindCommanderDeploymentDesiredCountExtremum(desiredCounts, findMaximum: true);
                if (formation == null)
                    break;

                desiredCounts[formation]++;
                assignedUnits++;
            }
        }

        private static Formation FindCommanderDeploymentDesiredCountExtremum(
            Dictionary<Formation, int> desiredCounts,
            bool findMaximum)
        {
            Formation bestFormation = null;
            int bestCount = findMaximum ? int.MinValue : int.MaxValue;
            foreach (KeyValuePair<Formation, int> pair in desiredCounts)
            {
                Formation formation = pair.Key;
                if (formation == null)
                    continue;

                bool isBetter =
                    (findMaximum && pair.Value > bestCount) ||
                    (!findMaximum && pair.Value < bestCount) ||
                    (pair.Value == bestCount &&
                     bestFormation != null &&
                     formation.Index < bestFormation.Index);
                if (!isBetter && bestFormation != null)
                    continue;

                bestFormation = formation;
                bestCount = pair.Value;
            }

            return bestFormation;
        }

        private static List<Formation> GetCommanderDeploymentDesiredFormationsByIndex(
            Dictionary<Formation, int> desiredCounts)
        {
            var formations = new List<Formation>();
            if (desiredCounts == null)
                return formations;

            foreach (Formation formation in desiredCounts.Keys)
            {
                if (formation != null)
                    formations.Add(formation);
            }

            formations.Sort((left, right) => left.Index.CompareTo(right.Index));
            return formations;
        }

        private static List<Formation> GetCommanderDeploymentDesiredFormationsByPriority(
            Dictionary<Formation, int> desiredCounts,
            Dictionary<Formation, TroopTraitsMask> filters)
        {
            List<Formation> formations = GetCommanderDeploymentDesiredFormationsByIndex(desiredCounts);
            formations.Sort((left, right) =>
            {
                int leftPriority = TroopFilteringUtilities.GetMaxPriority(
                    GetCommanderDeploymentCompositionFilter(filters, left));
                int rightPriority = TroopFilteringUtilities.GetMaxPriority(
                    GetCommanderDeploymentCompositionFilter(filters, right));
                int priorityCompare = rightPriority.CompareTo(leftPriority);
                if (priorityCompare != 0)
                    return priorityCompare;

                int leftCount = desiredCounts != null && left != null && desiredCounts.TryGetValue(left, out int lc)
                    ? lc
                    : 0;
                int rightCount = desiredCounts != null && right != null && desiredCounts.TryGetValue(right, out int rc)
                    ? rc
                    : 0;
                int countCompare = leftCount.CompareTo(rightCount);
                if (countCompare != 0)
                    return countCompare;

                return (left?.Index ?? int.MaxValue).CompareTo(right?.Index ?? int.MaxValue);
            });
            return formations;
        }

        private static int GetCommanderDeploymentAssignedCount(
            Dictionary<Formation, int> assignedCounts,
            Formation formation)
        {
            if (assignedCounts == null || formation == null)
                return 0;

            return assignedCounts.TryGetValue(formation, out int count) ? count : 0;
        }

        private static int ReadUInt16FromPayload(byte[] payload, ref int offset)
        {
            int value = payload[offset] | (payload[offset + 1] << 8);
            offset += 2;
            return value;
        }

        private static void LogCommanderDeploymentAssignmentDiagnostics(
            string stage,
            NetworkCommunicator peer,
            CoopCommanderDeploymentFormationAssignmentsMessage message,
            Team team,
            int decodedAssignments,
            int appliedMoves,
            int rejectedAssignments,
            int layoutCount,
            string detail)
        {
            if (!IsCommanderDeploymentOrderOfBattleDiagnosticsEnabled())
                return;

            try
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: commander deployment assignment diagnostics. " +
                    "Stage=" + (stage ?? string.Empty) +
                    " Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "<null>") +
                    " RequestedSide=" + (message == null ? "<null>" : message.RequestedSide.ToString()) +
                    " Team=" + (team?.TeamIndex.ToString() ?? "<null>") +
                    " TeamSide=" + (team?.Side.ToString() ?? "<null>") +
                    " AssignmentBytes=" + (message?.AssignmentBytes?.Length ?? 0) +
                    " LayoutBytes=" + (message?.FormationLayoutBytes?.Length ?? 0) +
                    " Decoded=" + decodedAssignments +
                    " AppliedMoves=" + appliedMoves +
                    " Rejected=" + rejectedAssignments +
                    " Layouts=" + layoutCount +
                    " Detail=" + (detail ?? string.Empty));
            }
            catch
            {
            }
        }

        private static readonly Dictionary<string, int> CommanderDeploymentAssignmentWarningCounts =
            new Dictionary<string, int>();

        private static void LogCommanderDeploymentAssignmentWarningIfSuspicious(
            string stage,
            NetworkCommunicator peer,
            CoopCommanderDeploymentFormationAssignmentsMessage message,
            Team team,
            int decodedAssignments,
            int appliedMoves,
            int rejectedAssignments,
            int layoutCount,
            string detail)
        {
            if (IsCommanderDeploymentOrderOfBattleDiagnosticsEnabled() ||
                decodedAssignments <= 0 ||
                (rejectedAssignments < decodedAssignments && layoutCount <= 0))
            {
                return;
            }

            string key = (stage ?? string.Empty) + ":" + (peer == null ? -1 : peer.Index);
            lock (CommanderDeploymentAssignmentWarningCounts)
            {
                CommanderDeploymentAssignmentWarningCounts.TryGetValue(key, out int count);
                if (count >= 5)
                    return;

                CommanderDeploymentAssignmentWarningCounts[key] = count + 1;
            }

            try
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: commander deployment assignment warning. " +
                    "Stage=" + (stage ?? string.Empty) +
                    " Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "<null>") +
                    " RequestedSide=" + (message == null ? "<null>" : message.RequestedSide.ToString()) +
                    " Team=" + (team?.TeamIndex.ToString() ?? "<null>") +
                    " TeamSide=" + (team?.Side.ToString() ?? "<null>") +
                    " AssignmentBytes=" + (message?.AssignmentBytes?.Length ?? 0) +
                    " LayoutBytes=" + (message?.FormationLayoutBytes?.Length ?? 0) +
                    " Decoded=" + decodedAssignments +
                    " AppliedMoves=" + appliedMoves +
                    " Rejected=" + rejectedAssignments +
                    " Layouts=" + layoutCount +
                    " Detail=" + (detail ?? string.Empty));
            }
            catch
            {
            }
        }

        private static string BuildCommanderDeploymentFormationSummary(Team team)
        {
            if (team?.FormationsIncludingEmpty == null)
                return "<null>";

            var parts = new List<string>();
            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null)
                    continue;

                parts.Add("#" + formation.Index + "=" + formation.CountOfUnits);
            }

            return parts.Count <= 0 ? string.Empty : string.Join(",", parts.ToArray());
        }

        private struct CommanderDeploymentFormationLayout
        {
            public CommanderDeploymentFormationLayout(Vec2 position, Vec2 direction)
            {
                Position = position;
                Direction = direction;
                IsValid = position.IsValid && direction.IsValid;
            }

            public bool IsValid { get; }
            public Vec2 Position { get; }
            public Vec2 Direction { get; }
        }

        private struct CommanderDeploymentFormationComposition
        {
            public CommanderDeploymentFormationComposition(
                int formationIndex,
                int infantryCount,
                int rangedCount,
                TroopTraitsMask infantryFilter,
                TroopTraitsMask rangedFilter)
                : this(
                    formationIndex,
                    infantryCount,
                    rangedCount,
                    0,
                    0,
                    infantryFilter,
                    rangedFilter,
                    TroopTraitsMask.Melee | TroopTraitsMask.Mount,
                    TroopTraitsMask.Ranged | TroopTraitsMask.Mount)
            {
            }

            public CommanderDeploymentFormationComposition(
                int formationIndex,
                int infantryCount,
                int rangedCount,
                int cavalryCount,
                int horseArcherCount,
                TroopTraitsMask infantryFilter,
                TroopTraitsMask rangedFilter,
                TroopTraitsMask cavalryFilter,
                TroopTraitsMask horseArcherFilter)
            {
                FormationIndex = formationIndex;
                InfantryCount = Math.Max(0, infantryCount);
                RangedCount = Math.Max(0, rangedCount);
                CavalryCount = Math.Max(0, cavalryCount);
                HorseArcherCount = Math.Max(0, horseArcherCount);
                InfantryFilter = infantryFilter & TroopTraitsMask.All;
                RangedFilter = rangedFilter & TroopTraitsMask.All;
                CavalryFilter = cavalryFilter & TroopTraitsMask.All;
                HorseArcherFilter = horseArcherFilter & TroopTraitsMask.All;
            }

            public int FormationIndex { get; }
            public int InfantryCount { get; }
            public int RangedCount { get; }
            public int CavalryCount { get; }
            public int HorseArcherCount { get; }
            public TroopTraitsMask InfantryFilter { get; }
            public TroopTraitsMask RangedFilter { get; }
            public TroopTraitsMask CavalryFilter { get; }
            public TroopTraitsMask HorseArcherFilter { get; }
        }

        private static bool IsCommanderDeploymentOrderOfBattleDiagnosticsEnabled()
        {
            try
            {
                string value = Environment.GetEnvironmentVariable("COOPSPECTATOR_OOB_DIAGNOSTICS");
                if (string.IsNullOrWhiteSpace(value))
                    return false;

                value = value.Trim();
                return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                       value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                       value.Equals("on", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static Agent ResolveMissionAgent(int agentIndex)
        {
            if (agentIndex < 0)
                return null;

            try
            {
                return TaleWorlds.MountAndBlade.Mission.MissionNetworkHelper.GetAgentFromIndex(agentIndex, canBeNull: true);
            }
            catch
            {
                Mission mission = Mission.Current;
                if (mission?.AllAgents == null)
                    return null;

                for (int i = 0; i < mission.AllAgents.Count; i++)
                {
                    Agent candidate = mission.AllAgents[i];
                    if (candidate != null && candidate.Index == agentIndex)
                        return candidate;
                }

                return null;
            }
        }

        private static Formation ResolveCommanderDeploymentFormation(Team team, int formationIndex)
        {
            if (team?.FormationsIncludingEmpty == null ||
                formationIndex < 0 ||
                formationIndex >= (int)FormationClass.NumberOfRegularFormations)
            {
                return null;
            }

            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation != null && formation.Index == formationIndex)
                    return formation;
            }

            return null;
        }

        private static bool IsValidCommanderDeploymentAssignmentAgent(Agent agent, Team team)
        {
            return agent != null &&
                   team != null &&
                   agent.IsActive() &&
                   !agent.IsMount &&
                   ReferenceEquals(agent.Team, team);
        }

        private static void TryStartCommanderDeploymentMassTransfer(Formation formation)
        {
            try
            {
                formation?.OnMassUnitTransferStart();
            }
            catch
            {
            }
        }

        private static void FinalizeCommanderDeploymentFormationAssignment(
            Mission mission,
            Team team,
            Formation formation,
            Agent commanderAgent,
            bool endMassTransfer,
            CommanderDeploymentFormationLayout layout)
        {
            if (mission == null || team == null || formation == null)
                return;

            try
            {
                if (commanderAgent != null && ReferenceEquals(commanderAgent.Team, team))
                    formation.PlayerOwner = commanderAgent;
            }
            catch
            {
            }

            try
            {
                team.TriggerOnFormationsChanged(formation);
            }
            catch
            {
            }

            if (endMassTransfer)
            {
                try
                {
                    formation.OnMassUnitTransferEnd();
                }
                catch
                {
                }
            }

            if (!TryApplyCommanderDeploymentFormationLayout(mission, formation, layout))
                TryEnsureCommanderDeploymentFormationPosition(mission, formation);

            try
            {
                formation.ApplyActionOnEachUnit(
                    agent =>
                    {
                        if (agent == null || !agent.IsActive())
                            return;

                        agent.ForceUpdateCachedAndFormationValues(updateOnlyMovement: false, arrangementChangeAllowed: false);
                        WorldPosition orderPosition = formation.GetOrderPositionOfUnit(agent);
                        if (orderPosition.IsValid)
                            agent.TeleportToPosition(orderPosition.GetGroundVec3());
                    });
                formation.SetHasPendingUnitPositions(hasPendingUnitPositions: false);
                formation.SetMovementOrder(MovementOrder.MovementOrderStop);
            }
            catch
            {
            }

            try
            {
                formation.QuerySystem?.ExpireAfterUnitAddRemove();
                formation.QuerySystem?.Expire();
                team.QuerySystem?.ExpireAfterUnitAddRemove();
                team.QuerySystem?.Expire();
            }
            catch
            {
            }
        }

        private static Dictionary<int, CommanderDeploymentFormationLayout> DecodeCommanderDeploymentFormationLayouts(byte[] formationLayoutBytes)
        {
            var layouts = new Dictionary<int, CommanderDeploymentFormationLayout>();
            if (formationLayoutBytes == null ||
                formationLayoutBytes.Length <= 0 ||
                formationLayoutBytes.Length % CoopCommanderDeploymentFormationAssignmentsMessage.BytesPerFormationLayout != 0)
            {
                return layouts;
            }

            for (int offset = 0; offset + CoopCommanderDeploymentFormationAssignmentsMessage.BytesPerFormationLayout <= formationLayoutBytes.Length;)
            {
                int formationIndex = formationLayoutBytes[offset++];
                float positionX = ReadSingleFromPayload(formationLayoutBytes, ref offset);
                float positionY = ReadSingleFromPayload(formationLayoutBytes, ref offset);
                float directionX = ReadSingleFromPayload(formationLayoutBytes, ref offset);
                float directionY = ReadSingleFromPayload(formationLayoutBytes, ref offset);

                if (formationIndex < 0 ||
                    formationIndex >= (int)FormationClass.NumberOfRegularFormations ||
                    !IsFinite(positionX) ||
                    !IsFinite(positionY) ||
                    !IsFinite(directionX) ||
                    !IsFinite(directionY))
                {
                    continue;
                }

                Vec2 direction = new Vec2(directionX, directionY);
                if (!direction.IsValid || direction.LengthSquared < 0.0001f)
                    direction = Vec2.Forward;
                else
                    direction = direction.Normalized();

                layouts[formationIndex] = new CommanderDeploymentFormationLayout(
                    new Vec2(positionX, positionY),
                    direction);
            }

            return layouts;
        }

        private static bool TryApplyCommanderDeploymentFormationLayout(
            Mission mission,
            Formation formation,
            CommanderDeploymentFormationLayout layout)
        {
            if (mission?.Scene == null ||
                formation == null ||
                !layout.IsValid)
            {
                return false;
            }

            try
            {
                float height = mission.Scene.GetTerrainHeight(layout.Position);
                mission.Scene.GetHeightAtPoint(layout.Position, BodyFlags.None, ref height);
                var worldPosition = new WorldPosition(
                    mission.Scene,
                    UIntPtr.Zero,
                    new Vec3(layout.Position, height),
                    hasValidZ: false);
                CoopSiegeDeploymentBoundaryRuntime.TryClampCommanderDeploymentPosition(
                    mission,
                    formation.Team,
                    ref worldPosition,
                    "server-formation-layout");
                formation.SetPositioning(worldPosition, layout.Direction);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static float ReadSingleFromPayload(byte[] payload, ref int offset)
        {
            float value = BitConverter.ToSingle(payload, offset);
            offset += sizeof(float);
            return value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void TryEnsureCommanderDeploymentFormationPosition(Mission mission, Formation formation)
        {
            if (mission?.Scene == null ||
                formation == null ||
                formation.CountOfUnits <= 0 ||
                formation.OrderPositionIsValid)
            {
                return;
            }

            try
            {
                Vec2 averagePosition = formation.GetAveragePositionOfUnits(excludeDetachedUnits: false, excludePlayer: false);
                float height = mission.Scene.GetTerrainHeight(averagePosition);
                mission.Scene.GetHeightAtPoint(averagePosition, BodyFlags.None, ref height);
                var worldPosition = new WorldPosition(
                    mission.Scene,
                    UIntPtr.Zero,
                    new Vec3(averagePosition, height),
                    hasValidZ: false);
                CoopSiegeDeploymentBoundaryRuntime.TryClampCommanderDeploymentPosition(
                    mission,
                    formation.Team,
                    ref worldPosition,
                    "server-formation-layout-fallback");
                formation.SetPositioning(worldPosition);
            }
            catch
            {
            }
        }

        private bool TryAcknowledgePeerBattleReconnectFinalize(NetworkCommunicator peer, string selectionId)
        {
            if (!GameNetwork.IsServer || peer == null || peer.IsServerPeer)
                return false;

            if (!int.TryParse(selectionId, out int transmissionId) || transmissionId <= 0)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored reconnect finalize ack with invalid transmission id. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " SelectionId=" + (selectionId ?? string.Empty));
                return false;
            }

            return CoopBattlePeerReconnectState.TryAcknowledgeActiveBattleReconnectFinalizeGate(
                peer,
                transmissionId,
                "CoopMissionNetworkBridge.HandleClientSelectionRequest BattleReconnectFinalizeReadyAck");
        }

        private void TryHandleBattleSnapshotBootstrapRequest(NetworkCommunicator peer)
        {
            if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive)
                return;

            if (TryForceResendBattleSnapshotBootstrapV2(
                    peer,
                    "CoopMissionNetworkBridge.TryHandleBattleSnapshotBootstrapRequest"))
            {
                return;
            }

            if (TryPrimePreSynchronizedBattleSnapshotBootstrap(
                    peer,
                    "CoopMissionNetworkBridge.TryHandleBattleSnapshotBootstrapRequest"))
            {
                return;
            }

            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (!TryGetBattleSnapshotTransmissionPayloadDescriptorV2(
                    snapshot,
                    out byte[] payloadBytes,
                    out int logicalByteCount,
                    out string comparisonKey,
                    out string payloadHash,
                    out CoopBattleSnapshotCompressionKind compressionKind,
                    out CoopBattleSnapshotPayloadEncoding payloadEncoding))
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored client bootstrap request because no current battle snapshot payload is available. " +
                    "Peer=" + (peer.UserName ?? "null"));
                return;
            }

            BattleSnapshotTransportState transportState = GetOrCreateBattleSnapshotTransportState(
                peer,
                payloadBytes,
                logicalByteCount,
                comparisonKey,
                payloadHash,
                compressionKind,
                payloadEncoding);
            if (transportState == null)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored client bootstrap request because transport state could not be created yet. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " IsSynchronized=" + peer.IsSynchronized +
                    " ComparisonKey=" + (comparisonKey ?? string.Empty));
                return;
            }

            SendBattleSnapshotManifest(peer, transportState);
            ModLogger.Info(
                "CoopMissionNetworkBridge: resent battle snapshot manifest after explicit client bootstrap request. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + transportState.TransmissionId +
                " ChunkCount=" + transportState.ChunkCount);
        }

        private bool TryForceResendBattleSnapshotBootstrapV2(NetworkCommunicator peer, string source)
        {
            if (!UseBattleSnapshotTransportV2 ||
                !GameNetwork.IsServer ||
                Mission == null ||
                !IsBattleSnapshotBootstrapEligiblePeer(peer, allowUnsynchronizedPeer: true))
            {
                return false;
            }

            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (!TryGetBattleSnapshotTransmissionPayloadDescriptorV2(
                    snapshot,
                    out byte[] payloadBytes,
                    out int logicalByteCount,
                    out string comparisonKey,
                    out string payloadHash,
                    out CoopBattleSnapshotCompressionKind compressionKind,
                    out CoopBattleSnapshotPayloadEncoding payloadEncoding))
            {
                return false;
            }

            BattleSnapshotTransportState transportState = GetOrCreateBattleSnapshotTransportState(
                peer,
                payloadBytes,
                logicalByteCount,
                comparisonKey,
                payloadHash,
                compressionKind,
                payloadEncoding,
                allowUnsynchronizedPeer: true);
            if (transportState == null)
                return false;

            if (transportState.IsCompleted)
                return true;

            int chunksSent = TryAdvanceBattleSnapshotTransportState(
                peer,
                transportState,
                allowUnsynchronizedPeer: true);

            ModLogger.Info(
                "CoopMissionNetworkBridge: force-advanced V2 battle snapshot bootstrap after explicit client request. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + transportState.TransmissionId +
                " ChunksSent=" + chunksSent +
                " ChunkCount=" + transportState.ChunkCount +
                " ActiveWindow=" + transportState.ActiveWindowStartChunkIndex + "-" + transportState.ActiveWindowEndChunkIndex +
                " IsSynchronized=" + peer.IsSynchronized +
                " Source=" + (source ?? "unknown"));
            return true;
        }

        private void TryArmActiveBattleReconnectFinalizeGate(
            NetworkCommunicator peer,
            int transmissionId,
            string source)
        {
            if (!GameNetwork.IsServer || Mission == null || peer == null || peer.IsServerPeer)
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.BattleActive || currentPhase >= CoopBattlePhase.BattleEnded)
                return;

            MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
            if (missionPeer == null)
                return;

            Agent controlledAgent = missionPeer.ControlledAgent;
            if (controlledAgent != null && controlledAgent.IsActive())
                return;

            string reason =
                "active-battle-late-join-finalize" +
                " Phase=" + currentPhase +
                " IsSynchronized=" + peer.IsSynchronized +
                " JustReconnecting=" + peer.JustReconnecting +
                " HasControlledAgent=" + (controlledAgent != null && controlledAgent.IsActive()) +
                " Scene=" + (Mission.SceneName ?? "null");
            CoopBattlePeerReconnectState.ArmActiveBattleReconnectFinalizeGate(
                peer,
                transmissionId,
                source,
                reason);
        }

        private bool TryPrimePreSynchronizedBattleSnapshotBootstrap(NetworkCommunicator peer, string source)
        {
            if (!ShouldUsePreSynchronizedBattleSnapshotBootstrap(peer, out string eligibilitySummary))
                return false;

            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (!TryGetBattleSnapshotTransmissionPayloadDescriptorV2(
                    snapshot,
                    out byte[] payloadBytes,
                    out int logicalByteCount,
                    out string comparisonKey,
                    out string payloadHash,
                    out CoopBattleSnapshotCompressionKind compressionKind,
                    out CoopBattleSnapshotPayloadEncoding payloadEncoding))
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: skipped pre-synchronized V2 battle snapshot bootstrap because no current payload is available yet. " +
                    "Peer=" + (peer?.UserName ?? "null") +
                    " Eligibility=" + (eligibilitySummary ?? "unknown") +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            BattleSnapshotTransportState transportState = GetOrCreateBattleSnapshotTransportState(
                peer,
                payloadBytes,
                logicalByteCount,
                comparisonKey,
                payloadHash,
                compressionKind,
                payloadEncoding,
                allowUnsynchronizedPeer: true);
            if (transportState == null)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: skipped pre-synchronized V2 battle snapshot bootstrap because transport state could not be created. " +
                    "Peer=" + (peer?.UserName ?? "null") +
                    " Eligibility=" + (eligibilitySummary ?? "unknown") +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            bool manifestWasSent = transportState.ManifestSent;
            int sentChunkCountBefore = transportState.SentChunkCount;
            int chunksSentNow = TryAdvanceBattleSnapshotTransportState(
                peer,
                transportState,
                allowUnsynchronizedPeer: true);
            bool manifestSentNow = !manifestWasSent && transportState.ManifestSent;
            int sentChunkDelta = Math.Max(0, transportState.SentChunkCount - sentChunkCountBefore);

            if (!manifestSentNow && chunksSentNow <= 0)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: pre-synchronized V2 battle snapshot bootstrap was already primed before vanilla existing-object sync. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + transportState.TransmissionId +
                    " ChunkCount=" + transportState.ChunkCount +
                    " Eligibility=" + (eligibilitySummary ?? "unknown") +
                    " Source=" + (source ?? "unknown"));
                return true;
            }

            ModLogger.Info(
                "CoopMissionNetworkBridge: primed pre-synchronized V2 battle snapshot bootstrap before vanilla existing-object sync. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + transportState.TransmissionId +
                " ChunkCount=" + transportState.ChunkCount +
                " ManifestSentNow=" + manifestSentNow +
                " ChunksSentNow=" + chunksSentNow +
                " SentChunkDelta=" + sentChunkDelta +
                " ActiveWindow=" + transportState.ActiveWindowStartChunkIndex + "-" + transportState.ActiveWindowEndChunkIndex +
                " Eligibility=" + (eligibilitySummary ?? "unknown") +
                " Source=" + (source ?? "unknown"));
            return true;
        }

        private bool ShouldUsePreSynchronizedBattleSnapshotBootstrap(NetworkCommunicator peer, out string eligibilitySummary)
        {
            eligibilitySummary = string.Empty;
            if (!UseBattleSnapshotTransportV2)
            {
                eligibilitySummary = "transport-v2-disabled";
                return false;
            }

            if (!GameNetwork.IsServer)
            {
                eligibilitySummary = "not-server";
                return false;
            }

            if (Mission == null)
            {
                eligibilitySummary = "mission-null";
                return false;
            }

            if (!MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(Mission.SceneName))
            {
                eligibilitySummary = "scene-not-battle-runtime:" + (Mission.SceneName ?? "null");
                return false;
            }

            if (!IsBattleSnapshotBootstrapEligiblePeer(peer, allowUnsynchronizedPeer: true))
            {
                eligibilitySummary =
                    "peer-ineligible:" +
                    (peer == null
                        ? "null"
                        : "ServerPeer=" + peer.IsServerPeer +
                          " ConnectionActive=" + peer.IsConnectionActive +
                          " IsSynchronized=" + peer.IsSynchronized);
                return false;
            }

            if (peer.IsSynchronized)
            {
                eligibilitySummary = "already-synchronized";
                return false;
            }

            eligibilitySummary =
                "Peer=" + (peer.UserName ?? "null") +
                " JustReconnecting=" + peer.JustReconnecting +
                " IsSynchronized=" + peer.IsSynchronized +
                " Scene=" + (Mission.SceneName ?? "null");
            return true;
        }

        private void HandleServerPayloadChunk(GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattlePayloadChunkMessage message))
                return;

            try
            {
                AcceptClientPayloadChunk(message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: server payload chunk handling failed: " + ex.Message);
            }
        }

        private bool HandleClientBattleSnapshotChunkRequest(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSnapshotChunkRequestMessage message))
                return false;

            try
            {
                AcceptClientBattleSnapshotChunkRequest(peer, message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client battle snapshot chunk request handling failed: " + ex.Message);
            }

            return true;
        }

        private bool HandleClientBattleSnapshotRangeAck(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSnapshotRangeAckMessage message))
                return false;

            try
            {
                AcceptClientBattleSnapshotRangeAck(peer, message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client battle snapshot range ack handling failed: " + ex.Message);
            }

            return true;
        }

        private bool HandleClientBattleSnapshotCompleteAck(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSnapshotCompleteAckMessage message))
                return false;

            try
            {
                AcceptClientBattleSnapshotCompleteAck(peer, message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client battle snapshot complete ack handling failed: " + ex.Message);
            }

            return true;
        }

        private bool HandleClientBattleSnapshotAbort(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSnapshotAbortMessage message))
                return false;

            try
            {
                AcceptClientBattleSnapshotAbort(peer, message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client battle snapshot abort handling failed: " + ex.Message);
            }

            return true;
        }

        private void HandleServerBattleSnapshotManifest(GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSnapshotManifestMessage message))
                return;

            try
            {
                AcceptServerBattleSnapshotManifest(message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: server battle snapshot manifest handling failed: " + ex.Message);
            }
        }

        private void HandleServerBattleSnapshotChunkV2(GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSnapshotChunkV2Message message))
                return;

            try
            {
                AcceptServerBattleSnapshotChunkV2(message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: server battle snapshot chunk V2 handling failed: " + ex.Message);
            }
        }

        private void TrySyncEntryStatusPayloads()
        {
            if (GameNetwork.NetworkPeers == null)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (!IsEligibleRemotePeer(peer))
                    continue;

                if (!AreBootstrapDependentPeerPayloadsReady(peer, out _))
                    continue;

                TrySendEntryStatusToPeer(peer, force: false);
            }
        }

        private void TrySyncMaterializedAgentEntryPayloads()
        {
            if (GameNetwork.NetworkPeers == null)
                return;

            if (!TryGetCachedMaterializedAgentEntryPayload(out CoopBattleEntryStatusBridgeFile.AuthoritativeMaterializedAgentEntrySnapshot snapshot, out string comparisonJson))
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (!IsEligibleRemotePeer(peer))
                    continue;

                if (!AreBootstrapDependentPeerPayloadsReady(peer, out _))
                    continue;

                TrySendMaterializedAgentEntrySnapshotToPeer(peer, force: false, snapshot, comparisonJson);
            }
        }

        private void TrySendImmediatePeerStatusPayloads(NetworkCommunicator peer)
        {
            if (!IsEligibleRemotePeer(peer))
                return;

            if (!AreBootstrapDependentPeerPayloadsReady(peer, out string readinessSummary))
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: withheld bootstrap-dependent peer payloads until runtime bootstrap is ready. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " Readiness=" + (readinessSummary ?? "unknown"));
                return;
            }

            TrySendMaterializedAgentEntrySnapshotToPeer(peer, force: true);
            TrySendEntryStatusToPeer(peer, force: true);
            TrySendAgentControlStateToPeer(peer, force: true);
            foreach (BattleSideEnum side in _delegatedCaptainEntryIdByFormationIndexAndSide.Keys.ToArray())
                TrySendDelegatedCaptainAssignmentStateToPeer(peer, side);
        }

        private void TrySyncAgentControlStates()
        {
            if (!GameNetwork.IsServer || GameNetwork.NetworkPeers == null)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (!IsEligibleRemotePeer(peer))
                    continue;

                TrySendAgentControlStateToPeer(peer, force: false);
            }
        }

        private void TrySendAgentControlStateToPeer(NetworkCommunicator peer, bool force)
        {
            if (peer == null ||
                !CoopBattleAgentControlRuntimeState.TryGetServerState(peer.Index, out CoopBattleAgentControlState state))
            {
                return;
            }

            TrySendAgentControlStateToPeer(peer, state, force);
        }

        private void TrySendAgentControlStateToPeer(
            NetworkCommunicator peer,
            CoopBattleAgentControlState state,
            bool force)
        {
            if (!IsEligibleRemotePeer(peer))
                return;

            if (!force &&
                _lastSentAgentControlRevisionByPeer.TryGetValue(peer.Index, out int lastRevision) &&
                lastRevision == state.Revision)
            {
                return;
            }

            try
            {
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(new CoopBattleAgentControlStateMessage(state));
                GameNetwork.EndModuleEventAsServer();
                _lastSentAgentControlRevisionByPeer[peer.Index] = state.Revision;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: failed to send agent control state. " +
                    "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                    " Mode=" + state.Mode +
                    " AgentIndex=" + state.AgentIndex +
                    " Revision=" + state.Revision +
                    " Error=" + ex.Message);
            }
        }

        private bool AreBootstrapDependentPeerPayloadsReady(NetworkCommunicator peer, out string readinessSummary)
        {
            readinessSummary = string.Empty;
            if (!IsPeerCurrentBattleSnapshotBootstrapReady(peer, out string snapshotReadinessSummary))
            {
                readinessSummary = "BattleSnapshot={" + (snapshotReadinessSummary ?? "unknown") + "}";
                return false;
            }

            if (!CoopMissionSpawnLogic.AreAuthoritativePeerPayloadsReady(Mission, out string payloadReadinessSummary))
            {
                readinessSummary =
                    "BattleSnapshot={" + (snapshotReadinessSummary ?? "unknown") + "} " +
                    "Runtime={" + (payloadReadinessSummary ?? "unknown") + "}";
                return false;
            }

            readinessSummary =
                "BattleSnapshot={" + (snapshotReadinessSummary ?? "unknown") + "} " +
                "Runtime={" + (payloadReadinessSummary ?? "unknown") + "}";
            return true;
        }

        private void TrySendMaterializedAgentEntrySnapshotToPeer(NetworkCommunicator peer, bool force)
        {
            if (!IsEligibleRemotePeer(peer) || Mission == null)
                return;

            if (!TryGetCachedMaterializedAgentEntryPayload(out CoopBattleEntryStatusBridgeFile.AuthoritativeMaterializedAgentEntrySnapshot snapshot, out string comparisonJson))
                return;

            TrySendMaterializedAgentEntrySnapshotToPeer(peer, force, snapshot, comparisonJson);
        }

        private bool TryGetCachedMaterializedAgentEntryPayload(
            out CoopBattleEntryStatusBridgeFile.AuthoritativeMaterializedAgentEntrySnapshot snapshot,
            out string comparisonJson)
        {
            snapshot = CoopMissionSpawnLogic.BuildAuthoritativeMaterializedAgentEntrySnapshot(
                Mission,
                "CoopMissionNetworkBridge",
                out long contentRevision);
            comparisonJson = string.Empty;
            if (snapshot == null)
                return false;

            if (_cachedMaterializedAgentEntrySnapshot == null ||
                _cachedMaterializedAgentEntryContentRevision != contentRevision)
            {
                string rebuiltComparisonJson = SerializeComparableMaterializedAgentEntryPayload(snapshot);
                if (string.IsNullOrWhiteSpace(rebuiltComparisonJson))
                    return false;

                _cachedMaterializedAgentEntryContentRevision = contentRevision;
                _cachedMaterializedAgentEntrySnapshot = snapshot;
                _cachedMaterializedAgentEntryComparisonJson = rebuiltComparisonJson;
            }
            else
            {
                snapshot = _cachedMaterializedAgentEntrySnapshot;
            }

            comparisonJson = _cachedMaterializedAgentEntryComparisonJson;
            return !string.IsNullOrWhiteSpace(comparisonJson);
        }

        private void TrySendMaterializedAgentEntrySnapshotToPeer(
            NetworkCommunicator peer,
            bool force,
            CoopBattleEntryStatusBridgeFile.AuthoritativeMaterializedAgentEntrySnapshot snapshot,
            string comparisonJson)
        {
            if (!IsEligibleRemotePeer(peer) ||
                snapshot == null ||
                string.IsNullOrWhiteSpace(comparisonJson))
            {
                return;
            }

            string transmissionKey = BuildPendingTransmissionKey(peer.Index, CoopBattlePayloadKind.AuthoritativeMaterializedAgentEntrySnapshot);
            bool hasPending = _pendingPayloadsByKey.TryGetValue(transmissionKey, out PendingPayloadTransmission pendingTransmission) &&
                pendingTransmission != null;

            if (hasPending &&
                TryFinalizePendingPayloadTransmission(
                    peer,
                    transmissionKey,
                    _lastSentMaterializedAgentEntryPayloadByPeer,
                    ref pendingTransmission,
                    out bool pendingStillInFlight))
            {
                hasPending = pendingTransmission != null;
                if (pendingStillInFlight)
                    return;
            }

            if (!force &&
                !hasPending &&
                _lastSentMaterializedAgentEntryPayloadByPeer.TryGetValue(peer.Index, out string previousPayload) &&
                string.Equals(previousPayload, comparisonJson, StringComparison.Ordinal))
            {
                return;
            }

            if (!hasPending ||
                !string.Equals(pendingTransmission.ComparisonKey, comparisonJson, StringComparison.Ordinal))
            {
                pendingTransmission = CreateMaterializedAgentEntryPendingTransmission(snapshot, comparisonJson);
                if (pendingTransmission == null)
                    return;

                _pendingPayloadsByKey[transmissionKey] = pendingTransmission;
                if (ExperimentalFeatures.EnableExactBattleAgentContractDiagnostics)
                {
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: queued payload transmission. " +
                        "Peer=" + (peer.UserName ?? "null") +
                        " Kind=" + CoopBattlePayloadKind.AuthoritativeMaterializedAgentEntrySnapshot +
                        " TransmissionId=" + pendingTransmission.TransmissionId +
                        " Bytes=" + pendingTransmission.TotalBytes +
                        (pendingTransmission.LogicalBytes != pendingTransmission.TotalBytes
                            ? " LogicalBytes=" + pendingTransmission.LogicalBytes
                            : string.Empty) +
                        " Chunks=" + pendingTransmission.ChunkCount +
                        " ChunkBytes=" + CoopBattlePayloadChunkMessage.MaxChunkBytes);
                }
            }

            if (!TryFlushPendingPayload(peer, pendingTransmission))
                return;

            if (!pendingTransmission.IsCompleted)
                return;

            _pendingPayloadsByKey.Remove(transmissionKey);
            _lastSentMaterializedAgentEntryPayloadByPeer[peer.Index] = comparisonJson;
            if (ExperimentalFeatures.EnableExactBattleAgentContractDiagnostics)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: completed payload transmission. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " Kind=" + CoopBattlePayloadKind.AuthoritativeMaterializedAgentEntrySnapshot +
                    " TransmissionId=" + pendingTransmission.TransmissionId +
                    " Bytes=" + pendingTransmission.TotalBytes +
                    (pendingTransmission.LogicalBytes != pendingTransmission.TotalBytes
                        ? " LogicalBytes=" + pendingTransmission.LogicalBytes
                        : string.Empty) +
                    " Chunks=" + pendingTransmission.ChunkCount +
                    " EntryCount=" + snapshot.EntryCount);
            }
        }

        private void TrySendEntryStatusToPeer(NetworkCommunicator peer, bool force)
        {
            if (!IsEligibleRemotePeer(peer) || Mission == null)
                return;

            MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
            if (missionPeer == null)
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot =
                CoopMissionSpawnLogic.BuildEntryStatusSnapshotForPeer(Mission, missionPeer, "CoopMissionNetworkBridge");
            if (snapshot == null)
                return;

            string comparisonJson = SerializeComparableEntryStatusPayload(snapshot);
            if (string.IsNullOrWhiteSpace(comparisonJson))
                return;

            string transmissionKey = BuildPendingTransmissionKey(peer.Index, CoopBattlePayloadKind.EntryStatusSnapshot);
            bool hasPending = _pendingPayloadsByKey.TryGetValue(transmissionKey, out PendingPayloadTransmission pendingTransmission) &&
                pendingTransmission != null;

            if (hasPending &&
                TryFinalizePendingPayloadTransmission(
                    peer,
                    transmissionKey,
                    _lastSentStatusPayloadByPeer,
                    ref pendingTransmission,
                    out bool pendingStillInFlight))
            {
                hasPending = pendingTransmission != null;
                if (pendingStillInFlight)
                    return;
            }

            if (!force &&
                !hasPending &&
                _lastSentStatusPayloadByPeer.TryGetValue(peer.Index, out string previousPayload) &&
                string.Equals(previousPayload, comparisonJson, StringComparison.Ordinal))
            {
                return;
            }

            if (!hasPending ||
                !string.Equals(pendingTransmission.ComparisonKey, comparisonJson, StringComparison.Ordinal))
            {
                pendingTransmission = CreateEntryStatusPendingTransmission(snapshot, comparisonJson);
                if (pendingTransmission == null)
                    return;

                _pendingPayloadsByKey[transmissionKey] = pendingTransmission;
                if (ExperimentalFeatures.EnableBattleEntryStatusDiagnostics)
                {
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: queued payload transmission. " +
                        "Peer=" + (peer.UserName ?? "null") +
                        " Kind=" + CoopBattlePayloadKind.EntryStatusSnapshot +
                        " TransmissionId=" + pendingTransmission.TransmissionId +
                        " Bytes=" + pendingTransmission.TotalBytes +
                        (pendingTransmission.LogicalBytes != pendingTransmission.TotalBytes
                            ? " LogicalBytes=" + pendingTransmission.LogicalBytes
                            : string.Empty) +
                        " Chunks=" + pendingTransmission.ChunkCount +
                        " ChunkBytes=" + CoopBattlePayloadChunkMessage.MaxChunkBytes);
                }
            }

            if (!TryFlushPendingPayload(peer, pendingTransmission))
                return;

            if (!pendingTransmission.IsCompleted)
                return;

            _pendingPayloadsByKey.Remove(transmissionKey);
            _lastSentStatusPayloadByPeer[peer.Index] = comparisonJson;
            if (ExperimentalFeatures.EnableBattleEntryStatusDiagnostics)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: completed payload transmission. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " Kind=" + CoopBattlePayloadKind.EntryStatusSnapshot +
                    " TransmissionId=" + pendingTransmission.TransmissionId +
                    " Bytes=" + pendingTransmission.TotalBytes +
                    (pendingTransmission.LogicalBytes != pendingTransmission.TotalBytes
                        ? " LogicalBytes=" + pendingTransmission.LogicalBytes
                        : string.Empty) +
                    " Chunks=" + pendingTransmission.ChunkCount);
            }
        }

        private void TrySyncBattleSnapshotPayloads()
        {
            if (GameNetwork.NetworkPeers == null)
                return;

            if (UseBattleSnapshotTransportV2)
            {
                TrySyncBattleSnapshotPayloadsV2();
                return;
            }

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (!IsBattleSnapshotBootstrapEligiblePeer(peer))
                    continue;

                if (TryRetryUnacknowledgedBattleSnapshot(peer))
                    continue;

                TrySendBattleSnapshotToPeer(peer, force: false);
            }
        }

        private void TrySyncBattleSnapshotPayloadsV2()
        {
            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (snapshot?.Sides == null || snapshot.Sides.Count <= 0)
                return;

            if (!TryGetBattleSnapshotTransmissionPayloadDescriptorV2(
                    snapshot,
                    out byte[] payloadBytes,
                    out int logicalByteCount,
                    out string comparisonKey,
                    out string payloadHash,
                    out CoopBattleSnapshotCompressionKind compressionKind,
                    out CoopBattleSnapshotPayloadEncoding payloadEncoding))
            {
                return;
            }

            List<BattleSnapshotTransportState> activeStates = new List<BattleSnapshotTransportState>();
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (!IsBattleSnapshotBootstrapEligiblePeer(peer))
                    continue;

                BattleSnapshotTransportState transportState = GetOrCreateBattleSnapshotTransportState(
                    peer,
                    payloadBytes,
                    logicalByteCount,
                    comparisonKey,
                    payloadHash,
                    compressionKind,
                    payloadEncoding);
                if (transportState != null && !transportState.IsCompleted)
                    activeStates.Add(transportState);
            }

            int concurrentHeavyPeers = 0;
            foreach (BattleSnapshotTransportState transportState in activeStates
                .OrderBy(state => state.ManifestSent ? 1 : 0)
                .ThenBy(state => state.HasPendingChunkRequests ? 0 : 1)
                .ThenBy(state => state.LastProgressUtc))
            {
                if (!_battleSnapshotTransportStatesByPeer.TryGetValue(transportState.PeerIndex, out BattleSnapshotTransportState currentState))
                    continue;

                NetworkCommunicator peer = GameNetwork.NetworkPeers.FirstOrDefault(candidate => candidate != null && candidate.Index == currentState.PeerIndex);
                if (!IsBattleSnapshotBootstrapEligiblePeer(peer))
                    continue;

                if (!currentState.IsCompleted)
                {
                    concurrentHeavyPeers++;
                    if (concurrentHeavyPeers > BattleSnapshotMaxConcurrentHeavyPeers)
                        continue;
                }

                TryAdvanceBattleSnapshotTransportState(peer, currentState);
            }
        }

        private void TrySendBattleSnapshotToPeer(NetworkCommunicator peer, bool force)
        {
            if (!IsBattleSnapshotBootstrapEligiblePeer(peer))
                return;

            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (snapshot?.Sides == null || snapshot.Sides.Count <= 0)
                return;

            if (!TryGetBattleSnapshotTransmissionPayload(
                    snapshot,
                    out byte[] payloadBytes,
                    out int logicalByteCount,
                    out string comparisonKey))
            {
                return;
            }

            TryQueueOrContinuePayloadTransmission(
                peer,
                CoopBattlePayloadKind.BattleSnapshot,
                payloadBytes,
                logicalByteCount,
                comparisonKey,
                force,
                _lastSentBattleSnapshotPayloadByPeer);
        }

        private BattleSnapshotTransportState GetOrCreateBattleSnapshotTransportState(
            NetworkCommunicator peer,
            byte[] payloadBytes,
            int logicalByteCount,
            string comparisonKey,
            string payloadHash,
            CoopBattleSnapshotCompressionKind compressionKind,
            CoopBattleSnapshotPayloadEncoding payloadEncoding,
            bool allowUnsynchronizedPeer = false)
        {
            if (!IsBattleSnapshotBootstrapEligiblePeer(peer, allowUnsynchronizedPeer) ||
                payloadBytes == null ||
                payloadBytes.Length <= 0 ||
                string.IsNullOrWhiteSpace(comparisonKey))
            {
                return null;
            }

            if (_battleSnapshotTransportStatesByPeer.TryGetValue(peer.Index, out BattleSnapshotTransportState existingState) &&
                existingState != null &&
                string.Equals(existingState.ComparisonKey, comparisonKey, StringComparison.Ordinal))
            {
                return existingState;
            }

            BattleSnapshotTransportState newState = BattleSnapshotTransportState.Create(
                peer.Index,
                payloadBytes,
                logicalByteCount,
                comparisonKey,
                payloadHash,
                compressionKind,
                payloadEncoding,
                NextTransmissionId(),
                BattleSnapshotInitialWindowChunks,
                BattleSnapshotMaxInflightChunksPerPeer);
            if (newState == null)
                return null;

            _battleSnapshotTransportStatesByPeer[peer.Index] = newState;
            RegisterExpectedBattleSnapshotTransmission(peer.Index, newState.TransmissionId);
            _acknowledgedBattleSnapshotTransmissionIdByPeer.Remove(peer.Index);
            _lastCompletedBattleSnapshotTransmissionUtcByPeer.Remove(peer.Index);
            _lastBattleSnapshotRetryUtcByPeer.Remove(peer.Index);
            _lastSentBattleSnapshotPayloadByPeer[peer.Index] = comparisonKey;
            ModLogger.Info(
                "CoopMissionNetworkBridge: initialized V2 battle snapshot transport state. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + newState.TransmissionId +
                " LogicalBytes=" + newState.LogicalBytes +
                " WireBytes=" + newState.TotalBytes +
                " Encoding=" + newState.PayloadEncoding +
                " ChunkCount=" + newState.ChunkCount +
                " ChunkBytes=" + CoopBattleSnapshotChunkV2Message.MaxChunkBytes);
            if (CoopBattlePeerReconnectState.TryGetActiveBattleReconnectFinalizeGateState(peer, out _))
            {
                TryArmActiveBattleReconnectFinalizeGate(
                    peer,
                    newState.TransmissionId,
                    "CoopMissionNetworkBridge.GetOrCreateBattleSnapshotTransportState");
            }
            return newState;
        }

        private int TryAdvanceBattleSnapshotTransportState(
            NetworkCommunicator peer,
            BattleSnapshotTransportState transportState,
            bool allowUnsynchronizedPeer = false)
        {
            if (!IsBattleSnapshotBootstrapEligiblePeer(peer, allowUnsynchronizedPeer) || transportState == null || transportState.IsCompleted)
                return 0;

            DateTime nowUtc = DateTime.UtcNow;
            bool shouldSendManifest = !transportState.HasObservedClientRequest &&
                                      (!transportState.ManifestSent ||
                                       nowUtc - transportState.LastManifestSentUtc >= BattleSnapshotManifestRetryDelay);
            if (shouldSendManifest)
            {
                SendBattleSnapshotManifest(peer, transportState);
                if (transportState.TryPrimeInitialActiveWindow(nowUtc))
                {
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: primed initial V2 battle snapshot active window after manifest. " +
                        "Peer=" + (peer.UserName ?? "null") +
                        " TransmissionId=" + transportState.TransmissionId +
                        " ActiveWindow=" + transportState.ActiveWindowStartChunkIndex + "-" + transportState.ActiveWindowEndChunkIndex +
                        " ChunkCount=" + transportState.ChunkCount);
                }
            }
            else if (transportState.ManifestSent &&
                     !transportState.HasObservedClientRequest &&
                     !transportState.HasActiveWindow &&
                     transportState.TryPrimeInitialActiveWindow(nowUtc))
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: primed initial V2 battle snapshot active window after existing manifest. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + transportState.TransmissionId +
                    " ActiveWindow=" + transportState.ActiveWindowStartChunkIndex + "-" + transportState.ActiveWindowEndChunkIndex +
                    " ChunkCount=" + transportState.ChunkCount);
            }

            if (!transportState.HasActiveWindow)
                return 0;

            if (transportState.IsActiveWindowSatisfiedByClient &&
                transportState.TryAdvanceToNextWindow())
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: advanced V2 battle snapshot active window. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + transportState.TransmissionId +
                    " ActiveWindow=" + transportState.ActiveWindowStartChunkIndex + "-" + transportState.ActiveWindowEndChunkIndex +
                    " HighestContiguous=" + transportState.HighestClientContiguousChunkIndex);
            }

            if (transportState.ShouldResendActiveWindow(nowUtc, BattleSnapshotRangeAckStallDelay))
            {
                transportState.RewindActiveWindowForResend(nowUtc);
                ModLogger.Info(
                    "CoopMissionNetworkBridge: rewound stalled V2 battle snapshot active window. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + transportState.TransmissionId +
                    " ActiveWindow=" + transportState.ActiveWindowStartChunkIndex + "-" + transportState.ActiveWindowEndChunkIndex +
                    " HighestContiguous=" + transportState.HighestClientContiguousChunkIndex);
            }

            int chunksSentThisTick = 0;
            while (chunksSentThisTick < MaxBattleSnapshotChunksPerPayloadPerTick &&
                   transportState.CanSendActiveWindowChunks)
            {
                if (transportState.TryGetNextActiveWindowChunkToSend(out int nextChunkIndex))
                {
                    SendBattleSnapshotChunkV2(peer, transportState, nextChunkIndex);
                    chunksSentThisTick++;
                    continue;
                }

                break;
            }

            return chunksSentThisTick;
        }

        private static void SendBattleSnapshotManifest(NetworkCommunicator peer, BattleSnapshotTransportState transportState)
        {
            if (peer == null || transportState == null)
                return;

            GameNetwork.BeginModuleEventAsServer(peer);
            GameNetwork.WriteMessage(new CoopBattleSnapshotManifestMessage(
                transportState.TransmissionId,
                BattleSnapshotTransportSchemaVersion,
                transportState.PayloadEncoding,
                transportState.CompressionKind,
                transportState.LogicalBytes,
                transportState.TotalBytes,
                CoopBattleSnapshotChunkV2Message.MaxChunkBytes,
                transportState.ChunkCount,
                transportState.ComparisonKey,
                transportState.PayloadHash));
            GameNetwork.EndModuleEventAsServer();
            transportState.MarkManifestSent(DateTime.UtcNow);
            ModLogger.Info(
                "CoopMissionNetworkBridge: sent V2 battle snapshot manifest. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + transportState.TransmissionId +
                " Encoding=" + transportState.PayloadEncoding +
                " ChunkCount=" + transportState.ChunkCount +
                " WireBytes=" + transportState.TotalBytes);
        }

        private static void SendBattleSnapshotChunkV2(NetworkCommunicator peer, BattleSnapshotTransportState transportState, int chunkIndex)
        {
            if (peer == null || transportState == null || chunkIndex < 0 || chunkIndex >= transportState.ChunkCount)
                return;

            byte[] chunkBytes = transportState.Chunks[chunkIndex] ?? Array.Empty<byte>();
            GameNetwork.BeginModuleEventAsServer(peer);
            GameNetwork.WriteMessage(new CoopBattleSnapshotChunkV2Message(
                transportState.TransmissionId,
                chunkIndex,
                transportState.ChunkCount,
                chunkBytes));
            GameNetwork.EndModuleEventAsServer();
            transportState.MarkChunkSent(chunkIndex, DateTime.UtcNow);
            if (chunkIndex == 0)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: sent first V2 battle snapshot chunk. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + transportState.TransmissionId +
                    " ChunkCount=" + transportState.ChunkCount +
                    " Bytes=" + chunkBytes.Length);
            }
        }

        private void AcceptClientBattleSnapshotChunkRequest(NetworkCommunicator peer, CoopBattleSnapshotChunkRequestMessage message)
        {
            if (peer == null || message == null)
                return;

            if (!_battleSnapshotTransportStatesByPeer.TryGetValue(peer.Index, out BattleSnapshotTransportState transportState) ||
                transportState == null ||
                transportState.TransmissionId != message.TransmissionId)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored V2 battle snapshot chunk request with unknown transmission. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + message.TransmissionId +
                    " Range=" + message.StartChunkIndex + "-" + message.EndChunkIndex);
                return;
            }

            transportState.ObserveClientChunkRequest(
                message.StartChunkIndex,
                message.EndChunkIndex,
                message.HighestContiguousChunkIndex,
                message.ReceivedChunkCount,
                DateTime.UtcNow);
            ModLogger.Info(
                "CoopMissionNetworkBridge: accepted V2 battle snapshot chunk request. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + message.TransmissionId +
                " Range=" + message.StartChunkIndex + "-" + message.EndChunkIndex +
                " HighestContiguous=" + message.HighestContiguousChunkIndex +
                " ReceivedChunkCount=" + message.ReceivedChunkCount +
                " ActiveWindow=" + transportState.ActiveWindowStartChunkIndex + "-" + transportState.ActiveWindowEndChunkIndex);

            TryAdvanceBattleSnapshotTransportState(
                peer,
                transportState,
                allowUnsynchronizedPeer: true);
        }

        private void AcceptClientBattleSnapshotRangeAck(NetworkCommunicator peer, CoopBattleSnapshotRangeAckMessage message)
        {
            if (peer == null || message == null)
                return;

            if (!_battleSnapshotTransportStatesByPeer.TryGetValue(peer.Index, out BattleSnapshotTransportState transportState) ||
                transportState == null ||
                transportState.TransmissionId != message.TransmissionId)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored V2 battle snapshot range ack with unknown transmission. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + message.TransmissionId +
                    " HighestContiguous=" + message.HighestContiguousChunkIndex +
                    " ReceivedChunkCount=" + message.ReceivedChunkCount);
                return;
            }

            transportState.ObserveClientProgressAck(
                message.HighestContiguousChunkIndex,
                message.ReceivedChunkCount,
                DateTime.UtcNow);
            ModLogger.Info(
                "CoopMissionNetworkBridge: accepted V2 battle snapshot range ack. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + message.TransmissionId +
                " HighestContiguous=" + message.HighestContiguousChunkIndex +
                " ReceivedChunkCount=" + message.ReceivedChunkCount +
                " ActiveWindow=" + transportState.ActiveWindowStartChunkIndex + "-" + transportState.ActiveWindowEndChunkIndex +
                " State=" + message.AssemblyState);

            TryAdvanceBattleSnapshotTransportState(
                peer,
                transportState,
                allowUnsynchronizedPeer: true);
        }

        private void AcceptClientBattleSnapshotCompleteAck(NetworkCommunicator peer, CoopBattleSnapshotCompleteAckMessage message)
        {
            if (peer == null || message == null)
                return;

            if (!_battleSnapshotTransportStatesByPeer.TryGetValue(peer.Index, out BattleSnapshotTransportState transportState) ||
                transportState == null ||
                transportState.TransmissionId != message.TransmissionId)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored V2 battle snapshot complete ack with unknown transmission. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + message.TransmissionId);
                return;
            }

            bool hashMatched = string.IsNullOrWhiteSpace(message.PayloadHash) ||
                               string.Equals(message.PayloadHash, transportState.PayloadHash, StringComparison.Ordinal);
            transportState.MarkCompleted(message.AppliedSuccessfully && hashMatched, DateTime.UtcNow);
            _acknowledgedBattleSnapshotTransmissionIdByPeer[peer.Index] = message.TransmissionId;
            _lastCompletedBattleSnapshotTransmissionUtcByPeer.Remove(peer.Index);
            _lastBattleSnapshotRetryUtcByPeer.Remove(peer.Index);
            ModLogger.Info(
                "CoopMissionNetworkBridge: acknowledged V2 battle snapshot completion. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + message.TransmissionId +
                " AppliedSuccessfully=" + message.AppliedSuccessfully +
                " HashMatched=" + hashMatched);
            TrySendImmediatePeerStatusPayloads(peer);
            LateJoinPeerBootstrapGatePatch.TryReplayDeferredPeerBootstrap(
                peer,
                "CoopMissionNetworkBridge.AcceptClientBattleSnapshotCompleteAck");
        }

        private void AcceptClientBattleSnapshotAbort(NetworkCommunicator peer, CoopBattleSnapshotAbortMessage message)
        {
            if (peer == null || message == null)
                return;

            if (!_battleSnapshotTransportStatesByPeer.TryGetValue(peer.Index, out BattleSnapshotTransportState transportState) ||
                transportState == null ||
                transportState.TransmissionId != message.TransmissionId)
            {
                return;
            }

            transportState.ResetForRestart(DateTime.UtcNow);
            ModLogger.Info(
                "CoopMissionNetworkBridge: client aborted V2 battle snapshot transport. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + message.TransmissionId +
                " Reason=" + (message.Reason ?? string.Empty));
        }

        private void AcceptServerBattleSnapshotManifest(CoopBattleSnapshotManifestMessage message)
        {
            if (message == null || message.TransmissionId <= 0)
                return;

            string normalizedPayloadHash = message.PayloadHash ?? string.Empty;
            bool alreadyAppliedCurrentSnapshot =
                _clientAppliedBattleSnapshotTransmissionId == message.TransmissionId &&
                string.Equals(
                    _clientAppliedBattleSnapshotPayloadHash ?? string.Empty,
                    normalizedPayloadHash,
                    StringComparison.Ordinal);
            if (alreadyAppliedCurrentSnapshot)
            {
                _clientObservedBattleSnapshotTransmissionId = message.TransmissionId;
                _clientObservedBattleSnapshotPayloadHash = normalizedPayloadHash;
                _clientBattleSnapshotAssembliesByTransmission.Remove(message.TransmissionId);
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored duplicate V2 battle snapshot manifest because the current snapshot is already applied. " +
                    "TransmissionId=" + message.TransmissionId +
                    " ChunkCount=" + message.ChunkCount);
                return;
            }

            ObserveClientBattleSnapshotManifest(message.TransmissionId, message.PayloadHash);

            foreach (int staleTransmissionId in _clientBattleSnapshotAssembliesByTransmission.Keys
                .Where(existingTransmissionId => existingTransmissionId != message.TransmissionId)
                .ToArray())
            {
                _clientBattleSnapshotAssembliesByTransmission.Remove(staleTransmissionId);
            }

            if (_clientBattleSnapshotAssembliesByTransmission.TryGetValue(message.TransmissionId, out BattleSnapshotClientAssemblyState existingState) &&
                existingState != null &&
                existingState.ChunkCount == message.ChunkCount &&
                string.Equals(existingState.PayloadHash, message.PayloadHash, StringComparison.Ordinal))
            {
                existingState.MarkManifestObserved(DateTime.UtcNow);
                if (!existingState.IsComplete)
                    SendClientBattleSnapshotChunkRequest(existingState, CoopBattleSnapshotAssemblyStateKind.Receiving, "manifest-repeat");
                return;
            }

            BattleSnapshotClientAssemblyState assemblyState = new BattleSnapshotClientAssemblyState(
                message.TransmissionId,
                message.ChunkCount,
                message.LogicalBytes,
                message.WireBytes,
                message.ComparisonKey,
                message.PayloadHash,
                message.PayloadEncoding,
                message.CompressionKind);
            _clientBattleSnapshotAssembliesByTransmission[message.TransmissionId] = assemblyState;
            ModLogger.Info(
                "CoopMissionNetworkBridge: received V2 battle snapshot manifest. " +
                "TransmissionId=" + message.TransmissionId +
                " ChunkCount=" + message.ChunkCount +
                " WireBytes=" + message.WireBytes +
                " LogicalBytes=" + message.LogicalBytes);
            SendClientBattleSnapshotChunkRequest(assemblyState, CoopBattleSnapshotAssemblyStateKind.Receiving, "manifest-initial");
        }

        private void AcceptServerBattleSnapshotChunkV2(CoopBattleSnapshotChunkV2Message message)
        {
            if (message == null || message.TransmissionId <= 0 || message.ChunkCount <= 0)
                return;

            if (!_clientBattleSnapshotAssembliesByTransmission.TryGetValue(message.TransmissionId, out BattleSnapshotClientAssemblyState assemblyState) ||
                assemblyState == null)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: received V2 battle snapshot chunk before manifest. " +
                    "TransmissionId=" + message.TransmissionId +
                    " ChunkIndex=" + message.ChunkIndex +
                    " ChunkCount=" + message.ChunkCount);
                return;
            }

            assemblyState.AcceptChunk(message.ChunkIndex, message.PayloadBytes ?? Array.Empty<byte>(), DateTime.UtcNow);
            if (message.ChunkIndex == 0)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: received first V2 battle snapshot chunk. " +
                    "TransmissionId=" + message.TransmissionId +
                    " ChunkCount=" + message.ChunkCount +
                    " Bytes=" + (message.PayloadBytes?.Length ?? 0));
            }

            if (!assemblyState.IsComplete &&
                assemblyState.TryGetCompletedWindowEndChunkIndex(BattleSnapshotInitialWindowChunks, out int completedWindowEndChunkIndex))
            {
                SendClientBattleSnapshotRangeAck(assemblyState, CoopBattleSnapshotAssemblyStateKind.Receiving, "window-complete");
                assemblyState.MarkWindowCompletionAcknowledged(completedWindowEndChunkIndex, DateTime.UtcNow);
            }

            if (!assemblyState.IsComplete)
                return;

            byte[] payloadBytes = assemblyState.Combine();
            if (!TryDecodeBattleSnapshotPayload(assemblyState, payloadBytes, out BattleSnapshotMessage snapshot))
            {
                SendClientBattleSnapshotAbort(assemblyState.TransmissionId, "decode-failed");
                return;
            }
            if (snapshot == null)
            {
                SendClientBattleSnapshotAbort(assemblyState.TransmissionId, "deserialize-failed");
                return;
            }

            BattleSnapshotRuntimeState.SetCurrent(snapshot, "CoopMissionNetworkBridge.V2");
            CampaignMapPatchMissionInit.TryApplyCampaignAtmosphereToLiveScene(
                Mission,
                "CoopMissionNetworkBridge.V2 applied authoritative battle snapshot");
            MarkClientBattleSnapshotApplied(assemblyState.TransmissionId, assemblyState.PayloadHash);
            _lastClientBattleSnapshotBootstrapRequestUtc = DateTime.MinValue;
            BattleMapSpawnHandoffPatch.TryProcessDeferredClientCreateAgentMessages(
                Mission,
                "CoopMissionNetworkBridge.V2 applied");
            BattleMapSpawnHandoffPatch.TryProcessDeferredClientMountedHeroCreateAgents(
                Mission,
                "CoopMissionNetworkBridge.V2 applied");
            _clientBattleSnapshotAssembliesByTransmission.Remove(assemblyState.TransmissionId);
            SendClientBattleSnapshotCompleteAck(assemblyState.TransmissionId, assemblyState.PayloadHash, appliedSuccessfully: true);
            ModLogger.Info(
                "CoopMissionNetworkBridge: applied V2 battle snapshot payload on client. " +
                "TransmissionId=" + assemblyState.TransmissionId +
                " BattleId=" + (snapshot.BattleId ?? string.Empty) +
                " Sides=" + (snapshot.Sides?.Count ?? 0));
        }

        private void TryQueueOrContinuePayloadTransmission(
            NetworkCommunicator peer,
            CoopBattlePayloadKind payloadKind,
            byte[] payloadBytes,
            int logicalByteCount,
            string comparisonKey,
            bool force,
            Dictionary<int, string> lastSentPayloadByPeer)
        {
            if (peer == null || payloadBytes == null || payloadBytes.Length <= 0 || lastSentPayloadByPeer == null)
            {
                return;
            }

            string transmissionKey = BuildPendingTransmissionKey(peer.Index, payloadKind);
            bool hasPending = _pendingPayloadsByKey.TryGetValue(transmissionKey, out PendingPayloadTransmission pendingTransmission) &&
                pendingTransmission != null;

            if (hasPending &&
                TryFinalizePendingPayloadTransmission(
                    peer,
                    transmissionKey,
                    lastSentPayloadByPeer,
                    ref pendingTransmission,
                    out bool pendingStillInFlight))
            {
                hasPending = pendingTransmission != null;
                if (pendingStillInFlight)
                    return;
            }

            if (!force &&
                !hasPending &&
                lastSentPayloadByPeer.TryGetValue(peer.Index, out string previousPayload) &&
                string.Equals(previousPayload, comparisonKey, StringComparison.Ordinal))
            {
                return;
            }

            if (!hasPending ||
                !string.Equals(pendingTransmission.ComparisonKey, comparisonKey, StringComparison.Ordinal))
            {
                pendingTransmission = PendingPayloadTransmission.Create(
                    payloadKind,
                    payloadBytes,
                    logicalByteCount,
                    comparisonKey,
                    NextTransmissionId());
                if (pendingTransmission == null)
                    return;

                if (payloadKind == CoopBattlePayloadKind.BattleSnapshot)
                    RegisterExpectedBattleSnapshotTransmission(peer.Index, pendingTransmission.TransmissionId);

                _pendingPayloadsByKey[transmissionKey] = pendingTransmission;
                ModLogger.Info(
                    "CoopMissionNetworkBridge: queued payload transmission. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " Kind=" + payloadKind +
                    " TransmissionId=" + pendingTransmission.TransmissionId +
                    " Bytes=" + pendingTransmission.TotalBytes +
                    (pendingTransmission.LogicalBytes != pendingTransmission.TotalBytes
                        ? " LogicalBytes=" + pendingTransmission.LogicalBytes
                        : string.Empty) +
                    " Chunks=" + pendingTransmission.ChunkCount +
                    " ChunkBytes=" + CoopBattlePayloadChunkMessage.MaxChunkBytes);
            }

            if (!TryFlushPendingPayload(peer, pendingTransmission))
                return;

            if (!pendingTransmission.IsCompleted)
                return;

            _pendingPayloadsByKey.Remove(transmissionKey);
            lastSentPayloadByPeer[peer.Index] = comparisonKey;
            if (payloadKind == CoopBattlePayloadKind.BattleSnapshot)
                MarkBattleSnapshotTransmissionCompleted(peer.Index);
            ModLogger.Info(
                "CoopMissionNetworkBridge: completed payload transmission. " +
                "Peer=" + (peer.UserName ?? "null") +
                " Kind=" + payloadKind +
                " TransmissionId=" + pendingTransmission.TransmissionId +
                " Bytes=" + pendingTransmission.TotalBytes +
                (pendingTransmission.LogicalBytes != pendingTransmission.TotalBytes
                    ? " LogicalBytes=" + pendingTransmission.LogicalBytes
                    : string.Empty) +
                " Chunks=" + pendingTransmission.ChunkCount);
        }

        private static bool TryFlushPendingPayload(NetworkCommunicator peer, PendingPayloadTransmission pendingTransmission)
        {
            if (peer == null || pendingTransmission == null || pendingTransmission.IsCompleted)
                return false;

            int chunksSent = 0;
            int chunkBudget = ResolveChunkBudgetPerTick(pendingTransmission);
            while (chunksSent < chunkBudget && pendingTransmission.NextChunkIndex < pendingTransmission.ChunkCount)
            {
                byte[] chunkBytes = pendingTransmission.Chunks[pendingTransmission.NextChunkIndex] ?? Array.Empty<byte>();
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(new CoopBattlePayloadChunkMessage(
                    pendingTransmission.PayloadKind,
                    pendingTransmission.TransmissionId,
                    pendingTransmission.NextChunkIndex,
                    pendingTransmission.ChunkCount,
                    chunkBytes));
                GameNetwork.EndModuleEventAsServer();
                pendingTransmission.NextChunkIndex++;
                chunksSent++;
            }

            return chunksSent > 0;
        }

        private static int ResolveChunkBudgetPerTick(PendingPayloadTransmission pendingTransmission)
        {
            if (pendingTransmission == null)
                return MaxStatusChunksPerPayloadPerTick;

            return pendingTransmission.PayloadKind == CoopBattlePayloadKind.BattleSnapshot
                ? MaxBattleSnapshotChunksPerPayloadPerTick
                : MaxStatusChunksPerPayloadPerTick;
        }

        private bool TryFinalizePendingPayloadTransmission(
            NetworkCommunicator peer,
            string transmissionKey,
            Dictionary<int, string> lastSentPayloadByPeer,
            ref PendingPayloadTransmission pendingTransmission,
            out bool pendingStillInFlight)
        {
            pendingStillInFlight = false;
            if (peer == null || string.IsNullOrWhiteSpace(transmissionKey) || pendingTransmission == null)
                return false;

            if (!pendingTransmission.IsCompleted)
            {
                if (!TryFlushPendingPayload(peer, pendingTransmission) || !pendingTransmission.IsCompleted)
                {
                    pendingStillInFlight = true;
                    return true;
                }
            }

            _pendingPayloadsByKey.Remove(transmissionKey);
            if (lastSentPayloadByPeer != null)
                lastSentPayloadByPeer[peer.Index] = pendingTransmission.ComparisonKey;
            if (pendingTransmission.PayloadKind == CoopBattlePayloadKind.BattleSnapshot)
                MarkBattleSnapshotTransmissionCompleted(peer.Index);
            bool logCompletion =
                pendingTransmission.PayloadKind == CoopBattlePayloadKind.BattleSnapshot ||
                (pendingTransmission.PayloadKind == CoopBattlePayloadKind.EntryStatusSnapshot &&
                 ExperimentalFeatures.EnableBattleEntryStatusDiagnostics) ||
                (pendingTransmission.PayloadKind == CoopBattlePayloadKind.AuthoritativeMaterializedAgentEntrySnapshot &&
                 ExperimentalFeatures.EnableExactBattleAgentContractDiagnostics);
            if (logCompletion)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: completed payload transmission. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " Kind=" + pendingTransmission.PayloadKind +
                    " TransmissionId=" + pendingTransmission.TransmissionId +
                    " Bytes=" + pendingTransmission.TotalBytes +
                    (pendingTransmission.LogicalBytes != pendingTransmission.TotalBytes
                        ? " LogicalBytes=" + pendingTransmission.LogicalBytes
                        : string.Empty) +
                    " Chunks=" + pendingTransmission.ChunkCount);
            }
            pendingTransmission = null;
            return true;
        }

        private bool TryRetryUnacknowledgedBattleSnapshot(NetworkCommunicator peer)
        {
            if (!IsBattleSnapshotBootstrapEligiblePeer(peer))
                return false;

            string transmissionKey = BuildPendingTransmissionKey(peer.Index, CoopBattlePayloadKind.BattleSnapshot);
            if (_pendingPayloadsByKey.TryGetValue(transmissionKey, out PendingPayloadTransmission pendingTransmission) &&
                pendingTransmission != null)
            {
                return false;
            }

            _expectedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out int expectedTransmissionId);
            _acknowledgedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out int acknowledgedTransmissionId);
            if (expectedTransmissionId <= 0 || acknowledgedTransmissionId >= expectedTransmissionId)
                return false;

            if (!_lastSentBattleSnapshotPayloadByPeer.ContainsKey(peer.Index) ||
                !_lastCompletedBattleSnapshotTransmissionUtcByPeer.TryGetValue(peer.Index, out DateTime completedUtc))
            {
                return false;
            }

            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc - completedUtc < BattleSnapshotAckRetryDelay)
                return false;

            if (_lastBattleSnapshotRetryUtcByPeer.TryGetValue(peer.Index, out DateTime previousRetryUtc) &&
                nowUtc - previousRetryUtc < BattleSnapshotAckRetryDelay)
            {
                return false;
            }

            _lastBattleSnapshotRetryUtcByPeer[peer.Index] = nowUtc;
            ModLogger.Info(
                "CoopMissionNetworkBridge: retrying unacknowledged battle snapshot payload. " +
                "Peer=" + (peer.UserName ?? "null") +
                " ExpectedTransmissionId=" + expectedTransmissionId +
                " AcknowledgedTransmissionId=" + acknowledgedTransmissionId +
                " SecondsSinceCompleted=" + (nowUtc - completedUtc).TotalSeconds.ToString("F1"));
            TrySendBattleSnapshotToPeer(peer, force: true);
            return true;
        }

        private void MarkBattleSnapshotTransmissionCompleted(int peerIndex)
        {
            if (peerIndex < 0)
                return;

            _lastCompletedBattleSnapshotTransmissionUtcByPeer[peerIndex] = DateTime.UtcNow;
        }

        private void AcceptClientPayloadChunk(CoopBattlePayloadChunkMessage message)
        {
            if (message == null || message.ChunkCount <= 0 || message.ChunkIndex < 0 || message.ChunkIndex >= message.ChunkCount)
                return;

            if (UseBattleSnapshotTransportV2 &&
                message.PayloadKind == CoopBattlePayloadKind.BattleSnapshot &&
                message.ChunkIndex == 0)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: legacy battle snapshot payload received while V2 transport is enabled. " +
                    "TransmissionId=" + message.TransmissionId +
                    " ChunkCount=" + message.ChunkCount +
                    " Bytes=" + (message.PayloadBytes?.Length ?? 0));
            }

            string assemblyKey = BuildAssemblyKey(message.PayloadKind, message.TransmissionId);
            bool createdAssembly = false;
            if (!_clientPayloadAssemblies.TryGetValue(assemblyKey, out PayloadAssemblyState assemblyState) ||
                assemblyState == null ||
                assemblyState.ChunkCount != message.ChunkCount)
            {
                assemblyState = new PayloadAssemblyState(message.PayloadKind, message.TransmissionId, message.ChunkCount);
                _clientPayloadAssemblies[assemblyKey] = assemblyState;
                createdAssembly = true;
            }

            if (createdAssembly)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: received first payload chunk. " +
                    "Kind=" + message.PayloadKind +
                    " TransmissionId=" + message.TransmissionId +
                    " ChunkIndex=" + message.ChunkIndex +
                    " ChunkCount=" + message.ChunkCount +
                    " Bytes=" + (message.PayloadBytes?.Length ?? 0));
            }

            if (assemblyState.Chunks[message.ChunkIndex] == null)
                assemblyState.ReceivedChunkCount++;
            assemblyState.Chunks[message.ChunkIndex] = message.PayloadBytes ?? Array.Empty<byte>();

            if (assemblyState.ReceivedChunkCount < assemblyState.ChunkCount)
                return;

            _clientPayloadAssemblies.Remove(assemblyKey);
            byte[] payloadBytes = assemblyState.Combine();
            ModLogger.Info(
                "CoopMissionNetworkBridge: assembled client payload. " +
                "Kind=" + assemblyState.PayloadKind +
                " TransmissionId=" + assemblyState.TransmissionId +
                " Bytes=" + payloadBytes.Length +
                " Chunks=" + assemblyState.ChunkCount);
            ApplyCompletedPayload(assemblyState.PayloadKind, assemblyState.TransmissionId, payloadBytes);
        }

        private void ApplyCompletedPayload(CoopBattlePayloadKind payloadKind, int transmissionId, byte[] payloadBytes)
        {
            if (!TryDecodePayloadJson(payloadKind, payloadBytes, out string payloadJson))
                return;

            if (string.IsNullOrWhiteSpace(payloadJson))
                return;

            switch (payloadKind)
            {
                case CoopBattlePayloadKind.AuthoritativeMaterializedAgentEntrySnapshot:
                {
                    CoopBattleEntryStatusBridgeFile.AuthoritativeMaterializedAgentEntrySnapshot snapshot =
                        JsonConvert.DeserializeObject<CoopBattleEntryStatusBridgeFile.AuthoritativeMaterializedAgentEntrySnapshot>(payloadJson, JsonSettings);
                    if (snapshot != null)
                    {
                        CoopMissionSpawnLogic.ObserveClientAuthoritativeMaterializedAgentEntrySnapshot(
                            snapshot,
                            "CoopMissionNetworkBridge.ApplyCompletedPayload");
                        ModLogger.Info(
                            "CoopMissionNetworkBridge: applied client payload. " +
                            "Kind=" + payloadKind +
                            " BattleId=" + (snapshot.BattleId ?? string.Empty) +
                            " MissionName=" + (snapshot.MissionName ?? string.Empty) +
                            " UseStringIdExactEquipmentPath=" + snapshot.UseStringIdExactEquipmentPath +
                            " EntryCount=" + snapshot.EntryCount +
                            " Source=" + (snapshot.Source ?? string.Empty));
                    }
                    break;
                }
                case CoopBattlePayloadKind.EntryStatusSnapshot:
                {
                    CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot =
                        JsonConvert.DeserializeObject<CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot>(payloadJson, JsonSettings);
                    if (snapshot != null)
                    {
                        int selectableEntryCount = CountSerializedIdList(snapshot.SelectableEntryIds);
                        int attackerSelectableEntryCount = snapshot.AttackerSelectableEntryCount > 0
                            ? snapshot.AttackerSelectableEntryCount
                            : CountSerializedIdList(snapshot.AttackerSelectableEntryIds);
                        int defenderSelectableEntryCount = snapshot.DefenderSelectableEntryCount > 0
                            ? snapshot.DefenderSelectableEntryCount
                            : CountSerializedIdList(snapshot.DefenderSelectableEntryIds);
                        CoopBattleEntryStatusBridgeFile.WriteStatus(snapshot);
                        ModLogger.Info(
                            "CoopMissionNetworkBridge: applied client payload. " +
                            "Kind=" + payloadKind +
                            " BattleDataReady=" + snapshot.BattleDataReady +
                            " BattleDataReadinessStage=" + (snapshot.BattleDataReadinessStage ?? string.Empty) +
                            " BattleDataReadinessReason=" + (snapshot.BattleDataReadinessReason ?? string.Empty) +
                            " AssignedSide=" + (snapshot.AssignedSide ?? string.Empty) +
                            " SelectedEntryId=" + (snapshot.SelectedEntryId ?? string.Empty) +
                            " SelectableEntryCount=" + selectableEntryCount +
                            " SelectableEntrySource=" + (snapshot.SelectableEntrySource ?? string.Empty) +
                            " AttackerSelectableEntryCount=" + attackerSelectableEntryCount +
                            " DefenderSelectableEntryCount=" + defenderSelectableEntryCount +
                            " AuthoritativeMaterializedAgentEntryCount=" + snapshot.AuthoritativeMaterializedAgentEntryCount +
                            " CanRespawn=" + snapshot.CanRespawn +
                            " CanStartBattle=" + snapshot.CanStartBattle +
                            " HasAgent=" + snapshot.HasAgent +
                            " Lifecycle=" + (snapshot.LifecycleState ?? string.Empty) +
                            " Peer=" + (snapshot.PeerName ?? string.Empty));
                    }
                    break;
                }
                case CoopBattlePayloadKind.BattleSnapshot:
                {
                    BattleSnapshotMessage snapshot =
                        JsonConvert.DeserializeObject<BattleSnapshotMessage>(payloadJson, JsonSettings);
                    if (snapshot != null)
                    {
                        BattleSnapshotRuntimeState.SetCurrent(snapshot, "CoopMissionNetworkBridge");
                        bool acknowledged = CoopBattleNetworkRequestTransport.TryAcknowledgeBattleSnapshot(
                            transmissionId,
                            "CoopMissionNetworkBridge.ApplyCompletedPayload");
                        ModLogger.Info(
                            "CoopMissionNetworkBridge: applied client payload. " +
                            "Kind=" + payloadKind +
                            " TransmissionId=" + transmissionId +
                            " BattleId=" + (snapshot.BattleId ?? string.Empty) +
                            " MapScene=" + (snapshot.MapScene ?? string.Empty) +
                            " Parties=" + (snapshot.Sides?.Sum(side => side?.Parties?.Count ?? 0) ?? 0) +
                            " Sides=" + (snapshot.Sides?.Count ?? 0) +
                            " AckSent=" + acknowledged);
                    }
                    break;
                }
            }
        }

        private static string SerializePayload(object payload)
        {
            try
            {
                return JsonConvert.SerializeObject(payload, JsonSettings);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: payload serialization failed: " + ex.Message);
                return string.Empty;
            }
        }

        private static string SerializeComparableEntryStatusPayload(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;

            DateTime originalUpdatedUtc = snapshot.UpdatedUtc;
            string originalSource = snapshot.Source;
            string originalBattlePhaseSource = snapshot.BattlePhaseSource;
            string originalLifecycleSource = snapshot.LifecycleSource;
            string originalSpawnReason = snapshot.SpawnReason;
            EntryStatusTransportFieldState transportFieldState = CompactEntryStatusSnapshotForTransport(snapshot);
            try
            {
                snapshot.UpdatedUtc = DateTime.MinValue;
                snapshot.Source = string.Empty;
                snapshot.BattlePhaseSource = string.Empty;
                snapshot.LifecycleSource = string.Empty;
                snapshot.SpawnReason = string.Empty;
                return SerializePayload(snapshot);
            }
            finally
            {
                transportFieldState.Restore(snapshot);
                snapshot.UpdatedUtc = originalUpdatedUtc;
                snapshot.Source = originalSource;
                snapshot.BattlePhaseSource = originalBattlePhaseSource;
                snapshot.LifecycleSource = originalLifecycleSource;
                snapshot.SpawnReason = originalSpawnReason;
            }
        }

        private static string SerializeComparableMaterializedAgentEntryPayload(
            CoopBattleEntryStatusBridgeFile.AuthoritativeMaterializedAgentEntrySnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;

            DateTime originalUpdatedUtc = snapshot.UpdatedUtc;
            string originalSource = snapshot.Source;
            try
            {
                snapshot.UpdatedUtc = DateTime.MinValue;
                snapshot.Source = string.Empty;
                return SerializePayload(snapshot);
            }
            finally
            {
                snapshot.UpdatedUtc = originalUpdatedUtc;
                snapshot.Source = originalSource;
            }
        }

        private bool TryGetBattleSnapshotTransmissionPayload(
            BattleSnapshotMessage snapshot,
            out byte[] payloadBytes,
            out int logicalByteCount,
            out string comparisonKey)
        {
            return TryGetBattleSnapshotTransmissionPayloadDescriptor(
                snapshot,
                out payloadBytes,
                out logicalByteCount,
                out comparisonKey,
                out _,
                out _);
        }

        private bool TryGetBattleSnapshotTransmissionPayloadDescriptorV2(
            BattleSnapshotMessage snapshot,
            out byte[] payloadBytes,
            out int logicalByteCount,
            out string comparisonKey,
            out string payloadHash,
            out CoopBattleSnapshotCompressionKind compressionKind,
            out CoopBattleSnapshotPayloadEncoding payloadEncoding)
        {
            payloadBytes = Array.Empty<byte>();
            logicalByteCount = 0;
            payloadHash = string.Empty;
            compressionKind = CoopBattleSnapshotCompressionKind.None;
            payloadEncoding = CoopBattleSnapshotPayloadEncoding.JsonUtf8;
            comparisonKey = BuildBattleSnapshotComparisonKey(snapshot, BattleSnapshotRuntimeState.GetUpdatedUtc());
            if (string.IsNullOrWhiteSpace(comparisonKey))
                return false;

            if (string.Equals(_cachedBattleSnapshotV2ComparisonKey, comparisonKey, StringComparison.Ordinal) &&
                _cachedBattleSnapshotV2PayloadBytes != null &&
                _cachedBattleSnapshotV2PayloadBytes.Length > 0)
            {
                payloadBytes = _cachedBattleSnapshotV2PayloadBytes;
                logicalByteCount = _cachedBattleSnapshotV2LogicalBytes;
                payloadHash = _cachedBattleSnapshotV2PayloadHash;
                compressionKind = _cachedBattleSnapshotV2CompressionKind;
                payloadEncoding = _cachedBattleSnapshotV2PayloadEncoding;
                return true;
            }

            if (!TrySerializeBattleSnapshotPayloadV2(snapshot, out byte[] rawBytes, out payloadEncoding))
                return false;

            byte[] wireBytes = CompressPayload(rawBytes, out bool compressed);
            payloadBytes = wireBytes ?? rawBytes;
            logicalByteCount = rawBytes.Length;
            compressionKind = compressed ? CoopBattleSnapshotCompressionKind.Gzip : CoopBattleSnapshotCompressionKind.None;
            payloadHash = ComputePayloadHash(payloadBytes);

            _cachedBattleSnapshotV2ComparisonKey = comparisonKey;
            _cachedBattleSnapshotV2PayloadBytes = payloadBytes;
            _cachedBattleSnapshotV2LogicalBytes = logicalByteCount;
            _cachedBattleSnapshotV2PayloadHash = payloadHash;
            _cachedBattleSnapshotV2CompressionKind = compressionKind;
            _cachedBattleSnapshotV2PayloadEncoding = payloadEncoding;

            int chunkCount = Math.Max(1, (payloadBytes.Length + CoopBattleSnapshotChunkV2Message.MaxChunkBytes - 1) / CoopBattleSnapshotChunkV2Message.MaxChunkBytes);
            ModLogger.Info(
                "CoopMissionNetworkBridge: prepared V2 battle snapshot transport payload. " +
                "ComparisonKey=" + comparisonKey +
                " Encoding=" + payloadEncoding +
                " RawBytes=" + rawBytes.Length +
                " WireBytes=" + payloadBytes.Length +
                " Compressed=" + compressed +
                " Chunks=" + chunkCount +
                " Entries=" + GetBattleSnapshotEntryCount(snapshot));
            return true;
        }

        private bool TryGetBattleSnapshotTransmissionPayloadDescriptor(
            BattleSnapshotMessage snapshot,
            out byte[] payloadBytes,
            out int logicalByteCount,
            out string comparisonKey,
            out string payloadHash,
            out CoopBattleSnapshotCompressionKind compressionKind)
        {
            payloadBytes = Array.Empty<byte>();
            logicalByteCount = 0;
            payloadHash = string.Empty;
            compressionKind = CoopBattleSnapshotCompressionKind.None;
            comparisonKey = BuildBattleSnapshotComparisonKey(snapshot, BattleSnapshotRuntimeState.GetUpdatedUtc());
            if (string.IsNullOrWhiteSpace(comparisonKey))
                return false;

            if (string.Equals(_cachedBattleSnapshotComparisonKey, comparisonKey, StringComparison.Ordinal) &&
                _cachedBattleSnapshotPayloadBytes != null &&
                _cachedBattleSnapshotPayloadBytes.Length > 0)
            {
                payloadBytes = _cachedBattleSnapshotPayloadBytes;
                logicalByteCount = _cachedBattleSnapshotLogicalBytes;
                payloadHash = _cachedBattleSnapshotPayloadHash;
                compressionKind = _cachedBattleSnapshotCompressionKind;
                return true;
            }

            string payloadJson = SerializePayload(snapshot);
            if (string.IsNullOrWhiteSpace(payloadJson))
                return false;

            byte[] rawBytes = Encoding.UTF8.GetBytes(payloadJson);
            if (rawBytes.Length <= 0)
                return false;

            byte[] wireBytes = CompressPayload(rawBytes, out bool compressed);
            payloadBytes = wireBytes ?? rawBytes;
            logicalByteCount = rawBytes.Length;
            compressionKind = compressed ? CoopBattleSnapshotCompressionKind.Gzip : CoopBattleSnapshotCompressionKind.None;
            payloadHash = ComputePayloadHash(payloadBytes);

            _cachedBattleSnapshotComparisonKey = comparisonKey;
            _cachedBattleSnapshotPayloadBytes = payloadBytes;
            _cachedBattleSnapshotLogicalBytes = logicalByteCount;
            _cachedBattleSnapshotPayloadHash = payloadHash;
            _cachedBattleSnapshotCompressionKind = compressionKind;

            int chunkCount = Math.Max(1, (payloadBytes.Length + CoopBattlePayloadChunkMessage.MaxChunkBytes - 1) / CoopBattlePayloadChunkMessage.MaxChunkBytes);
            ModLogger.Info(
                "CoopMissionNetworkBridge: prepared battle snapshot transport payload. " +
                "ComparisonKey=" + comparisonKey +
                " RawBytes=" + rawBytes.Length +
                " WireBytes=" + payloadBytes.Length +
                " Compressed=" + compressed +
                " Chunks=" + chunkCount +
                " Entries=" + GetBattleSnapshotEntryCount(snapshot));
            return true;
        }

        private static string BuildBattleSnapshotComparisonKey(BattleSnapshotMessage snapshot, DateTime updatedUtc)
        {
            if (snapshot == null)
                return string.Empty;

            string sidesSignature = snapshot.Sides == null
                ? "none"
                : string.Join(",",
                    snapshot.Sides.Select(side =>
                        (side?.SideId ?? "null") + ":" +
                        (side?.TotalManCount ?? 0) + ":" +
                        (side?.Troops?.Count ?? 0)));
            return (snapshot.BattleId ?? "null") +
                   "|" +
                   (snapshot.MapScene ?? "null") +
                   "|" +
                   sidesSignature +
                   "|" +
                   updatedUtc.Ticks;
        }

        private static int GetBattleSnapshotEntryCount(BattleSnapshotMessage snapshot)
        {
            return snapshot?.Sides?.Sum(side => side?.Troops?.Count ?? 0) ?? 0;
        }

        private static byte[] CompressPayload(byte[] rawBytes, out bool compressed)
        {
            compressed = false;
            if (rawBytes == null || rawBytes.Length <= 0)
                return Array.Empty<byte>();

            try
            {
                using (var output = new MemoryStream())
                {
                    using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                    {
                        gzip.Write(rawBytes, 0, rawBytes.Length);
                    }

                    byte[] compressedBytes = output.ToArray();
                    if (compressedBytes.Length > 0 && compressedBytes.Length < rawBytes.Length)
                    {
                        compressed = true;
                        return compressedBytes;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: payload compression failed, falling back to raw transport bytes. Error=" + ex.Message);
            }

            return rawBytes;
        }

        private static string ComputePayloadHash(byte[] payloadBytes)
        {
            if (payloadBytes == null || payloadBytes.Length <= 0)
                return string.Empty;

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(payloadBytes);
                var builder = new StringBuilder(hashBytes.Length * 2);
                for (int i = 0; i < hashBytes.Length; i++)
                    builder.Append(hashBytes[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private static bool TryDecodePayloadJson(CoopBattlePayloadKind payloadKind, byte[] payloadBytes, out string payloadJson)
        {
            payloadJson = string.Empty;
            if (payloadBytes == null || payloadBytes.Length <= 0)
                return false;

            byte[] decodedBytes = payloadBytes;
            if (IsGzipPayload(payloadBytes))
            {
                try
                {
                    using (var input = new MemoryStream(payloadBytes))
                    using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                    using (var output = new MemoryStream())
                    {
                        gzip.CopyTo(output);
                        decodedBytes = output.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Info("CoopMissionNetworkBridge: battle snapshot decompression failed. Error=" + ex.Message);
                    return false;
                }
            }

            payloadJson = decodedBytes.Length <= 0 ? string.Empty : Encoding.UTF8.GetString(decodedBytes);
            return !string.IsNullOrWhiteSpace(payloadJson);
        }

        private static bool TryDecodeBattleSnapshotPayload(
            BattleSnapshotClientAssemblyState assemblyState,
            byte[] payloadBytes,
            out BattleSnapshotMessage snapshot)
        {
            snapshot = null;
            if (assemblyState == null || payloadBytes == null || payloadBytes.Length <= 0)
                return false;

            byte[] decodedBytes = payloadBytes;
            if (assemblyState.CompressionKind == CoopBattleSnapshotCompressionKind.Gzip)
            {
                try
                {
                    using (var input = new MemoryStream(payloadBytes))
                    using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                    using (var output = new MemoryStream())
                    {
                        gzip.CopyTo(output);
                        decodedBytes = output.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Info("CoopMissionNetworkBridge: V2 battle snapshot decompression failed. Error=" + ex.Message);
                    return false;
                }
            }

            if (decodedBytes.Length <= 0)
                return false;

            if (assemblyState.PayloadEncoding == CoopBattleSnapshotPayloadEncoding.BinaryV1)
                return BattleSnapshotBinarySerializer.TryDeserialize(decodedBytes, out snapshot);

            string payloadJson = Encoding.UTF8.GetString(decodedBytes);
            if (string.IsNullOrWhiteSpace(payloadJson))
                return false;

            snapshot = JsonConvert.DeserializeObject<BattleSnapshotMessage>(payloadJson, JsonSettings);
            return snapshot != null;
        }

        private static string BuildChunkRangesString(IEnumerable<ChunkRange> ranges)
        {
            if (ranges == null)
                return string.Empty;

            return string.Join(",",
                ranges
                    .Where(range => range.EndIndex >= range.StartIndex)
                    .Select(range => range.StartIndex == range.EndIndex
                        ? range.StartIndex.ToString()
                        : range.StartIndex + "-" + range.EndIndex));
        }

        private static List<ChunkRange> ParseChunkRanges(string rawValue)
        {
            var ranges = new List<ChunkRange>();
            if (string.IsNullOrWhiteSpace(rawValue))
                return ranges;

            string[] parts = rawValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim();
                if (part.Length <= 0)
                    continue;

                int separatorIndex = part.IndexOf('-');
                if (separatorIndex < 0)
                {
                    if (int.TryParse(part, out int singleIndex))
                        ranges.Add(new ChunkRange(singleIndex, singleIndex));
                    continue;
                }

                string startRaw = part.Substring(0, separatorIndex);
                string endRaw = part.Substring(separatorIndex + 1);
                if (int.TryParse(startRaw, out int startIndex) && int.TryParse(endRaw, out int endIndex))
                    ranges.Add(new ChunkRange(startIndex, endIndex));
            }

            return ranges;
        }

        private static void SendClientBattleSnapshotChunkRequest(
            BattleSnapshotClientAssemblyState assemblyState,
            CoopBattleSnapshotAssemblyStateKind assemblyStateKind,
            string source)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || assemblyState == null)
                return;

            try
            {
                if (!assemblyState.TryGetInitialWindowRange(BattleSnapshotInitialWindowChunks, out int startChunkIndex, out int endChunkIndex))
                    return;

                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopBattleSnapshotChunkRequestMessage(
                    assemblyState.TransmissionId,
                    startChunkIndex,
                    endChunkIndex,
                    assemblyState.HighestContiguousChunkIndex,
                    assemblyState.ReceivedChunkCount,
                    assemblyStateKind));
                GameNetwork.EndModuleEventAsClient();
                assemblyState.MarkInitialWindowRequestSent(startChunkIndex, endChunkIndex, DateTime.UtcNow);
                ModLogger.Info(
                    "CoopMissionNetworkBridge: sent client V2 battle snapshot chunk request. " +
                    "TransmissionId=" + assemblyState.TransmissionId +
                    " Range=" + startChunkIndex + "-" + endChunkIndex +
                    " HighestContiguous=" + assemblyState.HighestContiguousChunkIndex +
                    " ReceivedChunkCount=" + assemblyState.ReceivedChunkCount +
                    " State=" + assemblyStateKind +
                    " Source=" + (source ?? "unknown"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client V2 battle snapshot chunk request send failed. Error=" + ex.Message);
            }
        }

        private static void SendClientBattleSnapshotRangeAck(
            BattleSnapshotClientAssemblyState assemblyState,
            CoopBattleSnapshotAssemblyStateKind assemblyStateKind,
            string source)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || assemblyState == null)
                return;

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopBattleSnapshotRangeAckMessage(
                    assemblyState.TransmissionId,
                    assemblyState.HighestContiguousChunkIndex,
                    assemblyState.ReceivedChunkCount,
                    string.Empty,
                    string.Empty,
                    assemblyStateKind));
                GameNetwork.EndModuleEventAsClient();
                assemblyState.MarkProgressAckSent(DateTime.UtcNow);
                ModLogger.Info(
                    "CoopMissionNetworkBridge: sent client V2 battle snapshot range ack. " +
                    "TransmissionId=" + assemblyState.TransmissionId +
                    " HighestContiguous=" + assemblyState.HighestContiguousChunkIndex +
                    " ReceivedChunkCount=" + assemblyState.ReceivedChunkCount +
                    " State=" + assemblyStateKind +
                    " Source=" + (source ?? "unknown"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client V2 battle snapshot range ack send failed. Error=" + ex.Message);
            }
        }

        private static void SendClientBattleSnapshotCompleteAck(int transmissionId, string payloadHash, bool appliedSuccessfully)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || transmissionId <= 0)
                return;

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopBattleSnapshotCompleteAckMessage(transmissionId, appliedSuccessfully, payloadHash));
                GameNetwork.EndModuleEventAsClient();
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client V2 battle snapshot complete ack send failed. Error=" + ex.Message);
            }
        }

        private static void SendClientBattleSnapshotAbort(int transmissionId, string reason)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || transmissionId <= 0)
                return;

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopBattleSnapshotAbortMessage(transmissionId, reason));
                GameNetwork.EndModuleEventAsClient();
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client V2 battle snapshot abort send failed. Error=" + ex.Message);
            }
        }

        private PendingPayloadTransmission CreateEntryStatusPendingTransmission(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot,
            string comparisonJson)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(comparisonJson))
                return null;

            EntryStatusTransportFieldState transportFieldState = CompactEntryStatusSnapshotForTransport(snapshot);
            try
            {
                string payloadJson = SerializePayload(snapshot);
                if (string.IsNullOrWhiteSpace(payloadJson))
                    return null;

                byte[] rawBytes = Encoding.UTF8.GetBytes(payloadJson);
                if (rawBytes.Length <= 0)
                    return null;

                byte[] wireBytes = CompressPayload(rawBytes, out bool compressed);
                PendingPayloadTransmission transmission = PendingPayloadTransmission.Create(
                    CoopBattlePayloadKind.EntryStatusSnapshot,
                    wireBytes ?? rawBytes,
                    rawBytes.Length,
                    comparisonJson,
                    NextTransmissionId());
                if (transmission == null)
                    return null;

                if (compressed)
                {
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: compressed entry status transport payload. " +
                        "RawBytes=" + rawBytes.Length +
                        " WireBytes=" + transmission.TotalBytes +
                        " Chunks=" + transmission.ChunkCount);
                }

                return transmission;
            }
            finally
            {
                transportFieldState.Restore(snapshot);
            }
        }

        private PendingPayloadTransmission CreateMaterializedAgentEntryPendingTransmission(
            CoopBattleEntryStatusBridgeFile.AuthoritativeMaterializedAgentEntrySnapshot snapshot,
            string comparisonJson)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(comparisonJson))
                return null;

            string payloadJson = SerializePayload(snapshot);
            if (string.IsNullOrWhiteSpace(payloadJson))
                return null;

            byte[] rawBytes = Encoding.UTF8.GetBytes(payloadJson);
            if (rawBytes.Length <= 0)
                return null;

            byte[] wireBytes = CompressPayload(rawBytes, out bool compressed);
            PendingPayloadTransmission transmission = PendingPayloadTransmission.Create(
                CoopBattlePayloadKind.AuthoritativeMaterializedAgentEntrySnapshot,
                wireBytes ?? rawBytes,
                rawBytes.Length,
                comparisonJson,
                NextTransmissionId());
            if (transmission == null)
                return null;

            if (compressed)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: compressed materialized-agent-entry transport payload. " +
                    "RawBytes=" + rawBytes.Length +
                    " WireBytes=" + transmission.TotalBytes +
                    " Chunks=" + transmission.ChunkCount);
            }

            return transmission;
        }

        private static int CountSerializedIdList(string rawValue)
        {
            return CoopBattleEntryStatusBridgeFile.DeserializeIdList(rawValue)?.Length ?? 0;
        }

        private static bool IsGzipPayload(byte[] payloadBytes)
        {
            return payloadBytes != null &&
                   payloadBytes.Length >= 2 &&
                   payloadBytes[0] == 0x1F &&
                   payloadBytes[1] == 0x8B;
        }

        private static EntryStatusTransportFieldState CompactEntryStatusSnapshotForTransport(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
        {
            if (snapshot == null)
                return default(EntryStatusTransportFieldState);

            EntryStatusTransportFieldState state = EntryStatusTransportFieldState.Capture(snapshot);
            snapshot.AllowedTroopIds = string.Empty;
            snapshot.AllowedEntryIds = string.Empty;
            snapshot.AuthoritativeMaterializedAgentEntryCount = 0;
            snapshot.AuthoritativeMaterializedAgentEntries = string.Empty;
            snapshot.AttackerAllowedTroopIds = string.Empty;
            snapshot.AttackerAllowedEntryIds = string.Empty;
            snapshot.AttackerSelectableEntryIds = string.Empty;
            snapshot.DefenderAllowedTroopIds = string.Empty;
            snapshot.DefenderAllowedEntryIds = string.Empty;
            snapshot.DefenderSelectableEntryIds = string.Empty;
            return state;
        }

        private int NextTransmissionId()
        {
            if (_nextTransmissionId >= 1048575)
                _nextTransmissionId = 1;

            return _nextTransmissionId++;
        }

        internal static bool HasPeerAcknowledgedCurrentBattleSnapshot(
            MissionPeer missionPeer,
            out int expectedTransmissionId,
            out int acknowledgedTransmissionId)
        {
            expectedTransmissionId = 0;
            acknowledgedTransmissionId = 0;

            NetworkCommunicator peer = missionPeer?.GetNetworkPeer();
            if (peer == null || peer.IsServerPeer)
                return false;

            _expectedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out expectedTransmissionId);
            _acknowledgedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out acknowledgedTransmissionId);
            return expectedTransmissionId > 0 && acknowledgedTransmissionId >= expectedTransmissionId;
        }

        internal static bool IsPeerCurrentBattleSnapshotBootstrapReady(
            NetworkCommunicator peer,
            out string readinessSummary)
        {
            readinessSummary = string.Empty;
            if (!GameNetwork.IsServer)
            {
                readinessSummary = "not-server";
                return true;
            }

            if (peer == null)
            {
                readinessSummary = "peer-null";
                return false;
            }

            if (peer.IsServerPeer)
            {
                readinessSummary = "server-peer";
                return true;
            }

            _expectedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out int expectedTransmissionId);
            _acknowledgedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out int acknowledgedTransmissionId);
            bool snapshotReady =
                expectedTransmissionId > 0 &&
                acknowledgedTransmissionId >= expectedTransmissionId;
            readinessSummary =
                "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                " ExpectedTransmissionId=" + expectedTransmissionId +
                " AcknowledgedTransmissionId=" + acknowledgedTransmissionId +
                " SnapshotReady=" + snapshotReady;
            return snapshotReady;
        }

        internal static bool HaveAllEligiblePeersAcknowledgedCurrentBattleSnapshot(
            Mission mission,
            out string readinessSummary)
        {
            readinessSummary = string.Empty;
            if (!GameNetwork.IsServer)
            {
                readinessSummary = "not-server";
                return true;
            }

            if (mission == null)
            {
                readinessSummary = "mission-null";
                return false;
            }

            int eligiblePeerCount = 0;
            int acknowledgedPeerCount = 0;
            string blockingPeer = "none";
            int blockingExpectedTransmissionId = 0;
            int blockingAcknowledgedTransmissionId = 0;

            if (GameNetwork.NetworkPeers != null)
            {
                foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
                {
                    if (!IsBattleSnapshotBootstrapEligiblePeer(peer))
                        continue;

                    eligiblePeerCount++;
                    _expectedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out int expectedTransmissionId);
                    _acknowledgedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out int acknowledgedTransmissionId);
                    bool acknowledged =
                        expectedTransmissionId > 0 &&
                        acknowledgedTransmissionId >= expectedTransmissionId;
                    if (acknowledged)
                    {
                        acknowledgedPeerCount++;
                        continue;
                    }

                    if (!string.Equals(blockingPeer, "none", StringComparison.Ordinal))
                        continue;

                    blockingPeer = peer.UserName ?? peer.Index.ToString();
                    blockingExpectedTransmissionId = expectedTransmissionId;
                    blockingAcknowledgedTransmissionId = acknowledgedTransmissionId;
                }
            }

            readinessSummary =
                "EligiblePeers=" + eligiblePeerCount +
                " AcknowledgedPeers=" + acknowledgedPeerCount +
                " BlockingPeer=" + blockingPeer +
                " ExpectedTransmissionId=" + blockingExpectedTransmissionId +
                " AcknowledgedTransmissionId=" + blockingAcknowledgedTransmissionId;
            return eligiblePeerCount <= 0 || acknowledgedPeerCount >= eligiblePeerCount;
        }

        internal static bool IsClientCurrentBattleSnapshotApplied(out string readinessSummary)
        {
            readinessSummary = string.Empty;

            if (!GameNetwork.IsClient)
            {
                readinessSummary = "not-client";
                return true;
            }

            Mission mission = Mission.Current;
            if (mission == null)
            {
                readinessSummary = "mission-null";
                return false;
            }

            CoopMissionNetworkBridge bridge = mission.GetMissionBehavior<CoopMissionNetworkBridge>();
            if (bridge == null)
            {
                readinessSummary = "bridge-null";
                return false;
            }

            int observedTransmissionId = _clientObservedBattleSnapshotTransmissionId;
            string observedPayloadHash = _clientObservedBattleSnapshotPayloadHash ?? string.Empty;
            int appliedTransmissionId = _clientAppliedBattleSnapshotTransmissionId;
            string appliedPayloadHash = _clientAppliedBattleSnapshotPayloadHash ?? string.Empty;
            bool hasPendingObservedAssembly =
                observedTransmissionId > 0 &&
                bridge._clientBattleSnapshotAssembliesByTransmission.TryGetValue(
                    observedTransmissionId,
                    out BattleSnapshotClientAssemblyState observedAssemblyState) &&
                observedAssemblyState != null;
            bool applied =
                observedTransmissionId > 0 &&
                appliedTransmissionId == observedTransmissionId &&
                string.Equals(appliedPayloadHash, observedPayloadHash, StringComparison.Ordinal) &&
                !hasPendingObservedAssembly;

            readinessSummary =
                "ObservedTransmissionId=" + observedTransmissionId +
                " AppliedTransmissionId=" + appliedTransmissionId +
                " ObservedPayloadHash=" + (string.IsNullOrWhiteSpace(observedPayloadHash) ? "null" : observedPayloadHash) +
                " AppliedPayloadHash=" + (string.IsNullOrWhiteSpace(appliedPayloadHash) ? "null" : appliedPayloadHash) +
                " HasPendingObservedAssembly=" + hasPendingObservedAssembly;
            return applied;
        }

        private static void RegisterExpectedBattleSnapshotTransmission(int peerIndex, int transmissionId)
        {
            if (peerIndex < 0 || transmissionId <= 0)
                return;

            _expectedBattleSnapshotTransmissionIdByPeer[peerIndex] = transmissionId;
        }

        private static void ClearPeerBattleSnapshotSyncState(int peerIndex)
        {
            if (peerIndex < 0)
                return;

            _expectedBattleSnapshotTransmissionIdByPeer.Remove(peerIndex);
            _acknowledgedBattleSnapshotTransmissionIdByPeer.Remove(peerIndex);
        }

        private bool TryAcknowledgePeerBattleSnapshot(NetworkCommunicator peer, string rawTransmissionId)
        {
            if (peer == null || string.IsNullOrWhiteSpace(rawTransmissionId) || !int.TryParse(rawTransmissionId, out int transmissionId))
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: rejected battle snapshot readiness ack. " +
                    "Peer=" + (peer?.UserName ?? "null") +
                    " RawTransmissionId=" + (rawTransmissionId ?? string.Empty));
                return false;
            }

            _acknowledgedBattleSnapshotTransmissionIdByPeer[peer.Index] = transmissionId;
            _expectedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out int expectedTransmissionId);
            bool snapshotReady = expectedTransmissionId > 0 && transmissionId >= expectedTransmissionId;
            if (snapshotReady)
            {
                _lastCompletedBattleSnapshotTransmissionUtcByPeer.Remove(peer.Index);
                _lastBattleSnapshotRetryUtcByPeer.Remove(peer.Index);
            }
            ModLogger.Info(
                "CoopMissionNetworkBridge: acknowledged client battle snapshot readiness. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + transmissionId +
                " ExpectedTransmissionId=" + expectedTransmissionId +
                " SnapshotReady=" + snapshotReady);
            return true;
        }

        private bool TrySerializeBattleSnapshotPayloadV2(
            BattleSnapshotMessage snapshot,
            out byte[] rawBytes,
            out CoopBattleSnapshotPayloadEncoding payloadEncoding)
        {
            rawBytes = Array.Empty<byte>();
            payloadEncoding = CoopBattleSnapshotPayloadEncoding.JsonUtf8;

            if (BattleSnapshotBinarySerializer.TrySerialize(snapshot, out rawBytes) &&
                rawBytes != null &&
                rawBytes.Length > 0)
            {
                payloadEncoding = CoopBattleSnapshotPayloadEncoding.BinaryV1;
                return true;
            }

            string payloadJson = SerializePayload(snapshot);
            if (string.IsNullOrWhiteSpace(payloadJson))
                return false;

            rawBytes = Encoding.UTF8.GetBytes(payloadJson);
            payloadEncoding = CoopBattleSnapshotPayloadEncoding.JsonUtf8;
            ModLogger.Info("CoopMissionNetworkBridge: falling back to JSON V2 battle snapshot payload encoding.");
            return rawBytes.Length > 0;
        }

        private static void ObserveClientBattleSnapshotManifest(int transmissionId, string payloadHash)
        {
            string normalizedPayloadHash = payloadHash ?? string.Empty;
            bool observedSignatureChanged =
                _clientObservedBattleSnapshotTransmissionId != transmissionId ||
                !string.Equals(_clientObservedBattleSnapshotPayloadHash, normalizedPayloadHash, StringComparison.Ordinal);
            _clientObservedBattleSnapshotTransmissionId = transmissionId;
            _clientObservedBattleSnapshotPayloadHash = normalizedPayloadHash;
            if (!observedSignatureChanged)
                return;

            _clientAppliedBattleSnapshotTransmissionId = 0;
            _clientAppliedBattleSnapshotPayloadHash = string.Empty;
        }

        private static void MarkClientBattleSnapshotApplied(int transmissionId, string payloadHash)
        {
            _clientAppliedBattleSnapshotTransmissionId = transmissionId;
            _clientAppliedBattleSnapshotPayloadHash = payloadHash ?? string.Empty;
        }

        private static void ClearClientBattleSnapshotApplicationState(string source)
        {
            _clientObservedBattleSnapshotTransmissionId = 0;
            _clientObservedBattleSnapshotPayloadHash = string.Empty;
            _clientAppliedBattleSnapshotTransmissionId = 0;
            _clientAppliedBattleSnapshotPayloadHash = string.Empty;
            BattleMapSpawnHandoffPatch.ClearDeferredClientMountedHeroCreateAgents(
                (source ?? "CoopMissionNetworkBridge.ClearClientBattleSnapshotApplicationState") + " deferred-mounted-hero-clear");
            BattleSnapshotRuntimeState.Clear(source ?? "CoopMissionNetworkBridge.ClearClientBattleSnapshotApplicationState");
        }

        private static bool IsEligibleRemotePeer(NetworkCommunicator peer)
        {
            return peer != null &&
                !peer.IsServerPeer &&
                peer.IsConnectionActive &&
                peer.IsSynchronized;
        }

        private static bool IsBattleSnapshotBootstrapEligiblePeer(NetworkCommunicator peer)
        {
            return IsBattleSnapshotBootstrapEligiblePeer(peer, allowUnsynchronizedPeer: false);
        }

        private static bool IsBattleSnapshotBootstrapEligiblePeer(NetworkCommunicator peer, bool allowUnsynchronizedPeer)
        {
            return peer != null &&
                !peer.IsServerPeer &&
                peer.IsConnectionActive &&
                (peer.IsSynchronized || allowUnsynchronizedPeer);
        }

        private static string BuildAssemblyKey(CoopBattlePayloadKind payloadKind, int transmissionId)
        {
            return ((int)payloadKind) + "|" + transmissionId;
        }

        private static string BuildPendingTransmissionKey(int peerIndex, CoopBattlePayloadKind payloadKind)
        {
            return peerIndex + "|" + (int)payloadKind;
        }

        private sealed class PayloadAssemblyState
        {
            public PayloadAssemblyState(CoopBattlePayloadKind payloadKind, int transmissionId, int chunkCount)
            {
                PayloadKind = payloadKind;
                TransmissionId = transmissionId;
                ChunkCount = Math.Max(1, chunkCount);
                Chunks = new byte[ChunkCount][];
                ReceivedChunkCount = 0;
            }

            public CoopBattlePayloadKind PayloadKind { get; }
            public int TransmissionId { get; }
            public int ChunkCount { get; }
            public int ReceivedChunkCount { get; set; }
            public byte[][] Chunks { get; }

            public byte[] Combine()
            {
                int totalBytes = Chunks.Where(chunk => chunk != null).Sum(chunk => chunk.Length);
                byte[] combined = totalBytes > 0 ? new byte[totalBytes] : Array.Empty<byte>();
                int offset = 0;
                for (int i = 0; i < Chunks.Length; i++)
                {
                    byte[] chunk = Chunks[i];
                    if (chunk == null || chunk.Length <= 0)
                        continue;

                    Buffer.BlockCopy(chunk, 0, combined, offset, chunk.Length);
                    offset += chunk.Length;
                }

                return combined;
            }
        }

        private readonly struct EntryStatusTransportFieldState
        {
            private EntryStatusTransportFieldState(
                string allowedTroopIds,
                string allowedEntryIds,
                int authoritativeMaterializedAgentEntryCount,
                string authoritativeMaterializedAgentEntries,
                string attackerAllowedTroopIds,
                string attackerAllowedEntryIds,
                string attackerSelectableEntryIds,
                string defenderAllowedTroopIds,
                string defenderAllowedEntryIds,
                string defenderSelectableEntryIds)
            {
                AllowedTroopIds = allowedTroopIds;
                AllowedEntryIds = allowedEntryIds;
                AuthoritativeMaterializedAgentEntryCount = authoritativeMaterializedAgentEntryCount;
                AuthoritativeMaterializedAgentEntries = authoritativeMaterializedAgentEntries;
                AttackerAllowedTroopIds = attackerAllowedTroopIds;
                AttackerAllowedEntryIds = attackerAllowedEntryIds;
                AttackerSelectableEntryIds = attackerSelectableEntryIds;
                DefenderAllowedTroopIds = defenderAllowedTroopIds;
                DefenderAllowedEntryIds = defenderAllowedEntryIds;
                DefenderSelectableEntryIds = defenderSelectableEntryIds;
            }

            public string AllowedTroopIds { get; }

            public string AllowedEntryIds { get; }

            public int AuthoritativeMaterializedAgentEntryCount { get; }

            public string AuthoritativeMaterializedAgentEntries { get; }

            public string AttackerAllowedTroopIds { get; }

            public string AttackerAllowedEntryIds { get; }

            public string AttackerSelectableEntryIds { get; }

            public string DefenderAllowedTroopIds { get; }

            public string DefenderAllowedEntryIds { get; }

            public string DefenderSelectableEntryIds { get; }

            public static EntryStatusTransportFieldState Capture(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
            {
                return snapshot == null
                    ? default(EntryStatusTransportFieldState)
                    : new EntryStatusTransportFieldState(
                        snapshot.AllowedTroopIds,
                        snapshot.AllowedEntryIds,
                        snapshot.AuthoritativeMaterializedAgentEntryCount,
                        snapshot.AuthoritativeMaterializedAgentEntries,
                        snapshot.AttackerAllowedTroopIds,
                        snapshot.AttackerAllowedEntryIds,
                        snapshot.AttackerSelectableEntryIds,
                        snapshot.DefenderAllowedTroopIds,
                        snapshot.DefenderAllowedEntryIds,
                        snapshot.DefenderSelectableEntryIds);
            }

            public void Restore(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
            {
                if (snapshot == null)
                    return;

                snapshot.AllowedTroopIds = AllowedTroopIds;
                snapshot.AllowedEntryIds = AllowedEntryIds;
                snapshot.AuthoritativeMaterializedAgentEntryCount = AuthoritativeMaterializedAgentEntryCount;
                snapshot.AuthoritativeMaterializedAgentEntries = AuthoritativeMaterializedAgentEntries;
                snapshot.AttackerAllowedTroopIds = AttackerAllowedTroopIds;
                snapshot.AttackerAllowedEntryIds = AttackerAllowedEntryIds;
                snapshot.AttackerSelectableEntryIds = AttackerSelectableEntryIds;
                snapshot.DefenderAllowedTroopIds = DefenderAllowedTroopIds;
                snapshot.DefenderAllowedEntryIds = DefenderAllowedEntryIds;
                snapshot.DefenderSelectableEntryIds = DefenderSelectableEntryIds;
            }
        }

        private sealed class PendingPayloadTransmission
        {
            private PendingPayloadTransmission(
                CoopBattlePayloadKind payloadKind,
                int transmissionId,
                int logicalBytes,
                string comparisonKey,
                byte[][] chunks,
                int totalBytes)
            {
                PayloadKind = payloadKind;
                TransmissionId = transmissionId;
                LogicalBytes = Math.Max(0, logicalBytes);
                ComparisonKey = comparisonKey ?? string.Empty;
                Chunks = chunks ?? Array.Empty<byte[]>();
                TotalBytes = Math.Max(0, totalBytes);
                NextChunkIndex = 0;
            }

            public CoopBattlePayloadKind PayloadKind { get; }
            public int TransmissionId { get; }
            public int LogicalBytes { get; }
            public string ComparisonKey { get; }
            public byte[][] Chunks { get; }
            public int TotalBytes { get; }
            public int NextChunkIndex { get; set; }
            public int ChunkCount => Chunks.Length;
            public bool IsCompleted => NextChunkIndex >= ChunkCount;

            public static PendingPayloadTransmission Create(
                CoopBattlePayloadKind payloadKind,
                byte[] payloadBytes,
                int logicalByteCount,
                string comparisonKey,
                int transmissionId)
            {
                if (payloadBytes == null || payloadBytes.Length <= 0)
                    return null;

                int chunkCount = Math.Max(1, (payloadBytes.Length + CoopBattlePayloadChunkMessage.MaxChunkBytes - 1) / CoopBattlePayloadChunkMessage.MaxChunkBytes);
                if (chunkCount > CoopBattlePayloadChunkMessage.MaxChunkCount)
                {
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: payload too large for staged chunk transport. " +
                        "Kind=" + payloadKind +
                        " Bytes=" + payloadBytes.Length +
                        " Chunks=" + chunkCount +
                        " ChunkBytes=" + CoopBattlePayloadChunkMessage.MaxChunkBytes);
                    return null;
                }

                byte[][] chunks = new byte[chunkCount][];
                for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                {
                    int chunkOffset = chunkIndex * CoopBattlePayloadChunkMessage.MaxChunkBytes;
                    int chunkLength = Math.Min(CoopBattlePayloadChunkMessage.MaxChunkBytes, payloadBytes.Length - chunkOffset);
                    if (chunkLength < 0)
                        chunkLength = 0;

                    byte[] chunkBytes = chunkLength > 0 ? new byte[chunkLength] : Array.Empty<byte>();
                    if (chunkLength > 0)
                        Buffer.BlockCopy(payloadBytes, chunkOffset, chunkBytes, 0, chunkLength);
                    chunks[chunkIndex] = chunkBytes;
                }

                return new PendingPayloadTransmission(
                    payloadKind,
                    transmissionId,
                    logicalByteCount,
                    comparisonKey,
                    chunks,
                    payloadBytes.Length);
            }
        }

        private readonly struct ChunkRange
        {
            public ChunkRange(int startIndex, int endIndex)
            {
                StartIndex = startIndex;
                EndIndex = endIndex;
            }

            public int StartIndex { get; }
            public int EndIndex { get; }
        }

        private sealed class BattleSnapshotTransportState
        {
            private readonly int _windowChunkCount;

            private BattleSnapshotTransportState(
                int peerIndex,
                int transmissionId,
                int logicalBytes,
                string comparisonKey,
                string payloadHash,
                CoopBattleSnapshotCompressionKind compressionKind,
                CoopBattleSnapshotPayloadEncoding payloadEncoding,
                byte[][] chunks,
                int totalBytes,
                int initialWindowChunks,
                int maxInflightChunks)
            {
                PeerIndex = peerIndex;
                TransmissionId = transmissionId;
                LogicalBytes = logicalBytes;
                ComparisonKey = comparisonKey ?? string.Empty;
                PayloadHash = payloadHash ?? string.Empty;
                CompressionKind = compressionKind;
                PayloadEncoding = payloadEncoding;
                Chunks = chunks ?? Array.Empty<byte[]>();
                TotalBytes = totalBytes;
                SentChunkFlags = new bool[Chunks.Length];
                CreatedUtc = DateTime.UtcNow;
                LastProgressUtc = CreatedUtc;
                HighestClientContiguousChunkIndex = -1;
                LastRequestedStartChunkIndex = -1;
                LastRequestedEndChunkIndex = -1;
                NextChunkToSendIndex = -1;
                _windowChunkCount = Math.Max(1, initialWindowChunks);
            }

            public int PeerIndex { get; }
            public int TransmissionId { get; }
            public int LogicalBytes { get; }
            public string ComparisonKey { get; }
            public string PayloadHash { get; }
            public CoopBattleSnapshotCompressionKind CompressionKind { get; }
            public CoopBattleSnapshotPayloadEncoding PayloadEncoding { get; }
            public byte[][] Chunks { get; }
            public int TotalBytes { get; }
            public int ChunkCount => Chunks.Length;
            public bool[] SentChunkFlags { get; }
            public int SentChunkCount { get; private set; }
            public int HighestClientContiguousChunkIndex { get; private set; }
            public int ClientReceivedChunkCount { get; private set; }
            public int LastRequestedStartChunkIndex { get; private set; }
            public int LastRequestedEndChunkIndex { get; private set; }
            public int ActiveWindowStartChunkIndex => LastRequestedStartChunkIndex;
            public int ActiveWindowEndChunkIndex => LastRequestedEndChunkIndex;
            public int NextChunkToSendIndex { get; private set; }
            public DateTime CreatedUtc { get; }
            public DateTime LastManifestSentUtc { get; private set; }
            public DateTime LastChunkSentUtc { get; private set; }
            public DateTime LastProgressUtc { get; private set; }
            public DateTime LastClientRequestUtc { get; private set; }
            public bool ManifestSent { get; private set; }
            public bool CompleteAckReceived { get; private set; }
            public bool AppliedSuccessfully { get; private set; }
            public bool HasObservedClientRequest => LastClientRequestUtc != DateTime.MinValue;
            public bool HasActiveWindow => LastRequestedStartChunkIndex >= 0 &&
                                           LastRequestedEndChunkIndex >= LastRequestedStartChunkIndex;
            public bool IsActiveWindowSatisfiedByClient => HasActiveWindow &&
                                                           HighestClientContiguousChunkIndex >= LastRequestedEndChunkIndex;
            public bool HasPendingChunkRequests => HasActiveWindow && !IsActiveWindowSatisfiedByClient;
            public int PendingRequestedChunkCount => CanSendActiveWindowChunks
                ? Math.Max(0, LastRequestedEndChunkIndex - NextChunkToSendIndex + 1)
                : 0;
            public bool IsCompleted => CompleteAckReceived;
            public bool CanSendActiveWindowChunks => !IsCompleted &&
                                                     ManifestSent &&
                                                     HasActiveWindow &&
                                                     NextChunkToSendIndex >= LastRequestedStartChunkIndex &&
                                                     NextChunkToSendIndex <= LastRequestedEndChunkIndex;

            public static BattleSnapshotTransportState Create(
                int peerIndex,
                byte[] payloadBytes,
                int logicalByteCount,
                string comparisonKey,
                string payloadHash,
                CoopBattleSnapshotCompressionKind compressionKind,
                CoopBattleSnapshotPayloadEncoding payloadEncoding,
                int transmissionId,
                int initialWindowChunks,
                int maxInflightChunks)
            {
                if (payloadBytes == null || payloadBytes.Length <= 0)
                    return null;

                int chunkCount = Math.Max(1, (payloadBytes.Length + CoopBattleSnapshotChunkV2Message.MaxChunkBytes - 1) / CoopBattleSnapshotChunkV2Message.MaxChunkBytes);
                if (chunkCount > CoopBattleSnapshotChunkV2Message.MaxChunkCount)
                {
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: V2 battle snapshot payload too large for chunk transport. " +
                        "PeerIndex=" + peerIndex +
                        " Bytes=" + payloadBytes.Length +
                        " Chunks=" + chunkCount);
                    return null;
                }

                byte[][] chunks = new byte[chunkCount][];
                for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                {
                    int chunkOffset = chunkIndex * CoopBattleSnapshotChunkV2Message.MaxChunkBytes;
                    int chunkLength = Math.Min(CoopBattleSnapshotChunkV2Message.MaxChunkBytes, payloadBytes.Length - chunkOffset);
                    byte[] chunkBytes = chunkLength > 0 ? new byte[chunkLength] : Array.Empty<byte>();
                    if (chunkLength > 0)
                        Buffer.BlockCopy(payloadBytes, chunkOffset, chunkBytes, 0, chunkLength);
                    chunks[chunkIndex] = chunkBytes;
                }

                return new BattleSnapshotTransportState(
                    peerIndex,
                    transmissionId,
                    logicalByteCount,
                    comparisonKey,
                    payloadHash,
                    compressionKind,
                    payloadEncoding,
                    chunks,
                    payloadBytes.Length,
                    initialWindowChunks,
                    maxInflightChunks);
            }

            public void MarkManifestSent(DateTime nowUtc)
            {
                ManifestSent = true;
                LastManifestSentUtc = nowUtc;
            }

            public bool TryPrimeInitialActiveWindow(DateTime nowUtc)
            {
                if (IsCompleted || HasActiveWindow || HasObservedClientRequest || ChunkCount <= 0)
                    return false;

                int initialStartChunkIndex = 0;
                int initialEndChunkIndex = Math.Min(ChunkCount - 1, initialStartChunkIndex + _windowChunkCount - 1);
                if (initialEndChunkIndex < initialStartChunkIndex)
                    return false;

                SetActiveWindow(initialStartChunkIndex, initialEndChunkIndex, nowUtc);
                return true;
            }

            public void MarkChunkSent(int chunkIndex, DateTime nowUtc)
            {
                if (chunkIndex < 0 || chunkIndex >= ChunkCount)
                    return;

                if (!SentChunkFlags[chunkIndex])
                {
                    SentChunkFlags[chunkIndex] = true;
                    SentChunkCount++;
                }

                LastChunkSentUtc = nowUtc;
                if (chunkIndex == NextChunkToSendIndex)
                    NextChunkToSendIndex++;
            }

            public void ObserveClientChunkRequest(
                int startChunkIndex,
                int endChunkIndex,
                int highestContiguousChunkIndex,
                int receivedChunkCount,
                DateTime nowUtc)
            {
                LastClientRequestUtc = nowUtc;
                ObserveClientProgressAck(highestContiguousChunkIndex, receivedChunkCount, nowUtc);

                int clampedStart = Math.Max(0, startChunkIndex);
                int clampedEnd = Math.Min(ChunkCount - 1, endChunkIndex);
                if (ChunkCount <= 0 || clampedEnd < clampedStart)
                    return;

                if (!HasActiveWindow)
                {
                    SetActiveWindow(clampedStart, clampedEnd, nowUtc);
                    return;
                }

                bool overlapsActiveWindow =
                    clampedStart <= LastRequestedEndChunkIndex &&
                    clampedEnd >= LastRequestedStartChunkIndex;
                if (overlapsActiveWindow)
                {
                    RewindActiveWindowForResend(nowUtc);
                    return;
                }

                if (HighestClientContiguousChunkIndex >= LastRequestedEndChunkIndex)
                    SetActiveWindow(clampedStart, clampedEnd, nowUtc);
            }

            public void ObserveClientProgressAck(
                int highestContiguousChunkIndex,
                int receivedChunkCount,
                DateTime nowUtc)
            {
                HighestClientContiguousChunkIndex = Math.Max(
                    HighestClientContiguousChunkIndex,
                    Math.Min(ChunkCount - 1, highestContiguousChunkIndex));
                ClientReceivedChunkCount = Math.Max(
                    ClientReceivedChunkCount,
                    Math.Min(ChunkCount, Math.Max(0, receivedChunkCount)));
                LastProgressUtc = nowUtc;
            }

            public bool TryAdvanceToNextWindow()
            {
                if (!HasActiveWindow || !IsActiveWindowSatisfiedByClient)
                    return false;

                if (LastRequestedEndChunkIndex >= ChunkCount - 1)
                {
                    NextChunkToSendIndex = -1;
                    return false;
                }

                int nextStartChunkIndex = LastRequestedEndChunkIndex + 1;
                int nextEndChunkIndex = Math.Min(ChunkCount - 1, nextStartChunkIndex + _windowChunkCount - 1);
                SetActiveWindow(nextStartChunkIndex, nextEndChunkIndex, DateTime.UtcNow);
                return true;
            }

            public bool ShouldResendActiveWindow(DateTime nowUtc, TimeSpan stallDelay)
            {
                if (!HasActiveWindow || IsCompleted || IsActiveWindowSatisfiedByClient)
                    return false;

                if (CanSendActiveWindowChunks)
                    return false;

                if (LastChunkSentUtc == DateTime.MinValue)
                    return true;

                return nowUtc - LastChunkSentUtc >= stallDelay &&
                       nowUtc - LastProgressUtc >= stallDelay;
            }

            public void RewindActiveWindowForResend(DateTime nowUtc)
            {
                if (!HasActiveWindow)
                    return;

                NextChunkToSendIndex = Math.Max(LastRequestedStartChunkIndex, HighestClientContiguousChunkIndex + 1);
                if (NextChunkToSendIndex > LastRequestedEndChunkIndex)
                    NextChunkToSendIndex = LastRequestedStartChunkIndex;

                LastClientRequestUtc = nowUtc;
                LastChunkSentUtc = DateTime.MinValue;
            }

            public bool TryGetNextActiveWindowChunkToSend(out int chunkIndex)
            {
                if (!CanSendActiveWindowChunks)
                {
                    chunkIndex = -1;
                    return false;
                }

                chunkIndex = NextChunkToSendIndex;
                return chunkIndex >= LastRequestedStartChunkIndex &&
                       chunkIndex <= LastRequestedEndChunkIndex;
            }

            private void SetActiveWindow(int startChunkIndex, int endChunkIndex, DateTime nowUtc)
            {
                LastRequestedStartChunkIndex = startChunkIndex;
                LastRequestedEndChunkIndex = endChunkIndex;
                NextChunkToSendIndex = Math.Max(startChunkIndex, HighestClientContiguousChunkIndex + 1);
                if (NextChunkToSendIndex > endChunkIndex)
                    NextChunkToSendIndex = startChunkIndex;
                LastProgressUtc = nowUtc;
            }

            public void MarkCompleted(bool appliedSuccessfully, DateTime nowUtc)
            {
                CompleteAckReceived = true;
                AppliedSuccessfully = appliedSuccessfully;
                LastProgressUtc = nowUtc;
            }

            public void ResetForRestart(DateTime nowUtc)
            {
                ManifestSent = false;
                LastManifestSentUtc = DateTime.MinValue;
                LastChunkSentUtc = DateTime.MinValue;
                LastProgressUtc = nowUtc;
                SentChunkCount = 0;
                HighestClientContiguousChunkIndex = -1;
                ClientReceivedChunkCount = 0;
                LastRequestedStartChunkIndex = -1;
                LastRequestedEndChunkIndex = -1;
                NextChunkToSendIndex = -1;
                LastClientRequestUtc = DateTime.MinValue;
                CompleteAckReceived = false;
                AppliedSuccessfully = false;
                Array.Clear(SentChunkFlags, 0, SentChunkFlags.Length);
            }
        }

        private sealed class BattleSnapshotClientAssemblyState
        {
            public BattleSnapshotClientAssemblyState(
                int transmissionId,
                int chunkCount,
                int logicalBytes,
                int wireBytes,
                string comparisonKey,
                string payloadHash,
                CoopBattleSnapshotPayloadEncoding payloadEncoding,
                CoopBattleSnapshotCompressionKind compressionKind)
            {
                TransmissionId = transmissionId;
                ChunkCount = Math.Max(1, chunkCount);
                LogicalBytes = Math.Max(0, logicalBytes);
                WireBytes = Math.Max(0, wireBytes);
                ComparisonKey = comparisonKey ?? string.Empty;
                PayloadHash = payloadHash ?? string.Empty;
                PayloadEncoding = payloadEncoding;
                CompressionKind = compressionKind;
                Chunks = new byte[ChunkCount][];
                ReceivedChunkFlags = new bool[ChunkCount];
                CreatedUtc = DateTime.UtcNow;
                LastManifestObservedUtc = CreatedUtc;
                LastChunkReceivedUtc = CreatedUtc;
                LastUsefulChunkReceivedUtc = CreatedUtc;
                HighestContiguousChunkIndex = -1;
                HighestObservedChunkIndex = -1;
                LastRequestedStartChunkIndex = -1;
                LastRequestedEndChunkIndex = -1;
                LastConfirmedWindowEndChunkIndex = -1;
            }

            public int TransmissionId { get; }
            public int ChunkCount { get; }
            public int LogicalBytes { get; }
            public int WireBytes { get; }
            public string ComparisonKey { get; }
            public string PayloadHash { get; }
            public CoopBattleSnapshotPayloadEncoding PayloadEncoding { get; }
            public CoopBattleSnapshotCompressionKind CompressionKind { get; }
            public byte[][] Chunks { get; }
            public bool[] ReceivedChunkFlags { get; }
            public int ReceivedChunkCount { get; private set; }
            public int HighestContiguousChunkIndex { get; private set; }
            public int HighestObservedChunkIndex { get; private set; }
            public DateTime CreatedUtc { get; }
            public DateTime LastManifestObservedUtc { get; private set; }
            public DateTime LastChunkReceivedUtc { get; private set; }
            public DateTime LastUsefulChunkReceivedUtc { get; private set; }
            public DateTime LastControlMessageSentUtc { get; private set; }
            public int LastRequestedStartChunkIndex { get; private set; }
            public int LastRequestedEndChunkIndex { get; private set; }
            public int LastConfirmedWindowEndChunkIndex { get; private set; }
            public bool IsComplete => ReceivedChunkCount >= ChunkCount;

            public void MarkManifestObserved(DateTime nowUtc)
            {
                LastManifestObservedUtc = nowUtc;
            }

            public void AcceptChunk(int chunkIndex, byte[] payloadBytes, DateTime nowUtc)
            {
                if (chunkIndex < 0 || chunkIndex >= ChunkCount)
                    return;

                LastChunkReceivedUtc = nowUtc;
                if (!ReceivedChunkFlags[chunkIndex])
                {
                    ReceivedChunkFlags[chunkIndex] = true;
                    ReceivedChunkCount++;
                    LastUsefulChunkReceivedUtc = nowUtc;
                }

                Chunks[chunkIndex] = payloadBytes ?? Array.Empty<byte>();
                if (chunkIndex > HighestObservedChunkIndex)
                    HighestObservedChunkIndex = chunkIndex;
                UpdateHighestContiguousChunkIndex();
            }

            public bool TryGetInitialWindowRange(int requestWindowChunks, out int startChunkIndex, out int endChunkIndex)
            {
                startChunkIndex = -1;
                endChunkIndex = -1;
                if (IsComplete || ChunkCount <= 0)
                    return false;

                int clampedWindowSize = Math.Max(1, requestWindowChunks);
                startChunkIndex = 0;
                endChunkIndex = Math.Min(ChunkCount - 1, clampedWindowSize - 1);
                return endChunkIndex >= startChunkIndex;
            }

            public bool TryGetCompletedWindowEndChunkIndex(int requestWindowChunks, out int completedWindowEndChunkIndex)
            {
                completedWindowEndChunkIndex = -1;
                if (IsComplete || ChunkCount <= 0)
                    return false;

                int clampedWindowSize = Math.Max(1, requestWindowChunks);
                int nextWindowStartChunkIndex = LastConfirmedWindowEndChunkIndex + 1;
                if (nextWindowStartChunkIndex >= ChunkCount)
                    return false;

                int nextWindowEndChunkIndex = Math.Min(ChunkCount - 1, nextWindowStartChunkIndex + clampedWindowSize - 1);
                if (HighestContiguousChunkIndex < nextWindowEndChunkIndex)
                    return false;

                completedWindowEndChunkIndex = nextWindowEndChunkIndex;
                return true;
            }

            public void MarkInitialWindowRequestSent(int startChunkIndex, int endChunkIndex, DateTime nowUtc)
            {
                LastRequestedStartChunkIndex = startChunkIndex;
                LastRequestedEndChunkIndex = endChunkIndex;
                LastControlMessageSentUtc = nowUtc;
            }

            public void MarkWindowCompletionAcknowledged(int completedWindowEndChunkIndex, DateTime nowUtc)
            {
                LastConfirmedWindowEndChunkIndex = Math.Max(LastConfirmedWindowEndChunkIndex, completedWindowEndChunkIndex);
                LastControlMessageSentUtc = nowUtc;
            }

            public void MarkProgressAckSent(DateTime nowUtc)
            {
                LastControlMessageSentUtc = nowUtc;
            }

            public byte[] Combine()
            {
                int totalBytes = Chunks.Where(chunk => chunk != null).Sum(chunk => chunk.Length);
                byte[] combined = totalBytes > 0 ? new byte[totalBytes] : Array.Empty<byte>();
                int offset = 0;
                for (int i = 0; i < Chunks.Length; i++)
                {
                    byte[] chunk = Chunks[i];
                    if (chunk == null || chunk.Length <= 0)
                        continue;

                    Buffer.BlockCopy(chunk, 0, combined, offset, chunk.Length);
                    offset += chunk.Length;
                }

                return combined;
            }

            private void UpdateHighestContiguousChunkIndex()
            {
                int index = HighestContiguousChunkIndex + 1;
                while (index < ChunkCount && ReceivedChunkFlags[index])
                    index++;

                HighestContiguousChunkIndex = index - 1;
            }
        }
    }
}
