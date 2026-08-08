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
    internal static class HideoutCampaignBattleAdapter
    {
        private const string DayControllerTypeName =
            "SandBox.Missions.MissionLogics.Hideout.HideoutMissionController";
        private const string AmbushControllerTypeName =
            "SandBox.Missions.MissionLogics.Hideout.HideoutAmbushMissionController";
        private const string PlayerFirstPhaseCountFieldName = "_firstPhasePlayerSideTroopCount";
        private const string EnemyFirstPhaseCountFieldName = "_firstPhaseEnemyTroopCount";

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
            bool hasHideoutCampaignContext =
                battle?.IsHideoutBattle == true ||
                settlement?.IsHideout == true;
            if (!hasHideoutCampaignContext ||
                mission?.MissionBehaviors == null ||
                !CoopHideoutBossPhaseContract.IsSupportedDayHideoutSceneName(mission.SceneName))
            {
                return false;
            }

            bool hasDayController = mission.MissionBehaviors.Any(behavior =>
                string.Equals(
                    behavior?.GetType().FullName,
                    DayControllerTypeName,
                    StringComparison.Ordinal));
            bool hasAmbushController = mission.MissionBehaviors.Any(behavior =>
                string.Equals(
                    behavior?.GetType().FullName,
                    AmbushControllerTypeName,
                    StringComparison.Ordinal));
            return hasDayController && !hasAmbushController;
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
            if (mission?.MissionBehaviors == null)
                return false;

            MissionBehavior controller = mission.MissionBehaviors.FirstOrDefault(behavior =>
                string.Equals(
                    behavior?.GetType().FullName,
                    DayControllerTypeName,
                    StringComparison.Ordinal));
            if (controller == null)
            {
                diagnostics = "native-controller-missing";
                return false;
            }

            if (!TryResolveControllerPlayerSide(controller, out BattleSideEnum playerSide))
            {
                diagnostics = "native-player-side-unresolved";
                return false;
            }

            MethodInfo getAllTroopsForSide = controller.GetType().GetMethod(
                "GetAllTroopsForSide",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(BattleSideEnum) },
                modifiers: null);
            IEnumerable originEnumerable = getAllTroopsForSide?.Invoke(
                controller,
                new object[] { side }) as IEnumerable;
            List<IAgentOriginBase> allOrigins = originEnumerable?
                .Cast<object>()
                .OfType<IAgentOriginBase>()
                .Where(origin => origin != null)
                .ToList() ?? new List<IAgentOriginBase>();
            if (allOrigins.Count == 0)
            {
                diagnostics = "native-origin-list-empty Side=" + side;
                return false;
            }

            string phaseCountFieldName = side == playerSide
                ? PlayerFirstPhaseCountFieldName
                : EnemyFirstPhaseCountFieldName;
            FieldInfo phaseCountField = controller.GetType().GetField(
                phaseCountFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(phaseCountField?.GetValue(controller) is int nativeFirstPhaseCount))
            {
                diagnostics =
                    "native-first-phase-count-missing Side=" + side +
                    " Field=" + phaseCountFieldName;
                return false;
            }

            int effectiveFirstPhaseCount = side == playerSide
                ? nativeFirstPhaseCount
                : CoopHideoutBossPhaseContract.ResolveVanillaFirstPhaseDefenderCount(
                    allOrigins.Count,
                    nativeFirstPhaseCount);
            if (!CoopHideoutBossPhaseContract.IsValidFirstPhaseParticipantCount(
                    allOrigins.Count,
                    effectiveFirstPhaseCount))
            {
                diagnostics =
                    "native-first-phase-count-invalid Side=" + side +
                    " Total=" + allOrigins.Count +
                    " Native=" + nativeFirstPhaseCount +
                    " Effective=" + effectiveFirstPhaseCount;
                return false;
            }

            List<PartyGroupAgentOrigin> initialOrigins = allOrigins
                .Take(effectiveFirstPhaseCount)
                .OfType<PartyGroupAgentOrigin>()
                .ToList();
            if (initialOrigins.Count != effectiveFirstPhaseCount)
            {
                diagnostics =
                    "native-origin-type-mismatch Side=" + side +
                    " Expected=" + effectiveFirstPhaseCount +
                    " PartyGroup=" + initialOrigins.Count;
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
            foreach (PartyGroupAgentOrigin origin in initialOrigins)
            {
                string partyId = origin.Party?.Id ?? string.Empty;
                string troopId = origin.Troop?.StringId ?? string.Empty;
                ParticipantBucket bucket = FindParticipantBucket(
                    buckets,
                    partyId,
                    troopId,
                    requireParty: true);
                if (bucket == null && string.IsNullOrWhiteSpace(partyId))
                {
                    bucket = FindParticipantBucket(
                        buckets,
                        null,
                        troopId,
                        requireParty: false);
                }

                if (bucket == null)
                {
                    if (unmatchedSamples.Count < 8)
                    {
                        unmatchedSamples.Add(
                            (partyId.Length > 0 ? partyId : "party") + "/" +
                            (troopId.Length > 0 ? troopId : "troop"));
                    }
                    continue;
                }

                bucket.RemainingHealthyCount--;
                orderedEntryIds.Add(bucket.Troop.EntryId);
            }

            if (orderedEntryIds.Count != effectiveFirstPhaseCount)
            {
                diagnostics =
                    "participant-match-incomplete Side=" + side +
                    " NativeTotal=" + allOrigins.Count +
                    " NativeFirstPhase=" + nativeFirstPhaseCount +
                    " EffectiveFirstPhase=" + effectiveFirstPhaseCount +
                    " Matched=" + orderedEntryIds.Count +
                    " UnmatchedSamples=[" + string.Join("; ", unmatchedSamples) + "]";
                orderedEntryIds.Clear();
                return false;
            }

            diagnostics =
                "participant-match-exact Side=" + side +
                " NativeTotal=" + allOrigins.Count +
                " NativeFirstPhase=" + nativeFirstPhaseCount +
                " EffectiveFirstPhase=" + effectiveFirstPhaseCount +
                " Matched=" + orderedEntryIds.Count +
                " Buckets=" + buckets.Count;
            return true;
        }

        private static bool TryResolveControllerPlayerSide(
            MissionBehavior controller,
            out BattleSideEnum playerSide)
        {
            playerSide = BattleSideEnum.None;
            try
            {
                PropertyInfo property = controller?.GetType().GetProperty(
                    "PlayerSide",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object value = property?.GetValue(controller);
                if (value is BattleSideEnum resolvedSide && resolvedSide != BattleSideEnum.None)
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
    }
}
