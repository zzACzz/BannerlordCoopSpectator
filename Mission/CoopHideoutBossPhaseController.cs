using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.MissionBehaviors
{
    public sealed class CoopHideoutBossPhaseController : MissionNetwork
    {
        private const float InitialAssaultGraceSeconds = 4f;
        private const float ServerPumpIntervalSeconds = 0.1f;
        private const float DefaultInnerRadius = 2.5f;
        private const float DefaultOuterRadius = 6f;
        private const float DefaultWalkDistance = 3f;
        private const float NativeApproachSpeedLimit =
            CoopHideoutBossPhaseContract.NativeAgentMaxSpeedCinematicOverride;
        private static readonly float[] ChoreographyDiagnosticSampleOffsetsSeconds =
        {
            0.05f,
            0.1f,
            0.25f,
            0.5f,
            1f,
            2f,
            3f,
            5f,
            8f,
            12f
        };

        private readonly HashSet<int> _requiredReadyPeerIndices = new HashSet<int>();
        private readonly HashSet<int> _readyPeerIndices = new HashSet<int>();
        private readonly Dictionary<int, FrozenAgentState> _frozenAgentStates =
            new Dictionary<int, FrozenAgentState>();
        private readonly Dictionary<Formation, FrozenFormationState> _frozenFormationStates =
            new Dictionary<Formation, FrozenFormationState>();
        private readonly Dictionary<int, BossFightParticipantPlacement> _targetPlacements =
            new Dictionary<int, BossFightParticipantPlacement>();
        private readonly Dictionary<int, int> _clientChoreographySequenceByAgent =
            new Dictionary<int, int>();
        private readonly HashSet<int> _nativeControllerDetachedAgentIndices = new HashSet<int>();
        private readonly Dictionary<int, string> _choreographyDiagnosticRoles =
            new Dictionary<int, string>();
        private readonly Dictionary<int, BossFightParticipantPlacement> _choreographyDiagnosticPlacements =
            new Dictionary<int, BossFightParticipantPlacement>();
        private readonly Dictionary<int, ChoreographyDiagnosticSampleWindow> _choreographyDiagnosticSampleWindows =
            new Dictionary<int, ChoreographyDiagnosticSampleWindow>();

        private static bool _choreographyDiagnosticReflectionResolved;
        private static FieldInfo _lastSynchedTargetPositionField;
        private static FieldInfo _checkIfTargetFrameIsChangedField;

        private CoopHideoutBossPhaseSession _session;
        private Team _playerTeam;
        private Team _enemyTeam;
        private Agent _hostAgent;
        private Agent _authoritativeMainHeroAgent;
        private Agent _bossAgent;
        private int _authoritativeHostPeerIndex = -1;
        private MissionMode _missionModeBeforeBossPhase;
        private DateTime _missionStartedUtc;
        private float _nextServerPumpMissionTime;
        private int _initialEnemyCount;
        private bool _bossFightEntityMissingLogged;
        private bool _phaseCompletionLogged;
        private bool _campaignStagedPlacementActive;
        private bool _campaignApproachHeld;
        private bool _autoStartAllBattleAfterCinematic;
        private bool _playerSideEliminationTriggered;
        private DateTime _campaignApproachHoldDeadlineUtc;
        private float _campaignAuthoredWalkDistance;
        private int _choreographySequence;

        public static event Action<CoopHideoutBossPhaseSession, int> ClientStateChanged;
        public static CoopHideoutBossPhaseSession CurrentClientState { get; private set; }
        public static int CurrentClientPhaseDurationMilliseconds { get; private set; }

        internal bool IsReservedBossEntry(string entryId)
        {
            if (string.IsNullOrWhiteSpace(entryId) || Mission == null)
                return false;

            CoopExactCampaignHideoutMissionController hideoutController =
                Mission.GetMissionBehavior<CoopExactCampaignHideoutMissionController>();
            if (hideoutController == null)
                return false;

            CoopExactCampaignHideoutAmbushMissionController nightController =
                hideoutController as CoopExactCampaignHideoutAmbushMissionController;
            bool hasReservedBossContract =
                hideoutController.HasReservedBossGroup ||
                hideoutController.ReservedBossAgent != null ||
                nightController?.HasNightReservedBossGroup == true ||
                _bossAgent != null;
            if (!hasReservedBossContract)
                return false;

            BattleSideEnum bossSide = hideoutController.PlayerSide == BattleSideEnum.Attacker
                ? BattleSideEnum.Defender
                : BattleSideEnum.Attacker;
            RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(
                BattleSnapshotRuntimeState.GetState(),
                bossSide);
            return commanderEntry != null &&
                   string.Equals(commanderEntry.EntryId, entryId, StringComparison.Ordinal);
        }

        internal bool ShouldDeferReservedBossPossession(string entryId)
        {
            return CoopHideoutBossPhaseContract.ShouldDeferReservedBossPossession(
                IsReservedBossEntry(entryId),
                _session?.Phase ?? CoopHideoutBossPhase.InitialAssault);
        }

        internal bool ShouldPreservePendingReservedBossSelection(string entryId)
        {
            return CoopHideoutBossPhaseContract.ShouldPreservePendingReservedBossSelection(
                IsReservedBossEntry(entryId),
                _session?.Phase ?? CoopHideoutBossPhase.InitialAssault);
        }

        internal bool ShouldRepairReservedBossPossessionFormation(
            string entryId,
            bool isExactEntryMatch,
            bool hasFormation)
        {
            return CoopHideoutBossPhaseContract.ShouldRepairReservedBossPossessionFormation(
                IsReservedBossEntry(entryId),
                _session?.Phase ?? CoopHideoutBossPhase.InitialAssault,
                isExactEntryMatch,
                hasFormation);
        }

        internal bool ShouldPreserveNightBossFormationDetachment(Agent agent)
        {
            if (agent == null || _session == null ||
                !_frozenAgentStates.TryGetValue(agent.Index, out FrozenAgentState frozen) ||
                !ReferenceEquals(frozen.Agent, agent))
            {
                return false;
            }

            bool isBossFightParticipant =
                ReferenceEquals(frozen.OriginalTeam, _playerTeam) ||
                ReferenceEquals(frozen.OriginalTeam, _enemyTeam);
            bool wasAiControlled =
                frozen.OriginalController == AgentControllerType.AI ||
                _nativeControllerDetachedAgentIndices.Contains(agent.Index);
            return CoopHideoutBossPhaseContract.ShouldPreserveCampaignBossFormationDetachment(
                _campaignStagedPlacementActive,
                _session.Phase,
                wasAiControlled,
                isBossFightParticipant);
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _missionStartedUtc = DateTime.UtcNow;
            _choreographyDiagnosticRoles.Clear();
            _choreographyDiagnosticPlacements.Clear();
            _choreographyDiagnosticSampleWindows.Clear();
            _nativeControllerDetachedAgentIndices.Clear();
            if (GameNetwork.IsClient)
            {
                CurrentClientState = null;
                CurrentClientPhaseDurationMilliseconds = 0;
                _clientChoreographySequenceByAgent.Clear();
                _campaignStagedPlacementActive = false;
            }

            if (!GameNetwork.IsServer)
                return;

            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            string battleInstanceId = snapshot?.BattleInstanceId;
            if (string.IsNullOrWhiteSpace(battleInstanceId))
                battleInstanceId = Guid.NewGuid().ToString("N");

            _session = new CoopHideoutBossPhaseSession
            {
                BattleInstanceId = CoopHideoutBossPhaseContract.Bound(
                    battleInstanceId,
                    CoopHideoutBossPhaseContract.MaximumBattleInstanceIdCharacters),
                Phase = CoopHideoutBossPhase.InitialAssault,
                Revision = 1,
                Choice = CoopHideoutBossChoice.None,
                Reason = "initial-assault"
            };

            ModLogger.Info(
                "CoopHideoutBossPhaseController: isolated campaign hideout controller initialized. " +
                "BattleInstanceId=" + _session.BattleInstanceId +
                " Scene=" + (Mission?.SceneName ?? "null") + ".");
        }

        protected override void AddRemoveMessageHandlers(
            GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            if (GameNetwork.IsServer)
                registerer.RegisterBaseHandler<CoopHideoutBossPhaseClientCommandMessage>(HandleClientCommand);
            if (GameNetwork.IsClient)
            {
                registerer.RegisterBaseHandler<CoopHideoutBossPhaseStateMessage>(HandleServerState);
                registerer.RegisterBaseHandler<CoopHideoutBossAgentChoreographyMessage>(
                    HandleServerAgentChoreography);
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            PumpChoreographyDiagnosticSamples();
            if (!GameNetwork.IsServer || Mission == null || _session == null)
                return;
            if (Mission.CurrentTime < _nextServerPumpMissionTime)
                return;

            _nextServerPumpMissionTime = Mission.CurrentTime + ServerPumpIntervalSeconds;
            PumpServerState(DateTime.UtcNow);
        }

        protected override void HandleNewClientAfterSynchronized(NetworkCommunicator networkPeer)
        {
            base.HandleNewClientAfterSynchronized(networkPeer);
            if (!GameNetwork.IsServer || networkPeer == null || networkPeer.IsServerPeer || _session == null)
                return;

            SendState(networkPeer, ResolvePhaseDurationMilliseconds(_session.Phase));
            SendCurrentAgentChoreography(networkPeer);
        }

        protected override void HandlePlayerDisconnect(NetworkCommunicator networkPeer)
        {
            base.HandlePlayerDisconnect(networkPeer);
            if (!GameNetwork.IsServer || networkPeer == null || _session == null)
                return;

            _requiredReadyPeerIndices.Remove(networkPeer.Index);
            _readyPeerIndices.Remove(networkPeer.Index);
            if (networkPeer.Index != _session.HostPeerIndex)
                return;

            if (_autoStartAllBattleAfterCinematic &&
                (_session.Phase == CoopHideoutBossPhase.PreparingCinematic ||
                 _session.Phase == CoopHideoutBossPhase.Cinematic))
            {
                return;
            }

            if (_session.Phase == CoopHideoutBossPhase.PreparingCinematic ||
                _session.Phase == CoopHideoutBossPhase.Cinematic ||
                _session.Phase == CoopHideoutBossPhase.AwaitingHostChoice ||
                _session.Phase == CoopHideoutBossPhase.Duel)
            {
                StartAllBattle("host-disconnected-fallback");
            }
        }

        public override void OnMissionStateFinalized()
        {
            _choreographyDiagnosticRoles.Clear();
            _choreographyDiagnosticPlacements.Clear();
            _choreographyDiagnosticSampleWindows.Clear();
            _nativeControllerDetachedAgentIndices.Clear();
            if (GameNetwork.IsClient)
            {
                CurrentClientState = null;
                CurrentClientPhaseDurationMilliseconds = 0;
                _clientChoreographySequenceByAgent.Clear();
            }
            base.OnMissionStateFinalized();
        }

        public static bool SendClientReady(int revision)
        {
            return SendClientCommand(revision, CoopHideoutBossClientCommandKind.ReadyForCinematic);
        }

        public static bool SendHostChoice(CoopHideoutBossChoice choice)
        {
            CoopHideoutBossPhaseSession state = CurrentClientState;
            if (state == null)
                return false;

            CoopHideoutBossClientCommandKind commandKind =
                choice == CoopHideoutBossChoice.Duel
                    ? CoopHideoutBossClientCommandKind.ChooseDuel
                    : CoopHideoutBossClientCommandKind.ChooseAllBattle;
            return SendClientCommand(state.Revision, commandKind);
        }

        private static bool SendClientCommand(int revision, CoopHideoutBossClientCommandKind commandKind)
        {
            CoopHideoutBossPhaseSession state = CurrentClientState;
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || state == null)
                return false;

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopHideoutBossPhaseClientCommandMessage(
                    state.BattleInstanceId,
                    revision,
                    commandKind));
                GameNetwork.EndModuleEventAsClient();
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: client command send failed. " +
                    "Kind=" + commandKind + " Error=" + ex.Message);
                return false;
            }
        }

        private bool HandleClientCommand(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            CoopHideoutBossPhaseClientCommandMessage message =
                baseMessage as CoopHideoutBossPhaseClientCommandMessage;
            if (peer == null || message == null || _session == null)
                return false;
            if (!string.Equals(
                    message.BattleInstanceId,
                    _session.BattleInstanceId,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (message.CommandKind == CoopHideoutBossClientCommandKind.ReadyForCinematic)
            {
                if (_session.Phase == CoopHideoutBossPhase.PreparingCinematic &&
                    message.Revision == _session.Revision &&
                    _requiredReadyPeerIndices.Contains(peer.Index))
                {
                    _readyPeerIndices.Add(peer.Index);
                }
                return true;
            }

            CoopHideoutBossChoice acceptedChoice;
            string rejectionReason;
            if (!CoopHideoutBossPhaseContract.TryAcceptHostChoice(
                    _session,
                    peer.Index,
                    message.Revision,
                    message.CommandKind,
                    out acceptedChoice,
                    out rejectionReason))
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: host choice rejected. " +
                    "PeerIndex=" + peer.Index +
                    " Revision=" + message.Revision +
                    " Reason=" + rejectionReason + ".");
                SendState(peer, ResolvePhaseDurationMilliseconds(_session.Phase));
                return true;
            }

            if (acceptedChoice == CoopHideoutBossChoice.Duel)
                StartDuel("host-choice-duel");
            else
                StartAllBattle("host-choice-all-battle");
            return true;
        }

        private void HandleServerState(GameNetworkMessage baseMessage)
        {
            CoopHideoutBossPhaseStateMessage message = baseMessage as CoopHideoutBossPhaseStateMessage;
            if (message == null ||
                message.ProtocolVersion != CoopHideoutBossPhaseContract.ProtocolVersion)
            {
                return;
            }

            CoopHideoutBossPhaseSession previous = CurrentClientState;
            if (previous != null &&
                string.Equals(previous.BattleInstanceId, message.BattleInstanceId, StringComparison.Ordinal) &&
                message.Revision < previous.Revision)
            {
                return;
            }

            CurrentClientState = message.ToSession();
            CurrentClientPhaseDurationMilliseconds = message.PhaseDurationMilliseconds;
            ApplyClientBossTargetPolicy(CurrentClientState);
            if (CoopDebugConfig.HideoutBossChoreographyDiagnostics &&
                CurrentClientState.Phase == CoopHideoutBossPhase.Duel &&
                previous?.Phase != CoopHideoutBossPhase.Duel)
            {
                LogAllTrackedChoreographyDiagnosticSnapshots(
                    "client-duel-state",
                    _choreographySequence);
                StartAllTrackedChoreographyDiagnosticSampleWindows(
                    "client-duel",
                    _choreographySequence);
            }
            try
            {
                ClientStateChanged?.Invoke(
                    CurrentClientState.Clone(),
                    CurrentClientPhaseDurationMilliseconds);
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopHideoutBossPhaseController: client state dispatch failed.",
                    ex);
            }
        }

        private void HandleServerAgentChoreography(GameNetworkMessage baseMessage)
        {
            CoopHideoutBossAgentChoreographyMessage message =
                baseMessage as CoopHideoutBossAgentChoreographyMessage;
            CoopHideoutBossPhaseSession clientState = CurrentClientState;
            if (message == null ||
                clientState == null ||
                message.ProtocolVersion != CoopHideoutBossPhaseContract.ProtocolVersion)
            {
                return;
            }

            _clientChoreographySequenceByAgent.TryGetValue(
                message.AgentIndex,
                out int lastAppliedSequence);
            if (!CoopHideoutBossPhaseContract.ShouldApplyAgentChoreographyMessage(
                    clientState.BattleInstanceId,
                    message.BattleInstanceId,
                    lastAppliedSequence,
                    message.Sequence))
            {
                return;
            }

            Agent agent = TaleWorlds.MountAndBlade.Mission.MissionNetworkHelper.GetAgentFromIndex(
                message.AgentIndex,
                canBeNull: true);
            if (agent?.IsActive() != true)
                return;

            try
            {
                _choreographySequence = Math.Max(_choreographySequence, message.Sequence);
                _campaignStagedPlacementActive = true;
                TrackClientChoreographyDiagnosticAgent(agent, message, clientState);
                ApplyAgentChoreographyLocally(
                    agent,
                    message.Kind,
                    message.InitialPosition,
                    message.TargetPosition,
                    message.Direction);
                _clientChoreographySequenceByAgent[message.AgentIndex] = message.Sequence;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: client choreography apply failed. " +
                    "Agent=" + message.AgentIndex +
                    " Sequence=" + message.Sequence +
                    " Kind=" + message.Kind +
                    " Error=" + ex.Message + ".");
            }
        }

        private void PumpServerState(DateTime nowUtc)
        {
            if (_session.Phase == CoopHideoutBossPhase.InitialAssault)
            {
                TryPrepareBossPhase(nowUtc);
                return;
            }

            if (_session.Phase == CoopHideoutBossPhase.PreparingCinematic)
            {
                RemoveDisconnectedRequiredPeers();
                if (_requiredReadyPeerIndices.All(index => _readyPeerIndices.Contains(index)) ||
                    nowUtc >= _session.DeadlineUtc)
                {
                    BeginCinematic(nowUtc);
                }
                return;
            }

            if (_session.Phase == CoopHideoutBossPhase.Cinematic)
            {
                if (_campaignStagedPlacementActive &&
                    !_campaignApproachHeld &&
                    nowUtc >= _campaignApproachHoldDeadlineUtc)
                {
                    HoldCampaignBossFightApproachAtTargets();
                }

                if (nowUtc >= _session.DeadlineUtc)
                    BeginAwaitingHostChoice();
                return;
            }

            if (_session.Phase == CoopHideoutBossPhase.AwaitingHostChoice)
            {
                if (CoopHideoutBossPhaseContract.ShouldFallbackFromAwaitingHostChoice(
                        IsHostPeerAvailable()))
                {
                    StartAllBattle("host-unavailable-during-choice-fallback");
                }
                return;
            }

            if (_session.Phase == CoopHideoutBossPhase.Duel)
            {
                if (_hostAgent?.IsActive() != true || _bossAgent?.IsActive() != true)
                    CompleteBossPhase("duel-resolved");
                return;
            }

            if (_session.Phase == CoopHideoutBossPhase.AllBattle)
            {
                if (IsTeamDepleted(_playerTeam) || IsTeamDepleted(_enemyTeam))
                    CompleteBossPhase("all-battle-resolved");
            }
        }

        private void TryPrepareBossPhase(DateTime nowUtc)
        {
            if (CoopBattlePhaseRuntimeState.GetPhase() < CoopBattlePhase.BattleActive)
                return;

            NetworkCommunicator hostPeer = ResolveHostPeer();
            Agent currentHostAgent = ResolveControlledAgent(hostPeer);
            if (_authoritativeMainHeroAgent == null && currentHostAgent?.IsActive() == true)
                _authoritativeMainHeroAgent = currentHostAgent;

            CoopExactCampaignHideoutMissionController hideoutController =
                Mission.GetMissionBehavior<CoopExactCampaignHideoutMissionController>();
            Team playerTeam = _authoritativeMainHeroAgent?.IsActive() == true
                ? _authoritativeMainHeroAgent.Team
                : _playerTeam;
            if ((playerTeam == null || playerTeam == Team.Invalid) && hideoutController != null)
            {
                playerTeam = Mission.Teams.FirstOrDefault(team =>
                    team != null &&
                    team != Team.Invalid &&
                    team.Side == hideoutController.PlayerSide);
            }
            if (playerTeam == null || playerTeam == Team.Invalid)
                return;

            Team enemyTeam = ResolveEnemyTeam(playerTeam);
            if (enemyTeam == null)
                return;

            // Keep the authoritative team identities before the last initial defender is removed.
            // The reserved boss group must still be able to resolve its team when that team has
            // no active agents left in Mission.Agents.
            _playerTeam = playerTeam;
            _enemyTeam = enemyTeam;

            List<Agent> activePlayerAgents = GetActiveHumanAgents(playerTeam);
            if (!_playerSideEliminationTriggered &&
                CoopHideoutBossPhaseContract.ShouldFailHideoutWhenPlayerSideEliminated(
                    hideoutController?.HasInitialAssaultMaterialized == true,
                    activePlayerAgents.Count))
            {
                _playerSideEliminationTriggered = true;
                bool completed = CoopMissionSpawnLogic.TryForceAuthoritativeBattleCompletion(
                    Mission,
                    enemyTeam.Side,
                    CoopHideoutBossPhaseContract.PlayerSideEliminatedCompletionReason,
                    "hideout player side eliminated before boss phase");
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: player-side elimination resolved. " +
                    "WinnerSide=" + enemyTeam.Side +
                    " Completed=" + completed + ".");
                return;
            }

            if (hideoutController != null && !hideoutController.IsBossPhaseEligible)
                return;

            bool mainHeroActive = _authoritativeMainHeroAgent?.IsActive() == true;
            Agent cinematicPrincipal = mainHeroActive
                ? _authoritativeMainHeroAgent
                : SelectBossCinematicPrincipal(activePlayerAgents);
            if (cinematicPrincipal?.IsActive() != true)
                return;

            List<Agent> activeEnemies = GetActiveHumanAgents(enemyTeam);
            _initialEnemyCount = Math.Max(
                _initialEnemyCount,
                Math.Max(
                    activeEnemies.Count,
                    hideoutController?.InitialAssaultEnemyCount ?? 0));
            if ((nowUtc - _missionStartedUtc).TotalSeconds < InitialAssaultGraceSeconds)
                return;

            GameEntity bossFightEntity = TryResolveBossFightEntity();
            if (bossFightEntity == null)
            {
                if (!_bossFightEntityMissingLogged)
                {
                    _bossFightEntityMissingLogged = true;
                    ModLogger.Info(
                        "CoopHideoutBossPhaseController: boss phase disabled for this scene because entity tag is missing. " +
                        "Tag=" + CoopHideoutBossPhaseContract.BossFightEntityTag +
                        " Scene=" + (Mission.SceneName ?? "null") + ".");
                }
                return;
            }

            int activeInitialAssaultEnemies = activeEnemies.Count;
            Agent bossAgent;
            CoopExactCampaignHideoutAmbushMissionController nightAmbushController =
                hideoutController as CoopExactCampaignHideoutAmbushMissionController;
            if (nightAmbushController?.HasNightReservedBossGroup == true)
            {
                if (!CoopHideoutBossPhaseContract.ShouldSpawnReservedBossGroup(
                        _initialEnemyCount,
                        activeInitialAssaultEnemies,
                        cinematicPrincipal.IsActive(),
                        bossFightEntity != null))
                {
                    return;
                }

                if (!nightAmbushController.TrySpawnNightReservedBossGroup(
                        out bossAgent,
                        out int spawnedBossGroupCount))
                {
                    return;
                }

                activeEnemies = GetActiveHumanAgents(enemyTeam);
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: native-shaped night boss group entered the encounter. " +
                    "InitialAssaultEnemiesActive=" + activeInitialAssaultEnemies +
                    " SpawnedBossGroup=" + spawnedBossGroupCount +
                    " EnemyAgentsAfterSpawn=" + activeEnemies.Count + ".");
            }
            else if (hideoutController?.HasReservedBossGroup == true)
            {
                if (!CoopHideoutBossPhaseContract.ShouldSpawnReservedBossGroup(
                        _initialEnemyCount,
                        activeInitialAssaultEnemies,
                        cinematicPrincipal.IsActive(),
                        bossFightEntity != null))
                {
                    return;
                }

                if (!hideoutController.TrySpawnReservedBossGroup(
                        out bossAgent,
                        out int spawnedBossGroupCount))
                {
                    return;
                }

                activeEnemies = GetActiveHumanAgents(enemyTeam);
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: reserved boss group entered the encounter. " +
                    "InitialAssaultEnemiesActive=" + activeInitialAssaultEnemies +
                    " SpawnedBossGroup=" + spawnedBossGroupCount +
                    " EnemyAgentsAfterSpawn=" + activeEnemies.Count + ".");
            }
            else
            {
                if (!CoopHideoutBossPhaseContract.ShouldPrepareBossPhase(
                        _initialEnemyCount,
                        activeEnemies.Count,
                        cinematicPrincipal.IsActive(),
                        bossFightEntity != null))
                {
                    return;
                }

                bossAgent = SelectBossAgent(activeEnemies);
            }
            if (bossAgent == null)
                return;

            _playerTeam = playerTeam;
            _enemyTeam = enemyTeam;
            _hostAgent = cinematicPrincipal;
            _bossAgent = bossAgent;
            _session.HostPeerIndex = _authoritativeHostPeerIndex;
            _session.HostAgentIndex = cinematicPrincipal.Index;
            _session.BossAgentIndex = bossAgent.Index;
            _missionModeBeforeBossPhase = Mission.Mode;
            _autoStartAllBattleAfterCinematic =
                CoopHideoutBossPhaseContract.ShouldAutoStartAllBattleAfterBossCinematic(
                    mainHeroActive,
                    cinematicPrincipal.IsActive());

            FreezeCombatantsForCinematic();
            CaptureRequiredReadyPeers();
            string rejection;
            CoopHideoutBossPhaseContract.TryTransition(
                _session,
                CoopHideoutBossPhase.PreparingCinematic,
                nowUtc.AddMilliseconds(CoopHideoutBossPhaseContract.CinematicReadyTimeoutMilliseconds),
                "boss-cinematic-prepare",
                out rejection);
            BroadcastState(CoopHideoutBossPhaseContract.CinematicReadyTimeoutMilliseconds);
            ModLogger.Info(
                "CoopHideoutBossPhaseController: boss cinematic preparation started. " +
                "InitialEnemyCount=" + _initialEnemyCount +
                " ActiveInitialAssaultEnemyCount=" + activeInitialAssaultEnemies +
                " ActiveEnemyCountAfterReserve=" + activeEnemies.Count +
                " ReservedTriggerCount=" + CoopHideoutBossPhaseContract.ResolveReservedBossTriggerCount(_initialEnemyCount) +
                " HostPeer=" + _session.HostPeerIndex +
                " MainHeroActive=" + mainHeroActive +
                " CinematicPrincipal=" + cinematicPrincipal.Index +
                " AutoAllBattle=" + _autoStartAllBattleAfterCinematic +
                " BossAgent=" + bossAgent.Index +
                " RequiredReadyPeers=" + _requiredReadyPeerIndices.Count + ".");
        }

        private void BeginCinematic(DateTime nowUtc)
        {
            _campaignApproachHeld = false;
            _campaignApproachHoldDeadlineUtc = DateTime.MaxValue;
            if (!TryPlaceBossFightParticipants())
            {
                StartAllBattle("boss-placement-failed-fallback");
                return;
            }

            int cinematicDurationMilliseconds =
                CoopHideoutBossPhaseContract.ResolveCinematicDurationMilliseconds(
                    _campaignStagedPlacementActive);
            if (_campaignStagedPlacementActive)
            {
                _campaignApproachHoldDeadlineUtc = nowUtc.AddMilliseconds(
                    CoopHideoutBossPhaseContract.ResolveCampaignBossApproachHoldMilliseconds(
                        _campaignAuthoredWalkDistance));
            }
            string rejection;
            if (!CoopHideoutBossPhaseContract.TryTransition(
                    _session,
                    CoopHideoutBossPhase.Cinematic,
                    nowUtc.AddMilliseconds(cinematicDurationMilliseconds),
                    "boss-cinematic-start",
                    out rejection))
            {
                StartAllBattle("boss-cinematic-transition-failed");
                return;
            }
            BroadcastState(cinematicDurationMilliseconds);
        }

        private void BeginAwaitingHostChoice()
        {
            if (_autoStartAllBattleAfterCinematic)
            {
                FinalizeCampaignBossFightApproach();
                StartAllBattle("main-hero-unavailable-auto-all-battle");
                return;
            }

            if (!IsHostPeerAvailable())
            {
                StartAllBattle("host-unavailable-before-choice");
                return;
            }

            if (!TryEnterBossConversationMissionMode())
            {
                StartAllBattle("boss-conversation-mode-failed-fallback");
                return;
            }

            FinalizeCampaignBossFightApproach();

            string rejection;
            if (!CoopHideoutBossPhaseContract.TryTransition(
                    _session,
                    CoopHideoutBossPhase.AwaitingHostChoice,
                    DateTime.MaxValue,
                    "awaiting-host-choice",
                    out rejection))
            {
                StartAllBattle("host-choice-transition-failed");
                return;
            }
            BroadcastState(0);
        }

        private void StartDuel(string reason)
        {
            if (_session == null || _hostAgent?.IsActive() != true || _bossAgent?.IsActive() != true)
            {
                StartAllBattle("duel-participant-missing-fallback");
                return;
            }

            LogAllTrackedChoreographyDiagnosticSnapshots(
                "server-duel-before",
                _choreographySequence + 1);
            SetTeamsAsEnemies(_playerTeam, _enemyTeam, true);
            RestoreMissionMode();
            foreach (FrozenAgentState frozen in _frozenAgentStates.Values)
            {
                Agent agent = frozen.Agent;
                if (agent?.IsActive() != true)
                    continue;

                if (ReferenceEquals(agent, _hostAgent))
                {
                    RestoreCombatAgent(frozen, restoreTeam: true);
                    continue;
                }
                if (ReferenceEquals(agent, _bossAgent))
                {
                    RestoreDuelBossAgent(frozen);
                    LogChoreographyDiagnosticSnapshot(
                        agent,
                        "server-duel-boss-restored",
                        _choreographySequence + 1);
                    continue;
                }

                agent.SetMortalityState(Agent.MortalityState.Invulnerable);
                if (agent.Team != Team.Invalid)
                    agent.SetTeam(Team.Invalid, sync: true);
                agent.SetLookAgent(frozen.OriginalTeam == _playerTeam ? _hostAgent : _bossAgent);
                FreezeAgentAfterCampaignBossApproach(agent);
            }

            ReactivateReleasedCampaignBossFightAi(_bossAgent);
            ApplyDuelBossPreferredTarget(_bossAgent, _hostAgent);
            string rejection;
            if (!CoopHideoutBossPhaseContract.TryTransition(
                    _session,
                    CoopHideoutBossPhase.Duel,
                    DateTime.MaxValue,
                    reason,
                    out rejection))
            {
                StartAllBattle("duel-transition-failed");
                return;
            }
            BroadcastReleasedAgentChoreography(CoopHideoutBossPhase.Duel);
            LogAllTrackedChoreographyDiagnosticSnapshots(
                "server-duel-after-release",
                _choreographySequence);
            StartAllTrackedChoreographyDiagnosticSampleWindows(
                "server-duel",
                _choreographySequence);
            BroadcastState(0);
        }

        private void StartAllBattle(string reason)
        {
            if (_session == null ||
                _session.Phase == CoopHideoutBossPhase.AllBattle ||
                _session.Phase == CoopHideoutBossPhase.Completed)
            {
                return;
            }

            foreach (FrozenAgentState frozen in _frozenAgentStates.Values)
                RestoreCombatAgent(frozen, restoreTeam: true);

            SetTeamsAsEnemies(_playerTeam, _enemyTeam, true);
            RestoreMissionMode();
            AttachUnformedBossFightAgentsToCombatFormations(_enemyTeam);
            RestoreFormationAiForBossPhase(CoopHideoutBossPhase.AllBattle);
            OrderTeamToCharge(_playerTeam);
            OrderTeamToCharge(_enemyTeam);
            foreach (FrozenAgentState frozen in _frozenAgentStates.Values)
                ReactivateReleasedCampaignBossFightAi(frozen?.Agent);
            ClearDuelBossPreferredTarget(_bossAgent);

            string rejection;
            if (!CoopHideoutBossPhaseContract.TryTransition(
                    _session,
                    CoopHideoutBossPhase.AllBattle,
                    DateTime.MaxValue,
                    reason,
                    out rejection))
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: all-battle transition rejected. " +
                    "Phase=" + _session.Phase + " Reason=" + rejection + ".");
                return;
            }
            _session.Choice = CoopHideoutBossChoice.AllBattle;
            BroadcastReleasedAgentChoreography(CoopHideoutBossPhase.AllBattle);
            BroadcastState(0);
        }

        private void CompleteBossPhase(string reason)
        {
            if (_session == null || _session.Phase == CoopHideoutBossPhase.Completed)
                return;

            string rejection;
            if (!CoopHideoutBossPhaseContract.TryTransition(
                    _session,
                    CoopHideoutBossPhase.Completed,
                    DateTime.MaxValue,
                    reason,
                    out rejection))
            {
                return;
            }
            BroadcastState(0);
            if (!_phaseCompletionLogged)
            {
                _phaseCompletionLogged = true;
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: boss phase completed. " +
                    "Choice=" + _session.Choice + " Reason=" + reason + ".");
            }
        }

        private void FreezeCombatantsForCinematic()
        {
            _frozenAgentStates.Clear();
            _frozenFormationStates.Clear();
            _targetPlacements.Clear();
            _campaignStagedPlacementActive =
                Mission.GetMissionBehavior<CoopExactCampaignHideoutMissionController>() != null;
            if (CoopHideoutBossPhaseContract.ShouldStopFormationsForCampaignBossCinematic(
                    _campaignStagedPlacementActive))
            {
                StopTeamFormationsForCinematic(_playerTeam);
                StopTeamFormationsForCinematic(_enemyTeam);
            }

            foreach (Agent agent in Mission.Agents)
            {
                if (agent?.IsActive() != true || !agent.IsHuman)
                    continue;

                _frozenAgentStates[agent.Index] = new FrozenAgentState(
                    agent,
                    agent.Team,
                    agent.Formation,
                    agent.Controller,
                    agent.CurrentMortalityState,
                    agent.IsPaused);
                agent.SetMortalityState(Agent.MortalityState.Invulnerable);
                bool isBossFightParticipant =
                    ReferenceEquals(agent.Team, _playerTeam) ||
                    ReferenceEquals(agent.Team, _enemyTeam);
                if (CoopHideoutBossPhaseContract.ShouldDetachAgentForCampaignBossCinematic(
                        _campaignStagedPlacementActive,
                        agent.IsAIControlled && isBossFightParticipant) &&
                    agent.Formation != null)
                {
                    agent.Formation = null;
                }
                if (!_campaignStagedPlacementActive || !isBossFightParticipant)
                    ScriptAgentAtCurrentPosition(agent);
            }

            SetTeamsAsEnemies(_playerTeam, _enemyTeam, false);
            Mission.SetMissionMode(MissionMode.CutScene, false);
        }

        private bool TryPlaceBossFightParticipants()
        {
            GameEntity entity = TryResolveBossFightEntity();
            if (entity == null || _hostAgent?.IsActive() != true || _bossAgent?.IsActive() != true)
                return false;

            MatrixFrame anchor = entity.GetGlobalFrame();
            List<Agent> playerAgents = GetActiveHumanAgents(_playerTeam)
                .OrderBy(agent => ReferenceEquals(agent, _hostAgent) ? 0 : 1)
                .ThenBy(agent => agent.Index)
                .ToList();
            List<Agent> enemyAgents = GetActiveHumanAgents(_enemyTeam)
                .OrderBy(agent => ReferenceEquals(agent, _bossAgent) ? 0 : 1)
                .ThenBy(agent => agent.Index)
                .ToList();
            if (playerAgents.Count == 0 || enemyAgents.Count == 0)
                return false;

            Vec2 playerFacingDirection = anchor.rotation.f.AsVec2;
            if (playerFacingDirection.LengthSquared < 0.0001f)
                playerFacingDirection = new Vec2(0f, 1f);
            playerFacingDirection.Normalize();
            Vec2 enemyFacingDirection = playerFacingDirection * -1f;

            CoopHideoutSceneManifestRuntime.TryResolve(
                Mission.SceneName,
                out CoopHideoutSceneManifest sceneManifest,
                out string manifestDiagnostics);
            CoopHideoutBossFightManifest bossFightManifest = sceneManifest?.BossFight;
            _campaignStagedPlacementActive =
                Mission.GetMissionBehavior<CoopExactCampaignHideoutMissionController>() != null;
            float authoredInnerRadius = bossFightManifest?.InnerRadius ?? DefaultInnerRadius;
            float innerRadius = CoopHideoutBossPhaseContract.ResolveBossDialogueInnerRadius(
                authoredInnerRadius,
                _campaignStagedPlacementActive);
            float outerRadius = bossFightManifest?.OuterRadius ?? DefaultOuterRadius;
            float walkDistance = bossFightManifest?.WalkDistance ?? DefaultWalkDistance;
            _campaignAuthoredWalkDistance = _campaignStagedPlacementActive
                ? Math.Max(0f, walkDistance)
                : 0f;
            CoopHideoutBossPrincipalPlacement principal =
                CoopHideoutBossPhaseContract.ResolvePrincipalPlacement(
                    innerRadius,
                    _campaignStagedPlacementActive ? walkDistance : 0f);

            CoopHideoutBossPrincipalPerturbation playerPerturbation =
                _campaignStagedPlacementActive
                    ? CoopHideoutBossPhaseContract.ResolveNativePrincipalPerturbation(
                        seedOffset: 0,
                        perturbAmount:
                            CoopHideoutBossPhaseContract.NativePrincipalPlacementPerturbation)
                    : new CoopHideoutBossPrincipalPerturbation();
            CoopHideoutBossPrincipalPerturbation bossPerturbation =
                _campaignStagedPlacementActive
                    ? CoopHideoutBossPhaseContract.ResolveNativePrincipalPerturbation(
                        seedOffset: 1,
                        perturbAmount:
                            CoopHideoutBossPhaseContract.NativePrincipalPlacementPerturbation)
                    : new CoopHideoutBossPrincipalPerturbation();

            Vec3 playerPrincipalInitialPosition = BuildLocalOffsetPosition(
                anchor,
                principal.PlayerInitialForwardOffset + playerPerturbation.ForwardOffset,
                playerPerturbation.SideOffset);
            Vec3 playerPrincipalTargetPosition = BuildLocalOffsetPosition(
                anchor,
                principal.PlayerTargetForwardOffset + playerPerturbation.ForwardOffset,
                playerPerturbation.SideOffset);
            Vec3 bossPrincipalInitialPosition = BuildLocalOffsetPosition(
                anchor,
                principal.BossInitialForwardOffset + bossPerturbation.ForwardOffset,
                bossPerturbation.SideOffset);
            Vec3 bossPrincipalTargetPosition = BuildLocalOffsetPosition(
                anchor,
                principal.BossTargetForwardOffset + bossPerturbation.ForwardOffset,
                bossPerturbation.SideOffset);

            var placements = new List<BossFightParticipantPlacement>
            {
                CreateParticipantPlacement(
                    _hostAgent,
                    playerPrincipalInitialPosition,
                    playerPrincipalTargetPosition,
                    playerFacingDirection),
                CreateParticipantPlacement(
                    _bossAgent,
                    bossPrincipalInitialPosition,
                    bossPrincipalTargetPosition,
                    enemyFacingDirection)
            };
            List<Agent> playerCompanions = playerAgents
                .Where(agent => !ReferenceEquals(agent, _hostAgent))
                .ToList();
            List<Agent> bossCompanions = enemyAgents
                .Where(agent => !ReferenceEquals(agent, _bossAgent))
                .ToList();
            if (_campaignStagedPlacementActive)
            {
                AppendNativeCampaignCompanionPlacements(
                    placements,
                    playerCompanions,
                    playerPrincipalInitialPosition,
                    isPlayerSide: true,
                    facingDirection: playerFacingDirection);
                AppendNativeCampaignCompanionPlacements(
                    placements,
                    bossCompanions,
                    bossPrincipalInitialPosition,
                    isPlayerSide: false,
                    facingDirection: enemyFacingDirection);
            }
            else
            {
                AppendAgentArcPlacements(
                    placements,
                    playerCompanions,
                    anchor,
                    (float)Math.PI,
                    outerRadius,
                    initialForwardOffset: 0f,
                    playerFacingDirection);
                AppendAgentArcPlacements(
                    placements,
                    bossCompanions,
                    anchor,
                    0f,
                    outerRadius,
                    initialForwardOffset: 0f,
                    enemyFacingDirection);
            }

            InitializeServerChoreographyDiagnosticsTracking(placements, anchor);

            bool previousTeleportingAgents = Mission.IsTeleportingAgents;
            try
            {
                if (!TryAssignHostControllerForCampaignBossCinematic())
                    return false;

                Mission.IsTeleportingAgents = true;
                foreach (BossFightParticipantPlacement placement in placements)
                {
                    _targetPlacements[placement.Agent.Index] = placement;
                    PlaceAgentAtInitialPosition(placement);
                }
                Mission.IsTeleportingAgents = false;
                int choreographySequence = _campaignStagedPlacementActive
                    ? NextChoreographySequence()
                    : 0;
                foreach (BossFightParticipantPlacement placement in placements)
                {
                    StartAgentApproach(placement);
                    if (IsNetworkChoreographyAgent(placement))
                    {
                        BroadcastAgentChoreography(
                            CoopHideoutBossAgentChoreographyKind.StartApproach,
                            placement,
                            choreographySequence);
                    }
                }
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: placed boss-fight participants. " +
                    "CampaignStaged=" + _campaignStagedPlacementActive +
                    " AuthoredInnerRadius=" + authoredInnerRadius +
                    " EffectiveInnerRadius=" + innerRadius +
                    " OuterRadius=" + outerRadius +
                    " AuthoredWalkDistance=" +
                    (_campaignStagedPlacementActive ? walkDistance : 0f) +
                    " BossApproachDistance=" +
                    (_campaignStagedPlacementActive
                        ? CoopHideoutBossPhaseContract.ResolveCampaignBossApproachDistance(walkDistance)
                        : 0f) +
                    " CinematicDurationMilliseconds=" +
                    CoopHideoutBossPhaseContract.ResolveCinematicDurationMilliseconds(
                        _campaignStagedPlacementActive) +
                    " ApproachHoldMilliseconds=" +
                    (_campaignStagedPlacementActive
                        ? CoopHideoutBossPhaseContract.ResolveCampaignBossApproachHoldMilliseconds(
                            walkDistance)
                        : 0) +
                    " Layout=" +
                    (_campaignStagedPlacementActive ? "native-triangular-rows" : "fallback-radial-arc") +
                    " Count=" + placements.Count +
                    " Manifest={" + manifestDiagnostics + "}.");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: participant placement failed. Error=" + ex.Message);
                return false;
            }
            finally
            {
                Mission.IsTeleportingAgents = previousTeleportingAgents;
            }
        }

        private void AppendAgentArcPlacements(
            List<BossFightParticipantPlacement> placements,
            List<Agent> agents,
            MatrixFrame anchor,
            float baseAngle,
            float radius,
            float initialForwardOffset,
            Vec2 facingDirection)
        {
            if (placements == null || agents == null)
                return;

            for (int i = 0; i < agents.Count; i++)
            {
                float angle = CoopHideoutBossPhaseContract.ResolveCompanionPlacementAngle(
                    i,
                    baseAngle,
                    CoopHideoutBossPhaseContract.NativeCompanionPlacementAngleStep);
                Vec3 targetPosition = BuildRadialPosition(anchor, angle, radius);
                Vec3 initialPosition = AddForwardOffset(
                    anchor,
                    targetPosition,
                    initialForwardOffset);
                placements.Add(CreateParticipantPlacement(
                    agents[i],
                    initialPosition,
                    targetPosition,
                    facingDirection));
            }
        }

        private void AppendNativeCampaignCompanionPlacements(
            List<BossFightParticipantPlacement> placements,
            List<Agent> agents,
            Vec3 principalInitialPosition,
            bool isPlayerSide,
            Vec2 facingDirection)
        {
            if (placements == null || agents == null)
                return;

            for (int i = 0; i < agents.Count; i++)
            {
                CoopHideoutBossCompanionPlacement companionPlacement =
                    CoopHideoutBossPhaseContract.ResolveNativeCompanionPlacement(
                        isPlayerSide,
                        agents.Count,
                        i);
                if (companionPlacement == null)
                    continue;

                Vec3 initialPosition = principalInitialPosition + new Vec3(
                    companionPlacement.InitialOffsetX,
                    companionPlacement.InitialOffsetY,
                    0f);
                Vec3 targetPosition = principalInitialPosition + new Vec3(
                    companionPlacement.TargetOffsetX,
                    companionPlacement.TargetOffsetY,
                    0f);
                placements.Add(CreateParticipantPlacement(
                    agents[i],
                    initialPosition,
                    targetPosition,
                    facingDirection));
            }
        }

        private BossFightParticipantPlacement CreateParticipantPlacement(
            Agent agent,
            Vec3 initialPosition,
            Vec3 targetPosition,
            Vec2 facingDirection)
        {
            return new BossFightParticipantPlacement(
                agent,
                ResolveGroundPosition(initialPosition),
                ResolveGroundPosition(targetPosition),
                facingDirection);
        }

        private void PlaceAgentAtInitialPosition(BossFightParticipantPlacement placement)
        {
            Agent agent = placement?.Agent;
            if (agent?.IsActive() != true)
                return;

            bool shouldApproach =
                _campaignStagedPlacementActive &&
                agent.IsAIControlled;
            Vec3 groundPosition = shouldApproach
                ? placement.InitialGroundPosition
                : placement.TargetGroundPosition;
            agent.MountAgent?.TeleportToPosition(groundPosition);
            agent.TeleportToPosition(groundPosition);
            Vec2 direction = placement.FacingDirection;
            if (direction.LengthSquared < 0.0001f)
                direction = new Vec2(0f, 1f);
            direction.Normalize();
            agent.LookDirection = new Vec3(direction.x, direction.y, 0f);
            agent.SetMovementDirection(in direction);
            if (CoopHideoutBossPhaseContract.ShouldDetachAgentForCampaignBossCinematic(
                    _campaignStagedPlacementActive,
                    agent.IsAIControlled) &&
                agent.Formation != null)
            {
                agent.Formation = null;
            }
        }

        private void StartAgentApproach(BossFightParticipantPlacement placement)
        {
            Agent agent = placement?.Agent;
            if (agent?.IsActive() != true || !agent.IsAIControlled)
                return;

            LogChoreographyDiagnosticSnapshot(
                agent,
                "server-start-approach-before",
                _choreographySequence,
                placement);
            bool shouldApproach = _campaignStagedPlacementActive;
            Vec2 direction = placement.FacingDirection;
            if (direction.LengthSquared < 0.0001f)
                direction = new Vec2(0f, 1f);
            direction.Normalize();
            Vec3 targetGroundPosition = placement.TargetGroundPosition;
            var targetWorldPosition = new WorldPosition(
                Mission.Scene,
                UIntPtr.Zero,
                targetGroundPosition,
                hasValidZ: false);
            if (shouldApproach)
            {
                agent.SetMaximumSpeedLimit(NativeApproachSpeedLimit, isMultiplier: false);
                agent.SetScriptedPositionAndDirection(
                    ref targetWorldPosition,
                    direction.RotationInRadians,
                    addHumanLikeDelay: true);
            }
            else
            {
                agent.SetScriptedPositionAndDirection(
                    ref targetWorldPosition,
                    direction.RotationInRadians,
                    addHumanLikeDelay: false);
            }

            LogChoreographyDiagnosticSnapshot(
                agent,
                "server-start-approach-after",
                _choreographySequence,
                placement);
        }

        private bool IsNetworkChoreographyAgent(BossFightParticipantPlacement placement)
        {
            Agent agent = placement?.Agent;
            if (!_campaignStagedPlacementActive ||
                agent?.IsActive() != true ||
                ReferenceEquals(agent, _hostAgent) ||
                agent.MissionPeer != null ||
                agent.IsPlayerControlled)
            {
                return false;
            }

            if (agent.IsAIControlled ||
                _nativeControllerDetachedAgentIndices.Contains(agent.Index))
            {
                return true;
            }

            return _frozenAgentStates.TryGetValue(agent.Index, out FrozenAgentState frozen) &&
                   ReferenceEquals(frozen.Agent, agent) &&
                   frozen.OriginalController == AgentControllerType.AI;
        }

        private int NextChoreographySequence()
        {
            if (_choreographySequence == int.MaxValue)
                _choreographySequence = 0;
            return ++_choreographySequence;
        }

        private void ApplyAgentChoreographyLocally(
            Agent agent,
            CoopHideoutBossAgentChoreographyKind kind,
            Vec3 initialPosition,
            Vec3 targetPosition,
            Vec2 direction)
        {
            if (agent?.IsActive() != true)
                return;

            direction = NormalizeDirection(direction);
            BossFightParticipantPlacement diagnosticPlacement =
                UpdateTrackedChoreographyDiagnosticPlacement(
                    agent,
                    initialPosition,
                    targetPosition,
                    direction);
            LogChoreographyDiagnosticSnapshot(
                agent,
                kind + "-apply-before",
                _choreographySequence,
                diagnosticPlacement);
            Vec3 lookDirection = new Vec3(direction.x, direction.y, 0f);
            if (kind == CoopHideoutBossAgentChoreographyKind.StartApproach)
            {
                agent.ClearTargetFrame();
                agent.DisableScriptedMovement();
                if (agent.Formation != null)
                    agent.Formation = null;
                agent.MountAgent?.TeleportToPosition(initialPosition);
                agent.TeleportToPosition(initialPosition);
                agent.LookDirection = lookDirection;
                agent.SetMovementDirection(in direction);
                agent.MovementInputVector = Vec2.Zero;
                agent.MovementFlags = Agent.MovementControlFlag.None;
                agent.SetMaximumSpeedLimit(NativeApproachSpeedLimit, isMultiplier: false);
                if (agent.IsAIControlled)
                    agent.SetIsAIPaused(false);

                var targetWorldPosition = new WorldPosition(
                    Mission.Scene,
                    UIntPtr.Zero,
                    targetPosition,
                    hasValidZ: false);
                agent.SetScriptedPositionAndDirection(
                    ref targetWorldPosition,
                    direction.RotationInRadians,
                    addHumanLikeDelay: true);
                LogChoreographyDiagnosticSnapshot(
                    agent,
                    kind + "-apply-after",
                    _choreographySequence,
                    diagnosticPlacement);
                return;
            }

            if (kind == CoopHideoutBossAgentChoreographyKind.HoldAtTarget)
            {
                agent.MountAgent?.TeleportToPosition(targetPosition);
                agent.TeleportToPosition(targetPosition);
                agent.LookDirection = lookDirection;
                agent.SetMovementDirection(in direction);
                agent.MovementInputVector = Vec2.Zero;
                agent.MovementFlags = Agent.MovementControlFlag.None;
                FreezeAgentAfterCampaignBossApproach(agent);
                LogChoreographyDiagnosticSnapshot(
                    agent,
                    kind + "-apply-after",
                    _choreographySequence,
                    diagnosticPlacement);
                StartChoreographyDiagnosticSampleWindow(
                    agent,
                    diagnosticPlacement,
                    "hold",
                    _choreographySequence);
                return;
            }

            RestoreDetachedNativeControllerForChoreography(agent, kind);
            agent.ClearTargetFrame();
            agent.DisableScriptedMovement();
            agent.MovementInputVector = Vec2.Zero;
            agent.MovementFlags = Agent.MovementControlFlag.None;
            agent.SetMaximumSpeedLimit(-1f, isMultiplier: false);
            if (agent.IsAIControlled)
            {
                agent.SetIsAIPaused(false);
                agent.SetAutomaticTargetSelection(true);
                agent.SetFiringOrder(FiringOrder.RangedWeaponUsageOrderEnum.FireAtWill);
                agent.SetWatchState(Agent.WatchState.Alarmed);
                agent.ResetEnemyCaches();
                agent.HumanAIComponent?.SyncBehaviorParamsIfNecessary();
                agent.ForceAiBehaviorSelection();
            }
            LogChoreographyDiagnosticSnapshot(
                agent,
                kind + "-apply-after",
                _choreographySequence,
                diagnosticPlacement);
            StartChoreographyDiagnosticSampleWindow(
                agent,
                diagnosticPlacement,
                "release",
                _choreographySequence);
        }

        private void FreezeAgentAfterCampaignBossApproach(Agent agent)
        {
            if (agent?.IsActive() != true)
                return;

            agent.ClearTargetFrame();
            agent.DisableScriptedMovement();
            agent.SetMaximumSpeedLimit(-1f, isMultiplier: false);

            bool shouldPause =
                CoopHideoutBossPhaseContract.ShouldPauseAiForCampaignBossChoreography(
                    _campaignStagedPlacementActive,
                    agent.IsAIControlled,
                    CoopHideoutBossAgentChoreographyKind.HoldAtTarget);
            bool shouldDetachNativeController =
                CoopHideoutBossPhaseContract.ShouldDetachNativeControllerForCampaignBossHold(
                    _campaignStagedPlacementActive,
                    agent.IsAIControlled,
                    agent.MissionPeer != null,
                    ReferenceEquals(agent, _hostAgent));
            if (shouldPause)
                agent.SetIsAIPaused(true);
            if (shouldDetachNativeController)
            {
                agent.Controller = AgentControllerType.None;
                _nativeControllerDetachedAgentIndices.Add(agent.Index);
            }

            agent.MovementInputVector = Vec2.Zero;
            agent.MovementFlags = Agent.MovementControlFlag.None;
        }

        private void RestoreDetachedNativeControllerForChoreography(
            Agent agent,
            CoopHideoutBossAgentChoreographyKind kind)
        {
            if (agent == null)
                return;

            bool wasDetached = _nativeControllerDetachedAgentIndices.Contains(agent.Index);
            if (!CoopHideoutBossPhaseContract.ShouldRestoreDetachedNativeControllerForChoreography(
                    wasDetached,
                    kind))
            {
                return;
            }

            _nativeControllerDetachedAgentIndices.Remove(agent.Index);
            if (agent.IsActive() && agent.Controller == AgentControllerType.None)
                agent.Controller = AgentControllerType.AI;
        }

        private void BroadcastAgentChoreography(
            CoopHideoutBossAgentChoreographyKind kind,
            BossFightParticipantPlacement placement,
            int sequence)
        {
            if (!GameNetwork.IsServer ||
                !GameNetwork.IsSessionActive ||
                _session == null ||
                placement?.Agent == null)
            {
                return;
            }

            bool broadcastStarted = false;
            try
            {
                GameNetwork.BeginBroadcastModuleEvent();
                broadcastStarted = true;
                GameNetwork.WriteMessage(new CoopHideoutBossAgentChoreographyMessage(
                    _session.BattleInstanceId,
                    sequence,
                    kind,
                    placement.Agent.Index,
                    placement.InitialGroundPosition,
                    placement.TargetGroundPosition,
                    placement.FacingDirection));
                GameNetwork.EndBroadcastModuleEvent(
                    GameNetwork.EventBroadcastFlags.AddToMissionRecord);
                broadcastStarted = false;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: choreography broadcast failed. " +
                    "Agent=" + placement.Agent.Index +
                    " Sequence=" + sequence +
                    " Kind=" + kind +
                    " Error=" + ex.Message + ".");
            }
            finally
            {
                if (broadcastStarted)
                {
                    try
                    {
                        GameNetwork.EndBroadcastModuleEvent(
                            GameNetwork.EventBroadcastFlags.AddToMissionRecord);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void SendAgentChoreography(
            NetworkCommunicator peer,
            CoopHideoutBossAgentChoreographyKind kind,
            BossFightParticipantPlacement placement,
            int sequence)
        {
            if (!GameNetwork.IsServer ||
                peer == null ||
                _session == null ||
                placement?.Agent == null)
            {
                return;
            }

            bool eventStarted = false;
            try
            {
                GameNetwork.BeginModuleEventAsServer(peer);
                eventStarted = true;
                GameNetwork.WriteMessage(new CoopHideoutBossAgentChoreographyMessage(
                    _session.BattleInstanceId,
                    sequence,
                    kind,
                    placement.Agent.Index,
                    placement.InitialGroundPosition,
                    placement.TargetGroundPosition,
                    placement.FacingDirection));
                GameNetwork.EndModuleEventAsServer();
                eventStarted = false;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: targeted choreography send failed. " +
                    "PeerIndex=" + peer.Index +
                    " Agent=" + placement.Agent.Index +
                    " Sequence=" + sequence +
                    " Kind=" + kind +
                    " Error=" + ex.Message + ".");
            }
            finally
            {
                if (eventStarted)
                {
                    try
                    {
                        GameNetwork.EndModuleEventAsServer();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void SendCurrentAgentChoreography(NetworkCommunicator peer)
        {
            if (!_campaignStagedPlacementActive ||
                peer == null ||
                _session == null ||
                _targetPlacements.Count == 0)
            {
                return;
            }

            int sequence = Math.Max(1, _choreographySequence);
            foreach (BossFightParticipantPlacement placement in _targetPlacements.Values)
            {
                if (!IsNetworkChoreographyAgent(placement))
                    continue;

                CoopHideoutBossAgentChoreographyKind kind;
                if (_session.Phase == CoopHideoutBossPhase.Cinematic && !_campaignApproachHeld)
                {
                    kind = CoopHideoutBossAgentChoreographyKind.StartApproach;
                }
                else if (CoopHideoutBossPhaseContract.ShouldReleaseAgentForBossChoice(
                             _session.Phase,
                             ReferenceEquals(placement.Agent, _bossAgent)))
                {
                    kind = CoopHideoutBossAgentChoreographyKind.Release;
                }
                else
                {
                    kind = CoopHideoutBossAgentChoreographyKind.HoldAtTarget;
                }

                SendAgentChoreography(peer, kind, placement, sequence);
            }
        }

        private static Vec2 NormalizeDirection(Vec2 direction)
        {
            if (direction.LengthSquared < 0.0001f)
                direction = new Vec2(0f, 1f);
            direction.Normalize();
            return direction;
        }

        private sealed class BossFightParticipantPlacement
        {
            internal Agent Agent { get; }
            internal Vec3 InitialGroundPosition { get; }
            internal Vec3 TargetGroundPosition { get; }
            internal Vec2 FacingDirection { get; }

            internal BossFightParticipantPlacement(
                Agent agent,
                Vec3 initialGroundPosition,
                Vec3 targetGroundPosition,
                Vec2 facingDirection)
            {
                Agent = agent;
                InitialGroundPosition = initialGroundPosition;
                TargetGroundPosition = targetGroundPosition;
                FacingDirection = facingDirection;
            }
        }

        private Vec3 BuildLocalOffsetPosition(
            MatrixFrame anchor,
            float forwardOffset,
            float sideOffset)
        {
            Vec2 forward = anchor.rotation.f.AsVec2;
            if (forward.LengthSquared < 0.0001f)
                forward = new Vec2(0f, 1f);
            forward.Normalize();
            Vec2 side = new Vec2(forward.y, -forward.x);
            return anchor.origin + new Vec3(
                forward.x * forwardOffset + side.x * sideOffset,
                forward.y * forwardOffset + side.y * sideOffset,
                0f);
        }

        private static Vec3 AddForwardOffset(
            MatrixFrame anchor,
            Vec3 position,
            float forwardOffset)
        {
            Vec2 forward = anchor.rotation.f.AsVec2;
            if (forward.LengthSquared < 0.0001f)
                forward = new Vec2(0f, 1f);
            forward.Normalize();
            return position + new Vec3(
                forward.x * forwardOffset,
                forward.y * forwardOffset,
                0f);
        }

        private Vec3 BuildRadialPosition(MatrixFrame anchor, float angle, float radius)
        {
            Vec2 forward2 = anchor.rotation.f.AsVec2;
            if (forward2.LengthSquared < 0.0001f)
                forward2 = new Vec2(0f, 1f);
            forward2.Normalize();
            Vec2 side2 = new Vec2(forward2.y, -forward2.x);
            float sin = (float)Math.Sin(angle);
            float cos = (float)Math.Cos(angle);
            Vec2 offset = forward2 * (cos * radius) + side2 * (sin * radius);
            return anchor.origin + new Vec3(offset.x, offset.y, 0f);
        }

        private Vec3 ResolveGroundPosition(Vec3 position)
        {
            Vec2 point = position.AsVec2;
            float height = Mission.Scene.GetTerrainHeight(point);
            Mission.Scene.GetHeightAtPoint(point, BodyFlags.None, ref height);
            return new Vec3(point, height);
        }

        private void HoldCampaignBossFightApproachAtTargets()
        {
            if (_campaignApproachHeld ||
                !_campaignStagedPlacementActive ||
                _targetPlacements.Count == 0)
            {
                return;
            }

            int sequence = NextChoreographySequence();
            int heldAgentCount = 0;
            int failedAgentCount = 0;
            int enemyAgentCount = 0;
            float enemyTravelledTotal = 0f;
            float enemyTravelledMinimum = float.MaxValue;
            float enemyTravelledMaximum = 0f;
            float enemyRemainingTotal = 0f;
            float enemyRemainingMinimum = float.MaxValue;
            float enemyRemainingMaximum = 0f;
            string firstFailure = string.Empty;
            bool previousTeleportingAgents = Mission.IsTeleportingAgents;
            try
            {
                Mission.IsTeleportingAgents = true;
                foreach (BossFightParticipantPlacement placement in _targetPlacements.Values)
                {
                    Agent agent = placement?.Agent;
                    if (!_campaignStagedPlacementActive ||
                        agent?.IsActive() != true ||
                        !agent.IsAIControlled)
                    {
                        continue;
                    }

                    bool shouldBroadcastChoreography =
                        IsNetworkChoreographyAgent(placement);
                    try
                    {
                        float travelled = agent.Position.AsVec2.Distance(
                            placement.InitialGroundPosition.AsVec2);
                        float remaining = agent.Position.AsVec2.Distance(
                            placement.TargetGroundPosition.AsVec2);
                        if (_frozenAgentStates.TryGetValue(
                                agent.Index,
                                out FrozenAgentState frozen) &&
                            ReferenceEquals(frozen.Agent, agent) &&
                            ReferenceEquals(frozen.OriginalTeam, _enemyTeam))
                        {
                            enemyAgentCount++;
                            enemyTravelledTotal += travelled;
                            enemyTravelledMinimum = Math.Min(enemyTravelledMinimum, travelled);
                            enemyTravelledMaximum = Math.Max(enemyTravelledMaximum, travelled);
                            enemyRemainingTotal += remaining;
                            enemyRemainingMinimum = Math.Min(enemyRemainingMinimum, remaining);
                            enemyRemainingMaximum = Math.Max(enemyRemainingMaximum, remaining);
                        }

                        Vec2 direction = NormalizeDirection(placement.FacingDirection);
                        LogChoreographyDiagnosticSnapshot(
                            agent,
                            "server-hold-before-teleport",
                            sequence,
                            placement);
                        TeleportAgentToFrameSynced(
                            agent,
                            placement.TargetGroundPosition,
                            direction);
                        LogChoreographyDiagnosticSnapshot(
                            agent,
                            "server-hold-after-teleport",
                            sequence,
                            placement);
                        RestoreAgentFormationForBossHold(placement);
                        ApplyAgentChoreographyLocally(
                            agent,
                            CoopHideoutBossAgentChoreographyKind.HoldAtTarget,
                            placement.InitialGroundPosition,
                            placement.TargetGroundPosition,
                            direction);
                        if (shouldBroadcastChoreography)
                        {
                            BroadcastAgentChoreography(
                                CoopHideoutBossAgentChoreographyKind.HoldAtTarget,
                                placement,
                                sequence);
                        }
                        heldAgentCount++;
                    }
                    catch (Exception ex)
                    {
                        failedAgentCount++;
                        if (firstFailure.Length == 0)
                            firstFailure = ex.Message;
                    }
                }
            }
            finally
            {
                Mission.IsTeleportingAgents = previousTeleportingAgents;
                _campaignApproachHeld = true;
            }

            LockFrozenFormationsForBossConversation();
            float enemyTravelledAverage = enemyAgentCount > 0
                ? enemyTravelledTotal / enemyAgentCount
                : 0f;
            float enemyRemainingAverage = enemyAgentCount > 0
                ? enemyRemainingTotal / enemyAgentCount
                : 0f;
            ModLogger.Info(
                "CoopHideoutBossPhaseController: synchronized campaign boss approach hold applied. " +
                "Sequence=" + sequence +
                " HeldAgents=" + heldAgentCount +
                " FailedAgents=" + failedAgentCount +
                " EnemyAgents=" + enemyAgentCount +
                " EnemyTravelledMin=" +
                (enemyAgentCount > 0 ? enemyTravelledMinimum : 0f) +
                " EnemyTravelledAvg=" + enemyTravelledAverage +
                " EnemyTravelledMax=" + enemyTravelledMaximum +
                " EnemyRemainingMin=" +
                (enemyAgentCount > 0 ? enemyRemainingMinimum : 0f) +
                " EnemyRemainingAvg=" + enemyRemainingAverage +
                " EnemyRemainingMax=" + enemyRemainingMaximum +
                (firstFailure.Length > 0
                    ? " FirstFailure=" + firstFailure
                    : string.Empty) + ".");
        }

        private void FinalizeCampaignBossFightApproach()
        {
            if (!_campaignStagedPlacementActive || _targetPlacements.Count == 0)
                return;

            if (!_campaignApproachHeld)
                HoldCampaignBossFightApproachAtTargets();

            int finalizedAgentCount = 0;
            try
            {
                foreach (BossFightParticipantPlacement placement in _targetPlacements.Values)
                {
                    if (FinalizeAgentForBossConversation(placement))
                        finalizedAgentCount++;
                }
                LockFrozenFormationsForBossConversation();
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: finalized campaign boss approach. " +
                    "PositionHeldAgents=" + finalizedAgentCount +
                    " FormationsRestored=True.");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: final boss approach snap failed. Error=" +
                    ex.Message + ".");
            }
        }

        private bool FinalizeAgentForBossConversation(
            BossFightParticipantPlacement placement)
        {
            Agent agent = placement?.Agent;
            if (agent?.IsActive() != true)
                return false;

            Vec2 direction = placement.FacingDirection;
            if (direction.LengthSquared < 0.0001f)
                direction = new Vec2(0f, 1f);
            direction.Normalize();
            RestoreAgentFormationForBossHold(placement);
            agent.LookDirection = new Vec3(direction.x, direction.y, 0f);
            agent.SetMovementDirection(in direction);
            agent.MovementInputVector = Vec2.Zero;
            agent.MovementFlags = Agent.MovementControlFlag.None;
            FreezeAgentAfterCampaignBossApproach(agent);
            return true;
        }

        private void RestoreAgentFormationForBossHold(
            BossFightParticipantPlacement placement)
        {
            Agent agent = placement?.Agent;
            if (agent?.IsActive() != true ||
                !_frozenAgentStates.TryGetValue(agent.Index, out FrozenAgentState frozen) ||
                frozen == null ||
                !ReferenceEquals(frozen.Agent, agent) ||
                ReferenceEquals(agent.Formation, frozen.OriginalFormation))
            {
                return;
            }

            agent.Formation = frozen.OriginalFormation;
        }

        private bool ClearAgentTargetFrameSynced(Agent agent)
        {
            if (agent?.IsActive() != true)
                return false;

            agent.ClearTargetFrame();
            agent.DisableScriptedMovement();
            if (!GameNetwork.IsServer || !GameNetwork.IsSessionActive)
                return false;

            bool broadcastStarted = false;
            try
            {
                GameNetwork.BeginBroadcastModuleEvent();
                broadcastStarted = true;
                GameNetwork.WriteMessage(
                    new NetworkMessages.FromServer.ClearAgentTargetFrame(agent.Index));
                GameNetwork.EndBroadcastModuleEvent(
                    GameNetwork.EventBroadcastFlags.AddToMissionRecord);
                broadcastStarted = false;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: forced synchronized target clear failed. " +
                    "Agent=" + agent.Index + " Error=" + ex.Message + ".");
                return false;
            }
            finally
            {
                if (broadcastStarted)
                {
                    try
                    {
                        GameNetwork.EndBroadcastModuleEvent(
                            GameNetwork.EventBroadcastFlags.AddToMissionRecord);
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Info(
                            "CoopHideoutBossPhaseController: forced synchronized target clear close failed. " +
                            "Agent=" + agent.Index + " Error=" + ex.Message + ".");
                    }
                }
            }
        }

        private bool TryEnterBossConversationMissionMode()
        {
            try
            {
                Mission.SetMissionMode(MissionMode.Battle, false);
                Mission.SetMissionMode(MissionMode.Conversation, false);
                return Mission.Mode == MissionMode.Conversation;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: campaign conversation mission mode failed. Error=" +
                    ex.Message + ".");
                return false;
            }
        }

        private bool TryAssignHostControllerForCampaignBossCinematic()
        {
            if (!_campaignStagedPlacementActive)
                return true;
            if (_hostAgent?.IsActive() != true ||
                !_frozenAgentStates.TryGetValue(
                    _hostAgent.Index,
                    out FrozenAgentState frozen) ||
                !ReferenceEquals(frozen.Agent, _hostAgent))
            {
                return false;
            }

            try
            {
                if (_hostAgent.Controller != AgentControllerType.AI)
                    _hostAgent.Controller = AgentControllerType.AI;
                return _hostAgent.Controller == AgentControllerType.AI;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: host AI cinematic controller assignment failed. " +
                    "Agent=" + _hostAgent.Index + " Error=" + ex.Message + ".");
                return false;
            }
        }

        private void ScriptAgentAtCurrentPosition(Agent agent)
        {
            if (agent?.IsActive() != true || !agent.IsAIControlled)
                return;

            try
            {
                WorldPosition position = agent.GetWorldPosition();
                Vec2 direction = agent.LookDirection.AsVec2;
                if (direction.LengthSquared < 0.0001f)
                    direction = new Vec2(0f, 1f);
                direction.Normalize();
                agent.SetScriptedPositionAndDirection(
                    ref position,
                    direction.RotationInRadians,
                    addHumanLikeDelay: false);
            }
            catch
            {
            }
        }


        private void TeleportAgentToFrameSynced(
            Agent agent,
            Vec3 position,
            Vec2 direction)
        {
            if (agent?.IsActive() != true)
                return;

            agent.MountAgent?.TeleportToPosition(position);
            agent.TeleportToPosition(position);
            agent.LookDirection = new Vec3(direction.x, direction.y, 0f);
            agent.SetMovementDirection(in direction);
            if (!GameNetwork.IsServer || !GameNetwork.IsSessionActive)
                return;

            bool broadcastStarted = false;
            try
            {
                GameNetwork.BeginBroadcastModuleEvent();
                broadcastStarted = true;
                GameNetwork.WriteMessage(
                    new NetworkMessages.FromServer.AgentTeleportToFrame(
                        agent.Index,
                        position,
                        direction));
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: synchronized final boss placement failed. " +
                    "Agent=" + agent.Index + " Error=" + ex.Message + ".");
            }
            finally
            {
                if (broadcastStarted)
                {
                    try
                    {
                        GameNetwork.EndBroadcastModuleEvent(
                            GameNetwork.EventBroadcastFlags.AddToMissionRecord);
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Info(
                            "CoopHideoutBossPhaseController: synchronized final boss placement close failed. " +
                            "Agent=" + agent.Index + " Error=" + ex.Message + ".");
                    }
                }
            }
        }

        private void RestoreCombatAgent(FrozenAgentState frozen, bool restoreTeam)
        {
            Agent agent = frozen?.Agent;
            if (agent?.IsActive() != true)
                return;

            if (restoreTeam && frozen.OriginalTeam != null && agent.Team != frozen.OriginalTeam)
                agent.SetTeam(frozen.OriginalTeam, sync: true);
            if (agent.IsAIControlled)
                agent.SetIsAIPaused(frozen.OriginalAiPaused);
            if (agent.Controller != frozen.OriginalController)
                agent.Controller = frozen.OriginalController;
            _nativeControllerDetachedAgentIndices.Remove(agent.Index);
            if (!ReferenceEquals(agent.Formation, frozen.OriginalFormation))
                agent.Formation = frozen.OriginalFormation;
            agent.SetMortalityState(frozen.OriginalMortalityState);
            agent.SetMaximumSpeedLimit(-1f, isMultiplier: false);
            if (agent.IsAIControlled)
                agent.ClearTargetFrame();
            agent.DisableScriptedMovement();
            if (agent.IsAIControlled)
                agent.SetIsAIPaused(frozen.OriginalAiPaused);
            agent.SetLookAgent(null);
            agent.SetWatchState(Agent.WatchState.Alarmed);
        }

        private void RestoreDuelBossAgent(FrozenAgentState frozen)
        {
            Agent agent = frozen?.Agent;
            if (agent?.IsActive() != true)
                return;

            if (frozen.OriginalTeam != null && agent.Team != frozen.OriginalTeam)
                agent.SetTeam(frozen.OriginalTeam, sync: true);
            if (agent.IsAIControlled)
                agent.SetIsAIPaused(frozen.OriginalAiPaused);
            if (agent.Controller != frozen.OriginalController)
                agent.Controller = frozen.OriginalController;
            _nativeControllerDetachedAgentIndices.Remove(agent.Index);
            agent.Formation = null;
            agent.SetMortalityState(frozen.OriginalMortalityState);
            agent.DisableScriptedMovement();
            agent.SetMaximumSpeedLimit(-1f, isMultiplier: false);
            if (agent.IsAIControlled)
                agent.SetIsAIPaused(frozen.OriginalAiPaused);
            agent.SetLookAgent(null);
        }

        private void ReactivateReleasedCampaignBossFightAi(Agent agent)
        {
            bool isBossFightParticipant =
                ReferenceEquals(agent?.Team, _playerTeam) ||
                ReferenceEquals(agent?.Team, _enemyTeam);
            if (agent?.IsActive() != true ||
                !CoopHideoutBossPhaseContract.ShouldReactivateAgentAfterCampaignBossChoice(
                    _campaignStagedPlacementActive,
                    agent.IsAIControlled,
                    isBossFightParticipant))
            {
                return;
            }

            agent.ClearTargetFrame();
            agent.SetIsAIPaused(false);
            agent.SetAutomaticTargetSelection(true);
            agent.SetFiringOrder(FiringOrder.RangedWeaponUsageOrderEnum.FireAtWill);
            agent.MovementInputVector = Vec2.Zero;
            agent.MovementFlags = Agent.MovementControlFlag.None;
            agent.SetWatchState(Agent.WatchState.Alarmed);
            agent.ResetEnemyCaches();
            agent.HumanAIComponent?.SyncBehaviorParamsIfNecessary();
            agent.ForceAiBehaviorSelection();
        }

        private void ApplyClientBossTargetPolicy(CoopHideoutBossPhaseSession state)
        {
            if (!GameNetwork.IsClient || state == null)
                return;

            Agent bossAgent = TaleWorlds.MountAndBlade.Mission.MissionNetworkHelper.GetAgentFromIndex(
                state.BossAgentIndex,
                canBeNull: true);
            if (CoopHideoutBossPhaseContract.ShouldClearBossPreferredTarget(state.Phase))
            {
                ClearDuelBossPreferredTarget(bossAgent);
                return;
            }
            if (state.Phase != CoopHideoutBossPhase.Duel)
                return;

            Agent hostAgent = TaleWorlds.MountAndBlade.Mission.MissionNetworkHelper.GetAgentFromIndex(
                state.HostAgentIndex,
                canBeNull: true);
            ApplyDuelBossPreferredTarget(bossAgent, hostAgent);
        }

        private void ApplyDuelBossPreferredTarget(Agent bossAgent, Agent hostAgent)
        {
            if (!CoopHideoutBossPhaseContract.ShouldPrimeBossPreferredTargetForDuel(
                    CoopHideoutBossPhase.Duel,
                    isBossAgent: bossAgent != null,
                    isAiControlled: bossAgent?.IsAIControlled == true,
                    hostAgentActive: hostAgent?.IsActive() == true,
                    bossAgentActive: bossAgent?.IsActive() == true))
            {
                return;
            }

            bossAgent.Formation = null;
            bossAgent.ClearTargetFrame();
            bossAgent.DisableScriptedMovement();
            bossAgent.SetMaximumSpeedLimit(-1f, isMultiplier: false);
            bossAgent.SetIsAIPaused(false);
            bossAgent.ResetEnemyCaches();
            bossAgent.SetAutomaticTargetSelection(true);
            bossAgent.SetTargetAgent(hostAgent);
            bossAgent.SetAlarmState(Agent.AIStateFlag.Alarmed);
            bossAgent.SetWatchState(Agent.WatchState.Alarmed);
            bossAgent.HumanAIComponent?.SyncBehaviorParamsIfNecessary();
            bossAgent.ForceAiBehaviorSelection();
        }

        private static void ClearDuelBossPreferredTarget(Agent bossAgent)
        {
            if (bossAgent?.IsActive() != true || !bossAgent.IsAIControlled)
                return;

            bossAgent.SetTargetAgent(null);
            bossAgent.SetAutomaticTargetSelection(true);
            bossAgent.ResetEnemyCaches();
            bossAgent.ForceAiBehaviorSelection();
        }

        private void BroadcastReleasedAgentChoreography(CoopHideoutBossPhase phase)
        {
            if (!_campaignStagedPlacementActive || _targetPlacements.Count == 0)
                return;

            int sequence = NextChoreographySequence();
            int releasedAgentCount = 0;
            foreach (BossFightParticipantPlacement placement in _targetPlacements.Values)
            {
                if (!IsNetworkChoreographyAgent(placement) ||
                    !CoopHideoutBossPhaseContract.ShouldReleaseAgentForBossChoice(
                        phase,
                        ReferenceEquals(placement.Agent, _bossAgent)))
                {
                    continue;
                }

                BroadcastAgentChoreography(
                    CoopHideoutBossAgentChoreographyKind.Release,
                    placement,
                    sequence);
                releasedAgentCount++;
            }

            ModLogger.Info(
                "CoopHideoutBossPhaseController: synchronized campaign boss agents released. " +
                "Phase=" + phase +
                " Sequence=" + sequence +
                " ReleasedAgents=" + releasedAgentCount + ".");
        }

        private void RestoreMissionMode()
        {
            MissionMode target = _missionModeBeforeBossPhase == MissionMode.CutScene
                ? MissionMode.Battle
                : _missionModeBeforeBossPhase;
            Mission.SetMissionMode(target, false);
        }

        private void OrderTeamToCharge(Team team)
        {
            if (team == null)
                return;

            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null || formation.CountOfUnits <= 0)
                    continue;
                formation.SetMovementOrder(MovementOrder.MovementOrderCharge);
                formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
            }
        }

        private void AttachUnformedBossFightAgentsToCombatFormations(Team bossTeam)
        {
            int eligibleAgentCount = 0;
            int attachedAgentCount = 0;
            int failedAgentCount = 0;
            var attachedByFormation = new Dictionary<FormationClass, int>();

            foreach (FrozenAgentState frozen in _frozenAgentStates.Values)
            {
                Agent agent = frozen?.Agent;
                bool isAgentActive = agent?.IsActive() == true;
                bool isBossSideParticipant =
                    isAgentActive && ReferenceEquals(agent.Team, bossTeam);
                if (!CoopHideoutBossPhaseContract.ShouldAttachUnformedBossFightAgentForAllBattle(
                        _campaignStagedPlacementActive,
                        CoopHideoutBossPhase.AllBattle,
                        isAgentActive,
                        agent?.IsAIControlled == true,
                        isBossSideParticipant,
                        agent?.Formation != null))
                {
                    continue;
                }

                eligibleAgentCount++;
                FormationClass formationClass = ResolveBossFightCombatFormationClass(agent);
                try
                {
                    Formation formation = bossTeam?.GetFormation(formationClass);
                    if (formation == null)
                    {
                        failedAgentCount++;
                        continue;
                    }

                    agent.Formation = formation;
                    attachedAgentCount++;
                    attachedByFormation.TryGetValue(formationClass, out int formationCount);
                    attachedByFormation[formationClass] = formationCount + 1;
                }
                catch (Exception ex)
                {
                    failedAgentCount++;
                    ModLogger.Info(
                        "CoopHideoutBossPhaseController: boss-side formation attachment failed. " +
                        "Agent=" + (agent?.Index.ToString() ?? "null") +
                        " Formation=" + formationClass +
                        " Error=" + ex.Message + ".");
                }
            }

            string formationSummary = attachedByFormation.Count == 0
                ? "none"
                : string.Join(
                    ",",
                    attachedByFormation
                        .OrderBy(pair => (int)pair.Key)
                        .Select(pair => pair.Key + ":" + pair.Value));
            ModLogger.Info(
                "CoopHideoutBossPhaseController: prepared boss-side formations for all-battle. " +
                "EligibleAgents=" + eligibleAgentCount +
                " AttachedAgents=" + attachedAgentCount +
                " FailedAgents=" + failedAgentCount +
                " Formations=" + formationSummary + ".");
        }

        private static FormationClass ResolveBossFightCombatFormationClass(Agent agent)
        {
            FormationClass formationClass =
                agent?.Character?.DefaultFormationClass ?? FormationClass.Infantry;
            int formationIndex = (int)formationClass;
            if (formationIndex >= 0 &&
                formationIndex < (int)FormationClass.NumberOfRegularFormations)
            {
                return formationClass;
            }

            return agent?.IsRangedCached == true
                ? FormationClass.Ranged
                : FormationClass.Infantry;
        }

        private void StopTeamFormationsForCinematic(Team team)
        {
            if (team == null)
                return;

            bool lockFormationAi =
                CoopHideoutBossPhaseContract.ShouldLockFormationAiForCampaignBossCinematic(
                    _campaignStagedPlacementActive);
            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null || formation.CountOfUnits <= 0)
                    continue;

                if (lockFormationAi)
                {
                    if (!_frozenFormationStates.ContainsKey(formation))
                    {
                        _frozenFormationStates.Add(
                            formation,
                            new FrozenFormationState(formation, formation.IsAIControlled));
                    }
                    formation.SetControlledByAI(false, false);
                }
                formation.SetMovementOrder(MovementOrder.MovementOrderStop);
            }
        }

        private void RestoreFormationAiForBossPhase(CoopHideoutBossPhase phase)
        {
            if (!CoopHideoutBossPhaseContract.ShouldRestoreFormationAiForBossPhase(phase))
                return;

            foreach (FrozenFormationState frozen in _frozenFormationStates.Values)
            {
                Formation formation = frozen?.Formation;
                if (formation == null)
                    continue;

                try
                {
                    formation.SetControlledByAI(frozen.OriginalIsAiControlled, false);
                }
                catch (Exception ex)
                {
                    ModLogger.Info(
                        "CoopHideoutBossPhaseController: formation AI restore failed. " +
                        "Formation=" + formation.Index + " Error=" + ex.Message + ".");
                }
            }
            _frozenFormationStates.Clear();
        }

        private void LockFrozenFormationsForBossConversation()
        {
            if (!CoopHideoutBossPhaseContract.ShouldLockFormationAiForCampaignBossCinematic(
                    _campaignStagedPlacementActive))
            {
                return;
            }

            foreach (FrozenFormationState frozen in _frozenFormationStates.Values)
            {
                Formation formation = frozen?.Formation;
                if (formation == null)
                    continue;

                try
                {
                    formation.SetControlledByAI(false, false);
                    if (formation.CountOfUnits > 0)
                        formation.SetMovementOrder(MovementOrder.MovementOrderStop);
                }
                catch (Exception ex)
                {
                    ModLogger.Info(
                        "CoopHideoutBossPhaseController: formation AI conversation lock failed. " +
                        "Formation=" + formation.Index + " Error=" + ex.Message + ".");
                }
            }
        }

        private void CaptureRequiredReadyPeers()
        {
            _requiredReadyPeerIndices.Clear();
            _readyPeerIndices.Clear();
            if (GameNetwork.NetworkPeers == null)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (IsEligibleSynchronizedPeer(peer))
                    _requiredReadyPeerIndices.Add(peer.Index);
            }
        }

        private void RemoveDisconnectedRequiredPeers()
        {
            if (GameNetwork.NetworkPeers == null)
            {
                _requiredReadyPeerIndices.Clear();
                return;
            }

            HashSet<int> connected = new HashSet<int>(
                GameNetwork.NetworkPeers
                    .Where(IsEligibleSynchronizedPeer)
                    .Select(peer => peer.Index));
            _requiredReadyPeerIndices.RemoveWhere(index => !connected.Contains(index));
        }

        private NetworkCommunicator ResolveHostPeer()
        {
            if (GameNetwork.NetworkPeers == null)
                return null;

            if (_authoritativeHostPeerIndex >= 0)
            {
                return GameNetwork.NetworkPeers.FirstOrDefault(peer =>
                    IsEligibleSynchronizedPeer(peer) &&
                    peer.Index == _authoritativeHostPeerIndex);
            }

            if (HostSelfJoinRedirectState.TryResolvePersistedHostedPeerUserName(out string hostUserName) &&
                !string.IsNullOrWhiteSpace(hostUserName))
            {
                NetworkCommunicator markedHost = GameNetwork.NetworkPeers.FirstOrDefault(peer =>
                    IsEligibleSynchronizedPeer(peer) &&
                    string.Equals(peer.UserName, hostUserName, StringComparison.OrdinalIgnoreCase));
                if (markedHost != null)
                {
                    _authoritativeHostPeerIndex = markedHost.Index;
                    return markedHost;
                }
            }

            NetworkCommunicator resolved = GameNetwork.NetworkPeers
                .Where(IsEligibleSynchronizedPeer)
                .OrderBy(peer => peer.Index)
                .FirstOrDefault(peer => ResolveControlledAgent(peer)?.IsActive() == true) ??
                GameNetwork.NetworkPeers
                    .Where(IsEligibleSynchronizedPeer)
                    .OrderBy(peer => peer.Index)
                    .FirstOrDefault();
            if (resolved != null)
                _authoritativeHostPeerIndex = resolved.Index;
            return resolved;
        }

        private bool IsHostPeerAvailable()
        {
            if (GameNetwork.NetworkPeers == null || _session == null)
                return false;
            return GameNetwork.NetworkPeers.Any(peer =>
                IsEligibleSynchronizedPeer(peer) && peer.Index == _session.HostPeerIndex);
        }

        private static bool IsEligibleSynchronizedPeer(NetworkCommunicator peer)
        {
            return peer != null &&
                   !peer.IsServerPeer &&
                   peer.IsConnectionActive &&
                   peer.IsSynchronized;
        }

        private static Agent ResolveControlledAgent(NetworkCommunicator peer)
        {
            if (peer == null)
                return null;
            MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
            return missionPeer?.ControlledAgent ?? peer.ControlledAgent;
        }

        private Team ResolveEnemyTeam(Team playerTeam)
        {
            if (Mission?.Teams == null || playerTeam == null)
                return null;

            if (_enemyTeam != null &&
                _enemyTeam != Team.Invalid &&
                !ReferenceEquals(_enemyTeam, playerTeam) &&
                _enemyTeam.Side != BattleSideEnum.None &&
                _enemyTeam.Side != playerTeam.Side)
            {
                return _enemyTeam;
            }

            Team explicitEnemy = Mission.Teams.FirstOrDefault(team =>
                team != null && team != playerTeam && team != Team.Invalid &&
                GetActiveHumanAgents(team).Count > 0 && playerTeam.IsEnemyOf(team));
            if (explicitEnemy != null)
                return explicitEnemy;

            Team activeOpposingTeam = Mission.Teams.FirstOrDefault(team =>
                team != null && team != playerTeam && team != Team.Invalid &&
                GetActiveHumanAgents(team).Count > 0);
            if (activeOpposingTeam != null)
                return activeOpposingTeam;

            // Stealth intentionally marks the two teams as non-enemies before the alarm. Resolve
            // the depleted defender team by battle side so a reserved boss group can be spawned.
            return Mission.Teams.FirstOrDefault(team =>
                team != null &&
                team != playerTeam &&
                team != Team.Invalid &&
                team.Side != BattleSideEnum.None &&
                team.Side != playerTeam.Side);
        }

        private List<Agent> GetActiveHumanAgents(Team team)
        {
            if (Mission?.Agents == null || team == null)
                return new List<Agent>();
            return Mission.Agents
                .Where(agent => agent != null && agent.IsHuman && agent.IsActive() && agent.Team == team)
                .ToList();
        }

        private static Agent SelectBossAgent(IEnumerable<Agent> candidates)
        {
            List<Agent> active = (candidates ?? Enumerable.Empty<Agent>())
                .Where(agent => agent?.IsActive() == true && agent.IsHuman)
                .ToList();
            Agent namedBoss = active.FirstOrDefault(agent =>
            {
                string id = agent.Character?.StringId ?? string.Empty;
                return id.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       id.IndexOf("chief", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       id.IndexOf("leader", StringComparison.OrdinalIgnoreCase) >= 0;
            });
            return namedBoss ?? active
                .OrderByDescending(agent => agent.Character?.Level ?? 0)
                .ThenBy(agent => agent.Index)
                .FirstOrDefault();
        }

        private static Agent SelectBossCinematicPrincipal(IEnumerable<Agent> candidates)
        {
            return (candidates ?? Enumerable.Empty<Agent>())
                .Where(agent => agent?.IsActive() == true && agent.IsHuman)
                .OrderByDescending(agent =>
                    CoopHideoutBossPhaseContract.ResolveBossCinematicPrincipalPriority(
                        agent.Character?.IsHero == true,
                        agent.MissionPeer != null || agent.IsPlayerControlled,
                        agent.Character?.Level ?? 0))
                .ThenBy(agent => agent.Index)
                .FirstOrDefault();
        }

        private GameEntity TryResolveBossFightEntity()
        {
            try
            {
                return Mission?.Scene?.FindEntityWithTag(CoopHideoutBossPhaseContract.BossFightEntityTag);
            }
            catch
            {
                return null;
            }
        }

        private bool IsTeamDepleted(Team team)
        {
            return team == null || GetActiveHumanAgents(team).Count == 0;
        }

        private static void SetTeamsAsEnemies(Team left, Team right, bool enemies)
        {
            if (left == null || right == null)
                return;
            left.SetIsEnemyOf(right, enemies);
            right.SetIsEnemyOf(left, enemies);
        }

        private void BroadcastState(int phaseDurationMilliseconds)
        {
            if (!GameNetwork.IsServer || _session == null)
                return;

            try
            {
                GameNetwork.BeginBroadcastModuleEvent();
                GameNetwork.WriteMessage(new CoopHideoutBossPhaseStateMessage(
                    _session,
                    phaseDurationMilliseconds));
                GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: state broadcast failed. Error=" + ex.Message);
            }
        }

        private void SendState(NetworkCommunicator peer, int phaseDurationMilliseconds)
        {
            if (!GameNetwork.IsServer || peer == null || _session == null)
                return;

            try
            {
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(new CoopHideoutBossPhaseStateMessage(
                    _session,
                    phaseDurationMilliseconds));
                GameNetwork.EndModuleEventAsServer();
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: targeted state send failed. " +
                    "PeerIndex=" + peer.Index + " Error=" + ex.Message);
            }
        }

        private int ResolvePhaseDurationMilliseconds(CoopHideoutBossPhase phase)
        {
            if (phase == CoopHideoutBossPhase.PreparingCinematic)
                return CoopHideoutBossPhaseContract.CinematicReadyTimeoutMilliseconds;
            if (phase == CoopHideoutBossPhase.Cinematic)
            {
                return CoopHideoutBossPhaseContract.ResolveCinematicDurationMilliseconds(
                    _campaignStagedPlacementActive);
            }
            return 0;
        }

        private void InitializeServerChoreographyDiagnosticsTracking(
            List<BossFightParticipantPlacement> placements,
            MatrixFrame anchor)
        {
            if (!CoopDebugConfig.HideoutBossChoreographyDiagnostics || placements == null)
                return;

            _choreographyDiagnosticRoles.Clear();
            _choreographyDiagnosticPlacements.Clear();
            _choreographyDiagnosticSampleWindows.Clear();

            TrackChoreographyDiagnosticPlacement(
                placements.FirstOrDefault(placement =>
                    ReferenceEquals(placement?.Agent, _hostAgent)),
                "host");
            TrackChoreographyDiagnosticPlacement(
                placements.FirstOrDefault(placement => ReferenceEquals(placement?.Agent, _bossAgent)),
                "boss");
            TrackChoreographyDiagnosticPlacement(
                placements.FirstOrDefault(placement =>
                    placement?.Agent?.IsAIControlled == true &&
                    !ReferenceEquals(placement.Agent, _bossAgent) &&
                    ReferenceEquals(placement.Agent.Team, _enemyTeam)),
                "enemy-bodyguard");
            TrackChoreographyDiagnosticPlacement(
                placements.FirstOrDefault(placement =>
                    placement?.Agent?.IsAIControlled == true &&
                    !ReferenceEquals(placement.Agent, _hostAgent) &&
                    ReferenceEquals(placement.Agent.Team, _playerTeam)),
                "player-ally");

            Vec2 forward = anchor.rotation.f.AsVec2;
            if (forward.LengthSquared < 0.0001f)
                forward = new Vec2(0f, 1f);
            forward.Normalize();
            Vec2 side = new Vec2(forward.y, -forward.x);
            ModLogger.Info(
                "CoopHideoutBossPhaseController: choreography diagnostics tracking initialized. " +
                "Runtime=server" +
                " Battle=" + (_session?.BattleInstanceId ?? "none") +
                " AnchorOrigin=" + anchor.origin +
                " AnchorForward=" + forward +
                " AnchorSide=" + side +
                " TrackedAgents=" + _choreographyDiagnosticRoles.Count + ".");
        }

        private void TrackClientChoreographyDiagnosticAgent(
            Agent agent,
            CoopHideoutBossAgentChoreographyMessage message,
            CoopHideoutBossPhaseSession clientState)
        {
            if (!CoopDebugConfig.HideoutBossChoreographyDiagnostics ||
                agent?.IsActive() != true ||
                message == null ||
                clientState == null)
            {
                return;
            }

            Vec2 direction = NormalizeDirection(message.Direction);
            var placement = new BossFightParticipantPlacement(
                agent,
                message.InitialPosition,
                message.TargetPosition,
                direction);
            if (_choreographyDiagnosticRoles.ContainsKey(agent.Index))
            {
                _choreographyDiagnosticPlacements[agent.Index] = placement;
                return;
            }

            string role = null;
            if (agent.Index == clientState.BossAgentIndex)
            {
                role = "boss";
            }
            else
            {
                Agent hostAgent = TaleWorlds.MountAndBlade.Mission.MissionNetworkHelper.GetAgentFromIndex(
                    clientState.HostAgentIndex,
                    canBeNull: true);
                Agent bossAgent = TaleWorlds.MountAndBlade.Mission.MissionNetworkHelper.GetAgentFromIndex(
                    clientState.BossAgentIndex,
                    canBeNull: true);
                if (!_choreographyDiagnosticRoles.ContainsValue("enemy-bodyguard") &&
                    agent.IsAIControlled &&
                    bossAgent?.Team != null &&
                    ReferenceEquals(agent.Team, bossAgent.Team))
                {
                    role = "enemy-bodyguard";
                }
                else if (!_choreographyDiagnosticRoles.ContainsValue("player-ally") &&
                         agent.IsAIControlled &&
                         hostAgent?.Team != null &&
                         ReferenceEquals(agent.Team, hostAgent.Team))
                {
                    role = "player-ally";
                }
            }

            TrackChoreographyDiagnosticPlacement(placement, role);
        }

        private void TrackChoreographyDiagnosticPlacement(
            BossFightParticipantPlacement placement,
            string role)
        {
            Agent agent = placement?.Agent;
            if (agent?.IsActive() != true || string.IsNullOrWhiteSpace(role))
                return;

            _choreographyDiagnosticRoles[agent.Index] = role;
            _choreographyDiagnosticPlacements[agent.Index] = placement;
            ModLogger.Info(
                "CoopHideoutBossPhaseController: choreography diagnostics agent tracked. " +
                "Runtime=" + ResolveChoreographyDiagnosticRuntimeRole() +
                " Battle=" + ResolveChoreographyDiagnosticBattleId() +
                " Role=" + role +
                " Agent=" + agent.Index +
                " Initial=" + placement.InitialGroundPosition +
                " Target=" + placement.TargetGroundPosition +
                " Direction=" + placement.FacingDirection + ".");
        }

        private BossFightParticipantPlacement UpdateTrackedChoreographyDiagnosticPlacement(
            Agent agent,
            Vec3 initialPosition,
            Vec3 targetPosition,
            Vec2 direction)
        {
            if (!CoopDebugConfig.HideoutBossChoreographyDiagnostics ||
                agent == null ||
                !_choreographyDiagnosticRoles.ContainsKey(agent.Index))
            {
                return null;
            }

            var placement = new BossFightParticipantPlacement(
                agent,
                initialPosition,
                targetPosition,
                NormalizeDirection(direction));
            _choreographyDiagnosticPlacements[agent.Index] = placement;
            return placement;
        }

        private void LogAllTrackedChoreographyDiagnosticSnapshots(
            string stage,
            int sequence)
        {
            if (!CoopDebugConfig.HideoutBossChoreographyDiagnostics)
                return;

            foreach (BossFightParticipantPlacement placement in
                     _choreographyDiagnosticPlacements.Values.ToArray())
            {
                LogChoreographyDiagnosticSnapshot(
                    placement?.Agent,
                    stage,
                    sequence,
                    placement);
            }
        }

        private void LogChoreographyDiagnosticSnapshot(
            Agent agent,
            string stage,
            int sequence,
            BossFightParticipantPlacement placement = null)
        {
            if (!CoopDebugConfig.HideoutBossChoreographyDiagnostics ||
                agent?.IsActive() != true ||
                !_choreographyDiagnosticRoles.TryGetValue(agent.Index, out string role))
            {
                return;
            }

            if (placement == null)
                _choreographyDiagnosticPlacements.TryGetValue(agent.Index, out placement);
            if (placement == null)
                return;

            try
            {
                ResolveChoreographyDiagnosticReflectionFields();
                Vec3 actualPosition = agent.Position;
                Vec3 visualPosition = agent.VisualPosition;
                Vec2 targetPosition = agent.GetTargetPosition();
                Vec3 targetDirection = agent.GetTargetDirection();
                Vec2 intended =
                    placement.TargetGroundPosition.AsVec2 -
                    placement.InitialGroundPosition.AsVec2;
                float intendedLength = intended.Length;
                Vec2 actualDelta =
                    actualPosition.AsVec2 - placement.InitialGroundPosition.AsVec2;
                float signedProgress = 0f;
                float lateralError = 0f;
                if (intendedLength > 0.0001f)
                {
                    Vec2 intendedDirection = intended / intendedLength;
                    signedProgress =
                        actualDelta.x * intendedDirection.x +
                        actualDelta.y * intendedDirection.y;
                    Vec2 lateralDirection =
                        new Vec2(-intendedDirection.y, intendedDirection.x);
                    lateralError =
                        actualDelta.x * lateralDirection.x +
                        actualDelta.y * lateralDirection.y;
                }

                object lastSynchedTargetPosition =
                    _lastSynchedTargetPositionField?.GetValue(agent);
                object checkIfTargetFrameIsChanged =
                    _checkIfTargetFrameIsChangedField?.GetValue(agent);
                Agent combatTargetAgent = agent.GetTargetAgent();
                Agent referenceHostAgent = ResolveChoreographyDiagnosticHostAgent();
                Vec3 referenceHostPosition =
                    referenceHostAgent?.IsActive() == true
                        ? referenceHostAgent.Position
                        : Vec3.Zero;
                Vec3 referenceHostVisualPosition =
                    referenceHostAgent?.IsActive() == true
                        ? referenceHostAgent.VisualPosition
                        : Vec3.Zero;
                Vec2 movementDirection = agent.GetMovementDirection();
                Vec2 directionToHost = referenceHostPosition.AsVec2 - actualPosition.AsVec2;
                float planarDistanceToHost = directionToHost.Length;
                float movementAlignmentToHost = 0f;
                float lookAlignmentToHost = 0f;
                if (planarDistanceToHost > 0.0001f)
                {
                    directionToHost /= planarDistanceToHost;
                    if (movementDirection.LengthSquared > 0.0001f)
                    {
                        Vec2 normalizedMovementDirection = movementDirection;
                        normalizedMovementDirection.Normalize();
                        movementAlignmentToHost =
                            normalizedMovementDirection.x * directionToHost.x +
                            normalizedMovementDirection.y * directionToHost.y;
                    }

                    Vec2 lookDirection = agent.LookDirection.AsVec2;
                    if (lookDirection.LengthSquared > 0.0001f)
                    {
                        lookDirection.Normalize();
                        lookAlignmentToHost =
                            lookDirection.x * directionToHost.x +
                            lookDirection.y * directionToHost.y;
                    }
                }
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: choreography diagnostic snapshot. " +
                    "Runtime=" + ResolveChoreographyDiagnosticRuntimeRole() +
                    " Battle=" + ResolveChoreographyDiagnosticBattleId() +
                    " Phase=" + ResolveChoreographyDiagnosticPhase() +
                    " Stage=" + (stage ?? "unknown") +
                    " Sequence=" + sequence +
                    " MissionTime=" + (Mission?.CurrentTime ?? -1f) +
                    " Role=" + role +
                    " Agent=" + agent.Index +
                    " Position=" + actualPosition +
                    " VisualPosition=" + visualPosition +
                    " Initial=" + placement.InitialGroundPosition +
                    " IntendedTarget=" + placement.TargetGroundPosition +
                    " IntendedDistance=" + intendedLength +
                    " SignedProgress=" + signedProgress +
                    " LateralError=" + lateralError +
                    " Remaining=" + actualPosition.AsVec2.Distance(
                        placement.TargetGroundPosition.AsVec2) +
                    " MovementLockedState=" + agent.MovementLockedState +
                    " PublicTargetPosition=" + targetPosition +
                    " PublicTargetDirection=" + targetDirection +
                    " LastSynchedTargetPosition=" +
                    (lastSynchedTargetPosition?.ToString() ?? "unavailable") +
                    " CheckIfTargetFrameIsChanged=" +
                    (checkIfTargetFrameIsChanged?.ToString() ?? "unavailable") +
                    " LookDirection=" + agent.LookDirection +
                    " MovementInput=" + agent.MovementInputVector +
                    " MovementDirection=" + movementDirection +
                    " MovementAlignmentToHost=" + movementAlignmentToHost +
                    " LookAlignmentToHost=" + lookAlignmentToHost +
                    " MovementFlags=" + agent.MovementFlags +
                    " Controller=" + agent.Controller +
                    " IsAIControlled=" + agent.IsAIControlled +
                    " IsPaused=" + agent.IsPaused +
                    " TargetAgent=" + (combatTargetAgent?.Index.ToString() ?? "null") +
                    " TargetDistance=" +
                    (combatTargetAgent?.IsActive() == true
                        ? actualPosition.Distance(combatTargetAgent.Position).ToString()
                        : "unavailable") +
                    " ReferenceHostAgent=" +
                    (referenceHostAgent?.IsActive() == true
                        ? referenceHostAgent.Index.ToString()
                        : "unavailable") +
                    " ReferenceHostPosition=" +
                    (referenceHostAgent?.IsActive() == true
                        ? referenceHostPosition.ToString()
                        : "unavailable") +
                    " ReferenceHostVisualPosition=" +
                    (referenceHostAgent?.IsActive() == true
                        ? referenceHostVisualPosition.ToString()
                        : "unavailable") +
                    " PlanarDistanceToHost=" +
                    (referenceHostAgent?.IsActive() == true
                        ? planarDistanceToHost.ToString()
                        : "unavailable") +
                    " AgentEnemyOfHost=" +
                    (referenceHostAgent?.IsActive() == true
                        ? agent.IsEnemyOf(referenceHostAgent).ToString()
                        : "unavailable") +
                    " HostEnemyOfAgent=" +
                    (referenceHostAgent?.IsActive() == true
                        ? referenceHostAgent.IsEnemyOf(agent).ToString()
                        : "unavailable") +
                    " WieldedItem=" +
                    (agent.WieldedWeapon.Item?.StringId ?? "none") +
                    " TeamSide=" + (agent.Team?.Side.ToString() ?? "null") +
                    " Formation=" + (agent.Formation?.Index.ToString() ?? "null") +
                    " MissionIsTeleportingAgents=" + (Mission?.IsTeleportingAgents ?? false) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: choreography diagnostic snapshot failed. " +
                    "Runtime=" + ResolveChoreographyDiagnosticRuntimeRole() +
                    " Stage=" + (stage ?? "unknown") +
                    " Agent=" + agent.Index +
                    " Error=" + ex.GetType().Name + ":" + ex.Message + ".");
            }
        }

        private Agent ResolveChoreographyDiagnosticHostAgent()
        {
            if (_hostAgent?.IsActive() == true)
                return _hostAgent;

            CoopHideoutBossPhaseSession clientState = CurrentClientState;
            if (clientState == null)
                return null;

            return TaleWorlds.MountAndBlade.Mission.MissionNetworkHelper.GetAgentFromIndex(
                clientState.HostAgentIndex,
                canBeNull: true);
        }

        private void StartAllTrackedChoreographyDiagnosticSampleWindows(
            string stage,
            int sequence)
        {
            if (!CoopDebugConfig.HideoutBossChoreographyDiagnostics)
                return;

            foreach (BossFightParticipantPlacement placement in
                     _choreographyDiagnosticPlacements.Values.ToArray())
            {
                StartChoreographyDiagnosticSampleWindow(
                    placement?.Agent,
                    placement,
                    stage,
                    sequence);
            }
        }

        private void StartChoreographyDiagnosticSampleWindow(
            Agent agent,
            BossFightParticipantPlacement placement,
            string stage,
            int sequence)
        {
            if (!CoopDebugConfig.HideoutBossChoreographyDiagnostics ||
                Mission == null ||
                agent?.IsActive() != true ||
                placement == null ||
                !_choreographyDiagnosticRoles.ContainsKey(agent.Index))
            {
                return;
            }

            _choreographyDiagnosticSampleWindows[agent.Index] =
                new ChoreographyDiagnosticSampleWindow(
                    agent,
                    placement,
                    stage ?? "unknown",
                    sequence,
                    Mission.CurrentTime);
        }

        private void PumpChoreographyDiagnosticSamples()
        {
            if (!CoopDebugConfig.HideoutBossChoreographyDiagnostics)
            {
                if (_choreographyDiagnosticSampleWindows.Count > 0)
                    _choreographyDiagnosticSampleWindows.Clear();
                return;
            }
            if (Mission == null || _choreographyDiagnosticSampleWindows.Count == 0)
                return;

            float currentMissionTime = Mission.CurrentTime;
            foreach (KeyValuePair<int, ChoreographyDiagnosticSampleWindow> pair in
                     _choreographyDiagnosticSampleWindows.ToArray())
            {
                ChoreographyDiagnosticSampleWindow window = pair.Value;
                if (window?.Agent?.IsActive() != true)
                {
                    _choreographyDiagnosticSampleWindows.Remove(pair.Key);
                    continue;
                }

                float elapsed = Math.Max(0f, currentMissionTime - window.StartMissionTime);
                while (window.NextSampleIndex < ChoreographyDiagnosticSampleOffsetsSeconds.Length &&
                       elapsed >= ChoreographyDiagnosticSampleOffsetsSeconds[window.NextSampleIndex])
                {
                    float sampleOffset =
                        ChoreographyDiagnosticSampleOffsetsSeconds[window.NextSampleIndex];
                    LogChoreographyDiagnosticSnapshot(
                        window.Agent,
                        window.Stage + "-t" +
                        (int)Math.Round(sampleOffset * 1000f) + "ms",
                        window.Sequence,
                        window.Placement);
                    window.NextSampleIndex++;
                }

                if (window.NextSampleIndex >= ChoreographyDiagnosticSampleOffsetsSeconds.Length)
                    _choreographyDiagnosticSampleWindows.Remove(pair.Key);
            }
        }

        private static void ResolveChoreographyDiagnosticReflectionFields()
        {
            if (_choreographyDiagnosticReflectionResolved)
                return;

            _lastSynchedTargetPositionField = typeof(Agent).GetField(
                "_lastSynchedTargetPosition",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _checkIfTargetFrameIsChangedField = typeof(Agent).GetField(
                "_checkIfTargetFrameIsChanged",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _choreographyDiagnosticReflectionResolved = true;
        }

        private string ResolveChoreographyDiagnosticBattleId()
        {
            return GameNetwork.IsServer
                ? _session?.BattleInstanceId ?? "none"
                : CurrentClientState?.BattleInstanceId ?? "none";
        }

        private string ResolveChoreographyDiagnosticPhase()
        {
            return GameNetwork.IsServer
                ? _session?.Phase.ToString() ?? "none"
                : CurrentClientState?.Phase.ToString() ?? "none";
        }

        private static string ResolveChoreographyDiagnosticRuntimeRole()
        {
            if (GameNetwork.IsServer)
                return "server";
            if (GameNetwork.IsClient)
                return "client";
            return "offline";
        }

        private sealed class ChoreographyDiagnosticSampleWindow
        {
            public ChoreographyDiagnosticSampleWindow(
                Agent agent,
                BossFightParticipantPlacement placement,
                string stage,
                int sequence,
                float startMissionTime)
            {
                Agent = agent;
                Placement = placement;
                Stage = stage;
                Sequence = sequence;
                StartMissionTime = startMissionTime;
            }

            public Agent Agent { get; }
            public BossFightParticipantPlacement Placement { get; }
            public string Stage { get; }
            public int Sequence { get; }
            public float StartMissionTime { get; }
            public int NextSampleIndex { get; set; }
        }

        private sealed class FrozenAgentState
        {
            public FrozenAgentState(
                Agent agent,
                Team originalTeam,
                Formation originalFormation,
                AgentControllerType originalController,
                Agent.MortalityState originalMortalityState,
                bool originalAiPaused)
            {
                Agent = agent;
                OriginalTeam = originalTeam;
                OriginalFormation = originalFormation;
                OriginalController = originalController;
                OriginalMortalityState = originalMortalityState;
                OriginalAiPaused = originalAiPaused;
            }

            public Agent Agent { get; }
            public Team OriginalTeam { get; }
            public Formation OriginalFormation { get; }
            public AgentControllerType OriginalController { get; }
            public Agent.MortalityState OriginalMortalityState { get; }
            public bool OriginalAiPaused { get; }
        }

        private sealed class FrozenFormationState
        {
            public FrozenFormationState(Formation formation, bool originalIsAiControlled)
            {
                Formation = formation;
                OriginalIsAiControlled = originalIsAiControlled;
            }

            public Formation Formation { get; }
            public bool OriginalIsAiControlled { get; }
        }
    }
}
