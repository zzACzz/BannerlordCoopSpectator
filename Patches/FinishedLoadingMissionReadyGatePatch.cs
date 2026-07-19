using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using NetworkMessages.FromClient;
using NetworkMessages.FromServer;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer.NetworkComponents;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Patches
{
    public static class FinishedLoadingMissionReadyGatePatch
    {
        private static FieldInfo _baseNetworkComponentDataField;
        private static MethodInfo _ensureBaseNetworkComponentDataMethod;
        private static readonly object DeferredPeerSync = new object();
        private static readonly HashSet<int> DeferredPeerIndices = new HashSet<int>();
        private static readonly TimeSpan FinishedLoadingBarrierTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan FinishedLoadingBarrierPollInterval = TimeSpan.FromMilliseconds(25);

        public static void Apply(Harmony harmony)
        {
            try
            {
                Type targetType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.Multiplayer.NetworkComponents.BaseNetworkComponent");
                if (targetType == null)
                {
                    ModLogger.Info("FinishedLoadingMissionReadyGatePatch: BaseNetworkComponent type not found. Skip.");
                    return;
                }

                MethodInfo targetMethod = targetType.GetMethod(
                    "HandleClientEventFinishedLoading",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) },
                    null);
                MethodInfo prefixMethod = typeof(FinishedLoadingMissionReadyGatePatch).GetMethod(
                    nameof(HandleClientEventFinishedLoading_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (targetMethod == null || prefixMethod == null)
                {
                    ModLogger.Info("FinishedLoadingMissionReadyGatePatch: target/prefix method not found. Skip.");
                    return;
                }

                _baseNetworkComponentDataField = targetType.GetField("_baseNetworkComponentData", BindingFlags.Instance | BindingFlags.NonPublic);
                _ensureBaseNetworkComponentDataMethod = targetType.GetMethod("EnsureBaseNetworkComponentData", BindingFlags.Instance | BindingFlags.NonPublic);

                harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
                ModLogger.Info("FinishedLoadingMissionReadyGatePatch: patched BaseNetworkComponent.HandleClientEventFinishedLoading.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("FinishedLoadingMissionReadyGatePatch.Apply failed.", ex);
            }
        }

        private static bool HandleClientEventFinishedLoading_Prefix(
            object __instance,
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            if (!GameNetwork.IsServer)
                return true;

            if (!(baseMessage is FinishedLoading message))
                return true;

            if (networkPeer == null || networkPeer.IsServerPeer)
                return true;

            Mission requestedMission = Mission.Current;
            bool startupDelayRequired =
                PendingBattleMissionStartupState.ShouldDelayServerFinishedLoadingValidation(
                    requestedMission,
                    out string delayDetails);
            bool payloadBarrierRequired =
                CoopMissionNetworkBridge.ShouldGateServerFinishedLoading(
                    requestedMission,
                    out string payloadBarrierDetails);
            if (!startupDelayRequired && !payloadBarrierRequired)
                return true;

            if (!TryRegisterDeferredPeer(networkPeer.Index))
            {
                __result = true;
                return false;
            }

            HandleClientEventFinishedLoadingDeferred(
                __instance,
                networkPeer,
                message.BattleIndex,
                requestedMission,
                delayDetails,
                payloadBarrierDetails);
            __result = true;
            return false;
        }

        private static async void HandleClientEventFinishedLoadingDeferred(
            object instance,
            NetworkCommunicator networkPeer,
            int requestedBattleIndex,
            Mission requestedMission,
            string initialDelayDetails,
            string initialPayloadBarrierDetails)
        {
            DateTime startedUtc = DateTime.UtcNow;
            string finalDelayDetails = initialDelayDetails ?? string.Empty;
            string finalPayloadBarrierDetails = initialPayloadBarrierDetails ?? string.Empty;
            string action = "none";

            try
            {
                EnsureBaseNetworkComponentData(instance);
                while (true)
                {
                    if (networkPeer == null ||
                        networkPeer.IsServerPeer ||
                        !networkPeer.IsConnectionActive)
                    {
                        action = "peer-disconnected";
                        break;
                    }

                    Mission currentMission = Mission.Current;
                    int currentBattleIndex = GetCurrentBattleIndex(instance);
                    bool battleChanged = currentBattleIndex != requestedBattleIndex;
                    if (!battleChanged &&
                        requestedMission == null &&
                        currentMission != null)
                    {
                        requestedMission = currentMission;
                    }

                    bool missionChanged =
                        battleChanged ||
                        (requestedMission != null &&
                         (currentMission == null ||
                          !ReferenceEquals(currentMission, requestedMission)));
                    if (missionChanged)
                    {
                        SendUnloadMission(networkPeer);
                        action = "UnloadMission:mission-or-battle-changed";
                        break;
                    }

                    if (DateTime.UtcNow - startedUtc >= FinishedLoadingBarrierTimeout)
                    {
                        SendUnloadMission(networkPeer);
                        action = "UnloadMission:barrier-timeout";
                        break;
                    }

                    bool startupDelayRequired =
                        PendingBattleMissionStartupState.ShouldDelayServerFinishedLoadingValidation(
                            currentMission,
                            out string delayDetails);
                    finalDelayDetails = delayDetails ?? string.Empty;
                    bool payloadBarrierRequired =
                        CoopMissionNetworkBridge.ShouldGateServerFinishedLoading(
                            currentMission,
                            out string payloadBarrierPolicy);
                    bool payloadBarrierReady = !payloadBarrierRequired;
                    if (payloadBarrierRequired)
                    {
                        CoopMissionNetworkBridge bridge =
                            currentMission.GetMissionBehavior<CoopMissionNetworkBridge>();
                        if (bridge == null)
                        {
                            finalPayloadBarrierDetails =
                                "Policy={" + payloadBarrierPolicy + "} Bridge=missing";
                        }
                        else
                        {
                            payloadBarrierReady = bridge.TryAdvancePeerFinishedLoadingBarrier(
                                networkPeer,
                                out string payloadReadiness);
                            finalPayloadBarrierDetails =
                                "Policy={" + payloadBarrierPolicy + "} Readiness={" +
                                payloadReadiness + "}";
                        }
                    }
                    else
                    {
                        finalPayloadBarrierDetails = "Policy={" + payloadBarrierPolicy + "}";
                    }

                    if (!startupDelayRequired &&
                        payloadBarrierReady &&
                        requestedMission != null)
                    {
                        bool finalMissionIdentityMatched =
                            ReferenceEquals(Mission.Current, requestedMission) &&
                            GetCurrentBattleIndex(instance) == requestedBattleIndex;
                        bool finalPayloadBarrierReady = true;
                        if (payloadBarrierRequired)
                        {
                            CoopMissionNetworkBridge bridge =
                                currentMission.GetMissionBehavior<CoopMissionNetworkBridge>();
                            string finalPayloadReadiness = "bridge-missing";
                            finalPayloadBarrierReady =
                                bridge != null &&
                                bridge.TryAdvancePeerFinishedLoadingBarrier(
                                    networkPeer,
                                    out finalPayloadReadiness);
                            finalPayloadBarrierDetails +=
                                " FinalRecheck={" + finalPayloadReadiness + "}";
                        }

                        if (finalMissionIdentityMatched &&
                            finalPayloadBarrierReady &&
                            networkPeer.IsConnectionActive)
                        {
                            Debug.Print(
                                "Server: " + networkPeer.UserName +
                                " has finished loading. From now on, I will include him in the broadcasted messages");
                            GameNetwork.ClientFinishedLoading(networkPeer);
                            action = "ClientFinishedLoading";
                        }
                        else
                        {
                            SendUnloadMission(networkPeer);
                            action = "UnloadMission:final-recheck-failed";
                        }

                        break;
                    }

                    await Task.Delay(FinishedLoadingBarrierPollInterval);
                }

                ModLogger.Info(
                    "FinishedLoadingMissionReadyGatePatch: processed deferred FinishedLoading validation. " +
                    "Peer=" + (networkPeer.UserName ?? "unknown") +
                    " DeferredForMs=" + (DateTime.UtcNow - startedUtc).TotalMilliseconds.ToString("0") +
                    " InitialDelayDetails=" + (initialDelayDetails ?? string.Empty) +
                    " FinalDelayDetails=" + (finalDelayDetails ?? string.Empty) +
                    " InitialPayloadBarrierDetails=" + (initialPayloadBarrierDetails ?? string.Empty) +
                    " FinalPayloadBarrierDetails=" + (finalPayloadBarrierDetails ?? string.Empty) +
                    " MissionScene=" + (Mission.Current?.SceneName ?? "null") +
                    " MissionState=" + (Mission.Current?.CurrentState.ToString() ?? "null") +
                    " CurrentBattleIndex=" + GetCurrentBattleIndex(instance) +
                    " FinishedLoadingBattleIndex=" + requestedBattleIndex +
                    " Action=" + action + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "FinishedLoadingMissionReadyGatePatch: deferred FinishedLoading handling failed. " +
                    "Peer=" + (networkPeer?.UserName ?? "unknown") +
                    " InitialDelayDetails=" + (initialDelayDetails ?? string.Empty) +
                    " FinalDelayDetails=" + (finalDelayDetails ?? string.Empty) +
                    " FinalPayloadBarrierDetails=" + (finalPayloadBarrierDetails ?? string.Empty) + ".",
                    ex);

                if (networkPeer != null && networkPeer.IsConnectionActive)
                    SendUnloadMission(networkPeer);
            }
            finally
            {
                if (networkPeer != null)
                    UnregisterDeferredPeer(networkPeer.Index);
            }
        }

        private static bool TryRegisterDeferredPeer(int peerIndex)
        {
            if (peerIndex < 0)
                return false;

            lock (DeferredPeerSync)
                return DeferredPeerIndices.Add(peerIndex);
        }

        private static void UnregisterDeferredPeer(int peerIndex)
        {
            if (peerIndex < 0)
                return;

            lock (DeferredPeerSync)
                DeferredPeerIndices.Remove(peerIndex);
        }

        private static void SendUnloadMission(NetworkCommunicator networkPeer)
        {
            if (networkPeer == null || networkPeer.IsServerPeer || !networkPeer.IsConnectionActive)
                return;

            GameNetwork.BeginModuleEventAsServer(networkPeer);
            GameNetwork.WriteMessage(new UnloadMission(true));
            GameNetwork.EndModuleEventAsServer();
        }

        private static void EnsureBaseNetworkComponentData(object instance)
        {
            try
            {
                _ensureBaseNetworkComponentDataMethod?.Invoke(instance, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                ModLogger.Info("FinishedLoadingMissionReadyGatePatch: EnsureBaseNetworkComponentData invoke failed: " + ex.Message);
            }
        }

        private static int GetCurrentBattleIndex(object instance)
        {
            try
            {
                BaseNetworkComponentData data = _baseNetworkComponentDataField?.GetValue(instance) as BaseNetworkComponentData;
                return data?.CurrentBattleIndex ?? -1;
            }
            catch (Exception ex)
            {
                ModLogger.Info("FinishedLoadingMissionReadyGatePatch: failed to read CurrentBattleIndex: " + ex.Message);
                return -1;
            }
        }
    }
}
