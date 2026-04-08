using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.UI
{
    public sealed class CoopSelectionUiSnapshot
    {
        public CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot Status { get; set; }
        public CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot CurrentSelection { get; set; }
        public BattleRuntimeState BattleState { get; set; }
        public string[] AttackerSelectableEntryIds { get; set; } = Array.Empty<string>();
        public string[] DefenderSelectableEntryIds { get; set; } = Array.Empty<string>();
        public string[] EffectiveSelectableEntryIds { get; set; } = Array.Empty<string>();
        public BattleSideEnum EffectiveSide { get; set; }
        public string SelectedEntryId { get; set; }
        public string BattlePhase { get; set; }
        public string Lifecycle { get; set; }
        public bool HasLocalControlledAgent { get; set; }
        public bool IsBattleEnded { get; set; }
        public bool CanSpawn { get; set; }
        public bool CanShowOverlay { get; set; }
    }

    internal static class CoopSelectionUiHelpers
    {
        public static CoopSelectionUiSnapshot BuildSnapshot(
            BattleSideEnum selectedSideOverride,
            string selectedEntryIdOverride,
            bool hasLocalControlledAgent)
        {
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status = CoopBattleEntryStatusBridgeFile.ReadStatus();
            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot currentSelection = CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            BattleRuntimeState battleState = BattleSnapshotRuntimeState.GetState();
            string[] attackerSelectableEntryIds = ResolveSelectableEntryIds(status, BattleSideEnum.Attacker);
            string[] defenderSelectableEntryIds = ResolveSelectableEntryIds(status, BattleSideEnum.Defender);
            BattleSideEnum effectiveSide = ResolveEffectiveSide(
                selectedSideOverride,
                status,
                currentSelection,
                attackerSelectableEntryIds,
                defenderSelectableEntryIds);
            string[] effectiveSelectableEntryIds = GetSelectableEntryIdsForSide(
                effectiveSide,
                attackerSelectableEntryIds,
                defenderSelectableEntryIds);
            string selectedEntryId = ResolveSelectedEntryId(
                effectiveSide,
                effectiveSelectableEntryIds,
                currentSelection,
                status,
                selectedEntryIdOverride);

            return new CoopSelectionUiSnapshot
            {
                Status = status,
                CurrentSelection = currentSelection,
                BattleState = battleState,
                AttackerSelectableEntryIds = attackerSelectableEntryIds,
                DefenderSelectableEntryIds = defenderSelectableEntryIds,
                EffectiveSide = effectiveSide,
                EffectiveSelectableEntryIds = effectiveSelectableEntryIds,
                SelectedEntryId = selectedEntryId,
                BattlePhase = !string.IsNullOrWhiteSpace(status?.BattlePhase)
                    ? status.BattlePhase
                    : CoopBattlePhaseBridgeFile.ReadStatus()?.Phase.ToString() ?? "Unknown",
                Lifecycle = status?.LifecycleState ?? "Unknown",
                HasLocalControlledAgent = hasLocalControlledAgent,
                IsBattleEnded = string.Equals(status?.BattlePhase, nameof(CoopBattlePhase.BattleEnded), StringComparison.OrdinalIgnoreCase),
                CanSpawn = (status?.CanRespawn ?? true) && !string.IsNullOrWhiteSpace(selectedEntryId),
                CanShowOverlay = ShouldOverlayBeVisible(status, hasLocalControlledAgent)
            };
        }

        public static string BuildMissionSummaryText(CoopSelectionUiSnapshot snapshot)
        {
            string missionName = snapshot?.Status?.MissionName;
            if (string.IsNullOrWhiteSpace(missionName))
                missionName = snapshot?.BattleState?.Snapshot?.BattleId ?? "unknown";

            return "Mission: " + missionName + " | Phase: " + (snapshot?.BattlePhase ?? "Unknown");
        }

        public static string BuildStatusText(CoopSelectionUiSnapshot snapshot)
        {
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status = snapshot?.Status;
            if (status == null)
                return "Waiting for mission status...";

            return "Phase=" + (snapshot?.BattlePhase ?? "Unknown") +
                   " | State=" + (snapshot?.Lifecycle ?? "Unknown") +
                   " | Spawn=" + (status.SpawnStatus ?? "none") +
                   " | CanRespawn=" + status.CanRespawn +
                   " | Deaths=" + status.DeathCount;
        }

        public static string BuildTeamHintText(CoopSelectionUiSnapshot snapshot)
        {
            return snapshot == null
                ? "Choose a side."
                : "Choose a side, then pick a living unit from the class shell.";
        }

        public static string BuildClassHintText(CoopSelectionUiSnapshot snapshot)
        {
            if (snapshot == null)
                return "Choose a living unit.";

            return snapshot.CanSpawn
                ? "Deploy into a currently living selectable unit."
                : "Choose a living unit before deploying.";
        }

        public static string BuildUnitEmptyText(CoopSelectionUiSnapshot snapshot)
        {
            if (snapshot == null || snapshot.EffectiveSide == BattleSideEnum.None)
                return "Select a side to view living units.";

            return "No living selectable units remain on the " + FormatSideLabel(snapshot.EffectiveSide).ToLowerInvariant() + " side.";
        }

        public static string BuildSideCountText(int selectableCount)
        {
            return selectableCount + " selectable";
        }

        public static string BuildSideDetailText(BattleRuntimeState battleState, BattleSideEnum side)
        {
            BattleSideState sideState = battleState?.SidesByKey != null &&
                                        battleState.SidesByKey.TryGetValue(side.ToString(), out BattleSideState resolvedSideState)
                ? resolvedSideState
                : null;
            int totalMen = sideState?.TotalManCount ?? 0;
            return totalMen > 0
                ? totalMen + " in battlefield roster"
                : "No battlefield roster data";
        }

        public static string BuildSelectedNameText(CoopSelectionUiSnapshot snapshot)
        {
            RosterEntryState entryState = ResolveEntryState(snapshot?.EffectiveSide ?? BattleSideEnum.None, snapshot?.SelectedEntryId);
            return entryState != null
                ? ResolveEntryDisplayName(entryState, snapshot?.SelectedEntryId)
                : "Select a living unit";
        }

        public static string BuildSelectedDetailText(CoopSelectionUiSnapshot snapshot)
        {
            RosterEntryState entryState = ResolveEntryState(snapshot?.EffectiveSide ?? BattleSideEnum.None, snapshot?.SelectedEntryId);
            return entryState != null
                ? ResolveEntryDetailText(entryState)
                : "This shell is coop-owned and only lists units that are alive and currently selectable.";
        }

        public static string BuildSelectedSummaryText(CoopSelectionUiSnapshot snapshot)
        {
            RosterEntryState entryState = ResolveEntryState(snapshot?.EffectiveSide ?? BattleSideEnum.None, snapshot?.SelectedEntryId);
            return entryState != null
                ? ResolveEntrySummaryText(entryState)
                : BuildStatusText(snapshot);
        }

        public static BattleSideEnum ParseBattleSide(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return BattleSideEnum.None;

            if (Enum.TryParse(raw, true, out BattleSideEnum parsed))
                return parsed;

            switch (raw.Trim().ToLowerInvariant())
            {
                case "attacker":
                case "attackers":
                case "1":
                    return BattleSideEnum.Attacker;
                case "defender":
                case "defenders":
                case "2":
                    return BattleSideEnum.Defender;
                default:
                    return BattleSideEnum.None;
            }
        }

        public static string FormatSideLabel(BattleSideEnum side)
        {
            switch (side)
            {
                case BattleSideEnum.Attacker:
                    return "Attackers";
                case BattleSideEnum.Defender:
                    return "Defenders";
                default:
                    return "No Side";
            }
        }

        public static RosterEntryState ResolveEntryState(BattleSideEnum side, string selectionId)
        {
            if (string.IsNullOrWhiteSpace(selectionId))
                return null;

            RosterEntryState directEntry = BattleSnapshotRuntimeState.GetEntryState(selectionId);
            if (directEntry != null)
                return directEntry;

            BattleSideState sideState = BattleSnapshotRuntimeState.GetSideState(side.ToString());
            return sideState?.Entries?.FirstOrDefault(entry =>
                string.Equals(entry?.EntryId, selectionId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry?.CharacterId, selectionId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry?.OriginalCharacterId, selectionId, StringComparison.OrdinalIgnoreCase));
        }

        public static string ResolveEntryDisplayName(RosterEntryState entryState, string fallbackId)
        {
            return entryState?.TroopName ??
                   entryState?.OriginalCharacterId ??
                   entryState?.CharacterId ??
                   fallbackId ??
                   "Unknown Unit";
        }

        public static string ResolveEntryDetailText(RosterEntryState entryState)
        {
            if (entryState == null)
                return "No snapshot details available for this unit.";

            List<string> parts = new List<string>();
            if (entryState.IsHero)
                parts.Add("hero");
            if (!string.IsNullOrWhiteSpace(entryState.HeroRole))
                parts.Add(entryState.HeroRole.ToLowerInvariant());
            if (entryState.IsMounted)
                parts.Add("mounted");
            if (entryState.IsRanged)
                parts.Add("ranged");
            if (entryState.HasShield)
                parts.Add("shield");
            if (entryState.Tier > 0)
                parts.Add("tier " + entryState.Tier);
            if (!string.IsNullOrWhiteSpace(entryState.PartyId))
                parts.Add(entryState.PartyId);
            return parts.Count > 0 ? string.Join(" | ", parts) : "Standard troop";
        }

        public static string ResolveEntrySummaryText(RosterEntryState entryState)
        {
            if (entryState == null)
                return "Living unit";

            List<string> parts = new List<string> { entryState.IsHero ? "Hero" : "Troop" };
            if (entryState.BaseHitPoints > 0)
                parts.Add("HP " + entryState.BaseHitPoints);
            if (entryState.SkillAthletics > 0)
                parts.Add("Ath " + entryState.SkillAthletics);
            if (entryState.SkillRiding > 0 && entryState.IsMounted)
                parts.Add("Rid " + entryState.SkillRiding);
            return string.Join(" | ", parts);
        }

        private static bool ShouldOverlayBeVisible(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status, bool hasLocalControlledAgent)
        {
            if (hasLocalControlledAgent)
                return false;

            if (status == null)
                return true;

            if (string.Equals(status.BattlePhase, nameof(CoopBattlePhase.BattleEnded), StringComparison.OrdinalIgnoreCase))
                return false;

            if (status.HasAgent || string.Equals(status.LifecycleState, "Alive", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static string[] ResolveSelectableEntryIds(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status, BattleSideEnum side)
        {
            if (status == null)
                return Array.Empty<string>();

            string selectableEntrySource =
                side == BattleSideEnum.Attacker ? status.AttackerSelectableEntrySource :
                side == BattleSideEnum.Defender ? status.DefenderSelectableEntrySource :
                status.SelectableEntrySource;
            string rawSelectableEntryIds =
                side == BattleSideEnum.Attacker ? status.AttackerSelectableEntryIds :
                side == BattleSideEnum.Defender ? status.DefenderSelectableEntryIds :
                status.SelectableEntryIds;
            string[] selectableEntryIds = CoopBattleEntryStatusBridgeFile.DeserializeIdList(rawSelectableEntryIds)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (selectableEntryIds.Length > 0 || !string.IsNullOrWhiteSpace(selectableEntrySource))
                return selectableEntryIds;

            string rawAllowedEntryIds =
                side == BattleSideEnum.Attacker ? status.AttackerAllowedEntryIds :
                side == BattleSideEnum.Defender ? status.DefenderAllowedEntryIds :
                status.AllowedEntryIds;
            return CoopBattleEntryStatusBridgeFile.DeserializeIdList(rawAllowedEntryIds)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static BattleSideEnum ResolveEffectiveSide(
            BattleSideEnum selectedSideOverride,
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status,
            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot currentSelection,
            string[] attackerSelectableEntryIds,
            string[] defenderSelectableEntryIds)
        {
            if (selectedSideOverride != BattleSideEnum.None)
                return selectedSideOverride;

            BattleSideEnum effectiveSide = ParseBattleSide(
                currentSelection?.Side ??
                status?.AssignedSide ??
                status?.RequestedSide ??
                status?.IntentSide);
            if (effectiveSide != BattleSideEnum.None)
                return effectiveSide;

            if (attackerSelectableEntryIds.Length > 0)
                return BattleSideEnum.Attacker;
            if (defenderSelectableEntryIds.Length > 0)
                return BattleSideEnum.Defender;

            return BattleSideEnum.None;
        }

        private static string[] GetSelectableEntryIdsForSide(
            BattleSideEnum side,
            string[] attackerSelectableEntryIds,
            string[] defenderSelectableEntryIds)
        {
            return side == BattleSideEnum.Attacker
                ? attackerSelectableEntryIds ?? Array.Empty<string>()
                : side == BattleSideEnum.Defender
                    ? defenderSelectableEntryIds ?? Array.Empty<string>()
                    : Array.Empty<string>();
        }

        private static string ResolveSelectedEntryId(
            BattleSideEnum effectiveSide,
            IReadOnlyList<string> selectableEntryIds,
            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot currentSelection,
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status,
            string selectedEntryIdOverride)
        {
            if (effectiveSide == BattleSideEnum.None || selectableEntryIds == null || selectableEntryIds.Count <= 0)
                return null;

            string[] candidates =
            {
                selectedEntryIdOverride,
                currentSelection?.TroopOrEntryId,
                status?.SelectedEntryId,
                status?.SpawnRequestEntryId,
                status?.SelectionRequestEntryId,
                status?.SelectedTroopId,
                status?.SpawnRequestTroopId,
                status?.SelectionRequestTroopId
            };

            foreach (string candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                string resolvedEntryId = ResolveSelectableEntryIdFromCandidate(effectiveSide, selectableEntryIds, candidate);
                if (!string.IsNullOrWhiteSpace(resolvedEntryId))
                    return resolvedEntryId;
            }

            return null;
        }

        private static string ResolveSelectableEntryIdFromCandidate(
            BattleSideEnum side,
            IReadOnlyList<string> selectableEntryIds,
            string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || selectableEntryIds == null || selectableEntryIds.Count <= 0)
                return null;

            string directMatch = selectableEntryIds.FirstOrDefault(entryId =>
                string.Equals(entryId, candidate, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(directMatch))
                return directMatch;

            foreach (string entryId in selectableEntryIds)
            {
                RosterEntryState entryState = ResolveEntryState(side, entryId);
                if (entryState == null)
                    continue;

                if (string.Equals(entryState.EntryId, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entryState.CharacterId, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entryState.OriginalCharacterId, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return entryId;
                }
            }

            return null;
        }
    }
}
