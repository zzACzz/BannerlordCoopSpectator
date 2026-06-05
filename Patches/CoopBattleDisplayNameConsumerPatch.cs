using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Applies exact campaign entry names only in concrete coop-battle UI consumers,
    /// without touching global Agent.Name* getters that break mannequin pipelines.
    /// </summary>
    public static class CoopBattleDisplayNameConsumerPatch
    {
        private static readonly Type SpectatorHudVmType =
            AccessTools.TypeByName("TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection.HUDExtensions.MissionMultiplayerSpectatorHUDVM");

        private static readonly Type KillNotificationUiHandlerType =
            AccessTools.TypeByName("TaleWorlds.MountAndBlade.Multiplayer.View.MissionViews.MissionMultiplayerKillNotificationUIHandler");

        private static readonly Type KillFeedVmType =
            AccessTools.TypeByName("TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection.KillFeed.MPKillFeedVM");

        private static readonly Type GeneralKillNotificationItemVmType =
            AccessTools.TypeByName("TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection.KillFeed.General.MPGeneralKillNotificationItemVM");

        private static readonly HashSet<string> LoggedOverrideKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static WeakReference<object> _lastSpectatorHudVm =
            new WeakReference<object>(null);

        public static void Apply(Harmony harmony)
        {
            PatchSpectatorHudFocusIn(harmony);
            PatchKillNotificationUiHandler(harmony);
            PatchKillFeedVm(harmony);
            PatchGeneralKillNotificationItemVm(harmony);
        }

        private static void PatchSpectatorHudFocusIn(Harmony harmony)
        {
            TryPatch(
                harmony,
                SpectatorHudVmType,
                "OnSpectatedAgentFocusIn",
                new[] { typeof(Agent) },
                nameof(MissionMultiplayerSpectatorHUDVM_OnSpectatedAgentFocusIn_Postfix),
                prefix: false,
                "MissionMultiplayerSpectatorHUDVM.OnSpectatedAgentFocusIn");
        }

        private static void PatchKillNotificationUiHandler(Harmony harmony)
        {
            TryPatch(
                harmony,
                KillNotificationUiHandlerType,
                "OnAgentRemoved",
                new[] { typeof(Agent), typeof(Agent), typeof(AgentState), typeof(KillingBlow) },
                nameof(MissionMultiplayerKillNotificationUIHandler_OnAgentRemoved_Prefix),
                prefix: true,
                "MissionMultiplayerKillNotificationUIHandler.OnAgentRemoved");
        }

        private static void PatchKillFeedVm(Harmony harmony)
        {
            TryPatch(
                harmony,
                KillFeedVmType,
                "OnAgentRemoved",
                new[] { typeof(Agent), typeof(Agent), typeof(bool) },
                nameof(MPKillFeedVM_OnAgentRemoved_Prefix),
                prefix: true,
                "MPKillFeedVM.OnAgentRemoved");
        }

        private static void PatchGeneralKillNotificationItemVm(Harmony harmony)
        {
            TryPatch(
                harmony,
                GeneralKillNotificationItemVmType,
                "InitProperties",
                new[] { typeof(Agent), typeof(Agent) },
                nameof(MPGeneralKillNotificationItemVM_InitProperties_Postfix),
                prefix: false,
                "MPGeneralKillNotificationItemVM.InitProperties");

            TryPatch(
                harmony,
                GeneralKillNotificationItemVmType,
                "InitDeathProperties",
                new[] { typeof(Agent), typeof(Agent), typeof(Agent) },
                nameof(MPGeneralKillNotificationItemVM_InitDeathProperties_Postfix),
                prefix: false,
                "MPGeneralKillNotificationItemVM.InitDeathProperties");
        }

        private static void TryPatch(
            Harmony harmony,
            Type targetType,
            string methodName,
            Type[] parameterTypes,
            string patchMethodName,
            bool prefix,
            string targetLabel)
        {
            try
            {
                MethodInfo target = targetType == null
                    ? null
                    : AccessTools.Method(targetType, methodName, parameterTypes);
                MethodInfo patch = typeof(CoopBattleDisplayNameConsumerPatch).GetMethod(
                    patchMethodName,
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (target == null || patch == null)
                {
                    ModLogger.Info("CoopBattleDisplayNameConsumerPatch: skip patch, target not found. Target=" + targetLabel);
                    return;
                }

                if (prefix)
                    harmony.Patch(target, prefix: new HarmonyMethod(patch));
                else
                    harmony.Patch(target, postfix: new HarmonyMethod(patch));

                ModLogger.Info("CoopBattleDisplayNameConsumerPatch: patch applied to " + targetLabel + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleDisplayNameConsumerPatch: failed to patch " + targetLabel + ": " + ex.Message);
            }
        }

        private static void MissionMultiplayerSpectatorHUDVM_OnSpectatedAgentFocusIn_Postfix(object __instance, Agent followedAgent)
        {
            try
            {
                _lastSpectatorHudVm.SetTarget(__instance);

                if (!TryResolveBattleOnlyExactDisplayNameForAgent(followedAgent, out string entryId, out string exactName))
                    return;

                AccessTools.Property(__instance.GetType(), "SpectatedPlayerName")?.SetValue(__instance, exactName);
                LogOverride("spectator-hud", followedAgent, entryId, exactName);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleDisplayNameConsumerPatch: spectator HUD postfix failed: " + ex.Message);
            }
        }

        internal static void TryRefreshSpectatorHudExactDisplayNameForAgent(Agent agent, string source)
        {
            try
            {
                if (agent == null || !ShouldRunForCurrentMission(Mission.Current))
                    return;

                MissionPeer localMissionPeer = GameNetwork.MyPeer == null
                    ? null
                    : PeerExtensions.GetComponent<MissionPeer>(GameNetwork.MyPeer);
                Agent followedAgent = localMissionPeer?.FollowedAgent;
                if (followedAgent == null || followedAgent.Index != agent.Index)
                    return;

                if (!_lastSpectatorHudVm.TryGetTarget(out object spectatorHudVm) || spectatorHudVm == null)
                    return;

                if (!TryResolveBattleOnlyExactDisplayNameForAgent(agent, out string entryId, out string exactName))
                    return;

                AccessTools.Property(spectatorHudVm.GetType(), "SpectatedPlayerName")?.SetValue(spectatorHudVm, exactName);
                LogOverride("spectator-hud-refresh", agent, entryId, exactName);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopBattleDisplayNameConsumerPatch: spectator HUD refresh failed. " +
                    "Source=" + (source ?? "unknown") +
                    " Error=" + ex.Message);
            }
        }

        private static bool MissionMultiplayerKillNotificationUIHandler_OnAgentRemoved_Prefix(
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            KillingBlow killingBlow)
        {
            try
            {
                Mission mission = Mission.Current;
                if (!ShouldRunForCurrentMission(mission))
                    return true;

                if (GameNetwork.IsDedicatedServer || affectedAgent == null || !affectedAgent.IsHuman)
                    return false;

                string killerName = ResolvePreferredCoopBattleDisplayName(affectorAgent);
                string victimName = ResolvePreferredCoopBattleDisplayName(affectedAgent);

                uint color = 4291306250u;
                MissionPeer localMissionPeer = null;
                if (GameNetwork.MyPeer != null)
                    localMissionPeer = PeerExtensions.GetComponent<MissionPeer>(GameNetwork.MyPeer);

                if (localMissionPeer != null &&
                    ((localMissionPeer.Team != mission?.SpectatorTeam && localMissionPeer.Team != affectedAgent.Team) ||
                     (affectorAgent != null && affectorAgent.MissionPeer == localMissionPeer)))
                {
                    color = 4281589009u;
                }

                TextObject message;
                if (affectorAgent != null)
                {
                    message = new TextObject("{=2ZarUUbw}{KILLERPLAYERNAME} has killed {KILLEDPLAYERNAME}!", null);
                    message.SetTextVariable("KILLERPLAYERNAME", killerName);
                }
                else
                {
                    message = new TextObject("{=9CnRKZOb}{KILLEDPLAYERNAME} has died!", null);
                }

                message.SetTextVariable("KILLEDPLAYERNAME", victimName);
                MessageManager.DisplayMessage(message.ToString(), color);
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleDisplayNameConsumerPatch: kill-notification prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool MPKillFeedVM_OnAgentRemoved_Prefix(object __instance, Agent affectedAgent, Agent affectorAgent, bool isPersonalFeedEnabled)
        {
            try
            {
                if (!ShouldRunForCurrentMission(Mission.Current))
                    return true;

                Agent assistedAgent = GetAssistedAgent(affectedAgent, affectorAgent);
                if (assistedAgent != null && assistedAgent.IsMainAgent && isPersonalFeedEnabled)
                {
                    string victimName = ResolvePreferredCoopBattleDisplayName(affectedAgent);
                    object personalCasualty = AccessTools.Property(__instance.GetType(), "PersonalCasualty")?.GetValue(__instance);
                    AccessTools.Method(personalCasualty?.GetType(), "OnPersonalAssist", new[] { typeof(string) })
                        ?.Invoke(personalCasualty, new object[] { victimName });
                }

                object generalCasualty = AccessTools.Property(__instance.GetType(), "GeneralCasualty")?.GetValue(__instance);
                AccessTools.Method(generalCasualty?.GetType(), "OnAgentRemoved", new[] { typeof(Agent), typeof(Agent), typeof(Agent) })
                    ?.Invoke(generalCasualty, new object[] { affectedAgent, affectorAgent, assistedAgent });
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleDisplayNameConsumerPatch: kill-feed prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static void MPGeneralKillNotificationItemVM_InitProperties_Postfix(object __instance, Agent affectedAgent, Agent affectorAgent)
        {
            try
            {
                if (!ShouldRunForCurrentMission(Mission.Current))
                    return;

                string killerName = ResolvePreferredCoopBattleDisplayName(affectorAgent);
                string victimName = ResolvePreferredCoopBattleDisplayName(affectedAgent);

                AccessTools.Property(__instance.GetType(), "MurdererName")?.SetValue(__instance, killerName);
                AccessTools.Property(__instance.GetType(), "VictimName")?.SetValue(__instance, victimName);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleDisplayNameConsumerPatch: general kill-feed InitProperties postfix failed: " + ex.Message);
            }
        }

        private static void MPGeneralKillNotificationItemVM_InitDeathProperties_Postfix(object __instance, Agent affectedAgent, Agent affectorAgent, Agent assistedAgent)
        {
            try
            {
                if (!ShouldRunForCurrentMission(Mission.Current))
                    return;

                string message = null;
                if (affectorAgent != null && affectorAgent.IsMainAgent)
                {
                    MBTextManager.SetTextVariable("TROOP_NAME", ResolvePreferredCoopBattleDisplayName(affectedAgent), false);
                    message = GameTexts.FindText("str_kill_feed_message", null).ToString();
                }
                else if (affectedAgent != null && affectedAgent.IsMainAgent)
                {
                    MBTextManager.SetTextVariable("TROOP_NAME", ResolvePreferredCoopBattleDisplayName(affectorAgent), false);
                    message = GameTexts.FindText("str_death_feed_message", null).ToString();
                }
                else if (assistedAgent != null && assistedAgent.IsMainAgent)
                {
                    MBTextManager.SetTextVariable("TROOP_NAME", ResolvePreferredCoopBattleDisplayName(affectedAgent), false);
                    message = GameTexts.FindText("str_assist_feed_message", null).ToString();
                }

                if (!string.IsNullOrWhiteSpace(message))
                    AccessTools.Property(__instance.GetType(), "Message")?.SetValue(__instance, message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleDisplayNameConsumerPatch: general kill-feed InitDeathProperties postfix failed: " + ex.Message);
            }
        }

        private static bool ShouldRunForCurrentMission(Mission mission)
        {
            if (mission == null ||
                !GameNetwork.IsClient ||
                !GameNetwork.IsSessionActive ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
            {
                return false;
            }

            return mission.GetMissionBehavior<MissionMultiplayerCoopBattle>() != null ||
                   mission.GetMissionBehavior<MissionMultiplayerCoopBattleClient>() != null;
        }

        private static string ResolvePreferredCoopBattleDisplayName(Agent agent)
        {
            if (TryResolveBattleOnlyExactDisplayNameForAgent(agent, out _, out string exactName))
                return exactName;

            if (agent?.MissionPeer != null && !string.IsNullOrWhiteSpace(agent.MissionPeer.DisplayedName))
                return agent.MissionPeer.DisplayedName;

            return agent?.Name ?? string.Empty;
        }

        private static bool TryResolveBattleOnlyExactDisplayNameForAgent(Agent agent, out string entryId, out string exactName)
        {
            entryId = null;
            exactName = null;
            if (agent == null ||
                !ShouldRunForCurrentMission(Mission.Current) ||
                !TryResolveBattleOnlyEntryId(agent, out entryId) ||
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

            exactName = resolvedDisplayName;
            return true;
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

        private static Agent GetAssistedAgent(Agent affectedAgent, Agent affectorAgent)
        {
            if (affectedAgent == null)
                return null;

            var assistingHitter = affectedAgent.GetAssistingHitter(affectorAgent != null ? affectorAgent.MissionPeer : null);
            MissionPeer hitterPeer = assistingHitter?.HitterPeer;
            return hitterPeer?.ControlledAgent;
        }

        private static void LogOverride(string consumer, Agent agent, string entryId, string exactName)
        {
            if (agent == null || string.IsNullOrWhiteSpace(entryId) || string.IsNullOrWhiteSpace(exactName))
                return;

            string key = consumer + "|" + agent.Index + "|" + entryId + "|" + exactName;
            if (!LoggedOverrideKeys.Add(key))
                return;

            ModLogger.Info(
                "CoopBattleDisplayNameConsumerPatch: applied exact display name override. " +
                "Consumer=" + consumer +
                " AgentIndex=" + agent.Index +
                " EntryId=" + entryId +
                " ExactName=" + exactName.Replace('\r', ' ').Replace('\n', ' '));
        }
    }
}
