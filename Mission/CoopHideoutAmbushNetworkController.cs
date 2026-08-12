using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.MissionBehaviors
{
    public sealed class CoopHideoutAmbushNetworkController : MissionNetwork
    {
        private const float ServerPumpIntervalSeconds = 0.1f;
        private const float FullAwarenessRefreshSeconds = 1f;

        private readonly Dictionary<int, CoopHideoutAmbushState> _lastGuardStates =
            new Dictionary<int, CoopHideoutAmbushState>();
        private readonly HashSet<int> _cinematicReadyPeers = new HashSet<int>();
        private CoopHideoutAmbushState _serverState;
        private int _lastRuntimeRevision = -1;
        private float _nextServerPumpAt;
        private float _nextFullAwarenessRefreshAt;

        public static event Action<CoopHideoutAmbushState> ClientStateChanged;

        public static event Action<CoopHideoutAmbushState> ClientAwarenessChanged;

        public static CoopHideoutAmbushState CurrentClientState { get; private set; }

        public static IReadOnlyDictionary<int, CoopHideoutAmbushState> CurrentClientGuardStates =>
            _clientGuardStates;

        private static readonly Dictionary<int, CoopHideoutAmbushState> _clientGuardStates =
            new Dictionary<int, CoopHideoutAmbushState>();

        private static bool _usePointRequestPending;

        public static bool IsUsePointRequestPending => _usePointRequestPending;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            if (GameNetwork.IsClient)
            {
                CurrentClientState = null;
                _clientGuardStates.Clear();
                _usePointRequestPending = false;
            }

            if (!GameNetwork.IsServer)
                return;

            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            string battleInstanceId = snapshot?.BattleInstanceId;
            if (string.IsNullOrWhiteSpace(battleInstanceId))
                battleInstanceId = Guid.NewGuid().ToString("N");
            _serverState = new CoopHideoutAmbushState
            {
                BattleInstanceId = CoopHideoutAmbushContract.Bound(
                    battleInstanceId,
                    CoopHideoutAmbushContract.MaximumBattleInstanceIdCharacters),
                Revision = 1,
                Phase = CoopHideoutAmbushPhase.WaitingForMaterialization,
                Reason = "night-hideout-network-initialized"
            };
        }

        protected override void AddRemoveMessageHandlers(
            GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            if (GameNetwork.IsServer)
            {
                registerer.RegisterBaseHandler<CoopHideoutAmbushClientCommandMessage>(
                    HandleClientCommand);
            }
            if (GameNetwork.IsClient)
            {
                registerer.RegisterBaseHandler<CoopHideoutAmbushStateMessage>(
                    HandleServerState);
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (!GameNetwork.IsServer || _serverState == null || Mission == null)
                return;
            if (Mission.CurrentTime < _nextServerPumpAt)
                return;

            _nextServerPumpAt = Mission.CurrentTime + ServerPumpIntervalSeconds;
            PumpServerState(forceAwareness: Mission.CurrentTime >= _nextFullAwarenessRefreshAt);
        }

        protected override void HandleNewClientAfterSynchronized(
            NetworkCommunicator networkPeer)
        {
            base.HandleNewClientAfterSynchronized(networkPeer);
            if (!GameNetwork.IsServer ||
                networkPeer == null ||
                networkPeer.IsServerPeer ||
                _serverState == null)
            {
                return;
            }

            SendState(networkPeer, BuildGlobalState());
            foreach (CoopHideoutAmbushState guardState in _lastGuardStates.Values)
                SendState(networkPeer, guardState);
        }

        protected override void HandlePlayerDisconnect(NetworkCommunicator networkPeer)
        {
            base.HandlePlayerDisconnect(networkPeer);
            if (networkPeer != null)
                _cinematicReadyPeers.Remove(networkPeer.Index);
        }

        public override void OnMissionStateFinalized()
        {
            if (GameNetwork.IsClient)
            {
                CurrentClientState = null;
                _clientGuardStates.Clear();
                _usePointRequestPending = false;
            }
            base.OnMissionStateFinalized();
        }

        public static bool SendUsePointRequest()
        {
            if (_usePointRequestPending)
                return false;

            _usePointRequestPending = true;
            if (SendClientCommand(CoopHideoutAmbushClientCommandKind.UseCallTroopsPoint))
                return true;

            _usePointRequestPending = false;
            return false;
        }

        public static bool SendCinematicReady()
        {
            return SendClientCommand(CoopHideoutAmbushClientCommandKind.CinematicReady);
        }

        private static bool SendClientCommand(
            CoopHideoutAmbushClientCommandKind commandKind)
        {
            CoopHideoutAmbushState state = CurrentClientState;
            if (!GameNetwork.IsClient ||
                !GameNetwork.IsSessionActive ||
                state == null)
            {
                return false;
            }

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopHideoutAmbushClientCommandMessage(
                    state.BattleInstanceId,
                    state.Revision,
                    commandKind));
                GameNetwork.EndModuleEventAsClient();
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutAmbushNetworkController: client command send failed. " +
                    "Kind=" + commandKind + " Error=" + ex.Message + ".");
                return false;
            }
        }

        private bool HandleClientCommand(
            NetworkCommunicator peer,
            GameNetworkMessage baseMessage)
        {
            CoopHideoutAmbushClientCommandMessage message =
                baseMessage as CoopHideoutAmbushClientCommandMessage;
            if (peer == null || message == null || _serverState == null)
                return false;
            if (!string.Equals(
                    message.BattleInstanceId,
                    _serverState.BattleInstanceId,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (message.CommandKind ==
                CoopHideoutAmbushClientCommandKind.CinematicReady)
            {
                if (_serverState.Phase == CoopHideoutAmbushPhase.CallTroops &&
                    message.Revision == _serverState.Revision)
                {
                    _cinematicReadyPeers.Add(peer.Index);
                }
                return true;
            }

            CoopExactCampaignHideoutAmbushMissionController controller =
                Mission.GetMissionBehavior<CoopExactCampaignHideoutAmbushMissionController>();
            bool senderIsHost = IsHostPeer(peer);
            if (!CoopHideoutAmbushContract.TryValidateHostUseRequest(
                    senderIsHost,
                    controller?.Phase ?? CoopHideoutAmbushPhase.WaitingForMaterialization,
                    message.Revision,
                    _serverState.Revision,
                    out bool idempotent,
                    out string rejection))
            {
                ModLogger.Info(
                    "CoopHideoutAmbushNetworkController: call-troops request rejected. " +
                    "Peer=" + peer.Index +
                    " Revision=" + message.Revision +
                    " Reason=" + rejection + ".");
                SendState(
                    peer,
                    BuildGlobalState(
                        CoopHideoutAmbushContract.CallTroopsRequestResponseReasonPrefix +
                        rejection));
                return true;
            }

            if (!idempotent &&
                (controller == null ||
                 !controller.TryBeginCallTroopsFromPeer(peer, out rejection)))
            {
                ModLogger.Info(
                    "CoopHideoutAmbushNetworkController: validated call-troops request was not applied. " +
                    "Peer=" + peer.Index + " Reason=" + rejection + ".");
                SendState(
                    peer,
                    BuildGlobalState(
                        CoopHideoutAmbushContract.CallTroopsRequestResponseReasonPrefix +
                        rejection));
                return true;
            }

            PumpServerState(forceAwareness: true);
            return true;
        }

        private void HandleServerState(GameNetworkMessage baseMessage)
        {
            CoopHideoutAmbushStateMessage message =
                baseMessage as CoopHideoutAmbushStateMessage;
            if (message == null ||
                message.ProtocolVersion != CoopHideoutAmbushContract.ProtocolVersion)
            {
                return;
            }

            CoopHideoutAmbushState state = message.ToState();
            if (CurrentClientState != null &&
                string.Equals(
                    CurrentClientState.BattleInstanceId,
                    state.BattleInstanceId,
                    StringComparison.Ordinal) &&
                state.Revision < CurrentClientState.Revision)
            {
                return;
            }

            if (state.GuardAgentIndex < 0)
            {
                if (_usePointRequestPending &&
                    CoopHideoutAmbushContract.ShouldReleaseUsePointRequestPending(
                        state.Phase,
                        state.Reason))
                {
                    _usePointRequestPending = false;
                }
                CurrentClientState = state;
                try
                {
                    ClientStateChanged?.Invoke(state.Clone());
                }
                catch (Exception ex)
                {
                    ModLogger.Error(
                        "CoopHideoutAmbushNetworkController: client phase dispatch failed.",
                        ex);
                }
                return;
            }

            _clientGuardStates[state.GuardAgentIndex] = state;
            try
            {
                ClientAwarenessChanged?.Invoke(state.Clone());
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopHideoutAmbushNetworkController: client awareness dispatch failed.",
                    ex);
            }
        }

        private void PumpServerState(bool forceAwareness)
        {
            CoopExactCampaignHideoutAmbushMissionController controller =
                Mission.GetMissionBehavior<CoopExactCampaignHideoutAmbushMissionController>();
            if (controller == null)
                return;

            CoopHideoutStealthPatrolController stealthController =
                Mission.GetMissionBehavior<CoopHideoutStealthPatrolController>();
            bool phaseChanged = _lastRuntimeRevision != controller.StateRevision;
            bool globalAlarmChanged =
                _serverState.HasGlobalAlarm != (stealthController?.HasGlobalAlarm == true);
            bool alarmFailureCounterActive =
                controller.IsAlarmFailureCounterActive;
            int alarmFailureRemainingMilliseconds =
                controller.AlarmFailureRemainingMilliseconds;
            bool alarmFailureCounterChanged =
                _serverState.IsAlarmFailureCounterActive !=
                    alarmFailureCounterActive ||
                _serverState.AlarmFailureRemainingMilliseconds !=
                    alarmFailureRemainingMilliseconds;
            bool usePointChanged =
                _serverState.IsUsePointAvailable != controller.IsUsePointAvailable;
            if (phaseChanged ||
                globalAlarmChanged ||
                alarmFailureCounterChanged ||
                usePointChanged)
            {
                _lastRuntimeRevision = controller.StateRevision;
                _serverState.Revision++;
                _serverState.Phase = controller.Phase;
                _serverState.HasGlobalAlarm = stealthController?.HasGlobalAlarm == true;
                _serverState.IsAlarmFailureCounterActive =
                    alarmFailureCounterActive;
                _serverState.AlarmFailureRemainingMilliseconds =
                    alarmFailureRemainingMilliseconds;
                _serverState.IsUsePointAvailable = controller.IsUsePointAvailable;
                _serverState.Reason = phaseChanged
                    ? "phase:" + controller.Phase
                    : globalAlarmChanged
                        ? "global-alarm-changed"
                        : alarmFailureCounterChanged
                            ? "alarm-failure-counter-changed"
                            : "use-point-availability-changed";
                BroadcastState(BuildGlobalState());
            }

            if (forceAwareness)
                _nextFullAwarenessRefreshAt = Mission.CurrentTime + FullAwarenessRefreshSeconds;
            foreach (CoopHideoutAmbushAwarenessSnapshot awareness in
                     stealthController?.GetAwarenessSnapshots() ??
                     Array.Empty<CoopHideoutAmbushAwarenessSnapshot>())
            {
                CoopHideoutAmbushState guardState = BuildGuardState(awareness);
                if (!forceAwareness &&
                    _lastGuardStates.TryGetValue(
                        guardState.GuardAgentIndex,
                        out CoopHideoutAmbushState previous) &&
                    Math.Abs(previous.SuspicionPermille - guardState.SuspicionPermille) < 25 &&
                    previous.ObservedAgentIndex == guardState.ObservedAgentIndex &&
                    previous.IsAlarmed == guardState.IsAlarmed)
                {
                    continue;
                }

                _lastGuardStates[guardState.GuardAgentIndex] = guardState;
                BroadcastState(guardState);
            }
        }

        private CoopHideoutAmbushState BuildGlobalState(string reasonOverride = null)
        {
            CoopHideoutAmbushState state = _serverState.Clone();
            state.GuardAgentIndex = -1;
            state.ObservedAgentIndex = -1;
            state.SuspicionPermille = 0;
            state.IsAlarmed = false;
            if (!string.IsNullOrWhiteSpace(reasonOverride))
                state.Reason = reasonOverride;
            return state;
        }

        private CoopHideoutAmbushState BuildGuardState(
            CoopHideoutAmbushAwarenessSnapshot awareness)
        {
            return new CoopHideoutAmbushState
            {
                BattleInstanceId = _serverState.BattleInstanceId,
                Revision = _serverState.Revision,
                Phase = _serverState.Phase,
                GuardAgentIndex = awareness?.GuardAgentIndex ?? -1,
                ObservedAgentIndex = awareness?.ObservedAgentIndex ?? -1,
                SuspicionPermille = CoopHideoutAmbushContract.CompressSuspicion(
                    awareness?.Suspicion01 ?? 0f),
                IsAlarmed = awareness?.IsAlarmed ?? false,
                HasGlobalAlarm = _serverState.HasGlobalAlarm,
                IsAlarmFailureCounterActive =
                    _serverState.IsAlarmFailureCounterActive,
                AlarmFailureRemainingMilliseconds =
                    _serverState.AlarmFailureRemainingMilliseconds,
                IsUsePointAvailable = _serverState.IsUsePointAvailable,
                Reason = "guard-awareness"
            };
        }

        private static bool IsHostPeer(NetworkCommunicator peer)
        {
            if (peer == null ||
                peer.IsServerPeer ||
                !peer.IsConnectionActive ||
                !peer.IsSynchronized)
            {
                return false;
            }

            if (HostSelfJoinRedirectState.TryResolvePersistedHostedPeerUserName(
                    out string hostUserName) &&
                !string.IsNullOrWhiteSpace(hostUserName))
            {
                return string.Equals(
                    peer.UserName,
                    hostUserName,
                    StringComparison.OrdinalIgnoreCase);
            }

            return GameNetwork.NetworkPeers?
                .Where(candidate =>
                    candidate != null &&
                    !candidate.IsServerPeer &&
                    candidate.IsConnectionActive &&
                    candidate.IsSynchronized)
                .OrderBy(candidate => candidate.Index)
                .FirstOrDefault()?.Index == peer.Index;
        }

        private static Agent ResolveControlledAgent(NetworkCommunicator peer)
        {
            MissionPeer missionPeer = peer?.GetComponent<MissionPeer>();
            return missionPeer?.ControlledAgent ?? peer?.ControlledAgent;
        }

        private static void BroadcastState(CoopHideoutAmbushState state)
        {
            if (!GameNetwork.IsServer || state == null)
                return;
            try
            {
                GameNetwork.BeginBroadcastModuleEvent();
                GameNetwork.WriteMessage(new CoopHideoutAmbushStateMessage(state));
                GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutAmbushNetworkController: state broadcast failed. " +
                    "Error=" + ex.Message + ".");
            }
        }

        private static void SendState(
            NetworkCommunicator peer,
            CoopHideoutAmbushState state)
        {
            if (!GameNetwork.IsServer || peer == null || state == null)
                return;
            try
            {
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(new CoopHideoutAmbushStateMessage(state));
                GameNetwork.EndModuleEventAsServer();
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutAmbushNetworkController: direct state send failed. " +
                    "Peer=" + peer.Index + " Error=" + ex.Message + ".");
            }
        }
    }
}
