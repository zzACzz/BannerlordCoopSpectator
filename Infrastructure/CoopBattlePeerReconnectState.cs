using System;
using System.Collections.Generic;
using CoopSpectator.GameMode;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class CoopBattlePeerReconnectState
    {
        private static readonly Dictionary<string, int> _lastKnownPeerIndexByPlayerId =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private static readonly Dictionary<int, string> _playerIdByPeerIndex =
            new Dictionary<int, string>();

        private static bool _peerSynchronizationHookInstalled;

        public static void EnsureHooksInstalled()
        {
            if (_peerSynchronizationHookInstalled)
                return;

            NetworkCommunicator.OnPeerSynchronized += HandlePeerSynchronized;
            _peerSynchronizationHookInstalled = true;
        }

        public static void Reset(string source)
        {
            int trackedPlayers = _lastKnownPeerIndexByPlayerId.Count;
            _lastKnownPeerIndexByPlayerId.Clear();
            _playerIdByPeerIndex.Clear();

            if (trackedPlayers <= 0)
                return;

            ModLogger.Info(
                "CoopBattlePeerReconnectState: cleared tracked reconnect identities. " +
                "TrackedPlayers=" + trackedPlayers +
                " Source=" + (source ?? "unknown"));
        }

        public static void ObserveDisconnect(NetworkCommunicator networkPeer, string source)
        {
            if (!ShouldTrackPeer(networkPeer))
                return;

            string playerIdKey = TryGetStablePlayerIdKey(networkPeer);
            if (string.IsNullOrWhiteSpace(playerIdKey))
                return;

            _playerIdByPeerIndex.Remove(networkPeer.Index);
            _lastKnownPeerIndexByPlayerId[playerIdKey] = networkPeer.Index;

            ModLogger.Info(
                "CoopBattlePeerReconnectState: recorded disconnected peer identity for reconnect migration. " +
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " PeerIndex=" + networkPeer.Index +
                " PlayerId=" + playerIdKey +
                " Source=" + (source ?? "unknown"));
        }

        public static void ObserveSynchronizedPeer(NetworkCommunicator networkPeer, string source)
        {
            if (!ShouldTrackPeer(networkPeer))
                return;

            string playerIdKey = TryGetStablePlayerIdKey(networkPeer);
            if (string.IsNullOrWhiteSpace(playerIdKey))
                return;

            int currentPeerIndex = networkPeer.Index;
            bool migrated = false;
            int previousPeerIndex = -1;
            if (_lastKnownPeerIndexByPlayerId.TryGetValue(playerIdKey, out previousPeerIndex) &&
                previousPeerIndex >= 0 &&
                previousPeerIndex != currentPeerIndex)
            {
                migrated |= CoopBattleAuthorityState.TryMigratePeerIndex(previousPeerIndex, currentPeerIndex, source);
                migrated |= CoopBattleSelectionRequestState.TryMigratePeerIndex(previousPeerIndex, currentPeerIndex, source);
                migrated |= CoopBattleSpawnRequestState.TryMigratePeerIndex(previousPeerIndex, currentPeerIndex, source);
                migrated |= CoopBattleSpawnRuntimeState.TryMigratePeerIndex(previousPeerIndex, currentPeerIndex, source);
                migrated |= CoopBattlePeerLifecycleRuntimeState.TryMigratePeerIndex(previousPeerIndex, currentPeerIndex, source);
                migrated |= CoopBattlePeerSessionState.TryMigratePeerIndex(previousPeerIndex, currentPeerIndex, source);

                _playerIdByPeerIndex.Remove(previousPeerIndex);
            }

            _lastKnownPeerIndexByPlayerId[playerIdKey] = currentPeerIndex;
            _playerIdByPeerIndex[currentPeerIndex] = playerIdKey;

            ModLogger.Info(
                "CoopBattlePeerReconnectState: observed synchronized peer. " +
                "Peer=" + (networkPeer.UserName ?? currentPeerIndex.ToString()) +
                " PeerIndex=" + currentPeerIndex +
                " PlayerId=" + playerIdKey +
                " PreviousPeerIndex=" + previousPeerIndex +
                " MigratedState=" + migrated +
                " Source=" + (source ?? "unknown"));
        }

        private static void HandlePeerSynchronized(NetworkCommunicator networkPeer)
        {
            if (!GameNetwork.IsServer)
                return;

            ObserveSynchronizedPeer(networkPeer, "NetworkCommunicator.OnPeerSynchronized");
        }

        private static bool ShouldTrackPeer(NetworkCommunicator networkPeer)
        {
            if (!GameNetwork.IsServer || networkPeer == null || networkPeer.IsServerPeer)
                return false;

            Mission mission = Mission.Current;
            return mission != null && MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName);
        }

        private static string TryGetStablePlayerIdKey(NetworkCommunicator networkPeer)
        {
            if (networkPeer?.VirtualPlayer == null)
                return null;

            object playerId = null;
            try
            {
                playerId = networkPeer.VirtualPlayer
                    .GetType()
                    .GetProperty("Id")
                    ?.GetValue(networkPeer.VirtualPlayer, null);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopBattlePeerReconnectState: failed to reflect VirtualPlayer.Id. " +
                    "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                    " Error=" + ex.Message);
            }

            string playerIdKey = playerId?.ToString();
            string peerName = networkPeer.UserName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(playerIdKey) && string.IsNullOrWhiteSpace(peerName))
                return null;

            return (playerIdKey ?? string.Empty).Trim() + "|" + peerName;
        }
    }
}
