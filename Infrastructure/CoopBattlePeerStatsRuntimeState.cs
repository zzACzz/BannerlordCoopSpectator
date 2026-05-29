using System;
using System.Collections.Generic;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal readonly struct PeerStatsRuntimeState
    {
        public PeerStatsRuntimeState(
            int peerIndex,
            int killCount,
            int assistCount,
            int deathCount,
            int score,
            string source,
            DateTime updatedUtc)
        {
            PeerIndex = peerIndex;
            KillCount = killCount;
            AssistCount = assistCount;
            DeathCount = deathCount;
            Score = score;
            Source = source;
            UpdatedUtc = updatedUtc;
        }

        public int PeerIndex { get; }
        public int KillCount { get; }
        public int AssistCount { get; }
        public int DeathCount { get; }
        public int Score { get; }
        public string Source { get; }
        public DateTime UpdatedUtc { get; }
    }

    internal static class CoopBattlePeerStatsRuntimeState
    {
        private static readonly Dictionary<int, PeerStatsRuntimeState> _statesByPeer = new Dictionary<int, PeerStatsRuntimeState>();

        public static void Reset()
        {
            _statesByPeer.Clear();
        }

        public static bool TryGetState(MissionPeer missionPeer, out PeerStatsRuntimeState state)
        {
            state = default;
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return false;

            return _statesByPeer.TryGetValue(networkPeer.Index, out state);
        }

        public static void Apply(
            MissionPeer missionPeer,
            int killCount,
            int assistCount,
            int deathCount,
            int score,
            string source)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return;

            PeerStatsRuntimeState nextState = new PeerStatsRuntimeState(
                networkPeer.Index,
                NormalizeKillOrAssistCount(killCount),
                NormalizeKillOrAssistCount(assistCount),
                NormalizeDeathCount(deathCount),
                NormalizeScore(score),
                source,
                DateTime.UtcNow);

            if (_statesByPeer.TryGetValue(networkPeer.Index, out PeerStatsRuntimeState previousState) &&
                previousState.KillCount == nextState.KillCount &&
                previousState.AssistCount == nextState.AssistCount &&
                previousState.DeathCount == nextState.DeathCount &&
                previousState.Score == nextState.Score)
            {
                return;
            }

            _statesByPeer[networkPeer.Index] = nextState;

            ModLogger.Info(
                "CoopBattlePeerStatsRuntimeState: state updated. " +
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " Kills=" + nextState.KillCount +
                " Assists=" + nextState.AssistCount +
                " Deaths=" + nextState.DeathCount +
                " Score=" + nextState.Score +
                " Source=" + (source ?? "unknown"));
        }

        public static bool TryMigratePeerIndex(int previousPeerIndex, int currentPeerIndex, string source)
        {
            if (previousPeerIndex < 0 || currentPeerIndex < 0 || previousPeerIndex == currentPeerIndex)
                return false;

            if (!_statesByPeer.TryGetValue(previousPeerIndex, out PeerStatsRuntimeState previousState))
                return false;

            PeerStatsRuntimeState migratedState = new PeerStatsRuntimeState(
                currentPeerIndex,
                previousState.KillCount,
                previousState.AssistCount,
                previousState.DeathCount,
                previousState.Score,
                previousState.Source,
                previousState.UpdatedUtc);
            _statesByPeer.Remove(previousPeerIndex);
            _statesByPeer[currentPeerIndex] = migratedState;

            ModLogger.Info(
                "CoopBattlePeerStatsRuntimeState: migrated reconnect peer stats. " +
                "PreviousPeerIndex=" + previousPeerIndex +
                " CurrentPeerIndex=" + currentPeerIndex +
                " Kills=" + migratedState.KillCount +
                " Assists=" + migratedState.AssistCount +
                " Deaths=" + migratedState.DeathCount +
                " Score=" + migratedState.Score +
                " Source=" + (source ?? "unknown"));
            return true;
        }

        private static int NormalizeKillOrAssistCount(int value)
        {
            return Math.Max(-1000, Math.Min(100000, value));
        }

        private static int NormalizeDeathCount(int value)
        {
            return Math.Max(-1000, Math.Min(100000, value));
        }

        private static int NormalizeScore(int value)
        {
            return Math.Max(-1000000, Math.Min(1000000, value));
        }
    }
}
