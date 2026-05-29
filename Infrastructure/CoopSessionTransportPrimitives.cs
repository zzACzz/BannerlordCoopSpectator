using System.Reflection;
using NetworkMessages.FromServer;
using TaleWorlds.Core;
using TaleWorlds.Engine;
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

        public static void FinalizeHostedServerTransportStart(int port, bool attachHostedLocalPeer)
        {
            StartServerTransport(port);
            if (!attachHostedLocalPeer)
                return;

            CreateServerPeer();
            MarkHostedLocalPeerFinishedLoading();
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

        public static string CompletePeerFinishedLoadingTransportStep(
            NetworkCommunicator networkPeer,
            bool shouldUnload)
        {
            if (networkPeer == null || networkPeer.IsServerPeer)
                return "Skipped";

            if (shouldUnload)
            {
                SendUnloadMission(networkPeer, true);
                return "UnloadMission";
            }

            MarkPeerFinishedLoading(networkPeer);
            return "ClientFinishedLoading";
        }

        public static void MarkHostedLocalPeerFinishedLoading()
        {
            if (GameNetwork.IsDedicatedServer || GameNetwork.MyPeer == null)
                return;

            GameNetwork.ClientFinishedLoading(GameNetwork.MyPeer);
        }

        public static void MarkLocalPeerUnsynchronized()
        {
            if (GameNetwork.MyPeer != null)
                GameNetwork.MyPeer.IsSynchronized = false;
        }

        public static void BeginClientMissionReceiveTransition()
        {
            MarkLocalPeerUnsynchronized();
        }

        public static void SendUnloadMission(NetworkCommunicator networkPeer, bool unloadingForBattleIndexMismatch)
        {
            if (networkPeer == null || networkPeer.IsServerPeer)
                return;

            GameNetwork.BeginModuleEventAsServer(networkPeer);
            GameNetwork.WriteMessage(new UnloadMission(unloadingForBattleIndexMismatch));
            GameNetwork.EndModuleEventAsServer();
        }

        public static void BroadcastUnloadMission(bool unloadingForBattleIndexMismatch = false)
        {
            BroadcastServerMessage(new UnloadMission(unloadingForBattleIndexMismatch));
        }

        public static void SendExistingObjectsBegin(NetworkCommunicator targetPeer)
        {
            SendServerMessage(targetPeer, new ExistingObjectsBegin());
        }

        public static void SendSynchronizeMissionTimeTracker(NetworkCommunicator targetPeer, float missionTimeSeconds)
        {
            SendServerMessage(targetPeer, new SynchronizeMissionTimeTracker(missionTimeSeconds));
        }

        public static void SendExistingObjectsEnd(NetworkCommunicator targetPeer)
        {
            SendServerMessage(targetPeer, new ExistingObjectsEnd());
        }

        public static void SendServerMessage(NetworkCommunicator targetPeer, GameNetworkMessage message)
        {
            if (targetPeer == null || targetPeer.IsServerPeer || message == null)
                return;

            GameNetwork.BeginModuleEventAsServer(targetPeer);
            GameNetwork.WriteMessage(message);
            GameNetwork.EndModuleEventAsServer();
        }

        public static void BroadcastServerMessage(GameNetworkMessage message)
        {
            if (message == null)
                return;

            GameNetwork.BeginBroadcastModuleEvent();
            GameNetwork.WriteMessage(message);
            GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
        }

        public static void SendReflectedServerMessage(NetworkCommunicator targetPeer, MethodInfo writeMessageMethod, object message)
        {
            if (targetPeer == null || targetPeer.IsServerPeer || writeMessageMethod == null || message == null)
                return;

            GameNetwork.BeginModuleEventAsServer(targetPeer);
            writeMessageMethod.Invoke(null, new[] { message });
            GameNetwork.EndModuleEventAsServer();
        }

        public static void BroadcastReflectedServerMessage(MethodInfo writeMessageMethod, object message)
        {
            if (writeMessageMethod == null || message == null)
                return;

            GameNetwork.BeginBroadcastModuleEvent();
            writeMessageMethod.Invoke(null, new[] { message });
            GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
        }

        public static void EndClientLobbyMissionAndResetChat()
        {
            BannerlordNetwork.EndMultiplayerLobbyMission();
            Game.Current?.GetGameHandler<ChatBox>()?.ResetMuteList();
        }

        public static void BeginClientLobbyMissionUnload()
        {
            MarkLocalPeerUnsynchronized();
            EndClientLobbyMissionAndResetChat();
        }

        public static void EndServerLobbyMissionAfterUnloadBroadcast()
        {
            GameNetwork.UnSynchronizeEveryone();
            BannerlordNetwork.EndMultiplayerLobbyMission();
        }

        public static void CompleteServerLobbyMissionShutdown(bool unloadingForBattleIndexMismatch = false)
        {
            BroadcastUnloadMission(unloadingForBattleIndexMismatch);
            EndServerLobbyMissionAfterUnloadBroadcast();
        }

        public static void DisableGlobalLoadingWindow()
        {
            LoadingWindow.DisableGlobalLoadingWindow();
        }

        public static void CompleteClientLobbyMissionUnload()
        {
            DisableGlobalLoadingWindow();
        }
    }
}
