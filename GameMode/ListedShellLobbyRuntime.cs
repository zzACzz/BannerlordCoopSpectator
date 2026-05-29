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

        private static readonly MethodInfo SetStateEndingAsClientMethod = typeof(MissionLobbyComponent).GetMethod(
            "SetStateEndingAsClient",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo MissionLobbyOnMyClientSynchronizedMethod = typeof(MissionLobbyComponent).GetMethod(
            "OnMyClientSynchronized",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NetworkFromServerBaseHandlersField =
            AccessTools.Field(typeof(GameNetwork.NetworkMessageHandlerRegistererContainer), "_fromServerBaseHandlers");
        private static readonly MethodInfo CurrentMultiplayerStateSetterMethod = typeof(MissionLobbyComponent)
            .GetProperty(
                nameof(MissionLobbyComponent.CurrentMultiplayerState),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetSetMethod(nonPublic: true);
        private static readonly FieldInfo OnPostMatchEndedField = typeof(MissionLobbyComponent).GetField(
            "OnPostMatchEnded",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Type MissionStateChangeType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.MissionStateChange");
        private static readonly MethodInfo GameNetworkWriteMessageMethod = ResolveGameNetworkWriteMessageMethod();
        private static readonly MethodInfo RemoveHittersAndGetAssistorPeerMethod = typeof(MissionLobbyComponent).GetMethod(
            "RemoveHittersAndGetAssistorPeer",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(MissionPeer), typeof(Agent) },
            modifiers: null);
        private static readonly MethodInfo MissionPeerKillCountSetter = typeof(MissionPeer)
            .GetProperty(nameof(MissionPeer.KillCount), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetSetMethod(nonPublic: true);
        private static readonly MethodInfo MissionPeerAssistCountSetter = typeof(MissionPeer)
            .GetProperty(nameof(MissionPeer.AssistCount), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetSetMethod(nonPublic: true);
        private static readonly MethodInfo MissionPeerDeathCountSetter = typeof(MissionPeer)
            .GetProperty(nameof(MissionPeer.DeathCount), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetSetMethod(nonPublic: true);
        private static readonly MethodInfo MissionPeerScoreSetter = typeof(MissionPeer)
            .GetProperty(nameof(MissionPeer.Score), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetSetMethod(nonPublic: true);
        private static readonly MethodInfo FormationPlayerOwnerGetter = typeof(Formation)
            .GetProperty("PlayerOwner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetGetMethod(nonPublic: true);
        private static readonly FieldInfo FormationPlayerOwnerBackingField = typeof(Formation).GetField(
            "<PlayerOwner>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
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

        internal static bool ShouldCallNativeOnMissionTick(MissionLobbyComponent lobbyComponent, float dt)
        {
            return OnMissionTick_Prefix(lobbyComponent, dt);
        }

        internal static bool ShouldCallNativeOnAgentRemoved(
            MissionLobbyComponent lobbyComponent,
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            KillingBlow killingBlow)
        {
            return OnAgentRemoved_Prefix(
                lobbyComponent,
                affectedAgent,
                affectorAgent,
                agentState,
                killingBlow);
        }

        internal static bool ShouldCallNativeHandleLateNewClientAfterLoadingFinished(
            MissionLobbyComponent lobbyComponent,
            NetworkCommunicator networkPeer)
        {
            try
            {
                Mission mission = lobbyComponent?.Mission ?? Mission.Current;
                if (!ShouldUseListedShellLobbyContract(mission))
                    return true;

                if (!GameNetwork.IsServer || networkPeer == null || networkPeer.IsServerPeer)
                    return false;

                SendListedShellLateJoinBootstrapToPeer(lobbyComponent, mission, networkPeer);
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyRuntime: late-new-client bootstrap failed open: " + ex.Message);
                return true;
            }
        }

        internal static void InitializeListedShellLobbyState(Mission mission)
        {
            try
            {
                if (!ShouldUseListedShellLobbyContract(mission))
                    return;

                RememberListedShellMissionState(mission, MissionLobbyComponent.MultiplayerGameState.WaitingFirstPlayers);
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyRuntime: behavior-initialize failed open: " + ex.Message);
            }
        }

        internal static void AfterListedShellLobbyStart(Mission mission, MissionLobbyComponent lobbyComponent)
        {
            try
            {
                if (!ShouldUseListedShellLobbyContract(mission) || !GameNetwork.IsClient)
                    return;

                MissionNetworkComponent missionNetwork = mission?.GetMissionBehavior<MissionNetworkComponent>();
                EventInfo synchronizedEvent = typeof(MissionNetworkComponent).GetEvent(
                    "OnMyClientSynchronized",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (missionNetwork == null ||
                    synchronizedEvent == null ||
                    synchronizedEvent.EventHandlerType == null ||
                    MissionLobbyOnMyClientSynchronizedMethod == null)
                {
                    return;
                }

                Delegate handler = Delegate.CreateDelegate(
                    synchronizedEvent.EventHandlerType,
                    lobbyComponent,
                    MissionLobbyOnMyClientSynchronizedMethod,
                    throwOnBindFailure: false);
                if (handler == null)
                    return;

                synchronizedEvent.RemoveEventHandler(missionNetwork, handler);
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyRuntime: after-start failed open: " + ex.Message);
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

                if (!TryResolveMissionLobbyState(mission, out MissionLobbyComponent.MultiplayerGameState listedState))
                    return true;

                if (listedState == MissionLobbyComponent.MultiplayerGameState.WaitingFirstPlayers)
                    return HandleWaitingFirstPlayersState(__instance, mission);

                if (listedState != MissionLobbyComponent.MultiplayerGameState.Playing)
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
                SetListedShellStateEndingAsServer(__instance, timer);
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyRuntime: mission-tick failed open: " + ex.Message);
                return true;
            }
        }

        internal static bool TryApplyListedShellMissionStateChange(
            Mission mission,
            MissionLobbyComponent lobbyComponent,
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

                if (lobbyComponent == null)
                    lobbyComponent = mission?.GetMissionBehavior<MissionLobbyComponent>();

                if (lobbyComponent != null)
                    CurrentMultiplayerStateSetterMethod?.Invoke(lobbyComponent, new object[] { currentState });
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

                if (currentState == MissionLobbyComponent.MultiplayerGameState.Ending && lobbyComponent != null)
                    SetStateEndingAsClientMethod?.Invoke(lobbyComponent, Array.Empty<object>());

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
                TrySetMissionPeerIntProperty(victimPeer, MissionPeerKillCountSetter, killDeathCountChange.KillCount);
                TrySetMissionPeerIntProperty(victimPeer, MissionPeerAssistCountSetter, killDeathCountChange.AssistCount);
                TrySetMissionPeerIntProperty(victimPeer, MissionPeerDeathCountSetter, killDeathCountChange.DeathCount);
                TrySetMissionPeerIntProperty(victimPeer, MissionPeerScoreSetter, killDeathCountChange.Score);
                attackerPeer?.OnKillAnotherPeer(victimPeer);
                if (killDeathCountChange.KillCount == 0 &&
                    killDeathCountChange.AssistCount == 0 &&
                    killDeathCountChange.DeathCount == 0 &&
                    killDeathCountChange.Score == 0)
                {
                    victimPeer.ResetKillRegistry();
                }
            }

            (scoreboardComponent ?? Mission.Current?.GetMissionBehavior<MissionScoreboardComponent>())
                ?.PlayerPropertiesChanged(killDeathCountChange.VictimPeer);
            return true;
        }

        private static bool OnAgentRemoved_Prefix(
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
                    return true;

                if (!TryResolveMissionLobbyState(mission, out MissionLobbyComponent.MultiplayerGameState listedState))
                    return true;

                if (!GameNetwork.IsServer ||
                    listedState == MissionLobbyComponent.MultiplayerGameState.Ending ||
                    affectedAgent == null ||
                    !affectedAgent.IsHuman ||
                    affectedAgent.IsMount)
                {
                    return true;
                }

                if (agentState != AgentState.Killed &&
                    agentState != AgentState.Unconscious &&
                    agentState != AgentState.Routed)
                {
                    return true;
                }

                if (affectedAgent.MissionPeer != null)
                {
                    HandleListedShellPlayerDeath(
                        __instance,
                        mission,
                        affectedAgent,
                        affectorAgent,
                        "ListedShellLobbyRuntime.OnAgentRemoved");
                    return false;
                }

                HandleListedShellBotDeath(
                    __instance,
                    mission,
                    affectedAgent,
                    affectorAgent,
                    "ListedShellLobbyRuntime.OnAgentRemoved");
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyRuntime: agent-removed failed open: " + ex.Message);
                return true;
            }
        }

        private static bool HandleWaitingFirstPlayersState(MissionLobbyComponent lobbyComponent, Mission mission)
        {
            if (!GameNetwork.IsServer)
                return true;

            MultiplayerTimerComponent timer = mission?.GetMissionBehavior<MultiplayerTimerComponent>();
            if (timer == null)
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

            SetListedShellStatePlayingAsServer(lobbyComponent, mission, timer);
            ModLogger.Info(
                "ListedShellLobbyRuntime: advanced listed-shell lobby from WaitingFirstPlayers to Playing via explicit coop-owned lobby contract. " +
                "Peers=" + synchronizedPeerCount +
                " Bots=" + configuredBotCount +
                " MinPlayers=" + minPlayersToStart +
                " Mission=" + (mission?.SceneName ?? "unknown"));
            return false;
        }

        private static void SetListedShellStatePlayingAsServer(
            MissionLobbyComponent lobbyComponent,
            Mission mission,
            MultiplayerTimerComponent timer)
        {
            MultiplayerWarmupComponent warmup = mission?.GetMissionBehavior<MultiplayerWarmupComponent>();
            if (warmup != null)
                mission.RemoveMissionBehavior(warmup);

            CurrentMultiplayerStateSetterMethod?.Invoke(
                lobbyComponent,
                new object[] { MissionLobbyComponent.MultiplayerGameState.Playing });
            RememberListedShellMissionState(mission, MissionLobbyComponent.MultiplayerGameState.Playing);
            timer.StartTimerAsServer(MultiplayerOptions.OptionType.MapTimeLimit.GetIntValue() * 60f);
            BroadcastMissionStateChange(
                MissionLobbyComponent.MultiplayerGameState.Playing,
                timer.GetCurrentTimerStartTime().NumberOfTicks);
        }

        private static void SetListedShellStateEndingAsServer(
            MissionLobbyComponent lobbyComponent,
            MultiplayerTimerComponent timer)
        {
            CurrentMultiplayerStateSetterMethod?.Invoke(
                lobbyComponent,
                new object[] { MissionLobbyComponent.MultiplayerGameState.Ending });
            RememberListedShellMissionState(lobbyComponent?.Mission, MissionLobbyComponent.MultiplayerGameState.Ending);
            timer.StartTimerAsServer(MissionLobbyComponent.PostMatchWaitDuration);
            BroadcastMissionStateChange(
                MissionLobbyComponent.MultiplayerGameState.Ending,
                timer.GetCurrentTimerStartTime().NumberOfTicks);
            (OnPostMatchEndedField?.GetValue(lobbyComponent) as Action)?.Invoke();
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

            GameNetwork.BeginBroadcastModuleEvent();
            GameNetworkWriteMessageMethod.Invoke(null, new[] { message });
            GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
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

            GameNetwork.BeginModuleEventAsServer(peer);
            GameNetworkWriteMessageMethod.Invoke(null, new[] { message });
            GameNetwork.EndModuleEventAsServer();
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

                int replayedDeathCount = ResolveListedShellReplayDeathCount(missionPeer);
                GameNetwork.BeginModuleEventAsServer(targetPeer);
                GameNetwork.WriteMessage(new NetworkMessages.FromServer.KillDeathCountChange(
                    missionPeer.GetNetworkPeer(),
                    null,
                    missionPeer.KillCount,
                    missionPeer.AssistCount,
                    replayedDeathCount,
                    missionPeer.Score));
                GameNetwork.EndModuleEventAsServer();
                replayedKillDeathCount++;

                if (missionPeer.BotsUnderControlAlive == 0 && missionPeer.BotsUnderControlTotal == 0)
                    continue;

                GameNetwork.BeginModuleEventAsServer(targetPeer);
                GameNetwork.WriteMessage(new NetworkMessages.FromServer.BotsControlledChange(
                    missionPeer.GetNetworkPeer(),
                    missionPeer.BotsUnderControlAlive,
                    missionPeer.BotsUnderControlTotal));
                GameNetwork.EndModuleEventAsServer();
                replayedBotsControlledCount++;
            }

            ModLogger.Info(
                "ListedShellLobbyRuntime: replayed listed-shell peer info through coop-owned late-client contract. " +
                "Peer=" + (targetPeer.UserName ?? targetPeer.Index.ToString()) +
                " Scene=" + (mission?.SceneName ?? "unknown") +
                " KillDeathReplays=" + replayedKillDeathCount +
                " BotsControlledReplays=" + replayedBotsControlledCount);
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

            if (!CoopBattlePeerLifecycleRuntimeState.TryGetState(missionPeer, out PeerLifecycleRuntimeState lifecycleState))
                return missionPeer.DeathCount;

            return Math.Max(missionPeer.DeathCount, lifecycleState.DeathCount);
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
            MissionPeer assistorPeer = TryResolveAssistorPeer(lobbyComponent, directKillerPeer, affectedAgent);

            CoopMissionSpawnLogic.TryHandleListedShellPlayerDeathTransition(
                mission,
                affectedAgent,
                source);

            if (assistorPeer != null)
            {
                BroadcastKillDeathCountChange(
                    assistorPeer.GetNetworkPeer(),
                    affectorPeer?.GetNetworkPeer(),
                    assistorPeer.KillCount,
                    assistorPeer.AssistCount,
                    assistorPeer.DeathCount,
                    assistorPeer.Score);
            }

            TrySetMissionPeerIntProperty(victimPeer, MissionPeerDeathCountSetter, victimPeer.DeathCount + 1);
            mission.GetMissionBehavior<MissionScoreboardComponent>()?.PlayerPropertiesChanged(victimPeer.GetNetworkPeer());
            BroadcastKillDeathCountChange(
                victimPeer.GetNetworkPeer(),
                affectorPeer?.GetNetworkPeer(),
                victimPeer.KillCount,
                victimPeer.AssistCount,
                victimPeer.DeathCount,
                victimPeer.Score);

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
            MissionPeer assistorPeer = TryResolveAssistorPeer(lobbyComponent, directKillerPeer, affectedAgent);

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
            MissionLobbyComponent lobbyComponent,
            MissionPeer killerPeer,
            Agent killedAgent)
        {
            if (lobbyComponent == null || RemoveHittersAndGetAssistorPeerMethod == null || killedAgent == null)
                return null;

            try
            {
                return RemoveHittersAndGetAssistorPeerMethod.Invoke(
                    lobbyComponent,
                    new object[] { killerPeer, killedAgent }) as MissionPeer;
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyRuntime: failed to resolve assistor peer for listed player death: " + ex.Message);
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
                TrySetMissionPeerIntProperty(killerPeer, MissionPeerScoreSetter, killerPeer.Score + killScore);
                TrySetMissionPeerIntProperty(killerPeer, MissionPeerKillCountSetter, killerPeer.KillCount + 1);
            }
            else
            {
                TrySetMissionPeerIntProperty(killerPeer, MissionPeerScoreSetter, killerPeer.Score - (int)(killScore * 1.5f));
                TrySetMissionPeerIntProperty(killerPeer, MissionPeerKillCountSetter, killerPeer.KillCount - 1);
            }

            mission.GetMissionBehavior<MissionScoreboardComponent>()?.PlayerPropertiesChanged(killerPeer.GetNetworkPeer());
            BroadcastKillDeathCountChange(
                killerPeer.GetNetworkPeer(),
                null,
                killerPeer.KillCount,
                killerPeer.AssistCount,
                killerPeer.DeathCount,
                killerPeer.Score);
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
                BroadcastKillDeathCountChange(
                    assistorPeer.GetNetworkPeer(),
                    affectorPeer?.GetNetworkPeer(),
                    assistorPeer.KillCount,
                    assistorPeer.AssistCount,
                    assistorPeer.DeathCount,
                    assistorPeer.Score);
            }

            TrySetMissionPeerIntProperty(ownerPeer, MissionPeerDeathCountSetter, ownerPeer.DeathCount + 1);
            ownerPeer.BotsUnderControlAlive = ClampBotsControlledCount(ownerPeer.BotsUnderControlAlive - 1);
            int botsAlive = ClampBotsControlledCount(ownerPeer.BotsUnderControlAlive);
            int botsTotal = ClampBotsControlledCount(ownerPeer.BotsUnderControlTotal);
            if (botsAlive > botsTotal)
            {
                botsAlive = botsTotal;
                ownerPeer.BotsUnderControlAlive = botsAlive;
            }

            MissionScoreboardComponent scoreboard = mission.GetMissionBehavior<MissionScoreboardComponent>();
            scoreboard?.PlayerPropertiesChanged(ownerNetworkPeer);
            if (botAgent.Team != null)
                scoreboard?.BotPropertiesChanged(botAgent.Team.Side);

            BroadcastKillDeathCountChange(
                ownerNetworkPeer,
                affectorPeer?.GetNetworkPeer(),
                ownerPeer.KillCount,
                ownerPeer.AssistCount,
                ownerPeer.DeathCount,
                ownerPeer.Score);
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
                TrySetMissionPeerIntProperty(killerPeer, MissionPeerKillCountSetter, killerPeer.KillCount + 1);
                TrySetMissionPeerIntProperty(killerPeer, MissionPeerScoreSetter, killerPeer.Score + killScore);
            }
            else
            {
                TrySetMissionPeerIntProperty(killerPeer, MissionPeerKillCountSetter, killerPeer.KillCount - 1);
                TrySetMissionPeerIntProperty(killerPeer, MissionPeerScoreSetter, killerPeer.Score - (int)(killScore * 1.5f));
            }

            MissionScoreboardComponent scoreboard = mission.GetMissionBehavior<MissionScoreboardComponent>();
            scoreboard?.PlayerPropertiesChanged(killerNetworkPeer);
            if (botAgent.Team != null)
                scoreboard?.BotPropertiesChanged(botAgent.Team.Side);

            BroadcastKillDeathCountChange(
                killerNetworkPeer,
                null,
                killerPeer.KillCount,
                killerPeer.AssistCount,
                killerPeer.DeathCount,
                killerPeer.Score);
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
            MissionScoreboardComponent.MissionScoreboardSide scoreboardSide = scoreboard?.GetSideSafe(botAgent.Team.Side);
            if (scoreboardSide?.BotScores == null)
                return false;

            if (assistorPeer != null)
            {
                BroadcastKillDeathCountChange(
                    assistorPeer.GetNetworkPeer(),
                    affectorPeer?.GetNetworkPeer(),
                    assistorPeer.KillCount,
                    assistorPeer.AssistCount,
                    assistorPeer.DeathCount,
                    assistorPeer.Score);
            }

            scoreboardSide.BotScores.DeathCount++;
            scoreboardSide.BotScores.AliveCount = Math.Max(0, scoreboardSide.BotScores.AliveCount - 1);
            scoreboard.BotPropertiesChanged(scoreboardSide.Side);
            BroadcastBotData(scoreboardSide);
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
            MissionScoreboardComponent.MissionScoreboardSide scoreboardSide = scoreboard?.GetSideSafe(botAgent.Team.Side);
            if (scoreboardSide?.BotScores == null)
                return false;

            if (botAgent.Team.IsEnemyOf(killedAgent.Team))
                scoreboardSide.BotScores.KillCount++;
            else
                scoreboardSide.BotScores.KillCount--;

            scoreboard.BotPropertiesChanged(scoreboardSide.Side);
            BroadcastBotData(scoreboardSide);
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
                if (FormationPlayerOwnerGetter != null)
                    return FormationPlayerOwnerGetter.Invoke(formation, Array.Empty<object>()) as Agent;

                return FormationPlayerOwnerBackingField?.GetValue(formation) as Agent;
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyRuntime: failed to resolve formation PlayerOwner through reflection: " + ex.Message);
                return null;
            }
        }

        private static void ApplyListedPlayerSuicide(Mission mission, MissionPeer killerPeer, Agent killedAgent)
        {
            if (mission == null || killerPeer == null || killedAgent == null)
                return;

            MissionMultiplayerGameModeBase gameMode = mission.GetMissionBehavior<MissionMultiplayerGameModeBase>();
            int killScore = gameMode?.GetScoreForKill(killedAgent) ?? 0;
            TrySetMissionPeerIntProperty(killerPeer, MissionPeerScoreSetter, killerPeer.Score - (int)(killScore * 1.5f));
            mission.GetMissionBehavior<MissionScoreboardComponent>()?.PlayerPropertiesChanged(killerPeer.GetNetworkPeer());
            BroadcastKillDeathCountChange(
                killerPeer.GetNetworkPeer(),
                killedAgent.MissionPeer?.GetNetworkPeer(),
                killerPeer.KillCount,
                killerPeer.AssistCount,
                killerPeer.DeathCount,
                killerPeer.Score);
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

            GameNetwork.BeginBroadcastModuleEvent();
            GameNetwork.WriteMessage(new NetworkMessages.FromServer.KillDeathCountChange(
                victimPeer,
                attackerPeer,
                killCount,
                assistCount,
                deathCount,
                score));
            GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
        }

        private static void BroadcastBotsControlledChange(NetworkCommunicator peer, int aliveCount, int totalCount)
        {
            if (peer == null)
                return;

            int clampedTotalCount = ClampBotsControlledCount(totalCount);
            int clampedAliveCount = Math.Min(ClampBotsControlledCount(aliveCount), clampedTotalCount);
            GameNetwork.BeginBroadcastModuleEvent();
            GameNetwork.WriteMessage(new NetworkMessages.FromServer.BotsControlledChange(peer, clampedAliveCount, clampedTotalCount));
            GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
        }

        private static void BroadcastBotData(MissionScoreboardComponent.MissionScoreboardSide scoreboardSide)
        {
            if (scoreboardSide?.BotScores == null)
                return;

            GameNetwork.BeginBroadcastModuleEvent();
            GameNetwork.WriteMessage(new NetworkMessages.FromServer.BotData(
                scoreboardSide.Side,
                scoreboardSide.BotScores.KillCount,
                scoreboardSide.BotScores.AssistCount,
                scoreboardSide.BotScores.DeathCount,
                scoreboardSide.BotScores.AliveCount));
            GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
        }

        private static int ClampBotsControlledCount(int value)
        {
            return Math.Max(0, Math.Min(MaxBotsControlledCountForNetworkContract, value));
        }

        private static void TrySetMissionPeerIntProperty(MissionPeer missionPeer, MethodInfo setter, int value)
        {
            if (missionPeer == null || setter == null)
                return;

            try
            {
                setter.Invoke(missionPeer, new object[] { value });
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ListedShellLobbyRuntime: failed to set MissionPeer int property through nonpublic contract. " +
                    "Peer=" + (missionPeer.GetNetworkPeer()?.UserName ?? missionPeer.GetNetworkPeer()?.Index.ToString() ?? "none") +
                    " Error=" + ex.Message);
            }
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
