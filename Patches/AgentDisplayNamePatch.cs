using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Keeps stable mission-safe fallback characters, but overrides their displayed names
    /// from the exact campaign snapshot for hero entries (player, companions, lords).
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

            TryPatchGetter(
                harmony,
                ResolveTrackableGetNameMethod(),
                nameof(Agent_ITrackableBase_GetName_Postfix),
                "Agent.ITrackableBase.GetName");
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

        private static MethodInfo ResolveTrackableGetNameMethod()
        {
            try
            {
                MethodInfo[] methods = typeof(Agent).GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method == null ||
                        method.ReturnType != typeof(TextObject) ||
                        method.GetParameters().Length != 0)
                    {
                        continue;
                    }

                    if (string.Equals(method.Name, "GetName", StringComparison.Ordinal) ||
                        method.Name.EndsWith(".GetName", StringComparison.Ordinal))
                    {
                        ModLogger.Info("AgentDisplayNamePatch: resolved trackable/display name target. Method=" + method.Name + ".");
                        return method;
                    }
                }

                ModLogger.Info("AgentDisplayNamePatch: no trackable/display name method matched on Agent.");
            }
            catch (Exception ex)
            {
                ModLogger.Info("AgentDisplayNamePatch: failed to resolve trackable/display name target: " + ex.Message);
            }

            return null;
        }

        private static void Agent_Name_Postfix(Agent __instance, ref string __result)
        {
            try
            {
                if (!CoopMissionSpawnLogic.TryResolveExactDisplayNameForAgent(__instance, out string entryId, out TextObject exactName))
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
                if (!CoopMissionSpawnLogic.TryResolveExactDisplayNameForAgent(__instance, out string entryId, out TextObject exactName))
                    return;

                __result = exactName;
                LogExactNameOverride(__instance, entryId, exactName.ToString(), "NameTextObject");
            }
            catch (Exception ex)
            {
                ModLogger.Info("AgentDisplayNamePatch: Agent.NameTextObject postfix failed: " + ex.Message);
            }
        }

        private static void Agent_ITrackableBase_GetName_Postfix(Agent __instance, ref TextObject __result)
        {
            try
            {
                if (!CoopMissionSpawnLogic.TryResolveExactDisplayNameForAgent(__instance, out string entryId, out TextObject exactName))
                    return;

                __result = exactName;
                LogExactNameOverride(__instance, entryId, exactName.ToString(), "ITrackableBase.GetName");
            }
            catch (Exception ex)
            {
                ModLogger.Info("AgentDisplayNamePatch: Agent.ITrackableBase.GetName postfix failed: " + ex.Message);
            }
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
