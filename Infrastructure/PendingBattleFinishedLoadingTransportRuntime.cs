using System;
using System.Reflection;
using System.Threading.Tasks;
using NetworkMessages.FromClient;
using NetworkMessages.FromServer;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer.NetworkComponents;

namespace CoopSpectator.Infrastructure
{
    internal static class PendingBattleFinishedLoadingTransportRuntime
    {
        private static FieldInfo _baseNetworkComponentDataField;
        private static MethodInfo _ensureBaseNetworkComponentDataMethod;
        private static bool _contractsInitialized;

        public static void InitializeBaseNetworkContracts(Type baseNetworkComponentType)
        {
            if (_contractsInitialized || baseNetworkComponentType == null)
                return;

            _baseNetworkComponentDataField = baseNetworkComponentType.GetField(
                "_baseNetworkComponentData",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _ensureBaseNetworkComponentDataMethod = baseNetworkComponentType.GetMethod(
                "EnsureBaseNetworkComponentData",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _contractsInitialized = true;
        }

        public static bool ShouldOwnDeferredServerFinishedLoadingValidation(Mission mission, out string details)
        {
            if (!PendingBattleMissionStartupState.ShouldOwnServerFinishedLoadingValidation(mission))
            {
                details = "PendingBattleOwnership=false";
                return false;
            }

            if (PendingBattleMissionStartupState.ShouldDelayServerFinishedLoadingValidation(mission, out details))
                return true;

            if (PendingBattleMissionStartupState.TryResolveAuthoritativeTransportToken(mission, out int token))
            {
                details =
                    "PendingBattleTokenReady=true" +
                    " MissionSessionToken=" + token;
                return true;
            }

            details = "PendingBattleTokenCaptureRequired=true";
            return true;
        }

        public static void HandleDeferredServerFinishedLoadingValidation(
            object baseNetworkComponentInstance,
            NetworkCommunicator networkPeer,
            FinishedLoading message,
            string initialDelayDetails,
            string source)
        {
            _ = HandleDeferredServerFinishedLoadingValidationAsync(
                baseNetworkComponentInstance,
                networkPeer,
                message,
                initialDelayDetails,
                source);
        }

        private static async Task HandleDeferredServerFinishedLoadingValidationAsync(
            object baseNetworkComponentInstance,
            NetworkCommunicator networkPeer,
            FinishedLoading message,
            string initialDelayDetails,
            string source)
        {
            DateTime startedUtc = DateTime.UtcNow;
            string finalDelayDetails = initialDelayDetails ?? string.Empty;

            try
            {
                while (PendingBattleMissionStartupState.ShouldDelayServerFinishedLoadingValidation(Mission.Current, out string delayDetails))
                {
                    finalDelayDetails = delayDetails ?? string.Empty;
                    await Task.Delay(1);
                }

                if (networkPeer == null || networkPeer.IsServerPeer || message == null)
                    return;

                Mission currentMission = Mission.Current;
                int missionSessionToken = ResolvePendingBattleMissionSessionToken(
                    baseNetworkComponentInstance,
                    currentMission,
                    source);
                bool shouldUnload = currentMission == null || missionSessionToken != message.BattleIndex;

                Debug.Print("Server: " + networkPeer.UserName + " has finished loading. From now on, I will include him in the broadcasted messages");

                if (shouldUnload)
                {
                    CoopSessionTransportPrimitives.SendUnloadMission(networkPeer, true);
                }
                else
                {
                    CoopSessionTransportPrimitives.MarkPeerFinishedLoading(networkPeer);
                }

                ModLogger.Info(
                    "PendingBattleFinishedLoadingTransportRuntime: processed deferred FinishedLoading validation. " +
                    "Peer=" + (networkPeer.UserName ?? "unknown") +
                    " DeferredForMs=" + (DateTime.UtcNow - startedUtc).TotalMilliseconds.ToString("0") +
                    " InitialDelayDetails=" + (initialDelayDetails ?? string.Empty) +
                    " FinalDelayDetails=" + (finalDelayDetails ?? string.Empty) +
                    " MissionScene=" + (currentMission?.SceneName ?? "null") +
                    " MissionState=" + (currentMission?.CurrentState.ToString() ?? "null") +
                    " MissionSessionToken=" + missionSessionToken +
                    " FinishedLoadingBattleIndex=" + message.BattleIndex +
                    " Action=" + (shouldUnload ? "UnloadMission" : "ClientFinishedLoading") +
                    " Source=" + Normalize(source) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "PendingBattleFinishedLoadingTransportRuntime: deferred FinishedLoading handling failed. " +
                    "Peer=" + (networkPeer?.UserName ?? "unknown") +
                    " InitialDelayDetails=" + (initialDelayDetails ?? string.Empty) +
                    " FinalDelayDetails=" + (finalDelayDetails ?? string.Empty) +
                    " Source=" + Normalize(source) + ".",
                    ex);
            }
        }

        private static int ResolvePendingBattleMissionSessionToken(
            object baseNetworkComponentInstance,
            Mission mission,
            string source)
        {
            if (PendingBattleMissionStartupState.TryResolveAuthoritativeTransportToken(mission, out int token))
                return token;

            EnsureBaseNetworkComponentData(baseNetworkComponentInstance);
            int currentBattleIndex = GetCurrentBattleIndex(baseNetworkComponentInstance);
            if (PendingBattleMissionStartupState.TryBindAuthoritativeTransportToken(
                mission,
                currentBattleIndex,
                "PendingBattleFinishedLoadingTransportRuntime.ResolvePendingBattleMissionSessionToken"))
            {
                return currentBattleIndex;
            }

            ModLogger.Info(
                "PendingBattleFinishedLoadingTransportRuntime: failed to bind authoritative pending battle mission-session token from native transport state. " +
                "MissionScene=" + (mission?.SceneName ?? "null") +
                " CurrentBattleIndex=" + currentBattleIndex +
                " Source=" + Normalize(source) + ".");
            return 0;
        }

        private static void EnsureBaseNetworkComponentData(object instance)
        {
            try
            {
                _ensureBaseNetworkComponentDataMethod?.Invoke(instance, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                ModLogger.Info("PendingBattleFinishedLoadingTransportRuntime: EnsureBaseNetworkComponentData invoke failed: " + ex.Message);
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
                ModLogger.Info("PendingBattleFinishedLoadingTransportRuntime: failed to read CurrentBattleIndex: " + ex.Message);
                return -1;
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
