using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Temporary authoritative coop state for side-scoped allowed troops and per-peer selection.
    /// This lets coop logic stop treating TDM class state as the primary source of truth.
    /// </summary>
    internal static class CoopBattleAuthorityState
    {
        internal readonly struct PeerSelectionState
        {
            public PeerSelectionState(int peerIndex, BattleSideEnum side, string troopId, string entryId)
            {
                PeerIndex = peerIndex;
                Side = side;
                TroopId = troopId;
                EntryId = entryId;
            }

            public int PeerIndex { get; }
            public BattleSideEnum Side { get; }
            public string TroopId { get; }
            public string EntryId { get; }
        }

        private static readonly Dictionary<BattleSideEnum, List<string>> _allowedTroopIdsBySide = new Dictionary<BattleSideEnum, List<string>>();
        private static readonly Dictionary<BattleSideEnum, List<string>> _allowedEntryIdsBySide = new Dictionary<BattleSideEnum, List<string>>();
        private static readonly Dictionary<int, string> _selectedTroopIdByPeer = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> _selectedEntryIdByPeer = new Dictionary<int, string>();
        private static readonly Dictionary<int, BattleSideEnum> _assignedSideByPeer = new Dictionary<int, BattleSideEnum>();
        private static readonly List<string> _fallbackAllowedTroopIds = new List<string>();
        private static readonly List<string> _fallbackAllowedEntryIds = new List<string>();
        private static string _fallbackSelectedTroopId;
        private static string _fallbackSelectedEntryId;

        public static void Reset(
            IReadOnlyDictionary<BattleSideEnum, List<string>> allowedEntryIdsBySide,
            IReadOnlyDictionary<BattleSideEnum, List<string>> allowedTroopIdsBySide,
            IReadOnlyList<string> fallbackAllowedEntryIds,
            IReadOnlyList<string> fallbackAllowedTroopIds,
            string fallbackSelectedEntryId,
            string fallbackSelectedTroopId)
        {
            _allowedTroopIdsBySide.Clear();
            _allowedEntryIdsBySide.Clear();
            _selectedTroopIdByPeer.Clear();
            _selectedEntryIdByPeer.Clear();
            _assignedSideByPeer.Clear();
            _fallbackAllowedTroopIds.Clear();
            _fallbackAllowedEntryIds.Clear();

            if (allowedEntryIdsBySide != null)
            {
                foreach (KeyValuePair<BattleSideEnum, List<string>> entry in allowedEntryIdsBySide)
                {
                    List<string> normalizedEntryIds = NormalizeEntryIds(entry.Value);
                    if (normalizedEntryIds.Count > 0)
                        _allowedEntryIdsBySide[entry.Key] = normalizedEntryIds;
                }
            }

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

            foreach (string entryId in NormalizeEntryIds(fallbackAllowedEntryIds))
                _fallbackAllowedEntryIds.Add(entryId);

            _fallbackSelectedEntryId = NormalizeEntryId(fallbackSelectedEntryId);
            if (string.IsNullOrWhiteSpace(_fallbackSelectedEntryId))
                _fallbackSelectedEntryId = _fallbackAllowedEntryIds.FirstOrDefault();

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

        public static IReadOnlyList<string> GetAllowedEntryIds(BattleSideEnum side)
        {
            if (_allowedEntryIdsBySide.TryGetValue(side, out List<string> sideEntryIds) && sideEntryIds.Count > 0)
                return sideEntryIds.ToArray();

            if (_fallbackAllowedEntryIds.Count > 0)
                return _fallbackAllowedEntryIds.ToArray();

            return Array.Empty<string>();
        }

        public static PeerSelectionState GetSelectionState(MissionPeer missionPeer)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            int peerIndex = networkPeer?.Index ?? -1;
            BattleSideEnum side = ResolveAssignedSide(missionPeer, networkPeer);
            string troopId = ResolveSelectedTroopId(missionPeer, networkPeer, side);
            string entryId = ResolveSelectedEntryId(networkPeer, side, troopId);
            return new PeerSelectionState(peerIndex, side, troopId, entryId);
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

        public static string GetSelectedEntryId(MissionPeer missionPeer)
        {
            PeerSelectionState selectionState = GetSelectionState(missionPeer);
            return selectionState.EntryId;
        }

        public static bool HasExplicitSelection(MissionPeer missionPeer)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return false;

            return _selectedTroopIdByPeer.ContainsKey(networkPeer.Index) || _selectedEntryIdByPeer.ContainsKey(networkPeer.Index);
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

            string entryId = ResolveEntryIdForSelection(side, normalizedTroopId);
            _selectedTroopIdByPeer.TryGetValue(networkPeer.Index, out string previousTroopId);
            bool hadPreviousEntry = _selectedEntryIdByPeer.TryGetValue(networkPeer.Index, out string previousEntryId);
            bool sameTroop = string.Equals(previousTroopId, normalizedTroopId, StringComparison.Ordinal);
            bool sameEntry =
                (string.IsNullOrWhiteSpace(entryId) && !hadPreviousEntry) ||
                string.Equals(previousEntryId, entryId, StringComparison.Ordinal);
            if (sameTroop && sameEntry)
                return true;

            _selectedTroopIdByPeer[networkPeer.Index] = normalizedTroopId;
            if (!string.IsNullOrWhiteSpace(entryId))
                _selectedEntryIdByPeer[networkPeer.Index] = entryId;
            else
                _selectedEntryIdByPeer.Remove(networkPeer.Index);
            ModLogger.Info(
                "CoopBattleAuthorityState: authoritative troop selection updated. " +
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " Side=" + side +
                " TroopId=" + normalizedTroopId +
                " EntryId=" + (entryId ?? "null") +
                " Source=" + source);
            return true;
        }

        public static bool TrySetSelectedEntryId(MissionPeer missionPeer, string entryId, string source)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null || string.IsNullOrWhiteSpace(entryId))
                return false;

            BattleSideEnum side = GetAssignedSide(missionPeer);
            string canonicalSideKey = NormalizeSideKey(side);
            RosterEntryState entry = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (entry == null ||
                string.IsNullOrWhiteSpace(entry.CharacterId) ||
                string.IsNullOrWhiteSpace(canonicalSideKey) ||
                !string.Equals(entry.SideId, canonicalSideKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            IReadOnlyList<string> allowedEntryIds = GetAllowedEntryIds(side);
            if (allowedEntryIds.Count > 0 && !ContainsEntryId(allowedEntryIds, entryId))
                return false;

            IReadOnlyList<string> allowedTroopIds = GetAllowedTroopIds(side);
            if (!ContainsTroopId(allowedTroopIds, entry.CharacterId))
                return false;

            if (_selectedEntryIdByPeer.TryGetValue(networkPeer.Index, out string previousEntryId) &&
                string.Equals(previousEntryId, entryId, StringComparison.Ordinal) &&
                _selectedTroopIdByPeer.TryGetValue(networkPeer.Index, out string previousTroopId) &&
                string.Equals(previousTroopId, entry.CharacterId, StringComparison.Ordinal))
            {
                return true;
            }

            _selectedEntryIdByPeer[networkPeer.Index] = entryId;
            _selectedTroopIdByPeer[networkPeer.Index] = entry.CharacterId;
            ModLogger.Info(
                "CoopBattleAuthorityState: authoritative entry selection updated. " +
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " Side=" + side +
                " EntryId=" + entryId +
                " TroopId=" + entry.CharacterId +
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

        private static List<string> NormalizeEntryIds(IEnumerable<string> entryIds)
        {
            if (entryIds == null)
                return new List<string>();

            return entryIds
                .Select(NormalizeEntryId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string NormalizeTroopId(string troopId)
        {
            return string.IsNullOrWhiteSpace(troopId) ? null : troopId.Trim();
        }

        private static string NormalizeEntryId(string entryId)
        {
            return string.IsNullOrWhiteSpace(entryId) ? null : entryId.Trim();
        }

        private static bool ContainsTroopId(IEnumerable<string> troopIds, string troopId)
        {
            return troopIds != null && troopIds.Any(candidate => string.Equals(candidate, troopId, StringComparison.Ordinal));
        }

        private static bool ContainsEntryId(IEnumerable<string> entryIds, string entryId)
        {
            return entryIds != null && entryIds.Any(candidate => string.Equals(candidate, entryId, StringComparison.Ordinal));
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
                _selectedEntryIdByPeer.TryGetValue(networkPeer.Index, out string selectedEntryId) &&
                !string.IsNullOrWhiteSpace(selectedEntryId))
            {
                RosterEntryState selectedEntry = BattleSnapshotRuntimeState.GetEntryState(selectedEntryId);
                if (selectedEntry != null && ContainsTroopId(allowedTroopIds, selectedEntry.CharacterId))
                {
                    _selectedTroopIdByPeer[networkPeer.Index] = selectedEntry.CharacterId;
                    return selectedEntry.CharacterId;
                }
            }

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

        private static string ResolveSelectedEntryId(NetworkCommunicator networkPeer, BattleSideEnum side, string troopId)
        {
            IReadOnlyList<string> allowedEntryIds = GetAllowedEntryIds(side);
            if (networkPeer != null &&
                _selectedEntryIdByPeer.TryGetValue(networkPeer.Index, out string selectedEntryId) &&
                !string.IsNullOrWhiteSpace(selectedEntryId))
            {
                RosterEntryState selectedEntry = BattleSnapshotRuntimeState.GetEntryState(selectedEntryId);
                string canonicalSideKey = NormalizeSideKey(side);
                if (selectedEntry != null &&
                    (allowedEntryIds.Count == 0 || ContainsEntryId(allowedEntryIds, selectedEntryId)) &&
                    string.Equals(selectedEntry.SideId, canonicalSideKey, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(selectedEntry.CharacterId, troopId, StringComparison.OrdinalIgnoreCase))
                {
                    return selectedEntryId;
                }
            }

            string resolvedEntryId = ResolveEntryIdForSelection(side, troopId);
            if (!string.IsNullOrWhiteSpace(resolvedEntryId) &&
                allowedEntryIds.Count > 0 &&
                !ContainsEntryId(allowedEntryIds, resolvedEntryId))
            {
                resolvedEntryId = allowedEntryIds.FirstOrDefault() ?? _fallbackSelectedEntryId;
            }

            if (networkPeer != null && !string.IsNullOrWhiteSpace(resolvedEntryId))
                _selectedEntryIdByPeer[networkPeer.Index] = resolvedEntryId;

            return resolvedEntryId;
        }

        private static string ResolveEntryIdForSelection(BattleSideEnum side, string troopId)
        {
            string canonicalSideKey = NormalizeSideKey(side);
            if (string.IsNullOrWhiteSpace(canonicalSideKey) || string.IsNullOrWhiteSpace(troopId))
                return null;

            return BattleSnapshotRuntimeState.TryResolveEntryId(canonicalSideKey, troopId);
        }

        private static string NormalizeSideKey(BattleSideEnum side)
        {
            switch (side)
            {
                case BattleSideEnum.Attacker:
                    return "attacker";
                case BattleSideEnum.Defender:
                    return "defender";
                default:
                    return null;
            }
        }
    }
}
