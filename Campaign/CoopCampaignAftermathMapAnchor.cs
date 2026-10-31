using System;
using CoopSpectator.Infrastructure;

namespace CoopSpectator.Campaign
{
    // A successful map publication owns all three parts. Live dialogue capture
    // may advance its caches while a battle still needs this retained backdrop.
    internal sealed class CoopCampaignAftermathMapAnchor
    {
        private object _campaign;
        private CoopCampaignMapPrototypeHostSnapshot _frame;
        private CoopCampaignMapPrototypeCatalogSnapshot _catalog;
        private CoopCampaignMapPrototypeDynamicSnapshot _dynamic;

        // Only call after catalog, dynamic and frame have all been written.
        internal bool Remember(object campaign, CoopCampaignMapPrototypeHostSnapshot frame,
            CoopCampaignMapPrototypeCatalogSnapshot catalog, CoopCampaignMapPrototypeDynamicSnapshot dynamic)
        {
            Reset();
            if (campaign == null || frame == null || catalog == null || dynamic == null ||
                !frame.IsActive || frame.Conversation?.IsActive == true ||
                frame.SchemaVersion != CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion ||
                catalog.SchemaVersion != frame.SchemaVersion || dynamic.SchemaVersion != frame.SchemaVersion ||
                string.IsNullOrEmpty(frame.SessionId) ||
                !string.Equals(frame.SessionId, catalog.SessionId, StringComparison.Ordinal) ||
                !string.Equals(frame.SessionId, dynamic.SessionId, StringComparison.Ordinal) ||
                frame.CatalogRevision <= 0 || frame.CatalogRevision != catalog.Revision ||
                frame.DynamicRevision <= 0 || frame.DynamicRevision != dynamic.Revision)
                return false;

            _frame = frame.Clone();
            _catalog = catalog.Clone();
            _dynamic = dynamic.Clone();
            _campaign = campaign;
            return true;
        }

        internal bool TryAcquire(object campaign, string sessionId, DateTime now,
            out CoopCampaignMapPrototypeHostSnapshot frame,
            out CoopCampaignMapPrototypeCatalogSnapshot catalog,
            out CoopCampaignMapPrototypeDynamicSnapshot dynamic)
        {
            frame = null;
            catalog = null;
            dynamic = null;
            if (_frame == null || campaign == null || !ReferenceEquals(_campaign, campaign) ||
                !string.Equals(_frame.SessionId, sessionId, StringComparison.Ordinal))
            {
                Reset();
                return false;
            }
            frame = _frame.Clone();
            catalog = _catalog.Clone();
            dynamic = _dynamic.Clone();
            // Renew publication receipt only; the retained world stays frozen.
            frame.UpdatedUtc = catalog.UpdatedUtc = dynamic.UpdatedUtc = now;
            return true;
        }

        internal void Reset()
        {
            _campaign = null;
            _frame = null;
            _catalog = null;
            _dynamic = null;
        }
    }
}
