using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.LordsHall;
using CoopSpectator.Network.Messages;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Source.Missions.Handlers;

namespace CoopSpectator.Campaign.LordsHall
{
    internal static class LordsHallCampaignBattleAdapter
    {
        private sealed class ParticipantBucket
        {
            public TroopStackInfo Troop { get; set; }

            public int RemainingHealthyCount { get; set; }
        }

        public static bool IsCampaignStage(MapEvent battle, Settlement settlement)
        {
            return battle?.IsSiegeAssault == true &&
                   settlement?.IsFortification == true &&
                   settlement.CurrentSiegeState == Settlement.SiegeState.InTheLordsHall;
        }

        public static bool TryResolveExpectedScene(
            Settlement settlement,
            out string sceneName,
            out string diagnostics)
        {
            sceneName = string.Empty;
            diagnostics = "settlement-null";
            if (settlement?.IsFortification != true)
                return false;

            try
            {
                int wallLevel = settlement.Town != null ? settlement.Town.GetWallLevel() : 0;
                var location = settlement.LocationComplex?.GetLocationWithId(LordsHallScenarioContract.SceneLocationId);
                if (location == null)
                {
                    diagnostics =
                        "location-missing Settlement=" + (settlement.StringId ?? "null") +
                        " LocationId=" + LordsHallScenarioContract.SceneLocationId;
                    return false;
                }

                sceneName = location.GetSceneName(wallLevel) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sceneName))
                {
                    diagnostics =
                        "scene-empty Settlement=" + (settlement.StringId ?? "null") +
                        " WallLevel=" + wallLevel;
                    return false;
                }

                diagnostics =
                    "resolved Settlement=" + settlement.StringId +
                    " WallLevel=" + wallLevel +
                    " Scene=" + sceneName;
                return true;
            }
            catch (Exception ex)
            {
                diagnostics = "scene-resolution-exception " + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        public static bool TryValidateActiveMission(
            MapEvent battle,
            Settlement settlement,
            Mission mission,
            out string expectedScene,
            out string diagnostics)
        {
            expectedScene = string.Empty;
            diagnostics = "not-lords-hall-campaign-stage";
            if (!IsCampaignStage(battle, settlement))
                return false;

            if (battle.PlayerSide != BattleSideEnum.Attacker)
            {
                diagnostics = "player-side-unsupported Side=" + battle.PlayerSide;
                return false;
            }

            if (mission == null)
            {
                diagnostics = "mission-null";
                return false;
            }

            if (!TryResolveExpectedScene(settlement, out expectedScene, out string sceneDiagnostics))
            {
                diagnostics = "scene={" + sceneDiagnostics + "}";
                return false;
            }

            if (!string.Equals(mission.SceneName, expectedScene, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "scene-mismatch Runtime=" + (mission.SceneName ?? "null") +
                    " Expected=" + expectedScene;
                return false;
            }

            if (!CampaignMissionShellRuntimeState.TryGetMissionShell(
                    mission.SceneName,
                    out string missionShell,
                    out string shellDiagnostics) ||
                !CampaignMissionShellRuntimeState.IsLordsHallMissionShell(missionShell))
            {
                diagnostics = "mission-shell={" + shellDiagnostics + "}";
                return false;
            }

            LordsHallFightMissionController controller =
                mission.GetMissionBehavior<LordsHallFightMissionController>();
            if (controller == null)
            {
                diagnostics = "native-controller-missing";
                return false;
            }

            diagnostics =
                "validated Settlement=" + settlement.StringId +
                " Scene=" + expectedScene +
                " MissionShell=" + missionShell +
                " PlayerSide=" + battle.PlayerSide;
            return true;
        }

        public static bool TryBuildParticipantEntryOrder(
            Mission mission,
            BattleSideEnum side,
            BattleSideSnapshotMessage sideSnapshot,
            out List<string> orderedEntryIds,
            out string diagnostics)
        {
            orderedEntryIds = new List<string>();
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            LordsHallFightMissionController controller =
                mission.GetMissionBehavior<LordsHallFightMissionController>();
            if (controller == null)
            {
                diagnostics = "native-controller-missing";
                return false;
            }

            List<IAgentOriginBase> allOrigins = controller
                .GetAllTroopsForSide(side)?
                .Where(origin => origin != null)
                .ToList() ?? new List<IAgentOriginBase>();
            if (allOrigins.Count == 0)
            {
                diagnostics = "native-origin-list-empty Side=" + side;
                return false;
            }

            List<PartyGroupAgentOrigin> origins = allOrigins.OfType<PartyGroupAgentOrigin>().ToList();
            if (origins.Count != allOrigins.Count)
            {
                diagnostics =
                    "native-origin-type-mismatch Side=" + side +
                    " Total=" + allOrigins.Count +
                    " PartyGroup=" + origins.Count;
                return false;
            }

            var buckets = (sideSnapshot?.Troops ?? new List<TroopStackInfo>())
                .Where(troop =>
                    troop != null &&
                    !string.IsNullOrWhiteSpace(troop.EntryId) &&
                    troop.Count - troop.WoundedCount > 0)
                .Select(troop => new ParticipantBucket
                {
                    Troop = troop,
                    RemainingHealthyCount = Math.Max(0, troop.Count - troop.WoundedCount)
                })
                .ToList();
            if (buckets.Count == 0)
            {
                diagnostics = "snapshot-buckets-empty Side=" + side;
                return false;
            }

            var unmatchedSamples = new List<string>();
            foreach (PartyGroupAgentOrigin origin in origins)
            {
                string partyId = origin.Party?.Id ?? string.Empty;
                string troopId = origin.Troop?.StringId ?? string.Empty;
                ParticipantBucket bucket = FindParticipantBucket(buckets, partyId, troopId, requireParty: true);
                if (bucket == null && string.IsNullOrWhiteSpace(partyId))
                    bucket = FindParticipantBucket(buckets, null, troopId, requireParty: false);
                if (bucket == null)
                {
                    if (unmatchedSamples.Count < 8)
                        unmatchedSamples.Add((partyId.Length > 0 ? partyId : "party") + "/" + (troopId.Length > 0 ? troopId : "troop"));
                    continue;
                }

                bucket.RemainingHealthyCount--;
                orderedEntryIds.Add(bucket.Troop.EntryId);
            }

            if (orderedEntryIds.Count != origins.Count)
            {
                diagnostics =
                    "participant-match-incomplete Side=" + side +
                    " NativeOrigins=" + origins.Count +
                    " Matched=" + orderedEntryIds.Count +
                    " UnmatchedSamples=[" + string.Join("; ", unmatchedSamples) + "]";
                orderedEntryIds.Clear();
                return false;
            }

            diagnostics =
                "participant-match-exact Side=" + side +
                " NativeOrigins=" + origins.Count +
                " Matched=" + orderedEntryIds.Count +
                " Buckets=" + buckets.Count;
            return true;
        }

        private static ParticipantBucket FindParticipantBucket(
            IEnumerable<ParticipantBucket> buckets,
            string partyId,
            string troopId,
            bool requireParty)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return null;

            foreach (ParticipantBucket bucket in buckets ?? Enumerable.Empty<ParticipantBucket>())
            {
                TroopStackInfo troop = bucket?.Troop;
                if (troop == null || bucket.RemainingHealthyCount <= 0)
                    continue;

                if (requireParty && !string.Equals(troop.PartyId, partyId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(troop.OriginalCharacterId, troopId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(troop.CharacterId, troopId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(troop.SpawnTemplateId, troopId, StringComparison.OrdinalIgnoreCase))
                {
                    return bucket;
                }
            }

            return null;
        }
    }
}
