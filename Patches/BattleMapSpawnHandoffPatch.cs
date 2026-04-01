using System;
using System.Reflection;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using NetworkMessages.FromServer;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Suppresses the local spectator-follow network echo during the narrow
    /// battle-map authoritative spawn handoff window.
    /// </summary>
    public static class BattleMapSpawnHandoffPatch
    {
        private static readonly FieldInfo FollowedAgentField =
            typeof(MissionPeer).GetField("_followedAgent", BindingFlags.Instance | BindingFlags.NonPublic);

        private static string _lastSuppressedFollowSwitchKey;
        private static string _lastLocalVisualFinalizeKey;
        private static string _lastSuppressedAssignFormationKey;
        private static string _lastSuppressedLocalSelectAllFormationsKey;
        private static string _lastObservedLocalSelectAllFormationsKey;
        private static string _lastSuppressedServerSelectAllFormationsKey;

        public static void Apply(Harmony harmony)
        {
            try
            {
                PatchMissionPeerFollowedAgent(harmony);
                PatchMissionNetworkComponentSetAgentPeer(harmony);
                PatchMissionNetworkComponentAssignFormationToPlayer(harmony);
                PatchOrderControllerSelectAllFormations(harmony);
                PatchMissionNetworkComponentSelectAllFormations(harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleMapSpawnHandoffPatch.Apply failed.", ex);
            }
        }

        private static void PatchMissionPeerFollowedAgent(Harmony harmony)
        {
            if (FollowedAgentField == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionPeer._followedAgent field not found. Skip.");
                return;
            }

            PropertyInfo property = typeof(MissionPeer).GetProperty(
                "FollowedAgent",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo setter = property?.GetSetMethod(nonPublic: true);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionPeer_FollowedAgent_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (setter == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionPeer.FollowedAgent setter not found. Skip.");
                return;
            }

            harmony.Patch(setter, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionPeer.FollowedAgent.");
        }

        private static void PatchMissionNetworkComponentSetAgentPeer(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventSetAgentPeer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSetAgentPeer_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventSetAgentPeer not found. Skip.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: postfix applied to MissionNetworkComponent.HandleServerEventSetAgentPeer.");
        }

        private static void PatchMissionNetworkComponentAssignFormationToPlayer(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventAssignFormationToPlayer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventAssignFormationToPlayer_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventAssignFormationToPlayer not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleServerEventAssignFormationToPlayer.");
        }

        private static void PatchMissionNetworkComponentSelectAllFormations(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleClientEventSelectAllFormations",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleClientEventSelectAllFormations_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleClientEventSelectAllFormations not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleClientEventSelectAllFormations.");
        }

        private static void PatchOrderControllerSelectAllFormations(Harmony harmony)
        {
            MethodInfo target = typeof(OrderController).GetMethod(
                "SelectAllFormations",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Agent), typeof(bool) },
                modifiers: null);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(OrderController_SelectAllFormations_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: OrderController.SelectAllFormations(Agent,bool) not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to OrderController.SelectAllFormations(Agent,bool).");
        }

        private static bool MissionPeer_FollowedAgent_Prefix(MissionPeer __instance, Agent value)
        {
            if (!ShouldSuppressLocalFollowSwitch(__instance, value, out string logKey, out string logDetails))
                return true;

            FollowedAgentField.SetValue(__instance, value);
            if (!string.Equals(_lastSuppressedFollowSwitchKey, logKey, StringComparison.Ordinal))
            {
                _lastSuppressedFollowSwitchKey = logKey;
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: suppressed local MissionPeer.FollowedAgent network echo during battle-map spawn handshake. " +
                    logDetails);
            }

            return false;
        }

        private static void MissionNetworkComponent_HandleServerEventSetAgentPeer_Postfix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is SetAgentPeer setAgentPeer))
                    return;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return;

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setAgentPeer.AgentIndex, canBeNull: true);
                MissionPeer missionPeer = setAgentPeer.Peer?.GetComponent<MissionPeer>();
                if (agent == null || missionPeer == null || !missionPeer.IsMine || !agent.IsActive())
                    return;

                CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                    CoopBattleSelectionBridgeFile.ReadCurrentSelection();
                CoopBattleEntryPolicy.ClientSnapshot entryPolicy =
                    CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
                if (entryPolicy == null || !entryPolicy.UseAuthoritativeTroopPath)
                    return;

                string logKey =
                    agent.Index + "|" +
                    (agent.SpawnEquipment != null).ToString() + "|" +
                    (agent.MountAgent?.Index.ToString() ?? "none") + "|" +
                    (entryPolicy.BridgeTroopOrEntryId ?? "none");
                if (string.Equals(_lastLocalVisualFinalizeKey, logKey, StringComparison.Ordinal))
                    return;

                if (agent.SpawnEquipment != null)
                    agent.UpdateSpawnEquipmentAndRefreshVisuals(agent.SpawnEquipment);
                agent.WieldInitialWeapons(
                    Agent.WeaponWieldActionType.Instant,
                    Equipment.InitialWeaponEquipPreference.Any);
                agent.MountAgent?.UpdateAgentProperties();

                _lastLocalVisualFinalizeKey = logKey;
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: finalized local player agent visuals after SetAgentPeer for battle-map handoff. " +
                    "AgentIndex=" + agent.Index +
                    " HasSpawnEquipment=" + (agent.SpawnEquipment != null) +
                    " MountAgentIndex=" + (agent.MountAgent?.Index.ToString() ?? "null") +
                    " BridgeTroop=" + (entryPolicy.BridgeTroopOrEntryId ?? "null") +
                    " Mission=" + (mission.SceneName ?? "null"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: local SetAgentPeer visual finalization failed: " + ex.Message);
            }
        }

        private static bool MissionNetworkComponent_HandleServerEventAssignFormationToPlayer_Prefix(GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is AssignFormationToPlayer assignFormationToPlayer))
                return true;

            if (!ShouldSuppressLocalAssignFormationToPlayer(assignFormationToPlayer, out string logKey, out string logDetails))
                return true;

            if (!string.Equals(_lastSuppressedAssignFormationKey, logKey, StringComparison.Ordinal))
            {
                _lastSuppressedAssignFormationKey = logKey;
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: suppressed local AssignFormationToPlayer during battle-map spawn handshake. " +
                    logDetails);
            }

            return false;
        }

        private static bool MissionNetworkComponent_HandleClientEventSelectAllFormations_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            if (!ShouldSuppressServerSelectAllFormations(networkPeer, out string logKey, out string logDetails))
                return true;

            __result = true;
            if (!string.Equals(_lastSuppressedServerSelectAllFormationsKey, logKey, StringComparison.Ordinal))
            {
                _lastSuppressedServerSelectAllFormationsKey = logKey;
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: suppressed server-side SelectAllFormations during exact-scene spawn handshake. " +
                    logDetails);
            }

            return false;
        }

        private static bool OrderController_SelectAllFormations_Prefix(
            OrderController __instance,
            Agent selectorAgent,
            bool uiFeedback)
        {
            if (!ShouldSuppressLocalSelectAllFormations(__instance, selectorAgent, out string logKey, out string logDetails))
            {
                LogObservedLocalSelectAllFormations(__instance, selectorAgent);
                return true;
            }

            if (!string.Equals(_lastSuppressedLocalSelectAllFormationsKey, logKey, StringComparison.Ordinal))
            {
                _lastSuppressedLocalSelectAllFormationsKey = logKey;
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: suppressed local OrderController.SelectAllFormations during exact-scene spawn handshake. " +
                    logDetails);
            }

            return false;
        }

        private static bool ShouldSuppressLocalFollowSwitch(
            MissionPeer missionPeer,
            Agent followedAgent,
            out string logKey,
            out string logDetails)
        {
            logKey = null;
            logDetails = null;

            if (missionPeer == null || followedAgent == null || !followedAgent.IsActive())
                return false;

            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return false;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer)
                return false;

            MissionPeer myMissionPeer = myPeer.GetComponent<MissionPeer>();
            if (myMissionPeer == null || !ReferenceEquals(missionPeer, myMissionPeer))
                return false;

            Mission mission = Mission.Current ?? followedAgent.Mission ?? myMissionPeer.ControlledAgent?.Mission;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            CoopBattleEntryPolicy.ClientSnapshot entryPolicy =
                CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
            if (entryPolicy == null || !entryPolicy.UseAuthoritativeTroopPath)
                return false;

            Agent controlledAgent = myMissionPeer.ControlledAgent;
            bool isFollowSwitchToLocalPlayerAgent =
                followedAgent.IsMine ||
                ReferenceEquals(followedAgent, controlledAgent) ||
                ReferenceEquals(followedAgent.MissionPeer, myMissionPeer);
            if (!isFollowSwitchToLocalPlayerAgent)
                return false;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status =
                CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (status != null && !IsRelevantBattleMapStatus(status, myPeer.Index, mission.SceneName))
                status = null;

            logKey =
                myPeer.Index + "|" +
                followedAgent.Index + "|" +
                (controlledAgent?.Index.ToString() ?? "none") + "|" +
                (status?.SpawnStatus ?? "none") + "|" +
                (status?.LifecycleState ?? "none") + "|" +
                (entryPolicy.BridgeTroopOrEntryId ?? "none");
            logDetails =
                "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                " FollowedAgentIndex=" + followedAgent.Index +
                " ControlledAgentIndex=" + (controlledAgent?.Index.ToString() ?? "null") +
                " SpawnStatus=" + (status?.SpawnStatus ?? "null") +
                " LifecycleState=" + (status?.LifecycleState ?? "null") +
                " StatusHasAgent=" + (status?.HasAgent.ToString() ?? "null") +
                " BridgeTroop=" + (entryPolicy.BridgeTroopOrEntryId ?? "null") +
                " Mission=" + (mission.SceneName ?? "null");
            return true;
        }

        private static void LogObservedLocalSelectAllFormations(OrderController orderController, Agent selectorAgent)
        {
            try
            {
                if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || orderController == null)
                    return;

                NetworkCommunicator myPeer = GameNetwork.MyPeer;
                if (myPeer == null || myPeer.IsServerPeer)
                    return;

                MissionPeer myMissionPeer = myPeer.GetComponent<MissionPeer>();
                Mission mission = Mission.Current ?? selectorAgent?.Mission ?? myMissionPeer?.ControlledAgent?.Mission;
                if (myMissionPeer == null || mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return;

                if (!SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
                    return;

                CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                    CoopBattleSelectionBridgeFile.ReadCurrentSelection();
                CoopBattleEntryPolicy.ClientSnapshot entryPolicy =
                    CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
                if (entryPolicy == null || !entryPolicy.UseAuthoritativeTroopPath)
                    return;

                Agent controlledAgent = myMissionPeer.ControlledAgent;
                Agent orderOwner = orderController.Owner;
                bool hasSpawnState =
                    CoopBattleSpawnRuntimeState.TryGetState(myMissionPeer, out PeerSpawnRuntimeState spawnState);
                bool hasLifecycleState =
                    CoopBattlePeerLifecycleRuntimeState.TryGetState(myMissionPeer, out PeerLifecycleRuntimeState lifecycleState);

                string logKey =
                    myPeer.Index + "|" +
                    (controlledAgent?.Index.ToString() ?? "null") + "|" +
                    (orderOwner?.Index.ToString() ?? "null") + "|" +
                    myMissionPeer.BotsUnderControlTotal + "|" +
                    myMissionPeer.BotsUnderControlAlive + "|" +
                    (hasSpawnState ? spawnState.Status.ToString() : "none") + "|" +
                    (hasLifecycleState ? lifecycleState.Status.ToString() : "none");
                if (string.Equals(_lastObservedLocalSelectAllFormationsKey, logKey, StringComparison.Ordinal))
                    return;

                _lastObservedLocalSelectAllFormationsKey = logKey;
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: observed local OrderController.SelectAllFormations without suppression on exact-scene spawn handshake candidate. " +
                    "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                    " ControlledAgentIndex=" + (controlledAgent?.Index.ToString() ?? "null") +
                    " OrderOwnerIndex=" + (orderOwner?.Index.ToString() ?? "null") +
                    " SelectorAgentIndex=" + (selectorAgent?.Index.ToString() ?? "null") +
                    " ControlledFormationIndex=" + (myMissionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                    " BotsUnderControlTotal=" + myMissionPeer.BotsUnderControlTotal +
                    " BotsUnderControlAlive=" + myMissionPeer.BotsUnderControlAlive +
                    " SpawnStatus=" + (hasSpawnState ? spawnState.Status.ToString() : "null") +
                    " LifecycleState=" + (hasLifecycleState ? lifecycleState.Status.ToString() : "null") +
                    " BridgeTroop=" + (entryPolicy.BridgeTroopOrEntryId ?? "null") +
                    " Mission=" + (mission.SceneName ?? "null"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: local SelectAllFormations observation failed: " + ex.Message);
            }
        }

        private static bool ShouldSuppressLocalAssignFormationToPlayer(
            AssignFormationToPlayer assignFormationToPlayer,
            out string logKey,
            out string logDetails)
        {
            logKey = null;
            logDetails = null;

            if (assignFormationToPlayer == null || !GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return false;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer)
                return false;

            NetworkCommunicator targetPeer = TryResolveMessagePeer(assignFormationToPlayer);
            if (targetPeer != null && !ReferenceEquals(targetPeer, myPeer))
                return false;

            MissionPeer myMissionPeer = myPeer.GetComponent<MissionPeer>();
            Mission mission = Mission.Current ?? myMissionPeer?.ControlledAgent?.Mission;
            if (myMissionPeer == null || mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            CoopBattleEntryPolicy.ClientSnapshot entryPolicy =
                CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
            if (entryPolicy == null || !entryPolicy.UseAuthoritativeTroopPath)
                return false;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status =
                CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (status != null && !IsRelevantBattleMapStatus(status, myPeer.Index, mission.SceneName))
                status = null;

            int botsUnderControlTotal = myMissionPeer.BotsUnderControlTotal;
            int botsUnderControlAlive = myMissionPeer.BotsUnderControlAlive;
            if (botsUnderControlTotal <= 1 && botsUnderControlAlive <= 0)
                return false;

            int formationIndex = TryResolveMessageInt(assignFormationToPlayer, "FormationIndex");
            logKey =
                myPeer.Index + "|" +
                formationIndex + "|" +
                botsUnderControlTotal + "|" +
                botsUnderControlAlive + "|" +
                (status?.SpawnStatus ?? "none") + "|" +
                (status?.LifecycleState ?? "none") + "|" +
                (entryPolicy.BridgeTroopOrEntryId ?? "none");
            logDetails =
                "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                " FormationIndex=" + formationIndex +
                " BotsUnderControlTotal=" + botsUnderControlTotal +
                " BotsUnderControlAlive=" + botsUnderControlAlive +
                " ControlledFormationIndex=" + (myMissionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                " ControlledAgentIndex=" + (myMissionPeer.ControlledAgent?.Index.ToString() ?? "null") +
                " SpawnStatus=" + (status?.SpawnStatus ?? "null") +
                " LifecycleState=" + (status?.LifecycleState ?? "null") +
                " BridgeTroop=" + (entryPolicy.BridgeTroopOrEntryId ?? "null") +
                " Mission=" + (mission.SceneName ?? "null");
            return true;
        }

        private static bool ShouldSuppressServerSelectAllFormations(
            NetworkCommunicator networkPeer,
            out string logKey,
            out string logDetails)
        {
            logKey = null;
            logDetails = null;

            if (!GameNetwork.IsServer || networkPeer == null || networkPeer.IsServerPeer)
                return false;

            Mission mission = Mission.Current;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            if (!SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
                return false;

            MissionPeer missionPeer = networkPeer.GetComponent<MissionPeer>();
            if (missionPeer == null || !missionPeer.IsControlledAgentActive)
                return false;

            if (!CoopBattleSpawnRuntimeState.TryGetState(missionPeer, out PeerSpawnRuntimeState spawnState) ||
                spawnState.Status != CoopBattleSpawnStatus.Spawned)
            {
                return false;
            }

            if (!CoopBattlePeerLifecycleRuntimeState.TryGetState(missionPeer, out PeerLifecycleRuntimeState lifecycleState) ||
                lifecycleState.Status != CoopBattlePeerLifecycleStatus.Alive)
            {
                return false;
            }

            if (missionPeer.BotsUnderControlTotal > 1 || missionPeer.BotsUnderControlAlive > 0)
                return false;

            logKey =
                networkPeer.Index + "|" +
                missionPeer.ControlledAgent?.Index + "|" +
                missionPeer.BotsUnderControlTotal + "|" +
                missionPeer.BotsUnderControlAlive + "|" +
                (spawnState.TroopId ?? "none") + "|" +
                (spawnState.EntryId ?? "none");
            logDetails =
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " ControlledAgentIndex=" + (missionPeer.ControlledAgent?.Index.ToString() ?? "null") +
                " ControlledFormationIndex=" + (missionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                " BotsUnderControlTotal=" + missionPeer.BotsUnderControlTotal +
                " BotsUnderControlAlive=" + missionPeer.BotsUnderControlAlive +
                " SpawnStatus=" + spawnState.Status +
                " LifecycleState=" + lifecycleState.Status +
                " TroopId=" + (spawnState.TroopId ?? "null") +
                " EntryId=" + (spawnState.EntryId ?? "null") +
                " Mission=" + (mission.SceneName ?? "null");
            return true;
        }

        private static bool ShouldSuppressLocalSelectAllFormations(
            OrderController orderController,
            Agent selectorAgent,
            out string logKey,
            out string logDetails)
        {
            logKey = null;
            logDetails = null;

            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || orderController == null)
                return false;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer)
                return false;

            MissionPeer myMissionPeer = myPeer.GetComponent<MissionPeer>();
            Mission mission = Mission.Current ?? selectorAgent?.Mission ?? myMissionPeer?.ControlledAgent?.Mission;
            if (myMissionPeer == null || mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            if (!SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
                return false;

            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            CoopBattleEntryPolicy.ClientSnapshot entryPolicy =
                CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
            if (entryPolicy == null || !entryPolicy.UseAuthoritativeTroopPath)
                return false;

            Agent controlledAgent = myMissionPeer.ControlledAgent;
            if (controlledAgent == null || !controlledAgent.IsActive())
                return false;

            Agent orderOwner = orderController.Owner;
            if (orderOwner != null && !ReferenceEquals(orderOwner, controlledAgent))
                return false;

            if (selectorAgent != null && !ReferenceEquals(selectorAgent, controlledAgent))
                return false;

            bool hasSpawnState = CoopBattleSpawnRuntimeState.TryGetState(myMissionPeer, out PeerSpawnRuntimeState spawnState);
            bool hasLifecycleState = CoopBattlePeerLifecycleRuntimeState.TryGetState(myMissionPeer, out PeerLifecycleRuntimeState lifecycleState);
            bool hasControlledFormation = myMissionPeer.ControlledFormation != null;
            bool hasLiveControlledBots = myMissionPeer.BotsUnderControlAlive > 0;
            bool hasMaterializedControlledFormation = myMissionPeer.BotsUnderControlTotal > 1;
            bool isEarlyExactSceneSpawnHandshake =
                !hasControlledFormation &&
                !hasLiveControlledBots &&
                !hasMaterializedControlledFormation;
            bool isLateExactSceneSpawnHandshake =
                hasSpawnState &&
                spawnState.Status == CoopBattleSpawnStatus.Spawned &&
                hasLifecycleState &&
                lifecycleState.Status == CoopBattlePeerLifecycleStatus.Alive &&
                !hasControlledFormation &&
                !hasLiveControlledBots &&
                !hasMaterializedControlledFormation;

            if (!isEarlyExactSceneSpawnHandshake && !isLateExactSceneSpawnHandshake)
                return false;

            logKey =
                myPeer.Index + "|" +
                controlledAgent.Index + "|" +
                myMissionPeer.BotsUnderControlTotal + "|" +
                myMissionPeer.BotsUnderControlAlive + "|" +
                (hasSpawnState ? (spawnState.TroopId ?? "none") : "none") + "|" +
                (hasSpawnState ? (spawnState.EntryId ?? "none") : "none") + "|" +
                (isEarlyExactSceneSpawnHandshake ? "early-preformation" : "late-spawned-alive");
            logDetails =
                "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                " ControlledAgentIndex=" + controlledAgent.Index +
                " OrderOwnerIndex=" + (orderOwner?.Index.ToString() ?? "null") +
                " SelectorAgentIndex=" + (selectorAgent?.Index.ToString() ?? "null") +
                " ControlledFormationIndex=" + (myMissionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                " BotsUnderControlTotal=" + myMissionPeer.BotsUnderControlTotal +
                " BotsUnderControlAlive=" + myMissionPeer.BotsUnderControlAlive +
                " SuppressionPhase=" + (isEarlyExactSceneSpawnHandshake ? "early-preformation" : "late-spawned-alive") +
                " SpawnStatus=" + (hasSpawnState ? spawnState.Status.ToString() : "null") +
                " LifecycleState=" + (hasLifecycleState ? lifecycleState.Status.ToString() : "null") +
                " TroopId=" + (hasSpawnState ? (spawnState.TroopId ?? "null") : "null") +
                " EntryId=" + (hasSpawnState ? (spawnState.EntryId ?? "null") : "null") +
                " Mission=" + (mission.SceneName ?? "null");
            return true;
        }

        private static NetworkCommunicator TryResolveMessagePeer(object message)
        {
            if (message == null)
                return null;

            Type messageType = message.GetType();
            foreach (PropertyInfo property in messageType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(NetworkCommunicator).IsAssignableFrom(property.PropertyType) || property.GetIndexParameters().Length > 0)
                    continue;

                try
                {
                    NetworkCommunicator peer = property.GetValue(message, null) as NetworkCommunicator;
                    if (peer != null)
                        return peer;
                }
                catch
                {
                }
            }

            foreach (FieldInfo field in messageType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(NetworkCommunicator).IsAssignableFrom(field.FieldType))
                    continue;

                try
                {
                    NetworkCommunicator peer = field.GetValue(message) as NetworkCommunicator;
                    if (peer != null)
                        return peer;
                }
                catch
                {
                }
            }

            return null;
        }

        private static int TryResolveMessageInt(object message, string memberName)
        {
            if (message == null || string.IsNullOrWhiteSpace(memberName))
                return -1;

            Type messageType = message.GetType();
            PropertyInfo property = messageType.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.PropertyType == typeof(int) && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return (int)property.GetValue(message, null);
                }
                catch
                {
                }
            }

            FieldInfo field = messageType.GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(int))
            {
                try
                {
                    return (int)field.GetValue(message);
                }
                catch
                {
                }
            }

            return -1;
        }

        private static bool IsRelevantBattleMapStatus(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status,
            int peerIndex,
            string missionName)
        {
            if (status == null)
                return true;

            if (!status.HasPeer || status.PeerIndex != peerIndex)
                return false;

            if (!string.IsNullOrWhiteSpace(missionName) &&
                !string.Equals(status.MissionName, missionName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            DateTime updatedUtc = status.UpdatedUtc;
            if (updatedUtc == DateTime.MinValue)
                return false;

            if (updatedUtc.Kind == DateTimeKind.Unspecified)
                updatedUtc = DateTime.SpecifyKind(updatedUtc, DateTimeKind.Utc);

            if (DateTime.UtcNow - updatedUtc > TimeSpan.FromSeconds(5))
                return false;

            string spawnStatus = status.SpawnStatus ?? string.Empty;
            if (string.Equals(spawnStatus, CoopBattleSpawnStatus.Pending.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(spawnStatus, CoopBattleSpawnStatus.Validating.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(spawnStatus, CoopBattleSpawnStatus.Validated.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(spawnStatus, CoopBattleSpawnStatus.Spawned.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string lifecycleState = status.LifecycleState ?? string.Empty;
            return
                string.Equals(lifecycleState, "SpawnQueued", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycleState, "Alive", StringComparison.OrdinalIgnoreCase);
        }
    }
}
