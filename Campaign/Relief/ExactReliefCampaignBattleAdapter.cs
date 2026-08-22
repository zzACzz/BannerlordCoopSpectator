using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Relief;
using CoopSpectator.Network.Messages;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Source.Missions;

namespace CoopSpectator.Campaign.Relief
{
    internal static class ExactReliefCampaignBattleAdapter
    {
        private sealed class SettlementCandidateBinding
        {
            public SiegeEvent SiegeEvent { get; set; }

            public ExactReliefSiegeCandidateDescriptor Descriptor { get; set; }
        }

        private static readonly FieldInfo MissionInitializerRecordBackingField =
            typeof(Mission).GetField(
                "<InitializerRecord>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo MissionInitializerRecordProperty =
            typeof(Mission).GetProperty(
                "InitializerRecord",
                BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool IsCampaignBattle(MapEvent battle)
        {
            return battle?.IsSiegeOutside == true &&
                   battle.IsPlayerMapEvent &&
                   battle.PlayerSide != BattleSideEnum.None;
        }

        public static bool IsCampaignStage(
            MapEvent battle,
            Settlement settlement)
        {
            return TryResolveSettlement(
                       battle,
                       out Settlement resolvedSettlement,
                       out _,
                       out _) &&
                   IsSameSettlement(settlement, resolvedSettlement);
        }

        public static bool TryResolveSettlement(
            MapEvent battle,
            out Settlement settlement,
            out BattleSideEnum besiegerBattleSide,
            out string diagnostics)
        {
            settlement = null;
            besiegerBattleSide = BattleSideEnum.None;
            diagnostics = "not-relief-campaign-battle";
            if (!IsCampaignBattle(battle))
                return false;

            var attackerParties = BuildPartyDescriptors(battle.AttackerSide);
            var defenderParties = BuildPartyDescriptors(battle.DefenderSide);
            List<SettlementCandidateBinding> candidateBindings =
                BuildSettlementCandidateBindings();
            List<ExactReliefSiegeCandidateDescriptor> candidates =
                candidateBindings
                    .Select(binding => binding.Descriptor)
                    .ToList();

            ExactReliefSettlementResolution resolution =
                ExactReliefSettlementResolutionPolicy.Resolve(
                    battle.EventType.ToString(),
                    attackerParties,
                    defenderParties,
                    candidates);
            if (resolution.Status != ExactReliefSettlementResolutionStatus.Resolved ||
                resolution.Candidate == null)
            {
                diagnostics =
                    "settlement-resolution-" +
                    resolution.Status.ToString().ToLowerInvariant() +
                    " MatchingCandidates=" +
                    resolution.MatchingCandidateCount +
                    " SiegeEvents=" + candidateBindings.Count +
                    " AttackerParties=" + attackerParties.Count +
                    " DefenderParties=" + defenderParties.Count;
                return false;
            }

            SettlementCandidateBinding resolvedBinding =
                candidateBindings.FirstOrDefault(binding =>
                    ReferenceEquals(binding.Descriptor, resolution.Candidate));
            settlement = resolvedBinding?.SiegeEvent?.BesiegedSettlement;
            if (settlement == null)
            {
                diagnostics = "settlement-resolution-binding-missing";
                return false;
            }

            besiegerBattleSide = resolution.BesiegerBattleSide ==
                                 ExactReliefBesiegerBattleSide.Attacker
                ? BattleSideEnum.Attacker
                : resolution.BesiegerBattleSide ==
                  ExactReliefBesiegerBattleSide.Defender
                    ? BattleSideEnum.Defender
                    : BattleSideEnum.None;
            if (besiegerBattleSide == BattleSideEnum.None)
            {
                settlement = null;
                diagnostics = "settlement-resolution-side-missing";
                return false;
            }

            diagnostics =
                "settlement-resolution-resolved Settlement=" +
                settlement.StringId +
                " BesiegerBattleSide=" + besiegerBattleSide +
                " SiegeEvents=" + candidateBindings.Count +
                " AttackerParties=" + attackerParties.Count +
                " DefenderParties=" + defenderParties.Count;
            return true;
        }

        public static bool TryValidateActiveMission(
            MapEvent battle,
            Settlement settlement,
            Mission mission,
            out string expectedScene,
            out string diagnostics)
        {
            expectedScene = string.Empty;
            diagnostics = "not-relief-campaign-battle";
            if (!IsCampaignBattle(battle))
                return false;

            if (!TryResolveSettlement(
                    battle,
                    out Settlement resolvedSettlement,
                    out BattleSideEnum besiegerBattleSide,
                    out string settlementDiagnostics))
            {
                diagnostics =
                    "campaign-stage-invalid {" +
                    settlementDiagnostics + "}";
                return false;
            }

            if (!IsSameSettlement(settlement, resolvedSettlement))
            {
                diagnostics =
                    "campaign-stage-settlement-mismatch Supplied=" +
                    (settlement?.StringId ?? "null") +
                    " Resolved=" + resolvedSettlement.StringId;
                return false;
            }

            settlement = resolvedSettlement;

            if (mission == null)
            {
                diagnostics = "mission-null";
                return false;
            }

            if (!TryGetMissionInitializerRecord(
                    mission,
                    out MissionInitializerRecord initializerRecord))
            {
                diagnostics = "mission-initializer-missing";
                return false;
            }

            expectedScene = initializerRecord.SceneName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expectedScene))
                expectedScene = mission.SceneName ?? string.Empty;

            if (string.IsNullOrWhiteSpace(expectedScene) ||
                !string.Equals(
                    mission.SceneName,
                    expectedScene,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "scene-mismatch Runtime=" +
                    (mission.SceneName ?? "null") +
                    " Expected=" +
                    (expectedScene ?? "null");
                return false;
            }

            if (!initializerRecord.SceneHasMapPatch)
            {
                diagnostics = "mission-initializer-map-patch-disabled";
                return false;
            }

            if (!SceneRuntimeClassifier.IsCampaignBattleScene(expectedScene))
            {
                diagnostics =
                    "relief-field-scene-invalid Scene=" +
                    expectedScene;
                return false;
            }

            if (mission.GetMissionBehavior<BattleSpawnLogic>() == null)
            {
                diagnostics = "native-battle-spawn-logic-missing";
                return false;
            }

            diagnostics =
                "validated Settlement=" + settlement.StringId +
                " Scene=" + expectedScene +
                " PlayerSide=" + battle.PlayerSide +
                " BesiegerBattleSide=" + besiegerBattleSide +
                " MissionMode=Battle" +
                " SceneHasMapPatch=True";
            return true;
        }

        public static bool TryValidateFinalEncounterResult(
            MapEvent battle,
            BattleSnapshotMessage snapshot,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            out string diagnostics)
        {
            diagnostics = "not-exact-relief";
            if (!TryResolveSettlement(
                    battle,
                    out Settlement settlement,
                    out _,
                    out string settlementDiagnostics))
            {
                diagnostics =
                    "relief-settlement-resolution-invalid {" +
                    settlementDiagnostics + "}";
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                snapshot?.ScenarioContext;
            if (!string.Equals(
                    scenarioContext?.SiegeContext?.SettlementId,
                    settlement.StringId,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "relief-settlement-mismatch Snapshot=" +
                    (scenarioContext?.SiegeContext?.SettlementId ??
                     string.Empty) +
                    " Live=" + settlement.StringId;
                return false;
            }

            string runtimeScene =
                result?.MapScene ??
                snapshot?.MultiplayerScene ??
                snapshot?.MapScene;
            if (!ExactReliefScenarioContract.IsReliefScenario(
                    scenarioContext))
            {
                diagnostics = "relief-scenario-identity-invalid";
                return false;
            }

            if (!ExactLandBattleScenarioContract.IsValidatedScenario(
                    snapshot,
                    runtimeScene,
                    out string scenarioDiagnostics))
            {
                diagnostics =
                    "relief-scenario-invalid {" +
                    scenarioDiagnostics + "}";
                return false;
            }

            if (!ExactReliefScenarioContract.IsReliefResult(result))
            {
                diagnostics =
                    "relief-result-stage-mismatch Stage=" +
                    (result?.BattleStage ?? string.Empty);
                return false;
            }

            diagnostics =
                "validated-final-relief" +
                " Settlement=" + settlement.StringId +
                " Scenario={" + scenarioDiagnostics + "}";
            return true;
        }

        private static List<SettlementCandidateBinding>
            BuildSettlementCandidateBindings()
        {
            var bindings = new List<SettlementCandidateBinding>();
            var siegeEvents = TaleWorlds.CampaignSystem.Campaign.Current?
                .SiegeEventManager?
                .SiegeEvents;
            if (siegeEvents == null)
                return bindings;

            for (int i = 0; i < siegeEvents.Count; i++)
            {
                SiegeEvent siegeEvent = siegeEvents[i];
                Settlement besiegedSettlement = siegeEvent?.BesiegedSettlement;
                bool isActive =
                    siegeEvent != null &&
                    besiegedSettlement?.IsFortification == true &&
                    siegeEvent.BesiegerCamp != null &&
                    !siegeEvent.ReadyToBeRemoved &&
                    ReferenceEquals(besiegedSettlement.SiegeEvent, siegeEvent);
                IReadOnlyList<ExactReliefPartyDescriptor> besiegerParties =
                    BuildPartyDescriptors(siegeEvent?.BesiegerCamp);
                var descriptor = new ExactReliefSiegeCandidateDescriptor(
                    besiegedSettlement?.StringId,
                    isActive,
                    besiegerParties);
                bindings.Add(new SettlementCandidateBinding
                {
                    SiegeEvent = siegeEvent,
                    Descriptor = descriptor
                });
            }

            return bindings;
        }

        private static IReadOnlyList<ExactReliefPartyDescriptor>
            BuildPartyDescriptors(MapEventSide side)
        {
            var descriptors = new List<ExactReliefPartyDescriptor>();
            if (side?.Parties == null)
                return descriptors;

            foreach (MapEventParty mapEventParty in side.Parties)
            {
                ExactReliefPartyDescriptor descriptor =
                    BuildPartyDescriptor(mapEventParty?.Party);
                if (descriptor != null)
                    descriptors.Add(descriptor);
            }

            return descriptors;
        }

        private static IReadOnlyList<ExactReliefPartyDescriptor>
            BuildPartyDescriptors(BesiegerCamp besiegerCamp)
        {
            var descriptors = new List<ExactReliefPartyDescriptor>();
            if (besiegerCamp == null)
                return descriptors;

            foreach (PartyBase party in
                     besiegerCamp.GetInvolvedPartiesForEventType(
                         MapEvent.BattleTypes.SiegeOutside))
            {
                ExactReliefPartyDescriptor descriptor =
                    BuildPartyDescriptor(party);
                if (descriptor != null)
                    descriptors.Add(descriptor);
            }

            return descriptors;
        }

        private static ExactReliefPartyDescriptor BuildPartyDescriptor(
            PartyBase party)
        {
            MobileParty mobileParty = party?.MobileParty;
            if (mobileParty == null ||
                string.IsNullOrWhiteSpace(mobileParty.StringId))
            {
                return null;
            }

            MobileParty armyLeaderParty =
                mobileParty.Army?.LeaderParty ??
                mobileParty.AttachedTo ??
                mobileParty;
            return new ExactReliefPartyDescriptor(
                mobileParty.StringId,
                armyLeaderParty?.StringId);
        }

        private static bool IsSameSettlement(
            Settlement left,
            Settlement right)
        {
            return ReferenceEquals(left, right) ||
                   left != null &&
                   right != null &&
                   !string.IsNullOrWhiteSpace(left.StringId) &&
                   string.Equals(
                       left.StringId,
                       right.StringId,
                       StringComparison.Ordinal);
        }

        private static bool TryGetMissionInitializerRecord(
            Mission mission,
            out MissionInitializerRecord initializerRecord)
        {
            initializerRecord = default;
            if (mission == null)
                return false;

            try
            {
                object boxedRecord =
                    MissionInitializerRecordProperty?.GetValue(mission, null) ??
                    MissionInitializerRecordBackingField?.GetValue(mission);
                if (boxedRecord is MissionInitializerRecord record)
                {
                    initializerRecord = record;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
