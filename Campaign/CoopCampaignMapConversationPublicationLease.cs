using System;
using CoopSpectator.Infrastructure;

namespace CoopSpectator.Campaign
{
    // Pure admission foundation. The native opening adapter supplies exact
    // campaign/mission identities and independently prove a standalone dialogue
    // context. This class neither reads native objects nor publishes a heartbeat.
    internal sealed class CoopCampaignMapConversationPublicationLease
    {
        internal const double EntryLifetimeSeconds = 2d;
        private object _campaign;
        private object _mission;
        private CoopCampaignMapPrototypeHostSnapshot _publishedMap;
        private double _publishedAt;
        private double _observedAt;

        // Call ONLY after a complete successful map/catalog/dynamic publication,
        // never for a captured-but-unwritten snapshot or a mission overlay.
        internal bool RememberPublishedMap(object campaign,
            CoopCampaignMapPrototypeHostSnapshot snapshot, double monotonicSeconds, DateTime utcNow)
        {
            Reset();
            if (campaign == null || !FiniteTime(monotonicSeconds) ||
                !CoopCampaignMapPrototypeContract.TryValidateHostSnapshot(snapshot, utcNow,
                    TimeSpan.FromSeconds(EntryLifetimeSeconds), out _) ||
                !snapshot.IsActive || snapshot.Camera == null ||
                snapshot.CatalogRevision <= 0 || snapshot.DynamicRevision <= 0)
                return false;

            _campaign = campaign;
            _publishedMap = snapshot.Clone();
            _publishedAt = _observedAt = monotonicSeconds;
            return true;
        }

        // Invoke on every context observation, not just when the dialogue VM is
        // ready. Any intervening unsupported mission revokes the old map anchor.
        // Eligibility is NOT equivalent to MissionMode.Conversation: hideout boss
        // and settlement conversations have separate ownership contracts.
        internal bool TryAcquire(object campaign, string sessionId, object mission,
            bool standaloneContextAdmitted, double monotonicSeconds,
            out CoopCampaignMapPrototypeHostSnapshot anchor)
        {
            anchor = null;
            if (_publishedMap == null || campaign == null ||
                !ReferenceEquals(campaign, _campaign) ||
                !string.Equals(sessionId, _publishedMap.SessionId, StringComparison.Ordinal) ||
                mission == null || !standaloneContextAdmitted ||
                !FiniteTime(monotonicSeconds) || monotonicSeconds < _observedAt ||
                (_mission != null && !ReferenceEquals(mission, _mission)))
            {
                Reset();
                return false;
            }

            // Entry must follow a fresh map publication. Once admitted, a long
            // conversation is bound to exactly this mission until context loss.
            if (_mission == null && monotonicSeconds - _publishedAt >= EntryLifetimeSeconds)
            {
                Reset();
                return false;
            }
            _mission = mission;
            _observedAt = monotonicSeconds;
            anchor = _publishedMap.Clone();
            // Preserve original revision/time: obtaining an anchor is not proof
            // of a fresh native capture, successful write, or network delivery.
            return true;
        }

        internal void Reset()
        {
            _campaign = null;
            _mission = null;
            _publishedMap = null;
            _publishedAt = _observedAt = 0d;
        }

        private static bool FiniteTime(double value) =>
            value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
