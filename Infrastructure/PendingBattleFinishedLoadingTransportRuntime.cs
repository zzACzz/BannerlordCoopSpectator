using System;
using System.Threading.Tasks;
using NetworkMessages.FromClient;
using NetworkMessages.FromServer;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class PendingBattleFinishedLoadingTransportRuntime
    {
        public static bool ShouldOwnDeferredServerFinishedLoadingValidation(Mission mission, out string details)
        {
            if (!PendingBattleMissionStartupState.ShouldOwnServerFinishedLoadingValidation(mission))
            {
                details = "PendingBattleOwnership=false";
                return false;
            }

            if (PendingBattleMissionStartupState.ShouldDelayServerFinishedLoadingValidation(mission, out details))
                return true;

            if (PendingBattleMissionStartupState.TryResolveAuthoritativeValidationState(
                    mission,
                    out int token,
                    out bool acceptCompatibilityZeroFinishedLoadingBattleIndex))
            {
                details =
                    "PendingBattleValidationReady=true" +
                    " MissionSessionToken=" + token +
                    " CompatibilityZeroAccepted=" + acceptCompatibilityZeroFinishedLoadingBattleIndex;
                return true;
            }

            details = "PendingBattleValidationStateUnavailable=true";
            return true;
        }

        public static void HandleDeferredServerFinishedLoadingValidation(
            NetworkCommunicator networkPeer,
            FinishedLoading message,
            string initialDelayDetails,
            string source)
        {
            _ = HandleDeferredServerFinishedLoadingValidationAsync(
                networkPeer,
                message,
                initialDelayDetails,
                source);
        }

        private static async Task HandleDeferredServerFinishedLoadingValidationAsync(
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
                ResolvePendingBattleMissionValidationState(
                    currentMission,
                    out int missionSessionToken,
                    out bool acceptCompatibilityZeroFinishedLoadingBattleIndex);
                bool compatibilityZeroAccepted =
                    acceptCompatibilityZeroFinishedLoadingBattleIndex &&
                    message.BattleIndex <= 0;
                bool shouldUnload =
                    currentMission == null ||
                    (!compatibilityZeroAccepted && missionSessionToken != message.BattleIndex);
                string validationMode =
                    currentMission == null
                        ? "MissionNull"
                        : compatibilityZeroAccepted
                            ? "CompatibilityZeroBattleIndexAccepted"
                            : missionSessionToken > 0
                                ? "AuthoritativePendingMissionSessionTokenMatchedOrMismatched"
                                : "AuthoritativePendingMissionSessionTokenUnavailable";

                Debug.Print("Server: " + networkPeer.UserName + " has finished loading. From now on, I will include him in the broadcasted messages");

                string action = CoopSessionTransportPrimitives.CompletePeerFinishedLoadingTransportStep(
                    networkPeer,
                    shouldUnload,
                    "PendingBattleFinishedLoadingTransportRuntime.HandleDeferredServerFinishedLoadingValidationAsync");

                ModLogger.Info(
                    "PendingBattleFinishedLoadingTransportRuntime: processed deferred FinishedLoading validation. " +
                    "Peer=" + (networkPeer.UserName ?? "unknown") +
                    " DeferredForMs=" + (DateTime.UtcNow - startedUtc).TotalMilliseconds.ToString("0") +
                    " InitialDelayDetails=" + (initialDelayDetails ?? string.Empty) +
                    " FinalDelayDetails=" + (finalDelayDetails ?? string.Empty) +
                    " MissionScene=" + (currentMission?.SceneName ?? "null") +
                    " MissionState=" + (currentMission?.CurrentState.ToString() ?? "null") +
                    " MissionSessionToken=" + missionSessionToken +
                    " CompatibilityZeroAccepted=" + compatibilityZeroAccepted +
                    " FinishedLoadingBattleIndex=" + message.BattleIndex +
                    " ValidationMode=" + validationMode +
                    " Action=" + action +
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

        private static void ResolvePendingBattleMissionValidationState(
            Mission mission,
            out int missionSessionToken,
            out bool acceptCompatibilityZeroFinishedLoadingBattleIndex)
        {
            if (PendingBattleMissionStartupState.TryResolveAuthoritativeValidationState(
                    mission,
                    out missionSessionToken,
                    out acceptCompatibilityZeroFinishedLoadingBattleIndex))
            {
                return;
            }

            ModLogger.Info(
                "PendingBattleFinishedLoadingTransportRuntime: authoritative pending battle finished-loading validation state was unavailable after startup delay resolved. " +
                "MissionScene=" + (mission?.SceneName ?? "null") + ".");
            missionSessionToken = 0;
            acceptCompatibilityZeroFinishedLoadingBattleIndex = false;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
