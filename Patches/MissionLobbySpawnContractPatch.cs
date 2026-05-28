using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal static class MissionLobbySpawnContractPatch
    {
        private static readonly MethodInfo SetStatePlayingAsServerMethod = typeof(MissionLobbyComponent).GetMethod(
            "SetStatePlayingAsServer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo SetStateEndingAsClientMethod = typeof(MissionLobbyComponent).GetMethod(
            "SetStateEndingAsClient",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo CurrentMultiplayerStateSetterMethod = typeof(MissionLobbyComponent)
            .GetProperty(
                nameof(MissionLobbyComponent.CurrentMultiplayerState),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetSetMethod(nonPublic: true);

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo respawnTarget = typeof(MissionLobbyComponent).GetMethod(
                    nameof(MissionLobbyComponent.GetSpawnPeriodDurationForPeer),
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(MissionPeer) },
                    modifiers: null);
                MethodInfo respawnPrefix = typeof(MissionLobbySpawnContractPatch).GetMethod(
                    nameof(GetSpawnPeriodDurationForPeer_Prefix),
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (respawnTarget == null || respawnPrefix == null)
                {
                    ModLogger.Info("MissionLobbySpawnContractPatch: respawn-period target or prefix not found. Skip.");
                    return;
                }

                MethodInfo tickTarget = typeof(MissionLobbyComponent).GetMethod(
                    nameof(MissionLobbyComponent.OnMissionTick),
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(float) },
                    modifiers: null);
                MethodInfo tickPrefix = typeof(MissionLobbySpawnContractPatch).GetMethod(
                    nameof(OnMissionTick_Prefix),
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (tickTarget == null || tickPrefix == null)
                {
                    ModLogger.Info("MissionLobbySpawnContractPatch: mission-tick target or prefix not found. Skip.");
                    return;
                }

                MethodInfo stateChangeTarget = typeof(MissionLobbyComponent).GetMethod(
                    "HandleServerEventMissionStateChange",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo stateChangePrefix = typeof(MissionLobbySpawnContractPatch).GetMethod(
                    nameof(HandleServerEventMissionStateChange_Prefix),
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (stateChangeTarget == null || stateChangePrefix == null)
                {
                    ModLogger.Info("MissionLobbySpawnContractPatch: mission-state-change target or prefix not found. Skip.");
                    return;
                }

                MethodInfo clientSynchronizedTarget = typeof(MissionLobbyComponent).GetMethod(
                    "OnMyClientSynchronized",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo clientSynchronizedPrefix = typeof(MissionLobbySpawnContractPatch).GetMethod(
                    nameof(OnMyClientSynchronized_Prefix),
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (clientSynchronizedTarget == null || clientSynchronizedPrefix == null)
                {
                    ModLogger.Info("MissionLobbySpawnContractPatch: client-synchronized target or prefix not found. Skip.");
                    return;
                }

                harmony.Patch(respawnTarget, prefix: new HarmonyMethod(respawnPrefix));
                harmony.Patch(tickTarget, prefix: new HarmonyMethod(tickPrefix));
                harmony.Patch(stateChangeTarget, prefix: new HarmonyMethod(stateChangePrefix));
                harmony.Patch(clientSynchronizedTarget, prefix: new HarmonyMethod(clientSynchronizedPrefix));
                ModLogger.Info("MissionLobbySpawnContractPatch: patched MissionLobbyComponent.GetSpawnPeriodDurationForPeer.");
                ModLogger.Info("MissionLobbySpawnContractPatch: patched MissionLobbyComponent.OnMissionTick.");
                ModLogger.Info("MissionLobbySpawnContractPatch: patched MissionLobbyComponent.HandleServerEventMissionStateChange.");
                ModLogger.Info("MissionLobbySpawnContractPatch: patched MissionLobbyComponent.OnMyClientSynchronized.");
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionLobbySpawnContractPatch.Apply failed: " + ex.Message);
            }
        }

        private static bool GetSpawnPeriodDurationForPeer_Prefix(MissionPeer peer, ref int __result)
        {
            try
            {
                Mission mission = Mission.Current;
                if (!ShouldUseListedShellLobbyContract(mission))
                    return true;

                __result = ResolveListedShellRespawnPeriodForPeer(mission, peer);
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionLobbySpawnContractPatch: prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool OnMissionTick_Prefix(MissionLobbyComponent __instance, float dt)
        {
            try
            {
                Mission mission = __instance?.Mission ?? Mission.Current;
                if (!ShouldUseListedShellLobbyContract(mission))
                    return true;

                if (!GameNetwork.IsServerOrRecorder)
                    return true;

                if (__instance.CurrentMultiplayerState == MissionLobbyComponent.MultiplayerGameState.WaitingFirstPlayers)
                    return HandleWaitingFirstPlayersState(__instance, mission);

                if (__instance.CurrentMultiplayerState != MissionLobbyComponent.MultiplayerGameState.Playing)
                    return true;

                MissionMultiplayerGameModeBase gameMode = mission.GetMissionBehavior<MissionMultiplayerGameModeBase>();
                MultiplayerTimerComponent timer = mission.GetMissionBehavior<MultiplayerTimerComponent>();
                if (gameMode == null || timer == null || gameMode.RoundController != null)
                    return true;

                bool timerPassed = timer.CheckIfTimerPassed();
                if (!timerPassed && !gameMode.CheckForMatchEnd())
                    return true;

                gameMode.GetWinnerTeam();
                SetRemainingAgentsInvulnerable(mission);
                ClearListedShellSpawnCompatibilityState();
                __instance.SetStateEndingAsServer();
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionLobbySpawnContractPatch: mission-tick prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool HandleServerEventMissionStateChange_Prefix(MissionLobbyComponent __instance, object baseMessage)
        {
            try
            {
                Mission mission = __instance?.Mission ?? Mission.Current;
                if (!ShouldUseListedShellLobbyContract(mission))
                    return true;

                if (!GameNetwork.IsClient)
                    return true;

                object currentStateValue = baseMessage?.GetType()
                    .GetProperty("CurrentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .GetValue(baseMessage);
                object stateStartTimeValue = baseMessage?.GetType()
                    .GetProperty("StateStartTimeInSeconds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .GetValue(baseMessage);
                if (!(currentStateValue is MissionLobbyComponent.MultiplayerGameState currentState) ||
                    !(stateStartTimeValue is long stateStartTimeInSeconds))
                {
                    return true;
                }

                CurrentMultiplayerStateSetterMethod?.Invoke(__instance, new object[] { currentState });
                if (currentState != MissionLobbyComponent.MultiplayerGameState.WaitingFirstPlayers)
                {
                    if (currentState == MissionLobbyComponent.MultiplayerGameState.Playing)
                    {
                        MultiplayerWarmupComponent warmup = mission?.GetMissionBehavior<MultiplayerWarmupComponent>();
                        if (warmup != null)
                            mission.RemoveMissionBehavior(warmup);
                    }

                    MultiplayerTimerComponent timer = mission?.GetMissionBehavior<MultiplayerTimerComponent>();
                    if (timer != null)
                    {
                        float duration = currentState == MissionLobbyComponent.MultiplayerGameState.Playing
                            ? MultiplayerOptions.OptionType.MapTimeLimit.GetIntValue() * 60f
                            : MissionLobbyComponent.PostMatchWaitDuration;
                        timer.StartTimerAsClient(stateStartTimeInSeconds, duration);
                    }
                }

                if (currentState == MissionLobbyComponent.MultiplayerGameState.Ending)
                    SetStateEndingAsClientMethod?.Invoke(__instance, Array.Empty<object>());

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionLobbySpawnContractPatch: mission-state-change prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool OnMyClientSynchronized_Prefix(MissionLobbyComponent __instance)
        {
            try
            {
                Mission mission = __instance?.Mission ?? Mission.Current;
                if (!ShouldUseListedShellLobbyContract(mission))
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionLobbySpawnContractPatch: client-synchronized prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool HandleWaitingFirstPlayersState(MissionLobbyComponent lobbyComponent, Mission mission)
        {
            if (!GameNetwork.IsServer)
                return true;

            MultiplayerTimerComponent timer = mission?.GetMissionBehavior<MultiplayerTimerComponent>();
            if (timer == null || SetStatePlayingAsServerMethod == null)
                return true;

            MultiplayerWarmupComponent warmup = mission.GetMissionBehavior<MultiplayerWarmupComponent>();
            if (warmup != null && warmup.IsInWarmup)
                return false;

            if (!timer.CheckIfTimerPassed())
                return false;

            int synchronizedPeerCount = CountSynchronizedPeers();
            int configuredBotCount =
                MultiplayerOptions.OptionType.NumberOfBotsTeam1.GetIntValue() +
                MultiplayerOptions.OptionType.NumberOfBotsTeam2.GetIntValue();
            int minPlayersToStart = MultiplayerOptions.OptionType.MinNumberOfPlayersForMatchStart.GetIntValue();
            bool shouldStart =
                synchronizedPeerCount + configuredBotCount >= minPlayersToStart ||
                MBCommon.CurrentGameType == MBCommon.GameType.MultiClientServer;
            if (!shouldStart)
                return false;

            SetStatePlayingAsServerMethod.Invoke(lobbyComponent, Array.Empty<object>());
            ModLogger.Info(
                "MissionLobbySpawnContractPatch: advanced listed-shell lobby from WaitingFirstPlayers to Playing via explicit coop-owned lobby contract. " +
                "Peers=" + synchronizedPeerCount +
                " Bots=" + configuredBotCount +
                " MinPlayers=" + minPlayersToStart +
                " Mission=" + (mission?.SceneName ?? "unknown"));
            return false;
        }

        private static int CountSynchronizedPeers()
        {
            if (GameNetwork.NetworkPeers == null)
                return 0;

            int count = 0;
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer != null && peer.IsSynchronized)
                    count++;
            }

            return count;
        }

        private static int ResolveListedShellRespawnPeriodForPeer(Mission mission, MissionPeer peer)
        {
            MissionMultiplayerGameModeBase gameMode = mission?.GetMissionBehavior<MissionMultiplayerGameModeBase>();
            if (gameMode?.WarmupComponent != null && gameMode.WarmupComponent.IsInWarmup)
                return 3;

            BattleSideEnum authoritativeSide = CoopBattleAuthorityState.GetAssignedSide(peer);
            if (authoritativeSide == BattleSideEnum.Attacker)
                return MultiplayerOptions.OptionType.RespawnPeriodTeam2.GetIntValue();

            if (authoritativeSide == BattleSideEnum.Defender)
                return MultiplayerOptions.OptionType.RespawnPeriodTeam1.GetIntValue();

            return -1;
        }

        private static void SetRemainingAgentsInvulnerable(Mission mission)
        {
            if (mission?.Agents == null)
                return;

            foreach (Agent agent in mission.Agents)
                agent.SetMortalityState(Agent.MortalityState.Invulnerable);
        }

        private static void ClearListedShellSpawnCompatibilityState()
        {
            ClearListedShellSpawnCompatibilityState(GameNetwork.NetworkPeers);
            ClearListedShellSpawnCompatibilityState(GameNetwork.DisconnectedNetworkPeers);
        }

        private static void ClearListedShellSpawnCompatibilityState(IEnumerable<NetworkCommunicator> peers)
        {
            if (peers == null)
                return;

            foreach (NetworkCommunicator peer in peers)
            {
                MissionPeer missionPeer = peer?.GetComponent<MissionPeer>();
                if (missionPeer == null)
                    continue;

                missionPeer.WantsToSpawnAsBot = false;
            }
        }

        private static bool ShouldUseListedShellLobbyContract(Mission mission)
        {
            if (mission == null)
                return false;

            return mission.GetMissionBehavior<ListedShellCompatibilityMode>() != null ||
                mission.GetMissionBehavior<ListedShellCompatibilityModeClient>() != null;
        }
    }
}
