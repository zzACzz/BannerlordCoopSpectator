using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.UI;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Applies exact campaign names only in explicit coop-battle UI consumers,
    /// without touching global Agent.Name getters that destabilize mannequin previews.
    /// </summary>
    public static class CoopBattleDisplayNameConsumerPatch
    {
        private sealed class DisplayNameResolutionState
        {
            public Agent Agent { get; set; }
            public string EntryId { get; set; }
            public string PreferredName { get; set; }
            public string PreferredSource { get; set; }
            public string ExactResolutionReason { get; set; }
            public string EntryResolutionSource { get; set; }
            public bool ExactResolved { get; set; }
        }

        private const string SpectatorHudVmTypeName =
            "TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection.HUDExtensions.MissionMultiplayerSpectatorHUDVM";

        private const string KillNotificationUiHandlerTypeName =
            "TaleWorlds.MountAndBlade.Multiplayer.View.MissionViews.MissionMultiplayerKillNotificationUIHandler";

        private const string GauntletKillNotificationUiHandlerTypeName =
            "TaleWorlds.MountAndBlade.Multiplayer.GauntletUI.Mission.MissionGauntletKillNotificationUIHandler";

        private const string KillFeedVmTypeName =
            "TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection.KillFeed.MPKillFeedVM";

        private const string GeneralKillNotificationItemVmTypeName =
            "TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection.KillFeed.General.MPGeneralKillNotificationItemVM";

        private const string SingleplayerGauntletKillNotificationUiHandlerTypeName =
            "TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer.MissionGauntletKillNotificationSingleplayerUIHandler";

        private const string SingleplayerGeneralKillNotificationItemVmTypeName =
            "TaleWorlds.MountAndBlade.ViewModelCollection.HUD.KillFeed.General.SPGeneralKillNotificationItemVM";

        private static readonly HashSet<string> LoggedOverrideKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedDiagnosticKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> PatchedTargetLabels =
            new HashSet<string>(StringComparer.Ordinal);

        public static void Apply(Harmony harmony)
        {
            PatchMissionFocusableObjectInformationProvider(harmony);
            PatchSpectatorHudFocusIn(harmony);
            PatchKillNotificationUiHandler(harmony);
            PatchGauntletKillNotificationUiHandler(harmony);
            PatchKillFeedVm(harmony);
            PatchGeneralKillNotificationItemVm(harmony);
            PatchSingleplayerKillNotificationUiHandler(harmony);
            PatchSingleplayerGeneralKillNotificationItemVm(harmony);
            PatchCombatLogDataBuilder(harmony);
        }

        private static void PatchMissionFocusableObjectInformationProvider(Harmony harmony)
        {
            TryPatch(
                harmony,
                typeof(MissionFocusableObjectInformationProvider),
                "GetInteractionTexts",
                new[]
                {
                    typeof(Agent),
                    typeof(IFocusable),
                    typeof(bool),
                    typeof(FocusableObjectInformation).MakeByRefType()
                },
                nameof(MissionFocusableObjectInformationProvider_GetInteractionTexts_Postfix),
                prefix: false,
                "MissionFocusableObjectInformationProvider.GetInteractionTexts");
        }

        private static void PatchSpectatorHudFocusIn(Harmony harmony)
        {
            TryPatch(
                harmony,
                ResolveTypeByName(SpectatorHudVmTypeName),
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
                ResolveTypeByName(KillNotificationUiHandlerTypeName),
                "OnAgentRemoved",
                new[] { typeof(Agent), typeof(Agent), typeof(AgentState), typeof(KillingBlow) },
                nameof(MissionMultiplayerKillNotificationUIHandler_OnAgentRemoved_Prefix),
                prefix: true,
                "MissionMultiplayerKillNotificationUIHandler.OnAgentRemoved");
        }

        private static void PatchGauntletKillNotificationUiHandler(Harmony harmony)
        {
            TryPatch(
                harmony,
                ResolveTypeByName(GauntletKillNotificationUiHandlerTypeName),
                "OnAgentRemoved",
                new[] { typeof(Agent), typeof(Agent), typeof(AgentState), typeof(KillingBlow) },
                nameof(MissionGauntletKillNotificationUIHandler_OnAgentRemoved_Prefix),
                prefix: true,
                "MissionGauntletKillNotificationUIHandler.OnAgentRemoved");
        }

        private static void PatchKillFeedVm(Harmony harmony)
        {
            TryPatch(
                harmony,
                ResolveTypeByName(KillFeedVmTypeName),
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
                ResolveTypeByName(GeneralKillNotificationItemVmTypeName),
                "InitProperties",
                new[] { typeof(Agent), typeof(Agent) },
                nameof(MPGeneralKillNotificationItemVM_InitProperties_Postfix),
                prefix: false,
                "MPGeneralKillNotificationItemVM.InitProperties");

            TryPatch(
                harmony,
                ResolveTypeByName(GeneralKillNotificationItemVmTypeName),
                "InitDeathProperties",
                new[] { typeof(Agent), typeof(Agent), typeof(Agent) },
                nameof(MPGeneralKillNotificationItemVM_InitDeathProperties_Postfix),
                prefix: false,
                "MPGeneralKillNotificationItemVM.InitDeathProperties");
        }

        private static void PatchSingleplayerKillNotificationUiHandler(Harmony harmony)
        {
            TryPatch(
                harmony,
                ResolveTypeByName(SingleplayerGauntletKillNotificationUiHandlerTypeName),
                "OnAgentRemoved",
                new[] { typeof(Agent), typeof(Agent), typeof(AgentState), typeof(KillingBlow) },
                nameof(MissionGauntletKillNotificationSingleplayerUIHandler_OnAgentRemoved_Postfix),
                prefix: false,
                "MissionGauntletKillNotificationSingleplayerUIHandler.OnAgentRemoved");
        }

        private static void PatchSingleplayerGeneralKillNotificationItemVm(Harmony harmony)
        {
            TryPatch(
                harmony,
                ResolveTypeByName(SingleplayerGeneralKillNotificationItemVmTypeName),
                "InitProperties",
                new[]
                {
                    typeof(Agent),
                    typeof(Agent),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool)
                },
                nameof(SPGeneralKillNotificationItemVM_InitProperties_Postfix),
                prefix: false,
                "SPGeneralKillNotificationItemVM.InitProperties");
        }

        private static void PatchCombatLogDataBuilder(Harmony harmony)
        {
            TryPatch(
                harmony,
                typeof(Mission.MissionNetworkHelper),
                "GetCombatLogDataForCombatLogNetworkMessage",
                new[] { typeof(NetworkMessages.FromServer.CombatLogNetworkMessage) },
                nameof(MissionNetworkHelper_GetCombatLogDataForCombatLogNetworkMessage_Postfix),
                prefix: false,
                "MissionNetworkHelper.GetCombatLogDataForCombatLogNetworkMessage");
        }

        private static Type ResolveTypeByName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            try
            {
                Type resolved = AccessTools.TypeByName(typeName);
                if (resolved != null)
                    return resolved;
            }
            catch
            {
            }

            string simpleTypeName = GetSimpleTypeName(typeName);
            if (string.IsNullOrWhiteSpace(simpleTypeName))
                return null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type candidate in GetAssemblyTypesSafe(assembly))
                {
                    if (candidate == null)
                        continue;

                    string candidateFullName = candidate.FullName ?? string.Empty;
                    if (string.Equals(candidateFullName, typeName, StringComparison.Ordinal) ||
                        string.Equals(candidate.Name, simpleTypeName, StringComparison.Ordinal) ||
                        candidateFullName.EndsWith("." + simpleTypeName, StringComparison.Ordinal))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static string GetSimpleTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            int separatorIndex = typeName.LastIndexOf('.');
            return separatorIndex >= 0 && separatorIndex < typeName.Length - 1
                ? typeName.Substring(separatorIndex + 1)
                : typeName;
        }

        private static IEnumerable<Type> GetAssemblyTypesSafe(Assembly assembly)
        {
            if (assembly == null)
                return Array.Empty<Type>();

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types?.Where(type => type != null) ?? Array.Empty<Type>();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static MethodInfo ResolveMethod(Type targetType, string methodName, Type[] parameterTypes)
        {
            if (targetType == null || string.IsNullOrWhiteSpace(methodName))
                return null;

            MethodInfo resolved = AccessTools.Method(targetType, methodName, parameterTypes);
            if (resolved != null)
                return resolved;

            MethodInfo[] candidates = targetType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
                .ToArray();

            foreach (MethodInfo candidate in candidates)
            {
                if (AreParameterTypesCompatible(candidate.GetParameters(), parameterTypes, exactMatchOnly: true))
                    return candidate;
            }

            foreach (MethodInfo candidate in candidates)
            {
                if (AreParameterTypesCompatible(candidate.GetParameters(), parameterTypes, exactMatchOnly: false))
                    return candidate;
            }

            return null;
        }

        private static bool AreParameterTypesCompatible(
            ParameterInfo[] candidateParameters,
            Type[] expectedParameterTypes,
            bool exactMatchOnly)
        {
            int candidateLength = candidateParameters?.Length ?? 0;
            int expectedLength = expectedParameterTypes?.Length ?? 0;
            if (candidateLength != expectedLength)
                return false;

            for (int i = 0; i < expectedLength; i++)
            {
                Type candidateType = candidateParameters[i]?.ParameterType;
                Type expectedType = expectedParameterTypes[i];
                if (candidateType == null || expectedType == null)
                    return false;

                if (candidateType == expectedType)
                    continue;

                if (exactMatchOnly)
                    return false;

                if (!candidateType.IsAssignableFrom(expectedType))
                    return false;
            }

            return true;
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
                MethodInfo target = ResolveMethod(targetType, methodName, parameterTypes);
                MethodInfo patch = typeof(CoopBattleDisplayNameConsumerPatch).GetMethod(
                    patchMethodName,
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (target == null || patch == null)
                {
                    ModLogger.Info("CoopBattleDisplayNameConsumerPatch: skip patch, target not found. Target=" + targetLabel);
                    return;
                }

                lock (PatchedTargetLabels)
                {
                    if (PatchedTargetLabels.Contains(targetLabel))
                        return;
                }

                if (prefix)
                    harmony.Patch(target, prefix: new HarmonyMethod(patch));
                else
                    harmony.Patch(target, postfix: new HarmonyMethod(patch));

                lock (PatchedTargetLabels)
                    PatchedTargetLabels.Add(targetLabel);

                ModLogger.Info(
                    "CoopBattleDisplayNameConsumerPatch: patch applied to " + targetLabel + ". " +
                    "ResolvedType=" + (target.DeclaringType?.FullName ?? targetType?.FullName ?? "null") +
                    " ResolvedMethod=" + (target.Name ?? methodName) + ".");
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
                PropertyInfo spectatedPlayerNameProperty = AccessTools.Property(__instance.GetType(), "SpectatedPlayerName");
                string preValue = spectatedPlayerNameProperty?.GetValue(__instance)?.ToString();
                DisplayNameResolutionState followedState = ResolvePreferredCoopBattleDisplayNameState(followedAgent);
                if (followedState.ExactResolved)
                {
                    spectatedPlayerNameProperty?.SetValue(__instance, followedState.PreferredName);
                    LogOverride("spectator-hud", followedAgent, followedState.EntryId, followedState.PreferredName);
                }

                string postValue = spectatedPlayerNameProperty?.GetValue(__instance)?.ToString();
                LogSingleAgentConsumerDiagnostic(
                    "spectator-hud",
                    followedState,
                    preValue,
                    postValue,
                    "MissionMultiplayerSpectatorHUDVM.OnSpectatedAgentFocusIn");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleDisplayNameConsumerPatch: spectator HUD postfix failed: " + ex.Message);
            }
        }

        private static void MissionFocusableObjectInformationProvider_GetInteractionTexts_Postfix(
            Agent requesterAgent,
            IFocusable focusable,
            bool isInteractable,
            ref FocusableObjectInformation focusableObjectInformation)
        {
            try
            {
                Agent focusedAgent = focusable as Agent;
                Mission mission = focusedAgent?.Mission ?? requesterAgent?.Mission ?? Mission.Current;
                if (!ShouldRunForCurrentMission(mission))
                {
                    LogConsumerGateDiagnostic(
                        "interaction-hud",
                        mission,
                        focusedAgent,
                        requesterAgent,
                        "MissionFocusableObjectInformationProvider.GetInteractionTexts");
                    return;
                }

                if (focusedAgent == null || !focusableObjectInformation.IsActive)
                    return;

                string preValue = focusableObjectInformation.PrimaryInteractionText?.ToString();
                if (!CoopMissionSpawnLogic.TryResolveExactDisplayNameForAgent(
                        focusedAgent,
                        out string entryId,
                        out TextObject exactName))
                {
                    return;
                }

                focusableObjectInformation.PrimaryInteractionText = exactName;
                LogOverride("interaction-hud", focusedAgent, entryId, exactName?.ToString());

                if (ExperimentalFeatures.EnableBattleSelectionDisplayNameDiagnostics)
                {
                    DisplayNameResolutionState focusedState = ResolvePreferredCoopBattleDisplayNameState(focusedAgent);
                    string postValue = focusableObjectInformation.PrimaryInteractionText?.ToString();
                    LogSingleAgentConsumerDiagnostic(
                        "interaction-hud",
                        focusedState,
                        preValue,
                        postValue,
                        "MissionFocusableObjectInformationProvider.GetInteractionTexts");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleDisplayNameConsumerPatch: interaction HUD postfix failed: " + ex.Message);
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
                {
                    LogConsumerGateDiagnostic(
                        "kill-notification",
                        mission,
                        affectedAgent,
                        affectorAgent,
                        "MissionMultiplayerKillNotificationUIHandler.OnAgentRemoved");
                    return true;
                }

                if (GameNetwork.IsDedicatedServer || affectedAgent == null || !affectedAgent.IsHuman)
                    return false;

                DisplayNameResolutionState killerState = ResolvePreferredCoopBattleDisplayNameState(affectorAgent);
                DisplayNameResolutionState victimState = ResolvePreferredCoopBattleDisplayNameState(affectedAgent);
                string killerName = killerState.PreferredName;
                string victimName = victimState.PreferredName;

                uint color = 4291306250u;
                MissionPeer localMissionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
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
                string renderedMessage = message.ToString();
                MessageManager.DisplayMessage(renderedMessage, color);
                LogPairConsumerDiagnostic(
                    "kill-notification",
                    killerState,
                    victimState,
                    renderedMessage,
                    "MissionMultiplayerKillNotificationUIHandler.OnAgentRemoved");
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleDisplayNameConsumerPatch: kill-notification prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static void MissionGauntletKillNotificationUIHandler_OnAgentRemoved_Prefix(
            Agent affectedAgent,
            Agent affectorAgent)
        {
            try
            {
                Mission mission = Mission.Current;
                if (!ShouldRunForCurrentMission(mission) || GameNetwork.IsDedicatedServer)
                    return;

                PrimeExactDisplayNameCacheForAgent(affectedAgent);
                PrimeExactDisplayNameCacheForAgent(affectorAgent);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleDisplayNameConsumerPatch: Gauntlet kill-notification cache prefix failed open: " + ex.Message);
            }
        }

        private static bool MPKillFeedVM_OnAgentRemoved_Prefix(object __instance, Agent affectedAgent, Agent affectorAgent, bool isPersonalFeedEnabled)
        {
            try
            {
                Mission mission = Mission.Current;
                if (!ShouldRunForCurrentMission(mission))
                {
                    LogConsumerGateDiagnostic(
                        "kill-feed-vm",
                        mission,
                        affectedAgent,
                        affectorAgent,
                        "MPKillFeedVM.OnAgentRemoved");
                    return true;
                }

                DisplayNameResolutionState victimState = ResolvePreferredCoopBattleDisplayNameState(affectedAgent);
                DisplayNameResolutionState killerState = ResolvePreferredCoopBattleDisplayNameState(affectorAgent);
                Agent assistedAgent = GetAssistedAgent(affectedAgent, affectorAgent);
                if (assistedAgent != null && assistedAgent.IsMainAgent && isPersonalFeedEnabled)
                {
                    object personalCasualty = AccessTools.Property(__instance.GetType(), "PersonalCasualty")?.GetValue(__instance);
                    AccessTools.Method(personalCasualty?.GetType(), "OnPersonalAssist", new[] { typeof(string) })
                        ?.Invoke(personalCasualty, new object[] { victimState.PreferredName });
                }

                object generalCasualty = AccessTools.Property(__instance.GetType(), "GeneralCasualty")?.GetValue(__instance);
                AccessTools.Method(generalCasualty?.GetType(), "OnAgentRemoved", new[] { typeof(Agent), typeof(Agent), typeof(Agent) })
                    ?.Invoke(generalCasualty, new object[] { affectedAgent, affectorAgent, assistedAgent });
                LogPairConsumerDiagnostic(
                    "kill-feed-vm",
                    killerState,
                    victimState,
                    "PersonalFeed=" + isPersonalFeedEnabled +
                    " AssistedAgentIndex=" + (assistedAgent?.Index.ToString() ?? "null"),
                    "MPKillFeedVM.OnAgentRemoved");
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
                Mission mission = Mission.Current;
                if (!ShouldRunForCurrentMission(mission))
                {
                    LogConsumerGateDiagnostic(
                        "general-killfeed-init",
                        mission,
                        affectedAgent,
                        affectorAgent,
                        "MPGeneralKillNotificationItemVM.InitProperties");
                    return;
                }

                DisplayNameResolutionState killerState = ResolvePreferredCoopBattleDisplayNameState(affectorAgent);
                DisplayNameResolutionState victimState = ResolvePreferredCoopBattleDisplayNameState(affectedAgent);
                string killerName = killerState.PreferredName;
                string victimName = victimState.PreferredName;
                PropertyInfo murdererNameProperty = AccessTools.Property(__instance.GetType(), "MurdererName");
                PropertyInfo victimNameProperty = AccessTools.Property(__instance.GetType(), "VictimName");

                murdererNameProperty?.SetValue(__instance, killerName);
                victimNameProperty?.SetValue(__instance, victimName);
                LogPairConsumerDiagnostic(
                    "general-killfeed-init",
                    killerState,
                    victimState,
                    "MurdererName=" + ShortenDiagnosticValue(murdererNameProperty?.GetValue(__instance)?.ToString(), 96) +
                    " VictimName=" + ShortenDiagnosticValue(victimNameProperty?.GetValue(__instance)?.ToString(), 96),
                    "MPGeneralKillNotificationItemVM.InitProperties");
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
                Mission mission = Mission.Current;
                if (!ShouldRunForCurrentMission(mission))
                {
                    LogConsumerGateDiagnostic(
                        "general-killfeed-death",
                        mission,
                        affectedAgent,
                        affectorAgent,
                        "MPGeneralKillNotificationItemVM.InitDeathProperties");
                    return;
                }

                string message = null;
                DisplayNameResolutionState killerState = ResolvePreferredCoopBattleDisplayNameState(affectorAgent);
                DisplayNameResolutionState victimState = ResolvePreferredCoopBattleDisplayNameState(affectedAgent);
                DisplayNameResolutionState assistedState = ResolvePreferredCoopBattleDisplayNameState(assistedAgent);
                if (affectorAgent != null && affectorAgent.IsMainAgent)
                {
                    MBTextManager.SetTextVariable("TROOP_NAME", victimState.PreferredName, false);
                    message = GameTexts.FindText("str_kill_feed_message", null).ToString();
                }
                else if (affectedAgent != null && affectedAgent.IsMainAgent)
                {
                    MBTextManager.SetTextVariable("TROOP_NAME", killerState.PreferredName, false);
                    message = GameTexts.FindText("str_death_feed_message", null).ToString();
                }
                else if (assistedAgent != null && assistedAgent.IsMainAgent)
                {
                    MBTextManager.SetTextVariable("TROOP_NAME", victimState.PreferredName, false);
                    message = GameTexts.FindText("str_assist_feed_message", null).ToString();
                }

                if (!string.IsNullOrWhiteSpace(message))
                    AccessTools.Property(__instance.GetType(), "Message")?.SetValue(__instance, message);

                LogTripleConsumerDiagnostic(
                    "general-killfeed-death",
                    killerState,
                    victimState,
                    assistedState,
                    ShortenDiagnosticValue(message, 160),
                    "MPGeneralKillNotificationItemVM.InitDeathProperties");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleDisplayNameConsumerPatch: general kill-feed InitDeathProperties postfix failed: " + ex.Message);
            }
        }

        private static void MissionGauntletKillNotificationSingleplayerUIHandler_OnAgentRemoved_Postfix(
            object __instance,
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState)
        {
            try
            {
                Mission mission = Mission.Current;
                if (!ShouldRunForCurrentMission(mission) ||
                    GameNetwork.IsDedicatedServer ||
                    __instance == null ||
                    affectedAgent == null ||
                    !affectedAgent.IsHuman ||
                    affectorAgent == null ||
                    !ReferenceEquals(affectorAgent, Agent.Main) ||
                    (agentState != AgentState.Killed &&
                     agentState != AgentState.Unconscious))
                {
                    return;
                }

                FieldInfo personalFeedEnabledField =
                    AccessTools.Field(__instance.GetType(), "_isPersonalFeedEnabled");
                if (personalFeedEnabledField?.GetValue(__instance) is bool personalFeedEnabled &&
                    !personalFeedEnabled)
                {
                    return;
                }

                DisplayNameResolutionState victimState =
                    ResolvePreferredCoopBattleDisplayNameState(affectedAgent);
                if (!victimState.ExactResolved ||
                    string.IsNullOrWhiteSpace(victimState.PreferredName))
                {
                    return;
                }

                FieldInfo dataSourceField = AccessTools.Field(__instance.GetType(), "_dataSource");
                object dataSource = dataSourceField?.GetValue(__instance);
                object personalFeed =
                    AccessTools.Property(dataSource?.GetType(), "PersonalFeed")
                        ?.GetValue(dataSource);
                object notificationList =
                    AccessTools.Property(personalFeed?.GetType(), "NotificationList")
                        ?.GetValue(personalFeed);
                object latestItem = GetLastNotificationItem(notificationList);
                PropertyInfo messageProperty =
                    AccessTools.Property(latestItem?.GetType(), "Message");
                if (latestItem == null || messageProperty == null)
                    return;

                messageProperty.SetValue(latestItem, victimState.PreferredName);
                LogOverride(
                    "singleplayer-personal-killfeed",
                    affectedAgent,
                    victimState.EntryId,
                    victimState.PreferredName);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopBattleDisplayNameConsumerPatch: singleplayer personal kill-feed postfix failed: " +
                    ex.Message);
            }
        }

        private static void SPGeneralKillNotificationItemVM_InitProperties_Postfix(
            object __instance,
            Agent affectedAgent,
            Agent affectorAgent)
        {
            try
            {
                Mission mission = Mission.Current;
                if (!ShouldRunForCurrentMission(mission) ||
                    GameNetwork.IsDedicatedServer ||
                    __instance == null)
                {
                    return;
                }

                PropertyInfo murdererNameProperty =
                    AccessTools.Property(__instance.GetType(), "MurdererName");
                PropertyInfo victimNameProperty =
                    AccessTools.Property(__instance.GetType(), "VictimName");
                string currentMurdererName =
                    murdererNameProperty?.GetValue(__instance)?.ToString();
                string currentVictimName =
                    victimNameProperty?.GetValue(__instance)?.ToString();

                DisplayNameResolutionState killerState =
                    ResolvePreferredCoopBattleDisplayNameState(affectorAgent);
                if (!string.IsNullOrWhiteSpace(currentMurdererName) &&
                    killerState.ExactResolved &&
                    !string.IsNullOrWhiteSpace(killerState.PreferredName))
                {
                    murdererNameProperty?.SetValue(
                        __instance,
                        killerState.PreferredName);
                    LogOverride(
                        "singleplayer-general-killfeed-killer",
                        affectorAgent,
                        killerState.EntryId,
                        killerState.PreferredName);
                }

                DisplayNameResolutionState victimState =
                    ResolvePreferredCoopBattleDisplayNameState(affectedAgent);
                if (!string.IsNullOrWhiteSpace(currentVictimName) &&
                    victimState.ExactResolved &&
                    !string.IsNullOrWhiteSpace(victimState.PreferredName))
                {
                    victimNameProperty?.SetValue(
                        __instance,
                        victimState.PreferredName);
                    LogOverride(
                        "singleplayer-general-killfeed-victim",
                        affectedAgent,
                        victimState.EntryId,
                        victimState.PreferredName);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopBattleDisplayNameConsumerPatch: singleplayer general kill-feed postfix failed: " +
                    ex.Message);
            }
        }

        private static object GetLastNotificationItem(object notificationList)
        {
            if (notificationList == null)
                return null;

            if (notificationList is IList list)
                return list.Count > 0 ? list[list.Count - 1] : null;

            PropertyInfo countProperty =
                AccessTools.Property(notificationList.GetType(), "Count");
            PropertyInfo itemProperty =
                AccessTools.Property(notificationList.GetType(), "Item");
            if (!(countProperty?.GetValue(notificationList) is int count) ||
                count <= 0 ||
                itemProperty == null)
            {
                return null;
            }

            return itemProperty.GetValue(
                notificationList,
                new object[] { count - 1 });
        }

        private static void MissionNetworkHelper_GetCombatLogDataForCombatLogNetworkMessage_Postfix(
            NetworkMessages.FromServer.CombatLogNetworkMessage message,
            ref CombatLogData __result)
        {
            try
            {
                Mission mission = Mission.Current;
                if (!ShouldRunForCurrentMission(mission) || message == null)
                    return;

                Agent victimAgent = Mission.MissionNetworkHelper.GetAgentFromIndex(
                    message.VictimAgentIndex,
                    canBeNull: true);
                if (victimAgent == null || victimAgent.IsMount)
                    return;

                if (TryResolveBattleOnlyExactDisplayNameForAgent(
                        victimAgent,
                        out string entryId,
                        out string resolvedName))
                {
                    if (!string.IsNullOrWhiteSpace(resolvedName))
                    {
                        __result.VictimAgentName = resolvedName;
                        LogOverride("personal-combat-log", victimAgent, entryId, resolvedName);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleDisplayNameConsumerPatch: combat-log exact name postfix failed: " + ex.Message);
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

            return HasCoopBattleRuntimeMarker(mission);
        }

        private static bool HasCoopBattleRuntimeMarker(Mission mission)
        {
            if (mission == null)
                return false;

            return mission.GetMissionBehavior<MissionMultiplayerCoopBattle>() != null ||
                   mission.GetMissionBehavior<MissionMultiplayerCoopBattleClient>() != null ||
                   mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeployment>() != null ||
                   mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeploymentClient>() != null ||
                   mission.GetMissionBehavior<CoopMissionClientLogic>() != null ||
                   mission.GetMissionBehavior<CoopMissionSpawnLogic>() != null ||
                   mission.GetMissionBehavior<CoopMissionNetworkBridge>() != null ||
                   mission.GetMissionBehavior<CoopMissionSelectionView>() != null ||
                   BattleSnapshotRuntimeState.GetState() != null;
        }

        private static string ResolvePreferredCoopBattleDisplayName(Agent agent)
        {
            return ResolvePreferredCoopBattleDisplayNameState(agent).PreferredName;
        }

        private static bool TryResolveBattleOnlyExactDisplayNameForAgent(Agent agent, out string entryId, out string exactName)
        {
            return TryResolveBattleOnlyExactDisplayNameForAgent(
                agent,
                out entryId,
                out exactName,
                out _,
                out _);
        }

        private static bool TryResolveBattleOnlyExactDisplayNameForAgent(
            Agent agent,
            out string entryId,
            out string exactName,
            out string exactResolutionReason,
            out string entryResolutionSource)
        {
            entryId = null;
            exactName = null;
            exactResolutionReason = "Uninitialized";
            entryResolutionSource = "None";
            if (agent == null ||
                !ShouldRunForCurrentMission(Mission.Current))
            {
                exactResolutionReason = agent == null ? "AgentNull" : "MissionNotEligible";
                return false;
            }

            if (CoopMissionSpawnLogic.TryResolveExactDisplayNameForAgent(
                    agent,
                    out entryId,
                    out TextObject resolvedExactName) &&
                resolvedExactName != null)
            {
                exactName = resolvedExactName.ToString();
                exactResolutionReason = "ExactEntry";
                entryResolutionSource = "CoopMissionSpawnLogic";
                return !string.IsNullOrWhiteSpace(exactName);
            }

            bool entryResolved = TryResolveBattleOnlyEntryId(agent, out entryId, out entryResolutionSource);
            if (!entryResolved || string.IsNullOrWhiteSpace(entryId))
            {
                exactResolutionReason = !entryResolved
                    ? "EntryIdMissing"
                    : "EntryIdBlank";
                return false;
            }

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (entryState == null)
            {
                exactResolutionReason = "EntryStateMissing";
                return false;
            }

            string resolvedDisplayName = BattleSnapshotRuntimeState.ResolveEntryDisplayName(entryState, entryId);
            if (string.IsNullOrWhiteSpace(resolvedDisplayName) ||
                string.Equals(resolvedDisplayName, "Unknown Unit", StringComparison.Ordinal))
            {
                exactResolutionReason = "DisplayNameUnresolved";
                return false;
            }

            exactName = resolvedDisplayName;
            exactResolutionReason = "ExactEntry";
            return true;
        }

        private static bool TryResolveBattleOnlyEntryId(Agent agent, out string entryId)
        {
            return TryResolveBattleOnlyEntryId(agent, out entryId, out _);
        }

        private static bool TryResolveBattleOnlyEntryId(Agent agent, out string entryId, out string source)
        {
            if (CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(agent, out entryId) &&
                !string.IsNullOrWhiteSpace(entryId))
            {
                source = "AuthoritativeTracked";
                return true;
            }

            if (ExactCampaignArmyBootstrap.TryGetEntryId(agent, out entryId) &&
                !string.IsNullOrWhiteSpace(entryId))
            {
                source = "ExactCampaignArmyBootstrap";
                return true;
            }

            source = "Unresolved";
            return false;
        }

        private static void PrimeExactDisplayNameCacheForAgent(Agent agent)
        {
            if (agent == null || agent.IsMount)
                return;

            CoopMissionSpawnLogic.TryResolveExactDisplayNameForAgent(
                agent,
                out _,
                out _);
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
            if (!ExperimentalFeatures.EnableBattleSelectionDisplayNameDiagnostics ||
                agent == null ||
                string.IsNullOrWhiteSpace(entryId) ||
                string.IsNullOrWhiteSpace(exactName))
            {
                return;
            }

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

        private static DisplayNameResolutionState ResolvePreferredCoopBattleDisplayNameState(Agent agent)
        {
            var state = new DisplayNameResolutionState
            {
                Agent = agent,
                EntryId = null,
                PreferredName = string.Empty,
                PreferredSource = "Empty",
                ExactResolutionReason = "Uninitialized",
                EntryResolutionSource = "None",
                ExactResolved = false
            };

            if (TryResolveBattleOnlyExactDisplayNameForAgent(
                agent,
                out string entryId,
                out string exactName,
                out string exactResolutionReason,
                out string entryResolutionSource))
            {
                state.EntryId = entryId;
                state.PreferredName = exactName ?? string.Empty;
                state.PreferredSource = "ExactEntry";
                state.ExactResolutionReason = exactResolutionReason ?? "ExactEntry";
                state.EntryResolutionSource = entryResolutionSource ?? "None";
                state.ExactResolved = true;
                return state;
            }

            state.EntryId = entryId;
            state.ExactResolutionReason = exactResolutionReason ?? "Unknown";
            state.EntryResolutionSource = entryResolutionSource ?? "None";
            if (agent?.MissionPeer != null && !string.IsNullOrWhiteSpace(agent.MissionPeer.DisplayedName))
            {
                state.PreferredName = agent.MissionPeer.DisplayedName;
                state.PreferredSource = "MissionPeer";
                return state;
            }

            state.PreferredName = agent?.Name ?? string.Empty;
            state.PreferredSource = !string.IsNullOrWhiteSpace(state.PreferredName)
                ? "AgentName"
                : "Empty";
            return state;
        }

        private static void LogSingleAgentConsumerDiagnostic(
            string consumer,
            DisplayNameResolutionState state,
            string preValue,
            string postValue,
            string source)
        {
            if (!ExperimentalFeatures.EnableBattleSelectionDisplayNameDiagnostics)
                return;

            string agentKey = BuildAgentDiagnosticKey(state?.Agent);
            string key =
                "single|" + consumer + "|" + agentKey + "|" +
                (state?.EntryId ?? "null") + "|" +
                (state?.PreferredSource ?? "null") + "|" +
                (state?.PreferredName ?? string.Empty) + "|" +
                (state?.ExactResolutionReason ?? "null") + "|" +
                (preValue ?? string.Empty) + "|" +
                (postValue ?? string.Empty);
            if (!TryAddDiagnosticKey(key))
                return;

            Agent agent = state?.Agent;
            ModLogger.Info(
                "CoopBattleDisplayNameConsumerPatch: consumer diagnostic. " +
                "Consumer=" + (consumer ?? "unknown") +
                " Source=" + (source ?? "unknown") +
                " AgentIndex=" + (agent?.Index.ToString() ?? "null") +
                " AgentName=" + ShortenDiagnosticValue(agent?.Name, 96) +
                " MissionPeerDisplayedName=" + ShortenDiagnosticValue(agent?.MissionPeer?.DisplayedName, 96) +
                " CharacterId=" + ShortenDiagnosticValue(agent?.Character?.StringId, 64) +
                " TeamSide=" + (agent?.Team?.Side.ToString() ?? "null") +
                " EntryId=" + ShortenDiagnosticValue(state?.EntryId, 96) +
                " EntrySource=" + (state?.EntryResolutionSource ?? "null") +
                " PreferredSource=" + (state?.PreferredSource ?? "null") +
                " ExactResolved=" + (state?.ExactResolved ?? false) +
                " ExactReason=" + (state?.ExactResolutionReason ?? "null") +
                " PreferredName=" + ShortenDiagnosticValue(state?.PreferredName, 96) +
                " PreValue=" + ShortenDiagnosticValue(preValue, 96) +
                " PostValue=" + ShortenDiagnosticValue(postValue, 96));
        }

        private static void LogPairConsumerDiagnostic(
            string consumer,
            DisplayNameResolutionState primaryState,
            DisplayNameResolutionState secondaryState,
            string payload,
            string source)
        {
            if (!ExperimentalFeatures.EnableBattleSelectionDisplayNameDiagnostics)
                return;

            string key =
                "pair|" + consumer + "|" +
                BuildAgentDiagnosticKey(primaryState?.Agent) + "|" +
                BuildAgentDiagnosticKey(secondaryState?.Agent) + "|" +
                (primaryState?.PreferredName ?? string.Empty) + "|" +
                (secondaryState?.PreferredName ?? string.Empty) + "|" +
                (payload ?? string.Empty);
            if (!TryAddDiagnosticKey(key))
                return;

            ModLogger.Info(
                "CoopBattleDisplayNameConsumerPatch: consumer pair diagnostic. " +
                "Consumer=" + (consumer ?? "unknown") +
                " Source=" + (source ?? "unknown") +
                " Primary={" + DescribeDisplayNameResolutionState(primaryState) + "} " +
                " Secondary={" + DescribeDisplayNameResolutionState(secondaryState) + "} " +
                " Payload=" + (payload ?? string.Empty));
        }

        private static void LogTripleConsumerDiagnostic(
            string consumer,
            DisplayNameResolutionState firstState,
            DisplayNameResolutionState secondState,
            DisplayNameResolutionState thirdState,
            string payload,
            string source)
        {
            if (!ExperimentalFeatures.EnableBattleSelectionDisplayNameDiagnostics)
                return;

            string key =
                "triple|" + consumer + "|" +
                BuildAgentDiagnosticKey(firstState?.Agent) + "|" +
                BuildAgentDiagnosticKey(secondState?.Agent) + "|" +
                BuildAgentDiagnosticKey(thirdState?.Agent) + "|" +
                (payload ?? string.Empty);
            if (!TryAddDiagnosticKey(key))
                return;

            ModLogger.Info(
                "CoopBattleDisplayNameConsumerPatch: consumer triple diagnostic. " +
                "Consumer=" + (consumer ?? "unknown") +
                " Source=" + (source ?? "unknown") +
                " First={" + DescribeDisplayNameResolutionState(firstState) + "} " +
                " Second={" + DescribeDisplayNameResolutionState(secondState) + "} " +
                " Third={" + DescribeDisplayNameResolutionState(thirdState) + "} " +
                " Payload=" + (payload ?? string.Empty));
        }

        private static void LogConsumerGateDiagnostic(
            string consumer,
            Mission mission,
            Agent primaryAgent,
            Agent secondaryAgent,
            string source)
        {
            if (!ExperimentalFeatures.EnableBattleSelectionDisplayNameDiagnostics)
                return;

            string key =
                "gate|" + consumer + "|" +
                (mission?.SceneName ?? "null") + "|" +
                BuildAgentDiagnosticKey(primaryAgent) + "|" +
                BuildAgentDiagnosticKey(secondaryAgent);
            if (!TryAddDiagnosticKey(key))
                return;

            ModLogger.Info(
                "CoopBattleDisplayNameConsumerPatch: consumer gate diagnostic. " +
                "Consumer=" + (consumer ?? "unknown") +
                " Source=" + (source ?? "unknown") +
                " MissionScene=" + (mission?.SceneName ?? "null") +
                " GameNetworkIsClient=" + GameNetwork.IsClient +
                " SessionActive=" + GameNetwork.IsSessionActive +
                " HasCoopBattleBehavior=" + (mission?.GetMissionBehavior<MissionMultiplayerCoopBattle>() != null) +
                " HasCoopBattleClientBehavior=" + (mission?.GetMissionBehavior<MissionMultiplayerCoopBattleClient>() != null) +
                " HasCoopSiegeBehavior=" + (mission?.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeployment>() != null) +
                " HasCoopSiegeClientBehavior=" + (mission?.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeploymentClient>() != null) +
                " HasCoopMissionClientLogic=" + (mission?.GetMissionBehavior<CoopMissionClientLogic>() != null) +
                " HasCoopMissionSpawnLogic=" + (mission?.GetMissionBehavior<CoopMissionSpawnLogic>() != null) +
                " HasCoopMissionNetworkBridge=" + (mission?.GetMissionBehavior<CoopMissionNetworkBridge>() != null) +
                " HasCoopMissionSelectionView=" + (mission?.GetMissionBehavior<CoopMissionSelectionView>() != null) +
                " HasBattleSnapshotRuntimeState=" + (BattleSnapshotRuntimeState.GetState() != null) +
                " PrimaryAgentIndex=" + (primaryAgent?.Index.ToString() ?? "null") +
                " SecondaryAgentIndex=" + (secondaryAgent?.Index.ToString() ?? "null"));
        }

        private static bool TryAddDiagnosticKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            lock (LoggedDiagnosticKeys)
                return LoggedDiagnosticKeys.Add(key);
        }

        private static string DescribeDisplayNameResolutionState(DisplayNameResolutionState state)
        {
            Agent agent = state?.Agent;
            return
                "AgentIndex=" + (agent?.Index.ToString() ?? "null") +
                ",AgentName=" + ShortenDiagnosticValue(agent?.Name, 72) +
                ",MissionPeerDisplayedName=" + ShortenDiagnosticValue(agent?.MissionPeer?.DisplayedName, 72) +
                ",CharacterId=" + ShortenDiagnosticValue(agent?.Character?.StringId, 48) +
                ",EntryId=" + ShortenDiagnosticValue(state?.EntryId, 72) +
                ",EntrySource=" + (state?.EntryResolutionSource ?? "null") +
                ",PreferredSource=" + (state?.PreferredSource ?? "null") +
                ",ExactResolved=" + (state?.ExactResolved ?? false) +
                ",ExactReason=" + (state?.ExactResolutionReason ?? "null") +
                ",PreferredName=" + ShortenDiagnosticValue(state?.PreferredName, 72);
        }

        private static string BuildAgentDiagnosticKey(Agent agent)
        {
            if (agent == null)
                return "null";

            return agent.Index + "|" + (agent.Character?.StringId ?? "null");
        }

        private static string ShortenDiagnosticValue(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
                return value ?? "null";

            return value.Substring(0, maxChars) + "...";
        }
    }
}
