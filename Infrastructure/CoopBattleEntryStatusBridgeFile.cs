using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoopSpectator.Infrastructure
{
    public static class CoopBattleEntryStatusBridgeFile
    {
        private const string CoopSpectatorSubFolder = "CoopSpectator";
        private const string StatusFileName = "battle_entry_status.txt";
        private const char EncodedListSeparator = '|';
        private static readonly object SnapshotCacheLock = new object();
        private static EntryStatusSnapshot _lastValidSnapshot;

        public sealed class EntryStatusSnapshot
        {
            public string MissionName { get; set; }
            public string Source { get; set; }
            public string BattlePhase { get; set; }
            public string BattlePhaseSource { get; set; }
            public string WinnerSide { get; set; }
            public string BattleCompletionReason { get; set; }
            public string PeerName { get; set; }
            public int PeerIndex { get; set; }
            public bool HasPeer { get; set; }
            public bool HasAgent { get; set; }
            public bool BattleDataReady { get; set; }
            public string BattleDataReadinessStage { get; set; }
            public string BattleDataReadinessReason { get; set; }
            public bool CanRespawn { get; set; }
            public bool CanStartBattle { get; set; }
            public string LifecycleState { get; set; }
            public string LifecycleSource { get; set; }
            public int DeathCount { get; set; }
            public string RequestedSide { get; set; }
            public string AssignedSide { get; set; }
            public string SelectedTroopId { get; set; }
            public string SelectedEntryId { get; set; }
            public string IntentSide { get; set; }
            public string IntentTroopOrEntryId { get; set; }
            public string SelectionRequestSide { get; set; }
            public string SelectionRequestTroopId { get; set; }
            public string SelectionRequestEntryId { get; set; }
            public string SpawnRequestSide { get; set; }
            public string SpawnRequestTroopId { get; set; }
            public string SpawnRequestEntryId { get; set; }
            public string SpawnStatus { get; set; }
            public string SpawnReason { get; set; }
            public string AllowedTroopIds { get; set; }
            public string AllowedEntryIds { get; set; }
            public string SelectableEntryIds { get; set; }
            public string SelectableEntrySource { get; set; }
            public string AuthoritativeCompatibilityEntryId { get; set; }
            public string AuthoritativeCompatibilityEntrySource { get; set; }
            public string AuthoritativeCompatibilityStatus { get; set; }
            public bool AuthoritativeWeaponContractSupported { get; set; }
            public bool AuthoritativeVisualContractSupported { get; set; }
            public string AuthoritativeCompatibilitySummary { get; set; }
            public int AuthoritativeMaterializedAgentEntryCount { get; set; }
            public string AuthoritativeMaterializedAgentEntries { get; set; }
            public string AttackerAllowedTroopIds { get; set; }
            public string AttackerAllowedEntryIds { get; set; }
            public int AttackerSelectableEntryCount { get; set; }
            public string AttackerSelectableEntryIds { get; set; }
            public string AttackerSelectableEntrySource { get; set; }
            public string DefenderAllowedTroopIds { get; set; }
            public string DefenderAllowedEntryIds { get; set; }
            public int DefenderSelectableEntryCount { get; set; }
            public string DefenderSelectableEntryIds { get; set; }
            public string DefenderSelectableEntrySource { get; set; }
            public DateTime UpdatedUtc { get; set; }
        }

        public sealed class AuthoritativeMaterializedAgentEntrySnapshot
        {
            public string BattleId { get; set; }
            public string MissionName { get; set; }
            public string Source { get; set; }
            public bool UseStringIdExactEquipmentPath { get; set; }
            public int EntryCount { get; set; }
            public string AgentEntries { get; set; }
            public DateTime UpdatedUtc { get; set; }
        }

        public static string SerializeIdList(IEnumerable<string> values)
        {
            if (values == null)
                return string.Empty;

            List<string> encodedValues = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Uri.EscapeDataString(value.Trim()))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return encodedValues.Count == 0
                ? string.Empty
                : string.Join(EncodedListSeparator.ToString(), encodedValues);
        }

        public static string[] DeserializeIdList(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return Array.Empty<string>();

            char[] separators = rawValue.IndexOf(EncodedListSeparator) >= 0
                ? new[] { EncodedListSeparator }
                : new[] { ',', ';' };

            List<string> decodedValues = new List<string>();
            foreach (string rawToken in rawValue.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = rawToken?.Trim();
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                string decodedToken;
                try
                {
                    decodedToken = Uri.UnescapeDataString(token);
                }
                catch
                {
                    decodedToken = token;
                }

                if (!string.IsNullOrWhiteSpace(decodedToken))
                    decodedValues.Add(decodedToken.Trim());
            }

            return decodedValues
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        public static string SerializeAgentEntryMap(IEnumerable<KeyValuePair<int, string>> values)
        {
            if (values == null)
                return string.Empty;

            List<string> encodedValues = values
                .Where(pair => pair.Key >= 0 && !string.IsNullOrWhiteSpace(pair.Value))
                .GroupBy(pair => pair.Key)
                .OrderBy(group => group.Key)
                .Select(group => group.Last())
                .Select(pair => Uri.EscapeDataString(pair.Key.ToString() + ":" + pair.Value.Trim()))
                .ToList();
            return encodedValues.Count == 0
                ? string.Empty
                : string.Join(EncodedListSeparator.ToString(), encodedValues);
        }

        public static Dictionary<int, string> DeserializeAgentEntryMap(string rawValue)
        {
            var mappings = new Dictionary<int, string>();
            foreach (string token in DeserializeIdList(rawValue))
            {
                int separatorIndex = token.IndexOf(':');
                if (separatorIndex <= 0 || separatorIndex >= token.Length - 1)
                    continue;

                string indexText = token.Substring(0, separatorIndex).Trim();
                string entryId = token.Substring(separatorIndex + 1).Trim();
                if (!int.TryParse(indexText, out int agentIndex) ||
                    agentIndex < 0 ||
                    string.IsNullOrWhiteSpace(entryId))
                {
                    continue;
                }

                mappings[agentIndex] = entryId;
            }

            return mappings;
        }

        public static void WriteStatus(EntryStatusSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            try
            {
                Directory.CreateDirectory(GetCoopFolderPath());
                string[] lines =
                {
                    "MissionName=" + (snapshot.MissionName ?? string.Empty),
                    "Source=" + (snapshot.Source ?? string.Empty),
                    "BattlePhase=" + (snapshot.BattlePhase ?? string.Empty),
                    "BattlePhaseSource=" + (snapshot.BattlePhaseSource ?? string.Empty),
                    "WinnerSide=" + (snapshot.WinnerSide ?? string.Empty),
                    "BattleCompletionReason=" + (snapshot.BattleCompletionReason ?? string.Empty),
                    "PeerName=" + (snapshot.PeerName ?? string.Empty),
                    "PeerIndex=" + snapshot.PeerIndex,
                    "HasPeer=" + snapshot.HasPeer,
                    "HasAgent=" + snapshot.HasAgent,
                    "BattleDataReady=" + snapshot.BattleDataReady,
                    "BattleDataReadinessStage=" + (snapshot.BattleDataReadinessStage ?? string.Empty),
                    "BattleDataReadinessReason=" + (snapshot.BattleDataReadinessReason ?? string.Empty),
                    "CanRespawn=" + snapshot.CanRespawn,
                    "CanStartBattle=" + snapshot.CanStartBattle,
                    "LifecycleState=" + (snapshot.LifecycleState ?? string.Empty),
                    "LifecycleSource=" + (snapshot.LifecycleSource ?? string.Empty),
                    "DeathCount=" + snapshot.DeathCount,
                    "RequestedSide=" + (snapshot.RequestedSide ?? string.Empty),
                    "AssignedSide=" + (snapshot.AssignedSide ?? string.Empty),
                    "SelectedTroopId=" + (snapshot.SelectedTroopId ?? string.Empty),
                    "SelectedEntryId=" + (snapshot.SelectedEntryId ?? string.Empty),
                    "IntentSide=" + (snapshot.IntentSide ?? string.Empty),
                    "IntentTroopOrEntryId=" + (snapshot.IntentTroopOrEntryId ?? string.Empty),
                    "SelectionRequestSide=" + (snapshot.SelectionRequestSide ?? string.Empty),
                    "SelectionRequestTroopId=" + (snapshot.SelectionRequestTroopId ?? string.Empty),
                    "SelectionRequestEntryId=" + (snapshot.SelectionRequestEntryId ?? string.Empty),
                    "SpawnRequestSide=" + (snapshot.SpawnRequestSide ?? string.Empty),
                    "SpawnRequestTroopId=" + (snapshot.SpawnRequestTroopId ?? string.Empty),
                    "SpawnRequestEntryId=" + (snapshot.SpawnRequestEntryId ?? string.Empty),
                    "SpawnStatus=" + (snapshot.SpawnStatus ?? string.Empty),
                    "SpawnReason=" + (snapshot.SpawnReason ?? string.Empty),
                    "AllowedTroopIds=" + (snapshot.AllowedTroopIds ?? string.Empty),
                    "AllowedEntryIds=" + (snapshot.AllowedEntryIds ?? string.Empty),
                    "SelectableEntryIds=" + (snapshot.SelectableEntryIds ?? string.Empty),
                    "SelectableEntrySource=" + (snapshot.SelectableEntrySource ?? string.Empty),
                    "AuthoritativeCompatibilityEntryId=" + (snapshot.AuthoritativeCompatibilityEntryId ?? string.Empty),
                    "AuthoritativeCompatibilityEntrySource=" + (snapshot.AuthoritativeCompatibilityEntrySource ?? string.Empty),
                    "AuthoritativeCompatibilityStatus=" + (snapshot.AuthoritativeCompatibilityStatus ?? string.Empty),
                    "AuthoritativeWeaponContractSupported=" + snapshot.AuthoritativeWeaponContractSupported,
                    "AuthoritativeVisualContractSupported=" + snapshot.AuthoritativeVisualContractSupported,
                    "AuthoritativeCompatibilitySummary=" + (snapshot.AuthoritativeCompatibilitySummary ?? string.Empty),
                    "AuthoritativeMaterializedAgentEntryCount=" + snapshot.AuthoritativeMaterializedAgentEntryCount,
                    "AuthoritativeMaterializedAgentEntries=" + (snapshot.AuthoritativeMaterializedAgentEntries ?? string.Empty),
                    "AttackerAllowedTroopIds=" + (snapshot.AttackerAllowedTroopIds ?? string.Empty),
                    "AttackerAllowedEntryIds=" + (snapshot.AttackerAllowedEntryIds ?? string.Empty),
                    "AttackerSelectableEntryCount=" + snapshot.AttackerSelectableEntryCount,
                    "AttackerSelectableEntryIds=" + (snapshot.AttackerSelectableEntryIds ?? string.Empty),
                    "AttackerSelectableEntrySource=" + (snapshot.AttackerSelectableEntrySource ?? string.Empty),
                    "DefenderAllowedTroopIds=" + (snapshot.DefenderAllowedTroopIds ?? string.Empty),
                    "DefenderAllowedEntryIds=" + (snapshot.DefenderAllowedEntryIds ?? string.Empty),
                    "DefenderSelectableEntryCount=" + snapshot.DefenderSelectableEntryCount,
                    "DefenderSelectableEntryIds=" + (snapshot.DefenderSelectableEntryIds ?? string.Empty),
                    "DefenderSelectableEntrySource=" + (snapshot.DefenderSelectableEntrySource ?? string.Empty),
                    "UpdatedUtc=" + snapshot.UpdatedUtc.ToString("O")
                };
                AtomicBridgeFileIO.WriteAllLines(GetStatusFilePath(), lines);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleEntryStatusBridgeFile: failed to write status file: " + ex.Message);
            }
        }

        public static EntryStatusSnapshot ReadStatus()
        {
            try
            {
                string path = GetStatusFilePath();
                if (!File.Exists(path))
                    return null;

                string[] lines = AtomicBridgeFileIO.ReadAllLinesShared(path);
                EntryStatusSnapshot snapshot = new EntryStatusSnapshot
                {
                    MissionName = string.Empty,
                    Source = string.Empty,
                    BattlePhase = string.Empty,
                    BattlePhaseSource = string.Empty,
                    WinnerSide = string.Empty,
                    BattleCompletionReason = string.Empty,
                    PeerName = string.Empty,
                    PeerIndex = -1,
                    HasPeer = false,
                    HasAgent = false,
                    BattleDataReady = false,
                    BattleDataReadinessStage = string.Empty,
                    BattleDataReadinessReason = string.Empty,
                    CanRespawn = false,
                    CanStartBattle = false,
                    LifecycleState = string.Empty,
                    LifecycleSource = string.Empty,
                    DeathCount = 0,
                    RequestedSide = string.Empty,
                    AssignedSide = string.Empty,
                    SelectedTroopId = string.Empty,
                    SelectedEntryId = string.Empty,
                    IntentSide = string.Empty,
                    IntentTroopOrEntryId = string.Empty,
                    SelectionRequestSide = string.Empty,
                    SelectionRequestTroopId = string.Empty,
                    SelectionRequestEntryId = string.Empty,
                    SpawnRequestSide = string.Empty,
                    SpawnRequestTroopId = string.Empty,
                    SpawnRequestEntryId = string.Empty,
                    SpawnStatus = string.Empty,
                    SpawnReason = string.Empty,
                    AllowedTroopIds = string.Empty,
                    AllowedEntryIds = string.Empty,
                    SelectableEntryIds = string.Empty,
                    SelectableEntrySource = string.Empty,
                    AuthoritativeCompatibilityEntryId = string.Empty,
                    AuthoritativeCompatibilityEntrySource = string.Empty,
                    AuthoritativeCompatibilityStatus = string.Empty,
                    AuthoritativeWeaponContractSupported = false,
                    AuthoritativeVisualContractSupported = false,
                    AuthoritativeCompatibilitySummary = string.Empty,
                    AuthoritativeMaterializedAgentEntryCount = 0,
                    AuthoritativeMaterializedAgentEntries = string.Empty,
                    AttackerAllowedTroopIds = string.Empty,
                    AttackerAllowedEntryIds = string.Empty,
                    AttackerSelectableEntryIds = string.Empty,
                    AttackerSelectableEntrySource = string.Empty,
                    DefenderAllowedTroopIds = string.Empty,
                    DefenderAllowedEntryIds = string.Empty,
                    DefenderSelectableEntryIds = string.Empty,
                    DefenderSelectableEntrySource = string.Empty,
                    UpdatedUtc = DateTime.MinValue
                };

                foreach (string rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    int separatorIndex = rawLine.IndexOf('=');
                    if (separatorIndex <= 0)
                        continue;

                    string key = rawLine.Substring(0, separatorIndex).Trim();
                    string value = rawLine.Substring(separatorIndex + 1).Trim();
                    switch (key)
                    {
                        case "MissionName":
                            snapshot.MissionName = value;
                            break;
                        case "Source":
                            snapshot.Source = value;
                            break;
                        case "BattlePhase":
                            snapshot.BattlePhase = value;
                            break;
                        case "BattlePhaseSource":
                            snapshot.BattlePhaseSource = value;
                            break;
                        case "WinnerSide":
                            snapshot.WinnerSide = value;
                            break;
                        case "BattleCompletionReason":
                            snapshot.BattleCompletionReason = value;
                            break;
                        case "PeerName":
                            snapshot.PeerName = value;
                            break;
                        case "PeerIndex":
                            if (int.TryParse(value, out int peerIndex))
                                snapshot.PeerIndex = peerIndex;
                            break;
                        case "HasPeer":
                            if (bool.TryParse(value, out bool hasPeer))
                                snapshot.HasPeer = hasPeer;
                            break;
                        case "HasAgent":
                            if (bool.TryParse(value, out bool hasAgent))
                                snapshot.HasAgent = hasAgent;
                            break;
                        case "BattleDataReady":
                            if (bool.TryParse(value, out bool battleDataReady))
                                snapshot.BattleDataReady = battleDataReady;
                            break;
                        case "BattleDataReadinessStage":
                            snapshot.BattleDataReadinessStage = value;
                            break;
                        case "BattleDataReadinessReason":
                            snapshot.BattleDataReadinessReason = value;
                            break;
                        case "CanRespawn":
                            if (bool.TryParse(value, out bool canRespawn))
                                snapshot.CanRespawn = canRespawn;
                            break;
                        case "CanStartBattle":
                            if (bool.TryParse(value, out bool canStartBattle))
                                snapshot.CanStartBattle = canStartBattle;
                            break;
                        case "LifecycleState":
                            snapshot.LifecycleState = value;
                            break;
                        case "LifecycleSource":
                            snapshot.LifecycleSource = value;
                            break;
                        case "DeathCount":
                            if (int.TryParse(value, out int deathCount))
                                snapshot.DeathCount = deathCount;
                            break;
                        case "RequestedSide":
                            snapshot.RequestedSide = value;
                            break;
                        case "AssignedSide":
                            snapshot.AssignedSide = value;
                            break;
                        case "SelectedTroopId":
                            snapshot.SelectedTroopId = value;
                            break;
                        case "SelectedEntryId":
                            snapshot.SelectedEntryId = value;
                            break;
                        case "IntentSide":
                            snapshot.IntentSide = value;
                            break;
                        case "IntentTroopOrEntryId":
                            snapshot.IntentTroopOrEntryId = value;
                            break;
                        case "SelectionRequestSide":
                            snapshot.SelectionRequestSide = value;
                            break;
                        case "SelectionRequestTroopId":
                            snapshot.SelectionRequestTroopId = value;
                            break;
                        case "SelectionRequestEntryId":
                            snapshot.SelectionRequestEntryId = value;
                            break;
                        case "SpawnRequestSide":
                            snapshot.SpawnRequestSide = value;
                            break;
                        case "SpawnRequestTroopId":
                            snapshot.SpawnRequestTroopId = value;
                            break;
                        case "SpawnRequestEntryId":
                            snapshot.SpawnRequestEntryId = value;
                            break;
                        case "SpawnStatus":
                            snapshot.SpawnStatus = value;
                            break;
                        case "SpawnReason":
                            snapshot.SpawnReason = value;
                            break;
                        case "AllowedTroopIds":
                            snapshot.AllowedTroopIds = value;
                            break;
                        case "AllowedEntryIds":
                            snapshot.AllowedEntryIds = value;
                            break;
                        case "SelectableEntryIds":
                            snapshot.SelectableEntryIds = value;
                            break;
                        case "SelectableEntrySource":
                            snapshot.SelectableEntrySource = value;
                            break;
                        case "AuthoritativeCompatibilityEntryId":
                            snapshot.AuthoritativeCompatibilityEntryId = value;
                            break;
                        case "AuthoritativeCompatibilityEntrySource":
                            snapshot.AuthoritativeCompatibilityEntrySource = value;
                            break;
                        case "AuthoritativeCompatibilityStatus":
                            snapshot.AuthoritativeCompatibilityStatus = value;
                            break;
                        case "AuthoritativeWeaponContractSupported":
                            if (bool.TryParse(value, out bool authoritativeWeaponContractSupported))
                                snapshot.AuthoritativeWeaponContractSupported = authoritativeWeaponContractSupported;
                            break;
                        case "AuthoritativeVisualContractSupported":
                            if (bool.TryParse(value, out bool authoritativeVisualContractSupported))
                                snapshot.AuthoritativeVisualContractSupported = authoritativeVisualContractSupported;
                            break;
                        case "AuthoritativeCompatibilitySummary":
                            snapshot.AuthoritativeCompatibilitySummary = value;
                            break;
                        case "AuthoritativeMaterializedAgentEntryCount":
                            if (int.TryParse(value, out int authoritativeMaterializedAgentEntryCount))
                                snapshot.AuthoritativeMaterializedAgentEntryCount = authoritativeMaterializedAgentEntryCount;
                            break;
                        case "AuthoritativeMaterializedAgentEntries":
                            snapshot.AuthoritativeMaterializedAgentEntries = value;
                            break;
                        case "AttackerAllowedTroopIds":
                            snapshot.AttackerAllowedTroopIds = value;
                            break;
                        case "AttackerAllowedEntryIds":
                            snapshot.AttackerAllowedEntryIds = value;
                            break;
                        case "AttackerSelectableEntryCount":
                            if (int.TryParse(value, out int attackerSelectableEntryCount))
                                snapshot.AttackerSelectableEntryCount = attackerSelectableEntryCount;
                            break;
                        case "AttackerSelectableEntryIds":
                            snapshot.AttackerSelectableEntryIds = value;
                            break;
                        case "AttackerSelectableEntrySource":
                            snapshot.AttackerSelectableEntrySource = value;
                            break;
                        case "DefenderAllowedTroopIds":
                            snapshot.DefenderAllowedTroopIds = value;
                            break;
                        case "DefenderAllowedEntryIds":
                            snapshot.DefenderAllowedEntryIds = value;
                            break;
                        case "DefenderSelectableEntryCount":
                            if (int.TryParse(value, out int defenderSelectableEntryCount))
                                snapshot.DefenderSelectableEntryCount = defenderSelectableEntryCount;
                            break;
                        case "DefenderSelectableEntryIds":
                            snapshot.DefenderSelectableEntryIds = value;
                            break;
                        case "DefenderSelectableEntrySource":
                            snapshot.DefenderSelectableEntrySource = value;
                            break;
                        case "UpdatedUtc":
                            if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime updatedUtc))
                                snapshot.UpdatedUtc = updatedUtc;
                            break;
                    }
                }

                if (!IsValidSnapshot(snapshot))
                    return GetLastValidSnapshot();

                RememberSnapshot(snapshot);
                return snapshot;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleEntryStatusBridgeFile: failed to read status file: " + ex.Message);
                return GetLastValidSnapshot();
            }
        }

        private static bool IsValidSnapshot(EntryStatusSnapshot snapshot)
        {
            return snapshot != null &&
                   snapshot.UpdatedUtc != DateTime.MinValue &&
                   (!string.IsNullOrWhiteSpace(snapshot.Source) ||
                    !string.IsNullOrWhiteSpace(snapshot.BattlePhase) ||
                    snapshot.PeerIndex >= 0);
        }

        private static void RememberSnapshot(EntryStatusSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            lock (SnapshotCacheLock)
            {
                _lastValidSnapshot = CloneSnapshot(snapshot);
            }
        }

        private static EntryStatusSnapshot GetLastValidSnapshot()
        {
            lock (SnapshotCacheLock)
            {
                return CloneSnapshot(_lastValidSnapshot);
            }
        }

        private static EntryStatusSnapshot CloneSnapshot(EntryStatusSnapshot snapshot)
        {
            if (snapshot == null)
                return null;

            return new EntryStatusSnapshot
            {
                MissionName = snapshot.MissionName,
                Source = snapshot.Source,
                BattlePhase = snapshot.BattlePhase,
                BattlePhaseSource = snapshot.BattlePhaseSource,
                WinnerSide = snapshot.WinnerSide,
                BattleCompletionReason = snapshot.BattleCompletionReason,
                PeerName = snapshot.PeerName,
                PeerIndex = snapshot.PeerIndex,
                HasPeer = snapshot.HasPeer,
                HasAgent = snapshot.HasAgent,
                BattleDataReady = snapshot.BattleDataReady,
                BattleDataReadinessStage = snapshot.BattleDataReadinessStage,
                BattleDataReadinessReason = snapshot.BattleDataReadinessReason,
                CanRespawn = snapshot.CanRespawn,
                CanStartBattle = snapshot.CanStartBattle,
                LifecycleState = snapshot.LifecycleState,
                LifecycleSource = snapshot.LifecycleSource,
                DeathCount = snapshot.DeathCount,
                RequestedSide = snapshot.RequestedSide,
                AssignedSide = snapshot.AssignedSide,
                SelectedTroopId = snapshot.SelectedTroopId,
                SelectedEntryId = snapshot.SelectedEntryId,
                IntentSide = snapshot.IntentSide,
                IntentTroopOrEntryId = snapshot.IntentTroopOrEntryId,
                SelectionRequestSide = snapshot.SelectionRequestSide,
                SelectionRequestTroopId = snapshot.SelectionRequestTroopId,
                SelectionRequestEntryId = snapshot.SelectionRequestEntryId,
                SpawnRequestSide = snapshot.SpawnRequestSide,
                SpawnRequestTroopId = snapshot.SpawnRequestTroopId,
                SpawnRequestEntryId = snapshot.SpawnRequestEntryId,
                SpawnStatus = snapshot.SpawnStatus,
                SpawnReason = snapshot.SpawnReason,
                AllowedTroopIds = snapshot.AllowedTroopIds,
                AllowedEntryIds = snapshot.AllowedEntryIds,
                SelectableEntryIds = snapshot.SelectableEntryIds,
                SelectableEntrySource = snapshot.SelectableEntrySource,
                AuthoritativeCompatibilityEntryId = snapshot.AuthoritativeCompatibilityEntryId,
                AuthoritativeCompatibilityEntrySource = snapshot.AuthoritativeCompatibilityEntrySource,
                AuthoritativeCompatibilityStatus = snapshot.AuthoritativeCompatibilityStatus,
                AuthoritativeWeaponContractSupported = snapshot.AuthoritativeWeaponContractSupported,
                AuthoritativeVisualContractSupported = snapshot.AuthoritativeVisualContractSupported,
                AuthoritativeCompatibilitySummary = snapshot.AuthoritativeCompatibilitySummary,
                AuthoritativeMaterializedAgentEntryCount = snapshot.AuthoritativeMaterializedAgentEntryCount,
                AuthoritativeMaterializedAgentEntries = snapshot.AuthoritativeMaterializedAgentEntries,
                AttackerAllowedTroopIds = snapshot.AttackerAllowedTroopIds,
                AttackerAllowedEntryIds = snapshot.AttackerAllowedEntryIds,
                AttackerSelectableEntryCount = snapshot.AttackerSelectableEntryCount,
                AttackerSelectableEntryIds = snapshot.AttackerSelectableEntryIds,
                AttackerSelectableEntrySource = snapshot.AttackerSelectableEntrySource,
                DefenderAllowedTroopIds = snapshot.DefenderAllowedTroopIds,
                DefenderAllowedEntryIds = snapshot.DefenderAllowedEntryIds,
                DefenderSelectableEntryCount = snapshot.DefenderSelectableEntryCount,
                DefenderSelectableEntryIds = snapshot.DefenderSelectableEntryIds,
                DefenderSelectableEntrySource = snapshot.DefenderSelectableEntrySource,
                UpdatedUtc = snapshot.UpdatedUtc
            };
        }

        public static string GetStatusFilePath()
        {
            return Path.Combine(GetCoopFolderPath(), StatusFileName);
        }

        private static string GetCoopFolderPath()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(docs, "Mount and Blade II Bannerlord", CoopSpectatorSubFolder);
        }
    }
}
