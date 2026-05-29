using NetworkMessages.FromServer;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Infrastructure
{
    internal static class CoopSessionTransportPrimitives
    {
        public static void StartClientTransport(string address, int port, int sessionKey, int peerIndex, string source)
        {
            string finalAddress = address;
            bool selfJoinRedirect = HostSelfJoinRedirectState.TryConsumeLoopbackRewrite(
                ref finalAddress,
                port,
                "CoopSessionTransportPrimitives.StartClientTransport");

            GameNetwork.StartMultiplayerOnClient(finalAddress, port, sessionKey, peerIndex);

            ModLogger.Info(
                "CoopSessionTransportPrimitives: started client transport. " +
                "OriginalAddress=" + (address ?? string.Empty) +
                " FinalAddress=" + (finalAddress ?? string.Empty) +
                " Port=" + port +
                " SessionKey=" + sessionKey +
                " PeerIndex=" + peerIndex +
                " SelfJoinRedirect=" + selfJoinRedirect +
                " Source=" + (string.IsNullOrWhiteSpace(source) ? string.Empty : source.Trim()) + ".");
        }

        public static void PreStartServerTransport()
        {
            GameNetwork.PreStartMultiplayerOnServer();
        }

        public static void StartServerTransport(int port)
        {
            GameNetwork.StartMultiplayerOnServer(port);
        }

        public static void CreateServerPeer()
        {
            BannerlordNetwork.CreateServerPeer();
        }

        public static void MarkPeerFinishedLoading(NetworkCommunicator networkPeer)
        {
            if (networkPeer == null || networkPeer.IsServerPeer)
                return;

            GameNetwork.ClientFinishedLoading(networkPeer);
        }

        public static void MarkHostedLocalPeerFinishedLoading()
        {
            if (GameNetwork.IsDedicatedServer || GameNetwork.MyPeer == null)
                return;

            GameNetwork.ClientFinishedLoading(GameNetwork.MyPeer);
        }

        public static void SendUnloadMission(NetworkCommunicator networkPeer, bool unloadingForBattleIndexMismatch)
        {
            if (networkPeer == null || networkPeer.IsServerPeer)
                return;

            GameNetwork.BeginModuleEventAsServer(networkPeer);
            GameNetwork.WriteMessage(new UnloadMission(unloadingForBattleIndexMismatch));
            GameNetwork.EndModuleEventAsServer();
        }

        public static void SendServerMessage(NetworkCommunicator targetPeer, GameNetworkMessage message)
        {
            if (targetPeer == null || targetPeer.IsServerPeer || message == null)
                return;

            GameNetwork.BeginModuleEventAsServer(targetPeer);
            GameNetwork.WriteMessage(message);
            GameNetwork.EndModuleEventAsServer();
        }
    }
}
