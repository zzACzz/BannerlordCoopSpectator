using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

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
        public string TeamRefreshKey { get; set; } = string.Empty;
        public string ClassRefreshKey { get; set; } = string.Empty;
    }

    internal sealed class CoopSidePresentation
    {
        public string TitleText { get; set; } = string.Empty;
        public string CountText { get; set; } = string.Empty;
        public string DetailText { get; set; } = string.Empty;
        public string BannerCodeText { get; set; } = string.Empty;
    }

    internal sealed class CoopCharacterVisualData
    {
        public static readonly CoopCharacterVisualData Empty = new CoopCharacterVisualData
        {
            HasVisual = false,
            BannerCodeText = string.Empty,
            EquipmentCode = string.Empty,
            CharStringId = string.Empty,
            MountCreationKey = string.Empty,
            StanceIndex = 0,
            ArmorColor1 = 0xFFFFFFFFu,
            ArmorColor2 = 0xFFFFFFFFu,
            BodyProperties = string.Empty,
            Race = 0,
            IsFemale = false
        };

        public bool HasVisual { get; set; }
        public string BannerCodeText { get; set; } = string.Empty;
        public string BodyProperties { get; set; } = string.Empty;
        public string CharStringId { get; set; } = string.Empty;
        public int Race { get; set; }
        public string EquipmentCode { get; set; } = string.Empty;
        public bool IsFemale { get; set; }
        public string MountCreationKey { get; set; } = string.Empty;
        public int StanceIndex { get; set; }
        public uint ArmorColor1 { get; set; }
        public uint ArmorColor2 { get; set; }
    }

    internal static class CoopSelectionUiHelpers
    {
        private const string NeutralPlayerBannerCode = "11.163.166.1528.1528.764.764.1.0.0.133.171.171.483.483.764.764.0.0.0";
        private const string BanditBannerCode = "24.193.116.1536.1536.768.768.1.0.0";
        private const string DeserterBannerCode = "35.116.116.1528.1528.766.740.1.0.0.510.19.171.1528.353.758.658.0.0.0.510.19.171.1528.398.760.845.0.0.0";
        private const uint NeutralPrimaryColor = 0xFF744C38u;
        private const uint NeutralSecondaryColor = 0xFFFFB53Eu;
        private const uint BanditPrimaryColor = 0xFF8B7C73u;
        private const uint BanditSecondaryColor = 0xFF8B7C73u;
        private const uint DeserterPrimaryColor = 0xFF95A9CCu;
        private const uint DeserterSecondaryColor = 0xFF2F2A2Bu;

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
            string[] effectiveSelectableEntryIds = OrderSelectableEntryIdsForDisplay(
                battleState,
                effectiveSide,
                GetSelectableEntryIdsForSide(
                    effectiveSide,
                    attackerSelectableEntryIds,
                    defenderSelectableEntryIds));
            string selectedEntryId = ResolveSelectedEntryId(
                effectiveSide,
                effectiveSelectableEntryIds,
                currentSelection,
                status,
                selectedEntryIdOverride);

            CoopSelectionUiSnapshot snapshot = new CoopSelectionUiSnapshot
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
                    : CoopBattlePhaseBridgeFile.ReadStatus()?.Phase.ToString() ?? string.Empty,
                Lifecycle = status?.LifecycleState ?? string.Empty,
                HasLocalControlledAgent = hasLocalControlledAgent,
                IsBattleEnded = string.Equals(status?.BattlePhase, nameof(CoopBattlePhase.BattleEnded), StringComparison.OrdinalIgnoreCase),
                CanSpawn = (status?.CanRespawn ?? true) && !string.IsNullOrWhiteSpace(selectedEntryId),
                CanShowOverlay = ShouldOverlayBeVisible(status, hasLocalControlledAgent)
            };

            snapshot.TeamRefreshKey = BuildTeamRefreshKey(snapshot);
            snapshot.ClassRefreshKey = BuildClassRefreshKey(snapshot);
            return snapshot;
        }

        public static string BuildMissionSummaryText(CoopSelectionUiSnapshot snapshot)
        {
            return BuildPhaseStatusText(snapshot);
        }

        public static string BuildTeamStatusText(CoopSelectionUiSnapshot snapshot)
        {
            return BuildPhaseStatusText(snapshot);
        }

        public static string BuildStatusText(CoopSelectionUiSnapshot snapshot)
        {
            return BuildPhaseStatusText(snapshot);
        }

        public static string BuildTeamHintText(CoopSelectionUiSnapshot snapshot)
        {
            return snapshot == null
                ? "Choose a side."
                : "Choose a side, auto assign, or spectate.";
        }

        public static string BuildClassHintText(CoopSelectionUiSnapshot snapshot)
        {
            if (snapshot == null)
                return "Choose a living unit.";

            return snapshot.CanSpawn
                ? "Deploy into the selected living unit."
                : "Choose a living unit, then deploy.";
        }

        public static string BuildUnitEmptyText(CoopSelectionUiSnapshot snapshot)
        {
            if (snapshot == null || snapshot.EffectiveSide == BattleSideEnum.None)
                return "Select a side to view living units.";

            return "No living selectable units remain for " + ResolveSideDisplayName(snapshot.BattleState, snapshot.EffectiveSide) + ".";
        }

        public static string BuildSideCountText(int selectableCount)
        {
            return selectableCount + " selectable";
        }

        public static string BuildSideDetailText(BattleRuntimeState battleState, BattleSideEnum side)
        {
            BattleSideState sideState = ResolveSideState(battleState, side);
            if (sideState == null)
                return "0 in battlefield roster";

            int battlefieldRosterCount = sideState.MissionReadyEntryOrder?.Count > 0
                ? sideState.MissionReadyEntryOrder.Count
                : sideState.Entries?.Count ?? 0;
            return battlefieldRosterCount + " in battlefield roster";
        }

        public static bool CanSelectSide(CoopSelectionUiSnapshot snapshot, BattleSideEnum side, int selectableCount)
        {
            if (selectableCount <= 0)
                return false;

            if (snapshot == null)
                return true;

            if (snapshot.IsBattleEnded)
                return false;

            if (!string.Equals(snapshot.BattlePhase, nameof(CoopBattlePhase.BattleActive), StringComparison.OrdinalIgnoreCase))
                return true;

            if (snapshot.Status != null && !snapshot.Status.HasAgent)
                return true;

            BattleSideEnum assignedSide = NormalizeSide(snapshot.Status?.AssignedSide);
            return assignedSide == BattleSideEnum.None || assignedSide == side;
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
            if (entryState == null)
                return "Living units only.";

            if (IsCommanderEntry(snapshot?.BattleState, snapshot?.EffectiveSide ?? BattleSideEnum.None, entryState))
                return "Army Commander";

            if (entryState.IsHero)
            {
                string heroRole = NormalizeHeroRole(entryState.HeroRole);
                if (!string.IsNullOrWhiteSpace(heroRole))
                    return heroRole;
            }

            return ResolveEntryTypeLabel(entryState);
        }

        public static string BuildSelectedSummaryText(CoopSelectionUiSnapshot snapshot)
        {
            RosterEntryState entryState = ResolveEntryState(snapshot?.EffectiveSide ?? BattleSideEnum.None, snapshot?.SelectedEntryId);
            if (entryState == null)
                return snapshot?.CanSpawn == true ? "Ready to deploy." : "Choose a unit to preview.";

            string partyName = ResolvePartyDisplayName(snapshot?.BattleState, entryState);
            if (!string.IsNullOrWhiteSpace(partyName))
                return partyName;

            return ResolveEntryTypeLabel(entryState);
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

        public static CoopSidePresentation ResolveSidePresentation(CoopSelectionUiSnapshot snapshot, BattleSideEnum side, int selectableCount)
        {
            return new CoopSidePresentation
            {
                TitleText = ResolveSideDisplayName(snapshot?.BattleState, side),
                CountText = BuildSideCountText(selectableCount),
                DetailText = BuildSideDetailText(snapshot?.BattleState, side),
                BannerCodeText = ResolveSideBannerCode(snapshot?.BattleState, side)
            };
        }

        public static string[] OrderSelectableEntryIdsForDisplay(CoopSelectionUiSnapshot snapshot)
        {
            return OrderSelectableEntryIdsForDisplay(snapshot?.BattleState, snapshot?.EffectiveSide ?? BattleSideEnum.None, snapshot?.EffectiveSelectableEntryIds);
        }

        public static string ResolveCommanderBadgeText(CoopSelectionUiSnapshot snapshot, string entryId)
        {
            RosterEntryState entryState = ResolveEntryState(snapshot?.EffectiveSide ?? BattleSideEnum.None, entryId);
            return IsCommanderEntry(snapshot?.BattleState, snapshot?.EffectiveSide ?? BattleSideEnum.None, entryState)
                ? "COMMANDER"
                : string.Empty;
        }

        public static CoopCharacterVisualData BuildSelectedVisualData(CoopSelectionUiSnapshot snapshot)
        {
            RosterEntryState entryState = ResolveEntryState(snapshot?.EffectiveSide ?? BattleSideEnum.None, snapshot?.SelectedEntryId);
            if (entryState == null)
                return CoopCharacterVisualData.Empty;

            BasicCharacterObject character = BattleSnapshotRuntimeState.TryResolveCharacterObject(entryState.EntryId);
            if (character == null)
                return CoopCharacterVisualData.Empty;

            Equipment equipment = CoopMissionSpawnLogic.BuildSnapshotEquipmentForExactRuntime(entryState) ?? character.Equipment;
            string equipmentCode = equipment?.CalculateEquipmentCode() ?? string.Empty;
            BodyProperties bodyProperties = character.GetBodyProperties(equipment);
            ResolveSideColors(snapshot?.BattleState, snapshot?.EffectiveSide ?? BattleSideEnum.None, character, out uint armorColor1, out uint armorColor2);

            return new CoopCharacterVisualData
            {
                HasVisual = !string.IsNullOrWhiteSpace(character.StringId) || !string.IsNullOrWhiteSpace(equipmentCode),
                BannerCodeText = ResolveSideBannerCode(snapshot?.BattleState, snapshot?.EffectiveSide ?? BattleSideEnum.None),
                BodyProperties = bodyProperties.ToString(),
                CharStringId = character.StringId ?? string.Empty,
                Race = character.Race,
                EquipmentCode = equipmentCode,
                IsFemale = character.IsFemale,
                MountCreationKey = ResolveMountCreationKey(equipment, entryState.EntryId ?? character.StringId),
                StanceIndex = entryState.IsMounted ? 1 : 0,
                ArmorColor1 = armorColor1,
                ArmorColor2 = armorColor2
            };
        }

        public static string ResolveEntryIconType(RosterEntryState entryState)
        {
            if (entryState == null)
                return string.Empty;

            bool isHeavy = IsHeavyEntry(entryState);
            if (entryState.IsMounted && entryState.IsRanged)
                return isHeavy ? nameof(TargetIconType.HorseArcher_Heavy) : nameof(TargetIconType.HorseArcher_Light);

            if (entryState.IsMounted)
                return isHeavy ? nameof(TargetIconType.Cavalry_Heavy) : nameof(TargetIconType.Cavalry_Light);

            if (entryState.IsRanged)
            {
                if (IsCrossbowEntry(entryState))
                    return isHeavy ? nameof(TargetIconType.Crossbowman_Heavy) : nameof(TargetIconType.Crossbowman_Light);

                return isHeavy ? nameof(TargetIconType.Archer_Heavy) : nameof(TargetIconType.Archer_Light);
            }

            if (entryState.HasThrown && !entryState.HasShield)
                return nameof(TargetIconType.Special_JavelinThrower);

            return isHeavy ? nameof(TargetIconType.Infantry_Heavy) : nameof(TargetIconType.Infantry_Light);
        }

        public static RosterEntryState ResolveEntryState(BattleSideEnum side, string entryId)
        {
            if (string.IsNullOrWhiteSpace(entryId))
                return null;

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (entryState == null)
                return null;

            if (side == BattleSideEnum.None)
                return entryState;

            string canonicalSideKey = NormalizeSideKey(side);
            return string.Equals(entryState.SideId, canonicalSideKey, StringComparison.OrdinalIgnoreCase)
                ? entryState
                : null;
        }

        public static string ResolveEntryDisplayName(RosterEntryState entryState, string fallbackId)
        {
            return BattleSnapshotRuntimeState.ResolveEntryDisplayName(entryState, fallbackId);
        }

        public static string ResolveEntryDetailText(RosterEntryState entryState)
        {
            if (entryState == null)
                return string.Empty;

            if (entryState.IsHero)
            {
                string heroRole = NormalizeHeroRole(entryState.HeroRole);
                if (!string.IsNullOrWhiteSpace(heroRole))
                    return heroRole;
            }

            return ResolveEntryTypeLabel(entryState);
        }

        public static string ResolveEntrySummaryText(RosterEntryState entryState)
        {
            return ResolveEntryTypeLabel(entryState);
        }

        public static bool IsCommanderEntry(BattleRuntimeState battleState, BattleSideEnum side, RosterEntryState entryState)
        {
            return BattleCommanderResolver.IsCommanderEntry(battleState, side, entryState);
        }

        public static string ResolveSideDisplayName(BattleRuntimeState battleState, BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return "No Side";

            SideFlavor sideFlavor = ResolveSideFlavor(battleState, side);
            if (sideFlavor == SideFlavor.Deserter)
                return "Deserters";

            if (sideFlavor == SideFlavor.Bandit)
                return "Bandits";

            RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(battleState, side);
            string commanderName = ResolveEntryDisplayName(commanderEntry, null);
            if (!string.IsNullOrWhiteSpace(commanderName) && !string.Equals(commanderName, "Unknown Unit", StringComparison.Ordinal))
                return "Army of " + commanderName;

            BattlePartyState leaderParty = ResolveLeaderParty(battleState, side);
            if (!string.IsNullOrWhiteSpace(leaderParty?.PartyName))
                return "Army of " + leaderParty.PartyName.Trim();

            return FormatSideLabel(side);
        }

        public static string ResolvePartyDisplayName(BattleRuntimeState battleState, RosterEntryState entryState)
        {
            if (battleState?.PartiesById == null || entryState == null || string.IsNullOrWhiteSpace(entryState.PartyId))
                return string.Empty;

            return battleState.PartiesById.TryGetValue(entryState.PartyId, out BattlePartyState partyState) &&
                   !string.IsNullOrWhiteSpace(partyState?.PartyName)
                ? partyState.PartyName.Trim()
                : string.Empty;
        }

        private static string[] ResolveSelectableEntryIds(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status, BattleSideEnum side)
        {
            if (status == null || side == BattleSideEnum.None)
                return Array.Empty<string>();

            string selectableRaw = side == BattleSideEnum.Attacker ? status.AttackerSelectableEntryIds : status.DefenderSelectableEntryIds;
            string selectableSource = side == BattleSideEnum.Attacker ? status.AttackerSelectableEntrySource : status.DefenderSelectableEntrySource;
            string allowedRaw = side == BattleSideEnum.Attacker ? status.AttackerAllowedEntryIds : status.DefenderAllowedEntryIds;
            string[] selectableEntryIds = CoopBattleEntryStatusBridgeFile.DeserializeIdList(selectableRaw);
            if (!string.IsNullOrWhiteSpace(selectableSource))
                return selectableEntryIds;

            if (selectableEntryIds.Length > 0)
                return selectableEntryIds;

            BattleSideEnum snapshotSide = NormalizeSide(status.AssignedSide);
            if (snapshotSide == BattleSideEnum.None)
                snapshotSide = NormalizeSide(status.RequestedSide);
            if (snapshotSide == side)
            {
                string currentSelectableSource = status.SelectableEntrySource;
                string[] currentSelectableEntryIds = CoopBattleEntryStatusBridgeFile.DeserializeIdList(status.SelectableEntryIds);
                if (!string.IsNullOrWhiteSpace(currentSelectableSource))
                    return currentSelectableEntryIds;

                if (currentSelectableEntryIds.Length > 0)
                    return currentSelectableEntryIds;
            }

            string[] allowedEntryIds = CoopBattleEntryStatusBridgeFile.DeserializeIdList(allowedRaw);
            return allowedEntryIds.Length > 0 ? allowedEntryIds : Array.Empty<string>();
        }

        private static string[] GetSelectableEntryIdsForSide(
            BattleSideEnum effectiveSide,
            string[] attackerSelectableEntryIds,
            string[] defenderSelectableEntryIds)
        {
            switch (effectiveSide)
            {
                case BattleSideEnum.Attacker:
                    return attackerSelectableEntryIds ?? Array.Empty<string>();
                case BattleSideEnum.Defender:
                    return defenderSelectableEntryIds ?? Array.Empty<string>();
                default:
                    return Array.Empty<string>();
            }
        }

        private static BattleSideEnum ResolveEffectiveSide(
            BattleSideEnum selectedSideOverride,
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status,
            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot currentSelection,
            string[] attackerSelectableEntryIds,
            string[] defenderSelectableEntryIds)
        {
            BattleSideEnum[] candidates =
            {
                selectedSideOverride,
                NormalizeSide(currentSelection?.Side),
                NormalizeSide(status?.SpawnRequestSide),
                NormalizeSide(status?.SelectionRequestSide),
                NormalizeSide(status?.IntentSide),
                NormalizeSide(status?.AssignedSide),
                NormalizeSide(status?.RequestedSide)
            };

            foreach (BattleSideEnum candidate in candidates)
            {
                if (candidate != BattleSideEnum.None)
                    return candidate;
            }

            string currentSelectionId = currentSelection?.TroopOrEntryId;
            BattleSideEnum candidateFromSelection = ResolveSideFromCandidateEntryId(currentSelectionId, attackerSelectableEntryIds, defenderSelectableEntryIds);
            if (candidateFromSelection != BattleSideEnum.None)
                return candidateFromSelection;

            candidateFromSelection = ResolveSideFromCandidateEntryId(status?.SelectedEntryId, attackerSelectableEntryIds, defenderSelectableEntryIds);
            if (candidateFromSelection != BattleSideEnum.None)
                return candidateFromSelection;

            if ((attackerSelectableEntryIds?.Length ?? 0) > 0 && (defenderSelectableEntryIds?.Length ?? 0) <= 0)
                return BattleSideEnum.Attacker;

            if ((defenderSelectableEntryIds?.Length ?? 0) > 0 && (attackerSelectableEntryIds?.Length ?? 0) <= 0)
                return BattleSideEnum.Defender;

            return BattleSideEnum.None;
        }

        private static BattleSideEnum ResolveSideFromCandidateEntryId(
            string candidate,
            IReadOnlyCollection<string> attackerSelectableEntryIds,
            IReadOnlyCollection<string> defenderSelectableEntryIds)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return BattleSideEnum.None;

            if (attackerSelectableEntryIds != null && attackerSelectableEntryIds.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                return BattleSideEnum.Attacker;

            if (defenderSelectableEntryIds != null && defenderSelectableEntryIds.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                return BattleSideEnum.Defender;

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(candidate);
            if (entryState == null)
                return BattleSideEnum.None;

            if (string.Equals(entryState.SideId, NormalizeSideKey(BattleSideEnum.Attacker), StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Attacker;

            if (string.Equals(entryState.SideId, NormalizeSideKey(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Defender;

            return BattleSideEnum.None;
        }

        private static string ResolveSelectedEntryId(
            BattleSideEnum effectiveSide,
            string[] effectiveSelectableEntryIds,
            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot currentSelection,
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status,
            string selectedEntryIdOverride)
        {
            if (effectiveSide == BattleSideEnum.None || effectiveSelectableEntryIds == null || effectiveSelectableEntryIds.Length <= 0)
                return null;

            string[] candidates =
            {
                selectedEntryIdOverride,
                currentSelection?.TroopOrEntryId,
                status?.SpawnRequestEntryId,
                status?.SelectionRequestEntryId,
                status?.SelectedEntryId,
                status?.IntentTroopOrEntryId,
                status?.SelectedTroopId,
                status?.SelectionRequestTroopId,
                status?.SpawnRequestTroopId
            };

            foreach (string candidate in candidates)
            {
                string resolved = ResolveSelectableEntryIdFromCandidate(effectiveSide, effectiveSelectableEntryIds, candidate);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;
            }

            return effectiveSelectableEntryIds[0];
        }

        private static string ResolveSelectableEntryIdFromCandidate(
            BattleSideEnum effectiveSide,
            IReadOnlyCollection<string> effectiveSelectableEntryIds,
            string candidate)
        {
            if (effectiveSide == BattleSideEnum.None || effectiveSelectableEntryIds == null || string.IsNullOrWhiteSpace(candidate))
                return null;

            string trimmedCandidate = candidate.Trim();
            string matchingEntryId = effectiveSelectableEntryIds.FirstOrDefault(entryId =>
                string.Equals(entryId, trimmedCandidate, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(matchingEntryId))
                return matchingEntryId;

            string resolvedEntryId = BattleSnapshotRuntimeState.TryResolveEntryId(NormalizeSideKey(effectiveSide), trimmedCandidate);
            if (string.IsNullOrWhiteSpace(resolvedEntryId))
                return null;

            return effectiveSelectableEntryIds.FirstOrDefault(entryId =>
                string.Equals(entryId, resolvedEntryId, StringComparison.OrdinalIgnoreCase));
        }

        private static string[] OrderSelectableEntryIdsForDisplay(
            BattleRuntimeState battleState,
            BattleSideEnum side,
            IEnumerable<string> entryIds)
        {
            List<string> orderedEntryIds = (entryIds ?? Array.Empty<string>())
                .Where(entryId => !string.IsNullOrWhiteSpace(entryId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (orderedEntryIds.Count <= 1 || side == BattleSideEnum.None)
                return orderedEntryIds.ToArray();

            List<RosterEntryState> candidateEntries = orderedEntryIds
                .Select(entryId => ResolveEntryState(side, entryId))
                .Where(entryState => entryState != null)
                .ToList();
            if (candidateEntries.Count <= 1)
                return orderedEntryIds.ToArray();

            RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(battleState, side, candidateEntries);
            if (commanderEntry == null)
                return orderedEntryIds.ToArray();

            int commanderIndex = orderedEntryIds.FindIndex(entryId =>
                string.Equals(entryId, commanderEntry.EntryId, StringComparison.OrdinalIgnoreCase));
            if (commanderIndex <= 0)
                return orderedEntryIds.ToArray();

            string commanderEntryId = orderedEntryIds[commanderIndex];
            orderedEntryIds.RemoveAt(commanderIndex);
            orderedEntryIds.Insert(0, commanderEntryId);
            return orderedEntryIds.ToArray();
        }

        private static bool ShouldOverlayBeVisible(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status, bool hasLocalControlledAgent)
        {
            if (hasLocalControlledAgent)
                return false;

            if (status == null)
                return true;

            if (string.Equals(status.BattlePhase, nameof(CoopBattlePhase.BattleEnded), StringComparison.OrdinalIgnoreCase))
                return false;

            if (status.CanRespawn || !status.HasAgent)
                return true;

            string lifecycle = status.LifecycleState ?? string.Empty;
            return
                string.Equals(lifecycle, "NoSide", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "WaitingForSelection", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "WaitingForSpawn", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "RespawnPending", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildPhaseStatusText(CoopSelectionUiSnapshot snapshot)
        {
            if (snapshot == null)
                return "Selection";

            if (snapshot.IsBattleEnded)
                return "Battle Ended";

            if (!string.IsNullOrWhiteSpace(snapshot.BattlePhase))
                return FormatReadableLabel(snapshot.BattlePhase);

            if (!string.IsNullOrWhiteSpace(snapshot.Lifecycle))
                return FormatReadableLabel(snapshot.Lifecycle);

            return "Selection";
        }

        private static string BuildTeamRefreshKey(CoopSelectionUiSnapshot snapshot)
        {
            if (snapshot == null)
                return "team|null";

            CoopSidePresentation attackerPresentation = ResolveSidePresentation(
                snapshot,
                BattleSideEnum.Attacker,
                snapshot.AttackerSelectableEntryIds?.Length ?? 0);
            CoopSidePresentation defenderPresentation = ResolveSidePresentation(
                snapshot,
                BattleSideEnum.Defender,
                snapshot.DefenderSelectableEntryIds?.Length ?? 0);

            return string.Join("\n", new[]
            {
                "team",
                snapshot.BattlePhase ?? string.Empty,
                snapshot.Lifecycle ?? string.Empty,
                snapshot.EffectiveSide.ToString(),
                BuildSideRefreshDescriptor(attackerPresentation, snapshot.AttackerSelectableEntryIds),
                BuildSideRefreshDescriptor(defenderPresentation, snapshot.DefenderSelectableEntryIds)
            });
        }

        private static string BuildClassRefreshKey(CoopSelectionUiSnapshot snapshot)
        {
            if (snapshot == null)
                return "class|null";

            List<string> parts = new List<string>
            {
                "class",
                snapshot.BattlePhase ?? string.Empty,
                snapshot.Lifecycle ?? string.Empty,
                snapshot.EffectiveSide.ToString(),
                snapshot.SelectedEntryId ?? string.Empty,
                snapshot.CanSpawn.ToString(),
                snapshot.IsBattleEnded.ToString(),
                ResolveSideDisplayName(snapshot.BattleState, snapshot.EffectiveSide),
                ResolveCommanderBadgeText(snapshot, snapshot.SelectedEntryId)
            };

            foreach (string entryId in snapshot.EffectiveSelectableEntryIds ?? Array.Empty<string>())
            {
                RosterEntryState entryState = ResolveEntryState(snapshot.EffectiveSide, entryId);
                parts.Add(string.Join("|", new[]
                {
                    entryId ?? string.Empty,
                    ResolveEntryDisplayName(entryState, entryId),
                    ResolveEntryIconType(entryState),
                    ResolveCommanderBadgeText(snapshot, entryId)
                }));
            }

            return string.Join("\n", parts);
        }

        private static string BuildSideRefreshDescriptor(CoopSidePresentation presentation, IReadOnlyCollection<string> selectableEntryIds)
        {
            return string.Join("|", new[]
            {
                presentation?.TitleText ?? string.Empty,
                presentation?.CountText ?? string.Empty,
                presentation?.DetailText ?? string.Empty,
                presentation?.BannerCodeText ?? string.Empty,
                selectableEntryIds?.Count.ToString() ?? "0"
            });
        }

        private static string ResolveSideBannerCode(BattleRuntimeState battleState, BattleSideEnum side)
        {
            string campaignBannerCode = TryResolveCampaignSideBannerCode(battleState, side);
            if (!string.IsNullOrWhiteSpace(campaignBannerCode))
                return campaignBannerCode;

            switch (ResolveSideFlavor(battleState, side))
            {
                case SideFlavor.Bandit:
                    return BanditBannerCode;
                case SideFlavor.Deserter:
                    return DeserterBannerCode;
                default:
                    return NeutralPlayerBannerCode;
            }
        }

        private static string TryResolveCampaignSideBannerCode(BattleRuntimeState battleState, BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return null;

            RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(battleState, side);
            string commanderClanBanner = TryResolveClanBannerCode(commanderEntry?.HeroClanId);
            if (!string.IsNullOrWhiteSpace(commanderClanBanner))
                return commanderClanBanner;

            string commanderHeroBanner = TryResolveHeroBannerCode(commanderEntry?.HeroId);
            if (!string.IsNullOrWhiteSpace(commanderHeroBanner))
                return commanderHeroBanner;

            BattlePartyState leaderParty = ResolveLeaderParty(battleState, side);
            string leaderHeroBanner = TryResolveHeroBannerCode(leaderParty?.Modifiers?.LeaderHeroId);
            if (!string.IsNullOrWhiteSpace(leaderHeroBanner))
                return leaderHeroBanner;

            string ownerHeroBanner = TryResolveHeroBannerCode(leaderParty?.Modifiers?.OwnerHeroId);
            if (!string.IsNullOrWhiteSpace(ownerHeroBanner))
                return ownerHeroBanner;

            BattleSideState sideState = ResolveSideState(battleState, side);
            if (sideState?.IsPlayerSide == true)
                return TryResolvePlayerClanBannerCode();

            return null;
        }

        private static void ResolveSideColors(BattleRuntimeState battleState, BattleSideEnum side, BasicCharacterObject character, out uint primaryColor, out uint secondaryColor)
        {
            Team missionTeam = ResolveMissionTeam(side);
            if (missionTeam != null)
            {
                primaryColor = missionTeam.Color;
                secondaryColor = missionTeam.Color2;
                return;
            }

            if (character?.Culture != null)
            {
                primaryColor = character.Culture.Color;
                secondaryColor = character.Culture.Color2;
                return;
            }

            switch (ResolveSideFlavor(battleState, side))
            {
                case SideFlavor.Bandit:
                    primaryColor = BanditPrimaryColor;
                    secondaryColor = BanditSecondaryColor;
                    break;
                case SideFlavor.Deserter:
                    primaryColor = DeserterPrimaryColor;
                    secondaryColor = DeserterSecondaryColor;
                    break;
                default:
                    primaryColor = NeutralPrimaryColor;
                    secondaryColor = NeutralSecondaryColor;
                    break;
            }
        }

        private static Team ResolveMissionTeam(BattleSideEnum side)
        {
            Mission mission = Mission.Current;
            if (mission == null)
                return null;

            switch (side)
            {
                case BattleSideEnum.Attacker:
                    return mission.AttackerTeam;
                case BattleSideEnum.Defender:
                    return mission.DefenderTeam;
                default:
                    return null;
            }
        }

        private static string TryResolveAssignedMissionPeerBannerCode(Team team)
        {
            if (team == null || GameNetwork.NetworkPeers == null)
                return null;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null || !ReferenceEquals(missionPeer.Team, team))
                    continue;

                string bannerCode = missionPeer.Peer?.BannerCode;
                if (!string.IsNullOrWhiteSpace(bannerCode))
                    return bannerCode;
            }

            return null;
        }

        private static string TryResolveSingleActivePlayerPeerBannerCode()
        {
            if (GameNetwork.NetworkPeers == null)
                return null;

            string resolvedBannerCode = null;
            int candidateCount = 0;
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                string bannerCode = missionPeer?.Peer?.BannerCode;
                if (string.IsNullOrWhiteSpace(bannerCode))
                    continue;

                candidateCount++;
                if (candidateCount > 1)
                    return null;

                resolvedBannerCode = bannerCode;
            }

            return resolvedBannerCode;
        }

        private static string TryResolvePlayerClanBannerCode()
        {
            try
            {
                string bannerCode = Hero.MainHero?.Clan?.Banner?.BannerCode;
                if (!string.IsNullOrWhiteSpace(bannerCode))
                    return bannerCode;

                return Clan.PlayerClan?.Banner?.BannerCode;
            }
            catch
            {
                return null;
            }
        }

        private static string TryResolveHeroBannerCode(string heroId)
        {
            if (string.IsNullOrWhiteSpace(heroId))
                return null;

            try
            {
                if (string.Equals(Hero.MainHero?.StringId, heroId, StringComparison.OrdinalIgnoreCase))
                    return Hero.MainHero?.Clan?.Banner?.BannerCode;
            }
            catch
            {
            }

            Hero hero = TryResolveCampaignObject<Hero>(heroId);
            return hero?.Clan?.Banner?.BannerCode;
        }

        private static string TryResolveClanBannerCode(string clanId)
        {
            if (string.IsNullOrWhiteSpace(clanId))
                return null;

            try
            {
                if (string.Equals(Clan.PlayerClan?.StringId, clanId, StringComparison.OrdinalIgnoreCase))
                    return Clan.PlayerClan?.Banner?.BannerCode;
            }
            catch
            {
            }

            Clan clan = TryResolveCampaignObject<Clan>(clanId);
            return clan?.Banner?.BannerCode;
        }

        private static T TryResolveCampaignObject<T>(string stringId) where T : MBObjectBase
        {
            if (string.IsNullOrWhiteSpace(stringId))
                return null;

            try
            {
                MBObjectManager objectManager = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
                return objectManager?.GetObject<T>(stringId);
            }
            catch
            {
                return null;
            }
        }

        private static SideFlavor ResolveSideFlavor(BattleRuntimeState battleState, BattleSideEnum side)
        {
            BattleSideState sideState = ResolveSideState(battleState, side);
            BattlePartyState leaderParty = ResolveLeaderParty(battleState, side);
            RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(battleState, side);
            string[] diagnosticTexts =
            {
                sideState?.SideText,
                sideState?.LeaderPartyId,
                leaderParty?.PartyName,
                commanderEntry?.TroopName,
                commanderEntry?.HeroRole,
                commanderEntry?.CharacterId,
                commanderEntry?.HeroOccupationId
            };

            if (diagnosticTexts.Any(text => ContainsKeyword(text, "deserter")))
                return SideFlavor.Deserter;

            if (diagnosticTexts.Any(text =>
                    ContainsKeyword(text, "bandit") ||
                    ContainsKeyword(text, "looter") ||
                    ContainsKeyword(text, "raider") ||
                    ContainsKeyword(text, "outlaw")))
            {
                return SideFlavor.Bandit;
            }

            return SideFlavor.Army;
        }

        private static BattleSideState ResolveSideState(BattleRuntimeState battleState, BattleSideEnum side)
        {
            if (battleState?.SidesByKey == null || side == BattleSideEnum.None)
                return null;

            battleState.SidesByKey.TryGetValue(NormalizeSideKey(side), out BattleSideState sideState);
            return sideState;
        }

        private static BattlePartyState ResolveLeaderParty(BattleRuntimeState battleState, BattleSideEnum side)
        {
            BattleSideState sideState = ResolveSideState(battleState, side);
            if (sideState == null || battleState?.PartiesById == null || string.IsNullOrWhiteSpace(sideState.LeaderPartyId))
                return null;

            return battleState.PartiesById.TryGetValue(sideState.LeaderPartyId, out BattlePartyState partyState)
                ? partyState
                : null;
        }

        private static string ResolveMountCreationKey(Equipment equipment, string stableSeedSource)
        {
            ItemObject horseItem = equipment?.Horse.Item;
            if (horseItem == null || !horseItem.HasHorseComponent)
                return string.Empty;

            try
            {
                int stableSeed = ComputeStableSeed(stableSeedSource, horseItem.StringId);
                return MountCreationKey.GetRandomMountKeyString(horseItem, stableSeed);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int ComputeStableSeed(string primary, string secondary)
        {
            unchecked
            {
                int hash = 17;
                foreach (char ch in (primary ?? string.Empty))
                    hash = (hash * 31) + ch;
                foreach (char ch in (secondary ?? string.Empty))
                    hash = (hash * 31) + ch;
                return hash == int.MinValue ? int.MaxValue : Math.Abs(hash);
            }
        }

        private static string ResolveEntryTypeLabel(RosterEntryState entryState)
        {
            if (entryState == null)
                return string.Empty;

            if (entryState.IsMounted && entryState.IsRanged)
                return "Horse Archer";

            if (entryState.IsMounted)
                return "Cavalry";

            if (entryState.IsRanged)
                return IsCrossbowEntry(entryState) ? "Crossbowman" : "Archer";

            if (entryState.HasThrown && !entryState.HasShield)
                return "Skirmisher";

            if (entryState.HasShield)
                return "Shield Infantry";

            return "Infantry";
        }

        private static string NormalizeHeroRole(string heroRole)
        {
            if (string.IsNullOrWhiteSpace(heroRole))
                return string.Empty;

            switch (heroRole.Trim().ToLowerInvariant())
            {
                case "player":
                    return "Player Hero";
                case "lord":
                    return "Lord";
                case "companion":
                case "wanderer":
                    return "Companion";
                case "captain":
                    return "Captain";
                default:
                    return FormatReadableLabel(heroRole);
            }
        }

        private static bool IsCrossbowEntry(RosterEntryState entryState)
        {
            return entryState != null && entryState.SkillCrossbow > 0 && entryState.SkillCrossbow >= entryState.SkillBow;
        }

        private static bool IsHeavyEntry(RosterEntryState entryState)
        {
            return entryState != null &&
                   (entryState.IsHero ||
                    entryState.Tier >= 5 ||
                    (entryState.Tier >= 4 && entryState.HasShield) ||
                    entryState.BaseHitPoints >= 120);
        }

        private static BattleSideEnum NormalizeSide(string rawSide)
        {
            if (string.IsNullOrWhiteSpace(rawSide))
                return BattleSideEnum.None;

            if (string.Equals(rawSide, nameof(BattleSideEnum.Attacker), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawSide, "attacker", StringComparison.OrdinalIgnoreCase))
            {
                return BattleSideEnum.Attacker;
            }

            if (string.Equals(rawSide, nameof(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawSide, "defender", StringComparison.OrdinalIgnoreCase))
            {
                return BattleSideEnum.Defender;
            }

            return BattleSideEnum.None;
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
                    return string.Empty;
            }
        }

        private static bool ContainsKeyword(string value, string keyword)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatReadableLabel(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return string.Empty;

            string normalized = rawValue.Trim().Replace('_', ' ');
            var result = new System.Text.StringBuilder(normalized.Length + 8);
            for (int index = 0; index < normalized.Length; index++)
            {
                char current = normalized[index];
                if (index > 0 &&
                    char.IsUpper(current) &&
                    !char.IsWhiteSpace(normalized[index - 1]) &&
                    !char.IsUpper(normalized[index - 1]))
                {
                    result.Append(' ');
                }

                result.Append(current);
            }

            return string.Join(" ", result
                .ToString()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => char.ToUpperInvariant(token[0]) + token.Substring(1)));
        }

        private enum SideFlavor
        {
            Army = 0,
            Bandit = 1,
            Deserter = 2
        }
    }
}
