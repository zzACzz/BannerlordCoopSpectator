using System;
using System.Collections.Generic;
using System.Linq;
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
        private const float NativeApproachSpeedLimit = 0.65f;

        private readonly HashSet<int> _requiredReadyPeerIndices = new HashSet<int>();
        private readonly HashSet<int> _readyPeerIndices = new HashSet<int>();
        private readonly Dictionary<int, FrozenAgentState> _frozenAgentStates =
            new Dictionary<int, FrozenAgentState>();
        private readonly Dictionary<int, BossFightParticipantPlacement> _targetPlacements =
            new Dictionary<int, BossFightParticipantPlacement>();

        private CoopHideoutBossPhaseSession _session;
        private Team _playerTeam;
        private Team _enemyTeam;
        private Agent _hostAgent;
        private Agent _bossAgent;
        private MissionMode _missionModeBeforeBossPhase;
        private DateTime _missionStartedUtc;
        private float _nextServerPumpMissionTime;
        private int _initialEnemyCount;
        private bool _bossFightEntityMissingLogged;
        private bool _phaseCompletionLogged;
        private bool _nightStagedPlacementActive;

        public static event Action<CoopHideoutBossPhaseSession, int> ClientStateChanged;
        public static CoopHideoutBossPhaseSession CurrentClientState { get; private set; }
        public static int CurrentClientPhaseDurationMilliseconds { get; private set; }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _missionStartedUtc = DateTime.UtcNow;
            if (GameNetwork.IsClient)
            {
                CurrentClientState = null;
                CurrentClientPhaseDurationMilliseconds = 0;
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
                "CoopHideoutBossPhaseController: isolated day hideout controller initialized. " +
                "BattleInstanceId=" + _session.BattleInstanceId +
                " Scene=" + (Mission?.SceneName ?? "null") + ".");
        }

        protected override void AddRemoveMessageHandlers(
            GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            if (GameNetwork.IsServer)
                registerer.RegisterBaseHandler<CoopHideoutBossPhaseClientCommandMessage>(HandleClientCommand);
            if (GameNetwork.IsClient)
                registerer.RegisterBaseHandler<CoopHideoutBossPhaseStateMessage>(HandleServerState);
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
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
            if (GameNetwork.IsClient)
            {
                CurrentClientState = null;
                CurrentClientPhaseDurationMilliseconds = 0;
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
                if (nowUtc >= _session.DeadlineUtc)
                    BeginAwaitingHostChoice(nowUtc);
                return;
            }

            if (_session.Phase == CoopHideoutBossPhase.AwaitingHostChoice)
            {
                if (!IsHostPeerAvailable() || nowUtc >= _session.DeadlineUtc)
                    StartAllBattle("host-choice-timeout-fallback");
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
            Agent hostAgent = ResolveControlledAgent(hostPeer);
            if (hostPeer == null || hostAgent?.IsActive() != true || hostAgent.Team == null)
                return;

            Team enemyTeam = ResolveEnemyTeam(hostAgent.Team);
            if (enemyTeam == null)
                return;

            // Keep the authoritative team identities before the last initial defender is removed.
            // The reserved boss group must still be able to resolve its team when that team has
            // no active agents left in Mission.Agents.
            _playerTeam = hostAgent.Team;
            _enemyTeam = enemyTeam;

            CoopExactCampaignHideoutMissionController hideoutController =
                Mission.GetMissionBehavior<CoopExactCampaignHideoutMissionController>();
            if (hideoutController != null && !hideoutController.IsBossPhaseEligible)
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
                        hostAgent.IsActive(),
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
                        hostAgent.IsActive(),
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
                        hostAgent.IsActive(),
                        bossFightEntity != null))
                {
                    return;
                }

                bossAgent = SelectBossAgent(activeEnemies);
            }
            if (bossAgent == null)
                return;

            _playerTeam = hostAgent.Team;
            _enemyTeam = enemyTeam;
            _hostAgent = hostAgent;
            _bossAgent = bossAgent;
            _session.HostPeerIndex = hostPeer.Index;
            _session.HostAgentIndex = hostAgent.Index;
            _session.BossAgentIndex = bossAgent.Index;
            _missionModeBeforeBossPhase = Mission.Mode;

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
                " HostPeer=" + hostPeer.Index +
                " HostAgent=" + hostAgent.Index +
                " BossAgent=" + bossAgent.Index +
                " RequiredReadyPeers=" + _requiredReadyPeerIndices.Count + ".");
        }

        private void BeginCinematic(DateTime nowUtc)
        {
            if (!TryPlaceBossFightParticipants())
            {
                StartAllBattle("boss-placement-failed-fallback");
                return;
            }

            string rejection;
            if (!CoopHideoutBossPhaseContract.TryTransition(
                    _session,
                    CoopHideoutBossPhase.Cinematic,
                    nowUtc.AddMilliseconds(CoopHideoutBossPhaseContract.CinematicDurationMilliseconds),
                    "boss-cinematic-start",
                    out rejection))
            {
                StartAllBattle("boss-cinematic-transition-failed");
                return;
            }
            BroadcastState(CoopHideoutBossPhaseContract.CinematicDurationMilliseconds);
        }

        private void BeginAwaitingHostChoice(DateTime nowUtc)
        {
            if (!IsHostPeerAvailable())
            {
                StartAllBattle("host-unavailable-before-choice");
                return;
            }

            FinalizeNightBossFightApproach();

            string rejection;
            if (!CoopHideoutBossPhaseContract.TryTransition(
                    _session,
                    CoopHideoutBossPhase.AwaitingHostChoice,
                    nowUtc.AddMilliseconds(CoopHideoutBossPhaseContract.HostChoiceTimeoutMilliseconds),
                    "awaiting-host-choice",
                    out rejection))
            {
                StartAllBattle("host-choice-transition-failed");
                return;
            }
            BroadcastState(CoopHideoutBossPhaseContract.HostChoiceTimeoutMilliseconds);
        }

        private void StartDuel(string reason)
        {
            if (_session == null || _hostAgent?.IsActive() != true || _bossAgent?.IsActive() != true)
            {
                StartAllBattle("duel-participant-missing-fallback");
                return;
            }

            foreach (FrozenAgentState frozen in _frozenAgentStates.Values)
            {
                Agent agent = frozen.Agent;
                if (agent?.IsActive() != true)
                    continue;

                if (ReferenceEquals(agent, _hostAgent) || ReferenceEquals(agent, _bossAgent))
                {
                    RestoreCombatAgent(frozen, restoreTeam: true);
                    continue;
                }

                agent.SetMortalityState(Agent.MortalityState.Invulnerable);
                if (agent.Team != Team.Invalid)
                    agent.SetTeam(Team.Invalid, sync: true);
                ScriptAgentAtCurrentPosition(agent);
                agent.SetLookAgent(frozen.OriginalTeam == _playerTeam ? _hostAgent : _bossAgent);
            }

            SetTeamsAsEnemies(_playerTeam, _enemyTeam, true);
            RestoreMissionMode();
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
            OrderTeamToCharge(_playerTeam);
            OrderTeamToCharge(_enemyTeam);

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
            _targetPlacements.Clear();
            _nightStagedPlacementActive = false;
            foreach (Agent agent in Mission.Agents)
            {
                if (agent?.IsActive() != true || !agent.IsHuman)
                    continue;

                _frozenAgentStates[agent.Index] = new FrozenAgentState(
                    agent,
                    agent.Team,
                    agent.Formation,
                    agent.CurrentMortalityState);
                agent.SetMortalityState(Agent.MortalityState.Invulnerable);
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

            CoopExactCampaignHideoutAmbushMissionController nightController =
                Mission.GetMissionBehavior<CoopExactCampaignHideoutAmbushMissionController>();
            CoopHideoutBossFightManifest bossFightManifest =
                nightController?.SceneManifest?.BossFight;
            _nightStagedPlacementActive = nightController != null;
            float innerRadius = bossFightManifest?.InnerRadius ?? DefaultInnerRadius;
            float outerRadius = bossFightManifest?.OuterRadius ?? DefaultOuterRadius;
            float walkDistance = bossFightManifest?.WalkDistance ?? DefaultWalkDistance;
            CoopHideoutBossPrincipalPlacement principal =
                CoopHideoutBossPhaseContract.ResolvePrincipalPlacement(
                    innerRadius,
                    _nightStagedPlacementActive ? walkDistance : 0f);

            Vec3 playerPrincipalInitialPosition = BuildForwardOffsetPosition(
                anchor,
                principal.PlayerInitialForwardOffset);
            Vec3 playerPrincipalTargetPosition = BuildForwardOffsetPosition(
                anchor,
                principal.PlayerTargetForwardOffset);
            Vec3 bossPrincipalInitialPosition = BuildForwardOffsetPosition(
                anchor,
                principal.BossInitialForwardOffset);
            Vec3 bossPrincipalTargetPosition = BuildForwardOffsetPosition(
                anchor,
                principal.BossTargetForwardOffset);

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
            if (_nightStagedPlacementActive)
            {
                AppendNativeNightCompanionPlacements(
                    placements,
                    playerCompanions,
                    playerPrincipalInitialPosition,
                    isPlayerSide: true,
                    playerFacingDirection);
                AppendNativeNightCompanionPlacements(
                    placements,
                    bossCompanions,
                    bossPrincipalInitialPosition,
                    isPlayerSide: false,
                    enemyFacingDirection);
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

            bool previousTeleportingAgents = Mission.IsTeleportingAgents;
            try
            {
                Mission.IsTeleportingAgents = true;
                foreach (BossFightParticipantPlacement placement in placements)
                {
                    _targetPlacements[placement.Agent.Index] = placement;
                    PlaceAgent(placement);
                }
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: placed boss-fight participants. " +
                    "NightStaged=" + _nightStagedPlacementActive +
                    " InnerRadius=" + innerRadius +
                    " OuterRadius=" + outerRadius +
                    " WalkDistance=" +
                    (_nightStagedPlacementActive ? walkDistance : 0f) +
                    " Layout=" +
                    (_nightStagedPlacementActive ? "native-triangular-rows" : "day-radial-arc") +
                    " Count=" + placements.Count + ".");
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

        private void AppendNativeNightCompanionPlacements(
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

        private void PlaceAgent(BossFightParticipantPlacement placement)
        {
            Agent agent = placement?.Agent;
            if (agent?.IsActive() != true)
                return;

            bool shouldApproach =
                _nightStagedPlacementActive &&
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
            if (shouldApproach)
            {
                if (agent.Formation != null)
                    agent.Formation = null;
                agent.SetMaximumSpeedLimit(NativeApproachSpeedLimit, isMultiplier: false);
                Vec3 targetGroundPosition = placement.TargetGroundPosition;
                var targetWorldPosition = new WorldPosition(
                    Mission.Scene,
                    UIntPtr.Zero,
                    targetGroundPosition,
                    hasValidZ: false);
                agent.SetScriptedPositionAndDirection(
                    ref targetWorldPosition,
                    direction.RotationInRadians,
                    addHumanLikeDelay: false,
                    additionalFlags: Agent.AIScriptedFrameFlags.DoNotRun);
            }
            else if (agent.IsAIControlled)
            {
                var worldPosition = new WorldPosition(
                    Mission.Scene,
                    UIntPtr.Zero,
                    groundPosition,
                    hasValidZ: false);
                agent.SetScriptedPositionAndDirection(
                    ref worldPosition,
                    direction.RotationInRadians,
                    addHumanLikeDelay: false);
            }
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

        private Vec3 BuildForwardOffsetPosition(MatrixFrame anchor, float forwardOffset)
        {
            return AddForwardOffset(anchor, anchor.origin, forwardOffset);
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

        private void FinalizeNightBossFightApproach()
        {
            if (!_nightStagedPlacementActive || _targetPlacements.Count == 0)
                return;

            bool previousTeleportingAgents = Mission.IsTeleportingAgents;
            try
            {
                Mission.IsTeleportingAgents = true;
                foreach (BossFightParticipantPlacement placement in
                         _targetPlacements.Values)
                {
                    Agent agent = placement?.Agent;
                    if (agent?.IsActive() != true)
                        continue;

                    Vec3 target = placement.TargetGroundPosition;
                    agent.MountAgent?.TeleportToPosition(target);
                    agent.TeleportToPosition(target);
                    agent.SetMaximumSpeedLimit(-1f, isMultiplier: false);
                    agent.DisableScriptedMovement();

                    Vec2 direction = placement.FacingDirection;
                    if (direction.LengthSquared < 0.0001f)
                        direction = new Vec2(0f, 1f);
                    direction.Normalize();
                    agent.LookDirection = new Vec3(direction.x, direction.y, 0f);
                    agent.SetMovementDirection(in direction);
                    ScriptAgentAtCurrentPosition(agent);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossPhaseController: final boss approach snap failed. Error=" +
                    ex.Message + ".");
            }
            finally
            {
                Mission.IsTeleportingAgents = previousTeleportingAgents;
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

        private void RestoreCombatAgent(FrozenAgentState frozen, bool restoreTeam)
        {
            Agent agent = frozen?.Agent;
            if (agent?.IsActive() != true)
                return;

            if (restoreTeam && frozen.OriginalTeam != null && agent.Team != frozen.OriginalTeam)
                agent.SetTeam(frozen.OriginalTeam, sync: true);
            if (!ReferenceEquals(agent.Formation, frozen.OriginalFormation))
                agent.Formation = frozen.OriginalFormation;
            agent.SetMortalityState(frozen.OriginalMortalityState);
            agent.SetMaximumSpeedLimit(-1f, isMultiplier: false);
            agent.DisableScriptedMovement();
            agent.SetLookAgent(null);
            agent.SetWatchState(Agent.WatchState.Alarmed);
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

            if (HostSelfJoinRedirectState.TryResolvePersistedHostedPeerUserName(out string hostUserName) &&
                !string.IsNullOrWhiteSpace(hostUserName))
            {
                NetworkCommunicator markedHost = GameNetwork.NetworkPeers.FirstOrDefault(peer =>
                    IsEligibleSynchronizedPeer(peer) &&
                    string.Equals(peer.UserName, hostUserName, StringComparison.OrdinalIgnoreCase));
                if (ResolveControlledAgent(markedHost)?.IsActive() == true)
                    return markedHost;
            }

            return GameNetwork.NetworkPeers.FirstOrDefault(peer =>
                IsEligibleSynchronizedPeer(peer) &&
                ResolveControlledAgent(peer)?.IsActive() == true);
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

        private static int ResolvePhaseDurationMilliseconds(CoopHideoutBossPhase phase)
        {
            if (phase == CoopHideoutBossPhase.PreparingCinematic)
                return CoopHideoutBossPhaseContract.CinematicReadyTimeoutMilliseconds;
            if (phase == CoopHideoutBossPhase.Cinematic)
                return CoopHideoutBossPhaseContract.CinematicDurationMilliseconds;
            if (phase == CoopHideoutBossPhase.AwaitingHostChoice)
                return CoopHideoutBossPhaseContract.HostChoiceTimeoutMilliseconds;
            return 0;
        }

        private sealed class FrozenAgentState
        {
            public FrozenAgentState(
                Agent agent,
                Team originalTeam,
                Formation originalFormation,
                Agent.MortalityState originalMortalityState)
            {
                Agent = agent;
                OriginalTeam = originalTeam;
                OriginalFormation = originalFormation;
                OriginalMortalityState = originalMortalityState;
            }

            public Agent Agent { get; }
            public Team OriginalTeam { get; }
            public Formation OriginalFormation { get; }
            public Agent.MortalityState OriginalMortalityState { get; }
        }
    }
}
