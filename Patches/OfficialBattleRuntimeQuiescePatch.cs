using System;
using System.Reflection;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Lets the official listed/custom-server battle shell finish startup, then
    /// suppresses the vanilla FlagDomination runtime tick once our exact
    /// campaign battle runtime has taken ownership on battle_terrain_* scenes.
    /// This avoids a second ReplaceBotWithPlayer/round loop fighting with our
    /// coop possession and spawn controllers after BattleActive begins.
    /// </summary>
    public static class OfficialBattleRuntimeQuiescePatch
    {
        private static string _lastFlagDominationSuppressionLogKey = string.Empty;
        private static string _lastFlagDominationPassThroughLogKey = string.Empty;
        private static string _lastWarmupSuppressionLogKey = string.Empty;
        private static string _lastWarmupPassThroughLogKey = string.Empty;
        private static string _lastRoundControllerSuppressionLogKey = string.Empty;
        private static string _lastRoundControllerPassThroughLogKey = string.Empty;
        private static string _lastSpawnComponentSuppressionLogKey = string.Empty;
        private static string _lastSpawnComponentPassThroughLogKey = string.Empty;
        private static string _lastCustomGameServerMissionTickSuppressionLogKey = string.Empty;
        private static string _lastCustomGameServerMissionTickPassThroughLogKey = string.Empty;
        private static string _lastCustomGameServerScoreHitSuppressionLogKey = string.Empty;
        private static string _lastCustomGameServerScoreHitPassThroughLogKey = string.Empty;
        private static string _lastCustomGameServerPlayerKillsSuppressionLogKey = string.Empty;
        private static string _lastCustomGameServerPlayerKillsPassThroughLogKey = string.Empty;
        private static string _lastCustomGameServerPlayerDiesSuppressionLogKey = string.Empty;
        private static string _lastCustomGameServerPlayerDiesPassThroughLogKey = string.Empty;
        private static string _lastCustomGameServerBotKillsSuppressionLogKey = string.Empty;
        private static string _lastCustomGameServerBotKillsPassThroughLogKey = string.Empty;
        private static string _lastCustomGameServerObjectiveGoldSuppressionLogKey = string.Empty;
        private static string _lastCustomGameServerObjectiveGoldPassThroughLogKey = string.Empty;
        private static string _lastCustomGameServerAgentBuildSuppressionLogKey = string.Empty;
        private static string _lastCustomGameServerAgentBuildPassThroughLogKey = string.Empty;
        private static string _lastScoreboardScoreHitSuppressionLogKey = string.Empty;
        private static string _lastScoreboardScoreHitPassThroughLogKey = string.Empty;
        private static string _lastScoreboardAgentBuildSuppressionLogKey = string.Empty;
        private static string _lastScoreboardAgentBuildPassThroughLogKey = string.Empty;
        private static string _lastHostedServiceEarlyTickSuppressionLogKey = string.Empty;
        private static string _lastHostedServiceEarlyTickPassThroughLogKey = string.Empty;
        private static string _lastHostedServiceTickSuppressionLogKey = string.Empty;
        private static string _lastHostedServiceTickPassThroughLogKey = string.Empty;
        private static string _lastLobbyPromotionLogKey = string.Empty;
        private static readonly MethodInfo MissionLobbyComponentSetStatePlayingAsServer =
            AccessTools.Method(typeof(MissionLobbyComponent), "SetStatePlayingAsServer");

        public static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            PatchInstanceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionMultiplayerFlagDomination",
                "OnMissionTick",
                new[] { typeof(float) },
                nameof(MissionMultiplayerFlagDomination_OnMissionTick_Prefix));
            PatchInstanceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MultiplayerWarmupComponent",
                "OnPreDisplayMissionTick",
                new[] { typeof(float) },
                nameof(MultiplayerWarmupComponent_OnPreDisplayMissionTick_Prefix));
            PatchInstanceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MultiplayerRoundController",
                "OnPreDisplayMissionTick",
                new[] { typeof(float) },
                nameof(MultiplayerRoundController_OnPreDisplayMissionTick_Prefix));
            PatchInstanceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.SpawnComponent",
                "OnMissionTick",
                new[] { typeof(float) },
                nameof(SpawnComponent_OnMissionTick_Prefix));
            PatchInstanceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionLobbyComponent",
                "OnMissionTick",
                new[] { typeof(float) },
                nameof(MissionLobbyComponent_OnMissionTick_Prefix));
            PatchInstanceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.DedicatedCustomServer.MissionCustomGameServerComponent",
                "OnScoreHit",
                new[]
                {
                    typeof(Agent),
                    typeof(Agent),
                    typeof(WeaponComponentData),
                    typeof(bool),
                    typeof(bool),
                    typeof(Blow).MakeByRefType(),
                    typeof(AttackCollisionData).MakeByRefType(),
                    typeof(float),
                    typeof(float),
                    typeof(float)
                },
                nameof(MissionCustomGameServerComponent_OnScoreHit_Prefix));
            PatchInstanceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.DedicatedCustomServer.MissionCustomGameServerComponent",
                "OnPlayerKills",
                new[] { typeof(MissionPeer), typeof(Agent), typeof(MissionPeer) },
                nameof(MissionCustomGameServerComponent_OnPlayerKills_Prefix));
            PatchInstanceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.DedicatedCustomServer.MissionCustomGameServerComponent",
                "OnPlayerDies",
                new[] { typeof(MissionPeer), typeof(MissionPeer), typeof(MissionPeer) },
                nameof(MissionCustomGameServerComponent_OnPlayerDies_Prefix));
            PatchInstanceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.DedicatedCustomServer.MissionCustomGameServerComponent",
                "OnBotKills",
                new[] { typeof(Agent), typeof(Agent) },
                nameof(MissionCustomGameServerComponent_OnBotKills_Prefix));
            PatchInstanceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.DedicatedCustomServer.MissionCustomGameServerComponent",
                "OnObjectiveGoldGained",
                new[] { typeof(MissionPeer), typeof(int) },
                nameof(MissionCustomGameServerComponent_OnObjectiveGoldGained_Prefix));
            PatchInstanceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.DedicatedCustomServer.MissionCustomGameServerComponent",
                "OnAgentBuild",
                new[] { typeof(Agent), typeof(Banner) },
                nameof(MissionCustomGameServerComponent_OnAgentBuild_Prefix));
            PatchInstanceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionScoreboardComponent",
                "OnScoreHit",
                new[]
                {
                    typeof(Agent),
                    typeof(Agent),
                    typeof(WeaponComponentData),
                    typeof(bool),
                    typeof(bool),
                    typeof(Blow).MakeByRefType(),
                    typeof(AttackCollisionData).MakeByRefType(),
                    typeof(float),
                    typeof(float),
                    typeof(float)
                },
                nameof(MissionScoreboardComponent_OnScoreHit_Prefix));
            PatchInstanceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.MissionScoreboardComponent",
                "OnAgentBuild",
                new[] { typeof(Agent), typeof(Banner) },
                nameof(MissionScoreboardComponent_OnAgentBuild_Prefix));
            PatchExplicitInterfaceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.DedicatedCustomServer.DedicatedCustomServerIntermissionManagerHandler",
                "TaleWorlds.MountAndBlade.ListedServer.IServerSideIntermissionManagerHandler",
                "OnEarlyTick",
                new[] { typeof(float) },
                nameof(DedicatedCustomServerIntermissionManagerHandler_OnEarlyTick_Prefix));
            PatchExplicitInterfaceMethod(
                harmony,
                "TaleWorlds.MountAndBlade.DedicatedCustomServer.DedicatedCustomServerIntermissionManagerHandler",
                "TaleWorlds.MountAndBlade.ListedServer.IServerSideIntermissionManagerHandler",
                "OnTick",
                new[] { typeof(float) },
                nameof(DedicatedCustomServerIntermissionManagerHandler_OnTick_Prefix));
        }

        private static void PatchInstanceMethod(
            Harmony harmony,
            string typeName,
            string methodName,
            Type[] parameterTypes,
            string prefixName)
        {
            try
            {
                Type targetType = AccessTools.TypeByName(typeName);
                if (targetType == null)
                {
                    ModLogger.Info("OfficialBattleRuntimeQuiescePatch: type not found. Type=" + typeName);
                    return;
                }

                MethodInfo target = targetType.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null,
                    parameterTypes,
                    null);
                MethodInfo prefix = typeof(OfficialBattleRuntimeQuiescePatch).GetMethod(
                    prefixName,
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (target == null || prefix == null)
                {
                    ModLogger.Info(
                        "OfficialBattleRuntimeQuiescePatch: method not found. Type=" +
                        typeName +
                        " Method=" +
                        methodName);
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                ModLogger.Info(
                    "OfficialBattleRuntimeQuiescePatch: patched " +
                    typeName +
                    "." +
                    methodName +
                    ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "OfficialBattleRuntimeQuiescePatch: patch apply failed. Type=" +
                    typeName +
                    " Method=" +
                    methodName +
                    " Error=" +
                    ex.GetType().Name +
                    ": " +
                    ex.Message);
            }
        }

        private static void PatchExplicitInterfaceMethod(
            Harmony harmony,
            string typeName,
            string interfaceTypeName,
            string methodName,
            Type[] parameterTypes,
            string prefixName)
        {
            try
            {
                Type targetType = AccessTools.TypeByName(typeName);
                Type interfaceType = AccessTools.TypeByName(interfaceTypeName);
                MethodInfo prefix = typeof(OfficialBattleRuntimeQuiescePatch).GetMethod(
                    prefixName,
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (targetType == null || interfaceType == null || prefix == null)
                {
                    ModLogger.Info(
                        "OfficialBattleRuntimeQuiescePatch: explicit interface patch target not found. Type=" +
                        typeName +
                        " Interface=" +
                        interfaceTypeName +
                        " Method=" +
                        methodName);
                    return;
                }

                InterfaceMapping map = targetType.GetInterfaceMap(interfaceType);
                MethodInfo target = null;
                for (int i = 0; i < map.InterfaceMethods.Length; i++)
                {
                    MethodInfo candidate = map.InterfaceMethods[i];
                    if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                        continue;

                    ParameterInfo[] parameters = candidate.GetParameters();
                    if (parameters.Length != parameterTypes.Length)
                        continue;

                    if (parameters.Select(p => p.ParameterType).SequenceEqual(parameterTypes))
                    {
                        target = map.TargetMethods[i];
                        break;
                    }
                }

                if (target == null)
                {
                    ModLogger.Info(
                        "OfficialBattleRuntimeQuiescePatch: explicit interface method not found. Type=" +
                        typeName +
                        " Interface=" +
                        interfaceTypeName +
                        " Method=" +
                        methodName);
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                ModLogger.Info(
                    "OfficialBattleRuntimeQuiescePatch: patched " +
                    typeName +
                    "." +
                    interfaceTypeName +
                    "." +
                    methodName +
                    ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "OfficialBattleRuntimeQuiescePatch: explicit interface patch apply failed. Type=" +
                    typeName +
                    " Interface=" +
                    interfaceTypeName +
                    " Method=" +
                    methodName +
                    " Error=" +
                    ex.GetType().Name +
                    ": " +
                    ex.Message);
            }
        }

        private static bool MissionMultiplayerFlagDomination_OnMissionTick_Prefix(object __instance, float dt)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!ShouldQuiesceOfficialFlagDominationRuntime(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastFlagDominationPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left MissionMultiplayerFlagDomination.OnMissionTick active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastFlagDominationSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed MissionMultiplayerFlagDomination.OnMissionTick after coop battle runtime ownership. ",
                reason);
            return false;
        }

        private static bool MultiplayerWarmupComponent_OnPreDisplayMissionTick_Prefix(object __instance, float dt)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!ShouldQuiesceOfficialWarmupAndSpawnRuntime(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastWarmupPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left MultiplayerWarmupComponent.OnPreDisplayMissionTick active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastWarmupSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed MultiplayerWarmupComponent.OnPreDisplayMissionTick after coop pre-battle ownership. ",
                reason);
            return false;
        }

        private static bool MultiplayerRoundController_OnPreDisplayMissionTick_Prefix(object __instance, float dt)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!ShouldQuiesceOfficialWarmupAndSpawnRuntime(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastRoundControllerPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left MultiplayerRoundController.OnPreDisplayMissionTick active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastRoundControllerSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed MultiplayerRoundController.OnPreDisplayMissionTick after coop pre-battle ownership. ",
                reason);
            return false;
        }

        private static bool SpawnComponent_OnMissionTick_Prefix(object __instance, float dt)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!ShouldQuiesceOfficialWarmupAndSpawnRuntime(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastSpawnComponentPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left SpawnComponent.OnMissionTick active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastSpawnComponentSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed SpawnComponent.OnMissionTick after coop pre-battle ownership. ",
                reason);
            return false;
        }

        private static bool MissionLobbyComponent_OnMissionTick_Prefix(object __instance, float dt)
        {
            if (!IsMissionCustomGameServerComponentInstance(__instance))
                return true;

            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!ShouldQuiesceOfficialDedicatedCustomServerRuntime(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastCustomGameServerMissionTickPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left MissionCustomGameServerComponent.OnMissionTick active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastCustomGameServerMissionTickSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed MissionCustomGameServerComponent.OnMissionTick after coop battle ownership. ",
                reason);
            return false;
        }

        private static bool MissionCustomGameServerComponent_OnScoreHit_Prefix(object __instance)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!ShouldQuiesceOfficialDedicatedCustomServerCombatCallbacks(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastCustomGameServerScoreHitPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left MissionCustomGameServerComponent.OnScoreHit active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastCustomGameServerScoreHitSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed MissionCustomGameServerComponent.OnScoreHit after coop combat ownership. ",
                reason);
            return false;
        }

        private static bool MissionCustomGameServerComponent_OnPlayerKills_Prefix(object __instance)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!ShouldQuiesceOfficialDedicatedCustomServerCombatCallbacks(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastCustomGameServerPlayerKillsPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left MissionCustomGameServerComponent.OnPlayerKills active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastCustomGameServerPlayerKillsSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed MissionCustomGameServerComponent.OnPlayerKills after coop combat ownership. ",
                reason);
            return false;
        }

        private static bool MissionCustomGameServerComponent_OnPlayerDies_Prefix(object __instance)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!ShouldQuiesceOfficialDedicatedCustomServerCombatCallbacks(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastCustomGameServerPlayerDiesPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left MissionCustomGameServerComponent.OnPlayerDies active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastCustomGameServerPlayerDiesSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed MissionCustomGameServerComponent.OnPlayerDies after coop combat ownership. ",
                reason);
            return false;
        }

        private static bool MissionCustomGameServerComponent_OnBotKills_Prefix(object __instance)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!ShouldQuiesceOfficialDedicatedCustomServerCombatCallbacks(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastCustomGameServerBotKillsPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left MissionCustomGameServerComponent.OnBotKills active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastCustomGameServerBotKillsSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed MissionCustomGameServerComponent.OnBotKills after coop combat ownership. ",
                reason);
            return false;
        }

        private static bool MissionCustomGameServerComponent_OnObjectiveGoldGained_Prefix(object __instance)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!ShouldQuiesceOfficialDedicatedCustomServerCombatCallbacks(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastCustomGameServerObjectiveGoldPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left MissionCustomGameServerComponent.OnObjectiveGoldGained active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastCustomGameServerObjectiveGoldSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed MissionCustomGameServerComponent.OnObjectiveGoldGained after coop combat ownership. ",
                reason);
            return false;
        }

        private static bool MissionCustomGameServerComponent_OnAgentBuild_Prefix(object __instance)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!ShouldQuiesceOfficialDedicatedCustomServerCombatCallbacks(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastCustomGameServerAgentBuildPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left MissionCustomGameServerComponent.OnAgentBuild active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastCustomGameServerAgentBuildSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed MissionCustomGameServerComponent.OnAgentBuild after coop combat ownership. ",
                reason);
            return false;
        }

        private static bool MissionScoreboardComponent_OnScoreHit_Prefix(object __instance)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!ShouldQuiesceOfficialDedicatedCustomServerCombatCallbacks(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastScoreboardScoreHitPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left MissionScoreboardComponent.OnScoreHit active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastScoreboardScoreHitSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed MissionScoreboardComponent.OnScoreHit after coop combat ownership. ",
                reason);
            return false;
        }

        private static bool MissionScoreboardComponent_OnAgentBuild_Prefix(object __instance)
        {
            Mission mission = (__instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!ShouldQuiesceOfficialDedicatedCustomServerCombatCallbacks(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastScoreboardAgentBuildPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left MissionScoreboardComponent.OnAgentBuild active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastScoreboardAgentBuildSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed MissionScoreboardComponent.OnAgentBuild after coop combat ownership. ",
                reason);
            return false;
        }

        private static bool DedicatedCustomServerIntermissionManagerHandler_OnEarlyTick_Prefix(object __instance, float dt)
        {
            Mission mission = Mission.Current;
            if (!ShouldQuiesceHostedDedicatedServiceRuntime(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastHostedServiceEarlyTickPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left DedicatedCustomServerIntermissionManagerHandler.OnEarlyTick active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastHostedServiceEarlyTickSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed DedicatedCustomServerIntermissionManagerHandler.OnEarlyTick after coop hosted runtime ownership. ",
                reason);
            return false;
        }

        private static bool DedicatedCustomServerIntermissionManagerHandler_OnTick_Prefix(object __instance, float dt)
        {
            Mission mission = Mission.Current;
            if (!ShouldQuiesceHostedDedicatedServiceRuntime(mission, out string reason))
            {
                LogPassThrough(
                    ref _lastHostedServiceTickPassThroughLogKey,
                    mission,
                    "OfficialBattleRuntimeQuiescePatch: left DedicatedCustomServerIntermissionManagerHandler.OnTick active. ",
                    reason);
                return true;
            }

            LogSuppression(
                ref _lastHostedServiceTickSuppressionLogKey,
                mission,
                "OfficialBattleRuntimeQuiescePatch: suppressed DedicatedCustomServerIntermissionManagerHandler.OnTick after coop hosted runtime ownership. ",
                reason);
            return false;
        }

        private static bool ShouldQuiesceOfficialFlagDominationRuntime(Mission mission, out string reason)
        {
            if (mission == null)
            {
                reason = "Mission=null";
                return false;
            }

            string sceneName = mission.SceneName ?? string.Empty;
            if (!SceneRuntimeClassifier.IsCampaignBattleScene(sceneName))
            {
                reason = "SceneNotCampaignBattle Scene=" + (string.IsNullOrWhiteSpace(sceneName) ? "(empty)" : sceneName);
                return false;
            }

            CoopMissionSpawnLogic coopSpawnLogic = mission.GetMissionBehavior<CoopMissionSpawnLogic>();
            if (coopSpawnLogic == null)
            {
                reason = "NoCoopMissionSpawnLogic Scene=" + sceneName;
                return false;
            }

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.BattleActive || currentPhase >= CoopBattlePhase.BattleEnded)
            {
                reason = "PhaseNotActive Scene=" + sceneName + " Phase=" + currentPhase;
                return false;
            }

            MissionLobbyComponent lobbyComponent = mission.GetMissionBehavior<MissionLobbyComponent>();
            TryPromoteMissionLobbyToPlaying(mission, lobbyComponent, currentPhase);
            MissionLobbyComponent.MultiplayerGameState? lobbyState = lobbyComponent?.CurrentMultiplayerState;
            if (lobbyState == MissionLobbyComponent.MultiplayerGameState.Ending)
            {
                reason = "LobbyEnding Scene=" + sceneName + " Phase=" + currentPhase;
                return false;
            }

            reason =
                "Scene=" + sceneName +
                " Phase=" + currentPhase +
                " LobbyState=" + (lobbyState?.ToString() ?? "null") +
                " HasCoopSpawnLogic=true" +
                " HasMultiplayerRoundController=" + (mission.GetMissionBehavior<MultiplayerRoundController>() != null);
            return true;
        }

        private static bool ShouldQuiesceOfficialWarmupAndSpawnRuntime(Mission mission, out string reason)
        {
            if (mission == null)
            {
                reason = "Mission=null";
                return false;
            }

            string sceneName = mission.SceneName ?? string.Empty;
            if (!SceneRuntimeClassifier.IsCampaignBattleScene(sceneName))
            {
                reason = "SceneNotCampaignBattle Scene=" + (string.IsNullOrWhiteSpace(sceneName) ? "(empty)" : sceneName);
                return false;
            }

            CoopMissionSpawnLogic coopSpawnLogic = mission.GetMissionBehavior<CoopMissionSpawnLogic>();
            if (coopSpawnLogic == null)
            {
                reason = "NoCoopMissionSpawnLogic Scene=" + sceneName;
                return false;
            }

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.PreBattleHold || currentPhase >= CoopBattlePhase.BattleEnded)
            {
                reason = "PhaseBeforeOwnership Scene=" + sceneName + " Phase=" + currentPhase;
                return false;
            }

            MissionLobbyComponent lobbyComponent = mission.GetMissionBehavior<MissionLobbyComponent>();
            TryPromoteMissionLobbyToPlaying(mission, lobbyComponent, currentPhase);
            MissionLobbyComponent.MultiplayerGameState? lobbyState = lobbyComponent?.CurrentMultiplayerState;
            if (lobbyState == MissionLobbyComponent.MultiplayerGameState.Ending)
            {
                reason = "LobbyEnding Scene=" + sceneName + " Phase=" + currentPhase;
                return false;
            }

            reason =
                "Scene=" + sceneName +
                " Phase=" + currentPhase +
                " LobbyState=" + (lobbyState?.ToString() ?? "null") +
                " HasCoopSpawnLogic=true" +
                " HasMultiplayerWarmupComponent=" + (mission.GetMissionBehavior<MultiplayerWarmupComponent>() != null) +
                " HasMultiplayerRoundController=" + (mission.GetMissionBehavior<MultiplayerRoundController>() != null);
            return true;
        }

        private static bool ShouldQuiesceOfficialDedicatedCustomServerRuntime(Mission mission, out string reason)
        {
            if (mission == null)
            {
                reason = "Mission=null";
                return false;
            }

            string sceneName = mission.SceneName ?? string.Empty;
            if (!SceneRuntimeClassifier.IsCampaignBattleScene(sceneName))
            {
                reason = "SceneNotCampaignBattle Scene=" + (string.IsNullOrWhiteSpace(sceneName) ? "(empty)" : sceneName);
                return false;
            }

            CoopMissionSpawnLogic coopSpawnLogic = mission.GetMissionBehavior<CoopMissionSpawnLogic>();
            if (coopSpawnLogic == null)
            {
                reason = "NoCoopMissionSpawnLogic Scene=" + sceneName;
                return false;
            }

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.PreBattleHold || currentPhase >= CoopBattlePhase.BattleEnded)
            {
                reason = "PhaseBeforeOwnership Scene=" + sceneName + " Phase=" + currentPhase;
                return false;
            }

            MissionLobbyComponent lobbyComponent = mission.GetMissionBehavior<MissionLobbyComponent>();
            TryPromoteMissionLobbyToPlaying(mission, lobbyComponent, currentPhase);
            MissionLobbyComponent.MultiplayerGameState? lobbyState = lobbyComponent?.CurrentMultiplayerState;
            if (lobbyState == MissionLobbyComponent.MultiplayerGameState.Ending)
            {
                reason = "LobbyEnding Scene=" + sceneName + " Phase=" + currentPhase;
                return false;
            }

            reason =
                "Scene=" + sceneName +
                " Phase=" + currentPhase +
                " LobbyState=" + (lobbyState?.ToString() ?? "null") +
                " HasCoopSpawnLogic=true" +
                " HasMissionCustomGameServerComponent=" + HasMissionBehaviorType(mission, "MissionCustomGameServerComponent");
            return true;
        }

        private static bool ShouldQuiesceOfficialDedicatedCustomServerCombatCallbacks(Mission mission, out string reason)
        {
            if (!ShouldQuiesceOfficialDedicatedCustomServerRuntime(mission, out reason))
                return false;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.PreBattleHold)
            {
                reason = "PhaseBeforeCombatOwnership Phase=" + currentPhase;
                return false;
            }

            reason +=
                " HasMissionScoreboardComponent=" + (mission?.GetMissionBehavior<MissionScoreboardComponent>() != null);
            return true;
        }

        private static bool ShouldQuiesceHostedDedicatedServiceRuntime(Mission mission, out string reason)
        {
            if (mission == null)
            {
                reason = "Mission=null";
                return false;
            }

            string sceneName = mission.SceneName ?? string.Empty;
            if (!SceneRuntimeClassifier.IsCampaignBattleScene(sceneName))
            {
                reason = "SceneNotCampaignBattle Scene=" + (string.IsNullOrWhiteSpace(sceneName) ? "(empty)" : sceneName);
                return false;
            }

            CoopMissionSpawnLogic coopSpawnLogic = mission.GetMissionBehavior<CoopMissionSpawnLogic>();
            if (coopSpawnLogic == null)
            {
                reason = "NoCoopMissionSpawnLogic Scene=" + sceneName;
                return false;
            }

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.BattleActive || currentPhase >= CoopBattlePhase.BattleEnded)
            {
                reason = "PhaseBeforeHostedRuntimeQuiesce Scene=" + sceneName + " Phase=" + currentPhase;
                return false;
            }

            MissionLobbyComponent lobbyComponent = mission.GetMissionBehavior<MissionLobbyComponent>();
            TryPromoteMissionLobbyToPlaying(mission, lobbyComponent, currentPhase);
            MissionLobbyComponent.MultiplayerGameState? lobbyState = lobbyComponent?.CurrentMultiplayerState;
            if (lobbyState == MissionLobbyComponent.MultiplayerGameState.Ending)
            {
                reason = "LobbyEnding Scene=" + sceneName + " Phase=" + currentPhase;
                return false;
            }

            reason =
                "Scene=" + sceneName +
                " Phase=" + currentPhase +
                " LobbyState=" + (lobbyState?.ToString() ?? "null") +
                " HasCoopSpawnLogic=true" +
                " HasMissionCustomGameServerComponent=" + HasMissionBehaviorType(mission, "MissionCustomGameServerComponent");
            return true;
        }

        private static bool IsMissionCustomGameServerComponentInstance(object instance)
        {
            return string.Equals(
                instance?.GetType().FullName,
                "TaleWorlds.MountAndBlade.DedicatedCustomServer.MissionCustomGameServerComponent",
                StringComparison.Ordinal);
        }

        private static bool HasMissionBehaviorType(Mission mission, string typeName)
        {
            if (mission == null || string.IsNullOrWhiteSpace(typeName))
                return false;

            try
            {
                foreach (MissionBehavior behavior in mission.MissionBehaviors)
                {
                    if (behavior == null)
                        continue;

                    Type behaviorType = behavior.GetType();
                    if (string.Equals(behaviorType.Name, typeName, StringComparison.Ordinal) ||
                        string.Equals(behaviorType.FullName, typeName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static void TryPromoteMissionLobbyToPlaying(
            Mission mission,
            MissionLobbyComponent lobbyComponent,
            CoopBattlePhase currentPhase)
        {
            if (mission == null ||
                lobbyComponent == null ||
                !GameNetwork.IsServer ||
                currentPhase < CoopBattlePhase.PreBattleHold ||
                currentPhase >= CoopBattlePhase.BattleEnded)
            {
                return;
            }

            if (lobbyComponent.CurrentMultiplayerState != MissionLobbyComponent.MultiplayerGameState.WaitingFirstPlayers)
                return;

            if (MissionLobbyComponentSetStatePlayingAsServer == null)
            {
                LogLobbyPromotion(
                    mission,
                    currentPhase,
                    "OfficialBattleRuntimeQuiescePatch: unable to promote MissionLobbyComponent to Playing. Reason=ReflectionTargetMissing");
                return;
            }

            try
            {
                MissionLobbyComponentSetStatePlayingAsServer.Invoke(lobbyComponent, null);
                LogLobbyPromotion(
                    mission,
                    currentPhase,
                    "OfficialBattleRuntimeQuiescePatch: promoted MissionLobbyComponent to Playing after coop battle ownership handoff.");
            }
            catch (Exception ex)
            {
                LogLobbyPromotion(
                    mission,
                    currentPhase,
                    "OfficialBattleRuntimeQuiescePatch: failed to promote MissionLobbyComponent to Playing. " +
                    ex.GetType().Name +
                    ": " +
                    ex.Message);
            }
        }

        private static void LogSuppression(ref string lastLogKey, Mission mission, string messagePrefix, string reason)
        {
            string key =
                (mission?.SceneName ?? "null") + "|" +
                CoopBattlePhaseRuntimeState.GetPhase() + "|" +
                (reason ?? string.Empty);
            if (string.Equals(lastLogKey, key, StringComparison.Ordinal))
                return;

            lastLogKey = key;
            ModLogger.Info(messagePrefix + (reason ?? string.Empty));
        }

        private static void LogPassThrough(ref string lastLogKey, Mission mission, string messagePrefix, string reason)
        {
            string key =
                (mission?.SceneName ?? "null") + "|" +
                CoopBattlePhaseRuntimeState.GetPhase() + "|" +
                (reason ?? string.Empty);
            if (string.Equals(lastLogKey, key, StringComparison.Ordinal))
                return;

            lastLogKey = key;
            ModLogger.Info(messagePrefix + (reason ?? string.Empty));
        }

        private static void LogLobbyPromotion(Mission mission, CoopBattlePhase phase, string message)
        {
            string key = (mission?.SceneName ?? "null") + "|" + phase + "|" + message;
            if (string.Equals(_lastLobbyPromotionLogKey, key, StringComparison.Ordinal))
                return;

            _lastLobbyPromotionLogKey = key;
            ModLogger.Info(message);
        }
    }
}
