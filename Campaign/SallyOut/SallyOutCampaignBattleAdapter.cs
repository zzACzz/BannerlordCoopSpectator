using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.SallyOut;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Source.Missions;

namespace CoopSpectator.Campaign.SallyOut
{
    internal static class SallyOutCampaignBattleAdapter
    {
        private static readonly object FinalEncounterCompletionSync = new object();
        private static MapEvent _pendingFinalEncounterBattle;
        private static string _pendingFinalEncounterResultId;
        private static string _pendingFinalEncounterWinnerSide;
        private static readonly Dictionary<Hero, int> PendingFinalEncounterHeroHitPoints =
            new Dictionary<Hero, int>();

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
            return battle?.IsSallyOut == true &&
                   battle.IsBlockadeSallyOut != true &&
                   battle.IsSiegeAmbush != true;
        }

        public static bool IsCampaignStage(MapEvent battle, Settlement settlement)
        {
            return IsCampaignBattle(battle) &&
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
            diagnostics = "not-sally-out-campaign-battle";
            if (!IsCampaignBattle(battle))
                return false;

            if (!IsCampaignStage(battle, settlement))
            {
                diagnostics =
                    "campaign-stage-invalid Settlement=" + (settlement?.StringId ?? "null") +
                    " IsFortification=" + (settlement?.IsFortification ?? false) +
                    " HasSiegeEvent=" + (settlement?.SiegeEvent != null);
                return false;
            }

            if (battle.PlayerSide == BattleSideEnum.None)
            {
                diagnostics = "player-side-none";
                return false;
            }

            if (mission == null)
            {
                diagnostics = "mission-null";
                return false;
            }

            if (!TryGetMissionInitializerRecord(mission, out MissionInitializerRecord initializerRecord))
            {
                diagnostics = "mission-initializer-missing";
                return false;
            }

            expectedScene = initializerRecord.SceneName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expectedScene))
                expectedScene = mission.SceneName ?? string.Empty;

            if (string.IsNullOrWhiteSpace(expectedScene) ||
                !string.Equals(mission.SceneName, expectedScene, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "scene-mismatch Runtime=" + (mission.SceneName ?? "null") +
                    " Expected=" + (expectedScene ?? "null");
                return false;
            }

            if (!initializerRecord.SceneHasMapPatch)
            {
                diagnostics = "mission-initializer-map-patch-disabled";
                return false;
            }

            if (mission.GetMissionBehavior<BattleSpawnLogic>() == null)
            {
                diagnostics = "native-battle-spawn-logic-missing";
                return false;
            }

            if (mission.GetMissionBehavior<SallyOutMissionController>() != null)
            {
                diagnostics = "unexpected-siege-ambush-controller";
                return false;
            }

            diagnostics =
                "validated Settlement=" + settlement.StringId +
                " Scene=" + expectedScene +
                " PlayerSide=" + battle.PlayerSide +
                " SceneHasMapPatch=True";
            return true;
        }

        public static bool RequiresNativeEncounterDirectionReversal(MapEvent battle)
        {
            return IsCampaignBattle(battle);
        }

        public static bool TryArmFinalEncounterCompletion(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            IEnumerable<KeyValuePair<Hero, int>> heroHitPoints,
            out string diagnostics)
        {
            diagnostics = "contract-not-armed";
            if (!IsCampaignBattle(battle))
            {
                diagnostics = "not-sally-out-campaign-battle";
                return false;
            }

            if (result?.IsFinalStage != true ||
                result.DefenderPushedBack ||
                !string.Equals(
                    result.BattleStage,
                    SallyOutScenarioContract.ResultStage,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics = "not-final-sally-out-result";
                return false;
            }

            if (!IsResolvedWinner(result.WinnerSide))
            {
                diagnostics = "winner-unresolved";
                return false;
            }

            string resultId = ResolveResultId(result);
            lock (FinalEncounterCompletionSync)
            {
                ClearFinalEncounterCompletionNoLock();
                _pendingFinalEncounterBattle = battle;
                _pendingFinalEncounterResultId = resultId;
                _pendingFinalEncounterWinnerSide = result.WinnerSide;

                if (heroHitPoints != null)
                {
                    foreach (KeyValuePair<Hero, int> pair in heroHitPoints)
                    {
                        if (pair.Key == null || !pair.Key.IsAlive)
                            continue;

                        PendingFinalEncounterHeroHitPoints[pair.Key] = Math.Max(1, pair.Value);
                    }
                }

                diagnostics =
                    "armed ResultId=" + resultId +
                    " WinnerSide=" + result.WinnerSide +
                    " CachedHeroHp=" + PendingFinalEncounterHeroHitPoints.Count;
                return true;
            }
        }

        public static bool TryConsumeFinalEncounterCompletion(
            MapEvent battle,
            out string diagnostics)
        {
            diagnostics = "contract-not-pending";
            Dictionary<Hero, int> heroHitPoints;
            string resultId;
            string winnerSide;

            lock (FinalEncounterCompletionSync)
            {
                if (_pendingFinalEncounterBattle == null)
                    return false;

                if (!ReferenceEquals(_pendingFinalEncounterBattle, battle) ||
                    !IsCampaignBattle(battle))
                {
                    ClearFinalEncounterCompletionNoLock();
                    diagnostics = "contract-battle-mismatch";
                    return false;
                }

                resultId = _pendingFinalEncounterResultId;
                winnerSide = _pendingFinalEncounterWinnerSide;
                heroHitPoints = new Dictionary<Hero, int>(PendingFinalEncounterHeroHitPoints);
                ClearFinalEncounterCompletionNoLock();
            }

            int reappliedHeroHitPoints = 0;
            int skippedDeadHeroes = 0;
            var reappliedSamples = new List<string>();
            foreach (KeyValuePair<Hero, int> pair in heroHitPoints)
            {
                Hero hero = pair.Key;
                if (hero == null || !hero.IsAlive)
                {
                    skippedDeadHeroes++;
                    continue;
                }

                int desiredHitPoints = Math.Max(1, pair.Value);
                int currentHitPoints = hero.HitPoints;
                if (currentHitPoints == desiredHitPoints)
                    continue;

                hero.HitPoints = desiredHitPoints;
                reappliedHeroHitPoints++;
                if (reappliedSamples.Count < 8)
                {
                    reappliedSamples.Add(
                        (hero.StringId ?? "unknown-hero") +
                        ":" + currentHitPoints + "->" + desiredHitPoints);
                }
            }

            diagnostics =
                "ResultId=" + resultId +
                " WinnerSide=" + winnerSide +
                " CachedHeroHp=" + heroHitPoints.Count +
                " ReappliedHeroHp=" + reappliedHeroHitPoints +
                " SkippedDeadHeroes=" + skippedDeadHeroes +
                " ReappliedSamples=[" + string.Join("; ", reappliedSamples) + "]";
            return true;
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
                object boxedRecord = MissionInitializerRecordProperty?.GetValue(mission) ??
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

        private static bool IsResolvedWinner(string winnerSide)
        {
            return string.Equals(winnerSide, "Attacker", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(winnerSide, "Defender", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveResultId(CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            if (!string.IsNullOrWhiteSpace(result?.ResultId))
                return result.ResultId;
            if (!string.IsNullOrWhiteSpace(result?.BattleInstanceId))
                return result.BattleInstanceId;
            if (!string.IsNullOrWhiteSpace(result?.BattleId))
                return result.BattleId;
            return "unknown-result";
        }

        private static void ClearFinalEncounterCompletionNoLock()
        {
            _pendingFinalEncounterBattle = null;
            _pendingFinalEncounterResultId = null;
            _pendingFinalEncounterWinnerSide = null;
            PendingFinalEncounterHeroHitPoints.Clear();
        }
    }
}
