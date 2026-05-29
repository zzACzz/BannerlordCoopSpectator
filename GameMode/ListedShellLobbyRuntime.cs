using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Patches;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.GameMode
{
    internal static class ListedShellLobbyRuntime
    {
        private sealed class ListedShellMissionStateHolder
        {
            public MissionLobbyComponent.MultiplayerGameState State;
        }

        private static readonly FieldInfo NetworkFromServerBaseHandlersField =
            AccessTools.Field(typeof(GameNetwork.NetworkMessageHandlerRegistererContainer), "_fromServerBaseHandlers");
        private static readonly Type MissionStateChangeType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.MissionStateChange");
        private static readonly MethodInfo GameNetworkWriteMessageMethod = ResolveGameNetworkWriteMessageMethod();
        private static readonly ConditionalWeakTable<Mission, ListedShellMissionStateHolder> ListedShellMissionStateByMission =
            new ConditionalWeakTable<Mission, ListedShellMissionStateHolder>();
        private const int MaxBotsControlledCountForNetworkContract = 255;

        internal static bool TryResolveMissionLobbyState(
            Mission mission,
            out MissionLobbyComponent.MultiplayerGameState state)
        {
            if (mission != null && ShouldUseListedShellLobbyContract(mission))
            {
                if (!ListedShellMissionStateByMission.TryGetValue(mission, out ListedShellMissionStateHolder holder))
                {
                    holder = ListedShellMissionStateByMission.GetOrCreateValue(mission);
                    holder.State = MissionLobbyComponent.MultiplayerGameState.WaitingFirstPlayers;
                }

                state = holder.State;
                return true;
            }

            MissionLobbyComponent lobbyComponent = mission?.GetMissionBehavior<MissionLobbyComponent>();
            if (lobbyComponent != null)
            {
                state = lobbyComponent.CurrentMultiplayerState;
                return true;
            }

            state = default;
            return false;
        }

        internal static bool IsMissionLobbyState(
            Mission mission,
            MissionLobbyComponent.MultiplayerGameState expectedState)
        {
            return TryResolveMissionLobbyState(mission, out MissionLobbyComponent.MultiplayerGameState currentState) &&
                currentState == expectedState;
        }

        internal static bool TryRegisterListedShellMissionStateHandler(
            GameNetwork.NetworkMessageHandlerRegistererContainer registerer,
            GameNetworkMessage.ServerMessageHandlerDelegate<GameNetworkMessage> handler)
        {
            return TryRegisterBaseServerHandler(registerer, MissionStateChangeType, handler);
        }

        internal static int ResolveAuthoritativeRespawnPeriodForPeer(Mission mission, MissionPeer peer)
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

        internal static void HandleListedShellMissionTick(MissionLobbyComponent lobbyComponent, float dt)
        {
            OnListedShellMissionTick(lobbyComponent, dt);
        }

        internal static void HandleListedShellAgentRemoved(
            MissionLobbyComponent lobbyComponent,
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            KillingBlow killingBlow)
        {
            OnListedShellAgentRemoved(
                lobbyComponent,
                affectedAgent,
                affectorAgent,
                agentState,
                killingBlow);
        }

        internal static void HandleListedShellLateNewClientAfterLoadingFinished(
            MissionLobbyComponent lobbyComponent,
            NetworkCommunicator networkPeer)
        {
            try
            {
                Mission mission = lobbyComponent?.Mission ?? Mission.Current;
                if (!ShouldUseListedShellLobbyContract(mission))
                    return;

                if (!GameNetwork.IsServer || networkPeer == null || networkPeer.IsServerPeer)
                    return;

                SendListedShellLateJoinBootstrapToPeer(lobbyComponent, mission, networkPeer);
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyRuntime: late-new-client bootstrap failed: " + ex.Message);
            }
        }

        internal static void HandleListedShellUdpNetworkHandlerTick(MissionLobbyComponent lobbyComponent)
        {
            try
            {
                Mission mission = lobbyComponent?.Mission ?? Mission.Current;
                if (!ShouldUseListedShellLobbyContract(mission))
                    return;

                if (!GameNetwork.IsServer)
                    return;

                if (!TryResolveMissionLobbyState(mission, out MissionLobbyComponent.MultiplayerGameState listedState) ||
                    listedState != MissionLobbyComponent.MultiplayerGameState.Ending)
                {
                    return;
                }

                MultiplayerTimerComponent timer = mission?.GetMissionBehavior<MultiplayerTimerComponent>();
                if (timer != null && timer.CheckIfTimerPassed())
                    EndListedShellMissionAsServer();
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyRuntime: udp-network-handler-tick failed: " + ex.Message);
            }
        }

        internal static void InitializeListedShellLobbyState(Mission mission)
        {
            try
            {
                if (!ShouldUseListedShellLobbyContract(mission))
                    return;

                CoopBattlePeerStatsRuntimeState.Reset();
                CoopBattleScoreboardRuntimeState.Reset(mission);
                CoopBattleScoreboardRuntimeState.InitializeMission(
                    mission,
                    "ListedShellLobbyRuntime.InitializeListedShellLobbyState");
                ListedShellMissionSessionState.InitializeMission(
                    mission,
                    "ListedShellLobbyRuntime.InitializeListedShellLobbyState");
                RememberListedShellMissionState(mission, MissionLobbyComponent.MultiplayerGameState.WaitingFirstPlayers);
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyRuntime: behavior-initialize failed open: " + ex.Message);
            }
        }

        internal static bool TryHandleListedShellScoreHit(
            MissionScoreboardComponent scoreboardComponent,
            Agent affectedAgent,
            Agent affectorAgent,
            bool isBlocked,
            float damagedHp)
        {
            Mission mission = scoreboardComponent?.Mission ?? Mission.Current;
            if (!ShouldUseListedShellLobbyContract(mission))
                return false;

            if (affectorAgent == null || !GameNetwork.IsServer || isBlocked || !(damagedHp > 0f))
                return true;

            if (affectorAgent.IsMount)
                affectorAgent = affectorAgent.RiderAgent;

            if (affectorAgent == null)
                return true;

            MissionPeer affectorPeer = affectorAgent.MissionPeer ??
                (affectorAgent.IsAIControlled ? affectorAgent.OwningAgentMissionPeer : null);
            if (affectorPeer == null)
                return true;

            int scoreDelta = (int)damagedHp;
            if (affectedAgent?.IsMount == true)
            {
                scoreDelta = (int)(damagedHp * 0.35f);
                affectedAgent = affectedAgent.RiderAgent;
            }

            if (affectedAgent == null || ReferenceEquals(affectorAgent, affectedAgent))
                return true;

            if (!affectorAgent.IsFriendOf(affectedAgent))
            {
                AdjustListedShellPeerStats(
                    affectorPeer,
                    0,
                    0,
                    0,
                    scoreDelta,
                    "ListedShellLobbyRuntime.TryHandleListedShellScoreHit enemy");
            }
            else
            {
                AdjustListedShellPeerStats(
                    affectorPeer,
                    0,
                    0,
                    0,
                    -(int)(scoreDelta * 1.5f),
                    "ListedShellLobbyRuntime.TryHandleListedShellScoreHit friendly");
            }

            PeerStatsRuntimeState statsState = ResolveListedShellPeerStats(affectorPeer);
            ListedShellMissionScoreboardComponent.NotifyListedShellPlayerPropertiesChanged(
                scoreboardComponent,
                affectorPeer.GetNetworkPeer());
            BroadcastKillDeathCountChange(
                affectorPeer.GetNetworkPeer(),
                null,
                statsState.KillCount,
                statsState.AssistCount,
                statsState.DeathCount,
                statsState.Score);
            return true;
        }

        private static void OnListedShellMissionTick(MissionLobbyComponent __instance, float dt)
        {
            try
            {
                Mission mission = __instance?.Mission ?? Mission.Current;
                if (!ShouldUseListedShellLobbyContract(mission))
                    return;

                if (GameNetwork.IsClient)
                    return;

                if (!GameNetwork.IsServerOrRecorder)
                    return;

                if (!TryResolveMissionLobbyState(mission, out MissionLobbyComponent.MultiplayerGameState listedState))
                    return;

                if (listedState == MissionLobbyComponent.MultiplayerGameState.WaitingFirstPlayers)
                {
                    HandleWaitingFirstPlayersState(__instance, mission);
                    return;
                }

                if (listedState != MissionLobbyComponent.MultiplayerGameState.Playing)
                    return;

                MissionMultiplayerGameModeBase gameMode = mission.GetMissionBehavior<MissionMultiplayerGameModeBase>();
                MultiplayerTimerComponent timer = mission.GetMissionBehavior<MultiplayerTimerComponent>();
                if (gameMode == null || timer == null || gameMode.RoundController != null)
                    return;

                bool timerPassed = timer.CheckIfTimerPassed();
                if (!timerPassed && !gameMode.CheckForMatchEnd())
                    return;

                gameMode.GetWinnerTeam();
                SetRemainingAgentsInvulnerable(mission);
                ClearListedShellSpawnCompatibilityState();
                __instance.SetStateEndingAsServer();
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyRuntime: mission-tick failed: " + ex.Message);
            }
        }

        internal static bool TryApplyListedShellMissionStateChange(
            Mission mission,
            object baseMessage,
            string source)
        {
            try
            {
                mission = mission ?? Mission.Current;
                if (!ShouldUseListedShellLobbyContract(mission))
                    return false;

                if (!GameNetwork.IsClient)
                    return false;

                object currentStateValue = baseMessage?.GetType()
                    .GetProperty("CurrentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .GetValue(baseMessage);
                object stateStartTimeValue = baseMessage?.GetType()
                    .GetProperty("StateStartTimeInSeconds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .GetValue(baseMessage);
                if (!(currentStateValue is MissionLobbyComponent.MultiplayerGameState currentState) ||
                    !(stateStartTimeValue is long stateStartTimeInSeconds))
                {
                    return false;
                }

                RememberListedShellMissionState(mission, currentState);
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

                ModLogger.Info(
                    "ListedShellLobbyRuntime: applied listed-shell mission state through coop-owned handler. " +
                    "Source=" + (source ?? "unknown") +
                    " Mission=" + (mission?.SceneName ?? "unknown") +
                    " State=" + currentState +
                    " StateStartTimeInSeconds=" + stateStartTimeInSeconds);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ListedShellLobbyRuntime: listed-shell mission-state apply failed open. " +
                    "Source=" + (source ?? "unknown") +
                    " Error=" + ex.Message);
                return false;
            }
        }

        internal static bool TryApplyListedShellKillDeathCountChange(
            GameNetworkMessage baseMessage,
            MissionScoreboardComponent scoreboardComponent)
        {
            NetworkMessages.FromServer.KillDeathCountChange killDeathCountChange =
                baseMessage as NetworkMessages.FromServer.KillDeathCountChange;
            if (killDeathCountChange?.VictimPeer == null)
                return false;

            MissionPeer victimPeer = killDeathCountChange.VictimPeer.GetComponent<MissionPeer>();
            MissionPeer attackerPeer = killDeathCountChange.AttackerPeer?.GetComponent<MissionPeer>();
            if (victimPeer != null)
            {
                ApplyListedShellPeerStats(
                    victimPeer,
                    killDeathCountChange.KillCount,
                    killDeathCountChange.AssistCount,
                    killDeathCountChange.DeathCount,
                    killDeathCountChange.Score,
                    "ListedShellLobbyRuntime.TryApplyListedShellKillDeathCountChange");
                attackerPeer?.OnKillAnotherPeer(victimPeer);
                if (killDeathCountChange.KillCount == 0 &&
                    killDeathCountChange.AssistCount == 0 &&
                    killDeathCountChange.DeathCount == 0 &&
                    killDeathCountChange.Score == 0)
                {
                    victimPeer.ResetKillRegistry();
                }
            }

            ListedShellMissionScoreboardComponent.NotifyListedShellPlayerPropertiesChanged(
                scoreboardComponent ?? Mission.Current?.GetMissionBehavior<MissionScoreboardComponent>(),
                killDeathCountChange.VictimPeer);
            return true;
        }

        private static void OnListedShellAgentRemoved(
            MissionLobbyComponent __instance,
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            KillingBlow killingBlow)
        {
            try
            {
                Mission mission = __instance?.Mission ?? Mission.Current;
                if (!ShouldUseListedShellLobbyContract(mission))
                    return;

                if (!TryResolveMissionLobbyState(mission, out MissionLobbyComponent.MultiplayerGameState listedState))
                    return;

                if (!GameNetwork.IsServer ||
                    listedState == MissionLobbyComponent.MultiplayerGameState.Ending ||
                    affectedAgent == null ||
                    !affectedAgent.IsHuman ||
                    affectedAgent.IsMount)
                {
                    return;
                }

                if (agentState != AgentState.Killed &&
                    agentState != AgentState.Unconscious &&
                    agentState != AgentState.Routed)
                {
                    return;
                }

                if (affectedAgent.MissionPeer != null)
                {
                    HandleListedShellPlayerDeath(
                        __instance,
                        mission,
                        affectedAgent,
                        affectorAgent,
                        "ListedShellLobbyRuntime.OnAgentRemoved");
                    return;
                }

                HandleListedShellBotDeath(
                    __instance,
                    mission,
                    affectedAgent,
                    affectorAgent,
                    "ListedShellLobbyRuntime.OnAgentRemoved");
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyRuntime: agent-removed failed: " + ex.Message);
            }
        }

        private static void HandleWaitingFirstPlayersState(MissionLobbyComponent lobbyComponent, Mission mission)
        {
            if (!GameNetwork.IsServer)
                return;

            MultiplayerTimerComponent timer = mission?.GetMissionBehavior<MultiplayerTimerComponent>();
            if (timer == null)
                return;

            MultiplayerWarmupComponent warmup = mission.GetMissionBehavior<MultiplayerWarmupComponent>();
            if (warmup != null && warmup.IsInWarmup)
                return;

            if (!timer.CheckIfTimerPassed())
                return;

            int synchronizedPeerCount = CountSynchronizedPeers();
            int configuredBotCount =
                MultiplayerOptions.OptionType.NumberOfBotsTeam1.GetIntValue() +
                MultiplayerOptions.OptionType.NumberOfBotsTeam2.GetIntValue();
            int minPlayersToStart = MultiplayerOptions.OptionType.MinNumberOfPlayersForMatchStart.GetIntValue();
            bool shouldStart =
                synchronizedPeerCount + configuredBotCount >= minPlayersToStart ||
                MBCommon.CurrentGameType == MBCommon.GameType.MultiClientServer;
            if (!shouldStart)
                return;

            SetListedShellStatePlayingAsServer(mission, timer);
            ModLogger.Info(
                "ListedShellLobbyRuntime: advanced listed-shell lobby from WaitingFirstPlayers to Playing via explicit coop-owned lobby contract. " +
                "Peers=" + synchronizedPeerCount +
                " Bots=" + configuredBotCount +
                " MinPlayers=" + minPlayersToStart +
                " Mission=" + (mission?.SceneName ?? "unknown"));
        }

        private static void SetListedShellStatePlayingAsServer(Mission mission, MultiplayerTimerComponent timer)
        {
            MultiplayerWarmupComponent warmup = mission?.GetMissionBehavior<MultiplayerWarmupComponent>();
            if (warmup != null)
                mission.RemoveMissionBehavior(warmup);

            RememberListedShellMissionState(mission, MissionLobbyComponent.MultiplayerGameState.Playing);
            timer.StartTimerAsServer(MultiplayerOptions.OptionType.MapTimeLimit.GetIntValue() * 60f);
            BroadcastMissionStateChange(
                MissionLobbyComponent.MultiplayerGameState.Playing,
                timer.GetCurrentTimerStartTime().NumberOfTicks);
        }

        internal static void SetListedShellStateEndingAsServer(MissionLobbyComponent lobbyComponent)
        {
            MultiplayerTimerComponent timer = lobbyComponent?.Mission?.GetMissionBehavior<MultiplayerTimerComponent>();
            if (lobbyComponent == null || timer == null)
                return;

            RememberListedShellMissionState(lobbyComponent?.Mission, MissionLobbyComponent.MultiplayerGameState.Ending);
            timer.StartTimerAsServer(MissionLobbyComponent.PostMatchWaitDuration);
            BroadcastMissionStateChange(
                MissionLobbyComponent.MultiplayerGameState.Ending,
                timer.GetCurrentTimerStartTime().NumberOfTicks);
        }

        internal static void EndListedShellMissionAsServer()
        {
            if (!GameNetwork.IsServer)
                return;

            CoopSessionTransportPrimitives.BroadcastUnloadMission();
            CoopSessionTransportPrimitives.EndServerLobbyMissionAfterUnloadBroadcast();
        }

        private static void BroadcastMissionStateChange(
            MissionLobbyComponent.MultiplayerGameState state,
            long stateStartTimeInTicks)
        {
            if (MissionStateChangeType == null || GameNetworkWriteMessageMethod == null)
                return;

            object message = Activator.CreateInstance(MissionStateChangeType, state, stateStartTimeInTicks);
            if (message == null)
                return;

            CoopSessionTransportPrimitives.BroadcastReflectedServerMessage(GameNetworkWriteMessageMethod, message);
        }

        private static void SendMissionStateChangeToPeer(
            NetworkCommunicator peer,
            MissionLobbyComponent.MultiplayerGameState state,
            long stateStartTimeInTicks)
        {
            if (peer == null || MissionStateChangeType == null || GameNetworkWriteMessageMethod == null)
                return;

            object message = Activator.CreateInstance(MissionStateChangeType, state, stateStartTimeInTicks);
            if (message == null)
                return;

            CoopSessionTransportPrimitives.SendReflectedServerMessage(peer, GameNetworkWriteMessageMethod, message);
        }

        private static MethodInfo ResolveGameNetworkWriteMessageMethod()
        {
            MethodInfo[] methods = typeof(GameNetwork).GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method?.Name != "WriteMessage")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1)
                    return method;
            }

            return null;
        }

        private static void RememberListedShellMissionState(
            Mission mission,
            MissionLobbyComponent.MultiplayerGameState state)
        {
            if (mission == null || !ShouldUseListedShellLobbyContract(mission))
                return;

            ListedShellMissionStateHolder holder = ListedShellMissionStateByMission.GetOrCreateValue(mission);
            holder.State = state;
        }

        private static void SendListedShellLateJoinBootstrapToPeer(
            MissionLobbyComponent lobbyComponent,
            Mission mission,
            NetworkCommunicator targetPeer)
        {
            if (targetPeer == null)
                return;

            if (!TryResolveMissionLobbyState(mission, out MissionLobbyComponent.MultiplayerGameState state))
                state = MissionLobbyComponent.MultiplayerGameState.WaitingFirstPlayers;

            long stateStartTimeInTicks = ResolveListedShellMissionStateStartTimeInTicks(lobbyComponent, mission, state);
            SendMissionStateChangeToPeer(targetPeer, state, stateStartTimeInTicks);
            SendListedShellPeerInformationsToPeer(mission, targetPeer);
            SendListedShellScoreboardStateToPeer(mission, targetPeer);
        }

        private static long ResolveListedShellMissionStateStartTimeInTicks(
            MissionLobbyComponent lobbyComponent,
            Mission mission,
            MissionLobbyComponent.MultiplayerGameState state)
        {
            if (state == MissionLobbyComponent.MultiplayerGameState.WaitingFirstPlayers)
                return 0L;

            MultiplayerTimerComponent timer =
                mission?.GetMissionBehavior<MultiplayerTimerComponent>() ??
                lobbyComponent?.Mission?.GetMissionBehavior<MultiplayerTimerComponent>();
            return timer?.GetCurrentTimerStartTime().NumberOfTicks ?? 0L;
        }

        private static void SendListedShellPeerInformationsToPeer(Mission mission, NetworkCommunicator targetPeer)
        {
            IEnumerable<NetworkCommunicator> peers = GameNetwork.NetworkPeersIncludingDisconnectedPeers;
            if (peers == null)
                return;

            int replayedKillDeathCount = 0;
            int replayedBotsControlledCount = 0;
            foreach (NetworkCommunicator subjectPeer in peers)
            {
                if (!ShouldReplayListedShellPeerInformationSubject(subjectPeer))
                    continue;

                MissionPeer missionPeer = subjectPeer.GetComponent<MissionPeer>();
                if (missionPeer == null)
                    continue;

                PeerStatsRuntimeState statsState = ResolveListedShellPeerStats(missionPeer);
                int replayedDeathCount = ResolveListedShellReplayDeathCount(missionPeer);
                CoopSessionTransportPrimitives.SendServerMessage(targetPeer, new NetworkMessages.FromServer.KillDeathCountChange(
                    missionPeer.GetNetworkPeer(),
                    null,
                    statsState.KillCount,
                    statsState.AssistCount,
                    replayedDeathCount,
                    statsState.Score));
                replayedKillDeathCount++;

                if (missionPeer.BotsUnderControlAlive == 0 && missionPeer.BotsUnderControlTotal == 0)
                    continue;

                CoopSessionTransportPrimitives.SendServerMessage(targetPeer, new NetworkMessages.FromServer.BotsControlledChange(
                    missionPeer.GetNetworkPeer(),
                    missionPeer.BotsUnderControlAlive,
                    missionPeer.BotsUnderControlTotal));
                replayedBotsControlledCount++;
            }

            ModLogger.Info(
                "ListedShellLobbyRuntime: replayed listed-shell peer info through coop-owned late-client contract. " +
                "Peer=" + (targetPeer.UserName ?? targetPeer.Index.ToString()) +
                " Scene=" + (mission?.SceneName ?? "unknown") +
                " KillDeathReplays=" + replayedKillDeathCount +
                " BotsControlledReplays=" + replayedBotsControlledCount);
        }

        private static void SendListedShellScoreboardStateToPeer(Mission mission, NetworkCommunicator targetPeer)
        {
            if (mission == null || targetPeer == null)
                return;

            MissionScoreboardComponent scoreboard = mission.GetMissionBehavior<MissionScoreboardComponent>();
            bool hasAttackerState = ListedShellMissionScoreboardComponent.TryResolveListedShellSideRuntimeState(
                scoreboard,
                BattleSideEnum.Attacker,
                "ListedShellLobbyRuntime.SendListedShellScoreboardStateToPeer attacker",
                out ScoreboardSideRuntimeState attackerState);
            bool hasDefenderState = ListedShellMissionScoreboardComponent.TryResolveListedShellSideRuntimeState(
                scoreboard,
                BattleSideEnum.Defender,
                "ListedShellLobbyRuntime.SendListedShellScoreboardStateToPeer defender",
                out ScoreboardSideRuntimeState defenderState);
            if (!hasAttackerState && !hasDefenderState)
                return;

            if (hasAttackerState || hasDefenderState)
            {
                CoopSessionTransportPrimitives.SendServerMessage(targetPeer, new NetworkMessages.FromServer.UpdateRoundScores(
                    hasAttackerState ? attackerState.SideScore : 0,
                    hasDefenderState ? defenderState.SideScore : 0));
            }

            if (hasAttackerState)
                SendListedShellBotDataToPeer(targetPeer, attackerState);

            if (hasDefenderState)
                SendListedShellBotDataToPeer(targetPeer, defenderState);

            ModLogger.Info(
                "ListedShellLobbyRuntime: replayed listed-shell scoreboard state through coop-owned late-client contract. " +
                "Peer=" + (targetPeer.UserName ?? targetPeer.Index.ToString()) +
                " Scene=" + (mission.SceneName ?? "unknown") +
                " AttackerScore=" + (hasAttackerState ? attackerState.SideScore : 0) +
                " DefenderScore=" + (hasDefenderState ? defenderState.SideScore : 0));
        }

        private static bool ShouldReplayListedShellPeerInformationSubject(NetworkCommunicator peer)
        {
            if (peer == null)
                return false;

            VirtualPlayer virtualPlayer = peer.VirtualPlayer;
            bool isDisconnectedPeer =
                virtualPlayer != null &&
                virtualPlayer.Index >= 0 &&
                virtualPlayer.Index < GameNetwork.VirtualPlayers.Length &&
                !ReferenceEquals(virtualPlayer, GameNetwork.VirtualPlayers[virtualPlayer.Index]);
            return isDisconnectedPeer || peer.IsSynchronized || peer.JustReconnecting;
        }

        private static int ResolveListedShellReplayDeathCount(MissionPeer missionPeer)
        {
            if (missionPeer == null)
                return 0;

            PeerStatsRuntimeState statsState = ResolveListedShellPeerStats(missionPeer);
            if (!CoopBattlePeerLifecycleRuntimeState.TryGetState(missionPeer, out PeerLifecycleRuntimeState lifecycleState))
                return statsState.DeathCount;

            return Math.Max(statsState.DeathCount, lifecycleState.DeathCount);
        }

        private static void HandleListedShellPlayerDeath(
            MissionLobbyComponent lobbyComponent,
            Mission mission,
            Agent affectedAgent,
            Agent affectorAgent,
            string source)
        {
            MissionPeer victimPeer = affectedAgent?.MissionPeer;
            if (lobbyComponent == null || mission == null || victimPeer == null)
                return;

            MissionPeer directKillerPeer = affectorAgent?.MissionPeer;
            MissionPeer affectorPeer = directKillerPeer ?? affectorAgent?.OwningAgentMissionPeer;
            MissionPeer assistorPeer = TryResolveAssistorPeer(directKillerPeer, affectedAgent);

            CoopMissionSpawnLogic.TryHandleListedShellPlayerDeathTransition(
                mission,
                affectedAgent,
                source);

            if (assistorPeer != null)
            {
                PeerStatsRuntimeState assistorStats = ResolveListedShellPeerStats(assistorPeer);
                BroadcastKillDeathCountChange(
                    assistorPeer.GetNetworkPeer(),
                    affectorPeer?.GetNetworkPeer(),
                    assistorStats.KillCount,
                    assistorStats.AssistCount,
                    assistorStats.DeathCount,
                    assistorStats.Score);
            }

            AdjustListedShellPeerStats(victimPeer, 0, 0, 1, 0, "ListedShellLobbyRuntime.HandleListedShellPlayerDeath victim");
            PeerStatsRuntimeState victimStats = ResolveListedShellPeerStats(victimPeer);
            ListedShellMissionScoreboardComponent.NotifyListedShellPlayerPropertiesChanged(
                mission.GetMissionBehavior<MissionScoreboardComponent>(),
                victimPeer.GetNetworkPeer());
            BroadcastKillDeathCountChange(
                victimPeer.GetNetworkPeer(),
                affectorPeer?.GetNetworkPeer(),
                victimStats.KillCount,
                victimStats.AssistCount,
                victimStats.DeathCount,
                victimStats.Score);

            if (affectorAgent == null || !affectorAgent.IsHuman)
                return;

            if (!ReferenceEquals(affectorAgent, affectedAgent))
            {
                if (directKillerPeer != null)
                {
                    ApplyListedPlayerKill(mission, directKillerPeer, affectedAgent);
                }
                else if (TryHandleListedShellPlayerOwnedBotKill(mission, affectorAgent, affectedAgent))
                {
                }
                else
                {
                    TryHandleListedShellSideBotKill(mission, affectorAgent, affectedAgent);
                }

                return;
            }

            if (directKillerPeer != null)
                ApplyListedPlayerSuicide(mission, directKillerPeer, affectedAgent);
        }

        private static void HandleListedShellBotDeath(
            MissionLobbyComponent lobbyComponent,
            Mission mission,
            Agent affectedAgent,
            Agent affectorAgent,
            string source)
        {
            if (lobbyComponent == null || mission == null || affectedAgent == null)
                return;

            MissionPeer directKillerPeer = affectorAgent?.MissionPeer;
            MissionPeer affectorPeer = directKillerPeer ?? affectorAgent?.OwningAgentMissionPeer;
            MissionPeer assistorPeer = TryResolveAssistorPeer(directKillerPeer, affectedAgent);

            if (!TryHandleListedShellControlledBotDeath(mission, affectedAgent, affectorPeer, assistorPeer))
                TryHandleListedShellSideBotDeath(mission, affectedAgent, affectorPeer, assistorPeer);

            if (affectorAgent == null || !affectorAgent.IsHuman || ReferenceEquals(affectorAgent, affectedAgent))
                return;

            if (directKillerPeer != null)
            {
                ApplyListedPlayerKill(mission, directKillerPeer, affectedAgent);
                return;
            }

            if (TryHandleListedShellPlayerOwnedBotKill(mission, affectorAgent, affectedAgent))
                return;

            TryHandleListedShellSideBotKill(mission, affectorAgent, affectedAgent);
        }

        private static MissionPeer TryResolveAssistorPeer(
            MissionPeer killerPeer,
            Agent killedAgent)
        {
            if (killedAgent == null)
                return null;

            try
            {
                Agent.Hitter assistingHitter = killedAgent.GetAssistingHitter(killerPeer);
                MissionPeer assistorPeer = assistingHitter?.HitterPeer;
                if (assistorPeer == null)
                    return null;

                int assistCountDelta = assistingHitter.IsFriendlyHit ? -1 : 1;
                AdjustListedShellPeerStats(
                    assistorPeer,
                    0,
                    assistCountDelta,
                    0,
                    0,
                    "ListedShellLobbyRuntime.TryResolveAssistorPeer");
                return assistorPeer;
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyRuntime: failed to resolve assistor peer for listed player death through direct hitter contract: " + ex.Message);
                return null;
            }
        }

        private static void ApplyListedPlayerKill(Mission mission, MissionPeer killerPeer, Agent killedAgent)
        {
            if (mission == null || killerPeer == null || killedAgent == null)
                return;

            MissionPeer killedPeer = ResolveListedKilledPeerForKillContract(killedAgent);
            if (killedPeer != null)
                killerPeer.OnKillAnotherPeer(killedPeer);

            MissionMultiplayerGameModeBase gameMode = mission.GetMissionBehavior<MissionMultiplayerGameModeBase>();
            int killScore = gameMode?.GetScoreForKill(killedAgent) ?? 0;
            if (killerPeer.Team != null && killedAgent.Team != null && killerPeer.Team.IsEnemyOf(killedAgent.Team))
            {
                AdjustListedShellPeerStats(killerPeer, 1, 0, 0, killScore, "ListedShellLobbyRuntime.ApplyListedPlayerKill enemy");
            }
            else
            {
                AdjustListedShellPeerStats(killerPeer, -1, 0, 0, -(int)(killScore * 1.5f), "ListedShellLobbyRuntime.ApplyListedPlayerKill friendly");
            }

            PeerStatsRuntimeState killerStats = ResolveListedShellPeerStats(killerPeer);
            ListedShellMissionScoreboardComponent.NotifyListedShellPlayerPropertiesChanged(
                mission.GetMissionBehavior<MissionScoreboardComponent>(),
                killerPeer.GetNetworkPeer());
            BroadcastKillDeathCountChange(
                killerPeer.GetNetworkPeer(),
                null,
                killerStats.KillCount,
                killerStats.AssistCount,
                killerStats.DeathCount,
                killerStats.Score);
        }

        private static bool TryHandleListedShellControlledBotDeath(
            Mission mission,
            Agent botAgent,
            MissionPeer affectorPeer,
            MissionPeer assistorPeer)
        {
            if (mission == null || botAgent == null)
                return false;

            MissionPeer ownerPeer = TryResolveListedControlledBotOwnerPeer(botAgent);
            NetworkCommunicator ownerNetworkPeer = ownerPeer?.GetNetworkPeer();
            if (ownerPeer == null || ownerNetworkPeer == null)
                return false;

            if (assistorPeer != null)
            {
                PeerStatsRuntimeState assistorStats = ResolveListedShellPeerStats(assistorPeer);
                BroadcastKillDeathCountChange(
                    assistorPeer.GetNetworkPeer(),
                    affectorPeer?.GetNetworkPeer(),
                    assistorStats.KillCount,
                    assistorStats.AssistCount,
                    assistorStats.DeathCount,
                    assistorStats.Score);
            }

            AdjustListedShellPeerStats(ownerPeer, 0, 0, 1, 0, "ListedShellLobbyRuntime.TryHandleListedShellControlledBotDeath owner");
            ownerPeer.BotsUnderControlAlive = ClampBotsControlledCount(ownerPeer.BotsUnderControlAlive - 1);
            int botsAlive = ClampBotsControlledCount(ownerPeer.BotsUnderControlAlive);
            int botsTotal = ClampBotsControlledCount(ownerPeer.BotsUnderControlTotal);
            if (botsAlive > botsTotal)
            {
                botsAlive = botsTotal;
                ownerPeer.BotsUnderControlAlive = botsAlive;
            }

            MissionScoreboardComponent scoreboard = mission.GetMissionBehavior<MissionScoreboardComponent>();
            ListedShellMissionScoreboardComponent.NotifyListedShellPlayerPropertiesChanged(
                scoreboard,
                ownerNetworkPeer);
            if (botAgent.Team != null)
                ListedShellMissionScoreboardComponent.NotifyListedShellBotPropertiesChanged(
                    scoreboard,
                    botAgent.Team.Side);

            PeerStatsRuntimeState ownerStats = ResolveListedShellPeerStats(ownerPeer);
            BroadcastKillDeathCountChange(
                ownerNetworkPeer,
                affectorPeer?.GetNetworkPeer(),
                ownerStats.KillCount,
                ownerStats.AssistCount,
                ownerStats.DeathCount,
                ownerStats.Score);
            BroadcastBotsControlledChange(ownerNetworkPeer, botsAlive, botsTotal);
            return true;
        }

        private static bool TryHandleListedShellPlayerOwnedBotKill(
            Mission mission,
            Agent botAgent,
            Agent killedAgent)
        {
            if (mission == null || botAgent == null || killedAgent == null)
                return false;

            MissionPeer killerPeer = TryResolveListedControlledBotOwnerPeer(botAgent);
            NetworkCommunicator killerNetworkPeer = killerPeer?.GetNetworkPeer();
            if (killerPeer == null || killerNetworkPeer == null)
                return false;

            MissionPeer killedPeer = ResolveListedKilledPeerForKillContract(killedAgent);
            if (killedPeer != null)
                killerPeer.OnKillAnotherPeer(killedPeer);

            MissionMultiplayerGameModeBase gameMode = mission.GetMissionBehavior<MissionMultiplayerGameModeBase>();
            int killScore = gameMode?.GetScoreForKill(killedAgent) ?? 0;
            if (botAgent.Team != null && killedAgent.Team != null && botAgent.Team.IsEnemyOf(killedAgent.Team))
            {
                AdjustListedShellPeerStats(killerPeer, 1, 0, 0, killScore, "ListedShellLobbyRuntime.TryHandleListedShellPlayerOwnedBotKill enemy");
            }
            else
            {
                AdjustListedShellPeerStats(killerPeer, -1, 0, 0, -(int)(killScore * 1.5f), "ListedShellLobbyRuntime.TryHandleListedShellPlayerOwnedBotKill friendly");
            }

            MissionScoreboardComponent scoreboard = mission.GetMissionBehavior<MissionScoreboardComponent>();
            ListedShellMissionScoreboardComponent.NotifyListedShellPlayerPropertiesChanged(
                scoreboard,
                killerNetworkPeer);
            if (botAgent.Team != null)
                ListedShellMissionScoreboardComponent.NotifyListedShellBotPropertiesChanged(
                    scoreboard,
                    botAgent.Team.Side);

            PeerStatsRuntimeState killerStats = ResolveListedShellPeerStats(killerPeer);
            BroadcastKillDeathCountChange(
                killerNetworkPeer,
                null,
                killerStats.KillCount,
                killerStats.AssistCount,
                killerStats.DeathCount,
                killerStats.Score);
            return true;
        }

        private static bool TryHandleListedShellSideBotDeath(
            Mission mission,
            Agent botAgent,
            MissionPeer affectorPeer,
            MissionPeer assistorPeer)
        {
            if (mission == null || botAgent?.Team == null)
                return false;

            MissionScoreboardComponent scoreboard = mission.GetMissionBehavior<MissionScoreboardComponent>();
            if (!ListedShellMissionScoreboardComponent.TryResolveListedShellSideRuntimeState(
                    scoreboard,
                    botAgent.Team.Side,
                    "ListedShellLobbyRuntime.TryHandleListedShellSideBotDeath seed",
                    out ScoreboardSideRuntimeState sideState))
            {
                return false;
            }

            if (assistorPeer != null)
            {
                PeerStatsRuntimeState assistorStats = ResolveListedShellPeerStats(assistorPeer);
                BroadcastKillDeathCountChange(
                    assistorPeer.GetNetworkPeer(),
                    affectorPeer?.GetNetworkPeer(),
                    assistorStats.KillCount,
                    assistorStats.AssistCount,
                    assistorStats.DeathCount,
                    assistorStats.Score);
            }

            CoopBattleScoreboardRuntimeState.ApplyBotData(
                mission,
                scoreboard,
                sideState.Side,
                sideState.BotKillCount,
                sideState.BotAssistCount,
                sideState.BotDeathCount + 1,
                Math.Max(0, sideState.BotAliveCount - 1),
                "ListedShellLobbyRuntime.TryHandleListedShellSideBotDeath");
            ListedShellMissionScoreboardComponent.SyncListedShellSideRuntimeToNativeMirror(
                scoreboard,
                sideState.Side,
                "ListedShellLobbyRuntime.TryHandleListedShellSideBotDeath");
            ListedShellMissionScoreboardComponent.NotifyListedShellBotPropertiesChanged(
                scoreboard,
                sideState.Side);
            BroadcastBotData(scoreboard, sideState.Side);
            return true;
        }

        private static bool TryHandleListedShellSideBotKill(
            Mission mission,
            Agent botAgent,
            Agent killedAgent)
        {
            if (mission == null || botAgent?.Team == null || killedAgent?.Team == null)
                return false;

            MissionScoreboardComponent scoreboard = mission.GetMissionBehavior<MissionScoreboardComponent>();
            if (!ListedShellMissionScoreboardComponent.TryResolveListedShellSideRuntimeState(
                    scoreboard,
                    botAgent.Team.Side,
                    "ListedShellLobbyRuntime.TryHandleListedShellSideBotKill seed",
                    out ScoreboardSideRuntimeState sideState))
            {
                return false;
            }

            if (botAgent.Team.IsEnemyOf(killedAgent.Team))
                CoopBattleScoreboardRuntimeState.ApplyBotData(
                    mission,
                    scoreboard,
                    sideState.Side,
                    sideState.BotKillCount + 1,
                    sideState.BotAssistCount,
                    sideState.BotDeathCount,
                    sideState.BotAliveCount,
                    "ListedShellLobbyRuntime.TryHandleListedShellSideBotKill enemy");
            else
                CoopBattleScoreboardRuntimeState.ApplyBotData(
                    mission,
                    scoreboard,
                    sideState.Side,
                    sideState.BotKillCount - 1,
                    sideState.BotAssistCount,
                    sideState.BotDeathCount,
                    sideState.BotAliveCount,
                    "ListedShellLobbyRuntime.TryHandleListedShellSideBotKill friendly");

            ListedShellMissionScoreboardComponent.SyncListedShellSideRuntimeToNativeMirror(
                scoreboard,
                sideState.Side,
                "ListedShellLobbyRuntime.TryHandleListedShellSideBotKill");
            ListedShellMissionScoreboardComponent.NotifyListedShellBotPropertiesChanged(
                scoreboard,
                sideState.Side);
            BroadcastBotData(scoreboard, sideState.Side);
            return true;
        }

        private static MissionPeer ResolveListedKilledPeerForKillContract(Agent killedAgent)
        {
            if (killedAgent == null)
                return null;

            return killedAgent.MissionPeer ?? TryResolveListedControlledBotOwnerPeer(killedAgent);
        }

        private static MissionPeer TryResolveListedControlledBotOwnerPeer(Agent botAgent)
        {
            Formation formation = botAgent?.Formation;
            if (formation == null)
                return null;

            Agent formationPlayerOwner = TryGetFormationPlayerOwner(formation);
            MissionPeer ownerPeer = formationPlayerOwner?.MissionPeer ?? formationPlayerOwner?.OwningAgentMissionPeer;
            if (ownerPeer != null)
                return ownerPeer;

            if (GameNetwork.NetworkPeers == null)
                return null;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                MissionPeer missionPeer = peer?.GetComponent<MissionPeer>();
                if (missionPeer?.ControlledFormation == formation)
                    return missionPeer;
            }

            return null;
        }

        private static Agent TryGetFormationPlayerOwner(Formation formation)
        {
            if (formation == null)
                return null;

            try
            {
                return formation.PlayerOwner;
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyRuntime: failed to resolve formation PlayerOwner through direct contract: " + ex.Message);
                return null;
            }
        }

        private static void ApplyListedPlayerSuicide(Mission mission, MissionPeer killerPeer, Agent killedAgent)
        {
            if (mission == null || killerPeer == null || killedAgent == null)
                return;

            MissionMultiplayerGameModeBase gameMode = mission.GetMissionBehavior<MissionMultiplayerGameModeBase>();
            int killScore = gameMode?.GetScoreForKill(killedAgent) ?? 0;
            AdjustListedShellPeerStats(killerPeer, 0, 0, 0, -(int)(killScore * 1.5f), "ListedShellLobbyRuntime.ApplyListedPlayerSuicide");
            PeerStatsRuntimeState killerStats = ResolveListedShellPeerStats(killerPeer);
            ListedShellMissionScoreboardComponent.NotifyListedShellPlayerPropertiesChanged(
                mission.GetMissionBehavior<MissionScoreboardComponent>(),
                killerPeer.GetNetworkPeer());
            BroadcastKillDeathCountChange(
                killerPeer.GetNetworkPeer(),
                killedAgent.MissionPeer?.GetNetworkPeer(),
                killerStats.KillCount,
                killerStats.AssistCount,
                killerStats.DeathCount,
                killerStats.Score);
        }

        private static void BroadcastKillDeathCountChange(
            NetworkCommunicator victimPeer,
            NetworkCommunicator attackerPeer,
            int killCount,
            int assistCount,
            int deathCount,
            int score)
        {
            if (victimPeer == null)
                return;

            CoopSessionTransportPrimitives.BroadcastServerMessage(new NetworkMessages.FromServer.KillDeathCountChange(
                victimPeer,
                attackerPeer,
                killCount,
                assistCount,
                deathCount,
                score));
        }

        private static void BroadcastBotsControlledChange(NetworkCommunicator peer, int aliveCount, int totalCount)
        {
            if (peer == null)
                return;

            int clampedTotalCount = ClampBotsControlledCount(totalCount);
            int clampedAliveCount = Math.Min(ClampBotsControlledCount(aliveCount), clampedTotalCount);
            CoopSessionTransportPrimitives.BroadcastServerMessage(
                new NetworkMessages.FromServer.BotsControlledChange(peer, clampedAliveCount, clampedTotalCount));
        }

        private static void BroadcastBotData(
            MissionScoreboardComponent scoreboard,
            BattleSideEnum side)
        {
            if (!ListedShellMissionScoreboardComponent.TryResolveListedShellSideRuntimeState(
                    scoreboard,
                    side,
                    "ListedShellLobbyRuntime.BroadcastBotData",
                    out ScoreboardSideRuntimeState sideState))
            {
                return;
            }

            CoopSessionTransportPrimitives.BroadcastServerMessage(new NetworkMessages.FromServer.BotData(
                sideState.Side,
                sideState.BotKillCount,
                sideState.BotAssistCount,
                sideState.BotDeathCount,
                sideState.BotAliveCount));
        }

        private static void SendListedShellBotDataToPeer(
            NetworkCommunicator targetPeer,
            ScoreboardSideRuntimeState sideState)
        {
            if (targetPeer == null)
                return;

            CoopSessionTransportPrimitives.SendServerMessage(targetPeer, new NetworkMessages.FromServer.BotData(
                sideState.Side,
                sideState.BotKillCount,
                sideState.BotAssistCount,
                sideState.BotDeathCount,
                sideState.BotAliveCount));
        }

        private static PeerStatsRuntimeState ResolveListedShellPeerStats(MissionPeer missionPeer)
        {
            if (missionPeer == null)
                return default;

            if (!CoopBattlePeerStatsRuntimeState.TryGetState(missionPeer, out PeerStatsRuntimeState statsState))
            {
                int authoritativeDeathCount =
                    CoopBattlePeerLifecycleRuntimeState.TryGetState(missionPeer, out PeerLifecycleRuntimeState lifecycleState)
                        ? lifecycleState.DeathCount
                        : 0;
                CoopBattlePeerStatsRuntimeState.Apply(
                    missionPeer,
                    0,
                    0,
                    authoritativeDeathCount,
                    0,
                    "ListedShellLobbyRuntime.ResolveListedShellPeerStats authoritative seed");
                CoopBattlePeerStatsRuntimeState.TryGetState(missionPeer, out statsState);
            }

            return statsState;
        }

        private static void AdjustListedShellPeerStats(
            MissionPeer missionPeer,
            int killCountDelta,
            int assistCountDelta,
            int deathCountDelta,
            int scoreDelta,
            string source)
        {
            PeerStatsRuntimeState currentState = ResolveListedShellPeerStats(missionPeer);
            ApplyListedShellPeerStats(
                missionPeer,
                currentState.KillCount + killCountDelta,
                currentState.AssistCount + assistCountDelta,
                currentState.DeathCount + deathCountDelta,
                currentState.Score + scoreDelta,
                source);
        }

        private static void ApplyListedShellPeerStats(
            MissionPeer missionPeer,
            int killCount,
            int assistCount,
            int deathCount,
            int score,
            string source)
        {
            if (missionPeer == null)
                return;

            CoopBattlePeerStatsRuntimeState.Apply(
                missionPeer,
                killCount,
                assistCount,
                deathCount,
                score,
                source);
        }

        private static int ClampBotsControlledCount(int value)
        {
            return Math.Max(0, Math.Min(MaxBotsControlledCountForNetworkContract, value));
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

        private static bool TryRegisterBaseServerHandler(
            GameNetwork.NetworkMessageHandlerRegistererContainer registerer,
            Type messageType,
            GameNetworkMessage.ServerMessageHandlerDelegate<GameNetworkMessage> handler)
        {
            if (registerer == null ||
                messageType == null ||
                handler == null ||
                NetworkFromServerBaseHandlersField == null)
            {
                return false;
            }

            IList registrations = NetworkFromServerBaseHandlersField.GetValue(registerer) as IList;
            if (registrations == null)
                return false;

            for (int i = 0; i < registrations.Count; i++)
            {
                object registration = registrations[i];
                if (registration == null)
                    continue;

                Type registrationType = registration.GetType();
                Type registeredMessageType = registrationType.GetProperty("Item2")?.GetValue(registration) as Type;
                Delegate registeredHandler = registrationType.GetProperty("Item1")?.GetValue(registration) as Delegate;
                if (!ReferenceEquals(registeredMessageType, messageType) ||
                    registeredHandler?.Target != handler.Target ||
                    registeredHandler?.Method != handler.Method)
                {
                    continue;
                }

                return false;
            }

            Type tupleType = typeof(Tuple<,>).MakeGenericType(
                typeof(GameNetworkMessage.ServerMessageHandlerDelegate<GameNetworkMessage>),
                typeof(Type));
            object tuple = Activator.CreateInstance(tupleType, handler, messageType);
            registrations.Add(tuple);
            return true;
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
