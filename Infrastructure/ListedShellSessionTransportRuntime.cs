using System;
using System.Threading.Tasks;
using NetworkMessages.FromServer;
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

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
