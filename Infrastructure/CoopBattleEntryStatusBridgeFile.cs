using System;
using System.Collections.Generic;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    public static class CoopBattleEntryStatusBridgeFile
    {
        private const string CoopSpectatorSubFolder = "CoopSpectator";
        private const string StatusFileName = "battle_entry_status.txt";

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
            public string AttackerAllowedTroopIds { get; set; }
            public string AttackerAllowedEntryIds { get; set; }
            public string DefenderAllowedTroopIds { get; set; }
            public string DefenderAllowedEntryIds { get; set; }
            public DateTime UpdatedUtc { get; set; }
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
                    "AttackerAllowedTroopIds=" + (snapshot.AttackerAllowedTroopIds ?? string.Empty),
                    "AttackerAllowedEntryIds=" + (snapshot.AttackerAllowedEntryIds ?? string.Empty),
                    "DefenderAllowedTroopIds=" + (snapshot.DefenderAllowedTroopIds ?? string.Empty),
                    "DefenderAllowedEntryIds=" + (snapshot.DefenderAllowedEntryIds ?? string.Empty),
                    "UpdatedUtc=" + snapshot.UpdatedUtc.ToString("O")
                };
                string path = GetStatusFilePath();
                using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                using (StreamWriter writer = new StreamWriter(stream))
                {
                    foreach (string line in lines)
                        writer.WriteLine(line);
                }
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

                string[] lines;
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(stream))
                {
                    List<string> collectedLines = new List<string>();
                    while (!reader.EndOfStream)
                        collectedLines.Add(reader.ReadLine());
                    lines = collectedLines.ToArray();
                }
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
                    AttackerAllowedTroopIds = string.Empty,
                    AttackerAllowedEntryIds = string.Empty,
                    DefenderAllowedTroopIds = string.Empty,
                    DefenderAllowedEntryIds = string.Empty,
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
                        case "AttackerAllowedTroopIds":
                            snapshot.AttackerAllowedTroopIds = value;
                            break;
                        case "AttackerAllowedEntryIds":
                            snapshot.AttackerAllowedEntryIds = value;
                            break;
                        case "DefenderAllowedTroopIds":
                            snapshot.DefenderAllowedTroopIds = value;
                            break;
                        case "DefenderAllowedEntryIds":
                            snapshot.DefenderAllowedEntryIds = value;
                            break;
                        case "UpdatedUtc":
                            if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime updatedUtc))
                                snapshot.UpdatedUtc = updatedUtc;
                            break;
                    }
                }

                return snapshot;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleEntryStatusBridgeFile: failed to read status file: " + ex.Message);
                return null;
            }
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
