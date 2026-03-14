using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Temporary authoritative coop state for side-scoped allowed troops and per-peer troop selection.
    /// This lets coop logic stop treating TDM class state as the primary source of truth.
    /// </summary>
    internal static class CoopBattleAuthorityState
    {
        internal readonly struct PeerSelectionState
        {
            public PeerSelectionState(int peerIndex, BattleSideEnum side, string troopId)
            {
                PeerIndex = peerIndex;
                Side = side;
                TroopId = troopId;
            }

            public int PeerIndex { get; }
            public BattleSideEnum Side { get; }
            public string TroopId { get; }
        }

        private static readonly Dictionary<BattleSideEnum, List<string>> _allowedTroopIdsBySide = new Dictionary<BattleSideEnum, List<string>>();
        private static readonly Dictionary<int, string> _selectedTroopIdByPeer = new Dictionary<int, string>();
        private static readonly Dictionary<int, BattleSideEnum> _assignedSideByPeer = new Dictionary<int, BattleSideEnum>();
        private static readonly List<string> _fallbackAllowedTroopIds = new List<string>();
        private static string _fallbackSelectedTroopId;

        public static void Reset(
            IReadOnlyDictionary<BattleSideEnum, List<string>> allowedTroopIdsBySide,
            IReadOnlyList<string> fallbackAllowedTroopIds,
            string fallbackSelectedTroopId)
        {
            _allowedTroopIdsBySide.Clear();
            _selectedTroopIdByPeer.Clear();
            _assignedSideByPeer.Clear();
            _fallbackAllowedTroopIds.Clear();

            if (allowedTroopIdsBySide != null)
            {
                foreach (KeyValuePair<BattleSideEnum, List<string>> entry in allowedTroopIdsBySide)
                {
                    List<string> normalizedTroopIds = NormalizeTroopIds(entry.Value);
                    if (normalizedTroopIds.Count > 0)
                        _allowedTroopIdsBySide[entry.Key] = normalizedTroopIds;
                }
            }

            foreach (string troopId in NormalizeTroopIds(fallbackAllowedTroopIds))
                _fallbackAllowedTroopIds.Add(troopId);

            _fallbackSelectedTroopId = NormalizeTroopId(fallbackSelectedTroopId);
            if (string.IsNullOrWhiteSpace(_fallbackSelectedTroopId))
                _fallbackSelectedTroopId = _fallbackAllowedTroopIds.FirstOrDefault();
        }

        public static IReadOnlyList<string> GetAllowedTroopIds(BattleSideEnum side)
        {
            if (_allowedTroopIdsBySide.TryGetValue(side, out List<string> sideTroopIds) && sideTroopIds.Count > 0)
                return sideTroopIds.ToArray();

            if (_fallbackAllowedTroopIds.Count > 0)
                return _fallbackAllowedTroopIds.ToArray();

            return Array.Empty<string>();
        }

        public static PeerSelectionState GetSelectionState(MissionPeer missionPeer)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            int peerIndex = networkPeer?.Index ?? -1;
            BattleSideEnum side = ResolveAssignedSide(missionPeer, networkPeer);
            string troopId = ResolveSelectedTroopId(missionPeer, networkPeer, side);
            return new PeerSelectionState(peerIndex, side, troopId);
        }

        public static BattleSideEnum GetAssignedSide(MissionPeer missionPeer)
        {
            return ResolveAssignedSide(missionPeer, missionPeer?.GetNetworkPeer());
        }

        public static bool TryAssignSide(MissionPeer missionPeer, BattleSideEnum side, string source)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null || side == BattleSideEnum.None)
                return false;

            if (_assignedSideByPeer.TryGetValue(networkPeer.Index, out BattleSideEnum previousSide) && previousSide == side)
                return true;

            _assignedSideByPeer[networkPeer.Index] = side;
            ModLogger.Info(
                "CoopBattleAuthorityState: authoritative side assigned. " +
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " Side=" + side +
                " Source=" + source);
            return true;
        }

        public static string GetSelectedTroopId(MissionPeer missionPeer)
        {
            PeerSelectionState selectionState = GetSelectionState(missionPeer);
            return selectionState.TroopId;
        }

        public static bool TrySetSelectedTroopId(MissionPeer missionPeer, string troopId, string source)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return false;

            string normalizedTroopId = NormalizeTroopId(troopId);
            if (string.IsNullOrWhiteSpace(normalizedTroopId))
                return false;

            BattleSideEnum side = GetAssignedSide(missionPeer);
            IReadOnlyList<string> allowedTroopIds = GetAllowedTroopIds(side);
            if (!ContainsTroopId(allowedTroopIds, normalizedTroopId))
                return false;

            if (_selectedTroopIdByPeer.TryGetValue(networkPeer.Index, out string previousTroopId) &&
                string.Equals(previousTroopId, normalizedTroopId, StringComparison.Ordinal))
            {
                return true;
            }

            _selectedTroopIdByPeer[networkPeer.Index] = normalizedTroopId;
            ModLogger.Info(
                "CoopBattleAuthorityState: authoritative troop selection updated. " +
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " Side=" + side +
                " TroopId=" + normalizedTroopId +
                " Source=" + source);
            return true;
        }

        private static List<string> NormalizeTroopIds(IEnumerable<string> troopIds)
        {
            if (troopIds == null)
                return new List<string>();

            return troopIds
                .Select(NormalizeTroopId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string NormalizeTroopId(string troopId)
        {
            return string.IsNullOrWhiteSpace(troopId) ? null : troopId.Trim();
        }

        private static bool ContainsTroopId(IEnumerable<string> troopIds, string troopId)
        {
            return troopIds != null && troopIds.Any(candidate => string.Equals(candidate, troopId, StringComparison.Ordinal));
        }

        private static BattleSideEnum ResolveAssignedSide(MissionPeer missionPeer, NetworkCommunicator networkPeer)
        {
            if (networkPeer != null &&
                _assignedSideByPeer.TryGetValue(networkPeer.Index, out BattleSideEnum assignedSide) &&
                assignedSide != BattleSideEnum.None)
            {
                return assignedSide;
            }

            BattleSideEnum runtimeSide = missionPeer?.Team?.Side ?? BattleSideEnum.None;
            if (networkPeer != null && runtimeSide != BattleSideEnum.None)
                _assignedSideByPeer[networkPeer.Index] = runtimeSide;

            return runtimeSide;
        }

        private static string ResolveSelectedTroopId(MissionPeer missionPeer, NetworkCommunicator networkPeer, BattleSideEnum side)
        {
            IReadOnlyList<string> allowedTroopIds = GetAllowedTroopIds(side);
            if (allowedTroopIds.Count == 0)
                return _fallbackSelectedTroopId;

            if (networkPeer != null &&
                _selectedTroopIdByPeer.TryGetValue(networkPeer.Index, out string selectedTroopId) &&
                ContainsTroopId(allowedTroopIds, selectedTroopId))
            {
                return selectedTroopId;
            }

            string resolvedTroopId = allowedTroopIds.FirstOrDefault() ?? _fallbackSelectedTroopId;
            if (networkPeer != null && !string.IsNullOrWhiteSpace(resolvedTroopId))
                _selectedTroopIdByPeer[networkPeer.Index] = resolvedTroopId;

            return resolvedTroopId;
        }
    }
}
