using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;

namespace CoopSpectator.Commands
{
    public static class CoopBattleSelectionConsoleCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("select_side", "coop")]
        public static string SelectSide(List<string> args)
        {
            if (args == null || args.Count < 1)
                return "ERROR: Usage: coop.select_side attacker|defender";

            string side = args[0]?.Trim();
            if (string.IsNullOrWhiteSpace(side))
                return "ERROR: Side is required.";

            bool written = CoopBattleSelectionBridgeFile.WriteSelectSideRequest(side, "SP console");
            return written
                ? "Selection side request queued: " + side
                : "ERROR: Failed to write side selection request.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("select_troop", "coop")]
        public static string SelectTroop(List<string> args)
        {
            if (args == null || args.Count < 1)
                return "ERROR: Usage: coop.select_troop <troopId|entryId>";

            string troopOrEntryId = args[0]?.Trim();
            if (string.IsNullOrWhiteSpace(troopOrEntryId))
                return "ERROR: troopId or entryId is required.";

            bool written = CoopBattleSelectionBridgeFile.WriteSelectTroopRequest(troopOrEntryId, "SP console");
            return written
                ? "Selection troop request queued: " + troopOrEntryId
                : "ERROR: Failed to write troop selection request.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("spawn_now", "coop")]
        public static string SpawnNow(List<string> args)
        {
            bool written = CoopBattleSpawnBridgeFile.WriteSpawnNowRequest("SP console");
            return written
                ? "Spawn request queued."
                : "ERROR: Failed to write spawn request.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("force_respawnable", "coop")]
        public static string ForceRespawnable(List<string> args)
        {
            bool written = CoopBattleSpawnBridgeFile.WriteForceRespawnableRequest("SP console");
            return written
                ? "Force-respawnable request queued."
                : "ERROR: Failed to write force-respawnable request.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("entry_status", "coop")]
        public static string EntryStatus(List<string> args)
        {
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null)
                return "EntryStatus: no status file yet.";

            return
                "Mission=" + (snapshot.MissionName ?? string.Empty) +
                " Peer=" + (snapshot.HasPeer ? (snapshot.PeerName ?? snapshot.PeerIndex.ToString()) : "none") +
                " HasAgent=" + snapshot.HasAgent +
                " CanRespawn=" + snapshot.CanRespawn +
                " Lifecycle=" + (snapshot.LifecycleState ?? string.Empty) +
                " LifecycleSource=" + (snapshot.LifecycleSource ?? string.Empty) +
                " Deaths=" + snapshot.DeathCount +
                " Requested=" + (snapshot.RequestedSide ?? string.Empty) +
                " Assigned=" + (snapshot.AssignedSide ?? string.Empty) +
                " SelectedTroop=" + (snapshot.SelectedTroopId ?? string.Empty) +
                " SelectedEntry=" + (snapshot.SelectedEntryId ?? string.Empty) +
                " IntentSide=" + (snapshot.IntentSide ?? string.Empty) +
                " IntentTroop=" + (snapshot.IntentTroopOrEntryId ?? string.Empty) +
                " SelectionReq=" + (snapshot.SelectionRequestSide ?? string.Empty) + "/" + (snapshot.SelectionRequestTroopId ?? string.Empty) +
                " SpawnReq=" + (snapshot.SpawnRequestSide ?? string.Empty) + "/" + (snapshot.SpawnRequestTroopId ?? string.Empty) +
                " Spawn=" + (snapshot.SpawnStatus ?? string.Empty) +
                " Reason=" + (snapshot.SpawnReason ?? string.Empty) +
                " UpdatedUtc=" + snapshot.UpdatedUtc.ToString("O");
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("side_options", "coop")]
        public static string SideOptions(List<string> args)
        {
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null)
                return "SideOptions: no status file yet.";

            BattleSideEnum currentSide = ResolveStatusSide(snapshot);
            return "SideOptions: 1=Attacker" + (currentSide == BattleSideEnum.Attacker ? "*" : string.Empty) +
                   ", 2=Defender" + (currentSide == BattleSideEnum.Defender ? "*" : string.Empty);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("select_side_index", "coop")]
        public static string SelectSideIndex(List<string> args)
        {
            if (args == null || args.Count < 1)
                return "ERROR: Usage: coop.select_side_index <1|2>";

            if (!int.TryParse(args[0], out int index))
                return "ERROR: Invalid side index.";

            string side;
            switch (index)
            {
                case 1:
                    side = "attacker";
                    break;
                case 2:
                    side = "defender";
                    break;
                default:
                    return "ERROR: Side index out of range. Use 1=attacker or 2=defender.";
            }

            bool written = CoopBattleSelectionBridgeFile.WriteSelectSideRequest(side, "SP console select_side_index");
            return written
                ? "Selection side request queued: " + side
                : "ERROR: Failed to write side selection request.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("troop_options", "coop")]
        public static string TroopOptions(List<string> args)
        {
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null)
                return "TroopOptions: no status file yet.";

            BattleSideEnum statusSide = ResolveStatusSide(snapshot);
            return FormatTroopOptions(snapshot, statusSide, "current side");
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("select_troop_index", "coop")]
        public static string SelectTroopIndex(List<string> args)
        {
            if (args == null || args.Count < 1)
                return "ERROR: Usage: coop.select_troop_index <1-based index>";

            if (!int.TryParse(args[0], out int index) || index <= 0)
                return "ERROR: Invalid troop index.";

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null)
                return "ERROR: No entry status file yet.";

            return SelectTroopIndexForSide(snapshot, ResolveStatusSide(snapshot), index, "current side", "SP console select_troop_index");
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("troop_options_side", "coop")]
        public static string TroopOptionsForSide(List<string> args)
        {
            if (args == null || args.Count < 1)
                return "ERROR: Usage: coop.troop_options_side attacker|defender";

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null)
                return "TroopOptions: no status file yet.";

            if (!TryParseBattleSide(args[0], out BattleSideEnum side) || side == BattleSideEnum.None)
                return "ERROR: Invalid side. Use attacker or defender.";

            return FormatTroopOptions(snapshot, side, side.ToString());
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("select_troop_index_side", "coop")]
        public static string SelectTroopIndexForSideCommand(List<string> args)
        {
            if (args == null || args.Count < 2)
                return "ERROR: Usage: coop.select_troop_index_side attacker|defender <1-based index>";

            if (!TryParseBattleSide(args[0], out BattleSideEnum side) || side == BattleSideEnum.None)
                return "ERROR: Invalid side. Use attacker or defender.";

            if (!int.TryParse(args[1], out int index) || index <= 0)
                return "ERROR: Invalid troop index.";

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null)
                return "ERROR: No entry status file yet.";

            return SelectTroopIndexForSide(snapshot, side, index, side.ToString(), "SP console select_troop_index_side");
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("entry_menu", "coop")]
        public static string EntryMenu(List<string> args)
        {
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null)
                return "EntryMenu: no status file yet.";

            BattleSideEnum side = ResolveStatusSide(snapshot);
            string sideLabel = side == BattleSideEnum.None ? "none" : side.ToString();
            string selectedTroop = ResolveSelectedTroopId(snapshot);
            string[] troopIds = ResolveAllowedTroopIds(snapshot, side);
            string troopList = troopIds.Length == 0
                ? "none"
                : string.Join(", ", troopIds.Select((troopId, index) =>
                    (index + 1) + "=" + troopId + (string.Equals(troopId, selectedTroop, System.StringComparison.OrdinalIgnoreCase) ? "*" : string.Empty)));

            return
                "EntryMenu: Side=" + sideLabel +
                " SelectedTroop=" + (selectedTroop ?? string.Empty) +
                " Spawn=" + (snapshot.SpawnStatus ?? string.Empty) +
                " HasAgent=" + snapshot.HasAgent +
                " CanRespawn=" + snapshot.CanRespawn +
                " Lifecycle=" + (snapshot.LifecycleState ?? string.Empty) +
                " LifecycleSource=" + (snapshot.LifecycleSource ?? string.Empty) +
                " Deaths=" + snapshot.DeathCount +
                " | Use: coop.select_side_index 1|2, coop.select_troop_index <n>, coop.select_troop_index_side attacker|defender <n>, coop.spawn_now, coop.force_respawnable" +
                " | Troops: " + troopList;
        }

        private static string FormatTroopOptions(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot, BattleSideEnum side, string sideLabelFallback)
        {
            string[] troopIds = ResolveAllowedTroopIds(snapshot, side);
            if (troopIds.Length == 0)
                return "TroopOptions: no allowed troops for " + sideLabelFallback + ".";

            string sideLabel = side == BattleSideEnum.None
                ? (snapshot.AssignedSide ?? snapshot.RequestedSide ?? snapshot.IntentSide ?? string.Empty)
                : side.ToString();
            if (string.IsNullOrWhiteSpace(sideLabel))
                sideLabel = sideLabelFallback;

            string selectedTroopId = ResolveSelectedTroopId(snapshot);
            return "TroopOptions[" + sideLabel + "]: " +
                string.Join(", ", troopIds.Select((troopId, index) =>
                    (index + 1) + "=" + troopId + (string.Equals(troopId, selectedTroopId, System.StringComparison.OrdinalIgnoreCase) ? "*" : string.Empty)));
        }

        private static string SelectTroopIndexForSide(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot,
            BattleSideEnum side,
            int index,
            string sideLabel,
            string source)
        {
            string[] troopIds = ResolveAllowedTroopIds(snapshot, side);
            if (troopIds.Length == 0)
                return "ERROR: No allowed troops for " + sideLabel + ".";

            if (index > troopIds.Length)
                return "ERROR: Troop index out of range for " + sideLabel + ". Max=" + troopIds.Length;

            string troopId = troopIds[index - 1];
            bool sideWritten = side != BattleSideEnum.None &&
                               CoopBattleSelectionBridgeFile.WriteSelectSideRequest(side.ToString(), source + " side");
            if (side != BattleSideEnum.None && !sideWritten)
                return "ERROR: Failed to write side selection request.";

            bool troopWritten = CoopBattleSelectionBridgeFile.WriteSelectTroopRequest(troopId, source);
            return troopWritten
                ? "Selection troop request queued for " + sideLabel + ": " + troopId
                : "ERROR: Failed to write troop selection request.";
        }

        private static BattleSideEnum ResolveStatusSide(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
        {
            if (snapshot == null)
                return BattleSideEnum.None;

            if (TryParseBattleSide(snapshot.AssignedSide, out BattleSideEnum assignedSide) && assignedSide != BattleSideEnum.None)
                return assignedSide;

            if (TryParseBattleSide(snapshot.RequestedSide, out BattleSideEnum requestedSide) && requestedSide != BattleSideEnum.None)
                return requestedSide;

            if (TryParseBattleSide(snapshot.IntentSide, out BattleSideEnum intentSide) && intentSide != BattleSideEnum.None)
                return intentSide;

            return BattleSideEnum.None;
        }

        private static string[] ResolveAllowedTroopIds(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot, BattleSideEnum side)
        {
            if (snapshot == null)
                return new string[0];

            string raw;
            switch (side)
            {
                case BattleSideEnum.Attacker:
                    raw = snapshot.AttackerAllowedTroopIds;
                    break;
                case BattleSideEnum.Defender:
                    raw = snapshot.DefenderAllowedTroopIds;
                    break;
                default:
                    raw = snapshot.AllowedTroopIds;
                    break;
            }

            return (raw ?? string.Empty)
                .Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries);
        }

        private static string ResolveSelectedTroopId(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(snapshot.SelectedTroopId))
                return snapshot.SelectedTroopId;

            if (!string.IsNullOrWhiteSpace(snapshot.SelectionRequestTroopId))
                return snapshot.SelectionRequestTroopId;

            if (!string.IsNullOrWhiteSpace(snapshot.SpawnRequestTroopId))
                return snapshot.SpawnRequestTroopId;

            return snapshot.IntentTroopOrEntryId ?? string.Empty;
        }

        private static bool TryParseBattleSide(string raw, out BattleSideEnum side)
        {
            side = BattleSideEnum.None;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            if (System.Enum.TryParse(raw, true, out side))
                return true;

            string normalized = raw.Trim().ToLowerInvariant();
            if (normalized == "attacker" || normalized == "attackers" || normalized == "1")
            {
                side = BattleSideEnum.Attacker;
                return true;
            }

            if (normalized == "defender" || normalized == "defenders" || normalized == "2")
            {
                side = BattleSideEnum.Defender;
                return true;
            }

            return false;
        }
    }
}
