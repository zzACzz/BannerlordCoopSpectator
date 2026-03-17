using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal enum CoopBattlePeerLifecycleStatus
    {
        None = 0,
        NoPeer = 1,
        NoSide = 2,
        AwaitingSelection = 3,
        SpawnQueued = 4,
        Alive = 5,
        DeadAwaitingRespawn = 6,
        Respawnable = 7,
        Waiting = 8,
    }

    internal readonly struct PeerLifecycleRuntimeState
    {
        public PeerLifecycleRuntimeState(
            int peerIndex,
            BattleSideEnum side,
            string troopId,
            string entryId,
            CoopBattlePeerLifecycleStatus status,
            string source,
            int deathCount,
            DateTime updatedUtc)
        {
            PeerIndex = peerIndex;
            Side = side;
            TroopId = troopId;
            EntryId = entryId;
            Status = status;
            Source = source;
            DeathCount = deathCount;
            UpdatedUtc = updatedUtc;
        }

        public int PeerIndex { get; }
        public BattleSideEnum Side { get; }
        public string TroopId { get; }
        public string EntryId { get; }
        public CoopBattlePeerLifecycleStatus Status { get; }
        public string Source { get; }
        public int DeathCount { get; }
        public DateTime UpdatedUtc { get; }
    }

    internal static class CoopBattlePeerLifecycleRuntimeState
    {
        private static readonly Dictionary<int, PeerLifecycleRuntimeState> _statesByPeer = new Dictionary<int, PeerLifecycleRuntimeState>();

        public static void Reset()
        {
            _statesByPeer.Clear();
        }

        public static bool TryGetState(MissionPeer missionPeer, out PeerLifecycleRuntimeState state)
        {
            state = default;
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return false;

            return _statesByPeer.TryGetValue(networkPeer.Index, out state);
        }

        public static void Clear(MissionPeer missionPeer, string source)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return;

            if (!_statesByPeer.Remove(networkPeer.Index))
                return;

            ModLogger.Info(
                "CoopBattlePeerLifecycleRuntimeState: cleared. " +
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " Source=" + source);
        }

        public static void MarkNoPeer(int peerIndex, string source)
        {
            SetState(peerIndex, BattleSideEnum.None, null, null, CoopBattlePeerLifecycleStatus.NoPeer, source, deathCountOverride: null);
        }

        public static void MarkNoSide(MissionPeer missionPeer, BattleSideEnum side, string source)
        {
            SetState(missionPeer, side, null, null, CoopBattlePeerLifecycleStatus.NoSide, source, incrementDeathCount: false);
        }

        public static void MarkAwaitingSelection(MissionPeer missionPeer, BattleSideEnum side, string source)
        {
            SetState(missionPeer, side, null, null, CoopBattlePeerLifecycleStatus.AwaitingSelection, source, incrementDeathCount: false);
        }

        public static void MarkSpawnQueued(MissionPeer missionPeer, string troopId, string entryId, string source)
        {
            BattleSideEnum side = CoopBattleAuthorityState.GetSelectionState(missionPeer).Side;
            SetState(missionPeer, side, troopId, entryId, CoopBattlePeerLifecycleStatus.SpawnQueued, source, incrementDeathCount: false);
        }

        public static void MarkAlive(MissionPeer missionPeer, string troopId, string entryId, string source)
        {
            BattleSideEnum side = CoopBattleAuthorityState.GetSelectionState(missionPeer).Side;
            SetState(missionPeer, side, troopId, entryId, CoopBattlePeerLifecycleStatus.Alive, source, incrementDeathCount: false);
        }

        public static void MarkDeadAwaitingRespawn(MissionPeer missionPeer, string troopId, string entryId, string source)
        {
            BattleSideEnum side = CoopBattleAuthorityState.GetSelectionState(missionPeer).Side;
            SetState(missionPeer, side, troopId, entryId, CoopBattlePeerLifecycleStatus.DeadAwaitingRespawn, source, incrementDeathCount: true);
        }

        public static void MarkRespawnable(MissionPeer missionPeer, string troopId, string entryId, string source)
        {
            BattleSideEnum side = CoopBattleAuthorityState.GetSelectionState(missionPeer).Side;
            SetState(missionPeer, side, troopId, entryId, CoopBattlePeerLifecycleStatus.Respawnable, source, incrementDeathCount: false);
        }

        public static void MarkWaiting(MissionPeer missionPeer, BattleSideEnum side, string troopId, string entryId, string source)
        {
            SetState(missionPeer, side, troopId, entryId, CoopBattlePeerLifecycleStatus.Waiting, source, incrementDeathCount: false);
        }

        private static void SetState(
            MissionPeer missionPeer,
            BattleSideEnum side,
            string troopId,
            string entryId,
            CoopBattlePeerLifecycleStatus status,
            string source,
            bool incrementDeathCount)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return;

            int? deathCountOverride = null;
            if (_statesByPeer.TryGetValue(networkPeer.Index, out PeerLifecycleRuntimeState previousState))
                deathCountOverride = incrementDeathCount ? previousState.DeathCount + 1 : previousState.DeathCount;
            else if (incrementDeathCount)
                deathCountOverride = 1;

            SetState(networkPeer.Index, side, troopId, entryId, status, source, deathCountOverride);
        }

        private static void SetState(
            int peerIndex,
            BattleSideEnum side,
            string troopId,
            string entryId,
            CoopBattlePeerLifecycleStatus status,
            string source,
            int? deathCountOverride)
        {
            int deathCount = deathCountOverride ?? (_statesByPeer.TryGetValue(peerIndex, out PeerLifecycleRuntimeState previousState) ? previousState.DeathCount : 0);
            PeerLifecycleRuntimeState nextState = new PeerLifecycleRuntimeState(
                peerIndex,
                side,
                string.IsNullOrWhiteSpace(troopId) ? null : troopId.Trim(),
                string.IsNullOrWhiteSpace(entryId) ? null : entryId.Trim(),
                status,
                source,
                deathCount,
                DateTime.UtcNow);

            if (_statesByPeer.TryGetValue(peerIndex, out PeerLifecycleRuntimeState previous) &&
                previous.Status == nextState.Status &&
                previous.Side == nextState.Side &&
                string.Equals(previous.TroopId, nextState.TroopId, StringComparison.Ordinal) &&
                string.Equals(previous.EntryId, nextState.EntryId, StringComparison.Ordinal) &&
                previous.DeathCount == nextState.DeathCount)
            {
                return;
            }

            _statesByPeer[peerIndex] = nextState;

            ModLogger.Info(
                "CoopBattlePeerLifecycleRuntimeState: state updated. " +
                "Peer=" + peerIndex +
                " Status=" + status +
                " Side=" + side +
                " TroopId=" + (nextState.TroopId ?? "null") +
                " EntryId=" + (nextState.EntryId ?? "null") +
                " Deaths=" + nextState.DeathCount +
                " Source=" + source);
        }
    }
}
