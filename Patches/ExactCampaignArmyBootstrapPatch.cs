using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    public static class ExactCampaignArmyBootstrapPatch
    {
        private static readonly FieldInfo BattlePowerSidePowerDataField =
            AccessTools.Field(typeof(BattlePowerCalculationLogic), "_sidePowerData");
        private static readonly FieldInfo BattlePowerCalculatedField =
            AccessTools.Field(typeof(BattlePowerCalculationLogic), "<IsTeamPowersCalculated>k__BackingField");
        private static string _lastInitSideOverrideKey;
        private static string _lastBattlePowerSafeCalculationKey;
        private static string _lastBattlePowerNonBattleTeamKey;

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.PropertyGetter(typeof(Team), nameof(Team.Side));
                MethodInfo postfix = AccessTools.Method(
                    typeof(ExactCampaignArmyBootstrapPatch),
                    nameof(Team_Side_Postfix));
                if (target == null || postfix == null)
                {
                    ModLogger.Info("ExactCampaignArmyBootstrapPatch: Team.Side getter not found. Skip.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                ModLogger.Info("ExactCampaignArmyBootstrapPatch: postfix applied to Team.Side.");

                MethodInfo battlePowerTarget =
                    AccessTools.Method(typeof(BattlePowerCalculationLogic), "CalculateTeamPowers");
                MethodInfo battlePowerPrefix = AccessTools.Method(
                    typeof(ExactCampaignArmyBootstrapPatch),
                    nameof(BattlePowerCalculationLogic_CalculateTeamPowers_Prefix));
                if (battlePowerTarget == null || battlePowerPrefix == null)
                {
                    ModLogger.Info("ExactCampaignArmyBootstrapPatch: BattlePowerCalculationLogic.CalculateTeamPowers not found. Skip.");
                    return;
                }

                harmony.Patch(battlePowerTarget, prefix: new HarmonyMethod(battlePowerPrefix));
                ModLogger.Info("ExactCampaignArmyBootstrapPatch: prefix applied to BattlePowerCalculationLogic.CalculateTeamPowers.");

                MethodInfo getTotalTeamPowerTarget =
                    AccessTools.Method(typeof(BattlePowerCalculationLogic), nameof(BattlePowerCalculationLogic.GetTotalTeamPower));
                MethodInfo getTotalTeamPowerPrefix = AccessTools.Method(
                    typeof(ExactCampaignArmyBootstrapPatch),
                    nameof(BattlePowerCalculationLogic_GetTotalTeamPower_Prefix));
                if (getTotalTeamPowerTarget == null || getTotalTeamPowerPrefix == null)
                {
                    ModLogger.Info("ExactCampaignArmyBootstrapPatch: BattlePowerCalculationLogic.GetTotalTeamPower not found. Skip.");
                    return;
                }

                harmony.Patch(getTotalTeamPowerTarget, prefix: new HarmonyMethod(getTotalTeamPowerPrefix));
                ModLogger.Info("ExactCampaignArmyBootstrapPatch: prefix applied to BattlePowerCalculationLogic.GetTotalTeamPower.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ExactCampaignArmyBootstrapPatch.Apply failed.", ex);
            }
        }

        private static void Team_Side_Postfix(Team __instance, ref BattleSideEnum __result)
        {
            if (__instance?.Mission == null)
                return;

            if (!ExactCampaignArmyBootstrap.TryGetSpawnLogicInitTeamSideOverride(
                    __instance,
                    __result,
                    out BattleSideEnum overrideSide))
            {
                return;
            }

            __result = overrideSide;

            string logKey =
                (__instance.Mission.SceneName ?? "null") + "|" +
                __instance.TeamIndex + "|" +
                overrideSide;
            if (string.Equals(_lastInitSideOverrideKey, logKey, StringComparison.Ordinal))
                return;

            _lastInitSideOverrideKey = logKey;
            ModLogger.Info(
                "ExactCampaignArmyBootstrapPatch: remapped Team.Side=None during MissionAgentSpawnLogic init for exact campaign bootstrap. " +
                "Scene=" + (__instance.Mission.SceneName ?? "null") +
                " TeamIndex=" + __instance.TeamIndex +
                " OverrideSide=" + overrideSide);
        }

        private static bool BattlePowerCalculationLogic_CalculateTeamPowers_Prefix(
            BattlePowerCalculationLogic __instance)
        {
            Mission mission = __instance?.Mission;
            if (mission == null ||
                !ExactCampaignArmyBootstrap.IsActive(mission) ||
                !SceneRuntimeClassifier.IsExactCampaignArmyMaterializationScene(
                    mission.SceneName ?? string.Empty))
            {
                return true;
            }

            try
            {
                Dictionary<Team, float>[] sidePowerData =
                    BattlePowerSidePowerDataField?.GetValue(__instance) as Dictionary<Team, float>[];
                if (sidePowerData == null || sidePowerData.Length < 2)
                {
                    sidePowerData = new Dictionary<Team, float>[2];
                    BattlePowerSidePowerDataField?.SetValue(__instance, sidePowerData);
                }

                for (int i = 0; i < 2; i++)
                {
                    if (sidePowerData[i] == null)
                        sidePowerData[i] = new Dictionary<Team, float>();
                    else
                        sidePowerData[i].Clear();
                }

                int skippedTeamCount = 0;
                foreach (Team team in mission.Teams)
                {
                    if (team == null || !TryGetBattlePowerSideIndex(team.Side, out int sideIndex))
                    {
                        skippedTeamCount++;
                        continue;
                    }

                    if (!sidePowerData[sideIndex].ContainsKey(team))
                        sidePowerData[sideIndex].Add(team, 0f);
                }

                int troopOriginCount = 0;
                int skippedTroopOriginCount = 0;
                IMissionAgentSpawnLogic spawnLogic = mission.GetMissionBehavior<IMissionAgentSpawnLogic>();
                if (spawnLogic != null)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        BattleSideEnum side = (BattleSideEnum)i;
                        bool isPlayerSide = mission.PlayerTeam != null && mission.PlayerTeam.Side == side;
                        IEnumerable<IAgentOriginBase> troopOrigins = null;
                        try
                        {
                            troopOrigins = spawnLogic.GetAllTroopsForSide(side);
                        }
                        catch
                        {
                        }

                        if (troopOrigins == null)
                            continue;

                        foreach (IAgentOriginBase troopOrigin in troopOrigins)
                        {
                            BasicCharacterObject troop = troopOrigin?.Troop;
                            if (troop == null)
                            {
                                skippedTroopOriginCount++;
                                continue;
                            }

                            Team troopTeam = null;
                            try
                            {
                                troopTeam = Mission.GetAgentTeam(troopOrigin, isPlayerSide);
                            }
                            catch
                            {
                            }

                            if (troopTeam == null || !TryGetBattlePowerSideIndex(troopTeam.Side, out int troopTeamSideIndex))
                                troopTeam = side == BattleSideEnum.Attacker ? mission.AttackerTeam : mission.DefenderTeam;

                            if (troopTeam == null || !TryGetBattlePowerSideIndex(troopTeam.Side, out troopTeamSideIndex))
                            {
                                skippedTroopOriginCount++;
                                continue;
                            }

                            Dictionary<Team, float> troopTeamDictionary = sidePowerData[troopTeamSideIndex];
                            if (!troopTeamDictionary.ContainsKey(troopTeam))
                                troopTeamDictionary.Add(troopTeam, 0f);

                            troopTeamDictionary[troopTeam] += troop.GetPower();
                            troopOriginCount++;
                        }
                    }
                }

                foreach (Team team in mission.Teams)
                {
                    try
                    {
                        team?.QuerySystem?.Expire();
                    }
                    catch
                    {
                    }
                }

                BattlePowerCalculatedField?.SetValue(__instance, true);
                LogBattlePowerSafeCalculation(
                    mission,
                    sidePowerData,
                    skippedTeamCount,
                    troopOriginCount,
                    skippedTroopOriginCount,
                    "calculated");
                return false;
            }
            catch (Exception ex)
            {
                try
                {
                    BattlePowerCalculatedField?.SetValue(__instance, true);
                }
                catch
                {
                }

                ModLogger.Info(
                    "ExactCampaignArmyBootstrapPatch: suppressed exact campaign army BattlePowerCalculationLogic.CalculateTeamPowers failure. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " Error=" + ex.GetType().Name + ":" + ex.Message);
                return false;
            }
        }

        private static bool BattlePowerCalculationLogic_GetTotalTeamPower_Prefix(
            BattlePowerCalculationLogic __instance,
            Team team,
            ref float __result)
        {
            Mission mission = __instance?.Mission;
            if (mission == null ||
                team == null ||
                !ExactCampaignArmyBootstrap.IsActive(mission) ||
                !SceneRuntimeClassifier.IsExactCampaignArmyMaterializationScene(
                    mission.SceneName ?? string.Empty) ||
                TryGetBattlePowerSideIndex(team.Side, out _))
            {
                return true;
            }

            __result = 0f;
            LogBattlePowerNonBattleTeamFallback(mission, team);
            return false;
        }

        private static bool TryGetBattlePowerSideIndex(BattleSideEnum side, out int sideIndex)
        {
            sideIndex = (int)side;
            return side == BattleSideEnum.Attacker || side == BattleSideEnum.Defender;
        }

        private static void LogBattlePowerSafeCalculation(
            Mission mission,
            Dictionary<Team, float>[] sidePowerData,
            int skippedTeamCount,
            int troopOriginCount,
            int skippedTroopOriginCount,
            string source)
        {
            string key =
                (mission?.SceneName ?? "null") + "|" +
                skippedTeamCount + "|" +
                troopOriginCount + "|" +
                skippedTroopOriginCount + "|" +
                (sidePowerData != null && sidePowerData.Length > 0 ? sidePowerData[0]?.Count ?? 0 : 0) + "|" +
                (sidePowerData != null && sidePowerData.Length > 1 ? sidePowerData[1]?.Count ?? 0 : 0);
            if (string.Equals(_lastBattlePowerSafeCalculationKey, key, StringComparison.Ordinal))
                return;

            _lastBattlePowerSafeCalculationKey = key;
            ModLogger.Info(
                "ExactCampaignArmyBootstrapPatch: calculated exact campaign army team powers with side filter. " +
                "Scene=" + (mission?.SceneName ?? "null") +
                " AttackerTeams=" + (sidePowerData != null && sidePowerData.Length > 0 ? sidePowerData[0]?.Count ?? 0 : 0) +
                " DefenderTeams=" + (sidePowerData != null && sidePowerData.Length > 1 ? sidePowerData[1]?.Count ?? 0 : 0) +
                " SkippedTeamCount=" + skippedTeamCount +
                " TroopOriginCount=" + troopOriginCount +
                " SkippedTroopOriginCount=" + skippedTroopOriginCount +
                " Source=" + (source ?? "unknown"));
        }

        private static void LogBattlePowerNonBattleTeamFallback(Mission mission, Team team)
        {
            string key =
                (mission?.SceneName ?? "null") + "|" +
                (team?.TeamIndex.ToString() ?? "null") + "|" +
                (team?.Side.ToString() ?? "null");
            if (string.Equals(_lastBattlePowerNonBattleTeamKey, key, StringComparison.Ordinal))
                return;

            _lastBattlePowerNonBattleTeamKey = key;
            ModLogger.Info(
                "ExactCampaignArmyBootstrapPatch: returned zero battle power for non-battle team during exact siege deployment. " +
                "Scene=" + (mission?.SceneName ?? "null") +
                " TeamIndex=" + (team?.TeamIndex.ToString() ?? "null") +
                " TeamSide=" + (team?.Side.ToString() ?? "null"));
        }
    }
}
