using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using Newtonsoft.Json;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.MissionBehaviors
{
    public sealed class CoopHeroCreationMissionNetwork : MissionNetwork
    {
        private static readonly TimeSpan ChunkTransferTimeout = TimeSpan.FromSeconds(15);
        private static int _nextClientTransferId;

        private readonly Dictionary<string, CoopHeroCreationParticipantSession> _sessionsByIdentity =
            new Dictionary<string, CoopHeroCreationParticipantSession>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, string> _identityByPeerIndex = new Dictionary<int, string>();
        private readonly Dictionary<int, CoopHeroCreationParticipantState> _excludedStateByPeerIndex =
            new Dictionary<int, CoopHeroCreationParticipantState>();
        private readonly HashSet<int> _pendingServerEnvelopePeerIndices = new HashSet<int>();
        private readonly Dictionary<int, PendingClientSubmissionTransfer> _pendingClientSubmissionByPeerIndex =
            new Dictionary<int, PendingClientSubmissionTransfer>();

        private CoopHeroCreationRequest _request;
        private DateTime _enrollmentDeadlineUtc;
        private DateTime _sessionDeadlineUtc;
        private DateTime _nextServerPumpUtc;
        private bool _enrollmentClosed;
        private bool _resultWritten;
        private bool _missionEnding;
        private string _lastPublishedProgressSignature;
        private int _nextServerTransferId;
        private int _clientEnvelopeTransferId = -1;
        private CoopHeroCreationChunkAccumulator _clientEnvelopeAccumulator;
        private string _lastCompletedClientEnvelopeTransportHash;

        public static event Action<CoopHeroCreationServerEnvelope> ClientEnvelopeReceived;
        public static CoopHeroCreationServerEnvelope CurrentClientEnvelope { get; private set; }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            if (GameNetwork.IsClient)
            {
                CurrentClientEnvelope = null;
                _clientEnvelopeTransferId = -1;
                _clientEnvelopeAccumulator = null;
                _lastCompletedClientEnvelopeTransportHash = null;
            }
            if (!GameNetwork.IsServer) return;

            string error;
            if (!CoopHeroCreationBridgeFile.TryReadRequest(out _request, out error))
            {
                ModLogger.Info("CoopHeroCreationMissionNetwork: request rejected. Error=" + error);
                _request = null;
                _sessionDeadlineUtc = DateTime.UtcNow;
                _enrollmentDeadlineUtc = _sessionDeadlineUtc;
                _enrollmentClosed = true;
                return;
            }
            if (!CoopHeroCreationContract.ValidateRequest(_request, out error))
            {
                ModLogger.Info("CoopHeroCreationMissionNetwork: request rejected. Error=" + error);
                WriteFailureResult(_request, error);
                _request = null;
                _sessionDeadlineUtc = DateTime.UtcNow;
                _enrollmentDeadlineUtc = _sessionDeadlineUtc;
                _enrollmentClosed = true;
                return;
            }

            DateTime now = DateTime.UtcNow;
            _enrollmentDeadlineUtc = now.AddSeconds(_request.Rules.EnrollmentSeconds);
            _sessionDeadlineUtc = now.AddSeconds(_request.Rules.SessionSeconds);
            _nextServerPumpUtc = now;
            PublishProgressIfChanged();
            ModLogger.Info(
                "CoopHeroCreationMissionNetwork: authoritative session initialized. RequestId=" + _request.RequestId +
                " EnrollmentSeconds=" + _request.Rules.EnrollmentSeconds +
                " SessionSeconds=" + _request.Rules.SessionSeconds + ".");
        }

        protected override void AddRemoveMessageHandlers(GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            if (GameNetwork.IsServer)
            {
                registerer.RegisterBaseHandler<CoopHeroCreationClientCommandMessage>(HandleClientCommand);
                registerer.RegisterBaseHandler<CoopHeroCreationClientPayloadChunkMessage>(HandleClientPayloadChunk);
            }
            if (GameNetwork.IsClient)
            {
                registerer.RegisterBaseHandler<CoopHeroCreationServerEnvelopeMessage>(HandleServerEnvelope);
                registerer.RegisterBaseHandler<CoopHeroCreationServerEnvelopeChunkMessage>(HandleServerEnvelopeChunk);
            }
        }

        protected override void OnUdpNetworkHandlerTick()
        {
            if (!GameNetwork.IsServer || Mission == null) return;
            if (_request == null)
            {
                BeginMissionEnd();
                return;
            }
            DateTime now = DateTime.UtcNow;
            if (now < _nextServerPumpUtc) return;
            _nextServerPumpUtc = now.AddMilliseconds(250);

            EnrollSynchronizedPeers(now);
            if (!_enrollmentClosed && now >= _enrollmentDeadlineUtc)
            {
                _enrollmentClosed = true;
                QueueServerEnvelopeForAllPeers();
            }

            int terminalCountBeforeTimeouts = _sessionsByIdentity.Values.Count(s => CoopHeroCreationContract.IsTerminal(s.State));
            if (_request != null)
                CoopHeroCreationStateMachine.ApplyTimeouts(
                    _sessionsByIdentity.Values,
                    now,
                    _sessionDeadlineUtc,
                    TimeSpan.FromSeconds(_request.Rules.DisconnectGraceSeconds));
            if (_sessionsByIdentity.Values.Count(s => CoopHeroCreationContract.IsTerminal(s.State)) != terminalCountBeforeTimeouts)
                QueueServerEnvelopeForAllPeers();

            PrunePendingClientSubmissions(now);

            SendPendingAndChangedEnvelopes();
            if (ShouldFinalize()) FinalizeAuthoritativeResult();
        }

        protected override void HandleNewClientAfterSynchronized(NetworkCommunicator networkPeer)
        {
            base.HandleNewClientAfterSynchronized(networkPeer);
            if (networkPeer != null && !networkPeer.IsServerPeer)
                _pendingServerEnvelopePeerIndices.Add(networkPeer.Index);
        }

        protected override void HandlePlayerDisconnect(NetworkCommunicator networkPeer)
        {
            base.HandlePlayerDisconnect(networkPeer);
            if (networkPeer == null) return;
            string identity;
            bool stateChanged = false;
            if (_identityByPeerIndex.TryGetValue(networkPeer.Index, out identity))
            {
                CoopHeroCreationParticipantSession session;
                if (_sessionsByIdentity.TryGetValue(identity, out session))
                    stateChanged = CoopHeroCreationStateMachine.Disconnect(session, DateTime.UtcNow);
            }
            _identityByPeerIndex.Remove(networkPeer.Index);
            _excludedStateByPeerIndex.Remove(networkPeer.Index);
            _pendingServerEnvelopePeerIndices.Remove(networkPeer.Index);
            _pendingClientSubmissionByPeerIndex.Remove(networkPeer.Index);
            if (stateChanged) QueueServerEnvelopeForAllPeers();
        }

        private bool HandleClientCommand(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            CoopHeroCreationClientCommandMessage message = baseMessage as CoopHeroCreationClientCommandMessage;
            if (peer == null || message == null || _request == null) return false;
            string identity;
            CoopHeroCreationParticipantSession session;
            if (!_identityByPeerIndex.TryGetValue(peer.Index, out identity) ||
                !_sessionsByIdentity.TryGetValue(identity, out session))
            {
                _pendingServerEnvelopePeerIndices.Add(peer.Index);
                return true;
            }

            string reason = string.Empty;
            CoopHeroCreationClientCommandKind kind = (CoopHeroCreationClientCommandKind)message.CommandKind;
            if (kind == CoopHeroCreationClientCommandKind.BeginEditing)
                CoopHeroCreationStateMachine.BeginEditing(session, out reason);
            else if (kind == CoopHeroCreationClientCommandKind.Decline)
                CoopHeroCreationStateMachine.Decline(session, out reason);
            else if (kind == CoopHeroCreationClientCommandKind.Submit)
                TryBeginClientSubmissionTransfer(peer, message, out reason);
            else reason = "command_unknown";

            session.Reason = string.IsNullOrWhiteSpace(reason) ? session.Reason : reason;
            QueueServerEnvelopeForAllPeers();
            return true;
        }

        private void HandleServerEnvelope(GameNetworkMessage baseMessage)
        {
            CoopHeroCreationServerEnvelopeMessage message = baseMessage as CoopHeroCreationServerEnvelopeMessage;
            if (message == null) return;

            if (string.Equals(
                    message.TransportHash,
                    _lastCompletedClientEnvelopeTransportHash,
                    StringComparison.OrdinalIgnoreCase))
                return;

            if (_clientEnvelopeAccumulator != null &&
                _clientEnvelopeTransferId == message.TransferId &&
                _clientEnvelopeAccumulator.Matches(
                    message.ChunkCount,
                    message.LogicalByteCount,
                    message.TransportHash))
                return;

            CoopHeroCreationChunkAccumulator accumulator;
            string error;
            if (!CoopHeroCreationChunkAccumulator.TryCreate(
                    message.ChunkCount,
                    message.LogicalByteCount,
                    message.TransportHash,
                    DateTime.UtcNow,
                    out accumulator,
                    out error))
            {
                ModLogger.Info("CoopHeroCreationMissionNetwork: client envelope manifest rejected. Error=" + error);
                return;
            }

            _clientEnvelopeTransferId = message.TransferId;
            _clientEnvelopeAccumulator = accumulator;
        }

        private bool HandleClientPayloadChunk(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            CoopHeroCreationClientPayloadChunkMessage message = baseMessage as CoopHeroCreationClientPayloadChunkMessage;
            if (peer == null || message == null || _request == null) return false;

            PendingClientSubmissionTransfer transfer;
            if (!_pendingClientSubmissionByPeerIndex.TryGetValue(peer.Index, out transfer) ||
                transfer.TransferId != message.TransferId)
                return true;

            bool completed;
            string error;
            if (!transfer.Accumulator.TryAccept(
                    message.ChunkIndex,
                    message.ChunkCount,
                    message.PayloadBytes,
                    DateTime.UtcNow,
                    out completed,
                    out error))
            {
                _pendingClientSubmissionByPeerIndex.Remove(peer.Index);
                SetPeerSessionReason(peer, error);
                QueueServerEnvelopeForAllPeers();
                return true;
            }

            if (!completed) return true;
            _pendingClientSubmissionByPeerIndex.Remove(peer.Index);

            string payloadJson;
            if (!transfer.Accumulator.TryComplete(out payloadJson, out error))
            {
                SetPeerSessionReason(peer, error);
                QueueServerEnvelopeForAllPeers();
                return true;
            }

            CompleteClientSubmission(peer, transfer, payloadJson);
            QueueServerEnvelopeForAllPeers();
            return true;
        }

        private void HandleServerEnvelopeChunk(GameNetworkMessage baseMessage)
        {
            CoopHeroCreationServerEnvelopeChunkMessage message = baseMessage as CoopHeroCreationServerEnvelopeChunkMessage;
            if (message == null || _clientEnvelopeAccumulator == null ||
                message.TransferId != _clientEnvelopeTransferId)
                return;

            bool completed;
            string error;
            if (!_clientEnvelopeAccumulator.TryAccept(
                    message.ChunkIndex,
                    message.ChunkCount,
                    message.PayloadBytes,
                    DateTime.UtcNow,
                    out completed,
                    out error))
            {
                _clientEnvelopeAccumulator = null;
                _clientEnvelopeTransferId = -1;
                ModLogger.Info("CoopHeroCreationMissionNetwork: client envelope chunk rejected. Error=" + error);
                return;
            }

            if (!completed) return;

            CoopHeroCreationChunkAccumulator completedAccumulator = _clientEnvelopeAccumulator;
            _clientEnvelopeAccumulator = null;
            _clientEnvelopeTransferId = -1;
            string payloadJson;
            if (!completedAccumulator.TryComplete(out payloadJson, out error))
            {
                ModLogger.Info("CoopHeroCreationMissionNetwork: client envelope rejected. Error=" + error);
                return;
            }

            CoopHeroCreationServerEnvelope envelope;
            try
            {
                envelope = JsonConvert.DeserializeObject<CoopHeroCreationServerEnvelope>(payloadJson);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopHeroCreationMissionNetwork: client envelope JSON rejected. Error=" + ex.Message);
                return;
            }

            if (envelope == null || envelope.ProtocolVersion != CoopHeroCreationContract.ProtocolVersion) return;
            _lastCompletedClientEnvelopeTransportHash = completedAccumulator.PayloadHash;
            CurrentClientEnvelope = envelope;
            try
            {
                ClientEnvelopeReceived?.Invoke(envelope);
            }
            catch (Exception ex)
            {
                ModLogger.Error("CoopHeroCreationMissionNetwork: client envelope UI dispatch failed.", ex);
            }
        }

        private bool TryBeginClientSubmissionTransfer(
            NetworkCommunicator peer,
            CoopHeroCreationClientCommandMessage message,
            out string reason)
        {
            if (message.Revision <= 0 ||
                string.IsNullOrWhiteSpace(message.SubmissionId) ||
                message.SubmissionId.Length > CoopHeroCreationClientCommandMessage.MaxSubmissionIdCharacters)
            {
                reason = "revision_or_submission_invalid";
                return false;
            }
            if (!CoopHeroCreationChunkCodec.IsSha256(message.PayloadHash))
            {
                reason = "payload_hash_invalid";
                return false;
            }

            PendingClientSubmissionTransfer existing;
            if (_pendingClientSubmissionByPeerIndex.TryGetValue(peer.Index, out existing) &&
                existing.TransferId == message.TransferId)
            {
                bool exactManifestRetry =
                    existing.Revision == message.Revision &&
                    string.Equals(existing.SubmissionId, message.SubmissionId, StringComparison.Ordinal) &&
                    string.Equals(existing.PayloadHash, message.PayloadHash, StringComparison.OrdinalIgnoreCase) &&
                    existing.Accumulator.Matches(
                        message.ChunkCount,
                        message.LogicalByteCount,
                        message.TransportHash);
                reason = exactManifestRetry ? string.Empty : "submission_manifest_conflict";
                return exactManifestRetry;
            }

            CoopHeroCreationChunkAccumulator accumulator;
            if (!CoopHeroCreationChunkAccumulator.TryCreate(
                    message.ChunkCount,
                    message.LogicalByteCount,
                    message.TransportHash,
                    DateTime.UtcNow,
                    out accumulator,
                    out reason))
                return false;

            _pendingClientSubmissionByPeerIndex[peer.Index] = new PendingClientSubmissionTransfer(
                message.TransferId,
                message.Revision,
                message.SubmissionId,
                message.PayloadHash,
                accumulator);
            reason = string.Empty;
            return true;
        }

        private void CompleteClientSubmission(
            NetworkCommunicator peer,
            PendingClientSubmissionTransfer transfer,
            string payloadJson)
        {
            string identity;
            CoopHeroCreationParticipantSession session;
            if (!_identityByPeerIndex.TryGetValue(peer.Index, out identity) ||
                !_sessionsByIdentity.TryGetValue(identity, out session))
                return;

            string reason = string.Empty;
            CoopHeroDraft draft = null;
            if ((payloadJson ?? string.Empty).Length > _request.Rules.MaximumPayloadCharacters)
                reason = "payload_too_large";
            else
            {
                try { draft = JsonConvert.DeserializeObject<CoopHeroDraft>(payloadJson ?? string.Empty); }
                catch (Exception ex) { reason = "payload_json_invalid:" + ex.GetType().Name; }
                if (draft != null)
                    CoopHeroCreationStateMachine.Submit(
                        session,
                        transfer.Revision,
                        transfer.SubmissionId,
                        transfer.PayloadHash,
                        draft,
                        _request.Rules,
                        out reason);
                else if (string.IsNullOrWhiteSpace(reason))
                    reason = "payload_json_deserialized_null";
            }

            if (!string.IsNullOrWhiteSpace(reason)) session.Reason = reason;
        }

        private void SetPeerSessionReason(NetworkCommunicator peer, string reason)
        {
            if (peer == null || string.IsNullOrWhiteSpace(reason)) return;
            string identity;
            CoopHeroCreationParticipantSession session;
            if (_identityByPeerIndex.TryGetValue(peer.Index, out identity) &&
                _sessionsByIdentity.TryGetValue(identity, out session))
                session.Reason = reason;
        }

        private void PrunePendingClientSubmissions(DateTime nowUtc)
        {
            int[] expiredPeerIndices = _pendingClientSubmissionByPeerIndex
                .Where(pair => nowUtc - pair.Value.Accumulator.LastActivityUtc >= ChunkTransferTimeout)
                .Select(pair => pair.Key)
                .ToArray();
            if (expiredPeerIndices.Length <= 0) return;

            foreach (int peerIndex in expiredPeerIndices)
            {
                _pendingClientSubmissionByPeerIndex.Remove(peerIndex);
                string identity;
                CoopHeroCreationParticipantSession session;
                if (_identityByPeerIndex.TryGetValue(peerIndex, out identity) &&
                    _sessionsByIdentity.TryGetValue(identity, out session))
                    session.Reason = "payload_transfer_timeout";
            }
            QueueServerEnvelopeForAllPeers();
        }

        private void EnrollSynchronizedPeers(DateTime now)
        {
            if (GameNetwork.NetworkPeers == null) return;
            bool participantSetChanged = false;
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized) continue;
                if (_identityByPeerIndex.ContainsKey(peer.Index) || _excludedStateByPeerIndex.ContainsKey(peer.Index)) continue;

                string identityHash = TryGetStableIdentityHash(peer);
                if (string.IsNullOrWhiteSpace(identityHash))
                {
                    _excludedStateByPeerIndex[peer.Index] = CoopHeroCreationParticipantState.IdentityUnavailable;
                    _pendingServerEnvelopePeerIndices.Add(peer.Index);
                    continue;
                }

                CoopHeroCreationParticipantSession existingSession;
                if (_sessionsByIdentity.TryGetValue(identityHash, out existingSession))
                {
                    _identityByPeerIndex[peer.Index] = identityHash;
                    participantSetChanged |= CoopHeroCreationStateMachine.Reconnect(existingSession);
                    _pendingServerEnvelopePeerIndices.Add(peer.Index);
                    continue;
                }

                if ((_request.ExistingPlayerHashes ?? new List<string>()).Contains(identityHash, StringComparer.OrdinalIgnoreCase))
                {
                    _excludedStateByPeerIndex[peer.Index] = CoopHeroCreationParticipantState.AlreadyExists;
                    _pendingServerEnvelopePeerIndices.Add(peer.Index);
                    continue;
                }
                if (_enrollmentClosed || now >= _enrollmentDeadlineUtc)
                {
                    _excludedStateByPeerIndex[peer.Index] = CoopHeroCreationParticipantState.Late;
                    _pendingServerEnvelopePeerIndices.Add(peer.Index);
                    continue;
                }

                _sessionsByIdentity[identityHash] = CoopHeroCreationStateMachine.Invite(identityHash);
                _identityByPeerIndex[peer.Index] = identityHash;
                _pendingServerEnvelopePeerIndices.Add(peer.Index);
                participantSetChanged = true;
            }
            if (participantSetChanged) QueueServerEnvelopeForAllPeers();
        }

        private void SendPendingAndChangedEnvelopes()
        {
            if (GameNetwork.NetworkPeers == null) return;
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized) continue;
                if (!_pendingServerEnvelopePeerIndices.Contains(peer.Index)) continue;
                if (SendEnvelope(peer)) _pendingServerEnvelopePeerIndices.Remove(peer.Index);
            }
        }

        private bool SendEnvelope(NetworkCommunicator peer)
        {
            CoopHeroCreationParticipantState state = CoopHeroCreationParticipantState.Late;
            string reason = "not_enrolled";
            string identity;
            CoopHeroCreationParticipantSession session;
            if (_identityByPeerIndex.TryGetValue(peer.Index, out identity) &&
                _sessionsByIdentity.TryGetValue(identity, out session))
            {
                state = session.State;
                reason = session.Reason ?? string.Empty;
            }
            else if (_excludedStateByPeerIndex.ContainsKey(peer.Index))
            {
                state = _excludedStateByPeerIndex[peer.Index];
                reason = state.ToString();
            }

            CoopHeroCreationServerEnvelope envelope = new CoopHeroCreationServerEnvelope
            {
                RequestId = _request?.RequestId,
                SessionId = _request?.SessionId,
                Nonce = _request?.Nonce,
                RulesHash = _request?.RulesHash,
                State = state,
                Reason = reason,
                EnrollmentDeadlineUtc = _enrollmentDeadlineUtc.ToString("o"),
                SessionDeadlineUtc = _sessionDeadlineUtc.ToString("o"),
                RelevantCount = _sessionsByIdentity.Count,
                TerminalCount = _sessionsByIdentity.Values.Count(s => CoopHeroCreationContract.IsTerminal(s.State)),
                Rules = _request?.Rules
            };
            try
            {
                string payloadJson = JsonConvert.SerializeObject(envelope, Formatting.None);
                CoopHeroCreationChunkedPayload payload;
                string error;
                if (!CoopHeroCreationChunkCodec.TryEncode(payloadJson, out payload, out error))
                {
                    ModLogger.Info(
                        "CoopHeroCreationMissionNetwork: envelope encoding failed. PeerIndex=" +
                        peer.Index + " Error=" + error);
                    return false;
                }

                int transferId = NextServerTransferId();
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(new CoopHeroCreationServerEnvelopeMessage(
                    transferId,
                    payload.LogicalByteCount,
                    payload.ChunkCount,
                    payload.PayloadHash));
                GameNetwork.EndModuleEventAsServer();

                for (int chunkIndex = 0; chunkIndex < payload.ChunkCount; chunkIndex++)
                {
                    GameNetwork.BeginModuleEventAsServer(peer);
                    GameNetwork.WriteMessage(new CoopHeroCreationServerEnvelopeChunkMessage(
                        transferId,
                        chunkIndex,
                        payload.ChunkCount,
                        payload.Chunks[chunkIndex]));
                    GameNetwork.EndModuleEventAsServer();
                }
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopHeroCreationMissionNetwork: envelope send failed. PeerIndex=" + peer.Index + " Error=" + ex.Message);
                return false;
            }
        }

        private void QueueServerEnvelopeForAllPeers()
        {
            PublishProgressIfChanged();
            if (GameNetwork.NetworkPeers == null) return;
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized) continue;
                _pendingServerEnvelopePeerIndices.Add(peer.Index);
            }
        }

        private void PublishProgressIfChanged()
        {
            if (_request == null || !GameNetwork.IsServer) return;
            CoopHeroCreationProgressSnapshot snapshot = CoopHeroCreationProgressContract.CreateSnapshot(
                _request,
                _sessionsByIdentity.Values,
                _enrollmentClosed,
                _resultWritten,
                DateTime.UtcNow);
            string signature = CoopHeroCreationProgressContract.BuildSignature(snapshot);
            if (string.Equals(signature, _lastPublishedProgressSignature, StringComparison.Ordinal)) return;

            try
            {
                CoopHeroCreationBridgeFile.WriteProgress(snapshot);
                _lastPublishedProgressSignature = signature;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopHeroCreationMissionNetwork: progress bridge write failed. Error=" + ex.Message);
            }
        }

        private int NextServerTransferId()
        {
            _nextServerTransferId = (_nextServerTransferId + 1) & CoopHeroCreationClientCommandMessage.MaxTransferId;
            return _nextServerTransferId;
        }

        private bool ShouldFinalize()
        {
            if (_resultWritten || _missionEnding || !_enrollmentClosed) return false;
            return _request == null || _sessionsByIdentity.Values.All(s => CoopHeroCreationContract.IsTerminal(s.State));
        }

        private void FinalizeAuthoritativeResult()
        {
            _resultWritten = true;
            if (_request != null)
            {
                CoopHeroCreationResult result = new CoopHeroCreationResult
                {
                    CampaignScopeId = _request.CampaignScopeId,
                    RequestId = _request.RequestId,
                    SessionId = _request.SessionId,
                    Nonce = _request.Nonce,
                    RulesHash = _request.RulesHash,
                    CompletedUtc = DateTime.UtcNow.ToString("o"),
                    Participants = _sessionsByIdentity.Values
                        .OrderBy(s => s.PlayerIdentityHash, StringComparer.Ordinal)
                        .Select(s => new CoopHeroCreationParticipantResult
                        {
                            PlayerIdentityHash = s.PlayerIdentityHash,
                            LogicalHeroId = CoopHeroCreationContract.BuildLogicalHeroId(_request.CampaignScopeId, s.PlayerIdentityHash),
                            State = s.State,
                            Reason = s.Reason,
                            Revision = s.Revision,
                            SubmissionId = s.SubmissionId,
                            PayloadHash = s.PayloadHash,
                            Draft = s.State == CoopHeroCreationParticipantState.Completed ? s.Draft : null
                        }).ToList()
                };
                result.ResultId = CoopHeroCreationContract.ComputeResultId(result);
                CoopHeroCreationBridgeFile.WriteResult(result);
                PublishProgressIfChanged();
                ModLogger.Info("CoopHeroCreationMissionNetwork: authoritative result written. ResultId=" + result.ResultId +
                               " Participants=" + result.Participants.Count + ".");
            }
            BeginMissionEnd();
        }

        private static void WriteFailureResult(CoopHeroCreationRequest request, string failureReason)
        {
            if (request == null) return;
            try
            {
                CoopHeroCreationResult result = new CoopHeroCreationResult
                {
                    CampaignScopeId = request.CampaignScopeId,
                    RequestId = request.RequestId,
                    SessionId = request.SessionId,
                    Nonce = request.Nonce,
                    RulesHash = request.RulesHash,
                    CompletedUtc = DateTime.UtcNow.ToString("o"),
                    FailureReason = string.IsNullOrWhiteSpace(failureReason)
                        ? "request_validation_failed"
                        : failureReason,
                    Participants = new List<CoopHeroCreationParticipantResult>()
                };
                result.ResultId = CoopHeroCreationContract.ComputeResultId(result);
                CoopHeroCreationBridgeFile.WriteResult(result);
                ModLogger.Info(
                    "CoopHeroCreationMissionNetwork: authoritative failure result written. ResultId=" +
                    result.ResultId + " Error=" + result.FailureReason + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopHeroCreationMissionNetwork: failed to write authoritative failure result.",
                    ex);
            }
        }

        private void BeginMissionEnd()
        {
            if (_missionEnding) return;
            _missionEnding = true;
            _pendingClientSubmissionByPeerIndex.Clear();
            _clientEnvelopeAccumulator = null;
            _clientEnvelopeTransferId = -1;
            MissionLobbyComponent lobby = Mission.GetMissionBehavior<MissionLobbyComponent>();
            if (lobby != null && lobby.CurrentMultiplayerState != MissionLobbyComponent.MultiplayerGameState.Ending)
                lobby.SetStateEndingAsServer();
        }

        private static string TryGetStableIdentityHash(NetworkCommunicator peer)
        {
            try
            {
                object id = peer?.VirtualPlayer?.GetType().GetProperty("Id")?.GetValue(peer.VirtualPlayer, null);
                string stableId = id?.ToString()?.Trim();
                return string.IsNullOrWhiteSpace(stableId)
                    ? null
                    : CoopHeroCreationHash.ComputeSha256("CoopHeroPlayer/v1|" + stableId);
            }
            catch { return null; }
        }

        public static bool SendBeginEditing() => SendClientControlCommand(CoopHeroCreationClientCommandKind.BeginEditing);
        public static bool SendDecline() => SendClientControlCommand(CoopHeroCreationClientCommandKind.Decline);
        public static bool SendSubmit(CoopHeroDraft draft, int revision, string submissionId)
        {
            if (draft == null || revision <= 0 || string.IsNullOrWhiteSpace(submissionId) ||
                submissionId.Length > CoopHeroCreationClientCommandMessage.MaxSubmissionIdCharacters ||
                !GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return false;

            string payloadJson = JsonConvert.SerializeObject(draft, Formatting.None);
            int maximumPayloadCharacters = CurrentClientEnvelope?.Rules?.MaximumPayloadCharacters ?? 24576;
            if (payloadJson.Length > maximumPayloadCharacters) return false;

            CoopHeroCreationChunkedPayload payload;
            string error;
            if (!CoopHeroCreationChunkCodec.TryEncode(payloadJson, out payload, out error))
            {
                ModLogger.Info("CoopHeroCreationMissionNetwork: client submission encoding failed. Error=" + error);
                return false;
            }

            int transferId = Interlocked.Increment(ref _nextClientTransferId) &
                             CoopHeroCreationClientCommandMessage.MaxTransferId;
            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopHeroCreationClientCommandMessage(
                    (int)CoopHeroCreationClientCommandKind.Submit,
                    revision,
                    transferId,
                    payload.LogicalByteCount,
                    payload.ChunkCount,
                    submissionId,
                    CoopHeroCreationHash.ComputeCanonicalJsonHash(draft),
                    payload.PayloadHash));
                GameNetwork.EndModuleEventAsClient();

                for (int chunkIndex = 0; chunkIndex < payload.ChunkCount; chunkIndex++)
                {
                    GameNetwork.BeginModuleEventAsClient();
                    GameNetwork.WriteMessage(new CoopHeroCreationClientPayloadChunkMessage(
                        transferId,
                        chunkIndex,
                        payload.ChunkCount,
                        payload.Chunks[chunkIndex]));
                    GameNetwork.EndModuleEventAsClient();
                }
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopHeroCreationMissionNetwork: client submission send failed. Error=" + ex.Message);
                return false;
            }
        }

        private static bool SendClientControlCommand(CoopHeroCreationClientCommandKind kind)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive) return false;
            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopHeroCreationClientCommandMessage(
                    (int)kind,
                    0,
                    0,
                    0,
                    0,
                    null,
                    null,
                    null));
                GameNetwork.EndModuleEventAsClient();
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopHeroCreationMissionNetwork: client command send failed. Error=" + ex.Message);
                return false;
            }
        }

        private sealed class PendingClientSubmissionTransfer
        {
            public PendingClientSubmissionTransfer(
                int transferId,
                int revision,
                string submissionId,
                string payloadHash,
                CoopHeroCreationChunkAccumulator accumulator)
            {
                TransferId = transferId;
                Revision = revision;
                SubmissionId = submissionId;
                PayloadHash = payloadHash;
                Accumulator = accumulator;
            }

            public int TransferId { get; }
            public int Revision { get; }
            public string SubmissionId { get; }
            public string PayloadHash { get; }
            public CoopHeroCreationChunkAccumulator Accumulator { get; }
        }
    }
}
