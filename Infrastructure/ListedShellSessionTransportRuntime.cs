using System;
using System.Threading.Tasks;
using CoopSpectator.Patches;
using NetworkMessages.FromClient;
using NetworkMessages.FromServer;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class ListedShellSessionTransportRuntime
    {
        private const int StartMultiplayerServerSessionPort = 0x270f;

        public static bool TryStartListedClientTransport(
            string gameType,
            string address,
            int port,
            int sessionKey,
            int peerIndex,
            string source)
        {
            try
            {
                ListedShellClientSessionOwnershipState.PromoteToReceiveBootstrap(
                    gameType,
                    address,
                    port,
                    sessionKey,
                    peerIndex,
                    source);
                GameNetwork.StartMultiplayerOnClient(address, port, sessionKey, peerIndex);
                ModLogger.Info(
                    "ListedShellSessionTransportRuntime: started listed client transport. " +
                    "GameType=" + Normalize(gameType) +
                    " Address=" + Normalize(address) +
                    " Port=" + port +
                    " SessionKey=" + sessionKey +
                    " PeerIndex=" + peerIndex +
                    " Source=" + Normalize(source) + ".");
                return true;
            }
            catch (Exception ex)
            {
                ListedShellClientSessionOwnershipState.Disarm(
                    "ListedShellSessionTransportRuntime.TryStartListedClientTransport failure");
                ModLogger.Error(
                    "ListedShellSessionTransportRuntime.TryStartListedClientTransport failed. " +
                    "GameType=" + Normalize(gameType) +
                    " Address=" + Normalize(address) +
                    " Port=" + port +
                    " SessionKey=" + sessionKey +
                    " PeerIndex=" + peerIndex +
                    " Source=" + Normalize(source) + ".",
                    ex);
                return false;
            }
        }

        public static async Task<bool> TryStartHostedListedServerTransportAsync(
            string gameType,
            string scene,
            bool isInGame,
            string source)
        {
            try
            {
                GameNetwork.PreStartMultiplayerOnServer();
                if (!TaleWorlds.MountAndBlade.Module.CurrentModule.StartMultiplayerGame(gameType, scene))
                {
                    ModLogger.Info(
                        "ListedShellSessionTransportRuntime: hosted listed StartMultiplayerGame returned false. " +
                        "GameType=" + Normalize(gameType) +
                        " Scene=" + Normalize(scene) +
                        " Source=" + Normalize(source) + ".");
                    return false;
                }

                while (Mission.Current == null || (int)Mission.Current.CurrentState != 2)
                    await Task.Delay(1);

                GameNetwork.StartMultiplayerOnServer(StartMultiplayerServerSessionPort);
                if (isInGame)
                {
                    BannerlordNetwork.CreateServerPeer();
                    if (!GameNetwork.IsDedicatedServer)
                        GameNetwork.ClientFinishedLoading(GameNetwork.MyPeer);
                }

                ModLogger.Info(
                    "ListedShellSessionTransportRuntime: started hosted listed server transport. " +
                    "GameType=" + Normalize(gameType) +
                    " Scene=" + Normalize(scene) +
                    " IsInGame=" + isInGame +
                    " Dedicated=" + GameNetwork.IsDedicatedServer +
                    " Source=" + Normalize(source) + ".");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "ListedShellSessionTransportRuntime.TryStartHostedListedServerTransportAsync failed. " +
                    "GameType=" + Normalize(gameType) +
                    " Scene=" + Normalize(scene) +
                    " IsInGame=" + isInGame +
                    " Source=" + Normalize(source) + ".",
                    ex);
                return false;
            }
        }

        public static void CompleteListedPeerFinishedLoading(
            NetworkCommunicator networkPeer,
            bool shouldUnload,
            string source)
        {
            if (networkPeer == null || networkPeer.IsServerPeer)
                return;

            if (shouldUnload)
            {
                GameNetwork.BeginModuleEventAsServer(networkPeer);
                GameNetwork.WriteMessage(new UnloadMission(true));
                GameNetwork.EndModuleEventAsServer();
            }
            else
            {
                GameNetwork.ClientFinishedLoading(networkPeer);
            }

            ModLogger.Info(
                "ListedShellSessionTransportRuntime: completed listed peer finished-loading transport step. " +
                "Peer=" + (networkPeer.UserName ?? "unknown") +
                " Action=" + (shouldUnload ? "UnloadMission" : "ClientFinishedLoading") +
                " Source=" + Normalize(source) + ".");
        }

        public static bool ShouldOwnListedServerFinishedLoadingValidation(Mission mission)
        {
            if (ListedShellMissionSessionState.ShouldOwnServerFinishedLoadingValidation(mission))
                return true;

            if (mission == null)
                return false;

            return mission.GetMissionBehavior<ListedShellCompatibilityMode>() != null ||
                mission.GetMissionBehavior<ListedShellCompatibilityModeClient>() != null;
        }

        public static void HandleListedServerFinishedLoadingValidation(
            NetworkCommunicator networkPeer,
            FinishedLoading message,
            string source)
        {
            if (networkPeer == null || networkPeer.IsServerPeer || message == null)
                return;

            DateTime startedUtc = DateTime.UtcNow;
            string delayDetails = string.Empty;

            try
            {
                Mission currentMission = Mission.Current;
                if (ListedShellMissionSessionState.ShouldDelayServerFinishedLoadingValidation(currentMission, out delayDetails))
                {
                    _ = HandleListedServerFinishedLoadingValidationDeferred(
                        networkPeer,
                        message.BattleIndex,
                        startedUtc,
                        delayDetails,
                        source);
                    return;
                }

                ProcessListedServerFinishedLoadingValidation(
                    networkPeer,
                    message.BattleIndex,
                    startedUtc,
                    delayDetails,
                    source);
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "ListedShellSessionTransportRuntime.HandleListedServerFinishedLoadingValidation failed. " +
                    "Peer=" + (networkPeer.UserName ?? "unknown") +
                    " Source=" + Normalize(source) + ".",
                    ex);
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static async Task HandleListedServerFinishedLoadingValidationDeferred(
            NetworkCommunicator networkPeer,
            int finishedLoadingBattleIndex,
            DateTime startedUtc,
            string initialDelayDetails,
            string source)
        {
            string finalDelayDetails = initialDelayDetails ?? string.Empty;

            try
            {
                while (ListedShellMissionSessionState.ShouldDelayServerFinishedLoadingValidation(Mission.Current, out string delayDetails))
                {
                    finalDelayDetails = delayDetails ?? string.Empty;
                    await Task.Delay(1);
                }

                ProcessListedServerFinishedLoadingValidation(
                    networkPeer,
                    finishedLoadingBattleIndex,
                    startedUtc,
                    finalDelayDetails,
                    source);
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "ListedShellSessionTransportRuntime: deferred listed FinishedLoading validation failed. " +
                    "Peer=" + (networkPeer?.UserName ?? "unknown") +
                    " InitialDelayDetails=" + (initialDelayDetails ?? string.Empty) +
                    " FinalDelayDetails=" + (finalDelayDetails ?? string.Empty) +
                    " Source=" + Normalize(source) + ".",
                    ex);
            }
        }

        private static void ProcessListedServerFinishedLoadingValidation(
            NetworkCommunicator networkPeer,
            int finishedLoadingBattleIndex,
            DateTime startedUtc,
            string delayDetails,
            string source)
        {
            if (networkPeer == null || networkPeer.IsServerPeer)
                return;

            Mission currentMission = Mission.Current;
            int authoritativeToken = ResolveListedShellMissionSessionToken(currentMission);
            bool shouldUnload = currentMission == null || authoritativeToken != finishedLoadingBattleIndex;

            Debug.Print("Server: " + networkPeer.UserName + " has finished loading explicit listed shell.");

            CompleteListedPeerFinishedLoading(
                networkPeer,
                shouldUnload,
                "ListedShellSessionTransportRuntime.ProcessListedServerFinishedLoadingValidation");

            ModLogger.Info(
                "ListedShellSessionTransportRuntime: processed listed FinishedLoading validation via authoritative mission-session token. " +
                "Peer=" + (networkPeer.UserName ?? "unknown") +
                " DeferredForMs=" + (DateTime.UtcNow - startedUtc).TotalMilliseconds.ToString("0") +
                " DelayDetails=" + (delayDetails ?? string.Empty) +
                " MissionScene=" + (currentMission?.SceneName ?? "null") +
                " MissionState=" + (currentMission?.CurrentState.ToString() ?? "null") +
                " MissionSessionToken=" + authoritativeToken +
                " FinishedLoadingBattleIndex=" + finishedLoadingBattleIndex +
                " Action=" + (shouldUnload ? "UnloadMission" : "ClientFinishedLoading") +
                " Source=" + Normalize(source) + ".");
        }

        private static int ResolveListedShellMissionSessionToken(Mission mission)
        {
            if (ListedShellMissionSessionState.TryResolveTransportToken(mission, out int token))
                return token;

            return 0;
        }
    }
}
