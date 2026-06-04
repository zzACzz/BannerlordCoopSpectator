using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Keeps stable mission-safe fallback characters, but overrides their displayed names
    /// from the exact campaign snapshot/runtime resolver for both hero and non-hero entries.
    /// </summary>
    public static class AgentDisplayNamePatch
    {
        private static readonly HashSet<string> LoggedOverrideKeys =
            new HashSet<string>(StringComparer.Ordinal);

        public static void Apply(Harmony harmony)
        {
            TryPatchGetter(
                harmony,
                typeof(Agent).GetProperty(nameof(Agent.Name), BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod(),
                nameof(Agent_Name_Postfix),
                "Agent.Name");

            TryPatchGetter(
                harmony,
                typeof(Agent).GetProperty(nameof(Agent.NameTextObject), BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod(),
                nameof(Agent_NameTextObject_Postfix),
                "Agent.NameTextObject");
        }

        private static void TryPatchGetter(Harmony harmony, MethodInfo target, string postfixMethodName, string targetLabel)
        {
            try
            {
                MethodInfo postfix = typeof(AgentDisplayNamePatch).GetMethod(
                    postfixMethodName,
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (target == null || postfix == null)
                {
                    ModLogger.Info("AgentDisplayNamePatch: skip patch, target not found. Target=" + targetLabel);
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                ModLogger.Info("AgentDisplayNamePatch: postfix applied to " + targetLabel + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("AgentDisplayNamePatch: failed to patch " + targetLabel + ": " + ex.Message);
            }
        }

        private static void Agent_Name_Postfix(Agent __instance, ref string __result)
        {
            try
            {
                if (!TryResolveBattleOnlyExactDisplayNameForAgent(__instance, out string entryId, out TextObject exactName))
                    return;

                __result = exactName.ToString();
                LogExactNameOverride(__instance, entryId, __result, "Name");
            }
            catch (Exception ex)
            {
                ModLogger.Info("AgentDisplayNamePatch: Agent.Name postfix failed: " + ex.Message);
            }
        }

        private static void Agent_NameTextObject_Postfix(Agent __instance, ref TextObject __result)
        {
            try
            {
                if (!TryResolveBattleOnlyExactDisplayNameForAgent(__instance, out string entryId, out TextObject exactName))
                    return;

                __result = exactName;
                LogExactNameOverride(__instance, entryId, exactName.ToString(), "NameTextObject");
            }
            catch (Exception ex)
            {
                ModLogger.Info("AgentDisplayNamePatch: Agent.NameTextObject postfix failed: " + ex.Message);
            }
        }

        private static bool TryResolveBattleOnlyExactDisplayNameForAgent(Agent agent, out string entryId, out TextObject exactName)
        {
            entryId = null;
            exactName = null;
            if (agent == null)
                return false;

            Mission mission = Mission.Current;
            if (!ShouldRunForCurrentMission(mission))
                return false;

            if (!TryResolveBattleOnlyEntryId(agent, out entryId) ||
                string.IsNullOrWhiteSpace(entryId))
            {
                return false;
            }

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (entryState == null)
                return false;

            string resolvedDisplayName = BattleSnapshotRuntimeState.ResolveEntryDisplayName(entryState, entryId);
            if (string.IsNullOrWhiteSpace(resolvedDisplayName) ||
                string.Equals(resolvedDisplayName, "Unknown Unit", StringComparison.Ordinal))
            {
                return false;
            }

            exactName = new TextObject(resolvedDisplayName);
            return true;
        }

        private static bool ShouldRunForCurrentMission(Mission mission)
        {
            if (mission == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
            {
                return false;
            }

            return mission.GetMissionBehavior<MissionMultiplayerCoopBattle>() != null ||
                   mission.GetMissionBehavior<MissionMultiplayerCoopBattleClient>() != null;
        }

        private static bool TryResolveBattleOnlyEntryId(Agent agent, out string entryId)
        {
            if (CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(agent, out entryId) &&
                !string.IsNullOrWhiteSpace(entryId))
            {
                return true;
            }

            return ExactCampaignArmyBootstrap.TryGetEntryId(agent, out entryId) &&
                   !string.IsNullOrWhiteSpace(entryId);
        }

        private static void LogExactNameOverride(Agent agent, string entryId, string exactName, string source)
        {
            if (agent == null || string.IsNullOrWhiteSpace(entryId) || string.IsNullOrWhiteSpace(exactName))
                return;

            string logKey = agent.Index + "|" + entryId + "|" + source + "|" + exactName;
            if (!LoggedOverrideKeys.Add(logKey))
                return;

            ModLogger.Info(
                "AgentDisplayNamePatch: applied exact display name override. " +
                "AgentIndex=" + agent.Index +
                " EntryId=" + entryId +
                " Source=" + source +
                " ExactName=" + exactName.Replace('\r', ' ').Replace('\n', ' '));
        }
    }
}
