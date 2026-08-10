using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.Network.Messages;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign.Hideout
{
    /// <summary>
    /// Reads the current native nighttime-hideout selection after SandBox has built it.
    /// No SandBox type is referenced directly so the dedicated build remains independent
    /// from assemblies that are not part of its compile-time contract.
    /// </summary>
    internal static class HideoutAmbushCampaignBattleAdapter
    {
        private const string DayControllerTypeName =
            "SandBox.Missions.MissionLogics.Hideout.HideoutMissionController";
        private const string PlayerSideFieldName = "_playerSide";
        private const string PlayerPriorTroopsFieldName = "_playerPriorTroops";
        private const string PlayerTroopCountFieldName = "_playerTroopCount";
        private const string InitialHideoutPopulationFieldName = "_initialHideoutPopulation";
        private const string SentryCountFieldName = "_sentryCount";

        private sealed class ParticipantBucket
        {
            public TroopStackInfo Troop { get; set; }

            public int RemainingHealthyCount { get; set; }
        }

        public static bool IsCampaignStage(
            MapEvent battle,
            Settlement settlement,
            Mission mission)
        {
            return TryValidateActiveMission(
                battle,
                settlement,
                mission,
                out _);
        }

        public static bool TryValidateActiveMission(
            MapEvent battle,
            Settlement settlement,
            Mission mission,
            out string diagnostics)
        {
            bool hasHideoutCampaignContext =
                battle?.IsHideoutBattle == true ||
                settlement?.IsHideout == true;
            bool sceneSupported =
                CoopHideoutBossPhaseContract.IsSupportedDayHideoutSceneName(
                    mission?.SceneName);
            if (!hasHideoutCampaignContext ||
                !sceneSupported ||
                mission?.MissionBehaviors == null)
            {
                diagnostics =
                    "Context=" + hasHideoutCampaignContext +
                    " SceneSupported=" + sceneSupported +
                    " Behaviors=" + (mission?.MissionBehaviors != null);
                return false;
            }

            MissionBehavior dayController = mission.MissionBehaviors.FirstOrDefault(
                behavior => string.Equals(
                    behavior?.GetType().FullName,
                    DayControllerTypeName,
                    StringComparison.Ordinal));
            MissionBehavior ambushController = mission.MissionBehaviors.FirstOrDefault(
                behavior => string.Equals(
                    behavior?.GetType().FullName,
                    CoopHideoutAmbushContract.NativeControllerTypeName,
                    StringComparison.Ordinal));
            Type controllerType = ambushController?.GetType();
            bool hasSelectedRosterContract =
                controllerType?.GetField(
                    PlayerPriorTroopsFieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic) != null &&
                controllerType.GetField(
                    PlayerTroopCountFieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic) != null &&
                controllerType.GetField(
                    PlayerSideFieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic) != null &&
                controllerType.GetField(
                    InitialHideoutPopulationFieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic) != null &&
                controllerType.GetField(
                    SentryCountFieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic) != null;

            diagnostics =
                "DayController=" + (dayController != null) +
                " AmbushController=" + (ambushController != null) +
                " SelectedRosterContract=" + hasSelectedRosterContract +
                " Scene=" + (mission.SceneName ?? "null");
            return CoopHideoutAmbushContract.CanEnterNightHideoutCampaignBridge(
                dayController != null,
                ambushController != null,
                hasSelectedRosterContract);
        }

        public static bool TryBuildInitialParticipantEntryOrder(
            Mission mission,
            BattleSideEnum side,
            BattleSideSnapshotMessage sideSnapshot,
            out List<string> orderedEntryIds,
            out string diagnostics)
        {
            orderedEntryIds = new List<string>();
            diagnostics = "mission-null";
            MissionBehavior controller = mission?.MissionBehaviors?.FirstOrDefault(
                behavior => string.Equals(
                    behavior?.GetType().FullName,
                    CoopHideoutAmbushContract.NativeControllerTypeName,
                    StringComparison.Ordinal));
            if (controller == null)
            {
                diagnostics = "native-ambush-controller-missing";
                return false;
            }

            if (!TryResolveControllerPlayerSide(controller, out BattleSideEnum playerSide))
            {
                diagnostics = "native-player-side-unresolved";
                return false;
            }

            List<ParticipantBucket> buckets = BuildBuckets(sideSnapshot);
            if (buckets.Count == 0)
            {
                diagnostics = "snapshot-buckets-empty Side=" + side;
                return false;
            }

            if (side == playerSide)
            {
                return TryBuildPlayerOrder(
                    controller,
                    buckets,
                    orderedEntryIds,
                    out diagnostics);
            }

            return TryBuildEnemyOrder(
                controller,
                mission,
                side,
                buckets,
                orderedEntryIds,
                out diagnostics);
        }

        private static bool TryBuildPlayerOrder(
            MissionBehavior controller,
            List<ParticipantBucket> buckets,
            List<string> orderedEntryIds,
            out string diagnostics)
        {
            ParticipantBucket mainHero = buckets.FirstOrDefault(bucket =>
                bucket.RemainingHealthyCount > 0 &&
                bucket.Troop?.IsPlayerCharacter == true);
            if (mainHero == null)
            {
                diagnostics = "native-player-main-hero-snapshot-entry-missing";
                return false;
            }

            mainHero.RemainingHealthyCount--;
            orderedEntryIds.Add(mainHero.Troop.EntryId);

            FieldInfo priorTroopsField = controller.GetType().GetField(
                PlayerPriorTroopsFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            IEnumerable priorTroopsEnumerable = priorTroopsField?.GetValue(controller) as IEnumerable;
            List<IAgentOriginBase> priorTroops = priorTroopsEnumerable?
                .Cast<object>()
                .OfType<IAgentOriginBase>()
                .Where(origin => origin != null)
                .ToList() ?? new List<IAgentOriginBase>();

            FieldInfo playerTroopCountField = controller.GetType().GetField(
                PlayerTroopCountFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            int configuredPriorCount =
                playerTroopCountField?.GetValue(controller) is int count
                    ? Math.Max(0, count)
                    : -1;
            if (configuredPriorCount < 0 || priorTroops.Count != configuredPriorCount)
            {
                diagnostics =
                    "native-player-prior-roster-not-ready Configured=" + configuredPriorCount +
                    " Materialized=" + priorTroops.Count;
                orderedEntryIds.Clear();
                return false;
            }

            var unmatchedSamples = new List<string>();
            foreach (IAgentOriginBase origin in priorTroops)
            {
                if (TryMatchOrigin(buckets, origin, out string entryId))
                {
                    orderedEntryIds.Add(entryId);
                }
                else if (unmatchedSamples.Count < 8)
                {
                    unmatchedSamples.Add(origin?.Troop?.StringId ?? "troop");
                }
            }

            int expectedCount = 1 + configuredPriorCount;
            if (orderedEntryIds.Count != expectedCount)
            {
                diagnostics =
                    "native-player-participant-match-incomplete Expected=" + expectedCount +
                    " Matched=" + orderedEntryIds.Count +
                    " UnmatchedSamples=[" + string.Join("; ", unmatchedSamples) + "]";
                orderedEntryIds.Clear();
                return false;
            }

            diagnostics =
                "native-player-participant-match-exact MainHero=1" +
                " PriorTroops=" + configuredPriorCount +
                " Matched=" + orderedEntryIds.Count;
            return true;
        }

        private static bool TryBuildEnemyOrder(
            MissionBehavior controller,
            Mission mission,
            BattleSideEnum side,
            List<ParticipantBucket> buckets,
            List<string> orderedEntryIds,
            out string diagnostics)
        {
            int totalHealthy = buckets.Sum(bucket => bucket.RemainingHealthyCount);
            bool hasInitialPopulation = TryReadIntField(
                controller,
                InitialHideoutPopulationFieldName,
                out int initialHideoutPopulation);
            bool hasSentryCount = TryReadIntField(
                controller,
                SentryCountFieldName,
                out int sentryCount);
            if (!hasInitialPopulation ||
                initialHideoutPopulation <= 0 ||
                !hasSentryCount ||
                sentryCount <= 0)
            {
                diagnostics =
                    "native-enemy-sentry-roster-not-ready" +
                    " InitialPopulation=" + initialHideoutPopulation +
                    " SentryCount=" + sentryCount;
                return false;
            }

            int liveMatchedCount = 0;
            int syntheticMatchedCount = 0;
            List<IAgentOriginBase> liveEnemyOrigins = mission?.Agents?
                .Where(agent =>
                    agent != null &&
                    agent.IsHuman &&
                    agent.Team?.Side == side &&
                    agent.Origin != null)
                .Select(agent => agent.Origin)
                .ToList() ?? new List<IAgentOriginBase>();

            var unmatchedSamples = new List<string>();
            foreach (IAgentOriginBase origin in liveEnemyOrigins)
            {
                if (TryMatchOrigin(buckets, origin, out string entryId))
                {
                    orderedEntryIds.Add(entryId);
                    liveMatchedCount++;
                }
                else if (TryResolveSyntheticOriginEntryId(
                             buckets,
                             origin,
                             out entryId))
                {
                    orderedEntryIds.Add(entryId);
                    syntheticMatchedCount++;
                }
                else if (unmatchedSamples.Count < 8)
                {
                    unmatchedSamples.Add(origin?.Troop?.StringId ?? "troop");
                }
            }

            bool hasValidNativePopulationContract =
                CoopHideoutAmbushContract.IsValidNativeInitialEnemyContract(
                    initialHideoutPopulation,
                    liveEnemyOrigins.Count,
                    sentryCount);
            if (hasValidNativePopulationContract &&
                orderedEntryIds.Count == liveEnemyOrigins.Count)
            {
                diagnostics =
                    "native-live-enemy-participant-match-exact" +
                    " InitialPopulation=" + initialHideoutPopulation +
                    " NativeSentries=" + sentryCount +
                    " LiveInitialEnemies=" + liveEnemyOrigins.Count +
                    " ExactMatched=" + liveMatchedCount +
                    " SyntheticMatched=" + syntheticMatchedCount +
                    " Matched=" + orderedEntryIds.Count;
                return true;
            }

            orderedEntryIds.Clear();
            diagnostics =
                "native-live-enemy-participant-match-incomplete" +
                " InitialPopulation=" + initialHideoutPopulation +
                " NativeSentries=" + sentryCount +
                " LiveInitialEnemies=" + liveEnemyOrigins.Count +
                " PopulationContractValid=" + hasValidNativePopulationContract +
                " ExactMatched=" + liveMatchedCount +
                " SyntheticMatched=" + syntheticMatchedCount +
                " SnapshotHealthy=" + totalHealthy +
                " UnmatchedSamples=[" + string.Join("; ", unmatchedSamples) + "]";
            return false;
        }

        private static List<ParticipantBucket> BuildBuckets(
            BattleSideSnapshotMessage sideSnapshot)
        {
            return (sideSnapshot?.Troops ?? new List<TroopStackInfo>())
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
        }

        private static bool TryResolveControllerPlayerSide(
            MissionBehavior controller,
            out BattleSideEnum playerSide)
        {
            playerSide = BattleSideEnum.None;
            try
            {
                FieldInfo field = controller?.GetType().GetField(
                    PlayerSideFieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(controller) is BattleSideEnum resolvedSide &&
                    resolvedSide != BattleSideEnum.None)
                {
                    playerSide = resolvedSide;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryReadIntField(
            MissionBehavior controller,
            string fieldName,
            out int value)
        {
            value = 0;
            try
            {
                FieldInfo field = controller?.GetType().GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(controller) is int resolved)
                {
                    value = Math.Max(0, resolved);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryMatchOrigin(
            IEnumerable<ParticipantBucket> buckets,
            IAgentOriginBase origin,
            out string entryId)
        {
            entryId = null;
            if (origin?.Troop == null)
                return false;

            string partyId = (origin as PartyGroupAgentOrigin)?.Party?.Id ?? string.Empty;
            string troopId = origin.Troop.StringId ?? string.Empty;
            ParticipantBucket bucket = FindParticipantBucket(
                buckets,
                partyId,
                troopId,
                requireParty: !string.IsNullOrWhiteSpace(partyId)) ??
                FindParticipantBucket(
                    buckets,
                    null,
                    troopId,
                    requireParty: false);
            if (bucket == null)
                return false;

            bucket.RemainingHealthyCount--;
            entryId = bucket.Troop.EntryId;
            return true;
        }

        private static bool TryResolveSyntheticOriginEntryId(
            IEnumerable<ParticipantBucket> buckets,
            IAgentOriginBase origin,
            out string entryId)
        {
            entryId = null;
            if (origin?.Troop == null)
                return false;

            string partyId = (origin as PartyGroupAgentOrigin)?.Party?.Id ?? string.Empty;
            string troopId = origin.Troop.StringId ?? string.Empty;
            ParticipantBucket bucket = FindParticipantBucket(
                buckets,
                partyId,
                troopId,
                requireParty: !string.IsNullOrWhiteSpace(partyId),
                requireRemaining: false) ??
                FindParticipantBucket(
                    buckets,
                    null,
                    troopId,
                    requireParty: false,
                    requireRemaining: false);
            if (bucket?.Troop == null || IsBossTroop(bucket.Troop))
                return false;

            entryId = bucket.Troop.EntryId;
            return !string.IsNullOrWhiteSpace(entryId);
        }

        private static ParticipantBucket FindParticipantBucket(
            IEnumerable<ParticipantBucket> buckets,
            string partyId,
            string troopId,
            bool requireParty,
            bool requireRemaining = true)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return null;

            foreach (ParticipantBucket bucket in buckets ?? Enumerable.Empty<ParticipantBucket>())
            {
                TroopStackInfo troop = bucket?.Troop;
                if (troop == null ||
                    (requireRemaining && bucket.RemainingHealthyCount <= 0))
                    continue;
                if (requireParty &&
                    !string.Equals(troop.PartyId, partyId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(troop.OriginalCharacterId, troopId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(troop.CharacterId, troopId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(troop.SpawnTemplateId, troopId, StringComparison.OrdinalIgnoreCase))
                {
                    return bucket;
                }
            }

            return null;
        }

        private static bool IsBossTroop(TroopStackInfo troop)
        {
            string token =
                troop?.OriginalCharacterId ??
                troop?.CharacterId ??
                troop?.SpawnTemplateId ??
                string.Empty;
            return token.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   token.IndexOf("chief", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   token.IndexOf("leader", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
