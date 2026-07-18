using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.SiegeAmbush;
using CoopSpectator.Network.Messages;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Source.Missions;

namespace CoopSpectator.Campaign.SiegeAmbush
{
    internal static class SiegeAmbushCampaignBattleAdapter
    {
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
            return battle?.IsSiegeAmbush == true &&
                   battle.IsBlockade != true &&
                   battle.IsBlockadeSallyOut != true;
        }

        public static bool IsCampaignStage(MapEvent battle, Settlement settlement)
        {
            return IsCampaignBattle(battle) &&
                   battle.PlayerSide == BattleSideEnum.Defender &&
                   settlement?.IsFortification == true &&
                   settlement.SiegeEvent != null;
        }

        public static bool TryValidateActiveMission(
            MapEvent battle,
            Settlement settlement,
            Mission mission,
            out string expectedScene,
            out string diagnostics)
        {
            expectedScene = string.Empty;
            diagnostics = "not-siege-ambush-campaign-battle";
            if (!IsCampaignBattle(battle))
                return false;

            if (!IsCampaignStage(battle, settlement))
            {
                diagnostics =
                    "campaign-stage-invalid Settlement=" + (settlement?.StringId ?? "null") +
                    " IsFortification=" + (settlement?.IsFortification ?? false) +
                    " HasSiegeEvent=" + (settlement?.SiegeEvent != null) +
                    " PlayerSide=" + (battle?.PlayerSide.ToString() ?? "None");
                return false;
            }

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
                    "scene-mismatch Runtime=" + (mission.SceneName ?? "null") +
                    " Expected=" + (expectedScene ?? "null");
                return false;
            }

            if (initializerRecord.SceneHasMapPatch)
            {
                diagnostics = "mission-initializer-map-patch-enabled";
                return false;
            }

            if (mission.GetMissionBehavior<BattleSpawnLogic>() == null)
            {
                diagnostics = "native-battle-spawn-logic-missing";
                return false;
            }

            if (mission.GetMissionBehavior<SallyOutMissionController>() == null)
            {
                diagnostics = "native-siege-ambush-controller-missing";
                return false;
            }

            diagnostics =
                "validated Settlement=" + settlement.StringId +
                " Scene=" + expectedScene +
                " PlayerSide=" + battle.PlayerSide +
                " SceneHasMapPatch=False";
            return true;
        }

        public static bool TryApplyMissionSiegeEngineResult(
            Mission mission,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (!SiegeAmbushScenarioContract.IsSiegeAmbushResult(result))
            {
                diagnostics =
                    "result-stage-invalid Value=" +
                    (result?.BattleStage ?? string.Empty);
                return false;
            }

            MissionSiegeEnginesLogic siegeEnginesLogic =
                mission.GetMissionBehavior<MissionSiegeEnginesLogic>();
            if (siegeEnginesLogic == null)
            {
                diagnostics = "mission-siege-engines-logic-missing";
                return false;
            }

            try
            {
                siegeEnginesLogic.GetMissionSiegeWeapons(
                    out var defenderWeapons,
                    out var attackerWeapons);

                List<MissionSiegeWeapon> liveAttackers =
                    attackerWeapons?
                        .OfType<MissionSiegeWeapon>()
                        .OrderBy(weapon => weapon.Index)
                        .ToList() ??
                    new List<MissionSiegeWeapon>();
                List<BattleSiegeEngineSnapshotMessage> resultAttackers =
                    result.AttackerSiegeEngines?
                        .Where(engine => engine != null)
                        .OrderBy(engine => engine.Index)
                        .ToList() ??
                    new List<BattleSiegeEngineSnapshotMessage>();

                if (liveAttackers.Count != resultAttackers.Count)
                {
                    diagnostics =
                        "attacker-engine-count-mismatch Live=" + liveAttackers.Count +
                        " Result=" + resultAttackers.Count;
                    return false;
                }

                for (int i = 0; i < liveAttackers.Count; i++)
                {
                    MissionSiegeWeapon liveWeapon = liveAttackers[i];
                    BattleSiegeEngineSnapshotMessage resultWeapon = resultAttackers[i];
                    string liveTypeId =
                        !string.IsNullOrWhiteSpace(liveWeapon.Type?.StringId)
                            ? liveWeapon.Type.StringId
                            : liveWeapon.Type?.ToString() ?? string.Empty;
                    if (liveWeapon.Index != resultWeapon.Index ||
                        !string.Equals(
                            liveTypeId,
                            resultWeapon.EngineTypeId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        diagnostics =
                            "attacker-engine-order-mismatch Position=" + i +
                            " Live=" + liveTypeId + "#" + liveWeapon.Index +
                            " Result=" + (resultWeapon.EngineTypeId ?? string.Empty) +
                            "#" + resultWeapon.Index;
                        return false;
                    }
                }

                for (int i = 0; i < liveAttackers.Count; i++)
                {
                    MissionSiegeWeapon liveWeapon = liveAttackers[i];
                    BattleSiegeEngineSnapshotMessage resultWeapon = resultAttackers[i];
                    float maxHealth = liveWeapon.MaxHealth > 0f
                        ? liveWeapon.MaxHealth
                        : Math.Max(1f, resultWeapon.MaxHealth);
                    float health = resultWeapon.Health;
                    if (float.IsNaN(health) || float.IsInfinity(health))
                        health = liveWeapon.Health;
                    health = Math.Max(0f, Math.Min(maxHealth, health));
                    liveWeapon.SetHealth(health);
                }

                int defenderCount =
                    defenderWeapons?.OfType<MissionSiegeWeapon>().Count() ?? 0;
                diagnostics =
                    "applied AttackerWeapons=" + liveAttackers.Count +
                    " DefenderWeapons=" + defenderCount;
                return true;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "apply-faulted " +
                    ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        public static bool TryBuildPriorityDefenderEntryOrder(
            MapEvent battle,
            Settlement settlement,
            BattleSideSnapshotMessage defenderSnapshot,
            out List<string> priorityEntryOrder,
            out string diagnostics)
        {
            priorityEntryOrder = new List<string>();
            diagnostics = "campaign-stage-invalid";
            if (!IsCampaignStage(battle, settlement))
                return false;

            if (defenderSnapshot?.Troops == null ||
                defenderSnapshot.Troops.Count <= 0)
            {
                diagnostics = "defender-snapshot-empty";
                return false;
            }

            FlattenedTroopRoster priorityRoster;
            try
            {
                priorityRoster =
                    TaleWorlds.CampaignSystem.Campaign.Current?.Models?.SiegeEventModel?
                        .GetPriorityTroopsForSallyOutAmbush();
            }
            catch (Exception ex)
            {
                diagnostics =
                    "priority-roster-read-failed " +
                    ex.GetType().Name + ":" + ex.Message;
                return false;
            }

            if (priorityRoster == null)
            {
                diagnostics = "priority-roster-null";
                return false;
            }

            var buckets = defenderSnapshot.Troops
                .Where(troop =>
                    troop != null &&
                    !string.IsNullOrWhiteSpace(troop.EntryId) &&
                    Math.Max(0, troop.Count - troop.WoundedCount) > 0)
                .Select(troop => new PriorityEntryBucket
                {
                    Troop = troop,
                    RemainingHealthyCount =
                        Math.Max(0, troop.Count - troop.WoundedCount)
                })
                .ToList();

            int activePriorityElements = 0;
            int unmatchedElements = 0;
            var unmatchedSamples = new List<string>();
            foreach (FlattenedTroopRosterElement priorityElement in priorityRoster)
            {
                if (priorityElement.Troop == null ||
                    priorityElement.IsWounded ||
                    priorityElement.IsKilled ||
                    priorityElement.IsRouted)
                {
                    continue;
                }

                activePriorityElements++;
                string priorityTroopId = priorityElement.Troop.StringId;
                PriorityEntryBucket bucket = buckets.FirstOrDefault(candidate =>
                    candidate.RemainingHealthyCount > 0 &&
                    DoesPriorityTroopMatchSnapshotEntry(
                        priorityTroopId,
                        candidate.Troop));
                if (bucket == null)
                {
                    unmatchedElements++;
                    if (unmatchedSamples.Count < 8)
                        unmatchedSamples.Add(priorityTroopId ?? "null");
                    continue;
                }

                priorityEntryOrder.Add(bucket.Troop.EntryId);
                bucket.RemainingHealthyCount--;
            }

            diagnostics =
                "PriorityRoster=" + priorityRoster.Count() +
                " ActivePriority=" + activePriorityElements +
                " Matched=" + priorityEntryOrder.Count +
                " Unmatched=" + unmatchedElements +
                " DefenderBuckets=" + buckets.Count +
                (unmatchedSamples.Count > 0
                    ? " UnmatchedSamples=[" + string.Join("; ", unmatchedSamples) + "]"
                    : string.Empty);
            return priorityEntryOrder.Count > 0;
        }

        private static bool DoesPriorityTroopMatchSnapshotEntry(
            string priorityTroopId,
            TroopStackInfo troop)
        {
            if (string.IsNullOrWhiteSpace(priorityTroopId) || troop == null)
                return false;

            return string.Equals(
                       priorityTroopId,
                       troop.OriginalCharacterId,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       priorityTroopId,
                       troop.CharacterId,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       priorityTroopId,
                       troop.SpawnTemplateId,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       priorityTroopId,
                       troop.HeroId,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetMissionInitializerRecord(
            Mission mission,
            out MissionInitializerRecord record)
        {
            record = default;
            if (mission == null)
                return false;

            try
            {
                object boxedRecord =
                    MissionInitializerRecordProperty?.GetValue(mission) ??
                    MissionInitializerRecordBackingField?.GetValue(mission);
                if (boxedRecord is MissionInitializerRecord initializerRecord)
                {
                    record = initializerRecord;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private sealed class PriorityEntryBucket
        {
            public TroopStackInfo Troop { get; set; }

            public int RemainingHealthyCount { get; set; }
        }
    }
}
