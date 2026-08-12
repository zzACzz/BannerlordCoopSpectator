using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.MissionBehaviors
{
    public sealed class CoopBattlePowerNetworkController : MissionNetwork
    {
        private const float ServerBroadcastIntervalSeconds = 0.1f;

        private readonly Dictionary<int, TrackedAgentPower> _trackedAgents =
            new Dictionary<int, TrackedAgentPower>();
        private CoopBattlePowerState _serverState;
        private bool _serverStateDirty;
        private float _nextBroadcastAt;

        public static event Action<CoopBattlePowerState> ClientStateChanged;

        public static CoopBattlePowerState CurrentClientState { get; private set; }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            if (GameNetwork.IsClient)
                CurrentClientState = null;
            if (!GameNetwork.IsServer)
                return;

            _serverState = new CoopBattlePowerState
            {
                BattleInstanceId = CoopBattlePowerContract.BoundBattleInstanceId(
                    BattleSnapshotRuntimeState.GetCurrent()?.BattleInstanceId ??
                    Guid.NewGuid().ToString("N")),
                Revision = 1
            };
            InitializeServerPower();
        }

        protected override void AddRemoveMessageHandlers(
            GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            if (GameNetwork.IsClient)
            {
                registerer.RegisterBaseHandler<CoopBattlePowerStateMessage>(
                    HandleServerState);
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (!GameNetwork.IsServer || !_serverStateDirty || _serverState == null)
                return;
            if (Mission.CurrentTime < _nextBroadcastAt)
                return;

            _nextBroadcastAt = Mission.CurrentTime + ServerBroadcastIntervalSeconds;
            _serverStateDirty = false;
            _serverState.Revision++;
            BroadcastState(_serverState);
        }

        public override void OnAgentBuild(Agent agent, Banner banner)
        {
            base.OnAgentBuild(agent, banner);
            TrackBuiltAgent(agent);
        }

        public override void OnAgentTeamChanged(
            Team prevTeam,
            Team newTeam,
            Agent agent)
        {
            base.OnAgentTeamChanged(prevTeam, newTeam, agent);
            if (prevTeam == Team.Invalid)
                TrackBuiltAgent(agent);
        }

        public override void OnAgentRemoved(
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            KillingBlow blow)
        {
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, blow);
            if (!GameNetwork.IsServer ||
                affectedAgent == null ||
                !_trackedAgents.TryGetValue(
                    affectedAgent.Index,
                    out TrackedAgentPower tracked) ||
                tracked.Removed)
            {
                return;
            }

            tracked.Removed = true;
            SubtractCurrentPower(tracked.Side, tracked.Power);
            _serverStateDirty = true;
        }

        protected override void HandleNewClientAfterSynchronized(
            NetworkCommunicator networkPeer)
        {
            base.HandleNewClientAfterSynchronized(networkPeer);
            if (GameNetwork.IsServer &&
                networkPeer != null &&
                !networkPeer.IsServerPeer &&
                _serverState != null)
            {
                SendState(networkPeer, _serverState);
            }
        }

        public override void OnMissionStateFinalized()
        {
            if (GameNetwork.IsClient)
                CurrentClientState = null;
            _trackedAgents.Clear();
            _serverState = null;
            base.OnMissionStateFinalized();
        }

        private void InitializeServerPower()
        {
            int snapshotAttackerPower =
                CalculateSnapshotSidePower(BattleSideEnum.Attacker);
            int snapshotDefenderPower =
                CalculateSnapshotSidePower(BattleSideEnum.Defender);
            _serverState.InitialAttackerPower = 0;
            _serverState.CurrentAttackerPower = 0;
            _serverState.InitialDefenderPower = 0;
            _serverState.CurrentDefenderPower = 0;
            _serverState.IsAvailable = false;
            _serverStateDirty = true;

            ModLogger.Info(
                "CoopBattlePowerNetworkController: initialized authoritative battle power. " +
                "Scene=" + (Mission?.SceneName ?? "null") +
                " SnapshotAttacker=" + snapshotAttackerPower +
                " SnapshotDefender=" + snapshotDefenderPower +
                " Built-agent power will follow native campaign scoreboard semantics.");
        }

        private static int CalculateSnapshotSidePower(BattleSideEnum side)
        {
            BattleSideState sideState = BattleSnapshotRuntimeState.GetSideState(side);
            int total = 0;
            foreach (RosterEntryState entry in sideState?.Entries ??
                     Enumerable.Empty<RosterEntryState>())
            {
                total = CoopBattlePowerContract.AddClamped(
                    total,
                    CoopBattlePowerContract.CalculateAvailableStackPower(
                        entry.Count,
                        entry.WoundedCount,
                        entry.Tier,
                        entry.IsHero,
                        entry.HeroLevel,
                        entry.IsMounted));
            }
            return total;
        }

        private static bool IsBattleAgent(Agent agent)
        {
            return agent?.IsHuman == true &&
                   agent.Team != null &&
                   agent.Team != Team.Invalid &&
                   (agent.Team.Side == BattleSideEnum.Attacker ||
                    agent.Team.Side == BattleSideEnum.Defender);
        }

        private static int ResolveAgentPower(Agent agent)
        {
            if (ExactCampaignArmyBootstrap.TryGetEntryId(agent, out string entryId))
            {
                RosterEntryState entry =
                    BattleSnapshotRuntimeState.GetEntryState(entryId);
                if (entry != null)
                {
                    return CoopBattlePowerContract.CalculateUnitPower(
                        entry.Tier,
                        entry.IsHero,
                        entry.HeroLevel,
                        entry.IsMounted);
                }
            }

            BasicCharacterObject character = agent?.Character;
            if (character == null)
                return 0;
            return CoopBattlePowerContract.QuantizePower(character.GetPower());
        }

        private void TrackBuiltAgent(Agent agent)
        {
            if (!GameNetwork.IsServer ||
                _serverState == null ||
                !IsBattleAgent(agent))
            {
                return;
            }

            if (_trackedAgents.TryGetValue(
                    agent.Index,
                    out TrackedAgentPower existing) &&
                !existing.Removed)
            {
                return;
            }

            int power = ResolveAgentPower(agent);
            if (power <= 0)
                return;

            _trackedAgents[agent.Index] =
                new TrackedAgentPower(agent.Team.Side, power);
            AddInitialPower(agent.Team.Side, power);
            AddCurrentPower(agent.Team.Side, power);
            _serverState.IsAvailable =
                _serverState.InitialAttackerPower > 0 &&
                _serverState.InitialDefenderPower > 0;
            _serverStateDirty = true;
        }

        private void AddInitialPower(BattleSideEnum side, int power)
        {
            if (side == BattleSideEnum.Attacker)
            {
                _serverState.InitialAttackerPower =
                    CoopBattlePowerContract.AddClamped(
                        _serverState.InitialAttackerPower,
                        power);
            }
            else
            {
                _serverState.InitialDefenderPower =
                    CoopBattlePowerContract.AddClamped(
                        _serverState.InitialDefenderPower,
                        power);
            }
        }

        private void AddCurrentPower(BattleSideEnum side, int power)
        {
            if (side == BattleSideEnum.Attacker)
            {
                _serverState.CurrentAttackerPower =
                    CoopBattlePowerContract.AddClamped(
                        _serverState.CurrentAttackerPower,
                        power);
            }
            else
            {
                _serverState.CurrentDefenderPower =
                    CoopBattlePowerContract.AddClamped(
                        _serverState.CurrentDefenderPower,
                        power);
            }
        }

        private void SubtractCurrentPower(BattleSideEnum side, int power)
        {
            if (side == BattleSideEnum.Attacker)
            {
                _serverState.CurrentAttackerPower =
                    CoopBattlePowerContract.SubtractClamped(
                        _serverState.CurrentAttackerPower,
                        power);
            }
            else
            {
                _serverState.CurrentDefenderPower =
                    CoopBattlePowerContract.SubtractClamped(
                        _serverState.CurrentDefenderPower,
                        power);
            }
        }

        private void HandleServerState(GameNetworkMessage baseMessage)
        {
            CoopBattlePowerStateMessage message =
                baseMessage as CoopBattlePowerStateMessage;
            if (message == null ||
                message.ProtocolVersion != CoopBattlePowerContract.ProtocolVersion)
            {
                return;
            }

            CoopBattlePowerState state = message.ToState();
            if (CurrentClientState != null &&
                string.Equals(
                    CurrentClientState.BattleInstanceId,
                    state.BattleInstanceId,
                    StringComparison.Ordinal) &&
                state.Revision < CurrentClientState.Revision)
            {
                return;
            }

            CurrentClientState = state;
            try
            {
                ClientStateChanged?.Invoke(state.Clone());
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopBattlePowerNetworkController: client state dispatch failed.",
                    ex);
            }
        }

        private static void BroadcastState(CoopBattlePowerState state)
        {
            try
            {
                GameNetwork.BeginBroadcastModuleEvent();
                GameNetwork.WriteMessage(new CoopBattlePowerStateMessage(state));
                GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopBattlePowerNetworkController: state broadcast failed. " +
                    "Error=" + ex.Message + ".");
            }
        }

        private static void SendState(
            NetworkCommunicator peer,
            CoopBattlePowerState state)
        {
            try
            {
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(new CoopBattlePowerStateMessage(state));
                GameNetwork.EndModuleEventAsServer();
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopBattlePowerNetworkController: direct state send failed. " +
                    "Peer=" + peer.Index + " Error=" + ex.Message + ".");
            }
        }

        private sealed class TrackedAgentPower
        {
            public TrackedAgentPower(BattleSideEnum side, int power)
            {
                Side = side;
                Power = power;
            }

            public BattleSideEnum Side { get; }

            public int Power { get; }

            public bool Removed { get; set; }
        }
    }
}
