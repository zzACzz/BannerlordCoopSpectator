using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.MissionBehaviors
{
    public sealed partial class CoopCampaignMapPrototypeNetworkController
    {
        private sealed class PartyPoseReceipt
        {
            internal CoopCampaignMapPartyPoseEnvelope Envelope;
            internal double ReceivedAt;
            internal bool Revoked;
        }
        private static readonly Dictionary<string, PartyPoseReceipt> ClientPartyPoses =
            new Dictionary<string, PartyPoseReceipt>(StringComparer.OrdinalIgnoreCase);
        private static string _partyPoseSession;

        private void BroadcastPartyPoses(CoopCampaignMapPrototypeHostSnapshot snapshot)
        {
            if (_serverConversation.IsActive || GameNetwork.NetworkPeers == null ||
                !CoopCampaignMapPartyPoseBatchCodec.IsBound(snapshot.PartyPoses, snapshot.SessionId,
                    snapshot.Revision, snapshot.CatalogRevision, false)) return;
            foreach (var sample in snapshot.PartyPoses)
            {
                var catalog = _serverCatalog.Find(value => string.Equals(value.EntityId, sample.EntityId, StringComparison.OrdinalIgnoreCase));
                var dynamicState = _serverDynamic.Find(value => string.Equals(value.EntityId, sample.EntityId, StringComparison.OrdinalIgnoreCase));
                if (catalog == null || dynamicState == null || !dynamicState.IsVisible ||
                    catalog.Kind == CoopCampaignMapPrototypeEntityKind.Settlement ||
                    !CoopCampaignMapPartyBodyPolicy.ShouldRenderBody(catalog.Kind, dynamicState.PartyNameplateFlags) ||
                    !sample.Pose.MatchesAppearance(snapshot.CatalogRevision, catalog.HumanVisual != null,
                        catalog.MountVisual != null, catalog.CaravanMountVisual != null)) continue;
                var forwarded = sample.Clone();
                // Host-file and network revisions have independent lifetimes.
                forwarded.Pose.AppearanceRevision = _networkCatalogRevision;
                forwarded.Sequence = _networkRevision;
                foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
                {
                    if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized ||
                        !_serverCatalogTransportByPeer.TryGetValue(peer.Index, out var transfer) ||
                        !transfer.IsCompleted || transfer.AppliedRevision != _networkCatalogRevision) continue;
                    GameNetwork.BeginModuleEventAsServer(peer);
                    GameNetwork.WriteMessage(new CoopCampaignMapPartyPoseMessage(forwarded));
                    GameNetwork.EndModuleEventAsServer();
                }
            }
        }

        private static bool CanUsePartyPose(CoopCampaignMapPartyPoseEnvelope envelope)
        {
            if (envelope == null || !TryGetMovementTargetState(out var state, out _) ||
                CurrentClientConversation?.IsActive == true ||
                !ClientCatalogById.TryGetValue(envelope.EntityId, out var catalog) ||
                !ClientDynamicById.TryGetValue(envelope.EntityId, out var dynamicState) ||
                catalog.Kind == CoopCampaignMapPrototypeEntityKind.Settlement ||
                !CoopCampaignMapPartyBodyPolicy.ShouldRenderBody(catalog.Kind, dynamicState.PartyNameplateFlags)) return false;
            // Existing per-party AppearanceRevision is currently always zero.
            // Bind to the completed catalog revision instead, never treat zero
            // as proof that equipment/body resources are unchanged.
            return envelope.Matches(state.SessionId, catalog.EntityId, -1,
                _clientCatalogCompletedRevision, dynamicState.IsVisible,
                catalog.HumanVisual != null, catalog.MountVisual != null, catalog.CaravanMountVisual != null);
        }

        private static void HandleServerPartyPose(GameNetworkMessage baseMessage)
        {
            var message = baseMessage as CoopCampaignMapPartyPoseMessage;
            if (message == null || message.ProtocolVersion != CoopCampaignMapPrototypeContract.ProtocolVersion) return;
            ReconcileClientPartyPoses();
            var envelope = message.Envelope;
            if (envelope?.IsValid() != true || !CanUsePartyPose(envelope)) return;
            if (ClientPartyPoses.TryGetValue(envelope.EntityId, out var previous) &&
                envelope.Sequence <= previous.Envelope.Sequence) return;
            if (!ClientPartyPoses.ContainsKey(envelope.EntityId) &&
                ClientPartyPoses.Count >= CoopCampaignMapPrototypeContract.MaxCatalogEntities) return;
            ClientPartyPoses[envelope.EntityId] = new PartyPoseReceipt {
                Envelope = envelope.Clone(), ReceivedAt = StateReceiptClock.Elapsed.TotalSeconds };
        }

        private static void ReconcileClientPartyPoses()
        {
            string session = CurrentClientState?.SessionId;
            if (!string.Equals(session, _partyPoseSession, StringComparison.Ordinal))
            { ClientPartyPoses.Clear(); _partyPoseSession = session; }
            // Retain receipt sequence for still-catalogued entities, including
            // hidden ones. Expired receipts are never returned for rendering.
            var removed = new List<string>();
            foreach (var pair in ClientPartyPoses)
            {
                if (!ClientCatalogById.ContainsKey(pair.Key)) removed.Add(pair.Key);
                else if (!CanUsePartyPose(pair.Value.Envelope)) pair.Value.Revoked = true;
            }
            foreach (string id in removed) ClientPartyPoses.Remove(id);
        }

        internal static bool TryGetClientPartyPose(string entityId, out CoopCampaignMapPartyPoseEnvelope envelope)
        {
            envelope = null;
            if (entityId == null || !ClientPartyPoses.TryGetValue(entityId, out var receipt) ||
                receipt.Revoked || !CanUsePartyPose(receipt.Envelope)) return false;
            double age = StateReceiptClock.Elapsed.TotalSeconds - receipt.ReceivedAt;
            if (age < 0 || age > MaximumHostStateAge.TotalSeconds) return false;
            envelope = receipt.Envelope.Clone(); return true;
        }
    }
}
